namespace MasterFlow.Application.Hydrology;

/// <summary>
/// Design storms as cumulative rainfall depth (in) at every computation step from t = 0. U.S. units throughout.
/// </summary>
public static class DesignStorms
{
    /// <summary>Rapid City IDCM 2012 Table 4-2: durations (min) and depths (in) for the 2-, 10- and 100-yr storms.</summary>
    public static readonly double[] RapidCityDurations = [5, 15, 60, 120, 180, 360, 720, 1440];
    public static readonly IReadOnlyDictionary<int, double[]> RapidCityDepths = new Dictionary<int, double[]>
    {
        [2] = [0.36, 0.69, 1.05, 1.20, 1.25, 1.55, 1.70, 2.00],
        [10] = [0.53, 1.04, 1.86, 1.98, 2.10, 2.35, 2.70, 3.07],
        [100] = [0.79, 1.57, 2.95, 3.06, 3.15, 3.58, 4.05, 4.55],
    };

    /// <summary>Depth for a duration by log–log interpolation of a depth–duration table (linear in log D vs log P); NaN outside the table.</summary>
    public static double DepthAt(double[] durations, double[] depths, double d)
    {
        if (d < durations[0] - 1e-9 || d > durations[^1] + 1e-9) return double.NaN;
        for (int i = 1; i < durations.Length; i++)
            if (d <= durations[i] + 1e-9)
            {
                double f = Math.Log(d / durations[i - 1]) / Math.Log(durations[i] / durations[i - 1]);
                return Math.Exp(Math.Log(depths[i - 1]) + f * Math.Log(depths[i] / depths[i - 1]));
            }
        return depths[^1];
    }

    /// <summary>
    /// Frequency-based design storm (HEC-HMS "frequency storm", IDCM §4.3.2): depths for durations Δ, 2Δ … D from the
    /// depth–duration table, incremental blocks arranged with the largest at <paramref name="peakFraction"/> of the storm
    /// (IDCM: first quartile) and the rest alternating after and before it (largest remaining next). Returns the block
    /// depths (in), one per block of <paramref name="blockMin"/> minutes.
    /// </summary>
    public static double[] FrequencyStormBlocks(double[] durations, double[] depths, double stormMin, double blockMin, double peakFraction)
    {
        int n = (int)Math.Round(stormMin / blockMin);
        var cum = Enumerable.Range(1, n).Select(k => DepthAt(durations, depths, k * blockMin)).ToArray();
        var inc = cum.Select((c, k) => k == 0 ? c : c - cum[k - 1]).OrderByDescending(x => x).ToArray();
        var blocks = new double[n];
        int peak = Math.Clamp((int)Math.Floor(peakFraction * n), 0, n - 1);
        int left = peak - 1, right = peak + 1;
        blocks[peak] = inc[0];
        bool goRight = true;
        for (int i = 1; i < n; i++)
        {
            if ((goRight && right < n) || left < 0) blocks[right++] = inc[i];
            else blocks[left--] = inc[i];
            goRight = !goRight;
        }
        return blocks;
    }

    /// <summary>
    /// NRCS 24-hour Type II distribution, cumulative fraction of the 24-h depth at the listed hours (TR-55, 1986,
    /// Appendix B / NEH 630 Ch. 4 tabulation); linear between ordinates.
    /// </summary>
    public static readonly (double Hour, double Fraction)[] TypeII =
    [
        (0, 0), (1, 0.011), (2, 0.022), (3, 0.035), (4, 0.048), (5, 0.063), (6, 0.080), (7, 0.098), (8, 0.120), (8.5, 0.133),
        (9, 0.147), (9.5, 0.163), (9.75, 0.172), (10, 0.181), (10.5, 0.204), (11, 0.235), (11.5, 0.283), (11.75, 0.357),
        (12, 0.663), (12.5, 0.735), (13, 0.772), (13.5, 0.799), (14, 0.820), (16, 0.880), (20, 0.952), (24, 1.000),
    ];

