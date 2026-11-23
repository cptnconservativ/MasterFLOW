using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Hydraulic jump and stilling basins: closed forms, IDCM Table 4-15 and Figure 4-8, Eq. 4-9-3 by hand.</summary>
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

    [Fact]
    public void Jump_Rectangle_MatchesBelanger()
    {
        // b = 10, y1 = 1, V1 = 30 → Q = 300; Fr1 = 30/√32.174 = 5.289; y2 = ½(√(1 + 8Fr²) − 1).
        var r = Ok("Q = 300\nbasin width = 10\ny1 = 1\ntailwater = 7");
        double fr = 30 / Math.Sqrt(G), y2 = 0.5 * (Math.Sqrt(1 + 8 * fr * fr) - 1);
        Assert.Equal(fr, (double)Item(r, "Hydraulic jump", "Froude")!, 2);
        Assert.Equal(y2, (double)Item(r, "Hydraulic jump", "Sequent")!, 3);
        Assert.Equal(Math.Pow(y2 - 1, 3) / (4 * y2), (double)Item(r, "Hydraulic jump", "Energy loss")!, 3);
        Assert.Equal(6.9 * (y2 - 1), (double)Item(r, "Hydraulic jump", "Jump length")!, 2);
        Assert.Equal("steady", Item(r, "Hydraulic jump", "Jump type"));
        Assert.StartsWith("TW ≈ y2", (string)Item(r, "Hydraulic jump", "Jump position")!);
    }

    [Fact]
    public void Jump_Trapezoid_BalancesSpecificForce()
    {
        var s = new StillingBasinTool.Trap(6, 2);
        double q = 400, y1 = 0.8;
        double y2 = StillingBasinTool.Sequent(s, q, y1, G);
        Assert.Equal(StillingBasinTool.Momentum(s, q, y1, G), StillingBasinTool.Momentum(s, q, y2, G), 6);
        Assert.True(y2 > StillingBasinTool.Critical(s, q, G));
        var r = Ok("Q = 400\nbasin width = 6\nside slope = 2\ny1 = 0.8\ntailwater = 5");
        Assert.Equal(y2, (double)Item(r, "Hydraulic jump", "Sequent")!, 3);
        Assert.Contains(r.Messages, m => m.Code == "I-SB-001");
        Assert.DoesNotContain(r.Tables, t => t.Title.StartsWith("Stilling basins"));
    }

    [Fact]
    public void FloorDrop_HoldsJump_ForEachBasin()
    {
        var r = Ok("Q = 200\nbasin width = 10\napproach slope = 8\napproach n = 0.013\ntailwater = 2.5");
        var t = r.Tables.Single(x => x.Title.StartsWith("Stilling basins"));
        foreach (var row in t.Rows)
        {
            double need = (double)row[7]!, drop = (double)row[8]!, have = (double)row[9]!;
            Assert.Equal(need, have, 2);
            Assert.InRange(have - 2.5 - drop, -0.006, 0.006);
            Assert.True(drop > 0);
        }
        // SAF: TW factor and length by Blaisdell.
        var saf = t.Rows.Single(x => (string)x[0]! == "SAF");
        double fr = (double)saf[4]!, y2 = (double)saf[5]!;
        Assert.Equal(StillingBasinTool.TailwaterFactor("SAF", fr), (double)saf[6]!, 3);
        Assert.Equal(4.5 * y2 / Math.Pow(fr, 0.76), (double)saf[11]!, 1);
    }

    [Fact]
    public void SafTailwaterFactor_Ranges()
    {
        Assert.Equal(1.10 - 4.0 / 120, StillingBasinTool.TailwaterFactor("SAF", 2), 9);
        Assert.Equal(0.85, StillingBasinTool.TailwaterFactor("SAF", 8), 9);
        Assert.Equal(1.00 - 144.0 / 800, StillingBasinTool.TailwaterFactor("SAF", 12), 9);
        Assert.Equal(1.05, StillingBasinTool.TailwaterFactor("Type II", 8), 9);
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
    public void ImpactBasin_ReproducesTable4_15Row()
    {
        // Q = 85 cfs is the 36-in. row: W = 9'-3" = 9.25 ft. A slow outlet keeps the Fr width below it.
        var r = Ok("outlet shape = box\noutlet span = 6\noutlet rise = 4\noutlet depth = 3\nimpact Q = 85");
        Assert.Equal(9.25, (double)Item(r, "USBR Type VI", "W from Q")!, 2);
        Assert.Equal(9.25, (double)Item(r, "USBR Type VI", "Design W")!, 2);
        Assert.Equal(0.75 * 9.25, (double)Item(r, "USBR Type VI", "H = 3/4")!, 2);
        Assert.Equal(9.25 * 4 / 3, (double)Item(r, "USBR Type VI", "L = 4/3")!, 2);
        var row = r.Tables.Single(t => t.Title.StartsWith("IDCM Table 4-15")).Rows.Single();
        Assert.Equal(36.0, row[0]);
        Assert.Equal("9-3", row[2]);
        Assert.Equal("12-4", row[4]);
    }

    [Fact]
    public void ImpactBasin_FroudeWidthGoverns_AndLimits()
    {
        // Box 3 ft wide at 1 ft depth, 120 cfs: A = 3, V = 40, D = 1.732, Fr = 40/√(g·1.732) = 5.357; W = 2.88·Fr^0.566·D.
        var r = Ok("outlet shape = box\noutlet span = 3\noutlet rise = 3\noutlet depth = 1\nimpact Q = 120");
        double d = Math.Sqrt(3), fr = 40 / Math.Sqrt(G * d);
        Assert.Equal(2.88 * Math.Pow(fr, 0.566) * d, (double)Item(r, "USBR Type VI", "Design W")!, 2);
        var big = Ok("outlet shape = circular\noutlet span = 6\noutlet depth = 3\nimpact Q = 500");
        Assert.Contains(big.Messages, m => m.Code == "W-SB-006");
        var two = Ok("outlet shape = circular\noutlet span = 6\noutlet depth = 3\nimpact Q = 500\nimpact basins = 2");
        Assert.DoesNotContain(two.Messages, m => m.Code == "W-SB-006");
        Assert.Equal(250.0, (double)Item(two, "USBR Type VI", "Q per basin")!);
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
    public void CsuBasin_MomentumEquationByHand()
    {
        // Box Wo = 4, Yo = 1.5, Q = 180 → Vo = 30, Fr = 30/√(g·1.5) = 4.317; VA = (1.65 − 0.3Fr)Vo.
        var r = Ok("outlet shape = box\noutlet span = 4\noutlet depth = 1.5\ncsu Q = 180\nexit velocity = 6\nexpansion ratio = 6");
        double vo = 30, fr = vo / Math.Sqrt(G * 1.5), va = (1.65 - 0.3 * fr) * vo, wb = 24, rho = 62.4 / G;
        double req = (rho * vo * 180 + 0.7 * 62.4 * 1.5 * 1.5 * 4 / 2 - rho * 6 * 180 - 62.4 * 180 * 180 / (2 * 36 * wb)) / (rho * va * va / 2);
        var t = r.Tables.Single(x => x.Title.StartsWith("CSU"));
        Assert.All(t.Rows, row => Assert.Equal(req, (double)row[13]!, 2));
        double ya = 1.5 * (0.19 + (fr - 1) * 0.145);
        var first = t.Rows.First(row => (double)row[3]! == 0.91 && (int)row[1]! == 4);
        Assert.Equal(ya, (double)first[5]!, 3);
        Assert.Equal(0.31 * 0.60 * 4 * 0.91 * ya * 17, (double)first[12]!, 2);   // CB · W1 · h · N
        Assert.Equal(2 * 4 + 6 * 0.91 * ya * 4, (double)first[14]!, 2);
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
    [InlineData("outlet span = 3\noutlet depth = 1\ncsu Q = 50", "E-SB-012")]
    [InlineData("outlet span = 0\nimpact Q = 50", "E-SB-010")]
    public void BadInput_IsReported(string input, string code)
    {
        var r = Tool.Run(input, Ctx());
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }
}
