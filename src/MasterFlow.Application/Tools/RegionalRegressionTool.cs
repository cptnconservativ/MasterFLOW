using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// FHWA HDS-2 / USGS regional regression peak flows: any power-form equation Q = C·ΠXi^bi typed from a USGS report
/// (or a StreamStats/NSS output), 90 % prediction intervals from the standard error of prediction, the range check of
/// each basin characteristic, weighting with a gage (equivalent years of record), and transfer of the weighted estimate
/// to an ungaged site on the same stream (Sauer area-ratio adjustment).
/// </summary>
public sealed class RegionalRegressionTool : IDesignTool
{
    public string Id => "regional-regression";
    public string Name => "Regional regression peak flows, gage weighting and transfer (HDS-2, USGS)";
    public string Description => "Peak discharges by USGS regional regression equations of power form Q = C·X1^b1·X2^b2·… entered from the report for the region (HDS-2 Ch. 5): 90 % prediction intervals from the standard error of prediction (percent or log units), basin characteristics checked against the ranges of the data the equations came from; weighting with a gaged estimate, log Qw = (N·log Qg + E·log Qr)/(N + E); and transfer to an ungaged site on the same stream, Qu = Qr,u·[R − (2ΔA/Ag)(R − 1)] with R = Qw,g/Qr,g for |ΔA|/Ag ≤ 0.5.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Regional regression (HDS-2 / USGS). The coefficients below are made up for the example:
        # copy the equations for your region from the USGS report (or StreamStats) before using the results.
        area variable = A
        # Optional gage on the same stream: its area, years of record and the at-site (Bulletin 17C) estimates under [Gage].
        gage area = 18.0
        gage years = 35
        # A = drainage area (mi²), P = mean annual precipitation (in); Minimum and Maximum are the ranges in the report.
        [Variables]
        Name, Site value, Minimum, Maximum
        A, 12.5, 0.5, 2000
        P, 18, 14, 30
        [Equations]
        T (yr), C, A, P, SEP (%), EYR
        2, 2.1, 0.58, 1.10, 95, 3
        10, 14.0, 0.55, 0.95, 70, 6
        25, 30.5, 0.54, 0.88, 65, 8
        50, 52.0, 0.53, 0.83, 66, 9
        100, 84.0, 0.52, 0.79, 70, 10
        [Gage]
        T (yr), Q gage
        2, 140
        10, 610
        25, 1050
        50, 1450
        100, 1950
        """;

    /// <summary>Standard error in log10 units from a percent standard error.</summary>
    public static double LogSe(double pct) => Math.Sqrt(Math.Log(1 + Math.Pow(pct / 100, 2))) / Math.Log(10);

    /// <summary>Weighted estimate by equivalent years: log Qw = (N·log Qg + E·log Qr)/(N + E).</summary>
    public static double Weighted(double qg, double n, double qr, double e) => Math.Pow(10, (n * Math.Log10(qg) + e * Math.Log10(qr)) / (n + e));

    /// <summary>Sauer (1974) transfer to an ungaged site: Qu = Qr,u·[R − (2ΔA/Ag)(R − 1)], ΔA = |Ag − Au|.</summary>
    public static double Transfer(double qru, double ratio, double ag, double au) => qru * (ratio - 2 * Math.Abs(ag - au) / ag * (ratio - 1));

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Regional regression peak flows (HDS-2, USGS)";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        var vars = new Dictionary<string, (double V, double? Min, double? Max)>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Variables"))
        {
            string name = r.Length > 0 ? r[0].Trim() : "";
            if (name.Length == 0 || p.Cell(r, 1, "site value", $"[Variables] {name}") is not { } v) continue;
            double? mn = r.Length > 2 && r[2].Length > 0 ? p.Cell(r, 2, "minimum", name) : null, mx = r.Length > 3 && r[3].Length > 0 ? p.Cell(r, 3, "maximum", name) : null;
            if (!(v > 0)) msgs.Add(CalcMessage.Error("E-RR-002", $"[Variables] {name}: the site value must be above zero for a power equation (enter X + 1 if the report uses it)."));
            vars[name] = (v, mn, mx);
        }
        var head = p.Header("Equations").Select(h => h.Trim()).ToList();
        int cSep = head.FindIndex(h => h.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) || h.StartsWith("SE", StringComparison.OrdinalIgnoreCase));
        int cLog = head.FindIndex(h => h.StartsWith("Slog", StringComparison.OrdinalIgnoreCase) || h.Contains("log", StringComparison.OrdinalIgnoreCase));
        int cEyr = head.FindIndex(h => h.StartsWith("EYR", StringComparison.OrdinalIgnoreCase) || h.StartsWith("E ", StringComparison.OrdinalIgnoreCase) || h.Equals("E", StringComparison.OrdinalIgnoreCase));
        var expCols = Enumerable.Range(2, Math.Max(0, head.Count - 2)).Where(i => i != cSep && i != cLog && i != cEyr).ToList();
        foreach (var i in expCols.Where(i => !vars.ContainsKey(head[i])))
            msgs.Add(CalcMessage.Error("E-RR-003", $"[Equations] column \"{head[i]}\" has no row under [Variables]."));
        var eqs = new List<(double T, double C, double[] B, double? SeLog, double? Eyr)>();
        foreach (var r in p.Rows("Equations"))
        {
            if (p.Cell(r, 0, "T", "[Equations]") is not { } t || p.Cell(r, 1, "C", $"[Equations] T {r[0]}") is not { } c) continue;
            var b = expCols.Select(i => p.Cell(r, i, head[i], $"[Equations] T {t:G4}") ?? 0).ToArray();
            double? se = cLog >= 0 && cLog < r.Length && r[cLog].Length > 0 ? p.Cell(r, cLog, "log SE", $"T {t:G4}") : cSep >= 0 && cSep < r.Length && r[cSep].Length > 0 && p.Cell(r, cSep, "SEP", $"T {t:G4}") is { } pct ? LogSe(pct) : null;
            double? eyr = cEyr >= 0 && cEyr < r.Length && r[cEyr].Length > 0 ? p.Cell(r, cEyr, "EYR", $"T {t:G4}") : null;
            if (!(t > 1) || !(c > 0)) msgs.Add(CalcMessage.Error("E-RR-004", $"[Equations] T {r[0]}: the return period must exceed 1 year and C must be above zero."));
            eqs.Add((t, c, b, se, eyr));
        }
        if (eqs.Count == 0) msgs.Add(CalcMessage.Error("E-RR-001", "Give the equations under [Equations]: T (yr), C, one exponent column per variable, and optionally SEP (%) or Slog, and EYR."));
        string? areaVar = p.Text("area variable");
        double? ag = p.OptionalNumber("gage area"), ng = p.OptionalNumber("gage years");
        var gage = new Dictionary<double, double>();
        foreach (var r in p.Rows("Gage"))
            if (p.Cell(r, 0, "T", "[Gage]") is { } t && p.Cell(r, 1, "Q gage", $"[Gage] T {t:G4}") is { } qg) gage[t] = qg;
        if (gage.Count > 0 && (ag is not > 0 || ng is not > 0)) msgs.Add(CalcMessage.Error("E-RR-005", "Gage weighting needs gage area and gage years (above zero)."));
        if (gage.Count > 0 && (areaVar is null || !vars.ContainsKey(areaVar))) msgs.Add(CalcMessage.Error("E-RR-006", "Gage weighting needs area variable = (the drainage area column of the equations)."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        foreach (var (name, (v, mn, mx)) in vars)
            if (v < mn || v > mx) msgs.Add(CalcMessage.Warning("W-RR-001", $"{name} = {v:G4} is outside the range of the data behind the equations ({mn:G4} to {mx:G4}); the estimate is an extrapolation and its error is larger than stated."));
        var names = expCols.Select(i => head[i]).ToList();
        double Q(double c, double[] b, Func<string, double> x) => c * names.Select((nm, j) => Math.Pow(x(nm), b[j])).Aggregate(1.0, (a, f) => a * f);
        var rows = new List<IReadOnlyList<object?>>();
        var est = new List<(double T, double Q)>();
        foreach (var e in eqs.OrderBy(e => e.T))
        {
            double q = Q(e.C, e.B, nm => vars[nm].V);
            est.Add((e.T, q));
            double? lo = e.SeLog is { } s ? q / Math.Pow(10, 1.645 * s) : null, hi = e.SeLog is { } s2 ? q * Math.Pow(10, 1.645 * s2) : null;
            rows.Add([e.T, R(100 / e.T, 2), R(q, 0), lo is null ? null : R(lo.Value, 0), hi is null ? null : R(hi.Value, 0)]);
        }
        var summary = new List<string> { $"Regression estimate: {string.Join(", ", est.Select(e => $"Q{e.T:G4} = {e.Q:N0}"))}." };
        var tables = new List<ToolTable>
        {
            new("Regression estimates at the site", ["T (yr)", "AEP (%)", "Q", "90 % lower", "90 % upper"], rows,
            [
                $"Q = C·{string.Join("·", names.Select(n => $"{n}^b"))} with the site values {string.Join(", ", vars.Select(v => $"{v.Key} = {v.Value.V:G4}"))}.",
                "90 % prediction interval Q/10^(1.645·S) to Q·10^(1.645·S), S in log10 units (from SEP % by S = √(ln(1 + SEP²))/ln 10); the model-error term of the site's leverage is not included.",
            ]),
        };
        if (gage.Count > 0)
        {
            double au = vars[areaVar!].V, agv = ag!.Value, ratioA = Math.Abs(agv - au) / agv;
            var wrows = new List<IReadOnlyList<object?>>();
            var wsum = new List<string>();
            foreach (var e in eqs.OrderBy(e => e.T))
            {
                if (!gage.TryGetValue(e.T, out double qg)) continue;
                double qrg = Q(e.C, e.B, nm => nm.Equals(areaVar, StringComparison.OrdinalIgnoreCase) ? agv : vars[nm].V);
                double eyr = e.Eyr ?? double.NaN;
                if (double.IsNaN(eyr)) { msgs.Add(CalcMessage.Warning("W-RR-002", $"T {e.T:G4}: no EYR (equivalent years of record) for the regression; the gage is used alone.")); eyr = 0; }
                double qw = Weighted(qg, ng!.Value, qrg, eyr), ratio = qw / qrg, qru = Q(e.C, e.B, nm => vars[nm].V);
                double? qu = ratioA <= 0.5 ? Transfer(qru, ratio, agv, au) : null;
                wrows.Add([e.T, R(qg, 0), R(qrg, 0), R(qw, 0), R(ratio, 3), R(qru, 0), qu is null ? null : R(qu.Value, 0)]);
                if (qu is { } u) wsum.Add($"Q{e.T:G4} = {u:N0}");
            }
            if (ratioA > 0.5) msgs.Add(CalcMessage.Warning("W-RR-003", $"The site's drainage area differs from the gage's by {ratioA * 100:F0} %: the Sauer transfer applies only within 50 %; use the regression estimate."));
            if (wsum.Count > 0) summary.Add($"Weighted with the gage and transferred to the site: {string.Join(", ", wsum)}.");
            tables.Add(new($"Gage weighting (gage area {agv:G4}, {ng:G3} years) and transfer to the site (area {au:G4})", ["T (yr)", "Q gage", "Q regression at gage", "Q weighted at gage", "R = Qw/Qr", "Q regression at site", "Q transferred to site"], wrows,
            [
                "log Qw = (N·log Qg + E·log Qr)/(N + E): N = years of gage record, E = equivalent years of record of the regression.",
                "Sauer (1974): Qu = Qr,u·[R − (2ΔA/Ag)(R − 1)], applied when |Ag − Au|/Ag ≤ 0.5; at ΔA = Ag/2 it reduces to the regression.",
            ]));
        }
        var charts = new List<ToolChart> { new("Peak discharge frequency", "Return period (yr)", "Q", [new("Regression", est.Select(e => (e.T, e.Q)).ToList())]) };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v, int d) => Math.Round(v, d) + 0.0;
}
