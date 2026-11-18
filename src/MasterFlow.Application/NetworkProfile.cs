using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application;

/// <summary>A structure on a profile, at its station measured from the outfall.</summary>
public sealed record ProfileStructure(string Name, double Station, double? Rim, double Hgl, double Egl, double Bottom, double? CriteriaLine, bool MeetsCriteria);

/// <summary>
/// HGL, EGL, ground and the criterion limit (ground − required clearance) read off a profile at one station.
/// Ground between structures is taken as a straight line between rims (the street surface between
/// structures is not known to MasterFlow).
/// </summary>
public sealed record ProfileReading(double Station, double Hgl, double Egl, double? Ground, double? Limit)
{
    public bool MeetsCriteria => Limit is not { } l || Hgl <= l + 1e-9;
}

/// <summary>
/// Plot-ready HGL/EGL profile along one flow path, from the outfall (station 0) upstream. Every series is
/// in (station, elevation) with stations increasing upstream; vertical steps at structures are kept as
/// two points at the same station. Used by the app, the PDF and the HTML report so they draw the same thing.
/// </summary>
public sealed record NetworkProfile(
    string From, string To,
    IReadOnlyList<(double S, double Z)> Invert, IReadOnlyList<(double S, double Z)> Crown,
    IReadOnlyList<(double S, double Z)> Hgl, IReadOnlyList<(double S, double Z)> Egl,
    IReadOnlyList<(double S, double Z)> Ground, IReadOnlyList<ProfileStructure> Structures,
    IReadOnlyList<(string Pipe, double StationDown, double StationUp)> Pipes)
{
    public double Length => Pipes.Count == 0 ? 0 : Pipes.Max(p => p.StationUp);

    /// <summary>Required HGL clearance below the rim/ground (null when no HGL criterion is set).</summary>
    public double? LimitBelowGround { get; init; }
    /// <summary>Name of the limit row, e.g. "Rapid City limit".</summary>
    public string LimitLabel { get; init; } = "HGL limit";
    /// <summary>Spacing of the labelled readings under the profile: 50 ft (U.S.) or 15 m (SI).</summary>
    public double ReadingInterval { get; init; } = 50;

    public static double DefaultInterval(UnitSystem u) => u == UnitSystem.SI ? 15 : 50;

    /// <summary>Readings at 0, interval, 2·interval, … and at the upstream end (unless within 0.2·interval of the last). At a structure (a vertical step) the value on its upstream side is used.</summary>
    public IReadOnlyList<ProfileReading> Readings(double? interval = null)
    {
        double step = interval ?? ReadingInterval;
        var list = new List<ProfileReading>();
        if (Hgl.Count == 0 || !(step > 0)) return list;
        var stations = new List<double>();
        for (int i = 0; i * step < Length - 1e-6; i++) stations.Add(i * step);
        // The upstream end is added unless it falls within a fifth of an interval of the last reading
        // (its structure is labelled on the plot anyway).
        if (stations.Count == 0 || Length - stations[^1] >= 0.2 * step - 1e-9) stations.Add(Length);
        foreach (var st in stations)
        {
            double? g = Ground.Count > 0 && st >= Ground[0].S - 1e-9 && st <= Ground[^1].S + 1e-9 ? At(Ground, st) : null;
            double? lim = g is { } gg && LimitBelowGround is { } c ? gg - c : null;
            list.Add(new ProfileReading(st, At(Hgl, st), At(Egl, st), g, lim));
        }
        return list;
    }

    /// <summary>Linear interpolation on a series ordered by station; at a vertical step the last (upstream) point wins.</summary>
    public static double At(IReadOnlyList<(double S, double Z)> pts, double s)
    {
        int j = -1;
        for (int i = 0; i < pts.Count; i++) if (pts[i].S <= s + 1e-9) j = i;
        if (j < 0) return pts[0].Z;
        if (Math.Abs(pts[j].S - s) < 1e-9 || j == pts.Count - 1) return pts[j].Z;
        var (s0, z0) = pts[j]; var (s1, z1) = pts[j + 1];
        return s1 - s0 < 1e-12 ? z1 : z0 + (z1 - z0) * (s - s0) / (s1 - s0);
    }
    public double MinZ => Invert.Concat(Hgl).Select(p => p.Z).Concat(Structures.Select(s => s.Bottom)).DefaultIfEmpty(0).Min();
    public double MaxZ => Egl.Concat(Hgl).Concat(Ground).Concat(Crown).Select(p => p.Z).DefaultIfEmpty(1).Max();

    /// <summary>Structures where a path to the outfall starts (no inflow pipes), longest path first.</summary>
    public static IReadOnlyList<string> PathStarts(StormNetworkResult r) =>
        r.Pipes.Select(p => p.Pipe.From).Where(s => !r.Pipes.Any(p => p.Pipe.To == s)).Distinct()
            .OrderByDescending(s => r.PathToOutfall(s).Sum(p => p.Pipe.Length)).ThenBy(s => s, StringComparer.Ordinal).ToList();

    public static NetworkProfile? Build(StormNetworkResult r, string from)
    {
        var path = r.PathToOutfall(from);
        if (!r.Succeeded || path.Count == 0) return null;
        var down = path.AsEnumerable().Reverse().ToList(); // outfall pipe first
        var inv = new List<(double, double)>(); var crown = new List<(double, double)>();
        var hgl = new List<(double, double)>(); var egl = new List<(double, double)>();
        var pipes = new List<(string, double, double)>();
        var structs = new List<ProfileStructure>();
        var crit = r.Network.Criteria.HglBelowRim;
        double sd = 0;

        void AddStructure(string name, double st, double bottom)
        {
            var s = r.Structure(name);
            if (s is null) return;
            double? rim = s.Structure.RimElevation;
            double? line = rim is { } rr && crit is { } cc ? rr - cc : null;
            structs.Add(new(name, st, rim, s.Hgl, s.Egl, bottom, line, line is not { } l || s.Hgl <= l + 1e-9));
        }

        var outfall = r.Structure(down[0].Pipe.To);
        AddStructure(down[0].Pipe.To, 0, down[0].Pipe.DownstreamInvert);
        if (outfall is not null) { hgl.Add((0, outfall.Hgl)); egl.Add((0, outfall.Egl)); }
        foreach (var p in down)
        {
            double su = sd + p.Pipe.Length;
            pipes.Add((p.Pipe.Name, sd, su));
            for (int i = p.Profile.Count - 1; i >= 0; i--)
            {
                var pt = p.Profile[i];
                double st = su - pt.Station;
                inv.Add((st, pt.Invert)); crown.Add((st, pt.Crown)); hgl.Add((st, pt.Hgl)); egl.Add((st, pt.Egl));
            }
            var up = r.Structure(p.Pipe.From);
            if (up is not null) { hgl.Add((su, up.Hgl)); egl.Add((su, up.Egl)); }
            AddStructure(p.Pipe.From, su, Math.Min(p.Pipe.UpstreamInvert, r.Pipes.Where(q => q.Pipe.To == p.Pipe.From).Select(q => q.Pipe.DownstreamInvert).DefaultIfEmpty(double.PositiveInfinity).Min()));
            sd = su;
        }
        var ground = structs.Where(s => s.Rim is not null).Select(s => (s.Station, s.Rim!.Value)).ToList();
        string src = r.Network.Criteria.Source;
        return new NetworkProfile(from, down[0].Pipe.To, inv, crown, hgl, egl, ground, structs, pipes)
        {
            LimitBelowGround = crit,
            LimitLabel = src.Contains("Rapid City", StringComparison.OrdinalIgnoreCase) ? "Rapid City limit" : "HGL limit",
            ReadingInterval = DefaultInterval(r.Network.Units),
        };
    }
}
