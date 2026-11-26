using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Sanitary sewer network design (Rapid City IDCM 2012 §3.12, Ten States Standards): flows, hydraulics, design and checks.</summary>
public class SanitarySewerTests
{
    private static readonly SanitarySewerTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);

    private static ToolResult Run(string input, DesignToolContext? ctx = null) => Tool.Run(input, ctx ?? Us);
    private static IReadOnlyList<object?> Row(ToolResult r, string table, string pipe) => r.Tables.First(t => t.Title.StartsWith(table)).Rows.First(x => (string?)x[0] == pipe);
    private static double Num(object? v) => Convert.ToDouble(v);

    private const string Head = """
        criteria = Rapid City
        h2s = no

        """;

    [Fact]
    public void Sample_RunsClean_AndSiIsRefused()
    {
        var r = Run(Tool.SampleInput(Us));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.NotEmpty(r.Figures);
        Assert.False(Run(Tool.SampleInput(Us), new DesignToolContext(UnitSystem.SI)).Succeeded);
    }

    [Fact]
    public void ResidentialFlow_ByHand_IdcmEquation()
    {
        // 20 ac at 4 DU/ac: 2.65 × 4 × 20 = 212 people; Qave = 100 × 212 / 1440 = 14.72 gpm; peak × 4.2 (RF 1.0 below 500 people).
        var r = Run(Head + """
            [Manholes]
            Name	Rim
            A	100
            B	98
            [Loads]
            Manhole	Land use	Area (ac)	DU/ac
            A	Residential	20	4
            [Pipes]
            Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert
            P1	A	B	304	8	94.0	92.0
            """);
        Assert.True(r.Succeeded);
        var f = Row(r, "Design flows", "P1");
        Assert.Equal(212, Num(f[1]), 0);
        Assert.Equal(100 * 2.65 * 4 * 20 / 1440, Num(f[10]), 2);
        Assert.Equal(100 * 2.65 * 4 * 20 / 1440 * 4.2, Num(f[12]), 2);
        // Slope from inside wall to inside wall: (94 − 92)/(304 − 4) = 0.667 %.
        Assert.Equal(2.0 / 300 * 100, Num(Row(r, "Sanitary sewer design", "P1")[11]), 3);
    }

    [Theory]
    [InlineData(500, 1.0)]
    [InlineData(501, 0.955)]
    [InlineData(1500, 0.905)]
    [InlineData(4000, 0.86)]
    [InlineData(10000, 0.775)]
    [InlineData(20000, 0.72)]
    public void ResidentialReductionFactors_IdcmTable(double pop, double rf) => Assert.Equal(rf, SanitarySewerTool.ResidentialRf(pop));

    [Fact]
    public void ReductionFactor_AppliesToAccumulatedPopulation_AndCommercialSeparately()
    {
        // 700 people upstream: RF 0.955 on residential; 10 ac commercial: 30 gpm × 1.7, RF 1.0.
        var r = Run(Head + """
            [Loads]
            Manhole	Land use	Area (ac)	DU/ac	Population
            A	Residential			400
            B	Residential			300
            B	Commercial	10
            [Pipes]
            Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert
            P1	A	B	300	8	100	98
            P2	B	C	300	10	97.8	96
            """);
        Assert.True(r.Succeeded);
        var f1 = Row(r, "Design flows", "P1");
        Assert.Equal(1.0, Num(f1[3]));
        var f2 = Row(r, "Design flows", "P2");
        Assert.Equal(0.955, Num(f2[3]));
        double res = 700 * 100 / 1440.0;
        Assert.Equal(res * 4.2 * 0.955 + 30 * 1.7, Num(f2[12]), 2);
        Assert.Equal(res + 30, Num(f2[10]), 2);
    }

    [Fact]
    public void Harmon_TenStates_ByHand()
    {
        Assert.Equal(1 + 14 / (4 + Math.Sqrt(1.0)), SanitarySewerTool.Harmon(1), 9);
        var r = Run("""
            criteria = Ten States
            h2s = no
            [Loads]
            Manhole	Land use	Area (ac)	DU/ac	Population
            A	Residential			1000
            [Pipes]
            Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert
            P1	A	B	300	8	100	98
            """);
        Assert.True(r.Succeeded);
        double qave = 1000 * 100 / 1440.0;
        Assert.Equal(qave * 3.8, Num(Row(r, "Design flows", "P1")[12]), 2);
    }

    [Fact]
    public void PartialFlow_MatchesNetworkSolverVelocity_AndHalfFullEqualsFull()
    {
        double d = 1.0, n = 0.013, s = 0.004;
        double qf = SanitarySewerTool.QFull(d, n, s);
        // Half full carries half the full flow at the full-flow velocity.
        Assert.Equal(qf / 2, SanitarySewerTool.Discharge(0.5, d, n, s), 9);
        Assert.Equal(0.5, SanitarySewerTool.NormalDepth(qf / 2, d, n, s), 6);
        double q = 0.3 * qf, y = SanitarySewerTool.NormalDepth(q, d, n, s);
        Assert.Equal(NetworkSizer.NormalVelocity(d, n, s, q, UnitConstants.For(UnitSystem.USCustomary)), q / SanitarySewerTool.Area(y, d), 3);
        Assert.True(double.IsNaN(SanitarySewerTool.NormalDepth(1.1 * qf, d, n, s)));
    }

    [Fact]
    public void TenStatesSlopes_TableAndFallback()
    {
        Assert.Equal(0.0040, SanitarySewerTool.TenStatesMinSlope(8), 9);
        Assert.Equal(0.0022, SanitarySewerTool.TenStatesMinSlope(12), 9);
        // 48 in. is not in the table: slope for 2 ft/s full with n = 0.013.
        double s48 = SanitarySewerTool.TenStatesMinSlope(48);
        Assert.Equal(2.0, SanitarySewerTool.QFull(4, 0.013, s48) / (Math.PI * 4), 6);
    }

    [Fact]
    public void Checks_FlagEachCriterion()
    {
        // P1: 6 in. at 0.2 % with n 0.012, 450 ft long; P2: 12 in. into 8 in. (larger upstream), no drop, crown above inlet crown.
        var r = Run(Head + """
            [Manholes]
            Name	Rim
            A	106
            B	104
            C	100
            [Loads]
            Manhole	Land use	Population
            A	Residential	300
            [Pipes]
            Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n
            P1	A	B	454	12	100.00	99.10	0.012
            P2	B	C	300	8	99.10	97.00
            """);
        Assert.True(r.Succeeded);
        var codes = r.Messages.Select(m => m.Code).ToHashSet();
        Assert.Contains("W-SS-002", codes);   // velocity at peak below 2 ft/s
        Assert.Contains("W-SS-003", codes);   // below Ten States 0.22 % for 12 in.
        Assert.Contains("W-SS-006", codes);   // 12 in. into 8 in.
        Assert.Contains("W-SS-007", codes);   // n < 0.013
        Assert.Contains("W-SS-008", codes);   // 454 ft > 400 ft
        Assert.Contains("W-SS-009", codes);   // cover at A: 106 − 100 − 1 = 5.0 OK, at B: 104 − 99.1 − 1 = 3.9
        Assert.Contains("W-SS-010", codes);   // zero drop at B
        Assert.DoesNotContain(r.Messages, m => m.Code == "W-SS-005");
    }

    [Fact]
    public void DropManhole_AndSteepSewer_AreFlagged()
    {
        var r = Run(Head + """
            [Loads]
            Manhole	Land use	Population
            A	Residential	200
            [Pipes]
            Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert
            P1	A	B	204	8	150	100
            P2	B	C	304	8	97.5	96
            """);
        var codes = r.Messages.Select(m => m.Code).ToHashSet();
        Assert.Contains("W-SS-012", codes);   // 2.5 ft drop at B
        Assert.Contains("W-SS-004", codes);   // 25 % slope: over 10 ft/s
        Assert.Contains("W-SS-013", codes);   // over 20 %: anchors
    }

    [Fact]
    public void Design_SizesForDdAndSetsInvertsForCoverDropAndCrowns()
    {
        // A large flow forces a size above 8 in.; inverts come from 5 ft cover and the drop rule.
        var r = Run(Head + """
            [Manholes]
            Name	Rim
            A	3410
            B	3408
            C	3406
            [Loads]
            Manhole	Land use	Area (ac)	DU/ac	Population	Flow (gpm)	PF
            A	Flow				400	1
            B	Flow				300	1
            [Pipes]
            Name	From	To	Length (ft)
            P1	A	B	400
            P2	B	C	400
            """);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var d1 = Row(r, "Designed sizes", "P1");
        var d2 = Row(r, "Designed sizes", "P2");
        double D1 = Num(d1[1]), D2 = Num(d2[1]);
        Assert.True(D1 > 8 && D2 >= D1);
        // Upstream invert at 5 ft cover.
        Assert.Equal(3410 - 5 - D1 / 12, Num(d1[2]), 2);
        // Each design meets d/D ≤ 0.70 and the drop at B is at least 0.10 ft, with crowns matched.
        foreach (var p in new[] { "P1", "P2" }) Assert.True(Num(Row(r, "Pipe checks", p)[8]) <= 0.70 + 1e-9);
        Assert.True(Num(d1[3]) - Num(d2[2]) >= 0.10 - 0.005);
        Assert.True(Num(d2[2]) + D2 / 12 <= Num(d1[3]) + D1 / 12 + 0.005);
        Assert.DoesNotContain(r.Messages, m => m.Code is "W-SS-001" or "W-SS-002" or "W-SS-003" or "W-SS-009" or "W-SS-010" or "W-SS-011");
    }

    [Fact]
    public void H2S_ZByHand_IdcmAndPomeroy()
    {
        double z = SanitarySewerTool.ZValue(300, 0.004, 0.1, 1.5, 1.0, pomeroy: false);
        Assert.Equal(300 / (Math.Sqrt(0.004) * Math.Cbrt(0.1)) * 1.5, z, 6);
        Assert.Equal(z * 3 / 10, SanitarySewerTool.ZValue(300, 0.004, 0.1, 1.5, 1.0, pomeroy: true), 6);
        var r = Run(Tool.SampleInput(Us).Replace("criteria = Rapid City", "criteria = Rapid City\nh2s formula = Pomeroy"));
        Assert.Contains(r.Tables, t => t.Title.StartsWith("Hydrogen sulfide"));
        Assert.Contains(r.Messages, m => m.Code == "I-SS-004" && m.Text.Contains("Pomeroy (EPA 1974"));
    }

    [Fact]
    public void Errors_AreReported_NotGuessed()
    {
        Assert.Contains(Run(Head + "[Pipes]\nName\tFrom\tTo\tLength (ft)\tDiameter (in)\tUp invert\tDown invert\nP1\tA\tB\t300\t8\t100\n").Messages, m => m.Code == "E-SS-004");
        Assert.Contains(Run(Head + "[Pipes]\nName\tFrom\tTo\tLength (ft)\nP1\tA\tB\t300\nP2\tA\tC\t300\n").Messages, m => m.Code == "E-SS-005");
        Assert.Contains(Run(Head + "[Pipes]\nName\tFrom\tTo\tLength (ft)\tDiameter (in)\tUp invert\tDown invert\nP1\tA\tB\t300\t8\t100\t99\nP2\tB\tA\t300\t8\t99\t98\n").Messages, m => m.Code == "E-SS-006");
        Assert.Contains(Run(Head + "[Pipes]\nName\tFrom\tTo\tLength (ft)\nP1\tA\tB\t300\n").Messages, m => m.Code == "E-SS-008");
        Assert.Contains(Run(Head + "[Pipes]\nName\tFrom\tTo\tLength (ft)\tDiameter (in)\tUp invert\tDown invert\nP1\tA\tB\t300\t8\t99\t100\n").Messages, m => m.Code == "E-SS-010");
        Assert.Contains(Run(Head + "[Loads]\nManhole\tLand use\tArea (ac)\nA\tFarm\t3\n[Pipes]\nName\tFrom\tTo\tLength (ft)\tDiameter (in)\tUp invert\tDown invert\nP1\tA\tB\t300\t8\t100\t99\n").Messages, m => m.Code == "E-SS-007");
        Assert.Contains(Run("criteria = Chicago\n[Pipes]\nName\tFrom\tTo\tLength (ft)\tDiameter (in)\tUp invert\tDown invert\nP1\tA\tB\t300\t8\t100\t99\n").Messages, m => m.Code == "E-SS-009");
    }

    [Fact]
    public void SelectedNetwork_IsChecked_AndRedesignWritesBack()
    {
        var net = NetworkOutputTests.NetA();
        var ctx = new DesignToolContext(UnitSystem.USCustomary, net, StormNetworkSolver.Solve(net));
        string loads = "[Loads]\nManhole\tLand use\tPopulation\nCB-3\tResidential\t400\nCB-4\tResidential\t200\n";
        var check = Run("h2s = no\n" + loads, ctx);
        Assert.True(check.Succeeded, string.Join("\n", check.Messages.Select(m => m.Text)));
        Assert.Null(check.UpdatedNetwork);
        Assert.Equal(3, check.Tables[0].Rows.Count);
        // NetA rims are only 2–5 ft above its inverts, so the redesign uses 2 ft cover.
        var design = Run("h2s = no\nredesign = sizes and inverts\nminimum cover = 2\n" + loads, ctx);
        Assert.True(design.Succeeded, string.Join("\n", design.Messages.Select(m => m.Text)));
        Assert.NotNull(design.UpdatedNetwork);
        var p1 = design.UpdatedNetwork!.Pipes.Single(x => x.Name == "P1");
        Assert.Equal(8.0 / 12, p1.Rise, 6);
        Assert.True(p1.Discharge > 0);
    }
}
