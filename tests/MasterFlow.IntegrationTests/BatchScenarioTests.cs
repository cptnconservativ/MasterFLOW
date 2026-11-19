using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Batch scenarios: every compared number must equal what the solver or the tool gives when run directly on the changed
/// input, and the base case must reproduce the network reference HGLs (validation/storm_hgl_output.txt).
/// </summary>
public class BatchScenarioTests
{
    private static readonly BatchScenarioTool Tool = new();
    private static ToolResult Run(string input, StormNetwork? net = null)
    {
        var n = net ?? NetworkOutputTests.NetA();
        return Tool.Run(input, new DesignToolContext(n.Units, n, StormNetworkSolver.Solve(n)));
    }
    private static double Hgl(ToolResult r, string structure, int scenario) => (double)r.Tables[1].Rows.Single(x => (string)x[0]! == structure)[2 + scenario]!;

    [Fact]
    public void Network_BaseMatchesReference_AndChangesMatchDirectSolves()
    {
        var r = Run("""
            tool = network
            [Scenarios]
            Scenario	tailwater	flow factor	pipe P1 size	n	structure CB-3 inflow
            Base
            Free	free
            Big	103.2	1.6	30	0.012
            """);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Equal(104.513873, Hgl(r, "MH-2", 0), 3);
        Assert.Equal(107.367201, Hgl(r, "CB-3", 0), 3);
        var n = NetworkOutputTests.NetA();
        var free = StormNetworkSolver.Solve(n with { Tailwater = TailwaterRule.FreeOutfall });
        Assert.Equal(Math.Round(free.Structure("CB-4")!.Hgl, 3), Hgl(r, "CB-4", 1), 6);
        var big = StormNetworkSolver.Solve(n with
        {
            Tailwater = TailwaterRule.Elevation, TailwaterElevation = 103.2,
            Pipes = n.Pipes.Select(p => (p with { Discharge = p.Discharge * 1.6, ManningN = 0.012 }) is var q && q.Name == "P1" ? q with { Rise = 2.5, Span = 2.5 } : q).ToList(),
        });
        foreach (var s in new[] { "OUT", "MH-2", "CB-3", "CB-4" }) Assert.Equal(Math.Round(big.Structure(s)!.Hgl, 3), Hgl(r, s, 2), 6);
        // Summary: base has CB-3 and CB-4 below the 1 ft criterion (clearances -0.87 and -2.26).
        Assert.Equal(2, r.Tables[0].Rows[0][4]);
        Assert.Equal("as entered", r.Tables[0].Rows[0][1]);
        var chart = Assert.Single(r.Charts);
        Assert.Equal(3, chart.Series.Count(s => s.Name.StartsWith("HGL")));
    }

    [Fact]
    public void Network_FlowFactorAlsoScalesStructureInflows()
    {
        var chain = InletNetworkToolTests.Chain();
        var r = Run("tool = network\n[Scenarios]\nScenario\tflow factor\nHalf\t0.5\n", chain);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var direct = StormNetworkSolver.Solve(chain with
        {
            Pipes = chain.Pipes.Select(p => p with { Discharge = p.Discharge * 0.5 }).ToList(),
            Structures = chain.Structures.Select(s => s with { Inflow = s.Inflow * 0.5 }).ToList(),
        });
        foreach (var p in direct.Pipes)
            Assert.Equal(Math.Round(p.Pipe.Discharge, 3), (double)r.Tables[3].Rows.Single(x => (string)x[0]! == p.Pipe.Name)[1]!, 6);
    }

    [Theory]
    [InlineData("Scenario\twidth\nA\t3", "E-BS-003")]
    [InlineData("Scenario\ttailwater\nA\thigh", "E-BS-004")]
    [InlineData("Scenario\tflow factor\nA\t-1", "E-BS-004")]
    [InlineData("Scenario\tflow factor\nA\tlots", "E-BS-004")]
    [InlineData("Scenario\tpipe P9 size\nA\t24", "E-BS-004")]
    [InlineData("Scenario\tpipe P1 size\nA\t0", "E-BS-004")]
    [InlineData("Scenario\tstructure NOPE inflow\nA\t3", "E-BS-004")]
    [InlineData("Scenario\tn\nA\t0", "E-BS-004")]
    [InlineData("Scenario\tn\nA\t0.013\nA\t0.015", "E-BS-001")]
    [InlineData("Scenario\tn", "E-BS-001")]
    public void Network_BadScenarios_AreReported(string table, string code)
    {
        var r = Run("tool = network\n[Scenarios]\n" + table + "\n");
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
        Assert.Empty(r.Tables);
    }

    [Fact]
    public void Network_NeedsANetwork_AndToolMustExist()
    {
        Assert.Contains(Tool.Run("tool = network\n[Scenarios]\nScenario\tn\nA\t0.013\n", new DesignToolContext(UnitSystem.USCustomary)).Messages, m => m.Code == "E-BS-002");
        Assert.Contains(Run("tool = nope\n[Scenarios]\nScenario\tx\nA\t1\n").Messages, m => m.Code == "E-BS-002");
        Assert.Contains(Run("tool = batch-scenarios\n[Scenarios]\nScenario\tx\nA\t1\n").Messages, m => m.Code == "E-BS-002");
        var many = "tool = network\n[Scenarios]\nScenario\tn\n" + string.Concat(Enumerable.Range(1, 51).Select(i => $"S{i}\t0.013\n"));
        Assert.Contains(Run(many).Messages, m => m.Code == "E-BS-001");
    }

