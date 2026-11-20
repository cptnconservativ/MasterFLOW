using MasterFlow.Application.Optimization;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Network optimizer: the dynamic program is checked against brute-force enumeration on a small network, every rule
/// is checked on the result with the HGL solver (independent of the optimizer's own checks), plus SI units, the
/// least-excavation objective, invert limits, the HGL loop and bad input.
/// </summary>
public class NetworkOptimizerTests
{
    private static NetworkPipe Pipe(string name, string from, string to, double d, double l, double up, double dn, double q, double qm = double.NaN) =>
        new() { Name = name, From = from, To = to, Rise = d, Span = d, Length = l, UpstreamInvert = up, DownstreamInvert = dn, ManningN = 0.013, Discharge = q, MinorDischarge = qm };

    /// <summary>A → B → OUT down a gentle grade, free outfall.</summary>
    private static StormNetwork Chain() => new()
    {
        Name = "Chain",
        Structures =
        [
            new() { Name = "A", Kind = "Inlet", RimElevation = 108.0 },
            new() { Name = "B", Kind = "Manhole", RimElevation = 106.4 },
            new() { Name = "OUT", Kind = "Outlet", RimElevation = 103.0 },
        ],
        Tailwater = TailwaterRule.FreeOutfall,
        Pipes = [Pipe("P1", "A", "B", 1.25, 160, 104.5, 103.5, 4, 1.6), Pipe("P2", "B", "OUT", 1.5, 220, 103.3, 101.0, 9, 3.6)],
        Criteria = NetworkCriteria.RapidCity2012,
    };

    /// <summary>Two inlets into a manhole, then the outfall: a branch to test size progression and crown matching.</summary>
    private static StormNetwork Wye() => new()
    {
        Name = "Wye",
        Structures =
        [
            new() { Name = "CB-1", Kind = "Inlet", RimElevation = 110.0 },
            new() { Name = "CB-2", Kind = "Inlet", RimElevation = 109.2 },
            new() { Name = "MH-1", Kind = "Manhole", RimElevation = 108.0 },
            new() { Name = "OUT", Kind = "Outlet", RimElevation = 104.0 },
        ],
        Tailwater = TailwaterRule.FreeOutfall,
        Pipes = [Pipe("P1", "CB-1", "MH-1", 1.5, 180, 106.0, 105.0, 7, 2.8), Pipe("P2", "CB-2", "MH-1", 1.25, 90, 105.8, 105.2, 3, 1.2), Pipe("P3", "MH-1", "OUT", 1.5, 260, 104.8, 101.5, 10, 4)],
        Criteria = NetworkCriteria.RapidCity2012,
    };

    private const string Prices = """
        [Pipe prices]
        Size	Price
        12	45
        15	55
        18	65
        21	75
        24	95
        30	125
        36	160
        """;

    private static ToolResult Run(string input, StormNetwork n) => new NetworkOptimizerTool().Run(input, new DesignToolContext(n.Units, n, StormNetworkSolver.Solve(n)));
    private static IReadOnlyList<object?> Row(ToolResult r, int table, string first) => r.Tables[table].Rows.Single(x => (string)x[0]! == first);

