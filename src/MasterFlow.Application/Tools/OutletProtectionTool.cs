using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;
using MasterFlow.Hydraulics.Numerics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Culvert and storm sewer outlet protection: outlet velocity and depth, HEC-14 (FHWA 2006) riprap apron (§10.1) and
/// riprap basin (§10.2) with the Rapid City IDCM 2012 §4.9.5 basin geometry and limits, and the IDCM plunge pool scour
/// depth (Eq. 4-9-1).
/// </summary>
public sealed class OutletProtectionTool : IDesignTool
{
    public string Id => "outlet-protection";
    public string Name => "Outlet protection: riprap apron, riprap basin, plunge pool (HEC-14)";
    public string Description => "Outlet velocity and depth of each culvert or pipe outlet (normal depth or your value), then HEC-14 (2006) riprap apron stone class, length, thickness and width (§10.1, Table 10.1) and HEC-14 riprap basin stone size, pool depth, pool and apron lengths (§10.2), laid out to Rapid City IDCM §4.9.5 (3D wide at the outlet, 3:1 flare, 1.5× stone thickness, Vp ≤ 3.33 Vc, outlet slope ≤ 5 %). Plunge pool scour depth by IDCM Eq. 4-9-1.";
    public bool UsesNetwork => false;

    /// <summary>HEC-14 (2006) Table 10.1 riprap apron classes: D50 (in, mm), apron length in culvert rises, apron depth in D50.</summary>
    internal static readonly (int Class, double D50In, double D50Mm, double LengthD, double DepthD50)[] ApronClasses =
    [
        (1, 5, 125, 4, 3.5), (2, 6, 150, 4, 3.3), (3, 10, 250, 5, 2.4), (4, 14, 350, 6, 2.2), (5, 20, 500, 7, 2.0), (6, 22, 550, 8, 2.0),
    ];

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Outlet protection, SI (m, m³/s). One row per outlet. Shape circular (Span = D) or box (Span × Rise).
        # Outlet depth: leave blank to use normal depth from Slope and n (critical or tailwater depth when the barrel is mild).
        [Outlets]
        Name	Shape	Span (m)	Rise (m)	Q (m³/s)	Slope (%)	n	Outlet depth (m)	Tailwater (m)	Channel V (m/s)
        CV-1	box	1.524	1.524	5.66	3.0	0.012	0.655	0	1.0
        P-12	circular	0.9	0.9	1.1	2.0	0.013		0.3	1.0

        [Plunge pool]
        Name	Head (m)	Unit discharge (m³/s/m)
        Drop A	2.5	1.0
        """ : """
        # Outlet protection to HEC-14 (FHWA 2006) and the Rapid City IDCM 2012 §4.9. One row per outlet.
        # Shape circular (Span = D) or box (Span × Rise), in ft. Outlet depth: leave blank to use normal depth from Slope and n
        # (critical depth, or the tailwater up to the crown, when the barrel is on a mild slope). Tailwater blank = unknown
        # (HEC-14 then takes 0.4·D for the apron). Channel V = downstream channel velocity for the IDCM Vp ≤ 3.33·Vc check.
        [Outlets]
        Name	Shape	Span (ft)	Rise (ft)	Q (cfs)	Slope (%)	n	Outlet depth (ft)	Tailwater (ft)	Channel V (ft/s)
        CV-1	box	5	5	200	3.0	0.012	2.15	0	6
        P-12	circular	3	3	40	2.0	0.013		1.0	3.5
        P-20	circular	1.5	1.5	8	1.0	0.013		0.5	2.5

        # Optional: fix the basin stone (ft) for an outlet instead of the HEC-14 choice (largest D50 with hs/D50 ≥ 2).
        # [Basin stone]
        # Name	D50 (ft)
        # CV-1	1.0

