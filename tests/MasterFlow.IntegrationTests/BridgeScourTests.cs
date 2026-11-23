using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-18 bridge scour: contraction (live-bed and clear-water), CSU pier and Froehlich/HIRE abutment equations by hand.</summary>
public class BridgeScourTests
{
    private static readonly BridgeScourTool Tool = new();
    private const double G = 32.2;

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => Tool.Run(input, new DesignToolContext(u));

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Run(input, u);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static object? Item(ToolResult r, string item) =>
        r.Tables.Single(t => t.Title.StartsWith("Contraction")).Rows.Single(x => ((string)x[0]!).StartsWith(item))[1];

    private static IReadOnlyList<object?> Row(ToolResult r, string table, string name) =>
        r.Tables.Single(t => t.Title.StartsWith(table)).Rows.Single(x => (string)x[0]! == name);

    [Fact]
    public void CriticalVelocity_MatchesEq61()
    {
        // 2 mm = 0.0065617 ft; Vc = 11.17·8^(1/6)·D50^(1/3).
        double d = 0.002 / 0.3048;
        Assert.Equal(11.17 * Math.Pow(8, 1.0 / 6) * Math.Pow(d, 1.0 / 3), BridgeScourTool.CriticalVelocity(8, d, false), 9);
        Assert.Equal(2.958, BridgeScourTool.CriticalVelocity(8, d, false), 2);
        Assert.Equal(6.19 * Math.Pow(2.4, 1.0 / 6) * Math.Pow(0.002, 1.0 / 3), BridgeScourTool.CriticalVelocity(2.4, 0.002, true), 9);
    }

    [Fact]
    public void FallVelocity_Rubey_IsReasonable()
    {
        // Published fall velocities near 20 °C: 0.2 mm ≈ 0.02–0.03 m/s, 2 mm ≈ 0.14–0.22 m/s (Rubey sits at the low end).
        Assert.InRange(BridgeScourTool.FallVelocity(0.0002, true), 0.018, 0.032);
        Assert.InRange(BridgeScourTool.FallVelocity(0.002, true), 0.13, 0.25);
        // Same physics in U.S. units.
        Assert.Equal(BridgeScourTool.FallVelocity(0.002, true) / 0.3048, BridgeScourTool.FallVelocity(0.002 / 0.3048, false), 2);
    }

    [Theory]
    [InlineData(0.3, 0.59)]
    [InlineData(0.5, 0.64)]
    [InlineData(2.0, 0.64)]
    [InlineData(2.5, 0.69)]
    public void K1_ByVStarOverT(double ratio, double k1) => Assert.Equal(k1, BridgeScourTool.K1(ratio));

    [Fact]
    public void LiveBedContraction_MatchesEq62()
    {
        var r = Ok("D50 = 2\ny1 = 8\nV1 = 5.2\nQ1 = 5000\nW1 = 120\nQ2 = 7000\nW2 = 100\ny0 = 8.5\nS1 = 0.002");
        Assert.StartsWith("live-bed", (string)Item(r, "Condition")!);
        double d = 0.002 / 0.3048, vstar = Math.Sqrt(G * 8 * 0.002), t = BridgeScourTool.FallVelocity(d, false);
        double k1 = BridgeScourTool.K1(vstar / t);
        double y2 = 8 * Math.Pow(7000.0 / 5000, 6.0 / 7) * Math.Pow(120.0 / 100, k1);
        Assert.Equal(k1, (double)Item(r, "k1")!);
        Assert.Equal(y2, (double)Item(r, "y2")!, 2);
        Assert.Equal(y2 - 8.5, (double)Item(r, "Contraction scour")!, 2);
    }

    [Fact]
    public void LiveBedWithoutSlope_AssumesMixedLoad()
    {
        var r = Ok("D50 = 2\ny1 = 8\nV1 = 5.2\nQ1 = 5000\nW1 = 120\nQ2 = 7000\nW2 = 100\ny0 = 8.5");
        Assert.Contains(r.Messages, m => m.Code == "W-BS-001");
        Assert.Equal(8 * Math.Pow(1.4, 6.0 / 7) * Math.Pow(1.2, 0.64), (double)Item(r, "y2")!, 2);
    }

    [Fact]
    public void ClearWaterContraction_MatchesEq64()
    {
        // Coarse gravel, 40 mm: Vc well above V1.
        var r = Ok("D50 = 40\ny1 = 6\nV1 = 4\nQ1 = 3000\nW1 = 125\nQ2 = 4000\nW2 = 90\ny0 = 6");
        Assert.StartsWith("clear-water", (string)Item(r, "Condition")!);
        double dm = 1.25 * 0.040 / 0.3048;
        double y2 = Math.Pow(0.0077 * 4000.0 * 4000 / (Math.Pow(dm, 2.0 / 3) * 90 * 90), 3.0 / 7);
        Assert.Equal(y2, (double)Item(r, "y2")!, 2);
        Assert.Equal(Math.Max(0, y2 - 6), (double)Item(r, "Contraction scour")!, 2);
    }

