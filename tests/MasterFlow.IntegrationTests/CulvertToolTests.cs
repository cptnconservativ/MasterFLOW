using MasterFlow.Application.Culverts;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// HDS-5 culverts. Reference numbers come from validation/hand_calcs_culvert.py, written separately from the engine
/// straight from the HDS-5 (2012) equations; the rest are closed-form identities and edge cases.
/// </summary>
public class CulvertToolTests
{
    private static CulvertDefinition Pipe48(string inlet = "circ-conc-square-headwall", double up = 100, double dn = 99, double length = 200) => new()
    {
        Shape = BarrelShape.Circular, Rise = 4, N = 0.012, Length = length, UpstreamInvert = up, DownstreamInvert = dn,
        Inlet = CulvertInlets.Find(inlet)!, Ke = 0.5,
    };

    private static CulvertDefinition Box64(string inlet, double s = 0.01) => new()
    {
        Shape = BarrelShape.Box, Rise = 4, Span = 6, N = 0.012, Length = 100, UpstreamInvert = 100, DownstreamInvert = 100 - 100 * s,
        Inlet = CulvertInlets.Find(inlet)!, Ke = 0.5,
    };

    private static CulvertAnalysis Us(CulvertDefinition c) => new(c, UnitSystem.USCustomary);

    [Fact]
    public void InletControl_Circular_MatchesHandCalcs_InEachRegime()
    {
        var a = Us(Pipe48());
        var r = a.InletControl(30);                       // Form 1 unsubmerged
        Assert.Equal("unsubmerged", r.Regime);
        Assert.Equal(1.193662, r.X, 5);
        Assert.Equal(1.624483, r.Dc, 5);
        Assert.Equal(2.234074, r.Hc, 5);
        Assert.Equal(2.279927, r.Headwater, 5);
        r = a.InletControl(90);                           // transition
        Assert.Equal("transition", r.Regime);
        Assert.Equal(4.725942, r.Headwater, 4);
        r = a.InletControl(120);                          // submerged
        Assert.Equal("submerged", r.Regime);
        Assert.Equal(6.299325, r.Headwater, 5);
    }

    [Fact]
    public void InletControl_IsContinuousAndRising_ThroughTheTransition()
    {
        var a = Us(Pipe48());
        double area = Math.PI * 4, q35 = 3.5 * area * 2, q40 = 4.0 * area * 2;
        Assert.Equal(a.InletControl(q35).Headwater, a.InletControl(q35 + 1e-6).Headwater, 4);
        Assert.Equal(a.InletControl(q40).Headwater, a.InletControl(q40 - 1e-6).Headwater, 4);
        double prev = 0;
        for (double q = 1; q <= 300; q += 1) { double h = a.InletControl(q).Headwater; Assert.True(h > prev, $"Q {q}"); prev = h; }
    }

    [Fact]
    public void InletControl_Box_Form1Form2AndSubmerged_MatchHandCalcs()
    {
        var f2 = Us(Box64("box-headwall-chamfer")).InletControl(100);
        Assert.Equal(2.083333, f2.X, 5);
        Assert.Equal(3.361084, f2.Headwater, 5);
        var f1 = Us(Box64("box-wingwall-30-75")).InletControl(100);
        Assert.Equal(2.051465, f1.Dc, 4);                 // dc = (q²/g)^(1/3)
        Assert.Equal(3.077198, f1.Hc, 5);                 // 1.5 dc
        Assert.Equal(3.273864, f1.Headwater, 5);
        Assert.Equal(6.985191, Us(Box64("box-wingwall-30-75")).InletControl(250).Headwater, 5);
    }

    [Fact]
    public void Mitered_UsesPlusPoint7SlopeCorrection()
    {
        var m = CulvertInlets.Find("circ-cmp-mitered")!;
        Assert.Equal(0.7, m.Ks);
        var flat = Us(Pipe48("circ-cmp-mitered", 100, 100 - 1e-9)).InletControl(150).Headwater;
        var sloped = Us(Pipe48("circ-cmp-mitered", 100, 96)).InletControl(150).Headwater;   // S = 0.02
        Assert.Equal(4 * 0.7 * 0.02, sloped - flat, 6);
    }

    [Fact]
    public void OutletControl_MatchesHandCalcs_FullAndPartlyFullOutlet()
    {
        var a = Us(Pipe48());
        Assert.Equal(0.839247, a.FrictionCoefficient, 5);
        var full = a.OutletControl(120, 5.0);
        Assert.Equal(3.315002, full.H, 5);
        Assert.Equal(7.315002, full.Headwater, 5);
        Assert.Equal(full.Headwater, full.HeadwaterBackwater, 9);   // submerged outlet: both methods are the same full-flow energy balance
        var low = a.OutletControl(120, 2.0);
        Assert.Equal(3.649148, low.Ho, 5);                // (dc + D)/2
        Assert.Equal(5.964150, low.Headwater, 5);
    }

