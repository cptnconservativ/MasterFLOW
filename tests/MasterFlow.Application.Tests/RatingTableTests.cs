using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tests;

public class RatingTableTests
{
    private static readonly UniformFlowInput Base = new()
    {
        Section = new TrapezoidalSection(10, 2, 2), ManningN = 0.013, Slope = 0.01,
    };

    [Fact]
    public void DepthRange_ProducesExpectedRows_AndMatchesSingleCalc()
    {
        var t = RatingTableBuilder.Build(new RatingTableRequest(Base, RatingTableMode.DischargeFromDepth, 0.5, 3.0, 0.5));
        Assert.True(t.Succeeded);
        Assert.Equal(6, t.Rows.Count);
        var row = t.Rows.Single(r => Math.Abs(r.Depth - 1.5) < 1e-12);
        Assert.Equal(247.0851422290, row.Discharge, 8); // independent hand calc (validation/hand_calcs.py, case T1)
        Assert.True(t.Rows.Zip(t.Rows.Skip(1)).All(p => p.Second.Discharge > p.First.Discharge), "Q must increase with depth.");
    }

    [Fact]
    public void DischargeRange_SolvesNormalDepth_AndIncludesEndValue()
    {
        var t = RatingTableBuilder.Build(new RatingTableRequest(Base with { Slope = 0.0016, ManningN = 0.025, Section = new TrapezoidalSection(20, 2, 2) },
            RatingTableMode.DepthFromDischarge, 100, 400, 70));
        Assert.True(t.Succeeded);
        Assert.Equal(400, t.Rows[^1].Discharge, 9);
        Assert.Equal(3.3609678403, t.Rows[^1].Depth, 8); // Chow Ex. 6-2 hand calc
        Assert.Equal(6, t.Rows.Count); // 100,170,240,310,380 + end 400
    }

    [Theory]
    [InlineData(0, 1, 0.1)]
    [InlineData(1, 0.5, 0.1)]
    [InlineData(1, 2, 0)]
    [InlineData(1, 1e9, 1e-3)]
    public void InvalidRanges_AreRejected(double start, double end, double inc)
    {
        var t = RatingTableBuilder.Build(new RatingTableRequest(Base, RatingTableMode.DischargeFromDepth, start, end, inc));
        Assert.False(t.Succeeded);
        Assert.Empty(t.Rows);
    }

    [Fact]
    public void InvalidBaseInput_ReportsRowErrors_NoRows()
    {
        var t = RatingTableBuilder.Build(new RatingTableRequest(Base with { Slope = 0 }, RatingTableMode.DischargeFromDepth, 1, 2, 0.5));
        Assert.False(t.Succeeded);
        Assert.Empty(t.Rows);
    }

    [Fact]
    public void Delimited_HasHeaderAndRows()
    {
        var t = RatingTableBuilder.Build(new RatingTableRequest(Base, RatingTableMode.DischargeFromDepth, 1, 2, 0.5));
        var text = RatingTableBuilder.ToDelimited(t, UnitConstants.USCustomary);
        var lines = text.TrimEnd().Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("Depth (ft)\tDischarge (cfs)", lines[0]);
    }

    [Fact]
    public void ResultSummary_MarksSolvedValue()
    {
        var r = UniformFlowCalculator.Solve(Base with { SolveFor = SolveFor.NormalDepth, Discharge = 247.0851422290 });
        var lines = ResultSummary.Lines(r);
        Assert.True(lines.Single(l => l.Symbol == "yn").IsSolved);
        Assert.Equal("1.500", lines.Single(l => l.Symbol == "yn").Formatted);
    }

    [Fact]
    public void ResultSummary_Empty_WhenFailed()
    {
        var r = UniformFlowCalculator.Solve(Base with { Slope = -1 });
        Assert.Empty(ResultSummary.Lines(r));
    }
}
