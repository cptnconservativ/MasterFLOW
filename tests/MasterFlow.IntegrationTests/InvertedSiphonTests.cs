using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Inverted siphon (ASCE MOP 60, Ten States): full-flow losses, staged barrels, velocities, air jumper.</summary>
public class InvertedSiphonTests
{
    private static readonly InvertedSiphonTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static string Sample => Tool.SampleInput(Us);
    private static ToolResult Run(string input) => Tool.Run(input, Us);

    [Fact]
    public void Sample_RunsClean_AndSiIsRefused()
    {
        var r = Run(Sample);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.DoesNotContain(r.Messages, m => m.Severity == MessageSeverity.Warning);
        Assert.Equal(2, r.Charts.Count);
        Assert.False(Tool.Run(Sample, new DesignToolContext(UnitSystem.SI)).Succeeded);
    }

    [Fact]
    public void LossCoefficient_ByHand()
    {
        // 10 in., 260 ft, n 0.013, K 1.9: 1.9/64.348 + 0.013²·260/(1.486²·(10/48)^(4/3)).
        double r = 10.0 / 12 / 4;
        double expect = 1.9 / (2 * 32.174) + 0.013 * 0.013 * 260 / (1.486 * 1.486 * Math.Pow(r, 4.0 / 3));
        Assert.Equal(expect, InvertedSiphonTool.LossCoefficient(10, 260, 0.013, 1.9), 12);
        double q = InvertedSiphonTool.Capacity(10, 3.0, 260, 0.013, 1.9);
        double v = q / InvertedSiphonTool.Area(10);
        Assert.Equal(3.0, expect * v * v, 9);
    }

    [Fact]
    public void Staging_FirstBarrelAlone_ThenWeirOverflow()
    {
        double[] c = [1.25, 2.0];
        double[] crest = [0, 2.0];
        // Below the crest only barrel 1 flows; above it barrel 2 takes the weir overflow 3·3·(h − 2)^1.5.
        var f = InvertedSiphonTool.Flows(1.5, c, crest, 9);
        Assert.Equal(1.25 * Math.Sqrt(1.5), f[0], 12);
        Assert.Equal(0, f[1]);
        f = InvertedSiphonTool.Flows(2.25, c, crest, 9);
        Assert.Equal(9 * Math.Pow(0.25, 1.5), f[1], 12);
        // Weir overflow cannot exceed the barrel's own full flow.
        Assert.Equal(2.0 * Math.Sqrt(10), InvertedSiphonTool.Flows(10, c, crest, 9)[1], 12);
        double h = InvertedSiphonTool.HeadFor(3.0, c, crest, 9);
        Assert.Equal(3.0, InvertedSiphonTool.Total(h, c, crest, 9), 9);
    }

    [Fact]
    public void Design_FirstBarrelCarriesAverageAtCleansingVelocity()
    {
        var r = Run(Sample);
        var barrels = r.Tables[0].Rows;
        Assert.Equal(10.0, barrels[0][1]);                 // largest size with 3 ft/s at 820 gpm (12 in. gives 2.3 ft/s)
        Assert.Equal(12.0, barrels[1][1]);
        Assert.Equal(8.0, barrels[2][1]);                  // wet-weather barrel
        var avg = r.Tables[1].Rows.First(x => (string)x[0]! == "average");
        Assert.Equal(820.0, avg[3]);                        // all of it in barrel 1
        Assert.True((double)avg[4]! >= 3.0);
        Assert.Equal(0.0, avg[5]);
        var peak = r.Tables[1].Rows.First(x => (string)x[0]! == "peak");
        Assert.True((double)peak[6]! >= 3.0);              // barrel 2 reaches 3 ft/s at the daily peak
        Assert.Equal(0.0, peak[7]);                         // barrel 3 still dry
    }

    [Fact]
    public void Shortfalls_AreFlagged()
    {
        Assert.Contains(Run(Sample + "\n[Barrels]\nD (in.)\n12").Messages, m => m.Code == "W-IS-001");
        Assert.Contains(Run(Sample + "\n[Barrels]\nD (in.)\n6\n18").Messages, m => m.Code == "W-IS-002");
        Assert.Contains(Run(Sample + "\n[Barrels]\nD (in.)\n15\n15").Messages, m => m.Code == "W-IS-003");
        Assert.Contains(Run(Sample + "\n[Barrels]\nD (in.)\n10\n24").Messages, m => m.Code == "W-IS-004");
        Assert.Contains(Run(Sample.Replace("upstream water surface = 3406.40", "upstream water surface = 3404.40")).Messages, m => m.Code is "W-IS-005" or "W-IS-003");
        Assert.Contains(Run(Sample + "\n[Barrels]\nD (in.)\n8\n8").Messages, m => m.Code == "W-IS-005");
        Assert.Contains(Run(Sample.Replace("manning n = 0.013", "manning n = 0.011")).Messages, m => m.Code == "W-IS-007");
        Assert.Contains(Run(Sample + "\nmax air loss = 0.05").Messages, m => m.Code == "W-IS-006");
    }

    [Fact]
    public void AirJumper_HalfTheIncomingSewer_RoundedUp()
    {
        var r = Run(Sample);
        var air = r.Tables[2].Rows;
        Assert.Equal(10.0, air[0][1]);                     // 18 in. sewer → 9 → 10 in.
        Assert.Equal(Math.Round(3000 / 448.831 * 60, 0), air[2][1]);
    }

    [Fact]
    public void BadInput_IsRefused()
    {
        Assert.Contains(Run(Sample.Replace("upstream water surface = 3406.40", "")).Messages, m => m.Code == "E-IS-002");
        Assert.Contains(Run(Sample.Replace("peak flow = 2000", "peak flow = 500")).Messages, m => m.Code == "E-IS-003");
        Assert.Contains(Run(Sample.Replace("upstream water surface = 3406.40", "upstream water surface = 3403.00")).Messages, m => m.Code == "E-IS-003");
        Assert.Contains(Run(Sample + "\n[Barrels]\nD (in.)\n0").Messages, m => m.Code == "E-IS-004");
    }
}
