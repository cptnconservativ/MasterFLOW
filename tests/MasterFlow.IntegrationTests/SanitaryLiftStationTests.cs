using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Sanitary lift station and force main: Hazen-Williams, operating points, firm capacity, cycling, detention, surge.</summary>
public class SanitaryLiftStationTests
{
    private static readonly SanitaryLiftStationTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static readonly List<(double Q, double H)> Curve = [(0, 95), (200, 88), (300, 82), (400, 73), (500, 61), (600, 45)];
    private static string Sample => Tool.SampleInput(Us);
    private static ToolResult Run(string input) => Tool.Run(input, Us);

    [Fact]
    public void Sample_RunsClean_AndSiIsRefused()
    {
        var r = Run(Sample);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.DoesNotContain(r.Messages, m => m.Severity == MessageSeverity.Warning);
        Assert.NotEmpty(r.Charts);
        Assert.False(Tool.Run(Sample, new DesignToolContext(UnitSystem.SI)).Succeeded);
    }

    [Fact]
    public void HazenWilliams_ByHand_AndMatchesCfsForm()
    {
        double hf = SanitaryLiftStationTool.FrictionGpm(350, 2600, 6, 120);
        Assert.Equal(10.44 * 2600 * Math.Pow(350, 1.852) / (Math.Pow(120, 1.852) * Math.Pow(6, 4.8655)), hf, 9);
        // Same as the cfs/ft form used by the HEC-24 tool within 1 % (the two forms round their constants differently).
        Assert.Equal(PumpStationTool.HazenWilliams(350 / 448.831, 2600, 0.5, 120), hf, hf * 0.01);
    }

    [Fact]
    public void OperatingPoint_PumpHeadEqualsSystemHead_AndParallelGivesLessThanDouble()
    {
        double q1 = SanitaryLiftStationTool.Operating(1, 42, 2600, 6, 120, 6.5, Curve);
        Assert.Equal(PumpStationTool.Lerp(Curve, q1), SanitaryLiftStationTool.SystemHead(q1, 42, 2600, 6, 120, 6.5), 3);
        double q2 = SanitaryLiftStationTool.Operating(2, 42, 2600, 6, 120, 6.5, Curve);
        Assert.InRange(q2, q1, 2 * q1);
        Assert.Equal(0, SanitaryLiftStationTool.Operating(1, 100, 2600, 6, 120, 6.5, Curve));
    }

    [Fact]
    public void CycleTime_IsShortestAtHalfThePumpRate()
    {
        double v = 1000, qp = 400;
        Assert.Equal(4 * v / qp, SanitaryLiftStationTool.CycleTime(v, qp, qp / 2), 9);
        Assert.True(SanitaryLiftStationTool.CycleTime(v, qp, 120) > 4 * v / qp);
        Assert.True(SanitaryLiftStationTool.CycleTime(v, qp, 300) > 4 * v / qp);
    }

    [Fact]
    public void Shortfalls_AreFlagged()
    {
        Assert.Contains(Run(Sample.Replace("peak flow = 300", "peak flow = 500")).Messages, m => m.Code == "W-LS-001");
        Assert.Contains(Run(Sample.Replace("force main diameter = 6", "force main diameter = 12")).Messages, m => m.Code == "W-LS-002");
        Assert.Contains(Run(Sample.Replace("lead on = 3372.5", "lead on = 3371.0")).Messages, m => m.Code == "W-LS-004");
        Assert.Contains(Run(Sample.Replace("minimum flow = 60", "minimum flow = 20")).Messages, m => m.Code is "W-LS-005" or "W-LS-007");
        Assert.Contains(Run(Sample.Replace("pressure class = 235", "pressure class = 80")).Messages, m => m.Code == "W-LS-009");
        Assert.Contains(Run(Sample.Replace("2600\t3410", "2600\t3420")).Messages, m => m.Code == "W-LS-006");
    }

    [Fact]
    public void Surge_IsJoukowsky()
    {
        var r = Run(Sample);
        var t = r.Tables.First(x => x.Title.StartsWith("Operating points"));
        double vAll = Convert.ToDouble(t.Rows.Where(x => Convert.ToInt32(x[0]) == 2).Max(x => Convert.ToDouble(x[6])));
        var w = r.Tables.First(x => x.Title.StartsWith("Wet well"));
        double dH = Convert.ToDouble(w.Rows.First(x => ((string)x[0]!).StartsWith("Surge"))[1]);
        Assert.Equal(1300 * vAll / 32.174, dH, 0);
    }

    [Fact]
    public void Errors_AreReported()
    {
        Assert.Contains(Run("peak flow = 100").Messages, m => m.Code == "E-LS-002");
        Assert.Contains(Run(Sample.Replace("lead on = 3372.5", "lead on = 3369")).Messages, m => m.Code == "E-LS-006");
        Assert.Contains(Run(Sample.Replace("0\t95", "0\t50")).Messages, m => m.Code == "E-LS-003");
        Assert.Contains(Run(Sample.Replace("wet well diameter = 8", "")).Messages, m => m.Code == "E-LS-007");
    }
}
