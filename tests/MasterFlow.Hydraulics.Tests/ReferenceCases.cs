using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Hydraulics.Tests;

/// <summary>
/// Validation against independent reference values. Expected numbers come from
/// validation/hand_calcs.py (separate Python implementation: closed forms and 300-step bisection),
/// from closed-form analytical solutions, or from a published textbook example. Tolerances are set
/// before comparison and documented in docs/VALIDATION_PLAN.md:
///   * engine vs independent hand calculation: relative 1e-8 (both are double precision);
///   * engine vs published example rounded to 0.01: absolute half of the last printed digit.
/// </summary>
public class ReferenceCases
{
    private const double Rel = 1e-8;

    private static void Near(double expected, double actual, double rel = Rel, string? what = null)
    {
        double err = Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);
        Assert.True(err <= rel, $"{what}: expected {expected:R}, got {actual:R} (relative error {err:E3} > {rel:E1})");
    }

    private static UniformFlowResult Solve(ChannelSection s, SolveFor f, double n, double slope, double q = 0, double y = 0, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = s, SolveFor = f, ManningN = n, Slope = slope, Discharge = q, Depth = y, Units = u });
        Assert.True(r.Succeeded, string.Join("; ", r.Messages.Select(m => m.Text)));
        return r;
    }

    // ---------------- Rectangular

    [Fact] // R1 basic: discharge from depth
    public void Rectangular_Discharge_MatchesHandCalc()
    {
        var r = Solve(new RectangularSection(10), SolveFor.Discharge, 0.013, 0.001, y: 2.0);
        Near(91.7008636875, r.Discharge, 1e-10, "Q");
        Near(20.0, r.Normal.Area, 1e-12, "A");
        Near(14.0, r.Normal.WettedPerimeter, 1e-12, "P");
        Near(1.4285714286, r.Normal.HydraulicRadius, 1e-10, "R");
        Near(4.5850431844, r.Normal.Velocity, 1e-10, "V");
        Near(0.5715785256, r.Normal.Froude, 1e-9, "Fr");
        Near(2.3267020110, r.Normal.SpecificEnergy, 1e-10, "E");
        Near(1.3774650811, r.Critical.Depth, Rel, "yc");
        Near(3.0612447774e-03, r.CriticalSlope, 1e-8, "Sc");
        Assert.Equal(FlowRegime.Subcritical, r.Regime);
    }

    [Fact] // R2 typical: normal depth; critical depth vs closed form yc = (q²/g)^(1/3)
    public void Rectangular_NormalDepth_And_ClosedFormCriticalDepth()
    {
        var r = Solve(new RectangularSection(10), SolveFor.NormalDepth, 0.013, 0.001, q: 50);
        Near(1.3355577378, r.Normal.Depth, 1e-9, "yn");
        double ycExact = Math.Cbrt(5.0 * 5.0 / 32.174);
        Near(ycExact, r.Critical.Depth, 1e-10, "yc closed form");
        // At critical depth, velocity head = D/2 and Fr = 1 (analytical identity).
        Near(1.0, r.Critical.Froude, 1e-9, "Fr at yc");
        Near(r.Critical.HydraulicDepth / 2, r.Critical.VelocityHead, 1e-9, "hv = D/2");
    }

    [Fact] // S1 SI units: k = 1.0, g = 9.80665
    public void Rectangular_SI_Discharge()
    {
        var r = Solve(new RectangularSection(3), SolveFor.Discharge, 0.015, 0.001, y: 1.0, u: UnitSystem.SI);
        Near(4.4991536946, r.Discharge, 1e-10, "Q");
        Near(0.6121147215, r.Critical.Depth, 1e-9, "yc");
        Near(0.4789048516, r.Normal.Froude, 1e-9, "Fr");
    }

    // ---------------- Trapezoidal

    [Fact] // Published: Chow (1959) Example 6-2: b = 20 ft, z = 2, S = 0.0016, n = 0.025, Q = 400 cfs → yn ≈ 3.36 ft
    public void Trapezoidal_Chow_Example_6_2()
    {
        var r = Solve(new TrapezoidalSection(20, 2, 2), SolveFor.NormalDepth, 0.025, 0.0016, q: 400);
        Assert.InRange(r.Normal.Depth, 3.355, 3.365);
        Near(3.3609678403, r.Normal.Depth, 1e-9, "yn vs hand calc");
        Near(2.1482332137, r.Critical.Depth, 1e-9, "yc");
    }

    [Fact] // T1: the case observed in the Flow Calc example project (comparison point only)
    public void Trapezoidal_FlowCalcExample_Discharge()
    {
        var r = Solve(new TrapezoidalSection(10, 2, 2), SolveFor.Discharge, 0.013, 0.01, y: 1.5);
        Near(247.0851422290, r.Discharge, 1e-10, "Q");
        Near(19.5, r.Normal.Area, 1e-12, "A");
        Near(16.7082039325, r.Normal.WettedPerimeter, 1e-10, "P");
        Near(2.2745285019, r.Critical.Depth, 1e-9, "yc");
        Near(2.0234951913, r.Normal.Froude, 1e-9, "Fr");
        Assert.Equal(FlowRegime.Supercritical, r.Regime);
        // Flow Calc displayed 247.07 cfs. With k = 1.486 we get 247.085; the difference (0.006 %) equals
        // the ratio 1.48592/1.486, i.e. Flow Calc appears to use the exact metric conversion of k.
        // This is an inference from one observation; see docs/VALIDATION_RESULTS.md.
        Near(247.07, r.Discharge * 1.4859185 / 1.486, 2e-5, "Flow Calc display with exact k");
    }

    [Fact] // T3: asymmetric side slopes
    public void Trapezoidal_Asymmetric_NormalDepth()
    {
        var r = Solve(new TrapezoidalSection(4, 3, 1.5), SolveFor.NormalDepth, 0.030, 0.005, q: 120);
        Near(2.6173254227, r.Normal.Depth, 1e-9, "yn");
        Near(15.7779644020, r.Normal.TopWidth, 1e-9, "T");
        Near(2.0884697640, r.Critical.Depth, 1e-9, "yc");
    }

    [Fact] // S2: SI normal depth
    public void Trapezoidal_SI_NormalDepth()
    {
        var r = Solve(new TrapezoidalSection(2, 1.5, 1.5), SolveFor.NormalDepth, 0.022, 0.0005, q: 8, u: UnitSystem.SI);
        Near(1.7414138067, r.Normal.Depth, 1e-9, "yn");
        Near(0.9282570949, r.Critical.Depth, 1e-9, "yc");
    }

    // ---------------- Triangular

    [Fact] // V1: closed-form normal and critical depth for a symmetric V
    public void Triangular_ClosedForms()
    {
        double z = 3, q = 20, n = 0.015, s = 0.002, k = 1.486, g = 32.174;
        double ynExact = Math.Pow(q * n / (k * z * Math.Sqrt(s)) * Math.Pow(2 * Math.Sqrt(1 + z * z) / z, 2.0 / 3.0), 3.0 / 8.0);
        double ycExact = Math.Pow(2 * q * q / (g * z * z), 0.2);
        var r = Solve(new TriangularSection(z, z), SolveFor.NormalDepth, n, s, q: q);
        Near(ynExact, r.Normal.Depth, 1e-10, "yn closed form");
        Near(ycExact, r.Critical.Depth, 1e-10, "yc closed form");
        Near(1.4045170237, r.Normal.Depth, 1e-9, "yn hand calc");
    }

    [Fact] // V2: asymmetric V, supercritical
    public void Triangular_Asymmetric_Supercritical()
    {
        var r = Solve(new TriangularSection(4, 1), SolveFor.NormalDepth, 0.016, 0.02, q: 5);
        Near(0.6023112175, r.Normal.Depth, 1e-9, "yn");
        Near(0.7570367906, r.Critical.Depth, 1e-9, "yc");
        Near(1.7710877772, r.Normal.Froude, 1e-9, "Fr");
        Assert.Equal(FlowRegime.Supercritical, r.Regime);
    }

    // ---------------- Round trips between solve modes (internal consistency)

    [Theory]
    [InlineData(SolveFor.Slope)]
    [InlineData(SolveFor.Roughness)]
    [InlineData(SolveFor.NormalDepth)]
    public void RoundTrip_FromDischargeCase(SolveFor mode)
    {
        var section = new TrapezoidalSection(6, 2.5, 1.0);
        var baseCase = Solve(section, SolveFor.Discharge, 0.022, 0.0035, y: 2.2);
        var back = Solve(section, mode, 0.022, 0.0035, q: baseCase.Discharge, y: 2.2);
        Near(0.022, back.ManningN, 1e-10, "n");
        Near(0.0035, back.Slope, 1e-10, "S");
        Near(2.2, back.Normal.Depth, 1e-10, "y");
    }

    // ---------------- Boundary / convergence

    [Fact] // B1: nearly empty channel (Q = 1e-4 cfs)
    public void Boundary_TinyDischarge_Converges()
    {
        var r = Solve(new RectangularSection(10), SolveFor.NormalDepth, 0.013, 0.001, q: 1e-4);
        // Hand-calc output is printed to 10 decimal places (7 significant figures here), so compare
        // normal depth at 1e-6; critical depth is checked against the exact closed form instead.
        Near(0.0004625629, r.Normal.Depth, 1e-6, "yn");
        Near(Math.Cbrt(1e-5 * 1e-5 / 32.174), r.Critical.Depth, 1e-10, "yc closed form");
    }

    [Fact] // B2: very large discharge requires repeated bracket expansion
    public void Convergence_HugeDischarge_BracketExpands()
    {
        var r = Solve(new TrapezoidalSection(50, 3, 3), SolveFor.NormalDepth, 0.035, 0.0002, q: 1e5);
        Near(64.6222522605, r.Normal.Depth, 1e-9, "yn");
        Near(29.7423036247, r.Critical.Depth, 1e-9, "yc");
    }

    [Fact] // B3: steep slope gives a warning but a valid result
    public void Boundary_SteepSlope_Warns()
    {
        var r = Solve(new RectangularSection(6), SolveFor.NormalDepth, 0.013, 0.20, q: 300);
        Near(1.1203475472, r.Normal.Depth, 1e-9, "yn");
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.SlopeSteep);
    }
}
