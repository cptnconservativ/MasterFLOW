using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Sag inlet ponding with clogging (Rapid City IDCM 2012 §4.5.3–4.5.4, Tables 4-11 and 4-12; HEC-22 sag inlet equations):
/// ponded depth, spread and ponded length at a sag for the 10-yr and 100-yr flows with the inlet clean, at the Table 4-11
/// clogging and fully clogged, with flanking inlets placed by Eq. 4-5-2 / Table 4-12 acting in relief and an optional
/// overland overflow path; checked against Tables 4-6 and 4-9 and the curb and right-of-way.
/// </summary>
public sealed class SagPondingTool : IDesignTool
{
    public string Id => "sag-ponding";
    public string Name => "Sag inlet ponding and clogging (flanking inlets, overflow)";
    public string Description => "Ponded depth, spread and ponded length at a sag inlet for the 10-yr and 100-yr flows with the inlet clean, at the IDCM Table 4-11 clogging (grate 40 %, combination 33 %, curb 0 %) and fully clogged; flanking inlets at X = (200·d·K)^0.5 (Eq. 4-5-2, Table 4-12) relieve the low point once the water rises above them, and an overland overflow path takes the rest. Checks spread and curb depth (Table 4-6), 100-yr depth (Table 4-9), the right-of-way, three inlets on arterials and the 0.3 % gradient within 50 ft of the low point.";
    public bool UsesNetwork => false;

