using MasterFlow.Application.Swmm;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Sanitary wet-weather flows: diurnal DWF, RTK RDII, dynamic-wave routing and the SWMM export.</summary>
public class SanitaryWetWeatherTests
{
    private static readonly SanitaryWetWeatherTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static double Num(object? v) => Convert.ToDouble(v);
    private static IReadOnlyList<object?> Row(ToolResult r, string table, string first) => r.Tables.First(t => t.Title.StartsWith(table)).Rows.First(x => (string?)x[0] == first);
    private static string Msgs(ToolResult r) => string.Join("\n", r.Messages.Select(m => m.Code + " " + m.Text));

    // Two pipes: A (20 ac residential, 4 DU/ac) → B (5 ac commercial) → C.
    private const string Network = """
        h2s = no
        [Manholes]
        Name	Rim
        A	110
        B	106
        C	102
        [Loads]
        Manhole	Land use	Area (ac)	DU/ac
        A	Residential	20	4
        B	Commercial	5
        [Pipes]
        Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert
        P1	A	B	304	8	104.0	102.0
        P2	B	C	304	8	101.9	99.9
        """;

    [Fact]
    public void Sample_RunsClean_WithDynamicWave_AndSwmmFile()
    {
        var r = Tool.Run(Tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Contains(r.Tables, t => t.Title.StartsWith("Manholes: peak HGL"));
        Assert.True(Math.Abs(Num(Row(r, "Run and continuity", "Continuity error")[1])) < 1);
        var inp = System.Text.Encoding.UTF8.GetString(r.Files.Single().Content);
        var sec = SwmmInp.Sections(inp);
        foreach (var s in new[] { "DWF", "PATTERNS", "HYDROGRAPHS", "RDII", "RAINGAGES", "TIMESERIES", "JUNCTIONS", "CONDUITS" }) Assert.True(sec.ContainsKey(s), s);
        Assert.Equal(4, sec["DWF"].Count);          // MH-1, MH-2, MH-4, MH-5 have loads
        Assert.Equal(4, sec["HYDROGRAPHS"].Count);  // gage line + three triangles
        Assert.False(sec.ContainsKey("INFLOWS"));   // no infiltration in the sample
        Assert.Contains("RTK1  All  LONG  0.02  10  3", inp);
        Assert.False(Tool.Run(Tool.SampleInput(Us), new DesignToolContext(UnitSystem.SI)).Succeeded);
    }

    [Fact]
    public void RtkTriangle_UnitAreaPeakAndCumulative()
    {
        var u = new SanitaryWetWeatherTool.Rtk("Medium", 0.02, 4, 2);
        Assert.Equal(12, u.BaseHours, 9);
        Assert.Equal(2.0 / 12, u.Ordinate(4), 9);   // peak 2/(T(1+K))
        Assert.Equal(0.5 * 4 * (2.0 / 12), u.Cumulative(4), 9);
        Assert.Equal(1, u.Cumulative(12), 9);
        // Numerical integral of the ordinate = 1.
        double s = 0; for (double t = 0.0005; t < 12; t += 0.001) s += u.Ordinate(t) * 0.001;
        Assert.Equal(1, s, 4);
    }

    [Fact]
    public void Rdii_VolumeIsRTimesRainTimesArea()
    {
        // 1 in/h for 2 h on 10 ac, R = 0.01 + 0.02 + 0.03: volume = 0.06 × 2 in × 10 ac = 1.2 ac-in = 4,356 ft³.
        var rtk = new[] { new SanitaryWetWeatherTool.Rtk("Short", 0.01, 1, 1), new SanitaryWetWeatherTool.Rtk("Medium", 0.02, 4, 2), new SanitaryWetWeatherTool.Rtk("Long", 0.03, 10, 3) };
        double dt = 0.25;
        var q = SanitaryWetWeatherTool.Rdii([1, 1, 1, 1, 1, 1, 1, 1], dt, 10, rtk, dt, 400);
        double vol = q.Sum() * dt * 3600;
        Assert.Equal(1.2 * 43560 / 12, vol, 0);
        // Early on only the short triangle responds: at 0.5 h, Σ R·i·U(0.5) with U = peak·t²/(2T).
        double expect = (0.01 * (2.0 / 2) * 0.25 / 2 + 0.02 * (2.0 / 12) * 0.25 / 8 + 0.03 * (2.0 / 40) * 0.25 / 20) * 10 * SanitaryWetWeatherTool.CfsPerAcInPerHr;
        Assert.Equal(expect, q[2], 9);
    }

    [Fact]
    public void DryWeather_DailyVolumeEqualsAverageFlow_PeakAtPatternHour()
    {
        var r = Tool.Run("storm depth = 0\nrouting = none\nduration = 24\ntime step = 60\nstorm start = 0\n" + Network, Us);
        Assert.True(r.Succeeded, Msgs(r));
        double qRes = 100 * 2.65 * 4 * 20 / 1440, qCom = 3 * 5;
        var a = Row(r, "Inflows by manhole", "A");
        Assert.Equal(qRes, Num(a[1]), 2);
        Assert.Equal(1.5, Num(a[2]), 3);
        Assert.Equal("09:00", a[3]);
        Assert.Equal("04:00", a[5]);
        var p2 = Row(r, "Pipes: wet-weather peak flows", "P2");
        // DWF peak hour at P2 = max over hours of qRes·res(h) + qCom·com(h).
        double best = Enumerable.Range(0, 24).Max(h => qRes * SanitaryDiurnal.Residential[h] + qCom * SanitaryDiurnal.Commercial[h]);
        Assert.Equal(best, Num(p2[4]), 1);
        Assert.Equal(0, Num(p2[5]), 6);
        Assert.Equal("Dynamic wave", r.Tables.First(t => t.Title.StartsWith("Pipes")).Headers[8]);
    }

    [Fact]
    public void Patterns_ParseRescaleAndBlend()
    {
        Assert.Equal(1, SanitaryDiurnal.Residential.Average(), 9);
        Assert.Equal(1, SanitaryDiurnal.Commercial.Average(), 9);
        var msgs = new List<CalcMessage>();
        var p = SanitaryDiurnal.Parse(string.Join(",", Enumerable.Repeat("2", 24)), SanitaryDiurnal.Flat, "k", msgs, "E-X-1");
        Assert.All(p, v => Assert.Equal(1, v, 9));
        Assert.Contains(msgs, m => m.Code == "I-X-1");
        SanitaryDiurnal.Parse("1,2,3", SanitaryDiurnal.Flat, "k", msgs, "E-X-1");
        Assert.Contains(msgs, m => m.Code == "E-X-1");
        var b = SanitaryDiurnal.Blend([(10, SanitaryDiurnal.Residential), (30, SanitaryDiurnal.Flat)]);
        Assert.Equal((10 * 1.5 + 30) / 40, b[9], 9);
        Assert.Equal(22.5, SanitaryDiurnal.ParseClock("10:30 PM")!.Value, 9);
        Assert.Equal(0, SanitaryDiurnal.ParseClock("12 AM")!.Value, 9);
        Assert.Null(SanitaryDiurnal.ParseClock("25:00"));
        Assert.Equal("23:30", SanitaryDiurnal.Clock(47.5));
    }

    [Fact]
    public void SavedProjectDesign_IsUsed_AndMissingDesignIsAnError()
    {
        var ctx = Us with { ToolInputs = new Dictionary<string, string> { ["sanitary-network"] = Network } };
        var r = Tool.Run("routing = none\nduration = 48", ctx);
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Contains(r.Messages, m => m.Code == "I-WW-001" && m.Text.Contains("project's"));
        Assert.Contains(r.Messages, m => m.Code == "W-WW-005");   // placeholder RTK
        Assert.Contains(Tool.Run("routing = none", Us).Messages, m => m.Code == "E-WW-002");
    }

    [Fact]
    public void Infiltration_IsConstant_InflowAllowanceReplacedByRdii()
    {
        // 200 gpd/in-mi on 8 in × 304 ft each: P1 = 200 × 8 × 304/5280/1440 gpm; P2 has its own as much again.
        var r = Tool.Run("routing = none\nduration = 48\ninfiltration = 200\ninflow = 100\n" + Network.Replace("h2s = no", "h2s = no"), Us);
        Assert.True(r.Succeeded, Msgs(r));
        double one = 200 * 8 * 304.0 / 5280 / 1440;
        Assert.Equal(one, Num(Row(r, "Inflows by manhole", "A")[6]), 2);
        Assert.Equal(one, Num(Row(r, "Inflows by manhole", "B")[6]), 2);
        Assert.Contains(r.Messages, m => m.Code == "I-WW-002");
        var inp = System.Text.Encoding.UTF8.GetString(r.Files[0].Content);
        Assert.Equal(2, SwmmInp.Sections(inp)["INFLOWS"].Count);
    }

    [Fact]
    public void BigStormOnSmallSewer_Surcharges_AndOverflows()
    {
        var input = "storm depth = 6\nstorm start = 6\nduration = 36\nrtk short = 0.15, 1, 1\nrtk medium = 0.1, 3, 2\nrtk long = none\n"
            + "[Sewersheds]\nManhole\tArea (ac)\nA\t200\nB\t100\n" + Network;
        var r = Tool.Run(input, Us);
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Contains(r.Messages, m => m.Code == "W-WW-001");
        Assert.Contains(r.Messages, m => m.Code == "W-WW-002");
        Assert.True(Num(Row(r, "Manholes: peak HGL", "A")[8]) > 0);
        Assert.Equal(200, Num(Row(r, "Inflows by manhole", "A")[7]), 6);
    }

    [Theory]
    [InlineData("rtk short = 0.5, 0, 1")]
    [InlineData("rtk short = 0.6\nrtk medium = 0.5, 1, 1")]
    [InlineData("storm duration = 6")]
    [InlineData("return period = 25")]
    [InlineData("residential pattern = 1, 2")]
    [InlineData("[Sewersheds]\nManhole\tArea (ac)\nZZ\t5")]
    public void BadInput_IsAnError(string extra)
    {
        var r = Tool.Run("routing = none\n" + extra + "\n" + Network, Us);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code.StartsWith("E-WW"));
    }
}
