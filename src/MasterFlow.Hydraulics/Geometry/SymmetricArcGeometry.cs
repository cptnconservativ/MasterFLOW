using MasterFlow.Domain;

namespace MasterFlow.Hydraulics.Geometry;

/// <summary>
/// A closed conduit symmetric about its vertical axis whose right-hand wall is a chain of pieces, each
/// piece an (optionally horizontally scaled) circular arc:
///   w(y) = cx + k √(ρ² − (y − cy)²),   for y in [y0, y1]  (w = half-width, y measured from the invert).
/// k = 1 gives a circular arc (ASTM C507 four-centred elliptical pipe); a single piece with k = a/b gives a
/// true ellipse. Area and wetted perimeter are evaluated in closed form for arcs; the ellipse perimeter
/// uses composite Gauss–Legendre quadrature of the arc-length integral (smooth integrand).
/// </summary>
public sealed class SymmetricArcGeometry : ISectionGeometry
{
    public sealed record Piece(double Y0, double Y1, double Cx, double Cy, double Rho, double K = 1.0)
    {
        public double HalfWidth(double y) => Cx + K * Math.Sqrt(Math.Max(0, Rho * Rho - (y - Cy) * (y - Cy)));
        private double Phi(double y) => Math.Asin(Math.Clamp((y - Cy) / Rho, -1, 1));

        /// <summary>∫ w dy from Y0 to y (half-area).</summary>
        public double HalfArea(double y)
        {
            double u0 = Y0 - Cy, u1 = Math.Min(y, Y1) - Cy;
            return Cx * (u1 - u0) + K * (F(u1) - F(u0));
            double F(double u) => 0.5 * (u * Math.Sqrt(Math.Max(0, Rho * Rho - u * u)) + Rho * Rho * Math.Asin(Math.Clamp(u / Rho, -1, 1)));
        }

        /// <summary>Wall length from Y0 to y (one side).</summary>
        public double HalfPerimeter(double y)
        {
            double p0 = Phi(Y0), p1 = Phi(Math.Min(y, Y1));
            if (Math.Abs(K - 1.0) < 1e-15) return Rho * (p1 - p0);
            // ds = ρ √(cos²φ + k² sin²φ) dφ
            return Rho * GaussLegendre(phi => Math.Sqrt(Math.Cos(phi) * Math.Cos(phi) + K * K * Math.Sin(phi) * Math.Sin(phi)), p0, p1);
        }
    }

    private readonly Piece[] _pieces;
    private readonly double[] _cumArea, _cumPerim;

    public SymmetricArcGeometry(IReadOnlyList<Piece> pieces, double maxHalfWidth, string description)
    {
        _pieces = [.. pieces];
        MaxDepth = _pieces[^1].Y1;
        MaxHalfWidth = maxHalfWidth;
        Description = description;
        _cumArea = new double[_pieces.Length + 1];
        _cumPerim = new double[_pieces.Length + 1];
        for (int i = 0; i < _pieces.Length; i++)
        {
            _cumArea[i + 1] = _cumArea[i] + 2 * _pieces[i].HalfArea(_pieces[i].Y1);
            _cumPerim[i + 1] = _cumPerim[i] + 2 * _pieces[i].HalfPerimeter(_pieces[i].Y1);
        }
    }

    public double MaxDepth { get; }
    public double MaxHalfWidth { get; }
    public string Description { get; }
    public bool DischargeIsMonotonic => false;

    private int Index(double y)
    {
        for (int i = 0; i < _pieces.Length; i++) if (y <= _pieces[i].Y1) return i;
        return _pieces.Length - 1;
    }

    public double Area(double y)
    {
        if (y <= 0) return 0;
        if (y >= MaxDepth) return _cumArea[^1];
        int i = Index(y);
        return _cumArea[i] + 2 * _pieces[i].HalfArea(y);
    }

    public double WettedPerimeter(double y)
    {
        if (y <= 0) return 0;
        if (y >= MaxDepth) return _cumPerim[^1];
        int i = Index(y);
        return _cumPerim[i] + 2 * _pieces[i].HalfPerimeter(y);
    }

    public double TopWidth(double y) => y <= 0 || y >= MaxDepth ? 0 : 2 * _pieces[Index(y)].HalfWidth(y);

    public IReadOnlyList<(double X, double Y)> Outline(double height)
    {
        var right = Sample(0, MaxDepth);
        var pts = new List<(double, double)>(right.Count * 2);
        pts.AddRange(right.Select(p => (MaxHalfWidth + p.W, p.Y)));
        pts.AddRange(right.AsEnumerable().Reverse().Select(p => (MaxHalfWidth - p.W, p.Y)));
        pts.Add(pts[0]);
        return pts;
    }

    public (double Left, double Right) WaterSurfaceExtent(double depth, double height)
    {
        double w = depth <= 0 || depth >= MaxDepth ? 0 : _pieces[Index(depth)].HalfWidth(depth);
        return (MaxHalfWidth - w, MaxHalfWidth + w);
    }

