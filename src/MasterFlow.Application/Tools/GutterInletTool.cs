using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Gutter flow and inlet interception, U.S. units. Gutter: IDCM 2012 Eq. 4-4-1 Q = (0.56/n) Sx^1.67 S^0.5 T^2.67 and
/// Eq. 4-4-2 V = (1.12/n) S^0.5 Sx^0.67 T^0.67 (exponents as printed). Inlets (IDCM §4.5.3 requires HEC-22): curb
/// opening on grade LT = 0.6 Q^0.42 S^0.3 (1/(n Se))^0.6, E = 1 − (1 − L/LT)^1.8; grate on grade E = Rf Eo + Rs (1 − Eo)
/// with Eo = 1 − (1 − W/T)^2.67, Rf = 1 − 0.09 (V − Vo), Rs = 1/(1 + 0.15 V^1.8 / (Sx L^2.3)); sag inlets by the lesser of
/// weir and orifice flow. Clogging per IDCM Table 4-11 (grate 40 %, combination 33 % in a sag but 40 % on grade where it acts as a grate, curb 0 %) reduces length, perimeter and area.
/// </summary>
public static class GutterHydraulics
{
    public const double G = 32.174;

    /// <summary>Spread T (ft) for gutter flow Q in a uniform cross slope (Eq. 4-4-1 solved for T).</summary>
    public static double Spread(double q, double n, double sx, double s) => Math.Pow(q * n / (0.56 * Math.Pow(sx, 1.67) * Math.Sqrt(s)), 1 / 2.67);
    /// <summary>Gutter capacity at spread T (Eq. 4-4-1).</summary>
    public static double Capacity(double t, double n, double sx, double s) => 0.56 / n * Math.Pow(sx, 1.67) * Math.Sqrt(s) * Math.Pow(t, 2.67);
    /// <summary>Mean gutter velocity (Eq. 4-4-2).</summary>
    public static double Velocity(double t, double n, double sx, double s) => 1.12 / n * Math.Sqrt(s) * Math.Pow(sx, 0.67) * Math.Pow(t, 0.67);

    /// <summary>Ratio of frontal flow (over width W next to the curb) to total gutter flow, HEC-22 Eq. 4-16 with a uniform slope.</summary>
    public static double FrontalRatio(double w, double t) => w >= t ? 1 : 1 - Math.Pow(1 - w / t, 2.67);

    /// <summary>Curb-opening length for total interception on grade, HEC-22 Eq. 4-22 (U.S., Ku = 0.6); Se = equivalent cross slope.</summary>
    public static double CurbLengthTotal(double q, double s, double n, double se) => 0.6 * Math.Pow(q, 0.42) * Math.Pow(s, 0.3) * Math.Pow(1 / (n * se), 0.6);
    public static double CurbEfficiency(double l, double lt) => l >= lt ? 1 : 1 - Math.Pow(1 - l / lt, 1.8);

    /// <summary>Grate on grade efficiency (HEC-22 Eqs. 4-18 to 4-21, U.S.).</summary>
    public static double GrateEfficiency(double l, double w, double t, double v, double vo, double sx)
    {
        double eo = FrontalRatio(w, t);
        double rf = Math.Clamp(1 - 0.09 * (v - vo), 0, 1);
        double rs = 1 / (1 + 0.15 * Math.Pow(v, 1.8) / (sx * Math.Pow(l, 2.3)));
        return rf * eo + rs * (1 - eo);
    }

    /// <summary>Sag curb opening: weir Q = Cw (L + 1.8 W) d^1.5 (Cw 2.3 depressed, 3.0 with W = 0), orifice Q = 0.67 h L √(2 g (d − h/2)); the lesser governs above the opening height.</summary>
    public static double CurbSag(double d, double l, double w, double h) =>
        Math.Min(w > 0 ? 2.3 * (l + 1.8 * w) * Math.Pow(d, 1.5) : 3.0 * l * Math.Pow(d, 1.5),
                 d > h ? 0.67 * h * l * Math.Sqrt(2 * G * (d - h / 2)) : double.PositiveInfinity);

