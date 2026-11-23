using MasterFlow.Application.Routing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Dynamic-wave routing: steady limits against Manning and full-pipe friction, continuity, flooding and the tool.</summary>
public class DynamicWaveTests
{
    private static readonly DynamicWaveTool Tool = new();

    private static NetworkPipe Pipe(string n, string f, string t, double d, double len, double ui, double di, double q = double.NaN) =>
        new() { Name = n, From = f, To = t, Rise = d, Span = d, Length = len, UpstreamInvert = ui, DownstreamInvert = di, Discharge = q };

    private static StormNetwork OnePipe(TailwaterRule tw = TailwaterRule.FreeOutfall, double? twElev = null, double s = 0.005) => new()
    {
        Name = "one", Units = UnitSystem.USCustomary,
        Structures = [new() { Name = "A", RimElevation = 130 }, new() { Name = "B" }],
        Tailwater = tw, TailwaterElevation = twElev,
        Pipes = [Pipe("X", "A", "B", 2.0, 1000, 105 + 1000 * s, 105)],
    };

    private static Func<string, double, double> Constant(string node, double q) => (n, _) => n == node ? q : 0;

    [Fact]
    public void SteadyPartFullFlow_ApproachesNormalDepth()
    {
        var r = new DynamicWave(OnePipe(), Constant("A", 10), new DynamicWaveOptions { TimeStep = 2, Duration = 3600 }).Run();
        Assert.Equal(10, r.LinkFlows["X"][^1], 2);
        // Normal depth by Manning for Q = 10 cfs, D = 2 ft, n = 0.013, S = 0.005.
        double yn = NormalDepth(2.0, 0.013, 0.005, 10);
        Assert.InRange(r.NodeHeads["A"][^1] - 110, yn - 0.08, yn + 0.08);
        Assert.InRange(r.ContinuityError, -0.2, 0.2);
    }

    [Fact]
    public void SteadyFullPipe_HeadLossIsManningFriction()
    {
        // Tailwater 3 ft above the outlet crown: the pipe flows full and H_A − TW = Sf·L.
        var net = OnePipe(TailwaterRule.Elevation, 110.0);
        var r = new DynamicWave(net, Constant("A", 20), new DynamicWaveOptions { TimeStep = 1, Duration = 2400, JunctionLosses = false }).Run();
        double a = Math.PI, rh = 0.5, sf = Math.Pow(20 * 0.013 / (1.486 * a * Math.Pow(rh, 2.0 / 3)), 2);
        Assert.Equal(20, r.LinkFlows["X"][^1], 2);
        Assert.Equal(110 + sf * 1000, r.NodeHeads["A"][^1], 2);
    }

    [Fact]
    public void JunctionLoss_AddsKVelocityHead()
    {
        var net = OnePipe(TailwaterRule.Elevation, 110.0) with { DefaultJunctionK = 0.5 };
        var r = new DynamicWave(net, Constant("A", 20), new DynamicWaveOptions { TimeStep = 1, Duration = 2400 }).Run();
        double a = Math.PI, v = 20 / a, sf = Math.Pow(20 * 0.013 / (1.486 * a * Math.Pow(0.5, 2.0 / 3)), 2);
        Assert.Equal(110 + sf * 1000 + 0.5 * v * v / (2 * 32.174), r.NodeHeads["A"][^1], 2);
    }

    [Fact]
    public void StaticTailwater_StaysAtRest()
    {
        var r = new DynamicWave(OnePipe(TailwaterRule.Elevation, 108.0, s: 0.001), (_, _) => 0, new DynamicWaveOptions { Duration = 600 }).Run();
        Assert.All(r.NodeHeads["A"], h => Assert.Equal(108.0, h, 3));
        Assert.All(r.LinkFlows["X"], q => Assert.Equal(0, q, 3));
    }

    private static StormNetwork NetA(double? cb4Rim = 104.0) => new()
    {
        Name = "Main St",
        Structures = [new() { Name = "OUT", RimElevation = 105 }, new() { Name = "MH-2", RimElevation = 106.0 }, new() { Name = "CB-3", RimElevation = 106.5 }, new() { Name = "CB-4", RimElevation = cb4Rim }],
        Tailwater = TailwaterRule.Elevation, TailwaterElevation = 102.50,
        Pipes = [Pipe("P1", "MH-2", "OUT", 2.0, 200, 100.80, 100.00, 20), Pipe("P2", "CB-3", "MH-2", 1.5, 150, 102.00, 101.10, 12), Pipe("P3", "CB-4", "MH-2", 1.25, 60, 101.80, 101.30, 8)],
        Criteria = NetworkCriteria.RapidCity2012,
    };

