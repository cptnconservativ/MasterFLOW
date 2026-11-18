using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Rational-method hydrology: flow-path tc, custom IDF data and routing through the storm network.
/// Expected values are from validation/hand_calcs_rational.py (written from the equations, not this code)
/// and from the worked example of NRCS TR-55 (1986) Example 3-1.
/// </summary>
public class RationalHydrologyTests
{
    private static DesignToolContext Ctx(StormNetwork? n = null)
    {
        n ??= NetworkOutputTests.NetA();
        return new DesignToolContext(UnitSystem.USCustomary, n, StormNetworkSolver.Solve(n));
    }

    private const string HandInput = """
        urban = yes
        design storm = 10
        minor storm = 2
        [Basins]
        Basin	Structure	Land use areas (ac)	Overland L (ft)	Overland S (%)	Travel time (min)	Flow length (ft)
        A1	CB-3	Single Family Residential: 2.4; Paved Streets: 0.6
        A2	CB-4	Business/Commercial: 1.1; Lawns, Clay Soil: 0.3	80	1.5	2.5	600
        A3	MH-2	Paved Streets: 0.4
        [Flow paths]
        Basin	Type	L (ft)	S (%)	Details
        A1	overland	100	2.0
        A1	shallow	250	1.5	surface: unpaved
        A1	gutter	400	1.2	n: 0.016; Sx: 2; T: 6
        A1	pipe	150	0.8	D: 15; n: 0.013
        A3	sheet	60	2	n: 0.011
        A3	shallow paved	200	1
        """;

    private static ToolResult Run(string input, DesignToolContext? ctx = null)
    {
        var r = new RationalMethodTool().Run(input, ctx ?? Ctx());
        return r;
    }

    private static IReadOnlyList<object?> Row(ToolTable t, string first) => t.Rows.Single(x => (string)x[0]! == first);
    private static ToolTable Table(ToolResult r, string startsWith) => r.Tables.Single(t => t.Title.StartsWith(startsWith));
    private static double D(object? o) => (double)o!;

    [Fact]
    public void TR55_Example31_SheetAndShallowConcentrated()
    {
        // TR-55 (1986) Example 3-1: sheet flow, dense grass n = 0.24, L = 100 ft, P2 = 3.6 in., s = 0.01 → Tt = 0.30 h.
        Assert.Equal(0.30 * 60, TcFlowPath.SheetFlowMinutes(0.24, 100, 3.6, 0.01), 0);   // 18.1 min
        Assert.InRange(TcFlowPath.SheetFlowMinutes(0.24, 100, 3.6, 0.01) / 60, 0.295, 0.305);
        // Shallow concentrated, unpaved, s = 0.01: V = 1.6 ft/s; 1,400 ft → 0.24 h.
        Assert.Equal(1.61, TcFlowPath.ShallowConcentratedVelocity(false, 0.01), 2);
        Assert.InRange(1400 / TcFlowPath.ShallowConcentratedVelocity(false, 0.01) / 3600, 0.235, 0.245);
        Assert.Equal(2.03, TcFlowPath.ShallowConcentratedVelocity(true, 0.01), 2);
        // Channel: Manning with the trapezoid's own A and P.
        double a = 2 * (4 + 3 * 2), p = 4 + 2 * 2 * Math.Sqrt(10);
        Assert.Equal(1.486 / 0.035 * Math.Pow(a / p, 2.0 / 3) * Math.Sqrt(0.004), TcFlowPath.ChannelVelocity(0.035, 4, 3, 2, 0.004), 12);
    }

