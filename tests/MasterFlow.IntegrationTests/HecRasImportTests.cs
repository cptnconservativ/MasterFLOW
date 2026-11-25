using System.Globalization;
using System.Text;
using MasterFlow.Application.River;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-RAS geometry (.g01) import into the river profile tool, checked against the same sections typed as tables.</summary>
public class HecRasImportTests
{
    private static readonly RiverProfileTool Tool = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Values in 8-character fixed fields, <paramref name="perLine"/> to a line, as HEC-RAS writes them.</summary>
    private static string Fixed(IEnumerable<double> vals, int perLine)
    {
        var sb = new StringBuilder();
        int c = 0;
        foreach (var v in vals)
        {
            sb.Append(v.ToString("0.###", Inv).PadLeft(8));
            if (++c % perLine == 0) sb.Append('\n');
        }
        if (c % perLine != 0) sb.Append('\n');
        return sb.ToString();
    }

    private static (double X, double Z)[] Trap(double z0) => [(0, z0 + 12), (24, z0), (34, z0), (58, z0 + 12)];

    private static string Xs(double rs, double len, (double X, double Z)[] pts, (double Sta, double N)[] mann, double lb, double rb, string extra = "")
    {
        var sb = new StringBuilder();
        sb.Append(Inv, $"Type RM Length L Ch R = 1 ,{rs}     ,{len},{len},{len}\n");
        sb.Append(Inv, $"#Sta/Elev= {pts.Length}\n");
        sb.Append(Fixed(pts.SelectMany(p => new[] { p.X, p.Z }), 10));
        sb.Append(Inv, $"#Mann= {mann.Length} , 0 , 0\n");
        sb.Append(Fixed(mann.SelectMany(m => new[] { m.Sta, m.N, 0.0 }), 9));
        sb.Append(Inv, $"Bank Sta={lb},{rb}\n");
        sb.Append(extra);
        sb.Append("Exp/Cntr=0.3,0.1\n\n");
        return sb.ToString();
    }

    private static string Geometry(int count = 5, double dx = 400, double s0 = 0.002)
    {
        var sb = new StringBuilder("Geom Title=Test creek\nProgram Version=6.30\n\nRiver Reach=Test Creek      ,Upper          \n");
        for (int i = count - 1; i >= 0; i--)   // HEC-RAS lists upstream first
        {
            double rs = i * dx;
            sb.Append(Xs(rs, i == 0 ? 0 : dx, Trap(1000 + s0 * rs), [(0, 0.05), (24, 0.03), (34, 0.05)], 24, 34));
        }
        return sb.ToString();
    }

    private static string Tables(int count = 5, double dx = 400, double s0 = 0.002)
    {
        var sb = new StringBuilder("[Sections]\nRS\tLeft bank\tRight bank\tLOB length\tCh length\tROB length\tn LOB\tn Ch\tn ROB\n");
        var pts = new StringBuilder("[Points]\nRS\tStation\tElevation\n");
        for (int i = 0; i < count; i++)
        {
            double rs = i * dx, len = i == 0 ? 0 : dx;
            sb.Append(Inv, $"{rs}\t24\t34\t{len}\t{len}\t{len}\t0.05\t0.03\t0.05\n");
            foreach (var (x, z) in Trap(1000 + s0 * rs)) pts.Append(Inv, $"{rs}\t{x}\t{z}\n");
        }
        return sb + "\n" + pts;
    }

    private const string Head = "downstream = normal\ndownstream slope = 0.002\n";
    private const string Flows = "[Flows]\nRS\tQ100\n1600\t600\n";

