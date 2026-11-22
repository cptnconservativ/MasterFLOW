using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Sag inlet ponding with clogging: IDCM Table 4-12 spacing, HEC-22 weir hand calcs, clogging, flanking and overflow relief.</summary>
public class SagPondingTests
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);

    private static string Basic(string cls, double q10, double q100, string inlet, string extra = "") => $"""
        street class = {cls}
        pavement width = 36
        Sx = 0.02
        gutter width = 0
        curb height = 0.5
        G1 = -2
        G2 = 3
        curve length = 250
        Q10 = {q10}
        Q100 = {q100}
        {extra}
        [Inlet]
        {inlet}
        """;

    private static IReadOnlyList<IReadOnlyList<object?>> Rows(ToolResult r) => r.Tables.Single(t => t.Title == "Ponding at the sag").Rows;

    [Fact]
    public void Sample_RunsCleanly_AndClogsInOrder()
    {
        var tool = new SagPondingTool();
        var r = tool.Run(tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var rows = Rows(r);
        Assert.Equal(6, rows.Count);
        // Clean < Table 4-11 < fully clogged depth, for each storm.
        for (int s = 0; s < 2; s++)
        {
            double d0 = (double)rows[3 * s][3]!, d1 = (double)rows[3 * s + 1][3]!, d2 = (double)rows[3 * s + 2][3]!;
            Assert.True(d0 < d1 && d1 < d2, $"{d0} {d1} {d2}");
            Assert.Equal(0.0, (double)rows[3 * s + 2][6]!);         // clogged inlet takes nothing
        }
        Assert.Single(r.Charts);
    }

    [Theory]
    [InlineData(50, 0.3, 55)]      // IDCM Table 4-12 values reproduced by X = (200 d K)^0.5 with d in feet
    [InlineData(167, 0.8, 163)]
    [InlineData(20, 0.2, 28)]
    public void FlankingDistance_MatchesTable412(double k, double d, double xTable)
    {
        Assert.Equal(xTable, Math.Sqrt(200 * d * k), 0);
        // A 1 % change over L = 100·K ft gives K; the tool reports X for that sag.
        double lvc = k * 5;
        var tool = new SagPondingTool();
        var r = tool.Run($"""
            street class = local
            pavement width = 36
            Sx = 0.02
            curb height = 0.5
            G1 = -2
            G2 = 3
            curve length = {lvc}
            Q10 = 1
            [Inlet]
            type = curb
            L = 10
            h = 0.5
            [Flanking]
            type = curb
            L = 5
            h = 0.5
            flanking depth = {d}
            """, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var geo = r.Tables.Single(t => t.Title.StartsWith("Sag curve")).Rows;
        Assert.Contains(geo, row => ((string)row[0]!).StartsWith("Flanking inlets") && ((string)row[1]!).StartsWith($"{Math.Sqrt(200 * d * k):F1}"));
    }

    [Fact]
    public void UndepressedCurbWeir_MatchesHandCalc()
    {
        // HEC-22 Eq. 4-30, undepressed: Q = 3.0 L d^1.5. L = 10 ft, Q = 5 cfs: d = (5/30)^(2/3) = 0.3029 ft (< h, weir flow).
        // Spread on a uniform 2 % cross slope: T = d/Sx = 15.1 ft.
        var tool = new SagPondingTool();
        var r = tool.Run(Basic("local", 5, 5, "type = curb\nL = 10\nh = 0.5"), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var row = Rows(r)[0];
        Assert.Equal(0.303, (double)row[3]!, 3);
        Assert.Equal(15.1, (double)row[5]!, 1);
        // Curb openings do not clog in Table 4-11: the design case equals the clean case.
        Assert.Equal((double)row[3]!, (double)Rows(r)[1][3]!);
    }

    [Fact]
    public void GrateClogging_Table411_Reduces40Percent()
    {
        // Grate 3 ft × 2 ft, 3.0 ft² open, Q = 2 cfs: weir on perimeter P = L + 2W (HEC-22, one side against the curb).
        // Clean: P = 7, 40 % clogged: P = 4.2 ft. d = (Q / 3.0 P)^(2/3) at the grate, plus Sx·W_eff/2 to the curb.
        var tool = new SagPondingTool();
        var r = tool.Run(Basic("local", 2, 2, "type = grate\nL = 3\nW = 2\nArea = 3.0"), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var rows = Rows(r);
        Assert.StartsWith("Table 4-11 clogging (40 %", (string)rows[1][1]!);
        double dClean = (double)rows[0][3]!, dClog = (double)rows[1][3]!;
        // As in the inlet network tool, the grate depth is taken at the middle of the effective (unclogged) width.
        Assert.Equal(Math.Pow(2 / (3.0 * 7), 2.0 / 3) + 0.02 * 2.0 / 2, dClean, 3);
        Assert.Equal(Math.Pow(2 / (3.0 * 4.2), 2.0 / 3) + 0.02 * 1.2 / 2, dClog, 3);
    }

    [Fact]
    public void FullyClogged_FlanksAndOverflowCarryTheFlow()
    {
        var tool = new SagPondingTool();
        var r = tool.Run(Basic("arterial4", 4, 12, "type = grate\nL = 3\nW = 2\nArea = 3.0",
            "overflow depth = 0.8\noverflow length = 30") + "\n[Flanking]\ntype = curb\nL = 10\nh = 0.5\nflanking depth = 0.2", Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var rows = Rows(r);
        foreach (var row in rows.Where(x => ((string)x[1]!).Contains("fully clogged")))
        {
            double q = Convert.ToDouble(row[2]), qf = (double)row[7]!, qo = (double)row[8]!;
            Assert.Equal(q, qf + qo, 2);                         // everything leaves by the flanks and the overflow
            Assert.True(qf > 0);
        }
        Assert.DoesNotContain(r.Messages, m => m.Code == "W-SP-004");
    }

    [Fact]
    public void ArterialWithoutFlanking_Warns_AndOverflowNeededWhenClogged()
    {
        var tool = new SagPondingTool();
        var r = tool.Run(Basic("arterial3", 3, 8, "type = grate\nL = 3\nW = 2\nArea = 3.0"), Us);
        Assert.Contains(r.Messages, m => m.Code == "W-SP-004");
        // No flanks and no overflow: a clogged grate passes nothing, so the sag fails to drain.
        Assert.Contains(r.Messages, m => m.Code == "W-SP-001");
        Assert.Contains(Rows(r), row => ((string)row[10]!).StartsWith("FAILS: no outlet"));
    }

    [Fact]
    public void CollectorSpreadLimit_Enforced()
    {
        // Collector, 36 ft: 12 ft dry → 12 ft allowed spread each side. Curb L = 5, Q10 = 4: d = (4/15)^(2/3) = 0.414, T = 20.7 ft > 12.
        var tool = new SagPondingTool();
        var r = tool.Run(Basic("collector", 4, 4, "type = curb\nL = 5\nh = 0.5"), Us);
        var row = Rows(r)[0];
        Assert.StartsWith("FAILS", (string)row[10]!);
        Assert.Contains("> 12.0 ft", (string)row[10]!);
    }

    [Fact]
    public void SiUnits_Refused()
    {
        var tool = new SagPondingTool();
        var r = tool.Run(tool.SampleInput(Us), new DesignToolContext(UnitSystem.SI));
        Assert.Contains(r.Messages, m => m.Code == "E-SP-001");
    }
}
