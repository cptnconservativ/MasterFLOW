using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Quantity takeoff on the Main St test network; expected values worked out by hand from its rims and inverts.</summary>
public class QuantityToolTests
{
    private static ToolResult Run(string input, StormNetwork? net = null)
    {
        var n = net ?? NetworkOutputTests.NetA();
        return new QuantityTool().Run(input, new DesignToolContext(n.Units, n, StormNetworkSolver.Solve(n)));
    }

    private const string Base = """
        side clearance = 2.0
        bedding depth = 0.5
        wall = auto
        structure base depth = 1.0
        base structure depth = 6
        contingency = 10
        [Unit prices]
        Item	Unit	Price
        Pipe 24 in RCP	LF	100
        Pipe 18 in RCP	LF	80
        Pipe 15 in RCP	LF	70
        Manhole	each	4000
        Inlet	each	3000
        Outlet	each	2000
        Manhole extra depth	VF	500
        Excavation	CY	20
        Bedding	CY	40
        Backfill	CY	15
        """;

    private static IReadOnlyList<object?> Row(ToolResult r, int table, string first) => r.Tables[table].Rows.Single(x => (string)x[0]! == first);

    [Fact]
    public void P1_Trench_MatchesHandCalculation()
    {
        var r = Run(Base);
        Assert.True(r.Succeeded);
        // P1: D 2.0 ft, wall 2/12 + 1/12 = 0.25, OD 2.5, width 4.5. Depth up 106 − (100.80 − 0.25 − 0.5) = 5.95; down 105 − (100.00 − 0.75) = 5.75.
        var p1 = Row(r, 1, "P1");
        Assert.Equal("Pipe 24 in RCP", p1[2]);
        Assert.Equal(4.5, (double)p1[5]!, 6);
        Assert.Equal(5.95, (double)p1[6]!, 6);
        Assert.Equal(5.75, (double)p1[7]!, 6);
        Assert.Equal(4.5 * 5.85 * 200 / 27, (double)p1[8]!, 6);          // 195.0 CY
        Assert.Equal(4.5 * 0.5 * 200 / 27, (double)p1[9]!, 6);            // 16.67 CY
        Assert.Equal(195.0 - 4.5 * 0.5 * 200 / 27 - Math.PI * 2.5 * 2.5 / 4 * 200 / 27, (double)p1[10]!, 6); // 141.97 CY
    }

    [Fact]
    public void Structures_KindsDepthsAndExtraDepth()
    {
        var r = Run(Base);
        var mh = Row(r, 2, "MH-2");
        Assert.Equal("Manhole", mh[1]);
        Assert.Equal(6.2, (double)mh[4]!, 6);   // 106.0 − 100.80 + 1.0
        Assert.Equal(0.2, (double)mh[5]!, 6);   // over 6 ft
        Assert.Equal("Inlet", Row(r, 2, "CB-3")[1]);
        Assert.Equal(3.2, (double)Row(r, 2, "CB-4")[4]!, 6);
        Assert.Null(Row(r, 2, "OUT")[5]);       // outlets carry no extra depth
        Assert.Equal(2.0, (double)Row(r, 0, "Inlet")[2]!);
        Assert.Equal(1.0, (double)Row(r, 0, "Manhole")[2]!);
        Assert.Equal(0.2, (double)Row(r, 0, "Manhole extra depth")[2]!, 6);
    }

    [Fact]
    public void Totals_SumAmountsAndApplyContingency()
    {
        var r = Run(Base);
        Assert.DoesNotContain(r.Messages, m => m.Code == "W-QT-004");
        var items = r.Tables[0].Rows.Where(x => x[3] is double).ToList();
        double sum = items.Sum(x => (double)x[4]!);
        Assert.Equal(sum, (double)Row(r, 0, "Subtotal")[4]!, 2);
        Assert.Equal(Math.Round(sum * 0.10, 2), (double)Row(r, 0, "Contingency (10 %)")[4]!, 2);
        Assert.Equal(sum * 1.10, (double)Row(r, 0, "Total opinion of probable cost")[4]!, 1);
        Assert.Equal(200 * 100.0, (double)Row(r, 0, "Pipe 24 in RCP")[4]!, 6);
    }

    [Fact]
    public void MissingPrice_IsWarnedAndLeftOutOfTotal()
    {
        var r = Run(Base.Replace("Pipe 15 in RCP\tLF\t70\n", ""));
        Assert.Contains(r.Messages, m => m.Code == "W-QT-004" && m.Text.Contains("Pipe 15 in RCP"));
        Assert.Null(Row(r, 0, "Pipe 15 in RCP")[4]);
    }

    [Fact]
    public void PlasticN_SmallPipe_IsPvc()
    {
        var n = NetworkOutputTests.NetA();
        n = n with { Pipes = [n.Pipes[0], n.Pipes[1] with { ManningN = 0.010 }, n.Pipes[2]] };
        var r = Run(Base, n);
        Assert.Equal("Pipe 18 in PVC", Row(r, 1, "P2")[2]);
        Assert.Contains(r.Messages, m => m.Code == "W-QT-004" && m.Text.Contains("Pipe 18 in PVC"));
    }

    [Fact]
    public void NoNetwork_Fails()
    {
        var r = new QuantityTool().Run(Base, new DesignToolContext(UnitSystem.USCustomary));
        Assert.False(r.Succeeded);
    }
}
