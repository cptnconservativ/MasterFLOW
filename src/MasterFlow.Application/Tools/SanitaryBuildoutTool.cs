using System.Globalization;
using System.Text;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Build-out projection and phasing of a sanitary sewer network: existing loads plus growth by basin (manhole) and
/// year, run year by year through the sanitary sewer design (<see cref="SanitarySewerTool"/>, Rapid City IDCM 2012
/// §3.12 flows with the peaking and reduction factors recomputed for each year's population and acres). For each
/// sewer it gives the year its peak-flow depth reaches the d/D limit (0.70) and the year it surcharges, the size the
/// build-out flow needs on the same slope, and whether to build the build-out size now or build the planned size and
/// relieve it later, by a decision horizon.
/// </summary>
public sealed class SanitaryBuildoutTool : IDesignTool
{
    public string Id => "sanitary-buildout";
    public string Name => "Sanitary sewer build-out projection and phasing";
    public string Description => "Projects sanitary flows from existing loads plus growth by basin and year (Rapid City IDCM 2012 §3.12 flows, peaking and reduction factors recomputed each year), and finds the year each sewer reaches d/D 0.70 at peak flow and the year it surcharges. Gives the size the build-out flow needs on the same slope and recommends building that size now or building the planned size and relieving it later.";
    public bool UsesNetwork => true;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double GpmPerCfs = 448.831;

    public string SampleInput(DesignToolContext context) => """
        # Sanitary sewer build-out projection and phasing. U.S. units: diameters in inches, lengths and elevations in ft, flows in gpm.
        # Keys and the [Manholes] and [Pipes] tables are as in "Sanitary sewer network design"; give the planned sizes and inverts.
        # [Loads] are today's loads. [Growth] rows add load from the Start year, rising linearly to the full amount at the End year
        # (End blank = all at once in the Start year).
        criteria = Rapid City
        base year = 2026
        build-out year = 2056
        decision horizon = 20          # years: a sewer reaching the limit sooner is built at the build-out size now
        report every = 5               # years between columns of the d/D table
        infiltration = 200             # gpd per inch-diameter per mile

        [Manholes]
        Name	Rim	Deflection (deg)
        MH-20	3462.10	0
        MH-21	3458.40	0
        MH-22	3455.20	30
        MH-23	3452.60	0
        MH-24	3449.90	0

        [Loads]
        Manhole	Land use	Area (ac)	DU/ac	Population	Flow (gpm)	PF
        MH-20	Residential	60	3.5
        MH-22	Commercial	12

        [Growth]
        Manhole	Land use	Area (ac)	DU/ac	Population	Flow (gpm)	PF	Start year	End year
        MH-20	Residential	90	3.5				2027	2040
        MH-21	Residential	150	4.0				2032	2050
        MH-22	Multi-family	25	14				2035
        MH-23	Commercial	40					2030	2045
        MH-23	Flow				120	1.5	2045

        [Pipes]
        Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n
        B-1	MH-20	MH-21	450	10	3452.00	3450.80	0.013
        B-2	MH-21	MH-22	460	15	3450.60	3449.75	0.013
        B-3	MH-22	MH-23	440	15	3449.50	3448.95	0.013
        B-4	MH-23	MH-24	470	18	3448.80	3448.20	0.013
        """;

    private sealed record GrowthLoad(string[] Cells, double Start, double End);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sanitary sewer build-out projection and phasing";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-BO-001", "The build-out tool works in U.S. units (gpm, in., ft), as the Rapid City IDCM and Ten States Standards are written; switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double baseYear = p.Number("base year", DateTime.Today.Year), endYear = p.Number("build-out year", baseYear + 30);
        double horizon = p.Number("decision horizon", 20), every = p.Number("report every", 5), maxDd = p.Number("max d/D", 0.70);
        if (Math.Abs(baseYear - Math.Round(baseYear)) > 1e-9 || Math.Abs(endYear - Math.Round(endYear)) > 1e-9) msgs.Add(CalcMessage.Error("E-BO-002", "base year and build-out year must be whole years."));
        if (!(endYear > baseYear)) msgs.Add(CalcMessage.Error("E-BO-002", "The build-out year must be after the base year."));
        if (endYear - baseYear > 200) msgs.Add(CalcMessage.Error("E-BO-002", "The projection is limited to 200 years."));
        if (!(horizon >= 0)) msgs.Add(CalcMessage.Error("E-BO-002", "decision horizon cannot be negative."));
        if (!(every >= 1)) msgs.Add(CalcMessage.Error("E-BO-002", "report every must be at least 1 year."));

