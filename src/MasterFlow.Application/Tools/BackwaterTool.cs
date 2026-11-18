using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.Application.Tools;

/// <summary>One computed station of a gradually varied flow profile.</summary>
public sealed record ProfilePoint(double Distance, double Depth, double Velocity, double Energy, double Froude, double FrictionSlope);

/// <summary>
/// Gradually varied flow in a prismatic channel by the standard step method (energy equation between stations,
/// average friction slope): subcritical profiles are computed upstream from a downstream control, supercritical
/// profiles downstream from an upstream control. Each step's depth is found by bisection on the branch of the
/// specific-energy curve that matches the regime; a step with no solution on that branch means the flow must pass
/// through critical depth (a jump or a drawdown to a control), and the profile stops there with a message.
/// Chow (1959) ch. 10; HEC-RAS Hydraulic Reference Manual ch. 2.
/// </summary>
public static class GradualFlow
{
    public static double Sf(ISectionGeometry g, double y, double q, double n, double k)
    {
        double a = g.Area(y), r = a / g.WettedPerimeter(y);
        double v = q / a;
        return Math.Pow(n * v / (k * Math.Pow(r, 2.0 / 3.0)), 2);
    }

    public static double Energy(ISectionGeometry g, double y, double q, double grav) { double v = q / g.Area(y); return y + v * v / (2 * grav); }

    public static (List<ProfilePoint> Points, string? Stop) Compute(ISectionGeometry g, double q, double n, double s0, double length, double yControl, bool subcritical, double yc, UnitConstants c, int steps = 200)
    {
        double k = c.ManningK, grav = c.Gravity, dx = length / steps;
        var pts = new List<ProfilePoint> { Point(0, yControl) };
        double y = yControl;
        double yMax = double.IsFinite(g.MaxDepth) ? g.MaxDepth * (1 - 1e-9) : Math.Max(10 * yControl, 100);
        for (int i = 1; i <= steps; i++)
        {
            double e1 = Energy(g, y, q, grav), sf1 = Sf(g, y, q, n, k);
            // Subcritical, going upstream: y2 + v2²/2g = E1 − S0 dx + ½(Sf1 + Sf2) dx  (bed rises S0·dx upstream).
            // Supercritical, going downstream: y2 + v2²/2g = E1 + S0 dx − ½(Sf1 + Sf2) dx.
            double F(double y2) => subcritical
                ? Energy(g, y2, q, grav) - (e1 - s0 * dx + 0.5 * (sf1 + Sf(g, y2, q, n, k)) * dx)
                : Energy(g, y2, q, grav) - (e1 + s0 * dx - 0.5 * (sf1 + Sf(g, y2, q, n, k)) * dx);
            double lo = subcritical ? yc * (1 + 1e-7) : 1e-6 * yc, hi = subcritical ? yMax : yc * (1 - 1e-7);
            double flo = F(lo), fhi = F(hi);
            if (subcritical ? flo > 0 : fhi > 0)
                return (pts, $"No {(subcritical ? "subcritical" : "supercritical")} depth satisfies the energy equation {pts[^1].Distance + dx:F1} from the control: the flow passes through critical depth there (hydraulic jump or drawdown).");
            if (subcritical ? fhi < 0 : flo < 0)
                return (pts, $"The depth would exceed {(double.IsFinite(g.MaxDepth) ? "the conduit crown" : "the search limit")} {pts[^1].Distance + dx:F1} from the control.");
            for (int it = 0; it < 100; it++)
            {
                double m = 0.5 * (lo + hi), fm = F(m);
                // F increases with y on the subcritical branch and decreases on the supercritical branch.
                if ((fm < 0) == subcritical) lo = m; else hi = m;
            }
            y = 0.5 * (lo + hi);
            pts.Add(Point(i * dx, y));
        }
        return (pts, null);

        ProfilePoint Point(double x, double yy)
        {
            double a = g.Area(yy), v = q / a, t = g.TopWidth(yy);
            return new ProfilePoint(x, yy, v, Energy(g, yy, q, grav), t > 0 ? v / Math.Sqrt(grav * a / t) : double.NaN, Sf(g, yy, q, n, k));
        }
    }

