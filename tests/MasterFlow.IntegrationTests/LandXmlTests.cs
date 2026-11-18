using System.Text;
using MasterFlow.Domain;
using MasterFlow.Persistence;

namespace MasterFlow.IntegrationTests;

public class LandXmlTests
{
    private static LandXmlReadResult Read(string xml) => LandXmlCrossSections.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private const string Civil3DStyle = """
        <?xml version="1.0" encoding="UTF-8"?>
        <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
          <Units><Imperial areaUnit="squareFoot" linearUnit="USSurveyFoot" volumeUnit="cubicYard" temperatureUnit="fahrenheit" pressureUnit="inHG"/></Units>
          <Alignments name="Site 1">
            <Alignment name="Ditch A" length="300" staStart="0">
              <CrossSects>
                <CrossSect name="SL-1" sta="100">
                  <CrossSectSurf name="EG">
                    <PntList2D>10 105.0 -10 105.0 -2 100.0 2 100.0</PntList2D>
                  </CrossSectSurf>
                  <CrossSectSurf name="FG">
                    <PntList2D>-6 103 0 100 6 103</PntList2D>
                  </CrossSectSurf>
                </CrossSect>
                <CrossSect name="SL-2" sta="1234.5">
                  <CrossSectSurf name="EG"><PntList2D>-5 10 0 9 5 10</PntList2D></CrossSectSurf>
                </CrossSect>
              </CrossSects>
            </Alignment>
          </Alignments>
        </LandXML>
        """;

    [Fact]
    public void Civil3DStyle_File_ReadsEverySurface_InFileUnits_SortedByOffset()
    {
        var r = Read(Civil3DStyle);
        Assert.Equal(UnitSystem.USCustomary, r.Units);
        Assert.Equal("USSurveyFoot", r.LinearUnit);
        Assert.Empty(r.Problems);
        Assert.Equal(3, r.Sections.Count);
        var eg = r.Sections[0];
        Assert.Equal(("Ditch A", 100.0, "EG"), (eg.Alignment, eg.Station, eg.Surface));
        // Offsets are put in order, values untouched (no unit conversion).
        Assert.Equal([-10.0, -2, 2, 10], eg.Points.Select(p => p.Station));
        Assert.Equal([105.0, 100, 100, 105], eg.Points.Select(p => p.Elevation));
        Assert.Equal("FG", r.Sections[1].Surface);
        Assert.Equal(1234.5, r.Sections[2].Station);
    }

    [Fact]
    public void Metric_File_IsSI()
    {
        var r = Read("""<LandXML><Units><Metric linearUnit="meter"/></Units><Alignment name="X"><CrossSects><CrossSect sta="0"><CrossSectSurf name="EG"><PntList2D>0 1 1 0 2 1</PntList2D></CrossSectSurf></CrossSect></CrossSects></Alignment></LandXML>""");
        Assert.Equal(UnitSystem.SI, r.Units);
        Assert.Single(r.Sections);
    }

    [Fact]
    public void MissingOrUnknownUnits_AreReported_NotGuessed()
    {
        var none = Read("""<LandXML><CrossSect sta="0"><CrossSectSurf name="EG"><PntList2D>0 1 1 0 2 1</PntList2D></CrossSectSurf></CrossSect></LandXML>""");
        Assert.Null(none.Units);
        Assert.Contains(none.Problems, p => p.Contains("does not state"));
        Assert.Single(none.Sections);

        var mm = Read("""<LandXML><Units><Metric linearUnit="millimeter"/></Units><CrossSect sta="0"><CrossSectSurf><PntList2D>0 1 1 0</PntList2D></CrossSectSurf></CrossSect></LandXML>""");
        Assert.Null(mm.Units);
        Assert.Contains(mm.Problems, p => p.Contains("millimeter"));
    }

    [Fact]
    public void BadValues_AreSkipped_WithAProblemEach()
    {
        var r = Read("""
            <LandXML><Units><Imperial linearUnit="foot"/></Units><Alignment name="A"><CrossSects>
              <CrossSect name="bad-sta" sta="abc"><CrossSectSurf name="EG"><PntList2D>0 1 1 0</PntList2D></CrossSectSurf></CrossSect>
              <CrossSect sta="1"><CrossSectSurf name="odd"><PntList2D>0 1 1</PntList2D></CrossSectSurf></CrossSect>
              <CrossSect sta="2"><CrossSectSurf name="text"><PntList2D>0 1 x 0</PntList2D></CrossSectSurf></CrossSect>
              <CrossSect sta="3"><CrossSectSurf name="one"><PntList2D>0 1</PntList2D></CrossSectSurf></CrossSect>
              <CrossSect sta="4"><CrossSectSurf name="none"/></CrossSect>
              <CrossSect sta="5"><CrossSectSurf name="good"><PntList2D>0 1 1 0 2 1</PntList2D></CrossSectSurf></CrossSect>
            </CrossSects></Alignment></LandXML>
            """);
        Assert.Equal("good", Assert.Single(r.Sections).Surface);
        Assert.Equal(5, r.Problems.Count);
    }

    [Fact]
    public void NotLandXml_OrNotXml_IsRefused()
    {
        Assert.Contains("Not a LandXML", Assert.Single(Read("<Project/>").Problems));
        Assert.Contains("Not a readable XML", Assert.Single(Read("Station,Elevation\n0,1").Problems));
        Assert.Contains(Read("<LandXML><Units><Imperial linearUnit=\"foot\"/></Units></LandXML>").Problems, p => p.Contains("no cross-sections"));
    }

    [Fact]
    public void Dtd_IsRefused_SoExternalEntitiesAreNeverResolved()
    {
        var path = Path.Combine(Path.GetTempPath(), "mf-xxe-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "SECRET");
        try
        {
            var r = Read($"""<?xml version="1.0"?><!DOCTYPE LandXML [<!ENTITY x SYSTEM "file://{path}">]><LandXML><Alignment name="&x;"><CrossSect sta="0"><CrossSectSurf><PntList2D>0 1 1 0</PntList2D></CrossSectSurf></CrossSect></Alignment></LandXML>""");
            Assert.Empty(r.Sections);
            Assert.Contains("Not a readable XML", Assert.Single(r.Problems));
            Assert.DoesNotContain(r.Problems, p => p.Contains("SECRET"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0, "0+00.00")]
    [InlineData(1234.5, "12+34.50")]
    [InlineData(99.999, "1+00.00")]
    [InlineData(-50, "-0+50.00")]
    public void StationText_IsSurveyorFormat(double sta, string expected) => Assert.Equal(expected, LandXmlCrossSections.StationText(sta));
}
