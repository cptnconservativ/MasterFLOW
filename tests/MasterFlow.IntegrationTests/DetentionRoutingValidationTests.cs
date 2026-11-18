using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Detention routing: storage from contours, volume tables and basin geometry; risers, v-notches and emergency spillways;
/// entered inflow hydrographs; the §4.10 criteria; and the reports. Expected values come from validation/hand_calcs_detention.py,
/// which integrates storage numerically and routes by both the textbook storage-indication table and a Runge-Kutta solution
/// of the level-pool equation (independent of the engine's code).
/// </summary>
public class DetentionRoutingValidationTests
{
    private const double G = 32.174;
    private static readonly BasinGeometry Basin = new(100, 100, 50, 4, 106);
    private static PondOutlet Orifice6 => new("Low", "orifice", 100, 0.5, 0, 0.6);
    private static PondOutlet Riser3 => new("Riser", "riser", 103, 3, 0, 3.0, OrificeC: 0.6);
    private static PondOutlet Spill => new("Spill", "spillway", 105, 20, 4, 2.6);

    private static double Inflow(double t) => t <= 0 || t >= 90 ? 0 : t <= 30 ? 120 * t / 30 : 120 * (90 - t) / 60;

    private const string ValidationInput = """
        inflow = hydrograph
        allowable Q100 = 120
        top of berm = 107
        [Basin]
        bottom elevation = 100
        bottom length = 100
        bottom width = 50
        side slope = 4
        top elevation = 106
        [Outlets]
        Name	Type	Invert	Size	Height	C	Length	n	Outlet invert	Orifice C
        Low	orifice	100	0.5		0.6
        Riser	riser	103	3		3.0				0.6
        Spill	spillway	105	20	4	2.6
        [Inflow hydrograph]
        Time (min)	100-yr (cfs)
        0	0
        30	120
        90	0
        """;

    [Fact]
    public void BasinGeometry_StorageMatchesNumericalIntegration()
    {
        var pond = Pond.FromBasin(Basin, [Orifice6], G, true);
        Assert.Equal(20976.000, pond.Storage(103), 3);                 // Simpson's rule on A(h), hand_calcs_detention.py
        Assert.Equal(100 * 50 * 3 + 150 * 4 * 9 + 4.0 / 3 * 16 * 27, pond.Storage(103), 9);
        Assert.Equal(0, pond.Storage(99));
        Assert.True(double.IsNaN(pond.Storage(106.01)));               // never extrapolated
        Assert.Equal((100 + 48) * (50 + 48), pond.Stages[^1].Area, 9);
    }

    [Fact]
    public void AverageEndArea_AndVolumeTable_ClosedForm()
    {
        var aea = new Pond([(0, 100), (2, 500)], [Orifice6 with { Invert = 0 }], G, true, StorageMethod.AverageEndArea);
        Assert.Equal(2 * (100 + 500) / 2.0, aea.Storage(2), 9);
        Assert.Equal(1 * (100 + 300) / 2.0, aea.Storage(1), 9);         // area linear in stage: 300 at mid-depth
        var vt = Pond.FromVolumes([(0, 0), (1, 1000), (3, 5000)], [Orifice6 with { Invert = 0 }], G, true);
        Assert.Equal(3000, vt.Storage(2), 9);
        Assert.Equal(500, vt.Storage(0.5), 9);
    }

