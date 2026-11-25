using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Hydraulics.Tests;

/// <summary>
/// Per-segment conveyance. Reference: independent hand calculation
/// (validation/hand_calcs_per_segment.py) on sample 6 (creek with overbanks), each segment its own
/// subarea: yn = 6.6943 ft, A = 181.048 ft², effective n = 0.02723, 7 subareas.
/// </summary>
public class PerSegmentConveyanceTests
{
    private static IrregularSection Creek(CompositeRoughnessMethod m) => new(
    [
        new(0, 108, 0.06, "Left overbank"), new(40, 105, 0.06), new(50, 105, 0.035, "Left bank"), new(55, 100, 0.035),
        new(65, 100, 0.035), new(70, 105, 0.06, "Right bank"), new(80, 105, 0.06), new(120, 108, null, "Right overbank"),
    ], m);

    private static UniformFlowResult Solve(CompositeRoughnessMethod m) => UniformFlowCalculator.Solve(new UniformFlowInput
    { Section = Creek(m), SolveFor = SolveFor.NormalDepth, ManningN = 0.06, Slope = 0.001, Discharge = 500, Depth = 6 });

    [Fact]
    public void Sample6_MatchesHandCalc()
    {
        var r = Solve(CompositeRoughnessMethod.PerSegment);
        Assert.True(r.Succeeded, string.Join("; ", r.Messages));
        Assert.Equal(6.6943116789, r.Normal.Depth, 6);
        Assert.Equal(181.0483613582, r.Normal.Area, 4);
        Assert.Equal(0.0272260133, r.Irregular!.CompositeN, 7);
        Assert.Equal(7, r.Irregular.Subareas.Count); // every wet segment is a subarea
    }

    [Fact]
    public void DefaultMethod_IsUnchanged()
    {
        Assert.Equal(CompositeRoughnessMethod.Conveyance, new IrregularSection([new(0, 1), new(1, 0), new(2, 1)]).RoughnessMethod);
        Assert.Equal(6.893, Solve(CompositeRoughnessMethod.Conveyance).Normal.Depth, 3);
    }

    [Fact]
    public void SingleSegmentPerN_GivesSameAnswer_AsHecRasSubdivision()
    {
        // Every segment has a different n from its neighbours, so both methods make the same subareas.
        var pts = new List<StationPoint> { new(0, 10, 0.03), new(10, 0, 0.05), new(20, 0, 0.03), new(30, 10) };
        double Depth(CompositeRoughnessMethod m) => UniformFlowCalculator.Solve(new UniformFlowInput
        { Section = new IrregularSection(pts, m), SolveFor = SolveFor.NormalDepth, ManningN = 0.03, Slope = 0.001, Discharge = 50, Depth = 2 }).Normal.Depth;
        Assert.Equal(Depth(CompositeRoughnessMethod.Conveyance), Depth(CompositeRoughnessMethod.PerSegment), 9);
    }
}
