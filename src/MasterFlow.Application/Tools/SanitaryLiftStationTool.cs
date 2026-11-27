using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Wastewater lift station and force main check: system head curves (Hazen-Williams, static head at the pump-off and
/// lead-on levels, high points), duty and firm-capacity operating points for identical parallel pumps, force main
/// velocity, wet well operating volume and pump cycling, detention times in the wet well and force main, brake
/// horsepower, and Joukowsky surge against the pipe pressure class. The items Rapid City IDCM §3.13.3 asks the design
/// report to contain, with Ten States Standards Ch. 40 limits.
/// </summary>
public sealed class SanitaryLiftStationTool : IDesignTool
{
    public string Id => "sanitary-lift-station";
    public string Name => "Sanitary lift station and force main";
    public string Description => "Wastewater lift station and force main (Rapid City IDCM §3.13.3, Ten States Standards): system head curves by Hazen-Williams at the pump-off and lead-on levels (and force main high points), duty and firm capacity of identical parallel pumps against the peak flow, force main velocity (2 ft/s minimum), wet well volume and cycle time, detention time in the wet well and force main, brake horsepower, and Joukowsky surge pressure against the pipe pressure class.";
    public bool UsesNetwork => false;

    private const double GpmPerCfs = 448.831, G = 32.174;
    public static readonly double[] MotorHp = [1, 1.5, 2, 3, 5, 7.5, 10, 15, 20, 25, 30, 40, 50, 60, 75, 100, 125, 150, 200, 250, 300];

    /// <summary>Hazen-Williams friction head (ft) for Q in gpm, L in ft, D in in.: 10.44·L·Q^1.852/(C^1.852·D^4.8655).</summary>
    public static double FrictionGpm(double qGpm, double l, double dIn, double c) => qGpm <= 0 ? 0 : 10.44 * l * Math.Pow(qGpm, 1.852) / (Math.Pow(c, 1.852) * Math.Pow(dIn, 4.8655));

    /// <summary>Total dynamic head (ft) for Q gpm: static + Hazen-Williams + K·V²/2g.</summary>
    public static double SystemHead(double qGpm, double stat, double l, double dIn, double c, double k)
    {
        double v = qGpm / GpmPerCfs / (Math.PI * Math.Pow(dIn / 12, 2) / 4);
        return stat + FrictionGpm(qGpm, l, dIn, c) + k * v * v / (2 * G);
    }

    /// <summary>Total flow (gpm) of <paramref name="n"/> identical pumps in parallel at static head <paramref name="stat"/>; 0 when the shut-off head is below the static head.</summary>
    public static double Operating(int n, double stat, double l, double dIn, double c, double k, IReadOnlyList<(double Q, double H)> curve)
    {
        if (n <= 0) return 0;
        double Pump(double q) => PumpStationTool.Lerp(curve, q);
        if (Pump(0) <= SystemHead(0, stat, l, dIn, c, k)) return 0;
        double lo = 0, hi = curve[^1].Q;
        if (Pump(hi) >= SystemHead(n * hi, stat, l, dIn, c, k)) return n * hi;
        for (int i = 0; i < 100; i++) { double m = 0.5 * (lo + hi); if (Pump(m) > SystemHead(n * m, stat, l, dIn, c, k)) lo = m; else hi = m; }
        return n * 0.5 * (lo + hi);
    }

    /// <summary>Cycle time (min) of one pump of capacity <paramref name="qp"/> between levels holding <paramref name="v"/> gal with inflow <paramref name="qin"/> (gpm): fill time + pump-down time.</summary>
    public static double CycleTime(double v, double qp, double qin) => qin <= 0 || qin >= qp ? double.PositiveInfinity : v / qin + v / (qp - qin);

    public string SampleInput(DesignToolContext context) => """
        # Sanitary lift station and force main. U.S. units: flows gpm, elevations and lengths ft, diameters in.
        average flow = 120          # gpm, design average (e.g. from the sanitary sewer network tool)
        peak flow = 300             # gpm, design peak hour
        initial average flow = 60   # gpm, average dry-weather flow in the first year (longest detention); blank = average flow
        pumps = 2                   # identical pumps; firm capacity = all but one running
        wet well diameter = 8       # ft inside (Rinker round), or give "wet well area" in ft²
        wet well bottom = 3368.0
        pump off = 3370.0
        lead on = 3372.5
        lag on = 3373.0
        high alarm = 3373.5
        inlet invert = 3374.2       # lowest gravity inlet; the high alarm should stay below it
        discharge elevation = 3412.0   # where the force main discharges (gravity manhole invert)
        force main length = 2600
        force main diameter = 6     # in. inside
        hazen C = 120               # PVC / lined DI design value; 100 for unlined iron
        minor K = 6.5               # sum of fitting and valve K on the force main velocity
        efficiency = 0.65           # wire-to-water pump efficiency at the duty point
        min cycle time = 10         # minutes between starts of one pump (6 starts/h); use the pump maker's value
        wave speed = 1300           # ft/s for surge; PVC DR18 about 1,300, ductile iron about 4,000
        pressure class = 235        # psi of the force main pipe

        [Pump curve]
        Q (gpm)	Head (ft)
        0	95
        200	88
        300	82
        400	73
        500	61
        600	45

        [Force main profile]
        Station (ft)	Ground or crown (ft)
        0	3380
        900	3396
        1800	3405
        2200	3402
        2600	3410
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sanitary lift station and force main";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-LS-001", "The lift station tool works in U.S. units (gpm, ft, in.); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double? Need(string key, string what)
        {
            var v = p.OptionalNumber(key, what);
            if (v is null && !p.Has(key)) msgs.Add(CalcMessage.Error("E-LS-002", $"Give {what} ({key} = …)."));
            return v;
        }
        double? qAvg = Need("average flow", "the average flow"), qPeak = Need("peak flow", "the peak flow");
        double qMin = p.Number("initial average flow", p.Number("minimum flow", qAvg ?? 0));
        int pumps = (int)Math.Round(p.Number("pumps", 2));
        double? off = Need("pump off", "the pump-off level"), leadOn = Need("lead on", "the lead pump-on level"), hd = Need("discharge elevation", "the force main discharge elevation");
        double? lagOn = p.OptionalNumber("lag on"), alarm = p.OptionalNumber("high alarm"), inletInv = p.OptionalNumber("inlet invert"), bottom = p.OptionalNumber("wet well bottom");
        double? wwDia = p.OptionalNumber("wet well diameter"), wwArea = p.OptionalNumber("wet well area");
        double? len = Need("force main length", "the force main length"), dIn = Need("force main diameter", "the force main inside diameter");
        double c = p.Number("hazen C", 120), k = p.Number("minor K", 0), eff = p.Number("efficiency", 0.65), tMin = p.Number("min cycle time", 10);
        double a = p.Number("wave speed", 1300), pClass = p.Number("pressure class", 0);
        double vMin = p.Number("min velocity", 2.0), vMax = p.Number("max velocity", 8.0);
        // Detention limits from published utility criteria (see docs/tools/sanitary-lift-station.md, D-712).
        double wwDetMax = p.Number("max wet well detention", 30);
        double fmProtect = p.Number("force main detention protect", 35), fmControl = p.Number("force main detention control", 90);
        double totalDetMax = p.Number("max total detention", 180);

        var curve = new List<(double Q, double H)>();
        foreach (var r in p.Rows("Pump curve"))
        {
            double? q = p.Cell(r, 0, "Q", "pump curve"), h = p.Cell(r, 1, "head", "pump curve");
            if (q is null || h is null || q < 0) { msgs.Add(CalcMessage.Error("E-LS-003", "Each [Pump curve] row needs Q (gpm, zero or more) and head (ft).")); continue; }
            curve.Add((q.Value, h.Value));
        }
        curve.Sort((x, y) => x.Q.CompareTo(y.Q));
        if (curve.Count < 2) msgs.Add(CalcMessage.Error("E-LS-003", "Give at least two points under [Pump curve] (Q gpm, head ft)."));
        else if (curve.Zip(curve.Skip(1)).Any(t => t.Second.H > t.First.H + 1e-9 || t.Second.Q <= t.First.Q))
            msgs.Add(CalcMessage.Error("E-LS-003", "The pump curve must have rising Q and falling head."));
        var profile = new List<(double Sta, double Elev)>();
        foreach (var r in p.Rows("Force main profile"))
        {
            double? s = p.Cell(r, 0, "station", "profile"), e = p.Cell(r, 1, "elevation", "profile");
            if (s is null || e is null) { msgs.Add(CalcMessage.Error("E-LS-004", "Each [Force main profile] row needs a station and an elevation.")); continue; }
            profile.Add((s.Value, e.Value));
        }
        profile.Sort((x, y) => x.Sta.CompareTo(y.Sta));

        if (qAvg is <= 0 || qPeak is <= 0 || qPeak < qAvg) msgs.Add(CalcMessage.Error("E-LS-005", "Flows must be above zero with the peak at least the average."));
        if (pumps < 1) msgs.Add(CalcMessage.Error("E-LS-005", "pumps must be 1 or more."));
        if (len is <= 0 || dIn is <= 0 || !(c > 0) || !(eff > 0 && eff <= 1) || !(tMin > 0) || !(a > 0) || k < 0)
            msgs.Add(CalcMessage.Error("E-LS-005", "Force main length, diameter, C, wave speed and cycle time must be above zero, efficiency 0–1, K zero or more."));
        if (off is { } o0 && leadOn is { } l0 && l0 <= o0) msgs.Add(CalcMessage.Error("E-LS-006", "The lead pump-on level must be above the pump-off level."));
        if (lagOn is { } lg && leadOn is { } l1 && lg < l1) msgs.Add(CalcMessage.Error("E-LS-006", "The lag pump-on level cannot be below the lead pump-on level."));
        double area = wwArea ?? (wwDia is { } wd ? Math.PI * wd * wd / 4 : double.NaN);
        if (!(area > 0)) msgs.Add(CalcMessage.Error("E-LS-007", "Give the wet well diameter (ft) or wet well area (ft²)."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double L = len!.Value, D = dIn!.Value, Qa = qAvg!.Value, Qp = qPeak!.Value;
        double pipeArea = Math.PI * Math.Pow(D / 12, 2) / 4;
        // Static head: the highest of the discharge and any force main high point beyond which the line drains (siphon not relied on).
        double hiPoint = profile.Count > 0 ? profile.Max(x => x.Elev) : double.NegativeInfinity;
        double topElev = Math.Max(hd!.Value, hiPoint);
        if (hiPoint > hd.Value) msgs.Add(CalcMessage.Warning("W-LS-006", $"The force main rises to {hiPoint:F2} ft, above the discharge ({hd.Value:F2}); the static head is taken to the high point and the line beyond it may run partly full (no siphon credit). Provide an air release / vacuum valve there."));
        var highs = new List<(double Sta, double Elev)>();
        for (int i = 1; i < profile.Count - 1; i++) if (profile[i].Elev > profile[i - 1].Elev && profile[i].Elev >= profile[i + 1].Elev) highs.Add(profile[i]);
        if (profile.Count > 1 && profile[^1].Elev > profile[^2].Elev && profile[^1].Elev > hd.Value) highs.Add(profile[^1]);

        double statMax = topElev - off!.Value, statMin = topElev - (lagOn ?? leadOn!.Value);
        int firmN = Math.Max(1, pumps - 1);
        double q1Off = Operating(1, statMax, L, D, c, k, curve), q1On = Operating(1, statMin, L, D, c, k, curve);
        double qFirm = Operating(firmN, statMax, L, D, c, k, curve), qAll = Operating(pumps, statMin, L, D, c, k, curve);
        double V(double q) => q / GpmPerCfs / pipeArea;
        double tdh1 = SystemHead(q1Off, statMax, L, D, c, k);
        double bhp = q1Off * tdh1 / (3960 * eff);
        double motor = MotorHp.FirstOrDefault(m => m >= bhp - 1e-9, double.NaN);

        // Wet well
        double vol = area * (leadOn!.Value - off.Value) * 7.4805;   // gal between off and lead on
        double vReq = tMin * q1Off / 4;                                // V = T·Q/4 (worst case Qin = Q/2)
        double tWorst = 4 * vol / q1Off;
        double tAvg = CycleTime(vol, q1Off, Qa), tMinFlow = CycleTime(vol, q1Off, qMin);
        double storedToOn = bottom is { } b ? area * (leadOn.Value - b) * 7.4805 : vol;
        double wwDet = vol / qMin, wwDetAvg = vol / Qa;   // operating volume (off to lead on), as V = Qavg·Tdet
        double fmVol = pipeArea * L * 7.4805;
        double fmDet = fmVol / qMin;

        // Surge: sudden stop of all running pumps (upper bound, Joukowsky).
        double vRun = V(Math.Max(qAll, q1Off));
        double dH = a * vRun / G;
        double pMax = (statMax + dH) * 0.4335;

        var summary = new List<string>
        {
            $"One pump: {q1Off:F0} gpm at {tdh1:F1} ft TDH (pump off) to {q1On:F0} gpm (wet well at {(lagOn ?? leadOn.Value):F2}); force main {V(q1Off):F2} ft/s.",
            $"Firm capacity ({firmN} of {pumps} pumps, largest out of service): {qFirm:F0} gpm against the {Qp:F0} gpm peak — {(qFirm >= Qp - 1e-9 ? "OK" : "SHORT")}.",
            $"Wet well {vol:F0} gal between off and lead on; shortest cycle {tWorst:F1} min (needs {tMin:G3}); detention at the initial average {qMin:F0} gpm: wet well {wwDet:F0} min, force main {fmDet:F0} min.",
            $"Brake horsepower {bhp:F1} hp at {eff:P0} efficiency; next standard motor {(double.IsNaN(motor) ? "over 300" : motor.ToString("G4"))} hp. Surge on sudden stop ΔH = {dH:F0} ft, peak {pMax:F0} psi" + (pClass > 0 ? $" against class {pClass:G4} psi." : "."),
        };

        if (qFirm < Qp - 1e-9) msgs.Add(CalcMessage.Warning("W-LS-001", $"Firm capacity {qFirm:F0} gpm is below the {Qp:F0} gpm peak flow (Ten States Standards Ch. 40: peak flow with the largest pump out of service)."));
        if (V(q1Off) < vMin - 1e-9) msgs.Add(CalcMessage.Warning("W-LS-002", $"Force main velocity {V(q1Off):F2} ft/s with one pump is below {vMin:G3} ft/s (Ten States Ch. 40 force main cleansing velocity); use a smaller force main or larger pump."));
        if (V(qAll) > vMax + 1e-9) msgs.Add(CalcMessage.Warning("W-LS-003", $"Force main velocity {V(qAll):F2} ft/s with all pumps exceeds {vMax:G3} ft/s (head loss and surge)."));
        if (tWorst < tMin - 1e-9) msgs.Add(CalcMessage.Warning("W-LS-004", $"Shortest cycle {tWorst:F1} min is below {tMin:G3} min: raise the lead-on level by {(vReq - vol) / 7.4805 / area:F2} ft (needs {vReq:F0} gal, V = T·Q/4)."));
        if (wwDet > wwDetMax) msgs.Add(CalcMessage.Warning("W-LS-005", $"Wet well detention {wwDet:F0} min at the initial average flow {qMin:F0} gpm exceeds {wwDetMax:G3} min (Fort Wayne SA8.07.1; 30 min at average flow is common practice): septic conditions and odor (IDCM §3.13.3 2d, 2e). Lower the lead-on level or use variable speed, keeping the minimum cycle time."));
        if (fmDet > fmControl) msgs.Add(CalcMessage.Warning("W-LS-007", $"Force main detention {fmDet:F0} min at {qMin:F0} gpm exceeds {fmControl:G3} min: install active hydrogen sulfide control (Clean Water Services Ch. 9 §9.01.3.i; IDCM §3.13.3 2e)."));
        else if (fmDet > fmProtect) msgs.Add(CalcMessage.Warning("W-LS-007", $"Force main detention {fmDet:F0} min at {qMin:F0} gpm is between {fmProtect:G3} and {fmControl:G3} min: protect the discharge manhole and downstream sewer from H2S (lining or coating; Clean Water Services Ch. 9 §9.01.3.i)."));
        if (wwDet + fmDet > totalDetMax) msgs.Add(CalcMessage.Warning("W-LS-013", $"Wet well plus force main detention {wwDet + fmDet:F0} min exceeds {totalDetMax:G3} min (New Braunfels Utilities §3.06.1 odor practice)."));
        if (q1Off <= 0) msgs.Add(CalcMessage.Warning("W-LS-008", $"One pump's shut-off head ({curve[0].H:F1} ft) does not exceed the static head {statMax:F1} ft: no flow."));
        if (pClass > 0 && pMax > pClass) msgs.Add(CalcMessage.Warning("W-LS-009", $"Static plus Joukowsky surge {pMax:F0} psi exceeds the {pClass:G4} psi pressure class; add surge relief, slow-closing check valves, or a heavier class (detailed transient analysis recommended)."));
        if (D < 4 - 1e-9) msgs.Add(CalcMessage.Warning("W-LS-010", $"Force main {D:G3} in. is below the 4 in. minimum (Ten States Ch. 40) unless grinder pumps are used."));
        if (alarm is { } al && inletInv is { } ii && al >= ii) msgs.Add(CalcMessage.Warning("W-LS-011", $"High alarm {al:F2} is at or above the inlet invert {ii:F2}; keep the operating range below the incoming sewer."));
        if (inletInv is { } ii2 && (lagOn ?? leadOn.Value) >= ii2) msgs.Add(CalcMessage.Warning("W-LS-011", $"The pump-on level is at or above the inlet invert {ii2:F2}; the incoming sewer would surcharge."));
        if (q1Off > curve[^1].Q * 0.999 || qAll / pumps > curve[^1].Q * 0.999) msgs.Add(CalcMessage.Warning("W-LS-012", "An operating point runs off the end of the pump curve; extend the curve or choose another pump."));
        foreach (var h in highs) msgs.Add(CalcMessage.Info("I-LS-001", $"High point at station {h.Sta:F0} ({h.Elev:F2}): combination air release / vacuum valve (Ten States Ch. 40)."));
        msgs.Add(CalcMessage.Info("I-LS-002", "IDCM §3.13.3 design report items covered: pump number, capacity and horsepower; system head curve with force main size, length and C; detention times in the wet well and force main. NPSH (dry-pit pumps), odor control, standby power, O&M and economics are for the report."));
        msgs.Add(CalcMessage.Info("I-LS-003", $"Limits: firm capacity with the largest pump out, 2 ft/s minimum force main velocity (Ten States Ch. 40), cycle V = T·Q/4; detention at the initial average flow: wet well ≤ {wwDetMax:G3} min (Fort Wayne SA8.07.1), force main < {fmProtect:G3} min needs no H2S measures, {fmProtect:G3}–{fmControl:G3} min protect the discharge, over {fmControl:G3} min active control (Clean Water Services §9.01.3.i), wet well + force main ≤ {totalDetMax:G3} min (New Braunfels §3.06.1). Change with max wet well detention, force main detention protect / control, max total detention."));

        // Tables
        var curveRows = new List<IReadOnlyList<object?>>();
        double qTop = Math.Max(curve[^1].Q * pumps, Qp) * 1.1;
        var sysOff = new List<(double, double)>(); var sysOn = new List<(double, double)>();
        for (int i = 0; i <= 20; i++)
        {
            double q = qTop * i / 20;
            double hOff = SystemHead(q, statMax, L, D, c, k), hOn = SystemHead(q, statMin, L, D, c, k);
            sysOff.Add((q, R(hOff, 2))); sysOn.Add((q, R(hOn, 2)));
            curveRows.Add([R(q, 0), R(V(q), 2), R(FrictionGpm(q, L, D, c), 2), R(hOff, 2), R(hOn, 2)]);
        }
        var opRows = new List<IReadOnlyList<object?>>();
        for (int n = 1; n <= pumps; n++)
            foreach (var (lvl, stat) in new[] { ("pump off", statMax), (lagOn is null ? "lead on" : "lag on", statMin) })
            {
                double q = Operating(n, stat, L, D, c, k, curve);
                opRows.Add([n, lvl, R(stat, 2), R(q, 0), R(q / n, 0), R(SystemHead(q, stat, L, D, c, k), 2), R(V(q), 2), R(q / n * SystemHead(q, stat, L, D, c, k) / (3960 * eff), 2)]);
            }
        List<IReadOnlyList<object?>> wwRows =
        [
            ["Operating volume, off to lead on (gal)", R(vol, 0)], ["Required V = T·Q/4 (gal)", R(vReq, 0)],
            ["Shortest cycle, inflow = Q/2 (min)", R(tWorst, 1)], [$"Cycle at average flow {Qa:F0} gpm (min)", R(tAvg, 1)], [$"Cycle at minimum flow {qMin:F0} gpm (min)", R(tMinFlow, 1)],
            ["Starts per hour (worst case)", R(60 / tWorst, 1)],
            [$"Wet well detention at initial average {qMin:F0} gpm (min)", R(wwDet, 0)], [$"Wet well detention at average {Qa:F0} gpm (min)", R(wwDetAvg, 0)], [$"Held below lead on at initial average (min)", R(storedToOn / qMin, 0)], ["Force main volume (gal)", R(fmVol, 0)], [$"Force main detention at initial average {qMin:F0} gpm (min)", R(fmDet, 0)], ["Wet well + force main (min)", R(wwDet + fmDet, 0)],
            ["Velocity at one pump (ft/s)", R(V(q1Off), 2)], ["Surge ΔH = a·V/g (ft)", R(dH, 1)], ["Static + surge (psi)", R(pMax, 0)],
        ];
        var tables = new List<ToolTable>
        {
            new("System head curves", ["Q (gpm)", "V (ft/s)", "Friction (ft)", "TDH at pump off (ft)", "TDH at pump on (ft)"], curveRows,
                [$"Static head to {topElev:F2} ft ({(hiPoint > hd.Value ? "force main high point" : "discharge")}); friction 10.44·L·Q^1.852/(C^1.852·D^4.8655), L = {L:F0} ft, D = {D:G3} in., C = {c:G3}; minor losses {k:G3}·V²/2g."]),
            new("Operating points", ["Pumps running", "Wet well", "Static (ft)", "Total Q (gpm)", "Q per pump (gpm)", "TDH (ft)", "FM velocity (ft/s)", "BHP per pump (hp)"], opRows,
                [$"Identical pumps in parallel on one force main; BHP = Q·H/(3960·η), η = {eff:G3}."]),
            new("Wet well, detention and surge", ["Item", "Value"], wwRows,
                [$"Wet well area {area:F2} ft²; surge for an instant stop of the running pumps at a = {a:G4} ft/s (upper bound)."]),
        };
        var charts = new List<ToolChart>
        {
            new("Pump and system curves", "Flow (gpm)", "Head (ft)",
                [new("Pump (1)", curve.Select(x => (x.Q, x.H)).ToList()), .. Enumerable.Range(2, Math.Max(0, pumps - 1)).Select(n => new ChartSeries($"Pumps ({n})", curve.Select(x => (x.Q * n, x.H)).ToList())),
                 new("System, pump off", sysOff, Dashed: true), new("System, pump on", sysOn, Dashed: true)]),
        };
        if (profile.Count > 1)
            charts.Add(new ToolChart("Force main profile and hydraulic grade (one pump)", "Station (ft)", "Elevation (ft)",
                [new("Force main", profile.Select(x => (x.Sta, x.Elev)).ToList()),
                 new("HGL, one pump", profile.Select(x => (x.Sta, R(off.Value + tdh1 - k * V(q1Off) * V(q1Off) / (2 * G) - FrictionGpm(q1Off, x.Sta, D, c), 2))).ToList(), Dashed: true)]));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
