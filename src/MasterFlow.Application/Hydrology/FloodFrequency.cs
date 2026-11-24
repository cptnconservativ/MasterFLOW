namespace MasterFlow.Application.Hydrology;

/// <summary>Special functions for flood frequency: normal, gamma and noncentral-t distributions.</summary>
public static class Stats
{
    /// <summary>ln Γ(x), Lanczos (g = 7, n = 9), about 15 significant digits for x &gt; 0.</summary>
    public static double LogGamma(double x)
    {
        if (x < 0.5) return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);
        ReadOnlySpan<double> c = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
            12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
        x -= 1;
        double a = c[0], t = x + 7.5;
        for (int i = 1; i < 9; i++) a += c[i] / (x + i);
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    /// <summary>Complementary error function from the incomplete gamma function: erfc(x) = Q(½, x²), accurate in the far tail.</summary>
    public static double Erfc(double x) => x >= 0 ? GammaQ(0.5, x * x) : 2 - GammaQ(0.5, x * x);

    public static double NormPdf(double z) => Math.Exp(-0.5 * z * z) / Math.Sqrt(2 * Math.PI);
    public static double NormCdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2));

    /// <summary>Standard normal quantile: Acklam's approximation refined by one Halley step (full double precision).</summary>
    public static double NormInv(double p)
    {
        if (p <= 0) return double.NegativeInfinity;
        if (p >= 1) return double.PositiveInfinity;
        double[] a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
        double[] b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
        double[] c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
        double[] d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];
        double x;
        if (p < 0.02425)
        {
            double q = Math.Sqrt(-2 * Math.Log(p));
            x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        else if (p <= 1 - 0.02425)
        {
            double q = p - 0.5, r = q * q;
            x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        else
        {
            double q = Math.Sqrt(-2 * Math.Log(1 - p));
            x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        double e = NormCdf(x) - p, u = e * Math.Sqrt(2 * Math.PI) * Math.Exp(x * x / 2);
        return x - u / (1 + x * u / 2);
    }

    /// <summary>Regularized lower incomplete gamma P(a, x) (series below a + 1, continued fraction above).</summary>
    public static double GammaP(double a, double x) => x <= 0 ? 0 : double.IsPositiveInfinity(x) ? 1 : x < a + 1 ? GammaSeries(a, x) : 1 - GammaCf(a, x);
    /// <summary>Regularized upper incomplete gamma Q(a, x) = 1 − P(a, x), without cancellation in the upper tail.</summary>
    public static double GammaQ(double a, double x) => x <= 0 ? 1 : double.IsPositiveInfinity(x) ? 0 : x < a + 1 ? 1 - GammaSeries(a, x) : GammaCf(a, x);

    private static double GammaSeries(double a, double x)
    {
        double ap = a, sum = 1 / a, del = sum;
        for (int n = 0; n < 1_000_000; n++)
        {
            ap += 1; del *= x / ap; sum += del;
            if (Math.Abs(del) < Math.Abs(sum) * 1e-16) break;
        }
        return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
    }

    private static double GammaCf(double a, double x)
    {
        const double tiny = 1e-300;
        double b = x + 1 - a, c = 1 / tiny, d = 1 / b, h = d;
        for (int i = 1; i < 1_000_000; i++)
        {
            double an = -i * (i - a);
            b += 2;
            d = an * d + b; if (Math.Abs(d) < tiny) d = tiny;
            c = b + an / c; if (Math.Abs(c) < tiny) c = tiny;
            d = 1 / d;
            double del = d * c; h *= del;
            if (Math.Abs(del - 1) < 1e-16) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    /// <summary>The x with P(a, x) = p (Wilson–Hilferty start, then safeguarded Newton).</summary>
    public static double GammaPInv(double a, double p)
    {
        if (p <= 0) return 0;
        if (p >= 1) return double.PositiveInfinity;
        double z = NormInv(p), x = a * Math.Pow(Math.Max(1 - 1 / (9 * a) + z / (3 * Math.Sqrt(a)), 0.01), 3);
        double lo = 0, hi = double.PositiveInfinity, lg = LogGamma(a);
        for (int i = 0; i < 300; i++)
        {
            double f = p < 0.5 ? GammaP(a, x) - p : (1 - p) - GammaQ(a, x);
            if (f < 0) lo = x; else hi = x;
            if (Math.Abs(f) < 1e-15 * Math.Max(p, 1e-300) || (hi - lo) < 1e-15 * x) break;
            double dens = Math.Exp((a - 1) * Math.Log(x) - x - lg);
            double nx = x - f / dens;
            if (!(nx > lo && nx < hi) || double.IsNaN(nx)) nx = double.IsPositiveInfinity(hi) ? 2 * x + 1 : 0.5 * (lo + hi);
            if (Math.Abs(nx - x) < 1e-14 * x) { x = nx; break; }
            x = nx;
        }
        return x;
    }

    /// <summary>Noncentral t CDF P(T ≤ t) for ν degrees of freedom and noncentrality δ, by integrating Φ(t·S − δ) over S = √(χ²ν/ν).</summary>
    public static double NoncentralTCdf(double t, double nu, double delta)
    {
        // u = ln S; density of u: ln f = ln 2 + (ν/2)·ln(ν/2) − lnΓ(ν/2) + ν·u − ν·e^(2u)/2.
        double c0 = Math.Log(2) + nu / 2 * Math.Log(nu / 2) - LogGamma(nu / 2);
        double uLo = -(36 / nu + 6 / Math.Sqrt(nu)), uHi = 0.5 * Math.Log(1 + 80 / nu) + 0.25;
        const int n = 800;
        double h = (uHi - uLo) / n, sum = 0;
        for (int i = 0; i <= n; i++)
        {
            double u = uLo + i * h, s = Math.Exp(u);
            double w = Math.Exp(c0 + nu * u - nu * s * s / 2) * NormCdf(t * s - delta);
            sum += w * (i == 0 || i == n ? 1 : i % 2 == 1 ? 4 : 2);
        }
        return Math.Clamp(sum * h / 3, 0, 1);
    }
}

/// <summary>Pearson Type III distribution of x (here log10 Q) by its mean, standard deviation and skew.</summary>
public readonly record struct Pearson3(double Mean, double Sd, double Skew)
{
    /// <summary>Below this |skew| the distribution is treated as normal (the gamma shape 4/G² exceeds 40,000).</summary>
    public const double NormalSkew = 0.01;
    private bool IsNormal => Math.Abs(Skew) < NormalSkew;
    private double Beta => 4 / (Skew * Skew);

    /// <summary>Frequency factor K for non-exceedance probability p: x = mean + K·sd.</summary>
    public static double K(double skew, double p)
    {
        double z = Stats.NormInv(p);
        if (Math.Abs(skew) < NormalSkew)
            return Math.Abs(skew) < 1e-9 ? z : 2 / skew * (Math.Pow(1 + skew * z / 6 - skew * skew / 36, 3) - 1);
        double b = 4 / (skew * skew), y = Stats.GammaPInv(b, skew > 0 ? p : 1 - p);
        return skew / 2 * (y - b);
    }

    public double Quantile(double p) => Mean + K(Skew, p) * Sd;

    /// <summary>Non-exceedance probability of x.</summary>
    public double Cdf(double x)
    {
        double z = (x - Mean) / Sd;
        if (IsNormal) return Stats.NormCdf(z);
        double y = Beta + 2 * z / Skew;     // gamma variate
        return Skew > 0 ? Stats.GammaP(Beta, y) : Stats.GammaQ(Beta, y);
    }

    /// <summary>
    /// Conditional moments of z = (x − mean)/sd given lo &lt; x &lt; hi: (P(lo &lt; x &lt; hi), E[z], E[z²], E[z³]).
    /// Integration by parts keeps them free of cancellation for any shape.
    /// </summary>
    public (double Prob, double M1, double M2, double M3) TruncatedMoments(double lo, double hi)
    {
        double a = (lo - Mean) / Sd, b = (hi - Mean) / Sd;
        if (IsNormal)
        {
            double pa = double.IsNegativeInfinity(a) ? 0 : Stats.NormPdf(a), pb = double.IsPositiveInfinity(b) ? 0 : Stats.NormPdf(b);
            double d = a > 0 ? Stats.NormCdf(-a) - Stats.NormCdf(-b) : Stats.NormCdf(b) - Stats.NormCdf(a);
            double aa = double.IsInfinity(a) ? 0 : a * pa, bb = double.IsInfinity(b) ? 0 : b * pb;
            double a2 = double.IsInfinity(a) ? 0 : (2 + a * a) * pa, b2 = double.IsInfinity(b) ? 0 : (2 + b * b) * pb;
            if (d <= 0) return (0, Clamp(a, b), Clamp(a, b) * Clamp(a, b), Math.Pow(Clamp(a, b), 3));
            return (d, (pa - pb) / d, (d + aa - bb) / d, (a2 - b2) / d);
        }
        // z = (g/2)·(Y − β) = α'·U with U = Y − β, Y ~ Gamma(β, 1). Map the z bounds to Y bounds.
        double beta = Beta, al = Skew / 2;
        double ya = beta + a / al, yb = beta + b / al;
        if (al < 0) (ya, yb) = (yb, ya);
        ya = double.IsNaN(ya) ? 0 : Math.Max(0, ya); yb = double.IsNaN(yb) ? double.PositiveInfinity : Math.Max(0, yb);
        double D = ya > beta ? Stats.GammaQ(beta, ya) - Stats.GammaQ(beta, yb) : Stats.GammaP(beta, yb) - Stats.GammaP(beta, ya);
        double lg = Stats.LogGamma(beta);
        double YF(double y) => y <= 0 || double.IsPositiveInfinity(y) ? 0 : Math.Exp(beta * Math.Log(y) - y - lg);   // y·f(y)
        double Ub(double y) => double.IsPositiveInfinity(y) ? 0 : y - beta;
        double fa = YF(ya), fb = YF(yb), ua = Ub(ya), ub = Ub(yb);
        // ∫(y−β)h f = −[y f h] + ∫ y h' f
        double e1 = -(fb - fa);
        double e2 = -(fb * ub - fa * ua) + beta * D + e1;
        double e3 = -(fb * ub * ub - fa * ua * ua) + 2 * beta * e1 + 2 * e2;
        if (!(D > 1e-300))
        {
            double y0 = Math.Abs(ya - beta) < Math.Abs(yb - beta) || double.IsPositiveInfinity(yb) ? ya : yb;
            double z0 = al * (y0 - beta);
            return (0, z0, z0 * z0, z0 * z0 * z0);
        }
        return (D, al * e1 / D, al * al * e2 / D, al * al * al * e3 / D);
    }

    private static double Clamp(double a, double b) => double.IsNegativeInfinity(a) ? b : double.IsPositiveInfinity(b) ? a : 0.5 * (a + b);
}

/// <summary>One annual peak in log10 space: exact when Lo = Hi, otherwise an interval (Lo may be −∞, Hi +∞).</summary>
public readonly record struct LogInterval(double Lo, double Hi)
{
    public bool Exact => Lo == Hi;
}

/// <summary>Expected Moments Algorithm (Cohn, Lane and Baier 1997; Bulletin 17C Appendix 7) for the log-Pearson III distribution.</summary>
public static class Ema
{
    /// <summary>
    /// Fits mean, standard deviation and skew of log10 Q to exact and interval observations. With
    /// <paramref name="fixedSkew"/> only the mean and standard deviation are estimated.
    /// </summary>
    public static (double Mean, double Sd, double Skew, int Iterations) Fit(IReadOnlyList<LogInterval> obs, double? fixedSkew = null)
    {
        int n = obs.Count;
        var exact = obs.Where(o => o.Exact).Select(o => o.Lo).ToList();
        if (exact.Count < 3) throw new ArgumentException("EMA needs at least three exact observations.");
        var (m, s, g) = SampleMoments(exact);
        if (fixedSkew is { } fs) g = fs;
        double c2 = n / (n - 1.0), c3 = n * (double)n / ((n - 1.0) * (n - 2.0));
        int it;
        for (it = 1; it <= 1000; it++)
        {
            var d = new Pearson3(m, s, g);
            var mom = obs.Select(o => o.Exact ? (1.0, (o.Lo - m) / s, Math.Pow((o.Lo - m) / s, 2), Math.Pow((o.Lo - m) / s, 3)) : d.TruncatedMoments(o.Lo, o.Hi)).ToList();
            // E[x] = m + s·E[z]
            double mNew = m + s * mom.Average(t => t.Item2);
            double dz = (mNew - m) / s;      // shift to the new mean, in z units
            double e2 = mom.Average(t => t.Item3 - 2 * dz * t.Item2 + dz * dz);
            double e3 = mom.Average(t => t.Item4 - 3 * dz * t.Item3 + 3 * dz * dz * t.Item2 - dz * dz * dz);
            double sNew = s * Math.Sqrt(c2 * e2);
            double gNew = fixedSkew ?? Math.Clamp(c3 * e3 / Math.Pow(c2 * e2, 1.5), -5, 5);
            bool done = Math.Abs(mNew - m) < 1e-10 && Math.Abs(sNew - s) < 1e-10 && Math.Abs(gNew - g) < 1e-9;
            (m, s, g) = (mNew, sNew, gNew);
            if (done) break;
        }
        return (m, s, g, it);
    }

    /// <summary>Bulletin 17B sample moments: mean, standard deviation (n − 1) and skew n·Σ(x−x̄)³/((n−1)(n−2)s³).</summary>
    public static (double Mean, double Sd, double Skew) SampleMoments(IReadOnlyList<double> x)
    {
        int n = x.Count;
        double m = x.Average(), s2 = x.Sum(v => (v - m) * (v - m)) / (n - 1), s = Math.Sqrt(s2);
        double g = n * x.Sum(v => Math.Pow(v - m, 3)) / ((n - 1.0) * (n - 2.0) * s * s * s);
        return (m, s, g);
    }

    /// <summary>Mean square error of a station skew (Bulletin 17B Eq. 6, Wallis–Matalas–Slack).</summary>
    public static double SkewMse(double g, int n)
    {
        double ag = Math.Abs(g);
        double a = ag <= 0.9 ? -0.33 + 0.08 * ag : -0.52 + 0.30 * ag;
        double b = ag <= 1.5 ? 0.94 - 0.26 * ag : 0.55;
        return Math.Pow(10, a - b * Math.Log10(n / 10.0));
    }

    /// <summary>Skew weighted by inverse mean square errors (Bulletin 17B/17C: each skew weighted by the other's mean square error).</summary>
    public static double WeightedSkew(double gs, double mseS, double gr, double mseR) => (mseR * gs + mseS * gr) / (mseR + mseS);
}

/// <summary>Multiple Grubbs–Beck low-outlier test (Cohn et al. 2013), as adopted in Bulletin 17C.</summary>
public static class Mgbt
{
    /// <summary>
    /// p-value P(ω_r ≤ η) for the r-th smallest of n normal observations, where ω_r = (x(r) − mean of the larger n − r)/sd of the larger n − r.
    /// The larger values are truncated normals above x(r); their mean is taken normal, their variance gamma, and the
    /// two are decorrelated, which gives a noncentral t conditional on x(r). That is integrated over the order-statistic density.
    /// </summary>
    public static double PValue(int n, int r, double eta)
    {
        int m = n - r;
        double lc = Stats.LogGamma(n + 1) - Stats.LogGamma(r) - Stats.LogGamma(n - r + 1);
        const double z0 = -10, z1 = 6;
        const int nz = 600;
        double h = (z1 - z0) / nz, sum = 0, wmax = 0;
        var ws = new double[nz + 1];
        for (int i = 0; i <= nz; i++)
        {
            double z = z0 + i * h, F = Stats.NormCdf(z), S = Stats.NormCdf(-z);
            ws[i] = F <= 0 || S <= 0 ? 0 : Math.Exp(lc + (r - 1) * Math.Log(F) + (n - r) * Math.Log(S) - 0.5 * z * z) / Math.Sqrt(2 * Math.PI);
            wmax = Math.Max(wmax, ws[i]);
        }
        for (int i = 0; i <= nz; i++)
        {
            if (ws[i] < 1e-12 * wmax) continue;
            double z = z0 + i * h;
            sum += ws[i] * Conditional(m, z, eta) * (i == 0 || i == nz ? 1 : i % 2 == 1 ? 4 : 2);
        }
        return Math.Clamp(sum * h / 3, 0, 1);
    }

    private static double Conditional(int m, double z, double eta)
    {
        // Raw moments of a standard normal truncated below at z: μk = (k−1)·μ(k−2) + z^(k−1)·λ, λ = φ(z)/(1 − Φ(z)).
        double lam = Stats.NormPdf(z) / Stats.NormCdf(-z);
        double r1 = lam, r2 = 1 + z * lam, r3 = 2 * r1 + z * z * lam, r4 = 3 * r2 + z * z * z * lam;
        double c2 = r2 - r1 * r1, c3 = r3 - 3 * r1 * r2 + 2 * r1 * r1 * r1, c4 = r4 - 4 * r1 * r3 + 6 * r1 * r1 * r2 - 3 * Math.Pow(r1, 4);
        double varM = c2 / m, eS2 = c2, varS2 = c4 / m - c2 * c2 * (m - 3.0) / (m * (m - 1.0)), covMS2 = c3 / m;
        varS2 = Math.Max(varS2, 1e-12 * c2 * c2);
        double alpha = eS2 * eS2 / varS2, nu = 2 * alpha;
        double eS = Math.Sqrt(eS2 / alpha) * Math.Exp(Stats.LogGamma(alpha + 0.5) - Stats.LogGamma(alpha));
        double varS = Math.Max(eS2 - eS * eS, 1e-15);
        double covMS = covMS2 / (2 * Math.Sqrt(eS2));
        double lambda = covMS / varS;
        double muMp = r1 - lambda * eS, sdMp = Math.Sqrt(Math.Max(varM - covMS * covMS / varS, 1e-15));
        // P((z − M')/S < η + λ), M' ~ N(muMp, sdMp²), S = √E[S²]·√(χ²ν/ν)
        double delta = (z - muMp) / sdMp;
        return Stats.NoncentralTCdf((eta + lambda) * Math.Sqrt(eS2) / sdMp, nu, delta);
    }

    /// <summary>
    /// Runs the test on log10 flows: returns the statistics and p-values for the lowest n/2 values and the number of
    /// potentially influential low floods (PILFs). Step-forward at α = 0.005 from the lowest value, then a consecutive
    /// run at α = 0.10 (Bulletin 17C).
    /// </summary>
    public static (int Klow, double[] W, double[] P) Test(IReadOnlyList<double> logs, double alpha1 = 0.005, double alpha10 = 0.10)
    {
        var z = logs.OrderBy(v => v).ToArray();
        int n = z.Length, n2 = n / 2;
        var w = new double[n2];
        var p = new double[n2];
        int j1 = 0, j2 = 0;
        for (int i = 1; i <= n2; i++)
        {
            var rest = z.Skip(i).ToArray();
            double mean = rest.Average(), sd = Math.Sqrt(rest.Sum(v => (v - mean) * (v - mean)) / (rest.Length - 1));
            w[i - 1] = (z[i - 1] - mean) / sd;
            p[i - 1] = PValue(n, i, w[i - 1]);
            if (p[i - 1] < alpha1) { j1 = i; j2 = i; }
            if (p[i - 1] < alpha10 && j2 == i - 1) j2 = i;
        }
        return (Math.Max(j1, j2), w, p);
    }
}
