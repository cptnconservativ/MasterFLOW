using System.Text;
using MasterFlow.Application.Drawing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Plan and profile DXF sheets: profile data equals the network solution, the DXF is well formed, sheets split by scale.</summary>
public class PlanProfileSheetsTests
{
    private static readonly PlanProfileSheetsTool Tool = new();

    private static StormNetwork Net(bool coords = true)
    {
        var n = NetworkOutputTests.NetA();
        if (!coords) return n;
        var xy = new Dictionary<string, (double E, double N)> { ["OUT"] = (1000, 5000), ["MH-2"] = (1200, 5000), ["CB-3"] = (1200, 5150), ["CB-4"] = (1260, 5000) };
        return n with { Structures = n.Structures.Select(s => s with { Easting = xy[s.Name].E, Northing = xy[s.Name].N }).ToList() };
    }

    private static DesignToolContext Ctx(StormNetwork n) => new(n.Units, n, StormNetworkSolver.Solve(n));

    [Fact]
    public void LongestRun_TablesMatchTheSolution()
    {
        var n = Net(); var ctx = Ctx(n);
        var r = Tool.Run(Tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        // Longest run: CB-3 → MH-2 → OUT (150 + 200 ft).
        var st = r.Tables.Single(t => t.Title.EndsWith("structures"));
        Assert.Equal(["0+00.00", "2+00.00", "3+50.00"], st.Rows.Select(x => (string)x[0]!).ToArray());
        Assert.Equal(["OUT", "MH-2", "CB-3"], st.Rows.Select(x => (string)x[1]!).ToArray());
        var res = ctx.NetworkResult!;
        Assert.Equal(Math.Round(res.Structure("MH-2")!.Hgl, 2), (double)st.Rows[1][5]!);
        Assert.Equal(Math.Round(res.Structure("CB-3")!.Egl, 2), (double)st.Rows[2][6]!);
        var pt = r.Tables.Single(t => t.Title.EndsWith("pipes"));
        Assert.Equal(Math.Round(res.Pipe("P1")!.HglUp, 2), (double)pt.Rows[0][7]!);
        Assert.Equal("24 in.", pt.Rows[0][2]);
        Assert.Equal(0.4, (double)pt.Rows[0][4]!, 6);
        Assert.Single(r.Figures);
        Assert.Single(r.Files);
        Assert.EndsWith(".dxf", r.Files[0].FileName);
    }

    [Fact]
    public void Dxf_IsWellFormedR12_WithLayersAndEntities()
    {
        var n = Net(); var ctx = Ctx(n);
        var r = Tool.Run("runs = all\nplan = yes", ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        string dxf = Encoding.ASCII.GetString(r.Files[0].Content);
        var lines = dxf.Split('\n');
        Assert.Equal("AC1009", lines[Array.IndexOf(lines, "$ACADVER") + 2]);
        Assert.Equal("EOF", lines[^2]);
        Assert.Equal(0, (lines.Length - 1) % 2);   // group code / value pairs
        for (int i = 0; i + 1 < lines.Length; i += 2) Assert.True(int.TryParse(lines[i], out _), $"line {i + 1}: \"{lines[i]}\" is not a group code");
        foreach (var layer in new[] { "MF-PLAN-PIPE", "MF-PROF-HGL", "MF-PROF-EGL", "MF-PROF-GROUND", "MF-SHEET" }) Assert.Contains("\n" + layer + "\n", dxf);
        Assert.Contains("DASHED", dxf);
        Assert.True(dxf.Split("\nPOLYLINE\n").Length - 1 == dxf.Split("\nSEQEND\n").Length - 1);
        Assert.True(dxf.All(c => c < 128));
        // all: CB-3 and CB-4 runs; 2 figures.
        Assert.Equal(2, r.Figures.Count);
        Assert.Contains("CB-4", dxf);
        Assert.Contains("RIM 106.50", dxf);
        if (Environment.GetEnvironmentVariable("MF_DXF_OUT") is { } o) File.WriteAllBytes(o, r.Files[0].Content);
    }

    [Fact]
    public void LongRunSplitsOntoSheets_AndPlanNeedsCoordinates()
    {
        var n = Net(coords: false); var ctx = Ctx(n);
        // 1" = 10' on an 11 × 8.5 sheet: (11 − 2 margins − 1 label strip) in. × 10 = 80 ft of profile per sheet → 350 ft needs 5 sheets.
        var r = Tool.Run("horizontal scale = 10\nvertical scale = 1\nsheet = 11 x 8.5", ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains(r.Messages, m => m.Code == "I-PP-001");
        Assert.Contains("on 5 sheet(s)", r.Summary[0]);
        string dxf = Encoding.ASCII.GetString(r.Files[0].Content);
        Assert.Contains("SHEET 5", dxf);
        Assert.DoesNotContain("MF-PLAN-PIPE\n10", dxf);
    }

    [Fact]
    public void HglClearanceFlagged()
    {
        var n = Net() with { TailwaterElevation = 104.6 };   // high tailwater pushes the HGL up to the rims
        var ctx = Ctx(n);
        var r = Tool.Run("runs = CB-4", ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains(r.Messages, m => m.Code == "W-PP-003");
        Assert.Contains("BELOW RIM", Encoding.ASCII.GetString(r.Files[0].Content));
        Assert.Contains(r.Tables[0].Rows, x => ((string)x[8]!).StartsWith("HGL within"));
    }

    [Fact]
    public void Errors()
    {
        Assert.Contains(Tool.Run("", new DesignToolContext(UnitSystem.USCustomary)).Messages, m => m.Code == "E-PP-001");
        var ctx = Ctx(Net());
        Assert.Contains(Tool.Run("runs = XX", ctx).Messages, m => m.Code == "E-PP-004");
        Assert.Contains(Tool.Run("sheet = big", ctx).Messages, m => m.Code == "E-PP-003");
    }

    [Theory]
    [InlineData(0, true, "0+00.00")]
    [InlineData(350.5, true, "3+50.50")]
    [InlineData(1234.5, false, "1+234.50")]
    public void Stationing(double s, bool us, string expected) => Assert.Equal(expected, PlanProfileSheetsTool.Sta(s, us));

    [Fact]
    public void DxfWriter_AsciiText()
    {
        var w = new DxfWriter();
        w.Text("T", 0, 0, 1, "12 in. × 2 – 3°");
        Assert.Contains("12 in. x 2 - 3%%d", w.ToText());
    }
}
