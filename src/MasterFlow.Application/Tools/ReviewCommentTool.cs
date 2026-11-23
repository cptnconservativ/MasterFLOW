using System.Globalization;
using MasterFlow.Application.Reports;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Review comment tracker (premium): the agency's plan review comments with the design team's responses and status,
/// across review rounds, saved with the project. Each comment that names a structure or pipe of the selected network
/// shows that element's current computed values, and the storm network plan review's open findings can be added as
/// tracked items. Produces the comment–response matrix and an open-items list, and a response letter as a report
/// (PDF or Word through Save…).
/// </summary>
public sealed class ReviewCommentTool : IDesignTool
{
    public string Id => "review-comments";
    public string Name => "Review comment tracker and response letter";
    public string Description => "Tracks plan review comments by round, reviewer, sheet and item with your responses and status (open, responded, closed, deferred). Comments that name a structure or pipe show its current HGL, clearance, flow and velocity from the selected network; the storm network plan review's failing checks can be pulled in as tracked items. Gives the comment–response matrix, the open items, counts by reviewer and status, and a response-to-comments letter (Save… as PDF or Word).";
    public bool UsesNetwork => true;

    private static readonly string[] Statuses = ["open", "responded", "closed", "deferred"];

    public string SampleInput(DesignToolContext context)
    {
        string a = "MH-2", b = "P1";
        if (context.Network is { } n && n.Structures.Count > 1 && n.Pipes.Count > 0) { a = n.Structures[1].Name; b = n.Pipes[0].Name; }
        return $"""
            # Review comment tracker. Separate columns with tabs (comments may contain commas). Status: open, responded,
            # closed (reviewer accepted) or deferred. Item = a structure or pipe of the selected network (its current
            # results are shown), a sheet detail, or blank. The tracker is saved with the project.
            project = Rapid Creek Subdivision Phase 2
            submittal = Final drainage plan and report
            agency = City of Rapid City Public Works, Engineering Services
            round = 2                       # current review round
            response date = 2026-11-21
            include plan review = yes       # add failing storm network plan review checks as tracked items
            signed = Kyle Hibbs, P.E.

            [Comments]
            ID	Round	Reviewer	Sheet	Item	Comment	Response	Status
            1	1	City Engineering	C5.1	{a}	HGL is within 1 ft of the rim for the 100-year storm (IDCM 4.6.7).	Rim raised and pipe upsized; see revised HGL table.	closed
            2	1	City Engineering	C5.2	{b}	Provide the outlet velocity and outlet protection at the outfall.	Riprap basin added per HEC-14; calculations in Appendix D.	responded
            3	1	City Engineering	Report		Show the WQCV computation per the Storm Water Quality Manual 2.2.2.	Added Section 5 with the WQCV and orifice plate design.	responded
            4	2	City Engineering	C5.1	{a}	Confirm the minimum 2-year velocity of 2.5 fps in all pipes.		open
            5	2	Fire Department	C2.0		Access road to the pond must support 75,000 lb.	Not a drainage comment; forwarded to the civil site team.	deferred
            """;
    }

