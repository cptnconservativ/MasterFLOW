using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application;

/// <summary>A structure on a profile, at its station measured from the outfall.</summary>
public sealed record ProfileStructure(string Name, double Station, double? Rim, double Hgl, double Egl, double Bottom, double? CriteriaLine, bool MeetsCriteria);

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
        return new NetworkProfile(from, down[0].Pipe.To, inv, crown, hgl, egl, ground, structs, pipes);
    }
}
