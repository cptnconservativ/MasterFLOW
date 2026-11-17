using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics.Geometry;
using MasterFlow.Hydraulics.Numerics;

namespace MasterFlow.Hydraulics;

/// <summary>
/// Steady, uniform open-channel flow by Manning's equation:
///   Q = (k / n) A R^(2/3) S^(1/2),   R = A / P
/// with k = 1.486 (U.S. customary) or 1.0 (SI). Critical depth is found from the critical-flow
/// condition Q² T / (g A³) = 1, equivalently A √(A/T) = Q / √g.
/// This calculator does NOT model gradually varied flow, unsteady flow, or pressurised flow.
/// </summary>
public static class UniformFlowCalculator
{
    /// <summary>Relative convergence tolerance applied to depth (fraction of depth).</summary>
    public const double DepthTolerance = 1e-12;

    /// <summary>Maximum relative discharge residual accepted after a depth solve.</summary>
    public const double AcceptedRelativeResidual = 1e-8;

    /// <summary>Upper search limit for depths, in base length units.</summary>
    public const double MaxSearchDepth = 1e5;

    public const double NearCriticalLow = 0.86;
    public const double NearCriticalHigh = 1.13;

    public static UniformFlowResult Solve(UniformFlowInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var c = UnitConstants.For(input.Units);
        var messages = new List<CalcMessage>();
        var trace = new List<string>();

        Validate(input, messages);
        if (messages.Any(m => m.Severity == MessageSeverity.Error))
            return Failed(input, c, messages, trace);

        var geometry = SectionGeometry.From(input.Section);
        trace.Add(geometry switch
        {
            TrapezoidGeometry t => F($"Section: {input.Section.ShapeName}; b = {t.BottomWidth:G6} {c.Length}, zL = {t.LeftSideSlope:G6}, zR = {t.RightSideSlope:G6} (H:V)."),
            CircularGeometry p => F($"Section: circular conduit, D = {p.Diameter:G8} {c.Length}; A = D²/8 (θ − sin θ), P = Dθ/2, θ = 2 acos(1 − 2y/D)."),
            SymmetricArcGeometry e => F($"Section: elliptical conduit, rise {((EllipticalSection)input.Section).Rise:G8} {c.Length}, span {((EllipticalSection)input.Section).Span:G8} {c.Length}; {e.Description}; full area {e.Area(e.MaxDepth):G8} {c.Area}."),
            _ => $"Section: {input.Section.ShapeName}.",
        });
        trace.Add(F($"Manning's equation Q = (k/n) A R^(2/3) S^(1/2) with k = {c.ManningK} ({c.System}); g = {c.Gravity} {c.Length}/s²."));

        double n = input.ManningN, s = input.Slope, q = input.Discharge, y = input.Depth;
        switch (input.SolveFor)
        {
            case SolveFor.Discharge:
                q = ManningDischarge(geometry, y, n, s, c.ManningK);
                trace.Add(F($"Discharge computed directly at y = {y:G10} {c.Length}: Q = {q:G10} {c.Discharge}."));
                break;

            case SolveFor.NormalDepth when !geometry.DischargeIsMonotonic:
            {
                var peak = MaxDischarge(geometry, n, s, c.ManningK);
                trace.Add(F($"Closed conduit: Q(y) peaks at y = {peak.Depth:G10} {c.Length} (golden-section search, {peak.Iterations} iterations), Qmax = {peak.Discharge:G10} {c.Discharge}."));
                if (q > peak.Discharge * (1 + 1e-10))
                {
                    double qFull = ManningDischarge(geometry, geometry.MaxDepth, n, s, c.ManningK);
                    messages.Add(CalcMessage.Error(MessageCodes.ExceedsConduitCapacity,
                        F($"Discharge {q:F3} {c.Discharge} exceeds the maximum free-surface capacity {peak.Discharge:F3} {c.Discharge} (full-flow capacity {qFull:F3} {c.Discharge}). The conduit would surcharge; pressurised flow is not modelled. Increase size or slope."), nameof(input.Discharge)));
                    return Failed(input, c, messages, trace);
                }
                var root = RootFinder.Brent(y2 => ManningDischarge(geometry, y2, n, s, c.ManningK) - q, 0.0, peak.Depth, DepthTolerance);
                trace.Add(DescribeRoot("Normal depth (lower root)", "f(y) = (k/n) A R^(2/3) S^(1/2) − Q on [0, y_Qmax]", root, c.Length));
                if (!AcceptRoot(root, y2 => ManningDischarge(geometry, y2, n, s, c.ManningK), q, "normal depth", messages))
                    return Failed(input, c, messages, trace);
                y = root.Root;
                break;
            }

            case SolveFor.NormalDepth:
            {
                var root = SolveNormalDepth(geometry, q, n, s, c.ManningK);
                trace.Add(DescribeRoot("Normal depth", "f(y) = (k/n) A R^(2/3) S^(1/2) − Q", root, c.Length));
                if (!AcceptRoot(root, y2 => ManningDischarge(geometry, y2, n, s, c.ManningK), q, "normal depth", messages))
                    return Failed(input, c, messages, trace);
                y = root.Root;
                break;
            }

            case SolveFor.Slope:
            {
                double kFactor = c.ManningK / n * geometry.Area(y) * Math.Pow(HydraulicRadius(geometry, y), 2.0 / 3.0);
                s = Math.Pow(q / kFactor, 2);
                trace.Add(F($"Slope solved in closed form: S = [Q n / (k A R^(2/3))]² = {s:G10}."));
                break;
            }

            case SolveFor.Roughness:
            {
                n = c.ManningK * geometry.Area(y) * Math.Pow(HydraulicRadius(geometry, y), 2.0 / 3.0) * Math.Sqrt(s) / q;
                trace.Add(F($"Roughness solved in closed form: n = k A R^(2/3) S^(1/2) / Q = {n:G10}."));
                if (n < 0.008 || n > 0.20)
                    messages.Add(CalcMessage.Warning(MessageCodes.RoughnessUnusual,
                        F($"Back-calculated n = {n:F4} is outside the typical range 0.008–0.20. Check discharge, depth and slope."), nameof(input.ManningN)));
                break;
            }
        }

        if (!double.IsFinite(q) || !double.IsFinite(y) || !double.IsFinite(s) || !double.IsFinite(n) || q <= 0 || y <= 0)
        {
            messages.Add(CalcMessage.Error(MessageCodes.NonFinite, "The calculation produced a non-finite or non-positive value; no result is reported."));
            return Failed(input, c, messages, trace);
        }

        var normal = State(geometry, y, q, c.Gravity);
        ConduitResult? conduit = geometry.DischargeIsMonotonic ? null : ConduitValues(geometry, y, q, n, s, c, trace);
        double conveyance = c.ManningK / n * normal.Area * Math.Pow(normal.HydraulicRadius, 2.0 / 3.0);

        // Critical flow for the same discharge.
        var critRoot = SolveCriticalDepth(geometry, q, c.Gravity);
        if (!critRoot.Converged && !geometry.DischargeIsMonotonic)
            trace.Add("Critical depth could not be bracketed below the crown.");
        trace.Add(DescribeRoot("Critical depth", "f(y) = A √(A/T) − Q/√g", critRoot, c.Length));
        if (!AcceptRoot(critRoot, yc => CriticalSectionFactor(geometry, yc), q / Math.Sqrt(c.Gravity), "critical depth", messages))
            return Failed(input, c, messages, trace);
        var critical = State(geometry, critRoot.Root, q, c.Gravity);
        double criticalSlope = Math.Pow(q * n / (c.ManningK * critical.Area * Math.Pow(critical.HydraulicRadius, 2.0 / 3.0)), 2);
        trace.Add(F($"Critical slope Sc = [Q n / (k Ac Rc^(2/3))]² = {criticalSlope:G8}."));

        var regime = double.IsNaN(normal.Froude) ? FlowRegime.Undefined
            : normal.Froude < NearCriticalLow ? FlowRegime.Subcritical
            : normal.Froude > NearCriticalHigh ? FlowRegime.Supercritical
            : FlowRegime.NearCritical;
        AddPostSolveMessages(input, y, s, normal.Froude, regime, c, messages);
        if (conduit is not null) AddConduitMessages(conduit, y, q, c, messages);

        return new UniformFlowResult
        {
            Input = input,
            Constants = c,
            Succeeded = true,
            Messages = messages,
            Trace = trace,
            ManningN = n,
            Slope = s,
            Discharge = q,
            Normal = normal,
            Conveyance = conveyance,
            UniformFlowSectionFactor = normal.Area * Math.Pow(normal.HydraulicRadius, 2.0 / 3.0),
            Critical = critical,
            CriticalSectionFactor = CriticalSectionFactor(geometry, critical.Depth),
            CriticalSlope = criticalSlope,
            Regime = regime,
            Conduit = conduit,
        };
    }

