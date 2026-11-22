namespace MasterFlow.Application.Feasibility;

/// <summary>A point in latitude and longitude (WGS 84, decimal degrees).</summary>
public readonly record struct GeoPoint(double Lat, double Lon);

/// <summary>
/// Plane geometry for a parcel-sized site. Latitude and longitude are projected to a local plane in feet about the
/// site's first vertex (equirectangular: x = Δλ·cos φ0·R, y = Δφ·R, R = 20,902,231 ft, the mean Earth radius); over a
/// site a mile across this differs from a state plane area by well under 0.1 %.
/// </summary>
public sealed class SiteGeometry
{
    public const double EarthRadiusFt = 20_902_231.0;
    public const double SqFtPerAcre = 43_560.0;

    public GeoPoint Origin { get; }
    public IReadOnlyList<GeoPoint> Ring { get; }
    /// <summary>The boundary in local feet (x east, y north), not closed.</summary>
    public IReadOnlyList<(double X, double Y)> Local { get; }
    private readonly double _cos;

    public SiteGeometry(IReadOnlyList<GeoPoint> ring)
    {
        if (ring.Count < 3) throw new ArgumentException("A boundary needs at least three vertices.");
        var r = ring.ToList();
        if (r.Count > 3 && r[0] == r[^1]) r.RemoveAt(r.Count - 1);
        Ring = r;
        Origin = new GeoPoint(r.Min(p => p.Lat), r.Min(p => p.Lon));
        _cos = Math.Cos(r.Average(p => p.Lat) * Math.PI / 180);
        Local = r.Select(ToLocal).ToList();
    }

    public (double X, double Y) ToLocal(GeoPoint p) =>
        ((p.Lon - Origin.Lon) * Math.PI / 180 * EarthRadiusFt * _cos, (p.Lat - Origin.Lat) * Math.PI / 180 * EarthRadiusFt);

    public GeoPoint ToGeo(double x, double y) =>
        new(Origin.Lat + y / EarthRadiusFt * 180 / Math.PI, Origin.Lon + x / (EarthRadiusFt * _cos) * 180 / Math.PI);

    /// <summary>Area (ft²) by the shoelace formula.</summary>
    public double AreaSqFt => Math.Abs(Shoelace(Local));
    public double Acres => AreaSqFt / SqFtPerAcre;
    public double PerimeterFt => Local.Select((p, i) => Dist(p, Local[(i + 1) % Local.Count])).Sum();
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds => (Local.Min(p => p.X), Local.Min(p => p.Y), Local.Max(p => p.X), Local.Max(p => p.Y));
    /// <summary>Bounding box in degrees (west, south, east, north).</summary>
    public (double West, double South, double East, double North) GeoBounds => (Ring.Min(p => p.Lon), Ring.Min(p => p.Lat), Ring.Max(p => p.Lon), Ring.Max(p => p.Lat));
    public GeoPoint Centroid => new(Ring.Average(p => p.Lat), Ring.Average(p => p.Lon));
    /// <summary>Longest straight line inside the bounding box (ft), a first hydraulic length when none is entered.</summary>
    public double DiagonalFt { get { var b = Bounds; return Math.Sqrt(Math.Pow(b.MaxX - b.MinX, 2) + Math.Pow(b.MaxY - b.MinY, 2)); } }

    public bool Contains(double x, double y) => Inside(Local, x, y);
    public bool Contains(GeoPoint p) { var l = ToLocal(p); return Contains(l.X, l.Y); }

    /// <summary>WKT polygon (lon lat order, closed) for spatial queries.</summary>
    public string Wkt() => "POLYGON((" + string.Join(", ", Ring.Append(Ring[0]).Select(p => FormattableString.Invariant($"{p.Lon:F7} {p.Lat:F7}"))) + "))";

    /// <summary>
    /// Sample points: the centres of an n × n grid over the bounding box that fall inside the boundary, in local feet,
    /// with the cell area (ft²). Used to measure the share of the site in a flood zone or slope class.
    /// </summary>
    public (List<(double X, double Y)> Points, double CellArea) SampleGrid(int n)
    {
        var b = Bounds;
        double dx = (b.MaxX - b.MinX) / n, dy = (b.MaxY - b.MinY) / n;
        var pts = new List<(double, double)>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double x = b.MinX + (i + 0.5) * dx, y = b.MinY + (j + 0.5) * dy;
                if (Contains(x, y)) pts.Add((x, y));
            }
        return (pts, dx * dy);
    }

    public static double Shoelace(IReadOnlyList<(double X, double Y)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].X * q.Y - q.X * p[i].Y; }
        return a / 2;
    }

    /// <summary>Even–odd point-in-polygon test.</summary>
    public static bool Inside(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > y) != (poly[j].Y > y) && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
        return c;
    }

    private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>
