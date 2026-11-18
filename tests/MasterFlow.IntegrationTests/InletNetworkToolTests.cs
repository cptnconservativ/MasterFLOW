using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// HEC-22 inlet capacity and bypass routing. Expected values come from validation/hand_calcs_inlets.py, written
/// separately from the published equations (output in validation/hand_calcs_inlets_output.txt). Tolerance 0.001.
/// </summary>
public class InletNetworkToolTests
{
    private const string Street = "street class = collector\npavement width = 40\ncurb height = 0.5\ncross slope = 0.02\nn = 0.016\n";

    // Column indexes of the main table.
    private const int QLocal = 2, QIn = 3, QGutter = 4, Spread = 5, Depth = 6, E = 9, Qi = 10, Bypass = 11, To = 12, Check = 14;

    /// <summary>CB-A → CB-B → CB-C → MH-1 → OUT, so blank Bypass to follows the pipes.</summary>
    internal static StormNetwork Chain() => new()
    {
        Name = "Chain",
        Structures =
        [
            new() { Name = "OUT", Kind = "Outlet", RimElevation = 100 },
            new() { Name = "MH-1", Kind = "Manhole", RimElevation = 104 },
            new() { Name = "CB-A", Kind = "Inlet", RimElevation = 108 },
            new() { Name = "CB-B", Kind = "Inlet", RimElevation = 107 },
            new() { Name = "CB-C", Kind = "Inlet", RimElevation = 106 },
        ],
        Pipes =
        [
            P("P1", "CB-A", "CB-B", 103, 102.5), P("P2", "CB-B", "CB-C", 102.4, 101.9),
            P("P3", "CB-C", "MH-1", 101.8, 101.3), P("P4", "MH-1", "OUT", 101.2, 100.5),
        ],
        Tailwater = TailwaterRule.HalfCriticalPlusDiameter,
    };

    private static NetworkPipe P(string name, string from, string to, double up, double dn) =>
        new() { Name = name, From = from, To = to, Rise = 1.5, Span = 1.5, Length = 100, UpstreamInvert = up, DownstreamInvert = dn, ManningN = 0.013, Discharge = double.NaN };

    private static DesignToolContext Ctx(StormNetwork? n = null, UnitSystem u = UnitSystem.USCustomary) => new(u, n ?? Chain());

    private static ToolResult Run(string input, DesignToolContext? ctx = null)
    {
        var r = new InletNetworkTool().Run(input, ctx ?? Ctx());
        return r;
    }

    private static IReadOnlyList<object?> Row(ToolResult r, string inlet, int table = 0) => r.Tables[table].Rows.Single(x => (string)x[0]! == inlet);
    private static double D(IReadOnlyList<object?> row, int i) => (double)row[i]!;
    private static string Msgs(ToolResult r) => string.Join("\n", r.Messages.Select(m => $"{m.Code} {m.Text}"));

    private const string Case1 = Street + """
        [Inlets]
        Inlet	Type	Location	Q (cfs)	S	L	W	a (in)	Grate	Area (ft2)	h
        CB-C	grate	sag	1.5		3	2		P-50x100	3.2
        CB-B	grate	grade	2.5	0.012	3	2		P-50x100
        CB-A	curb	grade	4	0.015	10	2	2
        """;

