using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Manhole sizing: opening geometry, elevation overlap, required diameter by hand.</summary>
public class ManholeSizingTests
{
    private static readonly ManholeSizingTool Tool = new();

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary, StormNetwork? n = null)
    {
        var r = Tool.Run(input, new DesignToolContext(u, n));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact]
    public void Samples_RunInBothUnits_AndWithNetwork()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
        var n = NetworkOutputTests.NetA();
        var r = Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary, n)), n: n);
        Assert.Contains(r.Tables[1].Rows, row => (string)row[1]! == "P1" && (double)row[2]! == 24);
    }

    [Fact]
    public void StraightThrough_48in_ByHand()
    {
        // w = 48 + 2·5 + 2·2 = 62; asin(62/D) + asin(8/D) = 90° → D = √(62² + 8²).
        var o = new[] { new ManholeSizingTool.Opening("a", 48, 5, 62, 0, null, null), new ManholeSizingTool.Opening("b", 48, 5, 62, 180, null, null) };
        Assert.Equal(Math.Sqrt(62 * 62 + 64), ManholeSizingTool.RequiredDiameter(o, 8, 8.0 / 12), 4);
        var r = Ok("[Pipes]\nStructure\tPipe\tID\tWall\tAngle\tInvert\nMH\tin\t48\t\t180\t100\nMH\tout\t48\t\t0\t99.9\n");
        Assert.Equal(72.0, r.Tables[0].Rows[0][4]);
    }

    [Fact]
    public void RightAngle_SatisfiesTheConditionAtTheRequiredDiameter()
    {
        var o = new[] { new ManholeSizingTool.Opening("a", 24, 3, 34, 0, null, null), new ManholeSizingTool.Opening("b", 24, 3, 34, 90, null, null) };
        double d = ManholeSizingTool.RequiredDiameter(o, 8, 8.0 / 12);
        Assert.Equal(90, 2 * Math.Asin(34 / d) * 180 / Math.PI + 2 * Math.Asin(8 / d) * 180 / Math.PI, 4);
    }

    [Fact]
    public void OpeningsAtDifferentElevations_DoNotGovern()
    {
        // Two 24 in. pipes 10° apart: they collide at the same invert, but not when one is 4 ft higher.
        string same = "[Pipes]\nStructure,Pipe,ID,Wall,Angle,Invert\nMH,a,24,,0,100\nMH,b,24,,10,100\n";
        string drop = "[Pipes]\nStructure,Pipe,ID,Wall,Angle,Invert\nMH,a,24,,0,100\nMH,b,24,,10,104\n";
        double dSame = (double)Ok(same).Tables[0].Rows[0][2]!, dDrop = (double)Ok(drop).Tables[0].Rows[0][2]!;
        Assert.True(dSame > 144);
        Assert.Equal(34, dDrop, 1);              // only the widest opening (24 + 2·3 + 4)
        Assert.Contains(Ok(same).Messages, m => m.Code == "W-MH-002");
    }

    [Fact]
    public void SiMatchesUs()
    {
        string us = "[Pipes]\nStructure,Pipe,ID,Wall,Angle\nMH,a,24,3,0\nMH,b,18,2.5,100\nMH,c,15,2.25,200\n";
        string si = "clearance = 50.8\nleg = 203.2\n[Pipes]\nStructure,Pipe,ID,Wall,Angle\nMH,a,609.6,76.2,0\nMH,b,457.2,63.5,100\nMH,c,381,57.15,200\n";
        Assert.Equal((double)Ok(us).Tables[0].Rows[0][2]! * 25.4, (double)Ok(si, UnitSystem.SI).Tables[0].Rows[0][2]!, 0);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        Assert.Contains(Tool.Run("[Pipes]\nStructure,Pipe,ID,Wall,Angle\nMH,a,-24,,0\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-MH-004");
        Assert.Contains(Tool.Run("[Pipes]\nStructure,Pipe,ID,Wall,Angle\nMH,a,24,,\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-MH-005");
        Assert.Contains(Tool.Run("leg = 0\n[Pipes]\nStructure,Pipe,ID,Wall,Angle\nMH,a,24,,0\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-MH-002");
        Assert.Contains(Tool.Run("", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-MH-007");
    }
}
