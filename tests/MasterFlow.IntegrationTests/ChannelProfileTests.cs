using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.IntegrationTests;

/// <summary>Channel profile tool: uniform flow, agreement with the single-reach standard step, transitions, mixed regime and riprap sizing.</summary>
public class ChannelProfileTests
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private const string Head = "Name\tLength (ft)\tSlope (ft/ft)\tQ (cfs)\tShape\tb (ft)\tzL\tzR\tDepth (ft)\tLining\tn capacity\tn stability\tD50 (in)";

    private static ToolResult Run(string input)
    {
        var r = new ChannelProfileTool().Run(input, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }
    private static IReadOnlyList<IReadOnlyList<object?>> Profile(ToolResult r) => r.Tables.Single(t => t.Title.StartsWith("Profile")).Rows;

    [Fact]
    public void UniformChannel_StaysAtNormalDepth()
    {
        var r = Run($"downstream = normal\n[Reaches]\n{Head}\nA\t800\t0.004\t200\ttrapezoid\t10\t3\t3\t6\tcustom\t0.035\t0.030\n");
        var g = SectionGeometry.From(new TrapezoidalSection(10, 3, 3));
        double yn = UniformFlowCalculator.SolveNormalDepth(g, 200, 0.035, 0.004, 1.486).Root;
        double ynS = UniformFlowCalculator.SolveNormalDepth(g, 200, 0.030, 0.004, 1.486).Root;
        Assert.All(Profile(r), row => Assert.Equal(yn, (double)row[3]!, 3));
        Assert.All(Profile(r), row => Assert.Equal(ynS, (double)row[8]!, 3));
    }

    [Fact]
    public void M1Backwater_MatchesSingleReachStandardStep()
    {
        // 10-ft trapezoid, z 3, n 0.035, S 0.002, Q 200, 1500 ft, control depth 5.0 ft: same as the Backwater tool's sample.
        var r = Run($"downstream = 3255.0\ndownstream invert = 3250\nstep = 7.5\n[Reaches]\n{Head}\nA\t1500\t0.002\t200\ttrapezoid\t10\t3\t3\t8\tcustom\t0.035\t0.035\n");
        var g = SectionGeometry.From(new TrapezoidalSection(10, 3, 3));
        double yc = UniformFlowCalculator.SolveCriticalDepth(g, 200, 32.174).Root;
        var (pts, _) = GradualFlow.Compute(g, 200, 0.035, 0.002, 1500, 5.0, true, yc, UnitConstants.For(UnitSystem.USCustomary), 200);
        Assert.Equal(pts[^1].Depth, (double)Profile(r)[^1][3]!, 3);
    }

    [Fact]
    public void Contraction_LossIsCoefficientTimesVelocityHeadChange()
    {
        // Frictionless, flat: a 20-ft rectangle (downstream) fed by a 30-ft rectangle (upstream); Q 300, downstream depth 3 ft.
        // E_up = E_down + 0.1·(hv_down − hv_up) (velocity rises downstream → contraction).
        var r = Run($"downstream = 3\ndownstream invert = 0\ncontraction = 0.1\nexpansion = 0.3\n[Reaches]\n{Head}\nD\t10\t0\t300\trectangle\t20\t\t\t6\tcustom\t0.000001\t0.000001\nU\t10\t0\t300\trectangle\t30\t\t\t6\tcustom\t0.000001\t0.000001\n");
        var p = Profile(r);
        double yU = (double)p[^1][3]!, hvD = Math.Pow(300 / (20 * 3.0), 2) / (2 * 32.174), hvU = Math.Pow(300 / (30 * yU), 2) / (2 * 32.174);
        Assert.Equal(3 + hvD + 0.1 * (hvD - hvU), yU + hvU, 4);
    }

    [Fact]
    public void SteepToMild_HasSupercriticalReachAndJump()
    {
        var r = Run($"downstream = normal\nstep = 10\n[Reaches]\n{Head}\nMild\t600\t0.001\t150\trectangle\t10\t\t\t8\tcustom\t0.015\t0.015\nSteep\t300\t0.03\t150\trectangle\t10\t\t\t8\tcustom\t0.015\t0.015\n");
        var p = Profile(r);
        double yc = Math.Pow(15.0 * 15.0 / 32.174, 1.0 / 3.0);
        Assert.Contains(p, row => (string)row[6]! == "super");
        Assert.Equal("sub", (string)p[0][6]!);
        var top = p[^1];
        Assert.Equal(yc, (double)top[3]!, 2);                        // critical depth at the head of the steep reach
        Assert.Contains(r.Messages, m => m.Code == "I-CP-002");
        // Far down the steep reach the depth approaches its normal depth (S2 profile).
        var g = SectionGeometry.From(new RectangularSection(10));
        double ynSteep = UniformFlowCalculator.SolveNormalDepth(g, 150, 0.015, 0.03, 1.486).Root;
        var steepLow = p.Where(row => (string)row[0]! == "Steep").MinBy(row => (double)row[1]!)!;
        Assert.InRange((double)steepLow[3]!, ynSteep * 0.98, yc);
    }

    [Fact]
    public void Riprap_D50_FromHec15()
    {
        var r = Run($"downstream = normal\n[Reaches]\n{Head}\nR\t200\t0.008\t240\ttrapezoid\t8\t3\t3\t5\triprap\t0.040\t0.030\t2\n");
        var row = r.Tables.Single(t => t.Title.StartsWith("Lining")).Rows.Single(x => (string)x[1]! == "Riprap D50");
        var g = SectionGeometry.From(new TrapezoidalSection(8, 3, 3));
        double yn = UniformFlowCalculator.SolveNormalDepth(g, 240, 0.030, 0.008, 1.486).Root;
        double d50 = 62.4 * yn * 0.008 / (0.047 * (165 - 62.4)) * 12;
        Assert.Contains($"{d50:F1} in.", (string)row[2]!);
        Assert.Equal("NO", row[4]);
    }
}
