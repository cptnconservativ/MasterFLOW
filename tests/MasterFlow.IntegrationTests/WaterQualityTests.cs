using MasterFlow.Application.Tools;
using MasterFlow.Application.WaterQuality;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Water quality tool against validation/hand_calcs_water_quality_output.txt (independent Python: explicit time stepping
/// for the orifice plate, Darcy and infiltration by hand), plus SI, edge cases and bad input.
/// </summary>
public class WaterQualityTests
{
    private static readonly WaterQualityTool Tool = new();
    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => Tool.Run(input, new DesignToolContext(u));
    private static ToolResult Sample(UnitSystem u = UnitSystem.USCustomary) => Run(Tool.SampleInput(new DesignToolContext(u)), u);
    private static object? Item(ToolResult r, string table, string item) => r.Tables.Single(t => t.Title == table).Rows.Single(x => (string)x[0]! == item)[1];

    /// <summary>The MHFD sample (criteria = mhfd), checked against the first part of the hand calculations.</summary>
    private const string MhfdSample = """
        criteria = mhfd
        regional depth = 0.43
        [Subbasins]
        Name	Area (ac)	Imperviousness (%)	BMP
        A1	6.2	65	Pond 1
        A2	3.4	45	Pond 1
        B1	1.1	90	Rain garden
        C1	0.8	80	Sand filter
        D1	0.5	95	Trench
        E1	2.0	10
        [BMP Pond 1]
        type = edb
        wqcv drain time = 40
        sediment allowance = 20
        row spacing = 4
        orifice coefficient = 0.6
        drained fraction = 99
        Elevation	Area
        100.0	4000
        101.0	6500
        102.0	9200
        103.0	12000
        104.0	15000
        [BMP Rain garden]
        type = bioretention
        ponding depth = 12
        media depth = 18
        permeability = 0.5
        filter drain time = 48
        [BMP Sand filter]
        type = sand filter
        ponding depth = 24
        media depth = 18
        permeability = 3.5
        filter drain time = 40
        [BMP Trench]
        type = infiltration
        infiltration rate = 1.0
        safety factor = 2
        void ratio = 0.4
        depth = 3
        maximum drain time = 72
        """;

    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(1.0, 1.0, 0.5)]                    // 0.91 − 1.19 + 0.78
    [InlineData(0.5, 1.0, 0.20625)]
    [InlineData(0.5, 0.8, 0.165)]
    public void Wqcv_Equation31(double i, double a, double expected) => Assert.Equal(expected, Wq.WqcvInches(i, a), 9);

    [Fact]
    public void DrainCoefficient_Table32_AndRegionalDepth()
    {
        Assert.Equal(0.8, Wq.DrainCoefficient(12), 9);
        Assert.Equal(0.9, Wq.DrainCoefficient(24), 9);
        Assert.Equal(1.0, Wq.DrainCoefficient(40), 9);
        Assert.Equal(0.95, Wq.DrainCoefficient(32), 9);
        Assert.Equal(0.8, Wq.DrainCoefficient(6), 9);
        Assert.Equal(1.0, Wq.DrainCoefficient(72), 9);
        Assert.Equal(2 * Wq.WqcvInches(0.5, 1), Wq.WqcvInches(0.5, 1, 0.86), 9);      // Eq. 3-2 scales by d6 / 0.43
        Assert.Equal(0.5, Wq.WqcvInches(1.5, 1), 9);                                    // clamped to 100 %
    }

    [Fact]
    public void Mhfd_MatchesHandCalculations()
    {
        var r = Run(MhfdSample);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var bmp = r.Tables[1].Rows;
        var pond = bmp.Single(x => (string)x[0]! == "Pond 1");
        Assert.Equal(57.9, (double)pond[3]!, 1);
        Assert.Equal(7993, (double)pond[7]!, 0);                                        // 7993.128 cf
        Assert.Equal(9592, (double)pond[8]!, 0);                                        // 9591.754 cf
        const string edb = "Extended detention basin Pond 1";
        Assert.Equal(101.604, (double)Item(r, edb, "WQCV water surface")!, 3);           // 101.6039
        Assert.Equal(5, Item(r, edb, "Rows"));
        Assert.Equal(1.5739, (double)Item(r, edb, "Hole area per row")!, 2);              // explicit stepping: 1.5739 sq in
        Assert.Equal(40.0, (double)Item(r, edb, "Drain time, 99 % of the volume")!, 1);
        Assert.Equal(7.526, (double)Item(r, edb, "Drain time, 50 % of the volume")!, 1);
        Assert.Equal(961.89, (double)Item(r, "Bioretention Rain garden", "Area by Darcy, V·df / (k (hf + df) tf)")!, 1);
        Assert.Equal(1282.5, (double)Item(r, "Bioretention Rain garden", "Required filter area")!, 1);
        Assert.Equal(78.5, (double)Item(r, "Sand filter Sand filter", "Area by Darcy, V·df / (k (hf + df) tf)")!, 1);
        Assert.Equal(381.4, (double)Item(r, "Sand filter Sand filter", "Required filter area")!, 1);
        Assert.Equal(541.2, (double)Item(r, "Infiltration Trench", "Bottom area, V / (n·d)")!, 1);
        Assert.Equal(28.8, (double)Item(r, "Infiltration Trench", "Drain time")!, 1);
        Assert.Contains(r.Summary, s => s.Contains("97.5 %"));                         // E1 is untreated
        Assert.NotEmpty(r.Charts);
    }

    [Fact]
    public void Darcy_GovernsWhenTheMediaIsSlow()
    {
        var r = Run("""
            criteria = mhfd
            [Subbasins]
            Name	Area (ac)	Imperviousness (%)	BMP
            S	2	80	F
            [BMP F]
            type = bioretention
            ponding depth = 12
            media depth = 24
            permeability = 0.1
            filter drain time = 48
            """);
        Assert.True(r.Succeeded);
        double v = Wq.WqcvInches(0.8, 0.8) / 12 * 2 * 43560;
        double darcy = v * 2.0 / (0.1 * (0.5 + 2.0) * 2.0);
        Assert.Equal(Math.Round(darcy, 1), (double)Item(r, "Bioretention F", "Required filter area")!, 1);
        Assert.Equal("Darcy filtration", Item(r, "Bioretention F", "Governs"));
        Assert.Equal(48, (double)Item(r, "Bioretention F", "Drain time at the required area")!, 1);
    }

    [Fact]
    public void Infiltration_DepthLimitedByDrainTime_AndLowRateWarned()
    {
        var r = Run("""
            [Subbasins]
            Name	Area (ac)	Imperviousness (%)	BMP
            S	1	70	T
            [BMP T]
            type = infiltration
            infiltration rate = 0.3
            safety factor = 2
            void ratio = 1.0
            depth = 4
            maximum drain time = 72
            """);
        Assert.True(r.Succeeded);
        // f = 0.15 in./hr = 0.0125 ft/hr; 72 h → 0.9 ft deep at n = 1.
        Assert.Equal(0.9, (double)Item(r, "Infiltration T", "Storage depth used")!, 6);
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-004");
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-005");
        Assert.Equal("LOW RATE", r.Tables[1].Rows[0][10]);
    }

    [Fact]
    public void SI_SampleRuns_AndMatchesTheUsEquation()
    {
        var r = Sample(UnitSystem.SI);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.DoesNotContain(r.Messages, m => m.Severity == MessageSeverity.Warning);
        var b1 = r.Tables[0].Rows.Single(x => (string)x[0]! == "B1");
        // 0.45 ha at 90 %: WQCV mm = 25.4 × in.; volume = depth × area.
        double inches = Wq.WqcvRapidCity(0.9, 1.0);                       // bioretention: 48 h ponding → a = 1.0
        Assert.Equal(Math.Round(inches * 25.4, 4), (double)b1[7]!, 4);
        Assert.Equal(Math.Round(inches * 0.0254 * 4500, 0), (double)b1[8]!, 0);
        Assert.Contains("m³", r.Tables[0].Headers[8]);
        // A2 has a 0.2 ha design credit: volume on 1.2 ha.
        var a2 = r.Tables[0].Rows.Single(x => (string)x[0]! == "A2");
        Assert.Equal(1.2, (double)a2[3]!, 9);
        Assert.Equal(Math.Round(Wq.WqcvRapidCity(0.45, 1.0) * 25.4, 4), (double)a2[7]!, 4);
        Assert.Equal(Math.Round(Wq.WqcvRapidCity(0.45, 1.0) * 0.0254 * 12000, 0), (double)a2[8]!, 0);
        Assert.Contains(r.Tables, t => t.Title == "Extended detention basin Pond 1");
    }

    [Theory]
    [InlineData("S\t-1\t50\tF", "E-WQ-002")]                  // negative area
    [InlineData("S\t1\t150\tF", "E-WQ-002")]                  // imperviousness over 100 %
    [InlineData("S\t1\t-5\tF", "E-WQ-002")]
    [InlineData("S\t1\tx\tF", "E-TL-003")]                     // not a number
    [InlineData("S\t1\t50\tNOPE", "E-WQ-004")]                 // BMP without a section
    public void BadSubbasins_AreReported(string row, string code)
    {
        var r = Run($"[Subbasins]\nName\tArea\tImperviousness\tBMP\n{row}\n[BMP F]\ntype = sand filter\n");
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
        Assert.Empty(r.Tables);
    }

    [Theory]
    [InlineData("type = pond", "E-WQ-005")]
    [InlineData("type = sand filter\nmedia depth = -18", "E-WQ-006")]
    [InlineData("type = sand filter\npermeability = 0", "E-WQ-006")]
    [InlineData("type = bioretention\nmedia porosity = 1.2", "E-WQ-006")]
    [InlineData("type = infiltration", "E-WQ-009")]                                   // no rate
    [InlineData("type = infiltration\ninfiltration rate = 1\nsafety factor = 0.5", "E-WQ-006")]
    [InlineData("type = infiltration\ninfiltration rate = 1\nvoid ratio = 1.5", "E-WQ-006")]
    [InlineData("type = edb\nElevation\tArea\n100\t1000", "E-WQ-007")]               // one stage row
    [InlineData("type = edb\nElevation\tArea\n100\t1000\n99\t2000", "E-WQ-007")]     // not increasing
    [InlineData("type = edb\nElevation\tArea\n100\t100\n101\t200", "E-WQ-008")]      // too small for the WQCV
    [InlineData("type = edb\ndrained fraction = 100\nElevation\tArea\n100\t10000\n102\t12000", "E-WQ-006")]
    [InlineData("type = edb\nwqcv drain time = 0\nElevation\tArea\n100\t10000\n102\t12000", "E-WQ-006")]
    public void BadBmps_AreReported(string section, string code)
    {
        var r = Run($"criteria = mhfd\n[Subbasins]\nName\tArea\tImperviousness\tBMP\nS\t3\t60\tF\n[BMP F]\n{section}\n");
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }

    [Fact]
    public void Empty_And_PercentAsFraction()
    {
        Assert.Contains(Run("").Messages, m => m.Code == "E-WQ-003");
        Assert.Contains(Run("criteria = mhfd\nregional depth = 0\n[Subbasins]\nName\tArea\tImperviousness\nS\t1\t50\n").Messages, m => m.Code == "E-WQ-001");
        var r = Run("[Subbasins]\nName\tArea\tImperviousness\nS\t1\t0.6\n");
        Assert.True(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-001");
        // A BMP nobody drains to is reported, not sized.
        var r2 = Run("[Subbasins]\nName\tArea\tImperviousness\nS\t1\t50\n[BMP Lonely]\ntype = sand filter\n");
        Assert.Contains(r2.Messages, m => m.Code == "W-WQ-002");
        Assert.Contains(r2.Summary, s => s.Contains("0.0 %"));
    }

    // ---------------- Rapid City Storm Water Quality Manual (2009), the default criteria

    [Fact]
    public void RapidCity_Equation_MatchesTheManualsExamples()
    {
        Assert.Equal(0.182, Wq.WqcvRapidCity(0.318, 1.0), 3);            // SWQM §2.2.2, north area, 40 h EDB
        Assert.Equal(0.178, Wq.WqcvRapidCity(0.365, 0.9), 3);            // south area, 24 h wetland
        Assert.Equal(0.7, Wq.RapidCityDrainCoefficient(6), 9);
        Assert.Equal(0.75, Wq.RapidCityDrainCoefficient(9), 9);
        Assert.Equal(0.8, Wq.RapidCityDrainCoefficient(12), 9);
        Assert.Equal(0.9, Wq.RapidCityDrainCoefficient(24), 9);
        Assert.Equal(1.0, Wq.RapidCityDrainCoefficient(40), 9);
        Assert.Equal(0.7, Wq.RapidCityDrainCoefficient(2), 9);
        Assert.Equal(1.16 * (0.91 - 1.191 + 0.78), Wq.WqcvRapidCity(1.0, 1.0), 12);
    }

    [Fact]
    public void RapidCity_DesignCredit_ReducesTheAreaNotTheDepth()
    {
        // SWQM §2.4.2 north example: 17.0 ac, 5.4 ac impervious, 1.79 ac natural park credit → 15.21 ac effective.
        var r = Run("""
            [Subbasins]
            Name	Area (ac)	Imperviousness (%)	Credit (ac)	BMP
            North	17.0	31.8	1.79	P
            [BMP P]
            type = porous landscape detention
            average depth = 12
            """);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var row = r.Tables[0].Rows[0];
        Assert.Equal(15.21, (double)row[3]!, 9);
        double inches = Wq.WqcvRapidCity(0.318, 0.8);                    // porous landscape detention: 12 h
        Assert.Equal(Math.Round(inches, 4), (double)row[7]!, 4);
        Assert.Equal(Math.Round(inches / 12 * 15.21 * 43560, 0), (double)row[8]!, 0);
        Assert.Equal(Math.Round(inches / 12 * 15.21 * 43560 / 1.0, 1), (double)Item(r, "Porous landscape detention P", "Minimum surface area, design volume / dav")!, 1);
    }

    // validation/hand_calcs_water_quality_output.txt, Rapid City part (explicit time stepping, independent of the 1/a scaling).
    private const double HandEdbLevel = 102.605, HandEdbHole = 0.4061, HandEdbHalf = 12.31, HandWetLevel = 100.570, HandWetHole = 4.0376;

    [Fact]
    public void RapidCity_Sample_MatchesHandCalculations()
    {
        var r = Sample();
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.DoesNotContain(r.Messages, m => m.Severity == MessageSeverity.Warning);
        Assert.Contains(r.Summary, s => s.Contains("Rapid City"));
        const string edb = "Extended detention basin Pond 1", wet = "Constructed wetland basin Wetland";
        // validation/hand_calcs_water_quality_output.txt, Rapid City part
        Assert.Equal(8776, (double)Item(r, edb, "WQCV (composite)")!, 0);
        Assert.Equal(10532, (double)Item(r, edb, "Design volume")!, 0);
        Assert.Equal(HandEdbLevel, (double)Item(r, edb, "WQCV water surface")!, 2);
        Assert.Equal(8, Item(r, edb, "Rows"));
        Assert.Equal(HandEdbHole, (double)Item(r, edb, "Hole area per row")!, 2);
        Assert.Equal(40.0, (double)Item(r, edb, "Drain time, the WQCV (83.3 % of the design volume)")!, 1);
        Assert.Equal(HandEdbHalf, (double)Item(r, edb, "Drain time, 50 % of the WQCV")!, 1);
        Assert.InRange((double)Item(r, edb, "Drain time, 50 % of the WQCV")!, 12, 40);
        Assert.Equal(5447, (double)Item(r, wet, "WQCV (composite)")!, 0);
        Assert.Equal(HandWetLevel, (double)Item(r, wet, "WQCV water surface")!, 2);
        Assert.Equal(HandWetHole, (double)Item(r, wet, "Hole area per row")!, 2);
        Assert.Equal(24.0, (double)Item(r, wet, "Drain time, 99 % of the volume")!, 1);
        Assert.True((double)Item(r, wet, "Pool volume / WQCV")! >= 75);
        const string bio = "Bioretention Rain garden";
        Assert.Equal(1855.9, (double)Item(r, bio, "Required area")!, 1);                   // a = 1.0 for the 48 h ponding
        Assert.Equal(824.8, (double)Item(r, bio, "Area to drain the ponding in 48 h by Darcy, V·df / (k (hf + df) t)")!, 1);
        Assert.Equal(21.3, (double)Item(r, bio, "Surface drain time at the required area")!, 1);
        Assert.Equal(40.5, (double)Item(r, bio, "Time to be ready for the next storm (surface + bed)")!, 1);
        Assert.Equal(367.9, (double)Item(r, "Sand filter Sand filter", "Required area, WQCV / depth")!, 1);
        Assert.Equal(973.8, (double)Item(r, "Porous landscape detention Island", "Minimum surface area, design volume / dav")!, 1);
        Assert.All(r.Tables[1].Rows, x => Assert.Equal("OK", x[10]));
    }

    private static ToolResult One(string bmp, string sub = "S\t3\t60\tF") => Run($"[Subbasins]\nName\tArea\tImperviousness\tBMP\n{sub}\n[BMP F]\n{bmp}\n");

    [Fact]
    public void RapidCity_ManualLimits_AreFlagged()
    {
        // Porous landscape detention: average depth 6–12 in., media ≥ 18 in. (§2.3.1)
        var r = One("type = porous landscape detention\naverage depth = 15\nmedia depth = 12");
        Assert.Equal(2, r.Messages.Count(m => m.Code == "W-WQ-010"));
        Assert.Equal("CHECK", r.Tables[1].Rows[0][10]);
        // Sand filter: design depth ≤ 3 ft, used at 3 ft (§2.3.3)
        r = One("type = sand filter\nponding depth = 48");
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-010" && m.Text.Contains("3 ft"));
        double v = Wq.WqcvRapidCity(0.6, 1.0) / 12 * 3 * 43560;
        Assert.Equal(Math.Round(v / 3, 1), (double)Item(r, "Sand filter F", "Required area, WQCV / depth")!, 1);
        // Bioretention: ponding ≤ 12 in. with collector pipes, ≤ 6 in. without (§2.3.9)
        r = One("type = bioretention\nponding depth = 18");
        Assert.Equal(12, (double)Item(r, "Bioretention F", "Ponding depth used")!, 9);
        r = One("type = bioretention\nponding depth = 12\nunderdrain = no");
        Assert.Equal(6, (double)Item(r, "Bioretention F", "Ponding depth used")!, 9);
        Assert.Contains(r.Messages, m => m.Code == "I-WQ-003");
        // Planting soil under 4 ft
        Assert.Contains(One("type = bioretention\nmedia depth = 24").Messages, m => m.Code == "W-WQ-010" && m.Text.Contains("4 ft"));
        // Wetland: pool < 75 % of WQCV and surcharge > 2 ft (§2.3.4)
        r = One("type = constructed wetland\npermanent pool = 100.1\nElevation\tArea\n100\t1000\n101\t1100\n105\t1500");
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-010" && m.Text.Contains("75 %"));
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-010" && m.Text.Contains("surcharge"));
        // EDB that releases half the WQCV in under 12 h (near-vertical sides, many rows: 11.2 h)
        r = One("type = edb\nElevation\tArea\n100\t10000\n101\t12000\n102\t14000", "S\t10\t60\tF");
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-009");
        // Infiltration is not a listed WQCV BMP: approval needed (§2.3.10)
        r = One("type = infiltration\ninfiltration rate = 2");
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-006");
        Assert.Equal("APPROVAL", r.Tables[1].Rows[0][10]);
        // The manual fixes drain times and the EDB sediment allowance; regional depth does not apply.
        r = Run("regional depth = 0.6\n[Subbasins]\nName\tArea\tImperviousness\tBMP\nS\t3\t60\tF\n[BMP F]\ntype = edb\nwqcv drain time = 24\nsediment allowance = 10\nElevation\tArea\n100\t2000\n102\t5000\n104\t9000\n");
        Assert.Equal(2, r.Messages.Count(m => m.Code == "W-WQ-007"));
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-008");
        Assert.Equal(40.0, (double)r.Tables[1].Rows[0][4]!, 9);
        Assert.Equal(20.0, (double)Item(r, "Extended detention basin F", "Sediment allowance")!, 9);
    }

    [Fact]
    public void RapidCity_Bioretention_48hSurface_72hBed()
    {
        // Slow soil: Darcy for a 48 h surface drain governs over the ponding storage.
        var r = One("type = bioretention\nponding depth = 12\nmedia depth = 48\npermeability = 0.2");
        double v = Wq.WqcvRapidCity(0.6, 1.0) / 12 * 3 * 43560;                     // 48 h → a = 1.0
        double darcy = v * 4.0 / (0.2 * (0.5 + 4.0) * 2.0);
        Assert.Equal(Math.Round(darcy, 1), (double)Item(r, "Bioretention F", "Required area")!, 1);
        Assert.Equal("48 h surface drain", Item(r, "Bioretention F", "Governs"));
        Assert.Equal(48, (double)Item(r, "Bioretention F", "Surface drain time at the required area")!, 1);
        Assert.DoesNotContain(r.Tables[2].Rows, x => ((string)x[0]!).StartsWith("Time to be ready"));   // bed not modeled
        // Bed modeled: 48 h surface + 0.25 × 4 ft / 0.5 ft/day = 48 h bed → 96 h > 72 h.
        r = One("type = bioretention\nponding depth = 12\nmedia depth = 48\npermeability = 0.5\nmedia porosity = 0.25");
        double tSurf = (double)Item(r, "Bioretention F", "Surface drain time at the required area")!;
        Assert.Equal(48.0, (double)Item(r, "Bioretention F", "Bed drainage after the ponding, stored depth / rate through the media (k, unit gradient)")!, 1);
        Assert.Equal(Math.Round(tSurf + 48, 1), (double)Item(r, "Bioretention F", "Time to be ready for the next storm (surface + bed)")!, 1);
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-010" && m.Text.Contains("72 h"));
        // No underdrain: soil and gravel drain to the subgrade at its design rate (0.5 in./hr / 2 = 0.5 ft/day).
        r = One("type = bioretention\nponding depth = 6\nunderdrain = no\npermeability = 2\nmedia porosity = 0.2\ngravel depth = 12\nsubgrade infiltration rate = 0.5");
        Assert.Equal(Math.Round((0.2 * 4 + 0.4 * 1) / 0.5 * 24, 1), (double)Item(r, "Bioretention F", "Bed drainage after the ponding, stored depth / rate through the subgrade (0.5 in./hr / 2)")!, 1);
        Assert.Contains(One("type = bioretention\nunderdrain = no\nponding depth = 6\nmedia porosity = 0.2").Messages, m => m.Code == "E-WQ-009");
        // The 48 h is fixed under the Rapid City criteria.
        r = One("type = bioretention\nfilter drain time = 24\nwqcv drain time = 12");
        Assert.Equal(2, r.Messages.Count(m => m.Code == "W-WQ-007"));
        Assert.Equal(48.0, (double)r.Tables[1].Rows[0][4]!, 9);
    }

    [Theory]
    [InlineData("criteria = denver\n", "type = edb", "E-WQ-010")]
    [InlineData("", "type = constructed wetland\nElevation\tArea\n100\t1000\n102\t3000", "E-WQ-011")]
    [InlineData("", "type = constructed wetland\npermanent pool = 99\nElevation\tArea\n100\t1000\n102\t3000", "E-WQ-011")]
    [InlineData("", "type = constructed wetland\npermanent pool = 101.9\nElevation\tArea\n100\t1000\n102\t3000", "E-WQ-008")]
    [InlineData("", "type = bioretention\nrunoff coefficient = 1.5", "E-WQ-006")]
    [InlineData("", "type = porous landscape detention\naverage depth = -1", "E-WQ-006")]
    public void RapidCity_BadInput_IsReported(string top, string bmp, string code)
    {
        var r = Run($"{top}[Subbasins]\nName\tArea\tImperviousness\tBMP\nS\t3\t60\tF\n[BMP F]\n{bmp}\n");
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }

    [Theory]
    [InlineData("S\t3\t60\t-1\tF")]
    [InlineData("S\t3\t60\t3\tF")]
    public void RapidCity_BadCredit_IsReported(string row)
    {
        var r = Run($"[Subbasins]\nName\tArea\tImperviousness\tCredit\tBMP\n{row}\n[BMP F]\ntype = sand filter\n");
        Assert.Contains(r.Messages, m => m.Code == "E-WQ-002" && m.Text.Contains("credit"));
    }

    [Fact]
    public void OrificePlate_DrainTimeIsInverseToArea()
    {
        var pond = new Pond([(100, 5000), (103, 9000)], [], 32.174, true);
        var plate = new OrificePlate(pond, 102, 1.0 / 3, 0.6, 32.174);
        plate.Size(40, 0.99);
        double a = plate.RowArea;
        Assert.Equal(40, plate.DrainHours(0.99), 6);
        plate.Size(20, 0.99);
        Assert.Equal(2 * a, plate.RowArea, 9);
        // The Runge–Kutta drawdown agrees with the integral: at 20 h the stored volume is 1 % of the start.
        var dd = plate.Drawdown(20, 0.05);
        Assert.InRange(pond.Storage(dd[^1].Elev), pond.Storage(102) * 0.0095, pond.Storage(102) * 0.0105);
    }
}