    [Fact]
    public void Riser_VNotch_Spillway_HandValues()
    {
        // hand_calcs_detention.py: riser at 104.0 = 28.2743 (weir control), at 105.5 = 53.7924 (orifice control); spillway at 105.5 = 19.8556
        Assert.Equal(28.2743, Riser3.Flow(104.0, G), 4);
        Assert.Equal(53.7924, Riser3.Flow(105.5, G), 4);
        Assert.Equal(19.8556, Spill.Flow(105.5, G), 4);
        Assert.Equal(1.4311, new PondOutlet("V", "v-notch", 0, 90, 0, 2.5).Flow(0.8, G), 4);
        // the riser switches from weir to orifice control where C·P·h^1.5 = Co·A·√(2gh): h = Co·A·√(2g)/(C·P)
        double hx = 0.6 * Math.PI * 9 / 4 * Math.Sqrt(2 * G) / (3.0 * Math.PI * 3);
        Assert.Equal(Riser3.RiserWeir(hx), Riser3.RiserOrifice(hx, G), 9);
        Assert.Equal(Riser3.RiserWeir(hx * 0.9), Riser3.Flow(103 + hx * 0.9, G), 9);
        Assert.Equal(Riser3.RiserOrifice(hx * 1.1, G), Riser3.Flow(103 + hx * 1.1, G), 9);
        // box riser: perimeter 2(W+L), area W·L
        var box = new PondOutlet("Box", "riser", 0, 2, 3, 3.0, OrificeC: 0.6);
        Assert.Equal(3.0 * 10 * Math.Pow(0.2, 1.5), box.Flow(0.2, G), 9);
    }

    [Fact]
    public void Spillway_IsNotLimitedByTheOutletPipe()
    {
        var pipe = new PondOutlet("Pipe", "pipe", 99.5, 1.5, 0, 0.5, 60, 0.013, 99.0);
        var pond = Pond.FromBasin(Basin, [Orifice6, Riser3, pipe, Spill], G, true);
        double e = 105.8;
        Assert.Equal(Math.Min(Orifice6.Flow(e, G) + Riser3.Flow(e, G), pond.BarrelAt(e)), pond.PrincipalOutflow(e), 9);
        Assert.True(pond.PrincipalOutflow(e) < Orifice6.Flow(e, G) + Riser3.Flow(e, G));   // the 18 in. pipe governs
        Assert.Equal(pond.PrincipalOutflow(e) + Spill.Flow(e, G), pond.Outflow(e), 9);
        double prev = 0;
        for (double z = 100; z <= 106 + 1e-9; z += 0.002)
        {
            double q = pond.Outflow(z);
            Assert.True(q >= prev - 1e-9, $"outflow falls at {z:F3}");
            prev = q;
        }
    }

    [Fact]
    public void Routing_MatchesIndependentStorageIndicationAndRungeKutta()
    {
        var pond = Pond.FromBasin(Basin, [Orifice6, Riser3, Spill], G, true);
        var hyd = Enumerable.Range(0, 91).Select(t => Inflow(t)).ToList();
        var steps = new List<RouteStep>();
        var r = pond.Route(hyd, 60, steps: steps);
        // hand_calcs_detention.py: table method 113.779 cfs at 33 min, WSE 105.9273; Runge-Kutta 113.685 cfs, WSE 105.9264
        Assert.InRange(r.PeakOutflow, 113.779 * 0.998, 113.779 * 1.002);
        Assert.InRange(r.PeakWse, 105.9264 - 0.003, 105.9273 + 0.003);
        Assert.Equal(33 * 60, r.TimeOfPeakOutflow, 6);
        Assert.True(r.PeakSpillway > 0);
        // level-pool property: the outflow peak lies on the falling limb of the inflow, where I ≈ O
        var pk = steps.First(s => s.TimeMin * 60 == r.TimeOfPeakOutflow);
        Assert.InRange(pk.Inflow - pk.Outflow, -3, 3);
        double vin = Hydrograph.Volume(hyd, 60), vout = Hydrograph.Volume(steps.Select(s => s.Outflow).ToList(), 60);
        Assert.InRange(vout / vin, 0.995, 1.001);
    }

