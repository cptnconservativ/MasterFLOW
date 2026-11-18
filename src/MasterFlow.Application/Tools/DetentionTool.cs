using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>One outlet of a detention pond's outlet works.</summary>
public sealed record PondOutlet(string Name, string Type, double Invert, double Size, double Height, double Coefficient, double Length = 0, double N = 0.013, double OutletInvert = double.NaN)
{
    /// <summary>
    /// Discharge at water surface elevation <paramref name="wse"/>. Orifice (circular: Size = D; rectangular: Size = width,
    /// Height = height): full Q = C A √(2g h), h above the centroid; below the top, Q = (2/3) C √(2g) B h^1.5 with B = A / height
    /// (so the two meet near the crown). Weir: Q = C L h^1.5 (Size = crest length). Barrel (Type pipe) is not an inlet; see <see cref="BarrelCapacity"/>.
    /// </summary>
    public double Flow(double wse, double g)
    {
        double h = wse - Invert;
        if (!(h > 0)) return 0;
        switch (Type)
        {
            case "weir": return Coefficient * Size * Math.Pow(h, 1.5);
            case "orifice":
            {
                bool round = Height <= 0;
                double ht = round ? Size : Height, area = round ? Math.PI * Size * Size / 4 : Size * Height, b = area / ht;
                return h < ht ? 2.0 / 3.0 * Coefficient * Math.Sqrt(2 * g) * b * Math.Pow(h, 1.5)
                              : Coefficient * area * Math.Sqrt(2 * g * (h - ht / 2));
            }
            default: return 0;
        }
    }

    /// <summary>
    /// Outlet pipe (barrel) full-flow capacity, free outfall: Q = A √(2g H / (1 + Ke + Kf)), H = WSE − (outlet invert + D),
    /// Ke = Coefficient (entrance loss), Kf = 29 n² L / R^(4/3) (U.S.) or 19.6 n² L / R^(4/3) (SI), R = D/4. Infinite below the crown.
    /// </summary>
    public double BarrelCapacity(double wse, double g, bool us)
    {
        double outInv = double.IsNaN(OutletInvert) ? Invert : OutletInvert, head = wse - (outInv + Size);
        if (!(head > 0)) return double.PositiveInfinity;
        double a = Math.PI * Size * Size / 4, r = Size / 4, kf = (us ? 29.0 : 19.6) * N * N * Length / Math.Pow(r, 4.0 / 3.0);
        return a * Math.Sqrt(2 * g * head / (1 + Coefficient + kf));
    }
}

/// <summary>Stage–storage–discharge relation of a pond and level-pool (storage-indication) routing.</summary>
public sealed class Pond
{
    public IReadOnlyList<(double Elev, double Area)> Stages { get; }
    public IReadOnlyList<PondOutlet> Outlets { get; }
    private readonly double[] _vol;
    private readonly double _g;
    private readonly bool _us;
    public PondOutlet? Barrel => Outlets.FirstOrDefault(o => o.Type == "pipe");

    public Pond(IReadOnlyList<(double Elev, double Area)> stages, IReadOnlyList<PondOutlet> outlets, double g, bool us)
    {
        Stages = stages; Outlets = outlets; _g = g; _us = us;
        _vol = new double[stages.Count];
        for (int i = 1; i < stages.Count; i++)
        {
            double h = stages[i].Elev - stages[i - 1].Elev, a1 = stages[i - 1].Area, a2 = stages[i].Area;
            _vol[i] = _vol[i - 1] + h / 3 * (a1 + a2 + Math.Sqrt(a1 * a2));   // frustum (conic) method
        }
    }

    public double Bottom => Stages[0].Elev;
    public double Top => Stages[^1].Elev;
    public double MaxStorage => _vol[^1];

