using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Hydraulics.Tests;

/// <summary>
/// Pipe sizing. Capacities are checked with the closed-form Manning full-flow equation written out here,
/// independent of the sizer: Qfull = (1.486/n)·(πD²/4)·(D/4)^⅔·S^½.
/// </summary>
public class NetworkSizerTests
{
    private static double QFull(double d, double s, double n = 0.013) => 1.486 / n * (Math.PI * d * d / 4) * Math.Pow(d / 4, 2.0 / 3.0) * Math.Sqrt(s);

    private static NetworkPipe Pipe(string name, string from, string to, double d, double l, double up, double dn, double q) =>
        new() { Name = name, From = from, To = to, Rise = d, Span = d, Length = l, UpstreamInvert = up, DownstreamInvert = dn, Discharge = q };

    private static StormNetwork Net(TailwaterRule tw = TailwaterRule.HalfCriticalPlusDiameter, double rimCb4 = 108) => new()
    {
        Tailwater = tw, TailwaterElevation = 102.5, Criteria = NetworkCriteria.RapidCity2012,
        Structures =
        [
            new() { Name = "OUT", Kind = "Outlet", RimElevation = 108 }, new() { Name = "MH-2", Kind = "Manhole", RimElevation = 108 },
            new() { Name = "CB-3", Kind = "Inlet", RimElevation = 108 }, new() { Name = "CB-4", Kind = "Inlet", RimElevation = rimCb4 },
        ],
        Pipes = [Pipe("P1", "MH-2", "OUT", 1.0, 200, 100.8, 100.0, 20), Pipe("P2", "CB-3", "MH-2", 1.0, 150, 102.0, 101.1, 12), Pipe("P3", "CB-4", "MH-2", 1.0, 60, 101.8, 101.3, 8)],
    };

    private static readonly double[] Sizes = [12, 15, 18, 21, 24, 27, 30, 33, 36, 42, 48, 54, 60, 66, 72, 78, 84, 90, 96, 102, 108];

    [Fact]
    public void Pass1_SmallestStandardSize_CarryingQPartFull_NotSmallerThanUpstream()
    {
        var p = NetworkSizer.Size(Net(), hglPass: false).Network.Pipes.ToDictionary(x => x.Name);
        foreach (var (name, q, s) in new[] { ("P2", 12.0, 0.9 / 150), ("P3", 8.0, 0.5 / 60) })
        {
            double expect = Sizes.Select(d => d / 12).First(d => d >= 1.0 && QFull(d, s) >= q);
            Assert.Equal(expect, p[name].Rise, 9);
        }
        double p1 = Sizes.Select(d => d / 12).First(d => d >= Math.Max(p["P2"].Rise, p["P3"].Rise) && QFull(d, 0.8 / 200) >= 20);
        Assert.Equal(p1, p["P1"].Rise, 9);

        var r = NetworkSizer.Size(Net());
        var f = r.Network.Pipes.ToDictionary(x => x.Name);
        Assert.True(f["P1"].Rise >= f["P2"].Rise && f["P1"].Rise >= f["P3"].Rise);
        Assert.All(r.Network.Pipes, x => Assert.True(x.Rise >= p[x.Name].Rise - 1e-9));   // pass 2 only increases
        Assert.All(r.Network.Pipes, x => Assert.Equal(x.Rise, x.Span));
        // With high rims, every Rapid City HGL and surcharge criterion is met after sizing.
        Assert.DoesNotContain(r.Solution.Messages, m => m.Code is "W-NW-001" or "W-NW-005");
        Assert.All(r.Changes, c => Assert.True(c.NewRise > c.OldRise));
        // Inverts, lengths and Q are untouched.
        Assert.Equal(Net().Pipes.Select(x => (x.UpstreamInvert, x.DownstreamInvert, x.Length, x.Discharge)), r.Network.Pipes.Select(x => (x.UpstreamInvert, x.DownstreamInvert, x.Length, x.Discharge)));
    }

    [Fact]
    public void Pass2_UpsizesForTailwater_AndReportsWhatCannotBeMet()
    {
        // Low rim at CB-4 (2.2 ft above its invert): the 1 ft criterion cannot be met by sizing alone.
        var r = NetworkSizer.Size(Net(TailwaterRule.Elevation, rimCb4: 104));
        Assert.Contains(r.Notes, n => n.StartsWith("CB-4: criterion not met"));
        var rims = r.Network.Structures.ToDictionary(s => s.Name, s => s.RimElevation!.Value);
        Assert.All(r.Network.Pipes, p =>
        {
            Assert.True(p.UpstreamInvert + p.Rise <= rims[p.From] + 1e-9);   // crown below the rim at both ends
            Assert.True(p.DownstreamInvert + p.Rise <= rims[p.To] + 1e-9);
        });
        // Tailwater 102.5 submerges a small outlet pipe; it is upsized until it no longer flows full or cannot help.
        Assert.True(r.Network.Pipes.Single(p => p.Name == "P1").Rise > 2.0);
    }

    [Fact]
    public void EllipticalPipes_AreLeftAlone_AndBadNetworkIsReported()
    {
        var n = Net() with { Pipes = Net().Pipes.Select(p => p.Name == "P2" ? p with { Shape = NetworkPipeShape.Elliptical, Rise = 19.0 / 12, Span = 30.0 / 12 } : p).ToList() };
        var r = NetworkSizer.Size(n);
        Assert.Equal(19.0 / 12, r.Network.Pipes.Single(p => p.Name == "P2").Rise, 12);
        Assert.Contains(r.Notes, x => x.Contains("P2 is elliptical"));
        var bad = NetworkSizer.Size(Net() with { Pipes = [] });
        Assert.Empty(bad.Changes);
        Assert.Contains(bad.Notes, x => x.Contains("does not solve"));
    }

    [Fact]
    public void NormalVelocity_MatchesIndependentBisection()
    {
        // python bisection: D = 2 ft, S = 0.002, Q = 3 cfs → V = 2.806070 fps
        var c = UnitConstants.For(UnitSystem.USCustomary);
        Assert.Equal(2.806070, NetworkSizer.NormalVelocity(2.0, 0.013, 0.002, 3, c), 4);
        Assert.Equal(QFull(2, 0.002), NetworkSizer.QFull(2, 0.013, 0.002, c), 9);
    }

    /// <summary>Review finding: a flat pipe whose existing size crosses the rim must not keep that size.</summary>
    [Fact]
    public void FlatPipe_WithLowRim_PicksAFeasibleSize()
    {
        var net = new StormNetwork
        {
            Tailwater = TailwaterRule.HalfCriticalPlusDiameter, Criteria = NetworkCriteria.RapidCity2012,
            Structures = [new() { Name = "A", Kind = "Inlet", RimElevation = 102 }, new() { Name = "OUT", Kind = "Outlet", RimElevation = 102 }],
            Pipes = [Pipe("P", "A", "OUT", 3.0, 100, 100.0, 100.0, 2)],
        };
        var r = NetworkSizer.Size(net, [18, 36], hglPass: false);
        var p = Assert.Single(r.Network.Pipes);
        Assert.Equal(1.5, p.Rise, 9);
        Assert.True(p.UpstreamInvert + p.Rise <= 102 + 1e-9);
        Assert.Contains(r.Notes, n => n.Contains("reduced"));
    }
}
