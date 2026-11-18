using System.Diagnostics;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Roadmap G4 budgets, measured after one warm-up run (JIT). Limits are the release targets; CI
/// machines are slower than desktops, so each test reports its time in the failure message.
/// </summary>
public class PerformanceTests
{
#if DEBUG
    // Unoptimised build (the default for `dotnet test`): budgets × 20 still catch quadratic blow-ups.
    // The release budgets are checked with `dotnet test -c Release` (docs/VALIDATION_RESULTS.md).
    private const double Slack = 20;
#else
    private const double Slack = 1;
#endif

    private static double Time(Action a)
    {
        a(); // warm-up
        var sw = Stopwatch.StartNew();
        a();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static readonly UniformFlowInput Trap = new() { Section = new TrapezoidalSection(20, 2, 2) { ChannelDepth = 10 }, SolveFor = SolveFor.NormalDepth, ManningN = 0.025, Slope = 0.0016, Discharge = 400 };

    [Fact]
    public void RatingTable_5000Rows_UnderOneSecond()
    {
        RatingTable? rt = null;
        double ms = Time(() => rt = RatingTableBuilder.Build(new RatingTableRequest(Trap, RatingTableMode.DischargeFromDepth, 0.002, 10, 0.002)));
        Assert.Equal(5000, rt!.Rows.Count);
        Assert.True(ms < 1000 * Slack, $"{ms:F0} ms");
    }

    [Fact]
    public void RatingTable_5000Rows_DepthFromDischarge_UnderOneSecond()
    {
        RatingTable? rt = null;
        double ms = Time(() => rt = RatingTableBuilder.Build(new RatingTableRequest(Trap, RatingTableMode.DepthFromDischarge, 1, 5000, 1)));
        Assert.Equal(5000, rt!.Rows.Count);
        Assert.True(ms < 1000 * Slack, $"{ms:F0} ms");
    }

    [Fact]
    public void Irregular_5000Points_SolvesUnderOneSecond()
    {
        var rnd = new Random(42);
        // Noisy (LiDAR-like) surface with overbank n: thousands of breakpoints and small pools.
        var pts = Enumerable.Range(0, 5000).Select(i => new StationPoint(i * 0.1, 5 - 5 * Math.Sin(Math.PI * i / 4999.0) + 0.05 * rnd.NextDouble(), i < 4999 ? (i < 1500 || i > 3500 ? 0.06 : 0.035) : null)).ToList();
        var input = new UniformFlowInput { Section = new IrregularSection(pts), SolveFor = SolveFor.NormalDepth, ManningN = 0.035, Slope = 0.001, Discharge = 800 };
        UniformFlowResult? r = null;
        double ms = Time(() => r = UniformFlowCalculator.Solve(input));
        Assert.True(r!.Succeeded, string.Join("; ", r.Messages.Select(m => m.Text)));
        Assert.True(ms < 1000 * Slack, $"{ms:F0} ms");
    }

    [Fact]
    public void PdfReport_With5000RowRatingTable_UnderFiveSeconds()
    {
        var rt = RatingTableBuilder.Build(new RatingTableRequest(Trap, RatingTableMode.DischargeFromDepth, 0.002, 10, 0.002));
        var item = new PdfReportItem(new ChannelCalculation { Title = "Big", Input = Trap }, UniformFlowCalculator.Solve(Trap), new ReportOptions { RatingTable = rt });
        byte[]? pdf = null;
        double ms = Time(() => pdf = PdfReport.ToBytes(new ProjectInfo(), [item]));
        Assert.True(pdf!.Length > 0);
        Assert.True(ms < 5000 * Slack, $"{ms:F0} ms");
    }
}
