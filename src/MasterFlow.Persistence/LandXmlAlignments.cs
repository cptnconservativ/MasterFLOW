using System.Globalization;
using System.Xml.Linq;
using MasterFlow.Application;
using MasterFlow.Domain;
using static MasterFlow.Persistence.LandXmlFile;

namespace MasterFlow.Persistence;

public sealed record LandXmlAlignmentReadResult(IReadOnlyList<Alignment> Alignments, UnitSystem? Units, IReadOnlyList<string> Problems);

/// <summary>
/// Reads LandXML 1.x horizontal alignments (Alignments/Alignment/CoordGeom with Line, Curve and Spiral), in
/// the file's units. Coordinates are "northing easting" as the schema states. Arcs use Start/Center/End and
/// rot; spirals use length, radiusStart/radiusEnd (INF = tangent), rot and the start tangent toward PI.
/// Elements of other kinds are reported and skipped; gaps between elements are reported, not closed.
/// </summary>
public static class LandXmlAlignments
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static LandXmlAlignmentReadResult Read(Stream stream)
    {
        if (!TryLoad(stream, out var root, out var error)) return new([], null, [error]);
        var (units, linear) = UnitsOf(root);
        var problems = new List<string>();
        if (units is null && root.Descendants().Any(e => e.Name.LocalName == "Alignment")) problems.Add(UnitProblem(linear));
        var list = new List<Alignment>();
        foreach (var a in root.Descendants().Where(e => e.Name.LocalName == "Alignment"))
        {
            string name = (string?)a.Attribute("name") ?? "";
            double sta0 = Num(a, "staStart") ?? 0;
            var geom = Kid(a, "CoordGeom");
            if (geom is null) { problems.Add($"Alignment \"{name}\": no CoordGeom; skipped."); continue; }
            var els = new List<AlignmentElement>();
            bool bad = false;
            foreach (var el in geom.Elements())
            {
                string kind = el.Name.LocalName, where = $"Alignment \"{name}\" {kind}";
                var start = Pt(el, "Start"); var end = Pt(el, "End");
                if (start is null || end is null) { problems.Add($"{where}: Start or End is missing or unreadable; alignment skipped."); bad = true; break; }
                var (sn, se) = start.Value; var (en, ee) = end.Value;
                bool cw = string.Equals((string?)el.Attribute("rot"), "cw", StringComparison.OrdinalIgnoreCase);
                switch (kind)
                {
                    case "Line":
                        els.Add(new AlignmentElement(AlignmentElementKind.Line, Math.Sqrt((en - sn) * (en - sn) + (ee - se) * (ee - se)), sn, se, en, ee));
                        break;
                    case "Curve":
                    {
                        if (Pt(el, "Center") is not { } c) { problems.Add($"{where}: no Center; alignment skipped."); bad = true; break; }
                        double r = Math.Sqrt((sn - c.N) * (sn - c.N) + (se - c.E) * (se - c.E));
                        double a0 = Math.Atan2(sn - c.N, se - c.E), a1 = Math.Atan2(en - c.N, ee - c.E);
                        double d = cw ? a0 - a1 : a1 - a0;
                        while (d < 0) d += 2 * Math.PI;
                        double len = Num(el, "length") ?? r * d;
                        if (Math.Abs(len - r * d) > Math.Max(0.01, 1e-4 * len)) problems.Add($"{where}: stated length {len.ToString("0.###", Inv)} differs from radius × angle {(r * d).ToString("0.###", Inv)}; the stated length is used for stationing.");
                        els.Add(new AlignmentElement(AlignmentElementKind.Arc, len, sn, se, en, ee, c.N, c.E, cw));
                        break;
                    }
                    case "Spiral":
                    {
                        double? len = Num(el, "length");
                        if (len is not > 0 || Pt(el, "PI") is not { } pi) { problems.Add($"{where}: needs length and PI; alignment skipped."); bad = true; break; }
                        string? spiType = (string?)el.Attribute("spiType");
                        if (spiType is not null && !spiType.Equals("clothoid", StringComparison.OrdinalIgnoreCase))
                            problems.Add($"{where}: type \"{spiType}\" computed as a clothoid.");
                        double K(string attr) { var t = (string?)el.Attribute(attr); return t is null || t.Equals("INF", StringComparison.OrdinalIgnoreCase) || !double.TryParse(t, NumberStyles.Float, Inv, out double rr) || rr == 0 ? 0 : 1 / rr; }
                        double sign = cw ? -1 : 1;
                        double bearing = Math.Atan2(pi.N - sn, pi.E - se);
                        els.Add(new AlignmentElement(AlignmentElementKind.Spiral, len.Value, sn, se, en, ee, StartBearing: bearing,
                            CurvatureStart: sign * K("radiusStart"), CurvatureEnd: sign * K("radiusEnd"), Clockwise: cw));
                        break;
                    }
                    default:
                        problems.Add($"{where}: element type not supported; alignment skipped.");
                        bad = true;
                        break;
                }
                if (bad) break;
            }
            if (bad || els.Count == 0) continue;
            var al = new Alignment(name, sta0, els);
            problems.AddRange(al.Problems.Select(p => $"Alignment \"{name}\": {p}"));
            list.Add(al);
        }
        return new(list, units, problems);
    }

    private static (double N, double E)? Pt(XElement el, string child)
    {
        var x = Kid(el, child);
        if (x is null) return null;
        var t = x.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return t.Length >= 2 && double.TryParse(t[0], NumberStyles.Float, Inv, out double n) && double.TryParse(t[1], NumberStyles.Float, Inv, out double e) ? (n, e) : null;
    }
}
