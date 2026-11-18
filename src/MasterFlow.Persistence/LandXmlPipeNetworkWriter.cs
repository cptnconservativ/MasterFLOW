using System.Globalization;
using System.Text;
using System.Xml;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Persistence;

/// <summary>
/// Writes a storm network as LandXML 1.2 (Units, PipeNetworks/PipeNetwork/Structs/Pipes), the form Civil 3D
/// imports as a pipe network (Insert → LandXML). The element order and names follow LandXML-1.2.xsd (checked
/// by a schema-validation test). Values are written in the network's own units with linearUnit, diameterUnit,
/// flowUnit and velocityUnit stated, so nothing is converted.
/// <para>
/// When a solved result is given, the schema's own hydraulic elements carry the main values — PipeFlow
/// (flowIn, hglUp, hglDown, depthCritical) on each pipe and StructFlow (hglIn, hglOut, lossIn = structure head
/// loss, lossOut = 0) on each structure — and a "MasterFlow hydraulics" Feature on every part lists the full
/// set (EGL, velocities, capacity, depths, regime, criteria check). Feature/Property is the schema's extension
/// mechanism; Civil 3D keeps or ignores it on import, but it never changes the network geometry.
/// </para>
/// Structures need northing/easting (imported from LandXML or typed) for Civil 3D to place them; the schema
/// requires a Center, so a structure without one is written without it and listed as a warning.
/// </summary>
public static class LandXmlPipeNetworkWriter
{
    private const string Ns = "http://www.landxml.org/schema/LandXML-1.2";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string N(double v) => v.ToString("0.######", Inv);

