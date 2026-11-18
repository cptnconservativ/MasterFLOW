using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MasterFlow.Domain;

namespace MasterFlow.Persistence;

/// <summary>One cross-section surface read from a LandXML file.</summary>
public sealed record LandXmlCrossSection(string Alignment, double Station, string Surface, IReadOnlyList<StationPoint> Points);

/// <summary>Result of reading a LandXML file: sections, the file's unit system (null when not stated) and problems found.</summary>
public sealed record LandXmlReadResult(IReadOnlyList<LandXmlCrossSection> Sections, UnitSystem? Units, string? LinearUnit, IReadOnlyList<string> Problems);

/// <summary>
/// Reads cross-sections from LandXML 1.x (the open format Civil 3D and other design programs export,
/// e.g. sample-line sections): Alignment / CrossSects / CrossSect (sta) / CrossSectSurf (name) /
/// PntList2D ("offset elevation" pairs). Values are taken as they are in the file, in the file's
/// units: nothing is converted. Points are ordered by offset (left to right). Any element
/// that cannot be read is reported, not guessed.
/// </summary>
public static class LandXmlCrossSections
{
    public static LandXmlReadResult Read(Stream stream)
    {
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex) { return new([], null, null, [$"Not a readable XML file: {ex.Message}"]); }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "LandXML") return new([], null, null, ["Not a LandXML file (the root element is not <LandXML>)."]);

        UnitSystem? units = null; string? linear = null;
        var unitsEl = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Units");
        var sys = unitsEl?.Elements().FirstOrDefault(e => e.Name.LocalName is "Imperial" or "Metric");
        if (sys is not null)
        {
            linear = (string?)sys.Attribute("linearUnit");
            units = linear switch
            {
                "foot" or "USSurveyFoot" or "IntlFoot" => UnitSystem.USCustomary,
                "meter" => UnitSystem.SI,
                _ => null,
            };
        }

        var problems = new List<string>();
        if (units is null)
            problems.Add(linear is null ? "The file does not state its linear unit; choose the unit system yourself." : $"Linear unit \"{linear}\" is not feet or metres; choose the unit system yourself (values are not converted).");

        var sections = new List<LandXmlCrossSection>();
        foreach (var cs in root.Descendants().Where(e => e.Name.LocalName == "CrossSect"))
        {
            var alignment = cs.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Alignment")?.Attribute("name")?.Value ?? "";
            string staText = (string?)cs.Attribute("sta") ?? "";
            if (!double.TryParse(staText, NumberStyles.Float, CultureInfo.InvariantCulture, out double sta))
            {
                problems.Add($"Cross-section \"{(string?)cs.Attribute("name") ?? "?"}\" in \"{alignment}\": station \"{staText}\" is not a number; skipped.");
                continue;
            }
            foreach (var surf in cs.Elements().Where(e => e.Name.LocalName == "CrossSectSurf"))
            {
                string name = (string?)surf.Attribute("name") ?? "";
                var list = surf.Elements().FirstOrDefault(e => e.Name.LocalName == "PntList2D");
                var where = $"{alignment} sta {sta.ToString("0.###", CultureInfo.InvariantCulture)} surface \"{name}\"";
                if (list is null) { problems.Add($"{where}: no PntList2D; skipped."); continue; }
                var tokens = list.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length % 2 != 0) { problems.Add($"{where}: odd number of values in PntList2D; skipped."); continue; }
                var pts = new List<StationPoint>();
                bool bad = false;
                for (int i = 0; i < tokens.Length; i += 2)
                {
                    if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                        !double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double z) ||
                        !double.IsFinite(x) || !double.IsFinite(z))
                    { problems.Add($"{where}: value pair {i / 2 + 1} (\"{tokens[i]} {tokens[i + 1]}\") is not numeric; skipped."); bad = true; break; }
                    pts.Add(new StationPoint(x, z));
                }
                if (bad) continue;
                if (pts.Count < 2) { problems.Add($"{where}: fewer than two points; skipped."); continue; }
                pts = pts.Select((p, i) => (p, i)).OrderBy(t => t.p.Station).ThenBy(t => t.i).Select(t => t.p).ToList();
                sections.Add(new LandXmlCrossSection(alignment, sta, name, pts));
            }
        }
        if (sections.Count == 0 && problems.All(p => !p.Contains("skipped")))
            problems.Add("The file contains no cross-sections (CrossSect elements). Export sample-line sections from the design program.");
        return new(sections, units, linear, problems);
    }

    /// <summary>Surveyor station text, e.g. 1234.5 → "12+34.50".</summary>
    public static string StationText(double sta)
    {
        double a = Math.Abs(sta);
        long hundreds = (long)Math.Floor(a / 100);
        double rest = a - hundreds * 100;
        if (Math.Round(rest, 2) >= 100) { hundreds++; rest = 0; }
        return (sta < 0 ? "-" : "") + hundreds.ToString(CultureInfo.InvariantCulture) + "+" + rest.ToString("00.00", CultureInfo.InvariantCulture);
    }
}
