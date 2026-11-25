using System.Globalization;

namespace MasterFlow.Domain;

/// <summary>
/// Works out which way water runs in each pipe of a storm network from its inverts, because a pipe drawn in
/// a design program (Civil 3D's refStart/refEnd) can be drawn against the flow. Rules (D-703):
/// <list type="number">
/// <item>With <c>allPipes</c> (import), every pipe runs from its higher invert to its lower invert.</item>
/// <item>At a structure that still has more than one outlet pipe, the outlet is the pipe with the lowest
/// invert at that structure. Other outlet pipes that are flat or adverse there are turned into inflows
/// (turning them never makes a pipe adverse). A pipe that genuinely slopes away from the structure is
/// never turned; that is a real split and stays unresolved.</item>
/// </list>
/// Pipes without two finite inverts are left as drawn.
/// </summary>
public static class PipeFlowDirection
{
    /// <summary>A pipe as drawn: <see cref="From"/>/<see cref="To"/> with the invert at each end.</summary>
    public sealed record Link(string Name, string From, string To, double FromInvert, double ToInvert);

    /// <summary>Pipes to turn around (by name), one note per turned pipe, and structures still left with several outlets.</summary>
    public sealed record Orientation(IReadOnlySet<string> Reversed, IReadOnlyList<string> Notes, IReadOnlyList<string> Unresolved);

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static Orientation Orient(IReadOnlyList<Link> links, bool allPipes)
    {
        var reversed = new HashSet<string>(StringComparer.Ordinal);
        var notes = new List<string>();
        var turnedAt = new Dictionary<string, string>(StringComparer.Ordinal);
        bool Known(Link l) => double.IsFinite(l.FromInvert) && double.IsFinite(l.ToInvert);
        // effective orientation
        (string From, string To, double Up, double Down) Eff(Link l) =>
            reversed.Contains(l.Name) ? (l.To, l.From, l.ToInvert, l.FromInvert) : (l.From, l.To, l.FromInvert, l.ToInvert);

        if (allPipes)
            foreach (var l in links)
                if (Known(l) && l.FromInvert < l.ToInvert)
                {
                    reversed.Add(l.Name);
                    notes.Add($"Pipe \"{l.Name}\" is drawn {l.From} → {l.To}, but its invert at {l.To} ({F(l.ToInvert)}) is above the invert at {l.From} ({F(l.FromInvert)}); flow taken as {l.To} → {l.From}.");
                }

        // structures with more than one outlet: lowest invert is the outlet
        var settled = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        for (int pass = 0; pass <= links.Count; pass++)
        {
            bool changed = false;
            foreach (var g in links.GroupBy(l => Eff(l).From, StringComparer.Ordinal))
            {
                var outs = g.ToList();
                if (outs.Count < 2 || !outs.All(Known)) continue;
                bool Away(Link l) => Eff(l).Down < Eff(l).Up;
                // the outlet: the pipe with the lowest invert here, among those that slope away from the structure if any do
                var pool = outs.Any(Away) ? outs.Where(Away).ToList() : outs;
                double lowest = pool.Min(l => Eff(l).Up);
                var atLowest = pool.Where(l => Eff(l).Up == lowest).ToList();
                Link? keep = atLowest.Count == 1 ? atLowest[0] : null;
                foreach (var l in outs)
                {
                    if (ReferenceEquals(l, keep) || settled.Contains(l.Name) || Away(l)) continue;   // a pipe sloping away is never turned
                    if (keep is null && atLowest.Contains(l)) continue;   // tie at the lowest invert: no basis to choose
                    if (!reversed.Remove(l.Name)) reversed.Add(l.Name);
                    settled.Add(l.Name);
                    turnedAt[l.Name] = g.Key;
                    changed = true;
                }
            }
            if (!changed) break;
        }
        foreach (var name in turnedAt.Keys)
        {
            var l = links.First(x => x.Name == name);
            var e = Eff(l);
            notes.Add($"Pipe \"{l.Name}\" is drawn {l.From} → {l.To}; {turnedAt[name]} already has a lower outlet invert, so this pipe is taken as an inflow ({e.From} → {e.To}, inverts {F(e.Up)} → {F(e.Down)}).");
        }
        foreach (var g in links.GroupBy(l => Eff(l).From, StringComparer.Ordinal).Where(g => g.Count() > 1))
            unresolved.Add(g.Key);
        return new(reversed, notes, unresolved);
    }

    /// <summary>Describes a structure's outlet pipes with their inverts there, for an error message.</summary>
    public static string DescribeOutlets(IEnumerable<Link> outs) =>
        string.Join(", ", outs.Select(l => double.IsFinite(l.FromInvert) && double.IsFinite(l.ToInvert)
            ? $"{l.Name} (invert {F(l.FromInvert)} here, {F(l.ToInvert)} at {l.To})" : $"{l.Name} (inverts missing)"));

    /// <summary>Applies <see cref="Orient"/> to a storm network: reversed pipes swap From/To and their inverts.</summary>
    public static (StormNetwork Network, Orientation Orientation) Orient(StormNetwork net, bool allPipes)
    {
        var o = Orient(net.Pipes.Select(p => new Link(p.Name, p.From, p.To, p.UpstreamInvert, p.DownstreamInvert)).ToList(), allPipes);
        if (o.Reversed.Count == 0) return (net, o);
        var pipes = net.Pipes.Select(p => o.Reversed.Contains(p.Name)
            ? p with { From = p.To, To = p.From, UpstreamInvert = p.DownstreamInvert, DownstreamInvert = p.UpstreamInvert }
            : p).ToList();
        return (net with { Pipes = pipes }, o);
    }
}