    [Fact]
    public void Case1_CurbThenGrateThenSag_MatchesHandCalc_AndFollowsPipes()
    {
        var r = Run(Case1);
        Assert.True(r.Succeeded, Msgs(r));
        var rows = r.Tables[0].Rows;
        Assert.Equal(["CB-A", "CB-B", "CB-C"], rows.Select(x => (string)x[0]!));      // flow order, not input order

        var a = Row(r, "CB-A");
        Assert.Equal(11.2563, D(a, Spread), 3);
        Assert.Equal(0.68753, D(a, E), 4);
        Assert.Equal(2.75014, D(a, Qi), 3);
        Assert.Equal(1.24986, D(a, Bypass), 3);
        Assert.Equal("CB-B", a[To]);

        var b = Row(r, "CB-B");
        Assert.Equal(1.24986, D(b, QIn), 3);
        Assert.Equal(3.74986, D(b, QGutter), 3);
        Assert.Equal(11.4562, D(b, Spread), 3);
        Assert.Equal(0.44415, D(b, E), 4);           // Vo 4.3738 at Le 1.8 ft (40 % clogged), Rs 0.07225, Eo 0.4009
        Assert.Equal(1.66550, D(b, Qi), 3);

        var c = Row(r, "CB-C");
        Assert.Equal(3.58436, D(c, QGutter), 3);
        Assert.Equal(0.36880, D(c, Depth), 4);       // weir, P = 1.8 + 2·2; depth at curb = grate average + Sx·W/2
        Assert.Equal(18.4399, D(c, Spread), 2);
        Assert.Equal(3.58436, D(c, Qi), 3);
        Assert.StartsWith("FAILS", (string)c[Check]!); // 18.4 ft > (40 − 12)/2 = 14 ft
        Assert.Contains(r.Messages, m => m.Code == "W-IN-002");

        // Mass balance: everything intercepted (the last inlet is a sag).
        Assert.Equal(8.0, rows.Sum(x => D(x, Qi)), 9);
        Assert.Contains("pipes CB-A → CB-B", DesignTools.ToText(r));

        // Apply: inflow = intercepted flow at each inlet.
        var net = r.UpdatedNetwork!;
        Assert.Equal(2.750, net.Structures.Single(s => s.Name == "CB-A").Inflow!.Value, 3);
        Assert.Equal(1.666, net.Structures.Single(s => s.Name == "CB-B").Inflow!.Value, 3);
        Assert.Equal(3.584, net.Structures.Single(s => s.Name == "CB-C").Inflow!.Value, 3);
        Assert.Null(net.Structures.Single(s => s.Name == "MH-1").Inflow);
    }

    [Fact]
    public void Case1_AgreesWithTheExistingGutterInletTool_ForTheCurbOpening()
    {
        var old = new GutterInletTool().Run("street class = collector\npavement width = 40\ncurb height = 0.5\ncross slope = 0.02\nn = 0.016\n[Inlets]\nI\tT\tL\tQ\tS\tL\tW\ta\tVo\tA\th\nCB-A\tcurb\tgrade\t4\t0.015\t10\t2\t2\t\t\t0.5", DesignToolTests.Ctx());
        var r = Run(Case1);
        Assert.Equal((double)old.Tables[0].Rows[0][8]!, D(Row(r, "CB-A"), Qi), 9);
    }

    [Fact]
    public void Case2_CompositeGutter_CurbOpeningOnGrade_MatchesHandCalc()
    {
        var r = Run(Street + "gutter width = 2\ngutter slope = 0.0833\n[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\tBypass to\nCB-A\tcurb\tgrade\t4\t0.015\t10\tout");
        Assert.True(r.Succeeded, Msgs(r));
        var a = Row(r, "CB-A");
        Assert.Equal(10.21886, D(a, Spread), 3);
        Assert.Equal(0.33098, D(a, Depth), 4);
        Assert.Equal(0.69907, D(a, E), 4);
        Assert.Equal(2.79626, D(a, Qi), 3);
        Assert.Equal("out", a[To]);
        Assert.Contains(r.Messages, m => m.Code == "W-IN-003");     // bypass leaves the system
    }

    [Theory]
    [InlineData(3.0, 0.34200, "weir")]
    [InlineData(6.0, 0.53756, "transition")]
    [InlineData(12.0, 1.04762, "orifice")]
    public void Case3_SagCurbOpening_WeirTransitionOrifice(double q, double d, string regime)
    {
        var r = Run(Street.Replace("collector", "local") + $"[Inlets]\nInlet\tType\tLocation\tQ\tL\th\nCB-C\tcurb\tsag\t{q}\t5\t0.5");
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Equal(d, D(Row(r, "CB-C"), Depth), 4);
        Assert.Contains(regime, (string)Row(r, "CB-C", 1)[1]!);
    }

    [Theory]
    [InlineData(2.0, 0.25086, "weir")]
    [InlineData(12.0, 0.65910, "orifice")]
    public void Case4_SagCombination_GrateAloneInWeir_GratePlusCurbInOrifice(double q, double d, string regime)
    {
        var r = Run(Street.Replace("collector", "local") + $"[Inlets]\nInlet\tType\tLocation\tQ\tL\tW\tArea\th\nCB-C\tcombination\tsag\t{q}\t3\t2\t3.2\t0.5");
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Equal(d, D(Row(r, "CB-C"), Depth), 4);
        Assert.Contains(regime, (string)Row(r, "CB-C", 1)[1]!);
    }

