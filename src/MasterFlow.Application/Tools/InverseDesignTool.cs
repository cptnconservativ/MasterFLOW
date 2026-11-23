using System.Globalization;
using System.Text.RegularExpressions;
using MasterFlow.Application.Reports;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Inverse design (premium, D-240). Instead of "check this design", answers "how far can this design input go": the
/// largest (or smallest) value of one input of a design tool or storm network for which every stated criterion still
/// holds. The input is varied over a range, the tool or network is re-run at each step, the crossing is bracketed on a
/// grid and refined by bisection, and the governing criterion and the margin of every criterion are reported.
/// </summary>
public sealed partial class InverseDesignTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Id => "inverse-design";
    public string Name => "Inverse design (largest or smallest value that still meets criteria)";
    public string Description => "Answers the design question backwards: the largest contributing area, the smallest orifice, the most flow a pipe can take, the highest tailwater, the least pipe size, that still meets every criterion you list. Varies one input of any design tool (a key, or a cell of an input table) or of a storm network (pipe flow, pipe size, structure inflow, rim, n), re-runs it across the range, brackets the limit and refines it by bisection. Reports the answer, the governing criterion, the margin of every criterion and a utilization chart.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context) => """
        # Inverse design: the largest (find = max) or smallest (find = min) value of one input for which every
        # constraint holds. The input is varied from "from" to "to", the tool is re-run at each step, and the limit is
        # refined by bisection to the tolerance.
        #   vary = <key>                          a "key = value" line of the tool's input (Section.key inside a section)
        #   vary = <Section> | <row> | <column>   a cell of an input table (row by its first cell, column by its header)
        #   vary = network: <name> | <pipe or structure> | q, size (in.), n, inflow or rim
        # Constraints use the live value references of the drainage report: {value: tool | table | row | column} or
        # {network: name | item | field}, then <=, <, >=, > and a number, or = and a text such as OK or PASS.
        # The tool's input is the one saved in the project, or the one between [Base input] and [End base input].
        tool = detention-routing
        vary = Outlets | Low-flow orifice | Size
        find = max
        from = 0.25
        to = 2.0
        steps = 14
        tolerance = 0.005

        [Constraints]
        {value: detention-routing | Routing results | 2-yr | Peak outflow} <= 4
        {value: detention-routing | Routing results | 10-yr | Peak outflow} <= 8
        {value: detention-routing | Routing results | 100-yr | Peak outflow} <= 16
        {value: detention-routing | Routing results | max | Drain time} >= 12
        """;

    private sealed record Constraint(string Reference, string Op, double? Limit, string? Text, string Line);
    private sealed record Trial(double X, bool Feasible, List<(Constraint C, LiveValue V, bool Ok, double? Use)> Checks, string? Error);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Inverse design";
        string raw = (input ?? "").Replace("\r", "");
        string? baseInput = null;
        var bm = RegexBase().Match(raw);
        if (bm.Success)
        {
            var em = RegexEndBase().Match(raw[(bm.Index + bm.Length)..]);
            int end = em.Success ? bm.Index + bm.Length + em.Index : raw.Length;
            baseInput = raw[(bm.Index + bm.Length)..end].TrimStart('\n');
            raw = raw[..bm.Index] + (em.Success ? raw[(end + em.Length)..] : "");
        }
        var p = ToolInput.Parse(raw);
        var msgs = new List<CalcMessage>();
        var constraints = Constraints(raw, msgs);
        string vary = p.Text("vary") ?? "";
        string find = (p.Text("find") ?? "max").Trim().ToLowerInvariant();
        double from = p.Number("from", double.NaN), to = p.Number("to", double.NaN);
        int steps = (int)Math.Round(p.Number("steps", 12));
        double tol = p.Number("tolerance", double.NaN);
        string toolId = (p.Text("tool") ?? "").Trim();
        bool networkVary = vary.StartsWith("network:", StringComparison.OrdinalIgnoreCase);

        if (vary.Length == 0) msgs.Add(CalcMessage.Error("E-ID-001", "Name the input to vary: vary = <key>, vary = <Section> | <row> | <column>, or vary = network: <name> | <item> | <field>."));
        if (find is not "max" and not "min") msgs.Add(CalcMessage.Error("E-ID-002", "find = max or find = min."));
        if (!double.IsFinite(from) || !double.IsFinite(to) || from == to) msgs.Add(CalcMessage.Error("E-ID-003", "Give the range to search: from = and to = (different numbers)."));
        if (steps < 2 || steps > 100) msgs.Add(CalcMessage.Error("E-ID-004", "steps must be 2 to 100."));
        if (constraints.Count == 0) msgs.Add(CalcMessage.Error("E-ID-005", "List at least one constraint under [Constraints], e.g. {value: detention-routing | Routing results | 100-yr | Peak outflow} <= 16."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);
        if (from > to) (from, to) = (to, from);
        if (!double.IsFinite(tol) || tol <= 0) tol = (to - from) / 1000;

        // What is varied, as a function x -> (context, tool runner).
        IDesignTool? tool = null;
        string? toolBase = null;
        if (toolId.Length > 0)
        {
            tool = DesignTools.Find(toolId);
            if (tool is null || tool is InverseDesignTool) return ToolResult.Failed(title, CalcMessage.Error("E-ID-006", $"tool = {toolId}: no design tool has that id."));
            if (context.MayRun is { } may && !may(tool)) return ToolResult.Failed(title, CalcMessage.Error("E-ID-007", $"{tool.Name} is not covered by your license."));
            toolBase = baseInput ?? (context.ToolInputs.TryGetValue(tool.Id, out var saved) && !string.IsNullOrWhiteSpace(saved) ? saved : null);
            if (toolBase is null) { toolBase = tool.SampleInput(context); msgs.Add(CalcMessage.Info("I-ID-001", $"{tool.Name} has no saved input in the project, so its sample input is used.")); }
        }
        else if (!networkVary) return ToolResult.Failed(title, CalcMessage.Error("E-ID-006", "Name the design tool whose input is varied: tool = <id>."));

        Func<double, (DesignToolContext Ctx, string? Error)> apply;
        string what, unit = "";
        double? current = null;
        if (networkVary)
        {
            var parts = vary[(vary.IndexOf(':') + 1)..].Split('|').Select(s => s.Trim()).ToArray();
            if (parts.Length != 3) return ToolResult.Failed(title, CalcMessage.Error("E-ID-001", "vary = network: <name> | <pipe or structure> | <field>."));
            var idx = context.Networks.Select((n, i) => (n, i)).Where(x => parts[0].Length == 0 || x.n.Network.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToList();
            if (idx.Count != 1) return ToolResult.Failed(title, CalcMessage.Error("E-ID-008", idx.Count == 0 ? $"No storm network is named \"{parts[0]}\"." : "Several storm networks: name one."));
            var net = context.Networks[idx[0]].Network;
            string field = parts[2].ToLowerInvariant();
            bool si = net.Units == UnitSystem.SI;
            var pipe = net.Pipes.FirstOrDefault(x => x.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            var str = net.Structures.FirstOrDefault(x => x.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            Func<double, StormNetwork>? change = field switch
            {
                "q" when pipe is not null => x => net with { Pipes = [.. net.Pipes.Select(q => q == pipe ? q with { Discharge = x } : q)] },
                "size" when pipe is not null => x => net with { Pipes = [.. net.Pipes.Select(q => q == pipe ? q with { Rise = x / (si ? 1000 : 12), Span = pipe.Shape == NetworkPipeShape.Circular ? x / (si ? 1000 : 12) : pipe.Span * x / (si ? 1000 : 12) / pipe.Rise } : q)] },
                "n" when pipe is not null => x => net with { Pipes = [.. net.Pipes.Select(q => q == pipe ? q with { ManningN = x } : q)] },
                "inflow" when str is not null => x => net with { Structures = [.. net.Structures.Select(s => s == str ? s with { Inflow = x } : s)] },
                "rim" when str is not null => x => net with { Structures = [.. net.Structures.Select(s => s == str ? s with { RimElevation = x } : s)] },
                _ => null,
            };
            if (change is null) return ToolResult.Failed(title, CalcMessage.Error("E-ID-009", $"vary = {vary}: use a pipe with q, size or n, or a structure with inflow or rim ({net.Name} has pipes {string.Join(", ", net.Pipes.Select(x => x.Name))} and structures {string.Join(", ", net.Structures.Select(x => x.Name))})."));
            current = field switch { "q" => pipe!.Discharge, "size" => pipe!.Rise * (si ? 1000 : 12), "n" => pipe!.ManningN, "inflow" => str!.Inflow, "rim" => str!.RimElevation, _ => null };
            unit = field switch { "q" or "inflow" => si ? "m³/s" : "cfs", "size" => si ? "mm" : "in.", "rim" => si ? "m" : "ft", _ => "" };
            what = $"{net.Name} {parts[1]} {field}";
            int at = idx[0];
            apply = x =>
            {
                var n2 = change(x);
                var r2 = StormNetworkSolver.Solve(n2);
                var list = context.Networks.ToList();
                list[at] = (n2, r2);
                bool selected = context.Network?.Name == net.Name;
                return (context with { Networks = list, Network = selected ? n2 : context.Network, NetworkResult = selected ? r2 : context.NetworkResult }, null);
            };
        }
        else
        {
            var parts = vary.Split('|').Select(s => s.Trim()).ToArray();
            if (parts.Length != 1 && parts.Length != 3) return ToolResult.Failed(title, CalcMessage.Error("E-ID-001", "vary = <key>, or vary = <Section> | <row> | <column>."));
            string Set(double x)
            {
                string t = toolBase!, v = x.ToString("0.######", Inv);
                if (parts.Length == 1) { BatchScenarioTool.TrySet(ref t, parts[0], v); return t; }
                return SetCell(t, parts[0], parts[1], parts[2], v) ?? throw new ArgumentException();
            }
            try { _ = Set(from); }
            catch (ArgumentException) { return ToolResult.Failed(title, CalcMessage.Error("E-ID-010", $"vary = {vary}: the input has no [{parts[0]}] table with a row \"{parts[1]}\" and a column starting with \"{parts[^1]}\".")); }
            current = parts.Length == 1 ? ToolInput.Parse(toolBase!).OptionalNumber(parts[0]) : CellNumber(toolBase!, parts[0], parts[1], parts[2]);
            if (parts.Length == 1 && !ToolInput.Parse(toolBase!).Has(parts[0])) msgs.Add(CalcMessage.Warning("W-ID-001", $"The input has no \"{parts[0]} =\" line; it is added for each trial. Check the key is one {tool!.Name} reads."));
            what = parts.Length == 1 ? parts[0] : $"{parts[1]} {parts[2]}";
            if (parts.Length == 3 && RegexUnit().Match(Header(toolBase!, parts[0], parts[2]) ?? "") is { Success: true } um) unit = um.Groups[1].Value;
            apply = x => (context with { ToolInputs = new Dictionary<string, string>(context.ToolInputs) { [tool!.Id] = Set(x) } }, null);
        }

        // One trial: re-run, resolve every constraint.
        var trials = new List<Trial>();
        Trial Eval(double x)
        {
            var (ctx, err) = apply(x);
            if (err is not null) return new(x, false, [], err);
            var cache = new Dictionary<string, (IDesignTool, ToolResult)?>(StringComparer.OrdinalIgnoreCase);
            (IDesignTool, ToolResult)? Run(string id)
            {
                if (cache.TryGetValue(id, out var hit)) return hit;
                var t = DesignTools.Find(id);
                (IDesignTool, ToolResult)? res = null;
                if (t is not null && t is not InverseDesignTool && ctx.ToolInputs.TryGetValue(id, out var inp) && (ctx.MayRun is not { } m || m(t)))
                {
                    try { res = (t, t.Run(inp, ctx)); }
                    catch (Exception ex) { res = (t, ToolResult.Failed(t.Name, CalcMessage.Error("E-TL-999", ex.Message))); }
                }
                return cache[id] = res;
            }
            var checks = new List<(Constraint, LiveValue, bool, double?)>();
            foreach (var c in constraints)
            {
                var v = LiveValues.Resolve(c.Reference, Run, ctx.Networks);
                bool ok; double? use = null;
                if (!v.Resolved) ok = false;
                else if (c.Limit is { } lim)
                {
                    double n = v.Number ?? double.NaN;
                    ok = c.Op switch { "<=" => n <= lim, "<" => n < lim, ">=" => n >= lim, _ => n > lim };
                    // Utilization: 1.0 at the limit, above 1 fails.
                    use = c.Op.StartsWith('<') ? (lim != 0 ? n / lim : n + 1) : (n != 0 ? lim / n : lim + 1);
                    if (c.Op.StartsWith('<') && lim < 0 || c.Op.StartsWith('>') && n < 0) use = null;
                }
                else ok = string.Equals(v.Text, c.Text, StringComparison.OrdinalIgnoreCase);
                checks.Add((c, v, ok, use));
            }
            var tr = new Trial(x, checks.All(k => k.Item3), checks, checks.Where(k => !k.Item2.Resolved).Select(k => $"{k.Item1.Reference}: {k.Item2.Problem}").FirstOrDefault());
            trials.Add(tr);
            return tr;
        }

        // Grid scan, then bisection at the crossing nearest the end being sought.
        var grid = Enumerable.Range(0, steps + 1).Select(i => Eval(from + (to - from) * i / steps)).ToList();
        if (grid.All(g => g.Error is not null))
            return ToolResult.Failed(title, CalcMessage.Error("E-ID-011", "No constraint could be evaluated: " + grid[0].Error + "."));
        int flips = Enumerable.Range(1, grid.Count - 1).Count(i => grid[i].Feasible != grid[i - 1].Feasible);
        double? answer = null;
        string verdict;
        if (grid.All(g => g.Feasible))
        {
            answer = find == "max" ? to : from;
            verdict = $"Every value from {F(from)} to {F(to)} meets all constraints; the {find} is beyond the range searched. Widen the range.";
            msgs.Add(CalcMessage.Warning("W-ID-002", verdict));
        }
        else if (grid.All(g => !g.Feasible))
        {
            verdict = $"No value from {F(from)} to {F(to)} meets all constraints.";
            msgs.Add(CalcMessage.Warning("W-ID-003", verdict + " " + Governing(grid[find == "max" ? 0 : ^1])));
        }
        else
        {
            // max: the last feasible grid point that has an infeasible point above it; min: mirror.
            int i = find == "max" ? grid.FindLastIndex(g => g.Feasible) : grid.FindIndex(g => g.Feasible);
            int j = find == "max" ? i + 1 : i - 1;
            double ok = grid[i].X, bad = j >= 0 && j < grid.Count ? grid[j].X : double.NaN;
            if (double.IsNaN(bad)) { answer = ok; verdict = $"The {find} is at the end of the range searched ({F(ok)}); widen the range."; msgs.Add(CalcMessage.Warning("W-ID-002", verdict)); }
            else
            {
                for (int k = 0; k < 60 && Math.Abs(bad - ok) > tol; k++)
                {
                    double mid = (ok + bad) / 2;
                    if (Eval(mid).Feasible) ok = mid; else bad = mid;
                }
                answer = ok;
                verdict = $"{(find == "max" ? "Largest" : "Smallest")} {what} that meets every constraint: {F(ok)}{(unit.Length > 0 ? " " + unit : "")}.";
            }
            if (flips > 1) msgs.Add(CalcMessage.Warning("W-ID-004", $"The constraints switch between met and not met {flips} times across the range, so there is more than one feasible band; the answer is the {find} end of the {(find == "max" ? "highest" : "lowest")} band. See the trials table."));
        }

        Trial? atAnswer = answer is { } a ? trials.Where(t => t.X == a).LastOrDefault() ?? Eval(a) : null;
        Trial? beyond = answer is { } a2 ? trials.Where(t => !t.Feasible && (find == "max" ? t.X > a2 : t.X < a2)).OrderBy(t => Math.Abs(t.X - a2)).FirstOrDefault() : null;

        var summary = new List<string> { verdict };
        if (current is { } cur && answer is { } ans)
            summary.Add($"The design now uses {F(cur)}{(unit.Length > 0 ? " " + unit : "")}: {(find == "max" ? (cur <= ans ? $"{F(ans - cur)} of room before a constraint fails" : $"{F(cur - ans)} over the limit") : (cur >= ans ? $"{F(cur - ans)} of room before a constraint fails" : $"{F(ans - cur)} below the limit"))}.");
        if (beyond is not null) summary.Add("Governing: " + Governing(beyond));

        var tables = new List<ToolTable>();
        if (atAnswer is not null)
            tables.Add(new ToolTable($"Constraints at {what} = {F(atAnswer.X)}", ["Constraint", "Limit", "Value", "Utilization", "Result", "Source"],
                atAnswer.Checks.Select(k => (IReadOnlyList<object?>)[k.C.Reference, k.C.Limit is { } l ? $"{k.C.Op} {F(l)}" : $"= {k.C.Text}", k.V.Resolved ? k.V.Text : k.V.Problem, k.Use is { } u ? $"{u:P0}" : "", k.Ok ? "OK" : "FAILS", k.V.Source]).ToList(),
                ["Utilization is value ÷ limit for ≤ and limit ÷ value for ≥: 100 % is at the limit."]));
        var ordered = trials.GroupBy(t => Math.Round(t.X, 9)).Select(g => g.Last()).OrderBy(t => t.X).ToList();
        tables.Add(new ToolTable("Trials", [what, .. constraints.Select((c, i) => $"C{i + 1}"), "All met"],
            ordered.Select(t => (IReadOnlyList<object?>)[t.X, .. t.Checks.Select(k => (object?)(k.V.Number ?? (object?)k.V.Text ?? "—")), t.Feasible ? "yes" : "no"]).ToList(),
            [.. constraints.Select((c, i) => $"C{i + 1}: {c.Line}")]));

        var charts = new List<ToolChart>();
        var series = constraints.Select((c, i) => new ChartSeries($"C{i + 1}", ordered.Select(t => (t.X, t.Checks[i].Use ?? double.NaN)).Where(q => double.IsFinite(q.Item2)).ToList())).Where(s => s.Points.Count > 1).ToList();
        if (series.Count > 0)
            charts.Add(new ToolChart($"Utilization of each constraint vs {what}", what + (unit.Length > 0 ? $" ({unit})" : ""), "Utilization (1.0 = at the limit)", series,
                [new ChartLine("Limit", 1.0)]));
        msgs.Add(CalcMessage.Info("I-ID-002", $"{trials.Count} runs: a {steps}-step scan from {F(from)} to {F(to)}, then bisection to ±{F(tol)}. The search assumes each constraint changes in one direction with {what}; W-ID-004 warns when it does not."));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static string Governing(Trial t)
    {
        var fail = t.Checks.Where(k => !k.Ok).OrderByDescending(k => k.Use ?? double.MaxValue).FirstOrDefault();
        if (fail.C is null) return "";
        return $"{fail.C.Line} ({(fail.V.Resolved ? $"value {fail.V.Text}" : fail.V.Problem)} at {F(t.X)}).";
    }

    private static string F(double v) => DesignTools.Cell(v);

    private static List<Constraint> Constraints(string raw, List<CalcMessage> msgs)
    {
        var list = new List<Constraint>();
        bool inside = false;
        foreach (var l in raw.Split('\n'))
        {
            var line = l.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { inside = line[1..^1].Trim().Equals("Constraints", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inside || line.Length == 0 || line.StartsWith('#')) continue;
            var m = RegexConstraint().Match(line);
            if (!m.Success) { msgs.Add(CalcMessage.Error("E-ID-012", $"\"{line}\": write a constraint as {{value: …}} <= 16, {{network: …}} >= 1.0, or {{value: …}} = OK.")); continue; }
            string op = m.Groups[2].Value, rhs = m.Groups[3].Value.Trim();
            if (op == "=") list.Add(new(m.Groups[1].Value, op, null, rhs, line));
            else if (double.TryParse(rhs, NumberStyles.Float, Inv, out var lim)) list.Add(new(m.Groups[1].Value, op, lim, null, line));
            else msgs.Add(CalcMessage.Error("E-ID-012", $"\"{line}\": {op} needs a number after it."));
        }
        return list;
    }

    /// <summary>The input with one cell of a [Section] table replaced, or null when the table, row or column is not there.</summary>
    public static string? SetCell(string text, string section, string row, string column, string value)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var at = Locate(lines, section, row, column);
        if (at is not { } a) return null;
        var (li, ci, tab) = a;
        var cells = tab ? lines[li].Split('\t').ToList() : lines[li].Split(',').ToList();
        while (cells.Count <= ci) cells.Add("");
        cells[ci] = tab ? value : " " + value;
        lines[li] = string.Join(tab ? "\t" : ",", cells);
        return string.Join("\n", lines);
    }

    private static double? CellNumber(string text, string section, string row, string column)
    {
        var lines = text.Replace("\r", "").Split('\n');
        if (Locate(lines, section, row, column) is not { } a) return null;
        var cells = a.Tab ? lines[a.Line].Split('\t') : lines[a.Line].Split(',');
        return a.Col < cells.Length && double.TryParse(cells[a.Col].Trim(), NumberStyles.Float, Inv, out var v) ? v : null;
    }

    private static string? Header(string text, string section, string column)
    {
        var lines = text.Replace("\r", "").Split('\n');
        int s = Array.FindIndex(lines, l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        var h = lines.Skip(s + 1).FirstOrDefault(l => l.Trim().Length > 0 && !l.Trim().StartsWith('#'));
        if (s < 0 || h is null) return null;
        var cells = (h.Contains('\t') ? h.Split('\t') : h.Split(',')).Select(c => c.Trim()).ToArray();
        return cells.FirstOrDefault(c => c.Equals(column, StringComparison.OrdinalIgnoreCase)) ?? cells.FirstOrDefault(c => c.StartsWith(column, StringComparison.OrdinalIgnoreCase));
    }

    private static (int Line, int Col, bool Tab)? Locate(string[] lines, string section, string row, string column)
    {
        int s = Array.FindIndex(lines, l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (s < 0) return null;
        int header = -1, col = -1;
        for (int i = s + 1; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) break;
            if (t.Length == 0 || t.StartsWith('#')) continue;
            bool tab = lines[i].Contains('\t');
            var cells = (tab ? lines[i].Split('\t') : lines[i].Split(',')).Select(c => c.Trim()).ToArray();
            if (header < 0)
            {
                header = i;
                col = Array.FindIndex(cells, c => c.Equals(column, StringComparison.OrdinalIgnoreCase));
                if (col < 0) col = Array.FindIndex(cells, c => c.StartsWith(column, StringComparison.OrdinalIgnoreCase));
                if (col < 0) return null;
                continue;
            }
            if (cells[0].Equals(row, StringComparison.OrdinalIgnoreCase)) return (i, col, tab);
        }
        return null;
    }

    [GeneratedRegex(@"^(\{[^{}]+\})\s*(<=|>=|<|>|=)\s*(.+)$")]
    private static partial Regex RegexConstraint();
    [GeneratedRegex(@"\(([^()]+)\)\s*$")]
    private static partial Regex RegexUnit();
    [GeneratedRegex(@"(?im)^[ \t]*\[Base input\][ \t]*$")]
    private static partial Regex RegexBase();
    [GeneratedRegex(@"(?im)^[ \t]*\[End base input\][ \t]*$")]
    private static partial Regex RegexEndBase();
}