    private static readonly Dictionary<string, (double Dry, double Depth100, string Text)> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["local"] = (0, 1.5, "Local: 10-yr spread may reach the crown; 100-yr depth ≤ 18 in. over the flow line"),
        ["collector"] = (12, 1.0, "Collector / two-lane arterial: 12 ft dry (10-yr); 100-yr depth ≤ 12 in."),
        ["arterial3"] = (24, 1.0, "Three-lane arterial: 24 ft dry (10-yr); 100-yr depth ≤ 12 in."),
        ["arterial4"] = (36, 1.0, "Arterial, more than three lanes: 36 ft dry (10-yr); 100-yr depth ≤ 12 in."),
    };

    public string SampleInput(DesignToolContext context) => """
        # Sag inlet ponding with clogging, Rapid City IDCM 2012 §4.5.3–4.5.4 and HEC-22, U.S. units (ft, cfs).
        street class = local          # local, collector, arterial3, arterial4
        pavement width = 36           # ft, flow line to flow line (crown at the middle)
        Sx = 0.02                     # pavement cross slope
        gutter width = 2              # ft, composite gutter (0 = uniform cross slope)
        gutter slope = 0.0833         # Sw
        curb height = 0.5             # ft
        # Sag vertical curve: approach grades (%) and curve length (ft); K = L/A (Eq. 4-5-2)
        G1 = -1.5
        G2 = 2.0
        curve length = 150
        # Flow reaching the sag on this side, including bypass from upstream inlets (cfs)
        Q10 = 6
        Q100 = 16
        # Overland overflow: depth over the flow line at which water leaves the street (low point of the right-of-way,
        # e.g. the back of walk at a driveway), and the length of that overflow path (broad-crested weir, C = 3.0).
        overflow depth = 0.9
        overflow length = 20

        # Low-point inlet. Type: curb, grate or combination. L = length (grate), W = grate width or curb depression
        # width, Area = grate clear opening (ft²), h = curb opening height, a = local depression (in.), Lc = curb opening
        # length of a combination inlet. Clog % blank = IDCM Table 4-11 (grate 40, combination 33, curb 0).
        [Inlet]
        type = combination
        L = 3
        W = 2
        Area = 3.2
        h = 0.5
        a = 2
        Lc = 3

        # Flanking inlets, one each side, at the distance where the gutter is "flanking depth" above the low point
        # (IDCM §4.5.4(2): about 0.2 ft; required on arterials). Delete this section for none.
        [Flanking]
        type = curb
        L = 5
        h = 0.5
        a = 2
        flanking depth = 0.2
        """;

    private sealed record InletDef(string Type, double L, double W, double Area, double H, double A, double Lc, double? Clog);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sag inlet ponding and clogging";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-SP-001", "The IDCM limits and HEC-22 U.S. coefficients are in ft and cfs. Switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        string cls = (p.Text("street class") ?? "local").ToLowerInvariant();
        if (!Classes.TryGetValue(cls, out var c)) { msgs.Add(CalcMessage.Error("E-SP-002", $"Street class \"{cls}\" must be local, collector, arterial3 or arterial4.")); c = Classes["local"]; }
        double width = p.Number("pavement width", double.NaN, "Pavement width"), sx = p.Number("Sx", 0.02), gw = p.Number("gutter width", 0), gs = p.Number("gutter slope", 0);
        double curb = p.Number("curb height", 0.5, "Curb height");
        double g1 = p.Number("G1", double.NaN, "G1"), g2 = p.Number("G2", double.NaN, "G2"), lvc = p.Number("curve length", double.NaN, "Curve length");
        double q10 = p.Number("Q10", double.NaN, "Q10"), q100 = p.Number("Q100", double.NaN, "Q100");
        double? ovD = p.OptionalNumber("overflow depth"), ovL = p.OptionalNumber("overflow length");
        if (!(width > 0)) msgs.Add(CalcMessage.Error("E-SP-003", "Give the pavement width (flow line to flow line)."));
        if (!(sx > 0) || !(curb > 0)) msgs.Add(CalcMessage.Error("E-SP-004", "Sx and the curb height must be greater than zero."));
        var gutter = new GutterSection(sx, 0.016, gw, gs);
        foreach (var prob in gutter.Problems()) msgs.Add(CalcMessage.Error("E-SP-005", prob));
        if (!(q10 >= 0) && !(q100 >= 0)) msgs.Add(CalcMessage.Error("E-SP-006", "Give Q10 and/or Q100, the flow reaching the sag on this side (with upstream bypass)."));
        bool curve = double.IsFinite(g1) && double.IsFinite(g2) && lvc > 0;
        if (curve && !(g2 - g1 > 0)) msgs.Add(CalcMessage.Error("E-SP-007", "A sag needs G2 − G1 > 0 (e.g. G1 = −1.5 %, G2 = +2.0 %)."));
        var main = ReadInlet(p, "Inlet", msgs, required: true)!;
        var flank = p.HasSection("Flanking") ? ReadInlet(p, "Flanking", msgs, required: true) : null;
        double dF = p.Number("Flanking.flanking depth", 0.2, "Flanking depth");
        if (flank is not null && !curve) msgs.Add(CalcMessage.Error("E-SP-008", "Flanking inlets need the sag curve (G1, G2, curve length) to place them."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double aGrade = curve ? g2 - g1 : double.NaN, k = curve ? lvc / aGrade : double.NaN;
        double xFlank = flank is not null ? Math.Sqrt(200 * dF * k) : double.NaN;
        double half = width / 2, tAllow = cls == "local" ? half : (width - c.Dry) / 2;
        double crownD = gutter.Depth(half);

        Func<double, double> Cap(InletDef i, double clog)
        {
            double aCont = gutter.Depression, aLoc = i.A / 12, aTot = aCont + aLoc;
            bool depressed = aTot > 0 && (i.W > 0 || gutter.W > 0);
            double wDep = i.W > 0 ? i.W : gutter.W;
            double lEff = i.L * (1 - clog), wEff = i.W * (1 - clog), area = i.Area * (1 - clog);
            double GrateDepth(double d) => Math.Max(0, d + aLoc - (gutter.IsComposite && wEff <= gutter.W ? gutter.Sw : sx) * wEff / 2);
            double GrateCap(double d) => InletHydraulics.GrateSag(GrateDepth(d), lEff, wEff, area);
            bool WeirRegime(double d) => InletHydraulics.GrateWeir(GrateDepth(d), lEff, wEff) <= InletHydraulics.GrateOrifice(GrateDepth(d), area);
            return i.Type switch
            {
                "curb" => d => d <= 0 || clog >= 1 ? 0 : InletHydraulics.CurbSag(Math.Max(0, d - aCont), d + aLoc, i.L * (1 - clog), wDep, i.H, depressed),
                "grate" => d => d <= 0 || clog >= 1 ? 0 : GrateCap(d),
                _ => d => d <= 0 || clog >= 1 ? 0 : WeirRegime(d) ? GrateCap(d) : GrateCap(d) + InletHydraulics.CurbOrifice(d + aLoc, i.Lc * (1 - clog), i.H),
            };
        }
        double Table411(InletDef i) => i.Clog ?? (i.Type switch { "grate" => 0.40, "combination" => 0.33, _ => 0 });

        var scen = new List<(string Name, double MainClog, double FlankClog)>
        {
            ("Clean", 0, 0),
            ($"Table 4-11 clogging ({Table411(main) * 100:0.#} %{(flank is not null ? $", flanking {Table411(flank) * 100:0.#} %" : "")})", Table411(main), flank is null ? 0 : Table411(flank)),
            ("Low-point inlet fully clogged", 1, flank is null ? 0 : Table411(flank)),
        };
        double depth100 = c.Depth100;
        var rows = new List<IReadOnlyList<object?>>();
        var chartSeries = new List<ChartSeries>();
        int fails = 0, warns = 0;
        foreach (var (storm, q) in new[] { ("10-yr", q10), ("100-yr", q100) })
        {
            if (!(q >= 0)) continue;
            for (int si = 0; si < scen.Count; si++)
            {
                var (sname, mc, fc) = scen[si];
                var capMain = Cap(main, mc);
                var capFlank = flank is null ? null : Cap(flank, fc);
                double Over(double d) => ovD is { } od && ovL is > 0 && d > od ? 3.0 * ovL.Value * Math.Pow(d - od, 1.5) : 0;
                double Flanks(double d) => capFlank is null || d <= dF ? 0 : 2 * capFlank(d - dF);
                double Total(double d) => capMain(d) + Flanks(d) + Over(d);
                double d = InletHydraulics.SolveDepth(q, Total);
                if (double.IsNaN(d))
                {
                    rows.Add([storm, sname, q, "> 5 ft", null, null, null, null, null, null, "FAILS: no outlet for the flow (add flanking inlets or an overflow path)"]);
                    msgs.Add(CalcMessage.Warning("W-SP-001", $"{storm}, {sname}: the sag cannot pass {q:G4} cfs below 5 ft of ponding; give an overflow depth and length or flanking inlets."));
                    fails++;
                    continue;
                }
                double qm = Math.Min(q, capMain(d)), qf = Math.Min(q - qm, Flanks(d)), qo = Math.Max(0, q - qm - qf);
                double t = d >= crownD ? width : gutter.SpreadAtDepth(d);
                double pondLen = curve ? 2 * Math.Sqrt(200 * k * d) : double.NaN;
                var issues = new List<string>();
                bool design = si == 1, clogged = si == 2;
                if (storm == "10-yr" && si <= 1)
                {
                    if (t > tAllow + 1e-9) issues.Add(cls == "local" ? $"spread {t:F1} ft crosses the crown ({half:F1} ft)" : $"spread {t:F1} ft > {tAllow:F1} ft ({c.Dry:0} ft dry)");
                    if (d > curb + 1e-9) issues.Add($"depth {d:F2} ft overtops the {curb:F2} ft curb");
                }
                if (storm == "100-yr" && si <= 1)
                {
                    if (d > depth100 + 1e-9) issues.Add($"depth {d * 12:F1} in. > {depth100 * 12:F0} in. (Table 4-9)");
                    if (ovD is { } od && d > od + 1e-9) issues.Add($"water leaves the right-of-way (above {od:F2} ft)");
                }
                if (clogged)
                {
                    if (d > depth100 + 1e-9 || (ovD is { } od2 && d > od2 + 1e-9)) issues.Add(ovD is not null && ovL is > 0 ? $"ponds {d:F2} ft deep; {qo:F2} cfs leaves by the overflow path" : $"ponds {d:F2} ft deep with the low-point inlet clogged");
                }
                string status = issues.Count == 0 ? "OK" : (clogged ? "REVIEW: " : "FAILS: ") + string.Join("; ", issues);
                if (issues.Count > 0) { if (clogged) warns++; else fails++; }
                rows.Add([storm, sname, q, Math.Round(d, 3), Math.Round(d * 12, 1), Math.Round(t, 1), Math.Round(qm, 2), flank is null ? null : Math.Round(qf, 2), ovD is null ? null : Math.Round(qo, 2), curve ? Math.Round(pondLen, 0) : null, status]);
                if (issues.Count > 0 && !clogged) msgs.Add(CalcMessage.Warning("W-SP-002", $"{storm}, {sname}: {string.Join("; ", issues)}."));
                if (issues.Count > 0 && clogged) msgs.Add(CalcMessage.Warning("W-SP-003", $"{storm}, low-point inlet clogged: {string.Join("; ", issues)}. IDCM §4.5.4(2): flanking inlets relieve the low point if it clogs."));
            }
        }

        // Rating curves: inflow vs ponded depth for each scenario.
        double dMax = Math.Max(curb, Math.Max(depth100, ovD ?? 0)) * 1.2;
        for (int si = 0; si < scen.Count; si++)
        {
            var (sname, mc, fc) = scen[si];
            var cm = Cap(main, mc); var cf = flank is null ? null : Cap(flank, fc);
            var pts = new List<(double, double)>();
            for (int j = 1; j <= 40; j++)
            {
                double d = dMax * j / 40;
                double qq = cm(d) + (cf is null || d <= dF ? 0 : 2 * cf(d - dF)) + (ovD is { } od && ovL is > 0 && d > od ? 3.0 * ovL.Value * Math.Pow(d - od, 1.5) : 0);
                pts.Add((qq, d));
            }
            chartSeries.Add(new ChartSeries(sname, pts, Dashed: si == 0));
        }
        var lines = new List<ChartLine> { new("Curb", curb), new($"Table 4-9 ({depth100 * 12:0} in.)", depth100) };
        if (ovD is { } ov) lines.Add(new("Overflow / ROW", ov));
        if (crownD < dMax) lines.Add(new("Crown", crownD));
        var charts = new List<ToolChart> { new("Sag rating: flow vs ponded depth at the curb", "Flow passed (cfs)", "Depth at curb (ft)", chartSeries, lines) };

        // Sag geometry checks.
        var geo = new List<IReadOnlyList<object?>>();
        if (curve)
        {
            geo.Add(["A = G2 − G1", $"{aGrade:F2} %"]);
            geo.Add(["K = L / A (Eq. 4-5-2)", $"{k:F1}"]);
            double g50 = aGrade * Math.Min(50, lvc / 2) / lvc;
            geo.Add(["Gradient 50 ft from the low point", $"{g50:F2} %"]);
            if (g50 < 0.3 - 1e-9) msgs.Add(CalcMessage.Info("I-SP-001", $"The gradient 50 ft from the low point is {g50:F2} %, below the 0.3 % AASHTO recommends (IDCM §4.5.4(2)); check spread on the flat approaches."));
            if (k > 167) msgs.Add(CalcMessage.Info("I-SP-002", $"K = {k:F0} is above the largest value of Table 4-12 (167, AASHTO); the sag is very flat."));
            if (flank is not null)
            {
                geo.Add([$"Flanking inlets X = (200·d·K)^0.5, d = {dF:G3} ft", $"{xFlank:F1} ft each side of the low point"]);
            }
            for (int j = 1; j <= 8; j++) { double dd = 0.1 * j; geo.Add([$"Ponded length at {dd:F1} ft depth (2·X)", $"{2 * Math.Sqrt(200 * dd * k):F0} ft"]); }
        }
        if (flank is null && cls.StartsWith("arterial", StringComparison.Ordinal))
            msgs.Add(CalcMessage.Warning("W-SP-004", "IDCM §4.5.4(2): sags on arterial streets need three inlets, the low point and one each side about 0.2 ft higher. Add a [Flanking] section."));
        if (flank is null) msgs.Add(CalcMessage.Info("I-SP-003", "No flanking inlets: with the low-point inlet clogged, the sag drains only by the overflow path."));
        if (crownD < curb && rows.Any(r => r[3] is double dd && dd > crownD)) msgs.Add(CalcMessage.Info("I-SP-004", $"Ponding above the crown ({crownD:F2} ft over the flow line) spreads across the whole street; both sides of the sag share the inlets on both sides."));

        var tables = new List<ToolTable>
        {
            new("Ponding at the sag", ["Storm", "Inlet condition", "Q (cfs)", "Depth at curb (ft)", "Depth (in.)", "Spread (ft)", "Low-point inlet (cfs)", "Flanking inlets (cfs)", "Overflow (cfs)", "Ponded length (ft)", "Check"], rows,
                [$"{c.Text}. Pavement {width:G4} ft, allowed 10-yr spread {tAllow:F1} ft; curb {curb:F2} ft; crown {crownD:F2} ft above the flow line.",
                 "Low-point inlet by HEC-22 sag equations (curb weir/orifice/transition, grate weir/orifice, combination). Clogging reduces grate length, width and open area and the curb opening length (IDCM Table 4-11).",
                 "Flanking inlets (one each side) start taking flow once the pond rises above them and act as sag inlets on the depth over them. Overflow = 3.0·L·(d − d_overflow)^1.5.",
                 "Checks apply to the clean and Table 4-11 cases; the fully clogged case is a resilience check (REVIEW)."]),
        };
        if (geo.Count > 0) tables.Add(new("Sag curve (IDCM Eq. 4-5-2, Table 4-12)", ["Item", "Value"], geo, ["X = (200·d·K)^0.5 with d in feet (the values of Table 4-12)."]));

        var summary = new List<string>
        {
            $"Sag on a {cls} street: low-point {main.Type} inlet{(flank is not null ? $", flanking {flank.Type} inlets at {xFlank:F0} ft each side" : ", no flanking inlets")}{(ovD is not null ? $", overflow at {ovD:F2} ft" : "")}.",
            fails == 0 ? "The 10-yr and 100-yr criteria are met with the inlets clean and at the Table 4-11 clogging." : $"{fails} criterion check(s) fail.",
            warns == 0 ? "With the low-point inlet fully clogged the sag still drains within the limits." : "With the low-point inlet fully clogged the limits are exceeded (see REVIEW rows).",
        };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static InletDef? ReadInlet(ToolInput p, string sec, List<CalcMessage> msgs, bool required)
    {
        string type = (p.Text($"{sec}.type") ?? "").ToLowerInvariant();
        if (type is not ("curb" or "grate" or "combination")) { msgs.Add(CalcMessage.Error("E-SP-010", $"[{sec}] type must be curb, grate or combination.")); return null; }
        double l = p.Number($"{sec}.L", double.NaN, $"[{sec}] L"), w = p.Number($"{sec}.W", 0), area = p.Number($"{sec}.Area", 0), h = p.Number($"{sec}.h", 0), a = p.Number($"{sec}.a", 0);
        double lc = p.Number($"{sec}.Lc", l);
        double? clog = p.OptionalNumber($"{sec}.clog") is { } cl ? cl / 100 : null;
        if (!(l > 0)) msgs.Add(CalcMessage.Error("E-SP-011", $"[{sec}] needs the opening length L."));
        if (type is "grate" or "combination" && (!(w > 0) || !(area > 0))) msgs.Add(CalcMessage.Error("E-SP-012", $"[{sec}] a grate needs its width W and clear opening Area."));
        if (type is "curb" or "combination" && !(h > 0)) msgs.Add(CalcMessage.Error("E-SP-013", $"[{sec}] a curb opening needs its height h."));
        if (clog is < 0 or > 1) msgs.Add(CalcMessage.Error("E-SP-014", $"[{sec}] clog must be 0–100 %."));
        return new InletDef(type, l, w, area, h, a, lc, clog);
    }
}
