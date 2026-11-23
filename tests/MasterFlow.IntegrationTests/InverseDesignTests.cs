using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Inverse design (premium, D-240): the largest or smallest value of one input that still meets every constraint.</summary>
public class InverseDesignTests
{
    private static readonly InverseDesignTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static string Detention => DesignTools.Find("detention-routing")!.SampleInput(Us);

    private static DesignToolContext Net()
    {
        var net = NetworkOutputTests.NetA();
        var res = StormNetworkSolver.Solve(net);
        return new DesignToolContext(UnitSystem.USCustomary, net, res) { Networks = [(net, res)] };
    }

    private static object? Cell(ToolResult r, string table, int row, int col) => r.Tables.Single(t => t.Title.StartsWith(table, StringComparison.Ordinal)).Rows[row][col];

    [Fact]
    public void Sample_FindsLargestOrificeHeldBy2YrRelease()
    {
        var r = Tool.Run(Tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Contains(r.Summary, s => s.StartsWith("Largest Low-flow orifice Size that meets every constraint: 1.0", StringComparison.Ordinal) && s.Contains(" ft."));
        Assert.Contains(r.Summary, s => s.StartsWith("Governing: {value: detention-routing | Routing results | 2-yr", StringComparison.Ordinal));
        Assert.Contains(r.Summary, s => s.StartsWith("The design now uses 0.75 ft", StringComparison.Ordinal));
        // At the answer the 2-yr release is just under 4 cfs; every constraint is OK.
        var at = r.Tables.Single(t => t.Title.StartsWith("Constraints at", StringComparison.Ordinal));
        Assert.All(at.Rows, row => Assert.Equal("OK", row[4]));
        Assert.InRange(double.Parse((string)at.Rows[0][2]!, System.Globalization.CultureInfo.InvariantCulture), 3.95, 4.0);
        // Re-running detention at a slightly larger orifice fails the 2-yr limit: the answer is the limit.
        var trials = r.Tables.Single(t => t.Title == "Trials");
        var firstFail = trials.Rows.First(x => (string)x[^1]! == "no");
        Assert.True((double)firstFail[1]! > 4);
        Assert.Single(r.Charts);
    }

    [Fact]
    public void Min_FindsSmallestOrificeThatDrainsInTime()
    {
        var input = "tool = detention-routing\nvary = Outlets | Low-flow orifice | Size\nfind = min\nfrom = 0.25\nto = 1.5\nsteps = 10\ntolerance = 0.002\n[Constraints]\n{value: detention-routing | Routing results | max | Drain time} <= 40\n";
        var r = Tool.Run(input, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Contains(r.Summary, s => s.StartsWith("Smallest", StringComparison.Ordinal));
        var at = r.Tables.Single(t => t.Title.StartsWith("Constraints at", StringComparison.Ordinal));
        Assert.InRange(double.Parse((string)at.Rows[0][2]!, System.Globalization.CultureInfo.InvariantCulture), 38, 40);
    }

    [Fact]
    public void Key_VariesAreaAgainstTextAndNumberConstraints()
    {
        var input = "tool = detention-routing\nvary = area\nfrom = 2\nto = 30\nsteps = 14\n[Constraints]\n{value: detention-routing | Routing results | 100-yr | Peak outflow} <= 16\n"
                    + "{value: detention-routing | Criteria checks | 100-yr peak outflow | Result} = OK\n[Base input]\n" + Detention + "\n[End base input]\n";
        var r = Tool.Run(input, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Contains(r.Summary, s => s.Contains("room before a constraint fails"));     // the sample uses 12 ac
        var trials = r.Tables.Single(t => t.Title == "Trials");
        Assert.Contains(trials.Rows, x => (string)x[^1]! == "no");
        Assert.Equal("OK", Cell(r, "Constraints at", 1, 2));
    }

    [Fact]
    public void Network_MostFlowBeforeHglReachesClearance()
    {
        var ctx = Net();
        var r = Tool.Run("vary = network: Main St | P1 | q\nfrom = 5\nto = 60\nsteps = 11\n[Constraints]\n{network: Main St | MH-2 | clearance} >= 1\n{network: Main St | P1 | v} <= 15\n", ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Contains(r.Summary, s => s.Contains("cfs") && s.StartsWith("Largest Main St P1 q", StringComparison.Ordinal));
        Assert.Contains(r.Summary, s => s.StartsWith("Governing: {network: Main St | MH-2 | clearance}", StringComparison.Ordinal));
        // Check by solving the network directly at the answer: clearance at MH-2 is still ≥ 1 ft.
        var x = (double)r.Tables.Single(t => t.Title == "Trials").Rows.Last(row => (string)row[^1]! == "yes")[0]!;
        var n2 = ctx.Networks[0].Network with { Pipes = [.. ctx.Networks[0].Network.Pipes.Select(p => p.Name == "P1" ? p with { Discharge = x } : p)] };
        var mh = StormNetworkSolver.Solve(n2).Structures.Single(s => s.Structure.Name == "MH-2");
        Assert.True(mh.HglClearance >= 1);
    }

    [Fact]
    public void RangeAllPassingOrAllFailing_Warns()
    {
        var ctx = Net();
        var pass = Tool.Run("vary = network: Main St | P1 | q\nfrom = 1\nto = 5\n[Constraints]\n{network: Main St | P1 | v} <= 15\n", ctx);
        Assert.Contains(pass.Messages, m => m.Code == "W-ID-002");
        var fail = Tool.Run("vary = network: Main St | P1 | q\nfrom = 40\nto = 60\n[Constraints]\n{network: Main St | MH-2 | clearance} >= 1\n", ctx);
        Assert.Contains(fail.Messages, m => m.Code == "W-ID-003" && m.Text.Contains("clearance"));
    }

    [Theory]
    [InlineData("tool = detention-routing\nfrom = 0\nto = 1\n[Constraints]\n{value: a | b | c | d} <= 1\n", "E-ID-001")]
    [InlineData("tool = detention-routing\nvary = area\nfrom = 1\nto = 1\n[Constraints]\n{value: a | b | c | d} <= 1\n", "E-ID-003")]
    [InlineData("tool = detention-routing\nvary = area\nfrom = 1\nto = 2\n", "E-ID-005")]
    [InlineData("tool = detention-routing\nvary = area\nfrom = 1\nto = 2\n[Constraints]\n{value: a | b | c | d} <= big\n", "E-ID-012")]
    [InlineData("tool = nope\nvary = area\nfrom = 1\nto = 2\n[Constraints]\n{value: a | b | c | d} <= 1\n", "E-ID-006")]
    [InlineData("tool = detention-routing\nvary = Outlets | Nothing | Size\nfrom = 1\nto = 2\n[Constraints]\n{value: a | b | c | d} <= 1\n", "E-ID-010")]
    [InlineData("vary = network: Main St | P1 | color\nfrom = 1\nto = 2\n[Constraints]\n{value: a | b | c | d} <= 1\n", "E-ID-009")]
    [InlineData("tool = detention-routing\nvary = area\nfrom = 1\nto = 2\n[Constraints]\n{value: detention-routing | Nope | 2-yr | Peak outflow} <= 1\n", "E-ID-011")]
    public void BadInput_Errors(string input, string code)
    {
        var r = Tool.Run(input, Net());
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }

    [Fact]
    public void SetCell_ReplacesOneTabbedCell()
    {
        var t = InverseDesignTool.SetCell("[Outlets]\nName\tType\tSize (ft)\nRiser\triser\t3.0\n", "Outlets", "riser", "Size", "2.5")!;
        Assert.Contains("Riser\triser\t2.5", t);
        Assert.Null(InverseDesignTool.SetCell("[Outlets]\nName, Size\nA, 1\n", "Outlets", "B", "Size", "2"));
        Assert.Contains("A, 2", InverseDesignTool.SetCell("[Outlets]\nName, Size\nA, 1\n", "Outlets", "A", "Size", "2"));
    }

    [Fact]
    public void IsPremium() => Assert.Equal("inverse-design", PremiumFeatures.ForTool("inverse-design")?.Id);
}
