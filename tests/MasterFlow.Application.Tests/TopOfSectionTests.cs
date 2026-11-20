using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tests;

public class TopOfSectionTests
{
    private static UniformFlowResult Creek(CompositeRoughnessMethod m) => UniformFlowCalculator.Solve(new UniformFlowInput
    {
        Section = new IrregularSection(
        [
            new(0, 108, 0.06), new(40, 105, 0.06), new(50, 105, 0.035), new(55, 100, 0.035),
            new(65, 100, 0.035), new(70, 105, 0.06), new(80, 105, 0.06), new(120, 107, null),
        ], m),
        SolveFor = SolveFor.NormalDepth, ManningN = 0.06, Slope = 0.001, Discharge = 500, Depth = 6,
    });

    [Fact]
    public void IrregularResults_ShowTopOfSection_AndFreeboard()
    {
        var lines = ResultSummary.Lines(Creek(CompositeRoughnessMethod.Conveyance));
        Assert.Equal(107, lines.Single(l => l.Symbol == "ztop").Value, 9); // lower of the two end points
        var f = lines.Single(l => l.Symbol == "f");
        Assert.Equal("Freeboard to top", f.Label);
        Assert.True(f.Value > 0);
    }

    [Fact]
    public void OvertoppedIrregular_ShowsNegativeFreeboard_Labelled()
    {
        var r = Creek(CompositeRoughnessMethod.EqualVelocity);
        var f = ResultSummary.Lines(r).Single(l => l.Symbol == "f");
        Assert.Equal("Freeboard (OVERTOPPED)", f.Label);
        Assert.True(f.Value < 0);
        Assert.Equal(107 - r.Irregular!.WaterSurfaceElevation, f.Value, 9);
    }
}
