using System.Globalization;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tests;

public class TitleExpressionTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static UniformFlowResult Pipe() => UniformFlowCalculator.Solve(new UniformFlowInput
    { Section = new CircularSection(3), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = 40, InvertElevation = 100 });

    [Fact]
    public void Replaces_Fields_WithFormats()
    {
        var r = Pipe();
        Assert.Equal("Circular pipe 36 in, Q = 40.0 cfs, yn = " + r.Normal.Depth.ToString("F2", Inv) + " ft",
            TitleExpression.Evaluate("{shape} {D} in, Q = {Q:F1} {Qunit}, yn = {yn:F2} {unit}", r, culture: Inv));
        Assert.Equal((100 + r.Normal.Depth).ToString("0.###", Inv), TitleExpression.Evaluate("{WSE}", r, culture: Inv));
        Assert.Equal("Storm/Line A", TitleExpression.Evaluate("{folder}", r, "Storm/Line A", Inv));
    }

    [Fact]
    public void Literals_Typos_And_NoResult_AreVisible()
    {
        var r = Pipe();
        Assert.Equal("{literal} 40", TitleExpression.Evaluate("{{literal}} {Q:0}", r, culture: Inv));
        Assert.Equal("Q = {?Qx}", TitleExpression.Evaluate("Q = {Qx}", r, culture: Inv));
        Assert.Equal("open {brace", TitleExpression.Evaluate("open {brace", r, culture: Inv));
        Assert.Equal("Plain title", TitleExpression.Evaluate("Plain title", r));
        var bad = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new CircularSection(-1), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.01, Depth = 1 });
        Assert.Equal("Q = — cfs", TitleExpression.Evaluate("Q = {Q} {Qunit}", bad, culture: Inv));
        Assert.Equal("b = —", TitleExpression.Evaluate("b = {b}", r, culture: Inv));
    }
}