    [Fact]
    public void Backwater_LongMildBarrel_ReachesNormalDepthAtTheInlet()
    {
        var a = Us(Pipe48(length: 3000, up: 106, dn: 100));   // S = 0.002 (mild for 60 cfs)
        double q = 60, yn = a.NormalDepth(q);
        Assert.True(yn < 4 && yn > a.CriticalDepth(q));
        var g = a.Geometry;
        double v = q / g.Area(yn), expected = yn + 1.5 * v * v / (2 * 32.174);
        var (hw, note) = a.Backwater(q, 0, a.CriticalDepth(q));
        Assert.Equal(expected, hw, 2);
        Assert.Equal("partly full throughout", note);
    }

    [Fact]
    public void SteepBarrel_InletControls_OutletAtNormalDepth()
    {
        var a = Us(Pipe48(up: 104, dn: 100));                 // S = 0.02
        var pt = a.Evaluate(60, 0.5);
        Assert.Equal("inlet", pt.Control);
        Assert.True(pt.NormalDepth < pt.CriticalDepth);
        Assert.Equal(pt.NormalDepth, pt.OutletDepth, 9);
        Assert.Equal(60 / a.Geometry.Area(pt.NormalDepth), pt.OutletVelocity, 9);
    }

    [Fact]
    public void MultipleBarrels_SplitTheFlow()
    {
        var one = Us(Pipe48()).Evaluate(60, 2);
        var two = Us(Pipe48() with { Barrels = 2 }).Evaluate(120, 2);
        Assert.Equal(one.Headwater, two.Headwater, 9);
        Assert.Equal(one.OutletVelocity, two.OutletVelocity, 9);
    }

    [Fact]
    public void Roadway_LevelCrest_IsBroadCrestedWeir_AndBalancesFlow()
    {
        var road = new RoadwayCrest([(0, 107.5), (100, 107.5)], 3.0);
        Assert.Equal(146.456546, road.Flow(108.12, 0), 4);
        Assert.Equal(146.456546 * 0.616307, road.Flow(108.12, 107.5 + 0.8 * 0.62), 3);   // Villemonte, ht/h = 0.8
        Assert.Equal(0, road.Flow(108.12, 109), 9);                                        // tailwater above headwater
        var a = Us(Pipe48());
        var pt = a.Solve(300, _ => 2, road);
        Assert.True(pt.OvertoppingFlow > 0);
        Assert.Equal(300, pt.CulvertFlow + pt.OvertoppingFlow, 4);
        Assert.Equal(a.Evaluate(pt.CulvertFlow, 2).HeadwaterElevation, pt.HeadwaterElevation, 9);
        Assert.Equal(road.Flow(pt.HeadwaterElevation, 99 + 2), pt.OvertoppingFlow, 6);
        var below = a.Solve(60, _ => 2, road);
        Assert.Equal(0, below.OvertoppingFlow);
    }

    [Fact]
    public void SI_GivesTheSameHeadwaterAsUS()
    {
        const double ft = 0.3048;
        var si = new CulvertAnalysis(Pipe48() with { Rise = 4 * ft, Length = 200 * ft, UpstreamInvert = 100 * ft, DownstreamInvert = 99 * ft }, UnitSystem.SI);
        double q = 120 * ft * ft * ft;
        Assert.Equal(1.919657, si.InletControl(q).Headwater, 5);                 // hand calc, Ku = 1.811
        Assert.Equal(Us(Pipe48()).InletControl(120).Headwater, si.InletControl(q).Headwater / ft, 2);
        Assert.Equal(Us(Pipe48()).OutletControl(120, 2).Headwater, si.OutletControl(q, 2 * ft).Headwater / ft, 2);
        Assert.Equal(Us(Pipe48()).InletControl(30).Headwater, si.InletControl(30 * ft * ft * ft).Headwater / ft, 2);
    }

