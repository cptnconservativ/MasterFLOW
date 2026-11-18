using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;
using MasterFlow.Hydraulics.Numerics;

namespace MasterFlow.Application.Culverts;

/// <summary>Closed rectangular (box) barrel of span B and rise D, flowing partly full or full.</summary>
public sealed class BoxGeometry(double span, double rise) : ISectionGeometry
{
    public double Span { get; } = span;
    public double Rise { get; } = rise;
    public double MaxDepth => Rise;
    public bool DischargeIsMonotonic => false;
    public double Area(double y) => y <= 0 ? 0 : Span * Math.Min(y, Rise);
    public double WettedPerimeter(double y) => y <= 0 ? 0 : y >= Rise ? 2 * (Span + Rise) : Span + 2 * y;
    public double TopWidth(double y) => y <= 0 || y >= Rise ? 0 : Span;
    public IReadOnlyList<(double X, double Y)> Outline(double height) => [(0, 0), (Span, 0), (Span, Rise), (0, Rise), (0, 0)];
    public (double Left, double Right) WaterSurfaceExtent(double depth, double height) => (0, Span);
    public IReadOnlyList<(double X, double Y)> WaterPolygon(double depth, double height) { double y = Math.Clamp(depth, 0, Rise); return [(0, 0), (Span, 0), (Span, y), (0, y), (0, 0)]; }
}

/// <summary>
/// One culvert crossing: identical barrels with one inlet configuration. Elevations are absolute (same datum as
/// the roadway and tailwater); lengths in the unit system's length unit. <see cref="Rise"/> is HDS-5's D.
/// </summary>
public sealed record CulvertDefinition
{
    public required BarrelShape Shape { get; init; }
    /// <summary>Circular: diameter. Box and elliptical: inside rise. HDS-5's D.</summary>
    public required double Rise { get; init; }
    /// <summary>Box and elliptical: inside span. Ignored for circular.</summary>
    public double Span { get; init; }
    public int Barrels { get; init; } = 1;
    public required double N { get; init; }
    public required double Length { get; init; }
    public required double UpstreamInvert { get; init; }
    public required double DownstreamInvert { get; init; }
    public required InletCoefficients Inlet { get; init; }
    /// <summary>Entrance loss coefficient for outlet control.</summary>
    public required double Ke { get; init; }

    public double Slope => (UpstreamInvert - DownstreamInvert) / Length;

    public ISectionGeometry Geometry() => Shape switch
    {
        BarrelShape.Circular => new CircularGeometry(Rise),
        BarrelShape.Box => new BoxGeometry(Span, Rise),
        _ => SymmetricArcGeometry.From(new EllipticalSection(Rise, Span)),
    };
}

/// <summary>Roadway crest for overtopping: a profile of (station, elevation) along the road over the culvert.</summary>
public sealed record RoadwayCrest(IReadOnlyList<(double Station, double Elevation)> Profile, double WeirCoefficient)
{
    public double LowPoint => Profile.Min(p => p.Elevation);

    /// <summary>
    /// Broad-crested weir flow over the crest at headwater elevation <paramref name="wse"/>: Q = Σ Cd kt h^1.5 Δx, with h
    /// from the linearly interpolated crest. kt is the Villemonte submergence factor (1 − (ht/h)^1.5)^0.385 when the
    /// tailwater is above the crest (ht = tailwater − crest).
    /// </summary>
    public double Flow(double wse, double tailwaterElevation)
    {
        double q = 0;
        for (int i = 0; i + 1 < Profile.Count; i++)
        {
            var (x0, z0) = Profile[i];
            var (x1, z1) = Profile[i + 1];
            if (wse <= Math.Min(z0, z1)) continue;
            const int sub = 64;
            double dx = (x1 - x0) / sub;
            for (int k = 0; k < sub; k++)
            {
                double z = z0 + (z1 - z0) * (k + 0.5) / sub, h = wse - z;
                if (h <= 0) continue;
                double kt = 1;
                double ht = tailwaterElevation - z;
                if (ht > 0) kt = ht >= h ? 0 : Math.Pow(1 - Math.Pow(ht / h, 1.5), 0.385);
                q += WeirCoefficient * kt * Math.Pow(h, 1.5) * Math.Abs(dx);
            }
        }
        return q;
    }
}

