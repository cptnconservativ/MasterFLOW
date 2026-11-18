using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Persistence;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Results back to Civil 3D: the LandXML export validates against the published LandXML-1.2.xsd and carries the
/// solved HGL/EGL in PipeFlow/StructFlow and Feature data; the AutoLISP write-back and CSV hold the same numbers.
/// </summary>
public class Civil3DRoundTripTests
{
    private static readonly XNamespace Ns = "http://www.landxml.org/schema/LandXML-1.2";
    private static readonly DateTimeOffset When = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>NetA with coordinates, an elliptical branch from an inlet, and Rapid City criteria.</summary>
    private static StormNetwork Net(UnitSystem u = UnitSystem.USCustomary)
    {
        var a = NetworkOutputTests.NetA();
        return a with
        {
            Units = u,
            Structures = [.. a.Structures.Select((s, i) => s with { Northing = 5000 + 100 * i, Easting = 2000 + 50 * i }),
                          new NetworkStructure { Name = "CB-9", Kind = "Inlet", RimElevation = 106, Northing = 5400, Easting = 2200 }],
            Pipes = [.. a.Pipes, new NetworkPipe { Name = "E1", From = "CB-9", To = "MH-2", Shape = NetworkPipeShape.Elliptical, Rise = 19.0 / 12, Span = 30.0 / 12, Length = 40, UpstreamInvert = 101.9, DownstreamInvert = 101.5, Discharge = 2 }],
        };
    }