    /// <summary>Sag grate: weir Q = 3.0 P d^1.5 (P = L + 2W, curb side excluded), orifice Q = 0.67 A √(2 g d); the lesser governs.</summary>
    public static double GrateSag(double d, double l, double w, double area) =>
        Math.Min(3.0 * (l + 2 * w) * Math.Pow(d, 1.5), area > 0 ? 0.67 * area * Math.Sqrt(2 * G * d) : double.PositiveInfinity);

    /// <summary>Depth at the inlet that passes <paramref name="q"/> through a sag capacity function (bisection; NaN when 3 ft is not enough).</summary>
    public static double SagDepth(double q, Func<double, double> capacity)
    {
        double lo = 0, hi = 3;
        if (capacity(hi) < q) return double.NaN;
        for (int i = 0; i < 100; i++) { double m = 0.5 * (lo + hi); if (capacity(m) < q) lo = m; else hi = m; }
        return hi;
    }
}

public sealed class GutterInletTool : IDesignTool
{
    public string Id => "gutter-inlets";
    public string Name => "Gutter spread and inlets (IDCM §4.4–4.5, HEC-22)";
    public string Description => "Inlets along a gutter in flow order: spread and depth from IDCM Eq. 4-4-1, checked against the Table 4-6 encroachment and curb height; interception by HEC-22 with Table 4-11 clogging; bypass carried to the next inlet. Sag inlets show the ponded depth and spread.";
    public bool UsesNetwork => true;

