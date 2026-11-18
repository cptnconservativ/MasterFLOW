using MasterFlow.Domain;

namespace MasterFlow.Application;

/// <summary>Kind of horizontal element.</summary>
public enum AlignmentElementKind { Line, Arc, Spiral }

/// <summary>
/// One horizontal alignment element in plan coordinates (northing, easting). Arcs carry their centre and
/// direction; spirals (clothoids) carry the start tangent bearing and the curvature at both ends
/// (0 for an infinite radius; positive = curving left/counter-clockwise).
/// </summary>
public sealed record AlignmentElement(
    AlignmentElementKind Kind, double Length,
    double StartN, double StartE, double EndN, double EndE,
    double CenterN = 0, double CenterE = 0, bool Clockwise = false,
    double StartBearing = 0, double CurvatureStart = 0, double CurvatureEnd = 0);

/// <summary>Station and offset of a point from an alignment. Offset is positive to the right of the direction of stationing.</summary>
public sealed record StationOffset(string Alignment, double Station, double Offset, bool BeyondEnds);

/// <summary>
/// A horizontal alignment (lines, circular arcs, clothoid spirals). A point's station and offset come from the
/// nearest element: lines and arcs exactly, spirals from a polyline of 0.5-unit chords found by numerical
/// integration of the heading. <see cref="Points"/> (arcs at most 1° per chord) is for drawing.
/// </summary>
public sealed class Alignment
{
    public string Name { get; }
    public double StartStation { get; }
    public IReadOnlyList<AlignmentElement> Elements { get; }
    /// <summary>Densified points: station, northing, easting.</summary>
    public IReadOnlyList<(double Sta, double N, double E)> Points { get; }
    /// <summary>Gaps or mismatches found between consecutive elements (computed vs. stated end points).</summary>
    public IReadOnlyList<string> Problems { get; }

    public double EndStation => Points.Count == 0 ? StartStation : Points[^1].Sta;

    public Alignment(string name, double startStation, IReadOnlyList<AlignmentElement> elements)
    {
        Name = name; StartStation = startStation; Elements = elements;
        var pts = new List<(double, double, double)>();
        var problems = new List<string>();
        var parts = new List<(AlignmentElement, double, int, int)>();
        double sta = startStation;
        foreach (var (el, i) in elements.Select((e, i) => (e, i)))
        {
            if (pts.Count > 0)
            {
                var last = pts[^1];
                double gap = Math.Sqrt(Sq(el.StartN - last.Item2) + Sq(el.StartE - last.Item3));
                if (gap > 0.01) problems.Add($"Element {i + 1} ({el.Kind}) starts {gap:F3} from the end of the previous element.");
            }
            var seg = Densify(el);
            if (seg.Count == 0) continue;
            var (en, ee) = seg[^1];
            double miss = Math.Sqrt(Sq(en - el.EndN) + Sq(ee - el.EndE));
            if (miss > Math.Max(0.01, 1e-4 * el.Length)) problems.Add($"Element {i + 1} ({el.Kind}): computed end differs from the stated end by {miss:F3}.");
            int first = Math.Max(0, pts.Count - 1);
            for (int k = 0; k < seg.Count; k++)
            {
                if (k == 0 && pts.Count > 0) continue;
                pts.Add((sta + el.Length * k / (seg.Count - 1), seg[k].N, seg[k].E));
            }
            parts.Add((el, sta, first, pts.Count - 1));
            sta += el.Length;
        }
        Points = pts; Problems = problems; _parts = parts;
    }

    // element, start station, index range in Points
    private readonly List<(AlignmentElement El, double Sta0, int First, int Last)> _parts;

    private static double Sq(double x) => x * x;

