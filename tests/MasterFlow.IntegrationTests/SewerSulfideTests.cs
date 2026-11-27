using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Sewer sulfide build-up and corrosion: Pomeroy–Parkhurst, full-pipe build-up, wall flux, corrosion rate, recommendations.</summary>
public class SewerSulfideTests
{
    private static readonly SewerSulfideTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static string Sample => Tool.SampleInput(Us);
    private static ToolResult Run(string input) => Tool.Run(input, Us);
    private static IEnumerable<string> Codes(ToolResult r, MessageSeverity s = MessageSeverity.Warning) => r.Messages.Where(m => m.Severity == s).Select(m => m.Code);

    private const string Head = """
        BOD = 250
        temperature = 20
        pH = 7.0
        """;

    [Fact]
    public void Sample_RunsClean_AndSiIsRefused()
    {
        var r = Run(Sample);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Equal(2, r.Tables.Count);
        Assert.Equal(2, r.Charts.Count);
        Assert.Contains("W-SC-001", Codes(r));   // the RCP reaches lose more than 1 in. in 75 years
        Assert.False(Tool.Run(Sample, new DesignToolContext(UnitSystem.SI)).Succeeded);
    }

    [Fact]
    public void Ebod_AndH2SFraction_ByHand()
    {
        Assert.Equal(250, SewerSulfideTool.Ebod(250, 20), 9);
        Assert.Equal(250 * Math.Pow(1.07, 5), SewerSulfideTool.Ebod(250, 25), 9);
        Assert.Equal(0.5, SewerSulfideTool.H2SFraction(7.0, 7.0), 12);
        Assert.Equal(1 / 1.1, SewerSulfideTool.H2SFraction(6.0, 7.0), 12);
        Assert.Equal(6.98, SewerSulfideTool.PK1(25), 12);
        Assert.InRange(SewerSulfideTool.PK1(10), 7.2, 7.27);   // tabulated 7.24
    }

    [Fact]
    public void Gravity_TendsToTheLimit_AndHoldsAtIt()
    {
        // 15 in. pipe, r = 0.1 m, dm = 0.15 m, s = 0.0015, u = 0.5 m/s.
        double lim = 0.32e-3 * 250 / 0.1 / (0.64 * Math.Pow(0.0015 * 0.5, 0.375) / 0.15);
        var (l, o) = SewerSulfideTool.Gravity(0, 250, 0.1, 0.15, 0.0015, 0.5, 0.32e-3, 0.64, 1);
        Assert.Equal(lim, l, 9);
        Assert.InRange(o, 0, lim);
        Assert.Equal(lim, SewerSulfideTool.Gravity(0, 250, 0.1, 0.15, 0.0015, 0.5, 0.32e-3, 0.64, 1000).Out, 6);
        Assert.Equal(lim, SewerSulfideTool.Gravity(lim, 250, 0.1, 0.15, 0.0015, 0.5, 0.32e-3, 0.64, 3).Out, 9);
        // Above the limit, sulfide falls back toward it.
        Assert.InRange(SewerSulfideTool.Gravity(2 * lim, 250, 0.1, 0.15, 0.0015, 0.5, 0.32e-3, 0.64, 1).Out, lim, 2 * lim);
        // Short times: the rise is the generation rate times t.
        Assert.Equal(0.32e-3 * 250 / 0.1 * 0.001, SewerSulfideTool.Gravity(0, 250, 0.1, 0.15, 0.0015, 0.5, 0.32e-3, 0.64, 0.001).Out, 6);
    }

    [Fact]
    public void ForceMain_ByHand()
    {
        // 8 in. (0.2032 m), 4,200 ft at 260 gpm: V = 1.66 ft/s, t = 0.703 h; ΔS = 1e-3·t·EBOD·(4/d + 1.57).
        double q = 260 / 448.831, a = Math.PI * Math.Pow(8.0 / 12, 2) / 4, t = 4200 / (q / a) / 3600;
        double expected = 1e-3 * t * 250 * (4 / 0.2032 + 1.57);
        var r = Run(Head + "\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\tMaterial\nFM\tforce main\t4200\t8\t\t\t260\tPVC\n");
        Assert.True(r.Succeeded);
        Assert.Equal(Math.Round(expected, 3), (double)r.Tables[0].Rows[0][12]!, 3);
        Assert.Equal(expected, SewerSulfideTool.FullPipe(0, 250, 0.2032, 1e-3, t), 9);
        // 6 in. and smaller uses M = 0.3e-3 unless M force main is given.
        var small = Run(Head + "\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nFM\tforce main\t1000\t6\t\t\t100\n");
        double q6 = 100 / 448.831, t6 = 1000 / (q6 / (Math.PI * 0.25 / 4)) / 3600;
        Assert.Equal(Math.Round(0.3e-3 * t6 * 250 * (4 / (0.5 * 0.3048) + 1.57), 3), (double)small.Tables[0].Rows[0][12]!, 3);
    }

