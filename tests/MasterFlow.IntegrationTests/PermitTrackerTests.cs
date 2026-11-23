using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Permit and approval tracker (premium, D-280): permits from project facts, CPM schedule, actual dates, next actions.</summary>
public class PermitTrackerTests
{
    private static readonly PermitTrackerTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);

    private static IReadOnlyList<object?> Item(ToolResult r, string id) => r.Tables.Single(t => t.Title == "Permits and approvals").Rows.Single(x => (string)x[0]! == id);
    private static List<string> Ids(ToolResult r) => r.Tables.Single(t => t.Title == "Permits and approvals").Rows.Select(x => (string)x[0]!).ToList();

    [Fact]
    public void Facts_DecideWhichPermits()
    {
        var small = Tool.Run("start = 2027-01-04\ntoday = 2027-01-04\ndisturbed area = 0.5\n", Us);
        Assert.Equal(["DRAIN", "GRADE", "START"], Ids(small));
        var big = Tool.Run("start = 2027-01-04\ntoday = 2027-01-04\ndisturbed area = 5\nsubdivision = yes\npublic water or sewer = yes\nin floodplain = yes\nmap revision = yes\nwaters or wetlands = yes\nstate highway access = yes\ninside city = no\n", Us);
        foreach (var id in new[] { "PRELIM", "ENG", "DANR-WS", "SWPPP", "NOI", "FLOOD", "CLOMR", "404", "401", "SDDOT", "PLAT" }) Assert.Contains(id, Ids(big));
        Assert.StartsWith("Pennington County", (string)Item(big, "GRADE")[2]!);
    }

    [Fact]
    public void CriticalPath_AddsUpDurations()
    {
        // 2 ac: DRAIN (15 + 2×21 + 7 = 64) → SWPPP (10) → NOI (2 + 7 = 9) → GRADE (3 + 10 = 13) = 96 days.
        var r = Tool.Run("start = 2027-01-04\ntoday = 2027-01-04\ndisturbed area = 2\n", Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Equal("2027-04-10", Item(r, "START")[9]);     // Jan 4 + 96 days
        Assert.Contains(r.Summary, s => s.StartsWith("Critical path: Storm water report and drainage plan → Storm water pollution prevention plan (SWPPP) → Construction storm water general permit (NOI) → Grading permit", StringComparison.Ordinal));
        Assert.All(Ids(r), id => Assert.Equal("yes", Item(r, id)[11]));
        Assert.Contains(r.Tables.Single(t => t.Title == "Next actions").Rows, x => (string)x[0]! == "DRAIN");
    }

    [Fact]
    public void FloodplainWithLongerReview_BecomesCritical_AndOthersHaveFloat()
    {
        var r = Tool.Run("start = 2027-01-04\ntoday = 2027-01-04\ndisturbed area = 2\nin floodplain = yes\n[Permits]\nId, Review\nFLOOD, 60\n", Us);
        Assert.Equal("yes", Item(r, "FLOOD")[11]);
        Assert.Equal("", Item(r, "NOI")[11]);
        // FLOOD 10 + 2×60 + 10 = 140 vs SWPPP + NOI = 19: NOI has 121 days of float.
        Assert.Equal(121.0, Convert.ToDouble(Item(r, "NOI")[10]));
    }

    [Fact]
    public void ActualDates_DriveTheForecast_AndLateReviewWarns()
    {
        var input = "start = 2027-01-04\ntoday = 2027-04-01\ndisturbed area = 0.5\n[Permits]\nId, Submitted, Approved\nDRAIN, 2027-01-20\n";
        var r = Tool.Run(input, Us);
        Assert.Equal("IN REVIEW", Item(r, "DRAIN")[12]);
        // Forecast approval Jan 20 + 49 = Mar 10 is past: the finish moves to today and the tracker says to follow up.
        Assert.Equal("2027-04-01", Item(r, "DRAIN")[9]);
        Assert.Contains(r.Messages, m => m.Code == "W-PT-002");
        var done = Tool.Run("start = 2027-01-04\ntoday = 2027-04-01\ndisturbed area = 0.5\n[Permits]\nId, Submitted, Approved, Fee\nDRAIN, 2027-01-20, 2027-02-15, $250\n", Us);
        Assert.Equal("APPROVED", Item(done, "DRAIN")[12]);
        Assert.Equal("READY", Item(done, "GRADE")[12]);
        Assert.Contains(done.Summary, s => s.Contains("Fees entered: $250"));
    }

    [Fact]
    public void AddedItem_JoinsTheConstructionStart()
    {
        var r = Tool.Run("start = 2027-01-04\ntoday = 2027-01-04\n[Permits]\nId, Name, Agency, Depends on, Prep, Review, Cycles\nXING, Railroad crossing permit, BNSF, DRAIN, 30, 90, 1\n", Us);
        Assert.Contains("XING", (string)Item(r, "START")[4]!);
        Assert.Equal("yes", Item(r, "XING")[11]);
    }

    [Theory]
    [InlineData("today = 2027-01-01\n", "E-PT-001")]
    [InlineData("start = 2027-01-04\n[Permits]\nId, Review\nDRAIN, soon\n", "E-PT-002")]
    [InlineData("start = 2027-01-04\n[Permits]\nId, Submitted\nDRAIN, Jan 5\n", "E-PT-004")]
    [InlineData("start = 2027-01-04\n[Permits]\nId, Depends on\nDRAIN, GRADE\n", "E-PT-005")]
    public void Errors(string input, string code) => Assert.Contains(Tool.Run(input, Us).Messages, m => m.Code == code);

    [Fact]
    public void SampleRuns_AndIsPremium()
    {
        var r = Tool.Run(Tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Single(r.Charts);
        Assert.Equal("permit-tracker", PremiumFeatures.ForTool("permit-tracker")?.Id);
    }
}
