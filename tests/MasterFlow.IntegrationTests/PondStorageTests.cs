using System.Text;
using MasterFlow.Application.Surfaces;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Pond stage–storage from a TIN: exact against the prismoidal frustum formula, LandXML reading, boundary and rim checks.</summary>
public class PondStorageTests
{
    private static readonly PondStorageTool Tool = new();
    private static DesignToolContext Ctx(UnitSystem u = UnitSystem.USCustomary) => new(u);

    // 4H:1V frustum: bottom 100 × 50 at 100.0, top 148 × 98 at 106.0.
    private static readonly (double, double, double)[] FrustumPts =
    [
        (5000, 8000, 100), (5100, 8000, 100), (5100, 8050, 100), (5000, 8050, 100),
        (4976, 7976, 106), (5124, 7976, 106), (5124, 8074, 106), (4976, 8074, 106),
    ];
    private static readonly (int, int, int)[] FrustumFaces = [(0, 1, 2), (0, 2, 3), (0, 4, 5), (0, 5, 1), (1, 5, 6), (1, 6, 2), (2, 6, 7), (2, 7, 3), (3, 7, 4), (3, 4, 0)];

    [Fact]
    public void Frustum_TinVolumes_EqualThePrismoidalFormula()
    {
        var tin = new TinSurface("F", FrustumPts, FrustumFaces);
        var basin = new BasinGeometry(100, 100, 50, 4, 106);
        for (double h = 0; h <= 6.0001; h += 0.25)
        {
            var (v, a) = tin.Below(100 + h);
            Assert.Equal(basin.Volume(h), v, 6);
            Assert.Equal(basin.Area(h), a, 6);
        }
        Assert.Equal(56208, basin.Volume(6), 6);   // hand check: 5000·6 + 150·4·36 + (4/3)·16·216
    }

    [Fact]
    public void SingleSlopedTriangle_MatchesHandCalc()
    {
        // Right triangle legs 10 × 10 (area 50) with z = 0, 0, 3 at the vertices: full volume below 3 = 50 × (3 − 1) = 100.
        // Below 1.5: z = 0.3·y, so the wet part is y ≤ 5, a trapezoid of area 50 − 12.5 = 37.5, and
        // volume = ∫0..5 (10 − y)(1.5 − 0.3y) dy = 75 − 56.25 + 12.5 = 31.25.
        var tin = new TinSurface("T", [(0, 0, 0), (10, 0, 0), (0, 10, 3)], [(0, 1, 2)]);
        Assert.Equal(100, tin.Below(3).Volume, 9);
        Assert.Equal(37.5, tin.Below(1.5).Area, 9);
        Assert.Equal(31.25, tin.Below(1.5).Volume, 9);
        Assert.Equal(0, tin.Below(-1).Volume);
    }

    [Fact]
    public void Sample_ReportsFrustumStorage_AndPasteBlocks()
    {
        var r = Tool.Run(Tool.SampleInput(Ctx()), Ctx());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var t = r.Tables[0];
        Assert.Equal(13, t.Rows.Count);
        Assert.Equal(56208.0, (double)t.Rows[^1][4]!, 0);
        Assert.Equal("rim (spill)", t.Rows[^1][8]);
        Assert.DoesNotContain(r.Messages, m => m.Code == "W-PS-003");
        var paste = string.Join("\n", r.Tables[1].Rows.Select(x => x[0]));
        Assert.Contains("Elevation (ft)\tVolume (ft³)", paste);
        Assert.Contains("103.00\t20976", paste);
        var si = Tool.Run(Tool.SampleInput(Ctx(UnitSystem.SI)), Ctx(UnitSystem.SI));
        Assert.True(si.Succeeded, string.Join("\n", si.Messages.Select(m => m.Text)));
        Assert.Equal(new BasinGeometry(30, 30, 15, 3, 32).Volume(2), (double)si.Tables[0].Rows[^1][4]!, 0);
    }

    [Fact]
    public void VolumeBlock_PastesIntoTheDetentionTool()
    {
        var r = Tool.Run(Tool.SampleInput(Ctx()), Ctx());
        var rows = r.Tables[1].Rows.Select(x => (string)x[0]!).ToList();
        int start = rows.IndexOf("[Stage storage]"), end = rows.FindIndex(start + 1, l => l.StartsWith('#'));
        string block = string.Join("\n", rows.Skip(start).Take(end - start));
        var det = DesignTools.Find("detention-routing")!;
        string sample = det.SampleInput(Ctx());
        int a = sample.IndexOf("[Stage storage]", StringComparison.Ordinal), b = sample.IndexOf("\n\n", a, StringComparison.Ordinal);
        string input = sample[..a] + block + sample[b..];
        var d = det.Run(input, Ctx());
        Assert.DoesNotContain(d.Messages, m => m.Code is "E-DT-003" or "E-DT-004");
        Assert.Contains(d.Summary, l => l.Contains("1.290 ac-ft"));
    }