    // ---------------------------------------------------------------- closed conduits

    public readonly record struct Peak(double Depth, double Discharge, int Iterations);

    /// <summary>Maximum free-surface discharge of a conduit and the depth where it occurs (golden-section search on [0.5 D, D]).</summary>
    public static Peak MaxDischarge(ISectionGeometry g, double n, double s, double k)
    {
        double d = g.MaxDepth;
        var (x, v, it) = RootFinder.GoldenMaximum(y => ManningDischarge(g, y, n, s, k), 0.5 * d, d, 1e-12 * d);
        return new Peak(x, v, it);
    }

    private static ConduitResult ConduitValues(ISectionGeometry g, double y, double q, double n, double s, UnitConstants c, List<string> trace)
    {
        double d = g.MaxDepth;
        double aFull = g.Area(d), rFull = aFull / g.WettedPerimeter(d);
        double qFull = c.ManningK / n * aFull * Math.Pow(rFull, 2.0 / 3.0) * Math.Sqrt(s);
        double sFull = Math.Pow(q * n / (c.ManningK * aFull * Math.Pow(rFull, 2.0 / 3.0)), 2);
        var peak = MaxDischarge(g, n, s, c.ManningK);
        double alt = double.NaN;
        if (q > qFull && q <= peak.Discharge * (1 + 1e-10))
        {
            bool lower = y <= peak.Depth;
            var r = lower
                ? RootFinder.Brent(z => ManningDischarge(g, z, n, s, c.ManningK) - q, peak.Depth, d, DepthTolerance)
                : RootFinder.Brent(z => ManningDischarge(g, z, n, s, c.ManningK) - q, 0, peak.Depth, DepthTolerance);
            if (r.Converged) alt = r.Root;
            trace.Add(DescribeRoot("Alternate normal depth", $"f(y) = Q(y) − Q on the {(lower ? "upper" : "lower")} branch", r, c.Length));
        }
        trace.Add(F($"Full flow: Qfull = (k/n) Af Rf^(2/3) S^(1/2) = {qFull:G10} {c.Discharge}; slope to carry Q just full = {sFull:G8}."));
        return new ConduitResult
        {
            Rise = d,
            PercentFull = 100.0 * Math.Min(y, d) / d,
            FullArea = aFull,
            FullFlowDischarge = qFull,
            FullFlowVelocity = qFull / aFull,
            FullFlowSlope = sFull,
            MaxDischarge = peak.Discharge,
            DepthAtMaxDischarge = peak.Depth,
            AlternateNormalDepth = alt,
        };
    }

