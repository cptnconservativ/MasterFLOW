namespace MasterFlow.Application.Grading;

/// <summary>One linear constraint a·z ≤ b (or = b) on the finished grades, with how much it may cost to break it.</summary>
public sealed record GradeConstraint(string Group, int[] Nodes, double[] Coef, double Bound, bool Equality, double Penalty, string Where);

/// <summary>
/// Finished-grade solver (D-260). Minimizes Σ wᵢ (zᵢ − eᵢ)² (stay close to existing ground, weighted by the area each
/// grid point stands for) subject to linear constraints on the grades: slope limits between neighbours, pads, tie-ins,
/// drainage away from buildings, cut/fill limits and the earthwork balance. Solved by Hildreth's dual coordinate ascent
/// (Hildreth 1957; Lent and Censor 1980), a row-action method that needs no matrix and handles thousands of two-point
/// constraints. Each constraint's multiplier is capped at its penalty, which makes the problem an exact-L1-penalty one:
/// when the constraints cannot all be met, the solver still converges and the constraints left violated, cheapest first,
/// are exactly the places where the requirements conflict.
/// </summary>
public static class GradingSolver
{
    public sealed record Result(double[] Z, double[] Violation, int Sweeps, bool Converged);

    public static Result Solve(double[] existing, double[] weight, IReadOnlyList<GradeConstraint> cons, int maxSweeps = 20000, double tol = 1e-5)
    {
        int n = existing.Length, m = cons.Count;
        var z = (double[])existing.Clone();
        var lam = new double[m];
        var norm = new double[m];
        for (int k = 0; k < m; k++)
        {
            double s = 0;
            for (int j = 0; j < cons[k].Nodes.Length; j++) s += cons[k].Coef[j] * cons[k].Coef[j] / weight[cons[k].Nodes[j]];
            norm[k] = s;
        }
        int sweep = 0; bool done = false;
        for (; sweep < maxSweeps && !done; sweep++)
        {
            double biggest = 0;
            for (int k = 0; k < m; k++)
            {
                var c = cons[k];
                if (norm[k] <= 0 || c.Penalty <= 0) continue;
                double az = 0;
                for (int j = 0; j < c.Nodes.Length; j++) az += c.Coef[j] * z[c.Nodes[j]];
                double next = lam[k] + (az - c.Bound) / norm[k];
                next = c.Equality ? Math.Clamp(next, -c.Penalty, c.Penalty) : Math.Clamp(next, 0, c.Penalty);
                double d = next - lam[k];
                if (d == 0) continue;
                lam[k] = next;
                for (int j = 0; j < c.Nodes.Length; j++)
                {
                    double step = d * c.Coef[j] / weight[c.Nodes[j]];
                    z[c.Nodes[j]] -= step;
                    biggest = Math.Max(biggest, Math.Abs(step));
                }
            }
            if (biggest < tol) done = true;
        }
        var viol = new double[m];
        for (int k = 0; k < m; k++)
        {
            var c = cons[k];
            double az = 0;
            for (int j = 0; j < c.Nodes.Length; j++) az += c.Coef[j] * z[c.Nodes[j]];
            viol[k] = c.Equality ? Math.Abs(az - c.Bound) : Math.Max(0, az - c.Bound);
        }
        return new Result(z, viol, sweep, done);
    }
}
