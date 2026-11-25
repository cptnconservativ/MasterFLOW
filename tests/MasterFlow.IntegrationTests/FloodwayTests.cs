using System.Text;
using MasterFlow.Application.River;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Floodway encroachment (HEC-RAS Method 4 / FEMA 1 ft surcharge) in the natural channel profile tool.</summary>
public class FloodwayTests
{
    private static readonly RiverProfileTool Tool = new();

    /// <summary>A symmetric compound channel: 30 ft channel 5 ft deep, 200 ft flat overbanks each side, falling 2 ft per 1000 ft.</summary>
    private static string Model(string extra)
    {
        var sb = new StringBuilder();
        sb.AppendLine("regime = subcritical\ndownstream = normal\ndownstream slope = 0.002\nfloodway profile = 100-yr");
        sb.AppendLine(extra);
        sb.AppendLine("[Flows]\nRS\t100-yr\n2000\t3000");
        sb.AppendLine("[Sections]\nRS\tLeft bank\tRight bank\tLOB length\tCh length\tROB length\tn LOB\tn Ch\tn ROB");
        foreach (int rs in new[] { 2000, 1000, 0 }) sb.AppendLine($"{rs}\t200\t230\t1000\t1000\t1000\t0.06\t0.035\t0.06");
        sb.AppendLine("[Points]\nRS\tStation\tElevation");
        foreach (int rs in new[] { 2000, 1000, 0 })
        {
            double b = 100 + rs * 0.002;
            foreach (var (x, z) in new[] { (0.0, 8.0), (10, 0), (200, 0), (205, -5), (225, -5), (230, 0), (420, 0), (430, 8) })
                sb.AppendLine($"{rs}\t{x}\t{b + z}");
        }
        return sb.ToString();
    }

    private static ToolResult Ok(string input)
    {
        var r = Tool.Run(input, new DesignToolContext(UnitSystem.USCustomary));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static IReadOnlyList<IReadOnlyList<object?>> Fw(ToolResult r) => r.Tables.Single(t => t.Title.StartsWith("Floodway")).Rows;

    [Fact]
    public void Method4_MatchesConveyanceAndIsSymmetric()
    {
        var pts = new List<(double, double, double?)> { (0, 108, null), (10, 100, null), (200, 100, null), (205, 95, null), (225, 95, null), (230, 100, null), (420, 100, null), (430, 108, null) };
        var s = new RiverSection("A", 0, pts, 200, 230, 0.06, 0.035, 0.06, 1.486);
        double ws = 102;
        var (l, rr) = RiverProfileTool.Method4(s, ws, 1.0);
        Assert.Equal(s.At(ws).Conveyance, s.Encroach(l, rr).At(ws + 1).Conveyance, 0);
        Assert.Equal(200 - l, rr - 230, 1);
        Assert.InRange(l, 10, 200);
    }

    [Fact]
    public void Floodway_SurchargeWithinOneFoot()
    {
        var r = Ok(Model(""));
        var rows = Fw(r);
        Assert.Equal(3, rows.Count);
        foreach (var row in rows)
        {
            Assert.InRange((double)row[3]!, -0.01, 1.005);
            Assert.Equal("OK", row[11]);
        }
        Assert.Contains(rows, row => (double)row[3]! > 0.5);   // the floodway really encroaches
        Assert.Contains(r.Charts[0].Series, s => s.Name.Contains("floodway"));
    }

    [Fact]
    public void FixedEncroachments_AreUsedAndChecked()
    {
        var r = Ok(Model("[Encroachments]\nRS\tLeft\tRight\n1000\t195\t235\n").Replace("[Encroachments]\nRS\tLeft\tRight\n1000\t195\t235\n[Flows]", "[Flows]") + "[Encroachments]\nRS\tLeft\tRight\n1000\t195\t235\n");
        var row = Fw(r).Single(x => (string)x[0]! == "1000");
        Assert.Equal("fixed", row[4]);
        Assert.Equal(195.0, (double)row[5]!, 1);
        Assert.Equal(235.0, (double)row[6]!, 1);
        Assert.Equal("EXCEEDS", row[11]);
        Assert.Contains(r.Messages, m => m.Code == "W-FW-001");
    }

    [Fact]
    public void UnknownFloodwayProfile_IsAnError()
    {
        var r = Tool.Run(Model("").Replace("floodway profile = 100-yr", "floodway profile = 25-yr"), new DesignToolContext(UnitSystem.USCustomary));
        Assert.Contains(r.Messages, m => m.Code == "E-FW-001");
    }
}