    private static readonly Dictionary<string, (double Dry, string Text)> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["local"] = (0, "Local: spread may reach the crown"),
        ["collector"] = (12, "Collector / two-lane arterial: 12 ft of pavement free of water"),
        ["arterial3"] = (24, "Three-lane arterial: 24 ft free of water"),
        ["arterial4"] = (36, "Arterial with more than three lanes: 36 ft free of water"),
    };

    public string SampleInput(DesignToolContext context)
    {
        var inlets = context.Network?.Structures.Where(s => s.Kind.Contains("inlet", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("CB", StringComparison.OrdinalIgnoreCase)).Select(s => s.Name).ToList() ?? [];
        string N(int i) => i < inlets.Count ? inlets[i] : $"CB-{i + 1}";
        return $"""
            # Gutter spread and inlet interception. U.S. units (ft, cfs).
            # Street: local, collector (also two-lane arterials and arterials with raised medians), arterial3, arterial4.
            street class = collector
            pavement width = 40        # flowline to flowline, ft; the allowed spread is (width − dry lanes)/2 on each side
            curb height = 0.5          # ft; no curb overtopping (Table 4-6)
            cross slope = 0.02         # Sx, ft/ft
            n = 0.016                  # Table 4-7: asphalt 0.016, concrete gutter 0.012 to 0.015

            # Inlets in flow order; bypass goes to the next row. Type: curb, grate or combination. Location: grade or sag.
            # Q local = runoff reaching the gutter at this inlet (e.g. from the rational method tool).
            # L, W = opening length and width (ft); a = gutter depression (in., curb opening); Vo = grate splash-over
            # velocity (fps, from HEC-22 for the grate type); Area = grate clear opening area (ft²); h = curb opening height (ft).
            [Inlets]
            Inlet	Type	Location	Q local (cfs)	S (ft/ft)	L (ft)	W (ft)	a (in)	Vo (fps)	Area (ft2)	h (ft)
            {N(0)}	curb	grade	4.0	0.015	10	2	2			0.5
            {N(1)}	grate	grade	2.5	0.012	3	2		6.0
            {N(2)}	combination	sag	1.5		3	2		 	4.0	0.5
            """;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Gutter spread and inlet interception — IDCM §4.4–4.5, HEC-22";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-GI-001", "IDCM Eqs. 4-4-1/4-4-2 and the HEC-22 U.S. coefficients are in ft and cfs. Switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        string cls = p.Text("street class") ?? "local";
        if (!Classes.TryGetValue(cls, out var c)) { msgs.Add(CalcMessage.Error("E-GI-002", $"Street class \"{cls}\" must be local, collector, arterial3 or arterial4.")); c = Classes["local"]; }
        double width = p.Number("pavement width", double.NaN), curb = p.Number("curb height", 0.5), sx = p.Number("cross slope", 0.02), n = p.Number("n", 0.016);
        if (!(width > 0)) msgs.Add(CalcMessage.Error("E-GI-003", "Pavement width (flowline to flowline) is required."));
        if (!(sx > 0) || !(n > 0) || !(curb > 0)) msgs.Add(CalcMessage.Error("E-GI-004", "Cross slope, n and curb height must be greater than zero."));
        double tAllow = (width - c.Dry) / 2;
        if (width > 0 && !(tAllow > 0)) msgs.Add(CalcMessage.Error("E-GI-005", $"A {width} ft pavement cannot leave {c.Dry} ft dry."));
        var rows = new List<IReadOnlyList<object?>>();
        var flows = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double carry = 0, lastS = double.NaN;
        var inlets = p.Rows("Inlets");
        if (inlets.Count == 0) msgs.Add(CalcMessage.Error("E-GI-006", "No inlets: add rows under [Inlets]."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], [.. p.Errors, .. msgs]);

        foreach (var r in inlets)
        {
            string name = r[0], where = $"Inlet {name}";
            string type = (r.Length > 1 ? r[1] : "").ToLowerInvariant(), loc = (r.Length > 2 ? r[2] : "grade").ToLowerInvariant();
            double qLocal = p.Cell(r, 3, "Q local", where) ?? 0;
            double? sIn = p.Cell(r, 4, "S", where), l = p.Cell(r, 5, "L", where), w = p.Cell(r, 6, "W", where), a = p.Cell(r, 7, "a", where), vo = p.Cell(r, 8, "Vo", where), area = p.Cell(r, 9, "area", where), h = p.Cell(r, 10, "h", where);
            if (type is not ("curb" or "grate" or "combination")) { msgs.Add(CalcMessage.Error("E-GI-007", $"{where}: type \"{type}\" must be curb, grate or combination.")); continue; }
            if (loc is not ("grade" or "sag")) { msgs.Add(CalcMessage.Error("E-GI-008", $"{where}: location \"{loc}\" must be grade or sag.")); continue; }
            if (l is not > 0) { msgs.Add(CalcMessage.Error("E-GI-009", $"{where}: opening length L is required.")); continue; }
            // Table 4-11. A combination (Type B) inlet is a grate on grade (IDCM §4.5.2(2)–(4)), so 40 %; it is a combination only in a sag, 33 %.
            double clog = type switch { "grate" => 0.40, "combination" => loc == "grade" ? 0.40 : 0.33, _ => 0 };
            double q = qLocal + carry;
            string status;
            double qi, bypass, t, d, v = double.NaN, eff;
            if (loc == "grade")
            {
                double s = sIn ?? lastS;
                if (!(s > 0)) { msgs.Add(CalcMessage.Error("E-GI-010", $"{where}: longitudinal slope S is required on grade.")); continue; }
                lastS = s;
                t = GutterHydraulics.Spread(q, n, sx, s); d = t * sx; v = GutterHydraulics.Velocity(t, n, sx, s);
                double lEff = l.Value * (1 - clog);
                if (type == "curb")
                {
                    double wd = w ?? 0, sw = wd > 0 && a is > 0 ? a.Value / 12 / wd : 0;
                    double se = sx + sw * (wd > 0 ? GutterHydraulics.FrontalRatio(wd, t) : 0);   // HEC-22 Eq. 4-23
                    double lt = GutterHydraulics.CurbLengthTotal(q, s, n, se);
                    eff = GutterHydraulics.CurbEfficiency(lEff, lt);
                    status = $"LT = {lt:F1} ft, Se = {se:F4}";
                }
                else
                {
                    if (w is not > 0 || vo is not > 0) { msgs.Add(CalcMessage.Error("E-GI-011", $"{where}: a grate on grade needs W and the splash-over velocity Vo (HEC-22, for the grate type).")); continue; }
                    eff = GutterHydraulics.GrateEfficiency(lEff, w.Value, t, v, vo.Value, sx);
                    status = $"Eo = {GutterHydraulics.FrontalRatio(w.Value, t):F3}" + (v > vo ? $", splash-over (V {v:F2} > Vo {vo:F1})" : "") + (type == "combination" ? "; combination on grade taken as the grate (IDCM §4.5.2)" : "");
                }
                qi = eff * q; bypass = q - qi;
            }
            else
            {
                double wd = w ?? 0;
                Func<double, double> cap = type switch
                {
                    "curb" => dd => GutterHydraulics.CurbSag(dd, l.Value, wd, h ?? double.PositiveInfinity),
                    "grate" => dd => GutterHydraulics.GrateSag(dd, l.Value * (1 - clog), wd * (1 - clog), (area ?? 0) * (1 - clog)),
                    // Combination in sag: the grate (clogged) plus the curb opening over the same length.
                    _ => dd => GutterHydraulics.GrateSag(dd, l.Value * (1 - clog), wd * (1 - clog), (area ?? 0) * (1 - clog)) + GutterHydraulics.CurbSag(dd, l.Value, 0, h ?? double.PositiveInfinity),
                };
                if (type != "curb" && wd <= 0) { msgs.Add(CalcMessage.Error("E-GI-012", $"{where}: a sag grate needs its width W.")); continue; }
                d = GutterHydraulics.SagDepth(q, cap);
                if (double.IsNaN(d)) { msgs.Add(CalcMessage.Error("E-GI-013", $"{where}: more than 3 ft of ponding would be needed for {q:F2} cfs; the inlet is far too small.")); continue; }
                t = d / sx; eff = 1; qi = q; bypass = 0;
                status = "sag: all flow intercepted at the ponded depth shown";
            }
            var check = new List<string>();
            if (d > curb + 1e-9) check.Add($"depth {d:F2} ft over the {curb:F2} ft curb");
            if (t > tAllow + 1e-9) check.Add($"spread {t:F1} ft over the {tAllow:F1} ft allowed");
            rows.Add([name, $"{type} / {loc}", q, t, d, v, l * (1 - clog), eff, qi, bypass, check.Count == 0 ? "OK" : "FAILS: " + string.Join("; ", check), status]);
            if (check.Count > 0) msgs.Add(CalcMessage.Warning("W-GI-001", $"{where}: {string.Join("; ", check)} (Table 4-6, {c.Text})."));
            flows[name] = flows.GetValueOrDefault(name) + qi;
            carry = bypass;
        }
        if (carry > 1e-6) msgs.Add(CalcMessage.Warning("W-GI-002", $"{carry:F2} cfs bypasses the last inlet; it must be picked up downstream."));
        msgs.InsertRange(0, p.Errors);
        var tables = new List<ToolTable>
        {
            new("Inlets in flow order", ["Inlet", "Type", "Q gutter (cfs)", "Spread T (ft)", "Depth d (ft)", "V (fps)", "L effective (ft)", "E", "Q intercepted (cfs)", "Bypass (cfs)", "Table 4-6 check", "Notes"], rows,
            [
                $"Street: {c.Text}. Allowed spread = ({width:0.#} − {c.Dry:0}) / 2 = {tAllow:F1} ft each side; curb height {curb:F2} ft; Sx = {sx}; n = {n}.",
                "Spread from Eq. 4-4-1 (on grade) or ponded depth ÷ Sx (sag). Clogging (Table 4-11) shortens the effective length: grate 40 %, combination 33 % in a sag (a combination on grade is a grate, 40 %), curb 0 %.",
            ]),
        };
        var result = new ToolResult(title, [$"{rows.Count} inlet(s); total intercepted {rows.Sum(x => (double)x[8]!):F2} cfs; last bypass {carry:F2} cfs."], tables, msgs);
        if (context.Network is { } net && result.Succeeded)
        {
            var names = net.Structures.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int k = flows.Keys.Count(names.Contains);
            if (k > 0)
                return result with
                {
                    UpdatedNetwork = net with { Structures = net.Structures.Select(s => flows.TryGetValue(s.Name, out var f) ? s with { Inflow = Math.Round(f, 3) } : s).ToList() },
                    UpdateDescription = $"Set the intercepted flow as the inflow at {k} structure(s) of \"{net.Name}\" (bypass leaves the sewer system at those inlets).",
                };
        }
        return result;
    }
}