    [Fact]
    public void Corrosion_ByHand_AndInverseInAlkalinity()
    {
        double phi = SewerSulfideTool.WallFlux(0.001, 0.6, 0.5, 2.0, 0.3, 0.5);
        Assert.Equal(0.69 * Math.Pow(0.0006, 0.375) * 0.5 * 2.0 * 0.3 / 0.5, phi, 12);
        Assert.Equal(11.5 * 0.7 * phi / 0.2, SewerSulfideTool.CorrosionRate(0.7, phi, 0.2), 12);
        Assert.Equal(SewerSulfideTool.CorrosionRate(0.7, phi, 0.2) / 4.5, SewerSulfideTool.CorrosionRate(0.7, phi, 0.9), 12);
        Assert.Equal(0, SewerSulfideTool.WallFlux(0.001, 0.6, 0.5, 2.0, 0.3, 0));
    }

    [Fact]
    public void Mixing_DilutesAtFlowIncreases()
    {
        // A short, steep reach adds almost nothing; doubling the flow halves the incoming sulfide.
        var r = Run(Head + "\ninitial sulfide = 4\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t1\t12\t1\t0.013\t200\nB\tgravity\t1\t12\t1\t0.013\t400\n");
        Assert.True(r.Succeeded);
        Assert.Equal(2.0, (double)r.Tables[0].Rows[1][10]!, 2);
        var lat = Run(Head + "\ninitial sulfide = 4\nlateral sulfide = 2\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t1\t12\t1\t0.013\t200\nB\tgravity\t1\t12\t1\t0.013\t400\n");
        Assert.Equal(3.0, (double)lat.Tables[0].Rows[1][10]!, 2);
        var less = Run(Head + "\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t100\t12\t1\t0.013\t400\nB\tgravity\t100\t12\t1\t0.013\t200\n");
        Assert.Contains("W-SC-005", Codes(less));
    }

    [Fact]
    public void Recommendation_ByMaterialAndSeverity()
    {
        string run(string mat, string extra = "") => Head + extra + $"\ninitial sulfide = 6\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\tMaterial\nA\tgravity\t3000\t15\t0.15\t0.013\t260\t{mat}\n";
        var rcp = Run(run("RCP", "\nk = 1\nalkalinity = 0.17"));
        Assert.Contains("W-SC-001", Codes(rcp));
        Assert.StartsWith("Use PVC", (string)rcp.Tables[1].Rows[0][2]!);
        var pvc = Run(run("PVC", "\nk = 1\nalkalinity = 0.17"));
        Assert.DoesNotContain("W-SC-001", Codes(pvc));
        Assert.StartsWith("Material resists", (string)pvc.Tables[1].Rows[0][2]!);
        // A mild case: plain concrete is enough.
        var mild = Run(Head + "\nBOD = 80\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\tMaterial\nA\tgravity\t500\t12\t2\t0.013\t300\tRCP\n");
        Assert.Equal("Plain concrete meets the design life.", (string)mild.Tables[1].Rows[0][2]!);
        Assert.DoesNotContain("W-SC-001", Codes(mild));
        // A longer design life can turn a pass into a fail.
        var longer = Run(Head + "\nBOD = 80\ndesign life = 2000\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\tMaterial\nA\tgravity\t500\t12\t2\t0.013\t300\tRCP\n");
        Assert.Contains("W-SC-001", Codes(longer));
    }

    [Fact]
    public void FahrenheitEqualsCelsius_AndWarmerIsWorse()
    {
        string reach = "\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t2000\t15\t0.2\t0.013\t260\n";
        var c = Run("temperature = 25" + reach);
        var f = Run("temperature F = 77" + reach);
        Assert.Equal(c.Tables[0].Rows[0][12], f.Tables[0].Rows[0][12]);
        var cold = Run("temperature = 10" + reach);
        Assert.True((double)cold.Tables[0].Rows[0][12]! < (double)c.Tables[0].Rows[0][12]!);
    }

    [Fact]
    public void SurchargedGravityReach_IsTreatedAsFull()
    {
        var r = Run(Head + "\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t500\t8\t0.1\t0.013\t900\n");
        Assert.True(r.Succeeded);
        Assert.Contains("W-SC-004", Codes(r));
        Assert.Equal("Gravity (full)", r.Tables[0].Rows[0][1]);
    }

    [Theory]
    [InlineData("[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t-10\t8\t0.5\t0.013\t100\n")]
    [InlineData("[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t100\t8\t0\t0.013\t100\n")]
    [InlineData("[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t100\t8\t0.5\t0.013\t0\n")]
    [InlineData("[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tsideways\t100\t8\t0.5\t0.013\t100\n")]
    [InlineData("pH = 13\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t100\t8\t0.5\t0.013\t100\n")]
    [InlineData("k = 1.5\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t100\t8\t0.5\t0.013\t100\n")]
    [InlineData("temperature F = 20\n[Reaches]\nReach\tType\tLength\tD\tSlope\tn\tFlow\nA\tgravity\t100\t8\t0.5\t0.013\t100\n")]
    [InlineData("BOD = 250\n")]
    public void BadInput_IsAnError(string input) => Assert.False(Run(input).Succeeded);
}
