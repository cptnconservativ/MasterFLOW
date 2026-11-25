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
    public string Description => "NRCS TR-60 (2005) for pond embankments: auxiliary spillway (ASH) and freeboard (FBH) hydrograph rainfall from P100 and PMP by hazard class (low, significant, high); auxiliary spillway rating as a broad-crested weir or critical flow in a trapezoidal earth spillway; checks of the routed ASH and FBH stages against the spillway crest and top of dam. Breach: peak outflow by Froehlich (1995), MacDonald–Langridge-Monopolis (1984) and USBR (1988), Froehlich (2008) breach width and failure time, and a triangular breach hydrograph.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Emergency spillway and dam breach. U.S. units: ft, ac-ft, in., cfs.
        hazard = significant        # low, significant or high (TR-60 class a, b, c)
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
        failure mode = overtopping
        """;

    /// <summary>TR-60 design rainfall: ASH and FBH as P100 + f·(PMP − P100), f by hazard class.</summary>
    public static (double Ash, double Fbh) Tr60Rainfall(string hazard, double p100, double pmp)
    {
        double d = pmp - p100;
        return hazard switch
        {
            "low" or "a" => (p100, p100 + 0.12 * d),
            "significant" or "b" => (p100 + 0.12 * d, p100 + 0.40 * d),
            "high" or "c" => (p100 + 0.26 * d, pmp),
            _ => (double.NaN, double.NaN),
        };
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
        double? p100 = p.OptionalNumber("P100"), pmp = p.OptionalNumber("PMP");
        string sp = (p.Text("spillway") ?? "").ToLowerInvariant();
        double? crest = p.OptionalNumber("crest"), top = p.OptionalNumber("top of dam"), ash = p.OptionalNumber("ASH stage"), fbh = p.OptionalNumber("FBH stage");
        double? vs = p.OptionalNumber("breach storage"), hw = p.OptionalNumber("breach depth"), hb = p.OptionalNumber("breach height");
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
            var (ra, rf) = Tr60Rainfall(hazard, a, b);
            tables.Add(new("TR-60 design rainfall", ["Hydrograph", "Rainfall (in.)", "Rule"],
                [["Auxiliary spillway (ASH)", R(ra, 2), hazard is "low" or "a" ? "P100" : hazard is "significant" or "b" ? "P100 + 0.12·(PMP − P100)" : "P100 + 0.26·(PMP − P100)"],
                 ["Freeboard (FBH)", R(rf, 2), hazard is "low" or "a" ? "P100 + 0.12·(PMP − P100)" : hazard is "significant" or "b" ? "P100 + 0.40·(PMP − P100)" : "PMP"]],
                ["NRCS TR-60 (2005) minimum criteria by hazard class (low a, significant b, high c). The principal spillway must also pass its own design storm without the auxiliary spillway flowing, as the state dam safety rules set.",
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
                tables.Add(new("Dam breach (regression equations)", ["Item", "Value", "Unit"], rows,
                    [$"Vw = {v:G4} ac-ft ({vm3:F0} m³), hw = {h1:G3} ft, hb = {hb ?? h1:G3} ft; equations in SI units, results converted. Ko = {ko:G2} ({mode}).",
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
