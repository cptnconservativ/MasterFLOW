using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Structure buoyancy: uplift, concrete and soil by hand; ballast and extension reach the required FS.</summary>
public class StructureBuoyancyTests
{
    private static readonly StructureBuoyancyTool Tool = new();

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private const string Head = "[Structures]\nName,Shape,L,W,Wall,Base,Ext,Top slab,Opening,Bottom,Top,GW\n";

    [Fact]
    public void Samples_RunInBothUnits()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void RoundManhole_ByHand()
    {
        var r = Ok(Head + "MH,round,5,,0.5,0.67,0,0.67,2,95,108,106.5\n");
        var row = r.Tables[0].Rows[0];
        double outer = Math.PI * 9, inner = Math.PI * 6.25;
        double u = 62.4 * outer * 11.5;
        double c = 150 * (outer * 0.67 + (outer - inner) * (13 - 1.34) + (outer - Math.PI) * 0.67);
        Assert.Equal(Math.Round(u), (double)row[2]!, 0);
        Assert.Equal(Math.Round(c), (double)row[3]!, 0);
        Assert.Equal(Math.Round(c / u, 2), (double)row[6]!, 2);
        Assert.Equal("FLOATS", row[7]);
        Assert.Equal(Math.Round((1.25 * u - c) / 150, 2), (double)row[8]!, 2);
    }

    [Fact]
    public void ExtensionFound_ReachesRequiredFs()
    {
        var r = Ok(Head + "MH,round,5,,0.5,0.67,0,0.67,2,95,108,106.5\n");
        double ext = (double)r.Tables[0].Rows[0][9]!;
        var s = new StructureBuoyancyTool.Structure("MH", true, 5, 5, 0.5, 0.67, ext, 0.67, 2, 95, 108, 106.5);
        var (u, c, soil) = StructureBuoyancyTool.Forces(s, ext, 150, 62.4, 120, 125);
        Assert.Equal(1.25, (c + soil) / u, 2);
        Assert.True(soil > 0);
    }

    [Fact]
    public void BoxSoilOnExtension_SplitsMoistAndBuoyant()
    {
        // 4 × 4 ft inside, 0.5 ft wall → 5 × 5 outside, 1 ft ext → 7 × 7 base. Soil from 101 (top of base) to 110; GW 105.
        var s = new StructureBuoyancyTool.Structure("B", false, 4, 4, 0.5, 1, 1, 0.5, 0, 100, 110, 105);
        var (_, _, soil) = StructureBuoyancyTool.Forces(s, 1, 150, 62.4, 120, 125);
        Assert.Equal((49 - 25) * (5 * 120 + 4 * (125 - 62.4)), soil, 6);
    }

    [Fact]
    public void GroundwaterBelowStructure_NoUplift()
    {
        var r = Ok(Head + "MH,round,4,,0.42,0.5,0,0.5,2,95,105,90\n");
        Assert.Equal("no uplift", r.Tables[0].Rows[0][7]);
    }

    [Fact]
    public void SiMatchesUs()
    {
        var us = Ok(Head + "MH,round,5,,0.5,0.67,0,0.67,2,95,108,106.5\n");
        double f = 0.3048;
        var si = Ok($"concrete = {150 * 0.157087}\nwater = {62.4 * 0.157087}\nsoil moist = {120 * 0.157087}\nsoil saturated = {125 * 0.157087}\n" + Head + $"MH,round,{5 * f},,{0.5 * f},{0.67 * f},0,{0.67 * f},{2 * f},{95 * f},{108 * f},{106.5 * f}\n", UnitSystem.SI);
        Assert.Equal((double)us.Tables[0].Rows[0][6]!, (double)si.Tables[0].Rows[0][6]!, 2);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        Assert.Contains(Tool.Run(Head + "MH,oval,5,,0.5,0.67,0,0.67,2,95,108,106.5\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-BY-003");
        Assert.Contains(Tool.Run(Head + "MH,round,5,,0.5,0.67,0,0.67,2,95,,106.5\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-BY-004");
        Assert.Contains(Tool.Run(Head + "MH,round,5,,0.5,0.67,0,0.67,2,108,95,106.5\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-BY-005");
        Assert.Contains(Tool.Run("FS required = 0.8\n" + Head + "MH,round,5,,0.5,0.67,0,0.67,2,95,108,106.5\n", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-BY-002");
        Assert.Contains(Tool.Run("", new(UnitSystem.USCustomary)).Messages, m => m.Code == "E-BY-008");
    }
}
