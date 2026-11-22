using System.Globalization;
using MasterFlow.Application.Hydrology;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// NOAA Atlas 14 rainfall lookup (Premium): the point precipitation frequency table for a latitude and longitude, from
/// NOAA's PFDS (fetched once and cached) or pasted, turned into depth and intensity tables, IDF curves, a comparison with
/// Rapid City IDCM Table 4-2, and ready-to-paste input lines for the rational, TR-55 and hydrograph tools.
/// </summary>
public sealed class Atlas14Tool : IDesignTool
{
    public string Id => "atlas14-idf";
    public string Name => "NOAA Atlas 14 rainfall (IDF lookup)";
    public string Description => "Point precipitation frequency estimates for any U.S. location from NOAA Atlas 14 (PFDS): fetched by latitude and longitude (fetch = yes; the coordinates are sent to NOAA and the table is cached on this computer) or pasted from the PFDS CSV. Gives depths, intensities, IDF curves, the comparison with Rapid City IDCM Table 4-2, and input lines for the rational method, TR-55 and hydrograph tools.";
    public bool UsesNetwork => false;

    private const string RapidCityPfds = """
        Point precipitation frequency estimates (inches)
        NOAA Atlas 14 Volume 8 Version 2
        Data type: Precipitation depth
        Time series type: Partial duration
        Project area: Midwestern States
        Latitude: 44.0805 Degree
        Longitude: -103.2310 Degree

        PRECIPITATION FREQUENCY ESTIMATES
        by duration for ARI (years):, 1,2,5,10,25,50,100,200,500,1000
        5-min:, 0.281,0.337,0.444,0.546,0.707,0.848,1.00,1.17,1.42,1.63
        10-min:, 0.412,0.494,0.650,0.799,1.04,1.24,1.47,1.72,2.08,2.38
        15-min:, 0.502,0.602,0.792,0.975,1.26,1.51,1.79,2.10,2.54,2.91
        30-min:, 0.680,0.815,1.07,1.32,1.72,2.06,2.44,2.86,3.48,3.98
        60-min:, 0.833,0.994,1.32,1.64,2.16,2.63,3.16,3.74,4.61,5.33
        2-hr:, 0.986,1.17,1.56,1.96,2.61,3.20,3.87,4.63,5.75,6.68
        3-hr:, 1.08,1.28,1.69,2.12,2.86,3.54,4.31,5.18,6.50,7.60
        6-hr:, 1.33,1.52,1.95,2.43,3.26,4.04,4.94,5.97,7.53,8.84
        12-hr:, 1.68,1.88,2.32,2.82,3.68,4.49,5.43,6.51,8.14,9.52
        24-hr:, 2.00,2.28,2.84,3.41,4.34,5.18,6.12,7.18,8.74,10.0
        2-day:, 2.20,2.65,3.44,4.16,5.24,6.14,7.10,8.14,9.60,10.8
        3-day:, 2.37,2.84,3.67,4.42,5.53,6.45,7.43,8.48,9.97,11.2
        4-day:, 2.55,3.01,3.83,4.57,5.68,6.60,7.59,8.65,10.2,11.4
        7-day:, 3.05,3.51,4.34,5.08,6.18,7.09,8.06,9.11,10.6,11.8
        10-day:, 3.49,4.00,4.89,5.67,6.81,7.75,8.73,9.77,11.2,12.4
        20-day:, 4.69,5.42,6.64,7.65,9.06,10.1,11.2,12.4,13.9,15.0
        30-day:, 5.67,6.59,8.07,9.28,10.9,12.1,13.3,14.5,16.0,17.2
        45-day:, 6.90,8.03,9.80,11.2,13.1,14.4,15.7,16.9,18.5,19.6
        60-day:, 7.96,9.22,11.2,12.7,14.7,16.2,17.5,18.8,20.3,21.3
        """;

