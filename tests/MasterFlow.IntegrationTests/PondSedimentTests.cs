using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Pond sediment: hand calculations of the load, Brune trap efficiency, Lara–Pemberton/Miller density and clean-out years.</summary>
public class PondSedimentTests
{
    private static readonly PondSedimentTool Tool = new();

    private static ToolResult Run(string input)
    {
        var r = Tool.Run(input, new DesignToolContext(UnitSystem.USCustomary));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact]
    public void Sample_Runs() => Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));

    [Fact]
    public void SimpleMethodRunoffAndTssLoad()
    {
        // 40 ac, 45 % impervious: Rv = 0.05 + 0.405 = 0.455; 17 in → 0.455 × 17 × 40 / 12 = 25.783 ac-ft.
        Assert.Equal(25.7833, PondSedimentTool.AnnualRunoff(40, 45, 17), 3);
        // 120 mg/L on 25.783 ac-ft: 120 × 25.783 × 2.7194 / 2000 = 4.2069 t.
        Assert.Equal(4.2069, PondSedimentTool.TssTons(120, 25.7833), 3);
    }

    [Fact]
    public void BruneDendyFit()
    {
        Assert.Equal(0.97, PondSedimentTool.BruneTrap(1), 6);                     // 0.19^0 = 1
        Assert.Equal(Math.Pow(0.97, 1 / 0.19), PondSedimentTool.BruneTrap(0.1), 6);   // 0.19^-1
        Assert.Equal(Math.Pow(0.97, 1 / 0.0361), PondSedimentTool.BruneTrap(0.01), 6); // 0.19^-2 = 27.7 → 0.43
        Assert.InRange(PondSedimentTool.BruneTrap(0.01), 0.42, 0.44);
        Assert.Equal(Math.Pow(0.97, 0.0361), PondSedimentTool.BruneTrap(100), 6);   // approaches 100 % for large pools
    }

    [Fact]
    public void LaraPembertonAndMiller()
    {
        // Submerged, 30/50/20 sand/silt/clay: W1 = 0.3·97 + 0.5·70 + 0.2·26 = 69.3; K = 0.5·5.7 + 0.2·16 = 6.05.
        var lp = PondSedimentTool.LaraPemberton(1);
        double w1 = 0.3 * lp.Ws + 0.5 * lp.Wm + 0.2 * lp.Wc, k = 0.3 * lp.Ks + 0.5 * lp.Km + 0.2 * lp.Kc;
        Assert.Equal(69.3, w1, 9);
        Assert.Equal(6.05, k, 9);
        // T = 30: 30/29·ln 30 − 1 = 2.5184; W = 69.3 + 0.4343 × 6.05 × 2.5184 = 75.917.
        Assert.Equal(75.917, PondSedimentTool.MillerAverage(w1, k, 30), 2);
        Assert.Equal(w1, PondSedimentTool.MillerAverage(w1, k, 1));
    }

    [Fact]
    public void DryPond_NoConsolidation_YearsToTriggerIsLinear()
    {
        // Empty (type 4) all sand: 97 lb/ft³, K = 0. Given TE 50 %, 10 t/yr from [Loads] → 5 t/yr trapped = 103.09 ft³/yr.
        // Pool storage 1000 ft³, trigger 50 % = 500 ft³ → 4.85 years.
        var r = Run("""
            drainage area = 10
            annual precipitation = 17
            trap efficiency = 50
            operation = empty
            sand = 100
            silt = 0
            clay = 0
            sediment storage = 1000
            pool area = 2000
            design life = 10
            [Loads]
            Name, area, yield
            Developed, 10, 1
            """);
        var row = r.Tables[1].Rows[0];
        Assert.Equal(500.0, (double)row[3]!);
        Assert.Equal(500 / (5 * 2000 / 97.0), (double)row[4]!, 1);
        Assert.Equal(103.0, (double)row[6]!, 0);
        Assert.Equal(103.09 / 2000 * 12, (double)row[7]!, 2);
        Assert.Contains(r.Messages, m => m.Code == "W-PSD-004");
    }

    [Fact]
    public void ForebayShareAndConstruction()
    {
        var r = Run("""
            drainage area = 10
            annual precipitation = 17
            trap efficiency = 100
            operation = empty
            sand = 100
            silt = 0
            clay = 0
            forebay volume = 970
            forebay capture = 50
            sediment storage = 97000
            construction area = 5
            construction yield = 2
            construction years = 1
            [Loads]
            Name, area, yield
            Developed, 10, 1
            """);
        // Developed: 10 t/yr, half to the forebay = 5 t = 103.09 ft³/yr; trigger 485 ft³ → 4.7 years.
        // From today: year 1 adds 10 t of construction load (5 t to the forebay) → 10 t in year 1, then 5 t/yr: 485 ft³ = 23.5 t → 1 + 13.5/5 = 3.7 years.
        var fb = r.Tables[1].Rows[0];
        Assert.Equal(4.7, (double)fb[5]!, 1);
        Assert.Equal(3.7, (double)fb[4]!, 1);
        Assert.Contains(r.Summary, s => s.Contains("once developed"));
    }

    [Fact]
    public void BruneTrapUsedFromPoolVolume()
    {
        var r = Run("drainage area = 40\nimpervious = 45\nannual precipitation = 17\ntss = 120\npool volume = 2.5781\nsediment storage = 5000\n");
        var te = r.Tables[0].Rows.First(x => (string)x[0]! == "Trap efficiency");
        Assert.Equal(PondSedimentTool.BruneTrap(0.1) * 100, (double)te[1]!, 1);
    }

    [Fact]
    public void Errors()
    {
        var r = Tool.Run("drainage area = 10\nannual precipitation = 17\nsand = 50\nsilt = 10\nclay = 10\noperation = wet\n", new DesignToolContext(UnitSystem.USCustomary));
        foreach (var code in new[] { "E-PSD-004", "E-PSD-007", "E-PSD-009", "E-PSD-010", "E-PSD-013" })
            Assert.Contains(r.Messages, m => m.Code == code);
    }
}
