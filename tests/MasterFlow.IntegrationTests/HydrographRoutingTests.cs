using MasterFlow.Application.Hydrology;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using Xunit.Abstractions;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Hydrograph network routing (premium). Reference values from validation/hand_calcs_hydrograph.py (independent code):
/// TR-55 Example 3-1, CN runoff, a 100 ac CN 80 subbasin under the IDCM 100-yr 2-h storm, and a diffusion-wave solution
/// of the same hydrograph through a 10,000 ft channel.
/// </summary>
public class HydrographRoutingTests(ITestOutputHelper output)
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private readonly HydrographRoutingTool _tool = new();

    [Fact]
    public void Tr55_Example3_1()
    {
        // TR-55 Worksheet 3 example: 0.30 + 0.24 + 0.99 = 1.53 h (rounded in TR-55).
        Assert.Equal(0.2959, Tr55.SheetHours(0.24, 100, 3.6, 0.01), 4);
        Assert.Equal(0.2410, 1400 / Tr55.ShallowVelocity(false, 0.01) / 3600, 4);
        Assert.Equal(0.9906, 7300 / Tr55.ChannelVelocity(0.05, 27 / 28.2, 0.005) / 3600, 4);
        Assert.Equal(1.6, Tr55.ShallowVelocity(false, 0.01), 1);   // TR-55 Figure 3-1 reading
    }

    [Fact]
    public void SoilTables_RawlsByDefault_IdcmByChoice_AndBothShownWhereTheyDiffer()
    {
        // Rawls et al. (1983): loam 0.34 cm/h, silt loam 0.65 cm/h; IDCM Table 4-4 prints them the other way round.
        Assert.Equal(0.134, Losses.RawlsSoils["loam"].Ks, 3);
        Assert.Equal(0.256, Losses.RawlsSoils["silt loam"].Ks, 3);
        Assert.Equal(0.256, Losses.RapidCitySoils["loam"].Ks, 3);
        Assert.Equal(0.134, Losses.RapidCitySoils["silt loam"].Ks, 3);
        foreach (var k in Losses.RawlsSoils.Keys.Where(k => k is not ("loam" or "silt loam")))
            Assert.Equal(Losses.RawlsSoils[k], Losses.RapidCitySoils[k]);

        const string body = "storms = 10\nrainfall = rapid-city\n[Subbasins]\nName\tNode\tArea (ac)\tSoil\tTc (min)\nA\tOUT\t10\tloam\t15\n";
        var rawls = _tool.Run(body, Us);
        var idcm = _tool.Run("soil table = idcm\n" + body, Us);
        Assert.True(rawls.Succeeded && idcm.Succeeded, string.Join("\n", rawls.Messages.Concat(idcm.Messages).Select(m => m.Text)));
        Assert.Contains(rawls.Messages, m => m.Code == "I-HR-014" && m.Text.Contains("0.134") && m.Text.Contains("0.256") && m.Text.Contains("Rawls et al. (1983) (used)"));
        Assert.Contains(idcm.Messages, m => m.Code == "I-HR-014" && m.Text.Contains("IDCM Table 4-4 (used)"));
        // Lower conductivity (Rawls loam) gives more runoff than the IDCM loam value.
        double Vol(ToolResult r) => (double)r.Tables.First(t => t.Title.StartsWith("Subbasin")).Rows[0][9]!;
        Assert.True(Vol(rawls) > Vol(idcm), $"{Vol(rawls)} vs {Vol(idcm)}");
    }

    [Fact]
    public void CurveNumber_ClosedForm()
    {
        Assert.Equal(20.25 / 7, Losses.CurveNumberRunoff(5, 80), 9);
        Assert.Equal(0, Losses.CurveNumberRunoff(0.5, 80));          // P = Ia exactly
        Assert.Equal(3.0, Losses.CurveNumberRunoff(3, 100), 9);       // CN 100: all rain runs off
    }

    [Fact]
    public void FrequencyStorm_MatchesTable4_2_AtEveryTabulatedDuration()
    {
        var d = DesignStorms.RapidCityDepths[100];
        var blocks = DesignStorms.FrequencyStormBlocks(DesignStorms.RapidCityDurations, d, 120, 5, 0.25);
        Assert.Equal(3.06, blocks.Sum(), 9);
        Assert.Equal(0.79, blocks.Max(), 9);
        Assert.Equal(6, Array.IndexOf(blocks, blocks.Max()));          // first quartile of 24 blocks
        // The wettest 15 and 60 minutes of the storm equal the table (nested blocks around the peak).
        double Best(int m) => Enumerable.Range(0, blocks.Length - m + 1).Max(i => blocks.Skip(i).Take(m).Sum());
        Assert.Equal(1.57, Best(3), 9);
        Assert.Equal(2.95, Best(12), 9);
    }

    [Fact]
    public void GreenAmpt_ConservesWater_AndImperviousRunsOff()
    {
        var rain = Enumerable.Range(0, 121).Select(k => k * 0.02).ToArray();    // 2.4 in over 2 h (1.2 in/h)
        var none = Losses.GreenAmpt(rain, 1 / 60.0, 0.134, 6.567, 0.2, 0.35, 1.0);
        Assert.Equal(2.4, none[^1], 9);
        var perv = Losses.GreenAmpt(rain, 1 / 60.0, 0.134, 6.567, 0.2, 0.35, 0);
        Assert.InRange(perv[^1], 0, 2.4 - 0.35);
        Assert.True(perv.Zip(perv.Skip(1)).All(x => x.Second >= x.First - 1e-12));
        // Ks larger than any rain rate: nothing runs off.
        Assert.Equal(0, Losses.GreenAmpt(rain, 1 / 60.0, 5, 2, 0.2, 0.35, 0)[^1], 9);
        // Finer step gives the same answer (exact Green–Ampt within a step).
        var fine = Losses.GreenAmpt(Enumerable.Range(0, 1201).Select(k => k * 0.002).ToArray(), 1 / 600.0, 0.134, 6.567, 0.2, 0.35, 0);
        Assert.Equal(fine[^1], perv[^1], 2);
    }

    [Fact]
    public void UnitHydrograph_PeakRateFactor484_AndUnitVolume()
    {
        Assert.Equal(484 * 1.0 / 0.5, ScsUnitHydrograph.PeakPerInch(1, 0.5), 9);
        var u = ScsUnitHydrograph.Ordinates(640, 0.6, 0.05);
        Assert.Equal(640 * 43560 / 12.0, u.Sum() * 0.05 * 3600, 3);
        Assert.InRange(u.Max(), 0.97 * 484 / 0.625, 1.03 * 484 / 0.625);
    }

    private const string Single = """
        storms = 100
        rainfall = rapid-city
        time step = 1
        simulation = 480
        [Subbasins]
        Name	Node	Area (ac)	CN	Tc (min)
        A	J1	100	80	30
        """;

    [Fact]
    public void Subbasin_MatchesIndependentConvolution()
    {
        var r = _tool.Run(Single, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        var row = r.Tables.Single(t => t.Title == "Subbasin runoff").Rows.Single();
        output.WriteLine(DesignTools.ToText(r));
        Assert.Equal(3.06, (double)row[7]!, 6);
        Assert.Equal(1.2952, (double)row[8]!, 4);
        Assert.Equal(10.7931, (double)row[9]!, 3);
        Assert.Equal(195.068, (double)row[10]!, 2);
        Assert.Equal(57, (double)row[11]!, 6);
    }

    [Fact]
    public void Reach_MuskingumCunge_AgreesWithDiffusionWave()
    {
        var r = _tool.Run(Single + """

            [Reaches]
            Name	From	To	Length (ft)	Slope	Shape	Size (ft)	Side slope	n
            Ch	J1	OUT	10000	0.002	trapezoid	20	0	0.035
            """, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        output.WriteLine(DesignTools.ToText(r));
        var row = r.Tables.Single(t => t.Title.StartsWith("Reaches")).Rows.Single();
        double peakOut = (double)row[8]!;
        // validation/hand_calcs_hydrograph.py, finite-volume diffusion wave: 142.5 cfs at 102 min.
        Assert.InRange(peakOut, 142.5 * 0.96, 142.5 * 1.04);
        Assert.InRange((double)row[10]!, 102 - 57 - 3, 102 - 57 + 3);        // peak lag
        Assert.InRange((double)row[13]!, -1.5, 1.5);                      // volume error %
    }

    [Fact]
    public void ZeroLengthReach_PassesHydrographThrough()
    {
        var g = new ReachGeometry(true, 3, 0, 0.013, 0.01);
        var inflow = Enumerable.Range(0, 200).Select(k => 40 * Math.Exp(-Math.Pow((k - 50) / 15.0, 2))).ToArray();
        var res = MuskingumCunge.Route(inflow, 60, 1, g);
        Assert.Equal(inflow.Max(), res.Outflow.Max(), 1);
        Assert.InRange(res.VolumeErrorPercent, -0.5, 0.5);
    }

    [Fact]
    public void Network_SampleRuns_WithPondAndApply()
    {
        var r = _tool.Run(_tool.SampleInput(Us), Us);
        output.WriteLine(DesignTools.ToText(r));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Contains(r.Tables, t => t.Title.StartsWith("Ponds"));
        Assert.Contains(r.Tables, t => t.Title.StartsWith("Time of concentration"));
        // Mass balance at the outfall: volume out ≈ total runoff less what the pond still holds (simulation ends at 480 min).
        foreach (var st in new[] { "2-yr", "10-yr", "100-yr" })
        {
            double runoff = r.Tables.Single(t => t.Title == "Subbasin runoff").Rows.Where(x => (string)x[0]! == st).Sum(x => (double)x[9]!);
            double outVol = r.Tables.Single(t => t.Title == "Nodes").Rows.Single(x => (string)x[0]! == st && (string)x[1]! == "OUT").Get(6);
            Assert.InRange(outVol, 0.5 * runoff, 1.01 * runoff);
        }
    }

    [Fact]
    public void BadInput_IsReported_NotEstimated()
    {
        Assert.False(_tool.Run(Single.Replace("CN	Tc", "CN	Tc").Replace("80	30", "120	30"), Us).Succeeded);
        Assert.False(_tool.Run(Single.Replace("100	80", "-5	80"), Us).Succeeded);
        Assert.False(_tool.Run(Single.Replace("storms = 100", "storms = 25"), Us).Succeeded);   // not in Table 4-2
        Assert.False(_tool.Run(Single, new DesignToolContext(UnitSystem.SI)).Succeeded);
        Assert.False(_tool.Run(Single + "\n[Reaches]\nName\tFrom\tTo\tLength\tSlope\tShape\tSize\tSide\tn\nA\tJ1\tX\t100\t0.01\tcircle\t2\t\t0.013\nB\tX\tJ1\t100\t0.01\tcircle\t2\t\t0.013\n", Us).Succeeded);  // loop
        Assert.False(_tool.Run(Single.Replace("rainfall = rapid-city", "rainfall = rapid-city\nstorm duration = 30"), Us).Succeeded);
    }

    [Fact]
    public void TypeII_AndTable_Rainfall()
    {
        var r = _tool.Run(Single.Replace("rainfall = rapid-city", "rainfall = type-ii\nP100 = 5").Replace("simulation = 480", "simulation = 1800"), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Equal(5, (double)r.Tables[0].Rows[0][1]!, 9);
        Assert.Equal(Losses.CurveNumberRunoff(5, 80), (double)r.Tables[1].Rows[0][8]!, 6);
        var t = _tool.Run("time step = 1\nsimulation = 300\nrainfall = table\n[Rainfall]\nTime (min)\t100-yr (in)\n0\t0\n60\t2\n[Subbasins]\nName\tNode\tArea\tCN\tTc\nA\tJ\t10\t98\t10\n", Us);
        Assert.True(t.Succeeded, string.Join("\n", t.Messages));
        Assert.Equal(2, (double)t.Tables[0].Rows[0][1]!, 9);
    }

    [Fact]
    public void WithNetwork_PipesAreReaches_AndApplyWritesPeaks()
    {
        var net = new StormNetwork
        {
            Structures = [new() { Name = "MH1" }, new() { Name = "MH2" }, new() { Name = "OUT" }],
            Pipes =
            [
                new() { Name = "P1", From = "MH1", To = "MH2", Rise = 2, Span = 2, Length = 300, UpstreamInvert = 100, DownstreamInvert = 98, Discharge = double.NaN },
                new() { Name = "P2", From = "MH2", To = "OUT", Rise = 2.5, Span = 2.5, Length = 300, UpstreamInvert = 97.8, DownstreamInvert = 96, Discharge = double.NaN },
            ],
        };
        var ctx = new DesignToolContext(UnitSystem.USCustomary, net);
        var r = _tool.Run("storms = 2, 10\n[Subbasins]\nName\tNode\tArea\tCN\tTc\nA\tMH1\t5\t85\t12\nB\tMH2\t4\t88\t9\n", ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.NotNull(r.UpdatedNetwork);
        var p1 = r.UpdatedNetwork!.Pipes[0]; var p2 = r.UpdatedNetwork.Pipes[1];
        Assert.True(p1.Discharge > 0 && p2.Discharge > p1.Discharge);
        Assert.True(p2.MinorDischarge > 0 && p2.MinorDischarge < p2.Discharge);
        Assert.Equal("2-yr", r.UpdatedNetwork.MinorStormName);
        // Non-coincident peaks: the routed peak in P2 is less than the sum of the two subbasin peaks.
        var subs = r.Tables.Single(t => t.Title == "Subbasin runoff").Rows.Where(x => (string)x[0]! == "10-yr").Sum(x => (double)x[10]!);
        Assert.True(p2.Discharge < subs + 1e-9);
    }
}

internal static class RowExt
{
    public static double Get(this IReadOnlyList<object?> row, int i) => (double)row[i]!;
}