    /// <summary>Storage at an elevation (frustum between stages: area varies with the square of the linear dimension).</summary>
    public double Storage(double e)
    {
        if (e <= Bottom) return 0;
        for (int i = 1; i < Stages.Count; i++)
            if (e <= Stages[i].Elev + 1e-12)
            {
                double h = e - Stages[i - 1].Elev, H = Stages[i].Elev - Stages[i - 1].Elev;
                double s1 = Math.Sqrt(Stages[i - 1].Area), s2 = Math.Sqrt(Stages[i].Area), ae = Math.Pow(s1 + (s2 - s1) * h / H, 2);
                return _vol[i - 1] + h / 3 * (Stages[i - 1].Area + ae + Math.Sqrt(Stages[i - 1].Area * ae));
            }
        return double.NaN;
    }

    /// <summary>Total outflow: sum of orifices and weirs, limited by the barrel when there is one.</summary>
    public double Outflow(double e)
    {
        double q = Outlets.Sum(o => o.Flow(e, _g));
        return Barrel is { } b ? Math.Min(q, b.BarrelCapacity(e, _g, _us)) : q;
    }

    /// <summary>Elevation where 2S/dt + O equals <paramref name="target"/> (bisection; NaN above the top stage).</summary>
    public double SolveIndication(double target, double dt)
    {
        double lo = Bottom, hi = Top;
        if (2 * Storage(hi) / dt + Outflow(hi) < target) return double.NaN;
        for (int i = 0; i < 80; i++) { double m = 0.5 * (lo + hi); if (2 * Storage(m) / dt + Outflow(m) < target) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    public sealed record RouteResult(double PeakInflow, double PeakOutflow, double PeakWse, double PeakStorage, double TimeOfPeakOutflow, double DrainHours, bool Overtopped);

    /// <summary>Routes a hydrograph given per time step (cfs, dt in seconds) by the storage-indication method, then continues with no inflow to find the drain time.</summary>
    public RouteResult Route(IReadOnlyList<double> inflow, double dt, double drainLimitHours = 168, List<double>? outflows = null)
    {
        double e = Bottom, s = 0, o = 0, peakO = 0, peakE = Bottom, peakS = 0, tPeak = 0;
        int k = 0, maxSteps = inflow.Count + (int)(drainLimitHours * 3600 / dt);
        double endInflow = (inflow.Count - 1) * dt, drain = double.NaN;
        double i1 = inflow.Count > 0 ? inflow[0] : 0;
        for (k = 1; k < maxSteps; k++)
        {
            double i2 = k < inflow.Count ? inflow[k] : 0;
            double target = (i1 + i2) + (2 * s / dt - o);
            double e2 = SolveIndication(target, dt);
            if (double.IsNaN(e2)) return new RouteResult(inflow.Max(), peakO, double.NaN, double.NaN, tPeak, double.NaN, true);
            e = e2; s = Storage(e); o = Outflow(e); i1 = i2;
            outflows?.Add(o);
            if (o > peakO) { peakO = o; tPeak = k * dt; }
            if (e > peakE) { peakE = e; peakS = s; }
            if (k * dt > endInflow && s <= 0.001 * peakS + 1e-9) { drain = (k * dt - endInflow) / 3600; break; }
        }
        return new RouteResult(inflow.Count > 0 ? inflow.Max() : 0, peakO, peakE, peakS, tPeak, drain, false);
    }
}

public sealed class DetentionTool : IDesignTool
{
    public string Id => "detention-routing";
    public string Name => "Detention pond routing (IDCM §4.10)";
    public string Description => "Stage–storage (frustum method) and outlet rating (orifices, weirs, outlet pipe), modified rational inflow hydrographs from IDCM Table 4-2 for the 2-, 10- and 100-year storms over a range of durations, storage-indication routing, and the §4.10 checks: peak outflow not above pre-development, 1 ft freeboard at the 100-yr level, drain within 72 hours, 18 in. minimum outlet pipe.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Detention pond routing, U.S. units (ft, ft², cfs, ac, min).
        area = 12           # contributing area, ac
        C2 = 0.45
        C10 = 0.55
        C100 = 0.65
        tc = 15             # min
        # Allowed peak outflow (pre-development peaks), cfs
        allowable Q2 = 4
        allowable Q10 = 8
        allowable Q100 = 16
        top of berm = 106.5  # for the 1 ft freeboard check

        [Stage storage]
        Elevation (ft)	Area (ft²)
        100.0	6000
        101.0	9000
        102.0	12500
        103.0	16500
        104.0	21000
        105.0	26000
        106.5	34000

        # Type: orifice (Size = diameter, or width with Height for rectangular), weir (Size = crest length), pipe (outlet barrel:
        # Size = D, Length, n, outlet invert; C = entrance loss Ke). Orifices and weirs add; the pipe limits their total.
        [Outlets]
        Name	Type	Invert (ft)	Size (ft)	Height (ft)	C	Length (ft)	n	Outlet invert (ft)
        Low-flow orifice	orifice	100.0	0.75		0.6
        Mid orifice	orifice	102.2	1.0	0.5	0.6
        Riser crest	weir	104.0	6		3.0
        Outlet pipe	pipe	99.5	2.0		0.5	80	0.013	98.7
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Detention pond routing — IDCM 2012 §4.10";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-DT-001", "The modified rational inflow uses IDCM Table 4-2 (inches) and Q = CiA in cfs; switch the project to U.S. units."));
        const double g = 32.174;
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double area = p.Number("area", double.NaN), tc = p.Number("tc", double.NaN);
        double? berm = p.OptionalNumber("top of berm");
        if (!(area > 0) || !(tc >= 5)) msgs.Add(CalcMessage.Error("E-DT-002", "Area (ac) must be > 0 and tc at least 5 min."));
        var stages = new List<(double, double)>();
        foreach (var r in p.Rows("Stage storage"))
            if (p.Cell(r, 0, "elevation", "Stage row") is { } e && p.Cell(r, 1, "area", $"Stage {r[0]}") is { } a)
            {
                if (stages.Count > 0 && e <= stages[^1].Item1) { msgs.Add(CalcMessage.Error("E-DT-003", "Stage elevations must increase.")); break; }
                if (a < 0) { msgs.Add(CalcMessage.Error("E-DT-003", "Stage areas cannot be negative.")); break; }
                stages.Add((e, a));
            }
        if (stages.Count < 2) msgs.Add(CalcMessage.Error("E-DT-004", "At least two stage–area rows are needed under [Stage storage]."));
        var outlets = new List<PondOutlet>();
        foreach (var r in p.Rows("Outlets"))
        {
            string name = r[0], type = (r.Length > 1 ? r[1] : "").ToLowerInvariant(), where = $"Outlet {name}";
            double? inv = p.Cell(r, 2, "invert", where), size = p.Cell(r, 3, "size", where), ht = p.Cell(r, 4, "height", where), cc = p.Cell(r, 5, "C", where),
                    len = p.Cell(r, 6, "length", where), nn = p.Cell(r, 7, "n", where), oinv = p.Cell(r, 8, "outlet invert", where);
            if (type is not ("orifice" or "weir" or "pipe")) { msgs.Add(CalcMessage.Error("E-DT-005", $"{where}: type must be orifice, weir or pipe.")); continue; }
            if (inv is null || size is not > 0) { msgs.Add(CalcMessage.Error("E-DT-006", $"{where}: invert and size are required.")); continue; }
            if (type == "pipe" && (len is not > 0 || nn is not > 0)) { msgs.Add(CalcMessage.Error("E-DT-007", $"{where}: the outlet pipe needs length and n.")); continue; }
            double c = cc ?? (type switch { "weir" => 3.0, "orifice" => 0.6, _ => 0.5 });
            outlets.Add(new PondOutlet(name, type, inv.Value, size.Value, ht ?? 0, c, len ?? 0, nn ?? 0.013, oinv ?? double.NaN));
        }
        if (outlets.Count(o => o.Type != "pipe") == 0) msgs.Add(CalcMessage.Error("E-DT-008", "At least one orifice or weir is needed under [Outlets]."));
        if (outlets.Count(o => o.Type == "pipe") > 1) msgs.Add(CalcMessage.Error("E-DT-009", "Only one outlet pipe (barrel) is supported."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        var pond = new Pond(stages, outlets, g, true);
        // Stage–storage–discharge table at the stage rows and every 0.5 ft.
        var elevs = stages.Select(s => s.Item1).Concat(Enumerable.Range(0, (int)Math.Floor((pond.Top - pond.Bottom) / 0.5) + 1).Select(i => pond.Bottom + 0.5 * i)).Distinct().Order().ToList();
        var sTable = elevs.Select(e => (IReadOnlyList<object?>)[e, pond.Storage(e), pond.Storage(e) / 43560, pond.Outflow(e),
            .. outlets.Where(o => o.Type != "pipe").Select(o => (object?)o.Flow(e, g)), .. (pond.Barrel is { } b ? new object?[] { b.BarrelCapacity(e, g, true) } : [])]).ToList();
        var sHead = new List<string> { "Elevation (ft)", "Storage (ft³)", "Storage (ac-ft)", "Outflow (cfs)" };
        sHead.AddRange(outlets.Where(o => o.Type != "pipe").Select(o => $"{o.Name} (cfs)"));
        if (pond.Barrel is { } bb) sHead.Add($"{bb.Name} capacity (cfs)");

        // Modified rational hydrographs, durations tc to 180 min; the critical duration gives the highest water level.
        const double dt = 60;
        var rows = new List<IReadOnlyList<object?>>();
        double? wse100 = null;
        foreach (int storm in new[] { 2, 10, 100 })
        {
            double c = p.Number($"C{storm}", double.NaN);
            double? allow = p.OptionalNumber($"allowable Q{storm}");
            if (!(c > 0 && c <= 1)) { msgs.Add(CalcMessage.Error("E-DT-010", $"C{storm} must be between 0 and 1.")); continue; }
            Pond.RouteResult? best = null; double bestTd = 0, bestQ = 0; List<double>? bestHyd = null;
            var durations = new SortedSet<double> { tc };
            for (double d = 5 * Math.Ceiling(tc / 5); d <= 180; d += 5) durations.Add(d);
            foreach (double td in durations)
            {
                double q = c * RapidCityRational.Intensity(storm, td) * area;
                int steps = (int)Math.Round((td + tc) * 60 / dt);
                var hyd = Enumerable.Range(0, steps + 1).Select(k =>
                {
                    double t = k * dt / 60;
                    return t <= tc ? q * t / tc : t <= td ? q : Math.Max(0, q * (td + tc - t) / tc);
                }).ToList();
                var rr = pond.Route(hyd, dt, drainLimitHours: 2);   // enough to pass the peak; the drain time is found below
                if (rr.Overtopped) { best = rr; bestTd = td; bestQ = q; break; }
                if (best is null || rr.PeakWse > best.PeakWse) { best = rr; bestTd = td; bestQ = q; bestHyd = hyd; }
            }
            if (best is null) continue;
            if (!best.Overtopped && bestHyd is not null) best = pond.Route(bestHyd, dt);
            if (best.Overtopped)
            {
                msgs.Add(CalcMessage.Error("E-DT-011", $"{storm}-yr: the pond fills above the top stage ({pond.Top:F2}) for a {bestTd:F0}-min storm. Extend the stage–storage table or enlarge the outlets."));
                rows.Add([$"{storm}-yr", bestTd, bestQ, null, null, null, null, allow, "overtops", null]);
                continue;
            }
            string ok = allow is { } al ? (best.PeakOutflow <= al + 1e-9 ? "OK" : $"FAILS (> {al:G4})") : "no limit given";
            if (allow is { } al2 && best.PeakOutflow > al2 + 1e-9) msgs.Add(CalcMessage.Warning("W-DT-001", $"{storm}-yr peak outflow {best.PeakOutflow:F2} cfs exceeds the pre-development {al2:G4} cfs (§4.10.4)."));
            rows.Add([$"{storm}-yr", bestTd, bestQ, best.PeakOutflow, best.PeakWse, best.PeakStorage / 43560, best.DrainHours, allow, ok, best.TimeOfPeakOutflow / 60]);
            if (storm == 100)
            {
                wse100 = best.PeakWse;
                if (double.IsNaN(best.DrainHours)) msgs.Add(CalcMessage.Warning("W-DT-002", "The pond does not drain within 7 days after the 100-yr storm (is the lowest outlet above the pond bottom?). IDCM §4.10.5 requires drain in 72 h."));
                else if (best.DrainHours > 72) msgs.Add(CalcMessage.Warning("W-DT-003", $"Drain time after the 100-yr storm is {best.DrainHours:F1} h, more than the 72 h allowed (§4.10.5)."));
                msgs.Add(CalcMessage.Info("I-DT-001", $"Trickle channel: capacity at least 2 % of the 100-yr peak inflow = {0.02 * bestQ:F2} cfs, slope at least 0.2 % (§4.10.4(3))."));
            }
        }
        if (wse100 is { } w && berm is { } bm)
        {
            double fb = bm - w;
            if (fb < 1 - 1e-9) msgs.Add(CalcMessage.Warning("W-DT-004", $"Freeboard at the 100-yr level is {fb:F2} ft; at least 1 ft is required (§4.10.3(2))."));
            else msgs.Add(CalcMessage.Info("I-DT-002", $"Freeboard at the 100-yr level: {fb:F2} ft (≥ 1 ft, §4.10.3(2))."));
        }
        else if (berm is null) msgs.Add(CalcMessage.Info("I-DT-003", "Enter top of berm to check the 1 ft freeboard."));
        if (pond.Barrel is { } pipe && pipe.Size < 1.5 - 1e-9) msgs.Add(CalcMessage.Warning("W-DT-005", $"Outlet pipe {pipe.Size * 12:F0} in. is below the 18 in. minimum (§4.10.5)."));
        if (outlets.Where(o => o.Type != "pipe").Min(o => o.Invert) > pond.Bottom + 1e-9)
            msgs.Add(CalcMessage.Warning("W-DT-006", "The lowest orifice or weir is above the pond bottom: the storage below it does not drain by gravity."));

        var tables = new List<ToolTable>
        {
            new("Routing results (critical duration per storm)", ["Storm", "Critical duration (min)", "Peak inflow (cfs)", "Peak outflow (cfs)", "Peak WSE (ft)", "Storage used (ac-ft)", "Drain time (h)", "Allowed (cfs)", "Check", "Time of peak outflow (min)"], rows,
            [
                $"Modified rational inflow: Q = C·i(Td)·A for storm durations Td from tc ({tc:G4} min) to 180 min; rises over tc, holds to Td, falls over tc. i from IDCM Table 4-2.",
                "Storage-indication (level-pool) routing, 1-min steps. The critical duration is the one with the highest water level. Drain time is from the end of inflow to 0.1 % of the peak storage.",
            ]),
            new("Stage–storage–discharge", sHead, sTable, ["Storage by the frustum (conic) method between stages. Orifices below their crown flow as weirs: Q = (2/3) C √(2g) B h^1.5."]),
        };
        return new ToolResult(title, [$"Pond {pond.Bottom:F2} to {pond.Top:F2} ft, {pond.MaxStorage / 43560:F2} ac-ft at the top stage; {outlets.Count} outlet(s)."], tables, msgs);
    }
}
