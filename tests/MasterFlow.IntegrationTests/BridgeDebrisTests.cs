using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Bridge debris and forces: hand calculations of the effective width, CSU scour, drag, lift and buoyancy.</summary>
public class BridgeDebrisTests
{
    private static readonly BridgeDebrisTool Tool = new();

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static Dictionary<string, object?> Rows(ToolTable t) => t.Rows.ToDictionary(x => (string)x[0]!, x => x[1]);

    [Fact]
    public void Samples_RunInBothUnits()
    {
        foreach (var u in new[] { UnitSystem.USCustomary, UnitSystem.SI }) Run(Tool.SampleInput(new DesignToolContext(u)), u);
    }

    [Fact]
    public void EffectiveWidthWithDebris()
    {
        // y 15, a 3, W 40, T 7.5, rectangular: (0.79·7.5·40 + (15 − 5.925)·3)/15 = (237 + 27.225)/15 = 17.615 ft.
        Assert.Equal(17.615, BridgeDebrisTool.EffectiveWidth(0.79, 7.5, 40, 15, 3), 9);
        // No raft (T = 0) gives the pier width.
        Assert.Equal(3, BridgeDebrisTool.EffectiveWidth(0.79, 0, 40, 15, 3), 9);
    }

    [Fact]
    public void CsuScourAndSkew()
    {
        var rows = Rows(Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary))).Tables[0]);
        double fr = 9 / Math.Sqrt(32.174 * 15), k2 = Math.Pow(Math.Cos(Math.PI / 18) + 10 * Math.Sin(Math.PI / 18), 0.65);
        Assert.Equal(k2, (double)rows["K2 (skew)"]!, 3);
        Assert.Equal(2.0 * 1.0 * k2 * 1.1 * Math.Pow(15 / 3.0, 0.35) * Math.Pow(fr, 0.43) * 3, (double)rows["Pier scour without debris"]!, 2);
        Assert.Equal(2.0 * 1.1 * Math.Pow(15 / 17.615, 0.35) * Math.Pow(fr, 0.43) * 17.615, (double)rows["Pier scour with debris (a_d, K1 = K2 = 1)"]!, 1);
    }

    [Fact]
    public void DragLiftBuoyancy()
    {
        var f = Rows(Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary))).Tables[1]);
        double q = 0.5 * 1.94 * 81;   // 78.57 lb/ft²
        Assert.Equal(q * 0.7 * 3 * 15 / 1000, (double)f["Pier longitudinal drag (CD = 0.7, area a·y)"]!, 2);
        Assert.Equal(q * 0.7 * 30 * 15 / 1000, (double)f["Pier lateral drag (CL = 0.7, area L·y)"]!, 2);
        Assert.Equal(q * 1.4 * 300 / 1000, (double)f["Debris raft drag (CD = 1.4, area 300 ft²)"]!, 2);
        Assert.Equal(q * 2.0 * 1.5 * 100 / 1000, (double)f["Deck drag (CD = 2, area 1.5 ft × span)"]!, 2);
        Assert.Equal(q * -0.4 * 40 * 100 / 1000, (double)f["Deck lift (CL = -0.4, area deck width × span)"]!, 2);
        // 1.5 ft above the low chord, all within the 3.5 ft girder zone with 50 % air: 62.42 × 40 × 100 × 0.75 / 1000.
        Assert.Equal(1.94 * 32.174 * 40 * 100 * 1.5 * 0.5 / 1000, (double)f["Buoyancy (slab below WS, plus 50 % air between girders)"]!, 2);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0.5)]
    [InlineData(7.5, 0.6)]
    [InlineData(15, 0.8)]
    [InlineData(45, 1.0)]
    public void LateralCoefficientTable(double deg, double c) => Assert.Equal(c, BridgeDebrisTool.LateralCoefficient(deg), 9);

    [Fact]
    public void Errors_InundatedDeckNeedsCoefficients()
    {
        string input = Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)).Replace("deck drag coefficient = 2.0", "").Replace("log length = 40", "");
        var r = Tool.Run(input, new DesignToolContext(UnitSystem.USCustomary));
        Assert.Contains(r.Messages, m => m.Code == "E-BD-008");
        Assert.Contains(r.Messages, m => m.Code == "E-BD-005");
    }
}