    private sealed record Comment(string Id, int Round, string Reviewer, string Sheet, string Item, string Text, string Response, string Status);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Review comment tracker";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        int round = (int)Math.Round(p.Number("round", 1, "Round"));
        string project = p.Text("project") ?? context.Project?.Name ?? "";
        string submittal = p.Text("submittal") ?? "";
        string agency = p.Text("agency") ?? "";
        string date = p.Text("response date") ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var head = p.Header("Comments").Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string name, int fb) { int i = head.FindIndex(h => h == name || h.StartsWith(name, StringComparison.Ordinal)); return i >= 0 ? i : fb; }
        int cId = Col("id", 0), cRound = Col("round", 1), cRev = Col("reviewer", 2), cSheet = Col("sheet", 3), cItem = Col("item", 4), cText = Col("comment", 5), cResp = Col("response", 6), cStat = Col("status", 7);
        string Get(string[] r, int i) => i < r.Length ? r[i].Trim() : "";
        var comments = new List<Comment>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Comments"))
        {
            string id = Get(r, cId);
            if (id.Length == 0) id = (comments.Count + 1).ToString(CultureInfo.InvariantCulture);
            if (!ids.Add(id)) msgs.Add(CalcMessage.Warning("W-RC-001", $"Comment ID {id} is used twice; give each comment its own ID."));
            string text = Get(r, cText);
            if (text.Length == 0) { msgs.Add(CalcMessage.Warning("W-RC-002", $"Comment {id} has no text; skipped.")); continue; }
            int rnd = int.TryParse(Get(r, cRound), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rr) ? rr : round;
            string status = Get(r, cStat).ToLowerInvariant();
            if (status.Length == 0) status = Get(r, cResp).Length > 0 ? "responded" : "open";
            if (!Statuses.Contains(status)) { msgs.Add(CalcMessage.Warning("W-RC-003", $"Comment {id}: status \"{status}\" is not open, responded, closed or deferred; treated as open.")); status = "open"; }
            comments.Add(new(id, rnd, Get(r, cRev), Get(r, cSheet), Get(r, cItem), text, Get(r, cResp), status));
        }
        if (p.Rows("Comments").Count > 0 && head.Count > 0 && !head.Any(h => h.StartsWith("comment"))) msgs.Add(CalcMessage.Warning("W-RC-004", "The [Comments] header has no Comment column; the sixth column is read as the comment. Separate columns with tabs."));

        // Plan review findings as tracked items.
        var net = context.Network; var res = context.NetworkResult;
        if (p.Flag("include plan review", false))
        {
            if (net is null || res is not { Succeeded: true }) msgs.Add(CalcMessage.Info("I-RC-001", "include plan review = yes needs a selected, solved storm network; no findings added."));
            else if (DesignTools.Find("network-plan-review") is { } pr)
            {
                string prInput = context.ToolInputs.TryGetValue(pr.Id, out var t) ? t : "";
                var prRes = pr.Run(prInput, context);
                var tbl = prRes.Tables.FirstOrDefault(x => x.Title == "Checklist");
                int k = 0;
                if (tbl is not null)
                {
                    int iRes = IndexOf(tbl, "Result"), iCheck = IndexOf(tbl, "Check"), iRef = IndexOf(tbl, "IDCM"), iItem = IndexOf(tbl, "Item"), iVal = IndexOf(tbl, "Value"), iLim = IndexOf(tbl, "Limit"), iNote = IndexOf(tbl, "Note");
                    foreach (var row in tbl.Rows)
                    {
                        string result = Cell(row, iRes);
                        if (result is not ("FAIL" or "REVIEW")) continue;
                        string item = Cell(row, iItem);
                        // A comment already tracking this element and check is not added twice.
                        string check = Cell(row, iCheck);
                        if (comments.Any(c => c.Item.Equals(item, StringComparison.OrdinalIgnoreCase) && c.Text.Contains(check, StringComparison.OrdinalIgnoreCase))) continue;
                        k++;
                        string text = $"{check} ({Cell(row, iRef)}): {Cell(row, iVal)} vs limit {Cell(row, iLim)}. {Cell(row, iNote)}".Trim();
                        comments.Add(new($"PR-{k}", round, "MasterFlow plan review", "", item, $"[{result}] {text}", "", "open"));
                    }
                }
                if (k > 0) msgs.Add(CalcMessage.Info("I-RC-002", $"{k} failing or to-review plan review check(s) added as open items PR-1… (IDCM §4.6). They clear when the network passes."));
            }
        }
        if (comments.Count == 0) msgs.Add(CalcMessage.Error("E-RC-001", "Enter comments under [Comments] (ID, Round, Reviewer, Sheet, Item, Comment, Response, Status), separated by tabs."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        // Current network values of the named elements.
        string Live(string item)
        {
            if (item.Length == 0 || res is not { Succeeded: true }) return "";
            bool us = net!.Units == UnitSystem.USCustomary; string L = us ? "ft" : "m", V = us ? "fps" : "m/s", Q = us ? "cfs" : "m³/s";
            if (res.Structure(item) is { } s)
            {
                string clr = s.HglClearance is { } c ? $", rim − HGL {c:F2} {L}" : "";
                return Inv($"HGL {s.Hgl:F2}, EGL {s.Egl:F2}{clr}");
            }
            if (res.Pipe(item) is { } pp)
                return Inv($"Q {pp.Pipe.Discharge:G4} {Q}, V {pp.VelocityUp:F2} {V}, HGL {pp.HglUp:F2}/{pp.HglDown:F2}, {pp.Regime}");
            return "";
        }
        int unknown = 0;
        var matrix = new List<IReadOnlyList<object?>>();
        foreach (var c in comments.OrderBy(c => c.Round).ThenBy(c => c.Id, Comparer<string>.Create(NaturalCompare)))
        {
            string live = Live(c.Item);
            if (c.Item.Length > 0 && live.Length == 0 && res is { Succeeded: true } && LooksLikeElement(c.Item)) unknown++;
            matrix.Add([c.Id, c.Round, c.Reviewer, c.Sheet, c.Item, c.Text, c.Response, c.Status, live]);
        }
        if (unknown > 0) msgs.Add(CalcMessage.Info("I-RC-003", $"{unknown} comment item(s) do not name a structure or pipe of network \"{net?.Name}\"; no current values are shown for them."));

        var open = comments.Where(c => c.Status == "open" || (c.Status != "deferred" && c.Response.Length == 0)).ToList();
        var noResponse = comments.Where(c => c.Status is "responded" or "closed" && c.Response.Length == 0).ToList();
        foreach (var c in noResponse) msgs.Add(CalcMessage.Warning("W-RC-005", $"Comment {c.Id} is marked {c.Status} but has no response."));
        var carried = open.Where(c => c.Round < round).ToList();
        if (carried.Count > 0) msgs.Add(CalcMessage.Warning("W-RC-006", $"{carried.Count} comment(s) from earlier rounds are still open: {string.Join(", ", carried.Select(c => c.Id))}."));
        if (open.Count > 0) msgs.Add(CalcMessage.Warning("W-RC-007", $"{open.Count} comment(s) need a response before resubmittal: {string.Join(", ", open.Select(c => c.Id))}."));

        var openRows = open.Select(c => (IReadOnlyList<object?>)new object?[] { c.Id, c.Round, c.Reviewer, c.Sheet, c.Item, c.Text, c.Round < round ? "carried over" : "", Live(c.Item) }).ToList();
        var byReviewer = comments.GroupBy(c => c.Reviewer.Length > 0 ? c.Reviewer : "(none)").OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<object?>)new object?[] { g.Key, g.Count(), g.Count(c => c.Status == "open"), g.Count(c => c.Status == "responded"), g.Count(c => c.Status == "closed"), g.Count(c => c.Status == "deferred") }).ToList();
        byReviewer.Add(["Total", comments.Count, comments.Count(c => c.Status == "open"), comments.Count(c => c.Status == "responded"), comments.Count(c => c.Status == "closed"), comments.Count(c => c.Status == "deferred")]);
        var byRound = comments.GroupBy(c => c.Round).OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<object?>)new object?[] { g.Key, g.Count(), g.Count(c => c.Status == "open"), g.Count(c => c.Status == "responded"), g.Count(c => c.Status == "closed"), g.Count(c => c.Status == "deferred") }).ToList();

        var mHead = new[] { "ID", "Round", "Reviewer", "Sheet", "Item", "Comment", "Response", "Status", "Current result (network)" };
        var tables = new List<ToolTable>
        {
            new("Comment–response matrix", mHead, matrix, [res is { Succeeded: true } ? $"Current results from network \"{net!.Name}\" as last solved." : "Select and solve a storm network to show current results for named structures and pipes."]),
            new("Open items", ["ID", "Round", "Reviewer", "Sheet", "Item", "Comment", "Note", "Current result (network)"], openRows, ["Open: status open, or no response yet (deferred items excepted)."]),
            new("Comments by reviewer", ["Reviewer", "Total", "Open", "Responded", "Closed", "Deferred"], byReviewer),
            new("Comments by round", ["Round", "Total", "Open", "Responded", "Closed", "Deferred"], byRound),
        };

        // Response letter (Save… → PDF or Word).
        var body = new List<ReportBlock>
        {
            new ReportHeading(1, "Response to review comments"),
            new ReportParagraph(Inv($"This letter responds to the review round {round} comments{(agency.Length > 0 ? $" from {agency}" : "")} on {(submittal.Length > 0 ? submittal : "the submittal")}{(project.Length > 0 ? $" for {project}" : "")}. Each comment is repeated with its response below.")),
        };
        foreach (var g in comments.Where(c => c.Status != "closed").GroupBy(c => c.Reviewer.Length > 0 ? c.Reviewer : "General"))
        {
            body.Add(new ReportHeading(2, g.Key));
            body.Add(new ReportTable(new ToolTable(g.Key, ["ID", "Sheet", "Comment", "Response"], g.Select(c => (IReadOnlyList<object?>)new object?[] { c.Id, c.Sheet, c.Text, c.Response.Length > 0 ? c.Response : "(response pending)" }).ToList())));
        }
        if (comments.Any(c => c.Status == "closed"))
        {
            body.Add(new ReportHeading(2, "Comments closed in earlier rounds"));
            body.Add(new ReportTable(new ToolTable("Closed", ["ID", "Round", "Comment", "Response"], comments.Where(c => c.Status == "closed").Select(c => (IReadOnlyList<object?>)new object?[] { c.Id, c.Round, c.Text, c.Response }).ToList())));
        }
        if (p.Text("signed") is { } signed) body.Add(new ReportParagraph($"Sincerely, {signed}"));
        var report = new ReportDocument($"Response to review comments{(project.Length > 0 ? " — " + project : "")}",
            [("Project", project), ("Submittal", submittal), ("Agency", agency), ("Review round", round.ToString(CultureInfo.InvariantCulture)), ("Date", date)],
            body, [new ReportTable(tables[0])]) { TableOfContents = false };

        var summary = new List<string>
        {
            $"{comments.Count} comment(s), round {round}: {comments.Count(c => c.Status == "open")} open, {comments.Count(c => c.Status == "responded")} responded, {comments.Count(c => c.Status == "closed")} closed, {comments.Count(c => c.Status == "deferred")} deferred.",
            open.Count == 0 ? "Every comment has a response: ready to resubmit." : $"{open.Count} item(s) still need a response.",
            "Save… writes the response letter (PDF or Word) with the full matrix as an appendix.",
        };
        return new ToolResult(title, summary, tables, msgs) { Report = report };
    }


    private static int IndexOf(ToolTable t, string h) => t.Headers.ToList().FindIndex(x => x.Equals(h, StringComparison.OrdinalIgnoreCase));
    private static string Cell(IReadOnlyList<object?> row, int i) => i >= 0 && i < row.Count ? DesignTools.Cell(row[i]) : "";
    private static bool LooksLikeElement(string s) => s.Length <= 16 && !s.Contains(' ');
    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// <summary>Orders "2" before "10" and "PR-2" before "PR-10".</summary>
    private static int NaturalCompare(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                int c = long.Parse(a[si..i], CultureInfo.InvariantCulture).CompareTo(long.Parse(b[sj..j], CultureInfo.InvariantCulture));
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
