using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Stormwater pump station design by FHWA HEC-24, <i>Highway Stormwater Pump Station Design</i> (2001): system curve
/// (static head + Hazen-Williams friction + minor losses), pump operating points for one to several identical pumps in
/// parallel on a common force main, mass-curve routing of the inflow hydrograph through the wet well and storage with
/// start/stop elevations, peak stage, starts per hour and cycle times, and the wet-well volume for the allowed cycle time.
/// </summary>
public sealed class PumpStationTool : IDesignTool
{
    public string Id => "pump-station";
    public string Name => "Stormwater pump stations (HEC-24): system curve, cycling and storage routing";
    public string Description => "FHWA HEC-24 (2001) pump station: system curve H = (discharge el − wet-well el) + Hazen-Williams friction + ΣK·V²/2g, operating points of identical pumps in parallel on one force main, mass-curve routing of the design inflow through the wet well and storage with each pump's start and stop elevations, peak stage and storage, cycles and the shortest cycle time per pump, and the wet-well volume V = 15·T·Q (ft³, T minutes, Q cfs) for the allowed cycle time. U.S. units.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Stormwater pump station, FHWA HEC-24 (2001). U.S. units: ft, cfs, minutes.
        discharge elevation = 3420.0    # outlet of the force main (or the HGL it discharges against)
        force main length = 400         # ft
        force main diameter = 2.0       # ft (common force main for all pumps)
        hazen C = 120
        minor K = 4.5                   # entrance, valves, bends and exit, on the force main velocity
        min cycle time = 10             # minutes (6 starts per hour)
        time step = 0.5                 # minutes

        # Pump curve (one pump): total dynamic head (ft) and flow (cfs), from the manufacturer.
        [Pump curve]
        Head (ft)	Q (cfs)
        40	0
        36	4
        32	6.5
        26	9
        18	11.5

        # Pumps (identical) in start order: start and stop elevations in the wet well.
        [Pumps]
        Pump	Start el	Stop el
        Lead	3392.0	3390.0
        Lag 1	3393.0	3390.5
        Lag 2	3394.0	3391.0

        # Wet well and storage: elevation (ft) and stored volume (ft³) above the lowest stop level.
        [Storage]
        Elevation	Volume (ft3)
        3389.0	0
        3390.0	900
        3392.0	2700
        3394.0	4500
        3396.0	9000
        3398.0	20000

