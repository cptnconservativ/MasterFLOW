using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Street capacity tool and the shared gutter core. Expected values come from validation/hand_calcs_street.py, an
/// independent Python implementation of the same published equations (IDCM Eq. 4-4-1, HEC-22 Eq. 4-4, Manning).
/// </summary>
public class StreetCapacityToolTests
{
    private static readonly GutterSection Composite = new(0.02, 0.016, 2, 0.0833);
    private const string Head = "street class = collector\npavement width = 36\ncurb height = 0.5\ncross slope = 0.02\nn = 0.016\n";
    private const string Rows = "[Reaches]\nReach\tS\tQ10\tQ100\tCross pan\n";

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => new StreetCapacityTool().Run(input, new DesignToolContext(u));
    private static string Msgs(ToolResult r) => string.Join("\n", r.Messages.Select(m => $"{m.Code} {m.Text}"));

    // ---------------------------------------------------------------- gutter core

    [Fact]
    public void Uniform_MatchesExistingEq441()
    {
        var u = new GutterSection(0.02, 0.016);
        Assert.Equal(GutterHydraulics.Spread(4, 0.016, 0.02, 0.015), u.Spread(4, 0.015), 9);
        Assert.Equal(GutterHydraulics.Velocity(u.Spread(4, 0.015), 0.016, 0.02, 0.015), u.Velocity(4, 0.015), 9);   // Q/A = Eq. 4-4-2
        Assert.Equal(11.2563 * 0.02, u.Depth(u.Spread(4, 0.015)), 4);
    }

    [Fact]
    public void Composite_HEC22Example42_Data()
    {
        Assert.Equal(0.688865, Composite.FrontalRatio(8), 4);
        Assert.Equal(1.956605, Composite.Capacity(8, 0.01), 5);     // HEC-22 prints 1.94 with the 5/3, 8/3 exponents
        Assert.Equal(8, Composite.Spread(1.956605, 0.01), 4);
        Assert.Equal(8 * 0.02 + 2 * (0.0833 - 0.02), Composite.Depth(8), 12);
    }

    [Fact]
    public void Composite_IsContinuousAtGutterEdge_AndInsideGutterIsTriangleOnSw()
    {
        double inside = Composite.Capacity(2, 0.01), justOut = Composite.Capacity(2 + 1e-7, 0.01);
        Assert.Equal(inside, justOut, 5);
        Assert.Equal(0.56 / 0.016 * Math.Pow(0.0833, 1.67) * 0.1 * Math.Pow(1.5, 2.67), Composite.Capacity(1.5, 0.01), 12);
        Assert.Equal(1.5, Composite.Spread(Composite.Capacity(1.5, 0.01), 0.01), 9);
        Assert.Equal(1, Composite.FrontalRatio(1.5));
        Assert.Equal(1.5 * 0.0833, Composite.Depth(1.5), 12);
        Assert.Equal(1.5, Composite.SpreadAtDepth(1.5 * 0.0833), 9);
    }

    [Fact]
    public void Composite_WithEqualSlopes_ReducesToUniformEo()
    {
        var flat = new GutterSection(0.02, 0.016, 2, 0.02);
        Assert.False(flat.IsComposite);
        Assert.Equal(1 - Math.Pow(1 - 2.0 / 8, 2.67), flat.FrontalRatio(8), 12);
        Assert.Equal(new GutterSection(0.02, 0.016).Capacity(8, 0.01), flat.Capacity(8, 0.01), 12);
    }

    [Fact]
    public void ZeroFlow_GivesZeroSpread_AndInvalidSectionsAreReported()
    {
        Assert.Equal(0, Composite.Spread(0, 0.01));
        Assert.Equal(0, Composite.Velocity(0, 0.01));
        Assert.NotEmpty(new GutterSection(0.02, 0.016, 2, 0.01).Problems());   // Sw < Sx
        Assert.NotEmpty(new GutterSection(0, 0.016).Problems());
        Assert.NotEmpty(new GutterSection(0.02, -0.016).Problems());
        Assert.NotEmpty(new GutterSection(0.02, 0.016, -1).Problems());
        Assert.Empty(Composite.Problems());
    }

    [Theory]
    [InlineData(0.0, 0.65)]
    [InlineData(0.3, 0.70)]
    [InlineData(0.45, 0.755)]
    [InlineData(0.8, 0.90)]
    public void Table48_AverageVelocitySpread(double ratio, double expected) =>
        Assert.Equal(expected, GutterSection.AverageVelocitySpreadRatio(ratio), 9);

    [Fact]
    public void Table48_OutsideTable_IsNaN()
    {
        Assert.True(double.IsNaN(GutterSection.AverageVelocitySpreadRatio(0.9)));
        Assert.True(double.IsNaN(GutterSection.AverageVelocitySpreadRatio(-0.1)));
    }

    // ---------------------------------------------------------------- tool, against the hand calculation

