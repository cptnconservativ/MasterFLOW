using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// "What changed?" redesign diff. Re-runs every calculation in two versions of a design (a baseline project file, a
/// baseline network in this project, or what-if edits typed against the current network) and lists only what moved:
/// pipes, structures, channels and tool results that now fail, that were fixed, or whose margin to a criterion grew or
/// shrank by more than a threshold. See docs/tools/redesign-diff.md; decisions D-210 to D-216.
/// </summary>
public sealed class RedesignDiffTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Id => "redesign-diff";
    public string Name => "What changed? Redesign diff";
    public string Description => "Re-runs every storm network, channel and design tool for a baseline and the current design and lists only what now fails, what was fixed, and where the margin to a criterion grew or shrank. The baseline is an earlier project file, another network in this project, or what-if edits (rims, inverts, sizes, flows) typed below.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context)
    {
        var net = context.Network;
        string s = net?.Structures.FirstOrDefault(x => x.RimElevation is not null && net.Pipes.Any(p => p.From == x.Name))?.Name ?? "MH-1";
        string pipe = net?.Pipes.FirstOrDefault()?.Name ?? "P-1";
        return $"""
            # Baseline: one of
            #   baseline = C:\Projects\Job\Job 2026-10-01.mfproj   (an earlier copy of this project; every network, channel and tool is compared)
            #   baseline network = Main St (v1)                     (another network in this project, compared with the selected one)
            # or neither, with [What if] edits applied to the selected network (the site plan moved: rims, inverts, flows).
            margin change = 10        # % of the limit: smaller margin changes are not listed
            show unchanged = no       # yes lists every check, changed or not
            tools = yes               # re-run every design tool saved in both versions (baseline file only)
            [What if]
            Item, Field, Value
            # Fields: rim, rim change, inflow, k (structures); rise (in), span (in), length, us invert, ds invert, invert change, n, q, q factor, minor q (pipes);
            # tailwater (network, Item = network). Item * = every structure or pipe.
            {s}, rim change, -0.75
            *, q factor, 1.15
            {pipe}, rise, {((net?.Pipes.FirstOrDefault()?.Rise ?? 1.5) * 12).ToString("0", Inv)}
            """;
    }

    /// <summary>One check on one element in one version: value, limit, and which side of the limit passes.</summary>
    private sealed record Check(string Element, string Name, string Reference, double Value, double Limit, bool Upper, string Unit)
    {
        /// <summary>Distance to the limit on the passing side (negative = fails).</summary>
        public double Margin => Upper ? Limit - Value : Value - Limit;
        public bool Passes => Margin >= -1e-9;
        public string Key => Element + "|" + Name;
    }

    private sealed record Change(int Rank, string Status, string Element, string Check, string Reference, double? Before, double? After, double? Limit, string Unit, double? MarginBefore, double? MarginAfter, string Note = "");

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "What changed? Redesign diff";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        double threshold = p.Number("margin change", 10) / 100;
        bool showAll = p.Flag("show unchanged", false), runTools = p.Flag("tools", true);

        // ---------- The two versions ----------
        string mode;
        var pairs = new List<(string Name, StormNetwork? Before, StormNetwork? After)>();
        IReadOnlyList<ChannelCalculation> calcBefore = [], calcAfter = [];
        IReadOnlyDictionary<string, string> toolsBefore = new Dictionary<string, string>(), toolsAfter = new Dictionary<string, string>();
        Project? baseProject = null;
        var edits = new List<IReadOnlyList<object?>>();
        if (p.Text("baseline") is { } path)
        {
            if (context.LoadProject is null) return ToolResult.Failed(title, CalcMessage.Error("E-RD-001", "Opening a baseline project file is not available here."));
            try { baseProject = context.LoadProject(path); }
            catch (Exception ex) { return ToolResult.Failed(title, CalcMessage.Error("E-RD-002", $"Could not open the baseline \"{path}\": {ex.Message}")); }
            mode = $"baseline project \"{Path.GetFileName(path)}\" ({baseProject.Info.Name}) against the open project";
            var current = context.Networks.Select(n => n.Network).ToList();
            if (current.Count == 0 && context.Network is not null) current.Add(context.Network);
            foreach (var name in baseProject.Networks.Select(n => n.Name).Union(current.Select(n => n.Name), StringComparer.OrdinalIgnoreCase))
                pairs.Add((name, baseProject.Networks.FirstOrDefault(n => Same(n.Name, name)), current.FirstOrDefault(n => Same(n.Name, name))));
            calcBefore = baseProject.Calculations; calcAfter = context.Calculations;
            toolsBefore = baseProject.ToolInputs; toolsAfter = context.ToolInputs;
        }
        else if (p.Text("baseline network") is { } bn)
        {
            var all = context.Networks.Select(n => n.Network).ToList();
            var b = all.FirstOrDefault(n => Same(n.Name, bn));
            string? cn = p.Text("current network");
            var a = cn is null ? context.Network : all.FirstOrDefault(n => Same(n.Name, cn));
            if (b is null) return ToolResult.Failed(title, CalcMessage.Error("E-RD-003", $"No storm network named \"{bn}\" in the project (or it has input errors)."));
            if (a is null) return ToolResult.Failed(title, CalcMessage.Error("E-RD-004", cn is null ? "Select the current storm network, or name it with current network = …" : $"No storm network named \"{cn}\"."));
            mode = $"network \"{b.Name}\" (baseline) against \"{a.Name}\"";
            pairs.Add(($"{a.Name}", b, a));
        }
        else
        {
            if (context.Network is not { } net) return ToolResult.Failed(title, CalcMessage.Error("E-RD-005", "Select a storm network (or give baseline = a project file) to compare."));
            if (!p.HasSection("What if") || p.Rows("What if").Count == 0)
                return ToolResult.Failed(title, CalcMessage.Error("E-RD-006", "Give a baseline (baseline = file, or baseline network = name) or list [What if] edits to apply to the selected network."));
            var (edited, list) = ApplyEdits(net, p, msgs);
            edits = list;
            mode = $"network \"{net.Name}\" as entered (baseline) against the [What if] edits";
            pairs.Add((net.Name, net, edited));
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        // ---------- Networks ----------
        var changes = new List<Change>();
        var inputDiffs = new List<IReadOnlyList<object?>>(edits);
        var charts = new List<ToolChart>();
        int compared = 0;
        foreach (var (name, before, after) in pairs)
        {
            if (before is null) { changes.Add(new(5, "ADDED", $"network {name}", "whole network", "", null, null, null, "", null, null, "not in the baseline")); continue; }
            if (after is null) { changes.Add(new(5, "REMOVED", $"network {name}", "whole network", "", null, null, null, "", null, null, "not in the current design")); continue; }
            var rb = StormNetworkSolver.Solve(before);
            var ra = StormNetworkSolver.Solve(after);
            if (!rb.Succeeded || !ra.Succeeded)
            {
                msgs.Add(CalcMessage.Warning("W-RD-001", $"Network \"{name}\": the {(rb.Succeeded ? "current" : "baseline")} version does not solve ({string.Join("; ", (rb.Succeeded ? ra : rb).Messages.Where(m => m.Severity == MessageSeverity.Error).Select(m => m.Text).Take(2))}); it was not compared."));
                continue;
            }
            compared++;
            var cb = NetworkChecks(before, rb, context).ToDictionary(c => c.Key);
            var ca = NetworkChecks(after, ra, context).ToDictionary(c => c.Key);
            Compare(cb, ca, threshold, showAll, changes, prefix: name + ": ");
            inputDiffs.AddRange(NetworkInputDiffs(name, before, after));
            // Plan-review findings (sizes, spacing, crowns, cover): only the FAIL set is compared.
            var fb = PlanReviewFails(before, rb); var fa = PlanReviewFails(after, ra);
            foreach (var k in fa.Keys.Except(fb.Keys)) changes.Add(new(0, "NEW FAIL", name + ": " + fa[k].Item, fa[k].Check, fa[k].Ref, null, D(fa[k].Value), D(fa[k].Limit), "", null, null, "IDCM §4.6 plan-review item"));
            foreach (var k in fb.Keys.Except(fa.Keys)) changes.Add(new(3, "FIXED", name + ": " + fb[k].Item, fb[k].Check, fb[k].Ref, D(fb[k].Value), null, D(fb[k].Limit), "", null, null, after.Pipes.Any(x => fb[k].Item.StartsWith(x.Name + " ")) || after.Structures.Any(s => s.Name == fb[k].Item) ? "IDCM §4.6 plan-review item" : "element removed"));
            // Chart: HGL clearance by structure, before and after.
            var names = before.Structures.Select(s => s.Name).Intersect(after.Structures.Select(s => s.Name)).ToList();
            double? Clear(StormNetworkResult r, string s) => r.Structure(s)?.HglClearance;
            var cl = names.Select((s, i) => (i, Clear(rb, s), Clear(ra, s))).Where(x => x.Item2 is not null && x.Item3 is not null).ToList();
            if (cl.Count > 0)
                charts.Add(new ToolChart($"{name}: HGL clearance below the rim", "Structure (order as listed: " + string.Join(", ", cl.Take(12).Select(x => names[x.i])) + (cl.Count > 12 ? " …" : "") + ")", "Rim − HGL (ft)",
                    [new ChartSeries("Baseline", cl.Select(x => ((double)x.i + 1, x.Item2!.Value)).ToList(), MarkersOnly: true),
                     new ChartSeries("Current", cl.Select(x => ((double)x.i + 1, x.Item3!.Value)).ToList(), MarkersOnly: true, Color: 3)],
                    after.Criteria.HglBelowRim is { } h ? [new ChartLine($"criterion {h:0.##} ft", h)] : null));
        }

        // ---------- Channel and pipe calculations ----------
        if (baseProject is not null)
        {
            foreach (var a in calcAfter)
            {
                var b = calcBefore.FirstOrDefault(x => x.Id == a.Id) ?? calcBefore.FirstOrDefault(x => Same(x.Title, a.Title));
                if (b is null) { changes.Add(new(5, "ADDED", $"calculation {a.Title}", "whole calculation", "", null, null, null, "", null, null)); continue; }
                var cb = CalcChecks(b).ToDictionary(c => c.Key); var ca = CalcChecks(a).ToDictionary(c => c.Key);
                Compare(cb, ca, threshold, showAll, changes, prefix: "");
            }
            foreach (var b in calcBefore.Where(b => !calcAfter.Any(a => a.Id == b.Id || Same(a.Title, b.Title))))
                changes.Add(new(5, "REMOVED", $"calculation {b.Title}", "whole calculation", "", null, null, null, "", null, null));
        }

        // ---------- Design tools ----------
        var toolRows = new List<IReadOnlyList<object?>>();
        if (baseProject is not null && runTools)
        {
            var ctxB = context with { Network = baseProject.Networks.FirstOrDefault(n => Same(n.Name, context.Network?.Name ?? "")) ?? baseProject.Networks.FirstOrDefault(), NetworkResult = null };
            ctxB = ctxB with { NetworkResult = ctxB.Network is null ? null : StormNetworkSolver.Solve(ctxB.Network), Calculations = baseProject.Calculations, ToolInputs = baseProject.ToolInputs,
                Networks = baseProject.Networks.Select(n => (n, (StormNetworkResult?)StormNetworkSolver.Solve(n))).ToList() };
            foreach (var id in toolsAfter.Keys.Intersect(toolsBefore.Keys).Where(i => i != Id && i != "drainage-report" && i != "batch-scenarios").OrderBy(i => i))
            {
                if (DesignTools.Find(id) is not { } tool) continue;
                if (context.MayRun is { } may && !may(tool)) { msgs.Add(CalcMessage.Info("I-RD-002", $"{tool.Name} was not compared: it needs a premium license feature this key does not include.")); continue; }
                ToolResult tb, ta;
                try { tb = tool.Run(toolsBefore[id], ctxB); ta = tool.Run(toolsAfter[id], context); }
                catch (Exception ex) { msgs.Add(CalcMessage.Warning("W-RD-002", $"{tool.Name} could not be re-run: {ex.Message}")); continue; }
                toolRows.AddRange(ToolDiff(tool.Name, tb, ta, threshold, changes));
            }
            foreach (var id in toolsAfter.Keys.Except(toolsBefore.Keys)) if (DesignTools.Find(id) is { } t && id != Id) changes.Add(new(5, "ADDED", $"tool {t.Name}", "input saved only in the current design", "", null, null, null, "", null, null));
        }

        // ---------- Output ----------
        var ordered = changes.OrderBy(c => c.Rank).ThenBy(c => c.Element, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Check).ToList();
        int nNew = changes.Count(c => c.Status == "NEW FAIL"), nWorse = changes.Count(c => c.Status == "STILL FAILS, WORSE"), nLess = changes.Count(c => c.Status == "LESS MARGIN"),
            nFixed = changes.Count(c => c.Status == "FIXED"), nMore = changes.Count(c => c.Status is "MORE MARGIN" or "STILL FAILS, BETTER");
        var summary = new List<string>
        {
            $"Compared {mode}.",
            nNew + nWorse + nLess == 0
                ? $"Nothing got worse. {nFixed} fixed, {nMore} with more margin."
                : $"{nNew} new failure(s), {nWorse} existing failure(s) worse, {nLess} with less margin; {nFixed} fixed, {nMore} with more margin.",
            $"Margin changes under {threshold * 100:0.#} % of the limit are not listed" + (showAll ? " (show unchanged = yes: every check is listed)." : "."),
        };
        var tables = new List<ToolTable>
        {
            new("Changes", ["Status", "Element", "Check", "Reference", "Before", "After", "Limit", "Unit", "Margin before", "Margin after", "Note"],
                ordered.Select(c => (IReadOnlyList<object?>)[c.Status, c.Element, c.Check, c.Reference, R(c.Before), R(c.After), R(c.Limit), c.Unit, R(c.MarginBefore), R(c.MarginAfter), c.Note]).ToList(),
                ["Margin = distance from the value to the limit on the passing side; negative fails. Order: new failures, worse failures, less margin, fixed, more margin, added/removed.",
                 "Networks are re-solved from their inputs; checks use each network's own criteria (Rapid City IDCM 2012 §4.6 by default)."]),
        };
        if (inputDiffs.Count > 0) tables.Add(new("What was changed (inputs)", ["Network", "Item", "Field", "Before", "After"], inputDiffs));
        if (toolRows.Count > 0) tables.Add(new("Design tool results that moved", ["Tool", "Table", "Row", "Column", "Before", "After", "Change (%)"], toolRows,
            [$"Numbers that changed by more than {threshold * 100:0.#} %, and result words (FAIL, OK, NO-GO …) that changed. Up to 200 rows."]));
        if (compared == 0 && baseProject is null) msgs.Add(CalcMessage.Warning("W-RD-003", "No network could be compared."));
        if (nNew + nWorse > 0) msgs.Add(CalcMessage.Warning("W-RD-004", $"{nNew + nWorse} check(s) fail or fail worse after the change."));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    // ---------------- checks ----------------

    private static IEnumerable<Check> NetworkChecks(StormNetwork net, StormNetworkResult r, DesignToolContext ctx)
    {
        var c = net.Criteria;
        bool us = net.Units == UnitSystem.USCustomary;
        string L = us ? "ft" : "m", V = us ? "fps" : "m/s", Q = us ? "cfs" : "m³/s";
        double minCover = (us ? 1.5 : 0.45);
        foreach (var s in r.Structures.Where(s => !s.IsOutfall))
        {
            if (s.HglClearance is { } h) yield return new($"{s.Structure.Name}", "HGL below the rim", "IDCM §4.6.7", h, c.HglBelowRim ?? 0, false, L);
            if (s.EglClearance is { } e && c.EglBelowRim is { } el) yield return new($"{s.Structure.Name}", "EGL below the rim", "criteria", e, el, false, L);
        }
        foreach (var x in r.Pipes)
        {
            string e = x.Pipe.Name;
            bool lateral = !net.Pipes.Any(u => Same(u.To, x.Pipe.From));
            if (double.IsFinite(x.FullFlowCapacity) && x.FullFlowCapacity > 0 && x.Pipe.Discharge > 0)
                yield return new(e, "Design flow within full-flow capacity", "IDCM §4.6.1 (open channel)", x.Pipe.Discharge, x.FullFlowCapacity, true, Q);
            double vMax = Math.Max(x.VelocityUp, x.VelocityDown);
            if (c.MaxVelocity is { } vm && !lateral && double.IsFinite(vMax)) yield return new(e, "Maximum velocity", "IDCM Table 4-13", vMax, vm, true, V);
            if (c.MinVelocity is { } vn && double.IsFinite(x.MinorVelocity)) yield return new(e, $"Minimum velocity ({net.MinorStormName})", "IDCM §4.6.2(3)a", x.MinorVelocity, vn, false, V);
            if (c.MinPipeRise is { } mr) yield return new(e, "Minimum pipe size", "IDCM §4.6.2(2)", x.Pipe.Rise, mr, false, L);
            if (!c.AllowSurcharge) yield return new(e, "HGL at or below the crown (no surcharge)", "IDCM §4.6.1", Math.Max(x.HglUp - x.Pipe.UpstreamInvert, x.HglDown - x.Pipe.DownstreamInvert), x.Pipe.Rise, true, L);
            foreach (bool up in new[] { true, false })
            {
                var st = net.Structures.FirstOrDefault(s => Same(s.Name, up ? x.Pipe.From : x.Pipe.To));
                if (st?.RimElevation is { } rim) yield return new($"{e} at {st.Name}", "Cover over the crown", "IDCM §4.6.2(2)", rim - ((up ? x.Pipe.UpstreamInvert : x.Pipe.DownstreamInvert) + x.Pipe.Rise), minCover, false, L);
            }
        }
    }

    private static Dictionary<string, (string Item, string Check, string Ref, object? Value, object? Limit)> PlanReviewFails(StormNetwork net, StormNetworkResult r)
    {
        var tool = new PlanReviewTool();
        var res = tool.Run("bends = auto", new DesignToolContext(net.Units, net, r));
        var d = new Dictionary<string, (string, string, string, object?, object?)>();
        if (!res.Succeeded) return d;
        foreach (var row in res.Tables[0].Rows.Where(x => (string?)x[0] == "FAIL"))
        {
            string item = (string)row[3]!, check = (string)row[1]!;
            // HGL, velocity, cover and size are compared with their margins in NetworkChecks.
            if (check.StartsWith("HGL") || check.Contains("velocity", StringComparison.OrdinalIgnoreCase) || check.Contains("cover") || check.StartsWith("Minimum pipe size")) continue;
            // Items carry the pipe's end structures; key on the pipe or structure name so a renamed route still matches.
            string key = check + "|" + item.Split(' ')[0] + "|" + (item.Contains(" at ") ? item[(item.LastIndexOf(" at ", StringComparison.Ordinal) + 4)..] : "");
            d.TryAdd(key, (item, check, (string)row[2]!, row[4], row[5]));
        }
        return d;
    }

    private static IEnumerable<Check> CalcChecks(ChannelCalculation calc)
    {
        UniformFlowResult r;
        try { r = UniformFlowCalculator.Solve(calc.Input); } catch { yield break; }
        if (!r.Succeeded) yield break;
        string el = $"calculation {calc.Title}";
        foreach (var d in DesignChecks.Evaluate(calc.Criteria, r).Where(d => d.Status != CheckStatus.NotEvaluated))
        {
            bool upper = d.Symbol.Contains('≤');
            yield return new(el, d.Name, d.Basis.Split(';')[0], d.Actual, d.Limit, upper, d.Unit);
        }
        if (r.Conduit is { } cd && double.IsFinite(cd.FullFlowDischarge) && cd.FullFlowDischarge > 0)
            yield return new(el, "Discharge within full-flow capacity", "Manning", r.Discharge, cd.FullFlowDischarge, true, r.Constants.Discharge);
    }

    private static void Compare(Dictionary<string, Check> before, Dictionary<string, Check> after, double threshold, bool showAll, List<Change> changes, string prefix)
    {
        foreach (var (k, a) in after)
        {
            if (!before.TryGetValue(k, out var b))
            {
                if (!a.Passes) changes.Add(new(0, "NEW FAIL", prefix + a.Element, a.Name, a.Reference, null, a.Value, a.Limit, a.Unit, null, a.Margin, "new element"));
                else if (showAll) changes.Add(new(5, "ADDED", prefix + a.Element, a.Name, a.Reference, null, a.Value, a.Limit, a.Unit, null, a.Margin));
                continue;
            }
            double scale = Math.Max(Math.Abs(a.Limit), 1e-6);
            double d = (a.Margin - b.Margin) / scale;
            string status;
            int rank;
            if (b.Passes && !a.Passes) (status, rank) = ("NEW FAIL", 0);
            else if (!b.Passes && a.Passes) (status, rank) = ("FIXED", 3);
            else if (!a.Passes && d < -threshold) (status, rank) = ("STILL FAILS, WORSE", 1);
            else if (!a.Passes && d > threshold) (status, rank) = ("STILL FAILS, BETTER", 4);
            else if (d < -threshold) (status, rank) = ("LESS MARGIN", 2);
            else if (d > threshold) (status, rank) = ("MORE MARGIN", 4);
            else if (showAll) (status, rank) = (a.Passes ? "UNCHANGED" : "STILL FAILS", 6);
            else continue;
            changes.Add(new(rank, status, prefix + a.Element, a.Name, a.Reference, b.Value, a.Value, a.Limit, a.Unit, b.Margin, a.Margin,
                Math.Abs(b.Limit - a.Limit) > 1e-9 ? $"limit was {b.Limit:0.###}" : ""));
        }
        foreach (var (k, b) in before.Where(x => !after.ContainsKey(x.Key)))
            if (showAll || !b.Passes) changes.Add(new(5, "REMOVED", prefix + b.Element, b.Name, b.Reference, b.Value, null, b.Limit, b.Unit, b.Margin, null, b.Passes ? "" : "failed before"));
    }

    // ---------------- tool results ----------------

    private static readonly string[] Words = ["FAIL", "FAILS", "OK", "PASS", "REVIEW", "NO-GO", "GO", "GO WITH CONDITIONS", "YES", "NO", "FATAL", "CONSTRAINT"];

    private static IEnumerable<IReadOnlyList<object?>> ToolDiff(string tool, ToolResult b, ToolResult a, double threshold, List<Change> changes)
    {
        int n = 0;
        var errB = b.Messages.Where(m => m.Severity == MessageSeverity.Error).Select(m => m.Code).ToHashSet();
        foreach (var m in a.Messages.Where(m => m.Severity == MessageSeverity.Error && !errB.Contains(m.Code)))
            changes.Add(new(0, "NEW FAIL", $"tool {tool}", "error", m.Code, null, null, null, "", null, null, m.Text));
        foreach (var m in b.Messages.Where(m => m.Severity == MessageSeverity.Error && a.Messages.All(x => x.Code != m.Code)))
            changes.Add(new(3, "FIXED", $"tool {tool}", "error", m.Code, null, null, null, "", null, null, m.Text));
        foreach (var ta in a.Tables)
        {
            var tb = b.Tables.FirstOrDefault(t => t.Title == ta.Title);
            if (tb is null) continue;
            var rowsB = Keyed(tb.Rows);
            foreach (var (key, ra) in Keyed(ta.Rows))
            {
                if (!rowsB.TryGetValue(key, out var rb)) continue;
                for (int i = 1; i < Math.Min(ra.Count, Math.Min(rb.Count, ta.Headers.Count)); i++)
                {
                    if (n >= 200) yield break;
                    object? x = rb[i], y = ra[i];
                    if (x is double db && y is double da)
                    {
                        if (!double.IsFinite(db) || !double.IsFinite(da) || db == da) continue;
                        double rel = (da - db) / Math.Max(Math.Abs(db), 1e-9);
                        if (Math.Abs(rel) <= threshold || Math.Abs(da - db) < 1e-6) continue;
                        n++; yield return [tool, ta.Title, key, ta.Headers[i], db, da, Math.Round(rel * 100, 1)];
                    }
                    else if (x is string sb && y is string sa && sb != sa && (IsWord(sb) || IsWord(sa)))
                    {
                        n++; yield return [tool, ta.Title, key, ta.Headers[i], sb, sa, null];
                        bool worse = Bad(sa) && !Bad(sb), better = Bad(sb) && !Bad(sa);
                        if (worse || better) changes.Add(new(worse ? 0 : 3, worse ? "NEW FAIL" : "FIXED", $"tool {tool}", $"{ta.Title}: {key}", ta.Headers[i], null, null, null, "", null, null, $"{sb} → {sa}"));
                    }
                }
            }
        }
        static bool IsWord(string s) => Words.Any(w => s.StartsWith(w, StringComparison.OrdinalIgnoreCase));
        static bool Bad(string s) => s.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase) || s.StartsWith("NO-GO", StringComparison.OrdinalIgnoreCase) || s.StartsWith("FATAL", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IReadOnlyList<object?>> Keyed(IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var d = new Dictionary<string, IReadOnlyList<object?>>();
        foreach (var r in rows)
        {
            string k0 = DesignTools.Cell(r.Count > 0 ? r[0] : null), k = k0;
            if (d.ContainsKey(k) && r.Count > 1 && r[1] is string s2) k = k0 + " / " + s2;
            for (int i = 2; d.ContainsKey(k); i++) k = k0 + $" #{i}";
            d[k] = r;
        }
        return d;
    }

    // ---------------- inputs ----------------

    private static IEnumerable<IReadOnlyList<object?>> NetworkInputDiffs(string name, StormNetwork b, StormNetwork a)
    {
        const double eps = 1e-6;
        foreach (var sa in a.Structures)
        {
            var sb = b.Structures.FirstOrDefault(s => Same(s.Name, sa.Name));
            if (sb is null) { yield return [name, sa.Name, "structure", "—", "added"]; continue; }
            if (!Eq(sb.RimElevation, sa.RimElevation)) yield return [name, sa.Name, "rim", sb.RimElevation, sa.RimElevation];
            if (!Eq(sb.Inflow, sa.Inflow)) yield return [name, sa.Name, "inflow", sb.Inflow, sa.Inflow];
            if (!Eq(sb.JunctionK, sa.JunctionK)) yield return [name, sa.Name, "K", sb.JunctionK, sa.JunctionK];
        }
        foreach (var sb in b.Structures.Where(s => !a.Structures.Any(x => Same(x.Name, s.Name)))) yield return [name, sb.Name, "structure", "present", "removed"];
        foreach (var pa in a.Pipes)
        {
            var pb = b.Pipes.FirstOrDefault(x => Same(x.Name, pa.Name));
            if (pb is null) { yield return [name, pa.Name, "pipe", "—", "added"]; continue; }
            if (Math.Abs(pb.Rise - pa.Rise) > eps) yield return [name, pa.Name, "rise", pb.Rise, pa.Rise];
            if (Math.Abs(pb.Span - pa.Span) > eps) yield return [name, pa.Name, "span", pb.Span, pa.Span];
            if (Math.Abs(pb.Length - pa.Length) > eps) yield return [name, pa.Name, "length", pb.Length, pa.Length];
            if (Math.Abs(pb.UpstreamInvert - pa.UpstreamInvert) > eps) yield return [name, pa.Name, "upstream invert", pb.UpstreamInvert, pa.UpstreamInvert];
            if (Math.Abs(pb.DownstreamInvert - pa.DownstreamInvert) > eps) yield return [name, pa.Name, "downstream invert", pb.DownstreamInvert, pa.DownstreamInvert];
            if (Math.Abs(pb.ManningN - pa.ManningN) > eps) yield return [name, pa.Name, "n", pb.ManningN, pa.ManningN];
            if (Math.Abs(pb.Discharge - pa.Discharge) > eps) yield return [name, pa.Name, "Q", pb.Discharge, pa.Discharge];
            if (!(double.IsNaN(pb.MinorDischarge) && double.IsNaN(pa.MinorDischarge)) && Math.Abs(pb.MinorDischarge - pa.MinorDischarge) > eps) yield return [name, pa.Name, "minor Q", pb.MinorDischarge, pa.MinorDischarge];
            if (!Same(pb.From, pa.From) || !Same(pb.To, pa.To)) yield return [name, pa.Name, "route", $"{pb.From} → {pb.To}", $"{pa.From} → {pa.To}"];
        }
        foreach (var pb in b.Pipes.Where(x => !a.Pipes.Any(y => Same(y.Name, x.Name)))) yield return [name, pb.Name, "pipe", "present", "removed"];
        if (!Eq(b.TailwaterElevation, a.TailwaterElevation) || b.Tailwater != a.Tailwater)
            yield return [name, "network", "tailwater", $"{b.Tailwater} {b.TailwaterElevation}", $"{a.Tailwater} {a.TailwaterElevation}"];
        static bool Eq(double? x, double? y) => x is null ? y is null : y is not null && Math.Abs(x.Value - y.Value) < eps;
    }

    /// <summary>Applies [What if] rows (Item, Field, Value) to a copy of the network. Item * = every structure or pipe.</summary>
    private static (StormNetwork Net, List<IReadOnlyList<object?>> Edits) ApplyEdits(StormNetwork net, ToolInput p, List<CalcMessage> msgs)
    {
        var structs = net.Structures.ToList();
        var pipes = net.Pipes.ToList();
        double? tw = net.TailwaterElevation;
        var tailRule = net.Tailwater;
        double inch = net.Units == UnitSystem.USCustomary ? 12 : 1000;
        foreach (var row in p.Rows("What if"))
        {
            if (row.Length < 3) { msgs.Add(CalcMessage.Error("E-RD-010", $"What if: \"{string.Join(", ", row)}\" needs Item, Field, Value.")); continue; }
            string item = row[0], field = row[1].Trim().ToLowerInvariant();
            if (!ToolInput.TryNumber(row[2], out double v)) { msgs.Add(CalcMessage.Error("E-RD-011", $"What if {item} {field}: \"{row[2]}\" is not a number.")); continue; }
            bool all = item == "*";
            if (Same(item, "network") && field == "tailwater") { tw = v; tailRule = TailwaterRule.Elevation; continue; }
            bool structField = field is "rim" or "rim change" or "inflow" or "k", pipeField = !structField;
            int hits = 0;
            if (structField)
                for (int i = 0; i < structs.Count; i++)
                {
                    if (!all && !Same(structs[i].Name, item)) continue;
                    var s = structs[i];
                    structs[i] = field switch
                    {
                        "rim" => s with { RimElevation = v },
                        "rim change" => s.RimElevation is { } r ? s with { RimElevation = r + v } : s,
                        "inflow" => s with { Inflow = v },
                        _ => s with { JunctionK = v, LossMethod = StructureLossMethod.StandardK },
                    };
                    hits++;
                }
            if (pipeField)
                for (int i = 0; i < pipes.Count; i++)
                {
                    if (!all && !Same(pipes[i].Name, item)) continue;
                    var x = pipes[i];
                    NetworkPipe? y = field switch
                    {
                        "rise" or "diameter" or "size" => x with { Rise = v / inch, Span = x.Shape == NetworkPipeShape.Circular ? v / inch : x.Span },
                        "span" => x with { Span = v / inch },
                        "length" => x with { Length = v },
                        "us invert" or "upstream invert" => x with { UpstreamInvert = v },
                        "ds invert" or "downstream invert" => x with { DownstreamInvert = v },
                        "invert change" => x with { UpstreamInvert = x.UpstreamInvert + v, DownstreamInvert = x.DownstreamInvert + v },
                        "n" => x with { ManningN = v },
                        "q" => x with { Discharge = v },
                        "q factor" => x with { Discharge = x.Discharge * v, MinorDischarge = x.MinorDischarge * v },
                        "minor q" => x with { MinorDischarge = v },
                        _ => null,
                    };
                    if (y is null) { msgs.Add(CalcMessage.Error("E-RD-012", $"What if: unknown field \"{row[1]}\".")); break; }
                    pipes[i] = y; hits++;
                }
            if (field == "q factor")
                for (int i = 0; i < structs.Count; i++)
                    if (all || Same(structs[i].Name, item))
                        structs[i] = structs[i] with { Inflow = structs[i].Inflow * v, MinorInflow = structs[i].MinorInflow * v };
            if (hits == 0 && msgs.All(m => m.Code != "E-RD-012")) msgs.Add(CalcMessage.Error("E-RD-013", $"What if: no {(structField ? "structure" : "pipe")} named \"{item}\" in \"{net.Name}\"."));
        }
        return (net with { Structures = structs, Pipes = pipes, TailwaterElevation = tw, Tailwater = tailRule }, []);
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static double? D(object? o) => o is double d ? d : null;
    private static double? R(double? v) => v is { } x && double.IsFinite(x) ? Math.Round(x, 3) : null;
}
