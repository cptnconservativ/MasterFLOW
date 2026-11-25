using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Underground detention: unit section areas, bed storage by hand, units per row for a volume.</summary>
public class UndergroundDetentionTests
{
    private static readonly UndergroundDetentionTool Tool = new();

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static double Total(ToolResult r) => (double)r.Tables[1].Rows[^1][4]!;

    private const string Arch = "unit = arch\nwidth = 4.25\nheight = 2.5\nlength = 7\nrows = 6\nper row = 12\nspacing = 0.5\nedge = 1\nstone below = 0.5\nstone above = 0.5\nbottom = 100\n";

    [Fact]
    public void Samples_RunInBothUnits()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void SectionAreas_AreExact()
    {
        Assert.Equal(Math.PI * 2.125 * 2.5 / 2, UndergroundDetentionTool.ArchArea(2.5, 4.25, 2.5), 9);
        Assert.Equal(Math.PI * 4 / 8, UndergroundDetentionTool.PipeArea(1, 2), 9);
        Assert.Equal(Math.PI, UndergroundDetentionTool.PipeArea(5, 2), 9);
        // Semi-ellipse: half its area lies below about 0.40·h, not 0.5·h.
        Assert.True(UndergroundDetentionTool.ArchArea(1.25, 4.25, 2.5) > 0.5 * UndergroundDetentionTool.ArchArea(2.5, 4.25, 2.5));
    }

    [Fact]
    public void ArchBed_ByHand()
    {
        var r = Ok(Arch);
        double units = 72 * 7 * Math.PI * 2.125 * 2.5 / 2, bed = 86 * 30 * 3.5;
        Assert.Equal(Math.Round(units + 0.4 * (bed - units)), Total(r), 0);
        Assert.Equal(100 + 3.5, (double)r.Tables[1].Rows[^1][0]!, 6);
        Assert.Equal(Math.Round(0.4 * 86 * 30 * 0.35), (double)r.Tables[1].Rows[2][4]!, 0);   // 0.35 ft deep: stone only, below the chambers
    }

    [Fact]
    public void RequiredVolume_FindsUnitsPerRow()
    {
        var r = Ok(Arch + "required volume = 10000\n");
        Assert.Contains(r.Messages, m => m.Code == "W-UD-001");
        string line = r.Summary.Single(s => s.StartsWith("Required"));
        int per = int.Parse(line.Split(": ")[1].Split(' ')[0]);
        double Store(int k) { double u = 6 * k * 7 * Math.PI * 2.125 * 2.5 / 2; return u + 0.4 * ((k * 7 + 2) * 30 * 3.5 - u); }
        Assert.True(Store(per) >= 10000 && Store(per - 1) < 10000);
    }

    [Fact]
    public void PipeAndCurve_Units()
    {
        var pipe = Ok("unit = pipe\ndiameter = 4\nlength = 20\nrows = 3\nper row = 2\nspacing = 1\nedge = 1\nstone below = 0.5\nstone above = 0.5\n");
        double u = 6 * 20 * Math.PI * 4, bed = 42 * 16 * 5;
        Assert.Equal(Math.Round(u + 0.4 * (bed - u)), Total(pipe), 0);
        var curve = Ok("unit = curve\nwidth = 4\nlength = 7\nrows = 1\nper row = 1\nedge = 0\nstone below = 0\nstone above = 0\n[Unit curve]\nDepth,Volume\n0,0\n1,20\n2,40\n");
        Assert.Equal(Math.Round(40 + 0.4 * (28 * 2 - 40)), Total(curve), 0);
    }

    [Fact]
    public void BadInput_IsReported()
    {
        var c = new DesignToolContext(UnitSystem.USCustomary);
        Assert.Contains(Tool.Run("unit = box\nper row = 2\n", c).Messages, m => m.Code == "E-UD-006");
        Assert.Contains(Tool.Run("unit = arch\nwidth = 4\nlength = 7\nper row = 2\n", c).Messages, m => m.Code == "E-UD-002");
        Assert.Contains(Tool.Run("unit = curve\nwidth = 4\nlength = 7\nper row = 2\n[Unit curve]\nDepth,Volume\n0,0\n1,5\n0.5,6\n", c).Messages, m => m.Code == "E-UD-004");
        Assert.Contains(Tool.Run(Arch + "porosity = 1.2\n", c).Messages, m => m.Code == "E-UD-008");
        Assert.Contains(Ok(Arch + "cover = 1\nmin cover = 1.5\n").Messages, m => m.Code == "W-UD-002");
    }
}
