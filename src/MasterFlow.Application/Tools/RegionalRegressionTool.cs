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
    public string Description => "Peak discharges by USGS regional regression equations of power form Q = C·X1^b1·X2^b2·… entered from the report for the region (HDS-2 Ch. 5), or the built-in South Dakota rural equations by subregion (equations = sd-A … sd-G, SDDOT Drainage Manual 2011 Figure 7.9-C) with the USGS nationwide urban adjustment by BDF (Figure 7.11-A): 90 % prediction intervals from the standard error of prediction (percent or log units), basin characteristics checked against the ranges of the data the equations came from; weighting with a gaged estimate, log Qw = (N·log Qg + E·log Qr)/(N + E); and transfer to an ungaged site on the same stream, Qu = Qr,u·[R − (2ΔA/Ag)(R − 1)] with R = Qw,g/Qr,g for |ΔA|/Ag ≤ 0.5.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Regional regression (HDS-2 / USGS). The coefficients below are made up for the example:
        # copy the equations for your region from the USGS report (or StreamStats) before using the results.
        # In South Dakota, replace [Variables] and [Equations] with the SDDOT rural equations for the subregion, e.g.
        #   equations = sd-C      (sd-A … sd-G, SDDOT Drainage Manual Figure 7.9-A/C)
        #   CA = 12.5             (contributing area, mi²; PII = … for A and B, S = … ft/mi for F)
        #   BDF = 12              (optional urban adjustment, 0 to 12)
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

    /// <summary>One South Dakota rural equation: Q = C·CA^BA·X^BX, standard error of prediction (%) and equivalent years of record.</summary>
    public sealed record SdEquation(double T, double C, double BA, double BX, double Sep, double Eyr);

    /// <summary>
    /// SDDOT Drainage Manual (October 2011) Chapter 7, Figure 7.9-C (USGS SIR 2008-5104) rural regression equations by hydrologic
    /// subregion, with the second variable (PII in A and B, main-channel slope S in ft/mi in F) and the Figure 7.9-D ranges.
    /// </summary>
    public static readonly IReadOnlyDictionary<char, (string? X, double? XMin, double? XMax, double AMin, double AMax, SdEquation[] Eqs)> SouthDakota =
        new Dictionary<char, (string?, double?, double?, double, double, SdEquation[])>
        {
            ['A'] = ("PII", 0.79, 1.30, 0.14, 983, [new(2, 30.9, 0.513, 6.14, 59, 4.5), new(5, 85.5, 0.509, 5.45, 54, 6.1), new(10, 137, 0.510, 5.12, 54, 7.8), new(25, 218, 0.513, 4.80, 56, 9.8), new(50, 287, 0.517, 4.62, 58, 11.0), new(100, 362, 0.521, 4.47, 61, 11.9), new(500, 553, 0.531, 4.22, 69, 13.0)]),
            ['B'] = ("PII", 0.60, 1.21, 0.22, 670, [new(2, 18.6, 0.425, 1.10, 67, 5.4), new(5, 51.6, 0.508, 0.835, 64, 7.1), new(10, 86.8, 0.546, 0.764, 67, 8.7), new(25, 148, 0.584, 0.730, 72, 10.6), new(50, 206, 0.606, 0.728, 76, 11.6), new(100, 275, 0.625, 0.742, 81, 12.4), new(500, 480, 0.661, 0.811, 93, 13.6)]),
            ['C'] = (null, null, null, 0.06, 904, [new(2, 25.0, 0.569, 0, 108, 1.8), new(5, 72.5, 0.578, 0, 67, 4.8), new(10, 125, 0.579, 0, 58, 8.3), new(25, 207, 0.573, 0, 53, 12.0), new(50, 286, 0.570, 0, 53, 14.9), new(100, 379, 0.566, 0, 55, 16.5), new(500, 664, 0.556, 0, 65, 16.6)]),
            ['D'] = (null, null, null, 0.11, 137, [new(2, 78.5, 0.357, 0, 109, 2.3), new(5, 230, 0.455, 0, 61, 7.4), new(10, 395, 0.515, 0, 44, 17.9), new(25, 676, 0.585, 0, 34, 39.1), new(50, 944, 0.627, 0, 33, 52.5), new(100, 1270, 0.663, 0, 34, 59.2), new(500, 2300, 0.732, 0, 41, 57.5)]),
            ['E'] = (null, null, null, 10.0, 760, [new(2, 12.1, 0.555, 0, 44, 4.3), new(5, 18.9, 0.611, 0, 28, 16.0), new(10, 22.6, 0.653, 0, 26, 27.0), new(25, 27.0, 0.702, 0, 30, 30.2), new(50, 30.3, 0.737, 0, 36, 27.4), new(100, 33.6, 0.769, 0, 42, 24.2), new(500, 41.4, 0.840, 0, 60, 18.5)]),
            ['F'] = ("S", 29.6, 460, 0.63, 920, [new(2, 0.937, 0.676, 0.447, 107, 2.6), new(5, 0.591, 0.779, 0.745, 83, 6.0), new(10, 0.471, 0.832, 0.907, 73, 10.5), new(25, 0.406, 0.888, 1.06, 66, 18.4), new(50, 0.381, 0.925, 1.16, 64, 24.6), new(100, 0.352, 0.960, 1.25, 64, 29.4), new(500, 0.243, 1.04, 1.47, 78, 31.2)]),
            ['G'] = (null, null, null, 3.81, 105, [new(2, 3.46, 0.650, 0, 51, 3.9), new(5, 7.70, 0.654, 0, 71, 3.2), new(10, 11.3, 0.673, 0, 87, 3.2), new(25, 16.5, 0.704, 0, 108, 3.3), new(50, 21.0, 0.731, 0, 126, 3.3), new(100, 25.8, 0.759, 0, 144, 3.4), new(500, 38.5, 0.826, 0, 193, 3.5)]),
        };

    /// <summary>USGS nationwide three-parameter urban equations (SDDOT Figure 7.11-A, HDS-2): UQ = RC·A^b1·(13 − BDF)^b2·RQ^b3.</summary>
    public static readonly IReadOnlyDictionary<double, (double Rc, double B1, double B2, double B3)> UrbanNationwide = new Dictionary<double, (double, double, double, double)>
    {
        [2] = (13.2, 0.21, -0.43, 0.73), [5] = (10.6, 0.17, -0.39, 0.78), [10] = (9.51, 0.16, -0.36, 0.79), [25] = (8.68, 0.15, -0.34, 0.80),
        [50] = (8.04, 0.15, -0.32, 0.81), [100] = (7.70, 0.15, -0.32, 0.82), [500] = (7.47, 0.16, -0.30, 0.82),
    };

    /// <summary>Urban peak from the equivalent rural peak (cfs), area (mi²) and basin development factor (0–12).</summary>
    public static double? Urban(double t, double areaSqMi, double bdf, double rq) =>
        UrbanNationwide.TryGetValue(t, out var u) ? u.Rc * Math.Pow(areaSqMi, u.B1) * Math.Pow(13 - bdf, u.B2) * Math.Pow(rq, u.B3) : null;

    private static char? SdRegion(string text)
    {
        var t = text.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("SUBREGION", "");
        return t.Length == 3 && t.StartsWith("SD") && t[2] is >= 'A' and <= 'G' ? t[2] : null;
    }

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
        string? areaVarDefault = null;
        var vars = new Dictionary<string, (double V, double? Min, double? Max)>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Variables"))
        {
            string name = r.Length > 0 ? r[0].Trim() : "";
            if (name.Length == 0 || p.Cell(r, 1, "site value", $"[Variables] {name}") is not { } v) continue;
            double? mn = r.Length > 2 && r[2].Length > 0 ? p.Cell(r, 2, "minimum", name) : null, mx = r.Length > 3 && r[3].Length > 0 ? p.Cell(r, 3, "maximum", name) : null;
            if (!(v > 0)) msgs.Add(CalcMessage.Error("E-RR-002", $"[Variables] {name}: the site value must be above zero for a power equation (enter X + 1 if the report uses it)."));
            vars[name] = (v, mn, mx);
        }
        var eqs = new List<(double T, double C, double[] B, double? SeLog, double? Eyr)>();
        List<string> names;
        string? preset = p.Text("equations");
        if (preset is not null && SdRegion(preset) is { } region)
        {
            // SDDOT Drainage Manual (2011) Figure 7.9-C: USGS rural regression equations for South Dakota (SIR 2008-5104).
            var (xName, xMin, xMax, aMin, aMax, list) = SouthDakota[region];
            names = xName is null ? ["CA"] : ["CA", xName];
            foreach (var nm in names)
            {
                if (!vars.ContainsKey(nm) && p.OptionalNumber(nm) is { } kv)
                {
                    if (!(kv > 0)) msgs.Add(CalcMessage.Error("E-RR-002", $"{nm} must be above zero."));
                    vars[nm] = (kv, null, null);
                }
                if (!vars.TryGetValue(nm, out var vv)) { msgs.Add(CalcMessage.Error("E-RR-007", $"SDDOT Subregion {region} needs {nm} ({(nm == "CA" ? "contributing drainage area, mi²" : nm == "PII" ? "precipitation intensity index, in. (Figure 7.9-B)" : "main-channel slope, ft/mi")}): give \"{nm} = …\" or a [Variables] row.")); continue; }
                // Figure 7.9-D ranges unless the user gave their own.
                vars[nm] = (vv.V, vv.Min ?? (nm == "CA" ? aMin : xMin), vv.Max ?? (nm == "CA" ? aMax : xMax));
            }
            foreach (var e in list) eqs.Add((e.T, e.C, xName is null ? [e.BA] : [e.BA, e.BX], LogSe(e.Sep), e.Eyr));
            areaVarDefault = "CA";
            msgs.Add(CalcMessage.Info("I-RR-001", $"South Dakota rural regression equations, Subregion {region} (SDDOT Drainage Manual 2011, Figure 7.9-C; USGS SIR 2008-5104), with the standard errors of prediction and equivalent years of record printed there."));
            if (region == 'D' && vars.TryGetValue("CA", out var ca) && ca.V > 15) msgs.Add(CalcMessage.Warning("W-RR-004", "SDDOT §7.9.2.2: use extreme caution with the Subregion D equations above 15 mi²; they give unusually large peaks."));
            if (region is 'F' or 'G') msgs.Add(CalcMessage.Info("I-RR-002", "SDDOT §7.9.2.2: Black Hills streams that cross fractured limestone outcrops can lose much of their flow; use caution just downstream of karst outcrops."));
            if (list.Any(e => e.Sep >= 100)) msgs.Add(CalcMessage.Info("I-RR-003", "SDDOT §7.9.2.5: where the standard error of prediction is about 100 % or more, check the estimate against the nearest gage in SIR 2008-5104."));
        }
        else
        {
            var head = p.Header("Equations").Select(h => h.Trim()).ToList();
            int cSep = head.FindIndex(h => h.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) || h.StartsWith("SE", StringComparison.OrdinalIgnoreCase));
            int cLog = head.FindIndex(h => h.StartsWith("Slog", StringComparison.OrdinalIgnoreCase) || h.Contains("log", StringComparison.OrdinalIgnoreCase));
            int cEyr = head.FindIndex(h => h.StartsWith("EYR", StringComparison.OrdinalIgnoreCase) || h.StartsWith("E ", StringComparison.OrdinalIgnoreCase) || h.Equals("E", StringComparison.OrdinalIgnoreCase));
            var expCols = Enumerable.Range(2, Math.Max(0, head.Count - 2)).Where(i => i != cSep && i != cLog && i != cEyr).ToList();
            foreach (var i in expCols.Where(i => !vars.ContainsKey(head[i])))
                msgs.Add(CalcMessage.Error("E-RR-003", $"[Equations] column \"{head[i]}\" has no row under [Variables]."));
            foreach (var r in p.Rows("Equations"))
            {
                if (p.Cell(r, 0, "T", "[Equations]") is not { } t || p.Cell(r, 1, "C", $"[Equations] T {r[0]}") is not { } c) continue;
                var b = expCols.Select(i => p.Cell(r, i, head[i], $"[Equations] T {t:G4}") ?? 0).ToArray();
                double? se = cLog >= 0 && cLog < r.Length && r[cLog].Length > 0 ? p.Cell(r, cLog, "log SE", $"T {t:G4}") : cSep >= 0 && cSep < r.Length && r[cSep].Length > 0 && p.Cell(r, cSep, "SEP", $"T {t:G4}") is { } pct ? LogSe(pct) : null;
                double? eyr = cEyr >= 0 && cEyr < r.Length && r[cEyr].Length > 0 ? p.Cell(r, cEyr, "EYR", $"T {t:G4}") : null;
                if (!(t > 1) || !(c > 0)) msgs.Add(CalcMessage.Error("E-RR-004", $"[Equations] T {r[0]}: the return period must exceed 1 year and C must be above zero."));
                eqs.Add((t, c, b, se, eyr));
            }
            names = expCols.Select(i => head[i]).ToList();
            if (preset is not null) msgs.Add(CalcMessage.Error("E-RR-008", $"equations = {preset}: built-in sets are sd-A to sd-G (SDDOT Drainage Manual Figure 7.9-C)."));
        }
        if (eqs.Count == 0) msgs.Add(CalcMessage.Error("E-RR-001", "Give the equations under [Equations]: T (yr), C, one exponent column per variable, and optionally SEP (%) or Slog, and EYR."));
        string? areaVar = p.Text("area variable") ?? areaVarDefault;
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
        double Q(double c, double[] b, Func<string, double> x) => c * names.Select((nm, j) => Math.Pow(x(nm), b[j])).Aggregate(1.0, (a, f) => a * f);
        var rows = new List<IReadOnlyList<object?>>();
        var est = new List<(double T, double Q)>();
        foreach (var e in eqs.OrderBy(e => e.T))
        {
            double q = Q(e.C, e.B, nm => vars[nm].V);
            est.Add((e.T, q));
            double? lo = e.SeLog is { } s ? q / Math.Pow(10, 1.645 * s) : null, hi = e.SeLog is { } s2 ? q * Math.Pow(10, 1.645 * s2) : null;
            double? lo1 = e.SeLog is { } s3 ? q / Math.Pow(10, s3) : null, hi1 = e.SeLog is { } s4 ? q * Math.Pow(10, s4) : null;
            rows.Add([e.T, R(100 / e.T, 2), R(q, 0), lo is null ? null : R(lo.Value, 0), hi is null ? null : R(hi.Value, 0), lo1 is null ? null : R(lo1.Value, 0), hi1 is null ? null : R(hi1.Value, 0)]);
        }
        var summary = new List<string> { $"Regression estimate: {string.Join(", ", est.Select(e => $"Q{e.T:G4} = {e.Q:N0}"))}." };
        var tables = new List<ToolTable>
        {
            new("Regression estimates at the site", ["T (yr)", "AEP (%)", "Q", "90 % lower", "90 % upper", "−1 SE", "+1 SE"], rows,
            [
                $"Q = C·{string.Join("·", names.Select(n => $"{n}^b"))} with the site values {string.Join(", ", vars.Select(v => $"{v.Key} = {v.Value.V:G4}"))}.",
                "90 % prediction interval Q/10^(1.645·S) to Q·10^(1.645·S), S in log10 units (from SEP % by S = √(ln(1 + SEP²))/ln 10); the model-error term of the site's leverage is not included.",
                "±1 standard error (HDS-2 Eq. 6.5 to 6.7): Q·10^−S to Q·10^S, about 68 % of the data; HDS-2 Example 6.2 reports this range.",
            ]),
        };
        if (p.OptionalNumber("BDF") is { } bdf)
        {
            if (bdf is < 0 or > 12 || areaVar is null || !vars.ContainsKey(areaVar)) msgs.Add(CalcMessage.Error("E-RR-009", "Urban adjustment needs BDF from 0 to 12 and the drainage area variable (area variable = …)."));
            else
            {
                double a = vars[areaVar].V;
                if (a is < 0.2 or > 100) msgs.Add(CalcMessage.Warning("W-RR-005", $"The urban equations apply to 0.2–100 mi² (SDDOT §7.11.2); the area is {a:G4} mi²."));
                var urows = new List<IReadOnlyList<object?>>();
                foreach (var (t, rq) in est)
                    if (Urban(t, a, bdf, rq) is { } uq) urows.Add([t, R(rq, 0), R(uq, 0), R(uq / rq, 2)]);
                if (urows.Count > 0)
                {
                    summary.Add($"Urban (BDF {bdf:G3}): {string.Join(", ", urows.Select(u => $"UQ{u[0]:G4} = {(double)u[2]!:N0}"))}.");
                    tables.Add(new($"Urban peaks, USGS nationwide equations (BDF = {bdf:G3}, A = {a:G4} mi²)", ["T (yr)", "Rural RQ", "Urban UQ", "UQ/RQ"], urows,
                        ["UQ = RC·A^b1·(13 − BDF)^b2·RQ^b3 (USGS three-parameter nationwide equations; SDDOT Drainage Manual Figure 7.11-A, HDS-2). BDF = sum of the 0/1 codes for channel improvements, linings, storm drains and curb and gutter in each basin third."]));
                }
            }
        }
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