    /// <summary>Points from start to end of one element, evenly spaced along its length.</summary>
    public static List<(double N, double E)> Densify(AlignmentElement el)
    {
        var list = new List<(double N, double E)>();
        switch (el.Kind)
        {
            case AlignmentElementKind.Line:
                list.Add((el.StartN, el.StartE)); list.Add((el.EndN, el.EndE));
                break;
            case AlignmentElementKind.Arc:
            {
                double r = Math.Sqrt(Sq(el.StartN - el.CenterN) + Sq(el.StartE - el.CenterE));
                if (!(r > 0)) { list.Add((el.StartN, el.StartE)); list.Add((el.EndN, el.EndE)); break; }
                double a0 = Math.Atan2(el.StartN - el.CenterN, el.StartE - el.CenterE); // math angle in (x = E, y = N)
                double sweep = el.Length / r * (el.Clockwise ? -1 : 1);
                int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 180)) + 1);
                for (int k = 0; k < n; k++)
                {
                    double a = a0 + sweep * k / (n - 1);
                    list.Add((el.CenterN + r * Math.Sin(a), el.CenterE + r * Math.Cos(a)));
                }
                break;
            }
            case AlignmentElementKind.Spiral:
            {
                // heading θ(s) = θ0 + k0·s + (k1 − k0)·s²/(2L); x' = cos θ, y' = sin θ (x = E, y = N); Simpson sub-steps
                int n = Math.Max(20, (int)Math.Ceiling(el.Length / 0.5)) + 1;
                double h = el.Length / (n - 1), x = el.StartE, y = el.StartN;
                double Th(double s) => el.StartBearing + el.CurvatureStart * s + (el.CurvatureEnd - el.CurvatureStart) * s * s / (2 * el.Length);
                list.Add((y, x));
                for (int k = 1; k < n; k++)
                {
                    double s0 = h * (k - 1), sm = s0 + h / 2, s1 = s0 + h;
                    x += h / 6 * (Math.Cos(Th(s0)) + 4 * Math.Cos(Th(sm)) + Math.Cos(Th(s1)));
                    y += h / 6 * (Math.Sin(Th(s0)) + 4 * Math.Sin(Th(sm)) + Math.Sin(Th(s1)));
                    list.Add((y, x));
                }
                break;
            }
        }
        return list;
    }

    /// <summary>
    /// Station and offset of a plan point (right of stationing positive). Lines and arcs are projected exactly
    /// (an arc by the point's angle about its centre, so an offset inside a curve does not shift the station);
    /// spirals by projection onto their 0.5-unit chords.
    /// </summary>
    public StationOffset Project(double n, double e)
    {
        double best = double.PositiveInfinity, sta = StartStation, off = 0;
        bool beyond = false;
        for (int pi = 0; pi < _parts.Count; pi++)
        {
            var (el, sta0, first, last) = _parts[pi];
            bool isFirst = pi == 0, isLast = pi == _parts.Count - 1;
            if (el.Kind == AlignmentElementKind.Arc && Math.Sqrt(Sq(el.StartN - el.CenterN) + Sq(el.StartE - el.CenterE)) is var r && r > 0)
            {
                double a0 = Math.Atan2(el.StartN - el.CenterN, el.StartE - el.CenterE);
                double sweep = el.Length / r, dir = el.Clockwise ? -1 : 1;
                double rel = dir * (Math.Atan2(n - el.CenterN, e - el.CenterE) - a0);
                rel = rel - 2 * Math.PI * Math.Floor(rel / (2 * Math.PI));   // 0 … 2π along the direction of the arc
                double outBy = rel - sweep;                                  // > 0: past the end, by angle
                bool before = false, after = false;
                double t = rel;
                if (outBy > 0)
                {
                    // nearer end of the arc
                    if (outBy < 2 * Math.PI - rel) { t = sweep; after = true; } else { t = 0; before = true; }
                }
                double a = a0 + dir * t, pn = el.CenterN + r * Math.Sin(a), pe = el.CenterE + r * Math.Cos(a);
                double d = Math.Sqrt(Sq(n - pn) + Sq(e - pe));
                if (d < best - 1e-12)
                {
                    best = d; sta = sta0 + t * r;
                    double rp = Math.Sqrt(Sq(n - el.CenterN) + Sq(e - el.CenterE));
                    // inside the curve is to the left for a ccw arc, to the right for a cw arc
                    bool left = el.Clockwise ? rp > r : rp < r;
                    off = left ? -d : d;
                    beyond = (isFirst && before) || (isLast && after);
                }
                continue;
            }
            for (int i = first + 1; i <= last; i++)
            {
                var (s0, n0, e0) = Points[i - 1]; var (s1, n1, e1) = Points[i];
                double dx = e1 - e0, dy = n1 - n0, len2 = dx * dx + dy * dy;
                if (len2 <= 0) continue;
                double tRaw = ((e - e0) * dx + (n - n0) * dy) / len2, t = Math.Clamp(tRaw, 0, 1);
                double px = e0 + t * dx, py = n0 + t * dy;
                double d = Math.Sqrt(Sq(e - px) + Sq(n - py));
                if (d < best - 1e-12)
                {
                    best = d;
                    sta = s0 + t * (s1 - s0);
                    double cross = dx * (n - n0) - dy * (e - e0); // > 0: point to the left
                    off = cross > 0 ? -d : d;
                    beyond = (isFirst && i == first + 1 && tRaw < -1e-9) || (isLast && i == last && tRaw > 1 + 1e-9);
                }
            }
        }
        return new StationOffset(Name, sta, off, beyond);
    }

    /// <summary>The nearest alignment (smallest |offset|) for a point, ignoring alignments where the point falls beyond the ends when any other fits.</summary>
    public static StationOffset? Nearest(IEnumerable<Alignment> alignments, double n, double e)
    {
        var all = alignments.Where(a => a.Points.Count > 1).Select(a => a.Project(n, e)).ToList();
        return all.Where(x => !x.BeyondEnds).OrderBy(x => Math.Abs(x.Offset)).FirstOrDefault()
            ?? all.OrderBy(x => Math.Abs(x.Offset)).FirstOrDefault();
    }

    /// <summary>Civil station text, e.g. 12+34.56 (U.S., 100 ft stations) or 1+234.567 (SI, 1000 m stations).</summary>
    public static string FormatStation(double sta, UnitSystem u)
    {
        double unit = u == UnitSystem.SI ? 1000 : 100;
        int d = u == UnitSystem.SI ? 3 : 2;
        string sign = sta < 0 ? "-" : "";
        sta = Math.Round(Math.Abs(sta), d);
        double whole = Math.Floor(sta / unit);
        double rest = Math.Max(0, Math.Round(sta - whole * unit, d)); // from the rounded value: 99.996 → 1+00.00, not 0+100.00
        return sign + whole.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "+" +
               rest.ToString(u == UnitSystem.SI ? "000.000" : "00.00", System.Globalization.CultureInfo.InvariantCulture);
    }
}