    private static void AddConduitMessages(ConduitResult cr, double y, double q, UnitConstants c, List<CalcMessage> messages)
    {
        if (double.IsFinite(cr.AlternateNormalDepth))
            messages.Add(CalcMessage.Warning(MessageCodes.TwoNormalDepths,
                F($"Two depths carry {q:F3} {c.Discharge}: {Math.Min(y, cr.AlternateNormalDepth):F4} and {Math.Max(y, cr.AlternateNormalDepth):F4} {c.Length}, because Q exceeds the full-flow capacity {cr.FullFlowDischarge:F3} {c.Discharge}. Flow this close to the crown is unstable and may seal the conduit; design with freeboard.")));
        if (y >= cr.Rise)
            messages.Add(CalcMessage.Info(MessageCodes.FlowingFull, "The conduit is flowing just full (gravity flow at the crown). Top width is zero, so hydraulic depth and Froude number are undefined."));
        else if (y > 0.9 * cr.Rise)
            messages.Add(CalcMessage.Warning(MessageCodes.NearCrown,
                F($"Depth is {cr.PercentFull:F1} % of the rise. Above about 90 % full the free surface is narrow: hydraulic depth A/T and the Froude number lose physical meaning, and small disturbances can seal the conduit.")));
    }

    // ---------------------------------------------------------------- core relations

