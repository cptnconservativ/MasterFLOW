using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Climate stress test: each projection must equal a direct solve of the scaled network, and each failure threshold
/// must bracket the change in state to 0.1 % rainfall.
/// </summary>
public class ClimateStressTests
{
    private static readonly ClimateStressTool Tool = new();
    private static ToolResult Run(string input, StormNetwork? net = null)
    {
        var n = net ?? NetworkOutputTests.NetA();
        return Tool.Run(input, new DesignToolContext(n.Units, n, StormNetworkSolver.Solve(n)));
    }
    private static StormNetwork Clean() => NetworkOutputTests.NetA() with
    {
        // Rims raised so the design meets every criterion; the test then sees only new failures.
        Structures = [.. NetworkOutputTests.NetA().Structures.Select(s => s.Name == "OUT" ? s : s with { RimElevation = 112 })],
    };

    [Fact]
    public void Projection_MatchesDirectSolve_WithTailwaterRiseAndExponent()
    {
        var r = Run("""
            network = Main St
            runoff exponent = 1.5
            [Projections]
            Scenario	Rainfall change (%)	Tailwater rise (ft)	Source
            Today	0	0	design
            Future	20	0.4	test
            """, Clean());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var row = r.Tables[0].Rows.Single(x => (string)x[0]! == "Future");
        Assert.Equal(Math.Round(Math.Pow(1.2, 1.5), 3), (double)row[2]!, 6);
        var direct = StormNetworkSolver.Solve(ClimateStressTool.Apply(Clean(), Math.Pow(1.2, 1.5), 0.4));
        Assert.Equal(102.9, direct.Network.TailwaterElevation!.Value, 9);
        var worst = direct.Structures.Where(s => !s.IsOutfall).MinBy(s => s.HglClearance!.Value)!;
        Assert.Equal($"{worst.Structure.Name}: {worst.HglClearance:0.00} ft", row[4]);
        Assert.Equal(direct.Pipes.Count(p => p.Pipe.Discharge > p.FullFlowCapacity), (int)row[7]!);
        Assert.Equal("test", row[10]);
        Assert.NotNull(r.Report);
    }

    [Fact]
    public void FailureThresholds_BracketTheChange()
    {
        var net = Clean();
        var r = Run("""
            network = Main St
            search up to = 150
            [Projections]
            Scenario	Rainfall change (%)
            Today	0
            """, net);
        var order = r.Tables[1].Rows;
        Assert.NotEmpty(order);
        foreach (var row in order.Where(x => (double)x[3]! > 0))
        {
            string el = (string)row[1]!, mode = ((string)row[2]!).Replace(" (allowed by the criteria)", "");
            double pct = (double)row[3]!;
            bool Has(double x)
            {
                var res = StormNetworkSolver.Solve(ClimateStressTool.Apply(net, 1 + x / 100, 0));
                return Fails(res, el, mode);
            }
            Assert.True(Has(pct + 0.05), $"{el} {mode} should fail at {pct}%");
            if (mode != "HGL criterion") Assert.False(Has(pct - 0.15), $"{el} {mode} should not fail below {pct}%");
        }
        // Thresholds are in increasing order.
        var p = order.Select(x => (double)x[3]!).ToList();
        Assert.Equal(p.OrderBy(x => x), p);
        Assert.Contains(r.Summary, x => x.StartsWith("First to fail:"));
        Assert.Contains(order, x => (double)x[3]! > 0);
    }

    private static bool Fails(StormNetworkResult res, string el, string mode) => mode switch
    {
        "Floods" => res.Structure(el)!.HglClearance < 0,
        "HGL criterion" => res.Structure(el)!.HglClearance < res.Network.Criteria.HglBelowRim,
        "Surcharged" => res.Pipe(el)!.Pipe.Discharge > res.Pipe(el)!.FullFlowCapacity,
        "Velocity above maximum" => res.Pipe(el)! is var p && (double.IsFinite(p.NormalVelocity) ? p.NormalVelocity : p.FullFlowVelocity * p.Pipe.Discharge / p.FullFlowCapacity) > res.Network.Criteria.MaxVelocity,
        _ => throw new InvalidOperationException(mode),
    };

    [Fact]
    public void ExistingFailures_AreListedFirst_NotCountedAsNew()
    {
        var r = Run("""
            [Projections]
            Scenario	Rainfall change (%)
            Today	0
            Plus 5	5
            """);
        Assert.Contains("already has", r.Summary[0]);
        var existing = r.Tables[1].Rows.Where(x => (string)x[4]! == "already in the design").ToList();
        Assert.Contains(existing, x => (string)x[1]! == "CB-4" && (string)x[2]! == "Floods");
        Assert.Equal("OK", r.Tables[0].Rows.Single(x => (string)x[0]! == "Today")[9]);
    }

    [Fact]
    public void AllowedSurcharge_IsNotAFailure()
    {
        var net = Clean() with { Criteria = NetworkCriteria.RapidCity2012 with { AllowSurcharge = true, MaxVelocity = null } };
        var r = Run("""
            search up to = 30
            [Projections]
            Scenario	Rainfall change (%)
            Big	30
            """, net);
        var row = r.Tables[0].Rows.Single();
        Assert.True((int)row[7]! > 0);
        Assert.Contains("meets its criteria", r.Summary[0]);
        Assert.StartsWith("OK", (string)row[9]!);   // the design already surcharges, so it is neither new nor a failure
        Assert.Contains(r.Tables[1].Rows, x => ((string)x[2]!).Contains("allowed"));
    }

    [Fact]
    public void BadInput_IsReported()
    {
        var bad = Run("""
            [Projections]
            Scenario	Rainfall change (%)
            Future	lots
            """);
        Assert.Contains(bad.Messages, m => m.Code == "E-CS-004");
        var none = Run("network = Elm St");
        Assert.Contains(none.Messages, m => m.Code == "E-CS-001");
    }
}
