using MasterFlow.Domain;

namespace MasterFlow.Hydraulics;

/// <summary>One pipe size change made by <see cref="NetworkSizer"/>.</summary>
public sealed record PipeSizeChange(string Pipe, double OldRise, double NewRise, string Reason);

public sealed record NetworkSizingResult(StormNetwork Network, IReadOnlyList<PipeSizeChange> Changes, IReadOnlyList<string> Notes, StormNetworkResult Solution);

/// <summary>
/// Sizes the circular pipes of a storm network to its criteria. Pass 1, upstream to downstream: the smallest
/// standard size not below the minimum size or any pipe upstream that carries the design Q flowing part full
/// (Q ≤ Qfull, when pressure flow is not allowed) and keeps the normal-depth velocity within the maximum.
/// Pass 2: while a structure's HGL is closer to the rim than required, or a pipe flows full where that is
/// not allowed, a pipe is
/// increased one size: of the pipes on the path from that structure to the outfall, the one whose increase lowers
/// the structure's water level most (every pipe downstream kept at least as large). Inverts, lengths, n and
/// elliptical pipes are not changed, and no size is used whose crown would be above a rim at either end. Every change and every limit reached is reported; nothing is hidden.
/// </summary>
public static class NetworkSizer
{
    /// <summary>Common circular RCP sizes: inches (U.S.) or millimetres (SI).</summary>
    public static IReadOnlyList<double> StandardSizes(UnitSystem u) => u == UnitSystem.SI
        ? [300, 375, 450, 525, 600, 675, 750, 825, 900, 1050, 1200, 1350, 1500, 1650, 1800, 1950, 2100, 2250, 2400, 2700, 3000]
        : [12, 15, 18, 21, 24, 27, 30, 33, 36, 42, 48, 54, 60, 66, 72, 78, 84, 90, 96, 102, 108];

