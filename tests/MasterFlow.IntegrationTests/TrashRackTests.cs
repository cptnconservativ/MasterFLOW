using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Trash rack: Kirschmer and Creager–Justin head loss by hand, clogging, velocity limit.</summary>
public class TrashRackTests
{
    private static readonly TrashRackTool Tool = new();

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private const string Head = "[Racks]\nName,Q,Width,Height,Bar t,Clear b,Angle,Shape\n";

    [Fact]
    public void Samples_RunInBothUnits()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void VerticalRack_ByHand()
    {
        var r = Ok("max velocity = 5\n" + Head + "A,42,6,4,0.375,3,90,rectangular\n");
        var row = r.Tables[0].Rows[0];
        double ratio = 3 / 3.375, an = 24 * ratio, vn = 42 / an, g2 = 2 * 32.174;
        Assert.Equal(Math.Round((1.45 - 0.45 * ratio - ratio * ratio) * vn * vn / g2, 3), (double)row[8]!, 3);
        double rc = ratio / 2, vc = 42 / (an / 2);
        Assert.Equal(Math.Round((1.45 - 0.45 * rc - rc * rc) * vc * vc / g2, 3), (double)row[9]!, 3);
        double va = 42.0 / 24;
        Assert.Equal(Math.Round(2.42 * Math.Pow(0.125, 4.0 / 3) * va * va / g2, 3), (double)row[7]!, 3);
        Assert.Equal("OK", row[10]);
    }

    [Fact]
    public void InclinedRack_HasLargerAreaAndLessLoss()
    {
        var v = Ok(Head + "A,42,6,4,0.375,3,90,round\n").Tables[0].Rows[0];
        var s = Ok(Head + "A,42,6,4,0.375,3,30,round\n").Tables[0].Rows[0];
        Assert.Equal(2 * (double)v[2]!, (double)s[2]!, 2);
        Assert.True((double)s[9]! < (double)v[9]!);
        Assert.True((double)s[7]! < (double)v[7]!);
    }

    [Fact]
    public void FastRack_IsFlagged()
    {
        var r = Ok("max velocity = 2\n" + Head + "A,90,4,3,1,4,90,rectangular\n");
        Assert.Equal("TOO FAST", r.Tables[0].Rows[0][10]);
        Assert.Contains(r.Messages, m => m.Code == "W-TK-001");
    }

    [Fact]
    public void SiMatchesUs()
    {
        var us = Ok(Head + "A,42,6,4,0.375,3,90,rectangular\n");
        var si = Ok(Head + $"A,{42 * 0.0283168},{6 * 0.3048},{4 * 0.3048},{0.375 * 25.4},{3 * 25.4},90,rectangular\n", UnitSystem.SI);
        Assert.Equal((double)us.Tables[0].Rows[0][9]! * 0.3048, (double)si.Tables[0].Rows[0][9]!, 3);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        Assert.Contains(Tool.Run("clogging = 100\n" + Head + "A,42,6,4,0.375,3,90,rectangular\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-TK-001");
        Assert.Contains(Tool.Run(Head + "A,42,6,0,0.375,3,90,rectangular\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-TK-003");
        Assert.Contains(Tool.Run(Head + "A,42,6,4,0.375,3,120,rectangular\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-TK-004");
        Assert.Contains(Tool.Run(Head + "A,42,6,4,0.375,3,90,hexagonal\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-TK-005");
        Assert.Contains(Tool.Run("", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-TK-006");
    }
}