        # Design inflow hydrograph (minutes, cfs): HEC-24 uses the 50-yr storm for interstates (check the 100-yr).
        [Inflow]
        Time (min)	Q (cfs)
        0	0
        10	6
        20	18
        30	26
        40	20
        60	10
        90	4
        120	1
        150	0
        """;

    /// <summary>Hazen-Williams friction loss (ft) for Q (cfs) in a pipe of length L and diameter D (ft): 4.73·L·Q^1.852/(C^1.852·D^4.87).</summary>
    public static double HazenWilliams(double q, double l, double d, double c) => 4.73 * l * Math.Pow(q, 1.852) / (Math.Pow(c, 1.852) * Math.Pow(d, 4.87));

    /// <summary>Linear interpolation in a table sorted by x, clamped at the ends.</summary>
    public static double Lerp(IReadOnlyList<(double X, double Y)> t, double x)
    {
        if (x <= t[0].X) return t[0].Y;
        for (int i = 1; i < t.Count; i++) if (x <= t[i].X) return t[i - 1].Y + (t[i].Y - t[i - 1].Y) * (x - t[i - 1].X) / (t[i].X - t[i - 1].X);
        return t[^1].Y;
    }

    /// <summary>Total flow of <paramref name="n"/> identical parallel pumps on a common force main with the wet well at <paramref name="ws"/>.</summary>
    public static double Operating(int n, double ws, double hd, double l, double d, double c, double k, IReadOnlyList<(double Q, double H)> curve)
    {
        if (n == 0) return 0;
        double area = Math.PI * d * d / 4;
        double Sys(double qt) => hd - ws + HazenWilliams(qt, l, d, c) + k * Math.Pow(qt / area, 2) / (2 * 32.2);
        double Pump(double qEach) => Lerp(curve, qEach);
        double qMax = curve[^1].Q;
        if (Pump(0) <= Sys(0)) return 0;   // shut-off head below the static head
        double lo = 0, hi = qMax;
        if (Pump(hi) >= Sys(n * hi)) return n * hi;   // runs off the end of the curve
        for (int i = 0; i < 80; i++) { double m = 0.5 * (lo + hi); if (Pump(m) > Sys(n * m)) lo = m; else hi = m; }
        return n * 0.5 * (lo + hi);
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Stormwater pump station (HEC-24)";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-PS-001", "The pump station tool uses U.S. units (ft, cfs, Hazen-Williams 4.73); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double hd = p.Number("discharge elevation", double.NaN), l = p.Number("force main length", double.NaN), d = p.Number("force main diameter", double.NaN);
        double c = p.Number("hazen C", 120), k = p.Number("minor K", 0), tMin = p.Number("min cycle time", 10), dt = p.Number("time step", 0.5);
        if (double.IsNaN(hd) || !(l > 0) || !(d > 0) || !(c > 0) || k < 0) msgs.Add(CalcMessage.Error("E-PS-002", "Give the discharge elevation, force main length and diameter (above zero), hazen C and minor K."));
        if (!(dt > 0) || !(tMin > 0)) msgs.Add(CalcMessage.Error("E-PS-003", "time step and min cycle time must be above zero."));
        List<(double X, double Y)> Table(string sec, string a, string b)
        {
            var t = new List<(double, double)>();
            foreach (var r in p.Rows(sec)) if (p.Cell(r, 0, a, sec) is { } x && p.Cell(r, 1, b, sec) is { } y) t.Add((x, y));
            return t;
        }
        var curveHQ = Table("Pump curve", "head", "Q");
        var curve = curveHQ.Select(t => (Q: t.Y, H: t.X)).OrderBy(t => t.Q).ToList();
        var storage = Table("Storage", "elevation", "volume").OrderBy(t => t.X).ToList();
        var inflow = Table("Inflow", "time", "Q").OrderBy(t => t.X).ToList();
        var pumps = new List<(string Name, double Start, double Stop)>();
        foreach (var r in p.Rows("Pumps"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Pump {pumps.Count + 1}";
            if (p.Cell(r, 1, "start", name) is not { } st || p.Cell(r, 2, "stop", name) is not { } sp) { msgs.Add(CalcMessage.Error("E-PS-004", $"{name}: give start and stop elevations.")); continue; }
            if (sp >= st) { msgs.Add(CalcMessage.Error("E-PS-005", $"{name}: the stop elevation must be below the start.")); continue; }
            pumps.Add((name, st, sp));
        }
        if (curve.Count < 2 || curve.Zip(curve.Skip(1)).Any(t => t.Second.H > t.First.H)) msgs.Add(CalcMessage.Error("E-PS-006", "[Pump curve] needs at least two points with head falling as flow rises."));
        if (storage.Count < 2 || storage.Zip(storage.Skip(1)).Any(t => t.Second.Y < t.First.Y)) msgs.Add(CalcMessage.Error("E-PS-007", "[Storage] needs at least two rows with volume rising with elevation."));
        if (inflow.Count < 2) msgs.Add(CalcMessage.Error("E-PS-008", "[Inflow] needs at least two rows (time, Q)."));
        if (pumps.Count == 0) msgs.Add(CalcMessage.Error("E-PS-009", "Give at least one pump under [Pumps]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var tables = new List<ToolTable>();
        var summary = new List<string>();
        // System and operating points at the stop and start levels.
        var op = new List<IReadOnlyList<object?>>();
        for (int n = 1; n <= pumps.Count; n++)
        {
            double wsLo = pumps.Take(n).Max(x => x.Stop), wsHi = pumps[n - 1].Start;
            double qLo = Operating(n, wsLo, hd, l, d, c, k, curve), qHi = Operating(n, wsHi, hd, l, d, c, k, curve);
            double v = qHi / (Math.PI * d * d / 4);
            op.Add([n, R(wsLo, 2), R(qLo, 2), R(wsHi, 2), R(qHi, 2), R(qHi / n, 2), R(hd - wsHi, 2), R(HazenWilliams(qHi, l, d, c) + k * v * v / 64.4, 2), R(v, 2)]);
            if (v < 2) msgs.Add(CalcMessage.Info("I-PS-001", $"{n} pump(s): force main velocity {v:F1} ft/s is below about 2 ft/s, where solids can settle (HEC-24 §9)."));
            if (v > 8) msgs.Add(CalcMessage.Warning("W-PS-001", $"{n} pump(s): force main velocity {v:F1} ft/s is high (HEC-24 suggests no more than about 8 ft/s for surge and wear)."));
        }
        var sys = Enumerable.Range(0, 11).Select(i => i * curve[^1].Q * pumps.Count / 10).Select(qq => (qq, hd - pumps[0].Stop + HazenWilliams(qq, l, d, c) + k * Math.Pow(qq / (Math.PI * d * d / 4), 2) / 64.4)).ToList();
        tables.Add(new("Operating points (common force main)", ["Pumps on", "Wet well at stop (ft)", "Q at stop (cfs)", "Wet well at start (ft)", "Q at start (cfs)", "Q per pump (cfs)", "Static head (ft)", "Friction + minor (ft)", "Force main V (ft/s)"], op,
            ["System curve: H = discharge elevation − wet well + 4.73·L·Q^1.852/(C^1.852·D^4.87) + K·V²/2g. Identical pumps in parallel share the head; each gives Q(H) from the pump curve."]));

        // Mass-curve routing.
        double tEnd = inflow[^1].X;
        double ws = storage[0].X, vol = 0;
        double Elev(double vv) => Lerp(storage.Select(s => (s.Y, s.X)).ToList(), vv);
        var on = new bool[pumps.Count];
        var starts = new List<double>[pumps.Count];
        for (int i = 0; i < pumps.Count; i++) starts[i] = [];
        double peakWs = ws, peakVol = 0, peakOut = 0, tPeakWs = 0, peakIn = inflow.Max(x => x.Y);
        var hydro = new List<(double T, double In, double Out, double Ws)>();
        bool overflow = false;
        double t = 0;
        while (t <= tEnd * 3 + dt)
        {
            for (int i = 0; i < pumps.Count; i++)
            {
                if (!on[i] && ws >= pumps[i].Start) { on[i] = true; starts[i].Add(t); }
                else if (on[i] && ws <= pumps[i].Stop) on[i] = false;
            }
            int nOn = on.Count(x => x);
            double qOut = Operating(nOn, ws, hd, l, d, c, k, curve);
            double qIn = t <= tEnd ? Lerp(inflow, t) : 0;
            vol = Math.Max(0, vol + (qIn - qOut) * dt * 60);
            if (vol > storage[^1].Y) { overflow = true; vol = storage[^1].Y; }
            ws = Elev(vol);
            hydro.Add((t, qIn, qOut, ws));
            if (ws > peakWs) { peakWs = ws; tPeakWs = t; }
            peakVol = Math.Max(peakVol, vol); peakOut = Math.Max(peakOut, qOut);
            t += dt;
            if (t > tEnd && nOn == 0 && vol <= Lerp(storage.Select(s => (s.X, s.Y)).ToList(), pumps.Min(x => x.Start)) && qIn == 0) break;
        }
        if (overflow) msgs.Add(CalcMessage.Warning("W-PS-002", "The storage table is full: the inflow exceeds the pumps plus storage. Add storage or pump capacity, or extend the table."));

        var cyc = new List<IReadOnlyList<object?>>();
        for (int i = 0; i < pumps.Count; i++)
        {
            var st = starts[i];
            double minCycle = st.Count > 1 ? st.Zip(st.Skip(1)).Min(x => x.Second - x.First) : double.NaN;
            double qPump = Operating(i + 1, pumps[i].Start, hd, l, d, c, k, curve) - Operating(i, pumps[i].Start, hd, l, d, c, k, curve);
            double vReq = 15 * tMin * qPump;
            double vHave = Lerp(storage.Select(s => (s.X, s.Y)).ToList(), pumps[i].Start) - Lerp(storage.Select(s => (s.X, s.Y)).ToList(), pumps[i].Stop);
            bool cycOk = double.IsNaN(minCycle) || minCycle >= tMin - 1e-9;
            cyc.Add([pumps[i].Name, pumps[i].Start, pumps[i].Stop, st.Count, double.IsNaN(minCycle) ? null : R(minCycle, 1), R(qPump, 2), R(vReq, 0), R(vHave, 0), vHave >= vReq - 1 ? "OK" : "SMALL", cycOk ? "OK" : "SHORT"]);
            if (vHave < vReq - 1) msgs.Add(CalcMessage.Warning("W-PS-003", $"{pumps[i].Name}: {vHave:F0} ft³ between start and stop is less than V = 15·T·Q = {vReq:F0} ft³ for a {tMin:G3}-min cycle: widen the start–stop band or enlarge the wet well."));
            if (!cycOk) msgs.Add(CalcMessage.Warning("W-PS-004", $"{pumps[i].Name}: shortest cycle in the run is {minCycle:F1} min, below {tMin:G3} min."));
        }
        tables.Add(new("Pump cycling", ["Pump", "Start el (ft)", "Stop el (ft)", "Starts in the run", "Shortest cycle (min)", "Added flow (cfs)", "V req. 15·T·Q (ft³)", "V start–stop (ft³)", "Volume", "Cycle"], cyc,
            [$"Minimum volume between start and stop for one pump: V = T·Q/4 (T in seconds), = 15·T·Q with T in minutes and Q in cfs; the worst case is inflow = Q/2. T = {tMin:G3} min."]));
        var hrows = hydro.Where((_, i) => i % Math.Max(1, (int)Math.Round(5 / dt)) == 0).Select(h => (IReadOnlyList<object?>)[R(h.T, 1), R(h.In, 2), R(h.Out, 2), R(h.Ws, 2)]).ToList();
        tables.Add(new("Routing (every 5 min)", ["Time (min)", "Inflow (cfs)", "Pumped (cfs)", "Wet well W.S. (ft)"], hrows));
        summary.Add($"Peak inflow {peakIn:F1} cfs, peak pumped {peakOut:F1} cfs; peak stage {peakWs:F2} ft at {tPeakWs:F0} min with {peakVol:F0} ft³ stored.");
        summary.Add($"{pumps.Count} pump(s): {string.Join(", ", cyc.Select(r => $"{r[0]} {r[3]} start(s)"))}.");
        var charts = new List<ToolChart>
        {
            new("Inflow and pumped outflow", "Time (min)", "Flow (cfs)", [new("Inflow", hydro.Select(h => (h.T, h.In)).ToList()), new("Pumped", hydro.Select(h => (h.T, h.Out)).ToList())]),
            new("Wet well water surface", "Time (min)", "Elevation (ft)", [new("W.S.", hydro.Select(h => (h.T, h.Ws)).ToList())], pumps.SelectMany(pp => new[] { new ChartLine($"{pp.Name} start", pp.Start), new ChartLine($"{pp.Name} stop", pp.Stop) }).ToList()),
            new("Pump and system curves", "Flow (cfs)", "Head (ft)", [new("Pump (1)", curve.Select(x => (x.Q, x.H)).ToList()), .. Enumerable.Range(2, Math.Max(0, pumps.Count - 1)).Select(nn => new ChartSeries($"Pumps ({nn})", curve.Select(x => (x.Q * nn, x.H)).ToList())), new("System (lead stop level)", sys, Dashed: true)]),
        };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
