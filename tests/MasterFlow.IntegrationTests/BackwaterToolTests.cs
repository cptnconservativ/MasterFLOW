using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Backwater profiles. The standard-step result is checked against an independent direct-step integration written
/// here (fixed depth steps, distance computed), tolerance 0.01 ft, and against the limits the theory requires
/// (approach to normal depth; profile class).
/// </summary>
public class BackwaterToolTests
{
    private static readonly UnitConstants C = UnitConstants.For(UnitSystem.USCustomary);
    private static readonly ISectionGeometry Trap = SectionGeometry.From(new TrapezoidalSection(10, 3, 3));
    private const double Q = 200, N = 0.035, S0 = 0.002;

    private static double Yc => UniformFlowCalculator.SolveCriticalDepth(Trap, Q, C.Gravity).Root;
    private static double Yn => UniformFlowCalculator.SolveNormalDepth(Trap, Q, N, S0, C.ManningK).Root;

    /// <summary>Direct step: distance (upstream) over which depth goes from y0 to y1 on a mild slope.</summary>
    private static double DirectStep(double y0, double y1, int steps = 4000)
    {
        double x = 0, dy = (y1 - y0) / steps;
        for (int i = 0; i < steps; i++)
        {
            double ya = y0 + i * dy, yb = ya + dy;
            double ea = GradualFlow.Energy(Trap, ya, Q, C.Gravity), eb = GradualFlow.Energy(Trap, yb, Q, C.Gravity);
            double sf = 0.5 * (GradualFlow.Sf(Trap, ya, Q, N, C.ManningK) + GradualFlow.Sf(Trap, yb, Q, N, C.ManningK));
            x -= (eb - ea) / (S0 - sf);   // Δx = ΔE/(S0 − Sf) in the flow direction; upstream is negative
        }
        return x;
    }

    [Fact]
    public void M1_StandardStep_MatchesDirectStep_AndApproachesNormalDepth()
    {
        double dist = DirectStep(5.0, 4.0);
        var (pts, stop) = GradualFlow.Compute(Trap, Q, N, S0, dist, 5.0, true, Yc, C, steps: 400);
        Assert.Null(stop);
        Assert.Equal(4.0, pts[^1].Depth, 2);
        var (far, _) = GradualFlow.Compute(Trap, Q, N, S0, 20000, 5.0, true, Yc, C);
        Assert.Equal(Yn, far[^1].Depth, 2);
        Assert.Equal("M1", GradualFlow.Classify(S0, Yn, Yc, 5.0));
    }

    [Fact]
    public void M2_FromFreeOverfall_RisesTowardNormalDepth()
    {
        var (pts, _) = GradualFlow.Compute(Trap, Q, N, S0, 3000, Yc, true, Yc, C);
        Assert.True(pts.Zip(pts.Skip(1)).All(p => p.Second.Depth >= p.First.Depth - 1e-9));
        Assert.InRange(pts[^1].Depth, Yc, Yn + 1e-6);
        Assert.Equal("M2", GradualFlow.Classify(S0, Yn, Yc, 0.5 * (Yc + Yn)));
    }

    [Fact]
    public void Steep_S2_FallsFromCriticalTowardNormal()
    {
        double s = 0.03, yn = UniformFlowCalculator.SolveNormalDepth(Trap, Q, N, s, C.ManningK).Root;
        Assert.True(yn < Yc);
        var (pts, _) = GradualFlow.Compute(Trap, Q, N, s, 2000, Yc, false, Yc, C);
        Assert.Equal(yn, pts[^1].Depth, 2);
        Assert.Equal("S2", GradualFlow.Classify(s, yn, Yc, 0.5 * (Yc + yn)));
    }

    [Fact]
    public void Tool_FlagsFreeboard_AndCorrectsLowControl()
    {
        var tool = new BackwaterTool();
        var ctx = DesignToolTests.Ctx();
        var r = tool.Run(tool.SampleInput(ctx), ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-BW-005");    // 5.0 ft at the control, 0.5 ft below a 5.5 ft bank, 1 ft needed
        Assert.Equal("freeboard short", r.Tables[0].Rows[0][8]);
        Assert.Equal(3250.0 + 5.0, (double)r.Tables[0].Rows[0][2]!, 9);
        r = tool.Run(tool.SampleInput(ctx).Replace("control depth = 5.0", "control depth = 0.5"), ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-BW-002");
        Assert.Equal(Yc, (double)r.Tables[0].Rows[0][1]!, 6);
    }

    /// <summary>Review finding: a 4 ft control in a 3 ft pipe was returned as an open-channel profile. At or above the crown it must fail.</summary>
    [Theory]
    [InlineData(4.0, false)]
    [InlineData(3.0, false)]
    [InlineData(2.5, true)]
    public void CircularConduit_ControlAtOrAboveCrown_IsRejected(double control, bool ok)
    {
        string input = $"section = circular\nD = 3\nn = 0.013\nslope = 0.002\nQ = 20\nlength = 300\ncontrol depth = {control}\ninvert at control = 100\nbank depth = 3\nfreeboard = 0";
        var r = new BackwaterTool().Run(input, DesignToolTests.Ctx());
        Assert.Equal(ok, r.Succeeded);
        if (!ok)
        {
            Assert.Contains(r.Messages, m => m.Code == "E-BW-008");
            Assert.Empty(r.Tables);
        }
        else Assert.All(r.Tables[0].Rows, row => Assert.True((double)row[1]! < 3));
    }
}
