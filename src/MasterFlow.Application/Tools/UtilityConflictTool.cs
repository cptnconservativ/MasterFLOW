using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Utility conflict finder (premium, D-270). Checks storm, sanitary, water, gas and dry utilities against each other in
/// 3D: pipe-to-pipe clashes and vertical clearance at every crossing, horizontal separation where lines run close, the
/// water-over-sewer rule, and utilities passing through storm structures, all against a table of separation rules.
/// The project's storm networks come in from their structure coordinates and pipe inverts; other utilities are
/// entered as polylines (one vertex per row).
/// </summary>
public sealed class UtilityConflictTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Id => "utility-conflicts";
    public string Name => "Utility conflict finder (3D clashes and separation rules)";
    public string Description => "Checks storm, sanitary, water, gas, electric and telecom lines against each other in 3D before the plans go out: clashes and vertical clearance at every crossing, horizontal separation where lines run side by side, water above sewer, and lines passing through storm manholes and inlets. The storm networks come straight from the project; other utilities are entered as polylines. Every finding has its location, the rule, the shortfall and the change that would fix it.";
    public bool UsesNetwork => true;

    /// <summary>Typical separation rules (Ten States Standards 2014 §38.3 and 8.8; common utility practice). Verify with the City and each utility owner.</summary>
    public const string DefaultRules = """
        Type A	Type B	Horizontal (ft)	Vertical (ft)	Water above	Reference
        water	sanitary	10	1.5	yes	Ten States Standards §38.31, §38.32; SD DANR
        water	storm	10	1.5	no	Ten States Standards §38.3 (storm sewer); verify locally
        water	gas	5	1.0	no	typical utility practice
        water	electric	5	1.0	no	typical utility practice
        water	telecom	3	1.0	no	typical utility practice
        sanitary	storm	3	0.5	no	typical; IDCM §4.6 clearance
        gas	storm	3	1.0	no	typical; verify with gas utility
        electric	storm	3	1.0	no	typical; verify with power utility
        telecom	storm	2	1.0	no	typical
        *	*	2	0.5	no	general minimum clearance
        """;

    public string SampleInput(DesignToolContext context)
    {
        // Lines placed around the selected network so the sample finds real crossings.
        var net = context.Network;
        double n0 = 1000, e0 = 1000, z0 = 100;
        if (net?.Structures.FirstOrDefault(s => s.Northing is not null && s.Easting is not null) is { } s0) { n0 = s0.Northing!.Value; e0 = s0.Easting!.Value; z0 = (s0.RimElevation ?? 105) - 5; }
        string F(double v) => v.ToString("0.00", Inv);
        return $"""
            # Utility conflict finder. The project's storm networks are included from their structure coordinates and pipe
            # inverts (structures need Northing/Easting). Other utilities: one vertex per row, consecutive rows with the same
            # Name form one line. Elev is the invert unless Ref says center or top. Type: water, sanitary, storm, gas,
            # electric, telecom (or any name; * in the rules matches any type). Size is the outside diameter in inches.
            structure diameter = 4        # ft, storm manholes and inlets (footprint for pass-through checks)
            storm wall = 0.2              # outside diameter = rise × (1 + this) for storm pipes (RCP wall)
            include storm networks = yes

            [Utilities]
            Name	Type	Size (in.)	Northing	Easting	Elev	Ref
            WM-1	water	9.0	{F(n0 - 60)}	{F(e0 + 120)}	{F(z0 + 1.5)}	top
            WM-1	water	9.0	{F(n0 + 140)}	{F(e0 + 120)}	{F(z0 + 1.5)}	top
            SS-1	sanitary	8.6	{F(n0 + 8)}	{F(e0 - 40)}	{F(z0 - 1)}	invert
            SS-1	sanitary	8.6	{F(n0 + 8)}	{F(e0 + 260)}	{F(z0 - 2.2)}	invert
            GAS-1	gas	4.5	{F(n0 - 3)}	{F(e0 + 30)}	{F(z0 + 2)}	center
            GAS-1	gas	4.5	{F(n0 - 3)}	{F(e0 + 240)}	{F(z0 + 2)}	center

            # Separation rules: edge to edge, ft. Water above = the water main must cross above (by Vertical).
            [Rules]
            {DefaultRules.TrimEnd()}
            """;
    }

    private sealed record Line(string Name, string Type, double Od, List<(double N, double E, double Z)> Pts, string Source);   // Z = centerline
    private sealed record Rule(string A, string B, double H, double V, bool WaterAbove, string Reference);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Utility conflicts";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        bool si = context.Units == UnitSystem.SI;
        string lu = si ? "m" : "ft";
        double inch = si ? 0.001 : 1.0 / 12;     // size column: mm or in.
        double structD = p.Number("structure diameter", si ? 1.2 : 4), wall = p.Number("storm wall", 0.2);

        // ---- rules
        var rules = new List<Rule>();
        var rh = p.Header("Rules").Select(h => h.Trim().ToLowerInvariant()).ToList();
        var ruleRows = p.Rows("Rules");
        if (ruleRows.Count == 0)
        {
            var d = ToolInput.Parse("[Rules]\n" + DefaultRules);
            rh = d.Header("Rules").Select(h => h.Trim().ToLowerInvariant()).ToList(); ruleRows = d.Rows("Rules");
        }
        int Rc(string s) => rh.FindIndex(h => h.StartsWith(s, StringComparison.Ordinal));
        int ra = Rc("type a"), rb = Rc("type b"), rH = Rc("horizontal"), rV = Rc("vertical"), rW = Rc("water above"), rR = Rc("reference");
        foreach (var r in ruleRows)
        {
            string G(int k) => k >= 0 && k < r.Length ? r[k].Trim() : "";
            if (!double.TryParse(G(rH), NumberStyles.Float, Inv, out var hh) || !double.TryParse(G(rV), NumberStyles.Float, Inv, out var vv))
            { msgs.Add(CalcMessage.Error("E-UC-001", $"[Rules] \"{string.Join(", ", r)}\": Horizontal and Vertical must be numbers.")); continue; }
            rules.Add(new(G(ra).ToLowerInvariant(), G(rb).ToLowerInvariant(), hh, vv, G(rW).StartsWith('y'), G(rR)));
        }
        Rule RuleFor(string a, string b)
        {
            bool M(string pat, string t) => pat == "*" || pat == t;
            return rules.FirstOrDefault(r => M(r.A, a) && M(r.B, b) || M(r.A, b) && M(r.B, a)) ?? new Rule("*", "*", 0, 0, false, "none");
        }

        // ---- lines
        var lines = new List<Line>();
        var structures = new List<(string Name, string Net, double N, double E, double Top, double Bottom)>();
        if (p.Flag("include storm networks", true))
        {
            var nets = context.Networks.Select(n => n.Network).ToList();
            if (nets.Count == 0 && context.Network is not null) nets.Add(context.Network);
            foreach (var net in nets)
            {
                int skipped = 0;
                foreach (var pipe in net.Pipes)
                {
                    var a = net.Structures.FirstOrDefault(s => s.Name == pipe.From); var b = net.Structures.FirstOrDefault(s => s.Name == pipe.To);
                    if (a?.Northing is not { } an || a.Easting is not { } ae || b?.Northing is not { } bn || b.Easting is not { } be) { skipped++; continue; }
                    double od = Math.Max(pipe.Rise, pipe.Span) * (1 + wall);
                    lines.Add(new($"{net.Name} {pipe.Name}", "storm", od, [(an, ae, pipe.UpstreamInvert + pipe.Rise / 2), (bn, be, pipe.DownstreamInvert + pipe.Rise / 2)], "storm network"));
                }
                foreach (var s in net.Structures.Where(s => s.Northing is not null && s.Easting is not null))
                {
                    var inv = net.Pipes.Where(q => q.From == s.Name).Select(q => q.UpstreamInvert).Concat(net.Pipes.Where(q => q.To == s.Name).Select(q => q.DownstreamInvert)).DefaultIfEmpty(double.NaN).Min();
                    if (double.IsFinite(inv)) structures.Add((s.Name, net.Name, s.Northing!.Value, s.Easting!.Value, s.RimElevation ?? inv + 5, inv - (si ? 0.15 : 0.5)));
                }
                if (skipped > 0) msgs.Add(CalcMessage.Warning("W-UC-001", $"{net.Name}: {skipped} pipe(s) left out because their structures have no Northing/Easting."));
            }
        }
        var uh = p.Header("Utilities").Select(h => h.Trim().ToLowerInvariant()).ToList();
        // Exact header first ("n", "e", "z"), then the start of a longer one ("northing (ft)").
        int U(params string[] n) => uh.FindIndex(h => n.Any(x => h == x)) is var i && i >= 0 ? i : uh.FindIndex(h => n.Any(x => x.Length > 2 && h.StartsWith(x, StringComparison.Ordinal)));
        int cName = U("name"), cType = U("type"), cSize = U("size", "od"), cN = U("northing", "n"), cE = U("easting", "e"), cZ = U("elev", "z"), cRef = U("ref");
        var urows = p.Rows("Utilities");
        if (urows.Count > 0 && (cName < 0 || cN < 0 || cE < 0 || cZ < 0)) return ToolResult.Failed(title, CalcMessage.Error("E-UC-002", "[Utilities] needs a header row: Name, Type, Size (in.), Northing, Easting, Elev, Ref."));
        Line? cur = null; int row = 0;
        foreach (var r in urows)
        {
            row++;
            string G(int k) => k >= 0 && k < r.Length ? r[k].Trim() : "";
            double Nm(int k, string what)
            {
                if (double.TryParse(G(k), NumberStyles.Float, Inv, out var v)) return v;
                msgs.Add(CalcMessage.Error("E-UC-003", $"[Utilities] row {row}: {what} \"{G(k)}\" is not a number.")); return double.NaN;
            }
            string name = G(cName);
            double size = cSize >= 0 && G(cSize).Length > 0 ? Nm(cSize, "Size") : (si ? 200 : 8);
            double od = size * inch, z = Nm(cZ, "Elev");
            string refz = G(cRef).ToLowerInvariant();
            double center = refz.StartsWith('c') ? z : refz.StartsWith('t') ? z - od / 2 : z + od / 2;
            var pt = (Nm(cN, "Northing"), Nm(cE, "Easting"), center);
            if (cur is null || cur.Name != name) { cur = new(name, (G(cType).Length > 0 ? G(cType) : "other").ToLowerInvariant(), od, [], "entered"); lines.Add(cur); }
            cur.Pts.Add(pt);
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);
        foreach (var l in lines.Where(l => l.Pts.Count < 2).ToList()) { msgs.Add(CalcMessage.Warning("W-UC-002", $"{l.Name} has one vertex; it needs two or more.")); lines.Remove(l); }
        if (lines.Count < 2) return ToolResult.Failed(title, CalcMessage.Error("E-UC-004", "Fewer than two utility lines to check: enter utilities under [Utilities], and give the storm network structures Northing/Easting."));

        // ---- pairwise checks
        var found = new List<(string Kind, string A, string B, double N, double E, string Required, double Actual, double Short, string Fix, string Reference)>();
        var segs = lines.SelectMany(l => Enumerable.Range(0, l.Pts.Count - 1).Select(i => (Line: l, P: l.Pts[i], Q: l.Pts[i + 1]))).ToList();
        for (int i = 0; i < segs.Count; i++)
            for (int j = i + 1; j < segs.Count; j++)
            {
                var (la, a0, a1) = segs[i]; var (lb, b0, b1) = segs[j];
                if (la == lb) continue;
                if (la.Source == "storm network" && lb.Source == "storm network" && la.Name.Split(' ')[0] == lb.Name.Split(' ')[0]) continue;   // same storm network: joined at structures
                var rule = RuleFor(la.Type, lb.Type);
                double ra2 = la.Od / 2, rb2 = lb.Od / 2;
                if (Cross(a0, a1, b0, b1) is { } x)
                {
                    double za = a0.Z + (a1.Z - a0.Z) * x.T, zb = b0.Z + (b1.Z - b0.Z) * x.U;
                    double clear = Math.Abs(za - zb) - ra2 - rb2;
                    var (upper, lower, zu, zl) = za >= zb ? (la, lb, za, zb) : (lb, la, zb, za);
                    double n = a0.N + (a1.N - a0.N) * x.T, e = a0.E + (a1.E - a0.E) * x.T;
                    if (clear < 0)
                        found.Add(("CLASH", la.Name, lb.Name, n, e, $"{rule.V:0.##} {lu} clear", clear, rule.V - clear, $"move {lower.Name} down or {upper.Name} up by {rule.V - clear:0.00} {lu}", rule.Reference));
                    else if (clear < rule.V - 1e-9)
                        found.Add(("VERTICAL", la.Name, lb.Name, n, e, $"{rule.V:0.##} {lu} clear", clear, rule.V - clear, $"lower {lower.Name} or raise {upper.Name} {rule.V - clear:0.00} {lu}", rule.Reference));
                    // Water must cross above sewer (Ten States §38.32): when it is below, the rule is broken whatever the clearance.
                    var water = la.Type == "water" ? la : lb.Type == "water" ? lb : null;
                    if (rule.WaterAbove && water is not null && lower == water)
                        found.Add(("WATER BELOW", water.Name, (water == la ? lb : la).Name, n, e, "water main above sewer", clear, 0, $"cross {water.Name} above, or encase the sewer / use pressure-rated pipe 10 ft each side (Ten States §38.32)", rule.Reference));
                    continue;
                }
                // Side by side: plan distance between the segments, edge to edge.
                var (dist, sA) = Distance(a0, a1, b0, b1);
                double edge = dist - ra2 - rb2;
                if (edge < rule.H - 1e-9)
                {
                    double runLen = RunWithin(a0, a1, b0, b1, rule.H + ra2 + rb2);
                    double za = a0.Z + (a1.Z - a0.Z) * sA;
                    double n = a0.N + (a1.N - a0.N) * sA, e = a0.E + (a1.E - a0.E) * sA;
                    // Vertical offset where they are closest: Ten States allows less than 10 ft with the water main 18 in. above the sewer in its own trench.
                    var (bn, be) = (n, e);
                    double zb = ZAt(b0, b1, bn, be);
                    double vclear = Math.Abs(za - zb) - ra2 - rb2;
                    string note = rule.WaterAbove && (la.Type == "water" || lb.Type == "water") && vclear >= rule.V && (la.Type == "water" ? za > zb : zb > za)
                        ? $"water main is {vclear:0.00} {lu} above: may be allowed in a separate trench with approval (Ten States §38.31)" : $"move one line {rule.H - edge:0.00} {lu} apart";
                    found.Add(("HORIZONTAL", la.Name, lb.Name, n, e, $"{rule.H:0.##} {lu} apart", edge, rule.H - edge, note + (runLen > 0 ? $"; parallel for about {runLen:0} {lu}" : ""), rule.Reference));
                }
            }
        // Through structures.
        foreach (var s in structures)
            foreach (var l in lines.Where(l => !(l.Source == "storm network" && l.Name.StartsWith(s.Net + " ", StringComparison.Ordinal))))
                for (int k = 0; k + 1 < l.Pts.Count; k++)
                {
                    var (d, t) = PointToSeg(s.N, s.E, l.Pts[k], l.Pts[k + 1]);
                    double edge = d - structD / 2 - l.Od / 2;
                    var rule = RuleFor(l.Type, "storm");
                    double z = l.Pts[k].Z + (l.Pts[k + 1].Z - l.Pts[k].Z) * t;
                    bool inDepth = z + l.Od / 2 > s.Bottom && z - l.Od / 2 < s.Top;
                    if (inDepth && edge < Math.Min(rule.H, si ? 0.3 : 1.0))
                        found.Add((edge < 0 ? "THROUGH STRUCTURE" : "STRUCTURE", l.Name, $"{s.Net} {s.Name}", l.Pts[k].N + (l.Pts[k + 1].N - l.Pts[k].N) * t, l.Pts[k].E + (l.Pts[k + 1].E - l.Pts[k].E) * t,
                            $"{Math.Min(rule.H, si ? 0.3 : 1.0):0.##} {lu} from the structure wall", edge, Math.Min(rule.H, si ? 0.3 : 1.0) - edge, $"reroute {l.Name} around {s.Name} or relocate the structure", rule.Reference));
                }

        // ---- report
        var order = new[] { "CLASH", "THROUGH STRUCTURE", "WATER BELOW", "VERTICAL", "STRUCTURE", "HORIZONTAL" };
        var sorted = found.OrderBy(f => Array.IndexOf(order, f.Kind)).ThenByDescending(f => f.Short).ToList();
        var rows = sorted.Select((f, i) => (IReadOnlyList<object?>)[i + 1, f.Kind, f.A, f.B, Math.Round(f.N, 2), Math.Round(f.E, 2), f.Required, Math.Round(f.Actual, 2), f.Short > 0 ? Math.Round(f.Short, 2) : null, f.Fix, f.Reference]).ToList();
        var tables = new List<ToolTable>
        {
            new("Conflicts", ["No.", "Kind", "Line", "With", "Northing", "Easting", "Rule", $"Actual ({lu})", $"Short by ({lu})", "Fix", "Reference"], rows,
                rows.Count == 0 ? ["No conflicts: every crossing and parallel run meets the separation rules."] : ["Distances are edge to edge (outside of pipe). CLASH: the pipes overlap. HORIZONTAL: closer in plan than the rule where they do not cross."]),
        };
        var perLine = lines.Select(l => (IReadOnlyList<object?>)[l.Name, l.Type, Math.Round(l.Od * (si ? 1000 : 12), 1), l.Pts.Count - 1, Math.Round(Enumerable.Range(0, l.Pts.Count - 1).Sum(k => Math.Sqrt(Math.Pow(l.Pts[k + 1].N - l.Pts[k].N, 2) + Math.Pow(l.Pts[k + 1].E - l.Pts[k].E, 2))), 1),
            found.Count(f => f.A == l.Name || f.B == l.Name), l.Source]).ToList();
        tables.Add(new("Lines checked", ["Line", "Type", $"OD ({(si ? "mm" : "in.")})", "Segments", $"Length ({lu})", "Findings", "From"], perLine));
        tables.Add(new("Separation rules", ["Type A", "Type B", $"Horizontal ({lu})", $"Vertical ({lu})", "Water above", "Reference"], rules.Select(r => (IReadOnlyList<object?>)[r.A, r.B, r.H, r.V, r.WaterAbove ? "yes" : "", r.Reference]).ToList(),
            ["The default rules are typical values; confirm them with the City of Rapid City and each utility owner before relying on them."]));

        int clashes = found.Count(f => f.Kind is "CLASH" or "THROUGH STRUCTURE");
        var summary = new List<string>
        {
            $"{lines.Count} lines ({lines.Count(l => l.Source == "storm network")} storm pipes from the project), {structures.Count} storm structures checked.",
            found.Count == 0 ? "No conflicts." : $"{found.Count} finding(s): {string.Join(", ", order.Where(k => found.Any(f => f.Kind == k)).Select(k => $"{found.Count(f => f.Kind == k)} {k.ToLowerInvariant()}"))}.",
        };
        if (clashes > 0) msgs.Add(CalcMessage.Warning("W-UC-003", $"{clashes} physical clash(es): pipes overlap or pass through a structure. Resolve before the plans go out."));

        // Plan view.
        var series = lines.Select(l => new ChartSeries(l.Name, l.Pts.Select(q => (q.E, q.N)).ToList(), l.Type is "electric" or "telecom" or "gas")).ToList();
        if (structures.Count > 0) series.Add(new ChartSeries("Storm structures", structures.Select(s => (s.E, s.N)).ToList(), false, -1, true));
        if (found.Count > 0) series.Add(new ChartSeries("Findings", found.Select(f => (f.E, f.N)).ToList(), false, -1, true));
        var charts = new List<ToolChart> { new("Plan: utilities and findings", $"Easting ({lu})", $"Northing ({lu})", series) };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    // ---------------------------------------------------------------- geometry (plan N, E; Z centerline)

    private static (double T, double U)? Cross((double N, double E, double Z) a0, (double N, double E, double Z) a1, (double N, double E, double Z) b0, (double N, double E, double Z) b1)
    {
        double dx1 = a1.E - a0.E, dy1 = a1.N - a0.N, dx2 = b1.E - b0.E, dy2 = b1.N - b0.N;
        double den = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(den) < 1e-12) return null;
        double t = ((b0.E - a0.E) * dy2 - (b0.N - a0.N) * dx2) / den, u = ((b0.E - a0.E) * dy1 - (b0.N - a0.N) * dx1) / den;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1 ? (t, u) : null;
    }

    private static (double D, double T) PointToSeg(double n, double e, (double N, double E, double Z) a, (double N, double E, double Z) b)
    {
        double dx = b.E - a.E, dy = b.N - a.N, L2 = dx * dx + dy * dy;
        double t = L2 == 0 ? 0 : Math.Clamp(((e - a.E) * dx + (n - a.N) * dy) / L2, 0, 1);
        return (Math.Sqrt(Math.Pow(a.E + t * dx - e, 2) + Math.Pow(a.N + t * dy - n, 2)), t);
    }

    /// <summary>Least plan distance between two segments that do not cross, and where it is on the first.</summary>
    private static (double D, double TA) Distance((double N, double E, double Z) a0, (double N, double E, double Z) a1, (double N, double E, double Z) b0, (double N, double E, double Z) b1)
    {
        var c = new List<(double D, double T)>
        {
            PointToSeg(b0.N, b0.E, a0, a1), PointToSeg(b1.N, b1.E, a0, a1),
            (PointToSeg(a0.N, a0.E, b0, b1).D, 0), (PointToSeg(a1.N, a1.E, b0, b1).D, 1),
        };
        return c.MinBy(x => x.D);
    }

    private static double ZAt((double N, double E, double Z) b0, (double N, double E, double Z) b1, double n, double e)
    {
        var (_, t) = PointToSeg(n, e, b0, b1);
        return b0.Z + (b1.Z - b0.Z) * t;
    }

    /// <summary>Length of segment a that lies within <paramref name="within"/> (plan) of segment b.</summary>
    private static double RunWithin((double N, double E, double Z) a0, (double N, double E, double Z) a1, (double N, double E, double Z) b0, (double N, double E, double Z) b1, double within)
    {
        double L = Math.Sqrt(Math.Pow(a1.N - a0.N, 2) + Math.Pow(a1.E - a0.E, 2));
        int n = Math.Clamp((int)(L / 1.0), 10, 2000), inside = 0;
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n;
            if (PointToSeg(a0.N + (a1.N - a0.N) * t, a0.E + (a1.E - a0.E) * t, b0, b1).D <= within) inside++;
        }
        return L * inside / (n + 1);
    }
}
