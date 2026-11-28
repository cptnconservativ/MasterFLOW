using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Sanitary trunk sewer HGL: flows from the sanitary design, energy solve from the outlet, services and rims.</summary>
public class SanitaryTrunkHglTests
{
    private static readonly SanitaryTrunkHglTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static ToolResult Run(string input) => Tool.Run(input, Us);
    private static IReadOnlyList<object?> Row(ToolResult r, string table, string key) => r.Tables.First(t => t.Title.StartsWith(table)).Rows.First(x => (string?)x[0] == key);
    private static double Num(object? v) => Convert.ToDouble(v);

    // One 18 in. sewer, 500 ft, 0.20 % inside to inside (496 ft run), carrying a 1,000 gpm point flow.
    private const string One = """
        h2s = no
        [Manholes]
        Name	Rim	Deflection
        A	110
        B	109
        [Loads]
        Manhole	Land use	Flow (gpm)	PF
        A	Flow	1000	1
        [Pipes]
        Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n
        P1	A	B	500	18	100.992	100.000	0.013
        """;

    [Fact]
    public void Sample_RunsClean_HasProfile_AndSiIsRefused()
    {
        var r = Run(Tool.SampleInput(Us));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.NotEmpty(r.Figures);
        Assert.Equal(3, r.Tables.First(t => t.Title.StartsWith("Services")).Rows.Count);
        Assert.False(Tool.Run(Tool.SampleInput(Us), new DesignToolContext(UnitSystem.SI)).Succeeded);
        Assert.Contains(DesignTools.All, t => t.Id == "sanitary-trunk-hgl");
    }

    [Fact]
    public void NormalDepthTailwater_GivesUniformFlowHgl()
    {
        var r = Run(One);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        double q = 1000 / 448.831, d = 1.5, s = 0.992 / 496;
        double yn = SanitarySewerTool.NormalDepth(q, d, 0.013, s);
        var row = Row(r, "Sewer HGL", "P1");
        Assert.Equal(100 + yn, Num(row[12]), 2);          // HGL down = outlet invert + normal depth
        Assert.Equal(100.992 + yn, Num(row[11]), 2);      // uniform flow carries it up the pipe
        Assert.Equal(yn / d, Num(row[7]), 3);
        Assert.Equal(yn / d, Num(row[10]), 2);
        Assert.Equal("OK", row[^1]);
    }

    [Fact]
    public void HighTailwater_SurchargesAndBacksUpService()
    {
        var r = Run("outlet water surface = 102.0\nservice freeboard = 1\n" + One + """

            [Services]
            Location	Station (ft)	Name	Elevation
            A		House A	102.02
            P1	250	House mid	104.5
            """);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains("surcharged", (string)Row(r, "Sewer HGL", "P1")[^1]!);
        Assert.Contains(r.Messages, m => m.Code == "W-TH-002");
        // HGL at A is above 102.0 (tailwater plus friction and the upstream-end loss), so House A backs up.
        var a = Row(r, "Services", "House A");
        Assert.True(Num(a[3]) > 102.0);
        Assert.Equal("backs up", a[^1]);
        Assert.Equal("OK", Row(r, "Services", "House mid")[^1]);
        Assert.Contains(r.Messages, m => m.Code == "W-TH-006");
    }

    [Fact]
    public void BackwaterAboveLimit_FlaggedEvenWhenNormalDepthPasses()
    {
        // Tailwater at 0.85 D: below the crown, above 0.70.
        var r = Run("outlet water surface = 101.275\n" + One);
        var row = Row(r, "Sewer HGL", "P1");
        Assert.True(Num(row[7]) < 0.70);
        Assert.True(Num(row[10]) > 0.80);
        Assert.Contains(r.Messages, m => m.Code == "W-TH-001" && m.Text.Contains("normal depth gives"));
    }

    [Fact]
    public void KLosses_HjEqualsKVSquaredOver2g()
    {
        var r = Run("losses = K\nmanhole K = 0.5\n" + One.Replace("P1	A	B", "P0	U	A\nP1	A	B").Replace("[Pipes]", "[Pipes]").Replace("Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n\n", "Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n\nP0	U	A	300	18	101.8	101.2	0.013\n").Replace("P0	U	A\nP1	A	B", "P1	A	B"));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var mh = Row(r, "Manholes", "A");
        double vOut = Num(mh[7]);
        Assert.Equal(0.5 * vOut * vOut / (2 * 32.174), Num(mh[5]), 3);
    }

    [Fact]
    public void Deflection_PicksBendCase()
    {
        string input = One.Replace("A	110", "A	110	90").Replace("Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n\n", "Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n\nP0	U	A	300	18	101.8	101.2	0.013\n");
        var r = Run(input);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains("90", (string)Row(r, "Manholes", "A")[4]!);
    }

    [Fact]
    public void FlowOverride_AndFactor()
    {
        var r = Run("flow factor = 1.25\n" + One + "\n[Flows]\nPipe\tQ peak (gpm)\nP1\t800\n");
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Equal(1000, Num(Row(r, "Sewer HGL", "P1")[5]), 1);
    }

    [Theory]
    [InlineData("[Services]\nLocation\tStation\tName\tElevation\nZZ\t\tNowhere\t105\n", "E-TH-003")]
    [InlineData("[Services]\nLocation\tStation\tName\tElevation\nP1\t900\tToo far\t105\n", "E-TH-003")]
    [InlineData("[Flows]\nPipe\tQ\nP9\t100\n", "E-TH-004")]
    public void BadInput_IsAnError(string extra, string code)
    {
        var r = Run(One + "\n" + extra);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }

    [Theory]
    [InlineData("losses = magic\n")]
    [InlineData("outlet water surface = sideways\n")]
    [InlineData("flow factor = -1\n")]
    [InlineData("rim freeboard = -2\n")]
    public void BadKeys_AreErrors(string key)
    {
        var r = Run(key + One);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == "E-TH-002");
    }
}