    /// <summary>Linear interpolation in a (time, cumulative) table; the last value beyond its end.</summary>
    public static double Interp(IReadOnlyList<(double X, double Y)> t, double x)
    {
        if (x <= t[0].X) return t[0].Y;
        for (int i = 1; i < t.Count; i++)
            if (x <= t[i].X) return t[i - 1].Y + (t[i].Y - t[i - 1].Y) * (x - t[i - 1].X) / (t[i].X - t[i - 1].X);
        return t[^1].Y;
    }

    /// <summary>Cumulative depth at t = 0, dt, 2dt … for <paramref name="steps"/> steps from a cumulative (min, in) curve.</summary>
    public static double[] Sample(IReadOnlyList<(double Min, double Depth)> curve, double dtMin, int steps) =>
        Enumerable.Range(0, steps + 1).Select(k => Interp(curve, k * dtMin)).ToArray();

    /// <summary>Cumulative (min, in) curve from blocks of equal length (uniform intensity within each block).</summary>
    public static List<(double Min, double Depth)> FromBlocks(double[] blocks, double blockMin)
    {
        var c = new List<(double, double)> { (0, 0) };
        double sum = 0;
        for (int i = 0; i < blocks.Length; i++) { sum += blocks[i]; c.Add(((i + 1) * blockMin, sum)); }
        return c;
    }
}

/// <summary>Rainfall losses: cumulative excess (in) at each step from cumulative rainfall (in).</summary>
public static class Losses
{
    /// <summary>NRCS curve number runoff, Q = (P − Ia)² / (P − Ia + S), S = 1000/CN − 10, Ia = λS (TR-55 Eq. 2-1 to 2-4; λ = 0.2).</summary>
    public static double CurveNumberRunoff(double p, double cn, double lambda = 0.2)
    {
        double s = 1000 / cn - 10, ia = lambda * s;
        return p <= ia ? 0 : (p - ia) * (p - ia) / (p - ia + s);
    }

    public static double[] CurveNumber(double[] cumRain, double cn, double lambda = 0.2) => cumRain.Select(p => CurveNumberRunoff(p, cn, lambda)).ToArray();

    /// <summary>
    /// Green and Ampt infiltration as HEC-HMS applies it (IDCM §4.3.3–4.3.6): the pervious part first fills the initial
    /// abstraction <paramref name="ia"/>, then infiltrates at capacity f = Ks (1 + ψΔθ / F). Within a step the ponded
    /// infiltration solves F₂ − F₁ − ψΔθ ln((F₂ + ψΔθ)/(F₁ + ψΔθ)) = Ks Δt (exact Green–Ampt), limited to the rain
    /// available. The impervious fraction <paramref name="impervious"/> (0–1, the effective impervious area) has no loss.
    /// </summary>
    public static double[] GreenAmpt(double[] cumRain, double dtHours, double ks, double suction, double deficit, double ia, double impervious)
    {
        double sd = suction * deficit, f = 0, store = 0, excessPerv = 0;
        var r = new double[cumRain.Length];
        for (int k = 1; k < cumRain.Length; k++)
        {
            double rain = Math.Max(0, cumRain[k] - cumRain[k - 1]);
            double toIa = Math.Min(rain, Math.Max(0, ia - store));
            store += toIa;
            double avail = rain - toIa, inf = 0;
            if (avail > 0)
            {
                double cap;
                if (sd <= 0) cap = ks * dtHours;
                else
                {
                    // Newton on g(F2) = F2 − F1 − sd·ln((F2+sd)/(F1+sd)) − Ks·dt, increasing for F2 > F1.
                    double f2 = f + ks * dtHours + Math.Sqrt(2 * ks * dtHours * sd);
                    for (int i = 0; i < 50; i++)
                    {
                        double g = f2 - f - sd * Math.Log((f2 + sd) / (f + sd)) - ks * dtHours, dg = 1 - sd / (f2 + sd);
                        double next = f2 - g / dg;
                        if (next <= f) next = 0.5 * (f2 + f);
                        if (Math.Abs(next - f2) < 1e-12) { f2 = next; break; }
                        f2 = next;
                    }
                    cap = f2 - f;
                }
                inf = Math.Min(avail, cap);
            }
            f += inf;
            excessPerv += avail - inf;
            r[k] = (1 - impervious) * excessPerv + impervious * cumRain[k];
        }
        return r;
    }