    [Fact]
    public void ContractionScour_NeverNegative()
    {
        var r = Ok("D50 = 40\ny1 = 6\nV1 = 4\nQ1 = 3000\nW1 = 125\nQ2 = 3000\nW2 = 125\ny0 = 9");
        Assert.Equal(0.0, (double)Item(r, "Contraction scour")!);
        Assert.Contains(r.Messages, m => m.Code == "I-BS-001");
    }

    [Fact]
    public void Pier_Csu_WithSkew_MatchesEq71()
    {
        // Live-bed: K3 from the bed column. Square nose skewed 15° → K1 = 1.0, K2 = (cos15 + 10 sin15)^0.65.
        var r = Ok("D50 = 2\ny1 = 8\nV1 = 5.2\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\tsquare\t3\t30\t15\t9\t7\tmedium dunes");
        var row = Row(r, "Pier", "P");
        double th = 15 * Math.PI / 180, k2 = Math.Pow(Math.Cos(th) + 10 * Math.Sin(th), 0.65), fr = 7 / Math.Sqrt(G * 9);
        double ys = 2.0 * 1.0 * k2 * 1.2 * Math.Pow(9.0 / 3, 0.35) * Math.Pow(fr, 0.43) * 3;
        Assert.Equal(1.0, (double)row[3]!);
        Assert.Equal(k2, (double)row[4]!, 3);
        Assert.Equal(1.2, (double)row[5]!);
        Assert.Equal(ys, (double)row[7]!, 2);
    }

    [Fact]
    public void Pier_LengthRatio_CappedAt12()
    {
        var a = Row(Ok("D50 = 2\ny1 = 8\nV1 = 5.2\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\tround\t2\t40\t30\t8\t6\tplane"), "Pier", "P");
        var b = Row(Ok("D50 = 2\ny1 = 8\nV1 = 5.2\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\tround\t2\t24\t30\t8\t6\tplane"), "Pier", "P");
        Assert.Equal((double)b[4]!, (double)a[4]!);
        double th = Math.PI / 6;
        Assert.Equal(Math.Pow(Math.Cos(th) + 12 * Math.Sin(th), 0.65), (double)a[4]!, 3);
    }

    [Fact]
    public void Pier_ClearWater_UsesK3Of1_1()
    {
        var row = Row(Ok("D50 = 40\ny1 = 6\nV1 = 4\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\tsharp\t2\t2\t0\t6\t4\tlarge dunes"), "Pier", "P");
        Assert.Equal(0.9, (double)row[3]!);
        Assert.Equal(1.1, (double)row[5]!);
    }

    [Fact]
    public void Pier_RoundAligned_CappedAt2_4a()
    {
        // Deep, narrow pier with high Fr below 0.8 would exceed 2.4·a only for very deep flow; force it with depth 200.
        var row = Row(Ok("D50 = 2\ny1 = 8\nV1 = 5.2\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\tround\t1\t1\t0\t200\t60\tlarge dunes"), "Pier", "P");
        double fr = 60 / Math.Sqrt(G * 200);
        Assert.True(fr <= 0.8);
        Assert.Equal(2.4, (double)row[7]!, 2);
        Assert.Contains("limited", (string)row[11]!);
    }

    [Fact]
    public void Pier_WidePierInShallowFlow_AppliesKw()
    {
        // a = 12 ft, y = 6 ft (y/a = 0.5), a/D50 ≫ 50, Fr < 1, V < Vc → Kw = 2.58·(y/a)^0.34·Fr^0.65.
        var row = Row(Ok("D50 = 40\ny1 = 6\nV1 = 4\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\tsquare\t12\t12\t0\t6\t3\tplane"), "Pier", "P");
        double fr = 3 / Math.Sqrt(G * 6), kw = 2.58 * Math.Pow(0.5, 0.34) * Math.Pow(fr, 0.65);
        Assert.Equal(kw, (double)row[6]!, 3);
        Assert.Equal(2.0 * 1.1 * 1.0 * 1.1 * kw * Math.Pow(0.5, 0.35) * Math.Pow(fr, 0.43) * 12, (double)row[7]!, 2);
    }

    [Fact]
    public void Pier_TotalAddsContraction_AndGivesBedElevation()
    {
        var r = Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        double ysC = (double)Item(r, "Contraction scour")!;
        var row = Row(r, "Pier", "Pier 1");
        Assert.Equal((double)row[7]! + ysC, (double)row[9]!, 0.011);
        Assert.Equal(3400 - (double)row[9]!, (double)row[10]!, 2);
    }