    [Fact]
    public void TwoInletsBypassToOne_AreSummed_AndSolvedFirst()
    {
        var r = Run(Street + "[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\tW\th\tBypass to\nX1\tcurb\tgrade\t3\t0.02\t5\t\t\tCB-C\nX2\tcurb\tgrade\t2\t0.02\t5\t\t\tCB-C\nCB-C\tcurb\tsag\t1\t\t10\t\t0.5\t");
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Equal("CB-C", (string)r.Tables[0].Rows[^1][0]!);
        double by = D(Row(r, "X1"), Bypass) + D(Row(r, "X2"), Bypass);
        Assert.True(by > 0);
        Assert.Equal(by, D(Row(r, "CB-C"), QIn), 9);
        Assert.Equal(6.0, r.Tables[0].Rows.Sum(x => D(x, Qi)), 9);
    }

    [Fact]
    public void MinorStorm_RoutedSeparately_AndAppliedAsMinorInflow()
    {
        var r = Run(Street + "[Inlets]\nInlet\tType\tLocation\tQ\tQ minor\tS\tL\nCB-A\tcurb\tgrade\t4\t1.5\t0.015\t5\nCB-B\tcurb\tgrade\t1\t0.5\t0.015\t5");
        Assert.True(r.Succeeded, Msgs(r));
        Assert.StartsWith("Minor storm", r.Tables[2].Title);
        var a = r.Tables[2].Rows.Single(x => (string)x[0]! == "CB-A");
        Assert.Equal(1.5, D(a, QGutter), 9);
        var cb = r.UpdatedNetwork!.Structures.Single(s => s.Name == "CB-A");
        Assert.Equal(Math.Round(D(a, Qi), 3), cb.MinorInflow!.Value, 9);
        Assert.True(cb.Inflow > cb.MinorInflow);
    }

    [Fact]
    public void CombinationOnGrade_EqualLengths_IsTheGrate_SweeperAddsInterception()
    {
        const string head = Street + "[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\tW\tGrate\tLc\tBypass to\n";
        var grate = Run(head + "CB-A\tgrate\tgrade\t4\t0.01\t3\t2\tP-50\t\tout");
        var combo = Run(head + "CB-A\tcombination\tgrade\t4\t0.01\t3\t2\tP-50\t\tout");
        var sweep = Run(head + "CB-A\tcombination\tgrade\t4\t0.01\t3\t2\tP-50\t8\tout");
        Assert.True(sweep.Succeeded, Msgs(sweep));
        Assert.Equal(D(Row(grate, "CB-A"), Qi), D(Row(combo, "CB-A"), Qi), 9);
        Assert.True(D(Row(sweep, "CB-A"), Qi) > D(Row(combo, "CB-A"), Qi));
    }

    [Fact]
    public void SplashOver_PolynomialAndBeyondFourFeet()
    {
        var g = InletHydraulics.FindGrate("p-1-7/8")!;
        Assert.Equal("P-50", g.Key);
        Assert.Equal(2.22 + 4.03 * 2 - 0.65 * 4 + 0.06 * 8, g.SplashOver(2), 12);
        Assert.Equal(g.SplashOver(4) + 0.4, g.SplashOver(6), 12);
        Assert.Equal(1, InletHydraulics.FrontalInterception(3, 5));
        Assert.Equal(0, InletHydraulics.FrontalInterception(30, 5));   // never negative
    }

    [Fact]
    public void BypassLoop_IsAnError()
    {
        var r = Run(Street + "[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\tBypass to\nA\tcurb\tgrade\t3\t0.02\t5\tB\nB\tcurb\tgrade\t3\t0.02\t5\tA");
        Assert.Contains(r.Messages, m => m.Code == "E-IN-029");
        Assert.Empty(r.Tables);
        Assert.Null(r.UpdatedNetwork);
    }