    [Fact]
    public void Tool_HydrographMode_ReproducesTheValidationCase()
    {
        var r = new DetentionTool().Run(ValidationInput, DesignToolTests.Ctx());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        var row = r.Tables[0].Rows.Single();
        Assert.Equal("100-yr", row[0]);
        Assert.InRange((double)row[3]!, 113.779 * 0.998, 113.779 * 1.002);
        Assert.InRange((double)row[4]!, 105.923, 105.930);
        // freeboard: top 107 vs max(routed 105.93, spillway alone at 120 cfs) + 1
        double spill = DetentionTool.SpillwayWse(Pond.FromBasin(Basin, [Orifice6, Riser3, Spill], G, true), 120);
        Assert.Equal(120, Spill.Flow(spill, G), 6);
        var fb = r.Tables[1].Rows.Single(x => (string)x[0]! == "Freeboard");
        Assert.Equal(Math.Round(107 - Math.Max(spill, (double)row[4]!), 2), (double)fb[2]!, 9);
        Assert.Equal(107 - Math.Max(spill, (double)row[4]!) >= 1 ? "OK" : "FAILS", fb[3]);
        Assert.Contains(r.Tables, t => t.Title.StartsWith("Hydrograph 100-yr"));
        Assert.Equal(4, r.Charts.Count);    // hydrograph, WSE, stage–discharge, stage–storage
    }

    [Fact]
    public void Tool_TimeStepAndTableSpacing_DoNotChangeTheAnswer()
    {
        var a = new DetentionTool().Run(ValidationInput.Replace("inflow = hydrograph", "inflow = hydrograph\ntime step = 0.5"), DesignToolTests.Ctx());
        var b = new DetentionTool().Run(ValidationInput.Replace("30\t120\n", "10\t40\n20\t80\n30\t120\n60\t60\n"), DesignToolTests.Ctx());
        Assert.True(a.Succeeded && b.Succeeded);
        Assert.InRange((double)a.Tables[0].Rows[0][3]!, 113.7 * 0.995, 113.7 * 1.005);
        Assert.Equal(113.779, (double)b.Tables[0].Rows[0][3]!, 1);
    }

    [Fact]
    public void Tool_VolumeTableInAcreFeet_IsConverted()
    {
        string input = ValidationInput.Replace("""
            [Basin]
            bottom elevation = 100
            bottom length = 100
            bottom width = 50
            side slope = 4
            top elevation = 106
            """, "[Stage storage]\nElevation (ft)\tVolume (ac-ft)\n100\t0\n103\t0.481543\n106\t1.5\n");
        var r = new DetentionTool().Run(input, DesignToolTests.Ctx());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        var sss = r.Tables.Single(t => t.Title == "Stage–storage–discharge");
        Assert.Equal(0.481543 * 43560, (double)sss.Rows.Single(x => (double)x[0]! == 103)[1]!, 3);
    }

    public static TheoryData<string, string, string> BadInputs() => new()
    {
        { "negative orifice size", "Low\torifice\t100\t0.5\t\t0.6", "Low\torifice\t100\t-0.5\t\t0.6" },
        { "unknown outlet type", "Low\torifice\t100", "Low\tnozzle\t100" },
        { "v-notch over 180 degrees", "Low\torifice\t100\t0.5\t\t0.6", "Low\tv-notch\t100\t200\t\t2.5" },
        { "orifice C above 1", "Low\torifice\t100\t0.5\t\t0.6", "Low\torifice\t100\t0.5\t\t1.6" },
        { "negative flow", "30\t120", "30\t-120" },
        { "time going back", "90\t0", "20\t0" },
        { "time in hours", "Time (min)", "Time (h)" },
        { "flow in m³/s", "100-yr (cfs)", "100-yr (m³/s)" },
        { "negative basin width", "bottom width = 50", "bottom width = -50" },
        { "both basin and stage table", "[Outlets]", "[Stage storage]\nElevation\tArea\n100\t10\n101\t20\n[Outlets]" },
        { "unknown inflow", "inflow = hydrograph", "inflow = scs" },
        { "zero time step", "inflow = hydrograph", "inflow = hydrograph\ntime step = 0" },
    };

    [Theory]
    [MemberData(nameof(BadInputs))]
    public void Tool_RejectsBadInput_WithAnError(string why, string find, string replace)
    {
        Assert.Contains(find, ValidationInput);
        var r = new DetentionTool().Run(ValidationInput.Replace(find, replace), DesignToolTests.Ctx());
        Assert.False(r.Succeeded, why);
        Assert.Empty(r.Tables);
    }