/// <summary>Inlet control computation for one barrel, with the intermediate values a checker needs.</summary>
public sealed record InletControlResult(double Headwater, string Regime, double X, double Dc, double Hc, string Equation);

/// <summary>Outlet control computation for one barrel.</summary>
public sealed record OutletControlResult(double Headwater, double Ho, double H, double Kf, double VelocityHead, double HeadwaterBackwater, string BackwaterNote);

/// <summary>Everything computed for one total flow.</summary>
public sealed record CulvertPoint
{
    public double TotalFlow { get; init; }
    public double CulvertFlow { get; init; }
    public double OvertoppingFlow { get; init; }
    public double TailwaterDepth { get; init; }
    public required InletControlResult Inlet { get; init; }
    public required OutletControlResult Outlet { get; init; }
    /// <summary>Controlling headwater depth above the upstream invert.</summary>
    public double Headwater { get; init; }
    public double HeadwaterElevation { get; init; }
    public string Control { get; init; } = "";
    public double NormalDepth { get; init; }
    public double CriticalDepth { get; init; }
    public double OutletDepth { get; init; }
    public double OutletVelocity { get; init; }
    public string OutletBasis { get; init; } = "";
}

/// <summary>Tailwater depth above the outlet invert for a total flow.</summary>
public delegate double TailwaterFunction(double totalFlow);

/// <summary>
/// Culvert hydraulics per FHWA HDS-5 (3rd ed., 2012):
/// <list type="bullet">
/// <item>Inlet control, eqs. A.1–A.3: unsubmerged Form 1 HWi/D = Hc/D + K (Ku Q/(A D^0.5))^M + Ks S, Form 2 HWi/D = K (Ku Q/(A D^0.5))^M,
/// for Ku Q/(A D^0.5) ≤ 3.5; submerged HWi/D = c (Ku Q/(A D^0.5))² + Y + Ks S for ≥ 4.0; linear between the two in the transition.</item>
/// <item>Outlet control (ch. 3): HWo = ho + H − S L, H = (1 + Ke + Kf) V²/2g, Kf = 2g n² L/(k² R^4/3), ho = TW when TW ≥ D, else max(TW, (dc + D)/2).
/// A backwater (standard step) value is computed alongside for partly full barrels.</item>
/// <item>Controlling headwater = the larger; roadway overtopping balances Q = Qculvert(HW) + Qroad(HW).</item>
/// </list>
/// Q is per barrel inside the inlet and outlet computations; reported flows are totals.
/// </summary>
public sealed class CulvertAnalysis
{
    private readonly CulvertDefinition _c;
    private readonly ISectionGeometry _g;
    private readonly UnitConstants _u;
    private readonly bool _si;
    public bool UseBackwater { get; init; }
    public double FullArea { get; }
    public double FullHydraulicRadius { get; }

    public CulvertAnalysis(CulvertDefinition culvert, UnitSystem units)
    {
        _c = culvert;
        _g = culvert.Geometry();
        _u = UnitConstants.For(units);
        _si = units == UnitSystem.SI;
        FullArea = _g.Area(_g.MaxDepth);
        FullHydraulicRadius = FullArea / _g.WettedPerimeter(_g.MaxDepth);
    }

    public ISectionGeometry Geometry => _g;
    private double D => _c.Rise;

    public double CriticalDepth(double qb)
    {
        if (qb <= 0) return 0;
        var r = UniformFlowCalculator.SolveCriticalDepth(_g, qb, _u.Gravity);
        return r.Converged ? Math.Min(r.Root, D) : D;
    }

