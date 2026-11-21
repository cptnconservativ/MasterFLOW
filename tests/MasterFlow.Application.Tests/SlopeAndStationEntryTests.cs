using System.Globalization;
using MasterFlow.Application;
using MasterFlow.Domain;

namespace MasterFlow.Application.Tests;

public class SlopeAndStationEntryTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData("0+10", 10)]
    [InlineData("0+10.00", 10)]
    [InlineData("1+25.50", 125.5)]
    [InlineData("10+00.50", 1000.5)]
    [InlineData("1+5", 105)]
    [InlineData("STA 1+25", 125)]
    [InlineData("Sta. 0+40", 40)]
    [InlineData("sta2+00", 200)]
    [InlineData("1+250.000", 1250)]   // metric chainage km+m
    [InlineData("0+050", 50)]
    [InlineData("-0+20", -20)]
    [InlineData("120", 120)]
    public void Stations_AcceptSurveyorNotation(string text, double expected)
    {
        Assert.True(StationPointText.TryStation(text, Inv, out double v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    [InlineData("1+1250")] // four digits after + is neither feet nor metric stationing
    [InlineData("Station")]
    public void BadStations_AreRejected(string text) => Assert.False(StationPointText.TryStation(text, Inv, out _));

    [Fact]
    public void FlowCalcStyleEntry_ParsesInPointText()
    {
        var r = StationPointText.Parse("0+00\t5\n0+10\t0\n0+30\t0\n0+40\t5", Inv);
        Assert.Empty(r.Errors);
        Assert.Equal([0.0, 10, 30, 40], r.Points.Select(p => p.Station));
    }

    [Fact]
    public void PercentFlat_WarnsItMayBeFtPerFt()
    {
        // Flow Calc comparison: "0.02" typed is 0.02 % in MasterFlow and 2 % in Flow Calc.
        var m = SlopeEntry.Check(0.02, SlopeUnit.Percent, "ft")!;
        Assert.Equal(SlopeEntry.Code, m.Code);
        Assert.Contains("0.02 % is 0.0002 ft/ft", m.Text);
        Assert.Contains("If you meant 0.02 ft/ft (2 %)", m.Text);
    }

    [Fact]
    public void RatioSteep_WarnsItMayBePercent()
    {
        var m = SlopeEntry.Check(0.5, SlopeUnit.RiseOverRun, "m")!;
        Assert.Contains("0.5 m/m is 50 %", m.Text);
        Assert.Contains("If you meant 0.5 %", m.Text);
    }

    [Theory]
    [InlineData(0.16, SlopeUnit.Percent)]
    [InlineData(2, SlopeUnit.Percent)]
    [InlineData(0.0016, SlopeUnit.RiseOverRun)]
    [InlineData(0.05, SlopeUnit.RiseOverRun)]
    [InlineData(0, SlopeUnit.Percent)] // zero is the engine's error, not this warning
    public void OrdinarySlopes_AreQuiet(double typed, SlopeUnit unit) => Assert.Null(SlopeEntry.Check(typed, unit, "ft"));
}
