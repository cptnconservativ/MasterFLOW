using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>RUSLE soil loss and the Rapid City SWQM (2009) Chapter 1 sediment control limits, against hand calcs.</summary>
public class ErosionSedimentTests
{
    private static readonly ErosionSedimentTool Tool = new();
    private static readonly DesignToolContext Ctx = new(UnitSystem.USCustomary);

    [Fact]
    public void Ls_UnitPlotIsOne_AndShortSlopes()
    {
        // The RUSLE unit plot is 72.6 ft at 9 %: L = 1, S = 16.8·sin(atan 0.09) − 0.5 = 1.006.
        var u = ErosionSedimentTool.Ls(72.6, 9);
        Assert.Equal(1.0, u.L, 9);
        Assert.Equal(1.006, u.S, 3);
        // λ < 15 ft: S = 3.0·sin^0.8 θ + 0.56; 10 % → sin = 0.0995, sin^0.8 = 0.1580 → 1.034.
        Assert.Equal(1.034, ErosionSedimentTool.Ls(10, 10).S, 3);
    }

    [Fact]
    public void Rusle_MatchesHandCalc()
    {
        // 180 ft at 4 %: sinθ = 0.03997, β = (0.03997/0.0896)/(3·0.07609 + 0.56) = 0.5659, m = 0.3614,
        // L = (180/72.6)^0.3614 = 1.388, S = 10.8·0.03997 + 0.03 = 0.4617, LS = 0.641; A = 40·0.32·0.641 = 8.20 t/ac/yr.
        var r = Tool.Run("R = 40\n[Areas]\nName\tArea (ac)\tSlope length (ft)\tSlope (%)\tK\tC\tP\nA\t2\t180\t4\t0.32\t1\t1", Ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var row = r.Tables[0].Rows[0];
        Assert.Equal(0.361, (double)row[4]!, 3);
        Assert.Equal(1.388, (double)row[5]!, 3);
        Assert.Equal(0.462, (double)row[6]!, 3);
        Assert.Equal(0.641, (double)row[7]!, 3);
        Assert.Equal(8.2, (double)row[11]!, 1);
        Assert.Equal(16.4, (double)row[12]!, 1);
    }

    [Fact]
    public void Sample_FlagsTable11AndTable12Problems()
    {
        var r = Tool.Run(Tool.SampleInput(Ctx), Ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains(r.Messages, m => m.Code == "W-ES-002" && m.Text.StartsWith("Stockpile"));
        Assert.Contains(r.Messages, m => m.Code == "W-ES-004" && m.Text.StartsWith("SF-2") && m.Text.Contains("280 ft") && m.Text.Contains("75 ft"));
        Assert.Contains(r.Messages, m => m.Code == "W-ES-004" && m.Text.StartsWith("W-1"));
        Assert.DoesNotContain(r.Messages, m => m.Text.StartsWith("CS-1") || m.Text.StartsWith("SF-1") || m.Text.StartsWith("SF-3"));
    }

    [Fact]
    public void Basin_VolumeDrainTimeAndShape()
    {
        // 12.5 ac → 45,000 ft³. Drain of B: plan area 40,000/4.5 = 8,889 ft²; 2 in. orifice a = 0.02182 ft²;
        // t = 2·As·√H/(0.6·a·√(2g)) = 2·8,889·2.121/(0.6·0.02182·8.022) = 359,100 s = 99.8 h.
        var r = Tool.Run("[Basins]\nName\tType\tTributary disturbed area (ac)\tVolume (ft³)\tLength (ft)\tWidth (ft)\tDepth (ft)\tOrifice (in.)\n" +
                         "B\tbasin\t12.5\t40000\t100\t70\t4.5\t2\nC\tbasin\t12.5\t48000\t160\t70\t4.5\t8\nT\ttrap\t6\t30000\t40\t35\t3\t", Ctx);
        var t = r.Tables.Single();
        var b = t.Rows.Single(x => (string?)x[0] == "B");
        Assert.Equal(45000.0, (double)b[3]!);
        Assert.Contains("99.8 h", (string)b[8]!);
        Assert.Contains("40,000 ft³ < 45,000", (string)b[10]!);
        Assert.Contains("2 × width", (string)b[10]!);
        var c = t.Rows.Single(x => (string?)x[0] == "C");
        Assert.Contains("< 16 h", (string)c[10]!);
        var tr = t.Rows.Single(x => (string?)x[0] == "T");
        Assert.Contains("at most 5 ac", (string)tr[10]!);
        Assert.DoesNotContain("2 × width", (string)tr[10]!);
    }

    [Fact]
    public void MoreThanTenAcresWithoutBasin_Warns()
    {
        var r = Tool.Run("R = 40\n[Areas]\nName\tArea (ac)\tSlope length (ft)\tSlope (%)\tK\nA\t11\t100\t3\t0.3", Ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-ES-007");
    }

    [Theory]
    [InlineData("3:1", 3.0)]
    [InlineData("25%", 4.0)]
    [InlineData("4", 25.0)]
    public void SlopeParsing(string s, double hv) => Assert.Equal(hv, ErosionSedimentTool.SlopeHv(s)!.Value, 9);

    [Fact]
    public void BadInput_IsReported()
    {
        Assert.Contains(Tool.Run("R = 40", Ctx).Messages, m => m.Code == "E-ES-003");
        Assert.Contains(Tool.Run("[Areas]\nName\tArea (ac)\tSlope length (ft)\tSlope (%)\tK\nA\t1\t100\t3\t0.3", Ctx).Messages, m => m.Code == "E-ES-002");
        Assert.Contains(Tool.Run("[Perimeter controls]\nName\tType\tLength (ft)\nX\thay bales\t100", Ctx).Messages, m => m.Code == "E-ES-004");
    }
}
