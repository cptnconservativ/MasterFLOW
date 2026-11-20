using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Optimization;

/// <summary>Settings for <see cref="NetworkOptimizer"/>. Lengths and elevations in the network's units (ft or m); sizes in ft or m.</summary>
public sealed record OptimizerOptions
{
    /// <summary>Candidate circular sizes (ft or m) with price per unit length (per LF or per m). Price 0 for every size with <see cref="ExcavationPrice"/> = 1 gives least excavation.</summary>
    public IReadOnlyList<(double Size, double Price)> Sizes { get; init; } = [];
    /// <summary>Invert grid step (ft or m). Every invert is a multiple of it.</summary>
    public double GridStep { get; init; } = 0.05;
    /// <summary>Cover from the rim to the outside top of the pipe, at both ends of every pipe except at an outfall.</summary>
    public double MinCover { get; init; } = 2.0;
    /// <summary>Rim to invert, at every pipe end except at an outfall.</summary>
    public double MaxDepth { get; init; } = 20;
    /// <summary>Smallest drop across a structure, outlet invert below every inflow invert.</summary>
    public double MinDrop { get; init; } = 0.1;
    /// <summary>Pipe wall thickness; null = D/12 + 1 in. (U.S.) or D/12 + 25 mm (SI), the RCP Wall B approximation the quantity takeoff uses.</summary>
    public double? Wall { get; init; }
    /// <summary>Trench width = outside diameter + this (both sides together).</summary>
    public double SideClearance { get; init; } = 2.0;
    public double Bedding { get; init; } = 0.5;
    /// <summary>Price per CY (U.S.) or per m³ (SI) of trench excavation.</summary>
    public double ExcavationPrice { get; init; }
    /// <summary>Structure depth = rim − outlet invert + this.</summary>
    public double StructureBaseDepth { get; init; } = 1.0;
    /// <summary>Depth included in the structure price; deeper structures pay <see cref="StructureDepthPrice"/> per VF (or m) over it.</summary>
    public double BaseStructureDepth { get; init; } = 6;
    public double StructurePrice { get; init; }
    public double StructureDepthPrice { get; init; }
    public double MinSize { get; init; }
    public double? MaxVelocity { get; init; }
    public double? MinVelocity { get; init; }
    /// <summary>Slope not steeper than critical for the design Q (Rapid City IDCM §4.6.6 item 4).</summary>
    public bool Subcritical { get; init; }
    /// <summary>Q / Qfull at normal depth may not exceed this.</summary>
    public double MaxFill { get; init; } = 1.0;
    public double MinSlope { get; init; }
    public double? MaxSlope { get; init; }
    /// <summary>Lowest and highest invert allowed for every pipe end at a structure. At an outfall the lowest defaults to the existing lowest invert there.</summary>
    public IReadOnlyDictionary<string, (double? Min, double? Max)> InvertLimits { get; init; } = new Dictionary<string, (double?, double?)>();
    /// <summary>Extra cover required over the outlet pipe of a structure (used to lower a line whose HGL is too high).</summary>
    public IReadOnlyDictionary<string, double> ExtraCover { get; init; } = new Dictionary<string, double>();
    /// <summary>Smallest size allowed for a pipe (used to upsize a pipe whose HGL loss is too high).</summary>
    public IReadOnlyDictionary<string, double> MinPipeSize { get; init; } = new Dictionary<string, double>();
}

/// <summary>Cost of a design, split by kind, and every rule it breaks.</summary>
public sealed record DesignCost(double PipeMaterial, double Excavation, double StructureDepth, double Structures, IReadOnlyList<string> Violations)
{
    public double Total => PipeMaterial + Excavation + StructureDepth + Structures;
    public bool Feasible => Violations.Count == 0;
}

/// <summary>The window of slopes a pipe size may take, and what sets each end.</summary>
public sealed record SlopeWindow(double Size, double Min, double Max, string MinReason, string MaxReason)
{
    public bool Feasible => Min <= Max;
}

public sealed record OptimizerResult(bool Feasible, StormNetwork Network, DesignCost? Cost, IReadOnlyList<string> Notes);

