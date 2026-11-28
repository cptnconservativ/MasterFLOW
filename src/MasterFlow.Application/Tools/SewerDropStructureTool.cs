using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Sewer drop structures: steep reaches over 10 ft/s at half depth (Rapid City IDCM §3.12.6 item 3) with the drop needed
/// to bring them back to the limit and Ten States §33.45 anchors; each manhole drop classed as a filleted invert, an
/// outside drop connection (Ten States §34.2, IDCM §3.12.10) or a vortex drop shaft; tangential vortex shafts sized by
/// D = k·(Q²/g)^0.2 (Jain 1984; Yu and Lee 2009) with the approach Froude number and energy to dissipate, and the baffle
/// drop alternative of Odgaard, Lyons and Craig (2013).
/// </summary>
public sealed class SewerDropStructureTool : IDesignTool
{
    public string Id => "sewer-drop-structures";
    public string Name => "Sewer drop structures and vortex drop shafts";
    public string Description => "Drop structures for sanitary and combined sewers: reaches over 10 ft/s at half depth (IDCM §3.12.6) with the drop to take out and the anchors needed over 20 % (Ten States §33.45); each manhole drop classed as a filleted invert, an outside drop connection (Ten States §34.2; IDCM §3.12.10 written permission, never internal) or a vortex drop shaft; tangential vortex shafts sized by D = k·(Q²/g)^0.2 with overload margin, approach Froude number and energy to dissipate; baffle drop shaft alternative (Odgaard et al. 2013).";
    public bool UsesNetwork => false;

    private const double G = 32.174, GpmPerCfs = 448.831;
    public static readonly double[] ShaftSizesIn = [24, 30, 36, 42, 48, 54, 60, 66, 72, 78, 84, 90, 96, 108, 120, 132, 144, 168, 192, 216, 240];

    /// <summary>Tangential vortex shaft diameter (ft) for Q (cfs): D = k·(Q²/g)^0.2 (Jain 1984; Yu and Lee 2009, k 1.0–1.3).</summary>
    public static double VortexDiameter(double q, double k) => k * Math.Pow(q * q / G, 0.2);

    /// <summary>Flow (cfs) at which a shaft of diameter <paramref name="d"/> ft reaches coefficient <paramref name="k"/>: Q = √g·(D/k)^2.5.</summary>
    public static double VortexCapacity(double d, double k) => Math.Sqrt(G) * Math.Pow(d / k, 2.5);

    /// <summary>Baffle drop shaft diameter (ft), Odgaard et al. (2013) Eq. 11: D = 6.0·Q^0.4/g^0.2 (centered dividing wall, B = D/2).</summary>
    public static double BaffleDiameter(double q) => 6.0 * Math.Pow(q, 0.4) / Math.Pow(G, 0.2);

    /// <summary>Odgaard et al. (2013) Eq. 5 capacity parameter F = (Q²/(B⁵·g))^(1/3).</summary>
    public static double BaffleF(double q, double b) => Math.Cbrt(q * q / (Math.Pow(b, 5) * G));

    /// <summary>Baffle spacing h (ft) from Eq. 8: (h − t)/B = F/β + hv/B, β = 0.55, hv/B = 0.1.</summary>
    public static double BaffleSpacing(double f, double b, double t, double beta = 0.55, double hvOverB = 0.1) => b * (f / beta + hvOverB) + t;

    /// <summary>Odgaard Eq. 7 plunge criterion value 2(h/D)(B/D)F + (1/β)(B/D)²F² (must be below 0.1).</summary>
    public static double PlungeCriterion(double h, double d, double b, double f, double beta = 0.55) => 2 * (h / d) * (b / d) * f + (1 / beta) * Math.Pow(b / d, 2) * f * f;

