using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-23 pier (DG 11) and abutment (DG 14) riprap, by hand.</summary>
public class ScourCountermeasureTests
{
    private static readonly ScourCountermeasureTool Tool = new();
    private const double G = 32.2;

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static IReadOnlyList<object?> Row(ToolResult r, string table, string name) =>
        r.Tables.Single(t => t.Title.StartsWith(table)).Rows.Single(x => (string)x[0]! == name);

    [Fact]
    public void Sample_RunsCleanInBothUnitSystems()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void PierRiprap_ByHand()
    {
        var r = Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        // Pier 1: round, K = 1.5, V = 8.5 ft/s: D50 = 0.692·12.75²/(1.65·64.4) = 1.059 ft = 12.7 in. → Class IV.
        var p1 = Row(r, "Riprap at piers", "Pier 1");
        Assert.Equal(0.692 * Math.Pow(1.5 * 8.5, 2) / (1.65 * 2 * G) * 12, (double)p1[6]!, 1);
        Assert.Equal("IV", p1[7]);
        Assert.Equal(3 * 15 / 12.0, (double)p1[9]!, 2);
        Assert.Equal(8.0, (double)p1[10]!, 2);
        // Pier 2: rectangular, K = 1.7, V = 1.5 × 6 = 9 ft/s.
        var p2 = Row(r, "Riprap at piers", "Pier 2");
        Assert.Equal(9.0, (double)p2[4]!, 2);
        Assert.Equal(0.692 * Math.Pow(1.7 * 9, 2) / (1.65 * 2 * G) * 12, (double)p2[6]!, 1);
    }

    [Fact]
    public void AbutmentRiprap_ByHand_BothFroudeRanges()
    {
        var r = Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        // Left: SBR 8 ≥ 5, V = Qf/Af = 2.5 ft/s, y = 5, spill-through, Fr = 0.197 → Eq. 14.1, K 0.89.
        var left = Row(r, "Riprap at abutments", "Left");
        Assert.Equal(2.5, (double)left[3]!, 3);
        Assert.Equal(5 * 0.89 / 1.65 * (6.25 / (G * 5)) * 12, (double)left[8]!, 1);
        Assert.Equal(10.0, (double)left[12]!, 2);   // apron 2y
        // Right: vertical wall, V = 6.2, y = 6.5: D50 = 6.5·1.02/1.65·(6.2²/(g·6.5)) = 0.738 ft = 8.9 in. → Class II.
        var right = Row(r, "Riprap at abutments", "Right");
        Assert.Equal(8.9, (double)right[8]!, 1);
        Assert.Equal("II", right[9]);
        // Fr > 0.8 uses Eq. 14.2.
        double d = ScourCountermeasureTool.AbutmentD50(12, 3, 2.65, G, false);
        Assert.Equal(3 * 0.61 / 1.65 * Math.Pow(144 / (G * 3), 0.14), d, 6);
    }

    [Fact]
    public void ApronIsCappedAt25Feet()
    {
        var r = Ok("[Abutments]\nName\tType\ty\tV\nA\tspill-through\t20\t4");
        Assert.Equal(25.0, (double)Row(r, "Riprap at abutments", "A")[12]!, 2);
    }

    [Fact]
    public void MissingVelocity_IsAnError()
    {
        var r = Tool.Run("[Piers]\nName\tShape\ta\nP\tround\t3", new DesignToolContext(UnitSystem.USCustomary));
        Assert.Contains(r.Messages, m => m.Code == "E-SC-004");
    }
}
