using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.Hydraulics.Tests;

/// <summary>
/// Circular conduits flowing partly full. References: closed forms (half-full, full), classic
/// constants (Qmax/Qfull = 1.0757 at y/D = 0.9382), and validation/hand_calcs_circular.py.
/// </summary>
public class CircularPipeTests
{
    private static void Near(double expected, double actual, double rel, string what)
    {
        double err = Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);
        Assert.True(err <= rel, $"{what}: expected {expected:R}, got {actual:R} (relative error {err:E3} > {rel:E1})");
    }

    private static UniformFlowResult Solve(double d, SolveFor f, double n, double s, double q = 0, double y = 0)
    {
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new CircularSection(d), SolveFor = f, ManningN = n, Slope = s, Discharge = q, Depth = y });
        Assert.True(r.Succeeded, string.Join("; ", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact] // basic: half full → Q = Qfull / 2 exactly, A = πD²/8, R = D/4, T = D
    public void HalfFull_ClosedForm()
    {
        var r = Solve(2.0, SolveFor.Discharge, 0.013, 0.005, y: 1.0);
        Near(Math.PI * 4 / 8, r.Normal.Area, 1e-14, "A");
        Near(0.5, r.Normal.HydraulicRadius, 1e-14, "R");
        Near(2.0, r.Normal.TopWidth, 1e-14, "T");
        Near(r.Conduit!.FullFlowDischarge / 2, r.Discharge, 1e-13, "Q = Qfull/2");
        Near(7.9982260189, r.Discharge, 1e-10, "Q hand calc");
        Near(1.0067458897, r.Critical.Depth, 1e-9, "yc hand calc");
        Near(50.0, r.Conduit.PercentFull, 1e-14, "% full");
    }

    [Fact] // full-flow closed form and the classic peak constants
    public void FullFlow_And_Peak_Constants()
    {
        var r = Solve(2.0, SolveFor.Discharge, 0.013, 0.005, y: 1.0);
        double qFull = 1.486 / 0.013 * Math.PI * 4 / 4 * Math.Pow(0.5, 2.0 / 3.0) * Math.Sqrt(0.005);
        Near(qFull, r.Conduit!.FullFlowDischarge, 1e-13, "Qfull");
        Near(1.07570613, r.Conduit.MaxDischarge / r.Conduit.FullFlowDischarge, 1e-7, "Qmax/Qfull");
        Near(0.93818122, r.Conduit.DepthAtMaxDischarge / 2.0, 1e-6, "y(Qmax)/D");
        Near(1.25e-3, r.Conduit.FullFlowSlope, 1e-10, "slope to carry Q full = S/4");
    }

    [Fact] // typical storm sewer normal depth
    public void Typical_NormalDepth()
    {
        var r = Solve(1.5, SolveFor.NormalDepth, 0.013, 0.004, q: 4.0);
        Near(0.8392399829, r.Normal.Depth, 1e-9, "yn");
        Near(0.7655222981, r.Critical.Depth, 1e-9, "yc");
        Near(0.8389749092, r.Normal.Froude, 1e-8, "Fr");
        Assert.True(double.IsNaN(r.Conduit!.AlternateNormalDepth));
    }

    [Fact] // Flow Calc Example #3 comparison: D = 36 in, n = 0.013, S = 1 %, Q = 71.37 → ND 32.76 in, dual ND 34.66 in
    public void DualNormalDepth_FlowCalcExample3()
    {
        var r = Solve(3.0, SolveFor.NormalDepth, 0.013, 0.01, q: 71.37);
        Near(2.7295067672, r.Normal.Depth, 1e-9, "lower root (hand calc)");
        Near(2.8886196484, r.Conduit!.AlternateNormalDepth, 1e-9, "upper root (hand calc)");
        // Flow Calc comparison (not authority). Q = 71.37 is Flow Calc's rounded output, and near the crown
        // dQ/dy is small, so the ±0.005 cfs rounding plus the k = 1.486 vs 1.48592 difference moves the
        // depth by up to ~0.01 in. MasterFlow gives 32.754 in vs Flow Calc 32.76 in; 34.663 vs 34.66 in.
        Assert.InRange(r.Normal.Depth * 12, 32.76 - 0.015, 32.76 + 0.015);
        Assert.InRange(r.Conduit.AlternateNormalDepth * 12, 34.66 - 0.015, 34.66 + 0.015);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.TwoNormalDepths);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.NearCrown);
        Near(71.7478679405, r.Conduit.MaxDischarge, 1e-9, "Qmax");
    }

    [Fact] // Q from y on the upper branch reports the lower alternate depth
    public void DischargeFromDepth_UpperBranch_ReportsAlternate()
    {
        var r = Solve(3.0, SolveFor.Discharge, 0.013, 0.01, y: 2.8886196484);
        Near(71.37, r.Discharge, 1e-9, "Q");
        Near(2.7295067672, r.Conduit!.AlternateNormalDepth, 1e-8, "alternate");
    }

    [Fact] // boundary: nearly empty
    public void NearlyEmpty_Converges()
    {
        var r = Solve(1.5, SolveFor.NormalDepth, 0.013, 0.004, q: 1e-4);
        Near(0.0051717821, r.Normal.Depth, 1e-7, "yn (reference printed to 8 significant figures)");
        Near(0.0036371374, r.Critical.Depth, 1e-7, "yc (reference printed to 8 significant figures)");
    }

    [Fact] // boundary: just full
    public void JustFull_FroudeUndefined()
    {
        var r = Solve(2.0, SolveFor.Discharge, 0.013, 0.005, y: 2.0);
        Near(r.Conduit!.FullFlowDischarge, r.Discharge, 1e-13, "Q = Qfull");
        Assert.True(double.IsNaN(r.Normal.Froude));
        Assert.Equal(FlowRegime.Undefined, r.Regime);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.FlowingFull);
        Assert.Equal(100.0, r.Conduit.PercentFull);
    }

    [Fact] // supercritical steep pipe
    public void Steep_Supercritical()
    {
        var r = Solve(0.6, SolveFor.NormalDepth, 0.012, 0.05, q: 1.0);
        Near(0.2830441087, r.Normal.Depth, 1e-9, "yn");
        Near(0.4849873494, r.Critical.Depth, 1e-9, "yc");
        Assert.Equal(FlowRegime.Supercritical, r.Regime);
    }

    [Fact] // invalid: Q above Qmax
    public void AboveCapacity_IsError()
    {
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new CircularSection(3.0), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = 72.0 });
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.ExceedsConduitCapacity);
        Assert.True(double.IsNaN(r.Discharge));
    }

    [Theory] // invalid geometry / depth
    [InlineData(0.0, 1.0)]
    [InlineData(-2.0, 1.0)]
    [InlineData(2.0, 2.5)]
    public void Invalid_DiameterOrDepth(double d, double y)
    {
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new CircularSection(d), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.01, Depth = y });
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code is MessageCodes.GeometryInvalid or MessageCodes.DepthExceedsRise);
    }

    [Theory] // round trips through every solve mode, both below and above full-flow Q
    [InlineData(0.6)]
    [InlineData(1.2)]
    [InlineData(1.85)]
    public void RoundTrips(double y)
    {
        var baseR = Solve(2.0, SolveFor.Discharge, 0.015, 0.003, y: y);
        var sR = Solve(2.0, SolveFor.Slope, 0.015, 0, q: baseR.Discharge, y: y);
        var nR = Solve(2.0, SolveFor.Roughness, 0, 0.003, q: baseR.Discharge, y: y);
        Near(0.003, sR.Slope, 1e-10, "S");
        Near(0.015, nR.ManningN, 1e-10, "n");
        var yR = Solve(2.0, SolveFor.NormalDepth, 0.015, 0.003, q: baseR.Discharge);
        Near(y, y <= yR.Conduit!.DepthAtMaxDischarge ? yR.Normal.Depth : yR.Conduit.AlternateNormalDepth, 1e-9, "y");
    }

    [Fact] // geometry consistency: T = dA/dy numerically
    public void TopWidth_IsDerivativeOfArea()
    {
        var g = new CircularGeometry(2.0);
        foreach (double y in new[] { 0.01, 0.5, 1.0, 1.7, 1.99 })
        {
            double h = 1e-6;
            Near((g.Area(y + h) - g.Area(y - h)) / (2 * h), g.TopWidth(y), 1e-6, $"T at {y}");
        }
    }
}