    /// <summary>Every rule on the optimized network, checked from the HGL solution and the geometry, not from the optimizer.</summary>
    private static void AssertMeetsRules(StormNetwork net, StormNetwork orig, double cover, double maxDepth, double drop, double minSize, bool sub = true, double wallPlus = 1.0 / 12)
    {
        var r = StormNetworkSolver.Solve(net);
        Assert.True(r.Succeeded);
        var rim = net.Structures.ToDictionary(s => s.Name, s => s.RimElevation!.Value);
        var outfalls = net.Structures.Where(s => !net.Pipes.Any(p => p.From == s.Name)).Select(s => s.Name).ToHashSet();
        foreach (var h in r.Pipes)
        {
            var p = h.Pipe;
            double wall = p.Rise / 12 + wallPlus;
            Assert.True(p.Rise >= minSize - 1e-9, $"{p.Name} below the minimum size");
            Assert.True(p.UpstreamInvert > p.DownstreamInvert, $"{p.Name} not sloping down");
            Assert.True(h.FullFlowCapacity >= h.Pipe.Discharge - 1e-6, $"{p.Name}: Q {h.Pipe.Discharge} over Qfull {h.FullFlowCapacity}");
            Assert.True(h.NormalVelocity <= 20 + 1e-6, $"{p.Name}: V over 20");
            if (!double.IsNaN(h.MinorVelocity)) Assert.True(h.MinorVelocity >= 2.5 - 1e-3, $"{p.Name}: minor V {h.MinorVelocity} under 2.5");
            if (sub) Assert.True(h.NormalDepth >= h.CriticalDepth - 1e-3, $"{p.Name}: steeper than critical (yn {h.NormalDepth}, yc {h.CriticalDepth})");
            foreach (var (s, inv) in new[] { (p.From, p.UpstreamInvert), (p.To, p.DownstreamInvert) })
            {
                if (outfalls.Contains(s)) continue;
                Assert.True(rim[s] - (inv + p.Rise + wall) >= cover - 1e-6, $"{p.Name} at {s}: cover {rim[s] - (inv + p.Rise + wall)}");
                Assert.True(rim[s] - inv <= maxDepth + 1e-6, $"{p.Name} at {s}: too deep");
            }
        }
        foreach (var s in net.Structures)
        {
            var outP = net.Pipes.FirstOrDefault(p => p.From == s.Name);
            if (outP is null) continue;
            foreach (var i in net.Pipes.Where(p => p.To == s.Name))
            {
                Assert.True(i.Rise <= outP.Rise + 1e-9, $"{s.Name}: {i.Name} larger than the outlet");
                Assert.True(i.DownstreamInvert >= outP.UpstreamInvert + drop - 1e-6, $"{s.Name}: drop from {i.Name}");
                Assert.True(i.DownstreamInvert + i.Rise >= outP.UpstreamInvert + outP.Rise - 1e-6, $"{s.Name}: crown of {i.Name} below the outlet crown");
            }
        }
        // Flows, lengths and names are untouched.
        Assert.Equal(orig.Pipes.Select(p => (p.Name, p.From, p.To, p.Length, p.ManningN)), net.Pipes.Select(p => (p.Name, p.From, p.To, p.Length, p.ManningN)));
    }

    [Fact]
    public void CapacitySlope_MatchesManning()
    {
        // 24 in., n 0.013, Q 20 cfs: S = (Q n / (1.486 A R^(2/3)))², A = π, R = 0.5 ft.
        var n = Chain();
        var opt = new NetworkOptimizer(n, StormNetworkSolver.Solve(n), new OptimizerOptions { Sizes = [(2.0, 1)] });
        var w = opt.Window(n.Pipes[1] with { Discharge = 20 }, 2.0);
        // Window reads Q from the solved network (9 cfs), so check that value: S = (9·0.013 / (1.486·π·0.5^(2/3)))².
        Assert.Equal(Math.Pow(9 * 0.013 / (1.486 * Math.PI * Math.Pow(0.5, 2.0 / 3.0)), 2), w.Min, 9);
        Assert.Equal("capacity", w.MinReason);
        Assert.Equal(1.0, w.Max, 9);   // no velocity or critical limit set
        // Half-full limit: Q ≤ 0.5 Qfull needs four times the slope.
        var opt2 = new NetworkOptimizer(n, StormNetworkSolver.Solve(n), new OptimizerOptions { Sizes = [(2.0, 1)], MaxFill = 0.5 });
        Assert.Equal(4 * w.Min, opt2.Window(n.Pipes[1], 2.0).Min, 9);
    }

