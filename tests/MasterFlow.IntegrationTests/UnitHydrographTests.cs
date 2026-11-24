using MasterFlow.Application.Hydrology;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Clark, Snyder and CUHP unit hydrographs and their use in hydrograph routing.</summary>
public class UnitHydrographTests
{
    private static double Volume(double[] u, double dtH) => u.Sum() * dtH * 3600;

    [Fact]
    public void TimeArea_IsTheHmsCurve()
    {
        Assert.Equal(0, UnitHydrographs.TimeArea(0));
        Assert.Equal(1.414 * Math.Pow(0.5, 1.5), UnitHydrographs.TimeArea(0.5), 9);
        Assert.Equal(0.5, UnitHydrographs.TimeArea(0.5), 3);
        Assert.Equal(1, UnitHydrographs.TimeArea(1.2));
    }

    [Fact]
    public void Clark_HoldsOneInchAndAttenuatesWithR()
    {
        double dt = 5.0 / 60;
        var a = UnitHydrographs.Clark(640, 1.0, 0.5, dt);
        var b = UnitHydrographs.Clark(640, 1.0, 2.0, dt);
        Assert.Equal(640 * 43560 / 12.0, Volume(a, dt), 0);
        Assert.Equal(640 * 43560 / 12.0, Volume(b, dt), 0);
        Assert.True(b.Max() < a.Max());
        // Linear reservoir recession: successive late ordinates fall by CB = 1 − Δt/(R + Δt/2).
        double cb = 1 - dt / (2.0 + dt / 2);
        int k = b.Length - 5;
        Assert.Equal(cb, b[k + 1] / b[k], 4);
    }

    [Fact]
    public void Snyder_PeakMatchesSnyderEquation()
    {
        double dt = 0.25, tp = UnitHydrographs.SnyderLag(2.0, 5, 2.5);
        Assert.Equal(2.0 * Math.Pow(12.5, 0.3), tp, 9);
        var (u, qp, tPeak, m) = UnitHydrographs.Snyder(3200, tp, 0.6, dt);
        double tpr = tp + (dt - tp / 5.5) / 4;
        Assert.Equal(640 * 0.6 * 5 / tpr, qp, 6);
        Assert.Equal(tpr + dt / 2, tPeak, 9);
        Assert.Equal(3200 * 43560 / 12.0, Volume(u, dt), 0);
        Assert.InRange(u.Max() / qp, 0.95, 1.05);   // volume scaling barely changes the peak
        Assert.True(m > 0);
    }

    [Fact]
    public void Cuhp_Lag()
    {
        Assert.Equal(0.1 * Math.Pow(1.0 * 0.5 / Math.Sqrt(0.01), 0.48), UnitHydrographs.CuhpLag(0.1, 1.0, 0.5, 0.01), 9);
    }

    [Fact]
    public void Routing_UsesEachUnitHydrograph()
    {
        string input = """
            storms = 100
            rainfall = type-ii
            P100 = 4.5
            time step = 2
            simulation = 1800
            [Subbasins]
            Name	Node	Area (ac)	CN	Tc (min)	UH	Clark R (min)	Ct	Cp	L (mi)	Lca (mi)	Slope
            A	OUT	300	80	45	scs
            B	OUT	300	80	45	clark	30
            C	OUT	300	80		snyder		1.8	0.6	1.2	0.6
            D	OUT	300	80		cuhp		0.12	0.6	1.2	0.6	0.01
            """;
        var r = new HydrographRoutingTool().Run(input, new DesignToolContext(UnitSystem.USCustomary));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var rows = r.Tables.Single(t => t.Title == "Subbasin runoff").Rows;
        Assert.StartsWith("NRCS", (string)rows.Single(x => (string)x[1]! == "A")[12]!);
        Assert.StartsWith("Clark", (string)rows.Single(x => (string)x[1]! == "B")[12]!);
        Assert.StartsWith("Snyder", (string)rows.Single(x => (string)x[1]! == "C")[12]!);
        Assert.StartsWith("CUHP", (string)rows.Single(x => (string)x[1]! == "D")[12]!);
        // Same runoff volume for each (same CN and rain); Clark with R attenuates the NRCS peak.
        var vols = rows.Select(x => (double)x[9]!).ToList();
        Assert.True(vols.Max() - vols.Min() < 0.02 * vols.Max());
        Assert.True((double)rows.Single(x => (string)x[1]! == "B")[10]! < (double)rows.Single(x => (string)x[1]! == "A")[10]!);
    }
}
