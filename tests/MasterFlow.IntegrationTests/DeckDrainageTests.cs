using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-21 deck drain spacing by hand.</summary>
public class DeckDrainageTests
{
    private static readonly DeckDrainageTool Tool = new();

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => Tool.Run(input, new DesignToolContext(u));

    private static double Item(ToolResult r, string start) => (double)r.Tables[0].Rows.Single(x => ((string)x[0]!).StartsWith(start))[1]!;

    [Fact]
    public void Sample_RunsClean()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
    }

    [Fact]
    public void SiIsRefused() => Assert.False(Run("bridge length = 100", UnitSystem.SI).Succeeded);

    [Fact]
    public void SpacingByHand()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        // Qallow = 0.56/0.016·0.02^1.67·0.01^0.5·8^2.67.
        double qa = 0.56 / 0.016 * Math.Pow(0.02, 1.67) * 0.1 * Math.Pow(8, 2.67);
        Assert.Equal(qa, Item(r, "Gutter capacity"), 3);
        Assert.Equal(43560 * qa / (0.9 * 6 * 26), Item(r, "Length to the first"), 1);
        // Grate 2 ft × 1.5 ft, 50 % clogged → L = 1 ft; V = 1.12/n·S^0.5·Sx^0.67·T^0.67.
        double v = 1.12 / 0.016 * 0.1 * Math.Pow(0.02, 0.67) * Math.Pow(8, 0.67);
        double eo = 1 - Math.Pow(1 - 1.5 / 8, 2.67);
        double rf = Math.Min(1, 1 - 0.09 * (v - 6));
        double rs = 1 / (1 + 0.15 * Math.Pow(v, 1.8) / (0.02 * 1));
        double e = rf * eo + rs * (1 - eo);
        Assert.Equal(e, Item(r, "Drain efficiency"), 3);
        Assert.Equal(43560 * e * qa / (0.9 * 6 * 26), Item(r, "Spacing of later"), 1);
        var drains = r.Tables[1].Rows.Where(x => ((string)x[0]!).StartsWith("Drain")).ToList();
        Assert.Equal(2, drains.Count);
    }

    [Fact]
    public void ShortBridge_NeedsNoDrains()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)).Replace("bridge length = 600", "bridge length = 300"));
        Assert.True(r.Succeeded);
        Assert.StartsWith("No deck drains", r.Summary[0]);
        Assert.DoesNotContain(r.Tables[1].Rows, x => ((string)x[0]!).StartsWith("Drain"));
    }

    [Fact]
    public void ApproachFlow_ShortensTheFirstReach()
    {
        var a = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        var b = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)).Replace("approach Q = 0", "approach Q = 0.5"));
        Assert.Equal(Item(a, "Length to the first") - 43560 * 0.5 / (0.9 * 6 * 26), Item(b, "Length to the first"), 0);
    }
}
