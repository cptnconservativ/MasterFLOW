using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>Review comment tracker: statuses, rounds, network values, plan review findings and the response letter.</summary>
public class ReviewCommentTests
{
    private static readonly ReviewCommentTool Tool = new();
    private static DesignToolContext Ctx(StormNetwork? n = null)
    {
        n ??= NetworkOutputTests.NetA();
        return new DesignToolContext(n.Units, n, StormNetworkSolver.Solve(n));
    }

    [Fact]
    public void Sample_CountsStatuses_AndShowsNetworkValues()
    {
        var ctx = Ctx();
        var r = Tool.Run(Tool.SampleInput(ctx).Replace("include plan review = yes", "include plan review = no"), ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var by = r.Tables.Single(t => t.Title == "Comments by reviewer");
        var total = by.Rows[^1];
        Assert.Equal([5, 1, 2, 1, 1], total.Skip(1).Select(x => (int)x!).ToArray());
        var m = r.Tables[0];
        var c1 = m.Rows.Single(x => (string?)x[0] == "1");
        var hgl = ctx.NetworkResult!.Structure("MH-2")!.Hgl;
        Assert.Contains($"HGL {hgl:F2}", (string)c1[8]!);
        Assert.Contains("rim − HGL", (string)c1[8]!);
        Assert.StartsWith("Q 20", (string)m.Rows.Single(x => (string?)x[0] == "2")[8]!);
        // Open: comment 4 only (5 is deferred).
        Assert.Equal(["4"], r.Tables.Single(t => t.Title == "Open items").Rows.Select(x => (string)x[0]!).ToArray());
        Assert.Contains(r.Messages, x => x.Code == "W-RC-007");
        Assert.NotNull(r.Report);
    }

    [Fact]
    public void CarriedOverAndMissingResponses()
    {
        var r = Tool.Run("round = 3\n[Comments]\nID\tRound\tReviewer\tSheet\tItem\tComment\tResponse\tStatus\n" +
                         "10\t1\tCity\tC1\t\tOld comment, still open\t\topen\n2\t3\tCity\tC2\t\tSays responded\t\tresponded\n1\t3\tCity\tC2\t\tNew\tDone\tclosed", Ctx());
        Assert.Contains(r.Messages, m => m.Code == "W-RC-006" && m.Text.Contains("10"));
        Assert.Contains(r.Messages, m => m.Code == "W-RC-005" && m.Text.Contains("Comment 2"));
        var open = r.Tables.Single(t => t.Title == "Open items");
        Assert.Equal("carried over", open.Rows.Single(x => (string?)x[0] == "10")[6]);
        // Natural order within a round: 1 before 2, and round 1 first.
        Assert.Equal(["10", "1", "2"], r.Tables[0].Rows.Select(x => (string)x[0]!).ToArray());
    }

    [Fact]
    public void PlanReviewFindings_AddedAsOpenItems()
    {
        var n = NetworkOutputTests.NetA() with { TailwaterElevation = 104.6 };   // HGL near the rims fails IDCM 4.6.7
        var ctx = Ctx(n);
        var r = Tool.Run("include plan review = yes\n[Comments]\nID\tComment\n1\tGeneral comment", ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var pr = r.Tables[0].Rows.Where(x => ((string)x[0]!).StartsWith("PR-")).ToList();
        Assert.NotEmpty(pr);
        Assert.All(pr, x => Assert.Equal("open", x[7]));
        Assert.Contains(r.Messages, m => m.Code == "I-RC-002");
    }

    [Fact]
    public void ResponseLetter_RendersToHtmlAndWord()
    {
        var ctx = Ctx();
        var r = Tool.Run(Tool.SampleInput(ctx), ctx);
        var html = ToolReport.ToHtml(new ProjectInfo { Name = "P" }, r, "x", "test");
        Assert.Contains("Response to review comments", html);
        Assert.Contains("Riprap basin added per HEC-14", html);
        Assert.True(DrainageReportWriter.ToDocx(new ProjectInfo { Name = "P" }, r.Report!).Length > 1000);
    }

    [Fact]
    public void BadInput()
    {
        Assert.Contains(Tool.Run("round = 1", Ctx()).Messages, m => m.Code == "E-RC-001");
        var r = Tool.Run("[Comments]\nID\tComment\tStatus\n1\tx\tmaybe\n1\ty\topen", Ctx());
        Assert.Contains(r.Messages, m => m.Code == "W-RC-003");
        Assert.Contains(r.Messages, m => m.Code == "W-RC-001");
    }
}
