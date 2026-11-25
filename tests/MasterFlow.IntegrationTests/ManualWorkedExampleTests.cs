using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Worked examples printed in the FHWA HEC / HDS manuals, NRCS TR-210-60 and the FHWA deck-force report, run through the
/// MasterFlow tools. Each test names the manual, the example and the page; the tolerance covers the manuals' chart reading
/// and intermediate rounding. Every case is listed in docs/REFERENCE_TEST_CASES.md with the manual's answer and MasterFlow's.
/// </summary>
public class ManualWorkedExampleTests
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary), Si = new(UnitSystem.SI);

    private static ToolResult Ok(IDesignTool tool, string input, DesignToolContext ctx)
    {
        var r = tool.Run(input, ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static object? Cell(ToolResult r, string table, string row, int col = 1) =>
        r.Tables.Single(t => t.Title.StartsWith(table, StringComparison.Ordinal)).Rows.First(x => ((string)x[0]!).StartsWith(row, StringComparison.Ordinal))[col];

    private static double Num(string text, string after)
    {
        int i = text.IndexOf(after, StringComparison.Ordinal) + after.Length;
        int j = i;
        while (j < text.Length && (char.IsDigit(text[j]) || text[j] is '.' or '-')) j++;
        return double.Parse(text[i..j], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Near(double expected, double actual, double relTol) =>
        Assert.True(Math.Abs(actual - expected) <= relTol * Math.Abs(expected), $"expected {expected} ± {relTol:P1}, got {actual}");

    // ---------------- HEC-11 (1989, SI edition), Section 5.4.1 Example Problem #1 ----------------
    [Fact]
    public void Hec11_Example1_RiprapLinedChannel()
    {
        // Va = 2.96 m/s, d = 3.60 m, 1V:2H banks, φ = 41°, Ss = 2.65, SF = 1.2. Manual (Chart 1): bed 0.085 m, bank 0.131 m.
        double k1 = RiprapRevetmentTool.K1(Math.Atan(0.5) * 180 / Math.PI, 41);
        Assert.Equal(0.73, k1, 2);                                                   // manual Figure 26: K1 = 0.73
        double bed = RiprapRevetmentTool.D50(2.96, 3.60, 1.0, 2.65, 1.2, true), bank = RiprapRevetmentTool.D50(2.96, 3.60, k1, 2.65, 1.2, true);
        Near(0.085, bed, 0.06);                                                      // 0.081 m: Eq. 6 against the chart read 0.085
        Near(0.131, bank, 0.03);
    }

    // ---------------- HDS-2 (3rd ed., 2024), Examples 6.1 and 6.2 ----------------
    [Fact]
    public void Hds2_Examples61And62_ColoradoMountainRegression()
    {
        // Capesius and Stephens (2009) Mountain Region, HDS-2 Table 6.1; A = 66.4 mi², S = 26 %, P = 28.8 in.; SE 40 % at the 25-year.
        const string input = """
            [Variables]
            Name, Site value, Minimum, Maximum
            A, 66.4, 1, 1060
            S, 26, 7.6, 60.2
            P, 28.8, 18, 47
            [Equations]
            T (yr), C, A, S, P, SEP (%)
            2, 0.0089, 0.78, 0.17, 2.10,
            5, 0.031, 0.77, 0.16, 1.85,
            10, 0.063, 0.77, 0.14, 1.71,
            25, 0.126, 0.75, 0.16, 1.55, 40
            50, 0.209, 0.75, 0.16, 1.45,
            100, 0.347, 0.75, 0.14, 1.35,
            """;
        var r = Ok(new RegionalRegressionTool(), input, Us);
        var row = r.Tables[0].Rows.Single(x => (double)x[0]! == 25);
        Near(900, (double)row[2]!, 0.01);                                            // Example 6.1: Q25 = 900 cfs
        Assert.Equal(0.167, RegionalRegressionTool.LogSe(40), 3);                    // Example 6.2: SE = 0.167 log units
        Near(600, (double)row[5]!, 0.05);                                            // −1 SE: 900(1 − 0.31) = 600 (rounded)
        Near(1300, (double)row[6]!, 0.03);                                           // +1 SE: 900(1 + 0.47) = 1,300 (rounded)
    }

    // ---------------- HEC-9 (3rd ed., 2005), Section 4.7 Example 2, Case 1, Pier Bent 2 ----------------
    private const string Hec9Si = """
        depth = 3.32
        velocity = 3.10
        pier width = 0.5
        pier length = 0.5
        pier shape = round
        log length = 13.70
        upstream water surface = 65.30
        downstream water surface = 64.55
        debris bottom = 61.98
        contracted area = 27.41
        other debris area = 39.55
        reference velocity = 3.10
        reference depth = 0.99
        """;

    [Fact]
    public void Hec9_Example2_DebrisLoadsOnPierBent_SI()
    {
        var r = Ok(new BridgeDebrisTool(), Hec9Si, Si);
        Near(45.48, (double)Cell(r, "Hydraulic forces", "HEC-9: Ahu")!, 0.001);
        Near(0.76, (double)Cell(r, "Hydraulic forces", "HEC-9: blockage ratio")!, 0.01);
        Near(0.99, (double)Cell(r, "Hydraulic forces", "HEC-9: Froude")!, 0.01);
        Near(741, (double)Cell(r, "Hydraulic forces", "HEC-9: upstream hydrostatic")!, 0.005);
        Near(446, (double)Cell(r, "Hydraulic forces", "HEC-9: downstream hydrostatic")!, 0.01);   // manual rounds hcd to 1.29 m
        Near(295, (double)Cell(r, "Hydraulic forces", "HEC-9: hydrostatic Fh")!, 0.01);
        // Drag: the manual rounds B to 0.76 (CD 0.36, 79 kN); unrounded B = 0.756 gives CD 0.378 and 83 kN.
        Near(79, (double)Cell(r, "Hydraulic forces", "HEC-9: drag FD")!, 0.06);
        Near(374, (double)Cell(r, "Hydraulic forces", "HEC-9: total debris load")!, 0.02);
        Near(63.46, (double)Cell(r, "Hydraulic forces", "HEC-9: elevation of Fh")!, 0.0005);
    }

    [Fact]
    public void Hec9_Example2_DebrisLoadsOnPileBent_US()
    {
        const string input = """
            depth = 10.9
            velocity = 10.17
            pier width = 1.5
            pier length = 1.5
            pier shape = round
            log length = 45
            upstream water surface = 214.25
            downstream water surface = 211.82
            debris bottom = 203.35
            contracted area = 295.07
            other debris area = 426.24
            reference velocity = 10.17
            reference depth = 3.25
            """;
        var r = Ok(new BridgeDebrisTool(), input, Us);
        Near(166.809, (double)Cell(r, "Hydraulic forces", "HEC-9: upstream hydrostatic")!, 0.005);   // kips
        Near(65.966, (double)Cell(r, "Hydraulic forces", "HEC-9: hydrostatic Fh")!, 0.005);
        // The manual prints FD = 19,589 lb, but its own inputs (CD 0.36, A 490.5, Vr 10.17) give 17,700 lb; MasterFlow's
        // unrounded CD 0.377 gives 18,500 lb. Checked against the manual's inputs, not its printed product.
        double manualInputs = 0.36 * 62.4 * 490.5 * 10.17 * 10.17 / (2 * 32.2) / 1000;
        Near(manualInputs, (double)Cell(r, "Hydraulic forces", "HEC-9: drag FD")!, 0.06);
        Near(208.22, (double)Cell(r, "Hydraulic forces", "HEC-9: elevation of Fh")!, 0.0005);
    }

    [Fact]
    public void Hec9_DesignLogLength_And_Table41()
    {
        // HEC-9 §3.3.2: channel 120 ft → min(120, 80, 30 + 30) = 60 ft; Pacific Northwest drops the third rule → 120 ft capped by 150 ft.
        Assert.Equal(60, BridgeDebrisTool.DesignLogLength(120, 80, false, false), 9);
        Assert.Equal(120, BridgeDebrisTool.DesignLogLength(120, 150, true, false), 9);
        Assert.Equal(24, BridgeDebrisTool.DesignLogLength(100, 24, false, true), 9);
        // Table 4.1 branches.
        Assert.Equal(1.8, BridgeDebrisTool.DebrisDragPier(0.2, 0.3), 9);
        Assert.Equal(2.6 - 2.0 * 0.6, BridgeDebrisTool.DebrisDragPier(0.2, 0.6), 9);
        Assert.Equal(3.1 - 3.6 * 0.5, BridgeDebrisTool.DebrisDragPier(0.5, 0.9), 9);
        Assert.Equal(1.4 - 1.4 * 0.8, BridgeDebrisTool.DebrisDragPier(0.8, 0.9), 9);
    }

    // ---------------- FHWA-HRT-09-028 (cited by HDS-7 2nd ed. §11.1.4), Chapter 5 example ----------------
    [Fact]
    public void Hds7_DeckForces_FhwaHrt09028Example()
    {
        // Six-girder deck, s = 2.5 m, W = 11.2 m, low chord 4 m above the bed. 100-yr: WS 5.5 m, V 2.3 m/s → h* = 0.6.
        var e100 = BridgeDebrisTool.DeckCoefficients("six-girder", 0.6)!.Value;
        Assert.Equal(1.403, e100.Upper.Cd, 3); Assert.Equal(1.003, e100.Lower.Cd, 3);
        Assert.Equal(-0.955, e100.Upper.Cl, 3); Assert.Equal(-1.462, e100.Lower.Cl, 3);
        Assert.Equal(0.247, e100.Upper.Cm, 3); Assert.Equal(0.092, e100.Lower.Cm, 3);
        var e1000 = BridgeDebrisTool.DeckCoefficients("six-girder", 2.4)!.Value;    // 1,000-yr: WS 10 m → h* = 2.4
        Assert.Equal(2.114, e1000.Upper.Cd, 3); Assert.Equal(1.714, e1000.Lower.Cd, 3);
        Assert.Equal(-0.112, e1000.Upper.Cl, 3); Assert.Equal(-0.192, e1000.Lower.Cl, 3);
        Assert.Equal(0.0318, e1000.Upper.Cm, 3); Assert.Equal(-0.0695, e1000.Lower.Cm, 3);
        // Drag per metre of bridge at the 1,000-year flood: 27.1 kN/m (report Fig. 43).
        const string input = """
            depth = 10
            velocity = 3.2
            pier width = 1
            pier length = 9
            log length = 12
            water surface = 10
            low chord = 4
            deck depth = 2.5
            deck width = 11.2
            span = 1
            deck type = six-girder
            """;
        var r = Ok(new BridgeDebrisTool(), input, Si);
        Near(27.1, (double)Cell(r, "Hydraulic forces", "Deck drag")!, 0.005);
    }

    // ---------------- ADS StormTech catalog: minimum installed storage per chamber ----------------
    [Theory]
    [InlineData("SC-160LP", 15.0)]
    [InlineData("SC-310", 29.3)]
    [InlineData("SC-740", 74.9)]
    [InlineData("DC-780", 78.4)]
    [InlineData("SC-800", 78.4)]
    [InlineData("MC-3500", 175.0)]
    [InlineData("MC-4500", 162.6)]
    [InlineData("MC-7200", 267.3)]
    public void StormTech_CatalogInstalledStorage(string model, double catalog)
    {
        var c = UndergroundDetentionTool.FindStormTech(model)!;
        Near(catalog, UndergroundDetentionTool.InstalledPerChamber(c), 0.003);
        Assert.Equal(c.BareFt3, UndergroundDetentionTool.StormTechVolume(c, c.HeightIn), 1);
    }

    [Fact]
    public void StormTech_BedThroughTheTool()
    {
        // 20 rows × 20 SC-310 with no edge stone: per-chamber storage approaches the catalog 29.3 ft³ (one fewer row spacing).
        var r = Ok(new UndergroundDetentionTool(), "chamber = SC-310\nrows = 20\nper row = 20\nedge = 0\ncover = 1.5\n", Us);
        double total = (double)Cell(r, "Bed layout", "Total storage")!;
        Near(29.3 * 400, total, 0.01);
        Assert.DoesNotContain(r.Messages, m => m.Code is "W-UD-002" or "W-UD-003");   // 18 in. cover is within 16 in. to 8 ft
        var low = Ok(new UndergroundDetentionTool(), "chamber = SC-310\nrows = 2\nper row = 5\ncover = 1.0\n", Us);
        Assert.Contains(low.Messages, m => m.Code == "W-UD-002");                    // 12 in. < 16 in. minimum
        var deep = Ok(new UndergroundDetentionTool(), "chamber = MC-4500\nrows = 2\nper row = 5\ncover = 8\n", Us);
        Assert.Contains(deep.Messages, m => m.Code == "W-UD-003");                   // 8 ft > 7 ft maximum
    }

    // ---------------- HEC-15 (3rd ed., 2005) ----------------
    [Fact]
    public void Hec15_RiprapChannelMildSlope_CU()
    {
        // §6.3.1: Q 40 cfs, B 2 ft, Z 3, S 0.02, Class 1 D50 5 in. Manual: d 1.72 ft, n 0.062, stable D50 ≥ 0.45 ft → Class 1 fails.
        var r = Ok(new ChannelLiningTool(), "[Reaches]\nName\tQ (cfs)\tS (ft/ft)\tB (ft)\tZ\tRc (ft)\tLining\tD50 (in)\nA\t40\t0.02\t2\t3\t\triprap\t5\n", Us);
        var row = r.Tables[0].Rows[0];
        Near(1.72, (double)row[3]!, 0.01);
        Near(0.062, (double)row[6]!, 0.02);
        Near(0.45 * 12, Num((string)row[13]!, "bottom "), 0.03);                    // 5.3 in. against the manual's rounded 0.45 ft
        Assert.Equal("FAILS", row[14]);
    }

    [Fact]
    public void Hec15_GrassLining_CU()
    {
        // §4.3: sod Cn 0.142, Cf 0.90, clayey sand τp,soil 0.068 lb/ft², Q 17.5 cfs, B 3 ft, Z 3, S 0.03.
        // Manual: d 0.70 ft, n 0.032, τd 1.31 lb/ft², τp on the vegetation 2.7 lb/ft² → acceptable.
        var r = Ok(new ChannelLiningTool(), "[Reaches]\nName\tQ (cfs)\tS (ft/ft)\tB (ft)\tZ\tRc (ft)\tLining\tD50 (in)\tCn\tCf\tD75 (in)\ttau soil\nA\t17.5\t0.03\t3\t3\t\tgrass\t\t0.142\t0.90\t\t0.068\n", Us);
        var row = r.Tables[0].Rows[0];
        Near(0.70, (double)row[3]!, 0.02);
        Near(0.032, (double)row[6]!, 0.03);
        Near(1.31, (double)row[7]!, 0.02);
        Near(2.7, Num((string)row[13]!, "τp "), 0.02);
        Assert.Equal("OK", row[14]);
    }

    // ---------------- Rinker precast manholes (drawings 016 and 017) ----------------
    [Fact]
    public void Rinker_ManholeWallsAndBuoyancyDefaults()
    {
        Assert.Equal(5, ManholeSizingTool.RinkerManhole(48)!.WallIn);
        Assert.Equal(5.5, ManholeSizingTool.RinkerManhole(54)!.WallIn);
        Assert.Equal(6, ManholeSizingTool.RinkerManhole(60)!.WallIn);
        Assert.Equal(7, ManholeSizingTool.RinkerManhole(72)!.WallIn);
        Assert.Equal(12, ManholeSizingTool.RinkerManhole(72)!.BaseIn);
        Assert.All(ManholeSizingTool.Rinker, m => Assert.Equal(ManholeSizingTool.WallB(m.IdIn, false), m.WallIn, 9));   // the C76 wall B rule fits every size
        // 48 in. barrel: π/4·(58² − 48²)/144 × 150 = 867 lb/ft.
        Assert.Equal(867.0, ManholeSizingTool.RinkerWeightPerFoot(ManholeSizingTool.RinkerManhole(48)!), 0);
        // Buoyancy with Wall and Base blank takes the Rinker 72 in. section (7 in. wall, 12 in. base).
        var tool = new StructureBuoyancyTool();
        string head = "[Structures]\nName\tShape\tInside L or D\tInside W\tWall\tBase\tExt\tTop slab\tOpening\tBottom\tTop\tGW\n";
        var blank = Ok(tool, head + "MH\tround\t6\t\t\t\t0\t0.67\t2\t95\t108\t106\n", Us);
        var given = Ok(tool, head + "MH\tround\t6\t\t" + (7 / 12.0).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\t1\t0\t0.67\t2\t95\t108\t106\n", Us);
        Assert.Contains(blank.Messages, m => m.Code == "I-BY-002");
        Assert.Equal(given.Tables[0].Rows[0], blank.Tables[0].Rows[0]);
    }

    // ---------------- HEC-21 (1993), Chapter 10 Examples 1 and 3 ----------------
    private const string Hec21 = "Sx = 0.02\nn = 0.016\nC = 0.9\nT allow = 10\napproach Q = 0\ndrain = grate\nL = 1.5\nW = 1.0\nVo = 4.9\nclogging = 0\n";

    [Fact]
    public void Hec21_Example1_NoInletsNeeded()
    {
        // 500 ft at 3 %, Wp = 18 ft, i = 6.0 in/hr. Manual: Lo = 1,837 ft (> 500 ft, no inlets).
        var r = Ok(new DeckDrainageTool(), Hec21 + "bridge length = 500\nwidth = 18\nS = 0.03\ni = 6.0\n", Us);
        Near(1837, (double)Cell(r, "Design values", "Length to the first drain")!, 0.01);
    }

    [Fact]
    public void Hec21_Example3_InletSpacing()
    {
        // 1,200 ft at 3 %, Wp = 34 ft, i = 7.3 in/hr, 1 × 1.5 ft curved vane grate. Manual: Qf 4.1 cfs, Lo 800 ft, E 0.245, Lc 196 ft.
        var r = Ok(new DeckDrainageTool(), Hec21 + "bridge length = 1200\nwidth = 34\nS = 0.03\ni = 7.3\n", Us);
        Near(4.1, (double)Cell(r, "Design values", "Gutter capacity")!, 0.01);
        Near(800, (double)Cell(r, "Design values", "Length to the first drain")!, 0.01);
        Near(0.245, (double)Cell(r, "Design values", "Frontal flow ratio")!, 0.005);
        // Known difference: the manual takes E = Eo (no side flow); MasterFlow adds the HEC-22 side-flow term Rs·(1 − Eo),
        // giving E 0.265 and Lc 212 ft. With E = Eo the spacing is the manual's 196 ft.
        double e = (double)Cell(r, "Design values", "Drain efficiency")!, lc = (double)Cell(r, "Design values", "Spacing of later drains")!;
        Near(0.265, e, 0.01);
        Near(196, lc * 0.245 / e, 0.01);
    }

    // ---------------- HEC-23 (3rd ed., 2009), Design Guideline 14 §14.4 ----------------
    [Fact]
    public void Hec23_DG14_AbutmentRiprap()
    {
        // Spill-through left abutment, y = 2.7 ft, SBR = 225/9.7 = 23.2 → V = Qf/Af = 7,720/613.5 = 12.6 ft/s, Fr = 1.35 > 0.8 → Eq. 14.2.
        // Manual: D50 = 0.40 × 2.7 = 1.1 ft; toe apron 2y = 5.4 ft; thickness 1.5·D50 = 1.7 ft.
        var r = Ok(new ScourCountermeasureTool(), "Ss = 2.65\n[Abutments]\nName\tType\tDepth y (ft)\tVelocity V (ft/s)\tQ total (cfs)\tA total (ft²)\tSBR\tQf setback (cfs)\tAf setback (ft²)\nLeft\tspill-through\t2.7\t\t33220\t2590.5\t23.2\t7720\t613.5\n", Us);
        var row = r.Tables.Single(t => t.Title.Contains("abutments")).Rows[0];
        Near(12.6, (double)row[3]!, 0.005);
        Near(1.35, (double)row[5]!, 0.01);
        Assert.Equal("Eq. 14.2", row[7]);
        Near(1.1 * 12, (double)row[8]!, 0.03);                                       // 13.0 in. (manual rounds to 1.1 ft)
        Near(5.4, (double)row[12]!, 0.005);
    }

    // ---------------- HEC-20 (4th ed., 2012), §6.5.2 Example Problem 2 ----------------
    [Fact]
    public void Hec20_Example2_EquilibriumSlope()
    {
        // Q 780 cfs, W 350 ft, S 0.0014, n 0.027, D50 0.30 mm, D90 0.96 mm, F* 0.047. Manual Eq. 6.17 with D90: Seq = 0.000108.
        var r = Ok(new StreamStabilityTool(), "Q = 780\nwidth = 350\nslope = 0.0014\nn = 0.027\nD50 = 0.30\nD90 = 0.96\nShields = 0.047\n", Us);
        Near(0.000108, (double)Cell(r, "", "Equilibrium slope Seq for D90")!, 0.01);
        Near(1.05, (double)Cell(r, "", "Normal depth")!, 0.03);                      // manual's given depth 1.05 ft
    }

    // ---------------- NRCS TR-210-60 (2019) ----------------
    [Fact]
    public void Tr60_Figure22_AuxiliaryAndFreeboardRainfall()
    {
        // P100 = 5 in., PMP = 25 in. (difference 20 in.).
        Assert.Equal((5.0, 7.4), SpillwayBreachTool.Tr60Rainfall("low", 5, 25, 20000, false));            // P100; P100 + 0.12Δ
        Assert.Equal((6.2, 10.2), SpillwayBreachTool.Tr60Rainfall("low", 5, 25, 40000, false));           // + 0.06Δ; + 0.26Δ
        Assert.Equal((7.4, 13.0), SpillwayBreachTool.Tr60Rainfall("low", 5, 25, 20000, true));            // upstream dam: 0.12Δ; 0.40Δ
        Assert.Equal((7.4, 13.0), SpillwayBreachTool.Tr60Rainfall("significant", 5, 25));
        Assert.Equal((10.2, 25.0), SpillwayBreachTool.Tr60Rainfall("high", 5, 25));
        // Figure 2–1 principal spillway (earth / vegetated auxiliary spillway).
        Assert.Equal("P50", SpillwayBreachTool.Tr60PrincipalSpillway("low", 20000, false, false, false));
        Assert.Equal("P25", SpillwayBreachTool.Tr60PrincipalSpillway("low", 20000, false, false, true));
        Assert.Equal("P75", SpillwayBreachTool.Tr60PrincipalSpillway("low", 40000, false, false, false));
        Assert.Equal("P100", SpillwayBreachTool.Tr60PrincipalSpillway("significant", null, false, false, false));
        Assert.Equal("¾ design life", SpillwayBreachTool.Tr60PrincipalSpillway("low", 40000, false, true, false));
        // Municipal water on a low hazard dam takes the significant hazard row.
        var r = Ok(new SpillwayBreachTool(), "hazard = low\npurpose = municipal\nP100 = 5\nPMP = 25\n", Us);
        Assert.Equal(7.4, (double)Cell(r, "TR-60 design rainfall", "Auxiliary")!, 2);
    }

    [Fact]
    public void Tr60_MinimumPeakBreachDischarge()
    {
        // Vs 120 ac-ft, Hw 22 ft, A 1,900 ft²: Br = 1.39 → 1100·Br^1.35 = 1,715 < 3.2·Hw^2.5 = 7,265 cfs (governs);
        // L = 300 ft < T = 461 ft, so the cap is 0.416·L·Hw^1.5 = 12,880 cfs.
        Assert.Equal(3.2 * Math.Pow(22, 2.5), SpillwayBreachTool.Tr60BreachMinimum(120, 22, 1900, 300), 6);
        Assert.Equal(0.416 * 300 * Math.Pow(22, 1.5), SpillwayBreachTool.Tr60BreachMinimum(5000, 22, 1900, 300), 6);
        Assert.Equal(65 * Math.Pow(22, 1.85), SpillwayBreachTool.Tr60BreachMinimum(5000, 22, 1900, null), 6);
        Assert.Equal(65 * Math.Pow(110, 1.85), SpillwayBreachTool.Tr60BreachMinimum(10, 110, 1900, null), 6);   // Hw ≥ 103 ft
        var r = Ok(new SpillwayBreachTool(), "breach storage = 120\nbreach depth = 22\nembankment area = 1900\nvalley width = 300\n", Us);
        Assert.Equal(Math.Round(3.2 * Math.Pow(22, 2.5)), (double)Cell(r, "Dam breach", "TR-210-60 minimum")!);
    }
}
