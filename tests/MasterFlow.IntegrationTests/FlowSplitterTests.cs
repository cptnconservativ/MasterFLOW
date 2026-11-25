using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Flow splitter: crest from the orifice, orifice from the crest, design-storm split and weir length.</summary>
public class FlowSplitterTests
{
    private static readonly FlowSplitterTool Tool = new();
    private const double G = 32.174;

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static double Val(ToolResult r, string item) => (double)r.Tables[0].Rows.Single(x => (string)x[0]! == item)[1]!;

    [Fact]
    public void Samples_RunInBothUnits()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void CrestFromOrifice_ByHand()
    {
        var r = Ok("Q design = 45\nQ wq = 1.8\norifice invert = 100\norifice diameter = 8\nweir length = 6\n");
        double a = Math.PI * (8.0 / 12) * (8.0 / 12) / 4, h = Math.Pow(1.8 / (0.61 * a), 2) / (2 * G);
        Assert.Equal(Math.Round(100 + 1.0 / 3 + h, 3), Val(r, "Bypass weir crest"), 3);
    }

    [Fact]
    public void DesignSplit_SumsToDesignFlow()
    {
        var r = Ok("Q design = 45\nQ wq = 1.8\norifice invert = 100\norifice diameter = 8\nweir length = 6\n");
        double z = Val(r, "Water surface at Q design"), crest = Val(r, "Bypass weir crest");
        double q = FlowSplitterTool.Orifice(0.61, 8.0 / 12, 100 + 1.0 / 3, z, G) + FlowSplitterTool.Weir(3.0, 6, crest, z);
        Assert.Equal(45, q, 1);
        Assert.Equal(45, Val(r, "Flow to the BMP at Q design") + Val(r, "Bypass flow at Q design"), 3);
    }

    [Fact]
    public void OrificeFromCrest_PassesQwqAtTheCrest()
    {
        var r = Ok("Q design = 20\nQ wq = 1.0\norifice invert = 100\ncrest = 101.5\nweir length = 4\n");
        double d = Val(r, "Orifice diameter") / 12;
        Assert.Equal(1.0, FlowSplitterTool.Orifice(0.61, d, 100 + d / 2, 101.5, G), 2);   // diameter shown to 0.01 in.
    }

    [Fact]
    public void WeirLength_ForMaxWse_AndBmpCapacity()
    {
        var r = Ok("Q design = 45\nQ wq = 1.8\norifice invert = 100\norifice diameter = 8\nweir length = 3\nmax WSE = 103.5\nmax treatment flow = 2\n");
        double l = Val(r, "Weir length for max WSE");
        double crest = Val(r, "Bypass weir crest");
        Assert.Equal(45, FlowSplitterTool.Orifice(0.61, 8.0 / 12, 100 + 1.0 / 3, 103.5, G) + FlowSplitterTool.Weir(3.0, l, crest, 103.5), 1);
        Assert.Contains(r.Messages, m => m.Code == "W-FS-004");
        Assert.Contains(r.Messages, m => m.Code == "W-FS-002");
    }

    [Fact]
    public void SiMatchesUs()
    {
        var us = Ok("Q design = 45\nQ wq = 1.8\norifice invert = 100\norifice diameter = 8\nweir length = 6\nweir C = 3.0\n");
        var si = Ok($"Q design = {45 * 0.0283168}\nQ wq = {1.8 * 0.0283168}\norifice invert = {100 * 0.3048}\norifice diameter = {8 * 25.4}\nweir length = {6 * 0.3048}\nweir C = {3.0 * 0.551916}\n", UnitSystem.SI);
        Assert.Equal(Val(us, "Water surface at Q design") * 0.3048, Val(si, "Water surface at Q design"), 2);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        var u = new DesignToolContext(UnitSystem.USCustomary);
        Assert.Contains(Tool.Run("Q design = 1\nQ wq = 2\norifice invert = 100\norifice diameter = 8\nweir length = 6\n", u).Messages, m => m.Code == "E-FS-001");
        Assert.Contains(Tool.Run("Q design = 10\nQ wq = 1\norifice invert = 100\nweir length = 6\n", u).Messages, m => m.Code == "E-FS-004");
        Assert.Contains(Tool.Run("Q design = 10\nQ wq = 1\norifice invert = 100\ncrest = 99\nweir length = 6\n", u).Messages, m => m.Code == "E-FS-006");
        Assert.Contains(Tool.Run("Q design = 100\nQ wq = 50\norifice invert = 100\ncrest = 100.2\nweir length = 6\n", u).Messages, m => m.Code == "E-FS-007");
    }
}