    public static double HydraulicRadius(ISectionGeometry g, double y) => g.Area(y) / g.WettedPerimeter(y);

    public static double ManningDischarge(ISectionGeometry g, double y, double n, double s, double k)
    {
        if (y <= 0) return 0;
        double a = g.Area(y);
        return k / n * a * Math.Pow(a / g.WettedPerimeter(y), 2.0 / 3.0) * Math.Sqrt(s);
    }

    /// <summary>Z(y) = A √(A/T). Critical flow occurs where Z = Q / √g.</summary>
    public static double CriticalSectionFactor(ISectionGeometry g, double y)
    {
        if (y <= 0) return 0;
        double a = g.Area(y), t = g.TopWidth(y);
        return t > 0 ? a * Math.Sqrt(a / t) : double.PositiveInfinity;
    }

    public static RootResult SolveNormalDepth(ISectionGeometry g, double q, double n, double s, double k) =>
        RootFinder.BrentWithUpwardExpansion(
            y => ManningDischarge(g, y, n, s, k) - q,
            low: 0.0, initialHigh: 1.0, maxHigh: MaxSearchDepth, xTolerance: DepthTolerance);

    public static RootResult SolveCriticalDepth(ISectionGeometry g, double q, double gravity) =>
        RootFinder.BrentWithUpwardExpansion(
            y => CriticalSectionFactor(g, y) - q / Math.Sqrt(gravity),
            low: 0.0, initialHigh: Math.Min(1.0, 0.5 * g.MaxDepth),
            maxHigh: double.IsFinite(g.MaxDepth) ? g.MaxDepth * (1 - 1e-12) : MaxSearchDepth, xTolerance: DepthTolerance);

    public static SectionState State(ISectionGeometry g, double y, double q, double gravity)
    {
        double a = g.Area(y), p = g.WettedPerimeter(y), t = g.TopWidth(y);
        double v = q / a, d = t > 0 ? a / t : double.NaN, hv = v * v / (2 * gravity);
        return new SectionState
        {
            Depth = y,
            Area = a,
            WettedPerimeter = p,
            HydraulicRadius = a / p,
            TopWidth = t,
            HydraulicDepth = d,
            Velocity = v,
            VelocityHead = hv,
            SpecificEnergy = y + hv,
            Froude = v / Math.Sqrt(gravity * d),
        };
    }

    // ---------------------------------------------------------------- validation