    /// <summary>Normal depth in the barrel, or D when the barrel cannot carry qb partly full (or the slope is not positive).</summary>
    public double NormalDepth(double qb)
    {
        if (qb <= 0) return 0;
        if (_c.Slope <= 0) return D;
        var peak = UniformFlowCalculator.MaxDischarge(_g, _c.N, _c.Slope, _u.ManningK);
        if (qb >= peak.Discharge) return D;
        // Lower branch: the depth below the discharge peak.
        var r = RootFinder.Brent(y => UniformFlowCalculator.ManningDischarge(_g, y, _c.N, _c.Slope, _u.ManningK) - qb, 1e-9 * D, peak.Depth, 1e-10 * D);
        return r.Converged ? r.Root : D;
    }

    public InletControlResult InletControl(double qb)
    {
        if (qb <= 0) return new(0, "no flow", 0, 0, 0, "");
        var inlet = _c.Inlet;
        double ku = CulvertInlets.Ku(_si), a = FullArea, s = _c.Slope;
        double X(double q) => ku * q / (a * Math.Sqrt(D));
        double x = X(qb);
        double dc = CriticalDepth(qb);
        double hc = dc + Math.Pow(qb / _g.Area(dc), 2) / (2 * _u.Gravity);
        double Unsub(double q, out double hcq)
        {
            double xx = X(q);
            if (inlet.Form == 1)
            {
                double dcq = CriticalDepth(q);
                hcq = dcq + Math.Pow(q / _g.Area(dcq), 2) / (2 * _u.Gravity);
                return D * (hcq / D + inlet.K * Math.Pow(xx, inlet.M) + inlet.Ks * s);
            }
            hcq = double.NaN;
            return D * inlet.K * Math.Pow(xx, inlet.M);
        }
        double Sub(double q) => D * (inlet.C * Math.Pow(X(q), 2) + inlet.Y + inlet.Ks * s);
        string form = inlet.Form == 1 ? "Form 1: HW/D = Hc/D + K(KuQ/AD^0.5)^M + Ks·S" : "Form 2: HW/D = K(KuQ/AD^0.5)^M";
        if (x <= CulvertInlets.UnsubmergedLimit)
            return new(Unsub(qb, out _), "unsubmerged", x, dc, hc, form);
        if (x >= CulvertInlets.SubmergedLimit)
            return new(Sub(qb), "submerged", x, dc, hc, "Submerged: HW/D = c(KuQ/AD^0.5)² + Y + Ks·S");
        double q35 = CulvertInlets.UnsubmergedLimit * a * Math.Sqrt(D) / ku, q40 = CulvertInlets.SubmergedLimit * a * Math.Sqrt(D) / ku;
        double h35 = Unsub(q35, out _), h40 = Sub(q40);
        double t = (x - CulvertInlets.UnsubmergedLimit) / (CulvertInlets.SubmergedLimit - CulvertInlets.UnsubmergedLimit);
        return new(h35 + t * (h40 - h35), "transition", x, dc, hc, $"Transition: linear between {form.Split(':')[0]} at KuQ/AD^0.5 = 3.5 ({h35:F3}) and submerged at 4.0 ({h40:F3})");
    }

    /// <summary>Kf = 2g n² L / (k² R^4/3); 29 n² L / R^1.33 in U.S. units.</summary>
    public double FrictionCoefficient => 2 * _u.Gravity * _c.N * _c.N * _c.Length / (_u.ManningK * _u.ManningK * Math.Pow(FullHydraulicRadius, 4.0 / 3.0));

    public OutletControlResult OutletControl(double qb, double tw) => OutletControl(qb, tw, true);

    /// <summary>Outlet control; <paramref name="withBackwater"/> = false skips the standard-step value (NaN) for speed.</summary>
    public OutletControlResult OutletControl(double qb, double tw, bool withBackwater)
    {
        if (qb <= 0) return new(Math.Max(0, tw + _c.DownstreamInvert - _c.UpstreamInvert), tw, 0, FrictionCoefficient, 0, double.NaN, "");
        double v = qb / FullArea, hv = v * v / (2 * _u.Gravity), kf = FrictionCoefficient;
        double h = (1 + _c.Ke + kf) * hv;
        double dc = CriticalDepth(qb);
        double ho = tw >= D ? tw : Math.Max(tw, (dc + D) / 2);
        double hw = _c.DownstreamInvert + ho + h - _c.UpstreamInvert;
        var (bw, note) = tw >= D ? (hw, "outlet submerged: full flow") : withBackwater ? Backwater(qb, tw, dc) : (double.NaN, "");
        return new(hw, ho, h, kf, hv, bw, note);
    }

