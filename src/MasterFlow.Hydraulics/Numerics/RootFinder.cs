namespace MasterFlow.Hydraulics.Numerics;

/// <summary>Outcome of a root search. A non-converged result must never be reported as an answer.</summary>
public sealed record RootResult(
    bool Converged,
    double Root,
    double Residual,
    int Iterations,
    double BracketLow,
    double BracketHigh,
    string Method,
    string? FailureReason = null);

/// <summary>
/// Bracketed root finding. Brent's method (inverse quadratic interpolation / secant / bisection) is
/// used because it is guaranteed to converge once a sign change is bracketed, unlike Newton-Raphson,
/// which can diverge for poor starting estimates or non-smooth geometry.
/// Reference: R. P. Brent, "Algorithms for Minimization without Derivatives", Prentice-Hall, 1973, ch. 4.
/// </summary>
public static class RootFinder
{
    public const int DefaultMaxIterations = 200;

    /// <summary>
    /// Finds x in [a, b] with f(x) = 0. f(a) and f(b) must have opposite signs (or one is zero).
    /// Converges when the bracket half-width is below <paramref name="xTolerance"/>
    /// (absolute + relative) or |f(x)| ≤ <paramref name="fTolerance"/>.
    /// </summary>
    public static RootResult Brent(
        Func<double, double> f,
        double a,
        double b,
        double xTolerance = 1e-12,
        double fTolerance = 0.0,
        int maxIterations = DefaultMaxIterations)
    {
        double fa = f(a), fb = f(b);
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        if (!double.IsFinite(fa) || !double.IsFinite(fb))
            return new RootResult(false, double.NaN, double.NaN, 0, lo, hi, "Brent", "Function is not finite at the bracket ends.");
        if (fa == 0) return new RootResult(true, a, 0, 0, lo, hi, "Brent");
        if (fb == 0) return new RootResult(true, b, 0, 0, lo, hi, "Brent");
        if (Math.Sign(fa) == Math.Sign(fb))
            return new RootResult(false, double.NaN, double.NaN, 0, lo, hi, "Brent", "The bracket does not contain a sign change.");

        double c = a, fc = fa, d = b - a, e = d;
        for (int iter = 1; iter <= maxIterations; iter++)
        {
            if (Math.Sign(fb) == Math.Sign(fc))
            {
                c = a; fc = fa; d = b - a; e = d;
            }
            if (Math.Abs(fc) < Math.Abs(fb))
            {
                a = b; b = c; c = a;
                fa = fb; fb = fc; fc = fa;
            }

            double tol = 2.0 * double.Epsilon + 2.0 * 1e-15 * Math.Abs(b) + 0.5 * xTolerance * (1.0 + Math.Abs(b));
            double m = 0.5 * (c - b);
            if (Math.Abs(m) <= tol || fb == 0.0 || Math.Abs(fb) <= fTolerance)
                return new RootResult(true, b, fb, iter, lo, hi, "Brent");

            if (Math.Abs(e) >= tol && Math.Abs(fa) > Math.Abs(fb))
            {
                double s = fb / fa, p, q;
                if (a == c)
                {
                    p = 2.0 * m * s;            // secant
                    q = 1.0 - s;
                }
                else
                {
                    double qq = fa / fc, r = fb / fc; // inverse quadratic interpolation
                    p = s * (2.0 * m * qq * (qq - r) - (b - a) * (r - 1.0));
                    q = (qq - 1.0) * (r - 1.0) * (s - 1.0);
                }
                if (p > 0) q = -q; else p = -p;
                if (2.0 * p < Math.Min(3.0 * m * q - Math.Abs(tol * q), Math.Abs(e * q)))
                {
                    e = d; d = p / q;
                }
                else
                {
                    d = m; e = m;              // bisection
                }
            }
            else
            {
                d = m; e = m;
            }

            a = b; fa = fb;
            b += Math.Abs(d) > tol ? d : (m > 0 ? tol : -tol);
            fb = f(b);
            if (!double.IsFinite(fb))
                return new RootResult(false, double.NaN, double.NaN, iter, lo, hi, "Brent", "Function became non-finite during iteration.");
        }
        return new RootResult(false, b, fb, maxIterations, lo, hi, "Brent", $"Did not converge in {maxIterations} iterations.");
    }

    /// <summary>
    /// For a function that is negative at <paramref name="low"/> and increases monotonically,
    /// expands the upper bound geometrically from <paramref name="initialHigh"/> until f changes sign,
    /// then applies Brent's method.
    /// </summary>
    public static RootResult BrentWithUpwardExpansion(
        Func<double, double> f,
        double low,
        double initialHigh,
        double maxHigh,
        double xTolerance = 1e-12,
        double fTolerance = 0.0,
        int maxIterations = DefaultMaxIterations)
    {
        double fl = f(low);
        double hi = Math.Max(initialHigh, low + 1e-9);
        double fh = f(hi);
        int expansions = 0;
        while (double.IsFinite(fh) && Math.Sign(fh) == Math.Sign(fl) && fl != 0 && hi < maxHigh && expansions < 200)
        {
            low = hi; fl = fh;          // the previous upper bound is a valid, tighter lower bound
            hi = Math.Min(hi * 2.0, maxHigh);
            fh = f(hi);
            expansions++;
        }
        if (!double.IsFinite(fh) || (Math.Sign(fh) == Math.Sign(fl) && fl != 0 && fh != 0))
            return new RootResult(false, double.NaN, double.NaN, expansions, low, hi, "Brent",
                $"No sign change found up to {hi:G6}; the target is outside the physically searchable range.");
        var r = Brent(f, low, hi, xTolerance, fTolerance, maxIterations);
        return r with { Iterations = r.Iterations + expansions };
    }
}