    [Theory]
    [InlineData("CB-A\tcurb\tgrade\t-1\t0.015\t5", "E-IN-014")]          // negative Q
    [InlineData("CB-A\tcurb\tgrade\t1\t0\t5", "E-IN-019")]               // flat grade on grade
    [InlineData("CB-A\tcurb\tgrade\t1\t-0.01\t5", "E-IN-019")]           // adverse grade
    [InlineData("CB-A\tcurb\tgrade\t1\t0.01\t0", "E-IN-015")]            // zero length
    [InlineData("CB-A\tgrate\tgrade\t1\t0.01\t3", "E-IN-022")]           // grate without W
    [InlineData("CB-A\tweir\tgrade\t1\t0.01\t3", "E-IN-012")]
    [InlineData("CB-A\tcurb\tsump\t1\t0.01\t3", "E-IN-013")]
    [InlineData("CB-A\tcurb\tgrade\tabc\t0.01\t3", "E-TL-003")]
    public void BadRows_AreErrors_WithNoNumbers(string row, string code)
    {
        var r = Run(Street + "[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\n" + row);
        Assert.Contains(r.Messages, m => m.Code == code);
        Assert.False(r.Succeeded);
        Assert.Empty(r.Tables);
    }

    [Fact]
    public void GrateChecks_UnknownGrate_MissingVo_MissingArea_MissingH_ShortCurb()
    {
        const string head = Street + "[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\tW\tGrate\tArea\th\tLc\n";
        Assert.Contains(Run(head + "A\tgrate\tgrade\t1\t0.01\t3\t2\tbogus").Messages, m => m.Code == "E-IN-021");
        Assert.Contains(Run(head + "A\tgrate\tgrade\t1\t0.01\t3\t2").Messages, m => m.Code == "E-IN-023");
        Assert.Contains(Run(head + "A\tgrate\tsag\t1\t\t3\t2").Messages, m => m.Code == "E-IN-024");
        Assert.Contains(Run(head + "A\tcurb\tsag\t1\t\t3").Messages, m => m.Code == "E-IN-025");
        Assert.Contains(Run(head + "A\tcombination\tgrade\t1\t0.01\t3\t2\tP-50\t\t\t2").Messages, m => m.Code == "E-IN-026");
        Assert.Contains(Run(head + "A\tgrate\tgrade\t1\t0.01\t3\t2\t-4").Messages, m => m.Code == "E-IN-020");
        // A number in the Grate column is the splash-over velocity.
        Assert.True(Run(head + "A\tgrate\tgrade\t1\t0.01\t3\t2\t5.5").Succeeded);
    }

    [Fact]
    public void SiUnits_UnknownTarget_DuplicateNames_GutterFlatterThanPavement_AreErrors()
    {
        Assert.Contains(Run(Case1, Ctx(u: UnitSystem.SI)).Messages, m => m.Code == "E-IN-001");
        Assert.Contains(Run(Street + "[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\tBypass to\nA\tcurb\tgrade\t1\t0.01\t3\tZZ").Messages, m => m.Code == "E-IN-028");
        Assert.Contains(Run(Street + "[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\nA\tcurb\tgrade\t1\t0.01\t3\nA\tcurb\tgrade\t1\t0.01\t3").Messages, m => m.Code == "E-IN-011");
        Assert.Contains(Run(Street + "gutter width = 2\ngutter slope = 0.01\n[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\nA\tcurb\tgrade\t1\t0.01\t3").Messages, m => m.Code == "E-IN-005");
        Assert.Contains(Run(Street + "[Inlets]\nInlet\tType\tQ\tS\tL\nA\tcurb\t1\t0.01\t3").Messages, m => m.Code == "E-IN-009");
    }

    [Fact]
    public void NoNetwork_BypassLeaves_AndNothingIsApplied()
    {
        var r = new InletNetworkTool().Run(Street + "[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\nA\tcurb\tgrade\t4\t0.02\t5", new DesignToolContext(UnitSystem.USCustomary));
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Equal("out", Row(r, "A")[To]);
        Assert.Null(r.UpdatedNetwork);
        Assert.Contains("leaves (no network)", DesignTools.ToText(r));
    }

    [Fact]
    public void ZeroFlowInlet_IsHandled()
    {
        var r = Run(Street + "[Inlets]\nInlet\tType\tLocation\tQ\tS\tL\tW\tGrate\tArea\nCB-A\tgrate\tgrade\t0\t0.01\t3\t2\tP-50\nCB-B\tgrate\tsag\t0\t\t3\t2\t\t3");
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Equal(0, D(Row(r, "CB-A"), Qi));
        Assert.Equal(0, D(Row(r, "CB-B"), Depth));
    }
}
