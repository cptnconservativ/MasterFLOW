using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-20 Level 2 stream stability: hand calculations of the wide-channel hydraulics, Shields, Seq, armoring and Lane.</summary>
public class StreamStabilityTests
{
    private static readonly StreamStabilityTool Tool = new();

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static Dictionary<string, object?> Rows(ToolResult r) => r.Tables[0].Rows.ToDictionary(x => (string)x[0]!, x => x[1]);

    [Fact]
    public void Samples_RunInBothUnits()
    {
        foreach (var u in new[] { UnitSystem.USCustomary, UnitSystem.SI }) Run(Tool.SampleInput(new DesignToolContext(u)), u);
    }

    [Fact]
    public void WideChannelShieldsAndCriticalSize()
    {
        var rows = Rows(Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary))));
        // y = (37.5 × 0.035 / (1.486 √0.004))^0.6 = 4.864 ft; τ = 62.4 × 4.864 × 0.004 = 1.214 lb/ft².
        Assert.Equal(4.864, (double)rows["Normal depth (wide channel)"]!, 3);
        Assert.Equal(1.214, (double)rows["Bed shear τ = γ·y·S0"]!, 3);
        // Dc = 1.214 / (0.047 × 1.65 × 62.4) = 0.2509 ft = 76.5 mm.
        Assert.Equal(76.5, (double)rows["Critical size Dc = τ/(F*·(Ss − 1)·γ)"]!, 1);
        Assert.Equal(62.4 * 3000 * 0.004 / 80, (double)rows["Specific stream power γ·Q·S/W"]!, 2);
    }

    [Fact]
    public void EquilibriumSlope_GivesCriticalShearAtNormalDepth()
    {
        double q = 37.5, n = 0.035, d = 25 / 304.8;
        double seq = StreamStabilityTool.EquilibriumSlope(q, n, 1.486, d, 0.047, 2.65);
        double y = StreamStabilityTool.WideDepth(q, n, seq, 1.486);
        Assert.Equal(0.047 * 1.65 * d, y * seq, 9);   // y·S = F*(Ss − 1)·D at Seq
    }

    [Fact]
    public void ArmoringDepth()
    {
        // Gradation with 20 % coarser than Dc: ys = ya·(1/0.2 − 1) = 4·ya.
        var g = new List<(double, double)> { (1, 0), (10, 50), (1000, 100) };
        Assert.Equal(50, StreamStabilityTool.PercentFiner(g, 10), 9);
        Assert.Equal(75, StreamStabilityTool.PercentFiner(g, 100), 9);   // halfway in log between 10 and 1000
        Assert.Equal(100, StreamStabilityTool.SizeAt(g, 75), 6);
        var rows = Rows(Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary))));
        double pc = (double)rows["Fraction coarser than Dc (76.5 mm)"]!, ya = (double)rows["Armor layer thickness ya"]!;
        Assert.Equal(2 * 0.2509, ya, 2);
        Assert.InRange((double)rows["Degradation to armor ys = ya·(1/Pc − 1)"]! - ya * (1 / pc - 1), -0.03, 0.03);   // table values are rounded
    }

    [Fact]
    public void LeopoldWolmanAndLane()
    {
        Assert.Equal(0.06 * Math.Pow(1400, -0.44), StreamStabilityTool.BraidingSlope(1400), 9);
        var r = Run("Q = 500\nwidth = 40\nslope = 0.001\nn = 0.03\nD50 = 50\n[Change]\nQs = 2\n");
        Assert.Contains(r.Summary, s => s.Contains("aggrades"));
        Assert.Contains(r.Summary, s => s.Contains("no degradation"));
    }

    [Fact]
    public void Errors()
    {
        var r = Tool.Run("Q = 100\n[Gradation]\nSize, Finer\n10, 50\n5, 60\n", new DesignToolContext(UnitSystem.USCustomary));
        Assert.Contains(r.Messages, m => m.Code == "E-SS-001");
        Assert.Contains(r.Messages, m => m.Code == "E-SS-005");
    }
}