    [Fact]
    public void Abutment_Froehlich_MatchesEq81()
    {
        var row = Row(Ok("D50 = 2\ny1 = 8\nV1 = 5.2\n[Abutments]\nName\tType\tL'\tya\tVe\tSkew\nA\tspill-through\t60\t3\t2.5\t60"), "Abutment", "A");
        double fr = 2.5 / Math.Sqrt(G * 3), k2 = Math.Pow(60.0 / 90, 0.13);
        double ys = 3 * (2.27 * 0.55 * k2 * Math.Pow(20, 0.43) * Math.Pow(fr, 0.61) + 1);
        Assert.Equal(k2, (double)row[3]!, 3);
        Assert.Equal(ys, (double)row[6]!, 2);
        Assert.Null(row[7]);
        Assert.Equal(ys, (double)row[8]!, 2);
    }

    [Fact]
    public void Abutment_LongEmbankment_UsesHire()
    {
        var row = Row(Ok("D50 = 2\ny1 = 8\nV1 = 5.2\n[Abutments]\nName\tType\tL'\tya\tVe\tSkew\nA\tvertical wall\t300\t3\t2\t90"), "Abutment", "A");
        double fr = 2 / Math.Sqrt(G * 3), hire = 3 * 4 * Math.Pow(fr, 0.33) * (1.0 / 0.55);
        Assert.Equal(hire, (double)row[7]!, 2);
        Assert.Equal(hire, (double)row[8]!, 2);
        Assert.StartsWith("HIRE", (string)row[10]!);
    }

    [Fact]
    public void Sample_RunsInBothUnitSystems()
    {
        foreach (var u in new[] { UnitSystem.USCustomary, UnitSystem.SI })
        {
            var r = Ok(Tool.SampleInput(new DesignToolContext(u)), u);
            Assert.Equal(3, r.Tables.Count);
            Assert.Contains(r.Summary, s => s.StartsWith("Contraction scour"));
        }
    }

    [Fact]
    public void Si_LiveBed_UsesSiConstants()
    {
        var r = Ok("D50 = 2\ny1 = 2.4\nV1 = 1.6\nQ1 = 140\nW1 = 36\nQ2 = 200\nW2 = 30\ny0 = 2.6\nS1 = 0.002", UnitSystem.SI);
        double vstar = Math.Sqrt(9.81 * 2.4 * 0.002), t = BridgeScourTool.FallVelocity(0.002, true), k1 = BridgeScourTool.K1(vstar / t);
        Assert.Equal(2.4 * Math.Pow(200.0 / 140, 6.0 / 7) * Math.Pow(36.0 / 30, k1), (double)Item(r, "y2")!, 2);
    }

    [Theory]
    [InlineData("y1 = 8\nV1 = 5", "E-BS-001")]
    [InlineData("D50 = 2\nV1 = 5", "E-BS-002")]
    [InlineData("D50 = 2\ny1 = 8\nV1 = 5\nQ2 = 7000", "E-BS-003")]
    [InlineData("D50 = 2\ny1 = 8\nV1 = 5\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\toval\t3\t3\t0\t8\t5\tplane", "E-BS-004")]
    [InlineData("D50 = 2\ny1 = 8\nV1 = 5\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\tround\t3\t3\t0\t8\t5\tripples", "E-BS-005")]
    [InlineData("D50 = 2\ny1 = 8\nV1 = 5\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\tround\t0\t3\t0\t8\t5\tplane", "E-BS-006")]
    [InlineData("D50 = 2\ny1 = 8\nV1 = 5\n[Piers]\nName\tShape\tWidth\tLength\tSkew\tDepth\tVelocity\tBed\nP\tround\t3\t3\t95\t8\t5\tplane", "E-BS-007")]
    [InlineData("D50 = 2\ny1 = 8\nV1 = 5\n[Abutments]\nName\tType\tL'\tya\tVe\tSkew\nA\tgabion\t60\t3\t2\t90", "E-BS-008")]
    [InlineData("D50 = 2\ny1 = 8\nV1 = 5\n[Abutments]\nName\tType\tL'\tya\tVe\tSkew\nA\tspill-through\t60\t0\t2\t90", "E-BS-009")]
    [InlineData("D50 = 2\ny1 = 8\nV1 = 5\n[Abutments]\nName\tType\tL'\tya\tVe\tSkew\nA\tspill-through\t60\t3\t2\t180", "E-BS-010")]
    public void BadInput_ReportsError(string input, string code)
    {
        var r = Run(input);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }
}
