using System.IO.Compression;
using System.Xml.Linq;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

public class WorkbookAndComparisonTests
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static (ChannelCalculation, UniformFlowResult) Calc(string title, UniformFlowInput i) => (new ChannelCalculation { Title = title, Input = i }, UniformFlowCalculator.Solve(i));

    private static List<(ChannelCalculation Calc, UniformFlowResult Result)> Alternatives() =>
    [
        Calc("Grass", new UniformFlowInput { Section = new TrapezoidalSection(4, 3, 3), SolveFor = SolveFor.NormalDepth, ManningN = 0.035, Slope = 0.005, Discharge = 60 }),
        Calc("Concrete", new UniformFlowInput { Section = new TrapezoidalSection(4, 3, 3), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.005, Discharge = 60 }),
        Calc("Pipe", new UniformFlowInput { Section = new CircularSection(3), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = 60 }),
        Calc("Bad", new UniformFlowInput { Section = new RectangularSection(-1), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.005, Depth = 1 }),
    ];

    [Fact]
    public void Comparison_AlignsQuantities_AcrossAlternatives()
    {
        var t = Comparison.Build(Alternatives().Select(a => (a.Calc.Title, a.Result)).ToList());
        Assert.Equal(["Grass", "Concrete", "Pipe", "Bad"], t.Titles);
        var yn = t.Rows.Single(r => r.Label == "Normal depth");
        Assert.True(yn.Values[0] > yn.Values[1], "rougher channel runs deeper");
        Assert.Null(yn.Values[3]);
        Assert.Equal("—", yn.Format(3));
        var full = t.Rows.Single(r => r.Label == "Full-flow Q");
        Assert.Null(full.Values[0]);
        Assert.NotNull(full.Values[2]);
        Assert.Contains(t.Notes, n => n.Contains("without a result"));
        var html = HtmlReport.RenderComparison(new ProjectInfo { Name = "Alt <A>" }, t);
        Assert.Contains("Alt &lt;A&gt;", html);
        Assert.Contains("Concrete", html);
    }

    [Fact]
    public void Workbook_IsValidOpenXml_WithFullPrecisionNumbers()
    {
        var items = Alternatives();
        var rt = RatingTableBuilder.Build(new RatingTableRequest(items[0].Calc.Input, RatingTableMode.DischargeFromDepth, 0.5, 2, 0.5));
        var bytes = XlsxWriter.ToBytes(ProjectWorkbook.Build(new ProjectInfo { Name = "P" }, items, rt, "Grass rating"));
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("xl/workbook.xml", names);
        var wb = XDocument.Load(zip.GetEntry("xl/workbook.xml")!.Open());
        var sheetNames = wb.Descendants(S + "sheet").Select(e => (string)e.Attribute("name")!).ToList();
        Assert.Equal(["Comparison", "Grass", "Concrete", "Pipe", "Bad", "Grass rating"], sheetNames);

        // Every part is well-formed XML; the Grass sheet carries yn at full precision.
        foreach (var e in zip.Entries) XDocument.Load(e.Open());
        var grass = XDocument.Load(zip.GetEntry("xl/worksheets/sheet2.xml")!.Open());
        var cells = grass.Descendants(S + "c").ToList();
        double yn = items[0].Result.Normal.Depth;
        Assert.Contains(cells, c => (string?)c.Element(S + "v") is { } v && double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) == yn);
        // No NaN or Infinity text anywhere.
        foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/")))
        {
            var text = new StreamReader(e.Open()).ReadToEnd();
            Assert.DoesNotContain("NaN", text);
            Assert.DoesNotContain("Infinity", text);
        }
    }

    [Fact]
    public void SheetNames_AreSanitisedAndUnique()
    {
        var rows = new List<IReadOnlyList<object?>> { new object?[] { "x" } };
        var bytes = XlsxWriter.ToBytes([new("a/b:c*?[d]", rows), new("A/B:C*?[D]", rows), new(new string('z', 40), rows), new("", rows)]);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var names = XDocument.Load(zip.GetEntry("xl/workbook.xml")!.Open()).Descendants(S + "sheet").Select(e => (string)e.Attribute("name")!).ToList();
        Assert.Equal(4, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n => Assert.True(n.Length is > 0 and <= 31 && n.IndexOfAny("[]:*?/\\".ToCharArray()) < 0, n));
        Assert.Equal("AA", XlsxWriter.Column(26));
        Assert.Equal("AZ", XlsxWriter.Column(51));
    }
}
