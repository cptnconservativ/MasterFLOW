using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;
using MasterFlow.Hydraulics.Numerics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Intensity-duration-frequency data for the rational method: intensity (in./hr) for a storm (return period,
/// years) and a duration (min). Tables are interpolated log-log between tabulated durations (D-053; log-log
/// interpolation of intensity and of depth give the same result because depth = i·t). Outside the tabulated
/// or stated duration range the intensity is NaN, never extrapolated.
/// </summary>
public sealed class IdfCurve
{
    private readonly Func<int, double, double> _intensity;

    private IdfCurve(string source, IReadOnlyList<int> storms, double min, double max, Func<int, double, double> intensity, IReadOnlyList<double>? durations)
    {
        Source = source; Storms = storms; MinDuration = min; MaxDuration = max; _intensity = intensity; Durations = durations;
    }

    public string Source { get; }
    public IReadOnlyList<int> Storms { get; }
    public double MinDuration { get; }
    public double MaxDuration { get; }
    /// <summary>Tabulated durations (min) of a table; null for an equation.</summary>
    public IReadOnlyList<double>? Durations { get; }

    public double Intensity(int storm, double minutes) =>
        Storms.Contains(storm) && minutes >= MinDuration - 1e-9 && minutes <= MaxDuration + 1e-9 ? _intensity(storm, minutes) : double.NaN;

    /// <summary>Rainfall depth (in.) for a duration: i·t/60.</summary>
    public double Depth(int storm, double minutes) => Intensity(storm, minutes) * minutes / 60;

    /// <summary>Rapid City IDCM 2012 Table 4-2 (2-, 10- and 100-year depths, 5 min to 24 hr).</summary>
    public static IdfCurve RapidCity { get; } = new("Rapid City IDCM 2012 Table 4-2 (depths, log-log interpolation)", [2, 10, 100],
        RapidCityRational.Durations[0], RapidCityRational.Durations[^1], RapidCityRational.Intensity, RapidCityRational.Durations);

    /// <summary>A table of intensities (in./hr) or depths (in.) by duration, interpolated log-log.</summary>
    public static IdfCurve FromTable(string source, IReadOnlyList<double> durations, IReadOnlyDictionary<int, double[]> values, bool valuesAreDepths)
    {
        var d = durations.ToArray();
        var intens = values.ToDictionary(kv => kv.Key, kv => kv.Value.Select((v, k) => valuesAreDepths ? v / (d[k] / 60) : v).ToArray());
        double I(int storm, double t)
        {
            var v = intens[storm];
            if (d.Length == 1) return Math.Abs(t - d[0]) < 1e-9 ? v[0] : double.NaN;
            for (int k = 1; k < d.Length; k++)
                if (t <= d[k] + 1e-9)
                {
                    double f = Math.Log(t / d[k - 1]) / Math.Log(d[k] / d[k - 1]);
                    return v[k - 1] * Math.Pow(v[k] / v[k - 1], f);
                }
            return double.NaN;
        }
        return new IdfCurve(source, values.Keys.OrderBy(k => k).ToList(), d[0], d[^1], I, d);
    }

    /// <summary>i = a / (t + b)^c with t in minutes, for each storm, valid from <paramref name="min"/> to <paramref name="max"/> minutes.</summary>
    public static IdfCurve FromEquation(string source, IReadOnlyDictionary<int, (double A, double B, double C)> coefficients, double min, double max) =>
        new(source, coefficients.Keys.OrderBy(k => k).ToList(), min, max, (s, t) => { var (a, b, c) = coefficients[s]; return a / Math.Pow(t + b, c); }, null);
}

/// <summary>One reach of a time-of-concentration flow path, with its velocity and travel time.</summary>
public sealed record TcSegment(string Basin, int Index, string Type, double Length, double SlopePercent, double Velocity, double Minutes, bool IsInitial, string Equation);

/// <summary>
/// Travel-time equations for the reaches of a flow path (IDCM 2012 §4.2.5–4.2.7: overland, gutter or storm
/// sewer, and channel phases; travel velocities from Manning's formula). U.S. units: ft, ft/s, minutes;
/// slopes are passed in ft/ft.
/// </summary>
public static class TcFlowPath
{
    /// <summary>Sheet flow, NRCS TR-55 (1986) Eq. 3-3: Tt = 0.007 (nL)^0.8 / (P2^0.5 s^0.4) hours; returned in minutes.</summary>
    public static double SheetFlowMinutes(double n, double lengthFt, double p2Inches, double slope) =>
        60 * 0.007 * Math.Pow(n * lengthFt, 0.8) / (Math.Sqrt(p2Inches) * Math.Pow(slope, 0.4));

    /// <summary>Shallow concentrated flow, TR-55 Appendix F: V = 16.1345 s^0.5 (unpaved) or 20.3282 s^0.5 (paved), ft/s.</summary>
    public static double ShallowConcentratedVelocity(bool paved, double slope) => (paved ? 20.3282 : 16.1345) * Math.Sqrt(slope);

    /// <summary>
    /// Mean velocity in a triangular gutter at spread T: IDCM Eq. 4-4-1 Q = (0.56/n) Sx^1.67 S^0.5 T^2.67
    /// divided by the flow area A = Sx·T²/2, i.e. V = (1.12/n) Sx^0.67 S^0.5 T^0.67 (ft/s).
    /// </summary>
    public static double GutterVelocity(double n, double slope, double crossSlope, double spreadFt) =>
        GutterHydraulics.Capacity(spreadFt, n, crossSlope, slope) / (crossSlope * spreadFt * spreadFt / 2);

    /// <summary>Full-flow (= half-full) velocity of a circular pipe, Manning: V = (1.486/n)(D/4)^(2/3) S^0.5 (IDCM §4.2.6(2)).</summary>
    public static double PipeFullVelocity(double n, double diameterFt, double slope) =>
        1.486 / n * Math.Pow(diameterFt / 4, 2.0 / 3.0) * Math.Sqrt(slope);

    /// <summary>Bank-full velocity of a trapezoidal channel (bottom b, side slopes z:1, depth d), Manning (IDCM §4.2.6(3)).</summary>
    public static double ChannelVelocity(double n, double bottomFt, double z, double depthFt, double slope)
    {
        double a = depthFt * (bottomFt + z * depthFt), p = bottomFt + 2 * depthFt * Math.Sqrt(1 + z * z);
        return 1.486 / n * Math.Pow(a / p, 2.0 / 3.0) * Math.Sqrt(slope);
    }
}

/// <summary>A sub-basin's runoff for the network routing: area, C·A and peak Q for each storm, and its own tc.</summary>
public sealed record BasinRunoff(string Name, string Structure, double Area, double Tc, IReadOnlyDictionary<int, double> CA, IReadOnlyDictionary<int, double> Q);

/// <summary>The rational-method flow in one pipe for one storm.</summary>
public sealed record RoutedPipe(string Pipe, string From, string To, double Area, double CA, double Tc, string CriticalPath,
    double Intensity, double RationalQ, double OtherInflow, double Q, string Governs, double Velocity, double TravelMinutes, string VelocityBasis);

/// <summary>
/// Rational method through a storm sewer (IDCM 2012 §4.2.7: "for subsequent design points, the time of
/// concentration is calculated by accumulating the travel times in downstream drainage way reaches").
/// For each pipe, from the top of the system down: ΣCA = the C·A of every sub-basin upstream; tc = the
/// longest of the sub-basin times at its upstream structure and (tc + pipe travel time) of each pipe
/// entering it; Q = i(tc)·ΣCA plus any other inflow (entered at a structure with no sub-basin, carried
/// downstream unchanged). Q is never taken below the peak of any single sub-basin or pipe draining to it,
/// because "sometimes a lower portion of the basin produces a larger peak" (§4.2.7). Pipe travel time is
/// L / V with V from Manning's formula, flowing full (= half full, §4.2.6(2)) or at normal depth for the Q.
/// </summary>
public static class RationalRouting
{
    public static (IReadOnlyList<RoutedPipe> Pipes, IReadOnlyList<CalcMessage> Messages) Route(
        StormNetwork net, IReadOnlyList<BasinRunoff> basins, IdfCurve idf, int storm, bool minorStorm, bool normalDepthVelocity)
    {
        var msgs = new List<CalcMessage>();
        var c = UnitConstants.For(net.Units);
        var structNames = net.Structures.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outlets = net.Pipes.GroupBy(p => p.From, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var g in outlets.Where(g => g.Count() > 1))
            msgs.Add(CalcMessage.Error("E-RM-020", $"Structure \"{g.Key}\" has {g.Count()} outlet pipes; split flows cannot be routed."));
        var into = net.Pipes.GroupBy(p => p.To, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var basinsAt = basins.Where(b => !string.IsNullOrWhiteSpace(b.Structure)).GroupBy(b => b.Structure, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        // Inflow at a structure with no sub-basin (offsite or bypass flow) is carried downstream unchanged.
        var other = net.Structures.Where(s => !basinsAt.ContainsKey(s.Name) && (minorStorm ? s.MinorInflow : s.Inflow) is > 0)
            .ToDictionary(s => s.Name, s => (minorStorm ? s.MinorInflow : s.Inflow)!.Value, StringComparer.OrdinalIgnoreCase);
        if (msgs.Count > 0) return ([], msgs);

        var done = new Dictionary<string, RoutedPipe>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RoutedPipe? Do(NetworkPipe p)
        {
            if (done.TryGetValue(p.Name, out var r)) return r;
            if (!visiting.Add(p.Name)) { msgs.Add(CalcMessage.Error("E-RM-021", $"The pipes form a loop through \"{p.From}\"; it cannot be routed.")); return null; }
            var ups = (into.TryGetValue(p.From, out var l) ? l : []).Select(Do).ToList();
            if (ups.Any(u => u is null)) return null;
            var here = basinsAt.TryGetValue(p.From, out var bl) ? bl : [];
            double area = here.Sum(b => b.Area) + ups.Sum(u => u!.Area);
            double ca = here.Sum(b => b.CA[storm]) + ups.Sum(u => u!.CA);
            double extra = other.GetValueOrDefault(p.From) + ups.Sum(u => u!.OtherInflow);
            double tc = double.NaN; string path = "";
            foreach (var b in here) if (!(b.Tc <= tc)) { tc = b.Tc; path = $"basin {b.Name}"; }
            foreach (var u in ups)
            {
                double t = u!.Tc + u.TravelMinutes;
                if (double.IsFinite(t) && !(t <= tc)) { tc = t; path = $"{u.CriticalPath} → {u.Pipe}"; }
            }
            double i = double.NaN, qr = 0;
            if (ca > 0)
            {
                i = idf.Intensity(storm, tc);
                if (!double.IsFinite(i))
                {
                    msgs.Add(CalcMessage.Error("E-RM-022", $"Pipe \"{p.Name}\": tc = {tc:F1} min is outside the IDF data ({idf.MinDuration:G4}–{idf.MaxDuration:G4} min) for the {storm}-yr storm."));
                    return null;
                }
                qr = ca * i;
            }
            double q = qr + extra; string governs = ca > 0 ? "ΣCA·i" : "other inflow";
            foreach (var b in here) if (b.Q[storm] + extra > q + 1e-12) { q = b.Q[storm] + extra; governs = $"basin {b.Name} alone"; }
            foreach (var u in ups) if (u!.Q > q + 1e-12) { q = u.Q; governs = $"pipe {u.Pipe} (upstream)"; }

            var (v, basis) = Velocity(p, q, c, normalDepthVelocity, msgs);
            double tt = v > 0 ? p.Length / v / 60 : double.NaN;
            r = new RoutedPipe(p.Name, p.From, p.To, area, ca, tc, path, i, qr, extra, q, governs, v, tt, basis);
            visiting.Remove(p.Name);
            return done[p.Name] = r;
        }
        foreach (var p in net.Pipes) Do(p);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ([], msgs);
        return (net.Pipes.Select(p => done[p.Name]).ToList(), msgs);
    }

    /// <summary>Pipe velocity for travel time: full flow (Manning, = half full) or the velocity at normal depth for <paramref name="q"/>.</summary>
    internal static (double V, string Basis) Velocity(NetworkPipe p, double q, UnitConstants c, bool normalDepth, List<CalcMessage> msgs)
    {
        ChannelSection sec = p.Shape == NetworkPipeShape.Circular ? new CircularSection(p.Rise) : new EllipticalSection(p.Rise, p.Span);
        var g = SectionGeometry.From(sec);
        double d = g.MaxDepth, af = g.Area(d), pf = g.WettedPerimeter(d), s = (p.UpstreamInvert - p.DownstreamInvert) / p.Length;
        if (!(s > 0))
        {
            msgs.Add(CalcMessage.Warning("W-RM-010", $"Pipe \"{p.Name}\" is flat or adverse; its travel time uses V = Q / A full."));
            return (q > 0 ? q / af : double.NaN, "Q / A full");
        }
        double k = c.ManningK / p.ManningN * Math.Sqrt(s);
        double vFull = k * Math.Pow(af / pf, 2.0 / 3.0);
        if (!normalDepth || !(q > 0)) return (vFull, "full");
        double Qy(double y) { double a = g.Area(y); return k * a * Math.Pow(a / g.WettedPerimeter(y), 2.0 / 3.0); }
        var peak = RootFinder.GoldenMaximum(Qy, 0.5 * d, d, 1e-12 * d);
        if (q > peak.Value) return (q / af, "Q / A full (surcharged)");
        var root = RootFinder.Brent(y => Qy(y) - q, 1e-9 * d, peak.X, 1e-13);
        if (!root.Converged) { msgs.Add(CalcMessage.Warning("W-RM-011", $"Pipe \"{p.Name}\": normal depth did not converge; full-flow velocity used.")); return (vFull, "full"); }
        return (q / g.Area(root.Root), "normal depth");
    }
}
