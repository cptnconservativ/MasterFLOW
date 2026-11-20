using MasterFlow.Domain;

namespace MasterFlow.Hydraulics.Geometry;

/// <summary>Wetted properties of one roughness subarea at a given water surface.</summary>
public readonly record struct Subarea(double StationLeft, double StationRight, double ManningN, double Area, double WettedPerimeter, double TopWidth);

/// <summary>
/// Station/elevation section. Internally elevations are shifted so the lowest point is at 0, which
/// makes "depth" and "height above the lowest point" the same number.
///
/// For a water surface at depth y every segment contributes exactly:
///   fully wet:      A = w (y − (z₁ + z₂)/2),  P = √(w² + Δz²),  T = w
///   partly wet:     the wet triangle up to the crossing point
///   vertical wall:  P = wetted height, A = T = 0
/// If y is above an end point, a vertical wall is assumed at that end (P grows by y − z_end).
/// A segment lying exactly at the water surface is dry. All wetted parts below the surface count,
/// including pools separated by ground above the surface (reported as separate wetted regions).
/// </summary>
public sealed class IrregularGeometry : ISectionGeometry
{
    private readonly double[] _x, _z;
    private readonly double?[] _segmentN;

    public IrregularGeometry(IrregularSection section)
    {
        // Identical consecutive points add nothing and are dropped.
        var pts = new List<StationPoint>();
        foreach (var p in section.Points)
            if (pts.Count == 0 || pts[^1].Station != p.Station || pts[^1].Elevation != p.Elevation) pts.Add(p);
            else if (p.ManningN is not null) pts[^1] = pts[^1] with { ManningN = p.ManningN };
        Datum = pts.Min(p => p.Elevation);
        _x = pts.Select(p => p.Station).ToArray();
        _z = pts.Select(p => p.Elevation - Datum).ToArray();
        _segmentN = pts.Take(pts.Count - 1).Select(p => p.ManningN).ToArray();
        Relief = _z.Max();
        Breakpoints = _z.Distinct().OrderBy(v => v).ToArray();
        Method = section.RoughnessMethod;
    }

    /// <summary>Elevation of the lowest point; depth y corresponds to water-surface elevation Datum + y.</summary>
    public double Datum { get; }

    /// <summary>Height of the highest point above the lowest point.</summary>
    public double Relief { get; }

    public double LeftEndHeight => _z[0];
    public double RightEndHeight => _z[^1];
    public double LeftStation => _x[0];
    public double RightStation => _x[^1];
    public int SegmentCount => _segmentN.Length;
    public CompositeRoughnessMethod Method { get; }

    /// <summary>Distinct point heights above the lowest point, ascending (starts at 0). Geometry is smooth between them.</summary>
    public IReadOnlyList<double> Breakpoints { get; }

    public double MaxDepth => double.PositiveInfinity;

    /// <summary>Q(y) can fall when water spills onto a flat overbank, so the solver scans for every root.</summary>
    public bool DischargeIsMonotonic => false;

    public bool HasMultipleRoughness(double defaultN) => _segmentN.Select(n => n ?? defaultN).Distinct().Count() > 1;

    private readonly record struct Wet(double A, double P, double T, double XLeft, double XRight);

    private Wet Segment(int i, double y)
    {
        double x1 = _x[i], z1 = _z[i], x2 = _x[i + 1], z2 = _z[i + 1];
        if (x1 == x2)
        {
            double lo = Math.Min(z1, z2), hi = Math.Max(z1, z2);
            double p = Math.Clamp(y - lo, 0, hi - lo);
            return new Wet(0, p, 0, x1, x1);
        }
        bool w1 = z1 < y, w2 = z2 < y;
        double w = x2 - x1;
        if (w1 && w2)
            return new Wet(w * (y - 0.5 * (z1 + z2)), Math.Sqrt(w * w + (z2 - z1) * (z2 - z1)), w, x1, x2);
        if (!w1 && !w2) return default;
        double zw = w1 ? z1 : z2, zd = w1 ? z2 : z1;
        double frac = (y - zw) / (zd - zw);
        double wl = w * frac, d = y - zw;
        return w1
            ? new Wet(0.5 * wl * d, Math.Sqrt(wl * wl + d * d), wl, x1, x1 + wl)
            : new Wet(0.5 * wl * d, Math.Sqrt(wl * wl + d * d), wl, x2 - wl, x2);
    }