/// <summary>
/// Least-cost (or least-excavation) sizes and inverts for a dendritic storm sewer, by dynamic programming over the
/// tree from the upstream ends to the outfall. The state at each structure is (outlet invert on a fixed grid, outlet
/// size); for each pipe, g(e₂, D) is the least cost of the pipe and everything above it with downstream invert e₂.
/// Every pipe meets: part-full capacity Q ≤ fill·Qfull, the velocity limits, the slope limits and (when asked)
/// subcritical slope; cover and depth at both ends; and at each structure the outlet is not smaller than any inflow
/// (IDCM §4.6.6(2)), crowns match or the inflow crown is higher (§4.6.6(3)), and every inflow invert is at least the
/// minimum drop above the outlet invert. The pipe cost (price per length plus trench excavation from a straight
/// ground line between rims) is separable in its two end inverts, so each pipe is solved with a sliding-window
/// minimum; the result is the exact optimum over the grid. The HGL is checked afterwards by the tool.
/// </summary>
public sealed class NetworkOptimizer
{
    private readonly StormNetwork _net;
    private readonly OptimizerOptions _o;
    private readonly Dictionary<string, NetworkStructure> _s;
    private readonly Dictionary<string, double> _q = [], _qMinor = [];
    private readonly HashSet<string> _outfalls;
    private readonly UnitConstants _c;
    private readonly double _volK;
    public IReadOnlyList<string> Order { get; }

    public NetworkOptimizer(StormNetwork net, StormNetworkResult solved, OptimizerOptions o)
    {
        _net = net; _o = o;
        _c = UnitConstants.For(net.Units);
        _volK = net.Units == UnitSystem.USCustomary ? 27 : 1;
        _s = net.Structures.ToDictionary(x => x.Name, StringComparer.Ordinal);
        foreach (var p in solved.Pipes) { _q[p.Pipe.Name] = p.Pipe.Discharge; _qMinor[p.Pipe.Name] = p.MinorDischarge; }
        _outfalls = net.Structures.Where(s => !net.Pipes.Any(p => p.From == s.Name)).Select(s => s.Name).ToHashSet();
        Order = NetworkSizer.UpstreamFirst(net.Pipes);
    }

    public double Q(string pipe) => _q.GetValueOrDefault(pipe, double.NaN);
    public double MinorQ(string pipe) => _qMinor.GetValueOrDefault(pipe, double.NaN);
    public bool IsOutfall(string structure) => _outfalls.Contains(structure);
    public double WallOf(double d) => _o.Wall ?? d / 12 + (_net.Units == UnitSystem.USCustomary ? 1.0 / 12 : 0.025);
    private double Rim(string s) => _s[s].RimElevation ?? double.NaN;

    /// <summary>Lowest invert allowed at a structure (its limit, or for an outfall the existing lowest invert there).</summary>
    public double? MinInvert(string s)
    {
        if (_o.InvertLimits.TryGetValue(s, out var l) && l.Min is { } m) return m;
        if (_outfalls.Contains(s)) return _net.Pipes.Where(p => p.To == s).Select(p => p.DownstreamInvert).DefaultIfEmpty(double.NaN).Min() is var e && double.IsFinite(e) ? e : null;
        return null;
    }
    public double? MaxInvert(string s) => _o.InvertLimits.TryGetValue(s, out var l) ? l.Max : null;

    // ---------------------------------------------------------------- hydraulics of one pipe

    private static double Area(double y, double d) { double th = 2 * Math.Acos(1 - 2 * y / d); return d * d / 8 * (th - Math.Sin(th)); }
    private static double Radius(double y, double d) { double th = 2 * Math.Acos(1 - 2 * y / d); return Area(y, d) / (d * th / 2); }

