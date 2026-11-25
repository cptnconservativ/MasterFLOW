using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Preliminary storage: TR-55 Fig. 6-1, runoff, modified rational critical duration, bounds.</summary>
public class StorageEstimateTests
{
    private static readonly StorageEstimateTool Tool = new();

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static double Vol(ToolResult r, string start) => (double)r.Tables[0].Rows.Single(x => ((string)x[0]!).StartsWith(start))[3]!;

    [Fact]
    public void Samples_RunInBothUnits()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void Tr55_ByHand()
    {
        var r = Ok("area = 25\nqi = 48\nqo = 18\nCN = 82\nP = 4.55\n");
        double s = 1000.0 / 82 - 10, q = Math.Pow(4.55 - 0.2 * s, 2) / (4.55 + 0.8 * s), x = 18.0 / 48;
        double ratio = 0.682 - 1.43 * x + 1.64 * x * x - 0.804 * x * x * x;
        Assert.Equal(Math.Round(ratio * q / 12 * 25, 3), Vol(r, "TR-55"), 3);
        Assert.Equal(Math.Round((1 - x) * (1 - x) * q / 12 * 25, 3), Vol(r, "Abt"), 3);
        Assert.Equal(Math.Round((1 - x) * q / 12 * 25, 3), Vol(r, "Baker"), 3);
        Assert.True(Vol(r, "TR-55") < Vol(r, "Baker") && Vol(r, "Abt") < Vol(r, "Baker"));
    }

    [Fact]
    public void TypeIa_Coefficients()
    {
        Assert.Equal(0.660 - 1.76 * 0.5 + 1.96 * 0.25 - 0.730 * 0.125, StorageEstimateTool.Tr55Ratio(0.5, false), 9);
        Assert.Equal(0, StorageEstimateTool.Runoff(0.3, 70));
    }

    [Fact]
    public void ModifiedRational_PicksTheCriticalDuration()
    {
        var r = Ok("area = 25\nqi = 48\nqo = 18\nC = 0.65\ntc = 15\nfrequency = 100\n");
        double best = 0;
        foreach (var td in new double[] { 15, 20, 30, 45, 60, 75, 90, 120, 180, 240 })
            best = Math.Max(best, StorageEstimateTool.ModRational(0.65, RapidCityRational.Intensity(100, td), 25, td, 15, 18));
        Assert.True(Vol(r, "Modified") >= Math.Round(best / 43560, 3) - 1e-9);
        Assert.Equal(4, r.Tables[1].Headers.Count);
    }

    [Fact]
    public void SiMatchesUs()
    {
        var us = Ok("area = 25\nqi = 48\nqo = 18\nCN = 82\nP = 4.55\n");
        var si = Ok($"area = {25 / 2.47105}\nqi = {48 / 35.3147}\nqo = {18 / 35.3147}\nCN = 82\nP = {4.55 * 25.4}\n", UnitSystem.SI);
        Assert.InRange(Vol(si, "TR-55") / (Vol(us, "TR-55") * 1233.48), 0.999, 1.001);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        var u = new DesignToolContext(UnitSystem.USCustomary);
        Assert.Contains(Tool.Run("area = 25\nqi = 18\nqo = 48\nCN = 80\nP = 4\n", u).Messages, m => m.Code == "E-SE-002");
        Assert.Contains(Tool.Run("area = 25\nqi = 48\nqo = 18\nCN = 80\nP = 4\nrainfall type = IV\n", u).Messages, m => m.Code == "E-SE-003");
        Assert.Contains(Tool.Run("area = 25\nqi = 48\nqo = 18\nC = 0.5\ntc = 10\nfrequency = 25\n", u).Messages, m => m.Code == "E-SE-008");
        Assert.Contains(Tool.Run("area = 25\nqi = 48\nqo = 18\n", u).Messages, m => m.Code == "E-SE-009");
        Assert.Contains(Ok("area = 25\nqi = 48\nqo = 45\nCN = 82\nP = 4.55\n").Messages, m => m.Code == "W-SE-001");
    }
}
