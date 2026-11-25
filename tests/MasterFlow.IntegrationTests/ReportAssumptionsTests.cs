using System.Text;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>The assumptions note and method lead on single-element reports match the element type.</summary>
public class ReportAssumptionsTests
{
    private const string ChannelWords = "prismatic channel";

    public static TheoryData<string> Pipes => new() { "circular", "elliptical", "elliptical-c507" };

    private static ChannelSection PipeSection(string kind) => kind switch
    {
        "circular" => new CircularSection(2.0),
        "elliptical" => new EllipticalSection(1.5, 2.5),
        _ => EllipticalPipeCatalog.Create(EllipticalPipeCatalog.Sizes[3], false, UnitSystem.USCustomary),
    };

    private static UniformFlowResult Solve(ChannelSection s, double q = 10) =>
        UniformFlowCalculator.Solve(new UniformFlowInput { Section = s, SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.005, Discharge = q });

    private static string PdfText(byte[] pdf)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mf-pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "r.pdf"), pdf);
            var psi = new System.Diagnostics.ProcessStartInfo("pdftotext", [Path.Combine(dir, "r.pdf"), "-"]) { RedirectStandardOutput = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            string text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return string.Join(' ', text.Split((char[])[' ', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static bool HasPdfToText()
    {
        try { using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pdftotext", "-v") { RedirectStandardError = true, RedirectStandardOutput = true }); p!.WaitForExit(); return true; }
        catch { return false; }
    }

    [Theory]
    [MemberData(nameof(Pipes))]
    public void PipeReports_UsePipeNote_NotOpenChannelNote(string kind)
    {
        var r = Solve(PipeSection(kind));
        Assert.True(r.Succeeded, string.Join("; ", r.Messages.Select(m => m.Text)));
        string note = ReportAssumptions.Note(r);
        Assert.DoesNotContain(ChannelWords, note);
        Assert.DoesNotContain("hydrostatic", note);
        Assert.Contains("gravity flow", note);
        Assert.Contains("partly full", note);
        Assert.Contains("Surcharged (pressure) flow", note);
        Assert.Contains("Manning's n is the engineer's selection", note);
        Assert.DoesNotContain("open-channel", ReportAssumptions.MethodLead(r.Input.Section));

        var calc = new ChannelCalculation { Title = "Pipe", Input = r.Input };
        string html = HtmlReport.Render(new ProjectInfo { Name = "P" }, calc, r);
        Assert.DoesNotContain(ChannelWords, html);
        Assert.DoesNotContain("open-channel flow", html);
        Assert.Contains("closed conduit flowing partly full", html);

        if (!HasPdfToText()) return;
        string pdf = PdfText(PdfReport.ToBytes(new ProjectInfo { Name = "P" }, [new PdfReportItem(calc, r, new ReportOptions())]));
        Assert.DoesNotContain(ChannelWords, pdf);
        Assert.DoesNotContain("open-channel flow", pdf);
        Assert.Contains("Surcharged (pressure) flow", pdf);
    }

    [Fact]
    public void PipeFlowingFull_NoteSaysGravityFullFlowNotSurcharge()
    {
        var input = new UniformFlowInput { Section = new CircularSection(2.0), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.005, Depth = 2.0 };
        var r = UniformFlowCalculator.Solve(input);
        Assert.True(r.Succeeded);
        Assert.Contains("just full at the crown", ReportAssumptions.Note(r));
        Assert.DoesNotContain("just full at the crown", ReportAssumptions.Note(Solve(new CircularSection(2.0))));
    }

    [Fact]
    public void OpenChannelReports_KeepChannelNote()
    {
        foreach (var s in new ChannelSection[] { new RectangularSection(3), new TrapezoidalSection(4, 3, 3), new TriangularSection(3, 3) })
        {
            var r = Solve(s);
            Assert.Contains("prismatic channel, uniform roughness across the wetted perimeter", ReportAssumptions.Note(r));
            Assert.StartsWith("Steady, uniform open-channel flow", ReportAssumptions.MethodLead(s));
            string html = HtmlReport.Render(new ProjectInfo { Name = "P" }, new ChannelCalculation { Title = "C", Input = r.Input }, r);
            Assert.Contains(ChannelWords, html);
        }
        var irr = new IrregularSection([new(0, 5, 0.06), new(10, 0, 0.035), new(20, 0, 0.035), new(30, 5, 0.06)], CompositeRoughnessMethod.Conveyance);
        var ri = Solve(irr, 50);
        Assert.Contains("Roughness varies across the section", ReportAssumptions.Note(ri));
    }
}
