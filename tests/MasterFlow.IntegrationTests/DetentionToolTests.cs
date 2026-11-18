using MasterFlow.Application.Tools;

namespace MasterFlow.IntegrationTests;

/// <summary>Detention routing: closed-form storage and orifice/weir flow, then routing checked by mass balance and level-pool properties.</summary>
public class DetentionToolTests
{
    private const double G = 32.174;

    [Fact]
    public void Storage_FrustumMethod()
    {
        var pond = new Pond([(0, 100), (1, 400)], [new PondOutlet("w", "weir", 0, 1, 0, 3)], G, true);
        Assert.Equal((100 + 400 + 200) / 3.0, pond.Storage(1), 9);
        // halfway: side √A from 10 to 20 → 15, A = 225; V = 0.5/3 (100 + 225 + 150)
        Assert.Equal(0.5 / 3 * (100 + 225 + 150), pond.Storage(0.5), 9);
        Assert.True(double.IsNaN(pond.Storage(1.5)));
    }

    [Fact]
    public void OrificeAndWeir_HandValues()
    {
        var orf = new PondOutlet("o", "orifice", 100, 0.75, 0, 0.6);
        Assert.Equal(0.6 * Math.PI * 0.75 * 0.75 / 4 * Math.Sqrt(2 * G * (2 - 0.375)), orf.Flow(102, G), 9);
        Assert.Equal(0, orf.Flow(99.9, G));
        Assert.Equal(3.0 * 6 * Math.Pow(0.5, 1.5), new PondOutlet("w", "weir", 104, 6, 0, 3.0).Flow(104.5, G), 9);
        // barrel: 2 ft pipe, 80 ft, n 0.013, Ke 0.5, inverts 99.5 / 98.7; capacity at WSE 105 is the flow whose HDS-5
        // controlling headwater is 5.5 ft (square-edge concrete headwall inlet, free outfall).
        var pipe = new PondOutlet("p", "pipe", 99.5, 2, 0, 0.5, 80, 0.013, 98.7);
        double qb = pipe.BarrelCapacity(105, G, true);
        double a = Math.PI, x = qb / (a * Math.Sqrt(2));
        double hwi = 2 * (0.0398 * x * x + 0.67 - 0.5 * 0.8 / 80);                  // submerged inlet control, by hand
        double kf = 2 * G * 0.013 * 0.013 * 80 / (1.486 * 1.486 * Math.Pow(0.5, 4.0 / 3.0));
        double dc = PondOutlet.CircularCriticalDepth(qb, 2, G);
        double hwo = (dc + 2) / 2 + (1.5 + kf) * Math.Pow(qb / a, 2) / (2 * G) - 0.8;  // outlet control, free outfall
        Assert.Equal(5.5, Math.Max(hwi, hwo), 6);
        // dc satisfies Q²/g = A³/T independently.
        double th = 2 * Math.Acos(1 - dc), aC = 4.0 / 8 * (th - Math.Sin(th)), tC = 2 * Math.Sin(th / 2);
        Assert.Equal(qb * qb / G, aC * aC * aC / tC, 4);
    }

    [Fact]
    public void Routing_ConservesVolume_Attenuates_AndDrains()
    {
        var pond = new Pond([(100, 6000), (102, 12500), (106, 34000)], [new PondOutlet("o", "orifice", 100, 1.0, 0, 0.6), new PondOutlet("w", "weir", 104, 6, 0, 3)], G, true);
        double q = 20, tc = 15, td = 30;
        var hyd = Enumerable.Range(0, (int)(td + tc) + 1).Select(t => t <= tc ? q * t / tc : t <= td ? q : q * (td + tc - t) / tc).ToList();
        var outs = new List<double>();
        var r = pond.Route(hyd, 60, outflows: outs);
        Assert.False(r.Overtopped);
        Assert.True(r.PeakOutflow < q);
        double vin = hyd.Zip(hyd.Skip(1)).Sum(x => (x.First + x.Second) / 2 * 60);
        double vout = new[] { 0.0 }.Concat(outs).Zip(outs).Sum(x => (x.First + x.Second) / 2 * 60);
        Assert.InRange(vout / vin, 0.995, 1.001);         // remaining 0.1 % of the peak storage at the drain criterion
        Assert.True(r.DrainHours > 0 && r.DrainHours < 72);
    }

    [Fact]
    public void Tool_FlagsOvertopping_AndFreeboard()
    {
        var tool = new DetentionTool();
        var ctx = DesignToolTests.Ctx();
        var r = tool.Run(tool.SampleInput(ctx).Replace("area = 12 ", "area = 60 "), ctx);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == "E-DT-011");
        r = tool.Run(tool.SampleInput(ctx).Replace("top of berm = 106.5", "top of berm = 104.0"), ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-DT-004");
        r = tool.Run(tool.SampleInput(ctx).Replace("Outlet pipe\tpipe\t99.5\t2.0", "Outlet pipe\tpipe\t99.5\t1.25"), ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-DT-005");
    }

    /// <summary>
    /// Review finding: with a 1 ft orifice and a 2 ft barrel, outflow fell from 4.6 to 0.15 cfs between 1.9999 and 2.0001 ft.
    /// The rating must be continuous at the crown and never fall as the stage rises.
    /// </summary>
    [Fact]
    public void BarrelRating_IsContinuousAtCrown_AndNonDecreasing()
    {
        var barrel = new PondOutlet("b", "pipe", 0, 2, 0, 0.5, 60, 0.013, -0.3);
        var pond = new Pond([(0, 5000), (6, 20000)], [new PondOutlet("o", "orifice", 0, 1, 0, 0.6), barrel], G, true);
        double below = pond.Outflow(1.9999), above = pond.Outflow(2.0001);
        Assert.InRange(Math.Abs(above - below), 0, 0.01);
        Assert.Equal(barrel.BarrelCapacity(1.9999, G, true), barrel.BarrelCapacity(2.0001, G, true), 2);
        double prev = 0;
        for (double e = 0; e <= 6 + 1e-9; e += 0.001)
        {
            double q = pond.Outflow(e);
            Assert.True(q >= prev - 1e-9, $"outflow falls at {e:F3}: {prev} → {q}");
            prev = q;
        }
    }
}