    /// <summary>
    /// Outlet control headwater by a standard-step profile up the barrel from max(TW, dc) at the outlet. Where the
    /// profile reaches the crown the rest of the barrel flows full (friction at the full-flow rate). HW = inlet
    /// energy + Ke·hv. A steep barrel (yn &lt; dc) with a low tailwater has critical depth at the inlet.
    /// </summary>
    public (double Headwater, string Note) Backwater(double qb, double tw, double dc)
    {
        double grav = _u.Gravity, k = _u.ManningK, n = _c.N, s0 = _c.Slope, L = _c.Length;
        double E(double y) { double a = _g.Area(y); return y + qb * qb / (2 * grav * a * a); }
        double Sf(double y) { double a = _g.Area(y), r = a / _g.WettedPerimeter(y); return Math.Pow(n * qb / (k * a * Math.Pow(r, 2.0 / 3.0)), 2); }
        double yn = NormalDepth(qb);
        double hvOf(double y) => E(y) - y;
        if (yn < dc - 1e-9 && tw < dc)
            return (dc + (1 + _c.Ke) * hvOf(dc), "steep barrel: critical depth at the inlet");
        double y = Math.Min(Math.Max(tw, dc), D * (1 - 1e-9));
        const int steps = 400;
        double dx = L / steps, x = 0;
        double yTop = D * (1 - 1e-9);
        for (int i = 0; i < steps; i++)
        {
            double e1 = E(y), sf1 = Sf(y);
            double F(double y2) => E(y2) - (e1 - s0 * dx + 0.5 * (sf1 + Sf(y2)) * dx);
            double lo = Math.Max(dc * (1 + 1e-9), 1e-9 * D), hi = yTop;
            if (lo >= hi || F(hi) < 0)
            {
                // The crown is reached within this step: find where from E(D) = E1 + (Sf̄ − S0)·Δx'.
                double sfD = Sf(yTop), denom = 0.5 * (sf1 + sfD) - s0;
                double part = denom > 0 ? Math.Clamp((E(yTop) - e1) / denom, 0, dx) : dx;
                x += part;
                double hvFull = Math.Pow(qb / FullArea, 2) / (2 * grav);
                double remaining = L - x;
                double sfFull = Math.Pow(n * qb / (k * FullArea * Math.Pow(FullHydraulicRadius, 2.0 / 3.0)), 2);
                double eIn = D + hvFull + (sfFull - s0) * remaining;
                return (eIn + _c.Ke * hvFull, $"barrel full over the upstream {remaining:F1} of {L:F1} (crown reached {x:F1} from the outlet)");
            }
            if (F(lo) > 0) { y = lo; x += dx; continue; }   // drawdown toward critical: hold at critical
            for (int it = 0; it < 80; it++) { double m = 0.5 * (lo + hi); if (F(m) < 0) lo = m; else hi = m; }
            y = 0.5 * (lo + hi);
            x += dx;
        }
        return (y + (1 + _c.Ke) * hvOf(y), "partly full throughout");
    }

    /// <summary>Controlling headwater (above the upstream invert) for flow per barrel <paramref name="qb"/>.</summary>
    public double ControllingHeadwater(double qb, double tw)
    {
        double hi = InletControl(qb).Headwater;
        var oc = OutletControl(qb, tw, UseBackwater);
        return Math.Max(hi, UseBackwater && double.IsFinite(oc.HeadwaterBackwater) ? oc.HeadwaterBackwater : oc.Headwater);
    }

