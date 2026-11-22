using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>"What changed?" redesign diff: what-if edits, baseline networks, baseline project files with channels and tools.</summary>
public class RedesignDiffTests
{
    private static StormNetwork Net => NetworkOutputTests.NetA();

    private static DesignToolContext Ctx(StormNetwork current, params StormNetwork[] others) =>
        new(UnitSystem.USCustomary, current, StormNetworkSolver.Solve(current)) { Networks = [(current, null), .. others.Select(o => (o, (StormNetworkResult?)null))] };

    private static ToolResult Run(string input, DesignToolContext ctx)
    {
        var r = new RedesignDiffTool().Run(input, ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> Changes(ToolResult r) => r.Tables.Single(t => t.Title == "Changes").Rows;
    private static IReadOnlyList<object?>? Find(ToolResult r, string element, string check) =>
        Changes(r).FirstOrDefault(x => ((string)x[1]!).EndsWith(element) && ((string)x[2]!).StartsWith(check));

    /// <summary>NetA with the network's lines relieved so most checks pass (bigger pipes, lower flows).</summary>
    private static StormNetwork Healthy => Net with
    {
        Pipes = Net.Pipes.Select(p => p with { Rise = p.Rise + 0.75, Span = p.Span + 0.75, Discharge = p.Discharge * 0.4 }).ToList(),
        TailwaterElevation = 101.0,
    };

    [Fact]
    public void WhatIf_LoweredRim_IsListedWithItsMargins_AndTheCause()
    {
        var n = Healthy;
        var before = StormNetworkSolver.Solve(n);
        double clear = before.Structure("MH-2")!.HglClearance!.Value;
        Assert.True(clear > 1.5);
        var r = Run($"[What if]\nItem, Field, Value\nMH-2, rim change, {-(clear - 0.5):0.000}", Ctx(n));
        var row = Find(r, "MH-2", "HGL below the rim")!;
        Assert.Equal("NEW FAIL", row[0]);
        Assert.Equal(Math.Round(clear, 3), (double)row[4]!, 3);
        Assert.Equal(0.5, (double)row[5]!, 2);
        Assert.Equal(-0.5, (double)row[9]!, 2);
        // Cover at MH-2 shrank by the same amount: LESS MARGIN (or a new failure if it went under 1.5 ft).
        Assert.Contains(Changes(r), x => ((string)x[1]!).Contains("at MH-2") && (string)x[2]! == "Cover over the crown");
        var cause = r.Tables.Single(t => t.Title.StartsWith("What was changed")).Rows.Single();
        Assert.Equal("rim", cause[2]);
        Assert.StartsWith("4 new failure", r.Summary[1]);   // HGL at MH-2 and the cover of the three pipes that meet there
        // Nothing else moved.
        Assert.DoesNotContain(Changes(r), x => ((string)x[1]!).EndsWith("CB-3"));
    }

    [Fact]
    public void WhatIf_NoRealChange_ReportsNothing()
    {
        var r = Run("[What if]\nItem, Field, Value\n*, q factor, 1.02", Ctx(Healthy));
        Assert.Empty(Changes(r));
        Assert.StartsWith("Nothing got worse", r.Summary[1]);
        var all = Run("show unchanged = yes\n[What if]\nItem, Field, Value\n*, q factor, 1.02", Ctx(Healthy));
        Assert.Contains(Changes(all), x => (string)x[0]! == "UNCHANGED");
    }

    [Fact]
    public void BaselineNetwork_FixedAndAddedPipes()
    {
        var baseline = Net with { Name = "Main St v1" };
        var current = Healthy with { Pipes = [.. Healthy.Pipes, new NetworkPipe { Name = "P9", From = "CB-9", To = "MH-2", Rise = 0.5, Span = 0.5, Length = 50, UpstreamInvert = 102.5, DownstreamInvert = 102.0, Discharge = 1 }],
            Structures = [.. Healthy.Structures, new NetworkStructure { Name = "CB-9", Kind = "Inlet", RimElevation = 106 }] };
        var r = Run("baseline network = Main St v1", Ctx(current, baseline));
        Assert.Equal("FIXED", Find(r, "P2", "Design flow within full-flow capacity")![0]);
        Assert.Equal("NEW FAIL", Find(r, "P9", "Minimum pipe size")![0]);   // 6 in. under the 12 in. minimum
        Assert.Contains(r.Tables.Single(t => t.Title.StartsWith("What was changed")).Rows, x => (string)x[1]! == "P9" && (string)x[4]! == "added");
        Assert.NotEmpty(r.Charts);
        // Ordering: failures first.
        Assert.Equal("NEW FAIL", Changes(r)[0][0]);
    }

    [Fact]
    public void BaselineProject_ComparesChannelsAndTools()
    {
        var calc = new ChannelCalculation
        {
            Title = "Ditch A",
            Input = new UniformFlowInput { Units = UnitSystem.USCustomary, Section = new TrapezoidalSection(4, 3, 3) { ChannelDepth = 2.5 }, SolveFor = SolveFor.NormalDepth, ManningN = 0.035, Slope = 0.005, Discharge = 40 },
            Criteria = new DesignCriteria { RequiredFreeboard = 0.5, MaxVelocity = 5 },
        };
        string tr55(double p) => $"storms = 25\nP25 = {p}\nhydrograph storm = none\ncriteria = none\n[Land cover]\nBasin\tCover\tHSG\tArea (ac)\tCN\nW\t \t \t250\t75\n[Basins]\nBasin\tTc (min)\nW\t91.8\n";
        var baseline = new Project { Networks = [Healthy], Calculations = [calc], ToolInputs = new Dictionary<string, string> { ["tr55-hydrology"] = tr55(6.0) } };
        var now = calc with { Input = calc.Input with { Discharge = 75 } };
        var ctx = Ctx(Healthy) with { Calculations = [now], ToolInputs = new Dictionary<string, string> { ["tr55-hydrology"] = tr55(7.0) }, LoadProject = _ => baseline };
        var r = Run("baseline = old.mfproj", ctx);
        Assert.Contains("old.mfproj", r.Summary[0]);
        // 75 cfs in the ditch: the freeboard shrinks (fails or loses margin).
        var fb = Find(r, "Ditch A", "Freeboard")!;
        Assert.Contains((string)fb[0]!, new[] { "NEW FAIL", "LESS MARGIN" });
        // The TR-55 peak rose by more than 10 %.
        var tools = r.Tables.Single(t => t.Title.StartsWith("Design tool results")).Rows;
        Assert.Contains(tools, x => ((string)x[1]!).StartsWith("Worksheet 4") && (double)x[6]! > 10);
        // The network did not change, so nothing is listed for it.
        Assert.DoesNotContain(Changes(r), x => ((string)x[1]!).StartsWith("Main St"));
        // A file that cannot be opened is reported.
        var bad = new RedesignDiffTool().Run("baseline = x.mfproj", ctx with { LoadProject = _ => throw new IOException("not found") });
        Assert.Contains(bad.Messages, m => m.Code == "E-RD-002");
    }

    [Fact]
    public void Errors_AndPremium()
    {
        var tool = new RedesignDiffTool();
        Assert.Contains(tool.Run("margin change = 10", Ctx(Net)).Messages, m => m.Code == "E-RD-006");
        Assert.Contains(tool.Run("[What if]\nItem, Field, Value\nZZ, rim, 100", Ctx(Net)).Messages, m => m.Code == "E-RD-013");
        Assert.Contains(tool.Run("[What if]\nItem, Field, Value\nP1, colour, 3", Ctx(Net)).Messages, m => m.Code == "E-RD-012");
        Assert.Contains(tool.Run("baseline network = nope", Ctx(Net)).Messages, m => m.Code == "E-RD-003");
        Assert.True(PremiumFeatures.IsPremium(tool));
    }
}