    public string SampleInput(DesignToolContext context) => """
        # NOAA Atlas 14 rainfall, U.S. units (in, in/h, min).
        # Online: give latitude and longitude and set fetch = yes. MasterFlow sends only the coordinates to NOAA's
        # Precipitation Frequency Data Server and keeps the table on this computer, so later runs use the same numbers
        # (refresh = yes fetches again). Offline: paste the PFDS "Download: CSV" text under [PFDS].
        latitude = 44.0805
        longitude = -103.2310
        fetch = no
        series = pds                 # pds (partial duration) or ams (annual maximum)
        storms = 2, 5, 10, 25, 50, 100
        compare = rapid city         # compare with IDCM Table 4-2; "none" to skip

        [PFDS]
        """ + "\n" + RapidCityPfds;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "NOAA Atlas 14 point precipitation frequency";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-A14-001", "Atlas 14 tables are read in inches; switch the project to U.S. units."));
        var msgs = new List<CalcMessage>();
        // The [PFDS] block is raw CSV, not a tool table: take everything after the heading as text.
        string text = input ?? "";
        int at = text.IndexOf("[PFDS]", StringComparison.OrdinalIgnoreCase);
        string pasted = at >= 0 ? text[(at + 6)..] : "";
        var p = ToolInput.Parse(at >= 0 ? text[..at] : text);
        double? lat = p.OptionalNumber("latitude"), lon = p.OptionalNumber("longitude");
        string series = (p.Text("series") ?? "pds").ToLowerInvariant();
        if (series is not ("pds" or "ams")) msgs.Add(CalcMessage.Error("E-A14-002", "series must be pds (partial duration) or ams (annual maximum)."));
        if (lat is { } la && (la < -15 || la > 72) || lon is { } lo && (lo < -180 || lo > -60 && lo < 130))
            msgs.Add(CalcMessage.Error("E-A14-003", "latitude and longitude must be decimal degrees in the Atlas 14 area (U.S. and territories; west longitudes negative)."));
        var years = (p.Text("storms") ?? "2, 5, 10, 25, 50, 100").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s.Replace("-yr", ""), out int y) ? y : -1).ToList();
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        Atlas14Table? table; string? err; string sourceNote;
        if (pasted.Trim().Length > 0)
        {
            table = Atlas14.Parse(pasted, "pasted PFDS CSV", out err);
            sourceNote = "PFDS CSV pasted into the tool";
        }
        else if (p.Flag("fetch", false))
        {
            if (lat is null || lon is null) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-A14-004", "fetch = yes needs latitude and longitude.")]);
            var (t, ferr) = Atlas14.Get(lat.Value, lon.Value, series, p.Flag("refresh", false), out bool cached);
            if (t is null) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-A14-005", $"Could not reach NOAA's PFDS ({ferr}). Open https://hdsc.nws.noaa.gov/pfds/ , pick the point, use \"Download: CSV\" and paste the text under [PFDS].")]);
            table = Atlas14.Parse(t, "NOAA PFDS", out err);
            sourceNote = cached ? $"NOAA PFDS, cached copy ({Atlas14.CacheFile(lat.Value, lon.Value, series)}); refresh = yes fetches again" : $"NOAA PFDS, fetched {DateTime.Now:yyyy-MM-dd} and cached";
            msgs.Add(CalcMessage.Info("I-A14-001", cached ? "Atlas 14 table read from this computer's cache; nothing was sent." : $"Atlas 14 table fetched from NOAA for {lat:F4}, {lon:F4} (only the coordinates were sent) and cached."));
        }
        else return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-A14-006", "Paste the PFDS CSV under [PFDS], or give latitude and longitude with fetch = yes.")]);
        if (table is null) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-A14-007", err ?? "The PFDS text could not be read.")]);

        var useYears = years.Where(y => table.Ari(y) >= 0).ToList();
        foreach (var y in years.Where(y => table.Ari(y) < 0)) msgs.Add(CalcMessage.Warning("W-A14-001", $"The table has no {y}-yr column (it has {string.Join(", ", table.Aris)})."));
        if (useYears.Count == 0) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-A14-008", "None of the requested storms are in the table.")]);
        if (table.Series != (series == "ams" ? "annual maximum" : "partial duration") && pasted.Length > 0)
            msgs.Add(CalcMessage.Info("I-A14-002", $"The pasted table is the {table.Series} series."));

        // Depth and intensity tables (all durations, chosen storms).
        var depthRows = new List<IReadOnlyList<object?>>(); var intRows = new List<IReadOnlyList<object?>>();
        for (int i = 0; i < table.Durations.Count; i++)
        {
            var (label, mins) = table.Durations[i];
            depthRows.Add([label, mins, .. useYears.Select(y => (object?)table.Depth[i, table.Ari(y)])]);
            if (mins <= 1440) intRows.Add([label, mins, .. useYears.Select(y => (object?)(table.Depth[i, table.Ari(y)] / (mins / 60)))]);
        }
        var tables = new List<ToolTable>
        {
            new($"Precipitation depth (in), {table.Series} series", ["Duration", "Minutes", .. useYears.Select(y => $"{y}-yr")], depthRows,
                [$"Source: {sourceNote}.", .. table.HeaderLines.Where(h => h.Contains(':') || h.Contains("Atlas", StringComparison.OrdinalIgnoreCase)).Take(8)]),
            new("Rainfall intensity (in/h) = depth / duration", ["Duration", "Minutes", .. useYears.Select(y => $"{y}-yr")], intRows),
        };
        if (table.Lower is not null && table.Upper is not null)
        {
            var ci = new List<IReadOnlyList<object?>>();
            for (int i = 0; i < table.Durations.Count; i++)
                foreach (var y in useYears) { int j = table.Ari(y); ci.Add([table.Durations[i].Label, $"{y}-yr", table.Lower[i, j], table.Depth[i, j], table.Upper[i, j]]); }
            tables.Add(new("90 % confidence interval (in)", ["Duration", "Storm", "Lower", "Estimate", "Upper"], ci));
        }

        // IDF chart (log-log), 5 min to 24 h.
        var series2 = useYears.Select((y, k) => new ChartSeries($"{y}-yr", table.Durations.Where(d => d.Minutes <= 1440)
            .Select(d => (d.Minutes, table.DepthAt(y, d.Minutes) / (d.Minutes / 60))).ToList(), Color: k)).ToList();
        var charts = new List<ToolChart> { new("IDF curves (Atlas 14)", "Duration (min)", "Intensity (in/h)", series2) { LogX = true, LogY = true } };

        // Comparison with IDCM Table 4-2.
        bool compare = !(p.Text("compare") ?? "rapid city").StartsWith("none", StringComparison.OrdinalIgnoreCase);
        if (compare)
        {
            var cmp = new List<IReadOnlyList<object?>>();
            var cmpSeries = new List<ChartSeries>();
            int k = 0;
            foreach (var (y, d) in DesignStorms.RapidCityDepths.OrderBy(x => x.Key))
            {
                if (table.Ari(y) < 0) continue;
                for (int i = 0; i < DesignStorms.RapidCityDurations.Length; i++)
                {
                    double m = DesignStorms.RapidCityDurations[i], a = table.DepthAt(y, m);
                    cmp.Add([$"{y}-yr", m, d[i], a, (a - d[i]) / d[i] * 100]);
                }
                cmpSeries.Add(new($"{y}-yr IDCM Table 4-2", DesignStorms.RapidCityDurations.Select((m, i) => (m, d[i])).ToList(), Dashed: true, Color: k));
                cmpSeries.Add(new($"{y}-yr Atlas 14", DesignStorms.RapidCityDurations.Select(m => (m, table.DepthAt(y, m))).ToList(), Color: k));
                k++;
            }
            if (cmp.Count > 0)
            {
                tables.Add(new("Comparison with Rapid City IDCM Table 4-2", ["Storm", "Duration (min)", "IDCM (in)", "Atlas 14 (in)", "Difference (%)"], cmp,
                    ["IDCM Table 4-2 is based on NOAA (1977) IDF data; NOAA Atlas 14 Vol. 8 (2013) is NOAA's current estimate for South Dakota.", "Atlas 14 depths are log–log interpolated where its durations differ."]));
                charts.Add(new ToolChart("Depth–duration: Atlas 14 vs IDCM Table 4-2", "Duration (min)", "Depth (in)", cmpSeries) { LogX = true, LogY = true });
                double worst = cmp.Max(r => Math.Abs((double)r[4]!));
                msgs.Add(CalcMessage.Info("I-A14-003", $"Rapid City IDCM §4.2.4 and §4.3.2 require Table 4-2 for City design; Atlas 14 differs from it by up to {worst:F0} % here. Use Atlas 14 where the reviewing agency asks for it, or to show sensitivity."));
            }
        }

        // Ready-to-paste inputs for other tools.
        var paste = new List<IReadOnlyList<object?>>();
        var rationalYears = useYears.Where(y => y is 2 or 5 or 10 or 100).ToList();
        if (rationalYears.Count > 0)
        {
            paste.Add(["Rational method", "[IDF]"]);
            paste.Add(["Rational method", "values = depth"]);
            paste.Add(["Rational method", "Duration (min)\t" + string.Join("\t", rationalYears.Select(y => $"{y}-yr"))]);
            foreach (var d in table.Durations.Where(d => d.Minutes <= 1440))
                paste.Add(["Rational method", Fmt(d.Minutes) + "\t" + string.Join("\t", rationalYears.Select(y => Fmt(table.DepthAt(y, d.Minutes))))]);
        }
        foreach (var y in useYears) paste.Add(["TR-55 / hydrograph routing (24-h depth)", $"P{y} = {Fmt(table.DepthAt(y, 1440))}"]);
        tables.Add(new("Input lines for other tools", ["Tool", "Line (copy into that tool's input)"], paste,
            ["Rational method: the lines replace IDCM Table 4-2 (storms 2, 5, 10, 100 only, which have Table 4-1 C values).", "TR-55 and Hydrograph routing (rainfall = type-ii): the P lines set the 24-h depths."]));

        var summary = new List<string>
        {
            $"{sourceNote}; {table.Series} series; {table.Durations.Count} durations × {table.Aris.Count} return periods.",
            "24-h depths: " + string.Join(", ", useYears.Select(y => $"{y}-yr {table.DepthAt(y, 1440):F2} in")) + ".",
            "1-h intensities: " + string.Join(", ", useYears.Select(y => $"{y}-yr {table.DepthAt(y, 60):F2} in/h")) + ".",
        };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };

        static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
