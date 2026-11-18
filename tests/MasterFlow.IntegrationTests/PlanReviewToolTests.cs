using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Plan review on the Main St test network; expected findings worked out by hand from its inverts and rims.</summary>
public class PlanReviewToolTests
{
    private static IReadOnlyList<IReadOnlyList<object?>> Rows(string input, StormNetwork? net = null)
    {
        var n = net ?? NetworkOutputTests.NetA();
        var r = new PlanReviewTool().Run(input, new DesignToolContext(n.Units, n, StormNetworkSolver.Solve(n)));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r.Tables[0].Rows;
    }

    private static bool Has(IReadOnlyList<IReadOnlyList<object?>> rows, string result, string check, string itemStart) =>
        rows.Any(r => (string)r[0]! == result && (string)r[1]! == check && ((string)r[3]!).StartsWith(itemStart));

    [Fact]
    public void Crowns_Cover_AndSpacing()
    {
        var rows = Rows("minimum cover = 1.5\nbends = no\nshow passing = yes");
        // At MH-2 the 24 in. outlet crown (100.80 + 2.0 = 102.80) is above both incoming crowns (102.60, 102.55); 0.8D points also miss.
        Assert.True(Has(rows, "FAIL", "Crowns matched at a size increase", "P2 → P1"));
        Assert.True(Has(rows, "FAIL", "Crowns matched at a size increase", "P3 → P1"));
        Assert.True(Has(rows, "OK", "Outlet invert not above inlet invert", "P2 → P1"));
        // CB-4: rim 104.0 − crown (101.80 + 1.25) = 0.95 ft < 1.5
        var cover = rows.Single(r => (string)r[1]! == "Minimum cover over the crown" && ((string)r[3]!).EndsWith("at CB-4"));
        Assert.Equal("FAIL", cover[0]);
        Assert.Equal(0.95, (double)cover[4]!, 9);
        Assert.True(Has(rows, "OK", "Manhole/inlet spacing", "P1"));
        Assert.True(Has(rows, "OK", "Minimum pipe size", "P3"));
    }

    [Fact]
    public void Bends_RaiseMinimumSize_AndLongPipesFailSpacing()
    {
        var net = NetworkOutputTests.NetA();
        net = net with { Pipes = net.Pipes.Select(x => x.Name == "P1" ? x with { Length = 450 } : x).ToList() };
        var rows = Rows("bends = yes", net);
        Assert.True(Has(rows, "FAIL", "Minimum pipe size", "P3"));      // 15 in. < 18 in.
        Assert.True(Has(rows, "FAIL", "Manhole/inlet spacing", "P1"));  // 450 > 400 ft for 24 in.
        Assert.DoesNotContain(rows, r => (string)r[0]! == "OK");          // passing hidden by default
    }

    [Fact]
    public void NeedsASolvedNetwork()
    {
        var r = new PlanReviewTool().Run("", new DesignToolContext(UnitSystem.USCustomary));
        Assert.False(r.Succeeded);
    }
}
