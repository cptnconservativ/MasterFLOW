using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MasterFlow.Application.Reports;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>Drainage report builder (premium): template parsing, re-running saved tool inputs, compliance, and the PDF / HTML / Word output.</summary>
public class DrainageReportTests
{
    private static readonly DrainageReportTool Tool = new();
    private static readonly ProjectInfo Info = new() { Name = "Founders Park Phase 2", ProjectNumber = "26-014", Client = "City of Rapid City", PreparedBy = "K. Hibbs", CheckedBy = "J. Doe" };

    /// <summary>A project with every tool's sample saved, one solved network and two channel calculations.</summary>
    private static DesignToolContext Project(Func<IDesignTool, bool>? mayRun = null)
    {
        var us = new DesignToolContext(UnitSystem.USCustomary);
        var net = NetworkOutputTests.NetA();
        var inputs = DesignTools.All.Where(t => t is not DrainageReportTool).ToDictionary(t => t.Id, t => t.SampleInput(us));
        var ditch = new UniformFlowInput { Section = new TrapezoidalSection(4, 3, 3) { ChannelDepth = 3 }, SolveFor = SolveFor.NormalDepth, ManningN = 0.035, Slope = 0.01, Discharge = 60 };
        return new DesignToolContext(UnitSystem.USCustomary, net, StormNetworkSolver.Solve(net))
        {
            Project = Info, ToolInputs = inputs, Networks = [(net, StormNetworkSolver.Solve(net))],
            Calculations = [new ChannelCalculation { Title = "Ditch A", Input = ditch }, new ChannelCalculation { Title = "Ditch B", Input = ditch with { Discharge = 90 } }],
            MayRun = mayRun,
        };
    }

    [Fact]
    public void DefaultTemplate_BuildsWholeReport()
    {
        var ctx = Project();
        var r = Tool.Run(Tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        var d = r.Report!;
        Assert.Equal("Storm Water Report", d.Title);
        Assert.Contains(("Project number", "26-014"), d.Cover);
        // Placeholders filled; project name in the introduction.
        Assert.Contains(d.Body.OfType<ReportParagraph>(), p => p.Text.Contains("Founders Park Phase 2 for City of Rapid City"));
        Assert.DoesNotContain(d.Body.OfType<ReportParagraph>(), p => p.Text.Contains('{'));
        // Every tool with saved input shows up in the appendix, plus the network and calculations.
        foreach (var t in DesignTools.All.Where(t => t is not DrainageReportTool && Tool.SampleInput(ctx).Contains("@tool " + t.Id + " full")))
            Assert.Contains(d.Appendix.OfType<ReportHeading>(), h => h.Text == t.Name);
        Assert.Single(d.Appendix.OfType<ReportNetwork>());
        Assert.Contains(d.Body.OfType<ReportCertification>(), c => c.Statement.Contains("South Dakota"));
        // Compliance table collects tool checks and the network's HGL checks.
        var comp = d.Body.OfType<ReportTable>().Single(t => t.Table.Title == "Compliance summary").Table;
        Assert.Contains(comp.Rows, row => ((string)row[0]!).StartsWith("Storm network Main St"));
        Assert.Contains(comp.Rows, row => (string)row[0]! == new DetentionTool().Name);
        Assert.All(comp.Rows, row => Assert.Matches("^(OK|FAILS.*|NOT CHECKED|OVER CAPACITY)$", (string)row[3]!));
        // Numbered headings, body first.
        var nums = DrainageReportWriter.Number(d);
        Assert.Equal("1", nums[0].Number);
        Assert.Contains(nums, n => n.Number == "2.1");
        Assert.All(nums.Where(n => n.Appendix), n => Assert.Equal("", n.Number));
    }

    [Fact]
    public void Pdf_Html_Docx_AreWellFormed()
    {
        var ctx = Project();
        var r = Tool.Run(Tool.SampleInput(ctx), ctx);
        var o = new ReportOptions { Timestamp = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero) };

        var pdf = ToolReport.ToPdf(Info, r, "", o);   // ToolReport hands a report result to the writer
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        var s = Encoding.Latin1.GetString(pdf);
        int portrait = Count(s, "/MediaBox [0 0 612 792]"), landscape = Count(s, "/MediaBox [0 0 792 612]");
        Assert.True(portrait >= 4, $"portrait pages {portrait}");
        Assert.True(landscape >= 4, $"landscape pages {landscape}");

        var html = ToolReport.ToHtml(Info, r, "", "test");
        Assert.Contains("<h1 id=\"h1\">1  Engineer&#39;s certification</h1>", html);
        Assert.Contains("Compliance summary", html);
        Assert.Contains("<svg", html);
        Assert.Contains("class=\"appendix\"", html);

        var docx = DrainageReportWriter.ToDocx(Info, r.Report!, o);
        using var zip = new ZipArchive(new MemoryStream(docx));
        var doc = XDocument.Load(zip.GetEntry("word/document.xml")!.Open());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        Assert.Contains(doc.Descendants(w + "pStyle"), e => (string?)e.Attribute(w + "val") == "Heading1");
        Assert.Equal(2, doc.Descendants(w + "sectPr").Count());                                   // portrait body + landscape appendix
        Assert.Contains(doc.Descendants(w + "pgSz"), e => (string?)e.Attribute(w + "orient") == "landscape");
        Assert.Contains(doc.Descendants(w + "instrText"), e => e.Value.Contains("TOC"));
        int images = zip.Entries.Count(e => e.FullName.StartsWith("word/media/"));
        Assert.True(images >= 3, $"images {images}");
        var rels = XDocument.Load(zip.GetEntry("word/_rels/document.xml.rels")!.Open()).ToString();
        for (int i = 1; i <= images; i++) Assert.Contains($"rImg{i}", rels);
        foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels"))) XDocument.Load(e.Open());   // every part parses
    }

