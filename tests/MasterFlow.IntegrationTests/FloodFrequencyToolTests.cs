using MasterFlow.Application.Hydrology;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Bulletin 17C tool: record handling, skew weighting, low outliers, historical data and plotting positions.</summary>
public class FloodFrequencyToolTests
{
    private static readonly FloodFrequencyTool Tool = new();
    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => Tool.Run(input, new DesignToolContext(u));

    private static ToolResult Ok(string input)
    {
        var r = Run(input);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static readonly double[] Flows = [4660, 1470, 811, 1550, 1760, 3680, 1570, 2720, 4610, 1500, 694, 444, 1080, 1400, 1180, 1920, 3020, 2100, 519, 3650];

    private static string Record(string head, IEnumerable<double>? flows = null, int start = 2000) =>
        head + "\n[Peaks]\nYear\tPeak\n" + string.Join("\n", (flows ?? Flows).Select((q, i) => $"{start + i}\t{q}"));

    private static object? Item(ToolResult r, string table, string item) =>
        r.Tables.Single(t => t.Title.StartsWith(table)).Rows.Single(x => ((string)x[0]!).StartsWith(item))[1];

    private static double FreqQ(ToolResult r, double aep) =>
        (double)r.Tables.Single(t => t.Title == "Frequency curve").Rows.Single(x => (double)x[0]! == aep)[3]!;

    [Theory]
    [InlineData("1200", 1200, 1200)]
    [InlineData("<500", 0, 500)]
    [InlineData("800-1200", 800, 1200)]
    [InlineData("12,500", 12500, 12500)]
    [InlineData("0", 0, 0)]
    public void ParsePeak_ReadsAllForms(string s, double lo, double hi) => Assert.Equal((lo, hi), FloodFrequencyTool.ParsePeak(s)!.Value);

    [Fact]
    public void ParsePeak_GreaterThan_IsOpenAbove()
    {
        Assert.Equal((8000, double.PositiveInfinity), FloodFrequencyTool.ParsePeak(">8000")!.Value);
        Assert.Null(FloodFrequencyTool.ParsePeak("abc"));
        Assert.Null(FloodFrequencyTool.ParsePeak("-5"));
    }

    [Fact]
    public void ConfidenceK_MatchesBulletin17B()
    {
        double z = 1.6448536, n = 30, k = 2.0;
        double a = 1 - z * z / (2 * (n - 1)), b = k * k - z * z / n;
        var c = FloodFrequencyTool.ConfidenceK(k, 30, 0.90)!.Value;
        Assert.Equal((k + Math.Sqrt(k * k - a * b)) / a, c.Upper, 6);
        Assert.Equal((k - Math.Sqrt(k * k - a * b)) / a, c.Lower, 6);
        Assert.Null(FloodFrequencyTool.ConfidenceK(2.0, 2, 0.90));
    }

    [Fact]
    public void ExactRecord_StationSkew_IsTheMethodOfMoments()
    {
        var r = Ok(Record("low outliers = none"));
        var s = Ema.SampleMoments(Flows.Select(Math.Log10).ToList());
        Assert.Equal(s.Mean, (double)Item(r, "Log-Pearson", "Mean (final)")!, 4);
        Assert.Equal(s.Sd, (double)Item(r, "Log-Pearson", "Standard deviation (final)")!, 4);
        Assert.StartsWith(Math.Round(s.Skew, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), (string)Item(r, "Log-Pearson", "Skew used")!);
        double q100 = Math.Pow(10, s.Mean + Pearson3.K(s.Skew, 0.99) * s.Sd);
        Assert.Equal(FloodFrequencyTool.Sig(q100), FreqQ(r, 0.01));
        Assert.Contains(r.Messages, m => m.Code == "W-FF-003");
    }

    [Fact]
    public void WeightedSkew_UsesInverseMse_AndRefitsMeanAndSd()
    {
        var r = Ok(Record("low outliers = none\nregional skew = -0.3\nregional skew SE = 0.55"));
        var s = Ema.SampleMoments(Flows.Select(Math.Log10).ToList());
        double gw = Ema.WeightedSkew(s.Skew, Ema.SkewMse(s.Skew, 20), -0.3, 0.3025);
        Assert.Equal(Math.Round(gw, 3), (double)Item(r, "Log-Pearson", "Weighted skew")!);
        // With no censoring the fixed-skew refit leaves the mean and sd at the sample values.
        Assert.Equal(s.Mean, (double)Item(r, "Log-Pearson", "Mean (final)")!, 4);
        double q100 = Math.Pow(10, s.Mean + Pearson3.K(gw, 0.99) * s.Sd);
        Assert.Equal(FloodFrequencyTool.Sig(q100), FreqQ(r, 0.01));
    }

    [Fact]
    public void RegionalSkew_IsUsedWhenAsked()
    {
        var r = Ok(Record("low outliers = none\nregional skew = -0.3\nregional skew SE = 0.55\nskew = regional"));
        Assert.StartsWith("-0.3 (regional)", (string)Item(r, "Log-Pearson", "Skew used")!);
    }

    [Fact]
    public void Mgbt_CensorsLowOutliers()
    {
        var r = Ok(Record("", Flows.Concat([26.0, 60.0])));
        Assert.Equal(2, Item(r, "Record", "PILFs"));
        Assert.Equal("444 cfs", Item(r, "Record", "Low-outlier threshold"));
        Assert.Contains(r.Messages, m => m.Code == "I-FF-001");
        var peaks = r.Tables.Single(t => t.Title == "Annual peaks").Rows;
        Assert.Equal(2, peaks.Count(x => ((string)x[4]!).StartsWith("PILF")));
        // Censoring the outliers lifts the low tail of the curve compared with fitting them as exact values.
        var exact = Ok(Record("low outliers = none", Flows.Concat([26.0, 60.0])));
        Assert.True(FreqQ(r, 0.99) > FreqQ(exact, 0.99));
    }

    [Fact]
    public void GivenThreshold_CensorsBelowIt()
    {
        var r = Ok(Record("low outliers = 700"));
        Assert.Equal(3, Item(r, "Record", "PILFs"));     // 694, 444, 519
        Assert.DoesNotContain(r.Tables, t => t.Title.StartsWith("Multiple"));
    }

    [Fact]
    public void ZeroFlows_AreCensored()
    {
        var r = Ok(Record("low outliers = none", Flows.Concat([0.0])));
        Assert.Equal(1, Item(r, "Record", "Zero flows"));
        Assert.Contains(r.Tables.Single(t => t.Title == "Annual peaks").Rows, x => (string)x[4]! == "zero flow, < LOT");
    }

    [Fact]
    public void HistoricalPeriod_AddsUnrecordedYears_AndRaisesTheTail()
    {
        string head = "low outliers = none\nhistoric start = 1900\nhistoric threshold = 9000";
        var r = Ok(Record(head, Flows) + "\n1950\t15000\tH");
        // 1900–2019 = 120 years: 20 systematic, 1 historical, 99 unrecorded.
        Assert.Equal(120, Item(r, "Record", "Years represented"));
        Assert.Contains("99 unrecorded", (string)Item(r, "Record", "Historical period")!);
        var pp = r.Tables.Single(t => t.Title == "Annual peaks").Rows.Single(x => (int)x[0]! == 1950)[3];
        Assert.Equal(Math.Round(1.0 / 120 * 1 / 2, 4), pp);
        var sysOnly = Ok(Record("low outliers = none"));
        Assert.True(FreqQ(r, 0.002) > FreqQ(sysOnly, 0.002));
    }

    [Fact]
    public void WeibullPlottingPositions_WithoutHistory()
    {
        var r = Ok(Record("low outliers = none"));
        var rows = r.Tables.Single(t => t.Title == "Annual peaks").Rows;
        Assert.Equal(Math.Round(1 / 21.0, 4), rows.Single(x => (string)x[1]! == "4660")[3]);
        Assert.Equal(Math.Round(20 / 21.0, 4), rows.Single(x => (string)x[1]! == "444")[3]);
    }

    [Fact]
    public void CensoredPeaks_AreCountedAndFitted()
    {
        var r = Ok(Record("low outliers = none", Flows) + "\n2030\t<300\n2031\t>9000");
        Assert.Equal(2, Item(r, "Record", "Censored"));
        var plain = Ok(Record("low outliers = none"));
        Assert.True(FreqQ(r, 0.01) > FreqQ(plain, 0.01));
    }

    [Fact]
    public void Sample_RunsInBothUnitSystems_WithChart()
    {
        foreach (var u in new[] { UnitSystem.USCustomary, UnitSystem.SI })
        {
            var r = Run(Tool.SampleInput(new DesignToolContext(u)), u);
            Assert.True(r.Succeeded);
            Assert.Single(r.Charts);
            Assert.Equal(FloodFrequencyTool.Aeps.Length, r.Tables.Single(t => t.Title == "Frequency curve").Rows.Count);
        }
    }

    [Theory]
    [InlineData("[Peaks]\nYear\tPeak\n2000\t100\n2001\t200", "E-FF-001")]
    [InlineData("[Peaks]\nYear\tPeak\nyr\t100", "E-FF-002")]
    [InlineData("[Peaks]\nYear\tPeak\n2000\tlots", "E-FF-002")]
    [InlineData("[Peaks]\nYear\tPeak\n2000\t100\n2000\t200", "E-FF-003")]
    [InlineData("regional skew = 0.1\n[Peaks]\nYear\tPeak\n2000\t100", "E-FF-004")]
    [InlineData("historic start = 1900\n[Peaks]\nYear\tPeak\n2000\t100", "E-FF-005")]
    [InlineData("skew = weighted\n[Peaks]\nYear\tPeak\n2000\t100", "E-FF-006")]
    [InlineData("confidence = 30\n[Peaks]\nYear\tPeak\n2000\t100", "E-FF-007")]
    [InlineData("low outliers = sometimes\n[Peaks]\nYear\tPeak\n2000\t100", "E-FF-008")]
    public void BadInput_ReportsError(string input, string code)
    {
        var r = Run(input);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }
}
