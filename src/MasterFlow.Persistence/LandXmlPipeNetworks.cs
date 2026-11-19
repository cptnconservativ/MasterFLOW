using System.Globalization;
using System.Xml.Linq;
using MasterFlow.Domain;
using static MasterFlow.Persistence.LandXmlFile;

namespace MasterFlow.Persistence;


/// <summary>
/// One pipe from a LandXML pipe network, in the file's units. Inverts come from the start and end
/// structures' Invert elements for this pipe; slope is computed from them and the pipe length.
/// </summary>
public sealed record LandXmlPipe(
    string Network, string Name, string StartStructure, string EndStructure, NetworkPipeShape Shape,
    double Rise, double Span, double? Length, double? StartInvert, double? EndInvert, string Material, string Description)
{
    /// <summary>(start invert − end invert) / length; null when any of them is missing or the length is not positive.</summary>
    public double? Slope => StartInvert is { } a && EndInvert is { } b && Length is { } l && l > 0 ? (a - b) / l : null;
}

public sealed record LandXmlNetworkReadResult(
    IReadOnlyList<LandXmlPipe> Pipes, IReadOnlyList<DrainageStructure> Structures, UnitSystem? Units, string? LinearUnit, IReadOnlyList<string> Problems);

/// <summary>
/// Reads LandXML 1.x pipe networks (PipeNetworks / PipeNetwork / Structs / Struct and Pipes / Pipe), the
/// form Civil 3D's LandXML export uses for storm and sanitary networks. Structure type comes from the
/// child element (InletStruct, OutletStruct, CircStruct, RectStruct, Connection). Circular (CircPipe) and
/// elliptical (ElliPipe, the LandXML 1.2 schema name; the misspelt EllipPipe is also accepted) pipes are imported; other shapes are listed as problems. Values are kept in the
/// file's units: pipe sizes are taken in the file's linear unit as the LandXML schema states, and a
/// size that looks like inches in a feet file is reported, not converted.
/// </summary>
public static class LandXmlPipeNetworks
{
    public static LandXmlNetworkReadResult Read(Stream stream)
    {
        if (!TryLoad(stream, out var root, out var error)) return new([], [], null, null, [error]);
        var (units, linear) = UnitsOf(root);
        // Pipe sizes follow the Units element's diameterUnit when it is stated (explicit in the file, so not a
        // hidden conversion); otherwise the linear unit, as the schema says.
        var unitEl = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Units")?.Elements().FirstOrDefault(e => e.Name.LocalName is "Imperial" or "Metric");
        string? diaUnit = (string?)unitEl?.Attribute("diameterUnit");
        double diaFactor = (units, diaUnit) switch
        {
            (UnitSystem.USCustomary, "inch") => 1.0 / 12.0,
            (UnitSystem.SI, "millimeter") => 0.001,
            (UnitSystem.SI, "centimeter") => 0.01,
            _ => 1.0,
        };
        var problems = new List<string>();
        if (units is null) problems.Add(UnitProblem(linear));

        var pipes = new List<LandXmlPipe>();
        var structs = new List<DrainageStructure>();
        foreach (var net in root.Descendants().Where(e => e.Name.LocalName == "PipeNetwork"))
        {
            string netName = (string?)net.Attribute("name") ?? "";
            var invertsByPipe = new Dictionary<(string Struct, string Pipe), double>();

            foreach (var s in net.Descendants().Where(e => e.Name.LocalName == "Struct"))
            {
                string name = (string?)s.Attribute("name") ?? "";
                double? n = null, e = null;
                if (Kid(s, "Center") is { } center)
                {
                    var t = center.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length >= 2 && double.TryParse(t[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double nn) && double.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ee))
                        (n, e) = (nn, ee);
                    else problems.Add($"{netName} structure \"{name}\": location \"{center.Value.Trim()}\" is not readable.");
                }
                var inverts = new List<StructureInvert>();
                foreach (var inv in Kids(s, "Invert"))
                {
                    string pipe = (string?)inv.Attribute("refPipe") ?? "";
                    if (Num(inv, "elev") is not { } el) { problems.Add($"{netName} structure \"{name}\": an invert for pipe \"{pipe}\" has no readable elevation."); continue; }
                    inverts.Add(new StructureInvert(pipe, (string?)inv.Attribute("flowDir") ?? "", el));
                    invertsByPipe[(name, pipe)] = el;
                }
                string kind = s.Elements().Select(x => x.Name.LocalName).FirstOrDefault(x => x is "InletStruct" or "OutletStruct" or "CircStruct" or "RectStruct" or "Connection") switch
                {
                    "InletStruct" => "Inlet",
                    "OutletStruct" => "Outlet",
                    "CircStruct" => "Manhole (circular)",
                    "RectStruct" => "Structure (rectangular)",
                    "Connection" => "Connection",
                    _ => "Structure",
                };
                structs.Add(new DrainageStructure
                {
                    Network = netName, Name = name, Kind = kind, Description = (string?)s.Attribute("desc") ?? "",
                    Northing = n, Easting = e, RimElevation = Num(s, "elevRim"), SumpElevation = Num(s, "elevSump"), Inverts = inverts,
                });
            }

            foreach (var p in net.Descendants().Where(e => e.Name.LocalName == "Pipe"))
            {
                string name = (string?)p.Attribute("name") ?? "";
                string from = (string?)p.Attribute("refStart") ?? "", to = (string?)p.Attribute("refEnd") ?? "";
                string where = $"{netName} pipe \"{name}\"";
                NetworkPipeShape shape; double rise, span; string material;
                if (Kid(p, "CircPipe") is { } cp && Num(cp, "diameter") is { } d && d > 0)
                    (shape, rise, span, material) = (NetworkPipeShape.Circular, d * diaFactor, d * diaFactor, (string?)cp.Attribute("material") ?? "");
                else if ((Kid(p, "ElliPipe") ?? Kid(p, "EllipPipe")) is { } ep && Num(ep, "height") is { } h && h > 0 && Num(ep, "span") is { } sp && sp > 0)
                    (shape, rise, span, material) = (NetworkPipeShape.Elliptical, h * diaFactor, sp * diaFactor, (string?)ep.Attribute("material") ?? "");
                else
                {
                    var kind = p.Elements().Select(x => x.Name.LocalName).FirstOrDefault(x => x.EndsWith("Pipe") || x == "Channel") ?? "no shape";
                    problems.Add($"{where}: shape \"{kind}\" is not a circular or elliptical pipe with readable size; skipped.");
                    continue;
                }
                if (units == UnitSystem.USCustomary && rise > 15)
                    problems.Add($"{where}: size {rise.ToString("G6", CultureInfo.InvariantCulture)} ft looks like inches; check the export. Not converted.");
                else if (units == UnitSystem.SI && rise > 10)
                    problems.Add($"{where}: size {rise.ToString("G6", CultureInfo.InvariantCulture)} m looks like millimetres; check the export. Not converted.");

                double? up = invertsByPipe.TryGetValue((from, name), out var a) ? a : null;
                double? down = invertsByPipe.TryGetValue((to, name), out var b) ? b : null;
                var pipe = new LandXmlPipe(netName, name, from, to, shape, rise, span, Num(p, "length"), up, down, material, (string?)p.Attribute("desc") ?? "");
                if (pipe.Slope is null)
                    problems.Add($"{where}: slope cannot be computed (needs both inverts and the length); set it yourself.");
                else if (pipe.Slope <= 0)
                    problems.Add($"{where}: inverts give a slope of {(100 * pipe.Slope.Value).ToString("F3", CultureInfo.InvariantCulture)} % (flat or adverse); check flow direction.");
                pipes.Add(pipe);
            }
        }
        if (pipes.Count == 0 && structs.Count == 0 && !problems.Any(p => p.Contains("skipped")))
            problems.Add("The file contains no pipe networks (PipeNetwork elements). Export the pipe network to LandXML from the design program.");
        return new(pipes, structs, units, linear, problems);
    }
}