    [Fact]
    public void CriticalAndVelocityLimits_BoundTheWindow()
    {
        var n = Chain();
        var solved = StormNetworkSolver.Solve(n);
        var opt = new NetworkOptimizer(n, solved, new OptimizerOptions { Sizes = [(1.5, 1)], Subcritical = true, MinVelocity = 2.5, MaxVelocity = 20 });
        var w = opt.Window(n.Pipes[1], 1.5);
        Assert.True(w.Feasible);
        // At the upper limit the normal depth equals critical depth for 9 cfs in 18 in.
        double yc = PondOutlet.CircularCriticalDepth(9, 1.5, 32.174);
        double th = 2 * Math.Acos(1 - 2 * yc / 1.5), a = 1.5 * 1.5 / 8 * (th - Math.Sin(th)), rh = a / (1.5 * th / 2);
        Assert.Equal(Math.Pow(9 * 0.013 / (1.486 * a * Math.Pow(rh, 2.0 / 3.0)), 2), w.Max, 9);
        Assert.Equal("critical slope", w.MaxReason);
        // At the lower limit the minor flow (3.6 cfs) moves at 2.5 ft/s, or the pipe is just full.
        double vAtMin = NetworkSizer.NormalVelocity(1.5, 0.013, w.Min, 3.6, solved.Constants);
        Assert.True(w.MinReason == "capacity" || Math.Abs(vAtMin - 2.5) < 1e-4, $"{w.MinReason} {vAtMin}");
    }

    [Fact]
    public void DynamicProgram_EqualsBruteForce_OnAChain()
    {
        // Coarse grid so every design can be enumerated. The cost is recomputed here from first principles.
        var n = Chain();
        const double step = 0.25, cover = 2.0, maxDepth = 7.0, drop = 0.1;
        var sizes = new (double, double)[] { (1.0, 45), (1.25, 55), (1.5, 65), (2.0, 95) };
        var o = new OptimizerOptions
        {
            Sizes = sizes, GridStep = step, MinCover = cover, MaxDepth = maxDepth, MinDrop = drop, SideClearance = 2, Bedding = 0.5, ExcavationPrice = 22,
            StructureBaseDepth = 1, BaseStructureDepth = 6, StructureDepthPrice = 350, MinSize = 1, MaxVelocity = 20, MinVelocity = 2.5, Subcritical = true,
            InvertLimits = new Dictionary<string, (double?, double?)> { ["OUT"] = (98.0, null) },
        };
        var solved = StormNetworkSolver.Solve(n);
        var opt = new NetworkOptimizer(n, solved, o);
        var res = opt.Optimize();
        Assert.True(res.Feasible);

        double Wall(double d) => d / 12 + 1.0 / 12;
        double Exc(double d, double l, double rim, double inv) => 22 * (d + 2 * Wall(d) + 2) * l / 2 * (rim - (inv - Wall(d) - 0.5)) / 27;
        double Struct(double rim, double inv) => 350 * Math.Max(0, rim - inv + 1 - 6);
        var windows = new Dictionary<(string, double), SlopeWindow>();
        foreach (var p in n.Pipes) foreach (var (d, _) in sizes) windows[(p.Name, d)] = opt.Window(p, d);
        bool SlopeOk(NetworkPipe p, double d, double up, double dn)
        {
            var w = windows[(p.Name, d)]; double s = (up - dn) / p.Length;
            return s > 0 && s >= w.Min - 1e-9 && s <= w.Max + 1e-9;
        }
        IEnumerable<double> Levels(double lo, double hi) { for (double k = Math.Ceiling(lo / step - 1e-9); k <= Math.Floor(hi / step + 1e-9); k++) yield return k * step; }
        double best = double.PositiveInfinity;
        var p1 = n.Pipes[0]; var p2 = n.Pipes[1];
        foreach (var (d2, pr2) in sizes)
        foreach (var (d1, pr1) in sizes.Where(x => x.Item1 <= d2))
        foreach (var bUp in Levels(106.4 - maxDepth, 106.4 - cover - d2 - Wall(d2)))           // P2 upstream (outlet of B)
        foreach (var outDn in Levels(98.0, 103.0))                                              // P2 downstream at the outfall
        {
            if (!SlopeOk(p2, d2, bUp, outDn)) continue;
            double c2 = pr2 * p2.Length + Exc(d2, p2.Length, 106.4, bUp) + Exc(d2, p2.Length, 103.0, outDn) + Struct(106.4, bUp);
            if (c2 >= best) continue;
            foreach (var bIn in Levels(Math.Max(106.4 - maxDepth, bUp + Math.Max(drop, d2 - d1)), 106.4 - cover - d1 - Wall(d1)))   // P1 downstream at B
            foreach (var aUp in Levels(108.0 - maxDepth, 108.0 - cover - d1 - Wall(d1)))
            {
                if (!SlopeOk(p1, d1, aUp, bIn)) continue;
                double c = c2 + pr1 * p1.Length + Exc(d1, p1.Length, 108.0, aUp) + Exc(d1, p1.Length, 106.4, bIn) + Struct(108.0, aUp);
                if (c < best) best = c;
            }
        }
        Assert.True(double.IsFinite(best));
        Assert.Equal(best, res.Cost!.Total, 6);
        Assert.Empty(res.Cost.Violations);
        AssertMeetsRules(res.Network, n, cover, maxDepth, drop, 1.0);
    }