    /// <param name="hglPass">false stops after pass 1 (capacity and velocity only).</param>
    public static NetworkSizingResult Size(StormNetwork net, IReadOnlyList<double>? sizes = null, bool hglPass = true)
    {
        var c = UnitConstants.For(net.Units);
        double toBase = net.Units == UnitSystem.SI ? 0.001 : 1.0 / 12.0;
        var list = (sizes ?? StandardSizes(net.Units)).Select(s => s * toBase).OrderBy(s => s).ToList();
        var notes = new List<string>();
        var changes = new List<PipeSizeChange>();

        var first = StormNetworkSolver.Solve(net);
        if (!first.Succeeded) return new(net, [], ["The network does not solve as entered; fix the errors first."], first);
        // Accumulated design and minor flows from the solver.
        var q = first.Network.Pipes.ToDictionary(p => p.Name, p => p.Discharge);
        var pipes = net.Pipes.ToDictionary(p => p.Name, p => p);
        var order = UpstreamFirst(net.Pipes);
        var k = net.Criteria;
        double minRise = k.MinPipeRise ?? 0;

        void Set(string name, double rise, string reason)
        {
            var p = pipes[name];
            if (Math.Abs(p.Rise - rise) < 1e-9) return;
            int i = changes.FindIndex(x => x.Pipe == name);
            double old = i >= 0 ? changes[i].OldRise : p.Rise;
            if (i >= 0) changes.RemoveAt(i);
            if (Math.Abs(old - rise) > 1e-9) changes.Add(new PipeSizeChange(name, old, rise, reason));
            pipes[name] = p with { Rise = rise, Span = rise };
        }
        var rims = net.Structures.ToDictionary(s => s.Name, s => s.RimElevation);
        // A size fits when its crown stays below the rim at both ends (inverts are kept).
        bool Fits(NetworkPipe p, double rise) =>
            !(rims.GetValueOrDefault(p.From) is { } ru && p.UpstreamInvert + rise > ru + 1e-9) &&
            !(rims.GetValueOrDefault(p.To) is { } rd && p.DownstreamInvert + rise > rd + 1e-9);
        double UpstreamMax(NetworkPipe p) => pipes.Values.Where(u => u.To == p.From).Select(u => u.Rise).DefaultIfEmpty(0).Max();

        // ---- pass 1: capacity and velocity, upstream first
        foreach (var name in order)
        {
            var p = pipes[name];
            if (p.Shape != NetworkPipeShape.Circular) { notes.Add($"Pipe {name} is elliptical; not resized."); continue; }
            double s = (p.UpstreamInvert - p.DownstreamInvert) / p.Length;
            double floor = Math.Max(minRise - 1e-9, UpstreamMax(p) - 1e-9);
            var candidates = list.Where(d => d >= floor && Fits(p, d)).ToList();
            if (candidates.Count == 0) { notes.Add($"Pipe {name}: no standard size is at least the minimum/upstream size and fits below the rims (inverts kept); left at {Fmt(p.Rise, toBase)}."); continue; }
            if (!(s > 0))
            {
                // Keep the existing size only when it is itself feasible (fits below the rims and meets the minimum/upstream
                // size); otherwise pick from the feasible candidates: the smallest not below the existing size, or the largest that fits.
                bool keepOk = p.Rise >= floor && Fits(p, p.Rise);
                double pick = keepOk ? Math.Max(p.Rise, candidates[0]) : candidates.FirstOrDefault(d => d >= p.Rise - 1e-9, candidates[^1]);
                Set(name, pick, "minimum or upstream size (flat/adverse pipe: capacity not checked)");
                notes.Add($"Pipe {name} is flat or adverse; only the minimum/upstream size rule was applied.");
                if (pick < p.Rise - 1e-9)
                    notes.Add($"Pipe {name}: the existing {Fmt(p.Rise, toBase)} crown is above a rim; reduced to {Fmt(pick, toBase)}, the largest standard size that fits. Capacity is not checked on a flat/adverse pipe: confirm it.");
                continue;
            }
            double chosen = double.NaN; string why = "";
            foreach (var d in candidates)
            {
                double qf = QFull(d, p.ManningN, s, c);
                bool capOk = k.AllowSurcharge || qf >= q[name];
                double v = NormalVelocity(d, p.ManningN, s, q[name], c);
                bool velOk = k.MaxVelocity is not { } mx || !(v > mx);
                if (capOk && velOk) { chosen = d; why = !k.AllowSurcharge ? $"carries Q = {q[name]:G4} part full (Qfull {qf:G4})" : "minimum or upstream size"; break; }
            }
            if (double.IsNaN(chosen))
            {
                chosen = candidates[^1];
                why = "largest standard size";
                notes.Add($"Pipe {name}: no standard size that fits below the rims carries Q = {q[name]:G4} part full within the velocity limit at slope {100 * s:F3} %; set to the largest that fits. Lower the pipe, change the slope or split the flow.");
            }
            Set(name, chosen, why);
        }

        // ---- pass 2: HGL clearance and surcharge, by upsizing the pipe with the largest loss downstream
        StormNetworkResult r = StormNetworkSolver.Solve(net with { Pipes = net.Pipes.Select(p => pipes[p.Name]).ToList() });
        for (int it = 0; it < 400 && r.Succeeded && hglPass; it++)
        {
            var failing = r.Structures.Where(x => !x.IsOutfall && k.HglBelowRim is { } need && x.HglClearance is { } cl && cl < need - 1e-9)
                .Select(x => x.Structure.Name).ToList();
            List<string> fullPipes = k.AllowSurcharge ? [] : r.Pipes.Where(p => p.FlowsFull).Select(p => p.Pipe.From).ToList();
            var targets = failing.Concat(fullPipes).Distinct().ToList();
            if (targets.Count == 0) break;
            bool changed = false;
            foreach (var t in targets)
            {
                // Try one size up on each pipe of the path to the outfall; keep the change that lowers the
                // water level at the target structure the most (ties: the smaller pipe).
                var path = r.PathToOutfall(t);
                double baseLevel = r.Structure(t)!.Hgl;
                (string Name, double Drop, Dictionary<string, NetworkPipe> Trial)? best = null;
                foreach (var cand in path.Select(p => p.Pipe.Name).Where(n => pipes[n].Shape == NetworkPipeShape.Circular && list.Any(d => d > pipes[n].Rise + 1e-9 && Fits(pipes[n], d))))
                {
                    var trial = new Dictionary<string, NetworkPipe>(pipes);
                    double next = list.First(d => d > trial[cand].Rise + 1e-9);
                    trial[cand] = trial[cand] with { Rise = next, Span = next };
                    for (var at = trial[cand]; ;)
                    {
                        var down = trial.Values.FirstOrDefault(p => p.From == at.To);
                        if (down is null) break;
                        if (down.Shape == NetworkPipeShape.Circular && down.Rise < at.Rise - 1e-9) trial[down.Name] = down with { Rise = at.Rise, Span = at.Rise };
                        at = trial[down.Name];
                    }
                    if (trial.Any(kv => Math.Abs(kv.Value.Rise - pipes[kv.Key].Rise) > 1e-9 && !Fits(kv.Value, kv.Value.Rise))) continue;
                    var tr = StormNetworkSolver.Solve(net with { Pipes = net.Pipes.Select(p => trial[p.Name]).ToList() });
                    if (!tr.Succeeded || tr.Structure(t) is not { } ts) continue;
                    double drop = baseLevel - ts.Hgl;
                    if (best is null || drop > best.Value.Drop + 1e-9 || (Math.Abs(drop - best.Value.Drop) <= 1e-9 && trial[cand].Rise < best.Value.Trial[best.Value.Name].Rise))
                        best = (cand, drop, trial);
                }
                if (best is not { } b || !(b.Drop > 1e-6)) continue;
                string reason = failing.Contains(t) ? $"HGL at {t} too close to the rim" : $"pipe from {t} flowed full";
                foreach (var (name, p) in b.Trial)
                    if (Math.Abs(p.Rise - pipes[name].Rise) > 1e-9) Set(name, p.Rise, name == b.Name ? reason : "not smaller than the pipe upstream");
                changed = true;
                break; // re-solve after each change
            }
            if (!changed)
            {
                foreach (var t in targets)
                    notes.Add($"{t}: criterion not met; no larger pipe on its path fits below the rims or lowers its water level. The tailwater, inverts or rim control: lower the pipes, raise the rim or reduce the flow.");
                break;
            }
            r = StormNetworkSolver.Solve(net with { Pipes = net.Pipes.Select(p => pipes[p.Name]).ToList() });
        }
        var sized = net with { Pipes = net.Pipes.Select(p => pipes[p.Name]).ToList() };
        if (changes.Count > 0) notes.Add("Inverts were kept: check cover, crown matching at structures and structure sizes before exporting.");
        return new(sized, changes.OrderBy(x => order.IndexOf(x.Pipe)).ToList(), notes, r);
    }

