using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tests;

/// <summary>Flow Calc shows full-flow Q, maximum Q and percent full with the main pipe results; so does MasterFlow now.</summary>
public class PipeCapacityTests
{
    private static UniformFlowResult Rcp36() => UniformFlowCalculator.Solve(new UniformFlowInput
    { Section = new CircularSection(3.0), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = 50, Depth = 2 });

    [Fact]
    public void PipeCapacity_FollowsTheGoverningValues()
    {
        var lines = ResultSummary.Lines(Rcp36());
        int lastGoverning = lines.ToList().FindLastIndex(l => l.Group == "Governing");
        Assert.Equal("Conduit", lines[lastGoverning + 1].Group);
        var names = lines.Skip(lastGoverning + 1).TakeWhile(l => l.Group == "Conduit").Select(l => l.Symbol).ToList();
        Assert.Contains("Qfull", names);
        Assert.Contains("Qmax", names);
        Assert.Contains(lines, l => l.Label == "Percent full" && l.Group == "Conduit");
        Assert.Contains(lines, l => l.Label == "Max. Q (part full)");
    }

    [Fact]
    public void CapacityText_MatchesManningFullFlow()
    {
        var r = Rcp36();
        double area = Math.PI * 9 / 4, rh = 0.75;
        double qFull = 1.486 / 0.013 * area * Math.Pow(rh, 2.0 / 3.0) * Math.Sqrt(0.01);
        Assert.Equal(qFull, r.Conduit!.FullFlowDischarge, 3);
        var text = ResultSummary.CapacityText(r)!;
        Assert.StartsWith($"Full-flow Q {qFull:F2} cfs · Max. Q ", text);
        Assert.Contains("% full · Q/Qfull ", text);
        Assert.InRange(r.Conduit.MaxDischarge / qFull, 1.07, 1.08); // circular pipe peak ≈ 1.076 Qfull
    }

    [Fact]
    public void CapacityText_IsNull_ForOpenChannels()
    {
        var r = UniformFlowCalculator.Solve(new UniformFlowInput
        { Section = new TrapezoidalSection(20, 2, 2), SolveFor = SolveFor.NormalDepth, ManningN = 0.025, Slope = 0.0016, Discharge = 400, Depth = 3 });
        Assert.Null(ResultSummary.CapacityText(r));
    }
}