    /// <summary>
    /// Green–Ampt soil parameters of Rawls, Brakensiek and Miller (1983), as reprinted in Chow, Maidment and Mays (1988)
    /// Table 4.3.1: suction head (in), Ks (in/h) and effective porosity by soil class. The default soil table.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (double Suction, double Ks, double EffPorosity)> RawlsSoils =
        new Dictionary<string, (double, double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["sand"] = (1.949, 4.638, 0.417), ["loamy sand"] = (2.413, 1.177, 0.401), ["sandy loam"] = (4.335, 0.429, 0.412),
            ["loam"] = (3.500, 0.134, 0.434), ["silt loam"] = (6.567, 0.256, 0.486), ["sandy clay loam"] = (8.602, 0.059, 0.330),
            ["clay loam"] = (8.220, 0.039, 0.309), ["silty clay loam"] = (10.748, 0.039, 0.432), ["sandy clay"] = (9.409, 0.024, 0.321),
            ["silty clay"] = (11.504, 0.020, 0.423), ["clay"] = (12.453, 0.012, 0.385),
        };

    /// <summary>
    /// Rapid City IDCM 2012 Table 4-4 Green–Ampt soil parameters: suction head (in) and Ks (in/h) by soil class. Same as
    /// <see cref="RawlsSoils"/> except that the IDCM prints the loam and silt loam conductivities the other way round
    /// (loam 0.65 cm/h, silt loam 0.34 cm/h).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (double Suction, double Ks, double EffPorosity)> RapidCitySoils =
        new Dictionary<string, (double, double, double)>(StringComparer.OrdinalIgnoreCase)
        {
            ["sand"] = (1.949, 4.638, 0.417), ["loamy sand"] = (2.413, 1.177, 0.401), ["sandy loam"] = (4.335, 0.429, 0.412),
            ["loam"] = (3.500, 0.256, 0.434), ["silt loam"] = (6.567, 0.134, 0.486), ["sandy clay loam"] = (8.602, 0.059, 0.330),
            ["clay loam"] = (8.220, 0.039, 0.309), ["silty clay loam"] = (10.748, 0.039, 0.432), ["sandy clay"] = (9.409, 0.024, 0.321),
            ["silty clay"] = (11.504, 0.020, 0.423), ["clay"] = (12.453, 0.012, 0.385),
        };

    /// <summary>IDCM Eq. 4-3-1 (Sutherland): EIA = 0.1·MIA^1.5, both in percent, for MIA ≥ 1 %.</summary>
    public static double Sutherland(double miaPercent) => miaPercent < 1 ? 0 : Math.Min(100, 0.1 * Math.Pow(miaPercent, 1.5));
}

/// <summary>NRCS (SCS) dimensionless unit hydrograph, NEH 630 Ch. 16 Table 16-1, peak rate factor 484.</summary>
public static class ScsUnitHydrograph
{
    public static readonly (double T, double Q)[] Dimensionless =
    [
        (0, 0), (0.1, 0.030), (0.2, 0.100), (0.3, 0.190), (0.4, 0.310), (0.5, 0.470), (0.6, 0.660), (0.7, 0.820), (0.8, 0.930),
        (0.9, 0.990), (1.0, 1.000), (1.1, 0.990), (1.2, 0.930), (1.3, 0.860), (1.4, 0.780), (1.5, 0.680), (1.6, 0.560), (1.7, 0.460),
        (1.8, 0.390), (1.9, 0.330), (2.0, 0.280), (2.2, 0.207), (2.4, 0.147), (2.6, 0.107), (2.8, 0.077), (3.0, 0.055), (3.2, 0.040),
        (3.4, 0.029), (3.6, 0.021), (3.8, 0.015), (4.0, 0.011), (4.5, 0.005), (5.0, 0),
    ];

    /// <summary>Time to peak Tp = D/2 + lag (h).</summary>
    public static double TimeToPeak(double lagHours, double dHours) => dHours / 2 + lagHours;

    /// <summary>qp = 484·A·Q/Tp: peak (cfs) of the unit hydrograph for 1 in of runoff from A mi².</summary>
    public static double PeakPerInch(double areaSqMi, double tpHours) => 484 * areaSqMi / tpHours;

