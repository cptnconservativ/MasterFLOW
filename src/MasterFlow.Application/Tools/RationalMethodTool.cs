using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>A land use with its impervious percentage and runoff coefficients (IDCM Table 4-1).</summary>
public sealed record LandUse(string Name, double Impervious, double C2, double C5, double C10, double C100)
{
    public double C(int storm) => storm switch { 2 => C2, 5 => C5, 10 => C10, 100 => C100, _ => double.NaN };
}

/// <summary>
/// Rapid City rational method (IDCM 2012 §4.2): Q = C·i·A (Eq. 4-2-1), composite C (Eq. 4-2-2) from Table 4-1,
/// tc = ti + tt (Eq. 4-2-3) with ti = 1.87(1.1 − C5)·L^0.5·S^−0.33 (Eq. 4-2-4, S in percent), the urban
/// limit tc ≤ L/180 + 10 (Eq. 4-2-5) and minimum tc of 5 min (urban) or 10 min (non-urban), and i from the
/// Table 4-2 depth for a duration equal to tc. U.S. units only (the equations are empirical in feet, acres and inches).
/// </summary>
public static class RapidCityRational
{
    public static IReadOnlyList<LandUse> Table41 { get; } =
    [
        new("Business/Commercial", 95, 0.87, 0.87, 0.88, 0.89),
        new("Business/Neighborhood", 70, 0.60, 0.65, 0.70, 0.80),
        new("Single Family Residential", 45, 0.40, 0.45, 0.50, 0.60),
        new("Multi-Unit (detached)", 50, 0.45, 0.50, 0.60, 0.70),
        new("Multi-Unit (attached)", 70, 0.60, 0.65, 0.70, 0.80),
        new("1/2 Acre lot or larger", 45, 0.30, 0.35, 0.40, 0.60),
        new("Apartments", 70, 0.65, 0.70, 0.70, 0.80),
        new("Light Industrial", 80, 0.71, 0.72, 0.76, 0.82),
        new("Heavy Industrial", 90, 0.80, 0.80, 0.85, 0.90),
        new("Parks/Cemeteries", 7, 0.10, 0.10, 0.35, 0.60),
        new("Playground", 13, 0.45, 0.25, 0.35, 0.65),
        new("Schools", 50, 0.45, 0.50, 0.60, 0.70),
        new("Railroad Yard Area", 20, 0.40, 0.45, 0.50, 0.60),
        new("Paved Streets", 100, 0.87, 0.88, 0.90, 0.93),
        new("Gravel Streets", 40, 0.15, 0.25, 0.35, 0.65),
        new("Drive & Walks", 96, 0.87, 0.87, 0.88, 0.89),
        new("Roofs", 90, 0.80, 0.85, 0.90, 0.90),
        new("Lawns, Sandy Soil", 2, 0.00, 0.01, 0.05, 0.20),
        new("Lawns, Clay Soil", 2, 0.05, 0.10, 0.20, 0.40),
    ];

    /// <summary>Table 4-2 durations (min) and design storm depths (in.) for the 2-, 10- and 100-year storms.</summary>
    public static IReadOnlyList<double> Durations { get; } = [5, 15, 60, 120, 180, 360, 720, 1440];
    public static IReadOnlyDictionary<int, double[]> Depths { get; } = new Dictionary<int, double[]>
    {
        [2] = [0.36, 0.69, 1.05, 1.20, 1.25, 1.55, 1.70, 2.00],
        [10] = [0.53, 1.04, 1.86, 1.98, 2.10, 2.35, 2.70, 3.07],
        [100] = [0.79, 1.57, 2.95, 3.06, 3.15, 3.58, 4.05, 4.55],
    };

    /// <summary>Rainfall depth (in.) for a duration, interpolated log-log between Table 4-2 durations; NaN outside 5–1440 min.</summary>
    public static double Depth(int storm, double minutes)
    {
        if (!Depths.TryGetValue(storm, out var d) || !(minutes >= Durations[0] - 1e-9) || minutes > Durations[^1] + 1e-9) return double.NaN;
        for (int i = 1; i < Durations.Count; i++)
            if (minutes <= Durations[i] + 1e-9)
            {
                double t = Math.Log(minutes / Durations[i - 1]) / Math.Log(Durations[i] / Durations[i - 1]);
                return d[i - 1] * Math.Pow(d[i] / d[i - 1], t);
            }
        return double.NaN;
    }

    /// <summary>Average intensity (in./hr) for a duration equal to tc.</summary>
    public static double Intensity(int storm, double tcMinutes) => Depth(storm, tcMinutes) / (tcMinutes / 60);

    /// <summary>Overland (initial) time, Eq. 4-2-4: ti = 1.87(1.1 − C5) L^0.5 S^−0.33, L in ft, S in percent.</summary>
    public static double OverlandTime(double c5, double lengthFt, double slopePercent) =>
        1.87 * (1.1 - c5) * Math.Sqrt(lengthFt) * Math.Pow(slopePercent, -0.33);

    /// <summary>Urban first-design-point limit, Eq. 4-2-5: tc = L/180 + 10.</summary>
    public static double UrbanLimit(double lengthFt) => lengthFt / 180 + 10;
}