        // ---- [Loads] (existing) and [Growth], both in the sanitary tool's load columns ----
        var loadHeader = p.Header("Loads");
        var growthHeader = p.Header("Growth");
        var cols = new[] { "area", "du", "pop", "flow", "pf" };
        int[] lMap = Map(loadHeader, 2), gMap = Map(growthHeader, 2);
        // Start and End year columns in [Growth]: by header words, else the 8th and 9th columns.
        int cStart = Find(growthHeader, "start", 7), cEnd = Find(growthHeader, "end", 8);
        var existing = p.Rows("Loads").Select(r => Normalize(r, lMap)).ToList();
        var growth = new List<GrowthLoad>();
        foreach (var r in p.Rows("Growth"))
        {
            string label = r.Length > 0 && r[0].Length > 0 ? $"Growth at {r[0]}" : "Growth";
            double? s = cStart >= 0 ? p.Cell(r, cStart, "start year", label) : null, e = cEnd >= 0 ? p.Cell(r, cEnd, "end year", label) : null;
            if (s is null) { msgs.Add(CalcMessage.Error("E-BO-003", $"{label}: give the Start year.")); continue; }
            if (e is { } ev && ev < s) { msgs.Add(CalcMessage.Error("E-BO-003", $"{label}: the End year is before the Start year.")); continue; }
            growth.Add(new GrowthLoad(Normalize(r, gMap), s.Value, e ?? s.Value));
        }
        if (growth.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error))
            msgs.Add(CalcMessage.Error("E-BO-004", "Give the growth under [Growth] (Manhole, Land use, Area, DU/ac, Population, Flow, PF, Start year, End year)."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        // Planned sizes and inverts are needed: they are what is phased.
        var net = context.Network;
        bool fromNetwork = !p.HasSection("Pipes") && net is { Pipes.Count: > 0 };
        if (!fromNetwork)
            foreach (var r in p.Rows("Pipes"))
                if (r.Length < 7 || r[4].Length == 0 || r[5].Length == 0 || r[6].Length == 0)
                    msgs.Add(CalcMessage.Error("E-BO-005", $"{(r.Length > 0 ? r[0] : "pipe")}: give the planned diameter and both inverts; the build-out tool phases sewers as planned (design them first with Sanitary sewer network design)."));
        if (fromNetwork && (p.Text("redesign") ?? "no").Trim().ToLowerInvariant() != "no")
            msgs.Add(CalcMessage.Error("E-BO-005", "redesign must be no: the build-out tool phases the network's sizes and inverts as they are."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        string baseText = Strip(input, "Loads", "Growth");
        var tool = new SanitarySewerTool();
        int y0 = (int)Math.Round(baseYear), y1 = (int)Math.Round(endYear);
        var years = Enumerable.Range(y0, y1 - y0 + 1).ToList();
        var byYear = new Dictionary<int, SanitarySewerTool.SanitaryDesign>();
        ToolResult? first = null;
        foreach (int yr in years)
        {
            var res = tool.Run("h2s = no\n" + baseText + LoadsFor(yr), context, out var d);
            first ??= res;
            if (d is null)
                return new(title, [], [], [CalcMessage.Error("E-BO-006", $"The sanitary sewer design failed for {yr}; see the messages below."), .. res.Messages.Where(m => m.Severity == MessageSeverity.Error)]);
            byYear[yr] = d;
        }
        var basis = byYear[y0].Sewers;

        // Size the build-out flow needs on each sewer's slope (diameters blanked, inverts kept).
        string sizedText = fromNetwork ? "redesign = sizes\n" + baseText : BlankDiameters(baseText);
        tool.Run("h2s = no\n" + sizedText + LoadsFor(y1), context, out var sized);
        var needD = sized?.Sewers.ToDictionary(s => s.Name, s => s.DiameterIn, StringComparer.OrdinalIgnoreCase) ?? [];

        double DdOf(SanitarySewerTool.DesignedSewer s)
        {
            double dFt = s.DiameterIn / 12, y = SanitarySewerTool.NormalDepth(s.QpeakGpm / GpmPerCfs, dFt, s.N, s.Slope);
            return double.IsFinite(y) ? y / dFt : double.PositiveInfinity;
        }

        var phaseRows = new List<IReadOnlyList<object?>>();
        var ddRows = new List<IReadOnlyList<object?>>();
        var reportYears = years.Where(y => (y - y0) % (int)Math.Round(every) == 0).ToList();
        if (reportYears[^1] != y1) reportYears.Add(y1);
        var series = new Dictionary<string, List<(double, double)>>();
        int buildNow = 0, relieve = 0, overNow = 0;
        foreach (var s0 in basis)
        {
            var dd = years.ToDictionary(y => y, y => DdOf(byYear[y].Sewers.First(x => x.Name == s0.Name)));
            int? yLimit = years.Cast<int?>().FirstOrDefault(y => dd[y!.Value] > maxDd + 1e-9);
            int? ySurch = years.Cast<int?>().FirstOrDefault(y => double.IsPositiveInfinity(dd[y!.Value]));
            var sEnd = byYear[y1].Sewers.First(x => x.Name == s0.Name);
            double? need = needD.TryGetValue(s0.Name, out var nd) ? nd : null;
            string rec;
            if (yLimit is null) rec = "Adequate through build-out";
            else if (yLimit == y0)
            {
                overNow++;
                rec = need is { } n0 ? F($"Over the limit now: build {n0:G3} in.") : "Over the limit now";
                msgs.Add(CalcMessage.Warning("W-BO-002", F($"{s0.Name}: d/D = {Show(dd[y0])} already exceeds {maxDd:G2} in {y0}; the planned {s0.DiameterIn:G3} in. is too small today.")));
            }
            else if (yLimit - y0 <= horizon + 1e-9)
            {
                buildNow++;
                rec = need is { } n1 && n1 > s0.DiameterIn + 1e-9 ? F($"Build {n1:G3} in. now (reaches {maxDd:G2} in {yLimit})") : F($"Reaches {maxDd:G2} in {yLimit}: build larger now");
                msgs.Add(CalcMessage.Warning("W-BO-001", F($"{s0.Name}: the planned {s0.DiameterIn:G3} in. reaches d/D {maxDd:G2} in {yLimit}, within the {horizon:G3}-year decision horizon; build the build-out size{(need is { } n2 ? $" ({n2:G3} in.)" : "")} now.")));
            }
            else
            {
                relieve++;
                rec = F($"Build {s0.DiameterIn:G3} in. now; relieve or parallel by {yLimit}") + (need is { } n3 ? F($" ({n3:G3} in. at build-out)") : "");
            }
            phaseRows.Add([s0.Name, s0.DiameterIn, R(s0.Slope * 100, 3), R(s0.QpeakGpm, 1), R(sEnd.QpeakGpm, 1), Show(dd[y0]), Show(dd[y1]),
                yLimit is null ? $"after {y1}" : yLimit.Value.ToString(Inv), ySurch is null ? $"after {y1}" : ySurch.Value.ToString(Inv), need, rec]);
            ddRows.Add([s0.Name, s0.DiameterIn, .. reportYears.Select(y => Show(dd[y]))]);
            series[s0.Name] = years.Select(y => ((double)y, double.IsFinite(dd[y]) ? Math.Min(dd[y], 1.0) : 1.0)).ToList();
        }

        // ---- outlet flows by year ----
        var outlets = basis.Where(x => !basis.Any(y => y.From.Equals(x.To, StringComparison.OrdinalIgnoreCase))).Select(x => x.Name).ToList();
        var flowRows = new List<IReadOnlyList<object?>>();
        foreach (int yr in reportYears)
        {
            var os = byYear[yr].Sewers.Where(x => outlets.Contains(x.Name)).ToList();
            flowRows.Add([yr, R(os.Sum(x => x.Population), 0), R(os.Sum(x => x.QaveGpm), 1), R(os.Sum(x => x.QpeakGpm), 1), R(os.Sum(x => x.QpeakGpm) / GpmPerCfs, 3)]);
        }

        var summary = new List<string>
        {
            F($"Flows projected each year from {y0} to the build-out year {y1}: today's loads plus growth phased in by basin; IDCM peaking and reduction factors recomputed for each year's population and acres served."),
            F($"Outlet peak flow {flowRows[0][3]} gpm in {y0}, {flowRows[^1][3]} gpm at build-out."),
            F($"{basis.Count - buildNow - relieve - overNow} sewer(s) adequate through build-out; {relieve} to build as planned and relieve later; {buildNow} to build at the build-out size now (reaching d/D {maxDd:G2} within {horizon:G3} years)") + (overNow > 0 ? $"; {overNow} already over the limit." : "."),
        };
        int sanWarn = first!.Messages.Count(m => m.Severity == MessageSeverity.Warning && m.Code is not ("W-SS-001" or "W-SS-014"));
        if (sanWarn > 0) summary.Add($"The sanitary sewer design check of the base year has {sanWarn} other warning(s); run Sanitary sewer network design for them.");
        msgs.Add(CalcMessage.Info("I-BO-001", F($"Decision rule (D-726): a sewer reaching d/D {maxDd:G2} within {horizon:G3} years of {y0} is built at the build-out size now; one reaching it later is built as planned and relieved by that year. Set decision horizon to the agency's planning period or the payback of oversizing.")));
        if (sized is null) msgs.Add(CalcMessage.Warning("W-BO-003", "The build-out sizes could not be designed (see Sanitary sewer network design for the errors); the Build-out size column is blank."));

        var tables = new List<ToolTable>
        {
            new("Phasing",
                ["Pipe", "D (in)", "Slope (%)", $"Q peak {y0} (gpm)", $"Q peak {y1} (gpm)", $"d/D {y0}", $"d/D {y1}", F($"Year d/D > {maxDd:G2}"), "Year surcharged", "Build-out size (in)", "Recommendation"], phaseRows,
                [F($"d/D at normal depth of the peak flow (with I/I) on the planned slope. Surcharged = peak flow above the open-channel capacity (0.938 D). Build-out size = the smallest listed size with d/D ≤ {maxDd:G2} at the {y1} flow on the same slope (not below any upstream pipe)."),
                 "For the trunk's HGL with the backwater and manhole losses at any year's flow, run Sanitary trunk sewer HGL with that year's loads."]),
            new("d/D at peak flow by year", ["Pipe", "D (in)", .. reportYears.Select(y => y.ToString(Inv))], ddRows,
                ["\"surch.\" = the peak flow exceeds the pipe's open-channel capacity."]),
            new("Flow at the outlet by year", ["Year", "Res. population", "Q ave (gpm)", "Q peak (gpm)", "Q peak (cfs)"], flowRows,
                ["Sum over the network's outlet sewers. Population counts residential and multi-family loads."]),
            new("Growth schedule", ["Manhole", "Land use", "Area (ac)", "DU/ac", "Population", "Flow (gpm)", "PF", "Start year", "End year"],
                growth.Select(g => (IReadOnlyList<object?>)[g.Cells[0], g.Cells[1], Num(g.Cells[2]), Num(g.Cells[3]), Num(g.Cells[4]), Num(g.Cells[5]), Num(g.Cells[6]), g.Start, g.End]).ToList(),
                ["Each row adds its load linearly from the Start year (none) to the End year (all); with no End year, all of it from the Start year."]),
        };
        var chosen = series.OrderByDescending(kv => kv.Value[^1].Item2).Take(8).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var charts = new List<ToolChart>
        {
            new("d/D at peak flow by year", "Year", "d/D (1.0 = surcharged)", [.. chosen.Select(kv => new ChartSeries(kv.Key, kv.Value))], [new ChartLine(F($"d/D limit {maxDd:G2}"), maxDd)]),
        };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };

        // ---- local helpers ----
        string LoadsFor(int yr)
        {
            var sb = new StringBuilder("\n[Loads]\nManhole\tLand use\tArea (ac)\tDU/ac\tPopulation\tFlow (gpm)\tPF\n");
            foreach (var c in existing) sb.AppendLine(string.Join('\t', c));
            foreach (var g in growth)
            {
                double f = g.End > g.Start ? Math.Clamp((yr - g.Start) / (g.End - g.Start), 0, 1) : yr >= g.Start ? 1 : 0;
                if (f <= 0) continue;
                var c = (string[])g.Cells.Clone();
                foreach (int i in new[] { 2, 4, 5 })
                    if (ToolInput.TryNumber(c[i], out double v)) c[i] = (v * f).ToString("R", Inv);
                sb.AppendLine(string.Join('\t', c));
            }
            return sb.ToString();
        }
        int[] Map(IReadOnlyList<string> header, int fallbackStart)
        {
            var map = new int[5];
            bool named = header.Skip(2).Any(h => cols.Any(w => h.Contains(w, StringComparison.OrdinalIgnoreCase)));
            for (int k = 0; k < 5; k++)
            {
                map[k] = named ? -1 : fallbackStart + k;
                if (named) for (int i = 2; i < header.Count; i++) if (header[i].Contains(cols[k], StringComparison.OrdinalIgnoreCase)) { map[k] = i; break; }
            }
            return map;
        }
        static int Find(IReadOnlyList<string> header, string word, int fallback)
        {
            for (int i = 0; i < header.Count; i++) if (header[i].Contains(word, StringComparison.OrdinalIgnoreCase)) return i;
            return header.Any(h => h.Contains("year", StringComparison.OrdinalIgnoreCase)) ? -1 : fallback;
        }
        static string[] Normalize(string[] r, int[] map)
        {
            var c = new string[7];
            c[0] = r.Length > 0 ? r[0] : ""; c[1] = r.Length > 1 ? r[1] : "";
            for (int k = 0; k < 5; k++) c[2 + k] = map[k] >= 0 && map[k] < r.Length ? r[map[k]] : "";
            return c;
        }
        static object? Num(string s) => ToolInput.TryNumber(s, out double v) ? v : null;
        object Show(double v) => double.IsPositiveInfinity(v) ? "surch." : R(v, 3);
    }

    /// <summary>The input without the named [Section]s (their heading and every line up to the next heading).</summary>
    public static string Strip(string input, params string[] sections)
    {
        var sb = new StringBuilder();
        bool skip = false;
        foreach (var raw in (input ?? "").Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) skip = sections.Any(s => s.Equals(line[1..^1].Trim(), StringComparison.OrdinalIgnoreCase));
            if (!skip) sb.Append(raw).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>The input with the Diameter column (5th) of [Pipes] blanked, so the sanitary design sizes every sewer on its entered inverts.</summary>
    public static string BlankDiameters(string input)
    {
        var sb = new StringBuilder();
        bool inPipes = false, header = false;
        foreach (var raw in input.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { inPipes = line[1..^1].Trim().Equals("Pipes", StringComparison.OrdinalIgnoreCase); header = inPipes; sb.Append(raw).Append('\n'); continue; }
            bool row = inPipes && line.Length > 0 && !line.StartsWith('#') && !(line.IndexOf('=') > 0 && !line.Contains('\t') && !line[..line.IndexOf('=')].Contains(','));
            if (!row) { sb.Append(raw).Append('\n'); continue; }
            if (header) { header = false; sb.Append(raw).Append('\n'); continue; }
            var c = (line.Contains('\t') ? raw.Split('\t') : line.Split(',')).Select(x => x.Trim()).ToArray();
            if (c.Length > 4) c[4] = "";
            sb.Append(string.Join('\t', c)).Append('\n');
        }
        return sb.ToString();
    }

    private static string F(FormattableString s) => s.ToString(Inv);
    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
