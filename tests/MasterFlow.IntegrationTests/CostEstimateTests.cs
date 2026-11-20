using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Cost estimate and bid tab on the Main St network; expected values by hand from its rims and inverts (see QuantityToolTests).</summary>
public class CostEstimateTests
{
    private static ToolResult Run(string input, StormNetwork? net = null)
    {
        var n = net ?? NetworkOutputTests.NetA();
        return new CostEstimateTool().Run(input, new DesignToolContext(n.Units, n, StormNetworkSolver.Solve(n)));
    }

    private const string Head = """
        side clearance = 2.0
        bedding depth = 0.5
        structure base depth = 1.0
        base structure depth = 6
        pavement overcut = 1.0
        mobilization = 5
        contingency = 10
        engineering = 10
        """;

    private const string Items = """
        [Bid items]
        No	Item	Unit	Quantity	Estimate	Bidder A	Bidder B
        1	Pipe 24 in RCP	LF		100	90	110
        2	Pipe 18 in RCP	LF		80	75	85
        3	Pipe 15 in RCP	LF		70	65	75
        4	Manhole	each		4000	3800	4400
        5	Inlet	each		3000	2900	3300
        6	Outlet	each		2000	2000	2100
        7	Manhole extra depth	VF		500	450	550
        8	Excavation	CY		20	18	22
        9	Bedding	CY		40	38	44
        10	Backfill	CY		15	14	16
        11	Pavement removal and replacement	SY		80	75	85
        12	Traffic control	LS	1	5000	4000	6000
        """;

    private static IReadOnlyList<object?> Bid(ToolResult r, string item) => r.Tables[0].Rows.Single(x => (string)x[1]! == item);
    private static IReadOnlyList<object?> Total(ToolResult r, string label) => r.Tables[1].Rows.Single(x => ((string)x[0]!).StartsWith(label));

    [Fact]
    public void Quantities_MatchTheTakeoff_AndPavementByHand()
    {
        var r = Run(Head + "\n" + Items);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Equal(200.0, (double)Bid(r, "Pipe 24 in RCP")[3]!);
        Assert.Equal(2.0, (double)Bid(r, "Inlet")[3]!);
        // Pavement: (trench width + 2 × 1 ft) × length / 9. Widths: P1 4.5, P2 1.5+2(0.2083)+2 = 3.9167, P3 1.25+2(0.1875)+2 = 3.625.
        double pav = ((4.5 + 2) * 200 + (1.5 + 2 * (1.5 / 12 + 1.0 / 12) + 2 + 2) * 150 + (1.25 + 2 * (1.25 / 12 + 1.0 / 12) + 2 + 2) * 60) / 9;
        Assert.Equal(Math.Round(pav, 1), (double)Bid(r, "Pavement removal and replacement")[3]!, 6);
        // Excavation equals the free takeoff's total.
        var qt = new QuantityTool().Run("[Unit prices]\nItem\tUnit\tPrice\n", new DesignToolContext(UnitSystem.USCustomary, NetworkOutputTests.NetA()));
        double ex = (double)qt.Tables[0].Rows.Single(x => (string)x[0]! == "Excavation")[2]!;
        Assert.Equal(ex, (double)Bid(r, "Excavation")[3]!, 6);
        Assert.Equal("entered", Bid(r, "Traffic control")[4]);
    }

