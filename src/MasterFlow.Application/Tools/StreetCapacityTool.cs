using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Street capacity and gutter spread checks, Rapid City IDCM 2012 §4.4.9–4.4.11, U.S. units.
/// Initial (10-yr) storm: spread T and depth at the curb from <see cref="GutterSection"/> (IDCM Eq. 4-4-1, composite
/// gutters by HEC-22 Eq. 4-4), checked against Table 4-6 (no curb overtopping, dry pavement by class) and, where a
/// reach ends at a cross pan, Table 4-10. Major (100-yr) storm: Manning's equation on the whole street section from
/// right-of-way to right-of-way (curbs, gutters, crown, parkways; conveyance split where n changes), checked against
/// Table 4-9 (depth over the gutter flow line: local 18 in., collector and arterial 12 in.) and the ROW line / building
/// ground line. Every reach also gets the allowable capacity at the governing limit, so inlet locations can be set
/// from it.
/// </summary>
public sealed class StreetCapacityTool : IDesignTool
{
    public string Id => "street-capacity";
    public string Name => "Street capacity and gutter spread (IDCM §4.4, HEC-22)";
    public string Description => "Checks each street reach for the 10-yr storm (spread and curb depth, Table 4-6; cross pans, Table 4-10) and the 100-yr storm (depth over the flow line, Table 4-9, with the water kept inside the right-of-way). Uniform or composite gutters (HEC-22). Reports the allowable capacity of every reach.";
    public bool UsesNetwork => false;

    private const double K = 1.486;

    private sealed record StreetClass(string Key, string Text, double Dry, double MajorDepth, bool Arterial);

