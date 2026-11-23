using System.Globalization;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Permit and approval tracker (premium, D-280). From a few project facts (disturbed area, floodplain, waters, public
/// mains, subdivision, state highway access, city or county) it lists the permits and approvals the project needs, with
/// agency, trigger, dependencies, review cycles and fees; then schedules them by the critical path method, using actual
/// submitted and approved dates where given, and forecasts when construction can start. Review comment responses are
/// handled by the review comment tracker; the plan-review pre-checker anticipates them.
/// </summary>
public sealed class PermitTrackerTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Id => "permit-tracker";
    public string Name => "Permit and approval tracker (critical path to construction)";
    public string Description => "Lists every permit and approval the project needs from its facts (disturbed area, floodplain, wetlands, public water and sewer, subdivision, state highway access, city or county), with agency, why it is needed, dependencies, review cycles and fees; schedules them by the critical path method with your actual submitted and approved dates; and forecasts the earliest construction start, the critical path, the float on everything else and what to do next.";
    public bool UsesNetwork => false;

    private sealed record Item(string Id, string Name, string Agency, string Why, List<string> Deps, double Prep, double Review, int Cycles, double Resubmit, string Fee, string Reference);

    /// <summary>The catalog: typical Rapid City / South Dakota approvals. Durations are calendar days and typical, not promised.</summary>
    private static List<Item> Catalog(ToolInput p, bool city)
    {
        string local = city ? "City of Rapid City" : "Pennington County";
        double area = p.Number("disturbed area", 0);
        bool Yes(string k) => p.Flag(k, false);
        var list = new List<Item>();
        void Add(bool when, Item i) { if (when) list.Add(i); }
        Add(Yes("subdivision"), new("PRELIM", "Preliminary subdivision plan", local + (city ? " Community Development" : " Planning"), "subdivision = yes", [], 20, 30, 2, 10, "", city ? "Rapid City Code Title 16" : "Pennington County Subdivision Regulations"));
        Add(true, new("DRAIN", "Storm water report and drainage plan", local + (city ? " Public Works (Development Engineering)" : " Highway Department"), "every development (IDCM §1.15)", Yes("subdivision") ? ["PRELIM"] : [], 15, 21, 2, 7, "", "IDCM §1.15, §4.1"));
        Add(Yes("subdivision") || Yes("public improvements"), new("ENG", "Development engineering plans (streets, storm, water, sewer)", local + (city ? " Public Works (Development Engineering)" : " Highway Department"), "public improvements", Yes("subdivision") ? ["PRELIM"] : [], 30, 21, 3, 10, "", "IDCM §1"));
        Add(Yes("public water or sewer"), new("DANR-WS", "Water and sewer main plan approval", "SD DANR Drinking Water / Surface Water Quality", "public water or sewer = yes", Yes("subdivision") || Yes("public improvements") ? ["ENG"] : [], 5, 30, 1, 10, "", "ARSD 74:53:01, 74:53:03"));
        Add(area >= 1, new("SWPPP", "Storm water pollution prevention plan (SWPPP)", "Engineer (no agency review)", $"{area:0.#} ac disturbed ≥ 1 ac", ["DRAIN"], 10, 0, 1, 0, "", "SD General Permit for Construction Activities"));
        Add(area >= 1, new("NOI", "Construction storm water general permit (NOI)", "SD DANR Surface Water Quality", $"{area:0.#} ac disturbed ≥ 1 ac", ["SWPPP"], 2, 7, 1, 0, "", "SD General Permit for Construction Activities (SDR10####)"));
        Add(Yes("in floodplain"), new("FLOOD", "Floodplain development permit", local + " floodplain administrator", "in floodplain = yes", ["DRAIN"], 10, 30, 2, 10, "", city ? "Rapid City Code Ch. 15.32" : "Pennington County Ordinance 7"));
        Add(Yes("in floodplain") && Yes("map revision"), new("CLOMR", "CLOMR (conditional letter of map revision)", "FEMA (through the community)", "map revision = yes", ["FLOOD"], 30, 90, 2, 30, "", "44 CFR 65; FEMA MT-2"));
        Add(Yes("waters or wetlands"), new("404", "Section 404 permit (nationwide or individual)", "US Army Corps of Engineers, Omaha District", "waters or wetlands = yes", [], 20, 45, 1, 15, "", "Clean Water Act §404"));
        Add(Yes("waters or wetlands"), new("401", "Section 401 water quality certification", "SD DANR Surface Water Quality", "waters or wetlands = yes", ["404"], 0, 30, 1, 0, "", "Clean Water Act §401"));
        Add(Yes("state highway access"), new("SDDOT", "Access / right-of-way permit", "SDDOT Rapid City Region", "state highway access = yes", ["ENG"], 10, 30, 2, 10, "", "ARSD 70:09"));
        var gradeDeps = new List<string> { "DRAIN" };
        if (area >= 1) gradeDeps.Add("NOI");
        if (Yes("in floodplain")) gradeDeps.Add("FLOOD");
        if (Yes("in floodplain") && Yes("map revision")) gradeDeps.Add("CLOMR");
        if (Yes("waters or wetlands")) { gradeDeps.Add("404"); gradeDeps.Add("401"); }
        Add(true, new("GRADE", "Grading permit", local + (city ? " Building Services / Public Works" : " Planning"), "earthwork", gradeDeps, 3, 10, 1, 5, "", city ? "Rapid City Code Ch. 15.12 (grading)" : "Pennington County"));
        Add(Yes("subdivision"), new("PLAT", "Final plat", local + (city ? " Community Development / City Council" : " Board of Commissioners"), "subdivision = yes", ["ENG", .. Yes("public water or sewer") ? new[] { "DANR-WS" } : []], 10, 30, 1, 10, "", city ? "Rapid City Code Title 16" : "Pennington County Subdivision Regulations"));
        var startDeps = new List<string> { "GRADE" };
        if (Yes("subdivision") || Yes("public improvements")) startDeps.Add("ENG");
        if (Yes("public water or sewer")) startDeps.Add("DANR-WS");
        if (Yes("state highway access")) startDeps.Add("SDDOT");
        list.Add(new("START", "Construction start (pre-construction meeting)", local, "milestone", startDeps, 0, 0, 1, 0, "", ""));
        return list;
    }

    public string SampleInput(DesignToolContext context) => """
        # Permit and approval tracker. Project facts decide which approvals are listed (typical Rapid City / South Dakota
        # set; edit or add rows under [Permits]). Durations are calendar days and typical, not promised: change them to
        # what the agencies are doing this season. Under [Permits], a row with an existing Id changes that item (blank
        # cells keep the catalog value); a new Id adds an item. Status: Submitted and Approved dates (yyyy-mm-dd) turn the
        # forecast into a tracker. Fees: enter from the current fee schedules.
        start = 2026-12-01            # when work on submittals starts
        today = 2027-01-15
        inside city = yes
        disturbed area = 12           # ac
        subdivision = yes
        public improvements = yes
        public water or sewer = yes
        in floodplain = yes
        map revision = no
        waters or wetlands = no
        state highway access = no

        [Permits]
        Id	Name	Agency	Depends on	Prep	Review	Cycles	Resubmit	Fee	Submitted	Approved	Note
        PRELIM									2026-12-10	2027-01-12
        DRAIN									2027-01-05
        FLOOD				15	45
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Permit and approval tracker";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        DateTime? Date(string? s) => s is not null && DateTime.TryParseExact(s.Trim(), ["yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy"], Inv, DateTimeStyles.None, out var d) ? d : null;
        var start = Date(p.Text("start"));
        if (start is null) return ToolResult.Failed(title, CalcMessage.Error("E-PT-001", "Give start = yyyy-mm-dd: the day work on the submittals begins."));
        var today = Date(p.Text("today")) ?? DateTime.Today;
        bool city = p.Flag("inside city", true);
        var items = Catalog(p, city).ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var order = items.Keys.ToList();

        // ---- overrides and additions
        var h = p.Header("Permits").Select(x => x.Trim().ToLowerInvariant()).ToList();
        int C(string n) => h.FindIndex(x => x.StartsWith(n, StringComparison.Ordinal));
        int cId = C("id"), cName = C("name"), cAg = C("agency"), cDep = C("depends"), cPrep = C("prep"), cRev = C("review"), cCyc = C("cycles"), cRes = C("resubmit"), cFee = C("fee"), cSub = C("submitted"), cApp = C("approved"), cNote = C("note");
        var actual = new Dictionary<string, (DateTime? Sub, DateTime? App, string Note)>(StringComparer.OrdinalIgnoreCase);
        int row = 0;
        foreach (var r in p.Rows("Permits"))
        {
            row++;
            string? G(int k) => k >= 0 && k < r.Length && r[k].Trim().Length > 0 ? r[k].Trim() : null;
            double? N(int k) { var s = G(k); if (s is null) return null; if (double.TryParse(s, NumberStyles.Float, Inv, out var v) && v >= 0) return v; msgs.Add(CalcMessage.Error("E-PT-002", $"[Permits] row {row}: \"{s}\" is not a number of days.")); return null; }
            var id = G(cId);
            if (id is null) { msgs.Add(CalcMessage.Error("E-PT-003", $"[Permits] row {row}: every row needs an Id.")); continue; }
            var deps = G(cDep)?.Split([';', ' ', '+'], StringSplitOptions.RemoveEmptyEntries).ToList();
            if (items.TryGetValue(id, out var it))
                items[id] = it with { Name = G(cName) ?? it.Name, Agency = G(cAg) ?? it.Agency, Deps = deps ?? it.Deps, Prep = N(cPrep) ?? it.Prep, Review = N(cRev) ?? it.Review, Cycles = (int)(N(cCyc) ?? it.Cycles), Resubmit = N(cRes) ?? it.Resubmit, Fee = G(cFee) ?? it.Fee };
            else
            {
                items[id] = new(id, G(cName) ?? id, G(cAg) ?? "", "added", deps ?? [], N(cPrep) ?? 10, N(cRev) ?? 30, (int)(N(cCyc) ?? 1), N(cRes) ?? 10, G(cFee) ?? "", "");
                order.Insert(order.IndexOf("START"), id);
                if (!items["START"].Deps.Contains(id)) items["START"].Deps.Add(id);
            }
            if (G(cSub) is { } sd && Date(sd) is null || G(cApp) is { } ad && Date(ad) is null) msgs.Add(CalcMessage.Error("E-PT-004", $"[Permits] row {row}: dates are yyyy-mm-dd."));
            actual[id] = (Date(G(cSub)), Date(G(cApp)), G(cNote) ?? "");
        }
        foreach (var it in items.Values)
            foreach (var d in it.Deps.Where(d => !items.ContainsKey(d)).ToList())
            { msgs.Add(CalcMessage.Warning("W-PT-001", $"{it.Id} depends on {d}, which is not in the list; the dependency is ignored.")); it.Deps.Remove(d); }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        // ---- forward pass (topological), with actual dates fixing what has happened.
        var es = new Dictionary<string, double>(); var ef = new Dictionary<string, double>(); var dur = new Dictionary<string, double>();
        var done = new HashSet<string>(); var visiting = new HashSet<string>();
        double D(DateTime d) => (d - start.Value).TotalDays;
        void Visit(string id)
        {
            if (done.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException(id);
            var it = items[id];
            foreach (var d in it.Deps) Visit(d);
            double s0 = it.Deps.Count == 0 ? 0 : it.Deps.Max(d => ef[d]);
            double total = it.Prep + it.Cycles * it.Review + Math.Max(0, it.Cycles - 1) * it.Resubmit;
            actual.TryGetValue(id, out var a);
            double fin;
            if (a.App is { } ap) { fin = D(ap); s0 = Math.Max(Math.Min(0, fin), Math.Min(s0, fin - total)); }
            else if (a.Sub is { } sb) { s0 = Math.Min(s0, D(sb) - it.Prep); fin = Math.Max(D(sb) + it.Cycles * it.Review + Math.Max(0, it.Cycles - 1) * it.Resubmit, D(today)); }
            else { s0 = Math.Max(s0, Math.Max(0, D(today))); fin = s0 + total; }   // nothing not yet started can start in the past
            es[id] = s0; ef[id] = fin; dur[id] = fin - s0;
            visiting.Remove(id); done.Add(id);
        }
        try { foreach (var id in order) Visit(id); }
        catch (InvalidOperationException ex) { return ToolResult.Failed(title, CalcMessage.Error("E-PT-005", $"The dependencies loop back on themselves at {ex.Message}.")); }

        // ---- backward pass from the construction start.
        double finish = ef["START"];
        // Items that lead to construction start are scheduled against it; the rest (e.g. the final plat) against the last finish.
        var feeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Up(string id) { if (feeds.Add(id)) foreach (var d in items[id].Deps) Up(d); }
        Up("START");
        double end = ef.Values.Max();
        var lf = order.ToDictionary(i => i, i => feeds.Contains(i) ? finish : end);
        foreach (var id in order.AsEnumerable().Reverse())
        {
            // Float of an item feeding construction start is measured to the start, not to the plat.
            var succ = order.Where(s => items[s].Deps.Contains(id, StringComparer.OrdinalIgnoreCase) && (!feeds.Contains(id) || feeds.Contains(s))).ToList();
            if (succ.Count > 0) lf[id] = succ.Min(s => lf[s] - dur[s]);
        }
        var floatD = order.ToDictionary(i => i, i => lf[i] - ef[i]);
        // Critical path: walk back from START along zero-float predecessors.
        var path = new List<string>();
        for (string? at = "START"; at is not null;)
        {
            path.Insert(0, at);
            at = items[at].Deps.Where(d => Math.Abs(ef[d] - es[at]) < 0.5).OrderByDescending(d => ef[d]).FirstOrDefault();
        }

        string Day(double d) => start.Value.AddDays(Math.Round(d)).ToString("yyyy-MM-dd", Inv);
        string Status(string id)
        {
            actual.TryGetValue(id, out var a);
            if (a.App is not null) return "APPROVED";
            if (a.Sub is not null) return "IN REVIEW";
            if (id == "START") return "";
            if (es[id] <= D(today) + 0.5) return items[id].Deps.All(d => actual.TryGetValue(d, out var x) && x.App is not null) ? "READY" : "WAITING";
            return "NOT STARTED";
        }
        var rows = order.Select(id =>
        {
            var it = items[id];
            actual.TryGetValue(id, out var a);
            return (IReadOnlyList<object?>)[id, it.Name, it.Agency, it.Why, string.Join(", ", it.Deps), it.Prep, it.Review, it.Cycles, Day(es[id]), Day(ef[id]), a.App is not null ? null : Math.Round(floatD[id]), path.Contains(id) ? "yes" : "", Status(id), it.Fee.Length > 0 ? it.Fee : id == "START" || it.Agency.StartsWith("Engineer", StringComparison.Ordinal) ? "" : "see fee schedule", it.Reference + (!string.IsNullOrEmpty(a.Note) ? " — " + a.Note : "")];
        }).ToList();

        var tables = new List<ToolTable>
        {
            new("Permits and approvals", ["Id", "Permit / approval", "Agency", "Why needed", "After", "Prep (d)", "Review (d)", "Cycles", "Start", "Finish", "Float (d)", "Critical", "Status", "Fee", "Reference"], rows,
                ["Typical Rapid City / South Dakota set; durations are calendar days, review cycles include resubmittal time. Approved and submitted dates override the forecast. Confirm requirements and fees with each agency."]),
            new("Critical path", ["Step", "Id", "Permit / approval", "Finish"], path.Select((id, i) => (IReadOnlyList<object?>)[i + 1, id, items[id].Name, Day(ef[id])]).ToList()),
        };
        var next = order.Where(id => Status(id) is "READY").Select(id => (IReadOnlyList<object?>)[id, items[id].Name, Day(es[id]), floatD[id] < 0.5 ? "critical: any delay moves construction" : $"{Math.Round(floatD[id])} d float"]).ToList();
        var late = order.Where(id => actual.TryGetValue(id, out var a) && a.Sub is { } sb && a.App is null && D(today) > D(sb) + items[id].Cycles * items[id].Review + Math.Max(0, items[id].Cycles - 1) * items[id].Resubmit)
            .Select(id => (IReadOnlyList<object?>)[id, items[id].Name, "", "past its forecast approval: call the reviewer"]).ToList();
        if (next.Count + late.Count > 0) tables.Add(new("Next actions", ["Id", "Permit / approval", "Can start", "Note"], [.. late, .. next]));
        double totalFees = items.Values.Sum(i => double.TryParse(i.Fee.TrimStart('$').Replace(",", ""), NumberStyles.Float, Inv, out var f) ? f : 0);

        var summary = new List<string>
        {
            $"{order.Count - 1} permits and approvals; earliest construction start {Day(finish)} ({Math.Round(finish - D(today))} days from {today:yyyy-MM-dd}).",
            "Critical path: " + string.Join(" → ", path.Where(i => i != "START").Select(i => items[i].Name)) + ".",
            $"{order.Count(i => Status(i) == "APPROVED")} approved, {order.Count(i => Status(i) == "IN REVIEW")} in review, {next.Count} ready to submit now." + (totalFees > 0 ? $" Fees entered: ${totalFees:N0}." : ""),
        };
        foreach (var l in late) msgs.Add(CalcMessage.Warning("W-PT-002", $"{l[1]} is past its forecast approval date; follow up with the reviewer."));
        msgs.Add(CalcMessage.Info("I-PT-001", "Review comments: track and answer them with the review comment tracker; the plan-review pre-checker anticipates them before submittal."));

        // Gantt-style chart: one bar per item (y = its row, from the top).
        var series = new List<ChartSeries>();
        for (int i = 0; i < order.Count; i++)
        {
            string id = order[i]; double y = order.Count - i;
            series.Add(new ChartSeries($"{id}{(path.Contains(id) ? " (critical)" : "")}", [(es[id], y), (Math.Max(ef[id], es[id] + 0.5), y)], !path.Contains(id)));
        }
        series.Add(new ChartSeries("Today", [(D(today), 0), (D(today), order.Count + 1)], true));
        var charts = new List<ToolChart> { new("Schedule (days from start; solid = critical path)", $"Days from {start:yyyy-MM-dd}", "Item (top = first)", series) };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }
}