public sealed class RationalMethodTool : IDesignTool
{
    public string Id => "rational-rapid-city";
    public string Name => "Rational method (Rapid City IDCM §4.2)";
    public string Description => "Peak flows Q = CiA for each sub-basin: composite C from Table 4-1, tc from flow paths (overland Eq. 4-2-4, TR-55 sheet and shallow concentrated flow, gutter, pipe and channel by Manning) or entered times, intensity from Table 4-2 or your own IDF curves. Routes the design and minor storms through the selected storm network (tc accumulated pipe by pipe, Q = i·ΣCA) and can write the pipe flows into it.";
    public bool UsesNetwork => true;

    private static readonly int[] StormsWithC = [2, 5, 10, 100];

    public string SampleInput(DesignToolContext context)
    {
        // structures at the top of a line first, so the sample's long flow path starts upstream
        var net = context.Network;
        var names = net?.Structures.Where(s => net.Pipes.Any(p => p.From == s.Name))
            .OrderBy(s => net.Pipes.Any(p => p.To == s.Name) ? 1 : 0).Select(s => s.Name).Take(3).ToList() ?? [];
        string S(int i) => i < names.Count ? names[i] : $"CB-{i + 1}";
        return $"""
            # Rapid City rational method (IDCM 2012 §4.2). U.S. units: acres, feet, minutes.
            urban = yes
            # Storm whose Q becomes the network's design flow, and the minor (check) storm.
            design storm = 10
            minor storm = 2
            # Network flows: rational = tc accumulated through the pipes and Q = i(tc)·ΣCA (IDCM §4.2.7);
            # sum = add the sub-basin peaks (conservative).
            network flows = rational
            # Pipe travel velocity: full (Manning full = half full, IDCM §4.2.6(2)) or normal (normal depth at the pipe's Q).
            pipe velocity = full

            # Land use areas in acres as  Name: area; Name: area  (names from Table 4-1, or add rows under [Land uses]).
            # A basin with rows under [Flow paths] gets ti and tt from them; otherwise overland L (ft) and S (%) give ti
            # (Eq. 4-2-4). Travel time (min) is added to tt either way. Flow length (ft) is the watershed length for the
            # urban limit L/180 + 10 (Eq. 4-2-5); blank = the flow path length (or overland L).
            [Basins]
            Basin	Structure	Land use areas (ac)	Overland L (ft)	Overland S (%)	Travel time (min)	Flow length (ft)
            A1	{S(0)}	Single Family Residential: 2.4; Paved Streets: 0.6				
            A2	{S(1)}	Business/Commercial: 1.1; Lawns, Clay Soil: 0.3	80	1.5	2.5	600
            A3	{S(2)}	Paved Streets: 0.4	40	2.0	0	300

            # Flow path reaches in order, top of the basin down. Slopes in percent. Types and details:
            #   overland  Eq. 4-2-4 with the basin's C5 (details: C5: value to override)
            #   sheet     TR-55 Eq. 3-3 (n: sheet-flow n; P2: 2-yr 24-hr depth in., default from the IDF)
            #   shallow   TR-55 shallow concentrated (surface: paved or unpaved)
            #   gutter    IDCM Eq. 4-4-1 ÷ area (n: 0.016; Sx: cross slope %; T: spread ft)
            #   pipe      Manning flowing full (D: diameter in.; n)
            #   channel   Manning bank-full trapezoid (b: bottom ft; z: side slope H:V; d: depth ft; n)
            #   velocity  a known velocity (V: ft/s); slope not needed
            [Flow paths]
            Basin	Type	L (ft)	S (%)	Details
            A1	overland	100	2.0	
            A1	shallow	250	1.5	surface: unpaved
            A1	gutter	400	1.2	n: 0.016; Sx: 2; T: 6
            A1	pipe	150	0.8	D: 15; n: 0.013

            # Optional: your own IDF data instead of Table 4-2, either a table (values = intensity in./hr or depth in.)
            # with a column per storm, or  i = a / (t + b)^c  under [IDF equation]. Storms must be 2, 5, 10 or 100
            # (the storms with Table 4-1 coefficients).
            # [IDF]
            # values = intensity
            # Duration (min)	2-yr	10-yr	100-yr
            # [IDF equation]
            # Storm	a	b	c

            # Optional: extra or replacement land uses (impervious %, C for 2, 5, 10 and 100 years).
            [Land uses]
            Name	Impervious (%)	C2	C5	C10	C100
            """;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Rational method — Rapid City IDCM 2012 §4.2";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-RM-001", "The Rapid City rational method equations (4-2-4, 4-2-5) and Table 4-2 are in U.S. units (ft, ac, in./hr). Switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        bool urban = p.Flag("urban", true);
        int design = (int)p.Number("design storm", 10), minor = (int)p.Number("minor storm", 2);

        var idf = ReadIdf(p, msgs);
        if (idf is null) { msgs.InsertRange(0, p.Errors); return new ToolResult(title, [], [], msgs); }
        var storms = idf.Storms.ToArray();
        foreach (var (s, what) in new[] { (design, "design storm"), (minor, "minor storm") })
            if (!storms.Contains(s)) msgs.Add(CalcMessage.Error("E-RM-002", $"The {what} must be one of the IDF storms ({string.Join(", ", storms)}-yr); got {s}."));

        string mode = (p.Text("network flows") ?? "rational").ToLowerInvariant();
        if (mode is not ("rational" or "sum")) { msgs.Add(CalcMessage.Error("E-RM-016", $"network flows = \"{mode}\": use rational or sum.")); mode = "rational"; }
        string vel = (p.Text("pipe velocity") ?? "full").ToLowerInvariant();
        if (vel is not ("full" or "normal")) { msgs.Add(CalcMessage.Error("E-RM-016", $"pipe velocity = \"{vel}\": use full or normal.")); vel = "full"; }

        var uses = RapidCityRational.Table41.ToDictionary(u => Norm(u.Name), u => u);
        foreach (var r in p.Rows("Land uses"))
        {
            if (r.Length < 6 || string.IsNullOrWhiteSpace(r[0])) { msgs.Add(CalcMessage.Error("E-RM-003", $"Land use row \"{string.Join(", ", r)}\" needs a name, impervious % and C2, C5, C10, C100.")); continue; }
            var v = Enumerable.Range(1, 5).Select(i => p.Cell(r, i, "value", $"Land use {r[0]}")).ToArray();
            if (v.Any(x => x is null)) continue;
            if (!(v[0] >= 0 && v[0] <= 100)) { msgs.Add(CalcMessage.Error("E-RM-007", $"Land use \"{r[0]}\": impervious {v[0]:G4} % must be from 0 to 100.")); continue; }
            var badC = v.Skip(1).Select((x, i) => (x!.Value, Storm: StormsWithC[i])).Where(t => !(t.Value >= 0 && t.Value <= 1)).ToList();
            if (badC.Count > 0) { msgs.Add(CalcMessage.Error("E-RM-007", $"Land use \"{r[0]}\": runoff coefficients must be from 0 to 1 ({string.Join(", ", badC.Select(t => $"C{t.Storm} = {t.Value:G4}"))}).")); continue; }
            uses[Norm(r[0])] = new LandUse(r[0], v[0]!.Value, v[1]!.Value, v[2]!.Value, v[3]!.Value, v[4]!.Value);
        }

        // Flow path reaches, grouped by basin in input order.
        var paths = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Flow paths"))
        {
            if (r.Length < 2 || string.IsNullOrWhiteSpace(r[0])) { msgs.Add(CalcMessage.Error("E-RM-030", $"Flow path row \"{string.Join(", ", r)}\" needs a basin and a type.")); continue; }
            (paths.TryGetValue(r[0], out var l) ? l : paths[r[0]] = []).Add(r);
        }
        double? p2Default = storms.Contains(2) ? NullIfNaN(idf.Depth(2, 1440)) : null;

        var rows = new List<IReadOnlyList<object?>>();
        var segRows = new List<IReadOnlyList<object?>>();
        var runoff = new List<BasinRunoff>();
        var basins = p.Rows("Basins");
        if (basins.Count == 0) msgs.Add(CalcMessage.Error("E-RM-004", "No sub-basins: add rows under [Basins]."));
        double minTc = urban ? 5 : 10;
        foreach (var r in basins)
        {
            string name = r.Length > 0 ? r[0] : "?", where = $"Basin {name}";
            string structure = r.Length > 1 ? r[1] : "";
            var parts = new List<(LandUse Use, double Area)>();
            bool bad = false;
            foreach (var part in (r.Length > 2 ? r[2] : "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int colon = part.LastIndexOf(':');
                string use = colon > 0 ? part[..colon].Trim() : part;
                if (colon <= 0 || !ToolInput.TryNumber(part[(colon + 1)..], out double a) || !(a > 0))
                { msgs.Add(CalcMessage.Error("E-RM-005", $"{where}: \"{part}\" must be  land use: area (acres > 0).")); bad = true; continue; }
                if (!uses.TryGetValue(Norm(use), out var lu))
                { msgs.Add(CalcMessage.Error("E-RM-006", $"{where}: land use \"{use}\" is not in Table 4-1 or [Land uses].")); bad = true; continue; }
                parts.Add((lu, a));
            }
            double? l = p.Cell(r, 3, "overland L", where), sPct = p.Cell(r, 4, "overland S", where), ttIn = p.Cell(r, 5, "travel time", where), flowL = p.Cell(r, 6, "flow length", where);
            if (parts.Count == 0) { if (!bad) msgs.Add(CalcMessage.Error("E-RM-005", $"{where}: no land use areas.")); continue; }
            if (bad) continue;
            if (ttIn is < 0) { msgs.Add(CalcMessage.Error("E-RM-008", $"{where}: travel time cannot be negative.")); continue; }
            if (flowL is <= 0) { msgs.Add(CalcMessage.Error("E-RM-007", $"{where}: flow length must be greater than zero.")); continue; }
            double area = parts.Sum(x => x.Area);
            double C(int storm) => parts.Sum(x => x.Use.C(storm) * x.Area) / area;   // Eq. 4-2-2
            double imp = parts.Sum(x => x.Use.Impervious * x.Area) / area;
            double c5 = C(5);

            double ti, ttPath = 0, pathL;
            if (paths.Remove(name, out var reaches))
            {
                var segs = Segments(name, reaches, c5, p2Default, p, msgs);
                if (segs is null) continue;
                ti = segs.Where(s => s.IsInitial).Sum(s => s.Minutes);
                ttPath = segs.Where(s => !s.IsInitial).Sum(s => s.Minutes);
                pathL = segs.Sum(s => s.Length);
                foreach (var s in segs)
                    segRows.Add([s.Basin, s.Index, s.Type, s.Length, s.SlopePercent, s.Velocity, s.Minutes, s.IsInitial ? "ti" : "tt", s.Equation]);
                segRows.Add([$"{name} total", null, null, pathL, null, null, ti + ttPath, null, $"ti = {ti:F2} min, tt = {ttPath:F2} min"]);
            }
            else
            {
                if (l is not > 0 || sPct is not > 0) { msgs.Add(CalcMessage.Error("E-RM-007", $"{where}: overland length and slope must be greater than zero (or give the basin rows under [Flow paths]).")); continue; }
                ti = RapidCityRational.OverlandTime(c5, l.Value, sPct.Value);
                pathL = l.Value;
            }
            double tt = ttPath + (ttIn ?? 0), tc = ti + tt;
            string rule = "ti + tt";
            if (urban)
            {
                double cap = RapidCityRational.UrbanLimit(flowL ?? pathL);
                if (cap < tc) { tc = cap; rule = "L/180 + 10"; }
            }
            if (tc < minTc) { tc = minTc; rule = $"minimum {minTc:0} min"; }
            if (!(tc <= idf.MaxDuration)) { msgs.Add(CalcMessage.Error("E-RM-009", $"{where}: tc = {tc:F0} min is beyond the IDF data ({idf.MaxDuration:G6} min).")); continue; }
            if (tc < idf.MinDuration) { msgs.Add(CalcMessage.Error("E-RM-009", $"{where}: tc = {tc:F1} min is shorter than the IDF data ({idf.MinDuration:G6} min).")); continue; }
            if (area > 160) msgs.Add(CalcMessage.Warning("W-RM-001", $"{where}: {area:F1} ac exceeds 160 ac; the City must permit the rational method (IDCM §4.2.2(3))."));
            var row = new List<object?> { name, structure, area, imp, c5, ti, tt, tc, rule };
            var q = new Dictionary<int, double>(); var ca = new Dictionary<int, double>();
            foreach (int s in storms)
            {
                double i = idf.Intensity(s, tc), cc = C(s);
                ca[s] = cc * area; q[s] = cc * i * area;
                row.AddRange([cc, i, q[s]]);
            }
            rows.Add(row);
            runoff.Add(new BasinRunoff(name, structure, area, tc, ca, q));
        }
        foreach (var k in paths.Keys) msgs.Add(CalcMessage.Warning("W-RM-003", $"Flow path rows for \"{k}\" match no basin under [Basins]; they are not used."));
        msgs.InsertRange(0, p.Errors);

        var headers = new List<string> { "Basin", "Structure", "A (ac)", "Impervious (%)", "C5", "ti (min)", "tt (min)", "tc (min)", "tc from" };
        foreach (int s in storms) headers.AddRange([$"C{s}", $"i{s} (in/hr)", $"Q{s} (cfs)"]);
        var tables = new List<ToolTable>
        {
            new("Sub-basin peak flows", headers, rows,
            [
                "C composite by area (Eq. 4-2-2). ti = 1.87(1.1 − C5) L^0.5 S^−0.33, S in % (Eq. 4-2-4), or the overland/sheet reaches of the flow path. " + (urban ? "Urban: tc not more than L/180 + 10 (Eq. 4-2-5), at least 5 min." : "Non-urban: tc at least 10 min."),
                $"i from {idf.Source} at a duration of tc. Q = C i A (Eq. 4-2-1).",
            ]),
        };
        if (segRows.Count > 0)
            tables.Add(new("Time of concentration flow paths", ["Basin", "Reach", "Type", "L (ft)", "S (%)", "V (ft/s)", "t (min)", "Part", "Equation"], segRows,
            [
                "Overland: IDCM Eq. 4-2-4. Sheet: TR-55 Eq. 3-3, Tt = 0.007(nL)^0.8/(P2^0.5 s^0.4) h. Shallow concentrated: TR-55 App. F, V = 16.1345√s unpaved, 20.3282√s paved.",
                "Gutter: V = Q/A from IDCM Eq. 4-4-1 = (1.12/n) Sx^0.67 S^0.5 T^0.67. Pipe: Manning flowing full (= half full, IDCM §4.2.6(2)). Channel: Manning bank-full (§4.2.6(3)). t = L / V.",
            ]));

        var summary = new List<string>
        {
            $"{rows.Count} sub-basin(s); total area {rows.Sum(r => (double)r[2]!):F2} ac. Design storm {design}-yr, minor storm {minor}-yr.",
            $"IDF: {idf.Source}.",
        };

        // IDF table and chart
        var durs = (idf.Durations ?? [5, 10, 15, 30, 60, 120, 180, 360, 720, 1440]).Where(d => d >= idf.MinDuration - 1e-9 && d <= idf.MaxDuration + 1e-9).ToList();
        if (idf.Durations is null && !durs.Contains(idf.MinDuration)) durs.Insert(0, idf.MinDuration);
        var idfHeaders = new List<string> { "Duration (min)" };
        idfHeaders.AddRange(storms.Select(s => $"i{s} (in/hr)"));
        idfHeaders.AddRange(storms.Select(s => $"P{s} (in.)"));
        tables.Add(new("IDF intensity at each duration", idfHeaders,
            durs.Select(d => (IReadOnlyList<object?>)new object?[] { d }.Concat(storms.Select(s => (object?)idf.Intensity(s, d))).Concat(storms.Select(s => (object?)idf.Depth(s, d))).ToList()).ToList()));
        var charts = new List<ToolChart>();
        if (durs.Count > 1)
        {
            double lo = Math.Log(durs[0]), hi = Math.Log(durs[^1]);
            var xs = Enumerable.Range(0, 61).Select(k => Math.Exp(lo + (hi - lo) * k / 60)).ToList();
            var series = storms.Select(s => new ChartSeries($"{s}-yr", xs.Select(x => (x, idf.Intensity(s, x))).Where(t => t.Item2 > 0).ToList())).ToList();
            var marks = runoff.Where(b => storms.Contains(design)).Select(b => (b.Tc, idf.Intensity(design, b.Tc))).ToList();
            if (marks.Count > 0) series.Add(new ChartSeries($"sub-basin tc ({design}-yr)", marks, MarkersOnly: true));
            charts.Add(new ToolChart("IDF curves", "Duration (min)", "Intensity (in./hr)", series) { LogX = true, LogY = true });
        }

        var result = new ToolResult(title, summary, tables, msgs) { Charts = charts };
        if (context.Network is not { } net || !result.Succeeded || runoff.Count == 0) return result;
        var flows = runoff.Where(b => !string.IsNullOrWhiteSpace(b.Structure)).GroupBy(b => b.Structure, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Design: g.Sum(b => b.Q[design]), Minor: g.Sum(b => b.Q[minor])), StringComparer.OrdinalIgnoreCase);
        if (flows.Count == 0) return result;
        if (net.Units != UnitSystem.USCustomary)
        {
            msgs.Add(CalcMessage.Warning("W-RM-004", $"Network \"{net.Name}\" is in SI units; flows are not applied (the rational method here is in U.S. units)."));
            return result;
        }
        var names = net.Structures.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = flows.Keys.Where(k => !names.Contains(k)).ToList();
        if (missing.Count > 0) msgs.Add(CalcMessage.Warning("W-RM-002", $"Structures not in network \"{net.Name}\": {string.Join(", ", missing)}. Their flows are not applied."));
        int n = flows.Keys.Count(names.Contains);
        var structures = net.Structures.Select(s => flows.TryGetValue(s.Name, out var f) ? s with { Inflow = Math.Round(f.Design, 3), MinorInflow = Math.Round(f.Minor, 3) } : s).ToList();
        int typed = net.Pipes.Count(x => double.IsFinite(x.Discharge));

        if (mode == "sum")
        {
            var updatedSum = net with
            {
                Structures = structures,
                Pipes = net.Pipes.Select(x => x with { Discharge = double.NaN, MinorDischarge = double.NaN }).ToList(),
                MinorStormName = $"{minor}-yr",
            };
            msgs.Add(CalcMessage.Info("I-RM-001", $"network flows = sum: several basins at one structure are added (Q1 + Q2), and each pipe carries the sum of the inflows upstream (D-047). This is conservative; network flows = rational accumulates tc instead."));
            if (typed > 0) msgs.Add(CalcMessage.Info("I-RM-003", $"{typed} pipe(s) had a typed Q; applying clears it so the pipes carry the accumulated inflows."));
            return result with
            {
                UpdatedNetwork = updatedSum,
                UpdateDescription = $"Set the {design}-yr Q as the inflow and the {minor}-yr Q as the minor inflow at {n} structure(s) of \"{net.Name}\"; pipes accumulate them.",
            };
        }

        // rational routing through the network, design and minor storms
        var routedNet = net with { Structures = structures };
        var noOutlet = flows.Keys.Where(k => names.Contains(k) && !net.Pipes.Any(x => x.From.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();
        if (noOutlet.Count > 0) msgs.Add(CalcMessage.Warning("W-RM-008", $"Sub-basins at {string.Join(", ", noOutlet)} drain to a structure with no outlet pipe; they are not in any pipe flow."));
        var routed = new Dictionary<int, IReadOnlyList<RoutedPipe>>();
        foreach (var (s, isMinor) in new[] { (design, false), (minor, true) }.DistinctBy(t => t.Item1))
        {
            var (pipes, rm) = RationalRouting.Route(routedNet, runoff.Where(b => names.Contains(b.Structure)).ToList(), idf, s, isMinor && s != design, vel == "normal");
            msgs.AddRange(rm.Where(m => !msgs.Any(x => x.Text == m.Text)));
            if (rm.Any(m => m.Severity == MessageSeverity.Error)) return result with { Messages = msgs };
            routed[s] = pipes;
            tables.Add(new($"Rational method through the network — {s}-yr", ["Pipe", "From", "To", "ΣA (ac)", "ΣCA (ac)", "tc (min)", "Longest path", "i (in/hr)", "i·ΣCA (cfs)", "Other inflow (cfs)", "Q (cfs)", "Q from", "V (ft/s)", "V basis", "Travel (min)"],
                pipes.Select(x => (IReadOnlyList<object?>)[x.Pipe, x.From, x.To, x.Area, x.CA, x.Tc, x.CriticalPath, x.Intensity, x.RationalQ, x.OtherInflow, x.Q, x.Governs, x.Velocity, x.VelocityBasis, x.TravelMinutes]).ToList(),
                [
                    "tc at a pipe = the longest of the sub-basin tc at its upstream structure and (tc + travel time) of each pipe entering it (IDCM §4.2.7). Q = i(tc)·ΣCA + other inflow.",
                    "Q is never less than the peak of a single sub-basin or upstream pipe draining to it (IDCM §4.2.7: a part of the basin can give a larger peak). Travel = L / V / 60.",
                ]));
        }
        var other = routed[design].Where(x => x.OtherInflow > 0 && !routed[design].Any(u => u.To == x.From && u.OtherInflow >= x.OtherInflow)).ToList();
        if (other.Count > 0) msgs.Add(CalcMessage.Info("I-RM-004", $"Inflow entered at structures with no sub-basin ({string.Join(", ", other.Select(x => x.From).Distinct())}) is added downstream unchanged, as other inflow."));
        var updated = routedNet with
        {
            Pipes = net.Pipes.Select(x => x with
            {
                Discharge = Math.Round(routed[design].Single(r => r.Pipe == x.Name).Q, 3),
                MinorDischarge = Math.Round(routed[minor].Single(r => r.Pipe == x.Name).Q, 3),
            }).ToList(),
            MinorStormName = $"{minor}-yr",
        };
        if (typed > 0) msgs.Add(CalcMessage.Info("I-RM-003", $"{typed} pipe(s) had a typed Q; applying replaces it with the routed Q."));
        summary.Add($"Network \"{net.Name}\": tc accumulated through {net.Pipes.Count} pipe(s) with {(vel == "full" ? "full-flow" : "normal-depth")} velocities. Outfall Q{design} = {string.Join(", ", routed[design].Where(x => !net.Pipes.Any(o => o.From.Equals(x.To, StringComparison.OrdinalIgnoreCase))).Select(x => $"{x.Q:F2} cfs ({x.Pipe})"))}.");
        return result with
        {
            Messages = msgs,
            UpdatedNetwork = updated,
            UpdateDescription = $"Set the routed {design}-yr Q (design) and {minor}-yr Q (minor) in all {net.Pipes.Count} pipe(s) of \"{net.Name}\", and the sub-basin Q as inflows at {n} structure(s).",
        };
    }

    private static double? NullIfNaN(double v) => double.IsFinite(v) ? v : null;

    /// <summary>Reads [IDF] or [IDF equation]; Table 4-2 when neither is given. Null after an error.</summary>
    private static IdfCurve? ReadIdf(ToolInput p, List<CalcMessage> msgs)
    {
        bool table = p.Rows("IDF").Count > 0, eq = p.Rows("IDF equation").Count > 0;
        if (table && eq) { msgs.Add(CalcMessage.Error("E-RM-010", "Give either an [IDF] table or an [IDF equation], not both.")); return null; }
        if (!table && !eq) return IdfCurve.RapidCity;
        if (table)
        {
            var header = p.Header("IDF");
            var storms = new List<int>();
            for (int k = 1; k < header.Count; k++)
            {
                var digits = new string(header[k].Where(char.IsDigit).ToArray());
                if (!int.TryParse(digits, out int s)) { msgs.Add(CalcMessage.Error("E-RM-011", $"[IDF] column \"{header[k]}\" must name a storm, e.g. 10-yr.")); return null; }
                storms.Add(s);
            }
            if (!CheckStorms(storms, msgs)) return null;
            string kind = (p.Text("IDF.values") ?? "intensity").ToLowerInvariant();
            if (kind is not ("intensity" or "depth")) { msgs.Add(CalcMessage.Error("E-RM-011", $"[IDF] values = \"{kind}\": use intensity (in./hr) or depth (in.).")); return null; }
            var durs = new List<double>(); var vals = storms.ToDictionary(s => s, _ => new List<double>());
            foreach (var r in p.Rows("IDF"))
            {
                double? d = p.Cell(r, 0, "duration", "[IDF]");
                if (d is not > 0) { msgs.Add(CalcMessage.Error("E-RM-012", $"[IDF] row \"{string.Join(", ", r)}\": duration must be greater than zero.")); return null; }
                if (durs.Count > 0 && !(d > durs[^1])) { msgs.Add(CalcMessage.Error("E-RM-012", $"[IDF] durations must increase down the table ({d:G6} after {durs[^1]:G6}).")); return null; }
                durs.Add(d.Value);
                for (int k = 0; k < storms.Count; k++)
                {
                    double? v = p.Cell(r, k + 1, kind, $"[IDF] {d:G6} min");
                    if (v is not > 0) { msgs.Add(CalcMessage.Error("E-RM-012", $"[IDF] {d:G6} min, {storms[k]}-yr: {kind} must be greater than zero.")); return null; }
                    vals[storms[k]].Add(v.Value);
                }
            }
            var curve = IdfCurve.FromTable($"[IDF] table of {kind}s (log-log interpolation)", durs, vals.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()), kind == "depth");
            CheckShape(curve, msgs);
            return curve;
        }
        var co = new Dictionary<int, (double, double, double)>();
        foreach (var r in p.Rows("IDF equation"))
        {
            var v = Enumerable.Range(0, 4).Select(k => p.Cell(r, k, "value", "[IDF equation]")).ToArray();
            if (v.Any(x => x is null)) { msgs.Add(CalcMessage.Error("E-RM-013", $"[IDF equation] row \"{string.Join(", ", r)}\" needs storm, a, b and c.")); return null; }
            int s = (int)v[0]!.Value;
            if (!(v[1] > 0) || !(v[2] >= 0) || !(v[3] > 0)) { msgs.Add(CalcMessage.Error("E-RM-013", $"[IDF equation] {s}-yr: a and c must be greater than zero and b zero or more.")); return null; }
            if (!co.TryAdd(s, (v[1]!.Value, v[2]!.Value, v[3]!.Value))) { msgs.Add(CalcMessage.Error("E-RM-013", $"[IDF equation] lists the {s}-yr storm twice.")); return null; }
        }
        if (!CheckStorms(co.Keys.ToList(), msgs)) return null;
        double min = p.Number("IDF equation.min duration", 5), max = p.Number("IDF equation.max duration", 1440);
        if (!(min > 0) || !(max > min)) { msgs.Add(CalcMessage.Error("E-RM-013", "[IDF equation] min duration must be greater than zero and max duration greater than min.")); return null; }
        var c = IdfCurve.FromEquation($"i = a/(t + b)^c ({string.Join("; ", co.OrderBy(k => k.Key).Select(k => $"{k.Key}-yr: a = {k.Value.Item1:G6}, b = {k.Value.Item2:G6}, c = {k.Value.Item3:G6}"))}), {min:G6}–{max:G6} min", co, min, max);
        CheckShape(c, msgs);
        return c;
    }

    private static bool CheckStorms(List<int> storms, List<CalcMessage> msgs)
    {
        if (storms.Count == 0) { msgs.Add(CalcMessage.Error("E-RM-011", "The IDF data has no storms.")); return false; }
        var bad = storms.Where(s => !StormsWithC.Contains(s)).ToList();
        if (bad.Count > 0) { msgs.Add(CalcMessage.Error("E-RM-011", $"IDF storm(s) {string.Join(", ", bad)}-yr have no Table 4-1 runoff coefficients; use 2, 5, 10 or 100.")); return false; }
        if (storms.Distinct().Count() != storms.Count) { msgs.Add(CalcMessage.Error("E-RM-011", "The IDF data lists a storm twice.")); return false; }
        return true;
    }

    /// <summary>Warns when intensity rises with duration or falls with return period (likely a typing error).</summary>
    private static void CheckShape(IdfCurve idf, List<CalcMessage> msgs)
    {
        var durs = idf.Durations ?? Enumerable.Range(0, 21).Select(k => idf.MinDuration * Math.Pow(idf.MaxDuration / idf.MinDuration, k / 20.0)).ToList();
        foreach (int s in idf.Storms)
            for (int k = 1; k < durs.Count; k++)
                if (idf.Intensity(s, durs[k]) > idf.Intensity(s, durs[k - 1]) * (1 + 1e-9))
                { msgs.Add(CalcMessage.Warning("W-RM-005", $"IDF {s}-yr: intensity rises from {durs[k - 1]:G6} to {durs[k]:G6} min. Check the data.")); break; }
        for (int k = 1; k < idf.Storms.Count; k++)
            if (durs.Any(d => idf.Intensity(idf.Storms[k], d) < idf.Intensity(idf.Storms[k - 1], d)))
                msgs.Add(CalcMessage.Warning("W-RM-005", $"IDF: the {idf.Storms[k]}-yr intensity is below the {idf.Storms[k - 1]}-yr intensity at some durations. Check the data."));
    }

    /// <summary>Travel times of a basin's flow path reaches; null after an error.</summary>
    private static List<TcSegment>? Segments(string basin, List<string[]> reaches, double c5, double? p2Default, ToolInput p, List<CalcMessage> msgs)
    {
        var list = new List<TcSegment>();
        bool ok = true, travelSeen = false;
        int index = 0;
        foreach (var r in reaches)
        {
            index++;
            string where = $"Basin {basin} reach {index}";
            string type = (r.Length > 1 ? r[1] : "").Trim().ToLowerInvariant();
            double? l = p.Cell(r, 2, "length", where), sPct = p.Cell(r, 3, "slope", where);
            var det = Details(r.Length > 4 ? r[4] : "", where, msgs);
            if (det is null) { ok = false; continue; }
            if (type.StartsWith("shallow")) { if (type.Contains("unpaved")) det.TryAdd("surface", "unpaved"); else if (type.Contains("paved")) det.TryAdd("surface", "paved"); type = "shallow"; }
            if (l is not > 0) { msgs.Add(CalcMessage.Error("E-RM-031", $"{where}: length must be greater than zero.")); ok = false; continue; }
            if (type != "velocity" && sPct is not > 0) { msgs.Add(CalcMessage.Error("E-RM-031", $"{where}: slope must be greater than zero (percent).")); ok = false; continue; }
            double s = (sPct ?? double.NaN) / 100;
            double? Num(string key, bool required = true)
            {
                if (!det.TryGetValue(key, out var t)) { if (required) { msgs.Add(CalcMessage.Error("E-RM-032", $"{where} ({type}): give {key}: value in Details.")); ok = false; } return null; }
                if (!ToolInput.TryNumber(t, out double v) || !(v > 0)) { msgs.Add(CalcMessage.Error("E-RM-032", $"{where} ({type}): {key} \"{t}\" must be a number greater than zero.")); ok = false; return null; }
                return v;
            }
            double? NumZero(string key)
            {
                var t = det[key];
                if (!ToolInput.TryNumber(t, out double v) || !(v >= 0)) { msgs.Add(CalcMessage.Error("E-RM-032", $"{where} ({type}): {key} \"{t}\" must be a number, zero or more.")); ok = false; return null; }
                return v;
            }
            double v = double.NaN, t = double.NaN; bool initial = false; string eq;
            switch (type)
            {
                case "overland":
                {
                    double cc = det.ContainsKey("c5") ? Num("c5") ?? double.NaN : c5;
                    if (det.ContainsKey("c5") && !(cc <= 1)) { msgs.Add(CalcMessage.Error("E-RM-032", $"{where}: C5 must be from 0 to 1.")); ok = false; continue; }
                    if (!double.IsFinite(cc)) continue;
                    t = RapidCityRational.OverlandTime(cc, l.Value, sPct!.Value); v = l.Value / (60 * t); initial = true;
                    eq = $"IDCM 4-2-4, C5 = {cc:F3}";
                    break;
                }
                case "sheet":
                {
                    double? n = Num("n"), p2 = det.ContainsKey("p2") ? Num("p2") : p2Default;
                    if (p2 is null && !det.ContainsKey("p2")) { msgs.Add(CalcMessage.Error("E-RM-032", $"{where} (sheet): give P2: 2-yr 24-hr depth (in.); the IDF data has no 2-yr 24-hr value.")); ok = false; }
                    if (n is null || p2 is null) continue;
                    if (l > 300) msgs.Add(CalcMessage.Warning("W-RM-006", $"{where}: sheet flow over {l:G4} ft exceeds the TR-55 limit of 300 ft (NRCS NEH 630 Ch. 15 suggests 100 ft)."));
                    else if (l > 100) msgs.Add(CalcMessage.Info("I-RM-006", $"{where}: sheet flow over {l:G4} ft; NRCS NEH 630 Ch. 15 (2010) suggests at most 100 ft."));
                    t = TcFlowPath.SheetFlowMinutes(n.Value, l.Value, p2.Value, s); v = l.Value / (60 * t); initial = true;
                    eq = $"TR-55 3-3, n = {n:G4}, P2 = {p2:G4} in.";
                    break;
                }
                case "shallow":
                {
                    string surf = det.GetValueOrDefault("surface", "").ToLowerInvariant();
                    if (surf is not ("paved" or "unpaved")) { msgs.Add(CalcMessage.Error("E-RM-032", $"{where} (shallow): give surface: paved or surface: unpaved.")); ok = false; continue; }
                    v = TcFlowPath.ShallowConcentratedVelocity(surf == "paved", s); eq = $"TR-55 App. F, {surf}";
                    break;
                }
                case "gutter":
                {
                    double? n = Num("n"), sx = Num("sx"), tw = Num("t");
                    if (n is null || sx is null || tw is null) continue;
                    v = TcFlowPath.GutterVelocity(n.Value, s, sx.Value / 100, tw.Value); eq = $"IDCM 4-4-1 ÷ A, n = {n:G4}, Sx = {sx:G4} %, T = {tw:G4} ft";
                    break;
                }
                case "pipe":
                {
                    double? d = Num("d"), n = Num("n");
                    if (d is null || n is null) continue;
                    v = TcFlowPath.PipeFullVelocity(n.Value, d.Value / 12, s); eq = $"Manning full, D = {d:G4} in., n = {n:G4}";
                    break;
                }
                case "channel":
                {
                    double? b = det.ContainsKey("b") ? NumZero("b") : 0, z = det.ContainsKey("z") ? NumZero("z") : 0, d = Num("d"), n = Num("n");
                    if (b is null || z is null || d is null || n is null) continue;
                    if (b == 0 && z == 0) { msgs.Add(CalcMessage.Error("E-RM-032", $"{where} (channel): b and z cannot both be zero.")); ok = false; continue; }
                    v = TcFlowPath.ChannelVelocity(n.Value, b.Value, z.Value, d.Value, s); eq = $"Manning bank-full, b = {b:G4} ft, z = {z:G4}, d = {d:G4} ft, n = {n:G4}";
                    break;
                }
                case "velocity":
                {
                    double? vv = Num("v");
                    if (vv is null) continue;
                    v = vv.Value; eq = "entered velocity";
                    break;
                }
                default:
                    msgs.Add(CalcMessage.Error("E-RM-033", $"{where}: type \"{(r.Length > 1 ? r[1] : "")}\" is not overland, sheet, shallow, gutter, pipe, channel or velocity."));
                    ok = false; continue;
            }
            if (!initial) t = l.Value / v / 60;
            if (initial && travelSeen) msgs.Add(CalcMessage.Warning("W-RM-007", $"{where}: {type} flow follows a concentrated-flow reach; overland and sheet flow normally start the path."));
            if (!initial) travelSeen = true;
            if (!double.IsFinite(t) || !(t > 0)) { msgs.Add(CalcMessage.Error("E-RM-034", $"{where}: travel time could not be computed.")); ok = false; continue; }
            list.Add(new TcSegment(basin, index, type, l.Value, sPct ?? double.NaN, v, t, initial, eq));
        }
        return ok ? list : null;
    }

    private static Dictionary<string, string>? Details(string text, string where, List<CalcMessage> msgs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0) { msgs.Add(CalcMessage.Error("E-RM-032", $"{where}: detail \"{part}\" must be  name: value.")); return null; }
            d[part[..colon].Trim().ToLowerInvariant()] = part[(colon + 1)..].Trim();
        }
        return d;
    }

    private static string Norm(string s) => new string(s.Replace("½", "1/2").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant().Replace("12acre", "halfacre").Replace("½acre", "halfacre");
}
