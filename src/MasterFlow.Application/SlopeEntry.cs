using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application;

/// <summary>
/// Catches a slope typed in the wrong unit. "0.02" meant as 0.02 ft/ft (2 %) is read by the
/// percent field as 0.02 %: a 100× difference that is easy to miss. The warning gives both
/// readings and says how to switch.
/// </summary>
public static class SlopeEntry
{
    public const string Code = "W-S-003";

    /// <summary>Below this typed percent, the value may have been meant as ft/ft (0.05 % is about the flattest storm sewer grade).</summary>
    public const double SuspiciousPercentBelow = 0.05;

    /// <summary>Above this typed ft/ft, the value may have been meant as percent.</summary>
    public const double SuspiciousRatioAbove = 0.2;

    public static CalcMessage? Check(double typed, SlopeUnit unit, string lengthUnit)
    {
        if (!double.IsFinite(typed) || typed <= 0) return null;
        var inv = CultureInfo.InvariantCulture;
        string rr = $"{lengthUnit}/{lengthUnit}";
        if (unit == SlopeUnit.Percent && typed < SuspiciousPercentBelow)
            return CalcMessage.Warning(Code,
                $"Slope check: {typed.ToString("G6", inv)} % is {(typed / 100).ToString("G6", inv)} {rr}, a very flat grade. " +
                $"If you meant {typed.ToString("G6", inv)} {rr} ({(typed * 100).ToString("G6", inv)} %), change the slope unit to {rr}.", "Slope");
        if (unit == SlopeUnit.RiseOverRun && typed > SuspiciousRatioAbove)
            return CalcMessage.Warning(Code,
                $"Slope check: {typed.ToString("G6", inv)} {rr} is {(typed * 100).ToString("G6", inv)} %, a very steep grade. " +
                $"If you meant {typed.ToString("G6", inv)} %, change the slope unit to %.", "Slope");
        return null;
    }
}