    private static string Fmt(double rise, double toBase) => (rise / toBase).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Pipes ordered so that every pipe comes after all pipes flowing into its upstream structure.</summary>
    internal static List<string> UpstreamFirst(IReadOnlyList<NetworkPipe> pipes)
    {
        var done = new HashSet<string>();
        var order = new List<string>();
        void Visit(NetworkPipe p, int depth)
        {
            if (done.Contains(p.Name) || depth > pipes.Count) return;
            foreach (var u in pipes.Where(u => u.To == p.From)) Visit(u, depth + 1);
            if (done.Add(p.Name)) order.Add(p.Name);
        }
        foreach (var p in pipes) Visit(p, 0);
        return order;
    }

    /// <summary>Manning full-flow capacity of a circular pipe: (k/n)·A·R^⅔·S^½ with A = πD²/4, R = D/4.</summary>
    public static double QFull(double d, double n, double s, UnitConstants c) =>
        c.ManningK / n * (Math.PI * d * d / 4) * Math.Pow(d / 4, 2.0 / 3.0) * Math.Sqrt(s);

    /// <summary>Mean velocity at normal depth in a circular pipe (bisection on depth up to the crown); full-flow velocity when Q exceeds the full-flow capacity.</summary>
    public static double NormalVelocity(double d, double n, double s, double q, UnitConstants c)
    {
        if (!(q > 0)) return 0;
        double qf = QFull(d, n, s, c);
        if (q >= qf) return q / (Math.PI * d * d / 4);
        double A(double y) { double th = 2 * Math.Acos(1 - 2 * y / d); return d * d / 8 * (th - Math.Sin(th)); }
        double Qy(double y) { double th = 2 * Math.Acos(1 - 2 * y / d), a = A(y), pw = d * th / 2; return c.ManningK / n * a * Math.Pow(a / pw, 2.0 / 3.0) * Math.Sqrt(s); }
        // Q(y) rises to a maximum near 0.938 D, then falls to Qfull; Q < Qfull has one root below that maximum.
        double lo = 1e-9 * d, hi = 0.938 * d;
        for (int i = 0; i < 200; i++) { double m = 0.5 * (lo + hi); if (Qy(m) < q) lo = m; else hi = m; }
        return q / A(0.5 * (lo + hi));
    }
}
