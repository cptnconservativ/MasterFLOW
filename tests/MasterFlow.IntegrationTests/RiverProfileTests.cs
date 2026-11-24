using System.Globalization;
using System.Text;
using MasterFlow.Application.River;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.IntegrationTests;

/// <summary>Natural channel profiles (HEC-RAS method): exact properties, hand calcs and an independent GVF integration.</summary>
public class RiverProfileTests
{
    private static readonly RiverProfileTool Tool = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double G = 32.174, K = 1.486;

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    /// <summary>A prismatic reach as cross-sections: RS 0 … (n−1)·dx, bed falling downstream at s0.</summary>
    private static string Prismatic(Func<double, IEnumerable<(double X, double Z)>> shape, int count, double dx, double s0, double n, string flows, string boundary, double lb = double.NaN, double rb = double.NaN)
    {
        var sb = new StringBuilder(boundary + "\n[Flows]\nRS\tQ\n" + flows + "\n[Sections]\nRS\tLeft bank\tRight bank\tLOB length\tCh length\tROB length\tn LOB\tn Ch\tn ROB\n");
        var pts = new StringBuilder("[Points]\nRS\tStation\tElevation\n");
        for (int i = 0; i < count; i++)
        {
            double rs = i * dx, z0 = 1000 + s0 * rs;
            var p = shape(z0).ToList();
            double l = double.IsNaN(lb) ? p[0].X : lb, r = double.IsNaN(rb) ? p[^1].X : rb, len = i == 0 ? 0 : dx;
            sb.Append(Inv, $"{rs}\t{l}\t{r}\t{len}\t{len}\t{len}\t{n}\t{n}\t{n}\n");
            foreach (var (x, z) in p) pts.Append(Inv, $"{rs}\t{x}\t{z}\n");
        }
        return sb + "\n" + pts;
    }

    private static IEnumerable<(double, double)> Trapezoid(double z0) => [(0, z0 + 12), (24, z0), (34, z0), (58, z0 + 12)];   // b = 10, z = 2
    private static IEnumerable<(double, double)> Rectangle(double z0) => [(0, z0 + 30), (0, z0), (20, z0), (20, z0 + 30)];

    private static List<IReadOnlyList<object?>> Rows(ToolResult r) => r.Tables[0].Rows.ToList();

    [Fact]
    public void UniformReach_StaysAtManningNormalDepth()
    {
        // Trapezoid b = 10, z = 2, n = 0.03, S = 0.002, Q = 300: every section at the normal depth of the existing engine.
        var r = Ok(Prismatic(Trapezoid, 6, 400, 0.002, 0.03, "2000\t300", "downstream = normal\ndownstream slope = 0.002"));
        double yn = UniformFlowCalculator.SolveNormalDepth(SectionGeometry.From(new TrapezoidalSection(10, 2, 2)), 300, 0.03, 0.002, K).Root;
        foreach (var row in Rows(r)) Assert.Equal(yn, (double)row[4]! - (double)row[3]!, 2);
    }

    [Fact]
    public void Rectangle_CriticalDepthMatchesClosedForm()
    {
        var s = new RiverSection("A", 0, Rectangle(0).Select(p => (p.Item1, p.Item2, (double?)null)).ToList(), 0, 20, 0.03, 0.03, 0.03, K);
        double q = 400, yc = Math.Pow(q * q / (G * 400), 1.0 / 3);   // q per ft = 20 cfs/ft
        Assert.Equal(yc, s.Critical(q, G).Ws, 4);
        var h = s.At(3);
        Assert.Equal(60, h.Area, 9);
        Assert.Equal(K / 0.03 * 60 * Math.Pow(60 / 26.0, 2.0 / 3), h.Conveyance, 6);   // P = 20 + 2·3 with the vertical walls
        Assert.Equal(1.0, h.Alpha, 9);
    }

    [Fact]
    public void CompoundSection_ConveyanceAndAlphaByHand()
    {
        // LOB 0–50 at el 4, channel 50–70 at el 0 with vertical banks, ROB 70–120 at el 4. WS = 6.
        var pts = new List<(double, double, double?)> { (0, 10, null), (0, 4, null), (50, 4, null), (50, 0, null), (70, 0, null), (70, 4, null), (120, 4, null), (120, 10, null) };
        var s = new RiverSection("C", 0, pts, 50, 70, 0.06, 0.03, 0.06, K);
        var h = s.At(6);
        // LOB: A = 100, P = 50 + 2 (left wall) + 0 (bank face belongs to the channel); channel: A = 120, P = 20 + 4 + 4; ROB as LOB.
        double kl = K / 0.06 * 100 * Math.Pow(100 / 52.0, 2.0 / 3), kc = K / 0.03 * 120 * Math.Pow(120 / 28.0, 2.0 / 3);
        Assert.Equal(kl, h.KLob, 6);
        Assert.Equal(kc, h.KCh, 6);
        Assert.Equal(kl, h.KRob, 6);
        double kt = 2 * kl + kc, at = 320;
        double alpha = (2 * kl * kl * kl / 1e4 + kc * kc * kc / (120.0 * 120)) / (kt * kt * kt / (at * at));
        Assert.Equal(alpha, h.Alpha, 9);
    }