    [Fact]
    public void FlowPaths_And_NetworkRouting_MatchHandCalculation()
    {
        var r = Run(HandInput);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var basins = r.Tables[0];
        // hand_calcs_rational_output.txt
        Assert.Equal(14.628813, D(Row(basins, "A1")[7]), 5);
        Assert.Equal(8.390364, D(Row(basins, "A1")[5]), 5);   // ti (overland reach)
        Assert.Equal(8.279278, D(Row(basins, "A2")[7]), 5);
        Assert.Equal(5.0, D(Row(basins, "A3")[7]), 9);
        Assert.Equal("minimum 5 min", Row(basins, "A3")[8]);
        // columns: C2,i2,Q2 (9..11), C10,i10,Q10 (12..14)
        Assert.Equal(7.308825, D(Row(basins, "A1")[14]), 5);
        Assert.Equal(5.380443, D(Row(basins, "A2")[14]), 5);
        Assert.Equal(4.132331, D(Row(basins, "A1")[11]), 5);

        var paths = Table(r, "Time of concentration");
        var a1 = paths.Rows.Where(x => (string)x[0]! == "A1").ToList();
        Assert.Equal(1.976065, D(a1[1][5]), 5);
        Assert.Equal(1.852423, D(a1[2][5]), 5);
        Assert.Equal(4.708188, D(a1[3][5]), 5);
        Assert.Equal(1.018492, D(paths.Rows.First(x => (string)x[0]! == "A3")[6]), 5);   // sheet, P2 = 2.00 in. from Table 4-2 (2-yr, 24 h)

        var net10 = Table(r, "Rational method through the network — 10-yr");
        var p1 = Row(net10, "P1");
        Assert.Equal(3.128, D(p1[4]), 9);
        Assert.Equal(15.171773, D(p1[5]), 5);
        Assert.Equal("basin A1 → P2", p1[6]);
        Assert.Equal(4.132587, D(p1[7]), 5);
        Assert.Equal(12.926733, D(p1[10]), 5);
        Assert.Equal(0.542960, D(Row(net10, "P2")[14]), 5);
        Assert.Equal(7.672375, D(Row(Table(r, "Rational method through the network — 2-yr"), "P1")[10]), 1e-5);

        // Applied: pipe Q (design and minor) and structure inflows; the network solves with them.
        var net = r.UpdatedNetwork!;
        Assert.Equal(12.927, net.Pipes.Single(x => x.Name == "P1").Discharge, 9);
        Assert.Equal(7.672, net.Pipes.Single(x => x.Name == "P1").MinorDischarge, 9);
        Assert.Equal(7.309, net.Pipes.Single(x => x.Name == "P2").Discharge, 9);
        Assert.Equal(2.29, net.Structures.Single(s => s.Name == "MH-2").Inflow!.Value, 9);
        var solved = StormNetworkSolver.Solve(net);
        Assert.True(solved.Succeeded);
        Assert.Equal(12.927, solved.Pipes.Single(x => x.Pipe.Name == "P1").Pipe.Discharge, 9);
        Assert.Contains(r.Messages, m => m.Code == "I-RM-003");   // NetA had typed Q, replaced

        // Running again on the applied network gives the same flows (sub-basin inflows are not double counted).
        var again = Run(HandInput, Ctx(net));
        Assert.Equal(12.926733, D(Row(Table(again, "Rational method through the network — 10-yr"), "P1")[10]), 5);
    }

    [Fact]
    public void LowerPartOfBasin_CanGovern()
    {
        // IDCM §4.2.7: a lower, faster part of the basin can give a larger peak than the whole basin.
        string input = """
            [Basins]
            Basin	Structure	Use	L	S	tt	FL
            Park	CB-3	Lawns, Clay Soil: 10	300	1	30	2000
            Lot	MH-2	Paved Streets: 3	40	2	0	200
            """;
        var r = Run(input);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var p1 = Row(Table(r, "Rational method through the network — 10-yr"), "P1");
        Assert.True(D(p1[8]) < D(p1[10]));                           // i·ΣCA is less than the governing Q
        Assert.Equal("basin Lot alone", p1[11]);
        Assert.Equal(0.9 * 6.36 * 3, D(p1[10]), 9);
    }

