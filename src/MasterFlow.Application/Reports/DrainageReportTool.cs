using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Reports;

/// <summary>
/// Automated drainage report builder (Premium). The input is the firm's report template: cover fields as
/// "key = value" lines, then a [Report] section of narrative text with headings (#, ##), bullets (-), {placeholders}
/// and @directives that pull in the project's calculations: every design tool's saved input is re-run, storm networks
/// are taken as solved, channel calculations are solved. The result carries a <see cref="ReportDocument"/> that the
/// report writers render as PDF, HTML or Word.
/// </summary>
public sealed partial class DrainageReportTool : IDesignTool
{
    public string Id => "drainage-report";
    public string Name => "Drainage report builder";
    public string Description => "Builds a complete storm water report from your template: cover, certification and stamp area, table of contents, narrative with project fields filled in, summaries of every design tool and storm network in the project, a compliance table of every criteria check, and landscape appendices with the full calculations and their inputs. Save as PDF, HTML or Word (.docx). The default template follows IDCM §1.15 and §4.1.8 (design inputs, outputs, assumptions and calculations).";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context) => DefaultTemplate;

    public const string DefaultTemplate = """
        # Cover fields. Placeholders in the text: {project.name} {project.number} {project.client} {project.preparedBy}
        # {project.checkedBy} {firm} {engineer} {license} {date}. Directives (one per line, in [Report]):
        #   @tool <tool id> summary | full | tables | charts | table "<title>"     @network all|<name> summary|full
        #   @compliance   @calculations   @inputs   @certification <statement>   @pagebreak   @appendices
        # Sections that end up with nothing under them are left out (omit empty sections = no keeps them).
        title = Storm Water Report
        subtitle = Prepared to the City of Rapid City Infrastructure Design Criteria Manual (2012)
        firm =
        engineer =
        license =
        date =
        table of contents = yes
        omit empty sections = yes

        [Report]
        # Engineer's certification
        @certification I hereby certify that this storm water report was prepared by me or under my direct supervision and that I am a duly licensed Professional Engineer under the laws of the State of South Dakota.

        # Introduction
        This report presents the storm water analysis and design for {project.name} for {project.client}. It addresses storm water conditions, impacts and design requirements in compliance with Section Four of the City of Rapid City Infrastructure Design Criteria Manual (IDCM, 2012), as required by IDCM §1.15, and contains the design input parameters, output, assumptions and calculations required by IDCM §4.1.8.

        ## Project description
        Describe the site, its location and size, and the proposed improvements.

        ## Existing conditions
        Describe existing drainage patterns, off-site flows, soils and the governing Drainage Basin Design Plan (IDCM §4.1.5).

        # Hydrology
        Runoff was computed by the methods listed below. Detailed calculations are in the appendices.
        @tool rational-rapid-city summary
        @tool hydrograph-routing summary

        # Storm sewer system
        The storm sewer hydraulic and energy grade lines were computed from the outfall upstream and checked against IDCM §4.6.
        @network all summary
        @tool inlet-network summary
        @tool gutter-inlets summary
        @tool street-capacity summary
        @tool network-plan-review summary

        # Detention
        @tool detention-routing summary

        # Culverts and open channels
        @tool hds5-culvert summary
        @tool backwater-profile summary
        @calculations summary

        # Compliance with the design criteria
        Every criteria check computed in this project is listed below; FAILS rows need a design change or a documented variance.
        @compliance

        # Conclusions
        State the conclusions: the proposed system conveys the design storms within the IDCM criteria, with any variances requested listed here.

        @appendices
        # Appendix A: Calculations
        @tool rational-rapid-city full
        @tool hydrograph-routing full
        @network all full
        @tool inlet-network full
        @tool gutter-inlets full
        @tool street-capacity full
        @tool network-plan-review full
        @tool detention-routing full
        @tool hds5-culvert full
        @tool backwater-profile full
        @tool quantity-takeoff full
        @calculations full

        # Appendix B: Calculation inputs
        @inputs
        """;

    private sealed class Run_(DesignToolContext ctx, List<CalcMessage> msgs)
    {
        private readonly Dictionary<string, ToolResult?> _results = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Source, ToolTable Table)> RunTables { get; } = [];

        /// <summary>The tool's result for its saved input, or null (no input, unknown tool, not licensed, failed: each reported once).</summary>
        public (IDesignTool Tool, ToolResult Result, string Input)? Tool(string id)
        {
            var tool = DesignTools.Find(id);
            if (tool is null || tool is DrainageReportTool) { Once(id, CalcMessage.Info("I-RB-002", $"@tool {id}: no design tool has that id in this version of MasterFlow, so it is skipped.")); return null; }
            if (!ctx.ToolInputs.TryGetValue(id, out var input) || string.IsNullOrWhiteSpace(input)) return null;
            if (!_results.TryGetValue(id, out var r))
            {
                if (ctx.MayRun is { } may && !may(tool))
                {
                    Once(id, CalcMessage.Warning("W-RB-002", $"{tool.Name} is not covered by your license, so it is left out of the report."));
                    _results[id] = null; return null;
                }
                try { r = tool.Run(input, ctx); }
                catch (Exception ex) { r = ToolResult.Failed(tool.Name, CalcMessage.Error("E-TL-999", "The tool stopped with an internal error: " + ex.Message)); }
                _results[id] = r;
                foreach (var t in r.Tables) RunTables.Add((tool.Name, t));
                if (!r.Succeeded) msgs.Add(CalcMessage.Warning("W-RB-003", $"{tool.Name} has errors with its saved input; the report shows its messages instead of results. Fix it in Design tools."));
            }
            return r is null ? null : (tool, r, input);
        }

        private readonly HashSet<string> _said = [];
        private void Once(string key, CalcMessage m) { if (_said.Add(m.Code + key)) msgs.Add(m); }
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Drainage report";
        var msgs = new List<CalcMessage>();
        var text = (input ?? "").Replace("\r", "");
        var rm = RegexReport().Match(text);
        int at = rm.Success ? rm.Index : -1;
        if (at < 0) return ToolResult.Failed(title, CalcMessage.Error("E-RB-001", "The template needs a [Report] line; the report text and directives go under it."));
        var head = ToolInput.Parse(text[..at]);
        msgs.AddRange(head.Errors);
        string body = text[(rm.Index + rm.Length)..];

        var info = context.Project ?? new ProjectInfo();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["project.name"] = info.Name, ["project.number"] = info.ProjectNumber, ["project.client"] = info.Client,
            ["project.preparedBy"] = info.PreparedBy, ["project.checkedBy"] = info.CheckedBy,
            ["firm"] = head.Text("firm") ?? "", ["engineer"] = head.Text("engineer") ?? info.PreparedBy, ["license"] = head.Text("license") ?? "",
            ["date"] = head.Text("date") ?? DateTime.Now.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
        };
        var unknown = new HashSet<string>();
        string Fill(string s) => RegexField().Replace(s, x =>
        {
            if (fields.TryGetValue(x.Groups[1].Value, out var v)) return v;
            unknown.Add(x.Value); return x.Value;
        });

        var run = new Run_(context, msgs);
        var bodyBlocks = new List<ReportBlock>(); var appBlocks = new List<ReportBlock>();
        var target = bodyBlocks;
        var para = new StringBuilder();
        var compliancePlaceholders = new List<(List<ReportBlock> List, int Index)>();
        void Flush() { if (para.Length > 0) { target.Add(new ReportParagraph(Fill(para.ToString().Trim()))); para.Clear(); } }

        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("##", StringComparison.Ordinal)) { Flush(); target.Add(new ReportHeading(2, Fill(line.TrimStart('#').Trim()))); continue; }
            if (line.StartsWith('#')) { Flush(); target.Add(new ReportHeading(1, Fill(line.TrimStart('#').Trim()))); continue; }
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal)) { Flush(); target.Add(new ReportParagraph(Fill(line[2..].Trim()), true)); continue; }
            if (!line.StartsWith('@')) { para.Append(line).Append(' '); continue; }
            Flush();
            var parts = Tokens(line[1..]);
            string cmd = parts[0].ToLowerInvariant();
            switch (cmd)
            {
                case "pagebreak": target.Add(new ReportPageBreak()); break;
                case "appendices": target = appBlocks; break;
                case "certification":
                    target.Add(new ReportCertification(Fill(line["@certification".Length..].Trim()), fields["engineer"], fields["license"], fields["firm"])); break;
                case "compliance": compliancePlaceholders.Add((target, target.Count)); break;
                case "inputs":
                    foreach (var (id, inp) in context.ToolInputs.OrderBy(k => DesignTools.Find(k.Key)?.Name ?? k.Key))
                        if (run.Tool(id) is { } t) target.Add(new ReportCode(t.Tool.Name, inp));
                    break;
                case "tool":
                    if (parts.Count < 2) { msgs.Add(CalcMessage.Error("E-RB-002", $"\"{line}\": give a tool id, e.g. @tool detention-routing summary.")); break; }
                    if (run.Tool(parts[1]) is { } tr) AddTool(target, tr.Tool, tr.Result, tr.Input, parts.Count > 2 ? parts[2].ToLowerInvariant() : "summary", parts.Count > 3 ? parts[3] : null, msgs);
                    break;
                case "network":
                {
                    string which = parts.Count > 1 ? parts[1] : "all", mode = parts.Count > 2 ? parts[2].ToLowerInvariant() : "summary";
                    var nets = context.Networks.Where(n => which.Equals("all", StringComparison.OrdinalIgnoreCase) || n.Network.Name.Equals(which, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (nets.Count == 0 && !which.Equals("all", StringComparison.OrdinalIgnoreCase)) msgs.Add(CalcMessage.Warning("W-RB-004", $"@network {which}: no storm network has that name."));
                    foreach (var (net, res) in nets) AddNetwork(target, net, res, mode, msgs);
                    break;
                }
                case "calculations": AddCalculations(target, context.Calculations, parts.Count > 1 ? parts[1].ToLowerInvariant() : "summary"); break;
                default: msgs.Add(CalcMessage.Error("E-RB-003", $"\"{line}\": unknown directive @{cmd}.")); break;
            }
        }
        Flush();

        // Compliance last, after every tool the template names has run.
        var compliance = Compliance(run.RunTables, context.Networks);
        foreach (var (list, idx) in compliancePlaceholders.OrderByDescending(x => x.Index))
            list.Insert(idx, new ReportTable(compliance));

        foreach (var u in unknown) msgs.Add(CalcMessage.Warning("W-RB-005", $"Placeholder {u} is not a known field and was left as written."));
        if (head.Flag("omit empty sections", true)) { bodyBlocks = OmitEmpty(bodyBlocks); appBlocks = OmitEmpty(appBlocks); }
        if (string.IsNullOrWhiteSpace(fields["engineer"]) || string.IsNullOrWhiteSpace(fields["license"]))
            msgs.Add(CalcMessage.Info("I-RB-001", "engineer and license are blank, so the certification block has blank lines for them."));

        var cover = new List<(string, string)>
        {
            ("Project", info.Name), ("Project number", info.ProjectNumber), ("Client", info.Client), ("Prepared by", fields["firm"]),
            ("Engineer of record", fields["engineer"]), ("License", fields["license"]), ("Checked by", info.CheckedBy), ("Date", fields["date"]),
        }.Where(c => !string.IsNullOrWhiteSpace(c.Item2)).ToList();
        var doc = new ReportDocument(Fill(head.Text("title") ?? "Storm Water Report"), cover, bodyBlocks, appBlocks)
        {
            Subtitle = Fill(head.Text("subtitle") ?? ""), TableOfContents = head.Flag("table of contents", true),
        };

        // Window preview: outline and compliance.
        var outline = new List<IReadOnlyList<object?>>();
        int h1 = 0, h2 = 0;
        void Outline(List<ReportBlock> blocks, bool appendix)
        {
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i] is ReportHeading h)
                {
                    if (h.Level == 1) { h1++; h2 = 0; } else h2++;
                    var after = blocks.Skip(i + 1).TakeWhile(x => x is not ReportHeading y || y.Level > h.Level).ToList();
                    outline.Add([appendix ? (h.Level == 1 ? "App." : "") : h.Level == 1 ? $"{h1}" : $"{h1}.{h2}", h.Text, after.OfType<ReportParagraph>().Count(), after.OfType<ReportTable>().Count(), after.OfType<ReportChart>().Count(), after.OfType<ReportNetwork>().Count()]);
                }
        }
        Outline(bodyBlocks, false);
        Outline(appBlocks, true);
        static int N(IReadOnlyList<object?> r) => r[1] is string s && RegexItems().Match(s) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
        int fails = compliance.Rows.Where(r => r[^1] is string s && s.StartsWith("FAILS", StringComparison.Ordinal)).Sum(N), oks = compliance.Rows.Where(r => r[^1] is "OK").Sum(N), all = compliance.Rows.Sum(N);
        var summary = new List<string>
        {
            $"{doc.Title}: {bodyBlocks.Concat(appBlocks).OfType<ReportHeading>().Count(h => h.Level == 1)} sections, {bodyBlocks.Concat(appBlocks).OfType<ReportTable>().Count()} tables, {bodyBlocks.Concat(appBlocks).OfType<ReportChart>().Count()} charts, {bodyBlocks.Concat(appBlocks).OfType<ReportNetwork>().Count()} storm network section(s).",
            $"Criteria checks: {oks} OK, {fails} FAILS, {all - oks - fails} other.",
            "Save… writes the full report as PDF, HTML or Word (.docx).",
        };
        return new ToolResult(title, summary, [new ToolTable("Report outline", ["No.", "Heading", "Paragraphs", "Tables", "Charts", "Networks"], outline), compliance], msgs) { Report = doc };
    }

    private static void AddTool(List<ReportBlock> target, IDesignTool tool, ToolResult r, string input, string mode, string? tableTitle, List<CalcMessage> msgs)
    {
        target.Add(new ReportHeading(2, tool.Name));
        var problems = r.Messages.Where(m => m.Severity != MessageSeverity.Info).ToList();
        if (!r.Succeeded) { target.Add(new ReportMessages(tool.Name, r.Messages)); return; }
        switch (mode)
        {
            case "summary":
                foreach (var s in r.Summary) target.Add(new ReportParagraph(s));
                // Key tables only: checks, criteria, summaries, and anything narrow enough for a portrait page.
                foreach (var t in r.Tables.Where(t => t.Headers.Count <= 6 && IsKey(t)).Take(3)) target.Add(new ReportTable(t));
                if (problems.Count > 0) target.Add(new ReportMessages(tool.Name, problems));
                break;
            case "tables":
                foreach (var t in r.Tables) target.Add(new ReportTable(t));
                break;
            case "charts":
                foreach (var c in r.Charts) target.Add(new ReportChart(c));
                break;
            case "table":
                var hit = r.Tables.FirstOrDefault(t => tableTitle is not null && t.Title.StartsWith(tableTitle, StringComparison.OrdinalIgnoreCase));
                if (hit is null) msgs.Add(CalcMessage.Warning("W-RB-006", $"@tool {tool.Id} table \"{tableTitle}\": no table starts with that title ({string.Join("; ", r.Tables.Select(t => t.Title))})."));
                else target.Add(new ReportTable(hit));
                break;
            case "full":
                foreach (var s in r.Summary) target.Add(new ReportParagraph(s));
                foreach (var c in r.Charts) target.Add(new ReportChart(c));
                foreach (var t in r.Tables) target.Add(new ReportTable(t));
                if (r.Messages.Count > 0) target.Add(new ReportMessages(tool.Name, r.Messages));
                break;
            default: msgs.Add(CalcMessage.Error("E-RB-004", $"@tool {tool.Id} {mode}: use summary, full, tables, charts or table \"title\".")); break;
        }
    }

    private static bool IsKey(ToolTable t) =>
        t.Title.Contains("check", StringComparison.OrdinalIgnoreCase) || t.Title.Contains("criteria", StringComparison.OrdinalIgnoreCase)
        || t.Title.Contains("summary", StringComparison.OrdinalIgnoreCase) || t.Title.Contains("result", StringComparison.OrdinalIgnoreCase)
        || t.Rows.Count <= 15;

    private static void AddNetwork(List<ReportBlock> target, StormNetwork net, StormNetworkResult? res, string mode, List<CalcMessage> msgs)
    {
        target.Add(new ReportHeading(2, $"Storm network: {net.Name}"));
        if (res is null || !res.Succeeded)
        {
            target.Add(new ReportParagraph("This network has no current solution; open it in Storm networks and solve it."));
            msgs.Add(CalcMessage.Warning("W-RB-007", $"Storm network {net.Name} is not solved, so the report has no HGL results for it."));
            return;
        }
        if (mode == "full") { target.Add(new ReportNetwork(res)); return; }
        var u = net.Units == UnitSystem.SI ? ("m", "m³/s", "m/s") : ("ft", "cfs", "ft/s");
        var need = net.Criteria.HglBelowRim;
        double minClear = res.Structures.Where(s => s.HglClearance is not null).Select(s => s.HglClearance!.Value).DefaultIfEmpty(double.NaN).Min();
        int failing = need is { } nd ? res.Structures.Count(s => s.HglClearance is { } c && c < nd) : 0;
        target.Add(new ReportParagraph($"{net.Pipes.Count} pipes and {net.Structures.Count} structures; criteria: {(string.IsNullOrWhiteSpace(net.Criteria.Source) ? "as entered" : net.Criteria.Source)}. "
            + (double.IsFinite(minClear) ? $"Least clearance from HGL to rim {minClear:F2} {u.Item1}" + (need is { } n2 ? $" (required {n2:F2} {u.Item1}); {failing} structure(s) fail the HGL check." : ".") : "")));
        var rows = res.Pipes.Select(p => (IReadOnlyList<object?>)[p.Pipe.Name, $"{p.Pipe.From} → {p.Pipe.To}", p.Pipe.Discharge, p.FullFlowCapacity, p.VelocityDown, p.HglUp, p.HglDown]).ToList();
        target.Add(new ReportTable(new ToolTable($"{net.Name}: pipe summary", ["Pipe", "From → To", $"Q ({u.Item2})", $"Q full ({u.Item2})", $"V ({u.Item3})", "HGL up", "HGL down"], rows)));
        var warn = res.Messages.Where(m => m.Severity != MessageSeverity.Info).ToList();
        if (warn.Count > 0) target.Add(new ReportMessages(net.Name, warn));
    }

    private static void AddCalculations(List<ReportBlock> target, IReadOnlyList<ChannelCalculation> calcs, string mode)
    {
        var items = calcs.Where(c => c.IncludeInBatch).Select(c => (c.Title, Result: UniformFlowCalculator.Solve(c.Input))).ToList();
        if (items.Count == 0) return;
        target.Add(new ReportHeading(2, "Channel and pipe calculations (Manning)"));
        foreach (var chunk in items.Chunk(mode == "full" ? 6 : 4))
        {
            var cmp = Comparison.Build(chunk.ToList());
            var rows = cmp.Rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Label, r.Unit }.Concat(r.Values.Select(v => (object?)v)).ToList()).ToList();
            rows.Add(new object?[] { "Status", "" }.Concat(cmp.Statuses).ToList());
            target.Add(new ReportTable(new ToolTable("Calculations: " + string.Join(", ", cmp.Titles), ["Quantity", "Unit", .. cmp.Titles], rows, cmp.Notes)));
            if (mode != "full") break;
        }
        if (mode != "full" && items.Count > 4) target.Add(new ReportParagraph($"{items.Count - 4} more calculations are in the appendix."));
    }

    /// <summary>Every OK/FAILS/NOT CHECKED cell of every tool table, plus each storm network's structure HGL checks.</summary>
    public static ToolTable Compliance(IEnumerable<(string Source, ToolTable Table)> tables, IEnumerable<(StormNetwork Network, StormNetworkResult? Result)> networks)
    {
        static bool IsStatus(object? c) => c is string s && (s == "OK" || s.StartsWith("FAILS", StringComparison.Ordinal) || s == "NOT CHECKED" || s == "OVER CAPACITY");
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var (src, t) in tables)
            foreach (var r in t.Rows)
                for (int i = 0; i < r.Count; i++)
                    if (IsStatus(r[i]))
                    {
                        string what = i < t.Headers.Count && !t.Headers[i].Equals("Check", StringComparison.OrdinalIgnoreCase) && !t.Headers[i].Equals("Status", StringComparison.OrdinalIgnoreCase)
                            && !t.Headers[i].Equals("Result", StringComparison.OrdinalIgnoreCase) ? t.Headers[i] : t.Title;
                        rows.Add([src, DesignTools.Cell(r[0]) + (r.Count > 2 && r[1] is string s1 && !IsStatus(s1) && s1.Length < 40 ? " — " + s1 : ""), what, r[i]]);
                    }
        foreach (var (net, res) in networks)
            if (res is { Succeeded: true } && net.Criteria.HglBelowRim is { } need)
                foreach (var s in res.Structures.Where(s => s.HglClearance is not null))
                    rows.Add([$"Storm network {net.Name}", s.Structure.Name, $"HGL ≥ {need:G3} below rim", s.HglClearance >= need ? "OK" : "FAILS"]);
        // Long runs of the same check (e.g. a bank check at every station) collapse to one row.
        var grouped = new List<IReadOnlyList<object?>>();
        foreach (var g in rows.GroupBy(r => (r[0], r[2], r[3])))
        {
            var list = g.ToList();
            grouped.AddRange(list.Count <= 3 ? list : [[g.Key.Item1, $"{list.Count} items ({list[0][1]} to {list[^1][1]})", g.Key.Item2, g.Key.Item3]]);
        }
        // Keep the original order of first appearance.
        rows = grouped;
        return new ToolTable("Compliance summary", ["Source", "Item", "Check", "Result"], rows,
            rows.Count == 0 ? ["No criteria checks were found in the project's calculations."] : null);
    }

    /// <summary>Drops headings with nothing under them before the next heading of the same or a higher level.</summary>
    private static List<ReportBlock> OmitEmpty(List<ReportBlock> blocks)
    {
        var keep = new List<ReportBlock>();
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is ReportHeading h)
            {
                bool content = blocks.Skip(i + 1).TakeWhile(b => b is not ReportHeading y || y.Level > h.Level).Any(b => b is not ReportHeading and not ReportPageBreak);
                if (!content) continue;
            }
            keep.Add(blocks[i]);
        }
        return keep;
    }

    /// <summary>Splits a directive on spaces, keeping "quoted text" together.</summary>
    private static List<string> Tokens(string s)
    {
        var list = new List<string>();
        foreach (Match m in RegexToken().Matches(s)) list.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Value);
        return list.Count == 0 ? [""] : list;
    }

    [GeneratedRegex(@"^[ \t]*\[Report\][ \t]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex RegexReport();
    [GeneratedRegex(@"\{([A-Za-z.]+)\}")]
    private static partial Regex RegexField();
    [GeneratedRegex(@"^(\d+) items \(")]
    private static partial Regex RegexItems();
    [GeneratedRegex("\"([^\"]*)\"|\\S+")]
    private static partial Regex RegexToken();
}
