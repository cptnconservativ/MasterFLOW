using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Hydraulic jump and stilling basins against the FHWA HEC-14 (2006) equations and worked examples.</summary>
public class StillingBasinTests
{
    private static readonly StillingBasinTool Tool = new();
    private static DesignToolContext Ctx(UnitSystem u = UnitSystem.USCustomary) => new(u);
    private const double G = 32.174;

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, Ctx(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static object? Item(ToolResult r, string table, string item) =>
        r.Tables.Single(t => t.Title.StartsWith(table)).Rows.Single(x => ((string)x[0]!).StartsWith(item))[1];

    private static IReadOnlyList<object?> Basin(ToolResult r, string name) =>
        r.Tables.Single(t => t.Title.StartsWith("Stilling basins")).Rows.Single(x => (string)x[0]! == name);

    private static IReadOnlyList<object?> Element(ToolResult r, string basin, string element) =>
        r.Tables.Single(t => t.Title.StartsWith("Blocks")).Rows.Single(x => (string)x[0]! == basin && (string)x[1]! == element);

    // HEC-14 Ch. 8 examples: 10 ft x 6 ft box, 417 cfs, So = 6.5 %, yo = 1.50 ft, ST = SS = 0.5, TW = 1.88 ft.
    private const string Hec14Ch8 = "Q = 417\noutlet width = 10\nbasin width = 10\ny1 = 1.5\nground slope = 6.5\ntailwater = 1.88\n";

    [Fact]
    public void Jump_Rectangle_MatchesBelanger()
    {
        // b = 10, y1 = 1, V1 = 30 → Q = 300; Fr1 = 30/√32.174 = 5.289; y2 = ½(√(1 + 8Fr²) − 1) (HEC-14 Eq. 6.3).
        var r = Ok("Q = 300\nbasin width = 10\ny1 = 1\ntailwater = 7");
        double fr = 30 / Math.Sqrt(G), y2 = 0.5 * (Math.Sqrt(1 + 8 * fr * fr) - 1);
        Assert.Equal(fr, (double)Item(r, "Hydraulic jump", "Froude")!, 2);
        Assert.Equal(y2, (double)Item(r, "Hydraulic jump", "Sequent")!, 3);
        Assert.Equal(Math.Pow(y2 - 1, 3) / (4 * y2), (double)Item(r, "Hydraulic jump", "Energy loss")!, 3);
        // Figure 6.5: L/y1 = 40 at Fr1 5, 49.5 at 6.
        Assert.Equal((40 + 9.5 * (fr - 5)) * 1, (double)Item(r, "Hydraulic jump", "Jump length")!, 2);
        Assert.Equal("steady", Item(r, "Hydraulic jump", "Jump type"));
        Assert.StartsWith("TW ≈ y2", (string)Item(r, "Hydraulic jump", "Jump position")!);
    }

    [Fact]
    public void Jump_Hec14Example_Ch6()
    {
        // HEC-14 §6.2.1 (CU): 7 ft box, 400 cfs, y1 = 3.0 ft, Fr = 1.9 → J = 2.2 read from Figure 6.4, y2 = 6.6 ft.
        // Eq. 6.3 at the unrounded Fr = 1.94 gives J = 2.29, y2 = 6.86 ft.
        var r = Ok("Q = 400\noutlet width = 7\ny1 = 3");
        Assert.Equal(1.9, (double)Item(r, "Hydraulic jump", "Froude")!, 1);
        Assert.InRange((double)Item(r, "Hydraulic jump", "Sequent")!, 6.6, 6.9);
    }

    [Fact]
    public void Jump_Trapezoid_BalancesSpecificForce()
    {
        var s = new StillingBasinTool.Trap(6, 2);
        double q = 400, y1 = 0.8;
        double y2 = StillingBasinTool.Sequent(s, q, y1, G);
        Assert.Equal(StillingBasinTool.Momentum(s, q, y1, G), StillingBasinTool.Momentum(s, q, y2, G), 6);
        Assert.True(y2 > StillingBasinTool.Critical(s, q, G));
        var r = Ok("Q = 400\noutlet width = 6\nside slope = 2\ny1 = 0.8\ntailwater = 5");
        Assert.Equal(y2, (double)Item(r, "Hydraulic jump", "Sequent")!, 3);
        Assert.DoesNotContain(r.Tables, t => t.Title.StartsWith("Stilling basins"));   // no basin width: jump only
    }

    [Fact]
    public void Jump_Circular_MatchesTable61()
    {
        // HEC-14 Table 6.1: K = ȳ/y = 0.424 at y/D = 0.5 and C = A/D² ... = 0.393 (with the table's own scaling).
        var c = new StillingBasinTool.Circle(2);
        Assert.Equal(0.424, c.Moment(1) / c.Area(1) / 1, 3);
        Assert.Equal(Math.PI / 2, c.Area(1), 9);
        Assert.Equal(Math.PI * (3 - 1), c.Moment(3), 9);   // surcharged: A(y − D/2), Eq. 6.5
        // HEC-14 §6.2.2 (CU): 7 ft pipe, 200 cfs, y1 = 2.4 ft → y2 ≈ 5.5 ft (J = 2.3 from Figure 6.6).
        double y2 = StillingBasinTool.Sequent(new StillingBasinTool.Circle(7), 200, 2.4, G);
        Assert.InRange(y2, 5.2, 5.8);
        var r = Ok("Q = 200\ndiameter = 7\ny1 = 2.4");
        Assert.Equal(y2, (double)Item(r, "Hydraulic jump", "Sequent")!, 3);
        double fr1 = (double)Item(r, "Hydraulic jump", "Froude")!;
        Assert.InRange(fr1, 1.35, 6);
        Assert.Equal(10 * fr1 * 2.4, (double)Item(r, "Hydraulic jump", "Jump length")!, 1);   // y2 < D: Figure 6.8, Lj/y1 = 10·Fr1
    }

    [Fact]
    public void FreeJumpBasin_Hec14Example()
    {
        // HEC-14 §8.2 (CU), 2nd iteration: z1 = 84.5 → y1 = 0.98, V1 = 42.5, Fr1 = 7.6, y2 = 10.07, LB = 61.4, LS = 16.8, z3 = 92.9.
        var r = Ok(Hec14Ch8 + "floor drop = 15.5");
        var b = Basin(r, "Free jump");
        Assert.Equal(0.98, (double)b[4]!, 2);
        Assert.Equal(42.5, (double)b[5]!, 0);
        Assert.Equal(7.6, (double)b[6]!, 1);
        Assert.InRange((double)b[9]!, 9.95, 10.1);
        Assert.InRange((double)b[13]!, 30.99, 31.01);   // LT
        Assert.InRange((double)b[14]!, 60.5, 62);      // LB
        Assert.InRange((double)b[15]!, 16.6, 17.0);    // LS
        Assert.InRange((double)b[10]!, 8.3, 8.5);      // z3 − z1 = 92.9 − 84.5
        Assert.Equal("holds the jump", b[17]);
    }

    [Fact]
    public void TypeIIIBasin_Hec14Example()
    {
        // HEC-14 §8.3 (CU): z1 = 87.5 → y1 = 1.04, Fr1 = 6.9, y2 = 9.64, LB = 26.0, LS = 16.3; 5 chute blocks 1.0 ft,
        // The example reads LB/y2 = 2.7 at Fr1 6.9; the printed Figure 8.2 Type III curve gives 2.54, so LB ≈ 24.6 here.
        // 4 baffles h3 = 1.8 at 1.3 ft, end sill 1.5 ft.
        var r = Ok(Hec14Ch8 + "floor drop = 12.5");
        var b = Basin(r, "USBR Type III");
        Assert.Equal(1.04, (double)b[4]!, 2);
        Assert.Equal(6.9, (double)b[6]!, 1);
        Assert.InRange((double)b[9]!, 9.6, 9.75);
        Assert.InRange((double)b[14]!, 24.2, 25.0);
        Assert.InRange((double)b[15]!, 16.0, 16.5);
        var chute = Element(r, "USBR Type III", "Chute blocks");
        Assert.Equal(5, chute[2]);
        Assert.Equal(1.0, (double)chute[4]!, 2);
        var baffle = Element(r, "USBR Type III", "Baffle blocks");
        Assert.Equal(4, baffle[2]);
        Assert.Equal(1.8, (double)baffle[3]!, 1);
        Assert.Equal(1.25, (double)baffle[4]!, 2);
        Assert.Equal(1.5, (double)Element(r, "USBR Type III", "End sill")[3]!, 1);
    }

    [Fact]
    public void TypeIVBasin_Hec14Example()
    {
        // HEC-14 §8.4 (CU): z1 = 82.6 → y1 = 0.95, Fr1 = 7.9 (outside 2.5–4.5), C = 1.1, y2 = 11.15, LB = 68.0, LS = 19.0.
        var r = Ok(Hec14Ch8 + "floor drop = 17.4");
        var b = Basin(r, "USBR Type IV");
        Assert.Equal(0.95, (double)b[4]!, 2);
        Assert.Equal(7.9, (double)b[6]!, 1);
        Assert.Equal(1.1, (double)b[8]!, 3);
        Assert.InRange((double)b[9]!, 11.1, 11.3);
        Assert.InRange((double)b[14]!, 67.5, 69.5);
        Assert.InRange((double)b[15]!, 18.6, 19.2);
        Assert.Equal("no", b[2]);
        // Elements by Eqs. 8.15–8.16 at Fr1 = 3.5: Nc = WB/(2.625y1), W1 = WB/(3.5Nc), W2 = 2.5W1.
        var s = new StillingBasinTool.BasinState(0, 0.95, 0, 3.5, 4, 1.1, 4.4, 0, 30, 0, 0);
        var rows = StillingBasinTool.Appurtenances("USBR Type IV", s, 10, null, 0, G, false, "ft").ToList();
        Assert.Equal(4, rows[0][2]);
        Assert.Equal(10 / 14.0, (double)rows[0][4]!, 2);
        Assert.Equal(2.5 * 10 / 14.0, (double)rows[0][5]!, 2);
        Assert.Equal(0.95 * (0.0536 * 3.5 + 1.04), (double)rows[1][3]!, 2);
    }

    [Fact]
    public void SafBasin_Hec14Example()
    {
        // HEC-14 §8.5 (CU): z1 = 91.4 → y1 = 1.13, V1 = 36.8, Fr1 = 6.1, C = 0.85, y2 = 7.85, LB = 10.5, LS = 12.0, z3 = 97.4;
        // 6 chute blocks and 6 baffles 0.83 ft wide, 48–50 % blocked, sill 0.6 ft.
        var r = Ok(Hec14Ch8 + "floor drop = 8.6");
        var b = Basin(r, "SAF");
        Assert.Equal(1.13, (double)b[4]!, 1);
        Assert.Equal(6.1, (double)b[6]!, 1);
        Assert.Equal(0.85, (double)b[8]!, 3);
        Assert.InRange((double)b[9]!, 7.8, 7.9);
        Assert.InRange((double)b[14]!, 10.3, 10.7);
        Assert.InRange((double)b[15]!, 11.8, 12.2);
        Assert.InRange((double)b[10]!, 5.9, 6.1);
        Assert.Equal(6, Element(r, "SAF", "Chute blocks")[2]);
        Assert.Equal(6, Element(r, "SAF", "Baffle blocks")[2]);
        Assert.Equal(0.83, (double)Element(r, "SAF", "Baffle blocks")[4]!, 2);
        Assert.InRange((double)Element(r, "SAF", "End sill")[3]!, 0.6, 0.7);
        // Total length 39.7 ft is the shortest of the four examples (HEC-14 Table 8.2).
        Assert.Contains("SAF, 39.7 ft", r.Summary[0]);
    }

    [Fact]
    public void AutoDrop_IsLeastThatHolds()
    {
        var r = Ok(Hec14Ch8 + "floor drop = auto");
        foreach (var row in r.Tables.Single(t => t.Title.StartsWith("Stilling basins")).Rows)
        {
            Assert.InRange((double)row[12]!, -0.002, 0.002);   // margin zero at the least drop
            Assert.True((double)row[3]! > 0);
        }
        // The HEC-14 trial floors (8.6 SAF, 12.5 Type III, 15.5 free jump) all hold with a little to spare.
        Assert.InRange((double)Basin(r, "SAF")[3]!, 8.0, 8.6);
        Assert.InRange((double)Basin(r, "USBR Type III")[3]!, 11.0, 12.5);
        Assert.InRange((double)Basin(r, "Free jump")[3]!, 14.5, 15.5);
    }

    [Fact]
    public void Geometry_Eqs85to87()
    {
        // HEC-14 free-jump example: drop 15.5, LB 61.4, ST = SS = 0.5, So = 0.065 → LT 31.0, LS 16.8, z3 − z1 = 8.4.
        var (lt, ls, rise) = StillingBasinTool.Geometry(15.5, 61.4, 0.5, 0.5, 0.065);
        Assert.Equal(31.0, lt, 6);
        Assert.Equal((31.0 * 0.435 - 61.4 * 0.065) / 0.565, ls, 6);
        Assert.Equal(0.5 * ls, rise, 6);
        // Flat ground: the exit rises back to the outlet invert.
        Assert.Equal(4.0, StillingBasinTool.Geometry(4, 20, 0.5, 0.5, 0).Rise, 9);
    }

    [Fact]
    public void TransitionRadius_Eq88()
    {
        // HEC-14 §8.2: yo = 1.50 ft, Fr = 4.0 → r = 15.3 ft; SI 0.463 m → 4.71 m.
        Assert.Equal(15.3, StillingBasinTool.TransitionRadius(1.50, 4.0), 1);
        Assert.Equal(4.71, StillingBasinTool.TransitionRadius(0.463, 4.0), 2);
    }

    [Fact]
    public void SafTailwaterFactor_Eq818()
    {
        Assert.Equal(1.10 - 4.0 / 120, StillingBasinTool.TailwaterFactor("SAF", 2), 9);
        Assert.Equal(0.85, StillingBasinTool.TailwaterFactor("SAF", 8), 9);
        Assert.Equal(1.00 - 144.0 / 800, StillingBasinTool.TailwaterFactor("SAF", 12), 9);
        Assert.Equal(1.10, StillingBasinTool.TailwaterFactor("USBR Type IV", 3), 9);
        Assert.Equal(1.0, StillingBasinTool.TailwaterFactor("USBR Type III", 8), 9);
    }

    [Fact]
    public void FixedDrop_ReportsSweptOutJump()
    {
        var r = Tool.Run("Q = 200\nbasin width = 10\ny1 = 0.6\ntailwater = 2\nfloor drop = 0", Ctx());
        Assert.Contains(r.Messages, m => m.Code == "W-SB-003");
        Assert.StartsWith("TW below y2", (string)Item(r, "Hydraulic jump", "Jump position")!);
    }

    [Fact]
    public void FlowRange_UsesRatingAndFlagsSweepOut()
    {
        var r = Tool.Run("Q = 200\nbasin width = 10\napproach slope = 8\napproach n = 0.013\n[Tailwater rating]\nQ\tTW\n20\t0.2\n200\t3\n[Check flows]\nQ\n20\n100", Ctx());
        Assert.Contains(r.Tables, t => t.Title.StartsWith("Flow range check"));
        Assert.Single(r.Charts);
    }

    [Fact]
    public void ImpactBasin_Hec14Example_SI()
    {
        // HEC-14 §9.4 (SI): 1.219 m pipe, 8.5 m³/s, yo = 0.701 → ye = 0.590, Fr = 5.07, Ho = 8.17, WB = 5.0 m, VB = 6.9 m/s.
        var r = Ok("outlet shape = circular\noutlet span = 1.219\noutlet depth = 0.701\nimpact Q = 8.5", UnitSystem.SI);
        Assert.Equal(0.590, (double)Item(r, "USBR Type VI impact", "Equivalent depth")!, 2);
        Assert.Equal(5.07, (double)Item(r, "USBR Type VI impact", "Fr")!, 1);
        Assert.InRange((double)Item(r, "USBR Type VI impact", "Ho =")!, 8.1, 8.3);
        Assert.Equal(5.0, (double)Item(r, "USBR Type VI impact", "Design WB")!, 6);
        Assert.InRange((double)Item(r, "USBR Type VI impact", "Exit velocity")!, 6.7, 7.1);
        Assert.Equal(6.52, (double)r.Tables.Single(t => t.Title.StartsWith("USBR Type VI dimensions")).Rows.Single(x => (string)x[0]! == "L")[1]!, 2);   // Table 9.2 (SI), WB = 5.0 row
    }

    [Fact]
    public void ImpactBasin_Hec14Example_CU_AndIdcmCheck()
    {
        // HEC-14 §9.4 (CU): 4 ft pipe, 300 cfs, yo = 2.3 → Fr = 5.06, Ho = 26.8, WB = 16 ft, VB = 22.7 ft/s.
        var r = Ok("outlet shape = circular\noutlet span = 4\noutlet depth = 2.3\nimpact Q = 300");
        Assert.Equal(16.0, (double)Item(r, "USBR Type VI impact", "Design WB")!, 6);
        Assert.InRange((double)Item(r, "USBR Type VI impact", "Exit velocity")!, 22.3, 23.1);
        Assert.Equal(12.25, (double)Item(r, "USBR Type VI dimensions", "h1")!, 2);
        // IDCM check: D = √A, W = 2.88·Fr^0.566·D.
        double a = (double)Item(r, "USBR Type VI impact", "Flow area")!, d = Math.Sqrt(a), fr = 300 / a / Math.Sqrt(G * d);
        Assert.Equal(2.88 * Math.Pow(fr, 0.566) * d, (double)Item(r, "USBR Type VI, Rapid City", "W from Fr")!, 2);
        Assert.Contains(r.Messages, m => m.Code == "I-SB-011");
    }

    [Fact]
    public void ImpactBasin_OverridesAndLimits()
    {
        // Ho/WB and HL/Ho typed from the figures replace the provisional lines.
        var r = Ok("outlet shape = box\noutlet span = 3\noutlet rise = 3\noutlet depth = 1\nimpact Q = 60\nimpact Ho/WB = 1.2\nimpact HL/Ho = 0.5");
        double ho = 1 + 20.0 * 20 / (2 * G);
        Assert.Equal(ho / 1.2, (double)Item(r, "USBR Type VI impact", "Required width")!, 2);
        Assert.Equal(ho * 0.5, (double)Item(r, "USBR Type VI impact", "HB")!, 2);
        Assert.Equal(1.0, (double)Item(r, "USBR Type VI impact", "Equivalent depth")!, 6);   // box: ye = y
        var big = Ok("outlet shape = circular\noutlet span = 6\noutlet depth = 3\nimpact Q = 500");
        Assert.Contains(big.Messages, m => m.Code == "W-SB-006");
        var two = Ok("outlet shape = circular\noutlet span = 6\noutlet depth = 3\nimpact Q = 500\nimpact basins = 2");
        Assert.DoesNotContain(two.Messages, m => m.Code == "W-SB-006");
        Assert.Equal(250.0, (double)Item(two, "USBR Type VI impact", "Q per basin")!);
    }

    [Fact]
    public void TableWidth_InterpolatesLogLog()
    {
        Assert.Equal(66, StillingBasinTool.ImpactWidthFromTable(21), 6);
        Assert.Equal(198, StillingBasinTool.ImpactWidthFromTable(339), 6);
        double w = StillingBasinTool.ImpactWidthFromTable(100);   // between 85 (111) and 115 (126)
        Assert.Equal(111 * Math.Pow(100.0 / 85, Math.Log(126.0 / 111) / Math.Log(115.0 / 85)), w, 6);
    }

    [Fact]
    public void CsuBasin_Hec14Example_SI()
    {
        // HEC-14 §9.1 (SI): 2438 mm box, 39.64 m³/s, yo = 1.829, Vo = 8.87, yA = 0.604 (Figure 4.3), TW = 1.001, WB/Wo = 5.
        // Required CB·AF·N ≈ 4.63; 4 rows (CB 0.42) give 4.12, too little; 5 rows (CB 0.38, N 19) give 4.72, OK; LB = 17.746 m.
        var r = Ok("outlet shape = box\noutlet span = 2.438\noutlet rise = 2.438\noutlet depth = 1.829\ncsu Q = 39.64\ncsu yA = 0.604\ntailwater = 1.001\nexpansion ratio = 5", UnitSystem.SI);
        var t = r.Tables.Single(x => x.Title.StartsWith("CSU"));
        var four = t.Rows.Single(row => (int)row[1]! == 4 && (double)row[3]! == 0.71);
        var five = t.Rows.Single(row => (int)row[1]! == 5 && (double)row[3]! == 0.71);
        Assert.InRange((double)five[13]!, 4.58, 4.70);
        Assert.InRange((double)four[12]!, 4.05, 4.2);
        Assert.Equal("too little drag", four[^1]);
        Assert.InRange((double)five[12]!, 4.65, 4.8);
        Assert.Equal("OK", five[^1]);
        Assert.InRange((double)five[14]!, 17.7, 17.8);
        Assert.Equal(2.07, (double)five[15]!, 1);   // flare ue, Eq. 9.5
    }

    [Fact]
    public void CsuBasin_MomentumEquationByHand()
    {
        // Box Wo = 4, yo = 1.5, Q = 180 → Vo = 30, Fr = 30/√(g·1.5) = 4.317; VA = (1.65 − 0.3Fr)Vo; no tailwater, VB = 6.
        var r = Ok("outlet shape = box\noutlet span = 4\noutlet depth = 1.5\ncsu Q = 180\nexit velocity = 6\nexpansion ratio = 6");
        double vo = 30, fr = vo / Math.Sqrt(G * 1.5), va = (1.65 - 0.3 * fr) * vo, wb = 24, rho = 62.4 / G;
        double req = (rho * vo * 180 + 0.7 * 62.4 * 1.5 * 1.5 * 4 / 2 - rho * 6 * 180 - 62.4 * 180 * 180 / (2 * 36 * wb)) / (rho * va * va / 2);
        var t = r.Tables.Single(x => x.Title.StartsWith("CSU"));
        Assert.All(t.Rows, row => Assert.Equal(req, (double)row[13]!, 2));
        double ya = 1.5 * (0.187 + (fr - 1) * 0.150);   // Figure 4.3 at L = 2B
        var first = t.Rows.First(row => (double)row[3]! == 0.91 && (int)row[1]! == 4);
        Assert.Equal(ya, (double)first[5]!, 3);
        Assert.Equal(0.31 * 0.60 * 4 * 0.91 * ya * 17, (double)first[12]!, 2);   // CB · W1 · h · N
        Assert.Equal(2 * 4 + 6 * 0.91 * ya * 4, (double)first[14]!, 2);
        // Option 2: VB from Eq. 9.1 with the basin's own drag satisfies the equation.
        if (first[17] is double vb2)
        {
            double prov = (double)first[12]!;
            double resid = rho * vo * 180 + 0.7 * 62.4 * 2.25 * 4 / 2 - prov * rho * va * va / 2 - rho * vb2 * 180 - 62.4 * 180 * 180 / (2 * vb2 * vb2 * wb);
            Assert.InRange(resid / (rho * vo * 180), -0.01, 0.01);
        }
    }

    [Fact]
    public void DigitizedFigures_MatchHec14Examples()
    {
        // Figure 9.14/9.15 against the Ch. 9 Type VI examples: Ho/WB 1.55 at Fr 4.74 and 1.68 at 5.07.
        Assert.InRange(StillingBasinTool.Fig914(4.74), 1.53, 1.57);
        Assert.InRange(StillingBasinTool.Fig914(5.07), 1.66, 1.70);
        Assert.InRange(StillingBasinTool.Fig915(4.74), 0.64, 0.66);
        // Figure 8.2 free jump ≈ 6.1 at the §8.2 example's Fr1 7.6.
        Assert.InRange(StillingBasinTool.Curve(StillingBasinTool.Fig82Free, 7.6), 6.0, 6.1);
        // Figure 4.3 at Fr 2.1 against the §9.1 example's 0.33 read from the chart.
        Assert.InRange(StillingBasinTool.Fig43(2.1), 0.33, 0.36);
        // Figure 9.4: rectangular 0.70; circular 0.56 to KuQ/D^2.5 = 3.8, then down to 0.30.
        Assert.Equal(0.70, StillingBasinTool.Fig94(true, 5), 9);
        Assert.Equal(0.56, StillingBasinTool.Fig94(false, 2), 9);
        Assert.Equal(0.30, StillingBasinTool.Fig94(false, 5.9), 2);
    }

    [Fact]
    public void Eq93_AsPrinted()
    {
        // 2Wo·So + yA + 0.25(Q/(WA·yA))²/2g = yo + 0.25·Vo²/2g.
        double q = 180, wo = 4, so = 0.12, yo = 1.5, vo = 30, fr = vo / Math.Sqrt(G * yo), wa = wo * (4 / (3 * fr) + 1);
        double ya = StillingBasinTool.Eq93(q, wo, wa, so, yo, vo, G);
        double lhs = 2 * wo * so + ya + 0.25 * Math.Pow(q / (wa * ya), 2) / (2 * G), rhs = yo + 0.25 * vo * vo / (2 * G);
        Assert.Equal(rhs, lhs, 6);
        Assert.True(ya < Math.Cbrt(2 * 0.25 * q * q / (wa * wa * 2 * G)));   // smaller root
        Assert.True(double.IsNaN(StillingBasinTool.Eq93(q, wo, wa, 2.0, yo, vo, G)));
    }

    [Fact]
    public void CsuTable91_CircularSixRowColumnIsReversed()
    {
        // Each 6-row value sits 0.02 below the 5-row value, as in every other column (D-321).
        foreach (var c in StillingBasinTool.CsuCb.Where(x => x.Circular))
            Assert.Equal(c.Cb[1] - 0.02, c.Cb[2], 9);
    }

    [Fact]
    public void Si_RunsAndScales()
    {
        var us = Ok("Q = 300\nbasin width = 10\ny1 = 1\ntailwater = 7");
        var si = Ok("Q = 8.495\nbasin width = 3.048\ny1 = 0.3048\ntailwater = 2.1336", UnitSystem.SI);
        Assert.Equal((double)Item(us, "Hydraulic jump", "Froude")!, (double)Item(si, "Hydraulic jump", "Froude")!, 1);
        Assert.Equal((double)Item(us, "Hydraulic jump", "Sequent")! * 0.3048, (double)Item(si, "Hydraulic jump", "Sequent")!, 2);
    }

    [Theory]
    [InlineData("Q = 100", "E-SB-001")]
    [InlineData("Q = 100\nbasin width = -2\ny1 = 0.5", "E-SB-003")]
    [InlineData("Q = 0\nbasin width = 5\ny1 = 0.5", "E-SB-002")]
    [InlineData("Q = 100\nbasin width = 5", "E-SB-004")]
    [InlineData("Q = 100\nbasin width = 5\ny1 = 3", "E-SB-009")]
    [InlineData("Q = 100\nbasin width = 5\nenergy above floor = 1", "E-SB-008")]
    [InlineData("Q = 100\nbasin width = 5\ny1 = 0.5\nfloor drop = -1", "E-SB-006")]
    [InlineData("Q = 100\nbasin width = 5\ny1 = 0.5\ntransition slope = 0", "E-SB-006")]
    [InlineData("outlet span = 3\noutlet depth = 1\ncsu Q = 50", "E-SB-012")]
    [InlineData("outlet span = 0\nimpact Q = 50", "E-SB-010")]
    public void BadInput_IsReported(string input, string code)
    {
        var r = Tool.Run(input, Ctx());
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }
}
