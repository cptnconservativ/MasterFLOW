using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tests;

public class DesignChecksTests
{
    // Rectangle b = 10 ft, depth 2 ft given, S = 1 %, n = 0.013, channel depth 3 ft (solve for Q).
    private static UniformFlowResult Rect(UnitSystem u = UnitSystem.USCustomary) => UniformFlowCalculator.Solve(new UniformFlowInput
    {
        Units = u, Section = new RectangularSection(10) { ChannelDepth = 3 }, SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.01, Depth = 2,
    });

    // Hand values, computed here independently of the engine.
    private const double TauD = 62.4 * 2 * 0.01;                 // 1.248 lb/ft² (HEC-15 eq. 2.3)
    private static readonly double V = 1.486 / 0.013 * Math.Pow(20.0 / 14.0, 2.0 / 3.0) * Math.Sqrt(0.01); // 14.55 ft/s

    [Fact]
    public void Shear_MatchesHandCalculation_AndPassesOrFailsAtTheLimit()
    {
        var r = Rect();
        var pass = Assert.Single(DesignChecks.Evaluate(new DesignCriteria { PermissibleShear = 1.25 }, r));
        Assert.Equal(TauD, pass.Actual, 10);
        Assert.Equal(CheckStatus.Pass, pass.Status);
        Assert.Equal(1.25 / TauD, pass.Ratio, 10);
        Assert.Equal("lb/ft²", pass.Unit);
        var fail = Assert.Single(DesignChecks.Evaluate(new DesignCriteria { PermissibleShear = 1.2 }, r));
        Assert.Equal(CheckStatus.Fail, fail.Status);
        Assert.Equal("W-C-001", Assert.Single(DesignChecks.Messages([fail])).Code);
    }

    [Fact]
    public void Shear_InSI_UsesNewtons()
    {
        var k = Assert.Single(DesignChecks.Evaluate(new DesignCriteria { PermissibleShear = 500 }, Rect(UnitSystem.SI)));
        Assert.Equal(9810 * 2 * 0.01, k.Actual, 10);
        Assert.Equal("N/m²", k.Unit);
    }

    [Fact]
    public void Freeboard_IsChannelDepthMinusYn()
    {
        var k = Assert.Single(DesignChecks.Evaluate(new DesignCriteria { RequiredFreeboard = 1.0 }, Rect()));
        Assert.Equal(1.0, k.Actual, 10);
        Assert.Equal(CheckStatus.Pass, k.Status);
        Assert.Equal(CheckStatus.Fail, DesignChecks.Evaluate(new DesignCriteria { RequiredFreeboard = 1.01 }, Rect())[0].Status);
    }

    [Fact]
    public void Freeboard_WithoutChannelDepth_IsNotEvaluated_NotGuessed()
    {
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new RectangularSection(10), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.01, Depth = 2 });
        var k = Assert.Single(DesignChecks.Evaluate(new DesignCriteria { RequiredFreeboard = 1 }, r));
        Assert.Equal(CheckStatus.NotEvaluated, k.Status);
        Assert.Contains("channel depth", k.Basis);
        Assert.Empty(DesignChecks.Messages([k]));
    }

    [Fact]
    public void Freeboard_Irregular_UsesTheLowerEndPoint()
    {
        // Ends at 105 and 104; bed at 100; depth 2 given → WSE 102, freeboard 104 − 102 = 2.
        var r = UniformFlowCalculator.Solve(new UniformFlowInput
        {
            Section = new IrregularSection([new(0, 105), new(10, 100), new(20, 100), new(30, 104)]), SolveFor = SolveFor.Discharge, ManningN = 0.03, Slope = 0.005, Depth = 2,
        });
        var k = Assert.Single(DesignChecks.Evaluate(new DesignCriteria { RequiredFreeboard = 1 }, r));
        Assert.Equal(2.0, k.Actual, 9);
        Assert.Equal(CheckStatus.Pass, k.Status);
    }

    [Fact]
    public void Velocity_Limits()
    {
        var ks = DesignChecks.Evaluate(new DesignCriteria { MinVelocity = 2, MaxVelocity = 10 }, Rect());
        Assert.Equal(2, ks.Count);
        Assert.All(ks, k => Assert.Equal(V, k.Actual, 9));
        Assert.Equal(CheckStatus.Fail, ks.Single(k => k.Name == "Maximum velocity").Status);
        Assert.Equal(CheckStatus.Pass, ks.Single(k => k.Name == "Minimum velocity").Status);
        Assert.Equal("W-C-003", Assert.Single(DesignChecks.Messages(ks)).Code);
    }

    [Fact]
    public void Pipes_ShearAndFreeboard_NotEvaluated_VelocityStillChecked()
    {
        var r = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new CircularSection(2), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = 10 });
        var ks = DesignChecks.Evaluate(new DesignCriteria { PermissibleShear = 1, RequiredFreeboard = 0.5, MinVelocity = 2 }, r);
        Assert.Equal(CheckStatus.NotEvaluated, ks[0].Status);
        Assert.Equal(CheckStatus.NotEvaluated, ks[1].Status);
        Assert.Equal(CheckStatus.Pass, ks[2].Status);
    }

    [Fact]
    public void NoCriteria_OrNoResult_GivesNoChecks()
    {
        Assert.Empty(DesignChecks.Evaluate(null, Rect()));
        Assert.Empty(DesignChecks.Evaluate(new DesignCriteria(), Rect()));
        var bad = UniformFlowCalculator.Solve(new UniformFlowInput { Section = new RectangularSection(10), SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0, Depth = 2 });
        Assert.Empty(DesignChecks.Evaluate(new DesignCriteria { PermissibleShear = 1 }, bad));
    }
}