    [Fact]
    public void Wye_MeetsEveryRule_IncludingTheHgl()
    {
        var n = Wye();
        var r = Run("grid step = 0.05\nhgl check = yes\n" + Prices, n);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        AssertMeetsRules(r.UpdatedNetwork!, n, 2.0, 20, 0.1, 1.0);
        var cost = r.Tables[3];
        Assert.Equal("Total", cost.Rows[3][0]);
        Assert.Equal(0, (int)cost.Rows[4][2]!);            // no rules broken
        // HGL at least 1 ft below every rim.
        var s = StormNetworkSolver.Solve(r.UpdatedNetwork!);
        Assert.All(s.Structures.Where(x => !x.IsOutfall), x => Assert.True(x.HglClearance >= 1 - 1e-6, $"{x.Structure.Name} {x.HglClearance}"));
        Assert.Contains("pipe(s)", r.UpdateDescription!);
        Assert.NotEmpty(r.Charts);
    }

    [Fact]
    public void LeastExcavation_DigsNoMoreThanLeastCost()
    {
        var n = Wye();
        var o = new OptimizerOptions
        {
            Sizes = [(1.0, 45), (1.25, 55), (1.5, 65), (1.75, 75), (2.0, 95), (2.5, 125)], GridStep = 0.05, ExcavationPrice = 22, StructureDepthPrice = 350, MinSize = 1,
            MaxVelocity = 20, MinVelocity = 2.5, Subcritical = true,
        };
        var solved = StormNetworkSolver.Solve(n);
        var cost = new NetworkOptimizer(n, solved, o).Optimize();
        var depthOpts = o with { Sizes = o.Sizes.Select(x => (x.Size, 0.0)).ToList(), ExcavationPrice = 1, StructureDepthPrice = 0 };
        var depthOpt = new NetworkOptimizer(n, solved, depthOpts);
        var depth = depthOpt.Optimize();
        Assert.True(cost.Feasible && depth.Feasible);
        // Excavation (CY) of each design, measured the same way.
        double cyDepth = depth.Cost!.Excavation, cyCost = depthOpt.Evaluate(cost.Network.Pipes).Excavation;
        Assert.True(cyDepth <= cyCost + 1e-6, $"{cyDepth} vs {cyCost}");
        // The tool runs without a price table for the depth objective.
        var r = Run("objective = depth\nhgl check = no", n);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains("Least excavation", r.Summary[0]);
    }

