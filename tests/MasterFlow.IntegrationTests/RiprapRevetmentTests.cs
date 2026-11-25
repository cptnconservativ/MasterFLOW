using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-11 riprap revetment: D50 equation, K1, corrections, class, thickness and the filter rules, by hand.</summary>
public class RiprapRevetmentTests
{
    private static readonly RiprapRevetmentTool Tool = new();

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => Tool.Run(input, new DesignToolContext(u));

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Run(input, u);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static IReadOnlyList<object?> Row(ToolResult r, string name) => r.Tables[0].Rows.Single(x => (string)x[0]! == name);

    [Fact]
    public void K1_MatchesHec11Definition()
    {
        // 1V:2H bank, θ = 26.57°, φ = 42°: K1 = √(1 − 0.2/sin²42°) = 0.7438.
        Assert.Equal(0.7438, RiprapRevetmentTool.K1(Math.Atan(0.5) * 180 / Math.PI, 42), 3);
        Assert.True(double.IsNaN(RiprapRevetmentTool.K1(45, 42)));
    }

    [Fact]
    public void D50_UsAndSiAgree()
    {
        double us = RiprapRevetmentTool.D50(8, 6.5, 0.75, 2.65, 1.2, false);
        double si = RiprapRevetmentTool.D50(8 * 0.3048, 6.5 * 0.3048, 0.75, 2.65, 1.2, true);
        Assert.Equal(us * 0.3048, si, 3);
    }

    [Fact]
    public void Sample_RunsCleanInBothUnitSystems()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void StraightReach_ByHand()
    {
        var r = Ok("Ss = 2.65\nshape = angular\n[Reaches]\nName\tV\td\tZ\tSF\tPlacement\nA\t8\t6.5\t2\t1.2\tdry");
        double k1 = Math.Sqrt(1 - 0.2 / Math.Pow(Math.Sin(42 * Math.PI / 180), 2));
        double csg = 2.12 / Math.Pow(1.65, 1.5);
        double d50 = 0.001 * csg * 512 / (Math.Sqrt(6.5) * Math.Pow(k1, 1.5)) * 12;   // in.
        var row = Row(r, "A");
        Assert.Equal(Math.Round(d50, 1), (double)row[8]!, 1);
        Assert.Equal(3.8, (double)row[8]!, 1);
        Assert.Equal("I", row[9]);
        Assert.Equal(1.0, (double)row[11]!, 2);   // max(2·6 in., 12 in.) = 1.0 ft
    }

    [Fact]
    public void StabilityFactorAndUnderwaterPlacement()
    {
        var r = Ok("[Reaches]\nName\tV\td\tZ\tSF\tPlacement\nB\t11\t6\t2\t1.6\tunderwater");
        var row = Row(r, "B");
        double k1 = Math.Sqrt(1 - 0.2 / Math.Pow(Math.Sin(42 * Math.PI / 180), 2));
        double c = 2.12 / Math.Pow(1.65, 1.5) * Math.Pow(1.6 / 1.2, 1.5);
        double d50 = 0.001 * c * 1331 / (Math.Sqrt(6) * Math.Pow(k1, 1.5)) * 12;
        Assert.Equal(Math.Round(d50, 1), (double)row[8]!, 1);
        var cls = RiprapRevetmentTool.Classes.First(x => x.D50In >= d50);
        Assert.Equal(cls.Class, row[9]);
        Assert.Equal(Math.Round(Math.Max(2 * cls.D50In / 12, 1.0) * 1.5, 2), (double)row[11]!, 2);
    }

    [Fact]
    public void SteepBank_IsAnError()
    {
        var r = Run("[Reaches]\nName\tV\td\tZ\nC\t6\t4\t1");
        Assert.Contains(r.Messages, m => m.Code == "E-RR-007");
    }

    [Fact]
    public void FilterRules_FlagAMissingFilter()
    {
        // Riprap straight onto fine sand fails the piping rule.
        var r = Run("[Reaches]\nName\tV\td\tZ\nA\t8\t6.5\t2\n[Soils]\nLayer\tD15\tD50\tD85\nSand\t0.08\t0.2\t0.4");
        Assert.True(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == "W-RR-004");
        var f = r.Tables.Single(t => t.Title.StartsWith("Granular")).Rows.Single();
        Assert.Equal("FAILS", f[4]);
    }
}
