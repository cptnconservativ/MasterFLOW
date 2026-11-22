using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace MasterFlow.Application.Surfaces;

/// <summary>A triangulated surface (TIN): points (x = easting, y = northing, z) and triangles by point index.</summary>
public sealed class TinSurface
{
    public string Name { get; }
    public IReadOnlyList<(double X, double Y, double Z)> Points { get; }
    public IReadOnlyList<(int A, int B, int C)> Faces { get; }

    public TinSurface(string name, IReadOnlyList<(double, double, double)> points, IReadOnlyList<(int, int, int)> faces)
    { Name = name; Points = points; Faces = faces; }

    /// <summary>
    /// Water volume and wet plan area below a level water surface at <paramref name="e"/>, over the triangles accepted by
    /// <paramref name="include"/> (all when null). Exact for a TIN: each triangle is clipped to z ≤ e (z is linear on it), and
    /// ∫(e − z) dA over the clipped polygon = area × (e − z at its centroid).
    /// </summary>
    public (double Volume, double Area) Below(double e, Func<int, bool>? include = null)
    {
        double vol = 0, area = 0;
        for (int f = 0; f < Faces.Count; f++)
        {
            if (include is not null && !include(f)) continue;
            var (a, b, c) = Faces[f];
            var poly = Clip([Points[a], Points[b], Points[c]], e);
            if (poly.Count < 3) continue;
            var (ar, cx, cy, cz) = PolygonProps(poly);
            area += ar; vol += ar * (e - cz);
        }
        return (vol, area);
    }

    /// <summary>Plan centroid of face <paramref name="f"/>.</summary>
    public (double X, double Y) Centroid(int f)
    {
        var (a, b, c) = Faces[f];
        return ((Points[a].X + Points[b].X + Points[c].X) / 3, (Points[a].Y + Points[b].Y + Points[c].Y) / 3);
    }

    public double MinZ(Func<int, bool>? include = null) => Enumerable.Range(0, Faces.Count).Where(f => include?.Invoke(f) ?? true)
        .SelectMany(f => new[] { Faces[f].A, Faces[f].B, Faces[f].C }).Select(i => Points[i].Z).DefaultIfEmpty(double.NaN).Min();
    public double MaxZ(Func<int, bool>? include = null) => Enumerable.Range(0, Faces.Count).Where(f => include?.Invoke(f) ?? true)
        .SelectMany(f => new[] { Faces[f].A, Faces[f].B, Faces[f].C }).Select(i => Points[i].Z).DefaultIfEmpty(double.NaN).Max();

    /// <summary>Sutherland–Hodgman clip of a 3-D polygon (on one plane) to the half-space z ≤ e.</summary>
    private static List<(double X, double Y, double Z)> Clip(List<(double X, double Y, double Z)> poly, double e)
    {
        var o = new List<(double, double, double)>();
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i]; var q = poly[(i + 1) % poly.Count];
            bool pin = p.Z <= e, qin = q.Z <= e;
            if (pin) o.Add(p);
            if (pin != qin)
            {
                double t = (e - p.Z) / (q.Z - p.Z);
                o.Add((p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y), e));
            }
        }
        return o;
    }

    /// <summary>Plan area, plan centroid and the z at the plan centroid of a planar polygon (fan triangulation).</summary>
    private static (double Area, double Cx, double Cy, double Cz) PolygonProps(List<(double X, double Y, double Z)> p)
    {
        double a = 0, cx = 0, cy = 0, cz = 0;
        for (int i = 1; i + 1 < p.Count; i++)
        {
            double t = 0.5 * Math.Abs((p[i].X - p[0].X) * (p[i + 1].Y - p[0].Y) - (p[i + 1].X - p[0].X) * (p[i].Y - p[0].Y));
            a += t;
            cx += t * (p[0].X + p[i].X + p[i + 1].X) / 3; cy += t * (p[0].Y + p[i].Y + p[i + 1].Y) / 3; cz += t * (p[0].Z + p[i].Z + p[i + 1].Z) / 3;
        }
        return a > 0 ? (a, cx / a, cy / a, cz / a) : (0, 0, 0, 0);
    }

    /// <summary>Point-in-polygon (even–odd rule) in plan.</summary>
    public static bool Inside(IReadOnlyList<(double X, double Y)> poly, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > y) != (poly[j].Y > y) && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
        return inside;
    }
}

/// <summary>Reads TIN surfaces from LandXML 1.x (Surfaces / Surface / Definition surfType="TIN" / Pnts / P id="…">N E Z</P> and Faces / F>a b c</F>).</summary>
public static class LandXmlSurfaces
{
    public sealed record Result(IReadOnlyList<TinSurface> Surfaces, string? LinearUnit, IReadOnlyList<string> Problems);

    public static Result Read(Stream stream)
    {
        var problems = new List<string>();
        XElement root;
        try
        {
            using var xr = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            root = XElement.Load(xr);
        }
        catch (Exception ex) { return new([], null, [$"Not a readable LandXML file: {ex.Message}"]); }
        string? linear = root.Descendants().FirstOrDefault(e => e.Name.LocalName is "Imperial" or "Metric")?.Attribute("linearUnit")?.Value;
        var list = new List<TinSurface>();
        foreach (var s in root.Descendants().Where(e => e.Name.LocalName == "Surface"))
        {
            string name = (string?)s.Attribute("name") ?? $"Surface {list.Count + 1}";
            var def = s.Elements().FirstOrDefault(e => e.Name.LocalName == "Definition");
            if (def is null) { problems.Add($"{name}: no Definition; skipped."); continue; }
            if ((string?)def.Attribute("surfType") is { } t && !t.Equals("TIN", StringComparison.OrdinalIgnoreCase)) { problems.Add($"{name}: surface type {t} is not a TIN; skipped."); continue; }
            var ids = new Dictionary<string, int>();
            var pts = new List<(double, double, double)>();
            foreach (var p in def.Descendants().Where(e => e.Name.LocalName == "P"))
            {
                var v = p.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (v.Length < 3 || !Num(v[0], out double n) || !Num(v[1], out double ea) || !Num(v[2], out double z)) { problems.Add($"{name}: point {(string?)p.Attribute("id")} is not \"northing easting elevation\"; skipped."); continue; }
                ids[(string?)p.Attribute("id") ?? (pts.Count + 1).ToString(CultureInfo.InvariantCulture)] = pts.Count;
                pts.Add((ea, n, z));
            }
            var faces = new List<(int, int, int)>();
            int hidden = 0;
            foreach (var f in def.Descendants().Where(e => e.Name.LocalName == "F"))
            {
                if ((string?)f.Attribute("i") == "1") { hidden++; continue; }   // invisible face (outside the surface boundary)
                var v = f.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (v.Length < 3 || !ids.TryGetValue(v[0], out int a) || !ids.TryGetValue(v[1], out int b) || !ids.TryGetValue(v[2], out int c)) { problems.Add($"{name}: a face refers to a missing point; skipped."); continue; }
                faces.Add((a, b, c));
            }
            if (hidden > 0) problems.Add($"{name}: {hidden} invisible face(s) (outside the surface boundary) left out.");
            if (faces.Count == 0) { problems.Add($"{name}: no faces; skipped."); continue; }
            list.Add(new TinSurface(name, pts, faces));
        }
        if (list.Count == 0 && problems.Count == 0) problems.Add("The file has no TIN surfaces (Surfaces/Surface/Definition with Pnts and Faces).");
        return new(list, linear, problems);
    }

    private static bool Num(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
}
