using MasterFlow.Domain;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.Application;

/// <summary>
/// Plot-ready geometry for a cross-section at a water depth, in real section coordinates
/// (offset X to the right, elevation Y up, invert at Y = 0). Shared by the desktop view and reports so
/// both draw exactly the same thing.
/// </summary>
public sealed record CrossSectionDrawing(
    IReadOnlyList<(double X, double Y)> Outline,
    IReadOnlyList<(double X, double Y)> Water,
    double? WaterDepth,
    double? CriticalDepth,
    double? ChannelDepth,
    double Width,
    double Height)
{
    public static CrossSectionDrawing Create(ChannelSection section, double? waterDepth, double? criticalDepth = null)
    {
        var g = SectionGeometry.From(section);
        double h;
        double? top = section.ChannelDepth;
        if (double.IsFinite(g.MaxDepth))
        {
            h = g.MaxDepth;
            top = null; // a conduit's crown is drawn by its outline
        }
        else
        {
            h = new[] { section.ChannelDepth ?? 0, (waterDepth ?? 0) * 1.25, (criticalDepth ?? 0) * 1.1, 1e-6 }.Max();
            if (section.ChannelDepth is null && waterDepth is null && g is TrapezoidGeometry t) h = Math.Max(1, t.BottomWidth / 4);
        }
        var outline = g.Outline(h);
        IReadOnlyList<(double, double)> water = waterDepth is > 0 and var y ? g.WaterPolygon(y, h) : [];
        double width = outline.Max(p => p.X) - outline.Min(p => p.X);
        return new CrossSectionDrawing(outline, water, waterDepth, criticalDepth, top, Math.Max(width, 1e-9), h);
    }

    /// <summary>Left/right X of the water surface at an arbitrary elevation (for reference lines).</summary>
    public (double Left, double Right) ExtentAt(ChannelSection section, double depth) =>
        SectionGeometry.From(section).WaterSurfaceExtent(depth, Height);
}
