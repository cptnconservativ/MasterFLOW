using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Construction-phase companion: submittals and RFIs checked against the design. Hydraulic numbers must equal a direct
/// solve of the changed network; dispositions follow the worst finding.
/// </summary>
public class ConstructionCompanionTests
{
    private static readonly ConstructionCompanionTool Tool = new();
    private static ToolResult Run(string input, StormNetwork? net = null)
    {
        var n = net ?? NetworkOutputTests.NetA();
        return Tool.Run(input, new DesignToolContext(n.Units, n, StormNetworkSolver.Solve(n)));
    }
    private static IEnumerable<IReadOnlyList<object?>> Rows(ToolResult r, string item, string check) =>
        r.Tables[0].Rows.Where(x => (string)x[1]! == item && (string)x[2]! == check);

    [Fact]
    public void NotAllowedMaterial_IsRejected()
    {
        var r = Run("""
            network = Main St
            allowed materials = RCP, HDPE
            [Pipe]
            Pipes	Material
            P3	CMP
            """);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.StartsWith("Disposition: REJECTED", r.Summary[0]);
        Assert.Equal("FAILS", Rows(r, "P3", "Material").Single()[5]);
        // CMP default n 0.024 is rougher than the design 0.013, so it is flagged for the hydraulic re-run.
        Assert.Equal("CHECKED", Rows(r, "P3", "Manning's n").Single()[5]);
        Assert.NotNull(r.Report);
    }

    [Fact]
    public void LaterPipeRows_OverrideEarlierOnes()
    {
        var r = Run("""
            network = Main St
            allowed materials = RCP, HDPE, PP
            [Pipe]
            Pipes	Material	Size (in.)	Class	n
            all	HDPE
            P3	RCP		III
            """);
        Assert.Equal("RCP", Rows(r, "P3", "Material").Single()[4]);
        Assert.Empty(Rows(r, "P3", "Manning's n"));            // RCP default n equals the design n
        Assert.Equal("HDPE", Rows(r, "P1", "Material").Single()[4]);
        Assert.Equal("NOTE", Rows(r, "P3", "Class").Single()[5]);
        // HDPE at 24 in. with n 0.012 breaks the IDCM RCP rule (above 18 in.) for P1, which the design met.
        Assert.Contains(r.Tables[0].Rows, x => (string)x[1]! == "Submitted" && (string)x[5]! == "FAILS" && ((string)x[6]!).Contains("P1"));
        Assert.StartsWith("Disposition: REVISE AND RESUBMIT", r.Summary[0]);
    }

    [Fact]
    public void SmallerPipe_Fails()
    {
        var r = Run("""
            network = Main St
            [Pipe]
            Pipes	Material	Size (in.)
            P3		12
            """);
        var row = Rows(r, "P3", "Size (in.)").Single();
        Assert.Equal(15.0, (double)row[3]!, 6);
        Assert.Equal("FAILS", row[5]);
        Assert.StartsWith("Disposition: REVISE AND RESUBMIT", r.Summary[0]);
    }

    [Fact]
    public void RfiInvertChange_HglRiseMatchesDirectSolve()
    {
        var r = Run("""
            network = Main St
            [Changes]
            RFI	Change	Reason
            RFI-7	P3 upstream invert = 102.3	rock
            RFI-8	CB-3 rim = 107.5	grade
            """);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var n = NetworkOutputTests.NetA();
        var baseRes = StormNetworkSolver.Solve(n);
        var changed = StormNetworkSolver.Solve(n with { Pipes = [.. n.Pipes.Select(p => p.Name == "P3" ? p with { UpstreamInvert = 102.3 } : p)] });
        double rise = changed.Structures.Max(s => s.Hgl - baseRes.Structure(s.Structure.Name)!.Hgl);
        var hyd = r.Tables.Single(t => t.Title.StartsWith("Hydraulic"));
        var rfi7 = hyd.Rows.Single(x => ((string)x[0]!).StartsWith("RFI-7 "));
        Assert.Equal(Math.Round(rise, 2), (double)rfi7[2]!, 6);
        Assert.Contains(hyd.Rows, x => (string)x[0]! == "Everything together");
    }

    [Fact]
    public void CompareRules()
    {
        var r = Run("""
            [Compare]
            Item	Property	Required	Proposed	Rule	Reference
            Riprap	D50 (in.)	12	9	>=	HEC-14
            Riprap	Thickness (in.)	18	20	>=	HEC-14
            Joint	Gap (in.)	0.5	0.75	<=	spec
            Grate	Load rating	H-20/H-25	HS-15	one of	IDCM
            Grate	Bicycle safe	yes	YES	=	IDCM
            """);
        Assert.Equal("FAILS", Rows(r, "Riprap", "D50 (in.)").Single()[5]);
        Assert.Equal("OK", Rows(r, "Riprap", "Thickness (in.)").Single()[5]);
        Assert.Equal("FAILS", Rows(r, "Joint", "Gap (in.)").Single()[5]);
        Assert.Equal("FAILS", Rows(r, "Grate", "Load rating").Single()[5]);
        Assert.Equal("OK", Rows(r, "Grate", "Bicycle safe").Single()[5]);
    }

    [Fact]
    public void CleanSubmittal_NoExceptionsTaken_AndBadRfiTextIsAnError()
    {
        var ok = Run("""
            network = Main St
            allowed materials = RCP
            [Pipe]
            Pipes	Material
            all	RCP
            """);
        Assert.StartsWith("Disposition: NO EXCEPTIONS TAKEN", ok.Summary[0]);
        Assert.False(ok.Report!.TableOfContents);

        var bad = Run("""
            network = Main St
            [Changes]
            RFI	Change
            RFI-1	move CB-3 two feet north
            """);
        Assert.Contains(bad.Messages, m => m.Code == "E-CC-002");
    }
}