    public static void Validate(UniformFlowInput input, List<CalcMessage> messages)
    {
        ValidateSection(input.Section, messages);
        bool need(SolveFor f) => input.SolveFor != f;

        if (need(SolveFor.Roughness))
        {
            if (!(input.ManningN > 0) || !double.IsFinite(input.ManningN))
                messages.Add(CalcMessage.Error(MessageCodes.RoughnessNotPositive, "Manning's n must be greater than zero.", nameof(input.ManningN)));
            else if (input.ManningN < 0.008 || input.ManningN > 0.20)
                messages.Add(CalcMessage.Warning(MessageCodes.RoughnessUnusual,
                    F($"Manning's n = {input.ManningN:F4} is outside the typical range 0.008–0.20 (Chow 1959, Table 5-6)."), nameof(input.ManningN)));
        }
        if (need(SolveFor.Slope))
        {
            if (!(input.Slope > 0) || !double.IsFinite(input.Slope))
                messages.Add(CalcMessage.Error(MessageCodes.SlopeNotPositive,
                    "Channel slope must be greater than zero. Uniform flow (normal depth) does not exist on horizontal or adverse slopes.", nameof(input.Slope)));
        }
        if (need(SolveFor.Discharge) && (!(input.Discharge > 0) || !double.IsFinite(input.Discharge)))
            messages.Add(CalcMessage.Error(MessageCodes.DischargeNotPositive, "Discharge must be greater than zero.", nameof(input.Discharge)));
        if (need(SolveFor.NormalDepth) && (!(input.Depth > 0) || !double.IsFinite(input.Depth)))
            messages.Add(CalcMessage.Error(MessageCodes.DepthNotPositive, "Flow depth must be greater than zero.", nameof(input.Depth)));
        else if (need(SolveFor.NormalDepth) && ConduitRise(input.Section) is { } rise && input.Depth > rise * (1 + 1e-12))
            messages.Add(CalcMessage.Error(MessageCodes.DepthExceedsRise,
                F($"Flow depth {input.Depth:G6} exceeds the conduit rise {rise:G6}. Pressurised flow is not modelled."), nameof(input.Depth)));
    }