    private static Func<string, double, double> Triangles(StormNetwork net)
    {
        var pk = DynamicWaveTool.PeakInflows(net);
        return (n, t) => { double qp = pk.GetValueOrDefault(n); return t <= 900 ? qp * t / 900 : t <= 900 + 1503 ? qp * (1 - (t - 900) / 1503) : 0; };
    }

    [Fact]
    public void Network_ConservesVolume_AndAttenuates()
    {
        var net = NetA() with { Structures = NetA().Structures.Select(x => x with { RimElevation = 120 }).ToList() };
        var r = new DynamicWave(net, Triangles(net), new DynamicWaveOptions { TimeStep = 1, Duration = 4500 }).Run();
        Assert.Equal(0.5 * 20 * 2403, r.InflowVolume, 0);
        Assert.InRange(r.ContinuityError, -0.5, 0.5);
        Assert.Equal(0, r.FloodVolume);
        Assert.True(r.Links.Single(l => l.Name == "P1").MaxFlow <= 20 + 1e-6);
    }

    [Fact]
    public void LowRim_Floods_AndHgLStopsAtTheRim()
    {
        var net = NetA();
        var r = new DynamicWave(net, Triangles(net), new DynamicWaveOptions { TimeStep = 1, Duration = 4500 }).Run();
        var cb4 = r.Nodes.Single(n => n.Name == "CB-4");
        Assert.True(cb4.FloodVolume > 0);
        Assert.Equal(104.0, cb4.MaxHead, 6);
        Assert.InRange(r.ContinuityError, -0.5, 0.5);
    }

    [Fact]
    public void Tool_Sample_RunsOnTheSelectedNetwork()
    {
        var ctx = DesignToolTests.Ctx();
        var r = Tool.Run(Tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var nodes = r.Tables.Single(t => t.Title.StartsWith("Structures")).Rows;
        Assert.Equal(4, nodes.Count);
        Assert.NotNull(nodes.Single(x => (string)x[0]! == "MH-2")[5]);     // steady HGL for comparison
        Assert.Equal(3, r.Charts.Count);
        Assert.Contains(r.Messages, m => m.Code == "W-DW-002");            // CB-4 rim 104.0 floods
        Assert.Equal("dynamic-wave", Application.Licensing.PremiumFeatures.ForTool("dynamic-wave")!.Id);
    }

    [Fact]
    public void Tool_TableHydrographs()
    {
        var ctx = DesignToolTests.Ctx();
        var r = Tool.Run("hydrograph = table\nduration = 60\n[Hydrographs]\nTime (min)\tCB-3\tCB-4\n0\t0\t0\n10\t6\t3\n30\t0\t0", ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        double vin = (double)r.Tables.Single(t => t.Title.StartsWith("Run")).Rows.Single(x => (string)x[0]! == "Inflow volume")[1]!;
        Assert.Equal(0.5 * 9 * 1800, vin, 0);
    }

    [Fact]
    public void PeakInflows_FromTypedPipeFlows()
    {
        var pk = DynamicWaveTool.PeakInflows(NetA());
        Assert.Equal(12, pk["CB-3"]);
        Assert.Equal(8, pk["CB-4"]);
        Assert.Equal(0, pk["MH-2"]);
        Assert.Equal(0, pk["OUT"]);
        Assert.Equal(["P2", "P1"], DynamicWaveTool.LongestPath(NetA()).Select(p => p.Name));
    }

    [Theory]
    [InlineData("hydrograph = wavy", "E-DW-002")]
    [InlineData("time to peak = 0", "E-DW-003")]
    [InlineData("manhole area = -1", "E-DW-004")]
    [InlineData("time step = 90", "E-DW-005")]
    [InlineData("duration = 0", "E-DW-006")]
    [InlineData("hydrograph = table", "E-DW-008")]
    [InlineData("hydrograph = table\n[Hydrographs]\nTime\tZZ\n0\t0\n5\t1", "E-DW-009")]
    public void BadInput_ReportsError(string input, string code)
    {
        var r = Tool.Run(input, DesignToolTests.Ctx());
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }

    [Fact]
    public void NoNetwork_IsAnError() => Assert.Contains(Tool.Run("", new DesignToolContext(UnitSystem.USCustomary)).Messages, m => m.Code == "E-DW-001");

    private static double NormalDepth(double d, double n, double s, double q)
    {
        double lo = 0, hi = 0.938 * d;
        for (int i = 0; i < 60; i++)
        {
            double y = 0.5 * (lo + hi), th = 2 * Math.Acos(1 - 2 * y / d), a = d * d / 8 * (th - Math.Sin(th)), p = d * th / 2;
            if (1.486 / n * a * Math.Pow(a / p, 2.0 / 3) * Math.Sqrt(s) < q) lo = y; else hi = y;
        }
        return 0.5 * (lo + hi);
    }
}