    [Fact]
    public void AnyTool_ComparisonCellsEqualDirectRuns_AndChartsOverlay()
    {
        var ctx = new DesignToolContext(UnitSystem.USCustomary);
        string input = Tool.SampleInput(ctx);
        var r = Tool.Run(input, ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var cmp = r.Tables.Single(t => t.Title == "Comparison");
        Assert.Equal(["Value (table / row / column)", "4 ft at 120 cfs", "5 ft at 120 cfs", "4 ft at 150 cfs", "5 ft at 150 cfs"], cmp.Headers);
        // Re-run the culvert directly with D = 5 and Q design = 150.
        int b = input.LastIndexOf("\n[Base input]", StringComparison.Ordinal) + "\n[Base input]".Length, e = input.LastIndexOf("[End base input]", StringComparison.Ordinal);
        string baseInput = input[b..e].Replace("D = 4", "D = 5").Replace("Q design = 120", "Q design = 150");
        var culvert = DesignTools.Find("hds5-culvert")!.Run(baseInput, ctx);
        Assert.True(culvert.Succeeded, string.Join("\n", culvert.Messages.Select(m => m.Text)));
        Assert.Equal(BatchScenarioTool.Cell(culvert, "Results", "Design", "HW elevation"), cmp.Rows[0][4]);
        Assert.Equal(BatchScenarioTool.Cell(culvert, "Results", "Check", "HW elevation"), cmp.Rows[3][4]);
        Assert.NotNull(cmp.Rows[0][1]);
        Assert.NotEqual(cmp.Rows[0][1], cmp.Rows[0][2]);                 // a bigger barrel lowers the headwater
        var chart = Assert.Single(r.Charts);
        Assert.Contains(chart.Series, s => s.Name.StartsWith("5 ft at 150 cfs"));
        Assert.Contains(chart.Series, s => s.Name.StartsWith("4 ft at 120 cfs"));
    }

    [Fact]
    public void AnyTool_FailuresAndUnknownKeysAreReported()
    {
        var r = Tool.Run("""
            tool = hds5-culvert
            [Scenarios]
            Scenario	D	colour
            Bad	-4
            Odd		blue
            [Compare]
            Table	Row	Column
            Nothing	Here	At all
            """, new DesignToolContext(UnitSystem.USCustomary));
        Assert.True(r.Succeeded);                                         // the batch runs; a failed scenario is reported
        Assert.Contains(r.Messages, m => m.Code == "I-BS-002");            // sample used as the base
        Assert.Contains(r.Messages, m => m.Code == "W-BS-001" && m.Text.Contains("Bad"));
        Assert.Contains(r.Messages, m => m.Code == "W-BS-002" && m.Text.Contains("colour"));
        Assert.Contains(r.Messages, m => m.Code == "W-BS-003");
        Assert.Equal("no", r.Tables[0].Rows[0][2]);
    }

    [Fact]
    public void TrySet_ReplacesTopLevelAndSectionKeys()
    {
        string t = "a = 1   # note\n[S]\nb = 2\nx\ty\n";
        Assert.True(BatchScenarioTool.TrySet(ref t, "a", "5"));
        Assert.True(BatchScenarioTool.TrySet(ref t, "S.b", "7"));
        Assert.False(BatchScenarioTool.TrySet(ref t, "c", "9"));
        Assert.False(BatchScenarioTool.TrySet(ref t, "T.d", "4"));
        var p = ToolInput.Parse(t);
        Assert.Equal(5, p.Number("a", 0));
        Assert.Equal(7, p.Number("S.b", 0));
        Assert.Equal(9, p.Number("c", 0));
        Assert.Equal(4, p.Number("T.d", 0));
        Assert.Contains("# note", t);
        Assert.Equal(["x", "y"], p.Header("S"));
    }

    [Fact]
    public void Network_SI()
    {
        var n = NetworkOutputTests.NetA();
        var si = n with
        {
            Units = UnitSystem.SI, TailwaterElevation = n.TailwaterElevation * 0.3048,
            Structures = n.Structures.Select(s => s with { RimElevation = s.RimElevation * 0.3048 }).ToList(),
            Pipes = n.Pipes.Select(p => p with { Rise = p.Rise * 0.3048, Span = p.Span * 0.3048, Length = p.Length * 0.3048, UpstreamInvert = p.UpstreamInvert * 0.3048, DownstreamInvert = p.DownstreamInvert * 0.3048, Discharge = p.Discharge * 0.0283168 }).ToList(),
            Criteria = n.Criteria with { HglBelowRim = 0.3, MaxVelocity = 6, MinVelocity = 0.75, MinPipeRise = 0.3 },
        };
        var ctx = new DesignToolContext(UnitSystem.SI, si, StormNetworkSolver.Solve(si));
        var r = Tool.Run(Tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains("(m)", r.Tables[1].Title);
        // The upsized pipe is in mm: P1 610 mm + 150 = 760 mm.
        Assert.Contains(r.Tables[0].Rows, x => ((string)x[1]!).Contains("pipe P1 size = 760"));
    }
}