    [Fact]
    public void TopAboveRim_Warns()
    {
        var input = Tool.SampleInput(Ctx()).Replace("# top = 106", "top = 107");
        var r = Tool.Run(input, Ctx());
        Assert.Contains(r.Messages, m => m.Code == "W-PS-003" && m.Text.Contains("106.00"));
        Assert.Contains(r.Tables[0].Rows, row => (string?)row[8] == "above rim");
    }

    [Fact]
    public void Boundary_LeavesOutAPocketOutsideThePond()
    {
        // Add a separate 10 × 10 pocket at elevation 95 far to the east; the boundary keeps it out.
        string input = Tool.SampleInput(Ctx())
            .Replace("\n\n[TIN faces]\nA", "\n91\t6000\t8000\t95\n92\t6010\t8000\t95\n93\t6010\t8010\t95\n\n[TIN faces]\nA")
            .Replace("4\t5\t1", "4\t5\t1\n91\t92\t93");
        var all = Tool.Run(input, Ctx());
        Assert.True(all.Succeeded, string.Join("\n", all.Messages.Select(m => m.Text)));
        Assert.Equal(95.0, (double)all.Tables[0].Rows[0][0]!);
        var bounded = Tool.Run(input.Replace("# [Boundary]\n# X\tY", "[Boundary]\nX\tY\n4900\t7900\n5200\t7900\n5200\t8200\n4900\t8200"), Ctx());
        Assert.True(bounded.Succeeded, string.Join("\n", bounded.Messages.Select(m => m.Text)));
        Assert.Equal(100.0, (double)bounded.Tables[0].Rows[0][0]!);
        Assert.Equal(56208.0, (double)bounded.Tables[0].Rows[^1][4]!, 0);
    }

    [Fact]
    public void LandXml_SurfaceByName_SkipsInvisibleFaces_AndChecksUnits()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\"?><LandXML xmlns=\"http://www.landxml.org/schema/LandXML-1.2\" version=\"1.2\"><Units><Imperial linearUnit=\"foot\" areaUnit=\"squareFoot\" volumeUnit=\"cubicFeet\"/></Units><Surfaces>");
        sb.Append("<Surface name=\"EG\"><Definition surfType=\"TIN\"><Pnts><P id=\"1\">0 0 1</P><P id=\"2\">0 10 1</P><P id=\"3\">10 0 1</P></Pnts><Faces><F>1 2 3</F></Faces></Definition></Surface>");
        sb.Append("<Surface name=\"Pond FG\"><Definition surfType=\"TIN\"><Pnts>");
        for (int i = 0; i < FrustumPts.Length; i++) sb.Append($"<P id=\"{i + 101}\">{FrustumPts[i].Item2} {FrustumPts[i].Item1} {FrustumPts[i].Item3}</P>");   // N E Z
        sb.Append("</Pnts><Faces>");
        foreach (var (a, b, c) in FrustumFaces) sb.Append($"<F>{a + 101} {b + 101} {c + 101}</F>");
        sb.Append("<F i=\"1\">101 102 108</F></Faces></Definition></Surface></Surfaces></LandXML>");
        string path = Path.Combine(Path.GetTempPath(), $"mf-pond-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, sb.ToString());
        try
        {
            var r = Tool.Run($"surface file = {path}\nsurface = pond fg\ninterval = 1\n", Ctx());
            Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
            Assert.Contains(r.Messages, m => m.Code == "W-PS-001" && m.Text.Contains("invisible"));
            Assert.DoesNotContain(r.Messages, m => m.Code == "W-PS-002");
            Assert.Equal(7, r.Tables[0].Rows.Count);
            Assert.Equal(56208.0, (double)r.Tables[0].Rows[^1][4]!, 0);
            Assert.Contains("\"Pond FG\"", r.Summary[0]);

            var si = Tool.Run($"surface file = {path}\nsurface = Pond FG\n", Ctx(UnitSystem.SI));
            Assert.Contains(si.Messages, m => m.Code == "W-PS-002");
            var missing = Tool.Run($"surface file = {path}\nsurface = XX\n", Ctx());
            Assert.Contains(missing.Messages, m => m.Code == "E-PS-003" && m.Text.Contains("Pond FG"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BadInput_GivesClearErrors()
    {
        Assert.Contains(Tool.Run("interval = 0.5", Ctx()).Messages, m => m.Code == "E-PS-007");
        Assert.Contains(Tool.Run("surface file = /no/such/file.xml", Ctx()).Messages, m => m.Code == "E-PS-001");
        Assert.Contains(Tool.Run("[TIN points]\nId\tX\tY\tZ\n1\t0\t0\t0\n[TIN faces]\nA\tB\tC\n1\t2\t3", Ctx()).Messages, m => m.Code == "E-PS-005");
        Assert.Contains(LandXmlSurfaces.Read(new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e \"y\">]><x/>"))).Problems, p => p.Contains("Not a readable"));
    }
}