    public IReadOnlyList<(double X, double Y)> WaterPolygon(double depth, double height)
    {
        var right = Sample(0, Math.Min(depth, MaxDepth));
        var pts = right.Select(p => (MaxHalfWidth + p.W, p.Y)).ToList();
        pts.AddRange(right.AsEnumerable().Reverse().Select(p => (MaxHalfWidth - p.W, p.Y)));
        return pts;
    }

    private List<(double Y, double W)> Sample(double y0, double y1)
    {
        const int n = 120;
        var list = new List<(double, double)>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            // cosine spacing concentrates points at the invert and crown where the wall turns fastest
            double t = 0.5 - 0.5 * Math.Cos(Math.PI * i / n);
            double y = y0 + (y1 - y0) * t;
            double w = y <= 0 || y >= MaxDepth ? 0 : _pieces[Index(y)].HalfWidth(y);
            list.Add((y, w));
        }
        return list;
    }

    // ---------------------------------------------------------------- factories

    /// <summary>Four-centred section from rise, span and end radius r (R derived from tangency).</summary>
    public static SymmetricArcGeometry FourCentred(double rise, double span, double r, out double derivedSideRadius)
    {
        double a = span / 2, b = rise / 2;
        bool horizontal = span >= rise;
        double major = horizontal ? a : b, minor = horizontal ? b : a;
        // Arcs of radius r at the ends of the major axis, radius R at the ends of the minor axis, tangent:
        // (major − r)² + (R − minor)² = (R − r)²  →  R = ((major − r)² + minor² − r²) / (2 (minor − r))
        double big = ((major - r) * (major - r) + minor * minor - r * r) / (2 * (minor - r));
        derivedSideRadius = big;
        Piece[] pieces;
        if (horizontal)
        {
            double yt = r * (big - b) / (big - r);           // tangency height above/below the centre
            pieces =
            [
                new(0, b - yt, 0, big, big),                  // invert arc, centre (0, R) from invert
                new(b - yt, b + yt, a - r, b, r),             // side arc
                new(b + yt, rise, 0, rise - big, big),        // crown arc
            ];
        }
        else
        {
            double ht = (b - r) * big / (big - r);
            pieces =
            [
                new(0, b - ht, 0, r, r),                      // invert arc (radius r)
                new(b - ht, b + ht, a - big, b, big),         // side arc (radius R, centre beyond the axis)
                new(b + ht, rise, 0, rise - r, r),            // crown arc
            ];
        }
        return new SymmetricArcGeometry(pieces, a, $"four-centred arcs: r = {r:G6} at the {(horizontal ? "springlines" : "invert and crown")}, R = {big:G6} (from tangency)");
    }

    /// <summary>True ellipse with the given rise and span.</summary>
    public static SymmetricArcGeometry TrueEllipse(double rise, double span)
    {
        double a = span / 2, b = rise / 2;
        return new SymmetricArcGeometry([new Piece(0, rise, 0, b, b, a / b)], a, "true ellipse");
    }

    public static bool IsValidFourCentred(double rise, double span, double r)
    {
        double minor = Math.Min(rise, span) / 2;
        return r > 0 && r < minor;
    }

    public static SymmetricArcGeometry From(EllipticalSection e) =>
        e.EndRadius is { } r ? FourCentred(e.Rise, e.Span, r, out _) : TrueEllipse(e.Rise, e.Span);

    // ---------------------------------------------------------------- quadrature

    private static readonly double[] GlX =
    [
        -0.9894009349916499, -0.9445750230732326, -0.8656312023878318, -0.7554044083550030,
        -0.6178762444026438, -0.4580167776572274, -0.2816035507792589, -0.0950125098376374,
         0.0950125098376374,  0.2816035507792589,  0.4580167776572274,  0.6178762444026438,
         0.7554044083550030,  0.8656312023878318,  0.9445750230732326,  0.9894009349916499,
    ];
    private static readonly double[] GlW =
    [
        0.0271524594117541, 0.0622535239386479, 0.0951585116824928, 0.1246289712555339,
        0.1495959888165767, 0.1691565193950025, 0.1826034150449236, 0.1894506104550685,
        0.1894506104550685, 0.1826034150449236, 0.1691565193950025, 0.1495959888165767,
        0.1246289712555339, 0.0951585116824928, 0.0622535239386479, 0.0271524594117541,
    ];

    /// <summary>16-point Gauss–Legendre on 8 equal panels.</summary>
    private static double GaussLegendre(Func<double, double> f, double a, double b)
    {
        const int panels = 8;
        double h = (b - a) / panels, sum = 0;
        for (int p = 0; p < panels; p++)
        {
            double mid = a + (p + 0.5) * h, half = h / 2;
            for (int i = 0; i < GlX.Length; i++) sum += GlW[i] * f(mid + half * GlX[i]);
        }
        return sum * h / 2;
    }
}
