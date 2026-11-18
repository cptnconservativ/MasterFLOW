namespace MasterFlow.Application.Tools;

/// <summary>
/// Shared gutter-flow core (U.S. units, ft and cfs): a curb-and-gutter section with a uniform cross slope Sx, or a
/// composite section whose gutter of width W is on a steeper slope Sw (HEC-22, 3rd ed., §4.3.2).
///
/// Uniform:   Q = (Ku/n) Sx^1.67 S^0.5 T^2.67, Ku = 0.56 (IDCM 2012 Eq. 4-4-1, exponents as printed, D-054).
/// Composite: Q = Qs / (1 − Eo), Qs = (Ku/n) Sx^1.67 S^0.5 (T − W)^2.67 (HEC-22 Eq. 4-4) with
///            Eo = 1 / {1 + (Sw/Sx) / [(1 + (Sw/Sx)/(T/W − 1))^2.67 − 1]} (HEC-22 Eq. 4-4);
///            spread inside the gutter (T ≤ W): a triangle on Sw, Q = (Ku/n) Sw^1.67 S^0.5 T^2.67.
/// Depth at the curb d = T·Sx + a with gutter depression a = W (Sw − Sx); d = T·Sw while T ≤ W.
/// Flow area A = Sx T²/2 + a W/2 (T ≥ W) and V = Q/A, which for a uniform section is IDCM Eq. 4-4-2 exactly.
/// The section is one gutter, from the curb to the edge of the spread; it does not know where the crown is.
/// </summary>
public sealed record GutterSection(double Sx, double N, double W = 0, double Sw = 0)
{
    public const double Ku = 0.56;
    private const double Ex = 1.67, Et = 2.67;

    /// <summary>True when the gutter is on a steeper slope than the pavement.</summary>
    public bool IsComposite => W > 0 && Sw > Sx;

    /// <summary>Gutter depression a = W (Sw − Sx), ft; 0 for a uniform section.</summary>
    public double Depression => IsComposite ? W * (Sw - Sx) : 0;

    /// <summary>Problems with the section itself (empty when valid).</summary>
    public IEnumerable<string> Problems()
    {
        if (!(Sx > 0)) yield return "Cross slope Sx must be greater than zero.";
        if (!(N > 0)) yield return "Manning's n must be greater than zero.";
        if (W < 0 || double.IsNaN(W)) yield return "Gutter width W cannot be negative.";
        if (W > 0 && Sw > 0 && Sw < Sx) yield return $"Gutter slope Sw = {Sw} is flatter than the pavement cross slope Sx = {Sx}; a composite gutter needs Sw > Sx (enter W = 0 for a uniform section).";
        if (Sw < 0) yield return "Gutter slope Sw cannot be negative.";
    }

    /// <summary>Gutter flow (cfs) at spread T (ft) on longitudinal slope S.</summary>
    public double Capacity(double t, double s)
    {
        if (!(t > 0)) return 0;
        double k = Ku / N * Math.Sqrt(s);
        if (!IsComposite) return k * Math.Pow(Sx, Ex) * Math.Pow(t, Et);
        if (t <= W) return k * Math.Pow(Sw, Ex) * Math.Pow(t, Et);
        double qs = k * Math.Pow(Sx, Ex) * Math.Pow(t - W, Et);
        // Q = Qs / (1 − Eo) with 1 − Eo = r / (u^2.67 − 1 + r), u = 1 + r/(T/W − 1): written this way it stays exact as T → W.
        double r = Sw / Sx, u = 1 + r / (t / W - 1);
        return qs * (Math.Pow(u, Et) - 1 + r) / r;
    }

    /// <summary>Spread T (ft) that carries Q (cfs) on slope S; Capacity is strictly increasing in T, so bisection is exact to 1e-10 ft.</summary>
    public double Spread(double q, double s)
    {
        if (!(q > 0)) return 0;
        if (!IsComposite) return Math.Pow(q * N / (Ku * Math.Pow(Sx, Ex) * Math.Sqrt(s)), 1 / Et);
        if (q <= Capacity(W, s)) return Math.Pow(q * N / (Ku * Math.Pow(Sw, Ex) * Math.Sqrt(s)), 1 / Et);
        double lo = W, hi = 2 * W + 1;
        while (Capacity(hi, s) < q) { lo = hi; hi *= 2; if (hi > 1e7) return double.PositiveInfinity; }
        for (int i = 0; i < 200 && hi - lo > 1e-10; i++) { double m = 0.5 * (lo + hi); if (Capacity(m, s) < q) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// Eo: share of the gutter flow passing over the gutter width W (the frontal flow a grate of width W sees).
    /// Composite: HEC-22 Eq. 4-4. Uniform: 1 − (1 − W/T)^2.67 (HEC-22 Eq. 4-16). 1 when the spread is inside W.
    /// </summary>
    public double FrontalRatio(double t) => FrontalRatio(t, W);

    /// <summary>Eo for a frontal width <paramref name="w"/> (e.g. a grate narrower or wider than the gutter). For a composite section only w = W uses Eq. 4-4.</summary>
    public double FrontalRatio(double t, double w)
    {
        if (!(w > 0)) return 0;
        if (t <= w) return 1;
        if (IsComposite && Math.Abs(w - W) < 1e-12)
        {
            double r = Sw / Sx;
            return 1 / (1 + r / (Math.Pow(1 + r / (t / W - 1), Et) - 1));
        }
        return 1 - Math.Pow(1 - w / t, Et);
    }

    /// <summary>Depth of flow at the curb (ft) for spread T.</summary>
    public double Depth(double t) => !(t > 0) ? 0 : IsComposite && t <= W ? t * Sw : t * Sx + Depression;

    /// <summary>Spread (ft) at which the depth at the curb reaches d.</summary>
    public double SpreadAtDepth(double d)
    {
        if (!(d > 0)) return 0;
        if (IsComposite && d <= W * Sw) return d / Sw;
        return (d - Depression) / Sx;
    }

    /// <summary>Flow area (ft²) at spread T.</summary>
    public double Area(double t)
    {
        if (!(t > 0)) return 0;
        if (IsComposite && t <= W) return 0.5 * Sw * t * t;
        return 0.5 * Sx * t * t + 0.5 * Depression * W;
    }

    /// <summary>Mean velocity V = Q/A (fps) for Q on slope S.</summary>
    public double Velocity(double q, double s)
    {
        double t = Spread(q, s);
        return t > 0 ? q / Area(t) : 0;
    }

    /// <summary>
    /// IDCM Table 4-8 (HEC-22 Table 4-1): Ta/T2, the spread at average velocity as a share of the downstream spread,
    /// from T1/T2 (upstream/downstream spread), linearly interpolated; 0.65 at T1 = 0. Outside 0–0.8 it is NaN (not tabulated).
    /// </summary>
    public static double AverageVelocitySpreadRatio(double t1OverT2)
    {
        double[] x = [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8], y = [0.65, 0.66, 0.68, 0.70, 0.74, 0.77, 0.82, 0.86, 0.90];
        if (!(t1OverT2 >= 0) || t1OverT2 > 0.8 + 1e-12) return double.NaN;
        int i = Math.Min((int)(t1OverT2 / 0.1), 7);
        return y[i] + (y[i + 1] - y[i]) * (t1OverT2 - x[i]) / 0.1;
    }
}
