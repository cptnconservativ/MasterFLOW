using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Snowmelt: degree-day and EM 1110-2-1406 rain-on-snow hand calculations and the pack water balance.</summary>
public class SnowmeltTests
{
    private static readonly SnowmeltTool Tool = new();

    private static ToolResult Run(string input)
    {
        var r = Tool.Run(input, new DesignToolContext(UnitSystem.USCustomary));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact]
    public void Sample_RunsAndBalances()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Assert.DoesNotContain(r.Messages, m => m.Code == "W-SM-002");
        Assert.Contains(r.Summary, s => s.Contains("day 6"));
    }

    [Fact]
    public void RainOnSnowEquations()
    {
        // Open, k = 1, v = 10 mph, Pr = 1.2 in, Ta = 42 °F: (0.029 + 0.084 + 0.0084)·10 + 0.09 = 1.304 in/day.
        Assert.Equal(1.304, SnowmeltTool.RainOnSnow(42, 1.2, 10, 1, false), 9);
        // Forested, Pr = 2 in, Ta = 40 °F: (0.074 + 0.014)·8 + 0.05 = 0.754 in/day.
        Assert.Equal(0.754, SnowmeltTool.RainOnSnow(40, 2, 10, 1, true), 9);
        Assert.Equal(0, SnowmeltTool.RainOnSnow(31, 1, 10, 1, false));
        Assert.Equal(0.48, SnowmeltTool.DegreeDay(0.06, 40, 32), 9);
        Assert.Equal(0, SnowmeltTool.DegreeDay(0.06, 30, 32));
    }

    [Fact]
    public void NoLiquidHolding_MeltDrainsSameDay_UntilPackGone()
    {
        // 1 in SWE, 0.05 in/°F/day, 42 °F → 0.5 in/day: two days of 0.5 in, then nothing.
        var d = SnowmeltTool.Simulate([("1", 42, 0), ("2", 42, 0), ("3", 42, 0)], 1, 0.05, 32, 34, false, 10, 1, 0);
        Assert.Equal([0.5, 0.5, 0.0], d.Select(x => Math.Round(x.Runoff, 9)));
        Assert.Equal(0, d[2].Swe, 9);
    }

    [Fact]
    public void SnowAccumulatesBelowThreshold_AndRainOnBareGroundRunsOff()
    {
        var d = SnowmeltTool.Simulate([("1", 30, 0.5), ("2", 50, 1.0)], 0, 0.06, 32, 34, false, 0, 1, 0);
        Assert.Equal(0.5, d[0].Swe, 9);
        Assert.Equal(0, d[0].Runoff, 9);
        // Day 2: rain-on-snow (0.029 + 0 + 0.007)·18 + 0.09 = 0.738 > 0.5 in pack → all 0.5 melts; 1.0 rain + 0.5 melt run off.
        Assert.Equal(0.5, d[1].Melt, 9);
        Assert.Equal(1.5, d[1].Runoff, 9);
    }

    [Fact]
    public void LiquidHeldInPackUpToCapacity()
    {
        // 10 in SWE, 0.5 in melt (0.05·10), capacity 10 % of ice 9.5 = 0.95 in: all melt is held, nothing drains.
        var d = SnowmeltTool.Simulate([("1", 42, 0)], 10, 0.05, 32, 34, false, 0, 1, 0.10);
        Assert.Equal(0.5, d[0].Liquid, 9);
        Assert.Equal(0, d[0].Runoff, 9);
        Assert.Equal(10, d[0].Swe, 9);
    }

    [Fact]
    public void ForestedEquationAboveSixtyPercent()
    {
        var r = Run("initial swe = 5\nforest cover = 80\n[Days]\nDay, T, P\n1, 40, 2\n");
        Assert.Equal(0.754, (double)r.Tables[0].Rows[0][5]!, 3);
        Assert.Equal("rain-on-snow, forested", r.Tables[0].Rows[0][9]);
    }

    [Fact]
    public void Errors()
    {
        var r = Tool.Run("melt factor = 0\n", new DesignToolContext(UnitSystem.USCustomary));
        Assert.Contains(r.Messages, m => m.Code == "E-SM-001");
        Assert.Contains(r.Messages, m => m.Code == "E-SM-004");
    }
}
