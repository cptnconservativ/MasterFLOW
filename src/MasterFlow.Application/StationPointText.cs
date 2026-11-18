using System.Globalization;
using MasterFlow.Domain;

namespace MasterFlow.Application;

/// <summary>
/// Reads and writes station/elevation points as plain text, one point per line:
///   station, elevation [, n [, description]]
/// The delimiter is detected per line: tab (Excel paste), then semicolon, vertical bar, colon, comma, then spaces.
/// Stations may use surveyor notation "1+25.50" (= 125.50). A first line that is not numeric is
/// treated as a header and skipped. Blank lines and lines starting with '#' are ignored.
/// Nothing is guessed: any line that cannot be read is reported with its line number.
/// </summary>
public static class StationPointText
{
    public sealed record ParseResult(IReadOnlyList<StationPoint> Points, IReadOnlyList<string> Errors);

    public static ParseResult Parse(string? text, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var pts = new List<StationPoint>();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return new(pts, ["No points entered."]);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        bool firstContent = true;
        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cells = Split(line);
            bool okStation = TryStation(cells[0], culture, out double x);
            bool okElev = cells.Length > 1 && TryNumber(cells[1], culture, out _);
            if (firstContent && !okStation && !okElev) { firstContent = false; continue; } // header
            firstContent = false;
            if (cells.Length < 2) { errors.Add($"Line {li + 1}: needs a station and an elevation."); continue; }
            if (!okStation) { errors.Add($"Line {li + 1}: station \"{cells[0]}\" is not a number."); continue; }
            if (!TryNumber(cells[1], culture, out double z)) { errors.Add($"Line {li + 1}: elevation \"{cells[1]}\" is not a number."); continue; }
            double? n = null;
            if (cells.Length > 2 && cells[2].Length > 0)
            {
                if (TryNumber(cells[2], culture, out double nv)) n = nv;
                else { errors.Add($"Line {li + 1}: n \"{cells[2]}\" is not a number."); continue; }
            }
            string? desc = cells.Length > 3 ? string.Join(" ", cells.Skip(3)).Trim() : null;
            pts.Add(new StationPoint(x, z, n, string.IsNullOrEmpty(desc) ? null : desc));
        }
        if (pts.Count == 0 && errors.Count == 0) errors.Add("No points entered.");
        return new(pts, errors);
    }

    public static string Format(IEnumerable<StationPoint> points, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return string.Join(Environment.NewLine, points.Select(p =>
        {
            var cells = new List<string> { p.Station.ToString("G10", culture), p.Elevation.ToString("G10", culture) };
            if (p.ManningN is { } n || p.Description is not null) cells.Add(p.ManningN?.ToString("G6", culture) ?? "");
            if (p.Description is not null) cells.Add(p.Description);
            return string.Join("\t", cells);
        }));
    }

    /// <summary>Comma-separated text with a header, invariant culture (for files other programs read).</summary>
    public static string ToCsv(IEnumerable<StationPoint> points)
    {
        var inv = CultureInfo.InvariantCulture;
        static string Q(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var lines = new List<string> { "Station,Elevation,n,Description" };
        lines.AddRange(points.Select(p => string.Join(",", p.Station.ToString("R", inv), p.Elevation.ToString("R", inv), p.ManningN?.ToString("R", inv) ?? "", Q(p.Description ?? ""))));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string[] Split(string line)
    {
        if (line.Contains('\t')) return line.Split('\t').Select(c => c.Trim()).ToArray();
        if (line.Contains(';')) return line.Split(';').Select(c => c.Trim()).ToArray();
        if (line.Contains('|')) return line.Split('|').Select(c => c.Trim()).ToArray();
        if (line.Contains(':')) return line.Split(':').Select(c => c.Trim()).ToArray();
        // A comma is a delimiter unless the culture uses it as the decimal separator and no spaces separate values.
        if (line.Contains(',') && (CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator != "," || line.Contains(", ")))
            return line.Split(',').Select(c => c.Trim()).ToArray();
        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool TryNumber(string s, CultureInfo culture, out double v) =>
        (double.TryParse(s, NumberStyles.Float, culture, out v) || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) && double.IsFinite(v);

    /// <summary>Plain number, or surveyor stationing "a+b" meaning a × 100 + b (e.g. 1+25.50 = 125.50).</summary>
    private static bool TryStation(string s, CultureInfo culture, out double v)
    {
        int plus = s.IndexOf('+', 1 < s.Length ? 1 : 0);
        if (plus > 0 && TryNumber(s[..plus], culture, out double hundreds) && TryNumber(s[(plus + 1)..], culture, out double rest) && rest >= 0 && rest < 100)
        {
            v = hundreds < 0 || s.StartsWith('-') ? hundreds * 100 - rest : hundreds * 100 + rest;
            return true;
        }
        return TryNumber(s, culture, out v);
    }
}
