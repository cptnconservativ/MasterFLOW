using System.Globalization;
using Avalonia.Data.Converters;
using MasterFlow.Application;
using MasterFlow.Domain;

namespace MasterFlow.Desktop.Controls;

/// <summary>Human-readable names for enum values shown in combo boxes.</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public static readonly EnumDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        MasterFlow.Application.Tools.IDesignTool t => MasterFlow.Application.Licensing.PremiumFeatures.Label(t),
        UnitSystem.USCustomary => "U.S. customary (ft, cfs)",
        UnitSystem.SI => "SI (m, m³/s)",
        SolveFor.Discharge => "Discharge Q",
        SolveFor.NormalDepth => "Normal depth yn",
        SolveFor.Slope => "Channel slope S",
        SolveFor.Roughness => "Manning's n",
        RatingTableMode.DischargeFromDepth => "Depth → discharge",
        RatingTableMode.DepthFromDischarge => "Discharge → normal depth",
        SectionShape.Circular => "Circular pipe",
        SectionShape.Elliptical => "Elliptical pipe",
        EllipticalPipeSize e => $"{e.EquivalentRound} in equiv. ({e.RiseIn:0.##} × {e.SpanIn:0.##} in)",
        SectionShape.Irregular => "Irregular (station/elevation)",
        SectionShape s => s.ToString(),
        CompositeRoughnessMethod.Conveyance => "Conveyance subdivision (HEC-RAS)",
        CompositeRoughnessMethod.EqualVelocity => "Equal velocity (Horton–Einstein)",
        SlopeUnit.Percent => "%",
        SlopeUnit.RiseOverRun => "ft/ft (m/m)",
        CompositeRoughnessMethod.PerSegment => "Per-segment conveyance",
        TailwaterRule.FreeOutfall => "Free outfall (critical depth)",
        TailwaterRule.Elevation => "Known tailwater elevation",
        TailwaterRule.HalfCriticalPlusDiameter => "(yc + D)/2 above outlet invert",
        TailwaterRule.Crown => "Outlet pipe crown",
        _ => value?.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
