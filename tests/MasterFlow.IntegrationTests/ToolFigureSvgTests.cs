using System.Text.RegularExpressions;
using MasterFlow.Application.Tools;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>Figure axes follow the data: elevation sections are not flattened by a forced zero baseline.</summary>
public class ToolFigureSvgTests
{
    private static List<double> YTicks(string svg) =>
        Regex.Matches(svg, "text-anchor=\"end\" fill=\"#5b6876\">([-0-9.]+)</text>")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();

    [Fact]
    public void ElevationSection_AxisStartsNearTheData_NotAtZero()
    {
        var ground = new List<(double, double)> { (0, 3410), (20, 3402), (40, 3400), (60, 3403), (80, 3411) };
        var water = new List<(double, double)> { (15, 3405), (65, 3405), (60, 3403), (40, 3400), (20, 3402) };
        var f = new ToolFigure("Section", [new("Water", ToolSeriesKind.Water, water), new("Ground", ToolSeriesKind.Ground, ground)], "Station (ft)", "Elevation (ft)");
        var ticks = YTicks(ToolFigureSvg.Render(f));
        Assert.NotEmpty(ticks);
        Assert.True(ticks.Min() >= 3390, $"lowest tick {ticks.Min()}");
        Assert.True(ticks.Max() <= 3420, $"highest tick {ticks.Max()}");
    }

    [Fact]
    public void DepthFigure_StillStartsAtZero()
    {
        var ground = new List<(double, double)> { (0, 0.5), (10, 0), (20, 0.4) };
        var f = new ToolFigure("Street", [new("Street", ToolSeriesKind.Ground, ground)], "Station (ft)", "Depth (ft)");
        var ticks = YTicks(ToolFigureSvg.Render(f));
        Assert.Equal(0, ticks.Min());
    }
}
