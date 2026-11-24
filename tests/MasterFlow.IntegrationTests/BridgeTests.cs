using System.Globalization;
using System.Text;
using MasterFlow.Application.River;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Bridge hydraulics in the river profile tool: opening geometry, Yarnell, energy, pressure and weir flow by hand.</summary>
public class BridgeTests
{
    private static readonly RiverProfileTool Tool = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double G = 32.174, K = 1.486;

    /// <summary>A wide rectangular channel (40 ft, walls to +20 ft) with flat floodplain benches at +8 ft out to ±200 ft.</summary>
    private static IEnumerable<(double, double)> Valley(double z0) =>
        [(0, z0 + 20), (0, z0 + 8), (180, z0 + 8), (180, z0), (220, z0), (220, z0 + 8), (400, z0 + 8), (400, z0 + 20)];

    private static string Reach(string flows, string bridges, string extra = "", double s0 = 0.002)
    {
        // Sections at RS 0, 300, 340 (bridge between 340 and 300), 640.
        double[] rs = [0, 300, 340, 640];
        var sb = new StringBuilder($"downstream = normal\ndownstream slope = {s0.ToString(Inv)}\n{extra}\n[Flows]\nRS\tQ\n640\t{flows}\n[Sections]\nRS\tLeft bank\tRight bank\tLOB length\tCh length\tROB length\tn LOB\tn Ch\tn ROB\n");
        var pts = new StringBuilder("[Points]\nRS\tStation\tElevation\n");
        for (int i = 0; i < rs.Length; i++)
        {
            double len = i == 0 ? 0 : rs[i] - rs[i - 1], z0 = 1000 + s0 * rs[i];
            sb.Append(Inv, $"{rs[i]}\t180\t220\t{len}\t{len}\t{len}\t0.05\t0.03\t0.05\n");
            foreach (var (x, z) in Valley(z0)) pts.Append(Inv, $"{rs[i]}\t{x}\t{z}\n");
        }
        return sb + "\n" + bridges + "\n" + pts;
    }

    private const string OneBridge = "[Bridges]\nName\tUpstream RS\tLow chord\tHigh chord\tDeck width\tLeft abutment\tRight abutment\tPier shape\nB1\t340\t{0}\t{1}\t30\t180\t220\tsquare\n[Bridge piers]\nBridge\tStation\tWidth\nB1\t200\t4";

    private static string Bridge(double low, double high) => string.Format(Inv, OneBridge, low, high);

    private static IReadOnlyList<object?> BridgeRow(ToolResult r) => r.Tables.Single(t => t.Title.StartsWith("Bridges")).Rows.Single();

