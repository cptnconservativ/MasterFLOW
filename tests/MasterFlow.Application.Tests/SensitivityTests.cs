using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tests;

/// <summary>
/// H2 sensitivity. Expected changes follow from Manning's equation alone (Q ∝ S^½ / n at fixed depth), not
/// from the engine: n × f → Q / f; S × f → Q × √f; at fixed Q and y, S ∝ n² and n ∝ S^½. Tolerance 1e-6 %.
/// </summary>
public class SensitivityTests
{
    private static readonly UniformFlowInput Q = new()
    {
        Section = new TrapezoidalSection(10, 2, 2), ManningN = 0.013, Slope = 0.01, Depth = 1.5, SolveFor = SolveFor.Discharge,
    };

    [Fact]
    public void SolveForQ_ChangesFollowManning()
    {
        var t = SensitivityAnalysis.Build(Q);
        Assert.Equal(9, t.Rows.Count);
        Assert.Equal("Q (cfs)", t.UnknownLabel);
        Assert.Equal(247.0851422290, t.Base!.Discharge, 8);   // hand calc T1, as in the rating table tests
        foreach (var r in t.Rows.Where(r => r.Parameter == "n"))
            Assert.Equal(100 * (1 / r.Factor - 1), r.UnknownChangePercent, 6);
        foreach (var r in t.Rows.Where(r => r.Parameter == "S"))
            Assert.Equal(100 * (Math.Sqrt(r.Factor) - 1), r.UnknownChangePercent, 6);
        Assert.All(t.Rows, r => Assert.Equal(1.5, r.Depth, 9));
        Assert.Equal(["Base", "n −20 %", "n −10 %", "n +10 %", "n +20 %", "S −50 %", "S −25 %", "S +25 %", "S +50 %"], t.Rows.Select(r => r.Case));
    }

    [Fact]
    public void SolveForSlope_DoesNotVarySlope_AndScalesWithNSquared()
    {
        var input = Q with { SolveFor = SolveFor.Slope, Discharge = 200 };
        var t = SensitivityAnalysis.Build(input);
        Assert.Equal(5, t.Rows.Count);
        Assert.Contains(t.Notes, n => n.Contains("Slope is the unknown"));
        foreach (var r in t.Rows.Where(r => r.Parameter == "n"))
            Assert.Equal(100 * (r.Factor * r.Factor - 1), r.UnknownChangePercent, 6);
    }

    [Fact]
    public void SolveForN_ScalesWithRootSlope()
    {
        var t = SensitivityAnalysis.Build(Q with { SolveFor = SolveFor.Roughness, Discharge = 200 });
        Assert.Equal(5, t.Rows.Count);
        foreach (var r in t.Rows.Where(r => r.Parameter == "S"))
            Assert.Equal(100 * (Math.Sqrt(r.Factor) - 1), r.UnknownChangePercent, 6);
    }

    [Fact]
    public void SolveForDepth_DepthRisesWithN_AndFailedCaseIsReportedNotInterpolated()
    {
        // 18 in. pipe at 95 % of its maximum capacity: n + 10 % and + 20 % (and the flatter slopes) cannot carry Q.
        var pipe = new UniformFlowInput { Section = new CircularSection(1.5), ManningN = 0.013, Slope = 0.01, SolveFor = SolveFor.Discharge, Depth = 0.938 * 1.5 };
        double qmax = UniformFlowCalculator.Solve(pipe).Discharge;
        var t = SensitivityAnalysis.Build(pipe with { SolveFor = SolveFor.NormalDepth, Discharge = 0.95 * qmax });
        Assert.True(t.Base!.Succeeded);
        var lower = t.Rows.Single(r => r.Case == "n −10 %");
        Assert.True(lower.Depth < t.Base.Depth && lower.UnknownChangePercent < 0);
        var fail = t.Rows.Single(r => r.Case == "n +20 %");
        Assert.False(fail.Succeeded);
        Assert.True(double.IsNaN(fail.Depth));
        Assert.False(string.IsNullOrWhiteSpace(fail.Error));
        Assert.Contains("failed", SensitivityAnalysis.ToDelimited(t, UnitConstants.For(UnitSystem.USCustomary)));
    }

    [Fact]
    public void PointRoughness_IsScaledToo()
    {
        var pts = new List<StationPoint> { new(0, 5, 0.035), new(10, 0, 0.035), new(20, 0, 0.035), new(30, 5) };
        var input = new UniformFlowInput { Section = new IrregularSection(pts), ManningN = 0.035, Slope = 0.002, Depth = 2, SolveFor = SolveFor.Discharge };
        var t = SensitivityAnalysis.Build(input);
        Assert.Equal(100 * (1 / 1.2 - 1), t.Rows.Single(r => r.Case == "n +20 %").UnknownChangePercent, 6);
        Assert.Contains(t.Notes, n => n.Contains("per-point n"));
    }
}
