using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Hydraulics.Tests;

/// <summary>
/// Vertical walls with their own n under conveyance subdivision. Before 2026-11-21 such a wall formed a
/// zero-area subarea and its friction was dropped. It now joins the subarea it faces with an
/// equal-velocity composite n for that subarea. Reference: hand calculation below.
/// </summary>
public class WallFrictionTests
{
    // 10-ft wide rectangular channel: concrete walls (n 0.015), gravel bed (n 0.030).
    private static IrregularSection Flume(CompositeRoughnessMethod m) =>
        new([new(0, 5, 0.015), new(0, 0, 0.030), new(10, 0, 0.015), new(10, 5)], m);

    private static UniformFlowResult Q(IrregularSection s, double y) => UniformFlowCalculator.Solve(new UniformFlowInput
    { Section = s, SolveFor = SolveFor.Discharge, ManningN = 0.030, Slope = 0.001, Depth = y, Discharge = 1 });

    private static double HandQ(double y)
    {
        double a = 10 * y, p = 10 + 2 * y;
        double nc = Math.Pow((2 * y * Math.Pow(0.015, 1.5) + 10 * Math.Pow(0.030, 1.5)) / p, 2.0 / 3.0);
        return 1.486 / nc * a * Math.Pow(a / p, 2.0 / 3.0) * Math.Sqrt(0.001);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(2)]
    [InlineData(4)]
    public void HecRas_WallsAddFriction_MatchesHandCalc(double y)
    {
        var r = Q(Flume(CompositeRoughnessMethod.Conveyance), y);
        Assert.True(r.Succeeded, string.Join("; ", r.Messages));
        Assert.Equal(HandQ(y), r.Discharge, 9);
        Assert.Equal(10 + 2 * y, r.Normal.WettedPerimeter, 12);

        // Old behaviour (walls frictionless) would have given this larger flow:
        double frictionless = 1.486 / 0.030 * 10 * y * Math.Pow(y, 2.0 / 3.0) * Math.Sqrt(0.001);
        Assert.True(r.Discharge < frictionless);
    }

    [Fact]
    public void AllThreeMethodsAgree_WhenThereIsOneWettedSubarea()
    {
        double c = Q(Flume(CompositeRoughnessMethod.Conveyance), 2).Discharge;
        Assert.Equal(c, Q(Flume(CompositeRoughnessMethod.EqualVelocity), 2).Discharge, 9);
        Assert.Equal(c, Q(Flume(CompositeRoughnessMethod.PerSegment), 2).Discharge, 9);
    }

    [Fact]
    public void InteriorStep_JoinsTheLowerSide()
    {
        // Bench at 2 ft on the left, 1-ft step down to the main channel; the step's concrete face looks right.
        var s = new IrregularSection(
        [
            new(0, 4, 0.04), new(10, 2, 0.015), new(10, 1, 0.03), new(20, 1, 0.03), new(30, 4), // n applies to the segment that starts at the point
        ], CompositeRoughnessMethod.Conveyance);
        var r = Q(s, 2.5);
        Assert.True(r.Succeeded);
        var subs = r.Irregular!.Subareas;
        Assert.Equal(2, subs.Count);
        var main = subs[1];
        Assert.Equal(10, main.StationLeft, 12); // the wall at station 10 joined the main channel
        // Composite of the 1-ft wall (n 0.015) and the wetted bed and right bank (n 0.030).
        double pBed = main.WettedPerimeter - 1;
        Assert.Equal(Math.Pow((1 * Math.Pow(0.015, 1.5) + pBed * Math.Pow(0.03, 1.5)) / main.WettedPerimeter, 2.0 / 3.0), main.ManningN, 12);
        Assert.Equal(0.04, subs[0].ManningN, 12);   // the bench is unchanged
    }
}