    private static List<string> SchemaErrors(string xml)
    {
        var set = new XmlSchemaSet();
        set.Add(Ns.NamespaceName, Path.Combine(AppContext.BaseDirectory, "Schemas", "LandXML-1.2.xsd"));
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = set };
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Severity}: {e.Message} (line {e.Exception?.LineNumber})");
        using var r = XmlReader.Create(new StringReader(xml), settings);
        while (r.Read()) { }
        return errors;
    }

    private static double D(XElement e, string attr) => double.Parse((string)e.Attribute(attr)!, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(UnitSystem.USCustomary, true)]
    [InlineData(UnitSystem.SI, true)]
    [InlineData(UnitSystem.USCustomary, false)]
    public void Export_ValidatesAgainstLandXml12Schema(UnitSystem units, bool solved)
    {
        var n = Net(units);
        var r = solved ? StormNetworkSolver.Solve(n) : null;
        if (solved) Assert.True(r!.Succeeded);
        var (xml, warnings) = LandXmlPipeNetworkWriter.Write(n, r, "test", When);
        Assert.Empty(SchemaErrors(xml));
        Assert.Equal(solved, !warnings.Any(w => w.Contains("No hydraulic results")));
        var doc = XDocument.Parse(xml);
        Assert.Equal(solved ? n.Pipes.Count : 0, doc.Descendants(Ns + "PipeFlow").Count());
        Assert.Equal(solved ? n.Structures.Count : 0, doc.Descendants(Ns + "StructFlow").Count());
        var unitEl = doc.Root!.Element(Ns + "Units")!.Elements().Single();
        Assert.Equal(units == UnitSystem.SI ? "cubicMeterSecond" : "cubicFeetSecond", (string?)unitEl.Attribute("flowUnit"));
        Assert.Equal(units == UnitSystem.SI ? "metersPerSecond" : "feetPerSecond", (string?)unitEl.Attribute("velocityUnit"));
    }

    [Fact]
    public void SchemaCheck_CatchesTheOldEllipPipeTag()
    {
        // The validator is live: the pre-fix tag name and a missing Center are both rejected.
        var n = Net();
        string xml = LandXmlPipeNetworkWriter.Write(n, StormNetworkSolver.Solve(n), "test", When).Xml;
        Assert.NotEmpty(SchemaErrors(xml.Replace("ElliPipe", "EllipPipe")));
        var noCenter = n with { Structures = [.. n.Structures.Select(s => s.Name == "MH-2" ? s with { Northing = null } : s)] };
        var (xml2, warnings) = LandXmlPipeNetworkWriter.Write(noCenter, null, "test", When);
        Assert.Contains(warnings, w => w.Contains("MH-2 has no northing/easting"));
        Assert.Contains(SchemaErrors(xml2), e => e.Contains("Center"));
    }

    [Fact]
    public void Export_PipeFlowAndStructFlow_CarryTheSolvedGrades()
    {
        var n = Net();
        var r = StormNetworkSolver.Solve(n);
        var doc = XDocument.Parse(LandXmlPipeNetworkWriter.Write(n, r, "test", When).Xml);
        foreach (var pr in r.Pipes)
        {
            var pipe = doc.Descendants(Ns + "Pipe").Single(p => (string?)p.Attribute("name") == pr.Pipe.Name);
            var flow = pipe.Element(Ns + "PipeFlow")!;
            Assert.Equal(pr.Pipe.Discharge, D(flow, "flowIn"), 1e-5);
            Assert.Equal(pr.HglUp, D(flow, "hglUp"), 1e-5);
            Assert.Equal(pr.HglDown, D(flow, "hglDown"), 1e-5);
            Assert.Equal(pr.CriticalDepth, D(flow, "depthCritical"), 1e-5);
            Assert.Equal((pr.Pipe.UpstreamInvert - pr.Pipe.DownstreamInvert) / pr.Pipe.Length, D(pipe, "slope"), 1e-6);
            var props = pipe.Element(Ns + "Feature")!.Elements(Ns + "Property").ToDictionary(p => (string)p.Attribute("label")!, p => (string)p.Attribute("value")!);
            Assert.Equal(pr.EglUp, double.Parse(props["EGL up"], CultureInfo.InvariantCulture), 1e-5);
            Assert.Equal(pr.VelocityDown, double.Parse(props["V down"], CultureInfo.InvariantCulture), 1e-5);
            Assert.Equal(pr.FullFlowCapacity, double.Parse(props["Q full"], CultureInfo.InvariantCulture), 1e-5);
            Assert.Equal(pr.Pipe.Discharge / pr.FullFlowCapacity, double.Parse(props["Q/Q full"], CultureInfo.InvariantCulture), 1e-3);
            Assert.True(props.ContainsKey("Check"));
        }
        // Elliptical pipes use the schema's ElliPipe and read back.
        Assert.Single(doc.Descendants(Ns + "ElliPipe"));
        Assert.Empty(doc.Descendants(Ns + "EllipPipe"));

        var mh = r.Structure("MH-2")!;
        var sf = doc.Descendants(Ns + "Struct").Single(s => (string?)s.Attribute("name") == "MH-2").Element(Ns + "StructFlow")!;
        Assert.Equal(mh.Hgl, D(sf, "hglIn"), 1e-5);
        Assert.Equal(mh.Loss, D(sf, "lossIn"), 1e-5);
        Assert.Equal(r.Pipe("P1")!.HglUp, D(sf, "hglOut"), 1e-5);   // HGL entering the outlet pipe
        // Every pipe and structure at an outfall still has a StructFlow, with zero loss.
        var outSf = doc.Descendants(Ns + "Struct").Single(s => (string?)s.Attribute("name") == "OUT").Element(Ns + "StructFlow")!;
        Assert.Equal(0.0, D(outSf, "lossIn"));
    }

    [Fact]
    public void Export_CheckProperty_NamesTheFailedCriteria()
    {
        // Tailwater 0.2 ft below MH-2's rim: Rapid City's 1 ft HGL clearance fails there.
        var n = Net() with { TailwaterElevation = 105.8 };
        var r = StormNetworkSolver.Solve(n);
        Assert.Contains(r.Messages, m => m.Code == "W-NW-001" && m.Text.StartsWith("Structure \"MH-2\""));
        var doc = XDocument.Parse(LandXmlPipeNetworkWriter.Write(n, r, "test", When).Xml);
        string Check(string name) => (string)doc.Descendants(Ns + "Struct").Single(s => (string?)s.Attribute("name") == name)
            .Element(Ns + "Feature")!.Elements(Ns + "Property").Single(p => (string?)p.Attribute("label") == "Check").Attribute("value")!;
        Assert.Contains("W-NW-001", Check("MH-2"));
        // A part whose name is a prefix of another is not tagged with the other's warnings.
        var parts = NetworkPartResults.Structures(r);
        Assert.All(parts.Where(p => p.Failures.Count == 0), p => Assert.Equal("OK", NetworkPartResults.Status(p.Failures)));
    }

    [Fact]
    public void Export_UnsolvableNetwork_WritesGeometryAndSaysWhy()
    {
        var n = Net() with { Pipes = [.. Net().Pipes.Select(p => p.Name == "P1" ? p with { ManningN = -1 } : p)] };
        var r = StormNetworkSolver.Solve(n);
        Assert.False(r.Succeeded);
        var (xml, warnings) = LandXmlPipeNetworkWriter.Write(n, r, "test", When);
        Assert.Contains(warnings, w => w.Contains("did not solve"));
        Assert.Empty(XDocument.Parse(xml).Descendants(Ns + "PipeFlow"));
        Assert.Throws<InvalidOperationException>(() => Civil3DWriteBack.AutoLisp(n, r));
        Assert.Throws<InvalidOperationException>(() => Civil3DWriteBack.Csv(n, r));
    }

    [Fact]
    public void Reader_AcceptsSchemaElliPipe_AndLegacyEllipPipe()
    {
        foreach (var tag in new[] { "ElliPipe", "EllipPipe" })
        {
            string x = $"""
                <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
                  <Units><Imperial linearUnit="foot" diameterUnit="inch"/></Units>
                  <PipeNetworks><PipeNetwork name="N" pipeNetType="storm"><Structs>
                    <Struct name="A" elevRim="110"><Center>100 200</Center><InletStruct/><Invert elev="100" flowDir="out" refPipe="P"/></Struct>
                    <Struct name="B" elevRim="109"><Center>150 200</Center><OutletStruct/><Invert elev="99" flowDir="in" refPipe="P"/></Struct>
                  </Structs><Pipes><Pipe name="P" refStart="A" refEnd="B" length="100"><{tag} height="19" span="30"/></Pipe></Pipes></PipeNetwork></PipeNetworks>
                </LandXML>
                """;
            var p = LandXmlPipeNetworks.Read(new MemoryStream(Encoding.UTF8.GetBytes(x))).Pipes.Single();
            Assert.Equal(NetworkPipeShape.Elliptical, p.Shape);
            Assert.Equal(19.0 / 12, p.Rise, 1e-9);
            Assert.Equal(30.0 / 12, p.Span, 1e-9);
        }
    }

    // ------------------------------------------------------------------ AutoLISP write-back

    /// <summary>Parenthesis depth over the file, skipping strings and ; comments; returns the minimum depth seen and the final depth.</summary>
    private static (int Min, int End) ParenBalance(string lisp)
    {
        int depth = 0, min = 0;
        bool inStr = false;
        for (int i = 0; i < lisp.Length; i++)
        {
            char c = lisp[i];
            if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
            if (c == '"') inStr = true;
            else if (c == ';') { while (i < lisp.Length && lisp[i] != '\n') i++; }
            else if (c == '(') depth++;
            else if (c == ')') { depth--; min = Math.Min(min, depth); }
        }
        Assert.False(inStr, "unterminated string");
        return (min, depth);
    }

    [Fact]
    public void AutoLisp_HoldsEveryPipesGrades_AndIsBalanced()
    {
        var n = Net();
        var r = StormNetworkSolver.Solve(n);
        var (lisp, warnings) = Civil3DWriteBack.AutoLisp(n, r, "test", When);
        Assert.Empty(warnings);
        Assert.Equal((0, 0), ParenBalance(lisp));
        Assert.Contains("(defun c:MFHGL", lisp);
        Assert.Contains("(setq mf:network \"Main St\")", lisp);
        Assert.Contains("vla-StartUndoMark", lisp);
        Assert.Contains("vla-EndUndoMark", lisp);
        foreach (var p in r.Pipes)
        {
            string row = $"  (\"{p.Pipe.Name}\" {Civil3DWriteBack.Real(p.HglUp)} {Civil3DWriteBack.Real(p.HglDown)} {Civil3DWriteBack.Real(p.EglUp)} {Civil3DWriteBack.Real(p.EglDown)} {Civil3DWriteBack.Real(p.Pipe.Discharge)})";
            Assert.Contains(row, lisp);
        }
        foreach (var (_, names) in Civil3DWriteBack.PipeProperties)
            Assert.Contains(string.Join(" ", names), lisp);
        Assert.Contains("elevations ft, flow cfs", lisp);
        Assert.DoesNotContain("NaN", lisp);
        Assert.Contains(";;; Criteria: City of Rapid City Infrastructure Design Criteria Manual (Jan. 2012) Sec.4.6", lisp);
        Assert.All(lisp.Split('\n').Where(l => l.StartsWith(';')), l => Assert.True(l.All(c => c < 127), l));
    }

    [Fact]
    public void AutoLisp_NumbersAreInvariant_AndNamesEscaped()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            // A decimal-comma culture (built by hand: the tests may run in globalization-invariant mode).
            var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            comma.NumberFormat.NumberGroupSeparator = ".";
            CultureInfo.CurrentCulture = comma;
            Assert.Equal("1,5", 1.5.ToString(CultureInfo.CurrentCulture));
            var a = Net();
            var n = a with
            {
                Name = "Main \"St\" C:\\net",
                Structures = [.. a.Structures.Select(s => s.Name == "CB-3" ? s with { Name = "CB \"3\"" } : s)],
                Pipes = [.. a.Pipes.Select(p => p.Name == "P2" ? p with { Name = "Pipe - (2)", From = "CB \"3\"" } : p)],
            };
            var r = StormNetworkSolver.Solve(n);
            Assert.True(r.Succeeded);
            var (lisp, _) = Civil3DWriteBack.AutoLisp(n, r, "test", When);
            Assert.Equal((0, 0), ParenBalance(lisp));
            Assert.Contains("(setq mf:network \"Main \\\"St\\\" C:\\\\net\")", lisp);
            Assert.Contains("(\"Pipe - (2)\" ", lisp);
            Assert.Matches(@"\(""P1"" \d+\.\d+ \d+\.\d+ ", lisp);
            Assert.DoesNotMatch(@"\(""P1"" \d+,\d", lisp);
            Assert.Equal("100.0", Civil3DWriteBack.Real(100));
            Assert.Equal("-0.25", Civil3DWriteBack.Real(-0.25));
            Assert.Equal("nil", Civil3DWriteBack.Real(double.NaN));
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public void AutoLisp_Metric_LabelsUnits()
    {
        var n = Net(UnitSystem.SI) with
        {
            Pipes = [.. Net().Pipes.Select(p => p with { Rise = p.Rise * 0.3048, Span = p.Span * 0.3048, Length = p.Length * 0.3048, UpstreamInvert = p.UpstreamInvert * 0.3048, DownstreamInvert = p.DownstreamInvert * 0.3048, Discharge = p.Discharge * 0.0283168 })],
            Structures = [.. Net().Structures.Select(s => s with { RimElevation = s.RimElevation * 0.3048 })],
            TailwaterElevation = 102.5 * 0.3048,
            Criteria = new NetworkCriteria(),
        };
        var r = StormNetworkSolver.Solve(n);
        Assert.True(r.Succeeded);
        var (lisp, _) = Civil3DWriteBack.AutoLisp(n, r, "test", When);
        Assert.Contains("elevations m, flow m3/s", lisp);
        Assert.Contains("(setq mf:units \"m, m3/s\")", lisp);
        Assert.Contains("Q (m³/s)", Civil3DWriteBack.Csv(n, r));
    }

    // ------------------------------------------------------------------ CSV

    private static List<string> CsvCells(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else if (c == '"') q = false; else sb.Append(c); }
            else if (c == '"') q = true;
            else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    [Fact]
    public void Csv_OneRowPerPart_ColumnsLineUp()
    {
        var a = Net();
        var n = a with { Name = "Main St, phase 1", Pipes = [.. a.Pipes.Select(p => p.Name == "P3" ? p with { Name = "P3 \"lateral\"" } : p)] };
        var r = StormNetworkSolver.Solve(n);
        var lines = Civil3DWriteBack.Csv(n, r).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + n.Pipes.Count + n.Structures.Count, lines.Length);
        var header = CsvCells(lines[0]);
        Assert.All(lines, l => Assert.Equal(header.Count, CsvCells(l).Count));
        int col(string h) => header.IndexOf(h);
        var p1 = CsvCells(lines.Single(l => CsvCells(l)[2] == "P1"));
        Assert.Equal("Main St, phase 1", p1[0]);
        Assert.Equal(r.Pipe("P1")!.HglUp, double.Parse(p1[col("HGL up (ft)")], CultureInfo.InvariantCulture), 1e-5);
        Assert.Equal(r.Pipe("P1")!.EglDown, double.Parse(p1[col("EGL down (ft)")], CultureInfo.InvariantCulture), 1e-5);
        Assert.Equal(r.Pipe("P1")!.FullFlowCapacity, double.Parse(p1[col("Q full (cfs)")], CultureInfo.InvariantCulture), 1e-5);
        Assert.Contains(lines, l => CsvCells(l)[2] == "P3 \"lateral\"");
        var mh = CsvCells(lines.Single(l => CsvCells(l)[2] == "MH-2"));
        Assert.Equal("Structure", mh[1]);
        Assert.Equal(r.Structure("MH-2")!.Hgl, double.Parse(mh[col("HGL (ft)")], CultureInfo.InvariantCulture), 1e-5);
        Assert.Equal(r.Structure("MH-2")!.HglClearance!.Value, double.Parse(mh[col("Rim - HGL (ft)")], CultureInfo.InvariantCulture), 1e-5);
    }
}