    private static ToolResult Run(string input)
    {
        var r = Tool.Run(input, new DesignToolContext(UnitSystem.USCustomary));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static string WriteTemp(string text)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mf-ras-{Guid.NewGuid():N}.g01");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Parse_ReadsSectionsInFixedWidthFields()
    {
        var g = HecRasGeometry.Parse(Geometry());
        Assert.Equal("Test creek", g.Title);
        Assert.Equal(5, g.Sections.Count);
        var s = g.Sections.First(x => x.Rs == "800");
        Assert.Equal("Test Creek", s.River);
        Assert.Equal("Upper", s.Reach);
        Assert.Equal(400, s.LengthCh);
        Assert.Equal([(0.0, 1013.6), (24.0, 1001.6), (34.0, 1001.6), (58.0, 1013.6)], s.Points);
        Assert.Equal(3, s.Manning.Count);
        Assert.Equal(0.03, s.Manning[1].N);
        Assert.Equal((24.0, 34.0), (s.LeftBank, s.RightBank));
        Assert.Equal((0.1, 0.3), (s.Contraction, s.Expansion));
    }

    [Fact]
    public void ImportedRun_MatchesTheSameSectionsTyped()
    {
        string path = WriteTemp(Geometry());
        try
        {
            var a = Run(Head + $"hec-ras geometry = {path}\n" + Flows);
            var b = Run(Head + Flows + Tables());
            var ra = a.Tables[0].Rows.ToList(); var rb = b.Tables[0].Rows.ToList();
            Assert.Equal(rb.Count, ra.Count);
            for (int i = 0; i < ra.Count; i++)
                for (int c = 0; c < ra[i].Count; c++) Assert.Equal(rb[i][c]?.ToString(), ra[i][c]?.ToString());
            Assert.Contains(a.Messages, m => m.Code == "I-RAS-001" && m.Text.Contains("5 cross-sections"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ManningBreakBetweenSurveyPoints_IsInserted()
    {
        var s = HecRasGeometry.Parse(Geometry()).Sections[0] with { Manning = [(0, 0.05), (12, 0.04), (24, 0.03)] };
        var w = HecRasGeometry.WithManningPoints(s);
        Assert.Equal(5, w.Points.Count);
        Assert.Equal(12, w.Points[1].Sta);
        Assert.Equal(0.5 * (w.Points[0].Elev + w.Points[2].Elev), w.Points[1].Elev, 9);
    }

    [Fact]
    public void BridgesAndInteriorIneffectiveAreas_AreListedNotImported()
    {
        string text = Geometry().Replace("Type RM Length L Ch R = 1 ,800 ",
            "Type RM Length L Ch R = 3 ,900     ,20,20,20\nBEGIN BRIDGE\n\nType RM Length L Ch R = 1 ,800 ");
        text = text.Replace("Bank Sta=24,34\nExp/Cntr", "Bank Sta=24,34\n#XS Ineff= 2 ,0\n" + Fixed([0, 20, 1005, 40, 45, 1006], 9) + "Exp/Cntr");
        var g = HecRasGeometry.Parse(text);
        Assert.Equal(5, g.Sections.Count);
        Assert.Contains(g.Skipped, n => n.Contains("RS 900") && n.Contains("bridge"));
        Assert.Contains(g.Skipped, n => n.Contains("from 40 to 45"));
        Assert.All(g.Sections, s => Assert.Equal((20.0, 1005.0), s.IneffLeft));
        var (_, msgs) = RiverProfileTool.ImportHecRas(text, null);
        Assert.Contains(msgs, m => m.Code == "W-RAS-003" && m.Text.Contains("[Bridges]"));
    }

    [Fact]
    public void ReachSelection_PicksTheNamedReach()
    {
        string two = Geometry() + Geometry(3).Replace("Geom Title=Test creek\nProgram Version=6.30\n\n", "").Replace("Upper", "Lower");
        var (tables, msgs) = RiverProfileTool.ImportHecRas(two, "test creek, lower");
        Assert.NotNull(tables);
        Assert.Contains(msgs, m => m.Text.Contains("3 cross-sections of Test Creek, Lower"));
        var (_, first) = RiverProfileTool.ImportHecRas(two, null);
        Assert.Contains(first, m => m.Code == "W-RAS-001");
        var (none, bad) = RiverProfileTool.ImportHecRas(two, "Other, Reach");
        Assert.Null(none);
        Assert.Contains(bad, m => m.Code == "E-RAS-005");
    }

    [Fact]
    public void Errors_ForMissingFileAndMixedInput()
    {
        var r = Tool.Run(Head + "hec-ras geometry = /no/such/file.g01\n" + Flows, new DesignToolContext(UnitSystem.USCustomary));
        Assert.Contains(r.Messages, m => m.Code == "E-RAS-002");
        string path = WriteTemp(Geometry());
        try
        {
            var r2 = Tool.Run(Head + $"hec-ras geometry = {path}\n" + Flows + Tables(), new DesignToolContext(UnitSystem.USCustomary));
            Assert.Contains(r2.Messages, m => m.Code == "E-RAS-001");
        }
        finally { File.Delete(path); }
        Assert.Contains(RiverProfileTool.ImportHecRas("Geom Title=x\n", null).Messages, m => m.Code == "E-RAS-004");
    }
}
