using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Roof drainage: IPC flow and leader table, Manning horizontal capacity, scupper head and ASCE 7 rain load.</summary>
public class RoofDrainageTests
{
    private static readonly RoofDrainageTool Tool = new();

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
    public void FlowAndLeader_ByHand()
    {
        // 6,000 + ½·400 = 6,200 ft² at 2.95 in./h = 190 gpm → 4 in. leader (192 gpm).
        var r = Ok("intensity = 2.95\n[Drains]\nName,Roof area,Wall area,Leader\nRD,6000,400,3\n");
        var row = r.Tables[0].Rows[0];
        Assert.Equal(Math.Round(6200 * 2.95 * 0.0103889, 1), (double)row[2]!, 1);
        Assert.Equal("4", row[3]);
        Assert.Equal("TOO SMALL", row[6]);
        Assert.Contains(r.Messages, m => m.Code == "W-RF-002");
    }

    [Fact]
    public void HorizontalCapacity_MatchesIpcTableBasis()
    {
        Assert.InRange(RoofDrainageTool.FullFlowGpm(8, 0.125), 470, 490);   // IPC table: 478 gpm
        Assert.InRange(RoofDrainageTool.FullFlowGpm(4, 0.25), 105, 112);    // IPC table: 110 gpm
        var r = Ok("intensity = 3\n[Drains]\nName,Roof area,Wall area,Leader,Horizontal,Slope\nRD,6000,0,,6,1/8\n");
        Assert.Equal(6.0, r.Tables[0].Rows[0][8]);
    }

    [Fact]
    public void Scupper_HeadAndRainLoad_ByHand()
    {
        var r = Ok("intensity = 2.95\n[Scuppers]\nName,Roof area,Wall area,Width,ds\nOF,6000,400,12,2\n");
        double gpm = 6200 * 2.95 * 0.0103889, h = 12 * Math.Pow(gpm / 448.831 / 3.22, 2.0 / 3);
        var row = r.Tables[0].Rows[0];
        Assert.Equal(Math.Round(h, 2), (double)row[5]!, 2);
        Assert.Equal(Math.Round(5.2 * (2 + h), 1), (double)row[6]!, 1);
        // FM Global form gives the same head: Q = 2.9·b·h^1.5 (gpm, in.).
        Assert.Equal(gpm, 2.9 * 12 * Math.Pow(h, 1.5), 0);
    }

    [Fact]
    public void SiMatchesUs()
    {
        var us = Ok("intensity = 2.95\n[Scuppers]\nName,Roof area,Wall area,Width,ds\nOF,6000,0,12,2\n");
        var si = Ok($"intensity = {2.95 * 25.4}\n[Scuppers]\nName,Roof area,Wall area,Width,ds\nOF,{6000 / 10.7639},0,{12 * 25.4},{2 * 25.4}\n", UnitSystem.SI);
        Assert.Equal((double)us.Tables[0].Rows[0][6]! * 0.0478803, (double)si.Tables[0].Rows[0][6]!, 2);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        Assert.Contains(Tool.Run("[Drains]\nName,Roof area\nRD,100\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-RF-001");
        Assert.Contains(Tool.Run("intensity = 3\n[Drains]\nName,Roof area,Wall area,Leader,Horizontal,Slope\nRD,100,0,,6,steep\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-RF-003");
        Assert.Contains(Tool.Run("intensity = 3\n[Drains]\nName,Roof area,Wall area,Leader,Horizontal\nRD,100,0,,6\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-RF-005");
        Assert.Contains(Tool.Run("intensity = 3\n[Scuppers]\nName,Roof area,Wall area,Width,ds\nOF,100,0,0,1\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-RF-006");
        Assert.Contains(Tool.Run("intensity = 3", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-RF-007");
    }
}