    [Fact]
    public void Totals_MarkupsOnTheEstimateOnly_AndRanking()
    {
        var r = Run(Head + "\n" + Items);
        double sub = r.Tables[0].Rows.Sum(x => (double)x[6]!);
        Assert.Equal(Math.Round(sub, 2), (double)Total(r, "Bid items subtotal")[1]!, 2);
        double mob = Math.Round(sub * 0.05, 2), cont = Math.Round((sub + mob) * 0.10, 2), eng = Math.Round((sub + mob + cont) * 0.10, 2);
        Assert.Equal(mob, (double)Total(r, "Mobilization")[1]!, 2);
        Assert.Equal(cont, (double)Total(r, "Contingency")[1]!, 2);
        Assert.Equal(eng, (double)Total(r, "Engineering")[1]!, 2);
        Assert.Equal(sub + mob + cont + eng, (double)Total(r, "Total")[1]!, 2);
        Assert.Null(Total(r, "Mobilization")[2]);                        // not applied to bids
        double a = r.Tables[0].Rows.Sum(x => (double)x[8]!), b = r.Tables[0].Rows.Sum(x => (double)x[10]!);
        Assert.Equal(Math.Round(a, 2), (double)Total(r, "Total")[2]!, 2);
        Assert.Equal(1, Total(r, "Rank")[2]);
        Assert.Equal(2, Total(r, "Rank")[3]);
        Assert.Equal(Math.Round(100 * (a - (sub + mob)) / (sub + mob), 1), (double)Total(r, "Difference")[2]!, 1);
        Assert.Contains(r.Summary, s => s.Contains("Low bid: Bidder A"));
    }

    [Fact]
    public void UnbalancedBid_IsFlagged()
    {
        var r = Run(Head + "\n" + Items.Replace("8\tExcavation\tCY\t\t20\t18\t22", "8\tExcavation\tCY\t\t20\t60\t22"));
        var flag = Assert.Single(r.Tables[2].Rows);
        Assert.Equal("Bidder A", flag[0]);
        Assert.Equal("HIGH", flag[7]);
        double avg = (20 + 60 + 22) / 3.0;
        Assert.Equal(Math.Round(100 * (60 - avg) / avg, 1), (double)flag[5]!, 1);
        Assert.Contains(r.Messages, m => m.Code == "W-CE-005");
    }

    [Fact]
    public void Escalation_AppliesToTheEstimateOnly()
    {
        var r = Run(Head + "\nprice index from = 12000\nprice index to = 13200\n" + Items);
        Assert.Equal(110.0, (double)Bid(r, "Pipe 24 in RCP")[5]!, 6);   // 100 × 1.1
        Assert.Equal(90.0, (double)Bid(r, "Pipe 24 in RCP")[7]!, 6);
        Assert.Contains(r.Messages, m => m.Code == "I-CE-003");
    }

    [Fact]
    public void Dewatering_BelowGroundwater_AndPavementList()
    {
        // Trench bottoms: P1 100.00 − 0.25 − 0.5 = 99.25; P2 101.10 − 0.2083 − 0.5 = 100.39; P3 101.30 − 0.1875 − 0.5 = 100.61.
        var r = Run(Head + "\ngroundwater elevation = 100.5\npavement = P1, P9\n" + Items + "\n13\tDewatering\tLF\t\t30\t25\t35\n");
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Equal(350.0, (double)Bid(r, "Dewatering")[3]!);
        Assert.Equal(Math.Round((4.5 + 2) * 200 / 9, 1), (double)Bid(r, "Pavement removal and replacement")[3]!, 6);
        Assert.Contains(r.Messages, m => m.Code == "W-CE-001" && m.Text.Contains("P9"));
    }

    [Fact]
    public void Units_ConvertFromTheTakeoff()
    {
        var r = Run(Head + "\n" + Items.Replace("8\tExcavation\tCY", "8\tExcavation\tCF"));
        var ex = Bid(r, "Excavation");
        Assert.Equal("CF", ex[2]);
        Assert.Contains(r.Messages, m => m.Code == "I-CE-002");
        var cy = Run(Head + "\n" + Items);
        Assert.Equal((double)Bid(cy, "Excavation")[3]! * 27, (double)ex[3]!, 0);
    }

