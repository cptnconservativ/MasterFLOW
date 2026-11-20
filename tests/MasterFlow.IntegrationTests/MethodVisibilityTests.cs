using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Persistence;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Flow Calc comparison: the ellipse result differed 3% (ASTM C507 vs true ellipse) and the creek 3%
/// (conveyance method). Those choices are now printed with the results and in reports.
/// </summary>
public class MethodVisibilityTests
{
    private static ChannelCalculation Calc(ChannelSection s) => new()
    {
        Title = "t",
        Input = new UniformFlowInput { Section = s, SolveFor = SolveFor.NormalDepth, ManningN = 0.035, Slope = 0.005, Discharge = 20, Depth = 1 },
    };

    [Fact]
    public void StandardEllipse_IsNamedAsAstmC507_InResultsAndReport()
    {
        var sec = EllipticalPipeCatalog.Create(EllipticalPipeCatalog.Sizes.Single(s => s.EquivalentRound == 36), false, UnitSystem.USCustomary);
        var calc = Calc(sec);
        calc = calc with { Input = calc.Input with { ManningN = 0.013 } };
        var r = UniformFlowCalculator.Solve(calc.Input);
        Assert.True(r.Succeeded);
        Assert.StartsWith("Pipe shape: ASTM C507 four-centred", ResultSummary.MethodText(calc.Input));
        Assert.Contains(ResultSummary.InputLines(calc.Input), l => l.Label == "Shape" && l.Value.StartsWith("ASTM C507"));
        Assert.Contains("Pipe shape: ASTM C507 four-centred", HtmlReport.Render(new ProjectInfo(), calc, r));
    }

    [Fact]
    public void TrueEllipse_IsNamed()
    {
        var calc = Calc(new EllipticalSection(29.0 / 12, 45.0 / 12));
        Assert.Equal("Pipe shape: True ellipse.", ResultSummary.MethodText(calc.Input));
    }

    [Theory]
    [InlineData(CompositeRoughnessMethod.Conveyance, "HEC-RAS")]
    [InlineData(CompositeRoughnessMethod.EqualVelocity, "Horton–Einstein")]
    [InlineData(CompositeRoughnessMethod.PerSegment, "Per-segment conveyance (Flow Calc)")]
    public void IrregularMethod_IsAlwaysStated(CompositeRoughnessMethod m, string expected)
    {
        // Single n: the method line is still shown (it matters for the per-segment method).
        var calc = Calc(new IrregularSection([new(0, 5), new(10, 0), new(30, 0), new(40, 5)], m));
        var r = UniformFlowCalculator.Solve(calc.Input);
        Assert.Contains(expected, ResultSummary.MethodText(calc.Input));
        Assert.Contains(ResultSummary.InputLines(calc.Input), l => l.Label == "Conveyance method" && l.Value.Contains(expected));
        Assert.Contains(expected, HtmlReport.Render(new ProjectInfo(), calc, r));
    }

    [Fact]
    public void PerSegment_SurvivesSaveAndLoad()
    {
        var p = new Project { Calculations = [Calc(new IrregularSection([new(0, 5), new(10, 0), new(30, 0), new(40, 5)], CompositeRoughnessMethod.PerSegment))] };
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        Assert.Equal(CompositeRoughnessMethod.PerSegment, ((IrregularSection)back.Calculations[0].Input.Section).RoughnessMethod);
    }

    [Fact]
    public void OtherShapes_HaveNoMethodLine() =>
        Assert.Null(ResultSummary.MethodText(Calc(new TrapezoidalSection(10, 2, 2)).Input));
}
