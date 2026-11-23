using System.Text;
using MasterFlow.Application.Swmm;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>EPA SWMM 5 exchange: .inp export, round trip back into a network, report comparison.</summary>
public class SwmmExchangeTests
{
    private static readonly SwmmExchangeTool Tool = new();
    private static DesignToolContext Ctx(UnitSystem u = UnitSystem.USCustomary) => DesignToolTests.Ctx(u);

    private static string Inp(ToolResult r) => Encoding.UTF8.GetString(r.Files.Single(f => f.FileName.EndsWith(".inp")).Content);

    [Fact]
    public void Export_WritesEverySectionWithOffsetsFromTheLowestInvert()
    {
        var ctx = Ctx();
        var r = Tool.Run("mode = export", ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        string inp = Inp(r);
        foreach (var s in new[] { "[TITLE]", "[OPTIONS]", "[JUNCTIONS]", "[OUTFALLS]", "[CONDUITS]", "[XSECTIONS]", "[INFLOWS]", "[COORDINATES]", "[REPORT]" })
            Assert.Contains(s, inp);
        Assert.Contains("FLOW_ROUTING         DYNWAVE", inp);
        var sec = SwmmInp.Sections(inp);
        var net = ctx.Network!;
        var inv = SwmmInp.NodeInverts(net);
        foreach (var p in net.Pipes)
        {
            var c = sec["CONDUITS"].Single(x => x[0] == p.Name);
            Assert.Equal(p.UpstreamInvert - inv[p.From], double.Parse(c[5], System.Globalization.CultureInfo.InvariantCulture), 3);
            Assert.Equal(p.DownstreamInvert - inv[p.To], double.Parse(c[6], System.Globalization.CultureInfo.InvariantCulture), 3);
            Assert.True(double.Parse(c[5], System.Globalization.CultureInfo.InvariantCulture) >= 0);
        }
        // NetA has its flows on the pipes: CB-3 and CB-4 get 12 and 8 cfs; MH-2 gets 20 − 20 = 0 and is left out.
        Assert.Equal(2, sec["INFLOWS"].Count);
        Assert.Equal("12", sec["INFLOWS"].Single(x => x[0] == "CB-3")[6]);
    }

    [Fact]
    public void RoundTrip_ExportThenImport_ReproducesTheNetworkAndItsHgl()
    {
        var ctx = Ctx();
        string inp = Inp(Tool.Run("mode = export", ctx));
        var back = Tool.Run("mode = import\n" + inp, ctx);
        Assert.True(back.Succeeded, string.Join("\n", back.Messages.Select(m => m.Text)));
        var net = back.UpdatedNetwork!;
        var orig = ctx.Network!;
        Assert.Equal(orig.Pipes.Count, net.Pipes.Count);
        foreach (var p in orig.Pipes)
        {
            var q = net.Pipes.Single(x => x.Name == p.Name);
            Assert.Equal((p.From, p.To), (q.From, q.To));
            Assert.Equal(p.UpstreamInvert, q.UpstreamInvert, 3);
            Assert.Equal(p.DownstreamInvert, q.DownstreamInvert, 3);
            Assert.Equal(p.Rise, q.Rise, 4);
            Assert.Equal(p.Length, q.Length, 3);
            Assert.Equal(p.ManningN, q.ManningN, 4);
        }
        // Re-solving the imported network (flows now accumulated from node inflows) gives the same pipe flows and HGL.
        var a = ctx.NetworkResult!;
        var b = StormNetworkSolver.Solve(net);
        Assert.True(b.Succeeded, string.Join("\n", b.Messages.Select(m => m.Text)));
        foreach (var p in b.Pipes) Assert.Equal(a.Pipes.Single(x => x.Pipe.Name == p.Pipe.Name).Pipe.Discharge, p.Pipe.Discharge, 4);
        foreach (var s in b.Structures.Where(x => !x.IsOutfall))
            Assert.Equal(a.Structures.Single(x => x.Structure.Name == s.Structure.Name).Hgl, s.Hgl, 2);
    }

    [Fact]
    public void Export_Subcatchments_RapidCityStormHoldsTable4_2Depth()
    {
        var ctx = Ctx();
        string outlet = ctx.Network!.Pipes[0].From;
        var r = Tool.Run($"mode = export\nstorm = rapid-city\nyear = 10\nstorm duration = 120\n[Subcatchments]\nName\tOutlet\tArea (ac)\tImpervious (%)\tWidth (ft)\tSlope (%)\tSoil\nA1\t{outlet}\t4\t50\t500\t2\tloam\n", ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var sec = SwmmInp.Sections(Inp(r));
        double depth = sec["TIMESERIES"].Sum(t => double.Parse(t[2], System.Globalization.CultureInfo.InvariantCulture)) * 5 / 60;
        Assert.Equal(1.98, depth, 2);                   // IDCM Table 4-2, 10-yr 120-min
        var ga = sec["INFILTRATION"].Single();
        Assert.Equal(["A1", "3.5", "0.256", "0.2"], ga);  // Table 4-4 loam
        Assert.False(sec.ContainsKey("INFLOWS"));       // subcatchments replace the constant inflows by default
    }

    private const string Report = """
          EPA STORM WATER MANAGEMENT MODEL - VERSION 5.2 (Build 5.2.4)

          ******************
          Node Depth Summary
          ******************

          ---------------------------------------------------------------------------------
                                           Average  Maximum  Maximum  Time of Max    Reported
                                             Depth    Depth      HGL   Occurrence   Max Depth
          Node                 Type           Feet     Feet     Feet  days hr:min        Feet
          ---------------------------------------------------------------------------------
          {0}                  JUNCTION       0.80     2.10   {1}     0  01:05        2.09

          *********************
          Node Flooding Summary
          *********************

          ---------------------------------------------------------------------------
                                                         Total   Maximum
                                       Maximum   Time of Max       Flood    Ponded
                               Hours      Rate    Occurrence      Volume     Depth
          Node               Flooded       CFS   days hr:min    10^6 gal      Feet
          ---------------------------------------------------------------------------
          {0}                   0.10      1.50      0  01:05       0.001     0.000

          ***************************
          Link Flow Summary
          ***************************

          -----------------------------------------------------------------------------
                                           Maximum  Time of Max   Maximum    Max/    Max/
                                            |Flow|   Occurrence   |Veloc|    Full    Full
          Link                 Type            CFS  days hr:min    ft/sec    Flow   Depth
          -----------------------------------------------------------------------------
          {2}                  CONDUIT       12.50     0  01:05      6.20    0.85    1.00
        """;

    [Fact]
    public void Compare_ReadsReportAndDifferencesTheHgl()
    {
        var ctx = Ctx();
        var s = ctx.NetworkResult!.Structures.First(x => !x.IsOutfall);
        var pipe = ctx.NetworkResult.Pipes.First();
        string rpt = string.Format(Report, s.Structure.Name, (s.Hgl + 0.5).ToString("F2", System.Globalization.CultureInfo.InvariantCulture), pipe.Pipe.Name);
        var r = Tool.Run("mode = compare\n" + rpt, ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var row = r.Tables[0].Rows.Single();
        Assert.Equal(0.5, (double)row[3]!, 2);
        Assert.Equal("check", row[8]);
        Assert.Equal(1.5, (double)row[6]!);
        Assert.Contains(r.Messages, m => m.Code == "W-SW-021");
        var link = r.Tables[1].Rows.Single();
        Assert.Equal(12.5, (double)link[2]!);
        Assert.Equal("surcharged", link[7]);
    }

    [Fact]
    public void Import_RejectsOtherUnitsAndNonInpText()
    {
        var ctx = Ctx();
        string inp = Inp(Tool.Run("mode = export", ctx));
        var si = Tool.Run("mode = import\n" + inp, Ctx(UnitSystem.SI));
        Assert.Contains(si.Messages, m => m.Code == "E-SW-011");
        var bad = Tool.Run("mode = import\n[TITLE]\nnothing", ctx);
        Assert.Contains(bad.Messages, m => m.Code == "E-SW-010");
        Assert.Contains(Tool.Run("mode = import", ctx).Messages, m => m.Code == "E-SW-013");
        Assert.Contains(Tool.Run("mode = sideways", ctx).Messages, m => m.Code == "E-SW-002");
        Assert.Contains(Tool.Run("mode = export", new DesignToolContext(UnitSystem.USCustomary)).Messages, m => m.Code == "E-SW-003");
    }

    [Fact]
    public void Import_ReadsShapesUnitsAndWarnsOnUnsupported()
    {
        string inp = """
            [OPTIONS]
            FLOW_UNITS GPM
            [JUNCTIONS]
            J1 100 6
            J2 99 6
            [OUTFALLS]
            O1 98 FIXED 99.5 NO
            [CONDUITS]
            C1 J1 J2 100 0.013 0 0.5
            C2 J2 O1 100 0.013 0 0
            C3 J1 O1 50 0.013 0 0
            [XSECTIONS]
            C1 CIRCULAR 1.5 0 0 0 1
            C2 HORIZ_ELLIPSE 1.5 2.4 0 0 1
            C3 RECT_CLOSED 2 3 0 0 1
            [INFLOWS]
            J1 FLOW "" FLOW 1.0 1.0 448.831
            [WEIRS]
            W1 J1 O1 TRANSVERSE 100 3.3
            """;
        var r = Tool.Run("mode = import\n" + inp, Ctx());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var n = r.UpdatedNetwork!;
        Assert.Equal(2, n.Pipes.Count);
        Assert.Equal(99.5, n.Pipes.Single(p => p.Name == "C1").DownstreamInvert, 6);
        Assert.Equal(NetworkPipeShape.Elliptical, n.Pipes.Single(p => p.Name == "C2").Shape);
        Assert.Equal(2.4, n.Pipes.Single(p => p.Name == "C2").Span);
        Assert.Equal(1.0, n.Structures.Single(s => s.Name == "J1").Inflow!.Value, 6);     // 448.831 gpm = 1 cfs
        Assert.Equal(106, n.Structures.Single(s => s.Name == "J1").RimElevation);
        Assert.Equal(TailwaterRule.Elevation, n.Tailwater);
        Assert.Equal(99.5, n.TailwaterElevation);
        Assert.Contains(r.Messages, m => m.Code == "W-SW-017");
        Assert.Contains(r.Messages, m => m.Code == "W-SW-019");
    }
}
