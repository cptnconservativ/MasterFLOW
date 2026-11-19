using MasterFlow.Application.Tools;
using MasterFlow.Application.WaterQuality;
using MasterFlow.Domain;

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
    public void Sample_MatchesHandCalculations()
    {
        var r = Sample();
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
        var b1 = r.Tables[0].Rows.Single(x => (string)x[0]! == "B1");
        // 0.45 ha at 90 %, a 0.8: WQCV mm = 25.4 × in.; volume = depth × area.
        double inches = Wq.WqcvInches(0.9, 0.8, 10.9 / 25.4);
        Assert.Equal(inches * 25.4, (double)b1[5]!, 3);
        Assert.Equal(Math.Round(inches * 0.0254 * 4500, 0), (double)b1[6]!, 0);
        Assert.Contains("m³", r.Tables[0].Headers[6]);
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
    [InlineData("type = wetland", "E-WQ-005")]
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
        var r = Run($"[Subbasins]\nName\tArea\tImperviousness\tBMP\nS\t3\t60\tF\n[BMP F]\n{section}\n");
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }

    [Fact]
    public void Empty_And_PercentAsFraction()
    {
        Assert.Contains(Run("").Messages, m => m.Code == "E-WQ-003");
        Assert.Contains(Run("regional depth = 0\n[Subbasins]\nName\tArea\tImperviousness\nS\t1\t50\n").Messages, m => m.Code == "E-WQ-001");
        var r = Run("[Subbasins]\nName\tArea\tImperviousness\nS\t1\t0.6\n");
        Assert.True(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == "W-WQ-001");
        // A BMP nobody drains to is reported, not sized.
        var r2 = Run("[Subbasins]\nName\tArea\tImperviousness\nS\t1\t50\n[BMP Lonely]\ntype = sand filter\n");
        Assert.Contains(r2.Messages, m => m.Code == "W-WQ-002");
        Assert.Contains(r2.Summary, s => s.Contains("0.0 %"));
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
