using System.Globalization;
using MasterFlow.Application.Hydrology;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Flood frequency at a gaged site by Bulletin 17C (England et al. 2019): log-Pearson III fitted by the Expected Moments
/// Algorithm, Multiple Grubbs–Beck low-outlier screening, historical and censored peaks, and skew weighting with a
/// regional skew.
/// </summary>
public sealed class FloodFrequencyTool : IDesignTool
{
    public string Id => "flood-frequency";
    public string Name => "Flood frequency (Bulletin 17C)";
    public string Description => "Bulletin 17C flood frequency from annual peaks: log-Pearson Type III by the Expected Moments Algorithm, Multiple Grubbs–Beck low-outlier test, historical peaks with a perception threshold, censored (<, >) and interval peaks, and station/regional skew weighting. Gives the frequency curve with confidence limits, plotting positions and a chart.";
    public bool UsesNetwork => false;

    /// <summary>Annual exceedance probabilities reported, as in PeakFQ.</summary>
    public static readonly double[] Aeps = [0.995, 0.99, 0.95, 0.9, 0.8, 0.6667, 0.5, 0.4292, 0.2, 0.1, 0.04, 0.02, 0.01, 0.005, 0.002];

    public string SampleInput(DesignToolContext context) => $"""
        # Bulletin 17C flood frequency. ILLUSTRATIVE record, not a real gage: use the USGS annual peaks (NWIS) for a real site.
        # Flows in {(context.Units == UnitSystem.SI ? "m³/s" : "cfs")}. Peak cells may be "<500" (less than), ">8000" (greater than) or "800-1200" (interval).
        regional skew = -0.1      # from the applicable USGS regional skew study
        regional skew SE = 0.55   # its standard error; MSE = SE²
        skew = weighted           # station, weighted or regional
        low outliers = MGBT       # MGBT, none, or a threshold flow
        historic start = 1907     # first year of the historical period (optional)
        historic threshold = 9000 # flows in unrecorded historical years are known to be below this
        confidence = 90           # % two-sided confidence interval

        # Code H = historical peak outside the systematic record.
        [Peaks]
        Year	Peak	Code
        1972	18600	H
        1979	4660
        1980	1470
        1981	811
        1982	1550
        1983	1760
        1984	3680
        1985	1570
        1986	2720
        1987	4610
        1988	1500
        1989	694
        1990	444
        1991	1080
        1992	1400
        1993	1180
        1994	1920
        1995	3020
        1996	2100
        1997	519
        1998	3650
        1999	3220
        2000	2170
        2001	1040
        2002	26
        2003	5120
        2004	3660
        2005	1340
        2006	1820
        2007	969
        2008	996
        2009	891
        2010	3750
        2011	1080
        2012	60
        2013	932
        2014	938
        2015	1750
        2016	1110
        2017	495
        2018	2230
        2019	1370
        2020	1920
        2021	1350
        2022	1450
        2023	2030
        """;

    private sealed record Peak(int Year, double Lo, double Hi, bool Historic, string Text)
    {
        public bool Exact => Lo == Hi;
    }

    /// <summary>Reads a peak cell: a number, "&lt;x", "&gt;x" or "a-b". Returns (lo, hi) in flow units, or null.</summary>
    public static (double Lo, double Hi)? ParsePeak(string s)
    {
        s = s.Trim().Replace(",", "");
        var inv = CultureInfo.InvariantCulture;
        if (s.StartsWith('<') && double.TryParse(s[1..], NumberStyles.Float, inv, out double lt) && lt > 0) return (0, lt);
        if (s.StartsWith('>') && double.TryParse(s[1..], NumberStyles.Float, inv, out double gt) && gt > 0) return (gt, double.PositiveInfinity);
        int dash = s.IndexOf('-', 1);
        if (dash > 0 && double.TryParse(s[..dash], NumberStyles.Float, inv, out double a) && double.TryParse(s[(dash + 1)..], NumberStyles.Float, inv, out double b) && a >= 0 && b > a) return (a, b);
        if (double.TryParse(s, NumberStyles.Float, inv, out double v) && v >= 0) return (v, v);
        return null;
    }