        # IDCM Eq. 4-9-1 plunge pool: ds = 1.32 HT^0.225 q^0.54 (HT = head from the upstream to the tailwater level, ft;
        # q = unit discharge, cfs/ft). Only where there is a continuous low flow (§4.9.2).
        [Plunge pool]
        Name	Head (ft)	Unit discharge (cfs/ft)
        Drop A	8	10
        """;

    private sealed record Outlet(string Name, bool Box, double Span, double Rise, double Q, double? Slope, double? N, double? Yo, double? Tw, double? Vc);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Outlet protection (HEC-14, IDCM §4.9)";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.81 : 32.174, k = si ? 1.0 : 1.486;
        string L = si ? "m" : "ft", V = si ? "m/s" : "ft/s", Qu = si ? "m³/s" : "cfs";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        var head = p.Header("Outlets").Select(h => h.ToLowerInvariant()).ToList();
        int Col(string name, int fallback) { int i = head.FindIndex(h => h == name || h.StartsWith(name + " ", StringComparison.Ordinal) || (name.Length > 1 && h.StartsWith(name, StringComparison.Ordinal))); return i >= 0 ? i : fallback; }
        int cShape = Col("shape", 1), cSpan = Col("span", 2), cRise = Col("rise", 3), cQ = Col("q", 4), cS = Col("slope", 5), cN = Col("n", 6),
            cYo = Col("outlet depth", 7), cTw = Col("tailwater", 8), cVc = Col("channel v", 9);
        var outlets = new List<Outlet>();
        foreach (var r in p.Rows("Outlets"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Outlet {outlets.Count + 1}";
            string shape = (r.Length > cShape ? r[cShape] : "circular").Trim().ToLowerInvariant();
            bool box = shape.StartsWith("box", StringComparison.Ordinal) || shape.StartsWith("rect", StringComparison.Ordinal);
            if (!box && !shape.StartsWith("circ", StringComparison.Ordinal) && shape.Length > 0) { msgs.Add(CalcMessage.Error("E-OP-001", $"{name}: shape \"{shape}\" is not circular or box.")); continue; }
            double? span = p.Cell(r, cSpan, "span", name), rise = p.Cell(r, cRise, "rise", name), q = p.Cell(r, cQ, "Q", name);
            if (span is not > 0 || q is not > 0) { msgs.Add(CalcMessage.Error("E-OP-002", $"{name}: give a span (diameter) and a discharge above zero.")); continue; }
            rise = box ? rise ?? span : span;
            if (rise is not > 0) { msgs.Add(CalcMessage.Error("E-OP-002", $"{name}: the rise must be above zero.")); continue; }
            outlets.Add(new(name, box, span.Value, rise.Value, q.Value, p.Cell(r, cS, "slope", name), p.Cell(r, cN, "n", name), p.Cell(r, cYo, "outlet depth", name), p.Cell(r, cTw, "tailwater", name), p.Cell(r, cVc, "channel velocity", name)));
        }
        var fixedStone = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Basin stone"))
            if (r.Length > 0 && p.Cell(r, 1, "D50", r[0]) is { } d && d > 0) fixedStone[r[0]] = d;
        var pools = new List<(string Name, double H, double Qu)>();
        foreach (var r in p.Rows("Plunge pool"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Pool {pools.Count + 1}";
            if (p.Cell(r, 1, "head", name) is { } h && p.Cell(r, 2, "unit discharge", name) is { } qq)
            {
                if (h <= 0 || qq <= 0) { msgs.Add(CalcMessage.Error("E-OP-003", $"{name}: head and unit discharge must be above zero.")); continue; }
                pools.Add((name, h, qq));
            }
        }
        msgs.InsertRange(0, p.Errors);
        if (outlets.Count == 0 && pools.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error))
            msgs.Add(CalcMessage.Error("E-OP-004", "Give at least one row under [Outlets] (or [Plunge pool])."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var cond = new List<IReadOnlyList<object?>>();
        var apron = new List<IReadOnlyList<object?>>();
        var basin = new List<IReadOnlyList<object?>>();
        var figures = new List<ToolFigure>();
        foreach (var o in outlets)
        {
            // Outlet flow conditions.
            ChannelSection sec = o.Box ? new RectangularSection(o.Span) : new CircularSection(o.Span);
            var geo = SectionGeometry.From(sec);
            double yc = Math.Min(o.Rise, Root(UniformFlowCalculator.SolveCriticalDepth(geo, o.Q, g), o.Rise));
            double yn = double.NaN;
            if (o.Slope is > 0 && o.N is > 0)
                yn = Math.Min(o.Rise, Root(UniformFlowCalculator.SolveNormalDepth(geo, o.Q, o.N.Value, o.Slope.Value / 100, k), o.Rise));
            double yo; string basis;
            if (o.Yo is > 0) { yo = Math.Min(o.Yo.Value, o.Rise); basis = "given"; }
            else if (!double.IsNaN(yn) && yn < yc) { yo = yn; basis = "normal (steep barrel)"; }
            else if (!double.IsNaN(yn) || o.Slope is null)
            {
                yo = Math.Max(yc, Math.Min(o.Tw ?? 0, o.Rise));
                basis = (o.Tw ?? 0) > yc ? "tailwater (mild barrel)" : "critical (mild barrel)";
                if (o.Slope is null) msgs.Add(CalcMessage.Info("I-OP-001", $"{o.Name}: no slope and n given; the outlet depth is taken as critical depth (or the tailwater, up to the crown)."));
            }
            else { yo = yc; basis = "critical"; }
            double ao = geo.Area(yo), vo = o.Q / ao;
            // HEC-14 equivalent depth: ye = yo for a box, (A/2)^0.5 for a circular outlet.
            double ye = o.Box ? yo : Math.Sqrt(ao / 2);
            double fr = vo / Math.Sqrt(g * ye);
            cond.Add([o.Name, o.Box ? $"Box {o.Span:G4} × {o.Rise:G4}" : $"Circular {o.Span:G4}", o.Q, R(yc, 3), double.IsNaN(yn) ? null : R(yn, 3), R(yo, 3), basis, R(vo, 2), R(ye, 3), R(fr, 2), o.Tw]);

            // Riprap apron, HEC-14 §10.1: D50 = 0.2 D (Q / (√g D^2.5))^(4/3) (D / TW), 0.4 D ≤ TW ≤ D; D = ye for a box.
            double dA = o.Box ? Math.Sqrt(o.Span * o.Rise / 2) : o.Span;
            double tw = Math.Clamp(o.Tw ?? 0.4 * dA, 0.4 * dA, dA);
            double d50 = 0.2 * dA * Math.Pow(o.Q / (Math.Sqrt(g) * Math.Pow(dA, 2.5)), 4.0 / 3) * (dA / tw);
            double d50In = si ? d50 * 1000 : d50 * 12;
            var cls = ApronClasses.FirstOrDefault(c => (si ? c.D50Mm : c.D50In) >= d50In - 1e-9);
            string apronNote = "";
            if (o.Tw is null) apronNote = "TW unknown: 0.4·D used. ";
            else if (o.Tw < 0.4 * dA) apronNote = "TW < 0.4·D: 0.4·D used (HEC-14 lower limit). ";
            else if (o.Tw > dA) apronNote = "TW > D: D used. ";
            if (o.Rise > (si ? 1.524 : 5.0) + 1e-9) apronNote += "Rise above 60 in. (1500 mm): HEC-14 limits the apron to smaller culverts; use a basin. ";
            if (cls.Class == 0)
            {
                apron.Add([o.Name, R(dA, 3), R(tw, 3), R(d50In, 1), "> class 6", null, null, null, null, apronNote + "D50 above the largest apron class: use a riprap basin or another dissipator."]);
                msgs.Add(CalcMessage.Warning("W-OP-001", $"{o.Name}: the apron needs D50 = {d50In:F1} {(si ? "mm" : "in.")}, above HEC-14 class 6; use the riprap basin (or another dissipator)."));
            }
            else
            {
                double la = cls.LengthD * o.Rise, depth = cls.DepthD50 * (si ? cls.D50Mm / 1000 : cls.D50In / 12);
                double w1 = 3 * o.Span, w2 = w1 + 2 * la / 3;
                apron.Add([o.Name, R(dA, 3), R(tw, 3), R(d50In, 1), $"{cls.Class} ({(si ? cls.D50Mm : cls.D50In):G3} {(si ? "mm" : "in.")})", R(la, 2), R(depth, 2), R(w1, 2), R(w2, 2), apronNote.Trim()]);
                if (o.Rise > (si ? 1.524 : 5.0) + 1e-9) msgs.Add(CalcMessage.Warning("W-OP-002", $"{o.Name}: rise {o.Rise:G4} {L} is above 60 in. (1500 mm), the HEC-14 limit for a riprap apron (Table 1.1); use the riprap basin."));
            }

            // Riprap basin, HEC-14 §10.2: hs/ye = 0.86 (D50/ye)^-0.55 (Vo/√(g ye)) − C0, with 2 ≤ hs/D50 ≤ 4 and 0.1 ≤ D50/ye ≤ 0.7.
            double twYe = (o.Tw ?? 0) / ye;
            double c0 = twYe < 0.75 ? 1.4 : twYe < 1.0 ? 4.0 * twYe - 1.6 : 2.4;
            double Hs(double d) => ye * (0.86 * Math.Pow(d / ye, -0.55) * fr - c0);
            double step = si ? 0.01 : 0.1, stone;
            bool chosen = fixedStone.TryGetValue(o.Name, out stone);
            if (!chosen)
            {
                // Largest D50 (to 0.1 ft / 0.01 m) with hs/D50 ≥ 2: the shallowest pool HEC-14 allows.
                stone = double.NaN;
                for (int i = (int)Math.Floor(0.7 * ye / step + 1e-9); i >= 1; i--)
                {
                    double d = i * step;
                    if (Hs(d) / d >= 2) { stone = d; break; }
                }
            }
            if (double.IsNaN(stone) || Hs(stone) <= 0)
            {
                basin.Add([o.Name, R(c0, 2), null, null, null, null, null, null, null, null, null, "No stone size meets hs/D50 ≥ 2 with D50/ye ≤ 0.7: the jet is too weak for a scour basin; an apron is enough."]);
                msgs.Add(CalcMessage.Info("I-OP-002", $"{o.Name}: no HEC-14 riprap basin stone fits (the outlet Froude number {fr:F2} is low); the apron governs."));
            }
            else
            {
                double hs = Hs(stone), wo = o.Span;
                double lp = Math.Max(10 * hs, 3 * wo), la = Math.Max(5 * hs, wo), lb = lp + la;
                double w1 = 3 * o.Span, w2 = w1 + 2 * lb / 3;
                var notes = new List<string>();
                if (chosen) notes.Add("D50 given");
                double ratio = hs / stone, dye = stone / ye;
                if (ratio < 2 - 1e-9 || ratio > 4 + 1e-9) notes.Add($"hs/D50 = {ratio:F2} outside 2–4");
                if (dye < 0.1 - 1e-9 || dye > 0.7 + 1e-9) notes.Add($"D50/ye = {dye:F2} outside 0.1–0.7");
                if (twYe > 0.75) notes.Add("TW/ye > 0.75: line the channel downstream of the basin to the jet-decay velocity (HEC-14 §10.2)");
                basin.Add([o.Name, R(c0, 2), R(stone, 2), R(stone / ye, 2), R(hs, 2), R(hs / stone, 2), R(lp, 1), R(la, 1), R(lb, 1), R(3 * stone, 2), R(2 * stone, 2), $"{w1:F1} to {w2:F1}" + (notes.Count > 0 ? "; " + string.Join("; ", notes) : "")]);
                if (notes.Any(n => n.Contains("outside"))) msgs.Add(CalcMessage.Warning("W-OP-003", $"{o.Name}: the basin is outside the HEC-14 test range ({string.Join("; ", notes.Where(n => n.Contains("outside")))})."));
                if (twYe > 0.75) msgs.Add(CalcMessage.Warning("W-OP-004", $"{o.Name}: TW/ye = {twYe:F2} > 0.75; HEC-14 requires riprap in the channel downstream of the basin until the jet decays to the allowable velocity. That length is not computed here."));
                if (figures.Count < 3) figures.Add(BasinFigure(o.Name, hs, lp, la, stone, yo, o.Tw ?? 0, L));
            }
            if (fr > 3) msgs.Add(CalcMessage.Warning("W-OP-005", $"{o.Name}: outlet Froude number {fr:F2} > 3; HEC-14 Table 1.1 limits the riprap basin to Fr < 3. Consider a USBR Type VI impact basin (IDCM §4.9.2, Q ≤ 400 cfs, V ≤ 50 ft/s) or a hydraulic jump basin."));
            if (o.Vc is > 0 && vo > 3.33 * o.Vc.Value) msgs.Add(CalcMessage.Warning("W-OP-006", $"{o.Name}: outlet velocity {vo:F1} {V} is more than 3.33 × the channel velocity {o.Vc:F1} {V}; IDCM §4.9.5 does not allow a riprap basin here. Use an energy dissipator (USBR Type VI or CSU basin, §4.9.2/§4.9.6)."));
            if (o.Slope is > 5) msgs.Add(CalcMessage.Warning("W-OP-007", $"{o.Name}: outlet slope {o.Slope:F1} % exceeds 5 %; IDCM §4.9.5 limits the pipe to 5 % for 5·D upstream of a riprap basin."));
            if (o.Tw is { } t && t < o.Rise / 4) msgs.Add(CalcMessage.Info("I-OP-003", $"{o.Name}: tailwater {t:G3} {L} is below D/4; the IDCM §4.9.5 basin tables assume at least D/4."));
            if (o.Q > (si ? 11.3 : 400) || vo > (si ? 15.2 : 50)) msgs.Add(CalcMessage.Info("I-OP-004", $"{o.Name}: Q or V is above the USBR Type VI impact basin limits (400 cfs, 50 ft/s), should that basin be considered."));
        }

        var poolRows = pools.Select(pl =>
        {
            // Eq. 4-9-1 is in U.S. units; SI inputs are converted and the result returned in metres.
            double h = si ? pl.H / 0.3048 : pl.H, q = si ? pl.Qu / 0.3048 / 0.3048 / 0.3048 * 0.3048 : pl.Qu;
            double ds = 1.32 * Math.Pow(h, 0.225) * Math.Pow(q, 0.54);
            return (IReadOnlyList<object?>)new object?[] { pl.Name, pl.H, pl.Qu, R(si ? ds * 0.3048 : ds, 2) };
        }).ToList();
        if (pools.Count > 0) msgs.Add(CalcMessage.Info("I-OP-005", "Plunge pools (IDCM §4.9.2): only where the channel has a continuous low flow, because of the hazard of a stagnant pool; protect the pool with heavy riprap or concrete."));

        var tables = new List<ToolTable>();
        if (cond.Count > 0)
        {
            tables.Add(new("Outlet conditions", ["Outlet", "Barrel", $"Q ({Qu})", $"yc ({L})", $"yn ({L})", $"yo ({L})", "yo basis", $"Vo ({V})", $"ye ({L})", "Fr", $"TW ({L})"], cond,
                ["ye = yo for a box; ye = (A/2)^0.5 for a circular outlet (HEC-14). Fr = Vo/√(g·ye).", "A steep barrel flows at normal depth; a mild one at critical depth, or the tailwater up to the crown."]));
            tables.Add(new("Riprap apron (HEC-14 §10.1)", ["Outlet", $"D ({L})", $"TW used ({L})", $"D50 req. ({(si ? "mm" : "in.")})", "Class", $"Length ({L})", $"Depth ({L})", $"Width at outlet ({L})", $"Width at end ({L})", "Notes"], apron,
                ["D50 = 0.2·D·(Q/(√g·D^2.5))^(4/3)·(D/TW), with 0.4·D ≤ TW ≤ D; for a box D = (A/2)^0.5 of the full barrel.", "Class, length (in culvert rises) and depth (in D50) from HEC-14 Table 10.1.",
                 "Width 3·D at the outlet, flaring 1 on 3 each side (IDCM §4.9.5 riprap basin geometry, applied to the apron); riprap side slopes no steeper than 2H:1V; place over a granular filter or geotextile."]));
            tables.Add(new("Riprap basin (HEC-14 §10.2)", ["Outlet", "C0", $"D50 ({L})", "D50/ye", $"Pool depth hs ({L})", "hs/D50", $"Pool length ({L})", $"Apron length ({L})", $"Basin length ({L})", $"Thickness, approach ({L})", $"Thickness, rest ({L})", $"Width ({L}); notes"], basin,
                ["hs/ye = 0.86·(D50/ye)^-0.55·Fr − C0; C0 = 1.4 for TW/ye < 0.75, 4·TW/ye − 1.6 up to 1.0, 2.4 above.",
                 "D50 is the largest (to 0.1 ft or 0.01 m) with hs/D50 ≥ 2, giving the shallowest pool; HEC-14 limits 2 ≤ hs/D50 ≤ 4 and 0.1 ≤ D50/ye ≤ 0.7.",
                 "Pool length 10·hs (≥ 3·Wo), apron 5·hs (≥ Wo); riprap 3·D50 thick on the approach, 2·D50 elsewhere.",
                 "IDCM §4.9.5: 3·D wide at the outlet with a 3:1 flare each side, side slopes no steeper than 2H:1V, rock at least 1.5 × the largest stone thick, outlet pipe ≤ 5 % for 5·D, Vp ≤ 3.33·Vc."]));
        }
        if (poolRows.Count > 0)
            tables.Add(new("Plunge pool scour (IDCM Eq. 4-9-1)", ["Pool", $"Head HT ({L})", si ? "q (m³/s/m)" : "q (cfs/ft)", $"Scour ds below TW ({L})"], poolRows, ["ds = 1.32·HT^0.225·q^0.54 (U.S. units)."]));

        var summary = new List<string>();
        if (outlets.Count > 0) summary.Add($"{outlets.Count} outlet(s): apron class and size by HEC-14 Table 10.1, riprap basin by HEC-14 §10.2, laid out to IDCM §4.9.5.");
        if (pools.Count > 0) summary.Add($"{pools.Count} plunge pool(s) by IDCM Eq. 4-9-1.");
        summary.Add("The USBR Type VI and CSU impact basins need IDCM Table 4-15 and Figure 4-17 and are not sized here.");
        return new ToolResult(title, summary, tables, msgs) { Figures = figures };
    }

    private static double Root(RootResult r, double fallback) => r.Converged && double.IsFinite(r.Root) ? r.Root : fallback;

    private static double R(double v, int d) => Math.Round(v, d) + 0.0;

    /// <summary>Basin longitudinal section: approach at the outlet invert, pool down to hs, rising back over the apron.</summary>
    private static ToolFigure BasinFigure(string name, double hs, double lp, double la, double d50, double yo, double tw, string u)
    {
        var ground = new List<(double, double)> { (-2, 0), (0, 0), (lp / 3, -hs), (lp, -hs), (lp + la, 0), (lp + la + 2, 0) };
        var rock = new List<(double, double)> { (0, -2 * d50), (lp / 3, -hs - 2 * d50), (lp, -hs - 2 * d50), (lp + la, -2 * d50) };
        var series = new List<ToolFigureSeries>
        {
            new("Finished grade (riprap surface)", ToolSeriesKind.Ground, ground),
            new("Bottom of riprap (2·D50)", ToolSeriesKind.Limit, rock),
            new("Outlet water surface", ToolSeriesKind.Water, [(-2, yo), (0, yo)]),
        };
        if (tw > 0) series.Add(new("Tailwater", ToolSeriesKind.Water, [(lp + la, tw), (lp + la + 2, tw)]));
        return new ToolFigure(string.Create(CultureInfo.InvariantCulture, $"{name}: riprap basin section"), series, $"Distance from outlet ({u})", $"Elevation above outlet invert ({u})",
            ["Schematic: the pool's upstream slope is drawn at a third of the pool length; set it from the riprap side slope (≤ 2H:1V) in the plans."]);
    }
}
