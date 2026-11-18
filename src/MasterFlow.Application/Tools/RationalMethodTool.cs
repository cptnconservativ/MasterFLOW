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
    public string Description => "Peak flows Q = CiA for each sub-basin and the 2-, 10- and 100-year storms: composite C from Table 4-1, tc from Eq. 4-2-3 to 4-2-5, intensity from the Table 4-2 depths. Can write the design and minor-storm flows into the selected storm network as structure inflows.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context)
    {
        var names = context.Network?.Structures.Where(s => !s.Kind.Contains("out", StringComparison.OrdinalIgnoreCase)).Select(s => s.Name).Take(3).ToList() ?? [];
        string S(int i) => i < names.Count ? names[i] : $"CB-{i + 1}";
        return $"""
            # Rapid City rational method (IDCM 2012 §4.2). U.S. units: acres, feet, minutes.
            urban = yes
            # Storm whose Q becomes each structure's inflow, and the minor (check) storm.
            design storm = 10
            minor storm = 2

            # Land use areas in acres as  Name: area; Name: area  (names from Table 4-1, or add rows under [Land uses]).
            # Overland L (ft) and S (%) give ti (Eq. 4-2-4); travel time tt (min) is gutter/pipe/channel time.
            # Flow length (ft) is the watershed length for the urban limit L/180 + 10 (Eq. 4-2-5); blank = overland L.
            [Basins]
            Basin	Structure	Land use areas (ac)	Overland L (ft)	Overland S (%)	Travel time (min)	Flow length (ft)
            A1	{S(0)}	Single Family Residential: 2.4; Paved Streets: 0.6	120	2.0	4.0	900
            A2	{S(1)}	Business/Commercial: 1.1; Lawns, Clay Soil: 0.3	80	1.5	2.5	600
            A3	{S(2)}	Paved Streets: 0.4	40	2.0	0	300

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
        int[] storms = [2, 10, 100];
        foreach (var (s, what) in new[] { (design, "design storm"), (minor, "minor storm") })
            if (!storms.Contains(s)) msgs.Add(CalcMessage.Error("E-RM-002", $"The {what} must be 2, 10 or 100 (the Table 4-2 storms); got {s}."));

        var uses = RapidCityRational.Table41.ToDictionary(u => Norm(u.Name), u => u);
        foreach (var r in p.Rows("Land uses"))
        {
            if (r.Length < 6 || string.IsNullOrWhiteSpace(r[0])) { msgs.Add(CalcMessage.Error("E-RM-003", $"Land use row \"{string.Join(", ", r)}\" needs a name, impervious % and C2, C5, C10, C100.")); continue; }
            var v = Enumerable.Range(1, 5).Select(i => p.Cell(r, i, "value", $"Land use {r[0]}")).ToArray();
            if (v.Any(x => x is null)) continue;
            uses[Norm(r[0])] = new LandUse(r[0], v[0]!.Value, v[1]!.Value, v[2]!.Value, v[3]!.Value, v[4]!.Value);
        }

        var rows = new List<IReadOnlyList<object?>>();
        var flows = new Dictionary<string, (double Design, double Minor)>(StringComparer.OrdinalIgnoreCase);
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
            double? l = p.Cell(r, 3, "overland L", where), sPct = p.Cell(r, 4, "overland S", where), tt = p.Cell(r, 5, "travel time", where), flowL = p.Cell(r, 6, "flow length", where);
            if (parts.Count == 0) { if (!bad) msgs.Add(CalcMessage.Error("E-RM-005", $"{where}: no land use areas.")); continue; }
            if (bad) continue;
            if (l is not > 0 || sPct is not > 0) { msgs.Add(CalcMessage.Error("E-RM-007", $"{where}: overland length and slope must be greater than zero.")); continue; }
            if (tt is < 0) { msgs.Add(CalcMessage.Error("E-RM-008", $"{where}: travel time cannot be negative.")); continue; }
            double area = parts.Sum(x => x.Area);
            double C(int storm) => parts.Sum(x => x.Use.C(storm) * x.Area) / area;   // Eq. 4-2-2
            double imp = parts.Sum(x => x.Use.Impervious * x.Area) / area;
            double c5 = C(5);
            double ti = RapidCityRational.OverlandTime(c5, l.Value, sPct.Value), tsum = ti + (tt ?? 0), tc = tsum;
            string rule = "ti + tt";
            if (urban)
            {
                double cap = RapidCityRational.UrbanLimit(flowL ?? l.Value);
                if (cap < tc) { tc = cap; rule = "L/180 + 10"; }
            }
            if (tc < minTc) { tc = minTc; rule = $"minimum {minTc:0} min"; }
            if (tc > 1440) { msgs.Add(CalcMessage.Error("E-RM-009", $"{where}: tc = {tc:F0} min is beyond the 24-hour Table 4-2 data.")); continue; }
            if (area > 160) msgs.Add(CalcMessage.Warning("W-RM-001", $"{where}: {area:F1} ac exceeds 160 ac; the City must permit the rational method (IDCM §4.2.2(3))."));
            var row = new List<object?> { name, structure, area, imp, c5, ti, tt ?? 0, tc, rule };
            var q = new Dictionary<int, double>();
            foreach (int s in storms)
            {
                double i = RapidCityRational.Intensity(s, tc), c = C(s), qq = c * i * area;
                q[s] = qq;
                row.AddRange([c, i, qq]);
            }
            rows.Add(row);
            if (!string.IsNullOrWhiteSpace(structure))
            {
                var prev = flows.GetValueOrDefault(structure);
                flows[structure] = (prev.Design + q.GetValueOrDefault(design), prev.Minor + q.GetValueOrDefault(minor));
            }
        }
        msgs.InsertRange(0, p.Errors);
        var headers = new List<string> { "Basin", "Structure", "A (ac)", "Impervious (%)", "C5", "ti (min)", "tt (min)", "tc (min)", "tc from" };
        foreach (int s in storms) headers.AddRange([$"C{s}", $"i{s} (in/hr)", $"Q{s} (cfs)"]);
        var tables = new List<ToolTable>
        {
            new("Sub-basin peak flows", headers, rows,
            [
                "C composite by area (Eq. 4-2-2). ti = 1.87(1.1 − C5) L^0.5 S^−0.33, S in % (Eq. 4-2-4). " + (urban ? "Urban: tc not more than L/180 + 10 (Eq. 4-2-5), at least 5 min." : "Non-urban: tc at least 10 min."),
                "i = Table 4-2 depth at a duration of tc (log-log interpolation between tabulated durations) ÷ tc. Q = C i A (Eq. 4-2-1).",
            ]),
            new("Table 4-2 intensity at each duration (in/hr)", ["Duration (min)", "i2", "i10", "i100"],
                RapidCityRational.Durations.Select(d => (IReadOnlyList<object?>)[d, RapidCityRational.Intensity(2, d), RapidCityRational.Intensity(10, d), RapidCityRational.Intensity(100, d)]).ToList()),
        };
        var summary = new List<string> { $"{rows.Count} sub-basin(s); total area {rows.Sum(r => (double)r[2]!):F2} ac. Design storm {design}-yr, minor storm {minor}-yr." };

        var result = new ToolResult(title, summary, tables, msgs);
        if (context.Network is { } net && flows.Count > 0 && result.Succeeded)
        {
            var names = net.Structures.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = flows.Keys.Where(k => !names.Contains(k)).ToList();
            if (missing.Count > 0) msgs.Add(CalcMessage.Warning("W-RM-002", $"Structures not in network \"{net.Name}\": {string.Join(", ", missing)}. Their flows are not applied."));
            var updated = net with
            {
                Structures = net.Structures.Select(s => flows.TryGetValue(s.Name, out var f) ? s with { Inflow = Math.Round(f.Design, 3), MinorInflow = Math.Round(f.Minor, 3) } : s).ToList(),
                MinorStormName = $"{minor}-yr",
            };
            int n = flows.Keys.Count(names.Contains);
            msgs.Add(CalcMessage.Info("I-RM-001", $"Several basins at one structure are added (Q1 + Q2), which is conservative; a combined-basin tc would give less. Pipe flows downstream are the sum of inflows (D-047)."));
            return result with
            {
                UpdatedNetwork = updated,
                UpdateDescription = $"Set the {design}-yr Q as the inflow and the {minor}-yr Q as the minor inflow at {n} structure(s) of \"{net.Name}\".",
            };
        }
        return result;
    }

    private static string Norm(string s) => new string(s.Replace("½", "1/2").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant().Replace("12acre", "halfacre").Replace("½acre", "halfacre");
}
