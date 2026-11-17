using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Numerics;

namespace MasterFlow.Hydraulics.Tests;

public class ValidationAndSolverTests
{
    private static UniformFlowResult Run(ChannelSection s, SolveFor f = SolveFor.NormalDepth, double n = 0.013, double slope = 0.001, double q = 10, double y = 1, double? channelDepth = null) =>
        UniformFlowCalculator.Solve(new UniformFlowInput
        {
            Section = channelDepth is null ? s : s with { ChannelDepth = channelDepth },
            SolveFor = f, ManningN = n, Slope = slope, Discharge = q, Depth = y,
        });

    private static void AssertError(UniformFlowResult r, string code)
    {
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code && m.Severity == MessageSeverity.Error);
        Assert.True(double.IsNaN(r.Discharge) && double.IsNaN(r.Normal.Depth is 0 ? double.NaN : r.Normal.Depth), "Failed results must not carry numbers.");
    }

    [Theory] // invalid-input cases for every geometry
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Rectangular_NonPositiveWidth_IsError(double b) => AssertError(Run(new RectangularSection(b)), MessageCodes.GeometryInvalid);

    [Fact]
    public void Trapezoidal_NegativeSideSlope_IsError() => AssertError(Run(new TrapezoidalSection(5, -1, 2)), MessageCodes.GeometryInvalid);

    [Fact]
    public void Trapezoidal_ZeroWidthVerticalWalls_IsError() => AssertError(Run(new TrapezoidalSection(0, 0, 0)), MessageCodes.GeometryInvalid);

    [Fact]
    public void Trapezoidal_ZeroBottomWidth_IsValidTriangle()
    {
        var trap = Run(new TrapezoidalSection(0, 2, 2));
        var tri = Run(new TriangularSection(2, 2));
        Assert.True(trap.Succeeded);
        Assert.Equal(tri.Normal.Depth, trap.Normal.Depth, 12);
    }

    [Fact]
    public void Triangular_BothSlopesZero_IsError() => AssertError(Run(new TriangularSection(0, 0)), MessageCodes.GeometryInvalid);

    [Fact]
    public void Triangular_OneVerticalWall_IsValid() => Assert.True(Run(new TriangularSection(0, 4)).Succeeded);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.001)]
    public void ZeroOrAdverseSlope_IsError_NoNormalDepth(double s) => AssertError(Run(new RectangularSection(10), slope: s), MessageCodes.SlopeNotPositive);

    [Fact]
    public void ZeroRoughness_IsError() => AssertError(Run(new RectangularSection(10), n: 0), MessageCodes.RoughnessNotPositive);

    [Fact]
    public void ZeroDischarge_IsError_WhenSolvingDepth() => AssertError(Run(new RectangularSection(10), q: 0), MessageCodes.DischargeNotPositive);

    [Fact]
    public void ZeroDepth_IsError_WhenSolvingDischarge() => AssertError(Run(new RectangularSection(10), SolveFor.Discharge, y: 0), MessageCodes.DepthNotPositive);

    [Fact]
    public void ChannelDepth_Invalid_IsError() => AssertError(Run(new RectangularSection(10), channelDepth: 0), MessageCodes.GeometryInvalid);

    [Fact]
    public void UnusualRoughness_Warns_ButSolves()
    {
        var r = Run(new RectangularSection(10), n: 0.5);
        Assert.True(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.RoughnessUnusual && m.Severity == MessageSeverity.Warning);
    }

    [Fact]
    public void Overtopping_Warns()
    {
        var r = Run(new TrapezoidalSection(4, 2, 2), q: 400, slope: 0.002, channelDepth: 2.0);
        Assert.True(r.Succeeded);
        Assert.True(r.Normal.Depth > 2.0);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.Overtopping);
    }

    [Fact]
    public void ExtremelyMildSlope_Warns()
    {
        var r = Run(new RectangularSection(10), slope: 1e-7);
        Assert.True(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.SlopeVeryMild);
    }

    [Fact]
    public void NearCritical_Warns()
    {
        // Pick the slope that makes normal depth equal critical depth (S = Sc): Fr = 1 exactly.
        var section = new RectangularSection(10);
        var first = Run(section, q: 100);
        var r = Run(section, q: 100, slope: first.CriticalSlope);
        Assert.Equal(FlowRegime.NearCritical, r.Regime);
        Assert.Equal(1.0, r.Normal.Froude, 8);
        Assert.Equal(r.Critical.Depth, r.Normal.Depth, 9);
        Assert.Contains(r.Messages, m => m.Code == MessageCodes.NearCritical);
    }

    [Fact]
    public void Trace_DescribesSolverAndBracket()
    {
        var r = Run(new TrapezoidalSection(10, 2, 2));
        Assert.Contains(r.Trace, t => t.StartsWith("Normal depth: Brent") && t.Contains("bracket"));
        Assert.Contains(r.Trace, t => t.StartsWith("Critical depth: Brent"));
    }

    // ---------------- Root finder

    [Fact]
    public void Brent_FindsCubeRoot()
    {
        var r = RootFinder.Brent(x => x * x * x - 2, 0, 2);
        Assert.True(r.Converged);
        Assert.Equal(Math.Cbrt(2), r.Root, 12);
        Assert.True(r.Iterations < 50);
    }

    [Fact]
    public void Brent_NoSignChange_ReportsFailure()
    {
        var r = RootFinder.Brent(x => x * x + 1, -1, 1);
        Assert.False(r.Converged);
        Assert.True(double.IsNaN(r.Root));
        Assert.NotNull(r.FailureReason);
    }

    [Fact]
    public void Brent_NonFinite_ReportsFailure()
    {
        var r = RootFinder.Brent(x => x < 0.5 ? -1 : double.NaN, 0, 1);
        Assert.False(r.Converged);
    }

    [Fact]
    public void Brent_IterationLimit_ReportsFailure_NotAnAnswer()
    {
        var r = RootFinder.Brent(x => Math.Exp(x) - 1.0 - 1e6 * Math.Pow(x - 0.3, 3), -5, 5, xTolerance: 0, maxIterations: 2);
        Assert.False(r.Converged);
        Assert.Contains("Did not converge", r.FailureReason);
    }

    [Fact]
    public void Expansion_GivesUp_AtSearchLimit()
    {
        var r = RootFinder.BrentWithUpwardExpansion(x => -1.0, 0, 1, 1000);
        Assert.False(r.Converged);
    }

    [Fact]
    public void Expansion_WithStepFunction_StillBrackets()
    {
        // Discontinuous function: Brent must still return the jump location within tolerance.
        var r = RootFinder.BrentWithUpwardExpansion(x => x < 37.25 ? -1.0 : 1.0, 0, 1, 1e5, xTolerance: 1e-10);
        Assert.True(r.Converged);
        Assert.Equal(37.25, r.Root, 6);
    }
}