    [Fact]
    public void Sample_MatchesHandCalc_10yr_And_100yr()
    {
        var tool = new StreetCapacityTool();
        var ctx = new DesignToolContext(UnitSystem.USCustomary);
        var r = tool.Run(tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded, Msgs(r));
        var minor = r.Tables[0].Rows;
        double[] t = [10.100127, 15.569535, 18.344481], d = [0.287003, 0.396391, 0.451890], v = [2.895598, 2.590566, 2.608545], qa = [4.825187, 3.411923, 3.114645];
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(t[i], (double)minor[i][3]!, 4);
            Assert.Equal(d[i], (double)minor[i][4]!, 5);
            Assert.Equal(v[i], (double)minor[i][6]!, 4);
            Assert.Equal(qa[i], (double)minor[i][9]!, 4);
            Assert.Equal(12, (double)minor[i][8]!, 9);   // governing spread: (36 − 12)/2
        }
        Assert.Equal("OK", minor[0][12]);
        Assert.StartsWith("FAILS", (string)minor[1][12]!);                // 15.6 ft > 12 ft
        Assert.StartsWith("OK", (string)minor[1][11]!);                    // cross pan 4.8 in. ≤ 6 in.
        Assert.Contains("crown", (string)minor[2][12]!);                   // 18.3 ft passes the 18 ft crown

