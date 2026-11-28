using System.Text;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Sanitary plan and profile DXF sheets: profile data equals the sanitary design, flags for design exceptions and d/D, well-formed R12 DXF.</summary>
public class SanitarySheetsTests
{
    private static readonly SanitarySheetsTool Tool = new();
    private static readonly SanitarySewerTool Design = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static string Dxf(ToolResult r) => Encoding.ASCII.GetString(r.Files[0].Content);

    private const string TwoPipes = """
        h2s = no
        [Manholes]
        Name	Rim
        A	110
        B	108
        C	106
        [Loads]
        Manhole	Land use	Population
        A	Residential	300
        [Pipes]
        Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert
        P1	A	B	204	8	104.00	103.20
        P2	B	C	304	8	103.10	101.90
        """;

    [Fact]
    public void Sample_RunsClean_AndDxfIsWellFormedR12()
    {
        var r = Tool.Run(Tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        string dxf = Dxf(r);
        var lines = dxf.Split('\n');
        Assert.Equal("AC1009", lines[Array.IndexOf(lines, "$ACADVER") + 2]);
        Assert.Equal("EOF", lines[^2]);
        for (int i = 0; i + 1 < lines.Length; i += 2) Assert.True(int.TryParse(lines[i], out _), $"line {i + 1}: \"{lines[i]}\" is not a group code");
        Assert.Equal(dxf.Split("\nPOLYLINE\n").Length - 1, dxf.Split("\nSEQEND\n").Length - 1);
        Assert.True(dxf.All(c => c < 128));
        foreach (var layer in new[] { "MF-SAN-PROF-WS", "MF-SAN-PROF-PIPE", "MF-SAN-PROF-GROUND", "MF-SAN-PROF-FLAG", "MF-SHEET" }) Assert.Contains("\n" + layer + "\n", dxf);
        Assert.EndsWith(".dxf", r.Files[0].FileName);
        // The sanitary design tables come along so the sheet can be checked against the computation form.
        Assert.Contains(r.Tables, t => t.Title.StartsWith("Sanitary sewer design computations"));
        if (Environment.GetEnvironmentVariable("MF_SAN_DXF_OUT") is { } o) File.WriteAllBytes(o, r.Files[0].Content);
    }

    [Fact]
    public void ProfileTable_MatchesDesign_WaterSurfaceIsInvertPlusPeakDepth()
    {
        var r = Tool.Run(TwoPipes, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var (_, d) = Design.Design(TwoPipes, Us);
        var sewers = r.Tables.First(t => t.Title == "Profile A to C: sewers");
        var mh = r.Tables.First(t => t.Title == "Profile A to C: manholes");
        // Stations from the outlet: C 0+00, B 3+04, A 5+08.
        Assert.Equal(["0+00.00", "3+04.00", "5+08.00"], mh.Rows.Select(x => (string)x[0]!).ToArray());
        foreach (var row in sewers.Rows)
        {
            var x = d!.Pipes.First(p => p.Name == (string)row[0]!);
            Assert.Equal(Math.Round(x.UpInvert + x.PeakDepth, 2), (double)row[7]!, 6);
            Assert.Equal(Math.Round(x.DownInvert + x.PeakDepth, 2), (double)row[8]!, 6);
            Assert.Equal(Math.Round(x.PeakDd, 3), (double)row[9]!, 6);
        }
        Assert.Contains("INV OUT 103.10", Dxf(r));
        Assert.Contains("INV IN 103.20", Dxf(r));
    }

    [Fact]
    public void LowVelocitySewer_IsFlaggedAsDesignException()
    {
        // 300 people on 8 in. at 0.4 %: well under 2 ft/s at peak.
        var r = Tool.Run(TwoPipes, Us);
        var flags = r.Tables.First(t => t.Title == "Flags on the sheets");
        Assert.Contains(flags.Rows, x => (string)x[0]! == "P2" && ((string)x[2]!).StartsWith("design exception"));
        Assert.Contains("DESIGN EXCEPTION: V", Dxf(r));
        Assert.Contains(r.Messages, m => m.Code == "W-SP-003");
        Assert.Contains(r.Messages, m => m.Code == "W-SS-002");   // the design's own warning is passed through
    }

    [Fact]
    public void HighDd_IsFlagged()
    {
        string input = TwoPipes.Replace("A	Residential	300", "A	Residential	9000");
        var r = Tool.Run(input, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var flags = r.Tables.First(t => t.Title == "Flags on the sheets");
        Assert.Contains(flags.Rows, x => ((string)x[2]!).StartsWith("d/D") || ((string)x[2]!).StartsWith("surcharged"));
    }

    [Fact]
    public void LongRun_SplitsOntoSheets_AndBadRunIsAnError()
    {
        // 508 ft at 1" = 10' on 11 x 8.5: 80 ft of profile per sheet → 7 sheets.
        var r = Tool.Run("horizontal scale = 10\nvertical scale = 1\nsheet = 11 x 8.5\n" + TwoPipes, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains("SHEET 7", Dxf(r));
        Assert.DoesNotContain("SHEET 8", Dxf(r));
        Assert.Contains(r.Messages, m => m.Code == "I-SP-001");   // no coordinates, no plan
        Assert.Contains(Tool.Run("runs = Z9\n" + TwoPipes, Us).Messages, m => m.Code == "E-SP-003");
        Assert.False(Tool.Run(TwoPipes, new DesignToolContext(UnitSystem.SI)).Succeeded);
    }
}
