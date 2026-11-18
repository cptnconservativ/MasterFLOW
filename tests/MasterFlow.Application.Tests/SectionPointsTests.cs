using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tests;

public class SectionPointsTests
{
    private static readonly IReadOnlyList<StationPoint> Creek =
    [
        new(100, 8, 0.06, "LOB"), new(140, 5, 0.06), new(150, 5, 0.035), new(155, 0, 0.035), new(165, 0, 0.035), new(170, 5, 0.05), new(180, 5, 0.05), new(220, 8, null, "ROB"),
    ];

    private static UniformFlowResult Solve(ChannelSection s) =>
        UniformFlowCalculator.Solve(new UniformFlowInput { Section = s, SolveFor = SolveFor.NormalDepth, ManningN = 0.03, Slope = 0.002, Discharge = 150 });

    public static TheoryData<string> Shapes() => new() { "rect", "trap", "asym-trap", "tri" };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void FromRegular_GivesTheSameHydraulics_BelowTheTop(string which)
    {
        ChannelSection s = which switch
        {
            "rect" => new RectangularSection(8),
            "trap" => new TrapezoidalSection(6, 2, 2),
            "asym-trap" => new TrapezoidalSection(4, 1.5, 3),
            _ => new TriangularSection(4, 3),
        };
        var a = Solve(s);
        var b = Solve(new IrregularSection(SectionPoints.FromRegular(s, 10)));
        Assert.True(a.Succeeded && b.Succeeded);
        Assert.Equal(a.Normal.Depth, b.Normal.Depth, 1e-9);
        Assert.Equal(a.Critical.Depth, b.Critical.Depth, 1e-8);
        Assert.Equal(a.Normal.Area, b.Normal.Area, 1e-9);
        Assert.Equal(a.Normal.WettedPerimeter, b.Normal.WettedPerimeter, 1e-9);
    }

    [Fact]
    public void FromRegular_RefusesConduits_AndBadHeight()
    {
        Assert.Throws<NotSupportedException>(() => SectionPoints.FromRegular(new CircularSection(2), 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionPoints.FromRegular(new RectangularSection(2), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => SectionPoints.FromRegular(new RectangularSection(2), double.NaN));
    }

    [Fact]
    public void ShiftAndZero_MoveEveryPoint_AndKeepN()
    {
        var z = SectionPoints.ZeroStations(Creek);
        Assert.Equal(0, z[0].Station);
        Assert.Equal(120, z[^1].Station);
        Assert.Equal(Creek.Select(p => p.ManningN), z.Select(p => p.ManningN));
        var e = SectionPoints.ShiftElevations(Creek, 100);
        Assert.Equal(105, e[1].Elevation);
        Assert.Equal(0, SectionPoints.ZeroElevations(e).Min(p => p.Elevation));
    }

    [Fact]
    public void ShiftingElevations_DoesNotChangeResults()
    {
        double n = 0.04;
        UniformFlowResult R(IReadOnlyList<StationPoint> p) => UniformFlowCalculator.Solve(new UniformFlowInput { Section = new IrregularSection(p), SolveFor = SolveFor.NormalDepth, ManningN = n, Slope = 0.001, Discharge = 500 });
        var a = R(Creek); var b = R(SectionPoints.ShiftStations(SectionPoints.ShiftElevations(Creek, 1234.5), -77));
        Assert.Equal(a.Normal.Depth, b.Normal.Depth, 1e-9);
        Assert.Equal(a.Irregular!.WaterSurfaceElevation + 1234.5, b.Irregular!.WaterSurfaceElevation, 1e-9);
    }

    [Fact]
    public void Reverse_MirrorsGeometry_KeepsEachSegmentsN_AndResults()
    {
        var r = SectionPoints.Reverse(Creek);
        Assert.Equal(100, r[0].Station);
        Assert.Equal(220, r[^1].Station);
        Assert.Equal("ROB", r[0].Description);
        // Old last segment (180→220, n 0.05) is now the first segment (100→140).
        Assert.Equal(0.05, r[0].ManningN);
        Assert.Equal(0.06, r[^2].ManningN);
        Assert.Null(r[^1].ManningN);
        UniformFlowResult R(IReadOnlyList<StationPoint> p) => UniformFlowCalculator.Solve(new UniformFlowInput { Section = new IrregularSection(p), SolveFor = SolveFor.NormalDepth, ManningN = 0.04, Slope = 0.001, Discharge = 500 });
        Assert.Equal(R(Creek).Normal.Depth, R(r).Normal.Depth, 1e-9);
        Assert.Equal(Creek, SectionPoints.Reverse(r));
    }

    [Fact]
    public void Sort_OrdersByStation_AndReassignsNByGround()
    {
        var shuffled = new List<StationPoint> { new(0, 5, 0.03), new(20, 0, 0.05), new(10, 0, 0.04), new(30, 5) };
        var s = SectionPoints.SortByStation(shuffled);
        Assert.Equal([0.0, 10, 20, 30], s.Select(p => p.Station));
        Assert.Null(s[^1].ManningN);
        var sorted = SectionPoints.SortByStation(Creek);
        Assert.Same(Creek, sorted); // already sorted: unchanged
    }

    [Theory]
    [InlineData("0|5\n10|0\n20|5")]
    [InlineData("0:5\n10:0\n20:5")]
    public void Parse_AcceptsBarAndColonDelimiters(string text)
    {
        var r = StationPointText.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Empty(r.Errors);
        Assert.Equal(3, r.Points.Count);
        Assert.Equal(10, r.Points[1].Station);
    }

    [Fact]
    public void Csv_RoundTripsThroughParse()
    {
        var csv = StationPointText.ToCsv(Creek.Append(new StationPoint(230, 9, null, "Fence, \"old\"")));
        Assert.StartsWith("Station,Elevation,n,Description", csv);
        var back = StationPointText.Parse(csv, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Empty(back.Errors);
        Assert.Equal(Creek.Count + 1, back.Points.Count);
        Assert.Equal(Creek.Select(p => (p.Station, p.Elevation, p.ManningN)), back.Points.Take(Creek.Count).Select(p => (p.Station, p.Elevation, p.ManningN)));
    }
}