/// Slopes from a regular elevation grid (nx × ny nodes over the site's bounding box, ft). Each node's slope is the
/// gradient magnitude from central differences (one-sided at the edges); a least-squares plane gives the general
/// direction of fall.
/// </summary>
public sealed class ElevationGrid
{
    public int Nx { get; }
    public int Ny { get; }
    public double Dx { get; }
    public double Dy { get; }
    public double X0 { get; }
    public double Y0 { get; }
    /// <summary>Elevations (ft) by [i, j]; NaN where no value came back.</summary>
    public double[,] Z { get; }

    public ElevationGrid(int nx, int ny, double x0, double y0, double dx, double dy, double[,] z)
    { Nx = nx; Ny = ny; X0 = x0; Y0 = y0; Dx = dx; Dy = dy; Z = z; }

    /// <summary>Node positions (local ft) of an nx × ny grid over the bounding box, row by row (j outer, i inner).</summary>
    public static List<(int I, int J, double X, double Y)> Nodes(SiteGeometry site, int nx, int ny)
    {
        var b = site.Bounds;
        double dx = (b.MaxX - b.MinX) / (nx - 1), dy = (b.MaxY - b.MinY) / (ny - 1);
        var l = new List<(int, int, double, double)>();
        for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) l.Add((i, j, b.MinX + i * dx, b.MinY + j * dy));
        return l;
    }

    public double X(int i) => X0 + i * Dx;
    public double Y(int j) => Y0 + j * Dy;

    /// <summary>Slope (ft/ft) at node (i, j); NaN when the neighbours are missing.</summary>
    public double Slope(int i, int j)
    {
        double G(int a0, int b0, int a1, int b1, double d) { double z0 = Z[a0, b0], z1 = Z[a1, b1]; return (z1 - z0) / d; }
        int il = Math.Max(0, i - 1), ir = Math.Min(Nx - 1, i + 1), jb = Math.Max(0, j - 1), jt = Math.Min(Ny - 1, j + 1);
        double gx = G(il, j, ir, j, (ir - il) * Dx), gy = G(i, jb, i, jt, (jt - jb) * Dy);
        return Math.Sqrt(gx * gx + gy * gy);
    }

    /// <summary>Least-squares plane z = a + b·x + c·y through the nodes inside the site: (b, c), the mean slope vector.</summary>
    public (double Bx, double Cy) Plane(SiteGeometry site)
    {
        double n = 0, sx = 0, sy = 0, sz = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0;
        for (int i = 0; i < Nx; i++)
            for (int j = 0; j < Ny; j++)
            {
                double x = X(i), y = Y(j), z = Z[i, j];
                if (!double.IsFinite(z) || !site.Contains(x, y)) continue;
                n++; sx += x; sy += y; sz += z; sxx += x * x; syy += y * y; sxy += x * y; sxz += x * z; syz += y * z;
            }
        if (n < 3) return (double.NaN, double.NaN);
        // Centred normal equations.
        double mx = sx / n, my = sy / n, mz = sz / n;
        double a11 = sxx - n * mx * mx, a22 = syy - n * my * my, a12 = sxy - n * mx * my, b1 = sxz - n * mx * mz, b2 = syz - n * my * mz;
        double det = a11 * a22 - a12 * a12;
        if (Math.Abs(det) < 1e-9) return (double.NaN, double.NaN);
        return ((b1 * a22 - b2 * a12) / det, (a11 * b2 - a12 * b1) / det);
    }

    /// <summary>Compass direction (N, NE, …) that a plane with gradient (bx, cy) falls toward.</summary>
    public static string FallDirection(double bx, double cy)
    {
        if (!double.IsFinite(bx) || !double.IsFinite(cy) || (bx == 0 && cy == 0)) return "—";
        double az = (Math.Atan2(-bx, -cy) * 180 / Math.PI + 360) % 360;   // downhill vector (-bx, -cy); azimuth from north
        string[] d = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        return d[(int)Math.Round(az / 45) % 8];
    }
}