    [Fact]
    public void Tool_Samples_RunInBothUnitSystems_WithChartAndChecks()
    {
        var t = new CulvertTool();
        foreach (var u in new[] { UnitSystem.USCustomary, UnitSystem.SI })
        {
            var ctx = new DesignToolContext(u);
            var r = t.Run(t.SampleInput(ctx), ctx);
            Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
            Assert.Single(r.Charts);
            Assert.Contains(r.Tables, x => x.Title.StartsWith("Criteria"));
            Assert.All(r.Tables.Single(x => x.Title.StartsWith("Criteria")).Rows, row => Assert.Equal("PASS", row[3]));
            var html = ToolReport.ToHtml(new ProjectInfo { Name = "P" }, r, "x", "test");
            Assert.Contains("<svg", html);
            Assert.Contains("Performance curve", html);
        }
        var us = t.Run(t.SampleInput(new(UnitSystem.USCustomary)), new(UnitSystem.USCustomary));
        var design = us.Tables.Single(x => x.Title == "Results").Rows[0];
        Assert.Equal(6.299325, (double)design[7]!, 5);    // HW: submerged inlet control, as hand calc A
        Assert.Equal("inlet", design[10]);
    }

    [Fact]
    public void Tool_ReportsFailures_AndBadInput()
    {
        var t = new CulvertTool();
        var ctx = new DesignToolContext(UnitSystem.USCustomary);
        string s = t.SampleInput(ctx);
        var r = t.Run(s.Replace("Q design = 120", "Q design = 140"), ctx);                 // freeboard < 1 ft
        Assert.Contains(r.Messages, m => m.Code == "W-CV-004");
        Assert.Contains(r.Tables.Single(x => x.Title.StartsWith("Criteria")).Rows, row => (string)row[3]! == "FAIL");
        Assert.Contains(t.Run(s.Replace("n = 0.012", "n = -0.012"), ctx).Messages, m => m.Code == "E-CV-004");
        Assert.Contains(t.Run(s.Replace("D = 4", "D = 0"), ctx).Messages, m => m.Code == "E-CV-002");
        Assert.Contains(t.Run(s.Replace("circ-conc-square-headwall", "box-wingwall-0"), ctx).Messages, m => m.Code == "E-CV-008");
        var unknown = t.Run(s.Replace("circ-conc-square-headwall", "nonsense"), ctx);
        Assert.Contains(unknown.Messages, m => m.Code == "E-CV-007" && m.Text.Contains("circ-cmp-mitered"));
        Assert.Contains(t.Run(s.Replace("barrels = 1", "barrels = 1.5"), ctx).Messages, m => m.Code == "E-CV-003");
        Assert.Contains(t.Run(s.Replace("upstream invert = 100.00", "upstream invert = 98.50"), ctx).Messages, m => m.Code == "W-CV-001");
        Assert.Contains(t.Run(s.Replace("0, 111.0", "0, 99.0"), ctx).Messages, m => m.Code == "E-CV-015");
        Assert.Contains(t.Run(s.Replace("inlet = circ-conc-square-headwall", "inlet = circ-cmp-projecting"), ctx).Tables.Single(x => x.Title.StartsWith("Criteria")).Rows,
            row => ((string)row[3]!).StartsWith("REVIEW"));
        var custom = t.Run(s.Replace("inlet = circ-conc-square-headwall", "inlet = custom\nform = 1\nK = 0.0098\nM = 2\nc = 0.0398\nY = 0.67"), ctx);
        Assert.True(custom.Succeeded);
        Assert.Equal(6.299325, (double)custom.Tables.Single(x => x.Title == "Results").Rows[0][7]!, 5);
        Assert.Contains(t.Run(s.Replace("inlet = circ-conc-square-headwall", "inlet = custom\nform = 3\nK = 1\nM = 1\nc = 1\nY = 1"), ctx).Messages, m => m.Code == "E-CV-006");
        var bw = t.Run(s.Replace("outlet method = hds5", "outlet method = backwater"), ctx);
        Assert.True(bw.Succeeded);
        var tw = t.Run(s.Replace("tailwater = channel", "tailwater depth = 5"), ctx);   // submerged outlet: outlet control
        Assert.Equal(7.315002, (double)tw.Tables.Single(x => x.Title == "Results").Rows[0][6]!, 5);
        Assert.Equal("outlet", tw.Tables.Single(x => x.Title == "Results").Rows[0][10]);
    }
}
public class CulvertSampleWriter
{
    [Fact]
    public void WriteSample()
    {
        var dir = Environment.GetEnvironmentVariable("CULVERT_SAMPLE_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        var t = new CulvertTool();
        var ctx = new DesignToolContext(UnitSystem.USCustomary);
        string input = t.SampleInput(ctx);
        var r = t.Run(input, ctx);
        File.WriteAllText(Path.Combine(dir, "Culvert HDS-5 sample.html"), ToolReport.ToHtml(new ProjectInfo { Name = "Sample culvert", PreparedBy = "MasterFlow" }, r, input, "sample"));
        File.WriteAllBytes(Path.Combine(dir, "Culvert HDS-5 sample.xlsx"), XlsxWriter.ToBytes(ToolReport.ToSheets(r)));
    }
}