    /// <summary>The slopes pipe <paramref name="p"/> may take at size <paramref name="d"/>.</summary>
    public SlopeWindow Window(NetworkPipe p, double d)
    {
        double q = Q(p.Name), n = p.ManningN;
        double lo = Math.Max(_o.MinSlope, 0), hi = _o.MaxSlope ?? 1.0;
        string loWhy = _o.MinSlope > 0 ? "minimum slope" : "positive slope", hiWhy = _o.MaxSlope is null ? "100 %" : "maximum slope";
        if (q > 0)
        {
            // Q ≤ fill·Qfull  ⇒  S ≥ (Q n / (fill k A R^⅔))²
            double sCap = Math.Pow(q * n / (_o.MaxFill * _c.ManningK * Math.PI * d * d / 4 * Math.Pow(d / 4, 2.0 / 3.0)), 2);
            if (sCap > lo) { lo = sCap; loWhy = "capacity"; }
            double qv = MinorQ(p.Name) is var qm && qm > 0 ? qm : q;
            if (_o.MinVelocity is { } vmin && vmin > 0)
            {
                double s = SlopeForVelocity(d, n, qv, vmin, lo);
                if (s > lo) { lo = s; loWhy = "minimum velocity"; }
            }
            if (_o.MaxVelocity is { } vmax)
            {
                double s = SlopeForVelocity(d, n, q, vmax, lo);
                if (s < hi) { hi = s; hiWhy = "maximum velocity"; }
            }
            if (_o.Subcritical)
            {
                double yc = PondOutlet.CircularCriticalDepth(q, d, _c.Gravity);
                // Critical slope: the slope at which normal depth equals critical depth.
                double sc = yc >= d * 0.999 ? double.PositiveInfinity : Math.Pow(q * n / (_c.ManningK * Area(yc, d) * Math.Pow(Radius(yc, d), 2.0 / 3.0)), 2);
                if (sc < hi) { hi = sc; hiWhy = "critical slope"; }
            }
        }
        return new SlopeWindow(d, lo, hi, loWhy, hiWhy);
    }

    /// <summary>Slope at which the normal-depth velocity of <paramref name="q"/> is <paramref name="v"/> (searched at or above <paramref name="from"/>, which carries Q part full). +∞ when even 100 % is too flat; <paramref name="from"/> when it is already faster.</summary>
    private double SlopeForVelocity(double d, double n, double q, double v, double from)
    {
        double V(double s) => NetworkSizer.NormalVelocity(d, n, s, q, _c);
        double lo = Math.Max(from, 1e-9), hi = 1.0;
        if (V(lo) >= v) return lo;
        if (V(hi) < v) return double.PositiveInfinity;
        for (int i = 0; i < 100; i++) { double m = Math.Sqrt(lo * hi); if (V(m) < v) lo = m; else hi = m; }
        return hi;
    }

    // ---------------------------------------------------------------- cost of a design

    private double TrenchWidth(double d) => d + 2 * WallOf(d) + _o.SideClearance;
    private double PriceOf(double d) => _o.Sizes.FirstOrDefault(x => Math.Abs(x.Size - d) < 1e-6).Price;
    /// <summary>Excavation cost of one end of a pipe: half the trench (width × L/2 × depth at that end).</summary>
    private double EndExcavation(double d, double length, double rim, double invert) =>
        _o.ExcavationPrice * TrenchWidth(d) * length / 2 * (rim - (invert - WallOf(d) - _o.Bedding)) / _volK;
    private double StructureCost(string s, double outletInvert)
    {
        if (_outfalls.Contains(s)) return 0;
        double depth = Rim(s) - outletInvert + _o.StructureBaseDepth;
        return _o.StructureDepthPrice * Math.Max(0, depth - _o.BaseStructureDepth);
    }