    [Fact]
    public void InvertLimits_AreRespected()
    {
        var n = Wye();
        var r = Run("hgl check = no\n" + Prices + "\n[Invert limits]\nStructure\tMinimum invert\tMaximum invert\nMH-1\t\t103.0\nOUT\t100.25\t\n", n);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var u = r.UpdatedNetwork!;
        Assert.All(u.Pipes.Where(p => p.To == "MH-1"), p => Assert.True(p.DownstreamInvert <= 103.0 + 1e-9));
        Assert.True(u.Pipes.Single(p => p.From == "MH-1").UpstreamInvert <= 103.0 + 1e-9);
        Assert.True(u.Pipes.Single(p => p.To == "OUT").DownstreamInvert >= 100.25 - 1e-9);
        // An unknown structure is reported and ignored.
        var r2 = Run("hgl check = no\n" + Prices + "\n[Invert limits]\nStructure\tMinimum invert\tMaximum invert\nNOPE\t1\t2\n", n);
        Assert.Contains(r2.Messages, m => m.Code == "W-NO-001");
    }

    [Fact]
    public void OutfallInvert_DefaultsToTheExisting()
    {
        var n = Wye();
        var r = Run("hgl check = no\n" + Prices, n);
        Assert.True(r.Succeeded);
        Assert.True(r.UpdatedNetwork!.Pipes.Single(p => p.To == "OUT").DownstreamInvert >= 101.5 - 1e-9);
    }

    [Fact]
    public void HglLoop_ClearsTheRimOnTheMainStreetNetwork()
    {
        // Main St has a 102.5 tailwater and a low inlet (CB-4 rim 104): the cost optimum alone leaves the HGL too high.
        var n = NetworkOutputTests.NetA();
        string input = Prices + "\n[Invert limits]\nStructure\tMinimum invert\tMaximum invert\nOUT\t97\t\n";
        var off = Run("hgl check = no\n" + input, n);
        Assert.True(off.Succeeded);
        Assert.Contains(off.Tables[2].Rows, x => (string)x[9]! == "FAILS");
        var on = Run("hgl check = yes\n" + input, n);
        Assert.True(on.Succeeded);
        Assert.DoesNotContain(on.Tables[2].Rows, x => (string)x[9]! == "FAILS");
        Assert.Contains(on.Messages, m => m.Code == "I-NO-004");
        AssertMeetsRules(on.UpdatedNetwork!, n, 2.0, 20, 0.1, 1.0);
        Assert.True((double)on.Tables[3].Rows[3][2]! > (double)off.Tables[3].Rows[3][2]!);   // the HGL costs money
    }

