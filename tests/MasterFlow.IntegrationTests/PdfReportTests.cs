using System.Text;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;
using SkiaSharp;

namespace MasterFlow.IntegrationTests;

public class PdfReportTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly ProjectInfo Info = new() { Name = "Route 9 ditches", ProjectNumber = "24-117", Client = "County <DOT>", PreparedBy = "KH", CheckedBy = "JD", Notes = "Check n with field data." };

    private static PdfReportItem Item(string title, UniformFlowInput input, ReportOptions? o = null) =>
        new(new ChannelCalculation { Title = title, Input = input }, UniformFlowCalculator.Solve(input), o ?? new ReportOptions { Timestamp = Stamp });

    private static readonly UniformFlowInput Ditch = new() { Section = new TrapezoidalSection(20, 2, 2) { ChannelDepth = 5 }, SolveFor = SolveFor.NormalDepth, ManningN = 0.025, Slope = 0.0016, Discharge = 400 };

    /// <summary>Page objects are written uncompressed by Skia: "/Type /Page" without the trailing "s".</summary>
    private static int Pages(byte[] pdf)
    {
        var s = Encoding.Latin1.GetString(pdf);
        int n = 0;
        for (int i = s.IndexOf("/Type /Page", StringComparison.Ordinal); i >= 0; i = s.IndexOf("/Type /Page", i + 1, StringComparison.Ordinal))
            if (i + 11 >= s.Length || s[i + 11] != 's') n++;
        return n;
    }

    private static void AssertPdf(byte[] pdf)
    {
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Contains("%%EOF", Encoding.Latin1.GetString(pdf[^32..]));
    }

    [Fact]
    public void SingleReport_IsValidPdf_WithMetadata()
    {
        var pdf = PdfReport.ToBytes(Info, [Item("Ditch A", Ditch)]);
        AssertPdf(pdf);
        Assert.True(Pages(pdf) >= 1);
        Assert.Contains("MasterFlow", Encoding.Latin1.GetString(pdf)); // producer/creator
    }

    [Fact]
    public void BatchReport_StartsEachCalculationOnANewPage()
    {
        var items = new[]
        {
            Item("Ditch", Ditch),
            Item("Flume SI", new UniformFlowInput { Section = new RectangularSection(3), Units = UnitSystem.SI, SolveFor = SolveFor.Discharge, ManningN = 0.015, Slope = 0.001, Depth = 1 }),
            Item("Pipe", new UniformFlowInput { Section = new CircularSection(3), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = 40 }),
            Item("Gutter n", new UniformFlowInput { Section = new TriangularSection(50, 0.5), SolveFor = SolveFor.Roughness, Slope = 0.02, Discharge = 2, Depth = 0.15 }),
        };
        var pdf = PdfReport.ToBytes(Info, items);
        AssertPdf(pdf);
        Assert.True(Pages(pdf) >= items.Length);
    }

    [Fact]
    public void LongContent_FlowsOntoMorePages()
    {
        // 400-point section and a 60-row rating table cannot fit on one page.
        var pts = Enumerable.Range(0, 400).Select(i => new StationPoint(i, 5 - 5 * Math.Sin(Math.PI * i / 399.0), i < 399 ? 0.035 : null)).ToList();
        var input = new UniformFlowInput { Section = new IrregularSection(pts), SolveFor = SolveFor.NormalDepth, ManningN = 0.035, Slope = 0.001, Discharge = 500 };
        var rt = RatingTableBuilder.Build(new RatingTableRequest(Ditch, RatingTableMode.DischargeFromDepth, 0.1, 6, 0.1));
        var pdf = PdfReport.ToBytes(Info, [Item("Long", input, new ReportOptions { Timestamp = Stamp, RatingTable = rt })]);
        AssertPdf(pdf);
        Assert.True(Pages(pdf) >= 5, $"pages = {Pages(pdf)}");
    }

    [Fact]
    public void FailedCalculation_StillReportsMessages()
    {
        var bad = Item("Impossible", new UniformFlowInput { Section = new CircularSection(1), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.001, Discharge = 500 });
        Assert.False(bad.Result.Succeeded);
        AssertPdf(PdfReport.ToBytes(Info, [bad]));
    }

    [Fact]
    public void PngLogo_IsEmbedded_SvgLogo_IsNot()
    {
        using var bmp = new SKBitmap(40, 20);
        bmp.Erase(SKColors.SteelBlue);
        using var data = SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Png, 100);
        string png = "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
        Assert.True(PdfReport.LogoSupported(png));
        Assert.False(PdfReport.LogoSupported("data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"))));
        Assert.False(PdfReport.LogoSupported("data:image/png;base64,not-base64!"));
        var pdf = PdfReport.ToBytes(Info, [Item("Logo", Ditch, new ReportOptions { Timestamp = Stamp, LogoDataUri = png, Watermark = "PRELIMINARY", FooterText = "Firm footer" })]);
        AssertPdf(pdf);
        Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(pdf));
    }

    [Fact]
    public void ComparisonReport_HandlesManyAlternatives()
    {
        var results = Enumerable.Range(1, 9).Select(k => ($"Alt {k}", UniformFlowCalculator.Solve(Ditch with { ManningN = 0.02 + 0.002 * k }))).ToList();
        var pdf = PdfReport.ComparisonToBytes(Info, Comparison.Build(results), new ReportOptions { Timestamp = Stamp });
        AssertPdf(pdf);
        Assert.True(Pages(pdf) >= 2); // 9 alternatives = two bands of at most 6
    }

    [Fact]
    public void Output_IsDeterministic_ForAFixedTimestamp()
    {
        var a = PdfReport.ToBytes(Info, [Item("Ditch", Ditch)]);
        var b = PdfReport.ToBytes(Info, [Item("Ditch", Ditch)]);
        Assert.Equal(a, b);
    }
}
