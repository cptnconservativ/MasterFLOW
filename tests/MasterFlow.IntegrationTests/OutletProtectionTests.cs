using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Outlet protection against the HEC-14 (2006) Chapter 1 design examples and hand calcs.</summary>
public class OutletProtectionTests
{
    private static readonly OutletProtectionTool Tool = new();
    private static DesignToolContext Ctx(UnitSystem u = UnitSystem.USCustomary) => new(u);
    private const string Head = "[Outlets]\nName\tShape\tSpan\tRise\tQ\tSlope (%)\tn\tOutlet depth\tTailwater\tChannel V\n";

    private static IReadOnlyList<object?> Row(ToolResult r, string table, string name) => r.Tables.Single(t => t.Title.StartsWith(table)).Rows.Single(x => (string?)x[0] == name);

    [Fact]
    public void RiprapBasin_MatchesHec14Example_CU()
    {
        // HEC-14 §1.2 example: 5 ft × 5 ft RCB, Q = 200 cfs, yo = 2.15 ft, TW = 0 → D50 = 1.2 ft, hs = 2.7 ft,
        // pool 27 ft, apron 13.5 ft, basin 40.5 ft, riprap 3.6 ft (approach) and 2.4 ft.
        var r = Tool.Run(Head + "CV\tbox\t5\t5\t200\t3\t0.012\t2.15\t0\t", Ctx());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var b = Row(r, "Riprap basin", "CV");
        Assert.Equal(1.4, (double)b[1]!);
        Assert.Equal(1.2, (double)b[2]!);
        Assert.Equal(2.7, (double)b[4]!, 1);
        Assert.Equal(27, (double)b[6]!, 0);
        Assert.Equal(13.5, (double)b[7]!, 0);
        Assert.Equal(40.5, (double)b[8]!, 0);
        Assert.Equal(3.6, (double)b[9]!, 2);
        Assert.Equal(2.4, (double)b[10]!, 2);
        var c = Row(r, "Outlet conditions", "CV");
        Assert.InRange((double)c[9]!, 2.2, 2.35);   // the example rounds Vo to 19 ft/s and gives Fro = 2.3
    }

    [Fact]
    public void RiprapBasin_MatchesHec14Example_SI()
    {
        // SI: 1.524 m RCB, Q = 5.66 m³/s, yo = 0.655 m, TW = 0 → D50 = 0.38 m, hs = 0.78 m, pool 7.8 m, apron 3.9 m, basin 11.7 m.
        var r = Tool.Run(Head + "CV\tbox\t1.524\t1.524\t5.66\t3\t0.012\t0.655\t0\t", Ctx(UnitSystem.SI));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var b = Row(r, "Riprap basin", "CV");
        Assert.Equal(0.38, (double)b[2]!, 2);
        Assert.Equal(0.78, (double)b[4]!, 2);
        Assert.Equal(7.8, (double)b[6]!, 1);
        Assert.Equal(3.9, (double)b[7]!, 1);
        Assert.Equal(11.7, (double)b[8]!, 1);
    }

    [Fact]
    public void RiprapApron_HandCalc_AndTable10_1()
    {
        // D = 3 ft, Q = 60 cfs, TW unknown → 0.4·D = 1.2 ft. Q/(√g D^2.5) = 60/(5.672·15.588) = 0.6786;
        // ^(4/3) = 0.5961; D50 = 0.2·3·0.5961·(3/1.2) = 0.894 ft = 10.7 in. → class 4 (14 in.), length 6·D = 18 ft,
        // depth 2.2·14 in. = 2.57 ft; width 9 ft, then 9 + 2·18/3 = 21 ft.
        var r = Tool.Run(Head + "P\tcircular\t3\t\t60\t\t\t1.5\t\t", Ctx());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var a = Row(r, "Riprap apron", "P");
        Assert.Equal(10.7, (double)a[3]!, 1);
        Assert.StartsWith("4 ", (string)a[4]!);
        Assert.Equal(18, (double)a[5]!);
        Assert.Equal(2.57, (double)a[6]!, 2);
        Assert.Equal(9, (double)a[7]!);
        Assert.Equal(21, (double)a[8]!);
        Assert.Contains("TW unknown", (string)a[9]!);
    }

    [Fact]
    public void OutletDepth_SteepBarrelIsNormal_MildIsCriticalOrTailwater()
    {
        var r = Tool.Run(Head + "S\tcircular\t3\t\t40\t2\t0.013\t\t\t\nM\tcircular\t3\t\t40\t0.2\t0.013\t\t\t\nT\tcircular\t3\t\t40\t0.2\t0.013\t\t2.5\t", Ctx());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var s = Row(r, "Outlet conditions", "S"); var m = Row(r, "Outlet conditions", "M"); var t = Row(r, "Outlet conditions", "T");
        Assert.Equal((double)s[4]!, (double)s[5]!);
        Assert.Equal((double)m[3]!, (double)m[5]!);
        Assert.Equal(2.5, (double)t[5]!);
        Assert.StartsWith("tailwater", (string)t[6]!);
    }

    [Fact]
    public void TailwaterChangesC0()
    {
        // ye = 2.15 (box): TW 1.935 → TW/ye 0.9 → C0 = 4·0.9 − 1.6 = 2.0; TW 3 → 2.4.
        var r = Tool.Run(Head + "A\tbox\t5\t5\t200\t\t\t2.15\t1.935\t\nB\tbox\t5\t5\t200\t\t\t2.15\t3\t", Ctx());
        Assert.Equal(2.0, (double)Row(r, "Riprap basin", "A")[1]!, 6);
        Assert.Equal(2.4, (double)Row(r, "Riprap basin", "B")[1]!, 6);
        Assert.Contains(r.Messages, x => x.Code == "W-OP-004");
    }

    [Fact]
    public void Checks_FroudeVelocityRatioSlopeAndPlungePool()
    {
        var r = Tool.Run(Head + "F\tbox\t4\t4\t300\t8\t0.012\t1.2\t0\t3\n[Plunge pool]\nName\tHead\tq\nD\t8\t10", Ctx());
        Assert.Contains(r.Messages, x => x.Code == "W-OP-005");   // Fr > 3
        Assert.Contains(r.Messages, x => x.Code == "W-OP-006");   // Vp > 3.33 Vc
        Assert.Contains(r.Messages, x => x.Code == "W-OP-007");   // slope > 5 %
        // 1.32 · 8^0.225 · 10^0.54 = 1.32 · 1.5967 · 3.4674 = 7.31 ft
        Assert.Equal(7.31, (double)r.Tables.Single(t => t.Title.StartsWith("Plunge")).Rows[0][3]!, 2);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        Assert.Contains(Tool.Run("x = 1", Ctx()).Messages, m => m.Code == "E-OP-004");
        Assert.Contains(Tool.Run(Head + "X\toval\t3\t\t40\t\t\t\t\t", Ctx()).Messages, m => m.Code == "E-OP-001");
        Assert.Contains(Tool.Run(Head + "X\tcircular\tabc\t\t40\t\t\t\t\t", Ctx()).Messages, m => m.Severity == MessageSeverity.Error);
    }
}
