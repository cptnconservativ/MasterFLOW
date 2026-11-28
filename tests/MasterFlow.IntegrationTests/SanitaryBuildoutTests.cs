using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Sanitary build-out projection and phasing: growth schedule, year d/D reaches the limit, build now or relieve later.</summary>
public class SanitaryBuildoutTests
{
    private static readonly SanitaryBuildoutTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static ToolResult Run(string input) => Tool.Run(input, Us);
    private static ToolTable Table(ToolResult r, string title) => r.Tables.First(t => t.Title.StartsWith(title));
    private static IReadOnlyList<object?> Row(ToolResult r, string table, object key) => Table(r, table).Rows.First(x => Equals(x[0], key) || (x[0] is double d && key is int i && d == i) || (x[0] is int j && key is int k && j == k));
    private static double Num(object? v) => Convert.ToDouble(v);

    // One 12 in. sewer at 0.40 % (300 ft run): point flows with PF 1, so Q peak = the flow.
    private static string One(string growth, string keys = "") => """
        base year = 2030
        build-out year = 2050
        report every = 5

        """ + keys + """
        [Manholes]
        Name	Rim
        A	110
        B	108
        [Loads]
        Manhole	Land use	Flow (gpm)	PF
        A	Flow	100	1
        [Growth]
        Manhole	Land use	Flow (gpm)	PF	Start year	End year

        """ + growth + """

        [Pipes]
        Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n
        P1	A	B	304	12	102.2	101.0	0.013
        """;

    [Fact]
    public void Sample_RunsClean_WithChart_AndSiIsRefused()
    {
        var r = Run(Tool.SampleInput(Us));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Single(r.Charts);
        var recs = Table(r, "Phasing").Rows.Select(x => (string)x[^1]!).ToList();
        Assert.Contains(recs, s => s.StartsWith("Adequate"));
        Assert.Contains(recs, s => s.Contains("relieve"));
        Assert.Contains(recs, s => s.Contains("now (reaches"));
        Assert.False(Tool.Run(Tool.SampleInput(Us), new DesignToolContext(UnitSystem.SI)).Succeeded);
        Assert.Contains(DesignTools.All, t => t.Id == "sanitary-buildout");
    }

    [Fact]
    public void LinearRamp_AddsFlowInProportion()
    {
        var r = Run(One("A\tFlow\t200\t1\t2030\t2040"));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Equal(100, Num(Row(r, "Flow at the outlet", 2030)[3]), 1);
        Assert.Equal(200, Num(Row(r, "Flow at the outlet", 2035)[3]), 1);
        Assert.Equal(300, Num(Row(r, "Flow at the outlet", 2040)[3]), 1);
        Assert.Equal(300, Num(Row(r, "Flow at the outlet", 2050)[3]), 1);
    }

    [Fact]
    public void StepGrowth_WithNoEndYear_AllAtOnce()
    {
        var r = Run(One("A\tFlow\t50\t1\t2036\t"));
        Assert.Equal(100, Num(Row(r, "Flow at the outlet", 2035)[3]), 1);
        Assert.Equal(150, Num(Row(r, "Flow at the outlet", 2040)[3]), 1);
    }

    [Fact]
    public void YearReachingLimit_MatchesHandCalculation()
    {
        // Q at d/D = 0.70 on this pipe, then the first whole year the ramp 100 → 100 + 1,000 gpm (2030–2050) passes it.
        double d = 1.0, s = 1.2 / 300;
        double q70 = SanitarySewerTool.Discharge(0.70 * d, d, 0.013, s) * 448.831;
        int expected = Enumerable.Range(2030, 21).First(y => 100 + 1000 * (y - 2030) / 20.0 > q70);
        var r = Run(One("A\tFlow\t1000\t1\t2030\t2050"));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var row = Row(r, "Phasing", "P1");
        Assert.Equal(expected.ToString(), row[7]);
        Assert.True(Num(row[9]) > 12);
    }

    [Fact]
    public void DecisionHorizon_ChoosesBuildNowOrRelieve()
    {
        string g = "A\tFlow\t1000\t1\t2030\t2050";
        var near = Run(One(g, "decision horizon = 20\n"));
        Assert.StartsWith("Build 15 in. now", (string)Row(near, "Phasing", "P1")[^1]!);
        Assert.Contains(near.Messages, m => m.Code == "W-BO-001");
        var far = Run(One(g, "decision horizon = 2\n"));
        Assert.StartsWith("Build 12 in. now; relieve or parallel by", (string)Row(far, "Phasing", "P1")[^1]!);
    }

    [Fact]
    public void OverNow_AndAdequate()
    {
        var small = Run(One("A\tFlow\t10\t1\t2031\t"));
        Assert.Equal("Adequate through build-out", Row(small, "Phasing", "P1")[^1]);
        var over = Run(One("A\tFlow\t10\t1\t2031\t").Replace("A\tFlow\t100\t1", "A\tFlow\t900\t1"));
        Assert.StartsWith("Over the limit now", (string)Row(over, "Phasing", "P1")[^1]!);
        Assert.Contains(over.Messages, m => m.Code == "W-BO-002");
    }

    [Theory]
    [InlineData("", "", "E-BO-004")]                                           // no growth
    [InlineData("A\tFlow\t10\t1\t2040\t2035", "", "E-BO-003")]                 // end before start
    [InlineData("A\tFlow\t10\t1\t\t", "", "E-BO-003")]                         // no start
    [InlineData("A\tFlow\t10\t1\t2031\t", "build-out year = 2020\n", "E-BO-002")]
    [InlineData("A\tFlow\t10\t1\t2031\t", "report every = 0\n", "E-BO-002")]
    [InlineData("Z\tFlow\t10\t1\t2031\t", "", "E-BO-006")]                     // no such manhole
    public void BadInput_IsAnError(string growth, string keys, string code)
    {
        var r = Run(One(growth, keys));
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }

    [Fact]
    public void MissingPlannedDiameter_IsAnError()
    {
        var r = Run(One("A\tFlow\t10\t1\t2031\t").Replace("304\t12\t", "304\t\t"));
        Assert.Contains(r.Messages, m => m.Code == "E-BO-005");
    }

    [Fact]
    public void Helpers_StripSections_AndBlankDiameters()
    {
        string t = "a = 1\n[Loads]\nx\ty\n1\t2\n[Pipes]\nName\tFrom\tTo\tLength\tDiameter\tUp\tDn\nP\tA\tB\t100\t12\t5\t4\n";
        string stripped = SanitaryBuildoutTool.Strip(t, "Loads");
        Assert.DoesNotContain("[Loads]", stripped);
        Assert.Contains("[Pipes]", stripped);
        Assert.Contains("P\tA\tB\t100\t\t5\t4", SanitaryBuildoutTool.BlankDiameters(t));
        Assert.Contains("Name\tFrom\tTo\tLength\tDiameter", SanitaryBuildoutTool.BlankDiameters(t));
    }
}
