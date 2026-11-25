using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-24 pump station: Hazen-Williams, operating points, mass balance of the routing, cycling volume.</summary>
public class PumpStationTests
{
    private static readonly PumpStationTool Tool = new();
    private static readonly List<(double Q, double H)> Curve = [(0, 40), (4, 36), (6.5, 32), (9, 26), (11.5, 18)];

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => Tool.Run(input, new DesignToolContext(u));

    [Fact]
    public void Sample_RunsClean()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.False(Run("x = 1", UnitSystem.SI).Succeeded);
    }

    [Fact]
    public void HazenWilliams_ByHand() =>
        Assert.Equal(4.73 * 400 * Math.Pow(10, 1.852) / (Math.Pow(120, 1.852) * Math.Pow(2, 4.87)), PumpStationTool.HazenWilliams(10, 400, 2, 120), 9);

    [Fact]
    public void OperatingPoint_PumpHeadEqualsSystemHead()
    {
        double q = PumpStationTool.Operating(1, 3392, 3420, 400, 2, 120, 4.5, Curve);
        double v = q / (Math.PI);
        double sys = 28 + PumpStationTool.HazenWilliams(q, 400, 2, 120) + 4.5 * v * v / 64.4;
        Assert.Equal(sys, PumpStationTool.Lerp(Curve.Select(c => (c.Q, c.H)).ToList(), q), 3);
        // Two pumps in parallel give more total flow but less than twice one pump.
        double q2 = PumpStationTool.Operating(2, 3392, 3420, 400, 2, 120, 4.5, Curve);
        Assert.InRange(q2, q, 2 * q);
        // A static head above the shut-off head gives no flow.
        Assert.Equal(0, PumpStationTool.Operating(1, 3370, 3420, 400, 2, 120, 4.5, Curve));
    }

    [Fact]
    public void Routing_ConservesVolume()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)).Replace("time step = 0.5", "time step = 0.1"));
        var chart = r.Charts[0];
        double Integrate(IReadOnlyList<(double X, double Y)> s) => s.Zip(s.Skip(1)).Sum(t => (t.Second.X - t.First.X) * t.First.Y) * 60;
        double vin = Integrate(chart.Series[0].Points), vout = Integrate(chart.Series[1].Points);
        // Everything that came in is pumped out except what is left below the lowest start level.
        Assert.InRange(vin - vout, -50, 2700 + 50);
        Assert.True(vout > 0.8 * vin);
    }

    [Fact]
    public void SmallStartStopBand_IsFlagged()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)).Replace("Lead\t3392.0\t3390.0", "Lead\t3390.4\t3390.0"));
        Assert.Contains(r.Messages, m => m.Code == "W-PS-003");
    }
}