    [Fact]
    public void OtherInflow_AtStructureWithoutBasin_IsCarriedDownstream()
    {
        var n = NetworkOutputTests.NetA();
        n = n with { Structures = n.Structures.Select(s => s.Name == "CB-4" ? s with { Inflow = 3.0, MinorInflow = 1.0 } : s).ToList() };
        var r = Run("[Basins]\nB\tS\tU\tL\tS\nA\tCB-3\tPaved Streets: 1\t40\t2", Ctx(n));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var t = Table(r, "Rational method through the network — 10-yr");
        Assert.Equal(3.0, D(Row(t, "P3")[10]), 9);
        Assert.Equal("other inflow", Row(t, "P3")[11]);
        var p1 = Row(t, "P1");
        Assert.Equal(3.0, D(p1[9]), 9);
        Assert.Equal(D(p1[8]) + 3.0, D(p1[10]), 9);
        Assert.Equal(1.0, D(Row(Table(r, "Rational method through the network — 2-yr"), "P3")[10]), 9);
        Assert.Contains(r.Messages, m => m.Code == "I-RM-004");
    }

    [Fact]
    public void NormalDepthVelocity_IsManningAtTheRoutedQ()
    {
        var r = Run(HandInput.Replace("urban = yes", "urban = yes\npipe velocity = normal"));
        Assert.True(r.Succeeded);
        var p2 = Row(Table(r, "Rational method through the network — 10-yr"), "P2");
        Assert.Equal("normal depth", p2[13]);
        // independent: circular pipe D = 1.5 ft, S = 0.9/150, n = 0.013 carrying Q; bisection on depth
        double q = D(p2[10]), d = 1.5, s = 0.9 / 150, lo = 1e-6, hi = 0.938 * d;
        static (double A, double P) Circ(double y, double d) { double th = 2 * Math.Acos(1 - 2 * y / d); return (d * d / 8 * (th - Math.Sin(th)), d * th / 2); }
        for (int k = 0; k < 200; k++)
        {
            double m = (lo + hi) / 2; var (a, pp) = Circ(m, d);
            if (1.486 / 0.013 * a * Math.Pow(a / pp, 2.0 / 3) * Math.Sqrt(s) < q) lo = m; else hi = m;
        }
        Assert.Equal(q / Circ(lo, d).A, D(p2[12]), 6);
        Assert.Equal(150 / D(p2[12]) / 60, D(p2[14]), 9);
    }

    [Fact]
    public void SumMode_ClearsPipeQ_SoInflowsAccumulate()
    {
        var r = Run(HandInput.Replace("urban = yes", "network flows = sum"));
        Assert.True(r.Succeeded);
        Assert.All(r.UpdatedNetwork!.Pipes, p => Assert.True(double.IsNaN(p.Discharge)));
        var solved = StormNetworkSolver.Solve(r.UpdatedNetwork);
        Assert.Equal(7.309 + 5.38 + 2.29, solved.Pipes.Single(x => x.Pipe.Name == "P1").Pipe.Discharge, 9);
        Assert.DoesNotContain(r.Tables, t => t.Title.StartsWith("Rational method through"));
    }

    [Fact]
    public void CustomIdf_Table_Depths_And_Equation()
    {
        var c = IdfCurve.FromTable("t", [10, 60], new Dictionary<int, double[]> { [10] = [4.0, 1.5] }, valuesAreDepths: false);
        Assert.Equal(4.0, c.Intensity(10, 10), 12);
        Assert.Equal(4.0 * Math.Pow(1.5 / 4.0, Math.Log(3) / Math.Log(6)), c.Intensity(10, 30), 12);   // log-log
        Assert.True(double.IsNaN(c.Intensity(10, 5)));                                                   // never extrapolated
        Assert.True(double.IsNaN(c.Intensity(2, 30)));
        var dep = IdfCurve.FromTable("d", [10, 60], new Dictionary<int, double[]> { [10] = [0.8, 1.5] }, valuesAreDepths: true);
        Assert.Equal(0.8 / (10.0 / 60), dep.Intensity(10, 10), 12);
        // Table 4-2 through the general table gives the same numbers as the IDCM implementation.
        var t42 = IdfCurve.FromTable("t42", RapidCityRational.Durations, RapidCityRational.Depths.ToDictionary(k => k.Key, k => k.Value), true);
        foreach (double t in new[] { 5.0, 7, 12.5, 15, 40, 300, 1440 }) Assert.Equal(RapidCityRational.Intensity(100, t), t42.Intensity(100, t), 12);

        string eq = "[IDF equation]\nmin duration = 5\nmax duration = 60\nStorm\ta\tb\tc\n2\t30\t8\t0.8\n10\t50\t9\t0.8\n[Basins]\nB\tS\tU\tL\tS\nA\tCB-3\tPaved Streets: 1\t40\t2";
        var r = Run(eq);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Equal(0.90 * 50 / Math.Pow(5 + 9, 0.8) * 1, D(Row(r.Tables[0], "A")[14]), 9);
        Assert.Contains("i = a/(t + b)^c", r.Summary[1]);

        string tbl = "[IDF]\nvalues = depth\nDuration (min)\t2-yr\t10-yr\n5\t0.4\t0.6\n60\t1.1\t2.0\n[Basins]\nB\tS\tU\tL\tS\nA\tCB-3\tPaved Streets: 1\t40\t2";
        r = Run(tbl);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Equal(0.90 * 0.6 * 12, D(Row(r.Tables[0], "A")[14]), 9);
    }

