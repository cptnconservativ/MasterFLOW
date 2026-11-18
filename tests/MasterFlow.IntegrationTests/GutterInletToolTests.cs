using MasterFlow.Application.Tools;

namespace MasterFlow.IntegrationTests;

/// <summary>Gutter/inlet tool. Expected values from a separate hand calculation (Python, same published equations), tolerance 0.001.</summary>
public class GutterInletToolTests
{
    private const string Street = "street class = collector\npavement width = 40\ncurb height = 0.5\ncross slope = 0.02\nn = 0.016\n[Inlets]\nInlet\tType\tLoc\tQ\tS\tL\tW\ta\tVo\tArea\th\n";

    [Fact]
    public void Spread_MatchesEq441()
    {
        Assert.Equal(11.2563, GutterHydraulics.Spread(4, 0.016, 0.02, 0.015), 3);
        Assert.Equal(4, GutterHydraulics.Capacity(GutterHydraulics.Spread(4, 0.016, 0.02, 0.015), 0.016, 0.02, 0.015), 9);
    }

    [Fact]
    public void DepressedCurbOpening_OnGrade_HandCalc_AndBypassCarries()
    {
        var r = new GutterInletTool().Run(Street + "CB-3\tcurb\tgrade\t4\t0.015\t10\t2\t2\t\t\t0.5\nCB-4\tcurb\tgrade\t0\t0.015\t10\t2\t2\t\t\t0.5", DesignToolTests.Ctx());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var a = r.Tables[0].Rows[0];
        Assert.Equal(11.2563, (double)a[3]!, 3);       // spread
        Assert.Equal(0.6875, (double)a[7]!, 3);        // E, LT = 21.009 ft with Se = 0.0539
        Assert.Equal(2.7501, (double)a[8]!, 3);        // intercepted
        var b = r.Tables[0].Rows[1];
        Assert.Equal(4 - 2.7501, (double)b[2]!, 3);    // bypass arrives at the next inlet
        Assert.Equal("OK", a[10]);                     // 11.3 ft < (40 − 12)/2 = 14 ft, depth 0.23 < 0.5
        Assert.Equal(2.7501, r.UpdatedNetwork!.Structures.Single(s => s.Name == "CB-3").Inflow!.Value, 3);
    }

    [Fact]
    public void SpreadOverLimit_IsFlagged()
    {
        var r = new GutterInletTool().Run(Street.Replace("collector", "arterial4") + "CB-3\tcurb\tgrade\t6\t0.005\t5\t\t\t\t\t0.5", DesignToolTests.Ctx());
        Assert.StartsWith("FAILS", (string)r.Tables[0].Rows[0][10]!);   // allowed (40 − 36)/2 = 2 ft
        Assert.Contains(r.Messages, m => m.Code == "W-GI-001");
        Assert.Contains(r.Messages, m => m.Code == "W-GI-002");
    }

    [Fact]
    public void SagGrate_DepthSatisfiesWeirOrOrifice_WithClogging()
    {
        var r = new GutterInletTool().Run(Street + "CB-3\tgrate\tsag\t3\t\t3\t2\t\t\t4", DesignToolTests.Ctx());
        double d = (double)r.Tables[0].Rows[0][4]!;
        // clogged 40 %: P = 1.8 + 2·1.2 = 4.2 ft, A = 2.4 ft²; weir 3.0·4.2·d^1.5 = 3 → d = 0.385 ft; orifice gives far more.
        Assert.Equal(Math.Pow(3 / (3.0 * 4.2), 1 / 1.5), d, 4);
        Assert.Equal(d / 0.02, (double)r.Tables[0].Rows[0][3]!, 6);
    }

    [Fact]
    public void GrateOnGrade_NeedsSplashOverVelocity()
    {
        var r = new GutterInletTool().Run(Street + "CB-3\tgrate\tgrade\t2\t0.01\t3\t2", DesignToolTests.Ctx());
        Assert.Contains(r.Messages, m => m.Code == "E-GI-011");
    }
}