    private double LeftWall(double y) => Math.Max(0, y - _z[0]);
    private double RightWall(double y) => Math.Max(0, y - _z[^1]);

    public double Area(double depth)
    {
        if (depth <= 0) return 0;
        double a = 0;
        for (int i = 0; i < SegmentCount; i++) a += Segment(i, depth).A;
        return a;
    }

    public double WettedPerimeter(double depth)
    {
        if (depth <= 0) return 0;
        double p = LeftWall(depth) + RightWall(depth);
        for (int i = 0; i < SegmentCount; i++) p += Segment(i, depth).P;
        return p;
    }

    public double TopWidth(double depth)
    {
        if (depth <= 0) return 0;
        double t = 0;
        for (int i = 0; i < SegmentCount; i++) t += Segment(i, depth).T;
        return t;
    }

    /// <summary>
    /// Roughness subareas at depth y: consecutive segments with the same n form one subarea, separated
    /// by vertical lines (which are not wetted perimeter). End walls belong to the end subareas.
    /// </summary>
    public IReadOnlyList<Subarea> Subareas(double depth, double defaultN)
    {
        var list = new List<Subarea>();
        if (depth <= 0) return list;
        if (Method == CompositeRoughnessMethod.PerSegment) return SegmentSubareas(depth, defaultN);
        int start = 0;
        while (start < SegmentCount)
        {
            double n = _segmentN[start] ?? defaultN;
            int end = start;
            while (end + 1 < SegmentCount && (_segmentN[end + 1] ?? defaultN) == n) end++;
            double a = 0, p = 0, t = 0;
            for (int i = start; i <= end; i++) { var s = Segment(i, depth); a += s.A; p += s.P; t += s.T; }
            if (start == 0) p += LeftWall(depth);
            if (end == SegmentCount - 1) p += RightWall(depth);
            list.Add(new Subarea(_x[start], _x[end + 1], n, a, p, t));
            start = end + 1;
        }
        return list;
    }

    /// <summary>
    /// One subarea per wet sloping segment. A vertical segment carries no area, so its wetted height is added
    /// to the subarea on its left (or right, at the left edge); end walls join the end subareas.
    /// </summary>
    private List<Subarea> SegmentSubareas(double depth, double defaultN)
    {
        var list = new List<Subarea>();
        double pendingP = LeftWall(depth);
        for (int i = 0; i < SegmentCount; i++)
        {
            var s = Segment(i, depth);
            double n = _segmentN[i] ?? defaultN;
            if (_x[i] == _x[i + 1])
            {
                if (list.Count > 0) list[^1] = list[^1] with { WettedPerimeter = list[^1].WettedPerimeter + s.P };
                else pendingP += s.P;
                continue;
            }
            if (s.A <= 0 && s.P <= 0) continue;
            list.Add(new Subarea(_x[i], _x[i + 1], n, s.A, s.P + pendingP, s.T));
            pendingP = 0;
        }
        double right = RightWall(depth) + pendingP;
        if (right > 0 && list.Count > 0) list[^1] = list[^1] with { WettedPerimeter = list[^1].WettedPerimeter + right };
        return list;
    }