    private static int Count(string s, string what) { int n = 0; for (int i = s.IndexOf(what, StringComparison.Ordinal); i >= 0; i = s.IndexOf(what, i + 1, StringComparison.Ordinal)) n++; return n; }

    [Fact]
    public void EmptySections_AreLeftOut_UnlessAskedToKeep()
    {
        var ctx = new DesignToolContext(UnitSystem.USCustomary) { Project = Info };
        const string tpl = "title = T\n[Report]\n# Hydrology\n@tool rational-rapid-city summary\n# Notes\nSome text.\n";
        var r = Tool.Run(tpl, ctx);
        Assert.DoesNotContain(r.Report!.Body.OfType<ReportHeading>(), h => h.Text == "Hydrology");
        Assert.Contains(r.Report.Body.OfType<ReportHeading>(), h => h.Text == "Notes");
        var keep = Tool.Run(tpl.Replace("title = T", "title = T\nomit empty sections = no"), ctx);
        Assert.Contains(keep.Report!.Body.OfType<ReportHeading>(), h => h.Text == "Hydrology");
    }

    [Fact]
    public void Template_Errors_And_Warnings()
    {
        var ctx = Project();
        Assert.False(Tool.Run("title = x\n# no report line", ctx).Succeeded);
        Assert.False(Tool.Run("[Report]\n@frobnicate", ctx).Succeeded);
        var r = Tool.Run("[Report]\n# A\nHello {project.nonsense}\n@tool not-a-tool summary\n@tool detention-routing table \"No such table\"\n@network Elm summary", ctx);
        Assert.True(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == "W-RB-005");
        Assert.Contains(r.Messages, m => m.Code == "I-RB-002");
        Assert.Contains(r.Messages, m => m.Code == "W-RB-006");
        Assert.Contains(r.Messages, m => m.Code == "W-RB-004");
        // Lines with commas and '=' are narrative, not tool tables or parameters.
        var p = Tool.Run("[Report]\n# A\nQ = C i A, where C is the runoff coefficient.", ctx);
        Assert.Equal("Q = C i A, where C is the runoff coefficient.", p.Report!.Body.OfType<ReportParagraph>().Single().Text);
    }

    [Fact]
    public void ToolsTheLicenseDoesNotCover_AreLeftOut()
    {
        var ctx = Project(t => t.Id != "detention-routing");
        var r = Tool.Run("[Report]\n# Detention\n@tool detention-routing full\n# Other\n@tool hds5-culvert summary", ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-RB-002");
        Assert.DoesNotContain(r.Report!.Body.OfType<ReportHeading>(), h => h.Text == "Detention");
        Assert.Contains(r.Report.Body.OfType<ReportHeading>(), h => h.Text == new CulvertTool().Name);
    }

    [Fact]
    public void FailingSavedInput_ShowsMessages_NotNumbers()
    {
        var ctx = Project() with { };
        var inputs = new Dictionary<string, string>(ctx.ToolInputs) { ["detention-routing"] = "inflow = rational\narea = -3" };
        var r = Tool.Run("[Report]\n# D\n@tool detention-routing full", ctx with { ToolInputs = inputs });
        Assert.Contains(r.Messages, m => m.Code == "W-RB-003");
        Assert.Single(r.Report!.Body.OfType<ReportMessages>());
        Assert.Empty(r.Report.Body.OfType<ReportTable>());
    }
}
