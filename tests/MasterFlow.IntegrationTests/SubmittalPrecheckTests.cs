using System.IO.Compression;
using System.Text;
using MasterFlow.Application.Documents;
using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>Plan-review pre-checker: document text extraction (PDF, Word, text), the report checklist, plan sheets and model findings.</summary>
public class SubmittalPrecheckTests
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);

    private static ToolResult Run(string input, DesignToolContext? ctx = null)
    {
        var r = new SubmittalPrecheckTool().Run(input, ctx ?? Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> Comments(ToolResult r) => r.Tables.Single(t => t.Title == "Anticipated review comments").Rows;
    private static bool Has(ToolResult r, string topic) => Comments(r).Any(c => ((string)c[2]!).StartsWith(topic));

    [Fact]
    public void Pdf_TextFromAMasterFlowReport()
    {
        // A real PDF written by MasterFlow (compressed streams, embedded fonts with ToUnicode maps).
        var tool = new RationalMethodTool();
        var res = tool.Run(tool.SampleInput(Us), Us);
        var pdf = ToolReport.ToPdf(new ProjectInfo { Name = "Founders Park drainage" }, res, "x");
        var path = Path.Combine(Path.GetTempPath(), $"mf-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            var d = DocumentText.Read(path);
            Assert.True(d.Pages.Count >= 1);
            Assert.Contains("Founders Park drainage", d.All);
            Assert.Contains("Rational method", d.All, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Pdf_LiteralStringsAndEscapes_AndHandWrittenFile()
    {
        string content = "BT /F1 12 Tf 72 720 Td (Storm Water Report \\(draft\\)) Tj 0 -14 Td [(Free)-300(board)] TJ ET";
        var sb = new StringBuilder("%PDF-1.4\n");
        sb.Append("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n");
        sb.Append("3 0 obj<</Type/Page/Parent 2 0 R/Contents 4 0 R>>endobj\n");
        sb.Append($"4 0 obj<</Length {content.Length}>>\nstream\n{content}\nendstream\nendobj\ntrailer<</Root 1 0 R>>\n%%EOF");
        var pages = DocumentText.Pdf(Encoding.Latin1.GetBytes(sb.ToString()));
        Assert.Single(pages);
        Assert.Contains("Storm Water Report (draft)", pages[0]);
        Assert.Contains("Free board", pages[0]);   // a −300 TJ gap is a word space
    }

    [Fact]
    public void Docx_And_Text()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            w.Write("<w:document><w:body><w:p><w:r><w:t>Drainage Basin Design Plan &amp; easements</w:t></w:r></w:p><w:p><w:r><w:t>Second</w:t></w:r></w:p></w:body></w:document>");
        var text = DocumentText.Docx(ms.ToArray());
        Assert.Contains("Drainage Basin Design Plan & easements\nSecond", text);
        var path = Path.Combine(Path.GetTempPath(), $"mf-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "plain text");
        try { Assert.Equal("plain text", DocumentText.Read(path).All); } finally { File.Delete(path); }
    }

    [Fact]
    public void Sample_ListsMissingContent_WithReferences()
    {
        var tool = new SubmittalPrecheckTool();
        var r = Run(tool.SampleInput(Us));
        // The sample text covers certification, basin plan, methods, storms, street, inlets, HGL and losses …
        Assert.False(Has(r, "Engineer's certification"));
        Assert.False(Has(r, "Drainage Basin Design Plan"));
        Assert.False(Has(r, "Storm sewer HGL"));
        Assert.False(Has(r, "Design storms"));
        // … but not easements, maintenance, water quality, freeboard, 72-hour drain or the spillway.
        foreach (var t in new[] { "Access and drainage easements", "Operation and maintenance", "Storm water quality", "Pond freeboard", "Pond drain time", "Emergency spillway" })
            Assert.True(Has(r, t), t);
        var c = Comments(r).First(x => (string)x[2]! == "Pond drain time");
        Assert.Equal("IDCM §4.10.5", c[4]);
        Assert.Equal("MISSING", c[1]);
        Assert.Contains("72 hours", (string)c[3]!);
        // Culverts were not part of this project, so culvert items are not asked for.
        Assert.False(Has(r, "Culvert"));
    }

    [Fact]
    public void ScopeOverrides_AndRationalAreaLimit()
    {
        var r = Run("detention = no\nculverts = yes\nmodel checks = no\n[Report text]\nThe Rational Method was applied to the 212 acre basin. Pond volume 0.741 ac-ft.\n");
        Assert.True(Has(r, "Rational method area limit"));
        Assert.Contains("212", (string)Comments(r).First(x => (string)x[2]! == "Rational method area limit")[3]!);
        Assert.True(Has(r, "Culvert method"));
        Assert.False(Has(r, "Pond freeboard"));
        var small = Run("model checks = no\n[Report text]\nThe Rational Method was applied. Pond volume 0.741 ac-ft; 45 ac basin.\n");
        Assert.False(Has(small, "Rational method area limit"));
    }

    [Fact]
    public void Plans_MissingSheets()
    {
        var r = Run("model checks = no\nstorm sewer = yes\ndetention = no\n[Plans text]\nSheet C1 Grading Plan\nSheet C2 Drainage Area Map\nSheet C5 Details\n");
        var sheets = r.Tables.Single(t => t.Title == "Plan sheets").Rows;
        Assert.Equal("found", sheets.Single(s => (string)s[1]! == "Grading plan")[0]);
        Assert.Equal("MISSING", sheets.Single(s => (string)s[1]! == "Storm sewer plan and profile")[0]);
        Assert.True(Has(r, "Erosion and sediment control plan"));
    }

    [Fact]
    public void ModelChecks_FailingNetwork_AndNamesMissingFromTheReport()
    {
        var n = NetworkOutputTests.NetA();   // HGL above the rims at CB-3 and CB-4
        var ctx = new DesignToolContext(UnitSystem.USCustomary, n, StormNetworkSolver.Solve(n)) { Networks = [(n, null)] };
        var r = Run("[Report text]\nPipes P1 and P2 at MH-2 and OUT.\n", ctx);
        var hgl = Comments(r).Where(c => (string)c[1]! == "REVISE" && ((string)c[4]!).Contains("4.6.7")).ToList();
        Assert.Contains(hgl, c => ((string)c[2]!).Contains("CB-3"));
        Assert.Contains(r.Messages, m => m.Code == "W-PC-001");
        // P3, CB-3 and CB-4 are not named in the report.
        var names = r.Tables.Single(t => t.Title.StartsWith("Model elements")).Rows.Select(x => (string)x[1]!).ToList();
        Assert.Equal(["P3", "CB-3", "CB-4"], names);
        // REVISE comments come first.
        Assert.Equal("REVISE", Comments(r)[0][1]);
    }

    [Fact]
    public void Errors_AndPremium()
    {
        var tool = new SubmittalPrecheckTool();
        Assert.Contains(tool.Run("report = /no/such/file.pdf", Us).Messages, m => m.Code == "E-PC-001");
        Assert.Contains(tool.Run("model checks = no", Us).Messages, m => m.Code == "E-PC-002");
        Assert.True(PremiumFeatures.IsPremium(tool));
    }
}
