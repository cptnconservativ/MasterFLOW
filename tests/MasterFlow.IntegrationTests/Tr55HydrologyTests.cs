using MasterFlow.Application.Hydrology;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>TR-55 hydrology tool, checked against the TR-55 (1986) worked examples and hand calculations.</summary>
public class Tr55HydrologyTests
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);

    private static ToolResult Run(string input)
    {
        var r = new Tr55HydrologyTool().Run(input, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact]
    public void Example41_GraphicalPeak_MatchesTr55()
    {
        // TR-55 Example 4-1: 250 ac, CN 75, Tc 1.53 h, Type II, P = 6.0 in, Fp = 1 → Ia = 0.667, Ia/P = 0.11,
        // Q = 3.28 in, qu = 270 csm/in (read from Exhibit 4-II), qp = 345 cfs.
        double q = Losses.CurveNumberRunoff(6.0, 75);
        Assert.Equal(3.28, q, 2);
        double qu = Tr55Tables.UnitPeak("II", 1.53, 0.667 / 6.0);
        Assert.InRange(qu, 270 * 0.98, 270 * 1.02);
        var r = Run("""
            storms = 25
            P25 = 6.0
            hydrograph storm = none
            criteria = none
            [Land cover]
            Basin	Cover	HSG	Area (ac)	CN
            W	 	 	250	75
            [Basins]
            Basin	Tc (min)
            W	91.8
            """);
        var row = r.Tables.Single(t => t.Title.StartsWith("Worksheet 4")).Rows.Single();
        Assert.Equal(0.667, (double)row[5]!, 3);
        Assert.InRange((double)row[^1]!, 345 * 0.97, 345 * 1.03);
    }

    [Fact]
    public void Example31_TimeOfConcentration_MatchesTr55()
    {
        // TR-55 Example 3-1: sheet 100 ft, n 0.24, s 0.01, P2 3.6 → 0.30 h; shallow unpaved 1400 ft at 0.01 → 0.24 h;
        // channel a 27 ft², pw 28.2 ft, n 0.05, s 0.005, 7300 ft → V 2.05 ft/s, 0.99 h; Tc = 1.53 h.
        var r = Run($"""
            storms = 2
            P2 = 3.6
            hydrograph storm = none
            [Land cover]
            Basin	Cover	HSG	Area (ac)
            W	pasture good	C	250
            [Tc flow paths]
            Basin	Type	Length (ft)	Slope (ft/ft)	n	P2 (in)	R (ft)	Surface
            W	sheet	100	0.01	0.24	3.6
            W	shallow	1400	0.01				unpaved
            W	channel	7300	0.005	0.05		{27 / 28.2}
            """);
        var rows = r.Tables.Single(t => t.Title.StartsWith("Worksheet 3")).Rows;
        Assert.Equal(0.30, (double)rows[0][5]!, 2);
        Assert.Equal(0.24, (double)rows[1][5]!, 2);
        Assert.Equal(2.05, (double)rows[2][4]!, 2);
        Assert.Equal(0.99, (double)rows[2][5]!, 2);
        Assert.Equal(1.53, (double)rows[3][5]!, 2);
    }

    [Fact]
    public void CurveNumbers_TableLookup_CompositeAndImperviousAdjustments()
    {
        Assert.Equal(83, Tr55Tables.Find("Residential 1/4")!.For('C'));
        Assert.Equal(55, Tr55Tables.Find("woods-good")!.For('B'));
        Assert.True(double.IsNaN(Tr55Tables.Find("sagebrush fair")!.For('A')));
        // Fig. 2-3: CNp 61, 40 % connected impervious → 61 + 0.4·37 = 75.8; Fig. 2-4: 20 %, half unconnected → 61 + 0.2·37·0.75 = 66.55.
        Assert.Equal(75.8, Tr55Tables.CompositeImpervious(61, 40, 0), 9);
        Assert.Equal(66.55, Tr55Tables.CompositeImpervious(61, 20, 0.5), 9);
        Assert.Equal(75.8, Tr55Tables.CompositeImpervious(61, 40, 0.5), 9);   // ≥ 30 %: no unconnected credit
        // Example 2-2 cover: 1/2-ac lots (B 70 on 75 ac, C 80 on 100 ac) and open space good C 74 on 75 ac → (5250 + 8000 + 5550)/250 = 75.2.
        var r = Run("""
            storms = 25
            P25 = 6
            hydrograph storm = none
            [Land cover]
            Basin	Cover	HSG	Area (ac)
            W	residential 1/2	B	75
            W	residential 1/2	C	100
            W	open space good	C	75
            [Basins]
            Basin	Tc (min)
            W	91.8
            """);
        var comp = r.Tables[0].Rows.Single(x => (string)x[1]! == "Composite");
        Assert.Equal(75.2, (double)comp[3]!, 1);
        Assert.Equal(1.0, Tr55Tables.PondFactor(0)); Assert.Equal(0.87, Tr55Tables.PondFactor(1), 9); Assert.Equal(0.72, Tr55Tables.PondFactor(5), 9);
        Assert.True(double.IsNaN(Tr55Tables.PondFactor(6)));
    }

    [Fact]
    public void UnitHydrograph_ConservesRunoffVolume_AndPeakIsNearGraphical()
    {
        var tool = new Tr55HydrologyTool();
        var r = tool.Run(tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded);
        var cn = r.Tables[0].Rows.Where(x => (string)x[1]! == "Composite").ToDictionary(x => (string)x[0]!, x => (double)x[4]!);
        foreach (var row in r.Tables.Single(t => t.Title == "Unit hydrograph results").Rows)
        {
            double area = cn[(string)row[0]!], vol = (double)row[4]!, qIn = (double)row[5]!;
            Assert.Equal(qIn * area / 12, vol, 2);                // ac-ft within 0.005
            Assert.InRange(Math.Abs((double)row[7]!), 0, 35);    // convolution vs. Exhibit 4 within 35 %
        }
        Assert.Equal(3, r.Charts.Count);
    }

    [Fact]
    public void Limits_AreReported()
    {
        var r = new Tr55HydrologyTool().Run("""
            storms = 2
            [Land cover]
            Basin	Cover	HSG	Area (ac)
            W	sagebrush good	A	10
            """, Us);
        Assert.Contains(r.Messages, m => m.Code == "E-TR-008");
        r = new Tr55HydrologyTool().Run("""
            storms = 2
            hydrograph storm = none
            [Land cover]
            Basin	Cover	HSG	Area (ac)
            W	woods good	A	300
            [Basins]
            Basin	Tc (min)	Pond and swamp (%)
            W	3	8
            """, Us);
        Assert.True(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == "I-TR-004");    // Tc < 0.1 h
        Assert.Contains(r.Messages, m => m.Code == "W-TR-006");    // pond > 5 %
        Assert.Contains(r.Messages, m => m.Code == "W-TR-008");    // Ia/P > 0.5 (CN 30, P 2.0)
        Assert.Contains(r.Messages, m => m.Code == "I-TR-005");    // > 160 ac in Rapid City
        Assert.False(new Tr55HydrologyTool().Run("storms = 2\n[Land cover]\nBasin\tCN\tArea\nW\t80\t5\n", new(UnitSystem.SI)).Succeeded);
    }
}