    [Theory]
    [InlineData("[IDF]\nDuration\t25-yr\n5\t6\n60\t2", "E-RM-011")]                      // no C for 25-yr
    [InlineData("[IDF]\nDuration\t10-yr\n60\t2\n5\t6", "E-RM-012")]                      // durations not increasing
    [InlineData("[IDF]\nDuration\t10-yr\n5\t-6\n60\t2", "E-RM-012")]                     // negative intensity
    [InlineData("[IDF]\nDuration\t10-yr\n5\t6\n60\t2\n[IDF equation]\nS\ta\tb\tc\n10\t1\t1\t1", "E-RM-010")]
    [InlineData("[IDF equation]\nS\ta\tb\tc\n10\t-50\t9\t0.8", "E-RM-013")]
    [InlineData("[IDF]\nDuration\t2-yr\n5\t6\n60\t2", "E-RM-002")]                        // design storm 10 not in the IDF
    public void CustomIdf_Errors(string idf, string code)
    {
        var r = Run(idf + "\n[Basins]\nB\tS\tU\tL\tS\nA\tCB-3\tPaved Streets: 1\t40\t2");
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
        Assert.Null(r.UpdatedNetwork);
    }

    [Fact]
    public void CustomIdf_RisingIntensity_IsWarned()
    {
        var r = Run("minor storm = 10\n[IDF]\nDuration\t10-yr\n5\t2\n60\t3\n[Basins]\nB\tS\tU\tL\tS\nA\tCB-3\tPaved Streets: 1\t40\t2");
        Assert.True(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == "W-RM-005");
    }

    [Theory]
    [InlineData("A\tshallow\t250\t-1.5\tsurface: unpaved", "E-RM-031")]     // negative slope
    [InlineData("A\tshallow\t0\t1.5\tsurface: unpaved", "E-RM-031")]        // zero length
    [InlineData("A\tshallow\t250\t1.5", "E-RM-032")]                        // no surface
    [InlineData("A\tgutter\t250\t1.5\tn: 0.016; Sx: 2", "E-RM-032")]        // no spread
    [InlineData("A\tpipe\t150\t1\tD: -15; n: 0.013", "E-RM-032")]           // negative diameter
    [InlineData("A\tchannel\t150\t1\tb: 0; z: 0; d: 2; n: 0.03", "E-RM-032")]
    [InlineData("A\tswale\t150\t1", "E-RM-033")]
    [InlineData("A\tgutter\t250\t1.5\tn 0.016", "E-RM-032")]                // detail without colon
    public void FlowPath_BadInput_IsRejected_AndNothingApplied(string row, string code)
    {
        var r = Run($"[Basins]\nB\tS\tU\nA\tCB-3\tPaved Streets: 1\n[Flow paths]\nBasin\tType\tL\tS\tDetails\n{row}");
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
        Assert.Null(r.UpdatedNetwork);
        Assert.Empty(r.Tables[0].Rows);
    }

