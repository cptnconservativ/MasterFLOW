using MasterFlow.Domain;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.Application.Routing;

/// <summary>Options for a dynamic-wave run. Times in seconds, lengths and areas in the network's units.</summary>
public sealed record DynamicWaveOptions
{
    public double TimeStep { get; init; } = 2;
    public double Duration { get; init; } = 3600;
    public double ReportStep { get; init; } = 60;
    /// <summary>Plan area of each structure (SWMM's minimum node surface area is 12.566 ft²).</summary>
    public double NodeArea { get; init; } = 12.566;
    /// <summary>Apply K·V²/2g at the entrance of each pipe, K from the upstream structure (or the network default).</summary>
    public bool JunctionLosses { get; init; } = true;
    public int MaxTrials { get; init; } = 8;
    public double HeadTolerance { get; init; } = 0.005;
}

public sealed record DynamicNodeResult(string Name, double Invert, double? Rim, double MaxHead, double TimeOfMax, double FloodVolume, double FloodMinutes, double SurchargeMinutes, double MaxInflow);
public sealed record DynamicLinkResult(string Name, double MaxFlow, double TimeOfMax, double MaxVelocity, double MaxDepthRatio, double FullFlowMinutes, double CourantStep);
public sealed record DynamicWaveResult(
    IReadOnlyList<DynamicNodeResult> Nodes, IReadOnlyList<DynamicLinkResult> Links,
    IReadOnlyList<double> Times, IReadOnlyDictionary<string, double[]> NodeHeads, IReadOnlyDictionary<string, double[]> LinkFlows,
    double InflowVolume, double OutflowVolume, double FloodVolume, double InitialStorage, double FinalStorage, int NonConvergedSteps)
{
    /// <summary>Continuity error, % of inflow: (inflow − outflow − flooding − Δstorage)/inflow.</summary>
    public double ContinuityError => InflowVolume > 0 ? 100 * (InflowVolume - OutflowVolume - FloodVolume - (FinalStorage - InitialStorage)) / InflowVolume : 0;
}

/// <summary>
/// Dynamic-wave routing through a storm sewer network by the link-node method of EPA SWMM 5 (Rossman 2017, Hydraulics
/// Reference Manual Ch. 3): the St. Venant momentum equation in each pipe, continuity at each structure, Picard
/// iterations with under-relaxation in every time step, and the SWMM surcharge algorithm at full structures.
/// </summary>
public sealed class DynamicWave
{
    private const int TableN = 200;

    private sealed class Link
    {
        public required NetworkPipe Pipe;
        public int Up, Dn;
        public double D, Afull, Rfull, L, S0, Z1, Z2, N, K;
        public double[] A = new double[TableN + 1], R = new double[TableN + 1], T = new double[TableN + 1];
        public double Q, QOld, AOld, AMid, DqDh, Y1, Y2;
        public double MaxQ, TMaxQ, MaxV, MaxRatio, FullTime, Courant;
        public double Area(double y) => Lookup(A, y);
        public double Hyd(double y) => Lookup(R, y);
        public double Top(double y) => y >= D ? 0 : Lookup(T, y);
        private double Lookup(double[] t, double y)
        {
            if (y <= 0) return 0;
            if (y >= D) return t[TableN];
            double f = y / D * TableN; int i = (int)f; double w = f - i;
            return t[i] * (1 - w) + t[Math.Min(i + 1, TableN)] * w;
        }
    }

    private sealed class Node
    {
        public required string Name;
        public double Invert, Crown, H, HOld, Lat, LatOld, NetOld, AreaUsed;
        public double? Rim, FixedHead;
        public bool Outfall;
        public double MaxH, TMaxH, Flood, FloodTime, SurTime, MaxIn;
    }

    private readonly StormNetwork _net;
    private readonly UnitConstants _c;
    private readonly DynamicWaveOptions _o;
    private readonly Func<string, double, double> _inflow;
    private readonly List<Node> _nodes = [];
    private readonly List<Link> _links = [];

