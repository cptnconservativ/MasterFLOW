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

    /// <summary>Largest physically meaningful depth (the crown of a conduit); +∞ for open sections.</summary>
    double MaxDepth { get; }

    /// <summary>
    /// True when Manning discharge Q(y) increases strictly with depth over (0, MaxDepth], which holds for
    /// sections whose walls never converge upward (rectangular, trapezoidal, triangular). Closed conduits
    /// do not satisfy this near the crown: Q peaks below the crown and two depths can carry the same Q.
    /// </summary>
    bool DischargeIsMonotonic { get; }

    /// <summary>Points (offset, elevation) of the section outline, for plotting, up to <paramref name="height"/> for open sections.</summary>
    IReadOnlyList<(double X, double Y)> Outline(double height);

    /// <summary>Left and right free-surface offsets at depth y, in the same frame as <see cref="Outline"/>(height).</summary>
    (double Left, double Right) WaterSurfaceExtent(double depth, double height);

    /// <summary>Closed polygon of the wetted area at depth y, in the same frame as <see cref="Outline"/>(height).</summary>
    IReadOnlyList<(double X, double Y)> WaterPolygon(double depth, double height);
}

public static class SectionGeometry
{
    public static ISectionGeometry From(ChannelSection section) => section switch
    {
        CircularSection c => new CircularGeometry(c.Diameter),
        _ => TrapezoidGeometry.From(section),
    };
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

    public double MaxDepth => double.PositiveInfinity;
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

    public IReadOnlyList<(double X, double Y)> WaterPolygon(double depth, double height)
    {
        var (l, r) = WaterSurfaceExtent(depth, height);
        double h = Math.Max(height, depth);
        return [(l, depth), (LeftSideSlope * h, 0), (LeftSideSlope * h + BottomWidth, 0), (r, depth)];
    }

    public static TrapezoidGeometry From(ChannelSection section) => section switch
    {
        RectangularSection r => new TrapezoidGeometry(r.BottomWidth, 0, 0),
        TrapezoidalSection t => new TrapezoidGeometry(t.BottomWidth, t.LeftSideSlope, t.RightSideSlope),
        TriangularSection v => new TrapezoidGeometry(0, v.LeftSideSlope, v.RightSideSlope),
        _ => throw new NotSupportedException($"Section type {section.GetType().Name} is not supported by TrapezoidGeometry."),
    };
}

/// <summary>
/// Circular conduit of diameter D, partially full. With central angle θ = 2 arccos(1 − 2y/D):
///   A = D²/8 (θ − sin θ),  P = D θ / 2,  T = D sin(θ/2) = 2 √(y (D − y)).
/// At y ≥ D the pipe is full: A = πD²/4, P = πD, T = 0.
/// Reference: Chow (1959) Table 2-1; FHWA HDS-4.
/// </summary>
public sealed class CircularGeometry(double diameter) : ISectionGeometry
{
    public double Diameter { get; } = diameter;
    public double MaxDepth => Diameter;
    public bool DischargeIsMonotonic => false;

    private double Theta(double y) => 2.0 * Math.Acos(Math.Clamp(1.0 - 2.0 * y / Diameter, -1.0, 1.0));

    public double Area(double y)
    {
        if (y <= 0) return 0;
        if (y >= Diameter) return Math.PI * Diameter * Diameter / 4.0;
        double t = Theta(y);
        return Diameter * Diameter / 8.0 * (t - Math.Sin(t));
    }

    public double WettedPerimeter(double y) => y <= 0 ? 0 : y >= Diameter ? Math.PI * Diameter : Diameter * Theta(y) / 2.0;

    public double TopWidth(double y) => y <= 0 || y >= Diameter ? 0 : 2.0 * Math.Sqrt(y * (Diameter - y));

    public IReadOnlyList<(double X, double Y)> Outline(double height)
    {
        const int n = 96;
        double r = Diameter / 2;
        return Enumerable.Range(0, n + 1)
            .Select(i => 2 * Math.PI * i / n)
            .Select(a => (r + r * Math.Sin(a), r - r * Math.Cos(a)))
            .ToList();
    }

    public (double Left, double Right) WaterSurfaceExtent(double depth, double height)
    {
        double r = Diameter / 2, y = Math.Clamp(depth, 0, Diameter);
        double half = Math.Sqrt(Math.Max(0, y * (Diameter - y)));
        return (r - half, r + half);
    }

    public IReadOnlyList<(double X, double Y)> WaterPolygon(double depth, double height)
    {
        double r = Diameter / 2, y = Math.Clamp(depth, 0, Diameter);
        double phi = Math.Acos(Math.Clamp((r - y) / r, -1, 1)); // half-angle from the invert
        const int n = 64;
        return Enumerable.Range(0, n + 1)
            .Select(i => -phi + 2 * phi * i / n)
            .Select(a => (r + r * Math.Sin(a), r - r * Math.Cos(a)))
            .ToList();
    }
}