    /// <summary>
    /// Total culvert flow (all barrels) at a headwater depth <paramref name="hw"/> above the upstream invert: the inverse
    /// of <see cref="ControllingHeadwater"/> by bisection. The controlling headwater rises with Q, so the rating is
    /// continuous and rising.
    /// </summary>
    public double Capacity(double hw, double tw)
    {
        if (!(hw > 0)) return 0;
        double lo = 0, hi = Math.Max(1e-6, FullArea * Math.Sqrt(2 * _u.Gravity * hw));
        int guard = 0;
        while (ControllingHeadwater(hi, tw) < hw && guard++ < 60) { lo = hi; hi *= 2; }
        for (int i = 0; i < 60 && hi - lo > 1e-9 * hi; i++)
        {
            double m = 0.5 * (lo + hi);
            if (ControllingHeadwater(m, tw) < hw) lo = m; else hi = m;
        }
        return 0.5 * (lo + hi) * _c.Barrels;
    }

    /// <summary>Culvert alone (no overtopping) at total culvert flow <paramref name="qCulvert"/> with tailwater depth <paramref name="tw"/>.</summary>
    public CulvertPoint Evaluate(double qCulvert, double tw)
    {
        double qb = qCulvert / _c.Barrels;
        var ic = InletControl(qb);
        var oc = OutletControl(qb, tw);
        double ho = UseBackwater && double.IsFinite(oc.HeadwaterBackwater) ? oc.HeadwaterBackwater : oc.Headwater;
        bool inlet = ic.Headwater >= ho;
        double hw = Math.Max(ic.Headwater, ho);
        double yn = NormalDepth(qb), dc = CriticalDepth(qb);
        double yo; string basis;
        if (inlet && yn < dc && tw < dc) { yo = yn; basis = "inlet control, steep barrel: normal depth"; }
        else if (tw >= D) { yo = D; basis = "tailwater above the crown: full barrel"; }
        else { yo = Math.Min(D, Math.Max(dc, tw)); basis = tw > dc ? "tailwater depth" : "critical depth"; }
        double vo = qb > 0 ? qb / _g.Area(yo) : 0;
        return new CulvertPoint
        {
            TotalFlow = qCulvert, CulvertFlow = qCulvert, TailwaterDepth = tw, Inlet = ic, Outlet = oc,
            Headwater = hw, HeadwaterElevation = _c.UpstreamInvert + hw, Control = qb <= 0 ? "" : inlet ? "inlet" : "outlet",
            NormalDepth = yn, CriticalDepth = dc, OutletDepth = yo, OutletVelocity = vo, OutletBasis = basis,
        };
    }

    /// <summary>
    /// Total flow <paramref name="q"/> shared between the barrels and the roadway: when the culvert alone would put the
    /// headwater above the crest low point, the culvert flow is found (bisection) so that Qculvert + Qroad(HW) = Q.
    /// </summary>
    public CulvertPoint Solve(double q, TailwaterFunction tailwater, RoadwayCrest? road)
    {
        double tw = tailwater(q);
        var alone = Evaluate(q, tw);
        if (road is null || alone.HeadwaterElevation <= road.LowPoint) return alone;
        double twElev = _c.DownstreamInvert + tw;
        double F(double qc) => qc + road.Flow(Evaluate(qc, tw).HeadwaterElevation, twElev) - q;
        double lo = 0, hi = q;
        if (F(lo) >= 0) { var p0 = Evaluate(0, tw); return p0 with { TotalFlow = q, CulvertFlow = 0, OvertoppingFlow = q }; }
        for (int i = 0; i < 100 && hi - lo > 1e-9 * Math.Max(1, q); i++) { double m = 0.5 * (lo + hi); if (F(m) < 0) lo = m; else hi = m; }
        double qc = 0.5 * (lo + hi);
        var p = Evaluate(qc, tw);
        return p with { TotalFlow = q, CulvertFlow = qc, OvertoppingFlow = road.Flow(p.HeadwaterElevation, twElev) };
    }
}
