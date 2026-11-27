using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Sanitary bypass pumping plan: diurnal flow at the connection, pump size and duration, line TDH, NPSH, upstream storage.</summary>
public class SanitaryBypassTests
{
    private static readonly SanitaryBypassTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static double Num(object? v) => Convert.ToDouble(v);
    private static IReadOnlyList<object?> Row(ToolResult r, string table, string first) => r.Tables.First(t => t.Title.StartsWith(table)).Rows.First(x => ((string?)x[0])!.StartsWith(first));
    private static string Msgs(ToolResult r) => string.Join("\n", r.Messages.Select(m => m.Code + " " + m.Text));

    // Flow-only bypass: 100 gpm average, entered lift.
    private const string FlowOnly = """
        average flow = 100
        static head = 10
        suction lift = 8
        site elevation = 0
        line sizes = 6
        discharge length = 500
        C = 150
        fittings K = 0
        suction length = 0
        suction diameter = 6
        """;

    [Fact]
    public void Sample_RunsClean_AndSiIsRefused()
    {
        var r = Tool.Run(Tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Contains(r.Tables, t => t.Title.StartsWith("Storage upstream"));
        Assert.NotEmpty(r.Charts);
        Assert.False(Tool.Run(Tool.SampleInput(Us), new DesignToolContext(UnitSystem.SI)).Succeeded);
    }

    [Fact]
    public void FlatFlow_TdhVolumeAndPumps_ByHand()
    {
        var r = Tool.Run(FlowOnly + "\npattern = flat\nwork start = 22:00\nwork duration = 4\nsetup time = 1\npump capacity = 40\n", Us);
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Equal(100, Num(Row(r, "Bypass pumping plan", "Bypass design flow")[1]), 6);
        Assert.Equal(100 * 6 * 60, Num(Row(r, "Bypass pumping plan", "Volume pumped")[1]), 0);
        Assert.Equal("3 duty + 1 standby", Row(r, "Bypass pumping plan", "Pumps")[1]);
        // TDH = 10 + 10.44·500·100^1.852/(150^1.852·6^4.8655) + entrance K 1.0 · V²/2g (suction length 0).
        double hf = 10.44 * 500 * Math.Pow(100, 1.852) / (Math.Pow(150, 1.852) * Math.Pow(6, 4.8655));
        double v = 100 / 448.831 / (Math.PI * 0.25 / 4);
        Assert.Equal(hf, Num(Row(r, "Bypass pumping plan", "Friction, discharge")[1]), 2);
        Assert.Equal(10 + hf + v * v / (2 * 32.174), Num(Row(r, "Bypass pumping plan", "Total dynamic head")[1]), 1);
        // NPSHa at sea level, 60 °F: 33.96 − 8 − V²/2g − 0.59.
        Assert.InRange(Num(Row(r, "Bypass pumping plan", "NPSH available")[1]) - (33.96 - 8 - v * v / (2 * 32.174) - 0.59), -0.06, 0.06);
    }

    [Fact]
    public void ResidentialPattern_WindowPeakAndBestWindow()
    {
        var r = Tool.Run(FlowOnly + "\npattern = residential\nwork start = 22:00\nwork duration = 2\nsetup time = 0\ndesign flow = window peak\n", Us);
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Equal(100 * Math.Max(SanitaryDiurnal.Residential[22], SanitaryDiurnal.Residential[23]), Num(Row(r, "Bypass pumping plan", "Highest flow while pumping")[1]), 1);
        Assert.Equal(100 * SanitaryDiurnal.Residential[22], Num(Row(r, "Bypass pumping plan", "Bypass design flow")[1]), 0);
        // Four hours: 02:00–06:00 has the lowest highest hour (0.45).
        var r4 = Tool.Run(FlowOnly + "\npattern = residential\nwork duration = 4\nsetup time = 0\n", Us);
        Assert.Equal("02:00", Row(r4, "Work window", "Lowest flow")[1]);
        Assert.Equal(45, Num(Row(r4, "Work window", "Lowest flow")[2]), 1);
        Assert.Contains(r4.Messages, m => m.Code == "I-BP-005");
        // Daily peak at 09:00 × wet weather factor and safety factor.
        var rw = Tool.Run(FlowOnly + "\npattern = residential\nwet weather factor = 1.5\nsafety factor = 1.2\n", Us);
        Assert.Equal(100 * 1.5 * 1.5 * 1.2, Num(Row(rw, "Bypass pumping plan", "Bypass design flow")[1]), 0);
    }

    [Fact]
    public void PipeStorage_AndAtmosphere_ByHand()
    {
        // A level 10 ft pipe of D = 1 ft filled from empty to the crown: π/4 × 10 ft³; to half depth: half of it; from half to full: the other half.
        Assert.Equal(Math.PI / 4 * 10, SanitaryBypassTool.PipeStorage(101, 100, 100, 10, 1), 6);
        Assert.Equal(Math.PI / 8 * 10, SanitaryBypassTool.PipeStorage(100.5, 100, 100, 10, 1), 6);
        Assert.Equal(Math.PI / 8 * 10, SanitaryBypassTool.PipeStorage(105, 100, 100, 10, 1, 0.5), 6);
        Assert.Equal(0, SanitaryBypassTool.PipeStorage(99, 100, 100, 10, 1), 9);
        Assert.Equal(33.96, SanitaryBypassTool.AtmosphericHead(0), 6);
        Assert.Equal(13.17 * 144 / 62.37, SanitaryBypassTool.AtmosphericHead(3000), 1);   // 13.17 psia (standard atmosphere)
        Assert.Equal(0.59, SanitaryBypassTool.VaporHead(60), 2);         // 0.256 psia
    }

    [Fact]
    public void Connection_FromSavedDesign_DesignPeakAndResponseTime()
    {
        var ctx = Us with { ToolInputs = new Dictionary<string, string> { ["sanitary-network"] = new SanitarySewerTool().SampleInput(Us) } };
        var (_, d) = SanitarySewerTool.Design(ctx.ToolInputs["sanitary-network"], ctx);
        var s5 = d!.Pipe("S-5")!;
        var r = Tool.Run("connection = S-5\ndesign flow = design peak\nresponse time = 120", ctx);
        Assert.True(r.Succeeded, Msgs(r));
        Assert.Equal(s5.QpeakGpm + s5.IiGpm, Num(Row(r, "Bypass pumping plan", "Bypass design flow")[1]), 0);
        Assert.Equal(s5.QaveGpm, Num(Row(r, "Bypass pumping plan", "Average flow")[1]), 1);
        // Static head = MH-6 rim + 3 ft riser − the lowest invert at MH-5.
        double low = Math.Min(s5.UpInvert, d.Pipe("S-4")!.DownInvert);
        Assert.Equal(d.Manhole("MH-6")!.Rim!.Value + 3 - low, Num(Row(r, "Bypass pumping plan", "Static head")[1]), 2);
        Assert.Contains(r.Messages, m => m.Code == "W-BP-004");
        // The overflow storage is larger than the surcharge storage.
        var st = r.Tables.First(t => t.Title.StartsWith("Storage")).Rows;
        Assert.True(Num(st[^1][2]) > Num(st[0][2]));
    }

    [Theory]
    [InlineData("connection = S-9", "E-BP-003")]
    [InlineData("work duration = 6", "E-BP-004")]
    [InlineData("average flow = 50\nwork start = 26:00\nstatic head = 5\nsuction lift = 5", "E-BP-006")]
    [InlineData("average flow = 50\ndesign flow = design peak\nstatic head = 5\nsuction lift = 5", "E-BP-007")]
    [InlineData("average flow = 50", "E-BP-009")]
    [InlineData("average flow = 50\nstatic head = 5\nsuction lift = 5\npattern = 1, 2, 3", "E-BP-005")]
    public void BadInput_IsAnError(string input, string code)
    {
        var ctx = Us with { ToolInputs = new Dictionary<string, string> { ["sanitary-network"] = new SanitarySewerTool().SampleInput(Us) } };
        var r = Tool.Run(input, ctx);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }
}
