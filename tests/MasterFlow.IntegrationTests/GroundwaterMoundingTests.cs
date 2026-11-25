using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Hantush mounding: erf, S*, limiting cases and recovery.</summary>
public class GroundwaterMoundingTests
{
    private static readonly GroundwaterMoundingTool Tool = new();

    [Fact]
    public void Sample_RunsCleanInBothUnitSystems()
    {
        foreach (var u in new[] { UnitSystem.USCustomary, UnitSystem.SI })
        {
            var r = Tool.Run(Tool.SampleInput(new DesignToolContext(u)), new DesignToolContext(u));
            Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        }
    }

    [Theory]
    [InlineData(0.5, 0.5204999)]
    [InlineData(1.0, 0.8427008)]
    [InlineData(2.0, 0.9953223)]
    public void Erf_KnownValues(double x, double e) => Assert.Equal(e, GroundwaterMoundingTool.Erf(x), 6);

    [Fact]
    public void SStar_Limits()
    {
        Assert.Equal(1, GroundwaterMoundingTool.SStar(50, 50), 4);
        Assert.Equal(0, GroundwaterMoundingTool.SStar(0, 3), 9);
        Assert.Equal(GroundwaterMoundingTool.SStar(0.7, 1.3), GroundwaterMoundingTool.SStar(1.3, 0.7), 9);
    }

    [Fact]
    public void NoLateralSpreading_GivesWtOverSy()
    {
        // With almost no conductivity the water just fills the pores under the basin: Δh = w·t/Sy.
        double dh = GroundwaterMoundingTool.Rise(0, 0, 1, 200, 200, 0.5, 1e-4, 0.25, 100);
        Assert.Equal(0.5 * 1 / 0.25, dh, 2);
    }

    [Fact]
    public void Mound_SpreadsAndRecovers()
    {
        double c = GroundwaterMoundingTool.Rise(0, 0, 2, 100, 50, 2, 20, 0.2, 25);
        Assert.True(c < 2 * 2 / 0.2);
        Assert.True(GroundwaterMoundingTool.Rise(80, 0, 2, 100, 50, 2, 20, 0.2, 25) < c);
        Assert.Equal(c, GroundwaterMoundingTool.RiseWithRecovery(0, 0, 2, 2, 100, 50, 2, 20, 0.2, 25), 9);
        Assert.True(GroundwaterMoundingTool.RiseWithRecovery(0, 0, 4, 2, 100, 50, 2, 20, 0.2, 25) < c);
    }

    [Fact]
    public void ShallowWaterTable_IsFlagged()
    {
        var r = Tool.Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)).Replace("floor depth to water table = 6.0", "floor depth to water table = 3.0"), new DesignToolContext(UnitSystem.USCustomary));
        Assert.Contains(r.Messages, m => m.Code == "W-GM-003");
    }
}