    [Fact]
    public void MissingPricesAndItems_AreReported()
    {
        var r = Run(Head + "\n" + Items.Replace("4\tManhole\teach\t\t4000\t3800\t4400", "4\tManhole\teach\t\t4000\t\t4400").Replace("\n12\tTraffic control\tLS\t1\t5000\t4000\t6000", "") + "\n14\tSeeding\tSY\t\t3\t3\t3\n");
        Assert.True(r.Succeeded);
        Assert.Equal("incomplete", Total(r, "Rank")[2]);
        Assert.Equal(1, Total(r, "Rank")[3]);
        Assert.Contains(r.Messages, m => m.Code == "W-CE-004" && m.Text.Contains("Bidder A"));
        Assert.Contains(r.Messages, m => m.Code == "W-CE-002" && m.Text.Contains("Seeding"));
        var r2 = Run(Head + "\n" + Items.Replace("7\tManhole extra depth\tVF\t\t500\t450\t550\n", ""));
        Assert.Contains(r2.Messages, m => m.Code == "W-CE-003" && m.Text.Contains("Manhole extra depth"));
    }

    [Theory]
    [InlineData("contingency = -5", "E-CE-002")]
    [InlineData("pavement overcut = -1", "E-CE-002")]
    [InlineData("unbalanced threshold = 0", "E-CE-002")]
    [InlineData("price index from = 12000", "E-CE-003")]
    [InlineData("price index from = 0\nprice index to = 13000", "E-CE-003")]
    [InlineData("mobilization = abc", "E-TL-002")]
    public void BadSettings_AreReported(string line, string code)
    {
        var r = Run(line + "\n" + Items);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
        Assert.Empty(r.Tables);
    }

    [Fact]
    public void BadItems_AreReported()
    {
        Assert.Contains(Run(Head + "\n[Bid items]\nNo\tItem\tUnit\tQuantity\tEstimate\n1\tPipe 24 in RCP\tLF\t-5\t100\n").Messages, m => m.Code == "E-CE-005");
        Assert.Contains(Run(Head + "\n[Bid items]\nNo\tItem\tUnit\tQuantity\tEstimate\n1\tPipe 24 in RCP\tLF\t\t-100\n").Messages, m => m.Code == "E-CE-005");
        Assert.Contains(Run(Head + "\n[Bid items]\nNo\tItem\tUnit\tQuantity\tEstimate\n1\tPipe 24 in RCP\tCY\t\t100\n").Messages, m => m.Code == "E-CE-006");
        Assert.Contains(Run(Head + "\n[Bid items]\nA\tB\tC\n1\t2\t3\n").Messages, m => m.Code == "E-CE-004");
        Assert.Contains(Run(Head + "\n[Bid items]\nNo\tItem\tUnit\tQuantity\n1\tPipe 24 in RCP\tLF\t\n").Messages, m => m.Code == "E-CE-004");
        Assert.Contains(Run(Head).Messages, m => m.Code == "E-CE-004");
        Assert.Contains(new CostEstimateTool().Run(Items, new DesignToolContext(UnitSystem.USCustomary)).Messages, m => m.Code == "E-CE-001");
    }

    [Fact]
    public void SI_SampleRuns()
    {
        var n = NetworkOutputTests.NetA();
        var si = n with
        {
            Units = UnitSystem.SI,
            Structures = n.Structures.Select(s => s with { RimElevation = s.RimElevation * 0.3048 }).ToList(),
            Pipes = n.Pipes.Select(p => p with { Rise = p.Rise * 0.3048, Span = p.Span * 0.3048, Length = p.Length * 0.3048, UpstreamInvert = p.UpstreamInvert * 0.3048, DownstreamInvert = p.DownstreamInvert * 0.3048, Discharge = p.Discharge * 0.0283168 }).ToList(),
        };
        var tool = new CostEstimateTool();
        var ctx = new DesignToolContext(UnitSystem.SI, si, StormNetworkSolver.Solve(si));
        var r = tool.Run(tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Contains(r.Tables[0].Rows, x => ((string)x[1]!).StartsWith("Pipe 610 mm"));
        Assert.Equal("m2", Bid(r, "Pavement removal and replacement")[2]);
        Assert.Equal(Math.Round(60.96, 1), (double)Bid(r, "Pipe 610 mm RCP")[3]!, 6);
    }
}
