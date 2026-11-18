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
    /// Golden-section search for the maximum of a unimodal function on [a, b]. Returns the location and
    /// value of the maximum. Used for the peak of Q(y) in closed conduits.
    /// </summary>
    public static (double X, double Value, int Iterations) GoldenMaximum(Func<double, double> f, double a, double b, double xTolerance, int maxIterations = 300)
    {
        double gr = (Math.Sqrt(5) - 1) / 2;
        double c = b - gr * (b - a), d = a + gr * (b - a);
        double fc = f(c), fd = f(d);
        int i = 0;
        for (; i < maxIterations && Math.Abs(b - a) > xTolerance; i++)
        {
            if (fc > fd) { b = d; d = c; fd = fc; c = b - gr * (b - a); fc = f(c); }
            else { a = c; c = d; fc = fd; d = a + gr * (b - a); fd = f(d); }
        }
        double x = 0.5 * (a + b);
        return (x, f(x), i);
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
        double hi = Math.Min(Math.Max(initialHigh, low + 1e-9), maxHigh);
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

    /// <summary>
    /// Finds every sign change of f on a grid built from <paramref name="breakpoints"/> (each interval split
    /// into <paramref name="subdivisions"/> equal steps) and refines each with Brent's method. Above the last
    /// breakpoint f must be monotonic; a final root there is found by upward expansion. Returns the roots in
    /// ascending order with the direction of each crossing (+1 rising, −1 falling). A non-converged
    /// refinement is returned as-is so the caller can refuse it.
    /// </summary>
    /// <summary>Target number of scan samples across all breakpoint intervals.</summary>
    public const int ScanBudget = 2048;

    public static IReadOnlyList<(RootResult Root, int Direction)> AllRoots(
        Func<double, double> f, IReadOnlyList<double> breakpoints, double maxHigh, int subdivisions = 16, double xTolerance = 1e-12)
    {
        // Sample budget: small sections get the full subdivision; large surveyed sections (thousands of
        // breakpoints, already finely spaced) get at least one sample per interval so cost stays linear.
        int intervals = Math.Max(1, breakpoints.Count - 1);
        int perInterval = Math.Clamp(ScanBudget / intervals, 1, Math.Max(1, subdivisions));
        var grid = new List<double>(intervals * perInterval + 1);
        for (int i = 0; i + 1 < breakpoints.Count; i++)
            for (int j = 0; j < perInterval; j++)
                grid.Add(breakpoints[i] + (breakpoints[i + 1] - breakpoints[i]) * j / perInterval);
        double top = breakpoints[^1];
        grid.Add(top);
        var roots = new List<(RootResult, int)>();
        double xa = grid[0], fa = f(xa);
        for (int i = 1; i < grid.Count; i++)
        {
            double xb = grid[i], fb = f(xb);
            if (fb == 0 && fa != 0)
                roots.Add((new RootResult(true, xb, 0, 0, xa, xb, "Brent"), fa < 0 ? 1 : -1));
            else if (fa != 0 && fb != 0 && Math.Sign(fa) != Math.Sign(fb))
                roots.Add((Brent(f, xa, xb, xTolerance), fa < 0 ? 1 : -1));
            xa = xb; fa = fb;
        }
        if (fa != 0)
        {
            double step = Math.Max(top, 1.0);
            var up = BrentWithUpwardExpansion(f, top, top + step, maxHigh, xTolerance);
            if (up.Converged && up.Root > top) roots.Add((up, fa < 0 ? 1 : -1));
        }
        return roots;
    }
}
