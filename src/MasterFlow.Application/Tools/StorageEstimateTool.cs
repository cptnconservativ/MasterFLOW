using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// First-cut detention volume before any pond is drawn: NRCS TR-55 Chapter 6 (Figure 6-1, storage/runoff ratio from the
/// outflow/inflow peak ratio), the modified rational critical-duration method on the Rapid City IDF (or your own), and the
/// Abt–Grigg and Baker ratio methods for comparison. Used to reserve land in site layout; the pond is then sized by routing.
/// </summary>
public sealed class StorageEstimateTool : IDesignTool
{
    public string Id => "storage-estimate";
    public string Name => "Preliminary detention storage estimate";
    public string Description => "First-cut detention volume: TR-55 Ch. 6 Vs/Vr = C0 + C1·r + C2·r² + C3·r³ with r = qo/qi (Type I/IA or II/III coefficients), runoff Vr from CN and rainfall; modified rational V = max over Td of 60·[C·i(Td)·A·Td − qo·(Td + tc)/2] on IDCM Table 4-2 or your IDF; and the Abt–Grigg (1 − r)² and Baker (1 − r) ratios for comparison. For reserving land; size the pond by routing.";
    public bool UsesNetwork => false;

    /// <summary>TR-55 Appendix F / Figure 6-1 storage ratio Vs/Vr for outflow/inflow ratio <paramref name="r"/>.</summary>
    public static double Tr55Ratio(double r, bool typeII)
    {
        var (c0, c1, c2, c3) = typeII ? (0.682, -1.43, 1.64, -0.804) : (0.660, -1.76, 1.96, -0.730);
        return c0 + c1 * r + c2 * r * r + c3 * r * r * r;
    }

    /// <summary>NRCS runoff depth Q = (P − 0.2S)²/(P + 0.8S), S = 1000/CN − 10 (inches).</summary>
    public static double Runoff(double p, double cn)
    {
        double s = 1000 / cn - 10, ia = 0.2 * s;
        return p <= ia ? 0 : (p - ia) * (p - ia) / (p + 0.8 * s);
    }