    public static (string Xml, IReadOnlyList<string> Warnings) Write(StormNetwork net, StormNetworkResult? result = null, string? appVersion = null, DateTimeOffset? when = null)
    {
        var warnings = new List<string>();
        if (result is { Succeeded: false }) { warnings.Add("The network did not solve; hydraulic results are not included."); result = null; }
        else if (result is null) warnings.Add("No hydraulic results: the network has not been solved, so only geometry is exported.");
        var pipeResults = result is null ? new Dictionary<string, PipePartResult>() : NetworkPartResults.Pipes(result).ToDictionary(p => p.Name);
        var structResults = result is null ? new Dictionary<string, StructurePartResult>() : NetworkPartResults.Structures(result).ToDictionary(s => s.Name);
        bool si = net.Units == UnitSystem.SI;

        var t = (when ?? DateTimeOffset.Now);
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8, OmitXmlDeclaration = false }))
        {
            w.WriteStartDocument();
            w.WriteStartElement("LandXML", Ns);
            w.WriteAttributeString("version", "1.2");
            w.WriteAttributeString("date", t.ToString("yyyy-MM-dd", Inv));
            w.WriteAttributeString("time", t.ToString("HH:mm:ss", Inv));
            w.WriteStartElement("Units", Ns);
            if (si)
            {
                w.WriteStartElement("Metric", Ns);
                Attrs(w, ("areaUnit", "squareMeter"), ("linearUnit", "meter"), ("volumeUnit", "cubicMeter"), ("temperatureUnit", "celsius"), ("pressureUnit", "milliBars"),
                    ("diameterUnit", "meter"), ("velocityUnit", "metersPerSecond"), ("flowUnit", "cubicMeterSecond"));
            }
            else
            {
                w.WriteStartElement("Imperial", Ns);
                Attrs(w, ("areaUnit", "squareFoot"), ("linearUnit", "foot"), ("volumeUnit", "cubicFeet"), ("temperatureUnit", "fahrenheit"), ("pressureUnit", "inHG"),
                    ("diameterUnit", "foot"), ("velocityUnit", "feetPerSecond"), ("flowUnit", "cubicFeetSecond"));
            }
            Attrs(w, ("angularUnit", "decimal degrees"), ("directionUnit", "decimal degrees"));
            w.WriteEndElement(); w.WriteEndElement();
            w.WriteStartElement("Application", Ns);
            Attrs(w, ("name", "MasterFlow"), ("manufacturer", "MasterFlow"), ("version", appVersion ?? AppInfo.Version));
            w.WriteEndElement();

            w.WriteStartElement("PipeNetworks", Ns);
            w.WriteStartElement("PipeNetwork", Ns);
            Attrs(w, ("name", net.Name), ("pipeNetType", "storm"));

            var outfalls = net.Structures.Where(s => net.Pipes.Any(p => p.To == s.Name) && !net.Pipes.Any(p => p.From == s.Name)).Select(s => s.Name).ToHashSet();
            w.WriteStartElement("Structs", Ns);
            foreach (var s in net.Structures.Where(s => net.Pipes.Any(p => p.From == s.Name || p.To == s.Name)))
            {
                var ins = net.Pipes.Where(p => p.To == s.Name).ToList();
                var outs = net.Pipes.Where(p => p.From == s.Name).ToList();
                var inverts = ins.Select(p => (p.Name, Dir: "in", Elev: p.DownstreamInvert)).Concat(outs.Select(p => (p.Name, Dir: "out", Elev: p.UpstreamInvert))).ToList();
                w.WriteStartElement("Struct", Ns);
                w.WriteAttributeString("name", s.Name);
                if (s.Kind.Length > 0) w.WriteAttributeString("desc", s.Kind);
                if (s.RimElevation is { } rim) w.WriteAttributeString("elevRim", N(rim));
                else warnings.Add($"Structure {s.Name} has no rim elevation.");
                if (inverts.Count > 0) w.WriteAttributeString("elevSump", N(inverts.Min(i => i.Elev)));
                if (s.Northing is { } n && s.Easting is { } e) w.WriteElementString("Center", Ns, $"{N(n)} {N(e)}");
                else warnings.Add($"Structure {s.Name} has no northing/easting; Civil 3D cannot place it. Import the network from LandXML first, or add coordinates.");
                string kind = s.Kind.ToLowerInvariant();
                if (kind.Contains("inlet") || kind.Contains("catch")) w.WriteElementString("InletStruct", Ns, "");
                else if (outfalls.Contains(s.Name) || kind.Contains("outlet") || kind.Contains("outfall") || kind.Contains("headwall")) w.WriteElementString("OutletStruct", Ns, "");
                else
                {
                    w.WriteStartElement("CircStruct", Ns);
                    w.WriteAttributeString("diameter", si ? "1.2" : "4");
                    w.WriteEndElement();
                }
                foreach (var (pipe, dir, elev) in inverts)
                {
                    w.WriteStartElement("Invert", Ns);
                    Attrs(w, ("elev", N(elev)), ("flowDir", dir), ("refPipe", pipe));
                    w.WriteEndElement();
                }
                if (structResults.TryGetValue(s.Name, out var sr))
                {
                    w.WriteStartElement("StructFlow", Ns);
                    Attrs(w, ("lossIn", N(double.IsFinite(sr.Loss) ? sr.Loss : 0)), ("lossOut", "0"), ("desc", "MasterFlow: lossIn = structure head loss; hglIn = HGL in the structure; hglOut = HGL entering the outlet pipe"),
                        ("hglIn", N(sr.Hgl)), ("hglOut", N(sr.OutletHgl)));
                    w.WriteEndElement();
                    Feature(w,
                        ("HGL", Num(sr.Hgl)), ("EGL", Num(sr.Egl)), ("HGL leaving", Num(sr.OutletHgl)), ("Structure loss", Num(sr.Loss)), ("Loss method", sr.LossMethod),
                        ("Rim minus HGL", sr.HglClearance is { } hc ? Num(hc) : ""), ("Rim minus EGL", sr.EglClearance is { } ec ? Num(ec) : ""),
                        ("Outfall", sr.IsOutfall ? "yes" : "no"), ("Check", NetworkPartResults.Status(sr.Failures)));
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteStartElement("Pipes", Ns);
            foreach (var p in net.Pipes)
            {
                w.WriteStartElement("Pipe", Ns);
                Attrs(w, ("name", p.Name), ("refEnd", p.To), ("refStart", p.From), ("length", N(p.Length)));
                if (p.Length > 0) w.WriteAttributeString("slope", N((p.UpstreamInvert - p.DownstreamInvert) / p.Length));
                if (p.Shape == NetworkPipeShape.Elliptical)
                {
                    w.WriteStartElement("ElliPipe", Ns);
                    Attrs(w, ("height", N(p.Rise)), ("span", N(p.Span)));
                }
                else
                {
                    w.WriteStartElement("CircPipe", Ns);
                    w.WriteAttributeString("diameter", N(p.Rise));
                }
                w.WriteAttributeString("mannings", N(p.ManningN));
                w.WriteEndElement();
                if (pipeResults.TryGetValue(p.Name, out var pr))
                {
                    w.WriteStartElement("PipeFlow", Ns);
                    w.WriteAttributeString("flowIn", N(pr.Discharge));
                    if (double.IsFinite(pr.CriticalDepth)) w.WriteAttributeString("depthCritical", N(pr.CriticalDepth));
                    Attrs(w, ("desc", "MasterFlow design storm"), ("hglDown", N(pr.HglDown)), ("hglUp", N(pr.HglUp)));
                    w.WriteEndElement();
                    Feature(w,
                        ("Q", Num(pr.Discharge)), ("Q full", Num(pr.FullFlowCapacity)), ("Q/Q full", Num(pr.CapacityRatio, "0.###")),
                        ("Manning n", Num(p.ManningN)), ("V up", Num(pr.VelocityUp)), ("V down", Num(pr.VelocityDown)), ("V full", Num(pr.FullFlowVelocity)),
                        ("Depth up", Num(pr.DepthUp)), ("Depth down", Num(pr.DepthDown)), ("Normal depth", Num(pr.NormalDepth)), ("Critical depth", Num(pr.CriticalDepth)),
                        ("HGL up", Num(pr.HglUp)), ("HGL down", Num(pr.HglDown)), ("EGL up", Num(pr.EglUp)), ("EGL down", Num(pr.EglDown)),
                        ("Friction loss", Num(pr.FrictionLoss)), ("Regime", pr.Regime), ("Condition", pr.Condition), ("Flows full", pr.FlowsFull ? "yes" : "no"),
                        ("Minor storm Q", Num(pr.MinorDischarge)), ("Minor storm V", Num(pr.MinorVelocity)), ("Check", NetworkPartResults.Status(pr.Failures)));
                }
                w.WriteEndElement();
            }
            w.WriteEndElement();

            if (result is not null)
            {
                var (len, flow, vel) = NetworkPartResults.UnitLabels(net.Units);
                Feature(w, ("Units", $"{len}, {flow}, {vel}"), ("Criteria", net.Criteria.Source), ("Tailwater", net.Tailwater.ToString()),
                    ("Warnings", result.Messages.Count(m => m.Severity == MessageSeverity.Warning).ToString(Inv)), ("Computed", t.ToString("yyyy-MM-dd HH:mm", Inv)));
            }
            w.WriteEndElement(); w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        // XmlWriter into a StringBuilder declares utf-16; the file is written as UTF-8.
        return (sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\""), warnings);
    }

    private static string Num(double v, string format = "0.######") => NetworkPartResults.Num(v, format);

    private static void Attrs(XmlWriter w, params (string Name, string Value)[] attrs)
    {
        foreach (var (name, value) in attrs) w.WriteAttributeString(name, value);
    }

    /// <summary>A "MasterFlow hydraulics" Feature; blank values (not computed) are left out.</summary>
    private static void Feature(XmlWriter w, params (string Label, string Value)[] props)
    {
        w.WriteStartElement("Feature", Ns);
        Attrs(w, ("name", "MasterFlow hydraulics"), ("code", "MasterFlow"), ("source", "MasterFlow"));
        foreach (var (label, value) in props.Where(x => x.Value.Length > 0))
        {
            w.WriteStartElement("Property", Ns);
            Attrs(w, ("label", label), ("value", value));
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }
}