    [Fact]
    public void Tool_SIUnits_AreRejected_NotConverted()
    {
        var r = new DetentionTool().Run(ValidationInput, DesignToolTests.Ctx(UnitSystem.SI));
        Assert.Contains(r.Messages, m => m.Code == "E-DT-001");
    }

    [Fact]
    public void Tool_PipeRunningUphill_AndVolumeTableNotStartingAtZero_AreErrors()
    {
        var t = new DetentionTool(); var ctx = DesignToolTests.Ctx();
        var r = t.Run(t.SampleInput(ctx).Replace("Outlet pipe\tpipe\t99.5\t2.0\t\t0.5\t80\t0.013\t98.7", "Outlet pipe\tpipe\t99.5\t2.0\t\t0.5\t80\t0.013\t99.9"), ctx);
        Assert.Contains(r.Messages, m => m.Code == "E-DT-018");
        r = t.Run(ValidationInput.Replace("[Basin]", "[Stage storage]\nElevation\tVolume (ft³)\n100\t50\n106\t60000\n[Unused]"), ctx);
        Assert.Contains(r.Messages, m => m.Code == "E-DT-003");
    }

    [Fact]
    public void Criteria_RiserSize_Drawdown_Slopes_Spillway()
    {
        var t = new DetentionTool(); var ctx = DesignToolTests.Ctx();
        string s = t.SampleInput(ctx);
        var r = t.Run(s.Replace("Riser\triser\t104.0\t3.0", "Riser\triser\t104.0\t1.0"), ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-DT-009");
        r = t.Run(s.Replace("Low-flow orifice\torifice\t100.0", "Low-flow orifice\torifice\t100.5"), ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-DT-010");
        Assert.Contains(r.Messages, m => m.Code == "W-DT-006");
        r = t.Run(s.Replace("side slope = 4", "side slope = 3").Replace("cross slope = 2", "cross slope = 1"), ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-DT-011");
        Assert.Contains(r.Messages, m => m.Code == "W-DT-012");
        r = t.Run(s.Replace("Emergency spillway\tspillway\t105.0\t20\t4", "Emergency spillway\tspillway\t105.0\t20\t3"), ctx);
        Assert.Contains(r.Messages, m => m.Code == "W-DT-013");
        r = t.Run(s.Replace("trickle capacity = 1.5", "trickle capacity = 0.1"), ctx);
        Assert.Equal("FAILS", r.Tables[1].Rows.Single(x => (string)x[0]! == "Trickle channel capacity")[3]);
    }

    [Fact]
    public void Sample_PassesEveryCriterion_AndReportsRender()
    {
        var t = new DetentionTool(); var ctx = DesignToolTests.Ctx();
        var r = t.Run(t.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded);
        Assert.All(r.Tables[1].Rows, row => Assert.Equal("OK", row[3]));
        var info = new ProjectInfo { Name = "P" };
        string html = ToolReport.ToHtml(info, r, "x", "test");
        Assert.Equal(r.Charts.Count, html.Split("<svg").Length - 1);
        Assert.Contains("Water surface elevation", html);
        var pdf = ToolReport.ToPdf(info, r, t.SampleInput(ctx));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var sheets = ToolReport.ToSheets(r);
        Assert.Contains(sheets, x => x.Name.StartsWith("Hydrograph 100-yr"));
        Assert.Contains(sheets, x => x.Name.StartsWith("Criteria checks"));
    }

    [Fact]
    public void Hydrograph_Resample_StartsAtZero_AndKeepsVolume()
    {
        var pts = new List<(double, double)> { (10, 10), (20, 20), (30, 0) };
        var q = Hydrograph.Resample(pts, 5);
        Assert.Equal([0, 5, 10, 15, 20, 10, 0], q);
        Assert.Equal(Hydrograph.Volume(pts), Hydrograph.Volume(q, 300), 9);
    }

    [Fact]
    public void Tool_WarnsWhenTheHydrographStopsAbruptly()
    {
        var r = new DetentionTool().Run(ValidationInput.Replace("90\t0", "60\t60"), DesignToolTests.Ctx());
        Assert.Contains(r.Messages, m => m.Code == "W-DT-014");
    }
}
