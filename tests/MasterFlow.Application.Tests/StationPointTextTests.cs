using System.Globalization;
using MasterFlow.Application;
using MasterFlow.Domain;

namespace MasterFlow.Application.Tests;

public class StationPointTextTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData("0\t5\n10\t0\n20\t0.5")]          // Excel paste
    [InlineData("0,5\n10,0\n20,0.5")]              // CSV
    [InlineData("0; 5\n10; 0\n20; 0.5")]           // semicolon
    [InlineData("0   5\n10 0\n 20  0.5 ")]         // spaces
    [InlineData("Sta\tElev\n0\t5\n10\t0\n20\t0.5")] // header skipped
    [InlineData("0+00\t5\n0+10\t0\n0+20\t0.5")]    // surveyor stationing
    [InlineData("# comment\n\n0\t5\r\n10\t0\r\n20\t0.5\r\n")]
    public void Parses_CommonLayouts(string text)
    {
        var r = StationPointText.Parse(text, Inv);
        Assert.Empty(r.Errors);
        Assert.Equal([new StationPoint(0, 5), new StationPoint(10, 0), new StationPoint(20, 0.5)], r.Points);
    }

    [Fact]
    public void Parses_RoughnessAndDescription()
    {
        var r = StationPointText.Parse("0\t5\t0.06\tLeft bank top\n1+25.50\t0\t\tThalweg\n130\t4", Inv);
        Assert.Empty(r.Errors);
        Assert.Equal(new StationPoint(0, 5, 0.06, "Left bank top"), r.Points[0]);
        Assert.Equal(new StationPoint(125.5, 0, null, "Thalweg"), r.Points[1]);
        Assert.Null(r.Points[2].ManningN);
    }

    [Fact]
    public void ReportsBadLines_WithLineNumbers_AndKeepsNothingSilent()
    {
        var r = StationPointText.Parse("0\t5\n10\tx\n20\n30\t1\tn?", Inv);
        Assert.Single(r.Points); // only line 1 is valid
        Assert.Contains(r.Errors, e => e.StartsWith("Line 2") && e.Contains("elevation"));
        Assert.Contains(r.Errors, e => e.StartsWith("Line 3"));
        Assert.Contains(r.Errors, e => e.StartsWith("Line 4") && e.Contains("n "));
        Assert.Single(StationPointText.Parse("", Inv).Errors);
    }

    [Fact]
    public void Format_RoundTrips()
    {
        StationPoint[] pts = [new(0, 5, 0.06, "LB"), new(10.25, 0), new(20, 0.5, 0.035), new(30, 5)];
        var back = StationPointText.Parse(StationPointText.Format(pts, Inv), Inv);
        Assert.Empty(back.Errors);
        Assert.Equal(pts, back.Points);
    }
}