    /// <summary>Chow's profile class: slope type (M, S, C, H, A) and zone (1 above both yn and yc, 2 between, 3 below both).</summary>
    public static string Classify(double s0, double yn, double yc, double y)
    {
        string type = s0 <= 0 ? (s0 == 0 ? "H" : "A") : Math.Abs(yn - yc) < 1e-6 * yc ? "C" : yn > yc ? "M" : "S";
        double upper = type is "H" or "A" ? yc : Math.Max(yn, yc), lower = type is "H" or "A" ? yc : Math.Min(yn, yc);
        int zone = y > upper ? 1 : y >= lower ? 2 : 3;
        if (type is "H" or "A") zone = y > yc ? 2 : 3;
        return type + zone;
    }
}

public sealed class BackwaterTool : IDesignTool
{
    public string Id => "backwater-profile";
    public string Name => "Backwater / drawdown profile (gradually varied flow)";
    public string Description => "Water surface along a prismatic channel or partly full pipe from a control depth (culvert headwater, pond, junction, free overfall) by the standard step method, with the profile type (M1, M2, S2, H2 …), and a check of where the water comes within the required freeboard of the top of bank.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Backwater profile, SI units (m, m³/s). Section: rectangular (b), trapezoidal (b, z), triangular (z) or circular (D).
        section = trapezoidal
        b = 3
        z = 3
        n = 0.035
        slope = 0.002
        Q = 6
        length = 400
        # Control: depth at the downstream end (subcritical) or "critical" for a free overfall; for a steep channel, the upstream depth.
        control depth = 1.6
        invert at control = 100.0
        bank depth = 1.8
        freeboard = 0.3
        """ : """
        # Backwater profile, U.S. units (ft, cfs). Section: rectangular (b), trapezoidal (b, z), triangular (z) or circular (D).
        section = trapezoidal
        b = 10
        z = 3
        n = 0.035
        slope = 0.002
        Q = 200
        length = 1500
        # Control: depth at the downstream end (subcritical), e.g. culvert headwater, or "critical" for a free overfall;
        # on a steep channel, the depth at the upstream end.
        control depth = 5.0
        invert at control = 3250.0
        bank depth = 5.5
        freeboard = 1.0     # IDCM §4.8: check the freeboard required for the channel
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Gradually varied flow profile (standard step)";
        var c = UnitConstants.For(context.Units);
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        string shape = (p.Text("section") ?? "").ToLowerInvariant();
        double b = p.Number("b", 0), z = p.Number("z", 0), d = p.Number("D", 0);
        ChannelSection? section = shape switch
        {
            "rectangular" => new RectangularSection(b),
            "trapezoidal" => new TrapezoidalSection(b, z, z),
            "triangular" => new TriangularSection(z, z),
            "circular" => new CircularSection(d),
            _ => null,
        };
        if (section is null) msgs.Add(CalcMessage.Error("E-BW-001", "section must be rectangular, trapezoidal, triangular or circular."));
        double n = p.Number("n", double.NaN), s0 = p.Number("slope", double.NaN), q = p.Number("Q", double.NaN), length = p.Number("length", double.NaN);
        double? invert = p.OptionalNumber("invert at control"), bank = p.OptionalNumber("bank depth");
        double fb = p.Number("freeboard", 0);
        string ctl = (p.Text("control depth") ?? "").ToLowerInvariant();
        if (!(n > 0) || !(q > 0) || !(length > 0) || double.IsNaN(s0)) msgs.Add(CalcMessage.Error("E-BW-002", "n, Q and length must be greater than zero, and slope must be given (0 or negative allowed)."));
        if ((shape == "rectangular" && !(b > 0)) || (shape == "trapezoidal" && !(b >= 0 && z >= 0 && b + z > 0)) || (shape == "triangular" && !(z > 0)) || (shape == "circular" && !(d > 0)))
            msgs.Add(CalcMessage.Error("E-BW-003", "Section dimensions are missing or not positive."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        var g = SectionGeometry.From(section!);
        var ycR = UniformFlowCalculator.SolveCriticalDepth(g, q, c.Gravity);
        if (!ycR.Converged) return ToolResult.Failed(title, CalcMessage.Error("E-BW-004", "Critical depth did not converge" + (double.IsFinite(g.MaxDepth) ? " (Q may exceed what the conduit can carry at critical depth)." : ".")));
        double yc = ycR.Root;
        double yn = double.NaN;
        if (s0 > 0)
        {
            var ynR = UniformFlowCalculator.SolveNormalDepth(g, q, n, s0, c.ManningK);
            if (ynR.Converged) yn = ynR.Root;
            else msgs.Add(CalcMessage.Warning("W-BW-001", "Normal depth not found (Q exceeds the section's open-channel capacity); the profile is computed without it."));
        }
        bool sub = !(yn < yc);   // mild, horizontal, adverse: subcritical; steep: supercritical
        double y0;
        if (ctl is "critical" or "yc") y0 = yc;
        else if (ToolInput.TryNumber(ctl, out double v) && v > 0) y0 = v;
        else return ToolResult.Failed(title, CalcMessage.Error("E-BW-005", "control depth must be a depth > 0 or \"critical\"."));
        if (double.IsFinite(g.MaxDepth) && y0 >= g.MaxDepth - 1e-9)
            return ToolResult.Failed(title, CalcMessage.Error("E-BW-008", $"The control depth {y0:F3} is at or above the conduit crown ({g.MaxDepth:F3}): the pipe is surcharged at the control, which is pressure flow, not an open-channel profile. Use the storm network HGL for a surcharged pipe, or enter a control below the crown."));
        if (sub && y0 < yc - 1e-9)
        {
            msgs.Add(CalcMessage.Warning("W-BW-002", $"The control depth {y0:F3} is below critical ({yc:F3}) on a {(s0 > 0 ? "mild" : "flat/adverse")} channel; subcritical flow cannot reach it, so critical depth is used at the control (drawdown to a free overfall)."));
            y0 = yc;
        }
        if (!sub && y0 > yc + 1e-9)
        {
            msgs.Add(CalcMessage.Warning("W-BW-003", $"The control depth {y0:F3} is above critical ({yc:F3}) on a steep channel; supercritical flow is computed downstream from critical depth at the upstream end. A downstream high water level would cause a hydraulic jump, which this tool does not locate."));
            y0 = yc;
        }
        var (pts, stop) = GradualFlow.Compute(g, q, n, s0, length, y0, sub, yc, c);
        if (stop is not null) msgs.Add(CalcMessage.Warning("W-BW-004", stop));
        string L = c.Length;
        var rows = new List<IReadOnlyList<object?>>();
        int every = Math.Max(1, (pts.Count - 1) / 25);
        for (int i = 0; i < pts.Count; i++)
        {
            if (i % every != 0 && i != pts.Count - 1) continue;
            var pt = pts[i];
            // Distance is measured upstream from a downstream control (sub) or downstream from an upstream control (super).
            double? wse = invert is { } inv ? inv + (sub ? s0 * pt.Distance : -s0 * pt.Distance) + pt.Depth : null;
            string flag = bank is { } bk ? (pt.Depth > bk + 1e-9 ? "OVERTOPS" : pt.Depth > bk - fb + 1e-9 ? "freeboard short" : "OK") : "";
            rows.Add([pt.Distance, pt.Depth, wse, pt.Velocity, pt.Energy, pt.Froude, pt.FrictionSlope, GradualFlow.Classify(s0, double.IsNaN(yn) ? double.PositiveInfinity : yn, yc, pt.Depth), flag]);
        }
        if (bank is { } bb)
        {
            var worst = pts.MaxBy(x => x.Depth)!;
            if (worst.Depth > bb - fb + 1e-9) msgs.Add(CalcMessage.Warning("W-BW-005", $"Freeboard: depth reaches {worst.Depth:F2} {L} at {worst.Distance:F0} {L}, {bb - worst.Depth:F2} {L} below the top of bank ({fb:F2} {L} required)."));
        }
        double far = pts[^1].Depth;
        string summary = $"Q = {q:G6} {c.Discharge}; yc = {yc:F3} {L}; yn = {(double.IsNaN(yn) ? "none (S0 ≤ 0)" : yn.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))} {L}; {(sub ? "subcritical, computed upstream from the downstream control" : "supercritical, computed downstream from the upstream control")}; " +
                         $"profile {GradualFlow.Classify(s0, double.IsNaN(yn) ? double.PositiveInfinity : yn, yc, y0)}; depth {y0:F3} → {far:F3} {L} over {pts[^1].Distance:F0} {L}.";
        if (!double.IsNaN(yn) && Math.Abs(far - yn) / yn < 0.01) summary += " The profile has reached normal depth (within 1 %).";
        return new ToolResult(title, [summary],
        [
            new($"Profile ({(sub ? "distance upstream of the control" : "distance downstream of the control")})", [$"Distance ({L})", $"Depth ({L})", $"WSE ({L})", $"V ({c.Velocity})", $"E ({L})", "Fr", "Sf", "Type", "Bank check"], rows,
                [$"Standard step, {pts.Count - 1} steps of {length / 200:G4} {L} (every {every}th shown); Sf by Manning at each station, averaged over the step. Type: Chow's classification of the depth at that station."]),
        ], msgs);
    }
}