    /// <summary>Critical depth (ft) of Q cfs in a circular pipe of diameter d ft: Q²/g = A³/T.</summary>
    public static double CriticalDepth(double q, double d)
    {
        if (!(q > 0)) return 0;
        double lo = 1e-6 * d, hi = 0.999 * d;
        double F(double y) => Math.Pow(SanitarySewerTool.Area(y, d), 3) / SanitarySewerTool.TopWidth(y, d) - q * q / G;
        if (F(hi) < 0) return hi;
        for (int i = 0; i < 100; i++) { double m = 0.5 * (lo + hi); if (F(m) < 0) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    /// <summary>Ten States §33.45 anchor spacing (ft) for a slope (ft/ft): 36 ft for 20–35 %, 24 ft for 35–50 %, 16 ft for 50 % and over; NaN below 20 %.</summary>
    public static double AnchorSpacing(double s) => s < 0.20 ? double.NaN : s < 0.35 ? 36 : s < 0.50 ? 24 : 16;

    public string SampleInput(DesignToolContext context) => """
        # Sewer drop structures and vortex drop shafts. U.S. units: flows gpm, diameters in., lengths and elevations ft.
        n = 0.013                   # Manning n (IDCM 3.12.6: not below 0.013)
        max velocity = 10           # ft/s at half depth (IDCM 3.12.6 item 3)
        drop pipe height = 2        # ft; a drop connection is needed at 24 in. or more (Ten States §34.2)
        vortex drop height = 15     # ft; drops this high or more (or with an inlet over the max velocity) get a vortex shaft
        max drop per structure = 30 # ft, for regrading a steep reach
        vortex k = 1.2              # D = k·(Q²/g)^0.2; 1.0–1.3 (Yu and Lee 2009)
        overload = 1.5              # the shaft should stay at k ≥ 1.0 up to this multiple of the peak flow
        min shaft diameter = 24     # in.
        baffle thickness = 0.5      # ft, for the baffle drop alternative

        [Reaches]
        Pipe	Length (ft)	D (in)	Up invert	Down invert	Peak flow (gpm)
        T-1	400	15	3520.00	3514.00	1350
        T-2	350	15	3514.00	3486.00	1350
        T-3	420	18	3462.00	3459.48	2100

        [Drops]
        Manhole	Inlet invert	Outlet invert	Peak flow (gpm)	Inlet D (in)	Inlet slope (%)	Outlet D (in)	Outlet slope (%)
        MH-12	3486.00	3484.60	1350	15	1.5	15	1.5
        MH-14	3486.00	3462.00	2100	15	1.5	18	0.6
        MH-20	3459.48	3455.00	600	10	0.8	10	0.8
        """;

    private sealed record Drop(string Name, double Inlet, double Outlet, double QGpm, double DinIn, double? SinPct, double DoutIn, double? SoutPct, string Source);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sewer drop structures and vortex drop shafts";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-DS-001", "The drop structure tool works in U.S. units (gpm, in., ft); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double n = p.Number("n", 0.013), vMax = p.Number("max velocity", 10), dropPipe = p.Number("drop pipe height", 2), vortexH = p.Number("vortex drop height", 15);
        double maxDrop = p.Number("max drop per structure", 30), k = p.Number("vortex k", 1.2), overload = p.Number("overload", 1.5);
        double minShaft = p.Number("min shaft diameter", 24), tBaffle = p.Number("baffle thickness", 0.5);
        if (!(n > 0) || !(vMax > 0) || !(dropPipe >= 0) || !(vortexH > 0) || !(maxDrop > 0) || !(k > 0) || !(overload >= 1) || !(minShaft >= 0) || !(tBaffle >= 0))
            msgs.Add(CalcMessage.Error("E-DS-002", "n, max velocity, vortex drop height, max drop per structure and vortex k must be above zero, overload at least 1, the others zero or more."));
        if (n < 0.013 - 1e-9) msgs.Add(CalcMessage.Warning("W-DS-001", $"n = {n:G3} is below 0.013, which IDCM 3.12.6 item 4 does not permit."));
        if (k < 1.0 - 1e-9 || k > 1.3 + 1e-9) msgs.Add(CalcMessage.Warning("W-DS-002", $"vortex k = {k:G3} is outside the 1.0–1.3 range of Yu and Lee (2009)."));

        // ---- reaches
        var reaches = new List<(string Name, double L, double DIn, double Up, double Dn, double QGpm)>();
        foreach (var r in p.Rows("Reaches"))
        {
            string name = r.Length > 0 ? r[0].Trim() : "";
            double? l = p.Cell(r, 1, "length", name), d = p.Cell(r, 2, "D", name), up = p.Cell(r, 3, "up invert", name), dn = p.Cell(r, 4, "down invert", name), q = p.Cell(r, 5, "peak flow", name);
            if (l is null || d is null || up is null || dn is null || q is null) { msgs.Add(CalcMessage.Error("E-DS-003", $"Reach '{name}' needs length, D (in.), up and down inverts and peak flow (gpm).")); continue; }
            if (!(l > 0) || !(d > 0) || !(q >= 0) || !(up > dn)) { msgs.Add(CalcMessage.Error("E-DS-003", $"Reach '{name}': length and D must be above zero, flow zero or more and the up invert above the down invert.")); continue; }
            reaches.Add((name, l.Value, d.Value, up.Value, dn.Value, q.Value));
        }
        var drops = new List<Drop>();
        foreach (var r in p.Rows("Drops"))
        {
            string name = r.Length > 0 ? r[0].Trim() : "";
            double? ii = p.Cell(r, 1, "inlet invert", name), oi = p.Cell(r, 2, "outlet invert", name), q = p.Cell(r, 3, "peak flow", name), di = p.Cell(r, 4, "inlet D", name);
            double? si = r.Length > 5 && r[5].Trim().Length > 0 ? p.Cell(r, 5, "inlet slope", name) : null;
            double? dOut = r.Length > 6 && r[6].Trim().Length > 0 ? p.Cell(r, 6, "outlet D", name) : di;
            double? so = r.Length > 7 && r[7].Trim().Length > 0 ? p.Cell(r, 7, "outlet slope", name) : null;
            if (ii is null || oi is null || q is null || di is null || dOut is null) { msgs.Add(CalcMessage.Error("E-DS-004", $"Drop '{name}' needs inlet and outlet inverts, peak flow (gpm) and inlet D (in.).")); continue; }
            if (!(q > 0) || !(di > 0) || !(dOut > 0) || si is <= 0 || so is <= 0 || !(ii >= oi)) { msgs.Add(CalcMessage.Error("E-DS-004", $"Drop '{name}': flow, diameters and slopes must be above zero and the inlet invert at or above the outlet invert.")); continue; }
            drops.Add(new Drop(name, ii.Value, oi.Value, q.Value, di.Value, si, dOut.Value, so, "entered"));
        }
        if (reaches.Count == 0 && drops.Count == 0) msgs.Add(CalcMessage.Error("E-DS-005", "Give [Reaches] to check velocities and/or [Drops] to class and size drop structures."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        // ---- reach velocity check and regrading
        var reachRows = new List<IReadOnlyList<object?>>();
        var regradeRows = new List<IReadOnlyList<object?>>();
        foreach (var x in reaches)
        {
            double dFt = x.DIn / 12, s = (x.Up - x.Dn) / x.L, q = x.QGpm / GpmPerCfs;
            double vHalf = SanitarySewerTool.QFull(dFt, n, s) / (Math.PI * dFt * dFt / 4);
            double y = SanitarySewerTool.NormalDepth(q, dFt, n, s);
            double vPeak = y > 0 && double.IsFinite(y) ? q / SanitarySewerTool.Area(y, dFt) : 0;
            double sAllow = SanitarySewerTool.SlopeForFullVelocity(dFt, n, vMax);
            double sMin = SanitarySewerTool.TenStatesMinSlope(x.DIn) / 100;
            double anchors = AnchorSpacing(s);
            string status = "OK";
            if (vHalf > vMax + 1e-9)
            {
                status = $"over {vMax:G3} ft/s";
                double sNew = Math.Max(sAllow, sMin);
                double take = x.L * (s - sNew);
                int count = Math.Max(1, (int)Math.Ceiling(take / maxDrop - 1e-9));
                msgs.Add(CalcMessage.Warning("W-DS-003", $"{x.Name}: {vHalf:F2} ft/s at half depth on {s * 100:F2} % exceeds {vMax:G3} ft/s (IDCM 3.12.6 item 3). Regrade at {sNew * 100:F2} % and take {take:F2} ft out in {count} drop structure{(count > 1 ? "s" : "")} (vortex energy-dissipating manholes), or get written approval."));
                regradeRows.Add([x.Name, R(s * 100, 2), R(sNew * 100, 3), R(take, 2), count, R(take / count, 2)]);
                for (int i = 1; i <= count; i++)
                {
                    double h = take / count;
                    drops.Add(new Drop($"{x.Name} drop {i}/{count}", h, 0, x.QGpm, x.DIn, sNew * 100, x.DIn, sNew * 100, "regrade"));
                }
            }
            if (!double.IsNaN(anchors))
            {
                status = (status == "OK" ? "" : status + "; ") + $"anchors at {anchors:F0} ft";
                msgs.Add(CalcMessage.Warning("W-DS-004", $"{x.Name}: slope {s * 100:F1} % is 20 % or more; anchor the sewer with concrete anchors at no more than {anchors:F0} ft centers (Ten States §33.45; IDCM 3.12.6 item 3)."));
            }
            else if (vHalf > vMax + 1e-9) msgs.Add(CalcMessage.Info("I-DS-001", $"{x.Name}: anchor the sewer as Ten States §33.45 requires while it runs over {vMax:G3} ft/s (IDCM 3.12.6 item 3)."));
            if (double.IsNaN(y)) msgs.Add(CalcMessage.Warning("W-DS-005", $"{x.Name}: the peak flow exceeds the pipe's open-channel capacity; it surcharges."));
            reachRows.Add([x.Name, R(x.L, 1), x.DIn, R(s * 100, 3), R(vHalf, 2), double.IsNaN(y) ? null : R(y / dFt, 3), double.IsNaN(y) ? null : R(vPeak, 2), R(sAllow * 100, 3), status]);
        }

        // ---- drops
        var dropRows = new List<IReadOnlyList<object?>>();
        var vortexRows = new List<IReadOnlyList<object?>>();
        var baffleRows = new List<IReadOnlyList<object?>>();
        foreach (var dr in drops)
        {
            double h = dr.Inlet - dr.Outlet, q = dr.QGpm / GpmPerCfs, dIn = dr.DinIn / 12, dOut = dr.DoutIn / 12;
            // Approach: normal depth in the inlet pipe when its slope is known, else critical depth (the brink).
            double yo = dr.SinPct is { } si ? SanitarySewerTool.NormalDepth(q, dIn, n, si / 100) : CriticalDepth(q, dIn);
            if (double.IsNaN(yo)) { yo = 0.938 * dIn; msgs.Add(CalcMessage.Warning("W-DS-005", $"{dr.Name}: the inlet pipe surcharges at the peak flow.")); }
            double vo = q / SanitarySewerTool.Area(yo, dIn);
            double fr = vo / Math.Sqrt(G * SanitarySewerTool.Area(yo, dIn) / SanitarySewerTool.TopWidth(yo, dIn));
            double yu = dr.SoutPct is { } so ? SanitarySewerTool.NormalDepth(q, dOut, n, so / 100) : CriticalDepth(q, dOut);
            if (double.IsNaN(yu)) yu = 0.938 * dOut;
            double vu = q / SanitarySewerTool.Area(yu, dOut);
            double ho = h + yo + vo * vo / (2 * G), hu = yu + vu * vu / (2 * G);
            double vFall = Math.Sqrt(vo * vo + 2 * G * h);
            string type;
            if (h < dropPipe - 1e-9) type = "Filleted invert (no drop pipe)";
            else if (h >= vortexH - 1e-9 || vo > vMax + 1e-9) type = "Vortex drop shaft";
            else type = "Outside drop connection";
            dropRows.Add([dr.Name, R(h, 2), R(dr.QGpm, 0), R(q, 3), dr.DinIn, R(yo / dIn, 3), R(vo, 2), R(fr, 2), R(vFall, 1), type]);
            if (type.StartsWith("Outside"))
                msgs.Add(CalcMessage.Warning("W-DS-006", $"{dr.Name}: a {h:F2} ft drop needs an outside drop connection (Ten States §34.2: 24 in. or more). Rapid City allows drop manholes only with the Public Works Director's written permission, and never internal drops (IDCM 3.12.10)."));
            if (!type.StartsWith("Vortex")) continue;

            // Tangential vortex drop shaft
            double dHyd = VortexDiameter(q, k);
            double dNeed = Math.Max(Math.Max(dHyd, dIn), minShaft / 12);
            double dStd = ShaftSizesIn.Where(z => z / 12 >= dNeed - 1e-9).DefaultIfEmpty(Math.Ceiling(dNeed) * 12).First() / 12;   // beyond the list: next whole foot
            double qAtK1 = VortexCapacity(dStd, 1.0);
            double fc = q / Math.Sqrt(G * Math.Pow(dStd, 5));
            double yc = Math.Cbrt(q * q / (G * dStd * dStd));   // critical depth in an intake channel as wide as the shaft
            vortexRows.Add([dr.Name, R(h, 2), R(q, 3), R(dHyd * 12, 1), R(dStd * 12, 0), R(fc, 3), R(qAtK1 / q, 2), R(yc, 2), fr < 1 ? "subcritical" : "supercritical", R(ho, 2), R(hu, 2), R(ho > 0 ? 1 - hu / ho : 0, 3), R(vu, 2)]);
            if (qAtK1 < overload * q - 1e-9)
                msgs.Add(CalcMessage.Warning("W-DS-007", $"{dr.Name}: the {dStd * 12:F0} in. shaft reaches k = 1.0 at {qAtK1 / q:F2} × the peak flow, below the {overload:G3} × overload; the air core may collapse and the inlet choke (Yu and Lee 2009). Use a larger shaft."));
            if (fr > 1) msgs.Add(CalcMessage.Info("I-DS-002", $"{dr.Name}: the approach is supercritical (Fr {fr:F2}); use a tapered tangential intake with a steep bottom (10 to 20 %, Hager) rather than a subcritical spiral intake, and confirm the intake by physical model or the shaft supplier."));
            if (dHyd < dIn) msgs.Add(CalcMessage.Info("I-DS-003", $"{dr.Name}: the hydraulic shaft size ({dHyd * 12:F1} in.) is smaller than the inlet pipe; the shaft is set by the pipe or the minimum size. At small flows an outside drop is usually simpler, and a vortex drop needs the Public Works Director's approval (IDCM 3.12.6, 3.12.10)."));

            // Baffle drop alternative, Odgaard et al. (2013)
            double dB = ShaftSizesIn.Where(z => z / 12 >= BaffleDiameter(q) - 1e-9).DefaultIfEmpty(Math.Ceiling(BaffleDiameter(q)) * 12).First() / 12;
            dB = Math.Max(dB, Math.Max(dIn, minShaft / 12));
            double bB = dB / 2, fB = BaffleF(q, bB), hB = BaffleSpacing(fB, bB, tBaffle);
            double plunge = PlungeCriterion(hB, dB, bB, fB);
            int baffles = (int)Math.Ceiling(h / hB - 1e-9);
            baffleRows.Add([dr.Name, R(BaffleDiameter(q) * 12, 1), R(dB * 12, 0), R(bB, 2), R(fB, 3), R(hB, 2), baffles, R(plunge, 3), fB < 0.12 || fB > 0.23 ? "outside 0.12–0.23" : plunge >= 0.1 ? "plunge not assured" : "OK"]);
            if (plunge >= 0.1) msgs.Add(CalcMessage.Warning("W-DS-008", $"{dr.Name}: baffle drop plunge criterion {plunge:F3} ≥ 0.1 (Odgaard et al. 2013 Eq. 7); reduce the baffle spacing."));
        }

        var summary = new List<string>();
        int fast = regradeRows.Count;
        if (reaches.Count > 0) summary.Add($"{reaches.Count} reach{(reaches.Count == 1 ? "" : "es")} checked: {fast} over {vMax:G3} ft/s at half depth{(fast > 0 ? $", needing {regradeRows.Sum(r => (int)r[4]!)} drop structure(s) to regrade" : "")}.");
        foreach (var g in dropRows.GroupBy(r => (string)r[9]!)) summary.Add($"{g.Key}: {string.Join(", ", g.Select(r => $"{r[0]} ({r[1]:F2} ft)"))}.");
        foreach (var v in vortexRows) summary.Add($"{v[0]}: {v[4]} in. tangential vortex shaft (hydraulic size {v[3]} in. at k = {k:G3}), {v[1]:F2} ft drop, about {(double)v[11]! * 100:F0} % of the inlet energy to dissipate in the shaft and chamber.");

        msgs.Add(CalcMessage.Info("I-DS-004", $"Vortex shaft D = k·(Q²/g)^0.2, k = {k:G3} (Jain 1984; Yu and Lee 2009, k 1.0–1.3), at least the inlet pipe and {minShaft:G3} in., then the next standard size. Capacity Froude number Fc = Q/√(g·D⁵). Energy to dissipate = 1 − Hu/Ho, from the inlet head above the outlet invert (drop + depth + V²/2g) and the outlet pipe's specific energy; physical models with a dissipation chamber measured about 95 to 99 % overall (Crispino et al. 2021)."));
        msgs.Add(CalcMessage.Info("I-DS-005", "Not sized here: intake geometry, the dissipation chamber and the air vent. The sources used give no air demand formula for sewer vortex drops; vent the shaft and chamber and confirm the design by physical or CFD model or with the supplier. Baffle drops follow Odgaard, Lyons and Craig (2013): D = 6.0·Q^0.4/g^0.2, B = D/2, β = 0.55, hv/B = 0.1, valid for 0.12 < F < 0.23."));

        var tables = new List<ToolTable>();
        if (reachRows.Count > 0)
            tables.Add(new("Reach velocities", ["Pipe", "Length (ft)", "D (in)", "Slope (%)", "V half = V full (ft/s)", "d/D peak", "V peak (ft/s)", $"Slope for {vMax:G3} ft/s (%)", "Status"], reachRows,
                [$"Manning n = {n:G3}. IDCM 3.12.6 item 3: at most {vMax:G3} ft/s at half depth unless approved, with vortex energy-dissipating manholes; over 20 % or over 10 ft/s, anchors per Ten States §33.45 (36 ft centers to 35 %, 24 ft to 50 %, 16 ft above)."]));
        if (regradeRows.Count > 0)
            tables.Add(new("Regrading steep reaches", ["Pipe", "Slope now (%)", "Regraded slope (%)", "Drop to take out (ft)", "Structures", "Drop each (ft)"], regradeRows,
                [$"Regraded at the slope for {vMax:G3} ft/s at half depth (not below the Ten States minimum); at most {maxDrop:G3} ft per structure."]));
        if (dropRows.Count > 0)
            tables.Add(new("Drops", ["Structure", "Drop (ft)", "Q (gpm)", "Q (cfs)", "Inlet D (in)", "Inlet d/D", "Inlet V (ft/s)", "Inlet Fr", "Impact V, free fall (ft/s)", "Type"], dropRows,
                [$"Under {dropPipe:G3} ft: fillet the invert (Ten States §34.2). {dropPipe:G3} ft up to {vortexH:G3} ft: outside drop connection. {vortexH:G3} ft or more, or an inlet over {vMax:G3} ft/s: vortex drop shaft. Inlet depth is normal depth when the inlet slope is given, else critical depth."]));
        if (vortexRows.Count > 0)
            tables.Add(new("Tangential vortex drop shafts", ["Structure", "Drop (ft)", "Q (cfs)", "Hydraulic D (in)", "Shaft D (in)", "Fc = Q/√(gD⁵)", "Q at k = 1.0 / Q", "Intake yc (ft)", "Approach", "Ho (ft)", "Hu (ft)", "Energy to dissipate (share of Ho)", "Outlet V (ft/s)"], vortexRows,
                [$"Intake yc = (Q²/(g·B²))^⅓ for an intake channel as wide as the shaft. Ho above the outlet invert; Hu = outlet pipe depth + V²/2g. Overload check: the shaft should stay at k ≥ 1.0 up to {overload:G3} × Q."]));
        if (baffleRows.Count > 0)
            tables.Add(new("Baffle drop shaft alternative (Odgaard et al. 2013)", ["Structure", "Eq. 11 D (in)", "Shaft D (in)", "Baffle width B (ft)", "F", "Baffle spacing h (ft)", "Baffles", "Plunge criterion", "Status"], baffleRows,
                [$"B = D/2 (centered dividing wall); h = B·(F/β + hv/B) + t, β = 0.55, hv/B = 0.1, t = {tBaffle:G3} ft; plunge criterion 2(h/D)(B/D)F + (B/D)²F²/β < 0.1 (Eq. 7)."]));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
