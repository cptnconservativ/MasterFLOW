using System.Globalization;
using System.Text.RegularExpressions;

namespace MasterFlow.Application.Hydrology;

/// <summary>Point precipitation frequency estimates from NOAA Atlas 14 (PFDS): depths (in) by duration (min) and ARI (yr).</summary>
public sealed record Atlas14Table(string Source, IReadOnlyList<string> HeaderLines, IReadOnlyList<(string Label, double Minutes)> Durations, IReadOnlyList<int> Aris,
    double[,] Depth, double[,]? Lower, double[,]? Upper, string Series)
{
    public int Ari(int years) => Aris.ToList().IndexOf(years);

    /// <summary>Depth for <paramref name="years"/> at <paramref name="minutes"/>, log–log between tabulated durations; NaN outside.</summary>
    public double DepthAt(int years, double minutes)
    {
        int j = Ari(years);
        if (j < 0) return double.NaN;
        var d = Durations.Select(x => x.Minutes).ToArray();
        var v = Enumerable.Range(0, d.Length).Select(i => Depth[i, j]).ToArray();
        return DesignStorms.DepthAt(d, v, minutes);
    }
}

/// <summary>
/// Reads the CSV that NOAA's Precipitation Frequency Data Server (PFDS) returns for a point, and fetches it. The format is
/// a few header lines ("Data type: Precipitation depth", "Latitude: …"), then "PRECIPITATION FREQUENCY ESTIMATES",
/// a line "by duration for ARI (years):, 1,2,5,10,25,50,100,200,500,1000" and one line per duration ("5-min:, 0.33,…"),
/// optionally followed by the lower and upper 90 % confidence limit blocks in the same layout.
/// </summary>
public static partial class Atlas14
{
    /// <summary>PFDS text endpoint (mean estimates, depth, English units). {0} lat, {1} lon, {2} series (pds or ams).</summary>
    public const string UrlFormat = "https://hdsc.nws.noaa.gov/cgi-bin/hdsc/new/fe_text_mean.csv?lat={0}&lon={1}&data=depth&units=english&series={2}";

    /// <summary>
    /// Fetches the PFDS CSV (returns null and a reason on failure). Replaceable so tests never reach the network.
    /// </summary>
    public static Func<double, double, string, (string? Text, string? Error)> Fetcher { get; set; } = DefaultFetch;