    /// <summary>
    /// Unit hydrograph ordinates (cfs per inch of excess in one step of <paramref name="dtHours"/>) at t = dt, 2dt …,
    /// sampled from the dimensionless curve and scaled so its volume is exactly 1 in over the area (the table, being
    /// rounded, holds 1.3 % more or less depending on sampling; HEC-HMS also conserves volume).
    /// </summary>
    public static double[] Ordinates(double areaAcres, double lagHours, double dtHours)
    {
        double tp = TimeToPeak(lagHours, dtHours), qp = PeakPerInch(areaAcres / 640, tp);
        int n = (int)Math.Ceiling(5 * tp / dtHours) + 1;
        var u = new double[n];
        var curve = Dimensionless.Select(x => (x.T, x.Q)).ToList();
        for (int k = 0; k < n; k++) u[k] = qp * DesignStorms.Interp(curve, (k + 1) * dtHours / tp);
        double vol = u.Sum() * dtHours * 3600, target = areaAcres * 43560 / 12;
        if (vol > 0) for (int k = 0; k < n; k++) u[k] *= target / vol;
        return u;
    }

    /// <summary>Runoff hydrograph (cfs) at t = 0, dt … from cumulative excess (in) by discrete convolution.</summary>
    public static double[] Convolve(double[] cumExcess, double[] uh, int length)
    {
        var q = new double[length];
        for (int k = 1; k < cumExcess.Length; k++)
        {
            double pe = cumExcess[k] - cumExcess[k - 1];
            if (pe <= 0) continue;
            // excess in step k (ending at k·dt) drives ordinate j at time (k − 1 + j + 1)·dt = (k + j)·dt
            for (int j = 0; j < uh.Length && k + j < length; j++) q[k + j] += pe * uh[j];
        }
        return q;
    }
}

/// <summary>TR-55 (1986) Chapter 3 travel time: sheet, shallow concentrated and channel flow.</summary>
public static class Tr55
{
    /// <summary>Sheet flow, Eq. 3-3: Tt = 0.007 (nL)^0.8 / (P2^0.5 s^0.4), h.</summary>
    public static double SheetHours(double n, double lengthFt, double p2In, double slope) => 0.007 * Math.Pow(n * lengthFt, 0.8) / (Math.Sqrt(p2In) * Math.Pow(slope, 0.4));

    /// <summary>Shallow concentrated flow, Figure 3-1 equations: V = 16.1345 √s unpaved, 20.3282 √s paved (ft/s).</summary>
    public static double ShallowVelocity(bool paved, double slope) => (paved ? 20.3282 : 16.1345) * Math.Sqrt(slope);

    /// <summary>Open channel, Eq. 3-4 (Manning): V = 1.49 r^⅔ s^½ / n (ft/s).</summary>
    public static double ChannelVelocity(double n, double r, double slope) => 1.49 * Math.Pow(r, 2.0 / 3.0) * Math.Sqrt(slope) / n;
}

/// <summary>A prismatic routing reach: circular pipe (diameter) or trapezoidal channel (bottom width, side slope z H:V).</summary>
public sealed record ReachGeometry(bool Circular, double Size, double SideSlope, double N, double Slope)
{
    public double Area(double y)
    {
        if (!Circular) return (Size + SideSlope * y) * y;
        double th = 2 * Math.Acos(1 - 2 * Math.Min(y, Size) / Size);
        return Size * Size / 8 * (th - Math.Sin(th));
    }

    public double Top(double y) => Circular ? Size * Math.Sin(Math.Acos(1 - 2 * Math.Min(y, Size) / Size)) : Size + 2 * SideSlope * y;

    public double Perimeter(double y) => Circular ? Size * Math.Acos(1 - 2 * Math.Min(y, Size) / Size) : Size + 2 * y * Math.Sqrt(1 + SideSlope * SideSlope);

    public double Flow(double y) { double a = Area(y), p = Perimeter(y); return a <= 0 ? 0 : 1.486 / N * a * Math.Pow(a / p, 2.0 / 3.0) * Math.Sqrt(Slope); }

