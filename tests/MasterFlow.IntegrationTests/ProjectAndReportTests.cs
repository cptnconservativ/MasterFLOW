using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Persistence;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

public class ProjectAndReportTests
{
    private static Project Sample() => new()
    {
        Info = new ProjectInfo { Name = "Route 9 ditches", ProjectNumber = "24-117", Client = "County <DOT>", PreparedBy = "KH", CheckedBy = "JD", Notes = "Check n with field data." },
        Calculations =
        [
            new ChannelCalculation { Title = "Ditch A", Input = new UniformFlowInput { Section = new TrapezoidalSection(4, 3, 3) { ChannelDepth = 2.5 }, SolveFor = SolveFor.NormalDepth, ManningN = 0.03, Slope = 0.005, Discharge = 60 } },
            new ChannelCalculation { Title = "Flume", Input = new UniformFlowInput { Section = new RectangularSection(3), Units = UnitSystem.SI, SolveFor = SolveFor.Discharge, ManningN = 0.015, Slope = 0.001, Depth = 1 } },
            new ChannelCalculation { Title = "Gutter", Input = new UniformFlowInput { Section = new TriangularSection(50, 0.5), SolveFor = SolveFor.Roughness, Slope = 0.02, Discharge = 2, Depth = 0.15 } },
        ],
    };

    [Fact]
    public void SaveLoad_RoundTrip_PreservesEverything_AndResults()
    {
        var p = Sample();
        string path = Path.Combine(Path.GetTempPath(), $"mf-{Guid.NewGuid():N}{ProjectFile.Extension}");
        try
        {
            ProjectFile.Save(p, path);
            var back = ProjectFile.Load(path);
            Assert.Equal(p.Info, back.Info);
            Assert.Equal(p.Calculations.Count, back.Calculations.Count);
            for (int i = 0; i < p.Calculations.Count; i++)
            {
                Assert.Equal(p.Calculations[i].Input, back.Calculations[i].Input);
                Assert.Equal(p.Calculations[i].Id, back.Calculations[i].Id);
                var a = UniformFlowCalculator.Solve(p.Calculations[i].Input);
                var b = UniformFlowCalculator.Solve(back.Calculations[i].Input);
                Assert.Equal(a.Discharge, b.Discharge);
                Assert.Equal(a.Normal.Depth, b.Normal.Depth);
            }
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void File_IsVersioned_AndReadable()
    {
        var json = ProjectFile.Serialize(Sample());
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"format\": \"MasterFlow.Project\"", json);
        Assert.Contains("\"shape\": \"trapezoidal\"", json);
        Assert.Contains("\"solveFor\": \"normalDepth\"", json);
    }

    [Fact]
    public void NewerSchema_IsRefused()
    {
        var json = ProjectFile.Serialize(Sample()).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
        var ex = Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));
        Assert.Contains("newer", ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"format\":\"Other\",\"schemaVersion\":1}")]
    [InlineData("{\"format\":\"MasterFlow.Project\"}")]
    [InlineData("{\"format\":\"MasterFlow.Project\",\"schemaVersion\":1,\"project\":{\"calculations\":[{\"input\":{\"section\":{\"shape\":\"hexagon\"}}}]}}")]
    public void BadFiles_GiveClearErrors(string json) => Assert.Throws<ProjectFileException>(() => ProjectFile.Deserialize(json));

    [Fact]
    public void Report_ContainsInputsResultsWarningsAndEscapesText()
    {
        var p = Sample();
        var calc = p.Calculations[0];
        var r = UniformFlowCalculator.Solve(calc.Input);
        var rt = RatingTableBuilder.Build(new RatingTableRequest(calc.Input, RatingTableMode.DischargeFromDepth, 0.5, 2.5, 0.5));
        var html = HtmlReport.Render(p.Info, calc, r, new ReportOptions { Watermark = "PRELIMINARY", FooterText = "Firm footer", RatingTable = rt, Timestamp = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero) });
        Assert.Contains("County &lt;DOT&gt;", html);
        Assert.DoesNotContain("County <DOT>", html);
        Assert.Contains("PRELIMINARY", html);
        Assert.Contains("Normal depth", html);
        Assert.Contains(r.Normal.Depth.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), html);
        Assert.Contains("<svg", html);
        Assert.Contains("Rating table", html);
        Assert.Contains("responsible engineer", html);
        Assert.Contains("2026-11-17", html);
    }

    [Fact]
    public void Report_ForFailedCalc_ShowsNoNumbers()
    {
        var p = Sample();
        var calc = p.Calculations[0] with { Input = p.Calculations[0].Input with { Slope = 0 } };
        var r = UniformFlowCalculator.Solve(calc.Input);
        var html = HtmlReport.Render(p.Info, calc, r);
        Assert.Contains("No results", html);
        Assert.Contains(MessageCodes.SlopeNotPositive, html);
        Assert.DoesNotContain("Critical flow (same discharge)", html);
    }
}