    /// <summary>Modified rational storage (ft³) for duration <paramref name="td"/> (min): inflow C·i·A over Td, outflow rising to qo over Td + tc.</summary>
    public static double ModRational(double c, double i, double acres, double td, double tc, double qo)
        => 60 * (c * i * acres * td - qo * (td + tc) / 2);

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Preliminary detention storage. SI: area in ha, rainfall in mm, flows in m³/s, times in minutes.
        area = 10
        qi = 1.35
        qo = 0.50
        CN = 82
        P = 115
        rainfall type = II
        C = 0.65
        tc = 15
        [IDF]
        Duration (min)	Intensity (mm/h)
        5	241
        15	159
        60	75
        120	39
        180	27
        360	15
        """ : """
        # Preliminary detention storage. U.S.: area in acres, rainfall in inches, flows in cfs, times in minutes.
        area = 25
        qi = 48                 # post-development peak inflow (cfs) for the design storm
        qo = 18                 # allowable release (cfs), e.g. the pre-development peak
        # TR-55 Ch. 6: runoff from CN and the 24-hour rainfall (Rapid City IDCM Table 4-2, 100-yr: 4.55 in.), or give Q runoff = … (in.)
        CN = 82
        P = 4.55
        rainfall type = II      # I, IA, II or III (South Dakota is Type II)
        # Modified rational: runoff C and tc; intensities from IDCM Table 4-2 (frequency 2, 10 or 100) unless an [IDF] table is given.
        C = 0.65
        tc = 15
        frequency = 100
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Preliminary detention storage";
        bool si = context.Units == UnitSystem.SI;
        // Computed in U.S. units; SI converted on entry and back on output.
        double acF = si ? 2.47105 : 1, inF = si ? 1 / 25.4 : 1, qF = si ? 35.3147 : 1;
        string Uv = si ? "m³" : "ac-ft", Uq = si ? "m³/s" : "cfs";
        double Vout(double cf) => si ? cf * 0.0283168 : cf / 43560;
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double area = p.Number("area", double.NaN, "area") * acF, qi = p.Number("qi", double.NaN, "qi") * qF, qo = p.Number("qo", double.NaN, "qo") * qF;
        double? cn = p.OptionalNumber("CN"), pp = p.OptionalNumber("P"), qrun = p.OptionalNumber("Q runoff", "Q runoff");
        string type = (p.Text("rainfall type") ?? "II").Trim().ToUpperInvariant();
        double? c = p.OptionalNumber("C"), tc = p.OptionalNumber("tc");
        int freq = (int)p.Number("frequency", 100, "frequency");
        if (!(area > 0) || !(qi > 0) || !(qo > 0)) msgs.Add(CalcMessage.Error("E-SE-001", "Give area, qi and qo, all above zero."));
        else if (qo >= qi) msgs.Add(CalcMessage.Error("E-SE-002", $"qo ({qo / qF:G4}) must be less than qi ({qi / qF:G4}); no storage is needed otherwise."));
        if (type is not ("I" or "IA" or "II" or "III")) msgs.Add(CalcMessage.Error("E-SE-003", $"rainfall type \"{type}\" is not I, IA, II or III."));
        if (cn is { } cc && cc is <= 0 or > 100) msgs.Add(CalcMessage.Error("E-SE-004", "CN must be above 0 and at most 100."));
        if (c is { } cv && cv is <= 0 or > 1) msgs.Add(CalcMessage.Error("E-SE-005", "C must be above 0 and at most 1."));
        if (tc is <= 0) msgs.Add(CalcMessage.Error("E-SE-006", "tc must be above zero."));
        var idf = new List<(double D, double I)>();
        foreach (var row in p.Rows("IDF"))
        {
            double? d = p.Cell(row, 0, "duration", "IDF"), i = p.Cell(row, 1, "intensity", "IDF");
            if (d is > 0 && i is > 0) idf.Add((d.Value, i.Value * inF)); else msgs.Add(CalcMessage.Error("E-SE-007", "IDF rows need a duration and an intensity above zero."));
        }
        idf.Sort((a, b) => a.D.CompareTo(b.D));
        if (c is not null && idf.Count == 0 && freq is not (2 or 10 or 100)) msgs.Add(CalcMessage.Error("E-SE-008", "frequency must be 2, 10 or 100 for IDCM Table 4-2, or give an [IDF] table."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double r = qo / qi;
        var rows = new List<IReadOnlyList<object?>>();
        var summary = new List<string>();
        double vr = double.NaN, qDepth = double.NaN;
        if (qrun is { } qq) qDepth = qq * inF;
        else if (cn is { } cnv && pp is { } pv) qDepth = Runoff(pv * inF, cnv);
        if (!double.IsNaN(qDepth))
        {
            vr = qDepth / 12 * area * 43560;                                        // ft³
            bool t2 = type is "II" or "III";
            double ratio = Tr55Ratio(r, t2);
            rows.Add(["TR-55 Ch. 6 (Fig. 6-1)", R(r, 3), R(ratio, 3), R(Vout(ratio * vr), si ? 0 : 3), $"runoff {qDepth * (si ? 25.4 : 1):F2} {(si ? "mm" : "in.")}, Type {(t2 ? "II/III" : "I/IA")} coefficients"]);
            rows.Add(["Abt and Grigg (1978)", R(r, 3), R((1 - r) * (1 - r), 3), R(Vout((1 - r) * (1 - r) * vr), si ? 0 : 3), "trapezoidal inflow hydrograph"]);
            rows.Add(["Baker (1979)", R(r, 3), R(1 - r, 3), R(Vout((1 - r) * vr), si ? 0 : 3), "triangular inflow and outflow hydrographs (largest of the ratio methods)"]);
            if (r is < 0.1 or > 0.8) msgs.Add(CalcMessage.Warning("W-SE-001", $"qo/qi = {r:F2} is outside 0.1–0.8, the range of TR-55 Figure 6-1."));
            summary.Add($"TR-55 Ch. 6: {Vout(Tr55Ratio(r, t2) * vr):F2} {Uv} (Vs/Vr = {Tr55Ratio(r, t2):F3}, runoff volume {Vout(vr):F2} {Uv}).");
        }
        else msgs.Add(CalcMessage.Info("I-SE-001", "No CN and P (or Q runoff) given: the TR-55 Ch. 6 and ratio methods are skipped."));

        if (c is { } cval && tc is { } tcv)
        {
            Func<double, double> intensity = idf.Count > 0 ? d => Idf(idf, d) : d => RapidCityRational.Intensity(freq, d);
            double best = double.NegativeInfinity, bestTd = double.NaN;
            var crows = new List<IReadOnlyList<object?>>();
            double tdMax = idf.Count > 0 ? idf[^1].D : 1440;
            for (double td = Math.Max(tcv, idf.Count > 0 ? idf[0].D : 5); td <= tdMax + 1e-9; td += td < 60 ? 5 : td < 360 ? 15 : 60)
            {
                double i = intensity(td);
                if (double.IsNaN(i)) continue;
                double v = ModRational(cval, i, area, td, tcv, qo);
                crows.Add([td, R(i * (si ? 25.4 : 1), 2), R(cval * i * area / qF, 2), R(Vout(v), si ? 0 : 3)]);
                if (v > best) { best = v; bestTd = td; }
            }
            if (double.IsNaN(bestTd)) msgs.Add(CalcMessage.Warning("W-SE-002", "No duration from tc to the end of the IDF gave an intensity; the modified rational method is skipped."));
            else
            {
                double cia = cval * intensity(tcv) * area;
                if (Math.Abs(cia - qi) > 0.15 * qi) msgs.Add(CalcMessage.Info("I-SE-002", $"C·i(tc)·A = {cia / qF:F1} {Uq} differs from qi = {qi / qF:F1} {Uq} by more than 15 %; check that both describe the same storm."));
                rows.Add(["Modified rational (critical duration)", R(r, 3), double.IsNaN(vr) ? null : R(best / vr, 3), R(Vout(Math.Max(0, best)), si ? 0 : 3), $"critical Td = {bestTd:F0} min, {(idf.Count > 0 ? "your IDF" : $"IDCM Table 4-2 {freq}-yr")}"]);
                summary.Add($"Modified rational: {Vout(Math.Max(0, best)):F2} {Uv} at a critical duration of {bestTd:F0} min.");
            }
            var tables0 = crows;
            var ctab = new ToolTable("Modified rational storage by duration", ["Td (min)", $"i ({(si ? "mm/h" : "in./h")})", $"C·i·A ({Uq})", $"Storage ({Uv})"], tables0,
                ["V = 60·[C·i(Td)·A·Td − qo·(Td + tc)/2] (ft³ with i in in./h, A in acres): the inflow lasts Td, the outflow rises to qo over Td + tc."]);
            var res = Build(rows, summary, msgs, Uv, si);
            return res with { Tables = [.. res.Tables, ctab] };
        }
        if (rows.Count == 0) { msgs.Add(CalcMessage.Error("E-SE-009", "Give CN and P (or Q runoff) for TR-55, or C and tc for the modified rational method.")); return new(title, [], [], msgs); }
        return Build(rows, summary, msgs, Uv, si);
    }

    private static ToolResult Build(List<IReadOnlyList<object?>> rows, List<string> summary, List<CalcMessage> msgs, string uv, bool si)
    {
        var vols = rows.Select(r => r[3]).OfType<double>().Where(v => v > 0).ToList();
        if (vols.Count > 1) summary.Insert(0, $"Storage estimate {vols.Min():F2} to {vols.Max():F2} {uv}; reserve land for the upper end until the pond is routed.");
        msgs.Add(CalcMessage.Info("I-SE-003", "These are planning estimates (TR-55 Ch. 6 errs by up to about 25 %); size the pond and outlet by routing (detention routing or hydrograph routing tools)."));
        var t = new ToolTable("Storage estimates", ["Method", "qo/qi", "Vs/Vr", $"Storage ({uv})", "Basis"], rows,
            ["TR-55: Vs/Vr = C0 + C1·r + C2·r² + C3·r³; Type I/IA: 0.660, −1.76, 1.96, −0.730; Type II/III: 0.682, −1.43, 1.64, −0.804. Vr = runoff depth × area.",
             "Abt and Grigg: Vs/Vr = (1 − r)²; Baker: Vs/Vr = 1 − r."]);
        return new ToolResult("Preliminary detention storage", summary, [t], msgs);
    }

    /// <summary>Intensity interpolated log-log between IDF points; NaN outside the table.</summary>
    private static double Idf(List<(double D, double I)> t, double d)
    {
        if (d < t[0].D - 1e-9 || d > t[^1].D + 1e-9) return double.NaN;
        for (int k = 1; k < t.Count; k++)
            if (d <= t[k].D + 1e-9)
            {
                double f = Math.Log(d / t[k - 1].D) / Math.Log(t[k].D / t[k - 1].D);
                return t[k - 1].I * Math.Pow(t[k].I / t[k - 1].I, f);
            }
        return t[0].I;
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
