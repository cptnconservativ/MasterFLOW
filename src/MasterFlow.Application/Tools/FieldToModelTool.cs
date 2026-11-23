using System.Globalization;
using MasterFlow.Application.Field;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Field-to-model loop (premium, D-250). As-built rim shots and tape measure-downs (rim to pipe invert) from the field,
/// with phone photos whose EXIF GPS places them on the model, update the storm network's rims and pipe inverts; the
/// HGL is re-run on the as-built network and checked against the criteria to support the as-built certification.
/// </summary>
public sealed class FieldToModelTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Id => "field-to-model";
    public string Name => "Field-to-model: as-built survey, photos and HGL certification";
    public string Description => "Turns field measurements into an as-built model: rim shots and tape measure-downs from the rim to each pipe invert, with phone photos placed by their GPS, update the storm network's rims and inverts. Lists every difference from design (inverts, rims, slopes, adverse pipes, structures out of place), re-runs the HGL on the as-built network, checks it against the criteria and writes the certification statement. Apply puts the as-built values into the network.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context)
    {
        var net = context.Network;
        var rows = new List<string>();
        if (net is not null)
            foreach (var s in net.Structures.Take(6))
            {
                bool first = true;
                foreach (var p in net.Pipes.Where(p => p.From == s.Name || p.To == s.Name))
                {
                    double inv = p.From == s.Name ? p.UpstreamInvert : p.DownstreamInvert;
                    double rim = s.RimElevation ?? inv + 6;
                    rows.Add(FormattableString.Invariant($"{s.Name}\t{(first ? (rim + 0.04).ToString("0.00", Inv) : "")}\t{p.Name}\t{rim - inv + 0.02:0.00}\t\t"));
                    first = false;
                }
            }
        else rows.Add("MH-1\t106.04\tP1\t5.27\t\t");
        return $"""
            # Field-to-model: as-built rims and tape measure-downs (rim to the pipe invert) update the storm network, and
            # the HGL is re-run on the as-built network to certify it. One row per measurement; a structure's rim is
            # given once. Instead of a measure-down, give a surveyed Invert. A row with no Structure is matched to the
            # nearest structure by its Northing/Easting or Latitude/Longitude.
            #   coordinate system = sd-south | sd-north | none   (NAD83 State Plane, US ft, for GPS positions)
            #   photos = <folder of phone JPEGs>: each photo is matched to the structure named in its file name, or to the
            #   nearest structure by the photo's GPS (within match radius), and listed in the photo log.
            network = {net?.Name ?? ""}
            coordinate system = sd-south
            photos =
            match radius = 25        # ft
            invert tolerance = 0.10  # ft: larger differences from design are flagged
            rim tolerance = 0.10     # ft

            [As-built]
            Structure	Rim	Pipe	Measure-down	Northing	Easting
            {string.Join("\n", rows)}
            """;
    }

    private sealed record Shot(int Row, string? Structure, double? Rim, string? Pipe, double? Down, double? Invert, double? N, double? E, string? Photo, string Note);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Field-to-model (as-built)";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        string netName = p.Text("network") ?? "";
        var pick = context.Networks.Where(n => netName.Length == 0 ? n.Network.Name == context.Network?.Name : n.Network.Name.Equals(netName, StringComparison.OrdinalIgnoreCase)).Select(n => n.Network).FirstOrDefault()
                   ?? (netName.Length == 0 || context.Network?.Name.Equals(netName, StringComparison.OrdinalIgnoreCase) == true ? context.Network : null);
        if (pick is null) return ToolResult.Failed(title, CalcMessage.Error("E-FM-001", netName.Length == 0 ? "Select a storm network (or name one: network = <name>)." : $"No storm network is named \"{netName}\"."));
        var net = pick;
        bool si = net.Units == UnitSystem.SI;
        string lu = si ? "m" : "ft";
        double radius = p.Number("match radius", si ? 7.5 : 25), tolInv = p.Number("invert tolerance", si ? 0.03 : 0.10), tolRim = p.Number("rim tolerance", si ? 0.03 : 0.10);
        string zoneId = p.Text("coordinate system") ?? "none";
        var zone = zoneId.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : StatePlaneZone.Find(zoneId);
        if (zone is null && !zoneId.Equals("none", StringComparison.OrdinalIgnoreCase))
            msgs.Add(CalcMessage.Warning("W-FM-001", $"coordinate system = {zoneId} is not known ({string.Join(", ", StatePlaneZone.Zones.Select(z => z.Id))} or none); GPS positions are not used."));
        // State plane in the model's units: US survey ft, or m for an SI network.
        (double N, double E)? Grid(double? lat, double? lon) => zone is not null && lat is { } a && lon is { } o ? zone.Project(a, o, !si) : null;
        bool coords = net.Structures.Any(s => s.Northing is not null && s.Easting is not null);

        // ---- shots
        var head = p.Header("As-built").Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => head.FindIndex(h => names.Any(n => h == n || h.StartsWith(n + " ", StringComparison.Ordinal) || h.StartsWith(n + "(", StringComparison.Ordinal)));
        int cS = Col("structure"), cR = Col("rim"), cP = Col("pipe"), cD = Col("measure-down", "measure down", "down"), cI = Col("invert"),
            cN = Col("northing", "n"), cE = Col("easting", "e"), cLa = Col("latitude", "lat"), cLo = Col("longitude", "lon"), cPh = Col("photo"), cNo = Col("note");
        var rowsIn = p.Rows("As-built");
        if (rowsIn.Count == 0 || cS < 0) return ToolResult.Failed(title, CalcMessage.Error("E-FM-002", "List the measurements under [As-built] with a header row: Structure, Rim, Pipe, Measure-down (or Invert), and optionally Northing, Easting, Latitude, Longitude, Photo, Note."));
        string? Txt(string[] r, int c) => c >= 0 && c < r.Length && r[c].Trim().Length > 0 ? r[c].Trim() : null;
        double? Num(string[] r, int c, string what, int row)
        {
            var s = Txt(r, c);
            if (s is null) return null;
            if (double.TryParse(s, NumberStyles.Float, Inv, out var v)) return v;
            msgs.Add(CalcMessage.Error("E-FM-003", $"[As-built] row {row}: {what} \"{s}\" is not a number."));
            return null;
        }
        var shots = new List<Shot>();
        for (int i = 0; i < rowsIn.Count; i++)
        {
            var r = rowsIn[i];
            double? n = Num(r, cN, "Northing", i + 1), e = Num(r, cE, "Easting", i + 1);
            if (n is null && Grid(Num(r, cLa, "Latitude", i + 1), Num(r, cLo, "Longitude", i + 1)) is { } g) (n, e) = (g.N, g.E);
            shots.Add(new(i + 1, Txt(r, cS), Num(r, cR, "Rim", i + 1), Txt(r, cP), Num(r, cD, "Measure-down", i + 1), Num(r, cI, "Invert", i + 1), n, e, Txt(r, cPh), Txt(r, cNo) ?? ""));
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        NetworkStructure? Nearest(double n, double e, out double dist)
        {
            var best = net.Structures.Where(s => s.Northing is not null && s.Easting is not null)
                .Select(s => (S: s, D: Math.Sqrt(Math.Pow(s.Northing!.Value - n, 2) + Math.Pow(s.Easting!.Value - e, 2)))).OrderBy(x => x.D).FirstOrDefault();
            dist = best.S is null ? double.NaN : best.D;
            return best.S is not null && best.D <= radius ? best.S : null;
        }

        // Resolve each shot to a structure.
        var located = new Dictionary<string, List<(double N, double E, string Source)>>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<(Shot Shot, NetworkStructure S)>();
        foreach (var s in shots)
        {
            NetworkStructure? st = null;
            if (s.Structure is { } name)
            {
                st = net.Structures.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (st is null) { msgs.Add(CalcMessage.Warning("W-FM-002", $"[As-built] row {s.Row}: {net.Name} has no structure \"{name}\"; the row is skipped.")); continue; }
            }
            else if (s.N is { } n0 && s.E is { } e0)
            {
                if (!coords) { msgs.Add(CalcMessage.Warning("W-FM-003", $"[As-built] row {s.Row}: no structure is named and the network's structures have no coordinates to match a position to.")); continue; }
                st = Nearest(n0, e0, out var d);
                if (st is null) { msgs.Add(CalcMessage.Warning("W-FM-004", $"[As-built] row {s.Row}: no structure within {radius:0.#} {lu} of the position (nearest {d:0.#} {lu}); the row is skipped.")); continue; }
            }
            else { msgs.Add(CalcMessage.Warning("W-FM-002", $"[As-built] row {s.Row}: give a Structure or a position.")); continue; }
            resolved.Add((s, st));
            if (s.N is { } n1 && s.E is { } e1) Add(located, st.Name, (n1, e1, s.Photo is null ? $"row {s.Row}" : s.Photo));
        }

        // ---- photos
        var photoRows = new List<IReadOnlyList<object?>>();
        var photoCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (p.Text("photos") is { } folder)
        {
            if (!Directory.Exists(folder)) msgs.Add(CalcMessage.Warning("W-FM-005", $"photos = {folder}: the folder does not exist."));
            else
            {
                var files = Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToList();
                int noGps = 0;
                foreach (var f in files)
                {
                    PhotoTag? tag;
                    try { tag = ExifGps.Read(f); } catch (IOException) { tag = null; }
                    if (tag is null) continue;
                    var g = Grid(tag.Latitude, tag.Longitude);
                    if (tag.Latitude is null) noGps++;
                    string stem = Path.GetFileNameWithoutExtension(f);
                    // File name first ("MH-2 east.jpg"), longest name wins so "MH-12" beats "MH-1".
                    var byName = net.Structures.Where(s => ContainsToken(stem, s.Name)).OrderByDescending(s => s.Name.Length).FirstOrDefault();
                    double dist = double.NaN; string how;
                    NetworkStructure? st = byName;
                    if (st is not null) { how = "file name"; if (g is { } gg && st.Northing is { } sn && st.Easting is { } se) dist = Math.Sqrt(Math.Pow(sn - gg.N, 2) + Math.Pow(se - gg.E, 2)); }
                    else if (g is { } g2 && coords) { st = Nearest(g2.N, g2.E, out dist); how = st is null ? $"no structure within {radius:0.#} {lu}" : "GPS"; }
                    else how = tag.Latitude is null ? "no GPS in the photo" : zone is null ? "set a coordinate system to use its GPS" : "network has no coordinates";
                    if (st is not null)
                    {
                        photoCount[st.Name] = photoCount.GetValueOrDefault(st.Name) + 1;
                        if (g is { } g3) Add(located, st.Name, (g3.N, g3.E, Path.GetFileName(f)));
                    }
                    photoRows.Add([Path.GetFileName(f), tag.Taken?.ToString("yyyy-MM-dd HH:mm", Inv), tag.Latitude?.ToString("0.000000", Inv), tag.Longitude?.ToString("0.000000", Inv), g?.N, g?.E, st?.Name ?? "—", dist, how]);
                }
                if (files.Count == 0) msgs.Add(CalcMessage.Warning("W-FM-005", $"photos = {folder}: no .jpg files."));
                if (noGps > 0) msgs.Add(CalcMessage.Info("I-FM-001", $"{noGps} photo(s) have no GPS position (location services off for the camera); they are matched by file name only."));
            }
        }

        // ---- as-built values
        var rim = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var ends = new Dictionary<(string Pipe, bool Up), (double Inv, string How)>();
        foreach (var (s, st) in resolved)
            if (s.Rim is { } r)
            {
                if (rim.TryGetValue(st.Name, out var r0) && Math.Abs(r0 - r) > 1e-6) msgs.Add(CalcMessage.Warning("W-FM-006", $"{st.Name}: rim given twice ({r0:0.00} and {r:0.00}); the first is used."));
                else rim[st.Name] = r;
            }
        foreach (var (s, st) in resolved)
        {
            if (s.Pipe is null) { if (s.Down is not null || s.Invert is not null) msgs.Add(CalcMessage.Warning("W-FM-007", $"[As-built] row {s.Row}: a measure-down or invert needs the Pipe it was taken on.")); continue; }
            var pipe = net.Pipes.FirstOrDefault(x => x.Name.Equals(s.Pipe, StringComparison.OrdinalIgnoreCase));
            if (pipe is null) { msgs.Add(CalcMessage.Warning("W-FM-008", $"[As-built] row {s.Row}: {net.Name} has no pipe \"{s.Pipe}\"."));  continue; }
            bool up = pipe.From.Equals(st.Name, StringComparison.OrdinalIgnoreCase);
            if (!up && !pipe.To.Equals(st.Name, StringComparison.OrdinalIgnoreCase)) { msgs.Add(CalcMessage.Warning("W-FM-008", $"[As-built] row {s.Row}: pipe {pipe.Name} does not connect to {st.Name} (it runs {pipe.From} → {pipe.To}).")); continue; }
            double? inv = s.Invert;
            string how = "surveyed invert";
            if (inv is null && s.Down is { } d)
            {
                double? rr = rim.TryGetValue(st.Name, out var rv) ? rv : st.RimElevation;
                if (rr is null) { msgs.Add(CalcMessage.Warning("W-FM-009", $"{st.Name}: a measure-down needs a rim; give the as-built rim.")); continue; }
                inv = rr - d; how = rim.ContainsKey(st.Name) ? "as-built rim − measure-down" : "design rim − measure-down";
                if (!rim.ContainsKey(st.Name)) msgs.Add(CalcMessage.Warning("W-FM-010", $"{st.Name}: no as-built rim, so the measure-down to {pipe.Name} is taken from the design rim; shoot the rim to certify."));
            }
            if (inv is null) continue;
            ends[(pipe.Name, up)] = (inv.Value, how);
        }

        var asBuilt = net with
        {
            Structures = [.. net.Structures.Select(s => rim.TryGetValue(s.Name, out var r) ? s with { RimElevation = r } : s)],
            Pipes = [.. net.Pipes.Select(x => x with
            {
                UpstreamInvert = ends.TryGetValue((x.Name, true), out var u) ? u.Inv : x.UpstreamInvert,
                DownstreamInvert = ends.TryGetValue((x.Name, false), out var d) ? d.Inv : x.DownstreamInvert,
            })],
        };
        var design = context.Networks.FirstOrDefault(n => n.Network == net).Result ?? (context.Network == net ? context.NetworkResult : null) ?? StormNetworkSolver.Solve(net);
        var built = StormNetworkSolver.Solve(asBuilt);

        // ---- tables
        var tables = new List<ToolTable>();
        var invRows = new List<IReadOnlyList<object?>>();
        int differ = 0, unmeasured = 0;
        foreach (var x in net.Pipes)
            foreach (var up in new[] { true, false })
            {
                string at = up ? x.From : x.To;
                double dz = up ? x.UpstreamInvert : x.DownstreamInvert;
                if (ends.TryGetValue((x.Name, up), out var m))
                {
                    double delta = m.Inv - dz;
                    bool bad = Math.Abs(delta) > tolInv + 1e-9;
                    if (bad) differ++;
                    invRows.Add([x.Name, up ? "upstream" : "downstream", at, dz, m.Inv, delta, m.How, bad ? (delta > 0 ? "HIGH" : "LOW") : "OK"]);
                }
                else { unmeasured++; invRows.Add([x.Name, up ? "upstream" : "downstream", at, dz, null, null, "", "NOT MEASURED"]); }
            }
        tables.Add(new ToolTable($"Pipe inverts: design vs as-built ({lu})", ["Pipe", "End", "Structure", "Design invert", "As-built invert", "Difference", "From", "Status"], invRows,
            [FormattableString.Invariant($"Status HIGH or LOW: the as-built invert differs from design by more than {tolInv:0.00} {lu}. NOT MEASURED: the design invert is kept.")]));

        var slopeRows = new List<IReadOnlyList<object?>>();
        int adverse = 0, overCap = 0;
        foreach (var (d0, b0) in net.Pipes.Zip(asBuilt.Pipes))
        {
            double sd = (d0.UpstreamInvert - d0.DownstreamInvert) / d0.Length, sb = (b0.UpstreamInvert - b0.DownstreamInvert) / b0.Length;
            var pd = design.Pipes.FirstOrDefault(r => r.Pipe.Name == d0.Name); var pb = built.Pipes.FirstOrDefault(r => r.Pipe.Name == d0.Name);
            bool moved = new[] { (d0.Name, true), (d0.Name, false) }.Any(k => ends.TryGetValue(k, out var m) && Math.Abs(m.Inv - (k.Item2 ? d0.UpstreamInvert : d0.DownstreamInvert)) > tolInv + 1e-9);
            string status = sb <= 0 ? "ADVERSE" : sb < 0.5 * sd ? "FLATTER" : moved ? "CHANGED" : "AS DESIGNED";
            if (sb <= 0) adverse++;
            if (pb is not null && pd is not null && d0.Discharge > pb.FullFlowCapacity && !(d0.Discharge > pd.FullFlowCapacity)) { status += ", OVER CAPACITY"; overCap++; }
            slopeRows.Add([d0.Name, sd * 100, sb * 100, (sb - sd) * 100, pd?.FullFlowCapacity, pb?.FullFlowCapacity, d0.Discharge, status]);
        }
        tables.Add(new ToolTable("Pipe slopes and capacity: design vs as-built", ["Pipe", "Design S (%)", "As-built S (%)", "Change (%)", $"Design Q full ({(si ? "m³/s" : "cfs")})", $"As-built Q full ({(si ? "m³/s" : "cfs")})", $"Q ({(si ? "m³/s" : "cfs")})", "Status"], slopeRows,
            ["ADVERSE: the as-built pipe runs uphill or flat. FLATTER: less than half the design slope. CHANGED: an end differs from design by more than the invert tolerance. OVER CAPACITY: the design flow exceeds the as-built full-flow capacity but not the design capacity."]));

        var need = net.Criteria.HglBelowRim;
        var strRows = new List<IReadOnlyList<object?>>();
        int fails = 0, checkedN = 0;
        foreach (var s in net.Structures)
        {
            var sd = design.Structures.FirstOrDefault(r => r.Structure.Name == s.Name); var sb = built.Structures.FirstOrDefault(r => r.Structure.Name == s.Name);
            double? rimB = rim.TryGetValue(s.Name, out var rr) ? rr : null;
            double? offset = located.TryGetValue(s.Name, out var pts) && s.Northing is { } sn && s.Easting is { } se ? pts.Select(q => Math.Sqrt(Math.Pow(q.N - sn, 2) + Math.Pow(q.E - se, 2))).Min() : null;
            string result = "";
            if (built.Succeeded && sb?.HglClearance is { } c && need is { } nd) { checkedN++; result = c >= nd - 1e-9 ? "OK" : "FAILS"; if (result == "FAILS") fails++; }
            string rimStatus = rimB is { } b && s.RimElevation is { } dr ? (Math.Abs(b - dr) > tolRim + 1e-9 ? (b > dr ? "rim HIGH" : "rim LOW") : "rim OK") : rimB is null ? "rim not shot" : "rim new";
            strRows.Add([s.Name, s.RimElevation, rimB, rimB is { } b2 && s.RimElevation is { } d2 ? b2 - d2 : null, sd?.Hgl, sb?.Hgl, sd is not null && sb is not null ? sb.Hgl - sd.Hgl : null, sb?.HglClearance, need, offset, photoCount.GetValueOrDefault(s.Name), rimStatus, result]);
        }
        tables.Insert(0, new ToolTable($"As-built HGL check by structure ({lu})", ["Structure", "Design rim", "As-built rim", "Rim difference", "Design HGL", "As-built HGL", "HGL change", "Rim − HGL", "Required", $"GPS offset ({lu})", "Photos", "Rim", "Result"], strRows,
            [$"As-built HGL from the as-built rims and inverts with the design flows and tailwater. GPS offset: distance from the model position to the nearest field position (phone GPS is typically good to 10–15 ft)."]));
        if (photoRows.Count > 0)
            tables.Add(new ToolTable("Photo log", ["Photo", "Taken", "Latitude", "Longitude", "Northing", "Easting", "Structure", $"Distance ({lu})", "Matched by"], photoRows));
        var far = strRows.Where(r => r[9] is double o && o > radius).Select(r => (string)r[0]!).ToList();
        if (far.Count > 0) msgs.Add(CalcMessage.Warning("W-FM-011", $"Field position more than {radius:0.#} {lu} from the model for {string.Join(", ", far)}: check the structure was built where the plans show it."));

        // ---- certification
        var summary = new List<string>();
        int measuredEnds = ends.Count, totalEnds = net.Pipes.Count * 2;
        summary.Add($"{measuredEnds} of {totalEnds} pipe ends and {rim.Count} of {net.Structures.Count} rims measured; {differ} invert(s) differ from design by more than {tolInv.ToString("0.00", Inv)} {lu}; {adverse} adverse pipe(s).");
        if (!built.Succeeded) msgs.Add(CalcMessage.Error("E-FM-004", "The as-built network could not be solved: " + string.Join("; ", built.Messages.Where(m => m.Severity == MessageSeverity.Error).Select(m => m.Text))));
        else if (need is null) summary.Add("The network has no HGL-below-rim criterion, so the as-built HGL is reported but not checked.");
        else
        {
            string req = need.Value.ToString("0.00", Inv);
            bool complete = unmeasured == 0 && rim.Count == net.Structures.Count;
            summary.Add(fails == 0
                ? $"Certification: as constructed, with the as-built rims and inverts, the system carries the design flows with the hydraulic grade line at least {req} {lu} below the rim at all {checkedN} structures ({net.Criteria.Source})."
                : $"As-built HGL is less than {req} {lu} below the rim at {fails} of {checkedN} structures ({string.Join(", ", strRows.Where(r => r[^1] is "FAILS").Select(r => r[0]))}); the as-built system does not meet the criterion there.");
            var other = built.Messages.Where(m => m.Severity == MessageSeverity.Warning).ToList();
            if (adverse > 0 || overCap > 0 || other.Count > 0)
                summary.Add($"Also check before certifying: {(adverse > 0 ? $"{adverse} adverse pipe(s); " : "")}{(overCap > 0 ? $"{overCap} pipe(s) whose as-built full-flow capacity is below the design flow; " : "")}{other.Count} other as-built criteria warning(s) (see messages).");
            if (!complete) summary.Add($"Not a complete as-built: {unmeasured} pipe end(s) and {net.Structures.Count - rim.Count} rim(s) were not measured and keep their design values.");
        }
        var worst = built.Structures.Where(s => s.HglClearance is not null).OrderBy(s => s.HglClearance).FirstOrDefault();
        var worstD = design.Structures.FirstOrDefault(s => s.Structure.Name == worst?.Structure.Name);
        if (worst is not null) summary.Add(FormattableString.Invariant($"Least rim − HGL: {worst.HglClearance:0.00} {lu} at {worst.Structure.Name} (design {worstD?.HglClearance:0.00} {lu})."));
        foreach (var m in built.Messages.Where(m => m.Severity == MessageSeverity.Warning).Take(5)) msgs.Add(CalcMessage.Warning(m.Code, "As-built: " + m.Text));

        // ---- profile of the longest flow path
        var charts = new List<ToolChart>();
        var path = LongestPath(net);
        if (path.Count > 0 && built.Succeeded)
        {
            var dI = new List<(double, double)>(); var bI = new List<(double, double)>(); var dH = new List<(double, double)>(); var bH = new List<(double, double)>(); var rims = new List<(double, double)>();
            double x0 = 0;
            foreach (var pipe in path)   // outfall first
            {
                var a = asBuilt.Pipes.First(q => q.Name == pipe.Name);
                var rd = design.Pipes.FirstOrDefault(q => q.Pipe.Name == pipe.Name); var rb = built.Pipes.FirstOrDefault(q => q.Pipe.Name == pipe.Name);
                dI.Add((x0, pipe.DownstreamInvert)); dI.Add((x0 + pipe.Length, pipe.UpstreamInvert));
                bI.Add((x0, a.DownstreamInvert)); bI.Add((x0 + pipe.Length, a.UpstreamInvert));
                if (rd is not null) { dH.Add((x0, rd.HglDown)); dH.Add((x0 + pipe.Length, rd.HglUp)); }
                if (rb is not null) { bH.Add((x0, rb.HglDown)); bH.Add((x0 + pipe.Length, rb.HglUp)); }
                if (asBuilt.Structures.First(s => s.Name == pipe.To).RimElevation is { } r1 && rims.Count == 0) rims.Add((x0, r1));
                if (asBuilt.Structures.First(s => s.Name == pipe.From).RimElevation is { } r2) rims.Add((x0 + pipe.Length, r2));
                x0 += pipe.Length;
            }
            charts.Add(new ToolChart($"{net.Name}: design vs as-built profile ({path[^1].From} to {path[0].To})", $"Distance from outfall ({lu})", $"Elevation ({lu})",
                [new("Design invert", dI, true), new("As-built invert", bI), new("Design HGL", dH, true), new("As-built HGL", bH), new("As-built rim", rims, false, -1, true)]));
        }

        int changes = ends.Count(e => Math.Abs(e.Value.Inv - (e.Key.Up ? net.Pipes.First(q => q.Name == e.Key.Pipe).UpstreamInvert : net.Pipes.First(q => q.Name == e.Key.Pipe).DownstreamInvert)) > 1e-9)
                      + rim.Count(r => net.Structures.First(s => s.Name == r.Key).RimElevation != r.Value);
        return new ToolResult(title, summary, tables, msgs)
        {
            Charts = charts,
            UpdatedNetwork = changes > 0 ? asBuilt : null,
            UpdateDescription = changes > 0 ? $"Put the as-built values into {net.Name}: {ends.Count} pipe invert(s) and {rim.Count} rim(s) from the field." : null,
        };
    }

    private static void Add(Dictionary<string, List<(double, double, string)>> d, string k, (double, double, string) v)
    {
        if (!d.TryGetValue(k, out var l)) d[k] = l = [];
        l.Add(v);
    }

    private static bool ContainsToken(string text, string name)
    {
        int i = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        while (i >= 0)
        {
            bool before = i == 0 || !char.IsLetterOrDigit(text[i - 1]), after = i + name.Length >= text.Length || !char.IsLetterOrDigit(text[i + name.Length]);
            if (before && after) return true;
            i = text.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>The pipes of the longest path from any structure to the outfall, outfall pipe first.</summary>
    private static List<NetworkPipe> LongestPath(StormNetwork net)
    {
        var best = new List<NetworkPipe>(); double bestLen = 0;
        foreach (var start in net.Structures)
        {
            var path = new List<NetworkPipe>(); var seen = new HashSet<string>(); string at = start.Name;
            while (net.Pipes.FirstOrDefault(q => q.From == at) is { } next && seen.Add(next.Name)) { path.Add(next); at = next.To; }
            double len = path.Sum(q => q.Length);
            if (len > bestLen) { bestLen = len; best = path; }
        }
        best.Reverse();
        return best;
    }
}