    /// <summary>Cost of the given pipes (sizes and inverts) with these prices, and every rule they break. Used for the existing design and to check the optimizer.</summary>
    public DesignCost Evaluate(IReadOnlyList<NetworkPipe> pipes)
    {
        var v = new List<string>();
        double mat = 0, ex = 0, sd = 0, st = 0;
        const double tol = 1e-6;
        foreach (var p in pipes)
        {
            double d = p.Rise, w = WallOf(d);
            mat += PriceOf(d) * p.Length;
            ex += EndExcavation(d, p.Length, Rim(p.From), p.UpstreamInvert) + EndExcavation(d, p.Length, Rim(p.To), p.DownstreamInvert);
            if (!_o.Sizes.Any(x => Math.Abs(x.Size - d) < 1e-6)) v.Add($"{p.Name}: size {d:G4} is not in the size list");
            if (d < Math.Max(_o.MinSize, _o.MinPipeSize.GetValueOrDefault(p.Name)) - tol) v.Add($"{p.Name}: below the minimum size");
            double s = (p.UpstreamInvert - p.DownstreamInvert) / p.Length;
            var win = Window(p, d);
            if (s < win.Min - tol || !(s > 0)) v.Add($"{p.Name}: slope {100 * s:F3} % below {win.MinReason} ({100 * win.Min:F3} %)");
            if (s > win.Max + tol) v.Add($"{p.Name}: slope {100 * s:F3} % above {win.MaxReason} ({100 * win.Max:F3} %)");
            foreach (var (at, inv, extra) in new[] { (p.From, p.UpstreamInvert, _o.ExtraCover.GetValueOrDefault(p.From)), (p.To, p.DownstreamInvert, 0.0) })
            {
                if (!_outfalls.Contains(at))
                {
                    double cover = Rim(at) - (inv + d + w);
                    if (cover < _o.MinCover + extra - tol) v.Add($"{p.Name} at {at}: cover {cover:F2} below {_o.MinCover + extra:F2}");
                    if (Rim(at) - inv > _o.MaxDepth + tol) v.Add($"{p.Name} at {at}: depth {Rim(at) - inv:F2} over {_o.MaxDepth:F2}");
                }
                if (MinInvert(at) is { } mn && inv < mn - tol) v.Add($"{p.Name} at {at}: invert below the limit {mn:F2}");
                if (MaxInvert(at) is { } mx && inv > mx + tol) v.Add($"{p.Name} at {at}: invert above the limit {mx:F2}");
            }
        }
        foreach (var s in _net.Structures)
        {
            var outP = pipes.FirstOrDefault(p => p.From == s.Name);
            if (outP is null) continue;
            st += _o.StructurePrice;
            sd += StructureCost(s.Name, outP.UpstreamInvert);
            foreach (var i in pipes.Where(p => p.To == s.Name))
            {
                if (i.Rise > outP.Rise + tol) v.Add($"{s.Name}: inflow {i.Name} larger than outlet {outP.Name}");
                if (i.DownstreamInvert < outP.UpstreamInvert + _o.MinDrop - tol) v.Add($"{s.Name}: drop from {i.Name} below {_o.MinDrop:G3}");
                if (i.DownstreamInvert + i.Rise < outP.UpstreamInvert + outP.Rise - tol) v.Add($"{s.Name}: crown of {i.Name} below the outlet crown");
            }
        }
        return new DesignCost(mat, ex, sd, st, v);
    }

    // ---------------------------------------------------------------- dynamic programming

    private sealed class Level(int lo, int hi)
    {
        public readonly int Lo = lo, Hi = hi;
        public int Count => Math.Max(0, Hi - Lo + 1);
    }

    /// <summary>Grid indices an invert at structure <paramref name="s"/> may take for a pipe of size d (k·step = elevation).</summary>
    private Level Range(string s, double d, bool outlet)
    {
        double step = _o.GridStep, rim = Rim(s);
        double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
        if (!_outfalls.Contains(s))
        {
            lo = rim - _o.MaxDepth;
            hi = rim - _o.MinCover - (outlet ? _o.ExtraCover.GetValueOrDefault(s) : 0) - d - WallOf(d);
        }
        else hi = rim;
        if (MinInvert(s) is { } mn) lo = Math.Max(lo, mn);
        if (MaxInvert(s) is { } mx) hi = Math.Min(hi, mx);
        if (double.IsNegativeInfinity(lo)) lo = hi - _o.MaxDepth;
        return new Level((int)Math.Ceiling(lo / step - 1e-9), (int)Math.Floor(hi / step + 1e-9));
    }

    private sealed class PipeTable
    {
        public required NetworkPipe Pipe;
        public required double[] Sizes;
        public required Level[] Ranges;       // downstream-invert range per size
        public required double[][] G;         // least cost by size, by downstream index
        public required int[][] ArgUp;       // chosen upstream index
        public required double[][] SuffixMin; // min over k' ≥ k of G
        public required int[][] SuffixArg;
    }