    private static ToolResult Ok(string input)
    {
        var r = Tool.Run(input, new DesignToolContext(UnitSystem.USCustomary));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static RiverSection Section(double z0 = 1000) =>
        new("S", 0, Valley(z0).Select(p => (p.Item1, p.Item2, (double?)null)).ToList(), 180, 220, 0.03, 0.03, 0.03, K);   // one n, so the hand conveyance is a single segment

    [Fact]
    public void Opening_BlocksAbutmentsAndPiers()
    {
        var o = Section().Opening("B", 180, 220, 1012, [(200, 4)], 30, 30, 30, 0.3, 0.5);
        var h = o.At(1005);
        Assert.Equal((40 - 4) * 5, h.Area, 6);
        Assert.Equal(36, h.TopWidth, 6);
        // Perimeter: bed 36 + abutment walls 2×5 + pier faces 2×5, at n = 0.03.
        double p = 36 + 10 + 10;
        Assert.Equal(K / 0.03 * h.Area * Math.Pow(h.Area / p, 2.0 / 3), h.Conveyance, 3);
    }

    [Fact]
    public void Opening_Lid_HoldsAreaAboveTheLowChord()
    {
        var o = Section().Opening("B", 180, 220, 1012, [], 30, 30, 30, 0.3, 0.5, lowChord: 1006);
        var below = o.At(1006);
        var above = o.At(1009);
        Assert.Equal(below.Area, above.Area, 9);
        Assert.Equal(0, above.TopWidth);
        double p = 40 + 12 + 40;     // bed + walls to the low chord + soffit
        Assert.Equal(K / 0.03 * above.Area * Math.Pow(above.Area / p, 2.0 / 3), above.Conveyance, 3);
    }

    [Fact]
    public void Yarnell_MatchesHandCalculation()
    {
        var r = Ok(Reach("1500", Bridge(1009, 1012), "bridge low flow = yarnell"));
        var row = BridgeRow(r);
        var std = r.Tables[0].Rows.ToDictionary(x => (string)x[0]!, x => x);
        double ws2 = (double)std["300"][4]!, area = (double)std["300"][9]!;
        double z2 = 1000 + 0.002 * 300, v = 1500 / area, hv = v * v / (2 * G), y2 = ws2 - z2;
        double alpha = 4 * y2 / area, omega = hv / y2, k = 1.25;
        double expected = ws2 + 2 * k * (k + 10 * omega - 0.6) * (alpha + 15 * Math.Pow(alpha, 4)) * hv;
        Assert.Equal(expected, (double)row[12]!, 1);
        Assert.Equal((double)row[12]!, (double)row[3]!, 6);
        Assert.StartsWith("low flow, Yarnell", (string)row[2]!);
    }

    [Fact]
    public void EnergyMethod_ClosesAcrossTheOpening()
    {
        var r = Ok(Reach("1500", Bridge(1009, 1012)));
        var row = BridgeRow(r);
        Assert.StartsWith("low flow, energy", (string)row[2]!);
        double loss = (double)row[7]!;
        Assert.True(loss > 0);
        // With no bridge the same reach loses only friction and the small transition terms; the pier adds loss.
        var plain = Ok(Reach("1500", ""));
        double wsPlain = (double)plain.Tables[0].Rows.Single(x => (string)x[0]! == "340")[4]!;
        Assert.True((double)row[3]! > wsPlain);
    }

    [Fact]
    public void Weir_FreeFlowOverAFlatRoad()
    {
        // A flat road at 1012 over a section whose ground is below it everywhere between stations 0 and 400.
        var b = new Bridge("B", 0, 1009, 1012, 30, 180, 220, [], "square", 2.6, 0.3, 0.5, 0.5, 0.8, []);
        var s = Section(1000);
        Assert.Equal(2.6 * 400 * Math.Pow(1.5, 1.5), BridgeHydraulics.WeirFlow(b, s, 1013.5, 1005), 0);
        // Submerged: Villemonte reduction with hd = 1.0, hu = 1.5.
        double f = Math.Pow(1 - Math.Pow(1.0 / 1.5, 1.5), 0.385);
        Assert.Equal(2.6 * 400 * Math.Pow(1.5, 1.5) * f, BridgeHydraulics.WeirFlow(b, s, 1013.5, 1013.0), 0);
        Assert.Equal(1012, BridgeHydraulics.MinCrest(b, s));
    }

    [Fact]
    public void HighFlow_SplitsBetweenOpeningAndWeir()
    {
        var r = Ok(Reach("9000", Bridge(1006, 1009)));
        var row = BridgeRow(r);
        double qo = (double)row[8]!, qw = (double)row[9]!;
        Assert.Contains("weir", (string)row[2]!);
        Assert.True(qw > 0);
        Assert.Equal(9000, qo + qw, 0);
        Assert.Contains(r.Messages, m => m.Code == "W-BR-004");
    }

    [Fact]
    public void Orifice_WhenTheDownstreamSideIsAboveTheLowChord()
    {
        // Low chord 1004 over a 40 ft channel: the downstream W.S. is above it, so the opening runs full.
        var r = Ok(Reach("2500", Bridge(1003.5, 1014)));
        var row = BridgeRow(r);
        string type = (string)row[2]!;
        Assert.True(type.Contains("orifice") || type.Contains("under pressure"), type);
        if (type.StartsWith("pressure (orifice)"))
        {
            var o = Section(1000 + 0.002 * 340).Opening("B", 180, 220, 1014, [(200, 4)], 30, 30, 30, 0.3, 0.5, 1003.5);
            double a = o.At(1003.5).Area, h = (double)row[4]! - (double)row[5]!;
            Assert.Equal(0.8 * a * Math.Sqrt(2 * G * h), (double)row[8]!, 0);
        }
    }

    [Fact]
    public void Sample_HasABridgeWithThreeProfiles()
    {
        var ctx = new DesignToolContext(UnitSystem.USCustomary);
        var r = Tool.Run(Tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded);
        var rows = r.Tables.Single(t => t.Title.StartsWith("Bridges")).Rows;
        Assert.Equal(3, rows.Count);
        Assert.All(rows, x => Assert.True((double)x[3]! >= (double)x[5]!));      // the bridge raises the water upstream
    }

    [Fact]
    public void YarnellK_Table()
    {
        Assert.Equal(0.90, Application.River.Bridge.YarnellK("semicircular"));
        Assert.Equal(0.95, Application.River.Bridge.YarnellK("twin cylinder with diaphragm"));
        Assert.Equal(1.05, Application.River.Bridge.YarnellK("triangular"));
        Assert.Equal(1.25, Application.River.Bridge.YarnellK("square"));
        Assert.Null(Application.River.Bridge.YarnellK("oval"));
    }

    [Theory]
    [InlineData("[Bridges]\nName\tUpstream RS\tLow chord\tHigh chord\tDeck width\tLeft abutment\tRight abutment\nB\t0\t1009\t1012\t30\t180\t220", "E-BR-001")]
    [InlineData("[Bridges]\nName\tUpstream RS\tLow chord\tHigh chord\tDeck width\tLeft abutment\tRight abutment\nB\t340\t1012\t1009\t30\t180\t220", "E-BR-002")]
    [InlineData("[Bridges]\nName\tUpstream RS\tLow chord\tHigh chord\tDeck width\tLeft abutment\tRight abutment\nB\t340\t1009\t1012\t30\t220\t180", "E-BR-003")]
    [InlineData("[Bridges]\nName\tUpstream RS\tLow chord\tHigh chord\tDeck width\tLeft abutment\tRight abutment\nB\t340\t1009\t1012\t0\t180\t220", "E-BR-004")]
    [InlineData("[Bridges]\nName\tUpstream RS\tLow chord\tHigh chord\tDeck width\tLeft abutment\tRight abutment\nB\t340\t1009\t1012\t30\t180\t220\n[Bridge piers]\nBridge\tStation\tWidth\nB\t200\t0", "E-BR-005")]
    [InlineData("[Bridges]\nName\tUpstream RS\tLow chord\tHigh chord\tDeck width\tLeft abutment\tRight abutment\nB\t340\t1009\t1012\t30\t180\t220\n[Bridge piers]\nBridge\tStation\tWidth\nZ\t200\t3", "E-BR-005")]
    [InlineData("[Bridges]\nName\tUpstream RS\tLow chord\tHigh chord\tDeck width\tLeft abutment\tRight abutment\nB\t340\t1009\t1012\t60\t180\t220", "E-BR-007")]
    [InlineData("[Bridges]\nName\tUpstream RS\tLow chord\tHigh chord\tDeck width\tLeft abutment\tRight abutment\tPier shape\nB\t340\t1009\t1012\t30\t180\t220\toval", "E-BR-008")]
    public void BadBridge_IsReported(string bridge, string code)
    {
        var r = Tool.Run(Reach("1500", bridge), new DesignToolContext(UnitSystem.USCustomary));
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }

    [Fact]
    public void BadLowFlowMethod_IsReported() =>
        Assert.Contains(Tool.Run(Reach("1500", Bridge(1009, 1012), "bridge low flow = momentum"), new DesignToolContext(UnitSystem.USCustomary)).Messages, m => m.Code == "E-BR-006");
}
