using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Hourly dry-weather flow patterns: 24 multipliers of the average daily flow, one per clock hour (hour 0 = midnight to
/// 1 AM), applied as steps the way EPA SWMM applies an HOURLY pattern. Used by the wet-weather and bypass pumping tools.
/// </summary>
public static class SanitaryDiurnal
{
    /// <summary>
    /// MasterFlow's default residential pattern: low of 0.40 at 4 AM, morning peak 1.50 at 9 AM, evening peak 1.30 at
    /// 8 PM, mean 1.000. It has the usual shape of a residential collection-system meter record (low before dawn, a
    /// morning and an evening peak) and is meant to be replaced by metered data (D-761).
    /// </summary>
    public static readonly double[] Residential =
    [
        0.65, 0.53, 0.45, 0.41, 0.40, 0.45, 0.66, 1.10, 1.42, 1.50, 1.44, 1.34,
        1.25, 1.16, 1.08, 1.03, 1.05, 1.13, 1.23, 1.29, 1.30, 1.23, 1.06, 0.84,
    ];

    /// <summary>Default commercial / public pattern: business hours 8 AM to 4 PM at 1.55 to 1.78, nights at 0.4 to 0.5; mean 1.000.</summary>
    public static readonly double[] Commercial =
    [
        0.43, 0.40, 0.40, 0.40, 0.40, 0.45, 0.65, 1.15, 1.55, 1.75, 1.78, 1.75,
        1.62, 1.71, 1.62, 1.55, 1.40, 1.15, 0.90, 0.75, 0.65, 0.57, 0.50, 0.47,
    ];

    /// <summary>Default industrial pattern: a flat 1.0 (one- to three-shift operations vary too much for a default).</summary>
    public static readonly double[] Flat = Enumerable.Repeat(1.0, 24).ToArray();

    /// <summary>The default pattern for a land use.</summary>
    public static double[] For(SanitaryLandUse use) => use switch
    {
        SanitaryLandUse.Residential or SanitaryLandUse.MultiFamily => Residential,
        SanitaryLandUse.Commercial or SanitaryLandUse.Public => Commercial,
        _ => Flat,
    };

    /// <summary>Multiplier at clock time <paramref name="hour"/> (any real number; wraps every 24 h), stepwise by hour.</summary>
    public static double At(IReadOnlyList<double> pattern, double hour)
    {
        double h = hour % 24; if (h < 0) h += 24;
        return pattern[Math.Min(23, (int)Math.Floor(h))];
    }

    /// <summary>
    /// Reads a pattern key: blank gives <paramref name="fallback"/>; <c>flat</c>, <c>residential</c> or <c>commercial</c>
    /// name a default; otherwise 24 comma-separated multipliers, rescaled to a mean of 1 (with a note) if they are not.
    /// </summary>
    public static double[] Parse(string? text, double[] fallback, string key, List<CalcMessage> msgs, string code)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        string t = text.Trim().ToLowerInvariant();
        if (t.StartsWith("res")) return Residential;
        if (t.StartsWith("com") || t.StartsWith("pub")) return Commercial;
        if (t.StartsWith("flat") || t.StartsWith("const")) return Flat;
        var parts = text.Split([',', ' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries);
        var v = new List<double>();
        foreach (var s in parts)
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && x >= 0) v.Add(x);
            else { msgs.Add(CalcMessage.Error(code, $"{key}: \"{s}\" is not a multiplier (a number ≥ 0).")); return fallback; }
        if (v.Count != 24) { msgs.Add(CalcMessage.Error(code, $"{key}: give 24 hourly multipliers (midnight–1 AM first), not {v.Count}.")); return fallback; }
        double mean = v.Average();
        if (!(mean > 0)) { msgs.Add(CalcMessage.Error(code, $"{key}: the multipliers cannot all be zero.")); return fallback; }
        if (Math.Abs(mean - 1) > 0.005)
        {
            msgs.Add(CalcMessage.Info(code.Replace("E-", "I-"), $"{key}: the 24 multipliers average {mean:F3}; they are divided by {mean:F3} so the day's volume equals the average daily flow."));
            return v.Select(x => x / mean).ToArray();
        }
        return v.ToArray();
    }

    /// <summary>The flow-weighted pattern of several loads (each with its own pattern): Σ Qᵢ·pᵢ(h) / Σ Qᵢ.</summary>
    public static double[] Blend(IEnumerable<(double Q, IReadOnlyList<double> Pattern)> parts)
    {
        var list = parts.Where(x => x.Q > 0).ToList();
        double total = list.Sum(x => x.Q);
        if (!(total > 0)) return Flat;
        return Enumerable.Range(0, 24).Select(h => list.Sum(x => x.Q * x.Pattern[h]) / total).ToArray();
    }

    /// <summary>Clock text for a decimal hour, e.g. 13.5 → "13:30".</summary>
    public static string Clock(double hour)
    {
        double h = hour % 24; if (h < 0) h += 24;
        int m = (int)Math.Round(h * 60) % 1440;
        return $"{m / 60:00}:{m % 60:00}";
    }

    /// <summary>Reads a clock time ("22:00", "10 PM", "22.5") as decimal hours; null when unreadable.</summary>
    public static double? ParseClock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().ToLowerInvariant().Replace(" ", "");
        bool pm = t.EndsWith("pm"), am = t.EndsWith("am");
        if (pm || am) t = t[..^2];
        double h;
        if (t.Contains(':'))
        {
            var a = t.Split(':');
            if (a.Length != 2 || !int.TryParse(a[0], out int hh) || !int.TryParse(a[1], out int mm) || mm is < 0 or > 59) return null;
            h = hh + mm / 60.0;
        }
        else if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out h)) return null;
        if (pm || am) { if (h is < 1 or >= 13) return null; h %= 12; if (pm) h += 12; }
        return h is >= 0 and < 24 ? h : null;
    }
}
