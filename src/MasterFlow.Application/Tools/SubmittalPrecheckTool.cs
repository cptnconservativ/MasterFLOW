using System.Text.RegularExpressions;
using MasterFlow.Application.Documents;
using MasterFlow.Application.Reports;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Plan-review pre-checker: reads the drainage report (PDF, Word or text) and the plan set, and checks the project's
/// own calculations, then lists every comment a Rapid City reviewer would be likely to make, each with the IDCM, SWQM
/// or drainage criteria section it rests on, worded as a review comment so it can be answered before submittal.
/// See docs/tools/submittal-precheck.md; decisions D-220 to D-227.
/// </summary>
public sealed class SubmittalPrecheckTool : IDesignTool
{
    public string Id => "submittal-precheck";
    public string Name => "Plan-review pre-checker (drainage report and plans)";
    public string Description => "Reads your storm water report and plan set (PDF, Word or text, read on this computer) and re-runs the project's calculations, then lists the comments a Rapid City reviewer would likely make, each citing its IDCM, Storm Water Quality Manual or criteria section, so you can fix them before submittal.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Point to the report and plans (PDF, .docx or .txt; read on this computer, nothing is sent), or paste text below.
        # report = C:\Projects\Job\Storm Water Report.pdf
        # plans = C:\Projects\Job\Construction Plans.pdf
        # What the project includes: auto decides from the report text and the project's calculations.
        detention = auto
        culverts = auto
        storm sewer = auto
        open channels = auto
        floodplain = auto
        model checks = yes          # re-run every saved design tool and storm network and list each failed criterion
        names in report = yes       # every pipe and structure in the model should appear in the report
        show satisfied = no         # yes also lists the checklist items the report already covers
        [Report text]
        Storm Water Report, Sample Subdivision. Prepared under the direct supervision of a Professional Engineer licensed in South Dakota.
        The site lies in the Rapid Creek drainage basin; the adopted Drainage Basin Design Plan governs. No inter-basin transfer is proposed.
        Runoff was computed by the Rational Method (IDCM 4.2.2) with runoff coefficients from Table 4-3, intensities from Table 4-2, and times of concentration per 4.2.5.
        The 2-year, 10-year and 100-year storms were analysed for existing and proposed (post-development) conditions.
        Street spread for the 10-year storm and street capacity for the 100-year storm were checked. Inlet capacity and bypass were computed per HEC-22.
        The storm sewer hydraulic grade line was computed with structure losses per Figures 4-6 and 4-7.
        Detention: the pond limits post-development peaks to pre-development for the 2-, 10- and 100-year storms. Stage-storage and the outlet rating are attached.
        """;

    /// <summary>A content item a reviewer looks for. Each pattern group must match somewhere (any regex within a group).</summary>
    private sealed record Item(string Topic, string Reference, string Applies, string[][] Groups, string Comment);

    /// <summary>The IDCM 2012 / SWQM 2009 report content checklist (D-222). Applies: always, detention, culverts, storm sewer, open channels, floodplain, streets.</summary>
    private static readonly Item[] Checklist =
    [
        new("Engineer's certification", "IDCM §1.15.1(2)", "always", [[@"professional engineer", @"\bP\.?\s?E\.?\b", @"direct supervision", @"certif"]],
            "Provide the engineer's certification: the report must be prepared under the direct supervision of a Professional Engineer (sealed and signed)."),
        new("Inputs, outputs, assumptions and calculations", "IDCM §4.1.8", "always", [[@"assum"], [@"calculation", @"computed", @"output"]],
            "The report must contain the design input parameters, output, assumptions and calculations. State the assumptions and attach the calculations."),
        new("Drainage Basin Design Plan", "IDCM §4.1.5", "always", [[@"drainage basin (design )?plan", @"\bDBDP\b", @"basin plan"]],
            "State the adopted Drainage Basin Design Plan the development conforms to (or that none exists and how this is addressed)."),
        new("Inter-basin transfer", "IDCM §4.1.2", "always", [[@"inter-?\s?basin", @"basin transfer", @"divert"]],
            "Address inter-basin transfer: state that the design does not transfer drainage between basins, or justify why no reasonable alternative exists."),
        new("Access and drainage easements", "IDCM §4.1.3, §4.1.6", "always", [[@"easement"]],
            "Show drainage easements for the 100-year runoff where a drainageway carries runoff from two or more lots, and legal access to every drainage facility."),
        new("Operation and maintenance", "IDCM §4.1.4", "always", [[@"maint(ain|enance)"]],
            "Identify who is responsible for operation and maintenance of each storm water facility."),
        new("Storm water quality", "IDCM §4.1.7; SWQM (2009)", "always", [[@"water quality", @"\bWQCV\b", @"stormwater quality manual", @"storm water quality manual"]],
            "Address permanent storm water quality per the Rapid City Storm Water Quality Manual (WQCV and BMP selection), as IDCM §4.1.7 requires."),
        new("Erosion and sediment control", "SWQM Ch. 1", "always", [[@"erosion", @"sediment control"]],
            "Provide the erosion and sediment control plan or reference it (Storm Water Quality Manual Chapter 1)."),
        new("Hydrologic method", "IDCM §4.2.1", "always", [[@"rational method", @"HEC-?HMS", @"\bCUHP\b", @"TR-?55", @"unit hydrograph"]],
            "State the hydrologic method. The Rational Method is limited to 160 acres; larger basins without a basin plan require HEC-HMS (IDCM §4.2.1)."),
        new("Runoff coefficients or curve numbers", "IDCM §4.2.3, §4.3.4", "always", [[@"runoff coefficient", @"\bC\s?=", @"curve number", @"\bCN\b", @"imperv"]],
            "Tabulate the runoff coefficients (or curve numbers and impervious areas) for each basin and their source."),
        new("Rainfall", "IDCM §4.2.4, §4.3.2, Table 4-2", "always", [[@"intensit", @"table 4-2", @"rainfall depth", @"\bIDF\b", @"atlas 14"]],
            "State the rainfall intensities or depths used and their source (IDCM Table 4-2)."),
        new("Time of concentration", "IDCM §4.2.5–4.2.7", "always", [[@"time of concentration", @"\bTc\b", @"travel time"]],
            "Show the time of concentration calculation for each basin (IDCM §4.2.5 to §4.2.7)."),
        new("Design storms (2-, 10-, 100-year)", "IDCM §4.4.9, §4.10.3(2)", "always", [[@"\b2[- ]?(yr|year)", @"two[- ]\(?2?\)?[- ]?year"], [@"\b10[- ]?(yr|year)", @"ten[- ]\(?1?0?\)?[- ]?year"], [@"\b100[- ]?(yr|year)", @"hundred"]],
            "Analyse the 2-, 10- and 100-year storms (initial 10-year and major 100-year for streets; 2-, 10- and 100-year for detention)."),
        new("Existing and proposed conditions", "IDCM §4.10.1", "always", [[@"pre-?\s?development", @"existing condition", @"historic"], [@"post-?\s?development", @"proposed condition", @"developed condition"]],
            "Compare existing (pre-development) and proposed (post-development) runoff for each design point."),
        new("Downstream conveyance", "IDCM §4.10.1", "always", [[@"downstream"]],
            "Evaluate the downstream conveyance to an established drainage facility (capacity, backwater, erosion), or limit release to pre-development rates."),
        new("Street spread and capacity", "IDCM §4.4.9–4.4.10, Table 4-6", "streets", [[@"spread", @"encroachment", @"street capacity", @"gutter"]],
            "Show street spread for the 10-year storm against Table 4-6 and street capacity for the 100-year storm."),
        new("Inlet capacity and bypass", "IDCM §4.5.3", "storm sewer", [[@"inlet capacity", @"intercept", @"bypass", @"carry-?over"]],
            "Show inlet capacity, interception and bypass at each inlet (IDCM §4.5.3)."),
        new("Storm sewer HGL", "IDCM §4.6.7", "storm sewer", [[@"hydraulic grade", @"\bHGL\b"]],
            "Provide the storm sewer hydraulic grade line; it must stay at least 1 ft below the rim or street surface (IDCM §4.6.7)."),
        new("Structure losses", "IDCM §4.6.8–4.6.9, Figs. 4-6, 4-7", "storm sewer", [[@"junction loss", @"structure loss", @"minor loss", @"head loss", @"figures? 4-6", @"losses"]],
            "Include losses at structures in the HGL (IDCM Figures 4-6 and 4-7)."),
        new("Storm sewer velocities", "IDCM §4.6.2(3), Table 4-13", "storm sewer", [[@"velocit"]],
            "Tabulate pipe velocities: at least 2.5 fps for the 2-year flow and no more than 20 fps in collectors and mains."),
        new("Pond release rates", "IDCM §4.10.3(2), §4.10.4", "detention", [[@"pre-?\s?development", @"existing condition"], [@"release", @"outflow", @"discharge"]],
            "Show that pond release for the 2-, 10- and 100-year storms does not exceed existing (pre-development) peaks, or provide the downstream analysis that allows more."),
        new("Pond freeboard", "IDCM §4.10.3(2), §4.10.4(4)", "detention", [[@"freeboard"]],
            "Show at least 1 ft of freeboard above the 100-year water surface and above the emergency spillway flow depth, whichever is greater."),
        new("Pond drain time", "IDCM §4.10.5", "detention", [[@"72[- ]?(hr|hour)", @"drain time", @"drawdown", @"dewater"]],
            "Show the pond drains completely within 72 hours after the 100-year storm by gravity through the principal outlet."),
        new("Outlet works", "IDCM §4.10.5", "detention", [[@"outlet", @"riser", @"orifice"], [@"18[\s-]?(in|inch|\x22|”)", @"rating", @"stage[- ]discharge"]],
            "Describe the principal outlet (reinforced concrete, 18 in. minimum pipe and riser) and provide its rating with weir, orifice and pipe control evaluated."),
        new("Emergency spillway", "IDCM §4.10.6", "detention", [[@"spillway"]],
            "Provide the emergency spillway design (location, crest, capacity, protection of the embankment toe)."),
        new("Pond grading", "IDCM §4.10.4(1)–(3)", "detention", [[@"side slope", @"4\s?:\s?1", @"4H"], [@"trickle", @"low[- ]?flow channel"]],
            "Show pond side slopes no steeper than 4H:1V, bottom slopes (0.5 % longitudinal, 2 % cross) and a trickle channel sized for 2 % of the 100-year inflow."),
        new("Stage-storage", "IDCM §4.10.7", "detention", [[@"stage[- ]storage", @"storage volume", @"routing"]],
            "Provide the stage-storage table and the routing (modified rational or HEC-HMS)."),
        new("Groundwater at the pond", "IDCM §4.10.4(6)", "detention", [[@"groundwater", @"ground water"]],
            "Address groundwater at the detention facility so standing water is avoided."),
        new("Culvert method", "IDCM §4.7.2(4)", "culverts", [[@"HDS-?\s?5", @"hydraulic design of highway culverts", @"inlet control|outlet control"]],
            "Design culverts per FHWA HDS-5; show inlet and outlet control."),
        new("Culvert design storm and freeboard", "IDCM §4.7.2(1)", "culverts", [[@"\b10[- ]?(yr|year)"], [@"freeboard", @"headwater"]],
            "Show the culvert passes the 10-year storm with 1 ft of freeboard and the 100-year storm meets Table 4-9."),
        new("Culvert outlet velocity and protection", "IDCM §4.7.2(2), §4.9", "culverts", [[@"outlet velocit", @"velocit"], [@"riprap", @"energy dissipat", @"apron", @"outlet protection"]],
            "Show outlet velocities (20 fps maximum) and the inlet and outlet protection (IDCM §4.9)."),
        new("Open channel design", "IDCM §4.8.3", "open channels", [[@"channel"], [@"freeboard", @"lining", @"froude", @"shear"]],
            "Show channel capacity, freeboard, lining and Froude number per IDCM §4.8.3."),
        new("Floodplain", "IDCM §4.2.1(5), §4.7.2(1)", "floodplain", [[@"FEMA", @"flood ?plain", @"\bFIRM\b", @"flood hazard"]],
            "Address the FEMA floodplain (FIRM panel, zone, BFE) and the City floodplain ordinance; submittals requiring FEMA approval follow FEMA guidelines."),
    ];

    /// <summary>Plan sheets a drainage review expects (D-225).</summary>
    private static readonly (string Sheet, string Applies, string[] Patterns, string Reference)[] Sheets =
    [
        ("Drainage area map", "always", [@"drainage (area|basin) map", @"drainage plan", @"basin map"], "IDCM §4.1.8"),
        ("Grading plan", "always", [@"grading plan", @"grading and drainage"], "IDCM §4.1.8"),
        ("Storm sewer plan and profile", "storm sewer", [@"storm sewer plan", @"plan (and|&) profile", @"storm (sewer|drain) profile"], "IDCM §4.6"),
        ("Erosion and sediment control plan", "always", [@"erosion (and|&) sediment", @"erosion control"], "SWQM Ch. 1"),
        ("Detention pond plan and outlet details", "detention", [@"pond", @"detention"], "IDCM §4.10"),
        ("Details", "always", [@"detail"], "City standard specifications"),
    ];

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Plan-review pre-check — anticipated Rapid City review comments";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        bool showOk = p.Flag("show satisfied", false);

        // ---------- Documents ----------
        string? reportText = null, plansText = null;
        var docRows = new List<IReadOnlyList<object?>>();
        string? Load(string key, string what)
        {
            if (p.Text(key) is not { } path) return null;
            try
            {
                var d = DocumentText.Read(path);
                int perPage = d.Pages.Count == 0 ? 0 : d.Letters / d.Pages.Count;
                docRows.Add([what, Path.GetFileName(path), d.Pages.Count, d.Letters, perPage < 200 ? "little text: scanned pages are not read" : "read"]);
                if (perPage < 200) msgs.Add(CalcMessage.Warning("W-PC-002", $"{what}: only {d.Letters:N0} readable characters in {d.Pages.Count} page(s). Scanned pages have no text layer; export the PDF from Word or CAD, or paste the text under [{what} text]."));
                return d.All;
            }
            catch (Exception ex) { msgs.Add(CalcMessage.Error("E-PC-001", $"Could not read the {what.ToLowerInvariant()} \"{path}\": {ex.Message}")); return null; }
        }
        reportText = Load("report", "Report");
        plansText = Load("plans", "Plans");
        if (p.HasSection("Report text")) { reportText = (reportText ?? "") + "\n" + SectionText(input, "Report text"); docRows.Add(["Report", "pasted text", null, null, "read"]); }
        if (p.HasSection("Plans text")) { plansText = (plansText ?? "") + "\n" + SectionText(input, "Plans text"); docRows.Add(["Plans", "pasted text", null, null, "read"]); }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);
        bool modelChecks = p.Flag("model checks", true);
        if (reportText is null && plansText is null && !modelChecks)
            return ToolResult.Failed(title, CalcMessage.Error("E-PC-002", "Give a report (report = file, or text under [Report text]), plans, or set model checks = yes."));

        // ---------- What the project includes (D-223) ----------
        var tools = context.ToolInputs;
        string rt = reportText ?? "";
        bool Has(string id) => tools.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t);
        bool Text(params string[] pats) => pats.Any(x => Regex.IsMatch(rt, x, RegexOptions.IgnoreCase)) || plansText is not null && pats.Any(x => Regex.IsMatch(plansText, x, RegexOptions.IgnoreCase));
        var scope = new Dictionary<string, bool>
        {
            ["always"] = true,
            ["detention"] = Flag(p, "detention", () => Has("detention-routing") || Has("outlet-designer") || Text(@"detention", @"\bpond\b")),
            ["culverts"] = Flag(p, "culverts", () => Has("hds5-culvert") || Text(@"\bculvert")),
            ["storm sewer"] = Flag(p, "storm sewer", () => context.Networks.Count > 0 || context.Network is not null || Has("inlet-network") || Text(@"storm sewer", @"\binlets?\b", @"manhole")),
            ["open channels"] = Flag(p, "open channels", () => context.Calculations.Count > 0 || Has("channel-profile") || Has("backwater-profile") || Text(@"open channel", @"\bswale\b", @"\bditch")),
            ["floodplain"] = Flag(p, "floodplain", () => Text(@"FEMA", @"flood ?plain", @"\bFIRM\b", @"zone A")),
            ["streets"] = Flag(p, "streets", () => Has("street-capacity") || Has("gutter-inlets") || Text(@"\bstreet", @"\bgutter", @"\bcurb")),
        };

        var comments = new List<(int Rank, string Type, string Topic, string Comment, string Reference, string Source)>();
        var checklist = new List<IReadOnlyList<object?>>();

        // ---------- Report content (D-222) ----------
        if (reportText is not null)
            foreach (var it in Checklist.Where(i => scope[i.Applies]))
            {
                var found = it.Groups.Select(g => g.Select(x => Regex.Match(reportText, x, RegexOptions.IgnoreCase)).FirstOrDefault(m => m.Success)).ToList();
                bool ok = found.All(m => m is not null);
                string evidence = ok ? Snippet(reportText, found[0]!.Index) : "not found: " + string.Join("; ", it.Groups.Where((g, k) => found[k] is null).Select(g => string.Join(" / ", g.Take(3).Select(Plain))));
                if (!ok) comments.Add((1, "MISSING", it.Topic, it.Comment, it.Reference, "report"));
                if (!ok || showOk) checklist.Add([ok ? "covered" : "MISSING", it.Topic, it.Reference, it.Applies, evidence]);
            }
        if (reportText is not null && scope["always"])
        {
            // Rational method above 160 ac (IDCM §4.2.1(1)).
            if (Regex.IsMatch(reportText, "rational method", RegexOptions.IgnoreCase))
            {
                var areas = Regex.Matches(reportText, @"(?<![\d.])(\d{2,5}(?:\.\d+)?)\s*(?:ac(?![-\w])|acres?\b)", RegexOptions.IgnoreCase).Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
                if (areas.Count > 0 && areas.Max() > 160)
                    comments.Add((0, "REVISE", "Rational method area limit", $"The report uses the Rational Method and mentions {areas.Max():0.#} acres. The Rational Method is limited to 160 acres; use HEC-HMS (or the adopted basin plan) for the larger basin.", "IDCM §4.2.1(1)", "report"));
            }
            if (Regex.IsMatch(reportText, @"\bretention\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(reportText, @"drainage basin design plan", RegexOptions.IgnoreCase))
                comments.Add((1, "VERIFY", "Retention", "Retention (extended holding) is not permitted unless identified in an approved Drainage Basin Design Plan. Clarify whether the facility is detention.", "IDCM §4.10.1", "report"));
        }

        // ---------- Plans (D-225) ----------
        var sheetRows = new List<IReadOnlyList<object?>>();
        if (plansText is not null)
            foreach (var s in Sheets.Where(s => scope[s.Applies]))
            {
                bool ok = s.Patterns.Any(x => Regex.IsMatch(plansText, x, RegexOptions.IgnoreCase));
                sheetRows.Add([ok ? "found" : "MISSING", s.Sheet, s.Reference]);
                if (!ok) comments.Add((1, "MISSING", s.Sheet, $"Provide the {s.Sheet.ToLowerInvariant()} in the plan set.", s.Reference, "plans"));
            }

        // ---------- Project calculations (D-224) ----------
        var modelRows = new List<IReadOnlyList<object?>>();
        var nets = context.Networks.Count > 0 ? context.Networks.ToList()
            : context.Network is { } n0 ? [(n0, context.NetworkResult)] : [];
        if (modelChecks)
        {
            var tables = new List<(string Source, ToolTable Table)>();
            foreach (var (id, text) in tools.OrderBy(k => k.Key))
            {
                if (id is "drainage-report" or "batch-scenarios" or "redesign-diff" || id == Id || string.IsNullOrWhiteSpace(text)) continue;
                if (DesignTools.Find(id) is not { } tool) continue;
                if (context.MayRun is { } may && !may(tool)) { msgs.Add(CalcMessage.Info("I-PC-002", $"{tool.Name} was not re-run: the license does not include it.")); continue; }
                ToolResult r;
                try { r = tool.Run(text, context); } catch (Exception ex) { msgs.Add(CalcMessage.Warning("W-PC-003", $"{tool.Name} could not be re-run: {ex.Message}")); continue; }
                foreach (var e in r.Messages.Where(m => m.Severity == MessageSeverity.Error))
                    comments.Add((0, "REVISE", tool.Name, $"The calculation does not run: {e.Text}", e.Code, "calculation"));
                tables.AddRange(r.Tables.Select(t => (tool.Name, t)));
            }
            var solved = nets.Select(x => (x.Network, x.Result ?? StormNetworkSolver.Solve(x.Network))).ToList();
            var comp = DrainageReportTool.Compliance(tables, solved.Select(x => (x.Network, (StormNetworkResult?)x.Item2)));
            foreach (var row in comp.Rows.Where(r => r[3] is string s && s != "OK"))
            {
                string res = (string)row[3]!;
                string reference = RefFor((string)row[0]!, (string)row[2]!);
                comments.Add((res.StartsWith("FAILS") || res == "OVER CAPACITY" ? 0 : 2, res.StartsWith("FAILS") || res == "OVER CAPACITY" ? "REVISE" : "VERIFY", $"{row[0]}: {row[1]}",
                    res == "NOT CHECKED" ? $"{row[2]} was not checked for {row[1]}; provide the value or the check." : $"{row[2]} is not met at {row[1]} ({res.ToLowerInvariant()}). Revise the design or justify.", reference, "calculation"));
                modelRows.Add([row[0], row[1], row[2], res]);
            }
            // IDCM §4.6 plan review of every network (the same checks the City applies to the storm sewer).
            foreach (var (net, res) in solved)
            {
                if (!res.Succeeded) { comments.Add((0, "REVISE", $"Storm network {net.Name}", "The network does not solve; the HGL cannot be reviewed.", "IDCM §4.6.7", "calculation")); continue; }
                var pr = new PlanReviewTool().Run("bends = auto", new DesignToolContext(net.Units, net, res));
                if (!pr.Succeeded) continue;
                foreach (var r in pr.Tables[0].Rows.Where(r => (string?)r[0] is "FAIL" or "REVIEW"))
                {
                    if (((string)r[1]!).StartsWith("HGL")) continue;   // already in the compliance rows
                    bool fail = (string)r[0]! == "FAIL";
                    comments.Add((fail ? 0 : 2, fail ? "REVISE" : "VERIFY", $"Storm network {net.Name}: {r[3]}", $"{r[1]}: {DesignTools.Cell(r[4])} against {DesignTools.Cell(r[5])}. {r[6]}".Trim(), $"IDCM {r[2]}", "calculation"));
                    modelRows.Add([$"Storm network {net.Name}", r[3], r[1], r[0]]);
                }
            }
        }

        // ---------- Report against the model (D-226) ----------
        var consistency = new List<IReadOnlyList<object?>>();
        if (reportText is not null && p.Flag("names in report", true))
            foreach (var (net, _) in nets)
            {
                var absent = net.Pipes.Select(x => x.Name).Concat(net.Structures.Select(s => s.Name))
                    .Where(nm => nm.Length > 0 && !Regex.IsMatch(reportText, @"(?<![A-Za-z0-9])" + Regex.Escape(nm) + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase)).ToList();
                foreach (var nm in absent.Take(40)) consistency.Add([net.Name, nm, "not named in the report"]);
                if (absent.Count > 0)
                    comments.Add((2, "VERIFY", $"Storm network {net.Name}", $"{absent.Count} pipe(s)/structure(s) in the model are not named in the report ({string.Join(", ", absent.Take(8))}{(absent.Count > 8 ? ", …" : "")}). Include the full pipe and structure tables so every element can be checked.", "IDCM §4.1.8", "report vs model"));
            }

        // ---------- Output ----------
        var ordered = comments.OrderBy(c => c.Rank).ThenBy(c => c.Source).ToList();
        var rows = ordered.Select((c, i) => (IReadOnlyList<object?>)[i + 1, c.Type, c.Topic, c.Comment, c.Reference, c.Source, ""]).ToList();
        int revise = comments.Count(c => c.Type == "REVISE"), missing = comments.Count(c => c.Type == "MISSING"), verify = comments.Count(c => c.Type == "VERIFY");
        var summary = new List<string>
        {
            comments.Count == 0 ? "No comments anticipated." : $"{comments.Count} anticipated comment(s): {revise} revise (criteria not met), {missing} missing content, {verify} to verify.",
            "Scope: " + string.Join(", ", scope.Where(k => k.Key != "always" && k.Value).Select(k => k.Key).DefaultIfEmpty("site drainage only")) + ".",
            reportText is null ? "No report was given: only the calculations were checked." : $"Report: {Regex.Matches(reportText, @"\w+").Count:N0} words read.",
        };
        var tablesOut = new List<ToolTable>
        {
            new("Anticipated review comments", ["No.", "Type", "Topic", "Comment", "Reference", "Found in", "Response"], rows,
                ["Copy into the comment-response letter; the Response column is left for your answers.",
                 "Precedence where criteria differ: IDCM 2012 > Storm Water Quality Manual 2009 > Drainage Criteria Manual. This is a pre-check, not a City review."]),
        };
        if (docRows.Count > 0) tablesOut.Add(new("Documents read", ["Document", "File", "Pages", "Characters", "Status"], docRows));
        if (checklist.Count > 0) tablesOut.Add(new("Report content checklist", ["Status", "Item", "Reference", "Applies to", "Evidence"], checklist));
        if (sheetRows.Count > 0) tablesOut.Add(new("Plan sheets", ["Status", "Sheet", "Reference"], sheetRows));
        if (modelRows.Count > 0) tablesOut.Add(new("Calculation findings", ["Source", "Item", "Check", "Result"], modelRows));
        if (consistency.Count > 0) tablesOut.Add(new("Model elements not named in the report", ["Network", "Element", "Finding"], consistency));
        int calcFails = comments.Count(c => c.Type == "REVISE" && c.Source == "calculation");
        if (calcFails > 0) msgs.Add(CalcMessage.Warning("W-PC-001", $"{calcFails} design criteria are not met in the calculations."));
        return new ToolResult(title, summary, tablesOut, msgs);
    }

    // ---------------- helpers ----------------

    private static bool Flag(ToolInput p, string key, Func<bool> auto) => (p.Text(key) ?? "auto").ToLowerInvariant() switch
    {
        "yes" or "y" or "true" => true,
        "no" or "n" or "false" => false,
        _ => auto(),
    };

    /// <summary>The raw lines under a [Section] heading, up to the next heading (narrative text keeps its commas).</summary>
    private static string SectionText(string input, string section)
    {
        var lines = input.Replace("\r", "").Split('\n');
        var sb = new System.Text.StringBuilder();
        bool on = false;
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) { on = string.Equals(t[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase); continue; }
            if (on) sb.AppendLine(l);
        }
        return sb.ToString();
    }

    private static string Snippet(string text, int at)
    {
        int s = Math.Max(0, at - 40), e = Math.Min(text.Length, at + 80);
        return "…" + Regex.Replace(text[s..e], @"\s+", " ").Trim() + "…";
    }

    /// <summary>A pattern as words for the evidence column: first alternative of each group, classes as a space.</summary>
    private static string Plain(string regex)
    {
        var t = Regex.Replace(regex, @"\\b|\\s\?|\\s", " ");
        t = Regex.Replace(t, @"\(\?:", "(");
        t = Regex.Replace(t, @"\(([^|()]*)(\|[^()]*)?\)\??", "$1");
        t = Regex.Replace(t, @"\[[^\]]*\]\??", " ");
        t = t.Replace("?", "").Replace("\\", "");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    /// <summary>The criteria section behind a compliance row, from the tool and check names (D-227).</summary>
    private static string RefFor(string source, string check)
    {
        string s = (source + " " + check).ToLowerInvariant();
        if (s.Contains("hgl")) return "IDCM §4.6.7";
        if (s.Contains("storm network") || s.Contains("pipe")) return "IDCM §4.6";
        if (s.Contains("detention") || s.Contains("outlet") || s.Contains("pond") || s.Contains("freeboard") && s.Contains("pond")) return "IDCM §4.10";
        if (s.Contains("culvert")) return "IDCM §4.7.2";
        if (s.Contains("spread") || s.Contains("street") || s.Contains("gutter")) return "IDCM §4.4.9–4.4.10";
        if (s.Contains("inlet")) return "IDCM §4.5";
        if (s.Contains("water quality") || s.Contains("wqcv")) return "SWQM 2009 via IDCM §4.1.7";
        if (s.Contains("channel") || s.Contains("lining") || s.Contains("shear")) return "IDCM §4.8.3";
        if (s.Contains("rational") || s.Contains("tr-55")) return "IDCM §4.2–4.3";
        return "IDCM Ch. 4";
    }
}