    /// <summary>Highest depth the rating is used to (circle: 0.938 D, where Manning flow peaks; channel: unlimited).</summary>
    public double MaxDepth => Circular ? 0.938 * Size : double.PositiveInfinity;

    /// <summary>Full-flow capacity of a pipe (Manning at y = D); +∞ for a channel.</summary>
    public double FullCapacity => Circular ? Flow(Size) : double.PositiveInfinity;

    public double DepthFor(double q)
    {
        if (q <= 0) return 0;
        double lo = 0, hi = Circular ? MaxDepth : Math.Max(1, Size);
        if (!Circular) while (Flow(hi) < q) hi *= 2;
        else if (Flow(hi) <= q) return hi;
        for (int i = 0; i < 60; i++) { double m = 0.5 * (lo + hi); if (Flow(m) < q) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    /// <summary>Kinematic wave celerity c = dQ/dA at flow <paramref name="q"/> (ft/s), from the Manning rating.</summary>
    public double Celerity(double q)
    {
        double y = DepthFor(q), dy = Math.Max(1e-5, 1e-4 * Math.Max(y, 0.01));
        double y1 = Math.Max(1e-6, y - dy), y2 = Circular ? Math.Min(MaxDepth, y + dy) : y + dy;
        if (y2 <= y1) { y1 = y2 - 2 * dy; }
        return (Flow(y2) - Flow(y1)) / (Area(y2) - Area(y1));
    }
}

/// <summary>
/// Variable-parameter Muskingum–Cunge routing (Ponce and Yevjevich 1978; the method IDCM §4.3.8 recommends). The subreach
/// length follows Ponce and Theurer (1982), Δx = ½ (c Δt + Q / (T S c)) at the reference flow (half the peak inflow), so the
/// scheme's numerical diffusion matches the physical diffusion of the diffusion wave. At every step and subreach
/// K = Δx / c and X = ½ (1 − Q / (T S c Δx)) are recomputed from the three-point average flow, c = dQ/dA from Manning;
/// X may be negative (limited to −1 … 0.5).
/// </summary>
public static class MuskingumCunge
{
    public sealed record Result(double[] Outflow, int Subreaches, double ReferenceCelerity, double VolumeErrorPercent, bool OverCapacity);

    public static Result Route(double[] inflow, double dtSec, double lengthFt, ReachGeometry g)
    {
        double peak = inflow.Max(), floor = Math.Max(0.01 * peak, 1e-4);
        bool over = peak > g.FullCapacity * (1 + 1e-9);
        double qCap = g.Circular ? g.Flow(g.MaxDepth) : double.PositiveInfinity;
        double qRef = Math.Min(Math.Max(peak * 0.5, 1e-3), qCap);
        double cRef = g.Celerity(qRef), tRef = g.Top(g.DepthFor(qRef));
        double dxIdeal = 0.5 * (cRef * dtSec + qRef / (tRef * g.Slope * cRef));
        int n = Math.Clamp((int)Math.Ceiling(lengthFt / dxIdeal), 1, 500);
        double dx = lengthFt / n;
        var cur = (double[])inflow.Clone();
        for (int s = 0; s < n; s++)
        {
            var o = new double[cur.Length];
            o[0] = cur[0];
            for (int k = 1; k < cur.Length; k++)
            {
                double qa = Math.Min(Math.Max(floor, (cur[k - 1] + cur[k] + o[k - 1]) / 3), qCap);
                double c = g.Celerity(qa), t = g.Top(g.DepthFor(qa));
                double kk = dx / c, x = Math.Clamp(0.5 * (1 - qa / (t * g.Slope * c * dx)), -1, 0.5);
                double den = 2 * kk * (1 - x) + dtSec;
                double c0 = (dtSec - 2 * kk * x) / den, c1 = (dtSec + 2 * kk * x) / den, c2 = (2 * kk * (1 - x) - dtSec) / den;
                o[k] = Math.Max(0, c0 * cur[k] + c1 * cur[k - 1] + c2 * o[k - 1]);
            }
            cur = o;
        }
        double vin = inflow.Sum(), vout = cur.Sum();
        return new Result(cur, n, cRef, vin > 0 ? (vout - vin) / vin * 100 : 0, over);
    }
}