    private static readonly Dictionary<string, StreetClass> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["local"] = new("local", "Local", 0, 1.5, false),
        ["collector"] = new("collector", "Collector", 12, 1.0, false),
        ["arterial2"] = new("arterial2", "Two-lane arterial", 12, 1.0, true),
        ["arterial3"] = new("arterial3", "Three-lane arterial", 24, 1.0, true),
        ["arterial4"] = new("arterial4", "Arterial, more than three lanes", 36, 1.0, true),
    };

    public string SampleInput(DesignToolContext context) => """
        # Street capacity: Rapid City IDCM 2012 §4.4.9 (10-yr, Table 4-6), §4.4.10 (100-yr, Table 4-9), §4.4.11 (Table 4-10).
        # U.S. units (ft, cfs). Slopes in ft/ft, or with a % sign (2% = 0.02).
        # Street class: local, collector, arterial2, arterial3, arterial4. Arterials with raised medians use the
        # collector/two-lane spread criterion (Table 4-6 footnote): set raised median = yes.
        street class = collector
        raised median = no
        pavement width = 36        # flow line to flow line, ft
        curb height = 0.5          # ft above the gutter flow line
        cross slope = 0.02         # Sx, pavement, ft/ft
        n = 0.016                  # Table 4-7 (asphalt 0.016; concrete gutter with asphalt 0.015)

        # Composite gutter (HEC-22): gutter width W and gutter cross slope Sw. W = 0 for a uniform cross slope.
        gutter width = 2           # ft (sample values; use the standard detail for the project)
        gutter slope = 0.0625      # Sw, ft/ft

        # 100-yr section behind each curb, out to the right-of-way line.
        parkway width = 10         # back of curb to ROW, ft
        parkway slope = 0.02       # rising away from the curb, ft/ft
        parkway n = 0.035          # grass/sidewalk mix
        building ground = 1.5      # optional: lowest building ground line (or flood-proofing) above the flow line, ft

        # Reaches in any order. Q10 = 10-yr flow in ONE gutter at the downstream end of the reach (both gutters taken
        # as equal). Q100 = 100-yr flow carried by the WHOLE street. Cross pan = yes when the reach ends at a
        # valley gutter across the intersecting street (Table 4-10). Leave a Q blank to skip that storm.
        [Reaches]
        Reach	S (ft/ft)	Q10 gutter (cfs)	Q100 street (cfs)	Cross pan
        Main St 1+00-4+50	0.012	3.2	28	no
        Main St 4+50-7+00	0.006	6.5	52	yes
        Main St 7+00-9+00	0.5%	9.0	95	no
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Street capacity and gutter spread — Rapid City IDCM §4.4.9–4.4.11, HEC-22";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-SC-001", "IDCM Eq. 4-4-1 (Ku = 0.56) and the Table 4-6/4-9 limits are in ft and cfs. Switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        string clsKey = p.Text("street class") ?? "local";
        if (!Classes.TryGetValue(clsKey, out var cls)) { msgs.Add(CalcMessage.Error("E-SC-002", $"Street class \"{clsKey}\" must be local, collector, arterial2, arterial3 or arterial4.")); cls = Classes["local"]; }
        bool median = p.Flag("raised median", false);
        double dry = median && cls.Arterial ? 12 : cls.Dry;
        if (median && !cls.Arterial) msgs.Add(CalcMessage.Info("I-SC-001", "Raised median only changes the spread criterion for arterials (Table 4-6 footnote); ignored for this class."));

        double b = p.Number("pavement width", double.NaN), h = p.Number("curb height", 0.5);
        double sx = Slope(p, "cross slope", 0.02), n = p.Number("n", 0.016);
        double w = p.Number("gutter width", 0), sw = Slope(p, "gutter slope", 0);
        double pw = p.Number("parkway width", 0), ps = Slope(p, "parkway slope", 0.02), pn = p.Number("parkway n", 0.035);
        double? bg = p.OptionalNumber("building ground");
        if (w > 0 && !p.Has("gutter slope")) msgs.Add(CalcMessage.Error("E-SC-003", "A gutter width W needs the gutter slope Sw (or set W = 0 for a uniform cross slope)."));
        var sec = new GutterSection(sx, n, Math.Max(w, 0), sw);
        foreach (var e in sec.Problems()) msgs.Add(CalcMessage.Error("E-SC-004", e));
        if (!(b > 0)) msgs.Add(CalcMessage.Error("E-SC-005", "Pavement width (flow line to flow line) must be greater than zero."));
        else if (w >= b / 2) msgs.Add(CalcMessage.Error("E-SC-006", $"Gutter width {w} ft must be less than half the pavement width ({b / 2} ft)."));
        if (!(h > 0)) msgs.Add(CalcMessage.Error("E-SC-007", "Curb height must be greater than zero."));
        if (pw < 0 || double.IsNaN(pw)) msgs.Add(CalcMessage.Error("E-SC-008", "Parkway width cannot be negative (0 = right-of-way at the back of curb)."));
        if (ps < 0 || double.IsNaN(ps)) msgs.Add(CalcMessage.Error("E-SC-009", "Parkway slope must rise away from the curb (≥ 0). Ground falling toward buildings is outside this check: water over the curb leaves the street."));
        if (!(pn > 0)) msgs.Add(CalcMessage.Error("E-SC-010", "Parkway n must be greater than zero."));
        if (bg is { } g0 && !(g0 > 0)) msgs.Add(CalcMessage.Error("E-SC-011", "Building ground line must be above the gutter flow line (> 0 ft)."));
        if (sw > 0 && w == 0) msgs.Add(CalcMessage.Info("I-SC-002", "Gutter slope ignored: gutter width is 0, so the section is a uniform cross slope."));
        if (w > 0 && sw == sx) msgs.Add(CalcMessage.Info("I-SC-003", "Gutter slope equals the cross slope: the section is uniform."));
        foreach (var (key, v) in new[] { ("cross slope", sx), ("gutter slope", sw), ("parkway slope", ps) })
            if (v >= 0.25) msgs.Add(CalcMessage.Warning("W-SC-001", $"{key} = {v} ft/ft is very steep; enter slopes in ft/ft or with a % sign."));
        var reaches = p.Rows("Reaches");
        if (reaches.Count == 0) msgs.Add(CalcMessage.Error("E-SC-012", "No reaches: add rows under [Reaches]."));
        if (p.Errors.Count > 0 || msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], [.. p.Errors, .. msgs]);

        double half = b / 2, a = sec.Depression;
        double crown = sec.IsComposite ? w * sw + (half - w) * sx : half * sx;
        double tAllow = (b - dry) / 2, tCurb = sec.SpreadAtDepth(h);
        if (!(tAllow > 0)) return new ToolResult(title, [], [], [.. msgs, CalcMessage.Error("E-SC-013", $"A {b} ft pavement cannot leave {dry} ft free of water (Table 4-6). Check the class and width.")]);
        var limits = new List<(double T, string Why)> { (tAllow, cls.Key == "local" ? "spread to crown" : $"{dry:0} ft dry"), (tCurb, "curb full") };
        if (half < tAllow) limits.Add((half, "crown"));
        var (tGov, govWhy) = limits.MinBy(x => x.T);
        if (crown > h) msgs.Add(CalcMessage.Info("I-SC-004", $"The crown ({crown:F3} ft above the flow line) is higher than the curb ({h:F2} ft): the curb overtops before flow can cross the crown."));

        // 100-yr street section, ROW to ROW, flow line at elevation 0.
        double rowTop = h + pw * ps;
        var pts = new List<StationPoint>();
        if (pw > 0) pts.Add(new(0, rowTop, pn, "ROW"));
        pts.Add(new(pw, h, n, "Top of curb")); pts.Add(new(pw, 0, n, "Flow line"));
        if (sec.IsComposite) pts.Add(new(pw + w, w * sw, n, "Gutter lip"));
        pts.Add(new(pw + half, crown, n, "Crown"));
        if (sec.IsComposite) pts.Add(new(pw + b - w, w * sw, n, "Gutter lip"));
        pts.Add(new(pw + b, 0, n, "Flow line")); pts.Add(new(pw + b, h, pn, "Top of curb"));
        if (pw > 0) pts.Add(new(2 * pw + b, rowTop, null, "ROW"));
        var geo = new IrregularGeometry(new IrregularSection(pts));
        double Q100(double y, double s) => geo.Conveyance(y, n, K) * Math.Sqrt(s);

        var minorRows = new List<IReadOnlyList<object?>>();
        var majorRows = new List<IReadOnlyList<object?>>();
        int minorFail = 0, majorFail = 0;
        double worstT = -1, worstD = -1;
        foreach (var r in reaches)
        {
            string name = r[0].Length > 0 ? r[0] : $"Reach {minorRows.Count + majorRows.Count + 1}", where = $"Reach {name}";
            double? s = SlopeCell(p, r, 1, "S", where);
            double? q10 = p.Cell(r, 2, "Q10", where), q100 = p.Cell(r, 3, "Q100", where);
            string pan = (r.Length > 4 ? r[4] : "").Trim().ToLowerInvariant();
            bool crossPan = pan is "yes" or "y" or "true" or "1";
            if (s is not > 0) { msgs.Add(CalcMessage.Error("E-SC-014", $"{where}: longitudinal slope S must be greater than zero.")); continue; }
            if (s >= 0.25) msgs.Add(CalcMessage.Warning("W-SC-001", $"{where}: S = {s} ft/ft is very steep; enter slopes in ft/ft or with a % sign."));
            if (q10 is < 0 || q100 is < 0) { msgs.Add(CalcMessage.Error("E-SC-015", $"{where}: flows cannot be negative.")); continue; }
            if (q10 is null && q100 is null) { msgs.Add(CalcMessage.Warning("W-SC-002", $"{where}: no Q10 or Q100, nothing to check.")); continue; }
            if (q10 is { } q && q100 is { } qq && qq < q) msgs.Add(CalcMessage.Warning("W-SC-003", $"{where}: Q100 ({qq} cfs, whole street) is less than Q10 ({q} cfs, one gutter). Check the entries."));

            if (q10 is { } qm)
            {
                double t = sec.Spread(qm, s.Value), d = sec.Depth(t), v = t > 0 ? qm / sec.Area(t) : 0;
                double qAllow = sec.Capacity(tGov, s.Value);
                var fails = new List<string>();
                if (t > tAllow + 1e-9) fails.Add(cls.Key == "local" ? $"spread {t:F1} ft passes the crown ({tAllow:F1} ft)" : $"spread {t:F1} ft > {tAllow:F1} ft allowed ({dry:0} ft dry)");
                if (t > half + 1e-9 && cls.Key != "local") fails.Add($"spread passes the crown ({half:F1} ft): Eq. 4-4-1 no longer applies");
                if (d > h + 1e-9) fails.Add($"depth {d:F2} ft overtops the {h:F2} ft curb");
                string panCheck = "—";
                if (crossPan)
                {
                    if (cls.Arterial) { panCheck = "FAILS: no cross flow on arterials"; fails.Add("cross pan on an arterial (Table 4-10: no cross flow)"); }
                    else if (d > 0.5 + 1e-9) { panCheck = $"FAILS: {d * 12:F1} in. > 6 in."; fails.Add($"cross-pan depth {d * 12:F1} in. > 6 in. (Table 4-10)"); }
                    else panCheck = $"OK ({d * 12:F1} in. ≤ 6 in.)";
                }
                if (fails.Count > 0) { minorFail++; msgs.Add(CalcMessage.Warning("W-SC-010", $"{where}, 10-yr: {string.Join("; ", fails)}.")); }
                if (t > worstT) worstT = t;
                minorRows.Add([name, s.Value, qm, t, d, d * 12, v, sec.W > 0 ? sec.FrontalRatio(t) : double.NaN, tGov, qAllow, qm / qAllow, panCheck, fails.Count == 0 ? "OK" : "FAILS: " + string.Join("; ", fails)]);
            }

            if (q100 is { } qM)
            {
                double y = SolveDepth(qM, yy => Q100(yy, s.Value));
                if (double.IsNaN(y)) { msgs.Add(CalcMessage.Error("E-SC-016", $"{where}: {qM} cfs needs more than 10 ft of depth in the street section.")); continue; }
                double area = geo.Area(y), top = geo.TopWidth(y), v = area > 0 ? qM / area : 0;
                double yAllow = cls.MajorDepth, qAtLimit = Q100(yAllow, s.Value), qAtRow = Q100(rowTop, s.Value);
                var fails = new List<string>();
                if (y > yAllow + 1e-9) fails.Add($"depth {y * 12:F1} in. > {yAllow * 12:0} in. over the flow line (Table 4-9)");
                if (y > rowTop + 1e-9) fails.Add(pw > 0 ? $"water {y - rowTop:F2} ft above the ROW line: leaves the right-of-way" : $"water {y - h:F2} ft above the curb with no parkway entered: leaves the street");
                if (bg is { } gl && y >= gl - 1e-9) fails.Add($"reaches the building ground line ({gl:F2} ft) — buildings must be flood-proofed");
                if (fails.Count > 0) { majorFail++; msgs.Add(CalcMessage.Warning("W-SC-020", $"{where}, 100-yr: {string.Join("; ", fails)}.")); }
                if (y > worstD) worstD = y;
                majorRows.Add([name, s.Value, qM, y, y * 12, y - crown, top, v, yAllow * 12, qAtLimit, qAtRow, Math.Min(qAtLimit, qAtRow), fails.Count == 0 ? "OK" : "FAILS: " + string.Join("; ", fails)]);
            }
        }
        msgs.InsertRange(0, p.Errors);

        string secText = sec.IsComposite
            ? $"Composite gutter W = {w:0.##} ft on Sw = {sw}, depression a = {a:F3} ft; pavement Sx = {sx}; n = {n}."
            : $"Uniform cross slope Sx = {sx}; n = {n}.";
        var tables = new List<ToolTable>();
        if (minorRows.Count > 0)
            tables.Add(new("10-yr storm: spread and curb depth (Table 4-6)",
                ["Reach", "S (ft/ft)", "Q10 gutter (cfs)", "Spread T (ft)", "Depth at curb (ft)", "Depth (in.)", "V (fps)", "Eo", "T governing (ft)", "Q allowed (cfs)", "Q / Q allowed", "Cross pan (Table 4-10)", "Check"], minorRows,
            [
                $"{cls.Text}{(median && cls.Arterial ? " with raised median (collector criterion)" : "")}: {(cls.Key == "local" ? "flow may spread to the crown" : $"{dry:0} ft of pavement free of water")}; no curb overtopping. Pavement {b:0.##} ft flow line to flow line, curb {h:F2} ft. {secText}",
                $"Allowed spread {tAllow:F2} ft; curb-full spread {tCurb:F2} ft; crown at {half:F2} ft. Governing: {tGov:F2} ft ({govWhy}). Q allowed = gutter capacity at the governing spread on the reach slope.",
                sec.IsComposite ? "Spread by HEC-22 Eq. 4-4 (Q = Qs/(1 − Eo)) with the IDCM Eq. 4-4-1 coefficients (0.56, 1.67, 2.67); V = Q/A. Eo = share of the flow over the gutter width." : "Spread by IDCM Eq. 4-4-1; V by Eq. 4-4-2 (= Q/A).",
                "Cross pan depth is taken as the gutter depth at the curb arriving at the pan (Table 4-10: 6 in. for local and collector; arterials allow no cross flow).",
            ]));
        if (majorRows.Count > 0)
            tables.Add(new("100-yr storm: depth over the flow line (Table 4-9)",
                ["Reach", "S (ft/ft)", "Q100 street (cfs)", "Depth over flow line (ft)", "Depth (in.)", "Above crown (ft)", "Top width (ft)", "V mean (fps)", "Depth allowed (in.)", "Q at depth limit (cfs)", "Q at ROW line (cfs)", "Q allowed (cfs)", "Check"], majorRows,
            [
                $"Manning's equation (k = 1.486) on the whole street section below, ROW to ROW; conveyance summed by subarea where n changes (street n = {n}, parkway n = {pn}). Water below the crown runs in both gutters with equal depth.",
                $"Table 4-9: {cls.Text.ToLowerInvariant()} street, {cls.MajorDepth * 12:0} in. over the gutter flow line; buildings not inundated at the ground line. ROW line {rowTop:F2} ft above the flow line{(bg is { } gg ? $"; building ground line {gg:F2} ft" : "; no building ground line entered")}.",
            ]));

        // Capacity curve (cf. IDCM Fig. 4-2 to 4-4): Q/√S against spread for this gutter.
        var curve = new List<IReadOnlyList<object?>>();
        double step = Math.Max(1, Math.Round(half / 12));
        var ts = new SortedSet<double>();
        for (double t = step; t < half - 1e-9; t += step) ts.Add(t);
        ts.Add(half); ts.Add(Math.Round(tGov, 2)); if (sec.W > 0) ts.Add(sec.W);
        foreach (var t in ts)
            curve.Add([t, sec.Depth(t), sec.Area(t), sec.Capacity(t, 1), sec.W > 0 ? sec.FrontalRatio(t) : double.NaN, Math.Abs(t - Math.Round(tGov, 2)) < 1e-9 ? $"governing ({govWhy})" : Math.Abs(t - half) < 1e-9 ? "crown" : ""]);
        tables.Add(new("Gutter conveyance by spread (one gutter)", ["Spread T (ft)", "Depth at curb (ft)", "Area (ft²)", "Q / √S (cfs)", "Eo", "Note"], curve,
            ["Q = (Q/√S) × √S for any longitudinal slope. Compare IDCM Figs. 4-2 to 4-4."]));
        tables.Add(new("Street section (100-yr)", ["Station (ft)", "Elevation above flow line (ft)", "n to next point", "Point"],
            pts.Select(x => (IReadOnlyList<object?>)[x.Station, x.Elevation, x.ManningN is { } nn ? nn : double.NaN, x.Description]).ToList()));

        var summary = new List<string>
        {
            $"{cls.Text} street, {b:0.##} ft pavement. 10-yr: {minorRows.Count} reach(es) checked, {minorFail} fail{(worstT >= 0 ? $"; widest spread {worstT:F1} ft (allowed {tAllow:F1} ft)" : "")}.",
            $"100-yr: {majorRows.Count} reach(es) checked, {majorFail} fail{(worstD >= 0 ? $"; deepest {worstD * 12:F1} in. over the flow line (allowed {cls.MajorDepth * 12:0} in.)" : "")}.",
        };

        // Figure: the street section with the widest 10-yr spread and the deepest 100-yr water.
        var ground = pts.Select(x => (x.Station, x.Elevation)).ToList();
        var series = new List<ToolFigureSeries> { new("Street section", ToolSeriesKind.Ground, ground) };
        if (worstD > 0) foreach (var poly in WaterPolygons(ground, worstD)) series.Add(new($"100-yr, {worstD * 12:F1} in.", ToolSeriesKind.Water, poly));
        if (worstT > 0) foreach (var poly in WaterPolygons(ground, sec.Depth(worstT))) series.Add(new($"10-yr, T = {worstT:F1} ft", ToolSeriesKind.Water, poly));
        double x0 = ground[0].Station, x1 = ground[^1].Station;
        series.Add(new($"100-yr limit {cls.MajorDepth * 12:0} in.", ToolSeriesKind.Limit, [(x0, cls.MajorDepth), (x1, cls.MajorDepth)]));
        series.Add(new($"10-yr allowed spread {tAllow:F1} ft", ToolSeriesKind.Limit, [(pw + tAllow, 0), (pw + tAllow, h)]));
        series.Add(new("", ToolSeriesKind.Limit, [(pw + b - tAllow, 0), (pw + b - tAllow, h)]));
        var fig = new ToolFigure("Street section with the governing water surfaces", series, "Station (ft)", "Elevation above gutter flow line (ft)",
            ["Vertical scale exaggerated. Light fill: deepest 100-yr reach; darker fill: widest 10-yr spread (both gutters equal)."]);

        return new ToolResult(title, summary, tables, msgs) { Figures = [fig] };
    }

    /// <summary>Lowest depth (0–10 ft) at which <paramref name="q"/>(y) reaches the flow; NaN when it is not reached.</summary>
    public static double SolveDepth(double flow, Func<double, double> q)
    {
        if (!(flow > 0)) return 0;
        double lo = 0, hi = 0;
        for (int i = 1; i <= 1000; i++) { hi = i * 0.01; if (q(hi) >= flow) break; lo = hi; if (i == 1000) return double.NaN; }
        for (int i = 0; i < 100 && hi - lo > 1e-12; i++) { double m = 0.5 * (lo + hi); if (q(m) < flow) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    /// <summary>Closed water polygons where the ground polyline is below level <paramref name="y"/> (one per pool).</summary>
    public static List<List<(double X, double Y)>> WaterPolygons(IReadOnlyList<(double X, double Y)> g, double y)
    {
        var polys = new List<List<(double X, double Y)>>();
        List<(double X, double Y)>? cur = null;
        for (int i = 0; i < g.Count; i++)
        {
            var pt = g[i];
            if (pt.Y < y) { cur ??= [(pt.X, y)]; cur.Add(pt); }
            if (i == g.Count - 1) break;
            var q = g[i + 1];
            if ((pt.Y < y) != (q.Y < y) && pt.Y != q.Y)
            {
                double x = pt.X + (y - pt.Y) / (q.Y - pt.Y) * (q.X - pt.X);
                if (pt.Y < y) { cur!.Add((x, y)); polys.Add(cur); cur = null; }
                else cur = [(x, y)];
            }
        }
        if (cur is not null) { cur.Add((g[^1].X, y)); polys.Add(cur); }
        return polys;
    }

    /// <summary>A slope parameter in ft/ft; "2%" means 0.02.</summary>
    private static double Slope(ToolInput p, string key, double fallback)
    {
        double v = p.Number(key, fallback);
        return p.Text(key) is { } t && t.Trim().EndsWith('%') ? v / 100 : v;
    }

    private static double? SlopeCell(ToolInput p, string[] row, int i, string what, string where)
    {
        double? v = p.Cell(row, i, what, where);
        return v is { } x && row[i].Trim().EndsWith('%') ? x / 100 : v;
    }
}
