using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>TR-60 design rainfall, spillway rating and breach regression equations by hand.</summary>
public class SpillwayBreachTests
{
    private static readonly SpillwayBreachTool Tool = new();

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => Tool.Run(input, new DesignToolContext(u));

    private static object? Item(ToolResult r, string table, string start) =>
        r.Tables.Single(t => t.Title.StartsWith(table)).Rows.Single(x => ((string)x[0]!).StartsWith(start))[1];

    [Fact]
    public void Sample_RunsClean()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.False(Run("x = 1", UnitSystem.SI).Succeeded);
    }

    [Theory]
    [InlineData("low", 5.0, 5.0 + 0.12 * 20)]
    [InlineData("significant", 5.0 + 0.12 * 20, 5.0 + 0.40 * 20)]
    [InlineData("high", 5.0 + 0.26 * 20, 25.0)]
    public void Tr60_ByHazard(string hazard, double ash, double fbh)
    {
        var (a, f) = SpillwayBreachTool.Tr60Rainfall(hazard, 5, 25);
        Assert.Equal(ash, a, 9);
        Assert.Equal(fbh, f, 9);
    }

    [Fact]
    public void TrapezoidCritical_RectangleMatchesClassic()
    {
        // Rectangle: q = √g·(2H/3)^1.5 per ft.
        Assert.Equal(40 * Math.Sqrt(32.2) * Math.Pow(2.0 / 3 * 2, 1.5), SpillwayBreachTool.TrapezoidCritical(40, 0, 2), 3);
    }

    [Fact]
    public void Breach_ByHand()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        double vw = 120 * 1233.48, hw = 22 * 0.3048, hb = 24 * 0.3048;
        Assert.Equal(Math.Round(0.607 * Math.Pow(vw, 0.295) * Math.Pow(hw, 1.24) * 35.3147, 0), (double)Item(r, "Dam breach", "Froehlich (1995)")!, 0);
        Assert.Equal(Math.Round(19.1 * Math.Pow(hw, 1.85) * 35.3147, 0), (double)Item(r, "Dam breach", "USBR")!, 0);
        Assert.Equal(Math.Round(0.27 * 1.3 * Math.Pow(vw, 0.32) * Math.Pow(hb, 0.04) / 0.3048, 1), (double)Item(r, "Dam breach", "Average breach width")!, 1);
        Assert.Equal(Math.Round(63.2 * Math.Sqrt(vw / (9.81 * hb * hb)) / 3600, 2), (double)Item(r, "Dam breach", "Failure time")!, 2);
    }

    [Fact]
    public void FbhOverTheDam_IsWarned()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)).Replace("FBH stage = 3415.2", "FBH stage = 3416.4"));
        Assert.Contains(r.Messages, m => m.Code == "W-SB-001");
    }
}
