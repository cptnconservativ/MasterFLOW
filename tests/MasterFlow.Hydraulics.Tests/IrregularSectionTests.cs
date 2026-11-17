using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.Hydraulics.Tests;

/// <summary>
/// Irregular (station/elevation) sections. References: exact closed forms for shapes that are also
/// parametric sections, and validation/hand_calcs_irregular.py (independent fine-grid integration of
/// depth over station and of wetted ground length). The script's grid integration is accurate to about
/// 1e-9 relative, so script-derived values are compared at 1e-6 (fixed before the first run).
/// </summary>
public class IrregularSectionTests
{
    private static void Near(double expected, double actual, double rel, string what)
    {
        double err = Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);
        Assert.True(err <= rel, $"{what}: expected {expected:R}, got {actual:R} (relative error {err:E3} > {rel:E1})");
    }

    private static IrregularSection Section(CompositeRoughnessMethod m, params (double X, double Z, double? N)[] pts) =>
        new([.. pts.Select(p => new StationPoint(p.X, p.Z, p.N))], m);

    private static IrregularSection Section(params (double X, double Z)[] pts) =>
        new([.. pts.Select(p => new StationPoint(p.X, p.Z))]);

    private static UniformFlowResult Solve(ChannelSection s, SolveFor f, double n = 0.035, double S = 0.001, double q = 0, double y = 0) =>
        UniformFlowCalculator.Solve(new UniformFlowInput { Section = s, SolveFor = f, ManningN = n, Slope = S, Discharge = q, Depth = y });

    private static readonly IrregularSection FlowCalcExample1 = Section((0, 5), (10, 0), (20, 0), (30, 5));

    // Compound channel: overbanks n = 0.06, main channel n = 0.035, banks at 5 ft.
    private static IrregularSection Compound(CompositeRoughnessMethod m = CompositeRoughnessMethod.Conveyance) => Section(m,
        (0, 8, .06), (40, 5, .06), (50, 5, .035), (55, 0, .035), (65, 0, .035), (70, 5, .035), (80, 5, .06), (120, 8, .06));

    private static IrregularSection CompoundSingleN() => Section(
        (0, 8), (40, 5), (50, 5), (55, 0), (65, 0), (70, 5), (80, 5), (120, 8));

    [Fact] // I1: the points describe the trapezoid b = 10, z = 2; must equal the parametric section exactly
    public void TrapezoidAsPoints_MatchesParametricTrapezoid_AndFlowCalcExample()
    {
        var pts = Solve(FlowCalcExample1, SolveFor.Discharge, n: 0.013, S: 0.01, y: 1.5);
        var trap = Solve(new TrapezoidalSection(10, 2, 2), SolveFor.Discharge, n: 0.013, S: 0.01, y: 1.5);
        Assert.True(pts.Succeeded);
        Near(trap.Discharge, pts.Discharge, 1e-12, "Q");
        Near(247.08514222, pts.Discharge, 1e-8, "Q vs hand calc");
        Near(19.5, pts.Normal.Area, 1e-14, "A");
        Near(16.0, pts.Normal.TopWidth, 1e-14, "T");
        Near(trap.Critical.Depth, pts.Critical.Depth, 1e-10, "yc");
        Near(trap.CriticalSlope, pts.CriticalSlope, 1e-8, "Sc");
        Assert.Equal(1.5, pts.Irregular!.WaterSurfaceElevation, 12);
        Assert.Equal(0.013, pts.Irregular.CompositeN, 12);
        Assert.Equal(1.0, pts.Irregular.EnergyCoefficient);
    }

    [Fact] // vertical walls from repeated stations behave as a rectangle
    public void VerticalWalls_MatchRectangle()
    {
        var pts = Solve(Section((0, 4), (0, 0), (8, 0), (8, 4)), SolveFor.NormalDepth, n: 0.015, S: 0.002, q: 120);
        var rect = Solve(new RectangularSection(8), SolveFor.NormalDepth, n: 0.015, S: 0.002, q: 120);
        Assert.True(pts.Succeeded, string.Join("; ", pts.Messages));
        Near(rect.Normal.Depth, pts.Normal.Depth, 1e-10, "yn");
        Near(rect.Normal.WettedPerimeter, pts.Normal.WettedPerimeter, 1e-10, "P");
        Near(rect.Critical.Depth, pts.Critical.Depth, 1e-10, "yc");
    }

    [Fact] // I2: conveyance subdivision at y = 6 ft (1 ft over the banks)
    public void Compound_ConveyanceSubdivision_MatchesHandCalc()
    {
        var r = Solve(Compound(), SolveFor.Discharge, y: 6.0);
        Assert.True(r.Succeeded);
        Near(128.33333333, r.Normal.Area, 1e-8, "A");
        Near(70.88369712, r.Normal.WettedPerimeter, 1e-8, "P");
        Near(311.83231363, r.Discharge, 1e-6, "Q");
        var ir = r.Irregular!;
        Assert.Equal(3, ir.Subareas.Count);
        Near(r.Discharge, ir.Subareas.Sum(s => s.Discharge), 1e-12, "subarea flows add up");
        Assert.True(ir.EnergyCoefficient > 1, "α > 1 for an overbank section");
        // Effective n = k A R^(2/3) / K. Subdivision lets the deep main channel carry most of the flow, so the
        // effective n can fall below every subarea n (0.0287 here); that is expected, not an error.
        Near(1.486 * r.Normal.Area * Math.Pow(r.Normal.HydraulicRadius, 2.0 / 3.0) / r.Conveyance, ir.CompositeN, 1e-14, "effective n");
        Assert.InRange(ir.CompositeN, 0.028, 0.029);
    }

    [Fact] // I2: normal depth above the highest point (both end walls extended)
    public void Compound_NormalDepth_MatchesHandCalc_WithWallWarning()
    {
        var r = Solve(Compound(), SolveFor.NormalDepth, q: 1500);
        Assert.True(r.Succeeded, string.Join("; ", r.Messages));
        Near(9.67513472, r.Normal.Depth, 1e-6, "yn");
        Assert.True(r.Irregular!.LeftWallExtended && r.Irregular.RightWallExtended);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.WallExtended);
    }

    [Fact] // I3: Horton–Einstein equal-velocity composite n
    public void Compound_EqualVelocity_MatchesHandCalc()
    {
        var r = Solve(Compound(CompositeRoughnessMethod.EqualVelocity), SolveFor.Discharge, y: 6.0);
        Near(183.66530498, r.Discharge, 1e-6, "Q");
        Assert.Empty(r.Irregular!.Subareas);
    }

    [Fact] // I4: single n — Q drops when the flat overbanks wet at 5 ft, so two depths carry 180 cfs
    public void SingleN_Compound_ReportsAllDepths_AndHighestGoverns()
    {
        var below = Solve(CompoundSingleN(), SolveFor.Discharge, y: 4.999);
        var above = Solve(CompoundSingleN(), SolveFor.Discharge, y: 5.001);
        Assert.True(above.Discharge < below.Discharge, "Q(y) steps down where the overbanks wet");

        var r = Solve(CompoundSingleN(), SolveFor.NormalDepth, q: 180);
        Assert.True(r.Succeeded, string.Join("; ", r.Messages));
        var roots = r.Irregular!.NormalDepthRoots;
        Assert.Equal(2, roots.Count);
        Near(4.54249328, roots[0], 1e-6, "lower depth");
        Near(5.41924191, roots[1], 1e-6, "upper depth");
        Assert.Equal(roots[1], r.Normal.Depth);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.MultipleNormalDepths);
        Assert.Contains(r.Trace, t => t.Contains("not a solution"));
    }

    [Fact] // I5: two pools separated by a ridge above the water surface
    public void TwoPools_BothCount_WithWarning()
    {
        var pool = Section((0, 3), (5, 0), (10, 2), (15, 0.5), (20, 3));
        var r = Solve(pool, SolveFor.Discharge, n: 0.03, S: 0.002, y: 1.5);
        Near(7.35416667, r.Normal.Area, 1e-8, "A");
        Near(12.6705197, r.Normal.WettedPerimeter, 1e-8, "P");
        Near(11.33544864, r.Discharge, 1e-6, "Q");
        Assert.Equal(2, r.Irregular!.WettedRegions);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.SeparateWettedAreas);
        var one = Solve(pool, SolveFor.Discharge, n: 0.03, S: 0.002, y: 2.5);
        Assert.Equal(1, one.Irregular!.WettedRegions);
    }

    [Fact] // I6: water above the left end: vertical wall assumed, wetted perimeter includes it
    public void WaterAboveEnd_ExtendsWall()
    {
        var r = Solve(Section((0, 2), (4, 0), (8, 0), (12, 3)), SolveFor.Discharge, n: 0.025, S: 0.004, y: 2.5);
        Near(20.16666667, r.Normal.Area, 1e-8, "A");
        Near(13.13880262, r.Normal.WettedPerimeter, 1e-8, "P");
        Near(100.87769199, r.Discharge, 1e-6, "Q");
        Assert.True(r.Irregular!.LeftWallExtended);
        Assert.False(r.Irregular.RightWallExtended);
    }

    [Theory] // round trips through every unknown (single n)
    [InlineData(0.8)]
    [InlineData(3.0)]
    [InlineData(5.6)]
    [InlineData(9.0)]
    public void RoundTrips(double y)
    {
        var sec = CompoundSingleN();
        var q = Solve(sec, SolveFor.Discharge, y: y);
        Assert.True(q.Succeeded);
        var s = Solve(sec, SolveFor.Slope, q: q.Discharge, y: y);
        Near(0.001, s.Slope, 1e-10, "S");
        var n = Solve(sec, SolveFor.Roughness, S: 0.001, q: q.Discharge, y: y);
        Near(0.035, n.ManningN, 1e-10, "n");
        var yn = Solve(sec, SolveFor.NormalDepth, q: q.Discharge);
        Assert.Contains(yn.Irregular!.NormalDepthRoots, r => Math.Abs(r - y) < 1e-8);
    }

    [Fact] // geometry consistency: T = dA/dy away from breakpoints
    public void TopWidth_IsDerivativeOfArea()
    {
        var g = new IrregularGeometry(Compound());
        foreach (double y in new[] { 0.3, 2.7, 4.6, 5.5, 6.9, 10.0 })
        {
            double h = 1e-6;
            Near((g.Area(y + h) - g.Area(y - h)) / (2 * h), g.TopWidth(y), 1e-6, $"T at {y}");
        }
    }

    [Fact] // a critical-depth candidate must be a specific-energy minimum; the lowest-energy one governs
    public void CriticalDepth_IsLowestSpecificEnergy()
    {
        var r = Solve(CompoundSingleN(), SolveFor.Discharge, y: 6);
        Assert.True(r.Succeeded);
        double zTarget = r.Discharge / Math.Sqrt(32.174);
        var g = new IrregularGeometry(CompoundSingleN());
        Near(zTarget, UniformFlowCalculator.CriticalSectionFactor(g, r.Critical.Depth), 1e-8, "Z(yc) = Q/√g");
        foreach (double y in Enumerable.Range(1, 200).Select(i => i * 0.05))
        {
            double a = g.Area(y), e = y + r.Discharge * r.Discharge / (2 * 32.174 * a * a);
            Assert.True(e >= r.Critical.SpecificEnergy - 1e-9, $"E({y}) = {e} below Ec = {r.Critical.SpecificEnergy}");
        }
    }

    [Fact]
    public void Invalid_Sections_AreRejected()
    {
        Assert.Contains(Solve(Section((0, 1), (1, 0)), SolveFor.Discharge, y: 0.5).Messages, m => m.Code == MessageCodes.GeometryInvalid);
        var reversed = Solve(Section((0, 2), (5, 0), (4, 0), (10, 2)), SolveFor.Discharge, y: 1);
        Assert.False(reversed.Succeeded);
        Assert.Contains(reversed.Messages, m => m.Code == MessageCodes.GeometryInvalid && m.Text.Contains("Station decreases"));
        Assert.False(Solve(Section((0, 1), (5, 1), (10, 1)), SolveFor.Discharge, y: 1).Succeeded);
        Assert.False(Solve(Section((0, 1), (5, double.NaN), (10, 1)), SolveFor.Discharge, y: 1).Succeeded);
        var badN = Section(CompositeRoughnessMethod.Conveyance, (0, 2, 0.03), (5, 0, -1), (10, 2, null));
        Assert.Contains(Solve(badN, SolveFor.Discharge, y: 1).Messages, m => m.Code == MessageCodes.RoughnessNotPositive);
        var nSolve = Solve(Compound(), SolveFor.Roughness, q: 300, y: 6);
        Assert.Contains(nSolve.Messages, m => m.Code == MessageCodes.RoughnessSolveNeedsSingleN);
    }

    [Fact] // duplicate consecutive points are harmless
    public void DuplicatePoints_AreIgnored()
    {
        var a = Solve(FlowCalcExample1, SolveFor.Discharge, n: 0.013, S: 0.01, y: 1.5);
        var b = Solve(Section((0, 5), (10, 0), (10, 0), (20, 0), (30, 5), (30, 5)), SolveFor.Discharge, n: 0.013, S: 0.01, y: 1.5);
        Assert.Equal(a.Discharge, b.Discharge, 12);
    }
}
