using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>HDS-6 sediment transport: hand calculations of Rubey, Engelund–Hansen, MPM and Yang, and the reach budget's continuity.</summary>
public class SedimentTransportTests
{
    private static readonly SedimentTransportTool Tool = new();
    private const double G = SedimentTransportTool.G;

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact]
    public void Samples_RunInBothUnits()
    {
        foreach (var u in new[] { UnitSystem.USCustomary, UnitSystem.SI }) Run(Tool.SampleInput(new DesignToolContext(u)), u);
    }

    [Fact]
    public void RubeyFallVelocity()
    {
        // d = 0.8 mm, ν = 1.135e-6: F = √(2/3 + 0.005597) − √0.005597 = 0.74513; ω = F·√(1.65·g·0.0008) = 0.08478 m/s.
        Assert.Equal(0.08478, SedimentTransportTool.FallVelocity(0.0008, 2.65, 1.135e-6), 4);
        Assert.InRange(SedimentTransportTool.Viscosity(15), 1.130e-6, 1.145e-6);   // tables: 1.139e-6
        Assert.InRange(SedimentTransportTool.Viscosity(20), 0.995e-6, 1.010e-6);   // tables: 1.003e-6
    }

    [Fact]
    public void EngelundHansen_MatchesClassicalForm()
    {
        var h = SedimentTransportTool.Wide(1.858, 0.028, 0.0015, 0.0008, 2.65);
        // qs = 0.05·V²·√(d/(g(Ss − 1)))·θ^1.5
        double expect = 0.05 * h.V * h.V * Math.Sqrt(0.0008 / (G * 1.65)) * Math.Pow(h.Theta, 1.5);
        Assert.Equal(expect, SedimentTransportTool.EngelundHansen(h, 0.0015, 0.0008, 2.65), 10);
        Assert.Equal(1.194, h.Y, 3);   // (1.858 × 0.028/√0.0015)^0.6
    }

    [Fact]
    public void MeyerPeterMuller_HandCalc()
    {
        // Gravel: q = 2 m²/s, n 0.035, S 0.005, D50 20 mm, D90 60 mm.
        var h = SedimentTransportTool.Wide(2, 0.035, 0.005, 0.02, 2.65);
        double nGrain = Math.Pow(0.06, 1.0 / 6) / 26, thetaP = Math.Pow(nGrain / 0.035, 1.5) * h.Theta;
        double expect = 8 * Math.Pow(thetaP - 0.047, 1.5) * Math.Sqrt(1.65 * G * 0.02 * 0.02 * 0.02);
        Assert.Equal(expect, SedimentTransportTool.Mpm(h, 0.035, 0.02, 0.06, 2.65), 12);
        Assert.True(expect > 0);
        // Below the threshold there is no bed load.
        Assert.Equal(0, SedimentTransportTool.Mpm(SedimentTransportTool.Wide(0.1, 0.035, 0.0005, 0.05, 2.65), 0.035, 0.05, 0.1, 2.65));
    }

    [Fact]
    public void Yang_ZeroBelowCriticalVelocity_AndIncreasingWithSlope()
    {
        double nu = SedimentTransportTool.Viscosity(15);
        var slow = SedimentTransportTool.Wide(0.05, 0.03, 0.0001, 0.001, 2.65);
        Assert.Equal(0, SedimentTransportTool.YangPpm(slow, 0.0001, 0.001, 2.65, nu));
        var a = SedimentTransportTool.Wide(1.5, 0.028, 0.001, 0.0005, 2.65);
        var b = SedimentTransportTool.Wide(1.5, 0.028, 0.002, 0.0005, 2.65);
        Assert.True(SedimentTransportTool.YangPpm(b, 0.002, 0.0005, 2.65, nu) > SedimentTransportTool.YangPpm(a, 0.001, 0.0005, 2.65, nu));
    }

    [Fact]
    public void ReachBudget_ConservesSediment()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
        var rows = r.Tables[2].Rows;
        double first = (double)rows[0][1]!, last = (double)rows[^1][2]!;
        double sum = rows.Sum(x => (double)x[3]!);
        Assert.InRange(sum - (first - last) * 24 * 3600 / 0.6, -15.0, 15.0);   // rates are rounded to 0.0001 m³/s
        Assert.Equal("degrades", rows[1][5]);                      // the narrower bridge reach
        Assert.Equal("aggrades", rows[2][5]);                      // wider, flatter reach downstream
    }

    [Fact]
    public void Errors()
    {
        var r = Tool.Run("Q = 10\nformula = toffaleti\n", new DesignToolContext(UnitSystem.USCustomary));
        Assert.Contains(r.Messages, m => m.Code == "E-ST-001");
        Assert.Contains(r.Messages, m => m.Code == "E-ST-002");
        Assert.Contains(r.Messages, m => m.Code == "E-ST-005");
    }
}
