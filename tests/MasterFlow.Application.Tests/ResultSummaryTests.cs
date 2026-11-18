using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tests;

public class ResultSummaryTests
{
    private static ResultLine Line(UniformFlowResult r, string label) => ResultSummary.Lines(r).Single(l => l.Label == label);

    [Fact] // hand calc: rectangle b = 10 ft, y = 2 ft, S = 0.01: R = 20/14, τ = 62.4 R S, τmax = 62.4 y S
    public void ShearAndVelocityDepth_MatchHandCalc()
    {
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new RectangularSection(10), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.01, Depth = 2 });
        Assert.Equal(62.4 * (20.0 / 14.0) * 0.01, Line(r, "Mean boundary shear").Value, 12);
        Assert.Equal(62.4 * 2 * 0.01, Line(r, "Max. shear (deepest point)").Value, 12);
        Assert.Equal(r.Discharge / 20.0 * 2, Line(r, "Velocity × depth").Value, 12);
        Assert.Equal("lb/ft²", Line(r, "Mean boundary shear").Unit);
        var si = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new RectangularSection(3), Units = UnitSystem.SI, SolveFor = SolveFor.Discharge, ManningN = 0.015, Slope = 0.001, Depth = 1 });
        Assert.Equal(9810 * 0.6 * 0.001, Line(si, "Mean boundary shear").Value, 10);
        Assert.Equal("N/m²", Line(si, "Mean boundary shear").Unit);
    }

    [Fact]
    public void Elevations_OnlyWithInvert()
    {
        var input = new UniformFlowInput { Section = new TrapezoidalSection(4, 2, 2), SolveFor = SolveFor.NormalDepth, ManningN = 0.03, Slope = 0.005, Discharge = 60 };
        var none = UniformFlowCalculator.Solve(input);
        Assert.DoesNotContain(ResultSummary.Lines(none), l => l.Group == "Elevations");
        var r = UniformFlowCalculator.Solve(input with { InvertElevation = 812.5 });
        Assert.Equal(812.5 + r.Normal.Depth, Line(r, "Water surface (HGL)").Value, 12);
        Assert.Equal(812.5 + r.Normal.Depth + r.Normal.VelocityHead, Line(r, "Energy grade (EGL)").Value, 12);
        Assert.Equal(812.5 + r.Critical.Depth, Line(r, "Critical water surface").Value, 12);
        Assert.Contains(ResultSummary.InputLines(r.Input), l => l.Label == "Invert elevation");
        Assert.False(UniformFlowCalculator.Solve(input with { InvertElevation = double.NaN }).Succeeded);
    }

    [Fact]
    public void Irregular_HasEnergyGrade()
    {
        var sec = new IrregularSection([new(0, 105), new(10, 100), new(20, 100), new(30, 105)]);
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = sec, SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.01, Depth = 1.5 });
        Assert.Equal(101.5, Line(r, "Water-surface elev.").Value, 12);
        Assert.Equal(101.5 + r.Normal.VelocityHead, Line(r, "Energy grade elev.").Value, 12);
    }

    [Fact]
    public void RoughnessTable_IsConsistent()
    {
        var e = ManningRoughnessTable.Entries;
        Assert.True(e.Count >= 40);
        Assert.All(e, x =>
        {
            Assert.InRange(x.Minimum, 0.008, 0.2);
            Assert.True(x.Minimum <= x.Normal, x.Description);
            if (x.Maximum is { } max) Assert.True(x.Normal <= max, x.Description);
        });
        Assert.Equal(e.Count, e.Select(x => x.Category + x.Description).Distinct().Count());
    }
}
