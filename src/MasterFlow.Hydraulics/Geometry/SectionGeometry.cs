using MasterFlow.Domain;

namespace MasterFlow.Hydraulics.Geometry;

/// <summary>
/// Depth-dependent geometric properties of a prismatic cross-section. Depth y is measured vertically
/// from the lowest point of the section.
/// </summary>
public interface ISectionGeometry
{
    /// <summary>Flow area A(y).</summary>
    double Area(double depth);

    /// <summary>Wetted perimeter P(y) (bed and walls in contact with water; the free surface is excluded).</summary>
    double WettedPerimeter(double depth);

    /// <summary>Free-surface (top) width T(y) = dA/dy.</summary>
    double TopWidth(double depth);

    /// <summary>
    /// True when Manning discharge Q(y) increases strictly with depth for all y &gt; 0, which holds for
    /// sections whose walls never converge upward (rectangular, trapezoidal, triangular). Closed conduits
    /// do not satisfy this near the crown.
    /// </summary>
    bool DischargeIsMonotonic { get; }

    /// <summary>Points (offset, elevation) describing the section outline up to a given height, for plotting.</summary>
    IReadOnlyList<(double X, double Y)> Outline(double height);

    /// <summary>Left and right free-surface offsets at depth y, in the same frame as <see cref="Outline"/>(height).</summary>
    (double Left, double Right) WaterSurfaceExtent(double depth, double height);
}

/// <summary>
/// General trapezoid with bottom width b and independent side slopes zL, zR (H:V). Rectangular
/// (zL = zR = 0) and triangular (b = 0) channels are exact special cases.
///   A = y (b + (zL + zR) y / 2)
///   P = b + y (√(1 + zL²) + √(1 + zR²))
///   T = b + (zL + zR) y
/// Reference: V. T. Chow, Open-Channel Hydraulics, 1959, Table 2-1.
/// </summary>
public sealed class TrapezoidGeometry : ISectionGeometry
{
    public TrapezoidGeometry(double bottomWidth, double leftSideSlope, double rightSideSlope)
    {
        BottomWidth = bottomWidth;
        LeftSideSlope = leftSideSlope;
        RightSideSlope = rightSideSlope;
        _wallFactor = Math.Sqrt(1 + leftSideSlope * leftSideSlope) + Math.Sqrt(1 + rightSideSlope * rightSideSlope);
    }

    private readonly double _wallFactor;

    public double BottomWidth { get; }
    public double LeftSideSlope { get; }
    public double RightSideSlope { get; }

    public bool DischargeIsMonotonic => true;

    public double Area(double y) => y <= 0 ? 0 : y * (BottomWidth + 0.5 * (LeftSideSlope + RightSideSlope) * y);
    public double WettedPerimeter(double y) => y <= 0 ? BottomWidth : BottomWidth + y * _wallFactor;
    public double TopWidth(double y) => BottomWidth + (LeftSideSlope + RightSideSlope) * Math.Max(0, y);

    public IReadOnlyList<(double X, double Y)> Outline(double height) =>
    [
        (0, height),
        (LeftSideSlope * height, 0),
        (LeftSideSlope * height + BottomWidth, 0),
        (LeftSideSlope * height + BottomWidth + RightSideSlope * height, height),
    ];

    public (double Left, double Right) WaterSurfaceExtent(double depth, double height)
    {
        double h = Math.Max(height, depth);
        double left = LeftSideSlope * (h - depth);
        double right = LeftSideSlope * h + BottomWidth + RightSideSlope * depth;
        return (left, right);
    }

    public static TrapezoidGeometry From(ChannelSection section) => section switch
    {
        RectangularSection r => new TrapezoidGeometry(r.BottomWidth, 0, 0),
        TrapezoidalSection t => new TrapezoidGeometry(t.BottomWidth, t.LeftSideSlope, t.RightSideSlope),
        TriangularSection v => new TrapezoidGeometry(0, v.LeftSideSlope, v.RightSideSlope),
        _ => throw new NotSupportedException($"Section type {section.GetType().Name} is not supported by TrapezoidGeometry."),
    };
}
