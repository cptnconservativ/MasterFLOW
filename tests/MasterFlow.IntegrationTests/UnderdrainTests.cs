using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Pavement edge drains and bioretention underdrains: hand calculations and closed-form limits.</summary>
public class UnderdrainTests
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);

    [Fact]
    public void Samples_Run()
    {
        foreach (IDesignTool t in new IDesignTool[] { new PavementEdgeDrainTool(), new BioretentionUnderdrainTool() })
        {
            var r = t.Run(t.SampleInput(Us), Us);
            Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        }
    }

    [Fact]
    public void CedergrenInfiltrationAndResultantPath()
    {
        // 2.4·(3/28 + 28/(28·40)) = 0.31714 ft³/day/ft².
        Assert.Equal(0.31714, PavementEdgeDrainTool.Infiltration(2.4, 3, 28, 40, 0), 5);
        var r = new PavementEdgeDrainTool().Run(new PavementEdgeDrainTool().SampleInput(Us), Us);
        var rows = r.Tables[0].Rows.ToDictionary(x => (string)x[0]!, x => x[1]);
        Assert.Equal(0.0361, (double)rows["Resultant slope SR"]!, 4);          // √(0.03² + 0.02²)
        Assert.Equal(50.48, (double)rows["Resultant length LR"]!, 2);          // 28·√(1 + 1.5²)
        Assert.Equal(0.3571, (double)rows["Edge drain full-flow capacity"]!, 4); // 4 in, n 0.012, S 0.03
        Assert.Equal(12.019, (double)rows["Base steady capacity k·H·SR"]!, 3);   // 1000 × 4/12 × 0.036056
    }

    [Fact]
    public void DrainTime_KinematicLimit()
    {
        // Thin layer on a steep bed (H/(L·S) = 0.01): the drained fraction grows as k·S·t/(ne·L), so t50 = 0.5·L·ne/(k·S) = 2.5 days.
        double t = Underdrain.DrainTime(100, 0.05, 0.05, 100, 0.25, 0.5);
        Assert.InRange(t, 2.5 * 0.95, 2.5 * 1.05);
    }

    [Fact]
    public void DrainTime_ScalesWithPermeability()
    {
        double a = Underdrain.DrainTime(50, 0.036, 0.333, 1000, 0.25, 0.5), b = Underdrain.DrainTime(50, 0.036, 0.333, 100, 0.25, 0.5);
        Assert.Equal(10, b / a, 1);   // the equation is linear in t·k
    }

    [Theory]
    [InlineData(1.5, "Excellent")]
    [InlineData(20, "Good")]
    [InlineData(100, "Fair")]
    [InlineData(500, "Poor")]
    [InlineData(1000, "Very poor")]
    public void AashtoQuality(double hours, string q) => Assert.Equal(q, PavementEdgeDrainTool.Quality(hours));

    [Fact]
    public void MediaFlowAndOrifice()
    {
        Assert.Equal(0.083333, BioretentionUnderdrainTool.MediaFlow(2, 1200, 1, 2), 6);   // 2/43200 × 1200 × 3/2
        Assert.Equal(0.6 * Math.PI / 4 / 144 * Math.Sqrt(2 * 32.174 * 4), BioretentionUnderdrainTool.Orifice(1, 0.6, 4), 9);
    }

    [Fact]
    public void FreeDrainingCell_MatchesClosedForm()
    {
        // Falling head through the media: t = (d/k)·ln((h0 + d)/d) = (2/(2/12))·ln 1.5 = 4.866 h.
        // Below the surface: d·nm/k = 2 × 0.2 / (2/12) = 2.4 h.
        var t = BioretentionUnderdrainTool.Drain(1200, 1, 2, 2, 0.2, 1, 0.4, 0, null, 0.6);
        Assert.Equal(12 * Math.Log(1.5), t.SurfaceHours, 1);
        Assert.Equal(2.4, t.SubsurfaceHours, 6);
    }

    [Fact]
    public void Warnings_ForSmallPipeAndSlowDrain()
    {
        var tool = new BioretentionUnderdrainTool();
        var r = tool.Run("area = 20000\nponding depth = 1\nmedia depth = 2\nmedia permeability = 4\npipe diameter = 4\norifice diameter = 0.25\n", Us);
        Assert.Contains(r.Messages, m => m.Code == "W-BU-001");
        Assert.Contains(r.Messages, m => m.Code == "W-BU-002");
        var e = new PavementEdgeDrainTool().Run("lanes = 2\nwidth = 28\ncross slope = 0.02\ngrade = 0.01\nbase thickness = 4\nbase permeability = 20\n", Us);
        Assert.Contains(e.Messages, m => m.Code == "W-ED-001");
        Assert.Contains(e.Messages, m => m.Code == "W-ED-002");
    }
}
