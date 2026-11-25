using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Emergency (auxiliary) spillways and dam breach for small embankments: NRCS TR-60 (2005) design rainfall for the
/// auxiliary spillway and freeboard hydrographs by hazard class, an auxiliary spillway rating (broad-crested weir, or
/// critical depth in a trapezoidal earth spillway), freeboard checks against the routed stages, and a breach peak flow,
/// breach width and failure time by Froehlich (1995, 2008), MacDonald and Langridge-Monopolis (1984) and USBR (1988).
/// </summary>
public sealed class SpillwayBreachTool : IDesignTool
{
    public string Id => "spillway-breach";
    public string Name => "Emergency spillways and dam breach (TR-60, Froehlich)";
    public string Description => "NRCS TR-210-60 (2019) for pond embankments: auxiliary spillway (ASH) and freeboard (FBH) hydrograph rainfall from P100 and PMP by hazard class, storage × height and upstream dams (Figure 2–2), and the principal spillway design storm (Figure 2–1); auxiliary spillway rating as a broad-crested weir or critical flow in a trapezoidal earth spillway; checks of the routed ASH and FBH stages against the spillway crest and top of dam. Breach: peak outflow by Froehlich (1995), MacDonald–Langridge-Monopolis (1984) and USBR (1988), Froehlich (2008) breach width and failure time, and a triangular breach hydrograph.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Emergency spillway and dam breach. U.S. units: ft, ac-ft, in., cfs.
        hazard = significant        # low, significant or high (TR-60 class a, b, c)
        storage x height = 24000    # ac-ft × effective height (ft), TR-210-60 Figure 2–2 (low hazard rows)
        upstream dam = no           # yes when an upstream dam could fail onto this one
        auxiliary spillway = vegetated   # earth or vegetated (principal spillway storm, Figure 2–1)
        P100 = 4.6                  # 100-yr 6-hr (or 24-hr) rainfall, in.
        PMP = 26.0                  # probable maximum precipitation for the same duration, in.

        # Auxiliary spillway: weir (C, L) or trapezoid (bottom width, side slope) with its crest elevation.
        spillway = trapezoid
        crest = 3412.0
        bottom width = 40
        side slope = 3
        top of dam = 3416.0

        # Peak stages from routing the ASH and FBH storms (detention routing tool with the rating below); optional.
        ASH stage = 3413.6
        FBH stage = 3415.2