    [Fact]
    public void M1Backwater_MatchesIndependentGvfIntegration()
    {
        // Rectangle b = 20, n = 0.03, S0 = 0.001, Q = 400, downstream depth 6 ft (M1). RK4 on dy/dx = (S0 − Sf)/(1 − Fr²).
        var r = Ok(Prismatic(Rectangle, 101, 20, 0.001, 0.03, "2000\t400", "downstream = known\ndownstream ws = 1006\ncontraction = 0\nexpansion = 0"));
        double b = 20, q = 400, n = 0.03;
        double F(double y) { double a = b * y, rh = a / (b + 2 * y), sf = Math.Pow(q * n / (K * a * Math.Pow(rh, 2.0 / 3)), 2), fr2 = q * q * b / (G * a * a * a); return (0.001 - sf) / (1 - fr2); }
        double yy = 6, h = 0.5;   // integrate upstream: x increases upstream, depth changes by −dy/dx along flow
        for (int i = 0; i < 4000; i++) { double k1 = F(yy), k2 = F(yy - h / 2 * k1), k3 = F(yy - h / 2 * k2), k4 = F(yy - h * k3); yy -= h / 6 * (k1 + 2 * k2 + 2 * k3 + k4); }
        // dy/dx (downstream positive) = F; going upstream by h the depth is y − h·F.
        var top = Rows(r).First(x => (string)x[0]! == "2000");
        Assert.Equal(yy, (double)top[4]! - (double)top[3]!, 2);
    }

    [Fact]
    public void EnergyEquation_ClosesBetweenEverySection()
    {
        var ctx = new DesignToolContext(UnitSystem.USCustomary);
        var p = ToolInput.Parse(Tool.SampleInput(ctx));
        var secs = RiverProfileTool.ParseSections(p, K, []);
        var r = Tool.Run(Tool.SampleInput(ctx), ctx);
        foreach (var prof in new[] { "10-yr", "100-yr" })
        {
            var rows = r.Tables[0].Rows.Where(x => (string)x[1]! == prof).ToDictionary(x => (string)x[0]!, x => (double)x[4]!);
            var ordered = secs.OrderBy(s => s.RiverStation).ToList();
            double q = prof == "10-yr" ? 300 : 1200;
            for (int i = 1; i < ordered.Count; i++)
            {
                var up = ordered[i]; var dn = ordered[i - 1];
                if (up.Name == "790") continue;     // the sample bridge sits between RS 790 and RS 740 (BridgeTests)
                RiverProfileTool.Result Mk(RiverSection s, double ws) { var h = s.At(ws); return new(s, q, ws, double.NaN, ws + h.VelocityHead(q, G), 0, h, "", ""); }
                var u = Mk(up, rows[up.Name]); var d = Mk(dn, rows[dn.Name]);
                Assert.InRange(u.Eg - (d.Eg + RiverProfileTool.Loss(u, d, G)), -0.03, 0.03);   // displayed WS are rounded to 0.01 ft
            }
        }
    }

    [Fact]
    public void IneffectiveArea_ExcludedBelowItsElevation()
    {
        var pts = new List<(double, double, double?)> { (0, 10, null), (0, 4, null), (50, 4, null), (50, 0, null), (70, 0, null), (70, 4, null), (120, 4, null), (120, 10, null) };
        var s = new RiverSection("C", 0, pts, 50, 70, 0.06, 0.03, 0.06, K, null, (90, 7));
        var below = s.At(6);
        Assert.Equal(120 + 100 + 2 * 20, below.Area, 9);       // channel 120, LOB 100, ROB only 70–90 (beyond 90 ineffective at WS 6 < 7)
        var above = s.At(7.5);
        Assert.Equal(20 * 7.5 + 2 * 50 * 3.5, above.Area, 9);
    }