    /// <summary>Folder where fetched tables are kept so a project re-runs offline with the same numbers.</summary>
    public static string CacheFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "MasterFlow", "atlas14");

    public static string CacheFile(double lat, double lon, string series) =>
        Path.Combine(CacheFolder, string.Create(CultureInfo.InvariantCulture, $"{lat:F4}_{lon:F4}_{series}.csv"));

    private static (string?, string?) DefaultFetch(double lat, double lon, string series)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MasterFlow");
            string url = string.Format(CultureInfo.InvariantCulture, UrlFormat, lat, lon, series);
            var text = http.GetStringAsync(url).GetAwaiter().GetResult();
            return (text, null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    /// <summary>Text from the cache, else from <see cref="Fetcher"/> (and then cached). <paramref name="fromCache"/> tells which.</summary>
    public static (string? Text, string? Error) Get(double lat, double lon, string series, bool refresh, out bool fromCache)
    {
        string file = CacheFile(lat, lon, series);
        fromCache = false;
        if (!refresh)
            try { if (File.Exists(file)) { fromCache = true; return (File.ReadAllText(file), null); } } catch { /* fetch instead */ }
        var (text, err) = Fetcher(lat, lon, series);
        if (text is not null && Parse(text, "online", out _) is not null)
            try { Directory.CreateDirectory(CacheFolder); File.WriteAllText(file, text); } catch { /* cache is optional */ }
        return (text, err);
    }

    /// <summary>Duration label to minutes: "5-min", "2-hr", "24-hr", "2-day", "60-day"; NaN if not a duration.</summary>
    public static double Minutes(string label)
    {
        var m = DurRx().Match(label.Trim());
        if (!m.Success) return double.NaN;
        double n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch { "min" => n, "hr" or "hour" => n * 60, _ => n * 1440 };
    }

    /// <summary>Parses PFDS CSV text (as downloaded, or lines pasted into the tool). Null with a reason when no estimates are found.</summary>
    public static Atlas14Table? Parse(string text, string source, out string? error)
    {
        error = null;
        var header = new List<string>();
        var durations = new List<(string, double)>();
        List<int>? aris = null;
        var blocks = new List<List<double[]>> { new(), new(), new() };
        int block = -1;
        string series = "partial duration";
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var upper = line.ToUpperInvariant();
            if (upper.StartsWith("PRECIPITATION FREQUENCY ESTIMATES")) { block = 0; continue; }
            if (upper.StartsWith("LOWER LIMIT")) { block = 1; continue; }
            if (upper.StartsWith("UPPER LIMIT")) { block = 2; continue; }
            var cells = line.Split(',').Select(c => c.Trim()).ToArray();
            if (cells[0].StartsWith("by duration for ARI", StringComparison.OrdinalIgnoreCase) || cells[0].StartsWith("by duration for AEP", StringComparison.OrdinalIgnoreCase))
            {
                if (block < 0) block = 0;
                if (cells[0].Contains("AEP", StringComparison.OrdinalIgnoreCase)) series = "annual maximum";
                var a = cells.Skip(1).Where(c => c.Length > 0).Select(c => AriFrom(c)).ToList();
                if (aris is null) aris = a;
                continue;
            }
            double mins = Minutes(cells[0].TrimEnd(':'));
            if (!double.IsNaN(mins) && block >= 0 && aris is not null)
            {
                var vals = cells.Skip(1).Where(c => c.Length > 0).Select(c => double.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN).ToArray();
                if (block == 0) durations.Add((cells[0].TrimEnd(':'), mins));
                blocks[block].Add(vals);
                continue;
            }
            if (block < 0) header.Add(line.TrimEnd(','));
            if (upper.StartsWith("TIME SERIES TYPE") && upper.Contains("ANNUAL")) series = "annual maximum";
        }
        if (aris is null || durations.Count == 0) { error = "No precipitation frequency estimates found (expected the PFDS CSV: \"by duration for ARI (years):\" then lines like \"5-min:, 0.33, …\")."; return null; }
        if (aris.Any(a => a <= 0)) { error = "Could not read the ARI (years) header."; return null; }
        double[,] Grid(List<double[]> rows)
        {
            var g = new double[durations.Count, aris.Count];
            for (int i = 0; i < durations.Count; i++)
                for (int j = 0; j < aris.Count; j++) g[i, j] = i < rows.Count && j < rows[i].Length ? rows[i][j] : double.NaN;
            return g;
        }
        var depth = Grid(blocks[0]);
        for (int i = 0; i < durations.Count; i++)
            for (int j = 0; j < aris.Count; j++)
                if (!(depth[i, j] > 0)) { error = $"{durations[i].Item1}, {aris[j]}-yr: depth missing or not positive."; return null; }
        return new Atlas14Table(source, header, durations, aris, depth, blocks[1].Count == durations.Count ? Grid(blocks[1]) : null,
            blocks[2].Count == durations.Count ? Grid(blocks[2]) : null, series);
    }

    /// <summary>"10" → 10; AEP headers ("1/10" or "10%") → the equivalent return period.</summary>
    private static int AriFrom(string c)
    {
        c = c.Trim();
        if (c.StartsWith("1/") && int.TryParse(c[2..], out int r)) return r;
        if (c.EndsWith('%') && double.TryParse(c.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct) && pct > 0) return (int)Math.Round(100 / pct);
        return int.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ? y : -1;
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*-?\s*(min|hr|hour|day)s?$", RegexOptions.IgnoreCase)]
    private static partial Regex DurRx();
}
