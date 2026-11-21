using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Application.WaterQuality;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Multi-stage outlet designer: each designed stage, re-routed independently, meets its allowable release.</summary>
public class OutletDesignerTests
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private const double G = 32.174;

    [Fact]
    public void Sample_EveryStormAtOrBelowAllowable_AndGoverningStormHitsIt()
    {
        var tool = new OutletDesignerTool();
        var r = tool.Run(tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var rows = r.Tables.Single(t => t.Title == "Routing results").Rows;
        foreach (var row in rows) Assert.True((double)row[2]! <= (double)row[3]! * 1.005, $"{row[0]} releases {row[2]} > {row[3]}");
        // The 10-yr and 100-yr limits govern the sample's two openings: their releases equal the allowable within 0.5 %.
        Assert.Equal(1.0, (double)rows[1][2]! / (double)rows[1][3]!, 2);
        Assert.Equal(1.0, (double)rows[2][2]! / (double)rows[2][3]!, 2);
        Assert.All(r.Tables.Single(t => t.Title == "Criteria checks").Rows.Where(x => ((string)x[0]!).Contains("release")), x => Assert.Equal("OK", x[3]));
        Assert.Equal(3, r.Charts.Count);
        Assert.Single(r.Figures);
    }

    [Fact]
    public void WqcvAndPlate_MatchHandCalculation()
    {
        // SWQM 2009 §2.2.2, I = 0.55, 40 h (a = 1.0): 1.16 (0.91·0.166375 − 1.191·0.3025 + 0.78·0.55) = 0.2553 in.; over 30 ac = 0.6383 ac-ft.
        Assert.Equal(0.2553, Wq.WqcvRapidCity(0.55, 1.0), 4);
        var tool = new OutletDesignerTool();
        var r = tool.Run(tool.SampleInput(Us), Us);
        var plate = r.Tables[0].Rows[0];
        Assert.Contains("0.638 ac-ft", (string)plate[5]!);
        var checks = r.Tables.Single(t => t.Title == "Criteria checks").Rows;
        Assert.Equal(40, (double)checks.Single(x => (string)x[0]! == "WQCV drain time")[2]!, 3);
        Assert.True((double)checks.Single(x => (string)x[0]! == "WQCV half drain")[2]! >= 12);
    }

    [Fact]
    public void SingleWeirStage_ReroutedIndependently_ReleasesTheAllowable()
    {
        // Prismatic pond (100 × 100 ft), triangular inflow 0 → 40 cfs at 30 min → 0 at 90 min, one weir at the bottom.
        string input = """
            time step = 0.5
            allowable Q100 = 15
            [Stage storage]
            Elevation (ft)	Area (ft²)
            100	10000
            110	10000
            [Stages]
            Storm	Type	Invert (ft)	Height (ft)	C
            100	weir	100	 	3.0
            [Inflow hydrograph]
            Time (min)	100-yr (cfs)
            0	0
            30	40
            90	0
            """;
        var r = new OutletDesignerTool().Run(input, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        string size = (string)r.Tables[0].Rows.Single(x => (string)x[0]! == "100-yr")[4]!;
        double length = double.Parse(size.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        var pond = new Pond([(100, 10000), (110, 10000)], [new PondOutlet("w", "weir", 100, length, 0, 3.0)], G, true);
        var hyd = Hydrograph.Resample([(0, 0), (30, 40), (90, 0)], 0.5);
        var rr = pond.Route(hyd, 30);
        Assert.InRange(rr.PeakOutflow, 15 * 0.985, 15 * 1.005);   // the length is reported to 0.01 ft
        // Mass balance at the peak: outflow peaks where it crosses the falling inflow, so I(t_peak) = O_peak.
        double tPk = rr.TimeOfPeakOutflow / 60;
        Assert.InRange(40 * (90 - tPk) / 60, 15 - 0.5, 15 + 0.5);
    }

    [Fact]
    public void MissingAllowable_SiUnits_AndLicense()
    {
        var r = new OutletDesignerTool().Run("area = 5\ntc = 10\nC2 = 0.5\n[Stage storage]\nElevation\tArea\n0\t100\n5\t500\n[Stages]\nStorm\tType\n2\torifice\n", Us);
        Assert.Contains(r.Messages, m => m.Code == "E-OD-012");
        Assert.False(new OutletDesignerTool().Run("", new(UnitSystem.SI)).Succeeded);
        Assert.NotNull(PremiumFeatures.ForTool("outlet-designer"));
    }
}