    [Fact]
    public void SI_RunsWithMetricSizesAndCover()
    {
        static StormNetwork ToSi(StormNetwork n) => n with
        {
            Units = UnitSystem.SI,
            Structures = n.Structures.Select(s => s with { RimElevation = s.RimElevation * 0.3048 }).ToList(),
            Pipes = n.Pipes.Select(p => p with
            {
                Rise = p.Rise * 0.3048, Span = p.Span * 0.3048, Length = p.Length * 0.3048, UpstreamInvert = p.UpstreamInvert * 0.3048, DownstreamInvert = p.DownstreamInvert * 0.3048,
                Discharge = p.Discharge * 0.0283168, MinorDischarge = p.MinorDischarge * 0.0283168,
            }).ToList(),
            Criteria = n.Criteria with { HglBelowRim = 0.3, MaxVelocity = 6, MinVelocity = 0.75, MinPipeRise = 0.3 },
        };
        var n = ToSi(Wye());
        var tool = new NetworkOptimizerTool();
        var ctx = new DesignToolContext(UnitSystem.SI, n, StormNetworkSolver.Solve(n));
        var r = tool.Run(tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var metric = new[] { 0.3, 0.375, 0.45, 0.525, 0.6, 0.75, 0.9 };
        Assert.All(r.UpdatedNetwork!.Pipes, p => Assert.Contains(metric, d => Math.Abs(d - p.Rise) < 1e-9));
        foreach (var p in r.UpdatedNetwork.Pipes)
        {
            double wall = p.Rise / 12 + 0.025;
            var rim = r.UpdatedNetwork.Structures.Single(s => s.Name == p.From).RimElevation!.Value;
            Assert.True(rim - (p.UpstreamInvert + p.Rise + wall) >= 0.6 - 1e-6);
            Assert.Equal(0, Math.Round(p.UpstreamInvert / 0.01) * 0.01 - p.UpstreamInvert, 6);   // on the 0.01 m grid
        }
        Assert.Contains("mm", r.Tables[0].Headers[3]);
    }

    [Theory]
    [InlineData("grid step = -0.05", "E-NO-005")]
    [InlineData("grid step = 0", "E-NO-005")]
    [InlineData("minimum cover = -1", "E-NO-005")]
    [InlineData("maximum depth = 1.5", "E-NO-005")]           // not below the 2 ft cover
    [InlineData("maximum fill = 1.2", "E-NO-005")]
    [InlineData("minimum velocity = 25", "E-NO-005")]         // above the 20 ft/s maximum
    [InlineData("objective = cheapest", "E-NO-005")]
    [InlineData("grid step = 0.001", "E-NO-007")]
    [InlineData("grid step = abc", "E-TL-002")]
    [InlineData("maximum depth = 4", "E-NO-010")]             // too shallow to carry the flows: infeasible
    public void BadInput_IsReported_AndNothingIsReturned(string line, string code)
    {
        var r = Run(line + "\n" + Prices, Wye());
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
        Assert.Null(r.UpdatedNetwork);
        Assert.Empty(r.Tables);
    }

    [Fact]
    public void BadPricesAndNetworks_AreReported()
    {
        Assert.Contains(Run("objective = cost", Wye()).Messages, m => m.Code == "E-NO-006");                  // no prices
        Assert.Contains(Run("[Pipe prices]\nSize\tPrice\n12\t-5\n", Wye()).Messages, m => m.Code == "E-NO-005");
        Assert.Contains(Run("[Pipe prices]\nSize\tPrice\n-12\t5\n", Wye()).Messages, m => m.Code == "E-NO-005");
        var ell = Wye() with { Pipes = [.. Wye().Pipes.Take(2), Wye().Pipes[2] with { Shape = NetworkPipeShape.Elliptical, Span = 2.5 }] };
        Assert.Contains(Run(Prices, ell).Messages, m => m.Code == "E-NO-002");
        var noRim = Wye() with { Structures = [.. Wye().Structures.Take(3), Wye().Structures[3] with { RimElevation = null }] };
        Assert.Contains(Run(Prices, noRim).Messages, m => m.Code == "E-NO-003");
        Assert.Contains(new NetworkOptimizerTool().Run(Prices, new DesignToolContext(UnitSystem.USCustomary)).Messages, m => m.Code == "E-NO-001");
        // A flow no listed size can carry is named.
        var big = Wye() with { Pipes = [.. Wye().Pipes.Take(2), Wye().Pipes[2] with { Discharge = 80 }] };
        var huge = Run("hgl check = no\n[Pipe prices]\nSize\tPrice\n12\t45\n15\t55\n", big);
        Assert.Contains(huge.Messages, m => m.Code == "E-NO-010" && m.Text.Contains("no size in the list"));
    }

    [Fact]
    public void Sample_RunsOnTheMainStreetNetwork()
    {
        var n = NetworkOutputTests.NetA();
        var tool = new NetworkOptimizerTool();
        var ctx = new DesignToolContext(n.Units, n, StormNetworkSolver.Solve(n));
        string sample = tool.SampleInput(ctx);
        Assert.Contains("OUT\t97", sample);
        var r = tool.Run(sample, ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        AssertMeetsRules(r.UpdatedNetwork!, n, 2.0, 20, 0.1, 1.0);
    }
}
