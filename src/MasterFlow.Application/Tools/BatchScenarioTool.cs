using System.Globalization;
using System.Text.RegularExpressions;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Batch scenario manager (premium). Runs many cases at once and compares them side by side:
/// <list type="bullet">
/// <item><c>tool = network</c>: the selected storm network's HGL/EGL under each scenario's tailwater, flow factors, n,
/// pipe sizes, structure inflows or criteria, with a scenario summary, HGL and clearance by structure, velocity by pipe
/// and the HGL profile of the longest path for every scenario on one chart.</item>
/// <item><c>tool = &lt;any design tool id&gt;</c>: that tool's input (between <c>[Base input]</c> and <c>[End base input]</c>)
/// with "key = value" lines overridden per scenario; the results are compared by the cells named under <c>[Compare]</c>
/// (table, row, column) and charts whose title starts with <c>chart</c> are overlaid.</item>
/// </list>
/// </summary>
public sealed class BatchScenarioTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Id => "batch-scenarios";
    public string Name => "Batch scenarios (compare storms, sizes, tailwater side by side)";
    public string Description => "Runs the storm network, or any design tool, for many scenarios at once (tailwater, flow factors, n, pipe sizes, inflows; or any input value of a tool) and compares them side by side: summary, HGL and clearance by structure, velocity by pipe, overlaid profiles, or the result cells you name.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context)
    {
        bool si = (context.Network?.Units ?? context.Units) == UnitSystem.SI;
        if (context.Network is { } net && net.Pipes.Count > 0)
        {
            var outfall = net.Structures.FirstOrDefault(s => !net.Pipes.Any(p => p.From == s.Name));
            var tw = net.Pipes.Where(p => p.To == outfall?.Name).Select(p => p.DownstreamInvert + p.Rise).DefaultIfEmpty(0).Max();
            var big = net.Pipes.OrderByDescending(p => p.Rise).First();
            double toSize = si ? 1000 : 12, step = si ? 150 : 6;
            return FormattableString.Invariant($"""
                # Batch scenarios on the selected storm network. Blank cells keep the network's value.
                # Keys: tailwater (elevation, free, crown or hec22), flow factor, minor flow factor, n, junction k,
                # hgl below rim, pipe <name> size ({(si ? "mm" : "in.")}), pipe <name> n, structure <name> inflow.
                tool = network

                [Scenarios]
                Scenario	tailwater	flow factor	pipe {big.Name} size
                Base
                High tailwater	{tw + (si ? 0.3 : 1.0):0.##}
                100-yr flows (×1.6)		1.6
                100-yr, {big.Name} upsized		1.6	{Math.Round(big.Rise * toSize) + step:0}
                Free outfall, 2-yr flows (×0.6)	free	0.6
                """);
        }
        return """
            # Batch scenarios on any design tool: its input goes between [Base input] and [End base input]; each scenario
            # row overrides "key = value" lines (Section.key for a key inside a section). [Compare] names result cells.
            tool = hds5-culvert
            chart = Performance curve     # overlay this chart from every scenario

            [Scenarios]
            # Scenario names must not contain commas (a row without a tab is split on commas).
            Scenario	D	Q design
            4 ft at 120 cfs
            5 ft at 120 cfs	5
            4 ft at 150 cfs		150
            5 ft at 150 cfs	5	150

            [Compare]
            Table	Row	Column
            Results	Design	HW elevation
            Results	Design	Control
            Results	Design	Outlet V
            Results	Check	HW elevation

            [Base input]
            shape = circular
            D = 4
            barrels = 1
            inlet = circ-conc-square-headwall
            n = 0.012
            length = 200
            upstream invert = 100.00
            downstream invert = 99.00
            Q design = 120
            Q check = 220
            tailwater depth = 3.0
            criteria = rapid-city
            street class = local
            [Roadway]
            station, elevation
            0, 110
            100, 108.5
            200, 110
            [End base input]
            """;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Batch scenarios";
        // The base input of another tool is taken raw, before parsing, so its own [Sections] stay intact.
        string raw = (input ?? "").Replace("\r", "");
        string? baseInput = null;
        var bm = Regex.Match(raw, @"(?im)^[ \t]*\[Base input\][ \t]*$");
        if (bm.Success)
        {
            var em = Regex.Match(raw[(bm.Index + bm.Length)..], @"(?im)^[ \t]*\[End base input\][ \t]*$");
            int end = em.Success ? bm.Index + bm.Length + em.Index : raw.Length;
            baseInput = raw[(bm.Index + bm.Length)..end].TrimStart('\n');
            raw = raw[..bm.Index] + (em.Success ? raw[(end + em.Length)..] : "");
        }
        var p = ToolInput.Parse(raw);
        var msgs = new List<CalcMessage>(p.Errors);
        string tool = (p.Text("tool") ?? "network").Trim();
        var head = p.Header("Scenarios");
        var rows = p.Rows("Scenarios").Where(r => r.Length > 0 && r[0].Length > 0).ToList();
        if (head.Count == 0 || rows.Count == 0) msgs.Add(CalcMessage.Error("E-BS-001", "List the scenarios under [Scenarios]: a header row (Scenario, then one column per key) and one row per scenario."));
        foreach (var g in rows.GroupBy(r => r[0], StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            msgs.Add(CalcMessage.Error("E-BS-001", $"Scenario \"{g.Key}\" is listed {g.Count()} times; names must differ."));
        if (rows.Count > 50) msgs.Add(CalcMessage.Error("E-BS-001", $"{rows.Count} scenarios; at most 50 run at once."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);
        var scen = rows.Select(r => (Name: r[0], Over: Enumerable.Range(1, head.Count - 1).Where(i => i < r.Length && r[i].Length > 0).Select(i => (Key: head[i].Trim(), Value: r[i].Trim())).ToList())).ToList();
        return tool.Equals("network", StringComparison.OrdinalIgnoreCase)
            ? RunNetwork(title, scen, context, msgs)
            : RunTool(title, tool, baseInput, scen, p, context, msgs);
    }

    // ------------------------------------------------------------------ storm network

    private static ToolResult RunNetwork(string title, List<(string Name, List<(string Key, string Value)> Over)> scen, DesignToolContext context, List<CalcMessage> msgs)
    {
        if (context.Network is not { } net || net.Pipes.Count == 0)
            return ToolResult.Failed(title, CalcMessage.Error("E-BS-002", "Select a storm network with pipes first (tool = network), or name another tool."));
        bool us = net.Units == UnitSystem.USCustomary;
        string L = us ? "ft" : "m", Vu = us ? "ft/s" : "m/s";
        double toSize = us ? 12 : 1000;
        var nets = new List<(string Name, string Desc, StormNetwork Net)>();
        foreach (var (name, over) in scen)
        {
            var n = net;
            var desc = new List<string>();
            foreach (var (key, value) in over)
            {
                string k = Regex.Replace(key.ToLowerInvariant(), @"\s+", " ");
                double num = double.NaN;
                bool isNum = ToolInput.TryNumber(value, out num);
                string Err(string what) { msgs.Add(CalcMessage.Error("E-BS-004", $"Scenario {name}: {key} = \"{value}\": {what}.")); return ""; }
                if (k == "tailwater")
                {
                    if (isNum) n = n with { Tailwater = TailwaterRule.Elevation, TailwaterElevation = num };
                    else if (value.Equals("free", StringComparison.OrdinalIgnoreCase)) n = n with { Tailwater = TailwaterRule.FreeOutfall };
                    else if (value.Equals("crown", StringComparison.OrdinalIgnoreCase)) n = n with { Tailwater = TailwaterRule.Crown };
                    else if (value.Equals("hec22", StringComparison.OrdinalIgnoreCase) || value.StartsWith("(yc", StringComparison.OrdinalIgnoreCase)) n = n with { Tailwater = TailwaterRule.HalfCriticalPlusDiameter };
                    else { Err("use an elevation, free, crown or hec22"); continue; }
                }
                else if (!isNum) { Err("not a number"); continue; }
                else if (k is "flow factor" or "minor flow factor")
                {
                    if (!(num >= 0)) { Err("must be zero or more"); continue; }
                    bool minor = k.StartsWith("minor");
                    n = n with
                    {
                        Pipes = n.Pipes.Select(x => minor ? x with { MinorDischarge = x.MinorDischarge * num } : x with { Discharge = x.Discharge * num }).ToList(),
                        Structures = n.Structures.Select(s => minor ? s with { MinorInflow = s.MinorInflow * num } : s with { Inflow = s.Inflow * num }).ToList(),
                    };
                }
                else if (k == "n") { if (!(num > 0)) { Err("must be more than zero"); continue; } n = n with { Pipes = n.Pipes.Select(x => x with { ManningN = num }).ToList() }; }
                else if (k == "junction k") { if (!(num >= 0)) { Err("must be zero or more"); continue; } n = n with { DefaultJunctionK = num }; }
                else if (k == "hgl below rim") n = n with { Criteria = n.Criteria with { HglBelowRim = num } };
                else if (Regex.Match(k, @"^pipe (.+) (size|n)$") is { Success: true } pm)
                {
                    var pipe = n.Pipes.FirstOrDefault(x => x.Name.Equals(pm.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (pipe is null) { Err("no such pipe"); continue; }
                    if (!(num > 0)) { Err("must be more than zero"); continue; }
                    var np = pm.Groups[2].Value == "n" ? pipe with { ManningN = num }
                        : pipe.Shape == NetworkPipeShape.Circular ? pipe with { Rise = num / toSize, Span = num / toSize } : pipe with { Rise = num / toSize };
                    n = n with { Pipes = n.Pipes.Select(x => x.Name == pipe.Name ? np : x).ToList() };
                }
                else if (Regex.Match(k, @"^structure (.+) (inflow|minor inflow)$") is { Success: true } sm)
                {
                    var st = n.Structures.FirstOrDefault(x => x.Name.Equals(sm.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (st is null) { Err("no such structure"); continue; }
                    if (!(num >= 0)) { Err("must be zero or more"); continue; }
                    var ns = sm.Groups[2].Value == "inflow" ? st with { Inflow = num } : st with { MinorInflow = num };
                    n = n with { Structures = n.Structures.Select(x => x.Name == st.Name ? ns : x).ToList() };
                }
                else { msgs.Add(CalcMessage.Error("E-BS-003", $"Scenario {name}: \"{key}\" is not a network key (tailwater, flow factor, minor flow factor, n, junction k, hgl below rim, pipe <name> size, pipe <name> n, structure <name> inflow).")); continue; }
                desc.Add($"{key} = {value}");
            }
            nets.Add((name, desc.Count == 0 ? "as entered" : string.Join("; ", desc), n));
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        var results = nets.Select(x => (x.Name, x.Desc, x.Net, Res: StormNetworkSolver.Solve(x.Net))).ToList();
        foreach (var r in results.Where(r => !r.Res.Succeeded))
            msgs.Add(CalcMessage.Warning("W-BS-001", $"Scenario {r.Name} did not solve: {string.Join(" ", r.Res.Messages.Where(m => m.Severity == MessageSeverity.Error).Select(m => m.Text))}"));
        var sumRows = new List<IReadOnlyList<object?>>();
        foreach (var (name, desc, n, res) in results)
        {
            if (!res.Succeeded) { sumRows.Add([name, desc, "no", null, null, null, null, null, null]); continue; }
            var st = res.Structures.Where(s => !s.IsOutfall && s.HglClearance is not null).ToList();
            var worst = st.OrderBy(s => s.HglClearance).FirstOrDefault();
            int fail = n.Criteria.HglBelowRim is { } need ? st.Count(s => s.HglClearance < need - 1e-9) : 0;
            var vmax = res.Pipes.OrderByDescending(x => x.VelocityUp).First();
            var minors = res.Pipes.Where(x => !double.IsNaN(x.MinorVelocity)).ToList();
            sumRows.Add([name, desc, "yes", worst is null ? null : $"{worst.Structure.Name}: {worst.HglClearance:F2}", fail, Math.Round(res.Pipes.Max(x => Math.Max(x.VelocityUp, x.VelocityDown)), 2),
                minors.Count > 0 ? Math.Round(minors.Min(x => x.MinorVelocity), 2) : null, res.Pipes.Count(x => x.FlowsFull), Math.Round(res.Structures.Where(s => s.IsOutfall).Select(s => s.Hgl).DefaultIfEmpty(double.NaN).Max(), 3)]);
        }
        var ok = results.Where(r => r.Res.Succeeded).ToList();
        var hglRows = net.Structures.Select(s => (IReadOnlyList<object?>)new object?[] { s.Name, s.RimElevation }
            .Concat(ok.Select(r => (object?)(r.Res.Structure(s.Name) is { } h ? Math.Round(h.Hgl, 3) : null))).ToList()).ToList();
        var clrRows = net.Structures.Where(s => s.RimElevation is not null && net.Pipes.Any(x => x.From == s.Name)).Select(s => (IReadOnlyList<object?>)new object?[] { s.Name }
            .Concat(ok.Select(r => (object?)(r.Res.Structure(s.Name)?.HglClearance is { } c ? Math.Round(c, 2) : null))).ToList()).ToList();
        var velRows = net.Pipes.Select(x => (IReadOnlyList<object?>)new object?[] { x.Name }
            .Concat(ok.SelectMany(r => r.Res.Pipe(x.Name) is { } h ? new object?[] { Math.Round(h.Pipe.Discharge, 3), Math.Round(Math.Max(h.VelocityUp, h.VelocityDown), 2) } : [null, null])).ToList()).ToList();

        // Profile of the longest path, HGL of every scenario.
        var charts = new List<ToolChart>();
        if (ok.Count > 0)
        {
            var r0 = ok[0].Res;
            var start = net.Structures.Where(s => !net.Pipes.Any(x => x.To == s.Name) && net.Pipes.Any(x => x.From == s.Name))
                .OrderByDescending(s => r0.PathToOutfall(s.Name).Sum(x => x.Pipe.Length)).FirstOrDefault();
            if (start is not null)
            {
                var series = new List<ChartSeries>();
                var path = r0.PathToOutfall(start.Name).Select(x => x.Pipe.Name).ToList();
                var ground = new List<(double, double)>(); var inv = new List<(double, double)>();
                double st0 = 0;
                foreach (var pn in path)
                {
                    var pp = net.Pipes.First(x => x.Name == pn);
                    if (net.Structures.First(z => z.Name == pp.From).RimElevation is { } ru) ground.Add((st0, ru));
                    inv.Add((st0, pp.UpstreamInvert));
                    st0 += pp.Length;
                    if (net.Structures.First(z => z.Name == pp.To).RimElevation is { } rd) ground.Add((st0, rd));
                    inv.Add((st0, pp.DownstreamInvert));
                }
                series.Add(new("Rim / ground", ground)); series.Add(new("Invert (base network)", inv));
                foreach (var (name, _, _, res) in ok)
                {
                    var pts = new List<(double, double)>(); double s = 0;
                    foreach (var pn in path)
                    {
                        var h = res.Pipe(pn)!;
                        pts.Add((s, h.HglUp)); s += h.Pipe.Length; pts.Add((s, h.HglDown));
                    }
                    series.Add(new($"HGL {name}", pts, Dashed: true));
                }
                charts.Add(new ToolChart($"HGL profile {start.Name} to {net.Pipes.First(x => x.Name == path[^1]).To}, every scenario", $"Distance ({L})", $"Elevation ({L})", series));
            }
        }
        var names = ok.Select(r => r.Name).ToList();
        int worstFail = sumRows.Max(r => r[4] is int f ? f : 0);
        var summary = new List<string> { $"{results.Count} scenarios on {net.Name}; {ok.Count} solved." };
        foreach (var r in sumRows) summary.Add($"{r[0]}: worst rim − HGL {r[3] ?? "—"} {L}, {r[4] ?? "—"} structure(s) below the criterion, max V {r[5] ?? "—"} {Vu}.");
        msgs.Add(CalcMessage.Info("I-BS-001", "Each scenario is the selected network with the listed changes, solved by the same HGL/EGL solver (IDCM §4.6.7 criteria from the network). Flow factors scale typed pipe flows and structure inflows."));
        return new ToolResult(title, summary,
        [
            new("Scenario summary", ["Scenario", "Changes", "Solved", $"Worst rim − HGL ({L})", "Structures below the HGL criterion", $"Max V ({Vu})", $"Min minor-storm V ({Vu})", "Pipes flowing full", $"Outfall HGL ({L})"], sumRows),
            new($"HGL by structure ({L})", ["Structure", $"Rim ({L})", .. names], hglRows),
            new($"Rim − HGL by structure ({L})", ["Structure", .. names], clrRows, [net.Criteria.HglBelowRim is { } nd ? $"Criterion: at least {nd:G3} {L}." : "No HGL criterion on the network."]),
            new("Flow and velocity by pipe", ["Pipe", .. names.SelectMany(n => new[] { $"{n} Q", $"{n} V" })], velRows, ["V is the larger of the end velocities."]),
        ], msgs) { Charts = charts };
    }

    // ------------------------------------------------------------------ any design tool

    private ToolResult RunTool(string title, string toolId, string? baseInput, List<(string Name, List<(string Key, string Value)> Over)> scen, ToolInput p, DesignToolContext context, List<CalcMessage> msgs)
    {
        var tool = DesignTools.Find(toolId);
        if (tool is null || tool.Id == Id) return ToolResult.Failed(title, CalcMessage.Error("E-BS-002", $"Tool \"{toolId}\" is not a design tool (or is this one). Use network or one of: {string.Join(", ", DesignTools.All.Where(t => t.Id != Id).Select(t => t.Id))}."));
        // A premium tool run from here follows the same license gate as the Run button.
        if (context.MayRun is { } may && !may(tool)) return ToolResult.Failed(title, CalcMessage.Error("E-BS-005", $"{tool.Name} is not covered by your license, so its scenarios cannot be run."));
        if (baseInput is null) { baseInput = tool.SampleInput(context); msgs.Add(CalcMessage.Info("I-BS-002", $"No [Base input]; the {tool.Name} sample input is the base.")); }
        var compare = p.Rows("Compare").Where(r => r.Length >= 3).Select(r => (Table: r[0], Row: r[1], Col: r[2])).ToList();
        string? chartKey = p.Text("chart");
        var runs = new List<(string Name, string Desc, ToolResult Res)>();
        foreach (var (name, over) in scen)
        {
            string text = baseInput;
            foreach (var (key, value) in over)
            {
                if (!TrySet(ref text, key, value)) msgs.Add(CalcMessage.Warning("W-BS-002", $"Scenario {name}: \"{key}\" is not a key of the base input; added as a new line."));
            }
            runs.Add((name, over.Count == 0 ? "base input" : string.Join("; ", over.Select(o => $"{o.Key} = {o.Value}")), tool.Run(text, context)));
        }
        var sumRows = runs.Select(r => (IReadOnlyList<object?>)[r.Name, r.Desc, r.Res.Succeeded ? "yes" : "no", r.Res.Messages.Count(m => m.Severity == MessageSeverity.Warning),
            string.Join(" ", r.Res.Summary.Take(2))]).ToList();
        foreach (var r in runs.Where(r => !r.Res.Succeeded))
            msgs.Add(CalcMessage.Warning("W-BS-001", $"Scenario {r.Name}: {string.Join(" ", r.Res.Messages.Where(m => m.Severity == MessageSeverity.Error).Select(m => $"[{m.Code}] {m.Text}"))}"));
        var tables = new List<ToolTable> { new($"Scenario summary ({tool.Name})", ["Scenario", "Changes", "Succeeded", "Warnings", "Result summary"], sumRows) };
        if (compare.Count > 0)
        {
            var cmpRows = new List<IReadOnlyList<object?>>();
            foreach (var (tName, rName, cName) in compare)
            {
                var row = new List<object?> { $"{tName} / {rName} / {cName}" };
                bool any = false;
                foreach (var r in runs)
                {
                    object? v = Cell(r.Res, tName, rName, cName);
                    any |= v is not null;
                    row.Add(v);
                }
                if (!any) msgs.Add(CalcMessage.Warning("W-BS-003", $"Compare: no scenario has a cell for table \"{tName}\", row \"{rName}\", column \"{cName}\" (names match by their start, ignoring case)."));
                cmpRows.Add(row);
            }
            tables.Add(new("Comparison", ["Value (table / row / column)", .. runs.Select(r => r.Name)], cmpRows, ["Each cell is taken from that scenario's own result table; blank where the scenario failed or has no such cell."]));
        }
        var charts = new List<ToolChart>();
        if (chartKey is not null)
        {
            var first = runs.Select(r => r.Res.Charts.FirstOrDefault(c => c.Title.StartsWith(chartKey, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(c => c is not null);
            if (first is null) msgs.Add(CalcMessage.Warning("W-BS-004", $"No scenario drew a chart whose title starts with \"{chartKey}\"."));
            else
            {
                var series = new List<ChartSeries>();
                foreach (var r in runs)
                    if (r.Res.Charts.FirstOrDefault(c => c.Title.StartsWith(chartKey, StringComparison.OrdinalIgnoreCase)) is { } c)
                        foreach (var s in c.Series.Take(3)) series.Add(new($"{r.Name}: {s.Name}", s.Points, s.Dashed, -1, s.MarkersOnly));
                charts.Add(new ToolChart($"{first.Title}: every scenario", first.XLabel, first.YLabel, series) { LogX = first.LogX, LogY = first.LogY });
            }
        }
        msgs.Add(CalcMessage.Info("I-BS-001", $"Each scenario runs {tool.Name} with the base input and its overrides; \"key = value\" lines are replaced (Section.key for a key inside a section)."));
        return new ToolResult(title, [$"{runs.Count} scenarios of {tool.Name}; {runs.Count(r => r.Res.Succeeded)} succeeded."], tables, msgs) { Charts = charts };
    }

    /// <summary>Replaces the value of "key = …" in the input (top level, or "Section.key" inside [Section]); appends it when absent. Returns false when appended.</summary>
    public static bool TrySet(ref string text, string key, string value)
    {
        string section = "", k = key.Trim();
        int dot = k.IndexOf('.');
        if (dot > 0) { section = k[..dot].Trim(); k = k[(dot + 1)..].Trim(); }
        var lines = text.Replace("\r", "").Split('\n').ToList();
        string cur = "";
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) { cur = t[1..^1].Trim(); continue; }
            if (!cur.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            int eq = t.IndexOf('=');
            if (eq > 0 && !t.StartsWith('#') && t[..eq].Trim().Equals(k, StringComparison.OrdinalIgnoreCase))
            {
                int hash = t.IndexOf(" #", eq, StringComparison.Ordinal);
                lines[i] = $"{t[..eq].Trim()} = {value}" + (hash >= 0 ? "  " + t[(hash + 1)..] : "");
                text = string.Join("\n", lines);
                return true;
            }
        }
        // Not found: add it at the top (top-level key) or right after the section heading.
        int at = 0;
        if (section.Length > 0)
        {
            int h = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
            if (h < 0) { lines.Add($"[{section}]"); h = lines.Count - 1; }
            at = h + 1;
        }
        lines.Insert(at, $"{k} = {value}");
        text = string.Join("\n", lines);
        return false;
    }

    /// <summary>The cell of a result table whose title, first-column row label and column header start with the given texts (ignoring case).</summary>
    public static object? Cell(ToolResult r, string table, string row, string col)
    {
        var t = r.Tables.FirstOrDefault(x => x.Title.StartsWith(table, StringComparison.OrdinalIgnoreCase));
        if (t is null) return null;
        int c = t.Headers.ToList().FindIndex(h => h.StartsWith(col, StringComparison.OrdinalIgnoreCase));
        if (c < 0) return null;
        var rr = t.Rows.FirstOrDefault(x => x.Count > 0 && DesignTools.Cell(x[0]).StartsWith(row, StringComparison.OrdinalIgnoreCase));
        return rr is not null && c < rr.Count ? rr[c] : null;
    }
}
