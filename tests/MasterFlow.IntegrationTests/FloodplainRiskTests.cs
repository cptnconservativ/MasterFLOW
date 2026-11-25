using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-17 risk, expected annual damage, present worth and LTEC by hand.</summary>
public class FloodplainRiskTests
{
    private static readonly FloodplainRiskTool Tool = new();

    [Fact]
    public void Risk_ClassicValues()
    {
        Assert.Equal(0.395, FloodplainRiskTool.Risk(100, 50), 3);
        Assert.Equal(0.634, FloodplainRiskTool.Risk(100, 100), 3);
        Assert.Equal(0.5, FloodplainRiskTool.Risk(2, 1), 9);
    }

    [Fact]
    public void PresentWorth_ByHand()
    {
        Assert.Equal((1 - Math.Pow(1.03, -50)) / 0.03, FloodplainRiskTool.PresentWorth(0.03, 50), 9);
        Assert.Equal(25.730, FloodplainRiskTool.PresentWorth(0.03, 50), 3);
        Assert.Equal(20, FloodplainRiskTool.PresentWorth(0, 20), 9);
    }

    [Fact]
    public void Ead_TrapezoidsPlusTail()
    {
        // 3-cell sample: 0.5·(0 + 120k)(0.06) + 0.5·(120k + 450k)(0.02) + 0.5·(450k + 1.1M)(0.01) + 0.5·(1.1M + 2.6M)(0.008) + 2.6M·0.002.
        double ead = FloodplainRiskTool.ExpectedAnnualDamage([(10, 0), (25, 120000), (50, 450000), (100, 1100000), (500, 2600000)]);
        Assert.Equal(3600 + 5700 + 7750 + 14800 + 5200, ead, 6);
    }

    [Fact]
    public void Sample_PicksTheLeastTotalExpectedCost()
    {
        var r = Tool.Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)), new DesignToolContext(UnitSystem.USCustomary));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var t = r.Tables.Single(x => x.Title.StartsWith("Least total"));
        var best = t.Rows.Single(x => (string)x[7]! != "");
        Assert.Equal("4-cell 10x6 box", best[0]);
        double pwf = FloodplainRiskTool.PresentWorth(0.03, 50);
        Assert.Equal(Math.Round(1850000 + (13770 + 3500) * pwf, 0), (double)best[6]!, 0);
    }

    [Fact]
    public void DamageAtTheMostFrequentEvent_IsWarned()
    {
        var r = Tool.Run("[Alternatives]\nName\tCapital\tO&M\nA\t100\t0\n[Damages]\nT\tA\n10\t50\n100\t500", new DesignToolContext(UnitSystem.USCustomary));
        Assert.Contains(r.Messages, m => m.Code == "W-FR-001");
    }
}
