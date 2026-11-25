using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-26 stream-simulation culvert: geometry, Limerinos and composite n, normal depth, Shields stability.</summary>
public class AopCulvertTests
{
    private static readonly AopCulvertTool Tool = new();

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => Tool.Run(input, new DesignToolContext(u));

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Run(input, u);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact]
    public void Sample_RunsCleanInBothUnitSystems()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void Limerinos_ByHand_AndSiConsistent()
    {
        // R = 2 ft, D84 = 0.36 ft: n = 0.0926·2^(1/6)/(1.16 + 2·log10(5.556)) = 0.0393.
        Assert.Equal(0.0926 * Math.Pow(2, 1.0 / 6) / (1.16 + 2 * Math.Log10(2 / 0.36)), AopCulvertTool.Limerinos(2, 0.36, false), 9);
        Assert.Equal(AopCulvertTool.Limerinos(2, 0.36, false), AopCulvertTool.Limerinos(2 * 0.3048, 0.36 * 0.3048, true), 3);
    }

    [Fact]
    public void PipeSection_HalfEmbedded()
    {
        // 10 ft pipe embedded 5 ft: bed width = 10 ft; flow to the crown is the upper semicircle.
        var (a, pw, b, _) = AopCulvertTool.Section(false, 10, 10, 5, 5);
        Assert.Equal(10, b, 6);
        Assert.Equal(Math.PI * 25 / 2, a, 6);
        Assert.Equal(Math.PI * 5, pw, 6);
    }

    [Fact]
    public void BoxNormalDepth_SatisfiesManningWithCompositeN()
    {
        var h = AopCulvertTool.Barrel(true, 14, 8, 2, 0.012, 0.015, 120, 110 / 304.8, false)!;
        double p = 14 + 2 * h.Y, nc = Math.Pow((14 * Math.Pow(h.NBed, 1.5) + 2 * h.Y * Math.Pow(0.012, 1.5)) / p, 2.0 / 3);
        Assert.Equal(nc, h.N, 6);
        double q = 1.486 / nc * 14 * h.Y * Math.Pow(14 * h.Y / p, 2.0 / 3) * Math.Sqrt(0.015);
        Assert.Equal(120, q, 1);
        // Bed n agrees with Limerinos on the bed partition radius.
        Assert.Equal(AopCulvertTool.Limerinos(h.RBed, 110 / 304.8, false), h.NBed, 4);
        Assert.Equal(62.4 * h.RBed * 0.015, h.TauBed, 6);
    }

    [Fact]
    public void FineBed_IsMobileAtTheDesignFlow()
    {
        string input = Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)).Replace("D50 = 90 ", "D50 = 8 ").Replace("D84 = 280", "D84 = 15").Replace("D95 = 400", "D95 = 25");
        var r = Run(input);
        Assert.Contains(r.Messages, m => m.Code == "W-AO-006");
    }

    [Fact]
    public void NarrowBarrel_IsFlagged()
    {
        string input = Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)).Replace("span = 14", "span = 8");
        var r = Run(input);
        Assert.Contains(r.Messages, m => m.Code == "W-AO-001");
    }
}