    /// <summary>Runs the optimization; an infeasible result names the first pipe (upstream first) where no design is possible.</summary>
    public OptimizerResult Optimize()
    {
        var notes = new List<string>();
        double step = _o.GridStep;
        var sizes = _o.Sizes.Select(x => x.Size).Where(x => x >= _o.MinSize - 1e-9).OrderBy(x => x).ToArray();
        var tables = new Dictionary<string, PipeTable>();
        var pipes = _net.Pipes.ToDictionary(p => p.Name);
        var inflow = _net.Structures.ToDictionary(s => s.Name, s => _net.Pipes.Where(p => p.To == s.Name).Select(p => p.Name).ToList());

        // Structure value h_s(k, D): least cost of everything above structure s with outlet size D at invert k.
        double[] H(string s, double d, Level r, out bool any)
        {
            var h = new double[r.Count];
            any = false;
            for (int j = 0; j < h.Length; j++)
            {
                int k = r.Lo + j;
                double sum = StructureCost(s, k * step);
                foreach (var i in inflow[s])
                {
                    double best = BestInflow(tables[i], k, d, out _, out _);
                    sum += best;
                    if (double.IsPositiveInfinity(sum)) break;
                }
                h[j] = sum;
                if (sum < double.PositiveInfinity) any = true;
            }
            return h;
        }

        foreach (var name in Order)
        {
            var p = pipes[name];
            double minD = Math.Max(_o.MinSize, _o.MinPipeSize.GetValueOrDefault(name));
            var ds = sizes.Where(d => d >= minD - 1e-9).ToArray();
            var t = new PipeTable
            {
                Pipe = p, Sizes = ds, Ranges = new Level[ds.Length], G = new double[ds.Length][], ArgUp = new int[ds.Length][],
                SuffixMin = new double[ds.Length][], SuffixArg = new int[ds.Length][],
            };
            bool anyWindow = false, anyFeasible = false;
            for (int a = 0; a < ds.Length; a++)
            {
                double d = ds[a];
                var down = Range(p.To, d, outlet: false);
                var up = Range(p.From, d, outlet: true);
                t.Ranges[a] = down;
                var g = t.G[a] = Enumerable.Repeat(double.PositiveInfinity, down.Count).ToArray();
                var arg = t.ArgUp[a] = Enumerable.Repeat(int.MinValue, down.Count).ToArray();
                var win = Window(p, d);
                if (win.Feasible && down.Count > 0 && up.Count > 0)
                {
                    anyWindow = true;
                    // Upstream index k1 ∈ [k2 + w0, k2 + w1]: the drop over the length within the slope window, at least one step.
                    int w0 = Math.Max(1, (int)Math.Ceiling(win.Min * p.Length / step - 1e-9));
                    double w1d = Math.Floor(win.Max * p.Length / step + 1e-9);
                    int w1 = w1d > int.MaxValue / 4 ? int.MaxValue / 4 : (int)w1d;
                    if (w0 <= w1)
                    {
                        var h = H(p.From, d, up, out bool hAny);
                        if (hAny)
                        {
                            // f(k1) = h(k1) + excavation at the upstream end, by upstream index.
                            var f = new double[up.Count];
                            for (int j = 0; j < f.Length; j++) f[j] = h[j] + EndExcavation(d, p.Length, Rim(p.From), (up.Lo + j) * step);
                            double fixedCost = PriceOf(d) * p.Length;
                            // Sliding-window minimum (monotone deque) as k2 rises.
                            var dq = new LinkedList<int>();
                            int next = up.Lo;   // next upstream index to push
                            for (int j2 = 0; j2 < down.Count; j2++)
                            {
                                int k2 = down.Lo + j2;
                                long loK = (long)k2 + w0, hiK = Math.Min((long)k2 + w1, up.Hi);
                                while (next <= hiK)
                                {
                                    if (next >= up.Lo)
                                    {
                                        double fv = f[next - up.Lo];
                                        while (dq.Count > 0 && f[dq.Last!.Value - up.Lo] >= fv) dq.RemoveLast();
                                        dq.AddLast(next);
                                    }
                                    next++;
                                }
                                while (dq.Count > 0 && dq.First!.Value < loK) dq.RemoveFirst();
                                if (dq.Count == 0) continue;
                                int k1 = dq.First!.Value;
                                double val = f[k1 - up.Lo];
                                if (double.IsPositiveInfinity(val)) continue;
                                g[j2] = fixedCost + val + EndExcavation(d, p.Length, Rim(p.To), k2 * step);
                                arg[j2] = k1;
                                anyFeasible = true;
                            }
                        }
                    }
                }
                var sm = t.SuffixMin[a] = new double[down.Count];
                var sa = t.SuffixArg[a] = new int[down.Count];
                double m = double.PositiveInfinity; int mi = int.MinValue;
                for (int j = down.Count - 1; j >= 0; j--)
                {
                    if (g[j] < m) { m = g[j]; mi = down.Lo + j; }
                    sm[j] = m; sa[j] = mi;
                }
            }
            tables[name] = t;
            if (!anyFeasible)
            {
                string why = !anyWindow
                    ? $"Pipe {name}: no size in the list (from {Fmt(minD)}) carries Q = {Q(name):G4} within the slope, velocity{(_o.Subcritical ? ", critical-slope" : "")} and cover/depth limits. Add larger sizes, relax the limits or split the flow."
                    : $"Pipe {name} ({p.From} → {p.To}): no invert pair meets cover, depth, drop and crown matching with everything upstream. Check the rims, the maximum depth ({_o.MaxDepth:G4}) and the invert limits.";
                return new OptimizerResult(false, _net, null, [why]);
            }
        }

        // Outfalls: every inflow independently, invert at or above the outfall limit (built into the range).
        var chosen = new Dictionary<string, (double D, int Up, int Down)>();
        foreach (var of in _outfalls)
            foreach (var i in inflow[of])
            {
                var t = tables[i];
                double best = double.PositiveInfinity; int ba = -1, bk = 0;
                for (int a = 0; a < t.Sizes.Length; a++)
                    if (t.SuffixMin[a].Length > 0 && t.SuffixMin[a][0] < best) { best = t.SuffixMin[a][0]; ba = a; bk = t.SuffixArg[a][0]; }
                if (ba < 0) return new OptimizerResult(false, _net, null, [$"Outfall {of}: no feasible design for pipe {i}."]);
                Backtrack(tables, inflow, chosen, i, ba, bk);
            }

        var outPipes = _net.Pipes.Select(p => chosen.TryGetValue(p.Name, out var c)
            ? p with { Shape = NetworkPipeShape.Circular, Rise = c.D, Span = c.D, UpstreamInvert = Math.Round(c.Up * step, 6), DownstreamInvert = Math.Round(c.Down * step, 6) }
            : p).ToList();
        var cost = Evaluate(outPipes);
        if (!cost.Feasible) notes.Add("Internal check: the optimized design breaks a rule; please report this. " + string.Join("; ", cost.Violations));
        return new OptimizerResult(cost.Feasible, _net with { Pipes = outPipes }, cost, notes);
    }