    /// <summary>Conveyance K(y) such that Q = K √S, using the section's composite-roughness method.</summary>
    public double Conveyance(double depth, double defaultN, double k)
    {
        if (depth <= 0) return 0;
        var subs = Subareas(depth, defaultN);
        if (subs.Count == 1 || Method == CompositeRoughnessMethod.EqualVelocity)
        {
            double a = 0, p = 0, sum = 0;
            foreach (var s in subs) { a += s.Area; p += s.WettedPerimeter; sum += s.WettedPerimeter * Math.Pow(s.ManningN, 1.5); }
            if (a <= 0 || p <= 0) return 0;
            double nc = Math.Pow(sum / p, 2.0 / 3.0);
            return k / nc * a * Math.Pow(a / p, 2.0 / 3.0);
        }
        double kt = 0;
        foreach (var s in subs)
            if (s.Area > 0 && s.WettedPerimeter > 0)
                kt += k / s.ManningN * s.Area * Math.Pow(s.Area / s.WettedPerimeter, 2.0 / 3.0);
        return kt;
    }

    /// <summary>Conveyance of one subarea (for reporting the flow split).</summary>
    public static double SubareaConveyance(Subarea s, double k) =>
        s.Area > 0 && s.WettedPerimeter > 0 ? k / s.ManningN * s.Area * Math.Pow(s.Area / s.WettedPerimeter, 2.0 / 3.0) : 0;

    /// <summary>Number of separate wetted regions (pools divided by ground above the water surface).</summary>
    public int WettedRegions(double depth)
    {
        // A region continues only while consecutive wet pieces touch; a segment can be wet at both
        // ends of a ridge's flanks yet separated by dry ground at its crest.
        int regions = 0;
        bool inside = false;
        double lastRight = double.NaN;
        for (int i = 0; i < SegmentCount; i++)
        {
            var s = Segment(i, depth);
            bool wet = s.A > 0 || s.P > 0;
            if (wet && !(inside && Math.Abs(s.XLeft - lastRight) <= 1e-12 * Math.Max(1, Math.Abs(lastRight)))) regions++;
            inside = wet && s.XRight == _x[i + 1];
            lastRight = s.XRight;
        }
        return regions;
    }

    public IReadOnlyList<(double X, double Y)> Outline(double height)
    {
        var pts = new List<(double, double)>();
        if (height > _z[0]) pts.Add((_x[0], height));
        for (int i = 0; i < _x.Length; i++) pts.Add((_x[i], _z[i]));
        if (height > _z[^1]) pts.Add((_x[^1], height));
        return pts;
    }

    public (double Left, double Right) WaterSurfaceExtent(double depth, double height)
    {
        double left = double.PositiveInfinity, right = double.NegativeInfinity;
        for (int i = 0; i < SegmentCount; i++)
        {
            var s = Segment(i, depth);
            if (s.T <= 0 && s.P <= 0) continue;
            left = Math.Min(left, s.XLeft);
            right = Math.Max(right, s.XRight);
        }
        if (LeftWall(depth) > 0) left = _x[0];
        if (RightWall(depth) > 0) right = _x[^1];
        return double.IsFinite(left) ? (left, right) : (_x[0], _x[0]);
    }

    public IReadOnlyList<(double X, double Y)> WaterPolygon(double depth, double height)
    {
        var (left, right) = WaterSurfaceExtent(depth, height);
        var pts = new List<(double X, double Y)>();
        if (depth <= 0 || right <= left) return pts;
        // Ground from left to right, clamped to the water surface; then back along the surface.
        for (int i = 0; i < _x.Length; i++)
        {
            if (_x[i] >= left && _x[i] <= right) pts.Add((_x[i], Math.Min(_z[i], depth)));
            if (i + 1 < _x.Length && _x[i] != _x[i + 1] && (_z[i] < depth) != (_z[i + 1] < depth))
            {
                double xc = _x[i] + (_x[i + 1] - _x[i]) * (depth - _z[i]) / (_z[i + 1] - _z[i]);
                if (xc >= left && xc <= right) pts.Add((xc, depth));
            }
        }
        pts.Add((right, depth));
        pts.Add((left, depth));
        return pts;
    }
}