    [Fact]
    public void MixedRegime_SteepThenMild_FindsSupercriticalUpstream()
    {
        // Upper sections on a steep slope (0.03), lower ones mild (0.0005): supercritical upstream, subcritical below.
        var sb = new StringBuilder("regime = mixed\ndownstream = normal\ndownstream slope = 0.0005\nupstream = critical\n[Flows]\nRS\tQ\n5000\t400\n[Sections]\nRS\tLeft bank\tRight bank\tLOB length\tCh length\tROB length\tn LOB\tn Ch\tn ROB\n");
        var pts = new StringBuilder("[Points]\nRS\tStation\tElevation\n");
        double z = 1000;
        var rsList = new List<(double Rs, double Z)>();
        for (int i = 0; i <= 20; i++) { double rs = i * 100; if (i > 0) z += rs <= 1000 ? 0.05 : 3.0; rsList.Add((rs, z)); }
        foreach (var (rs, zz) in rsList)
        {
            double len = rs == 0 ? 0 : 100;
            sb.Append(Inv, $"{rs}\t0\t20\t{len}\t{len}\t{len}\t0.015\t0.015\t0.015\n");
            foreach (var (x, e) in Rectangle(zz)) pts.Append(Inv, $"{rs}\t{x}\t{e}\n");
        }
        var r = Ok(sb + "\n" + pts);
        var rows = Rows(r);
        Assert.Equal("supercritical", rows.First(x => (string)x[0]! == "1900")[16]);
        Assert.True((double)rows.First(x => (string)x[0]! == "1900")[11]! > 1);
        Assert.Equal("subcritical", rows.First(x => (string)x[0]! == "100")[16]);
    }

    [Fact]
    public void Si_RunsWithMetricConstants()
    {
        var us = Ok(Prismatic(Trapezoid, 3, 400, 0.002, 0.03, "2000\t300", "downstream = normal\ndownstream slope = 0.002"));
        double yUs = (double)Rows(us)[0][4]! - (double)Rows(us)[0][3]!;
        // Same channel in metres (scale 0.3048): Manning normal depth scales by the same factor.
        string si = Prismatic(z0 => Trapezoid(z0 / 0.3048).Select(p => (p.Item1 * 0.3048, p.Item2 * 0.3048)), 3, 400 * 0.3048, 0.002, 0.03, "2000\t" + (300 * Math.Pow(0.3048, 3)).ToString(Inv), "downstream = normal\ndownstream slope = 0.002");
        var r = Ok(si, UnitSystem.SI);
        double ySi = (double)Rows(r)[0][4]! - (double)Rows(r)[0][3]!;
        Assert.Equal(yUs * 0.3048, ySi, 2);
    }

    [Theory]
    [InlineData("downstream = normal\ndownstream slope = 0.002\n[Sections]\nRS\tCh length\n0\t0", "E-RP-001")]
    [InlineData("downstream = normal\n[Flows]\nRS\tQ\n100\t50\n", "E-RP-005")]
    [InlineData("regime = sideways\n[Flows]\nRS\tQ\n100\t50\n", "E-RP-004")]
    [InlineData("downstream = normal\ndownstream slope = 0.01\n[Flows]\nRS\tQ\n100\t-5\n", "E-RP-002")]
    [InlineData("downstream = normal\ndownstream slope = 0.01\n[Flows]\nRS\tQ\n100\t50\n[Sections]\nRS\tLeft bank\tRight bank\tn Ch\n0\t5\t1\t0.03\n100\t1\t5\t0.03\n[Points]\nRS\tStation\tElevation\n0\t0\t5\n0\t3\t0\n0\t6\t5\n100\t0\t6\n100\t3\t1\n100\t6\t6\n", "E-RP-014")]
    [InlineData("downstream = normal\ndownstream slope = 0.01\n[Flows]\nRS\tQ\n100\t50\n[Sections]\nRS\tn Ch\n0\t0.03\n100\t0.03\n[Points]\nRS\tStation\tElevation\n0\t0\t5\n0\t3\t0\n0\t2\t5\n100\t0\t6\n100\t3\t1\n100\t6\t6\n", "E-RP-013")]
    [InlineData("downstream = normal\ndownstream slope = 0.01\n[Flows]\nRS\tQ\n50\t50\n[Sections]\nRS\tn Ch\n0\t0.03\n100\t0.03\n[Points]\nRS\tStation\tElevation\n0\t0\t5\n0\t3\t0\n0\t6\t5\n100\t0\t6\n100\t3\t1\n100\t6\t6\n", "E-RP-008")]
    public void BadInput_IsReported(string input, string code)
    {
        var r = Tool.Run(input, new DesignToolContext(UnitSystem.USCustomary));
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == code);
    }
}
