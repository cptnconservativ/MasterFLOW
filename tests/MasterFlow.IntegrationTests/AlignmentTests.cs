using System.Text;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Persistence;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// H6 alignments: geometry checked against closed-form values (circle geometry and the clothoid series),
/// tolerances fixed before running: 0.01 ft for station and offset on arcs (1° chords at R = 100 give a
/// sagitta of 0.004 ft), 0.002 ft for the spiral end point.
/// </summary>
public class AlignmentTests
{
    // Line east 200 ft from (N 1000, E 1000), then a 90° left (ccw) arc, R = 100, centre (N 1100, E 1200).
    private const string LineArcXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
          <Units><Imperial linearUnit="USSurveyFoot" areaUnit="squareFoot" volumeUnit="cubicFeet"/></Units>
          <Alignments>
            <Alignment name="Main St" length="357.0796" staStart="1000">
              <CoordGeom>
                <Line><Start>1000 1000</Start><End>1000 1200</End></Line>
                <Curve rot="ccw" radius="100" length="157.0796"><Start>1000 1200</Start><Center>1100 1200</Center><End>1100 1300</End></Curve>
              </CoordGeom>
            </Alignment>
          </Alignments>
        </LandXML>
        """;

    private static LandXmlAlignmentReadResult Read(string xml) => LandXmlAlignments.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void LineAndArc_ProjectToKnownStationAndOffset()
    {
        var r = Read(LineArcXml);
        Assert.Empty(r.Problems);
        Assert.Equal(UnitSystem.USCustomary, r.Units);
        var a = Assert.Single(r.Alignments);
        Assert.Equal(1000 + 200 + 157.0796, a.EndStation, 6);   // the stated arc length is used for stationing

        // On the tangent, 10 ft south of a line heading east = right of stationing.
        var p = a.Project(990, 1100);
        Assert.Equal(1100, p.Station, 6);
        Assert.Equal(10, p.Offset, 6);
        Assert.False(p.BeyondEnds);

        // On the arc at 45°, 10 ft inside the curve (left of stationing): radius 90 from the centre.
        double ang = -Math.PI / 4;
        p = a.Project(1100 + 90 * Math.Sin(ang), 1200 + 90 * Math.Cos(ang));
        Assert.InRange(p.Station, 1200 + 25 * Math.PI - 0.01, 1200 + 25 * Math.PI + 0.01);
        Assert.InRange(p.Offset, -10.01, -9.99);

        // Before the start: flagged as beyond the ends.
        Assert.True(a.Project(1000, 900).BeyondEnds);
    }

    [Fact]
    public void Spiral_EndMatchesClothoidSeries()
    {
        // Tangent-to-curve clothoid, L = 100, R = 500, turning left from a heading of due east.
        // x = L − L³/(40R²) + L⁵/(3456R⁴), y = L²/(6R) − L⁴/(336R³)
        double L = 100, R = 500;
        double x = L - Math.Pow(L, 3) / (40 * R * R) + Math.Pow(L, 5) / (3456 * Math.Pow(R, 4));
        double y = L * L / (6 * R) - Math.Pow(L, 4) / (336 * Math.Pow(R, 3));
        var el = new AlignmentElement(AlignmentElementKind.Spiral, L, 0, 0, y, x, StartBearing: 0, CurvatureStart: 0, CurvatureEnd: 1 / R);
        var pts = Alignment.Densify(el);
        Assert.InRange(pts[^1].E, x - 0.002, x + 0.002);
        Assert.InRange(pts[^1].N, y - 0.002, y + 0.002);
        Assert.Empty(new Alignment("S", 0, [el]).Problems);
    }

    [Fact]
    public void Spiral_ReadFromLandXml_WithPiAndRadii()
    {
        double L = 100, R = 500;
        double x = L - Math.Pow(L, 3) / (40 * R * R), y = L * L / (6 * R) - Math.Pow(L, 4) / (336 * Math.Pow(R, 3));
        string xml = $"""
            <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
              <Units><Imperial linearUnit="foot"/></Units>
              <Alignments><Alignment name="Ramp" staStart="0"><CoordGeom>
                <Line><Start>0 -50</Start><End>0 0</End></Line>
                <Spiral length="100" radiusStart="INF" radiusEnd="500" rot="ccw" spiType="clothoid"><Start>0 0</Start><PI>0 66.67</PI><End>{y:F5} {x:F5}</End></Spiral>
              </CoordGeom></Alignment></Alignments>
            </LandXML>
            """;
        var r = Read(xml);
        Assert.Empty(r.Problems);
        var a = Assert.Single(r.Alignments);
        Assert.Equal(150, a.EndStation, 6);
        Assert.Equal(25, a.Project(0, -25).Station, 6);
        Assert.Equal(-5, a.Project(5, -25).Offset, 6);
    }

    [Fact]
    public void Reader_ReportsGapsAndUnsupportedElements()
    {
        string xml = LineArcXml.Replace("<Start>1000 1200</Start><Center>", "<Start>1000 1201</Start><Center>");
        var r = Read(xml);
        Assert.Contains(r.Problems, p => p.Contains("starts 1.000 from the end"));
        r = Read(LineArcXml.Replace("<Line>", "<Chain>").Replace("</Line>", "</Chain>"));
        Assert.Empty(r.Alignments);
        Assert.Contains(r.Problems, p => p.Contains("not supported"));
    }

    [Theory]
    [InlineData(1234.5, UnitSystem.USCustomary, "12+34.50")]
    [InlineData(99.996, UnitSystem.USCustomary, "1+00.00")]
    [InlineData(-50, UnitSystem.USCustomary, "-0+50.00")]
    [InlineData(1234.5678, UnitSystem.SI, "1+234.568")]
    public void FormatStation_CivilText(double sta, UnitSystem u, string expected) =>
        Assert.Equal(expected, Alignment.FormatStation(sta, u));
}