    [Fact]
    public void FlowPath_EdgeCases()
    {
        // Triangular channel (b = 0) and a known velocity (no slope) are valid; a long sheet reach is warned.
        var r = Run("[Basins]\nB\tS\tU\nA\tCB-3\tPaved Streets: 1\n[Flow paths]\nBasin\tType\tL\tS\tDetails\nA\tsheet\t350\t1\tn: 0.15\nA\tchannel\t300\t0.5\tb: 0; z: 4; d: 1; n: 0.03\nA\tvelocity\t500\t\tV: 5\nZ\tvelocity\t10\t\tV: 1");
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains(r.Messages, m => m.Code == "W-RM-006");
        Assert.Contains(r.Messages, m => m.Code == "W-RM-003");   // flow path for a basin not listed
        var rows = Table(r, "Time of concentration").Rows;
        Assert.Equal(500 / 5.0 / 60, D(rows[2][6]), 12);
        Assert.Equal(TcFlowPath.ChannelVelocity(0.03, 0, 4, 1, 0.005), D(rows[1][5]), 12);
        // urban cap uses the total path length when flow length is blank: (350 + 300 + 500)/180 + 10
        var a = r.Tables[0].Rows.Single();
        Assert.Equal(1150.0 / 180 + 10, D(a[7]), 9);

        // Sheet flow with an IDF that has no 2-yr 24-h depth needs P2.
        r = Run("minor storm = 10\n[IDF]\nDuration\t10-yr\n5\t6\n60\t2\n[Basins]\nB\tS\tU\nA\tCB-3\tPaved Streets: 1\n[Flow paths]\nB\tT\tL\tS\tD\nA\tsheet\t50\t1\tn: 0.011");
        Assert.Contains(r.Messages, m => m.Code == "E-RM-032" && m.Text.Contains("P2"));
        r = Run("minor storm = 10\n[IDF]\nDuration\t10-yr\n5\t6\n60\t2\n[Basins]\nB\tS\tU\nA\tCB-3\tPaved Streets: 1\n[Flow paths]\nB\tT\tL\tS\tD\nA\tsheet\t50\t1\tn: 0.011; P2: 2.2");
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));

        // tc beyond the IDF range is an error, not an extrapolation.
        r = Run("urban = no\nminor storm = 10\n[IDF]\nDuration\t10-yr\n5\t6\n30\t2\n[Basins]\nB\tS\tU\tL\tS\ttt\nA\tCB-3\tLawns, Clay Soil: 1\t300\t0.5\t40");
        Assert.Contains(r.Messages, m => m.Code == "E-RM-009");
        Assert.Null(r.UpdatedNetwork);
    }

    [Fact]
    public void Routing_TcBeyondIdf_InTheNetwork_IsAnError()
    {
        var n = NetworkOutputTests.NetA();
        n = n with { Pipes = n.Pipes.Select(p => p.Name == "P2" ? p with { Length = 20000 } : p).ToList() };   // slow pipe pushes tc past 30 min
        var r = Run("minor storm = 10\n[IDF]\nDuration\t10-yr\n5\t6\n30\t2\n[Basins]\nB\tS\tU\tL\tS\nA\tCB-3\tPaved Streets: 1\t40\t2", Ctx(n));
        Assert.Contains(r.Messages, m => m.Code == "E-RM-022");
        Assert.Null(r.UpdatedNetwork);
    }

    [Fact]
    public void Report_HasIdfChart_FlowPaths_AndNetworkSheets()
    {
        var r = Run(HandInput);
        var html = ToolReport.ToHtml(new ProjectInfo { Name = "P" }, r, HandInput, "test");
        Assert.Contains("<svg", html);
        Assert.Contains("IDF curves", html);
        Assert.Contains("sub-basin tc (10-yr)", html);
        Assert.Contains("Time of concentration flow paths", html);
        var sheets = ToolReport.ToSheets(r);
        Assert.Contains(sheets, s => s.Name.StartsWith("Rational method through"));
        Assert.True(XlsxWriter.ToBytes(sheets).Length > 1000);
        Assert.Contains("basin A1 → P2", DesignTools.ToText(r));
    }
}
