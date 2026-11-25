using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Slope-area: conveyance and α by hand, velocity-head recovery, reach criteria.</summary>
public class SlopeAreaTests
{
    private static readonly SlopeAreaTool Tool = new();

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static string Rect(string name, double bed, double ws, double len, double width = 20) =>
        $"[{name}]\nws = {ws}\nlength = {len}\nn = 0.03\nStation,Elevation\n0,{bed + 10}\n0,{bed}\n{width},{bed}\n{width},{bed + 10}\n";

    [Fact]
    public void Samples_RunInBothUnits()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void UniformRectangle_ByHand()
    {
        var r = Ok(Rect("A", 100, 105, 200) + Rect("B", 99.8, 104.8, 0));
        double k = 1.486 / 0.03 * 100 * Math.Pow(100.0 / 30, 2.0 / 3);
        Assert.Equal(Math.Round(k), (double)r.Tables[0].Rows[0][4]!, 0);
        Assert.Equal(1.0, (double)r.Tables[0].Rows[0][5]!, 6);
        Assert.Equal(Math.Round(k * Math.Sqrt(0.001)), (double)r.Tables[1].Rows[0][5]!, 0);
    }

    [Fact]
    public void ContractingReach_RecoversFullVelocityHead()
    {
        // Upstream 30 ft wide, downstream 20 ft: velocity rises downstream, so the full Δhv counts.
        var pu = SlopeAreaTool.Section([(0, 110), (0, 100), (30, 100), (30, 110)], 105, [], [0.03], 1.486);
        var pd = SlopeAreaTool.Section([(0, 110), (0, 99.8), (20, 99.8), (20, 110)], 104.6, [], [0.03], 1.486);
        double q = SlopeAreaTool.Solve([new("u", 105, 200, pu), new("d", 104.6, 0, pd)], 32.174);
        double hf = 0.4 + (SlopeAreaTool.Hv(pu, q, 32.174) - SlopeAreaTool.Hv(pd, q, 32.174));
        Assert.Equal(q, Math.Sqrt(pu.K * pd.K) * Math.Sqrt(hf / 200), 6);
    }

    [Fact]
    public void Subsections_SplitConveyance_AndRaiseAlpha()
    {
        var pts = new List<(double, double)> { (0, 106), (10, 103), (30, 103), (35, 98), (55, 98), (60, 103), (80, 103), (90, 106) };
        var one = SlopeAreaTool.Section(pts, 105, [], [0.035], 1.486);
        var three = SlopeAreaTool.Section(pts, 105, [30, 60], [0.06, 0.035, 0.06], 1.486);
        Assert.Equal(one.Area, three.Area, 9);
        Assert.Equal(1.0, one.Alpha, 9);
        Assert.True(three.Alpha > 1.0);
    }

    [Fact]
    public void Criteria_AreFlagged()
    {
        // Tiny fall over a short reach, with a big conveyance change.
        var r = Ok(Rect("A", 100, 105, 50, 30) + Rect("B", 99.99, 104.9, 0, 18));
        Assert.Contains(r.Messages, m => m.Code == "W-SA-001");
    }

    [Fact]
    public void SiMatchesUs()
    {
        var us = Ok(Rect("A", 100, 105, 200) + Rect("B", 99.8, 104.8, 0));
        double f = 0.3048;
        string Rs(string n, double bed, double ws, double len) => $"[{n}]\nws = {ws * f}\nlength = {len * f}\nn = 0.03\nStation,Elevation\n0,{(bed + 10) * f}\n0,{bed * f}\n{20 * f},{bed * f}\n{20 * f},{(bed + 10) * f}\n";
        var si = Ok(Rs("A", 100, 105, 200) + Rs("B", 99.8, 104.8, 0), UnitSystem.SI);
        Assert.Equal((double)us.Tables[1].Rows[0][5]! * 0.0283168, (double)si.Tables[1].Rows[0][5]!, 1);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        var u = new DesignToolContext(UnitSystem.USCustomary);
        Assert.Contains(Tool.Run(Rect("A", 100, 105, 200), u).Messages, m => m.Code == "E-SA-008");
        Assert.Contains(Tool.Run(Rect("A", 100, 105, 0) + Rect("B", 99.8, 104.8, 0), u).Messages, m => m.Code == "E-SA-009");
        Assert.Contains(Tool.Run(Rect("A", 100, 115, 200) + Rect("B", 99.8, 104.8, 0), u).Messages, m => m.Code == "E-SA-005");
        Assert.Contains(Tool.Run(Rect("A", 100, 104, 200) + Rect("B", 99.8, 104.8, 0), u).Messages, m => m.Code == "E-SA-010");
        Assert.Contains(Tool.Run("[A]\nws = 105\nlength = 10\nn = 0.03, 0.04\nStation,Elevation\n0,110\n5,100\n10,110\n" + Rect("B", 99.8, 104.8, 0), u).Messages, m => m.Code == "E-SA-004");
    }
}
