using System.Text;
using System.Xml.Linq;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Persistence;

namespace MasterFlow.IntegrationTests;

/// <summary>LandXML pipe network export: round trip through the reader, units stated, hydraulics as Features.</summary>
public class LandXmlExportTests
{
    private static StormNetwork Net(UnitSystem u = UnitSystem.USCustomary) => NetworkOutputTests.NetA() with
    {
        Units = u,
        Structures = NetworkOutputTests.NetA().Structures.Select((s, i) => s with { Northing = 5000 + 100 * i, Easting = 2000 + 50 * i }).ToList(),
        Pipes = [.. NetworkOutputTests.NetA().Pipes, new NetworkPipe { Name = "E1", From = "CB-9", To = "MH-2", Shape = NetworkPipeShape.Elliptical, Rise = 19.0 / 12, Span = 30.0 / 12, Length = 40, UpstreamInvert = 101.9, DownstreamInvert = 101.5, Discharge = 2 }],
    };

    [Fact]
    public void Export_RoundTripsThroughTheReader()
    {
        var n = Net() with { Structures = [.. Net().Structures, new NetworkStructure { Name = "CB-9", Kind = "Inlet", RimElevation = 106, Northing = 5400, Easting = 2200 }] };
        var r = StormNetworkSolver.Solve(n);
        var (xml, warnings) = LandXmlPipeNetworkWriter.Write(n, r, "test", new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        Assert.Empty(warnings);
        var back = LandXmlPipeNetworks.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
        Assert.Equal(UnitSystem.USCustomary, back.Units);
        Assert.Empty(back.Problems);
        Assert.Equal(n.Pipes.Select(p => p.Name), back.Pipes.Select(p => p.Name));
        foreach (var p in n.Pipes)
        {
            var b = back.Pipes.Single(x => x.Name == p.Name);
            Assert.Equal((p.From, p.To, p.Shape), (b.StartStructure, b.EndStructure, b.Shape));
            Assert.Equal(p.Rise, b.Rise, 6);
            Assert.Equal(p.Span, b.Span, 6);
            Assert.Equal(p.Length, b.Length!.Value, 6);
            Assert.Equal(p.UpstreamInvert, b.StartInvert!.Value, 6);
            Assert.Equal(p.DownstreamInvert, b.EndInvert!.Value, 6);
        }
        var mh = back.Structures.Single(s => s.Name == "MH-2");
        Assert.Equal(106.0, mh.RimElevation);
        Assert.Equal(5100.0, mh.Northing);
        Assert.Equal(2050.0, mh.Easting);
        Assert.Equal("Inlet", back.Structures.Single(s => s.Name == "CB-3").Kind);
        Assert.Equal("Outlet", back.Structures.Single(s => s.Name == "OUT").Kind);
        Assert.StartsWith("Manhole", mh.Kind);

        // Units and hydraulics are explicit.
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.landxml.org/schema/LandXML-1.2";
        var imp = doc.Root!.Element(ns + "Units")!.Element(ns + "Imperial")!;
        Assert.Equal("foot", (string?)imp.Attribute("linearUnit"));
        Assert.Equal("foot", (string?)imp.Attribute("diameterUnit"));
        var hgl = doc.Descendants(ns + "Struct").Single(s => (string?)s.Attribute("name") == "MH-2")
            .Descendants(ns + "Property").Single(p => (string?)p.Attribute("label") == "HGL");
        Assert.Equal(r.Structure("MH-2")!.Hgl, double.Parse((string)hgl.Attribute("value")!, System.Globalization.CultureInfo.InvariantCulture), 5);
    }

    [Fact]
    public void Export_Metric_AndMissingCoordinatesAreWarned()
    {
        var n = NetworkOutputTests.NetA() with { Units = UnitSystem.SI };
        var (xml, warnings) = LandXmlPipeNetworkWriter.Write(n);
        Assert.Contains("linearUnit=\"meter\"", xml);
        Assert.Contains("diameterUnit=\"meter\"", xml);
        Assert.Contains(warnings, w => w.Contains("no northing/easting"));
        Assert.Equal(UnitSystem.SI, LandXmlPipeNetworks.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml))).Units);
    }

    [Fact]
    public void Reader_HonoursDiameterUnitInches()
    {
        const string x = """
            <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
              <Units><Imperial linearUnit="foot" diameterUnit="inch"/></Units>
              <PipeNetworks><PipeNetwork name="N"><Structs>
                <Struct name="A" elevRim="110"><InletStruct/><Invert elev="100" flowDir="out" refPipe="P"/></Struct>
                <Struct name="B" elevRim="109"><OutletStruct/><Invert elev="99" flowDir="in" refPipe="P"/></Struct>
              </Structs><Pipes><Pipe name="P" refStart="A" refEnd="B" length="100"><CircPipe diameter="18"/></Pipe></Pipes></PipeNetwork></PipeNetworks>
            </LandXML>
            """;
        var r = LandXmlPipeNetworks.Read(new MemoryStream(Encoding.UTF8.GetBytes(x)));
        Assert.Equal(1.5, r.Pipes.Single().Rise, 12);
        Assert.DoesNotContain(r.Problems, p => p.Contains("looks like inches"));
    }
}