        # Breach: storage at failure (ac-ft), water depth above the breach bottom and breach height (ft); overtopping or piping.
        breach storage = 120
        breach depth = 22
        breach height = 24
        embankment area = 1900      # ft², embankment cross-section at the breach (TR-210-60 breach factor)
        valley width = 300          # ft, valley width at the breach depth
        failure mode = overtopping
        """;

    /// <summary>
    /// TR-210-60 (2019) Figure 2–2 fractions f of (PMP − P100) added to P100 for the auxiliary spillway (ASH) and freeboard
    /// (FBH) hydrographs, by hazard class, the product of storage (ac-ft) × effective height (ft) and whether an upstream dam
    /// whose failure could endanger this one exists; NaN f for the FBH of a high hazard dam means the full PMP.
    /// </summary>
    public static (double FAsh, double FFbh, string Row) Tr60Fractions(string hazard, double? storageHeight, bool upstreamDam) => hazard switch
    {
        "low" or "a" when upstreamDam => (0.12, 0.40, "low hazard, upstream dam"),
        "low" or "a" when storageHeight > 30000 => (0.06, 0.26, "low hazard, storage × height > 30,000"),
        "low" or "a" => (0, 0.12, "low hazard, storage × height < 30,000, no upstream dam"),
        "significant" or "b" => (0.12, 0.40, "significant hazard"),
        "high" or "c" => (0.26, 1.0, "high hazard"),
        _ => (double.NaN, double.NaN, ""),
    };

    /// <summary>TR-60 design rainfall: ASH and FBH as P100 + f·(PMP − P100) (TR-210-60 Figure 2–2).</summary>
    public static (double Ash, double Fbh) Tr60Rainfall(string hazard, double p100, double pmp, double? storageHeight = null, bool upstreamDam = false)
    {
        var (fa, ff, _) = Tr60Fractions(hazard, storageHeight, upstreamDam);
        double d = pmp - p100;
        return (p100 + fa * d, p100 + ff * d);
    }

    /// <summary>
    /// TR-210-60 (2019) Figure 2–1 minimum principal spillway design precipitation (10-day storm) as a return period in years,
    /// or a fraction of the design life for single-purpose irrigation dams (footnote 2), for an earth or vegetated auxiliary spillway.
    /// </summary>
    public static string Tr60PrincipalSpillway(string hazard, double? storageHeight, bool upstreamDam, bool irrigationOnly, bool vegetated, bool ramp = false)
    {
        bool big = storageHeight > 30000;
        return hazard switch
        {
            "low" or "a" when irrigationOnly && !upstreamDam => big ? "¾ design life" : "½ design life",
            "low" or "a" when upstreamDam => vegetated ? "P50" : "P100",
            "low" or "a" when big => vegetated ? "P50" : "P75",
            "low" or "a" => vegetated ? "P25" : ramp ? "P100" : "P50",
            "significant" or "b" => vegetated ? "P50" : "P100",
            "high" or "c" => "P100",
            _ => "",
        };
    }

    /// <summary>
    /// TR-210-60 (2019) ch. 1 minimum peak breach discharge (cfs): Hw ≥ 103 ft → 65·Hw^1.85; Hw &lt; 103 ft → 1100·Br^1.35 with
    /// Br = Vs·Hw/A, but not less than 3.2·Hw^2.5 nor more than 65·Hw^1.85; where the valley width L at Hw is less than
    /// T = 65·Hw^0.35/0.416, 0.416·L·Hw^1.5 replaces 65·Hw^1.85. Vs ac-ft, Hw ft, A ft² (embankment section), L ft.
    /// </summary>
    public static double Tr60BreachMinimum(double vs, double hw, double? area, double? valleyWidth)
    {
        double t = 65 * Math.Pow(hw, 0.35) / 0.416;
        double cap = valleyWidth is { } l && l < t ? 0.416 * l * Math.Pow(hw, 1.5) : 65 * Math.Pow(hw, 1.85);
        if (hw >= 103 || area is not > 0) return cap;
        double br = vs * hw / area.Value;
        return Math.Min(Math.Max(1100 * Math.Pow(br, 1.35), 3.2 * Math.Pow(hw, 2.5)), cap);
    }

    /// <summary>Critical flow in a trapezoid with bottom b and side slope z at specific energy (head over crest) H (U.S.).</summary>
    public static double TrapezoidCritical(double b, double z, double h)
    {
        // Solve E = y + A/(2T) = H for critical depth, then Q = A·√(g·A/T).
        double lo = 0, hi = h;
        for (int i = 0; i < 100; i++)
        {
            double y = 0.5 * (lo + hi), a = (b + z * y) * y, t = b + 2 * z * y;
            if (y + a / (2 * t) < h) lo = y; else hi = y;
        }
        double yc = 0.5 * (lo + hi), ac = (b + z * yc) * yc, tc = b + 2 * z * yc;
        return ac * Math.Sqrt(32.2 * ac / tc);
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Emergency spillway and dam breach";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-SB-001", "This tool uses U.S. units (in., ft, ac-ft, cfs); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        string hazard = (p.Text("hazard") ?? "low").ToLowerInvariant();
        string purpose = (p.Text("purpose") ?? "").Trim().ToLowerInvariant();
        bool upstreamDam = (p.Text("upstream dam") ?? "no").Trim().ToLowerInvariant() is "yes" or "y" or "true";
        bool vegetated = (p.Text("auxiliary spillway") ?? "earth").Trim().ToLowerInvariant() is "vegetated" or "vegetated earth";
        double? storageHeight = p.OptionalNumber("storage x height") ?? (p.OptionalNumber("storage") is { } stv && p.OptionalNumber("effective height") is { } ehv ? stv * ehv : null);
        if (hazard is "low" or "a" && purpose is "municipal" or "industrial")
        {
            hazard = "significant";
            msgs.Add(CalcMessage.Info("I-SB-003", "TR-210-60 Figures 2–1 and 2–2: a low hazard dam for industrial or municipal water uses the significant hazard criteria."));
        }
        double? p100 = p.OptionalNumber("P100"), pmp = p.OptionalNumber("PMP");
        string sp = (p.Text("spillway") ?? "").ToLowerInvariant();
        double? crest = p.OptionalNumber("crest"), top = p.OptionalNumber("top of dam"), ash = p.OptionalNumber("ASH stage"), fbh = p.OptionalNumber("FBH stage");
        double? vs = p.OptionalNumber("breach storage"), hw = p.OptionalNumber("breach depth"), hb = p.OptionalNumber("breach height");
        double? embArea = p.OptionalNumber("embankment area"), valleyW = p.OptionalNumber("valley width");
        string mode = (p.Text("failure mode") ?? "overtopping").ToLowerInvariant();
        if (hazard is not ("low" or "significant" or "high" or "a" or "b" or "c")) msgs.Add(CalcMessage.Error("E-SB-002", "hazard must be low, significant or high."));
        if (p100 is not null && pmp is not null && !(pmp > p100 && p100 > 0)) msgs.Add(CalcMessage.Error("E-SB-003", "Give P100 above zero and PMP above P100."));
        if (mode is not ("overtopping" or "piping")) msgs.Add(CalcMessage.Error("E-SB-004", "failure mode must be overtopping or piping."));
        if (sp is not ("" or "weir" or "trapezoid")) msgs.Add(CalcMessage.Error("E-SB-005", "spillway must be weir or trapezoid."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        var tables = new List<ToolTable>();
        var summary = new List<string>();
        var charts = new List<ToolChart>();

        if (p100 is { } a && pmp is { } b)
        {
            var (fa, ff, rowName) = Tr60Fractions(hazard, storageHeight, upstreamDam);
            var (ra, rf) = Tr60Rainfall(hazard, a, b, storageHeight, upstreamDam);
            static string Rule(double f) => f == 0 ? "P100" : f == 1 ? "PMP" : $"P100 + {f:0.00}·(PMP − P100)";
            if (hazard is "low" or "a" && storageHeight is null && !upstreamDam)
                msgs.Add(CalcMessage.Info("I-SB-004", "Low hazard without storage x height (ac-ft × ft): the < 30,000 row of TR-210-60 Figure 2–2 is used; above 30,000 the ASH is P100 + 0.06·(PMP − P100) and the FBH P100 + 0.26·(PMP − P100)."));
            tables.Add(new("TR-60 design rainfall", ["Hydrograph", "Rainfall (in.)", "Rule"],
                [["Auxiliary spillway (ASH)", R(ra, 2), Rule(fa)],
                 ["Freeboard (FBH)", R(rf, 2), Rule(ff)],
                 ["Principal spillway (10-day storm, Figure 2–1)", null, Tr60PrincipalSpillway(hazard, storageHeight, upstreamDam, purpose is "irrigation", vegetated)]],
                [$"NRCS TR-210-60 (2019) Figures 2–1 and 2–2 minimum criteria, row: {rowName}{(storageHeight is { } sh ? $" (storage × height {sh:N0} ac-ft·ft)" : "")}, {(vegetated ? "vegetated" : "earth")} auxiliary spillway. Analyze both 6- and 24-hour storms for the ASH and FBH (TR-210-60 ch. 2).",
                 "Make the ASH and FBH hydrographs from these depths (TR-55 or hydrograph routing tool) and route them with the rating below (detention routing tool) to get the stages."]));
            summary.Add($"TR-60 {hazard} hazard: ASH rainfall {ra:F2} in., FBH rainfall {rf:F2} in.");
        }

        if (sp != "" && crest is { } cr)
        {
            var rating = new List<IReadOnlyList<object?>>();
            var pts = new List<(double, double)>();
            double hmax = top is { } tt ? Math.Max(0.5, tt - cr + 1) : 5;
            Func<double, double>? q = null;
            if (sp == "weir")
            {
                double c = p.Number("C", 3.0), l = p.Number("L", double.NaN);
                if (!(l > 0)) msgs.Add(CalcMessage.Error("E-SB-006", "A weir spillway needs L (crest length, ft)."));
                else q = h => c * l * Math.Pow(h, 1.5);
            }
            else
            {
                double bw = p.Number("bottom width", double.NaN), z = p.Number("side slope", 3);
                if (!(bw > 0) || z < 0) msgs.Add(CalcMessage.Error("E-SB-006", "A trapezoid spillway needs bottom width above zero and side slope ≥ 0."));
                else q = h => TrapezoidCritical(bw, z, h);
            }
            if (q is not null)
            {
                for (int i = 0; i <= 20; i++)
                {
                    double h = hmax * i / 20;
                    double qq = q(h);
                    rating.Add([R(cr + h, 2), R(h, 2), R(qq, 1)]);
                    pts.Add((qq, cr + h));
                }
                tables.Add(new("Auxiliary spillway rating", ["Elevation (ft)", "Head on crest (ft)", "Q (cfs)"], rating,
                    [sp == "weir" ? "Broad-crested weir Q = C·L·H^1.5." : "Earth spillway with a level control section: critical flow at the control, specific energy = head on the crest (approach losses neglected), Q = A·√(g·A/T)."]));
                charts.Add(new("Auxiliary spillway rating", "Q (cfs)", "Elevation (ft)", [new("Rating", pts)], top is { } t2 ? [new("Top of dam", t2), new("Crest", cr)] : [new("Crest", cr)]));
                var chk = new List<IReadOnlyList<object?>>();
                if (ash is { } sa) chk.Add(["ASH peak stage", R(sa, 2), top is { } t3 ? R(t3 - sa, 2) : null, top is { } t4 && sa < t4 ? "below top of dam" : "OVERTOPS", R(q(Math.Max(0, sa - cr)), 1)]);
                if (fbh is { } sf) chk.Add(["FBH peak stage", R(sf, 2), top is { } t5 ? R(t5 - sf, 2) : null, top is { } t6 && sf <= t6 ? "OK (at or below top of dam)" : "OVERTOPS", R(q(Math.Max(0, sf - cr)), 1)]);
                if (chk.Count > 0)
                {
                    tables.Add(new("Freeboard checks", ["Item", "Stage (ft)", "Below top of dam (ft)", "Result", "Spillway Q (cfs)"], chk,
                        ["TR-60: the FBH must not overtop the dam; the ASH sets the auxiliary spillway's integrity (erosion) check, run with the SITES/WinDAM erosion model or state rules."]));
                    if (fbh is { } f && top is { } t7 && f > t7) msgs.Add(CalcMessage.Warning("W-SB-001", $"The FBH stage {f:F2} ft is above the top of dam {t7:F2} ft: widen the auxiliary spillway or raise the dam."));
                }
                summary.Add($"Auxiliary spillway: {q(1):F0} cfs at 1 ft of head{(top is { } t8 ? $", {q(t8 - cr):F0} cfs at the top of dam" : "")}.");
            }
        }

        if (vs is { } v && hw is { } h1)
        {
            if (!(v > 0) || !(h1 > 0)) msgs.Add(CalcMessage.Error("E-SB-007", "breach storage and breach depth must be above zero."));
            else
            {
                double vm3 = v * 1233.48, hwm = h1 * 0.3048, hbm = (hb ?? h1) * 0.3048, g = 9.81;
                double qF = 0.607 * Math.Pow(vm3, 0.295) * Math.Pow(hwm, 1.24);
                double qM = 1.154 * Math.Pow(vm3 * hwm, 0.412);
                double qU = 19.1 * Math.Pow(hwm, 1.85);
                double ko = mode == "overtopping" ? 1.3 : 1.0;
                double bAvg = 0.27 * ko * Math.Pow(vm3, 0.32) * Math.Pow(hbm, 0.04);
                double tf = 63.2 * Math.Sqrt(vm3 / (g * hbm * hbm)) / 3600;
                double toCfs = 35.3147;
                List<IReadOnlyList<object?>> rows =
                [
                    ["Froehlich (1995) Qp = 0.607·Vw^0.295·hw^1.24", R(qF * toCfs, 0), "cfs"],
                    ["MacDonald–Langridge-Monopolis (1984) Qp = 1.154·(Vw·hw)^0.412", R(qM * toCfs, 0), "cfs"],
                    ["USBR (1988) Qp = 19.1·hw^1.85", R(qU * toCfs, 0), "cfs"],
                    ["Average breach width (Froehlich 2008) B = 0.27·Ko·Vw^0.32·hb^0.04", R(bAvg / 0.3048, 1), "ft"],
                    ["Breach side slope (Froehlich 2008)", mode == "overtopping" ? "1.0H:1V" : "0.7H:1V", ""],
                    ["Failure time tf = 63.2·√(Vw/(g·hb²))", R(tf, 2), "hr"],
                ];
                double qTr = Tr60BreachMinimum(v, h1, embArea, valleyW);
                rows.Insert(0, [$"TR-210-60 minimum peak breach discharge{(h1 < 103 && embArea is > 0 ? $" (Br = Vs·Hw/A = {v * h1 / embArea!.Value:G3})" : "")}", R(qTr, 0), "cfs"]);
                if (h1 < 103 && embArea is not > 0) msgs.Add(CalcMessage.Info("I-SB-005", "Give embankment area (ft², the dam section at the breach) for the TR-210-60 breach factor; without it the 65·Hw^1.85 cap is shown."));
                tables.Add(new("Dam breach (regression equations)", ["Item", "Value", "Unit"], rows,
                    [$"Vw = {v:G4} ac-ft ({vm3:F0} m³), hw = {h1:G3} ft, hb = {hb ?? h1:G3} ft; equations in SI units, results converted. Ko = {ko:G2} ({mode}).",
                     "TR-210-60 ch. 1: the breach hydrograph peak is at least the minimum above, whatever method maps the inundation; a process model (NWS BREACH, WinDAM) may replace it.",
                     "The regression equations scatter by a factor of about 2 to 3; use the largest for a hazard classification screen and a breach model (HEC-RAS, WinDAM) for an inundation map."]));
                double qp = Math.Max(qF, Math.Max(qM, qU)) * toCfs, vFt3 = v * 43560, tb = 2 * vFt3 / qp / 3600;
                double tp = Math.Min(tf, tb * 0.9);
                charts.Add(new("Breach hydrograph (triangular, largest peak)", "Time (hr)", "Q (cfs)", [new("Breach outflow", [(0, 0), (tp, qp), (tb, 0)])]));
                summary.Add($"Breach peak {qF * toCfs:F0} cfs (Froehlich), up to {qp:F0} cfs by the other equations; breach about {bAvg / 0.3048:F0} ft wide, forming in {tf:F1} hr.");
            }
        }
        if (tables.Count == 0) msgs.Add(CalcMessage.Error("E-SB-008", "Give P100 and PMP, a spillway with its crest, or breach storage and depth."));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