    /// <param name="inflow">Lateral inflow at a structure (by name) at time t (s), in the network's flow units.</param>
    public DynamicWave(StormNetwork net, Func<string, double, double> inflow, DynamicWaveOptions options)
    {
        _net = net; _c = UnitConstants.For(net.Units); _o = options; _inflow = inflow;
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in net.Structures) { idx[s.Name] = _nodes.Count; _nodes.Add(new Node { Name = s.Name, Rim = s.RimElevation, Invert = double.PositiveInfinity, Crown = double.NegativeInfinity }); }
        foreach (var p in net.Pipes)
        {
            foreach (var nm in new[] { p.From, p.To })
                if (!idx.ContainsKey(nm)) { idx[nm] = _nodes.Count; _nodes.Add(new Node { Name = nm, Invert = double.PositiveInfinity, Crown = double.NegativeInfinity }); }
            ChannelSection sec = p.Shape == NetworkPipeShape.Circular ? new CircularSection(p.Rise) : new EllipticalSection(p.Rise, p.Span);
            var g = SectionGeometry.From(sec);
            var l = new Link { Pipe = p, Up = idx[p.From], Dn = idx[p.To], D = g.MaxDepth, L = p.Length, Z1 = p.UpstreamInvert, Z2 = p.DownstreamInvert, N = p.ManningN };
            for (int i = 0; i <= TableN; i++)
            {
                double y = l.D * i / TableN;
                l.A[i] = i == 0 ? 0 : g.Area(y);
                l.R[i] = i == 0 ? 0 : g.Area(y) / g.WettedPerimeter(y);
                l.T[i] = i == TableN ? 0 : g.TopWidth(y);
            }
            l.T[TableN] = 0;
            l.Afull = l.A[TableN]; l.Rfull = l.R[TableN];
            l.S0 = (p.UpstreamInvert - p.DownstreamInvert) / p.Length;
            var up = net.Structures.FirstOrDefault(s => string.Equals(s.Name, p.From, StringComparison.OrdinalIgnoreCase));
            l.K = options.JunctionLosses ? up?.JunctionK ?? net.DefaultJunctionK : 0;
            _links.Add(l);
            var a = _nodes[l.Up]; var b = _nodes[l.Dn];
            a.Invert = Math.Min(a.Invert, l.Z1); a.Crown = Math.Max(a.Crown, l.Z1 + l.D);
            b.Invert = Math.Min(b.Invert, l.Z2); b.Crown = Math.Max(b.Crown, l.Z2 + l.D);
        }
        foreach (var n in _nodes)
        {
            n.Outfall = !_links.Any(l => _nodes[l.Up] == n);
            if (n.Outfall && net.Tailwater == TailwaterRule.Elevation && net.TailwaterElevation is { } tw) n.FixedHead = tw;
            n.H = n.HOld = n.Invert;
        }
        // Static start: with a fixed tailwater above the outfall invert, the system starts filled level to it.
        if (_nodes.FirstOrDefault(n => n.FixedHead is not null)?.FixedHead is { } tw0)
            foreach (var n in _nodes) n.H = n.HOld = Math.Max(n.Invert, tw0);
        foreach (var n in _nodes) n.MaxH = n.H;
        foreach (var l in _links)
        {
            double y = Math.Clamp(0.5 * (_nodes[l.Up].H - l.Z1 + _nodes[l.Dn].H - l.Z2), 0, l.D);
            l.Y1 = Math.Clamp(_nodes[l.Up].H - l.Z1, 0, l.D); l.Y2 = Math.Clamp(_nodes[l.Dn].H - l.Z2, 0, l.D);
            l.AMid = l.Area(y);
        }
    }

    private double Gravity => _c.Gravity;

    /// <summary>Critical depth for Q in a link (bisection on Q²T = gA³).</summary>
    private double Critical(Link l, double q)
    {
        if (q <= 0) return 0;
        double lo = 1e-6 * l.D, hi = 0.999 * l.D;
        if (q * q * l.Top(hi) <= Gravity * Math.Pow(l.Area(hi), 3)) { }
        else return hi;
        for (int i = 0; i < 50; i++)
        {
            double y = 0.5 * (lo + hi), a = l.Area(y);
            if (q * q * l.Top(y) > Gravity * a * a * a) lo = y; else hi = y;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>Normal depth for Q (Manning), or the crown when Q exceeds the part-full capacity; NaN for flat or adverse pipes.</summary>
    private double Normal(Link l, double q)
    {
        if (l.S0 <= 0) return double.NaN;
        if (q <= 0) return 0;
        double Qy(double y) => _c.ManningK / l.N * l.Area(y) * Math.Pow(l.Hyd(y), 2.0 / 3) * Math.Sqrt(l.S0);
        double lo = 0, hi = 0.938 * l.D;     // Q peaks near 0.94 D in a circle
        if (Qy(hi) < q) return l.D;
        for (int i = 0; i < 50; i++) { double y = 0.5 * (lo + hi); if (Qy(y) < q) lo = y; else hi = y; }
        return 0.5 * (lo + hi);
    }

    private double OutfallHead(Node n, double t)
    {
        var l = _links.First(x => _nodes[x.Dn] == n);
        double q = Math.Abs(l.Q), yc = Critical(l, q), yn = Normal(l, q);
        double free = n.Invert + (double.IsNaN(yn) ? yc : Math.Min(yc, yn));
        return n.FixedHead is { } h ? Math.Max(h, free) : free;
    }

    public DynamicWaveResult Run()
    {
        double dt = _o.TimeStep, g = Gravity;
        int steps = (int)Math.Ceiling(_o.Duration / dt);
        var times = new List<double>();
        var heads = _nodes.ToDictionary(n => n.Name, _ => new List<double>());
        var flows = _links.ToDictionary(l => l.Pipe.Name, _ => new List<double>());
        double vin = 0, vout = 0, vflood = 0;
        double dStorage = 0;
        int nonConv = 0;
        foreach (var l in _links) { l.Courant = double.PositiveInfinity; l.AOld = l.AMid; }
        foreach (var n in _nodes) { n.Lat = _inflow(n.Name, 0); n.NetOld = n.Lat; }
        void Record(double t)
        {
            times.Add(t / 60);
            foreach (var n in _nodes) heads[n.Name].Add(n.H);
            foreach (var l in _links) flows[l.Pipe.Name].Add(l.Q);
        }
        Record(0);
        double nextReport = _o.ReportStep;
        for (int k = 1; k <= steps; k++)
        {
            double t = k * dt;
            foreach (var l in _links) { l.QOld = l.Q; l.AOld = l.AMid; }
            foreach (var n in _nodes) { n.HOld = n.H; n.LatOld = n.Lat; n.Lat = Math.Max(0, _inflow(n.Name, t)); }
            bool converged = false;
            for (int trial = 1; trial <= _o.MaxTrials && !converged; trial++)
            {
                double omega = trial == 1 ? 1.0 : 0.5;
                foreach (var l in _links) UpdateLink(l, dt, omega);
                converged = true;
                foreach (var n in _nodes)
                {
                    double hPrev = n.H;
                    if (n.Outfall) { n.H = OutfallHead(n, t); continue; }
                    double net = n.Lat, dqdh = 0, area = _o.NodeArea;
                    n.AreaUsed = _o.NodeArea;
                    foreach (var l in _links)
                    {
                        if (_nodes[l.Dn] == n) { net += l.Q; dqdh += l.DqDh; area += 0.5 * l.L * l.Top(l.Y2); }
                        else if (_nodes[l.Up] == n) { net -= l.Q; dqdh += l.DqDh; area += 0.5 * l.L * l.Top(l.Y1); }
                    }
                    double hNew;
                    bool surcharged = n.HOld > n.Crown && n.Crown > n.Invert;
                    if (surcharged && dqdh > 0)
                    {
                        // SWMM surcharge algorithm: adjust the head so that the net flow goes to zero (with the plan-area storage term).
                        double dh = (net - _o.NodeArea * (hPrev - n.HOld) / dt) / (dqdh + _o.NodeArea / dt);
                        hNew = hPrev + omega * dh;
                        if (hNew < n.Crown) hNew = n.Crown;     // drops back to the free-surface branch next step
                    }
                    else
                    {
                        double dv = 0.5 * (n.NetOld + net) * dt;
                        hNew = n.HOld + dv / area;
                        if (trial > 1) hNew = (1 - omega) * hPrev + omega * hNew;
                        n.AreaUsed = area;
                    }
                    hNew = Math.Max(hNew, n.Invert);
                    if (n.Rim is { } rim && hNew > rim) hNew = rim;
                    n.H = hNew;
                    if (Math.Abs(hNew - hPrev) > _o.HeadTolerance) converged = false;
                }
            }
            if (!converged) nonConv++;
            // Bookkeeping: volumes, flooding, maxima.
            foreach (var n in _nodes)
            {
                double net = n.Lat;
                foreach (var l in _links) { if (_nodes[l.Dn] == n) net += l.Q; else if (_nodes[l.Up] == n) net -= l.Q; }
                vin += 0.5 * (n.LatOld + n.Lat) * dt;
                if (n.Outfall) vout += _links.Where(l => _nodes[l.Dn] == n).Sum(l => 0.5 * (l.Q + l.QOld)) * dt;
                else if (n.Rim is { } rim && n.H >= rim - 1e-9)
                {
                    // Excess of inflow over what the node can hold at the rim is lost as flooding (SWMM default, no ponding).
                    double excess = Math.Max(0, 0.5 * (n.NetOld + net)) * dt;
                    double stored = (n.H - n.HOld) * _o.NodeArea;
                    double over = Math.Max(0, excess - Math.Max(0, stored));
                    n.Flood += over; vflood += over; n.FloodTime += dt;
                }
                n.NetOld = net;
                if (!n.Outfall) dStorage += n.AreaUsed * (n.H - n.HOld);
                if (n.H > n.MaxH + 1e-12) { n.MaxH = n.H; n.TMaxH = t; }
                if (n.H > n.Crown && !n.Outfall) n.SurTime += dt;
                double inflow = n.Lat + _links.Where(l => _nodes[l.Dn] == n).Sum(l => Math.Max(0, l.Q));
                n.MaxIn = Math.Max(n.MaxIn, inflow);
            }
            foreach (var l in _links)
            {
                if (Math.Abs(l.Q) > Math.Abs(l.MaxQ)) { l.MaxQ = l.Q; l.TMaxQ = t; }
                double v = l.AMid > 1e-9 ? Math.Abs(l.Q) / l.AMid : 0;
                l.MaxV = Math.Max(l.MaxV, v);
                double ratio = Math.Min(1, Math.Max(l.Y1, l.Y2) / l.D);
                l.MaxRatio = Math.Max(l.MaxRatio, ratio);
                if (l.Y1 >= l.D && l.Y2 >= l.D) l.FullTime += dt;
                double ymid = Math.Min(l.D, 0.5 * (l.Y1 + l.Y2));
                if (ymid > 0.01 * l.D && ymid < 0.99 * l.D)
                {
                    double c = v + Math.Sqrt(g * (ymid >= l.D ? l.D : l.AMid / Math.Max(l.Top(ymid), 1e-6)));
                    l.Courant = Math.Min(l.Courant, l.L / c);
                }
            }
            if (t >= nextReport - 1e-9) { Record(t); nextReport += _o.ReportStep; }
        }
        return new DynamicWaveResult(
            _nodes.Select(n => new DynamicNodeResult(n.Name, n.Invert, n.Rim, n.MaxH, n.TMaxH / 60, n.Flood, n.FloodTime / 60, n.SurTime / 60, n.MaxIn)).ToList(),
            _links.Select(l => new DynamicLinkResult(l.Pipe.Name, l.MaxQ, l.TMaxQ / 60, l.MaxV, l.MaxRatio, l.FullTime / 60, l.Courant)).ToList(),
            times, heads.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()), flows.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
            vin, vout, vflood, 0, dStorage, nonConv);
    }

    /// <summary>SWMM 5 dynwave link update (Hydraulics Reference Manual Eq. 3-13 and 3-14).</summary>
    private void UpdateLink(Link l, double dt, double omega)
    {
        double g = Gravity;
        var up = _nodes[l.Up]; var dn = _nodes[l.Dn];
        double h1 = Math.Max(up.H, l.Z1), h2 = Math.Max(dn.H, l.Z2);
        double y1 = Math.Min(h1 - l.Z1, l.D), y2 = Math.Min(h2 - l.Z2, l.D);
        // Free fall at the downstream end: the depth there is not below min(critical, normal).
        double qGuess = Math.Abs(l.Q);
        if (h2 - l.Z2 < l.D)
        {
            double yc = Critical(l, qGuess), yn = Normal(l, qGuess);
            double yf = double.IsNaN(yn) ? yc : Math.Min(yc, yn);
            if (y2 < yf) { y2 = yf; h2 = l.Z2 + yf; }
        }
        l.Y1 = Math.Max(h1 - l.Z1, 0); l.Y2 = Math.Max(h2 - l.Z2, 0);
        double ymid = 0.5 * (y1 + y2);
        double aMid = l.Y1 >= l.D && l.Y2 >= l.D ? l.Afull : l.Area(ymid);
        double rMid = l.Y1 >= l.D && l.Y2 >= l.D ? l.Rfull : l.Hyd(ymid);
        double a1 = l.Area(y1), a2 = l.Area(y2);
        if (aMid < 1e-6 * l.Afull)
        {
            l.Q = 0; l.AMid = aMid; l.DqDh = 0;
            return;
        }
        double v = l.QOld / aMid;
        double fr = ymid >= l.D ? 0 : Math.Abs(v) / Math.Sqrt(g * aMid / Math.Max(l.Top(ymid), 1e-9));
        double sigma = fr >= 1 ? 0 : fr <= 0.5 ? 1 : 2 * (1 - fr);
        double kn = _c.ManningK / l.N;
        double dq1 = dt * g * Math.Abs(v) / (kn * kn * Math.Pow(rMid, 4.0 / 3));
        double dq2 = dt * g * aMid * (h2 - h1) / l.L;
        double dq3 = 2 * v * (aMid - l.AOld) * sigma;
        double dq4 = dt * v * v * (a2 - a1) / l.L * sigma;
        double v1 = a1 > 1e-9 ? Math.Abs(l.QOld) / a1 : 0;
        double dq5 = l.K * v1 * dt / (2 * l.L);
        double denom = 1 + dq1 + dq5;
        double q = (l.QOld - dq2 + dq3 + dq4) / denom;
        // Normal-flow limitation (SWMM "slope" option): a free-surface entrance on a water-surface slope steeper than the pipe.
        if (l.S0 > 0 && l.Y1 < l.D && (h1 - h2) / l.L > l.S0 && q > 0)
        {
            double qn = kn * a1 * Math.Pow(l.Hyd(y1), 2.0 / 3) * Math.Sqrt(l.S0);
            q = Math.Min(q, qn);
        }
        // No flow out of a dry upstream end.
        if (y1 <= 0 && q > 0) q = 0;
        if (y2 <= 0 && dn.H <= l.Z2 && q < 0) q = 0;
        l.Q = omega >= 1 ? q : (1 - omega) * l.Q + omega * q;
        l.AMid = aMid;
        l.DqDh = g * dt * aMid / l.L / denom;
    }
}