    /// <summary>Bulletin 17B confidence-limit factors (Eqs. 9-1..9-4): (K lower, K upper) for N years at two-sided confidence c, or null when undefined.</summary>
    public static (double Lower, double Upper)? ConfidenceK(double k, int n, double confidence)
    {
        double z = Stats.NormInv(1 - (1 - confidence) / 2);
        double a = 1 - z * z / (2 * (n - 1.0)), b = k * k - z * z / n, disc = k * k - a * b;
        if (a <= 0 || disc < 0) return null;
        return ((k - Math.Sqrt(disc)) / a, (k + Math.Sqrt(disc)) / a);
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Flood frequency (Bulletin 17C)";
        string Qu = context.Units == UnitSystem.SI ? "m³/s" : "cfs";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double? gr = p.OptionalNumber("regional skew"), grSe = p.OptionalNumber("regional skew SE"), hStart = p.OptionalNumber("historic start"), hT = p.OptionalNumber("historic threshold");
        double conf = p.Number("confidence", 90) / 100;
        string skewMode = (p.Text("skew") ?? (gr is null ? "station" : "weighted")).ToLowerInvariant();
        string lowMode = p.Text("low outliers") ?? "MGBT";

        var peaks = new List<Peak>();
        foreach (var r in p.Rows("Peaks"))
        {
            if (r.Length < 2 || !int.TryParse(r[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int year)) { msgs.Add(CalcMessage.Error("E-FF-002", $"Peak row \"{string.Join(" ", r)}\": the year is not a whole number.")); continue; }
            if (ParsePeak(r[1]) is not { } q) { msgs.Add(CalcMessage.Error("E-FF-002", $"{year}: peak \"{r[1]}\" is not a flow, \"<x\", \">x\" or \"a-b\".")); continue; }
            if (peaks.Any(x => x.Year == year)) { msgs.Add(CalcMessage.Error("E-FF-003", $"{year} appears twice; give one annual peak per year.")); continue; }
            bool hist = r.Length > 2 && r[2].Trim().Equals("H", StringComparison.OrdinalIgnoreCase);
            peaks.Add(new(year, q.Lo, q.Hi, hist, r[1].Trim()));
        }
        if (gr is not null && grSe is not > 0) msgs.Add(CalcMessage.Error("E-FF-004", "Give the regional skew SE (standard error) above zero with the regional skew."));
        if (skewMode is not ("station" or "weighted" or "regional")) msgs.Add(CalcMessage.Error("E-FF-006", $"skew = \"{skewMode}\" is not station, weighted or regional."));
        else if (skewMode != "station" && gr is null) msgs.Add(CalcMessage.Error("E-FF-006", $"skew = {skewMode} needs a regional skew."));
        if (!(conf > 0.5 && conf < 1)) msgs.Add(CalcMessage.Error("E-FF-007", "confidence must be between 50 and 100 %."));
        var sys = peaks.Where(x => !x.Historic).OrderBy(x => x.Year).ToList();
        if (sys.Count(x => x.Exact && x.Lo > 0) < 3) msgs.Add(CalcMessage.Error("E-FF-001", "Give at least three systematic annual peaks above zero (Bulletin 17C recommends at least 10)."));
        if (hStart is not null && hT is not > 0) msgs.Add(CalcMessage.Error("E-FF-005", "A historical period needs the historic threshold (perception threshold) above zero."));
        if (hStart is { } hs && sys.Count > 0 && hs > sys[0].Year) msgs.Add(CalcMessage.Error("E-FF-005", "historic start must be at or before the first systematic year."));
        double? lotIn = null;
        if (!lowMode.Equals("mgbt", StringComparison.OrdinalIgnoreCase) && !lowMode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (ToolInput.TryNumber(lowMode, out double v) && v > 0) lotIn = v;
            else msgs.Add(CalcMessage.Error("E-FF-008", $"low outliers = \"{lowMode}\" is not MGBT, none or a flow above zero."));
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        if (sys.Count < 10) msgs.Add(CalcMessage.Warning("W-FF-001", $"Only {sys.Count} systematic peaks; Bulletin 17C recommends at least 10 years, and the curve is very uncertain."));

        // Low outliers.
        var sysExactPos = sys.Where(x => x.Exact && x.Lo > 0).Select(x => x.Lo).OrderBy(v => v).ToList();
        double lot = 0;
        int klow = 0;
        double[] mw = [], mp = [];
        if (lotIn is { } li) { lot = li; klow = sysExactPos.Count(v => v < lot); }
        else if (lowMode.Equals("mgbt", StringComparison.OrdinalIgnoreCase) && sysExactPos.Count >= 6)
        {
            (klow, mw, mp) = Mgbt.Test(sysExactPos.Select(Math.Log10).ToList());
            if (klow > 0) lot = sysExactPos[klow];
        }
        bool zeros = sys.Any(x => x.Exact && x.Lo == 0);
        if (zeros && lot == 0) lot = sysExactPos[0];
        if (klow > 0) msgs.Add(CalcMessage.Info("I-FF-001", $"{klow} potentially influential low flood(s) below {Fmt(lot)} {Qu}{(lotIn is null ? " (Multiple Grubbs–Beck)" : " (given threshold)")}: treated as \"less than the threshold\" in EMA."));
        if (sysExactPos.Count - klow < 3) return new(title, [], [], [.. msgs, CalcMessage.Error("E-FF-009", "Fewer than three peaks remain above the low-outlier threshold.")]);

        // Observations for EMA, in log10.
        var obs = new List<LogInterval>();
        var notes = new Dictionary<int, string>();
        foreach (var pk in peaks)
        {
            double lo = pk.Lo, hi = pk.Hi;
            if (!pk.Historic && pk.Exact && pk.Lo < lot) { lo = 0; hi = lot; notes[pk.Year] = pk.Lo == 0 ? "zero flow, < LOT" : "PILF, < LOT"; }
            else if (!pk.Exact) notes[pk.Year] = hi == double.PositiveInfinity ? "greater than" : lo == 0 ? "less than" : "interval";
            if (pk.Historic) notes[pk.Year] = (notes.TryGetValue(pk.Year, out var n0) ? n0 + ", " : "") + "historical";
            obs.Add(new(lo <= 0 ? double.NegativeInfinity : Math.Log10(lo), double.IsPositiveInfinity(hi) ? double.PositiveInfinity : Math.Log10(hi)));
        }
        int nUnrecorded = 0;
        if (hStart is { } h0)
        {
            int last = peaks.Max(x => x.Year);
            var years = peaks.Select(x => x.Year).ToHashSet();
            for (int y = (int)h0; y <= last; y++)
                if (!years.Contains(y)) { obs.Add(new(double.NegativeInfinity, Math.Log10(hT!.Value))); nUnrecorded++; }
            foreach (var hp in peaks.Where(x => x.Historic && x.Hi < hT!.Value))
                msgs.Add(CalcMessage.Warning("W-FF-002", $"Historical peak {hp.Year} ({hp.Text}) is below the historic threshold; a historical peak is usually known because it exceeded it."));
        }

        // EMA: station skew, then the chosen skew.
        var st = Ema.Fit(obs);
        int nSys = sys.Count;
        double mseS = Ema.SkewMse(st.Skew, nSys);
        double gFinal = st.Skew;
        var final = st;
        double? gw = null;
        if (skewMode != "station")
        {
            double mseR = grSe!.Value * grSe.Value;
            gw = Ema.WeightedSkew(st.Skew, mseS, gr!.Value, mseR);
            gFinal = skewMode == "weighted" ? gw.Value : gr.Value;
            final = Ema.Fit(obs, gFinal);
        }
        else if (gr is null) msgs.Add(CalcMessage.Warning("W-FF-003", "Station skew only: Bulletin 17C recommends weighting it with a regional skew, unless the record is long."));
        var dist = new Pearson3(final.Mean, final.Sd, gFinal);

        var tables = new List<ToolTable>();
        int nYears = hStart is { } hh ? peaks.Max(x => x.Year) - (int)hh + 1 : sys.Count;
        tables.Add(new("Record", ["Item", "Value"],
        [
            ["Systematic peaks", nSys], ["Systematic period", $"{sys.First().Year}–{sys.Last().Year}"],
            ["Historical peaks", peaks.Count(x => x.Historic)],
            ["Historical period", hStart is { } hs2 ? $"{(int)hs2}–{peaks.Max(x => x.Year)}, threshold {Fmt(hT!.Value)} {Qu}, {nUnrecorded} unrecorded years below it" : "none"],
            ["Censored or interval peaks", peaks.Count(x => !x.Exact)], ["Zero flows", sys.Count(x => x.Exact && x.Lo == 0)],
            ["Low-outlier threshold (LOT)", lot > 0 ? $"{Fmt(lot)} {Qu}" : "none"], ["PILFs below the LOT", klow],
            ["Years represented in EMA", obs.Count],
        ]));
        if (mp.Length > 0)
        {
            List<IReadOnlyList<object?>> rows = [];
            for (int i = 0; i < mp.Length; i++)
                rows.Add([i + 1, Fmt(sysExactPos[i]), R(mw[i], 3), mp[i] < 0.0001 ? "< 0.0001" : R(mp[i], 4), i < klow ? "PILF" : ""]);
            tables.Add(new("Multiple Grubbs–Beck test", ["Rank (low to high)", $"Peak ({Qu})", "ω = (log x − mean above)/sd above", "p-value", "Result"], rows,
                ["Cohn et al. (2013): a value is a PILF when its p-value is below 0.005 (any rank up to n/2), or below 0.10 in an unbroken run from the lowest value. The LOT is the smallest peak kept."]));
        }
        tables.Add(new("Log-Pearson III parameters (log10 Q)", ["Item", "Value"],
        [
            ["Mean (EMA, station skew)", R(st.Mean, 4)], ["Standard deviation (EMA, station skew)", R(st.Sd, 4)], ["Station skew (EMA)", R(st.Skew, 3)],
            ["Station skew MSE (Bulletin 17B Eq. 6, N = systematic years)", R(mseS, 4)],
            ["Regional skew", gr is { } g1 ? R(g1, 3) : "none"], ["Regional skew MSE", grSe is { } se ? R(se * se, 4) : "none"],
            ["Weighted skew", gw is { } g2 ? R(g2, 3) : "—"], ["Skew used", $"{R(gFinal, 3)} ({skewMode})"],
            ["Mean (final)", R(final.Mean, 4)], ["Standard deviation (final)", R(final.Sd, 4)], ["EMA iterations", final.Iterations],
        ], ["The final mean and standard deviation are re-estimated by EMA with the skew held at the value used."]));

        var freq = new List<IReadOnlyList<object?>>();
        var curve = new List<(double, double)>();
        var lowC = new List<(double, double)>();
        var upC = new List<(double, double)>();
        foreach (double aep in Aeps)
        {
            double k = Pearson3.K(gFinal, 1 - aep), q = Math.Pow(10, final.Mean + k * final.Sd);
            var ci = ConfidenceK(k, nSys, conf);
            double? ql = ci is { } c1 ? Math.Pow(10, final.Mean + c1.Lower * final.Sd) : null, qh = ci is { } c2 ? Math.Pow(10, final.Mean + c2.Upper * final.Sd) : null;
            freq.Add([aep, R(1 / aep, aep > 0.4 ? 2 : 0), R(k, 4), Sig(q), ql is { } a ? Sig(a) : null, qh is { } b ? Sig(b) : null]);
        }
        foreach (double aep in new[] { 0.99, 0.95, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2, 0.15, 0.1, 0.07, 0.05, 0.04, 0.03, 0.02, 0.01, 0.007, 0.005, 0.003, 0.002 })
        {
            double k = Pearson3.K(gFinal, 1 - aep);
            curve.Add((1 / aep, Math.Pow(10, final.Mean + k * final.Sd)));
            if (ConfidenceK(k, nSys, conf) is { } c)
            {
                lowC.Add((1 / aep, Math.Pow(10, final.Mean + c.Lower * final.Sd)));
                upC.Add((1 / aep, Math.Pow(10, final.Mean + c.Upper * final.Sd)));
            }
        }
        int cpct = (int)Math.Round(conf * 100);
        tables.Add(new("Frequency curve", ["AEP", "Return period (yr)", "K", $"Q ({Qu})", $"{cpct} % lower ({Qu})", $"{cpct} % upper ({Qu})"], freq,
            [$"Q = 10^(mean + K·sd), K the Pearson III frequency factor for the skew used. Confidence limits by the Bulletin 17B approximation with N = {nSys} systematic years (D-362); PeakFQ computes EMA-based limits, so its values will differ."]));

        // Plotting positions (Hirsch–Stedinger with a historical threshold, Weibull otherwise) for exact peaks.
        var exact = peaks.Where(x => x.Exact && x.Lo > 0).ToList();
        var pp = new Dictionary<int, double>();
        if (hStart is not null)
        {
            var above = exact.Where(x => x.Lo >= hT!.Value).OrderByDescending(x => x.Lo).ToList();
            var below = exact.Where(x => x.Lo < hT!.Value && !x.Historic).OrderByDescending(x => x.Lo).ToList();
            double pe = above.Count / (double)nYears;
            for (int i = 0; i < above.Count; i++) pp[above[i].Year] = pe * (i + 1) / (above.Count + 1.0);
            int nb = sys.Count(x => !(x.Exact && x.Lo >= hT!.Value));
            for (int i = 0; i < below.Count; i++) pp[below[i].Year] = pe + (1 - pe) * (i + 1) / (nb + 1.0);
        }
        else
        {
            var ordered = exact.OrderByDescending(x => x.Lo).ToList();
            for (int i = 0; i < ordered.Count; i++) pp[ordered[i].Year] = (i + 1) / (sys.Count + 1.0);
        }
        List<IReadOnlyList<object?>> peakRows = [.. peaks.OrderBy(x => x.Year).Select(x => (IReadOnlyList<object?>)[x.Year, x.Text, x.Historic ? "H" : "",
            pp.TryGetValue(x.Year, out double ap) ? R(ap, 4) : null, notes.GetValueOrDefault(x.Year, "")])];
        tables.Add(new("Annual peaks", ["Year", $"Peak ({Qu})", "Code", "Plotting position (AEP)", "Treatment"], peakRows,
            [hStart is null ? "Weibull plotting positions i/(n + 1) over the systematic years." : "Hirsch–Stedinger plotting positions with the historical threshold (a = 0)."]));

        var obsPts = exact.Where(x => pp.ContainsKey(x.Year)).Select(x => (1 / pp[x.Year], x.Lo)).OrderBy(t => t.Item1).ToList();
        var pilfPts = exact.Where(x => !x.Historic && x.Lo < lot && pp.ContainsKey(x.Year)).Select(x => (1 / pp[x.Year], x.Lo)).ToList();
        var series = new List<ChartSeries> { new("LP3 frequency curve", curve) };
        if (lowC.Count > 0) { series.Add(new($"{cpct} % lower", lowC, Dashed: true)); series.Add(new($"{cpct} % upper", upC, Dashed: true)); }
        series.Add(new("Observed peaks", obsPts.Where(o => !pilfPts.Contains(o)).ToList(), MarkersOnly: true));
        if (pilfPts.Count > 0) series.Add(new("PILFs (censored in the fit)", pilfPts, MarkersOnly: true));
        var chart = new ToolChart("Flood frequency curve", "Return period (yr)", $"Peak flow ({Qu})", series) { LogX = true, LogY = true };

        double Q(double aep) => Math.Pow(10, dist.Quantile(1 - aep));
        var summary = new List<string>
        {
            $"{nSys} systematic peaks{(peaks.Any(x => x.Historic) ? $", {peaks.Count(x => x.Historic)} historical" : "")}{(klow > 0 ? $", {klow} PILF(s) below {Fmt(lot)} {Qu}" : "")}; skew used {gFinal:F3} ({skewMode}).",
            $"Q10 = {Sig(Q(0.1))}, Q50 = {Sig(Q(0.02))}, Q100 = {Sig(Q(0.01))}, Q500 = {Sig(Q(0.002))} {Qu}.",
        };
        msgs.Add(CalcMessage.Info("I-FF-002", "Bulletin 17C: use the frequency curve for gaged sites with at least about 10 years of record; for ungaged Rapid City basins, use the IDCM hydrology methods or USGS regional regression, and compare."));
        return new ToolResult(title, summary, tables, msgs) { Charts = [chart] };
    }

    private static double R(double v, int d) => Math.Round(v, d) + 0.0;
    /// <summary>Three significant figures, as flows are reported.</summary>
    public static double Sig(double q)
    {
        if (!(q > 0)) return 0;
        int e = (int)Math.Floor(Math.Log10(q)) - 2;
        double f = Math.Pow(10, e);
        return Math.Round(Math.Round(q / f) * f, Math.Max(0, -e));
    }
    private static string Fmt(double q) => Sig(q).ToString("#,0.###", CultureInfo.InvariantCulture);
}
