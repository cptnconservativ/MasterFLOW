using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.Hydraulics.Tests;

/// <summary>
/// Elliptical conduits. References: ASTM C507-22 Fig. 5 tabulated areas; validation/hand_calcs_elliptical.py
/// (independent fine-polyline geometry); exact ellipse area πab and Ramanujan's second perimeter formula.
/// </summary>
public class EllipticalPipeTests
{
    private static void Near(double expected, double actual, double rel, string what)
    {
        double err = Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);
        Assert.True(err <= rel, $"{what}: expected {expected:R}, got {actual:R} (relative error {err:E3} > {rel:E1})");
    }

    public static TheoryData<int> AllSizes() => [.. EllipticalPipeCatalog.Sizes.Select(s => s.EquivalentRound)];

    [Theory] // every standard size: four-centred shape reproduces the tabulated waterway area
    [MemberData(nameof(AllSizes))]
    public void Standard_Area_MatchesAstmTable(int size)
    {
        var s = EllipticalPipeCatalog.Sizes.Single(x => x.EquivalentRound == size);
        var g = SymmetricArcGeometry.From(EllipticalPipeCatalog.Create(s, false, UnitSystem.USCustomary));
        // Table radii are rounded to 1/4 in and areas to 3 significant figures; R2 is derived from
        // tangency. Largest observed difference 0.54 % (18 in); tolerance fixed at 0.6 % beforehand.
        Near(s.TabulatedAreaFt2, g.Area(g.MaxDepth), 0.006, $"area of {size}-in");
    }

    [Fact] // HE 36 vs independent polyline integration
    public void He36_Geometry_And_Discharge_MatchIndependentCalc()
    {
        var sec = EllipticalPipeCatalog.Create(EllipticalPipeCatalog.Sizes.Single(x => x.EquivalentRound == 36), false, UnitSystem.USCustomary);
        var g = SymmetricArcGeometry.FourCentred(sec.Rise, sec.Span, sec.EndRadius!.Value, out double big);
        Near(39.253676 / 12, big, 1e-7, "derived R2");
        Near(7.36010652, g.Area(g.MaxDepth), 1e-8, "full area");
        Near(10.00888436, g.WettedPerimeter(g.MaxDepth), 1e-8, "full perimeter");
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = sec, SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.005, Depth = 1.2 });
        Assert.True(r.Succeeded);
        Near(24.30662119, r.Discharge, 1e-8, "Q at 1.2 ft");
        var yn = UniformFlowCalculator.Solve(new UniformFlowInput { Section = sec, SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.005, Discharge = 20 });
        Near(1.07482756, yn.Normal.Depth, 1e-7, "yn for 20 cfs");
    }

    [Fact] // true ellipse closed forms
    public void TrueEllipse_AreaAndPerimeter()
    {
        var g = SymmetricArcGeometry.TrueEllipse(2.0, 3.0);
        Near(Math.PI * 1.5 * 1.0, g.Area(2.0), 1e-14, "full area πab");
        Near(Math.PI * 1.5 / 2, g.Area(1.0), 1e-14, "half area");
        double h = Math.Pow(0.5 / 2.5, 2);
        double ramanujan = Math.PI * 2.5 * (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h)));
        Near(ramanujan, g.WettedPerimeter(2.0), 1e-10, "perimeter (Ramanujan II)");
        Near(3.0, g.TopWidth(1.0), 1e-14, "T at mid-height = span");
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new EllipticalSection(2.0, 3.0), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.005, Depth = 0.8 });
        Near(8.89571643, r.Discharge, 1e-8, "Q at 0.8 ft (polyline hand calc)");
    }

    [Fact] // rotation: VE and HE of the same size have equal full area and perimeter; VE is taller
    public void Vertical_IsRotatedHorizontal()
    {
        var s = EllipticalPipeCatalog.Sizes.Single(x => x.EquivalentRound == 48);
        var he = SymmetricArcGeometry.From(EllipticalPipeCatalog.Create(s, false, UnitSystem.USCustomary));
        var ve = SymmetricArcGeometry.From(EllipticalPipeCatalog.Create(s, true, UnitSystem.USCustomary));
        Near(he.Area(he.MaxDepth), ve.Area(ve.MaxDepth), 1e-12, "area");
        Near(he.WettedPerimeter(he.MaxDepth), ve.WettedPerimeter(ve.MaxDepth), 1e-12, "perimeter");
        Near(60.0 / 12, ve.MaxDepth, 1e-14, "VE rise");
        Near(38.25 / 12, ve.TopWidth(ve.MaxDepth / 2), 1e-12, "VE width at springline");
    }

    [Theory] // T = dA/dy and continuity across arc junctions
    [InlineData(false)]
    [InlineData(true)]
    public void TopWidth_IsDerivative_AndPiecesJoinSmoothly(bool vertical)
    {
        var s = EllipticalPipeCatalog.Sizes.Single(x => x.EquivalentRound == 60);
        var g = SymmetricArcGeometry.From(EllipticalPipeCatalog.Create(s, vertical, UnitSystem.USCustomary));
        for (double t = 0.02; t < 0.99; t += 0.03)
        {
            double y = t * g.MaxDepth, h = 1e-6;
            Near((g.Area(y + h) - g.Area(y - h)) / (2 * h), g.TopWidth(y), 1e-5, $"T at {t:F2} rise");
            Assert.True(g.WettedPerimeter(y + h) > g.WettedPerimeter(y - h));
        }
    }

    [Fact] // dual depths and capacity behave as for circular pipes
    public void DualDepth_And_Capacity()
    {
        var sec = EllipticalPipeCatalog.Create(EllipticalPipeCatalog.Sizes.Single(x => x.EquivalentRound == 36), false, UnitSystem.USCustomary);
        var baseR = UniformFlowCalculator.Solve(new UniformFlowInput { Section = sec, SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.01, Depth = sec.Rise });
        var full = baseR.Conduit!;
        Assert.True(full.MaxDischarge > full.FullFlowDischarge);
        Assert.InRange(full.DepthAtMaxDischarge / sec.Rise, 0.85, 0.99);
        double q = 0.5 * (full.FullFlowDischarge + full.MaxDischarge);
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = sec, SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = q });
        Assert.True(r.Succeeded);
        Assert.True(double.IsFinite(r.Conduit!.AlternateNormalDepth));
        Assert.True(r.Normal.Depth < r.Conduit.DepthAtMaxDischarge && r.Conduit.AlternateNormalDepth > r.Conduit.DepthAtMaxDischarge);
        var over = UniformFlowCalculator.Solve(new UniformFlowInput { Section = sec, SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = full.MaxDischarge * 1.01 });
        Assert.Contains(over.Messages, m => m.Code == MessageCodes.ExceedsConduitCapacity);
    }

    [Theory] // invalid inputs
    [InlineData(0, 3, null)]
    [InlineData(2, -1, null)]
    [InlineData(2, 3, 1.2)]   // end radius ≥ half the smaller dimension
    [InlineData(2, 3, 0.0)]
    public void Invalid(double rise, double span, double? r)
    {
        var res = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new EllipticalSection(rise, span, r), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.01, Depth = 0.5 });
        Assert.False(res.Succeeded);
        Assert.Contains(res.Messages, m => m.Code == MessageCodes.GeometryInvalid);
    }

    [Fact]
    public void DepthAboveRise_IsError()
    {
        var res = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new EllipticalSection(2, 3), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.01, Depth = 2.1 });
        Assert.Contains(res.Messages, m => m.Code == MessageCodes.DepthExceedsRise);
    }
}
