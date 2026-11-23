using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Utility conflict finder (premium, D-270): 3D clashes, vertical and horizontal separation, water above sewer, structures.</summary>
public class UtilityConflictTests
{
    private static readonly UtilityConflictTool Tool = new();
    private static readonly DesignToolContext Bare = new(UnitSystem.USCustomary);

    private static StormNetwork Net()
    {
        NetworkPipe Pipe(string n, string f, string t, double d, double l, double ui, double di) => new() { Name = n, From = f, To = t, Rise = d, Span = d, Length = l, UpstreamInvert = ui, DownstreamInvert = di, Discharge = 10 };
        NetworkStructure S(string n, double rim, double dn, double de) => new() { Name = n, RimElevation = rim, Northing = 1000 + dn, Easting = 1000 + de };
        return new StormNetwork
        {
            Name = "Main St", Structures = [S("OUT", 105, 0, 0), S("MH-2", 107.5, 0, 200)],
            Pipes = [Pipe("P1", "MH-2", "OUT", 2.5, 200, 100.80, 100.00)],
        };
    }

    private static DesignToolContext Ctx(StormNetwork n) => new(UnitSystem.USCustomary, n, StormNetworkSolver.Solve(n)) { Networks = [(n, StormNetworkSolver.Solve(n))] };

    private static List<IReadOnlyList<object?>> Findings(ToolResult r) => [.. r.Tables.Single(t => t.Title == "Conflicts").Rows];

    private const string Head = "[Utilities]\nName, Type, Size (in.), Northing, Easting, Elev, Ref\n";

    [Fact]
    public void WaterThroughStormPipe_IsAClashWithTheFix()
    {
        // P1 runs along N 1000 from E 1200 (inv 100.8) to E 1000 (inv 100.0): at E 1120 its center is 100.48 + 1.25 = 101.73.
        var r = Tool.Run(Head + "WM-1, water, 9, 940, 1120, 101.5, top\nWM-1, water, 9, 1140, 1120, 101.5, top\n", Ctx(Net()));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        var f = Findings(r).Single(x => (string)x[1]! == "CLASH");
        // OD storm 2.5 × 1.2 = 3.0 ft; water center 101.125, OD 0.75 ft: clear = |101.73 − 101.125| − 1.5 − 0.375 = −1.27.
        Assert.Equal(-1.27, (double)f[7]!, 2);
        Assert.Equal(1000, (double)f[4]!, 2); Assert.Equal(1120, (double)f[5]!, 2);
        Assert.Contains("by 2.77 ft", (string)f[9]!);
        Assert.Contains(r.Messages, m => m.Code == "W-UC-003");
    }

    [Fact]
    public void WaterUnderSanitary_BreaksWaterAboveRule()
    {
        var r = Tool.Run(Head + "WM, water, 9, 0, 50, 95, invert\nWM, water, 9, 100, 50, 95, invert\nSS, sanitary, 8, 50, 0, 100, invert\nSS, sanitary, 8, 50, 100, 99, invert\n", Bare);
        var kinds = Findings(r).Select(x => (string)x[1]!).ToList();
        Assert.Contains("WATER BELOW", kinds);
        Assert.DoesNotContain("VERTICAL", kinds);     // plenty of clearance, still on the wrong side
    }

    [Fact]
    public void ParallelWaterAndSewer_TooClose_ButWaterAboveNoted()
    {
        var r = Tool.Run(Head + "WM, water, 9, 0, 0, 104, invert\nWM, water, 9, 0, 200, 104, invert\nSS, sanitary, 8, 6, 0, 100, invert\nSS, sanitary, 8, 6, 200, 99, invert\n", Bare);
        var f = Findings(r).Single();
        Assert.Equal("HORIZONTAL", f[1]);
        Assert.Equal(6 - 0.375 - 0.333, (double)f[7]!, 2);
        Assert.Contains("separate trench", (string)f[9]!);
        Assert.Contains("parallel for about 2", (string)f[9]!);     // about 200 ft
    }

    [Fact]
    public void GasPastManhole_AndCleanCrossing()
    {
        // Gas passes 2.4 ft from the MH-2 center (structure 4 ft): 0.4 − 0.19 = 0.21 ft from the wall, at pipe depth.
        var r = Tool.Run(Head + "GAS, gas, 4.5, 997.6, 1150, 102, center\nGAS, gas, 4.5, 997.6, 1250, 102, center\n"
                         + "TEL, telecom, 2, 900, 1100, 104.5, center\nTEL, telecom, 2, 1100, 1100, 104.5, center\n", Ctx(Net()));
        var all = Findings(r);
        Assert.Contains(all, x => (string)x[1]! == "STRUCTURE" && (string)x[3]! == "Main St MH-2");
        // Telecom crosses P1 at E 1100: P1 top = 100.4 + 2.5 + 0.25 wall; 104.5 − 0.083 − 103.15 ≈ 1.27 ft clear ≥ 1.0.
        Assert.DoesNotContain(all, x => (string)x[2]! == "TEL" || (string)x[3]! == "TEL");
    }

    [Fact]
    public void NoConflicts_SaysSo_AndDefaultRulesApply()
    {
        var r = Tool.Run(Head + "A, water, 8, 0, 0, 100, invert\nA, water, 8, 0, 100, 100, invert\nB, gas, 2, 50, 0, 100, invert\nB, gas, 2, 50, 100, 100, invert\n", Bare);
        Assert.Contains("No conflicts.", r.Summary);
        Assert.Equal(10, r.Tables.Single(t => t.Title == "Separation rules").Rows.Count);
    }

    [Fact]
    public void Errors_AndNetworkWithoutCoordinates()
    {
        Assert.Contains(Tool.Run(Head + "A, water, 8, x, 0, 100\nA, water, 8, 1, 0, 100\n", Bare).Messages, m => m.Code == "E-UC-003");
        Assert.Contains(Tool.Run("x = 1\n", Bare).Messages, m => m.Code == "E-UC-004");
        Assert.Contains(Tool.Run("[Utilities]\nFoo, Bar\n1, 2\n", Bare).Messages, m => m.Code == "E-UC-002");
        var noXy = NetworkOutputTests.NetA();
        var r = Tool.Run(Head + "A, water, 8, 0, 0, 100\nA, water, 8, 0, 100, 100\nB, gas, 2, 50, 0, 100\nB, gas, 2, 50, 100, 100\n", Ctx(noXy));
        Assert.Contains(r.Messages, m => m.Code == "W-UC-001");
    }

    [Fact]
    public void SampleRuns_AndIsPremium()
    {
        var ctx = Ctx(Net());
        var r = Tool.Run(Tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Contains(Findings(r), x => (string)x[1]! == "CLASH");
        Assert.Equal("utility-conflicts", PremiumFeatures.ForTool("utility-conflicts")?.Id);
    }
}