    public static void ValidateSection(ChannelSection section, List<CalcMessage> messages)
    {
        static bool ok(double v) => double.IsFinite(v) && v >= 0;
        switch (section)
        {
            case RectangularSection r:
                if (!(r.BottomWidth > 0) || !double.IsFinite(r.BottomWidth))
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "Bottom width must be greater than zero.", "BottomWidth"));
                break;
            case TrapezoidalSection t:
                if (!ok(t.BottomWidth))
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "Bottom width must be zero or greater.", "BottomWidth"));
                if (!ok(t.LeftSideSlope))
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "Left side slope (H:V) must be zero or greater.", "LeftSideSlope"));
                if (!ok(t.RightSideSlope))
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "Right side slope (H:V) must be zero or greater.", "RightSideSlope"));
                if (ok(t.BottomWidth) && ok(t.LeftSideSlope) && ok(t.RightSideSlope) && t.BottomWidth == 0 && t.LeftSideSlope + t.RightSideSlope == 0)
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "A section with zero bottom width and vertical walls has no flow area.", "BottomWidth"));
                break;
            case TriangularSection v:
                if (!ok(v.LeftSideSlope))
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "Left side slope (H:V) must be zero or greater.", "LeftSideSlope"));
                if (!ok(v.RightSideSlope))
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "Right side slope (H:V) must be zero or greater.", "RightSideSlope"));
                if (ok(v.LeftSideSlope) && ok(v.RightSideSlope) && v.LeftSideSlope + v.RightSideSlope == 0)
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "At least one side slope must be greater than zero for a triangular section.", "LeftSideSlope"));
                break;
            case CircularSection p:
                if (!(p.Diameter > 0) || !double.IsFinite(p.Diameter))
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "Pipe diameter must be greater than zero.", "Diameter"));
                break;
            case EllipticalSection e:
                if (!(e.Rise > 0) || !double.IsFinite(e.Rise) || !(e.Span > 0) || !double.IsFinite(e.Span))
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "Rise and span must be greater than zero.", "Rise"));
                else if (e.EndRadius is { } r && !SymmetricArcGeometry.IsValidFourCentred(e.Rise, e.Span, r))
                    messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "The end radius must be positive and smaller than half the smaller of rise and span.", "EndRadius"));
                break;
            default:
                messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, $"Section type {section.GetType().Name} is not supported."));
                break;
        }
        if (section.ChannelDepth is { } cd && !(cd > 0 && double.IsFinite(cd)))
            messages.Add(CalcMessage.Error(MessageCodes.GeometryInvalid, "Channel depth, when given, must be greater than zero.", "ChannelDepth"));
    }

    private static double? ConduitRise(ChannelSection s) => s switch
    {
        CircularSection { Diameter: > 0 } c => c.Diameter,
        EllipticalSection { Rise: > 0 } e => e.Rise,
        _ => null,
    };

    private static void AddPostSolveMessages(UniformFlowInput input, double y, double s, double froude, FlowRegime regime, UnitConstants c, List<CalcMessage> messages)
    {
        if (input.Section.ChannelDepth is { } cd && y > cd)
            messages.Add(CalcMessage.Warning(MessageCodes.Overtopping,
                F($"Flow depth {y:F3} {c.Length} exceeds the channel depth {cd:F3} {c.Length}. The result assumes the section walls extend upward at the same slopes; in reality the channel would overtop."), "ChannelDepth"));
        if (s > 0.10)
            messages.Add(CalcMessage.Warning(MessageCodes.SlopeSteep,
                F($"Slope {s * 100:F2} % is steep (> 10 %). Manning's equation was developed for mild slopes; air entrainment and roll waves are not represented and depth is measured vertically.")));
        else if (s < 1e-5)
            messages.Add(CalcMessage.Warning(MessageCodes.SlopeVeryMild,
                F($"Slope {s * 100:F5} % is extremely mild. Uniform flow is unlikely to develop; downstream control will usually govern depth (gradually varied flow).")));
        switch (regime)
        {
            case FlowRegime.NearCritical:
                messages.Add(CalcMessage.Warning(MessageCodes.NearCritical,
                    F($"Froude number {froude:F3} is within {NearCriticalLow}–{NearCriticalHigh}. Near-critical flow is unstable and the water surface may undulate; USACE EM 1110-2-1601 recommends avoiding this range in design.")));
                break;
            case FlowRegime.Supercritical:
                messages.Add(CalcMessage.Info(MessageCodes.Supercritical, F($"Supercritical flow (Fr = {froude:F3}): normal depth is below critical depth.")));
                break;
            case FlowRegime.Undefined:
                break;
            default:
                messages.Add(CalcMessage.Info(MessageCodes.Subcritical, F($"Subcritical flow (Fr = {froude:F3}): normal depth is above critical depth.")));
                break;
        }
    }

    private static bool AcceptRoot(RootResult root, Func<double, double> eval, double target, string what, List<CalcMessage> messages)
    {
        if (!root.Converged || !double.IsFinite(root.Root))
        {
            messages.Add(CalcMessage.Error(MessageCodes.NoConvergence, $"The {what} solution did not converge: {root.FailureReason ?? "unknown reason"} No result is reported."));
            return false;
        }
        double rel = Math.Abs(eval(root.Root) - target) / Math.Max(Math.Abs(target), double.Epsilon);
        if (rel > AcceptedRelativeResidual)
        {
            messages.Add(CalcMessage.Error(MessageCodes.NoConvergence,
                F($"The {what} solution stopped with a relative residual of {rel:E2}, above the acceptance limit {AcceptedRelativeResidual:E0}. No result is reported.")));
            return false;
        }
        return true;
    }

    private static string DescribeRoot(string label, string function, RootResult r, string unit) => r.Converged
        ? F($"{label}: {r.Method} root of {function}; bracket [{r.BracketLow:G6}, {r.BracketHigh:G6}] {unit}; {r.Iterations} iterations; y = {r.Root:G12} {unit}.")
        : F($"{label}: {r.Method} search FAILED ({r.FailureReason}).");

    private static UniformFlowResult Failed(UniformFlowInput input, UnitConstants c, List<CalcMessage> messages, List<string> trace) =>
        new() { Input = input, Constants = c, Succeeded = false, Messages = messages, Trace = trace };

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
