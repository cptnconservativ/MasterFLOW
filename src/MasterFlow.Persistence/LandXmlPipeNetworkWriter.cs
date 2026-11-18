using System.Globalization;
using System.Text;
using System.Xml;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Persistence;

/// <summary>
/// Writes a storm network as LandXML 1.2 (Units, PipeNetworks/PipeNetwork/Structs/Pipes), the form Civil 3D
/// imports as a pipe network (Insert → LandXML). Values are written in the network's own units with
/// linearUnit and diameterUnit stated (foot or meter), so nothing is converted. Structure inverts carry the
/// pipe inverts; when a solved result is given, HGL/EGL are added as Feature properties for reference.
/// Structures need northing/easting (imported from LandXML or typed) for Civil 3D to place them.
/// </summary>
public static class LandXmlPipeNetworkWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string N(double v) => v.ToString("0.######", Inv);

    public static (string Xml, IReadOnlyList<string> Warnings) Write(StormNetwork net, StormNetworkResult? result = null, string? appVersion = null, DateTimeOffset? when = null)
    {
        var warnings = new List<string>();
        var t = (when ?? DateTimeOffset.Now);
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8, OmitXmlDeclaration = false }))
        {
            const string ns = "http://www.landxml.org/schema/LandXML-1.2";
            w.WriteStartDocument();
            w.WriteStartElement("LandXML", ns);
            w.WriteAttributeString("version", "1.2");
            w.WriteAttributeString("date", t.ToString("yyyy-MM-dd", Inv));
            w.WriteAttributeString("time", t.ToString("HH:mm:ss", Inv));
            w.WriteStartElement("Units", ns);
            if (net.Units == UnitSystem.SI)
            {
                w.WriteStartElement("Metric", ns);
                w.WriteAttributeString("areaUnit", "squareMeter"); w.WriteAttributeString("linearUnit", "meter"); w.WriteAttributeString("volumeUnit", "cubicMeter");
                w.WriteAttributeString("temperatureUnit", "celsius"); w.WriteAttributeString("pressureUnit", "milliBars"); w.WriteAttributeString("diameterUnit", "meter");
            }
            else
            {
                w.WriteStartElement("Imperial", ns);
                w.WriteAttributeString("areaUnit", "squareFoot"); w.WriteAttributeString("linearUnit", "foot"); w.WriteAttributeString("volumeUnit", "cubicFeet");
                w.WriteAttributeString("temperatureUnit", "fahrenheit"); w.WriteAttributeString("pressureUnit", "inHG"); w.WriteAttributeString("diameterUnit", "foot");
            }
            w.WriteAttributeString("angularUnit", "decimal degrees"); w.WriteAttributeString("directionUnit", "decimal degrees");
            w.WriteEndElement(); w.WriteEndElement();
            w.WriteStartElement("Application", ns);
            w.WriteAttributeString("name", "MasterFlow"); w.WriteAttributeString("manufacturer", "MasterFlow");
            w.WriteAttributeString("version", appVersion ?? AppInfo.Version);
            w.WriteEndElement();

            w.WriteStartElement("PipeNetworks", ns);
            w.WriteStartElement("PipeNetwork", ns);
            w.WriteAttributeString("name", net.Name);
            w.WriteAttributeString("pipeNetType", "storm");

            var outfalls = net.Structures.Where(s => net.Pipes.Any(p => p.To == s.Name) && !net.Pipes.Any(p => p.From == s.Name)).Select(s => s.Name).ToHashSet();
            w.WriteStartElement("Structs", ns);
            foreach (var s in net.Structures.Where(s => net.Pipes.Any(p => p.From == s.Name || p.To == s.Name)))
            {
                var ins = net.Pipes.Where(p => p.To == s.Name).ToList();
                var outs = net.Pipes.Where(p => p.From == s.Name).ToList();
                var inverts = ins.Select(p => (p.Name, Dir: "in", Elev: p.DownstreamInvert)).Concat(outs.Select(p => (p.Name, Dir: "out", Elev: p.UpstreamInvert))).ToList();
                w.WriteStartElement("Struct", ns);
                w.WriteAttributeString("name", s.Name);
                if (s.RimElevation is { } rim) w.WriteAttributeString("elevRim", N(rim));
                else warnings.Add($"Structure {s.Name} has no rim elevation.");
                if (inverts.Count > 0) w.WriteAttributeString("elevSump", N(inverts.Min(i => i.Elev)));
                if (s.Kind.Length > 0) w.WriteAttributeString("desc", s.Kind);
                if (s.Northing is { } n && s.Easting is { } e) w.WriteElementString("Center", ns, $"{N(n)} {N(e)}");
                else warnings.Add($"Structure {s.Name} has no northing/easting; Civil 3D cannot place it. Import the network from LandXML first, or add coordinates.");
                string kind = s.Kind.ToLowerInvariant();
                if (kind.Contains("inlet") || kind.Contains("catch")) w.WriteElementString("InletStruct", ns, "");
                else if (outfalls.Contains(s.Name) || kind.Contains("outlet") || kind.Contains("outfall") || kind.Contains("headwall")) w.WriteElementString("OutletStruct", ns, "");
                else
                {
                    w.WriteStartElement("CircStruct", ns);
                    w.WriteAttributeString("diameter", net.Units == UnitSystem.SI ? "1.2" : "4");
                    w.WriteEndElement();
                }
                foreach (var (pipe, dir, elev) in inverts)
                {
                    w.WriteStartElement("Invert", ns);
                    w.WriteAttributeString("elev", N(elev)); w.WriteAttributeString("flowDir", dir); w.WriteAttributeString("refPipe", pipe);
                    w.WriteEndElement();
                }
                if (result?.Structure(s.Name) is { } sr)
                    Feature(w, ns, ("HGL", sr.Hgl), ("EGL", sr.Egl), ("StructureLoss", sr.IsOutfall ? double.NaN : sr.Loss));
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteStartElement("Pipes", ns);
            foreach (var p in net.Pipes)
            {
                w.WriteStartElement("Pipe", ns);
                w.WriteAttributeString("name", p.Name);
                w.WriteAttributeString("refStart", p.From);
                w.WriteAttributeString("refEnd", p.To);
                w.WriteAttributeString("length", N(p.Length));
                if (p.Shape == NetworkPipeShape.Elliptical)
                {
                    w.WriteStartElement("EllipPipe", ns);
                    w.WriteAttributeString("height", N(p.Rise)); w.WriteAttributeString("span", N(p.Span));
                }
                else
                {
                    w.WriteStartElement("CircPipe", ns);
                    w.WriteAttributeString("diameter", N(p.Rise));
                }
                w.WriteEndElement();
                if (result?.Pipe(p.Name) is { } pr)
                    Feature(w, ns, ("Q", pr.Pipe.Discharge), ("ManningN", p.ManningN), ("HglUp", pr.HglUp), ("HglDown", pr.HglDown), ("EglUp", pr.EglUp), ("EglDown", pr.EglDown));
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement(); w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        // XmlWriter into a StringBuilder declares utf-16; the file is written as UTF-8.
        return (sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\""), warnings);
    }

    private static void Feature(XmlWriter w, string ns, params (string Label, double Value)[] props)
    {
        w.WriteStartElement("Feature", ns);
        w.WriteAttributeString("name", "MasterFlow hydraulics");
        w.WriteAttributeString("source", "MasterFlow");
        foreach (var (label, value) in props.Where(x => double.IsFinite(x.Value)))
        {
            w.WriteStartElement("Property", ns);
            w.WriteAttributeString("label", label); w.WriteAttributeString("value", N(value));
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }
}