    /// <summary>Least cost of inflow pipe t into a structure whose outlet is size <paramref name="outD"/> at index <paramref name="k1"/>.</summary>
    private double BestInflow(PipeTable t, int k1, double outD, out int size, out int k2)
    {
        double best = double.PositiveInfinity; size = -1; k2 = 0;
        for (int a = 0; a < t.Sizes.Length && t.Sizes[a] <= outD + 1e-9; a++)
        {
            double need = Math.Max(_o.MinDrop, outD - t.Sizes[a]);   // drop, and inflow crown not below the outlet crown
            int kMin = k1 + (int)Math.Ceiling(need / _o.GridStep - 1e-9);
            var r = t.Ranges[a];
            int j = Math.Max(0, kMin - r.Lo);
            if (j >= r.Count) continue;
            if (t.SuffixMin[a][j] < best) { best = t.SuffixMin[a][j]; size = a; k2 = t.SuffixArg[a][j]; }
        }
        return best;
    }

    private void Backtrack(Dictionary<string, PipeTable> tables, Dictionary<string, List<string>> inflow, Dictionary<string, (double, int, int)> chosen, string pipe, int a, int k2)
    {
        var t = tables[pipe];
        int k1 = t.ArgUp[a][k2 - t.Ranges[a].Lo];
        chosen[pipe] = (t.Sizes[a], k1, k2);
        foreach (var i in inflow[t.Pipe.From])
        {
            BestInflow(tables[i], k1, t.Sizes[a], out int sa, out int sk);
            Backtrack(tables, inflow, chosen, i, sa, sk);
        }
    }

    private string Fmt(double d) => _net.Units == UnitSystem.USCustomary ? $"{d * 12:0.##} in." : $"{d * 1000:0} mm";
}
