using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Pipe structural: RCP indirect design and Modified Iowa deflection by hand.</summary>
public class PipeStructuralTests
{
    private static readonly PipeStructuralTool Tool = new();

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact]
    public void Samples_RunInBothUnits()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void LiveLoad_SingleWheel_ByHand()
    {
        var (w, wl) = PipeStructuralTool.LiveLoad(3, 2.5, 1.15);
        double a = 10.0 / 12 + 3.45, b = 20.0 / 12 + 3.45, ww = 16000 * 1.20625 * 1.2 / (a * b);
        Assert.Equal(ww, w, 6);
        Assert.Equal(ww * b * 2.5 / (b + 1.3125 * 2.5), wl, 6);
    }

    [Fact]
    public void LiveLoad_AxleWheelsCombine_WhenSpreadPassesSixFeet()
    {
        var (w, _) = PipeStructuralTool.LiveLoad(5, 3, 1.15);
        double a = 10.0 / 12 + 5.75, b = 6 + 20.0 / 12 + 5.75, im = 0.33 * (1 - 0.625);
        Assert.Equal(32000 * (1 + im) * 1.2 / (a * b), w, 6);
    }

    [Fact]
    public void Rcp24_ThreeFeet_Type2_IsClassII()
    {
        var r = Ok("[Rigid]\nName,ID,Cover,Installation\nA,24,3,2\n");
        var row = r.Tables[0].Rows[0];
        Assert.Equal(1373, (double)row[6]!, 0);
        double we = 1.4 * 120 * (3 + 2.5 * (4 - Math.PI) / 8) * 2.5, wf = 62.4 * Math.PI;
        var (_, wl) = PipeStructuralTool.LiveLoad(3, 2.5, 1.15);
        Assert.Equal(Math.Round(((we + wf) / 3.0 + wl / 1.1) / 2), (double)row[11]!, 0);
        Assert.Equal("Class II", row[12]);
    }

    [Fact]
    public void Rcp_DeepFill_NoLiveLoad_HigherClass()
    {
        var r = Ok("[Rigid]\nName,ID,Cover,Installation\nA,48,20,2\nB,48,20,4\n");
        Assert.Equal(0.0, (double)r.Tables[0].Rows[0][8]!);
        Assert.NotEqual(r.Tables[0].Rows[0][12], r.Tables[0].Rows[1][12]);       // a poorer installation needs a stronger pipe
    }

    [Fact]
    public void Bfe_Interpolates()
    {
        Assert.Equal(3.0, PipeStructuralTool.Bfe(2, 24), 9);
        Assert.Equal(2.95, PipeStructuralTool.Bfe(2, 30), 9);
        Assert.Equal(1.7, PipeStructuralTool.Bfe(4, 200), 9);
    }

    [Fact]
    public void Flexible_ModifiedIowa_ByHand()
    {
        // 10 ft cover, no live load: Pe = 120·10/144 psi; E′ = 2000 (gravel, moderate); PS = 40.
        var r = Ok("live load = none\n[Flexible]\nName,Material,ID,OD,Cover,PS,Soil,Compaction\nA,HDPE,18,21,10,40,gravel,moderate\n");
        double pe = 1200.0 / 144, d = 100 * 0.1 * pe / (0.149 * 40 + 0.061 * 2000);
        Assert.Equal(Math.Round(d, 2), (double)r.Tables[0].Rows[0][9]!, 2);
        Assert.Equal(3000, PipeStructuralTool.EPrime("crushed rock", "slight"));
        Assert.True(double.IsNaN(PipeStructuralTool.EPrime("peat", "high")));
    }

    [Fact]
    public void SiMatchesUs()
    {
        var us = Ok("[Rigid]\nName,ID,Cover,Installation\nA,24,3,2\n");
        var si = Ok("unit weight = 18.85\n[Rigid]\nName,ID,Cover,Installation\nA,609.6,0.9144,2\n", UnitSystem.SI);
        Assert.Equal((double)us.Tables[0].Rows[0][11]! * 0.0478803, (double)si.Tables[0].Rows[0][11]!, 0);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        Assert.Contains(Tool.Run("[Rigid]\nName,ID,Cover,Installation\nA,24,3,5\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-PST-004");
        Assert.Contains(Tool.Run("[Rigid]\nName,ID,Cover,Installation\nA,24,-3,2\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-PST-003");
        Assert.Contains(Tool.Run("[Flexible]\nName,Material,ID,OD,Cover,PS,Soil,Compaction\nA,wood,18,21,4,40,gravel,high\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-PST-006");
        Assert.Contains(Tool.Run("[Flexible]\nName,Material,ID,OD,Cover,PS,Soil,Compaction\nA,HDPE,18,21,4,40,peat,high\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-PST-008");
        Assert.Contains(Tool.Run("", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-PST-010");
    }
}
