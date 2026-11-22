using MasterFlow.Application.Hydrology;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>NOAA Atlas 14 lookup: PFDS CSV parsing, cached fetch (no network in tests) and hand-off to the rational method.</summary>
[Collection("Atlas14")]
public class Atlas14Tests
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);

    [Fact]
    public void Sample_ParsesRapidCityTable()
    {
        var tool = new Atlas14Tool();
        var r = tool.Run(tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var depth = r.Tables[0];
        Assert.Equal(19, depth.Rows.Count);
        var h24 = depth.Rows.Single(x => (string)x[0]! == "24-hr");
        Assert.Equal(1440.0, (double)h24[1]!);
        Assert.Equal(2.28, (double)h24[2]!);      // 2-yr (PFDS, 44.0805, −103.2310, retrieved 2026-11-21)
        Assert.Equal(6.12, (double)h24[^1]!);     // 100-yr
        var i5 = r.Tables[1].Rows[0];
        Assert.Equal(0.546 / (5.0 / 60), (double)i5[4]!, 9);   // 10-yr 5-min intensity
        var cmp = r.Tables.Single(t => t.Title.StartsWith("Comparison")).Rows.Single(x => (string)x[0]! == "100-yr" && (double)x[1]! == 1440);
        Assert.Equal((6.12 - 4.55) / 4.55 * 100, (double)cmp[4]!, 6);
        Assert.Equal(2, r.Charts.Count);
    }

    [Fact]
    public void Parse_DurationsAndInterpolation()
    {
        Assert.Equal(120, Atlas14.Minutes("2-hr"));
        Assert.Equal(4320, Atlas14.Minutes("3-day"));
        Assert.True(double.IsNaN(Atlas14.Minutes("Latitude")));
        var t = Atlas14.Parse("by duration for ARI (years):, 2,10\n60-min:, 1.0,2.0\n2-hr:, 1.2,2.4\n", "x", out var err)!;
        Assert.Null(err);
        // log-log between 60 and 120 min at 90 min: 1.0·1.2^(ln 1.5 / ln 2)
        Assert.Equal(Math.Pow(1.2, Math.Log(1.5) / Math.Log(2)), t.DepthAt(2, 90), 9);
        Assert.Null(Atlas14.Parse("nothing here", "x", out err));
        Assert.NotNull(err);
    }

    [Fact]
    public void Fetch_UsesFetcherOnce_ThenCache()
    {
        string sample = new Atlas14Tool().SampleInput(Us);
        string csv = sample[(sample.IndexOf("[PFDS]") + 6)..];
        var oldF = Atlas14.Fetcher; var oldC = Atlas14.CacheFolder;
        string dir = Path.Combine(Path.GetTempPath(), "mf-a14-" + Guid.NewGuid().ToString("N"));
        int calls = 0;
        try
        {
            Atlas14.CacheFolder = dir;
            Atlas14.Fetcher = (la, lo, s) => { calls++; return (csv, null); };
            string input = "latitude = 44.0805\nlongitude = -103.2310\nfetch = yes\n";
            var r1 = new Atlas14Tool().Run(input, Us);
            var r2 = new Atlas14Tool().Run(input, Us);
            Assert.True(r1.Succeeded && r2.Succeeded);
            Assert.Equal(1, calls);
            Assert.Contains(r2.Messages, m => m.Text.Contains("cache"));
            Atlas14.Fetcher = (la, lo, s) => (null, "offline");
            var r3 = new Atlas14Tool().Run("latitude = 40\nlongitude = -100\nfetch = yes\n", Us);
            Assert.Contains(r3.Messages, m => m.Code == "E-A14-005");
        }
        finally { Atlas14.Fetcher = oldF; Atlas14.CacheFolder = oldC; try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RationalLines_RunInTheRationalTool()
    {
        var a = new Atlas14Tool();
        var r = a.Run(a.SampleInput(Us), Us);
        var lines = r.Tables.Single(t => t.Title == "Input lines for other tools").Rows.Where(x => (string)x[0]! == "Rational method").Select(x => (string)x[1]!);
        var rm = new RationalMethodTool();
        var ctx = DesignToolTests.Ctx();
        var res = rm.Run(rm.SampleInput(ctx) + "\n" + string.Join("\n", lines) + "\n", ctx);
        Assert.True(res.Succeeded, string.Join("\n", res.Messages.Select(m => m.Text)));
        Assert.Contains(res.Summary, s => s.Contains("[IDF]"));
    }
}