        var major = r.Tables[1].Rows;
        double[] y = [0.476362, 0.643555, 0.826664], qLim = [237.337654, 167.823064, 153.200797], qRow = [93.424638, 66.061195, 60.305344];
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(y[i], (double)major[i][3]!, 5);
            Assert.Equal(qLim[i], (double)major[i][9]!, 3);
            Assert.Equal(qRow[i], (double)major[i][10]!, 3);
        }
        Assert.Equal("OK", major[0][12]);
        Assert.Equal("OK", major[1][12]);
        Assert.Contains("ROW", (string)major[2][12]!);                     // 0.83 ft > 0.70 ft ROW line, under 12 in.
        Assert.DoesNotContain("Table 4-9", (string)major[2][12]!);
        Assert.Single(r.Figures);
    }

    [Fact]
    public void SpreadExactlyAtTheLimit_Passes()
    {
        var sec = new GutterSection(0.02, 0.016);
        double q = sec.Capacity(12, 0.01);   // (36 − 12)/2 = 12 ft allowed
        var r = Run(Head + Rows + $"A\t0.01\t{q:R}\t\tno");
        Assert.Equal("OK", r.Tables[0].Rows[0][12]);
        Assert.Equal(1, (double)r.Tables[0].Rows[0][10]!, 6);
        var over = Run(Head + Rows + $"A\t0.01\t{q * 1.001:R}\t\tno");
        Assert.StartsWith("FAILS", (string)over.Tables[0].Rows[0][12]!);
    }

    [Fact]
    public void CurbOverTopping_GovernsALocalStreetWithALowCurb()
    {
        // Local: spread may reach the crown (18 ft) but a 0.25 ft curb is full at 12.5 ft.
        var r = Run(Head.Replace("collector", "local").Replace("curb height = 0.5", "curb height = 0.25") + Rows + "A\t0.01\t3\t\tno");
        var row = r.Tables[0].Rows[0];
        Assert.Equal(12.5, (double)row[8]!, 9);
        Assert.Contains("curb full", r.Tables[0].Notes![1]);
        Assert.Equal(new GutterSection(0.02, 0.016).Capacity(12.5, 0.01), (double)row[9]!, 9);
    }

    [Fact]
    public void LocalStreet_DepthLimitIs18Inches_CollectorIs12()
    {
        string sec = "\nparkway width = 15\nparkway slope = 0.08\n";
        var local = Run(Head.Replace("collector", "local") + sec + Rows + "A\t0.01\t\t300\tno");
        var coll = Run(Head + sec + Rows + "A\t0.01\t\t300\tno");
        double y = (double)local.Tables[0].Rows[0][3]!;
        Assert.InRange(y, 1.0, 1.5);                                       // between the two limits
        Assert.Equal("OK", local.Tables[0].Rows[0][12]);
        Assert.Contains("Table 4-9", (string)coll.Tables[0].Rows[0][12]!);
        Assert.Equal(18, (double)local.Tables[0].Rows[0][8]!);
        Assert.Equal(12, (double)coll.Tables[0].Rows[0][8]!);
    }

    [Fact]
    public void BuildingGroundLine_IsChecked()
    {
        var r = Run(Head + "parkway width = 10\nparkway slope = 0.05\nbuilding ground = 0.4\n" + Rows + "A\t0.006\t\t52\tno");
        Assert.True((double)r.Tables[0].Rows[0][3]! > 0.4);
        Assert.Contains("building ground", (string)r.Tables[0].Rows[0][12]!);   
    }

    [Fact]
    public void CrossPan_OnArterial_Fails_AndDeepPanFails()
    {
        var art = Run(Head.Replace("collector", "arterial2") + Rows + "A\t0.01\t1\t\tyes");
        Assert.StartsWith("FAILS", (string)art.Tables[0].Rows[0][11]!);
        var deep = Run(Head.Replace("collector", "local").Replace("curb height = 0.5", "curb height = 0.8") + Rows + "A\t0.003\t20\t\tyes");
        Assert.Contains("> 6 in.", (string)deep.Tables[0].Rows[0][11]!);
    }

    [Fact]
    public void RaisedMedianArterial_UsesTheCollectorSpread()
    {
        var r = Run(Head.Replace("collector", "arterial4").Replace("pavement width = 36", "pavement width = 60") + "raised median = yes\n" + Rows + "A\t0.01\t2\t\tno");
        Assert.Equal(24, (double)r.Tables[0].Rows[0][8]!, 9);              // (60 − 12)/2, not (60 − 36)/2
    }

    [Fact]
    public void PercentSlopes_AreReadAsPercent()
    {
        var a = Run(Head.Replace("0.02", "2%") + Rows + "A\t1%\t3\t20\tno");
        var b = Run(Head + Rows + "A\t0.01\t3\t20\tno");
        Assert.True(a.Succeeded, Msgs(a));
        Assert.Equal((double)b.Tables[0].Rows[0][3]!, (double)a.Tables[0].Rows[0][3]!, 12);
        Assert.Equal((double)b.Tables[1].Rows[0][3]!, (double)a.Tables[1].Rows[0][3]!, 12);
    }

    [Fact]
    public void ZeroFlow_IsZeroSpread()
    {
        var r = Run(Head + Rows + "A\t0.01\t0\t0\tno");
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Equal(0, (double)r.Tables[0].Rows[0][3]!);
        Assert.Equal(0, (double)r.Tables[1].Rows[0][3]!);
    }

    [Theory]
    [InlineData(Head + Rows + "A\t0.01\t-3\t\tno", "E-SC-015")]               // negative flow
    [InlineData(Head + Rows + "A\t0\t3\t\tno", "E-SC-014")]                  // zero slope
    [InlineData(Head + Rows + "A\t-0.01\t3\t\tno", "E-SC-014")]              // negative slope
    [InlineData(Head + "gutter width = 2\ngutter slope = 0.01\n" + Rows + "A\t0.01\t3\t\tno", "E-SC-004")]   // Sw < Sx
    [InlineData(Head + "gutter width = 2\n" + Rows + "A\t0.01\t3\t\tno", "E-SC-003")]                        // W without Sw
    [InlineData(Head + "gutter width = 18\ngutter slope = 0.05\n" + Rows + "A\t0.01\t3\t\tno", "E-SC-006")]  // W ≥ B/2
    [InlineData(Head + "parkway slope = -0.02\n" + Rows + "A\t0.01\t3\t\tno", "E-SC-009")]
    [InlineData(Head + "building ground = -1\n" + Rows + "A\t0.01\t3\t\tno", "E-SC-011")]
    [InlineData(Head, "E-SC-012")]                                                                            // no reaches
    [InlineData("street class = freeway\npavement width = 36\n" + Rows + "A\t0.01\t3\t\tno", "E-SC-002")]
    [InlineData("street class = arterial4\npavement width = 30\n" + Rows + "A\t0.01\t3\t\tno", "E-SC-013")]  // cannot leave 36 ft dry
    [InlineData("street class = local\ncurb height = 0\npavement width = 30\n" + Rows + "A\t0.01\t3\t\tno", "E-SC-007")]
    public void BadInput_IsAnErrorWithoutNumbers(string input, string code)
    {
        var r = Run(input);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }

    [Fact]
    public void SIUnits_AreRefused()
    {
        var r = Run(Head + Rows + "A\t0.01\t3\t\tno", UnitSystem.SI);
        Assert.Contains(r.Messages, m => m.Code == "E-SC-001");
        Assert.Empty(r.Tables);
    }

    [Fact]
    public void WaterPolygons_SplitBelowTheCrown_AndJoinAboveIt()
    {
        (double, double)[] g = [(0, 0.5), (0, 0), (18, 0.36), (36, 0), (36, 0.5)];
        Assert.Equal(2, StreetCapacityTool.WaterPolygons(g, 0.2).Count);
        Assert.Single(StreetCapacityTool.WaterPolygons(g, 0.4));
    }

    [Fact]
    public void Report_DrawsTheStreetSection()
    {
        var tool = new StreetCapacityTool();
        var ctx = new DesignToolContext(UnitSystem.USCustomary);
        string input = tool.SampleInput(ctx);
        var html = ToolReport.ToHtml(new ProjectInfo(), tool.Run(input, ctx), input, "test");
        Assert.Contains("<svg", html);
        Assert.Contains("<polygon", html);
        Assert.Contains("100-yr limit 12 in.", html);
    }
}
