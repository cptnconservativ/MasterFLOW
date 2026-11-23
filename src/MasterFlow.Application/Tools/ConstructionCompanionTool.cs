using System.Globalization;
using System.Text.RegularExpressions;
using MasterFlow.Application.Reports;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Construction-phase companion (premium, D-300). Checks a contractor's submittal or RFI against the design: pipe
/// material substitutions (size, Manning's n, allowed materials) and RFI field changes (inverts, rims, sizes) are put
/// into the storm network, which is re-solved and re-reviewed against the criteria (HGL, velocity, capacity, cover,
/// plan review); other items (riprap, castings, bedding, anything with a number or a required value) are compared
/// property by property. It gives each item a result and drafts the response letter with the disposition.
/// </summary>
public sealed partial class ConstructionCompanionTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Id => "construction-companion";
    public string Name => "Construction-phase companion (submittal and RFI review with response letter)";
    public string Description => "Checks contractor submittals and RFIs against the design: pipe substitutions and field changes are put into the storm network, re-solved and re-reviewed (HGL clearance, velocities, capacity, cover, IDCM §4.6 plan review), and riprap, castings and other items are compared with the design values. Each item gets OK or a reason, and the response letter is drafted with the disposition (no exceptions taken, make corrections noted, revise and resubmit, rejected), ready to save as Word or PDF.";
    public bool UsesNetwork => true;

    /// <summary>Manning's n by pipe material (FHWA HDS-5 Table B.1 / HEC-22 Table 6-1, typical design values).</summary>
    public static readonly IReadOnlyDictionary<string, double> MaterialN = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["RCP"] = 0.013, ["HDPE"] = 0.012, ["PVC"] = 0.010, ["CMP"] = 0.024, ["DIP"] = 0.012, ["PP"] = 0.012, ["SRPE"] = 0.012,
    };

    public string SampleInput(DesignToolContext context)
    {
        var net = context.Network;
        string first = net?.Pipes.FirstOrDefault()?.Name ?? "P1", last = net?.Pipes.LastOrDefault()?.Name ?? "P2";
        var p2 = net?.Pipes.FirstOrDefault(p => p.Name == last);
        var st = net?.Structures.FirstOrDefault(s => s.Name == p2?.From);
        return $"""
            # Construction-phase companion: a submittal or RFI checked against the design, with the response drafted.
            # [Pipe]: proposed pipe for some or all pipes (Pipes = all, or names separated by spaces). Blank cells keep the
            #   design value; n defaults by material (RCP 0.013, HDPE 0.012, PVC 0.010, CMP 0.024, DIP 0.012, PP 0.012).
            # [Changes]: field changes asked for in an RFI, as "<pipe> upstream invert | downstream invert | size (in.) = value"
            #   or "<structure> rim = value"; each RFI is checked on its own and all together.
            # [Compare]: anything else, property by property; Rule is >=, <=, = or "one of" (Required lists the choices with /).
            submittal = 07 — Storm sewer pipe, castings and riprap
            contractor = ABC Utilities
            date = 2027-05-03
            network = {net?.Name ?? ""}
            allowed materials = RCP, HDPE, PP   # per the project specifications
            reviewer =

            [Pipe]
            Pipes	Material	Size (in.)	Class	n
            all	HDPE
            {first}	RCP		III

            [Changes]
            RFI	Change	Reason
            RFI-3	{last} upstream invert = {((p2?.UpstreamInvert ?? 102) + 0.45).ToString("0.00", Inv)}	rock at {st?.Name ?? "CB-3"}

            [Compare]
            Item	Property	Required	Proposed	Rule	Reference
            Riprap at outfall	D50 (in.)	12	9	>=	HEC-14 apron design
            Riprap at outfall	Thickness (in.)	18	18	>=	1.5 × D50 (HEC-14)
            Grates	Load rating	H-20/H-25/heavy duty	H-20	one of	IDCM §4.5
            Grates	Bicycle safe	yes	yes	=	IDCM §4.5
            Bedding	Material	Class B/Class C	Class B	one of	project specifications
            """;
    }

    private sealed record Finding(string Item, string Check, object? Design, object? Proposed, string Result, string Note, string Reference);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Construction-phase companion";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        var found = new List<Finding>();
        string netName = p.Text("network") ?? "";
        var net = context.Networks.Select(n => n.Network).FirstOrDefault(n => netName.Length == 0 ? n.Name == context.Network?.Name : n.Name.Equals(netName, StringComparison.OrdinalIgnoreCase))
                  ?? (netName.Length == 0 || context.Network?.Name.Equals(netName, StringComparison.OrdinalIgnoreCase) == true ? context.Network : null);
        bool needNet = p.Rows("Pipe").Count > 0 || p.Rows("Changes").Count > 0;
        if (needNet && net is null) return ToolResult.Failed(title, CalcMessage.Error("E-CC-001", netName.Length == 0 ? "Select the storm network the submittal is for (or name it: network = …)." : $"No storm network is named \"{netName}\"."));
        bool si = net?.Units == UnitSystem.SI;
        double inch = si ? 0.001 : 1.0 / 12;
        string lu = si ? "m" : "ft", su = si ? "mm" : "in.";
        var allowed = (p.Text("allowed materials") ?? "").Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ---- pipe substitutions
        var proposed = net;
        if (net is not null)
        {
            var h = p.Header("Pipe").Select(x => x.Trim().ToLowerInvariant()).ToList();
            int C(string n) => h.FindIndex(x => x.StartsWith(n, StringComparison.Ordinal));
            int cP = C("pipe"), cM = C("material"), cS = C("size"), cCl = C("class"), cN = h.FindIndex(x => x == "n" || x.StartsWith("n ", StringComparison.Ordinal) || x.StartsWith("manning", StringComparison.Ordinal));
            var pipes = net.Pipes.ToDictionary(x => x.Name, x => x);
            // Rows are merged per pipe in order, so "all HDPE" followed by "P1 RCP" leaves P1 as RCP.
            var spec = new Dictionary<string, (string? Mat, double? Size, string? Class, double? N)>();
            foreach (var r in p.Rows("Pipe"))
            {
                string? G(int k) => k >= 0 && k < r.Length && r[k].Trim().Length > 0 ? r[k].Trim() : null;
                var which = (G(cP) ?? "all").Equals("all", StringComparison.OrdinalIgnoreCase) ? [.. net.Pipes.Select(x => x.Name)] : (G(cP) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                string? mat = G(cM);
                double? size = G(cS) is { } ss && double.TryParse(ss, NumberStyles.Float, Inv, out var sv) ? sv : null;
                double? nIn = G(cN) is { } ns && double.TryParse(ns, NumberStyles.Float, Inv, out var nv) ? nv : null;
                foreach (var name in which)
                {
                    if (!pipes.ContainsKey(name)) { msgs.Add(CalcMessage.Warning("W-CC-001", $"[Pipe]: {net.Name} has no pipe \"{name}\".")); continue; }
                    var o = spec.TryGetValue(name, out var e) ? e : default;
                    // A new material without its own n resets n to that material's default.
                    spec[name] = (mat ?? o.Mat, size ?? o.Size, G(cCl) ?? o.Class, nIn ?? (mat is not null ? null : o.N));
                }
            }
            foreach (var pipe in net.Pipes)
            {
                if (!spec.TryGetValue(pipe.Name, out var sp)) continue;
                string name = pipe.Name;
                var (mat, size, cls, nIn) = sp;
                double? n = nIn ?? (mat is not null && MaterialN.TryGetValue(mat, out var dn) ? dn : null);
                if (mat is not null && allowed.Count > 0)
                    Add(name, "Material", string.Join(", ", allowed), mat, allowed.Contains(mat), "not an allowed material in the specifications", "project specifications");
                if (size is { } s)
                    Add(name, $"Size ({su})", Math.Round(pipe.Rise / inch, 1), s, s >= pipe.Rise / inch - 1e-6, "smaller than the design size", "design");
                if (n is { } nn && Math.Abs(nn - pipe.ManningN) > 1e-9)
                    found.Add(new(name, "Manning's n", pipe.ManningN, nn, nn > pipe.ManningN ? "CHECKED" : "OK", nn > pipe.ManningN ? "rougher than design: hydraulics re-run below" : "smoother than design", "HDS-5 Table B.1"));
                if (cls is not null) found.Add(new(name, "Class", "", cls, "NOTE", "confirm the class for the fill height over the pipe (ASTM C76 / manufacturer's tables)", "ASTM C76"));
                pipes[name] = pipe with { ManningN = n ?? pipe.ManningN, Rise = size is { } s2 ? s2 * inch : pipe.Rise, Span = size is { } s3 && pipe.Shape == NetworkPipeShape.Circular ? s3 * inch : pipe.Span };
            }
            proposed = net with { Pipes = [.. net.Pipes.Select(x => pipes[x.Name])] };
        }

        // ---- RFI changes
        var rfis = new List<(string Rfi, string Change, string Reason, Func<StormNetwork, StormNetwork?> Apply)>();
        {
            var h = p.Header("Changes").Select(x => x.Trim().ToLowerInvariant()).ToList();
            int cR = h.FindIndex(x => x.StartsWith("rfi", StringComparison.Ordinal)), cC = h.FindIndex(x => x.StartsWith("change", StringComparison.Ordinal)), cWhy = h.FindIndex(x => x.StartsWith("reason", StringComparison.Ordinal));
            foreach (var r in p.Rows("Changes"))
            {
                string G(int k) => k >= 0 && k < r.Length ? r[k].Trim() : "";
                var m = RegexChange().Match(G(cC));
                if (!m.Success || !double.TryParse(m.Groups[3].Value, NumberStyles.Float, Inv, out var v))
                { msgs.Add(CalcMessage.Error("E-CC-002", $"[Changes] \"{G(cC)}\": write \"<pipe> upstream invert = 101.5\", \"<pipe> downstream invert = …\", \"<pipe> size = 18\" or \"<structure> rim = …\".")); continue; }
                string target = m.Groups[1].Value.Trim(), what = m.Groups[2].Value.ToLowerInvariant();
                Func<StormNetwork, StormNetwork?> apply = what switch
                {
                    "upstream invert" => n => n.Pipes.Any(x => x.Name == target) ? n with { Pipes = [.. n.Pipes.Select(x => x.Name == target ? x with { UpstreamInvert = v } : x)] } : null,
                    "downstream invert" => n => n.Pipes.Any(x => x.Name == target) ? n with { Pipes = [.. n.Pipes.Select(x => x.Name == target ? x with { DownstreamInvert = v } : x)] } : null,
                    "size" => n => n.Pipes.Any(x => x.Name == target) ? n with { Pipes = [.. n.Pipes.Select(x => x.Name == target ? x with { Rise = v * inch, Span = x.Shape == NetworkPipeShape.Circular ? v * inch : x.Span } : x)] } : null,
                    _ => n => n.Structures.Any(x => x.Name == target) ? n with { Structures = [.. n.Structures.Select(x => x.Name == target ? x with { RimElevation = v } : x)] } : null,
                };
                rfis.Add((G(cR).Length > 0 ? G(cR) : "RFI", G(cC), G(cWhy), apply));
            }
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        // ---- hydraulics: design vs proposed (+ each RFI, + all RFIs)
        var tables = new List<ToolTable>();
        var scenarioRows = new List<IReadOnlyList<object?>>();
        string? hydraulicVerdict = null;
        if (net is not null && proposed is not null && (p.Rows("Pipe").Count > 0 || rfis.Count > 0))
        {
            var baseRes = context.Networks.FirstOrDefault(n => n.Network == net).Result ?? StormNetworkSolver.Solve(net);
            var baseReview = Review(net, baseRes, context);
            var cases = new List<(string Name, StormNetwork Net)>();
            if (p.Rows("Pipe").Count > 0) cases.Add(("Submitted pipe", proposed));
            foreach (var g in rfis.GroupBy(x => x.Rfi))
            {
                var n = g.Aggregate((StormNetwork?)net, (acc, c) => acc is null ? null : c.Apply(acc));
                if (n is null) { msgs.Add(CalcMessage.Warning("W-CC-002", $"{g.Key}: a pipe or structure named in the change is not in {net.Name}.")); continue; }
                cases.Add(($"{g.Key} ({string.Join("; ", g.Select(x => x.Change))})", n));
            }
            if (cases.Count > 1)
                cases.Add(("Everything together", rfis.Aggregate((StormNetwork?)proposed, (acc, c) => acc is null ? null : c.Apply(acc)) ?? proposed));
            var need = net.Criteria.HglBelowRim;
            bool anyNewFail = false;
            foreach (var (name, n) in cases)
            {
                var res = StormNetworkSolver.Solve(n);
                if (!res.Succeeded) { scenarioRows.Add([name, "does not solve", null, null, null, null, "FAILS"]); anyNewFail = true; continue; }
                var worst = res.Structures.Where(s => !s.IsOutfall && s.HglClearance is not null).MinBy(s => s.HglClearance!.Value);
                var worstD = baseRes.Structures.FirstOrDefault(s => s.Structure.Name == worst?.Structure.Name);
                double maxRise = res.Structures.Select(s => s.Hgl - (baseRes.Structures.FirstOrDefault(b => b.Structure.Name == s.Structure.Name)?.Hgl ?? s.Hgl)).DefaultIfEmpty(0).Max();
                int overCap = res.Pipes.Count(x => x.Pipe.Discharge > x.FullFlowCapacity && !(baseRes.Pipes.FirstOrDefault(b => b.Pipe.Name == x.Pipe.Name) is { } b0 && b0.Pipe.Discharge > b0.FullFlowCapacity));
                var review = Review(n, res, context);
                var newFails = review.Where(k => !baseReview.Contains(k)).ToList();
                bool hglFail = need is { } nd && worst?.HglClearance < nd && !(worstD?.HglClearance < nd);
                bool bad = hglFail || overCap > 0 || newFails.Count > 0;
                anyNewFail |= bad;
                scenarioRows.Add([name, worst is null ? "" : $"{worst.Structure.Name}: {worst.HglClearance:0.00} {lu} (design {worstD?.HglClearance:0.00})", Math.Round(maxRise, 2), overCap, newFails.Count, newFails.Count > 0 ? string.Join("; ", newFails.Take(4)) : "", bad ? "FAILS" : "OK"]);
                foreach (var k in newFails) found.Add(new(name.Split(' ')[0], "Plan review", "", "", "FAILS", k, "IDCM §4.6"));
                if (hglFail) found.Add(new(name.Split(' ')[0], "HGL clearance", need, Math.Round(worst!.HglClearance!.Value, 2), "FAILS", $"HGL within {need:0.0#} {lu} of the rim at {worst.Structure.Name}", "IDCM §4.6.7"));
                if (overCap > 0) found.Add(new(name.Split(' ')[0], "Capacity", "", overCap, "FAILS", $"{overCap} pipe(s) now carry more than their full-flow capacity", "IDCM §4.6"));
                foreach (var (rfi, change, why, _) in rfis.Where(x => name.StartsWith(x.Rfi + " ", StringComparison.Ordinal)))
                    if (!bad) found.Add(new(rfi, "Field change", "", change, "OK", $"checked: HGL rises at most {maxRise:0.00} {lu}; criteria still met" + (why.Length > 0 ? $" (reason given: {why})" : ""), "IDCM §4.6"));
            }
            tables.Add(new("Hydraulic check of the proposed changes", ["Case", "Least rim − HGL", $"Largest HGL rise ({lu})", "Pipes newly over capacity", "New plan review failures", "Which", "Result"], scenarioRows,
                ["Each case is the design network with the submittal or RFI applied, solved with the design flows and tailwater and reviewed against IDCM §4.6. Only changes from the design's own results are counted."]));
            hydraulicVerdict = anyNewFail ? "The proposed changes do not meet the design criteria (see the hydraulic check)." : "With the proposed changes the storm sewer still meets the design criteria.";
        }

        // ---- property comparisons
        {
            var h = p.Header("Compare").Select(x => x.Trim().ToLowerInvariant()).ToList();
            int C(string n) => h.FindIndex(x => x.StartsWith(n, StringComparison.Ordinal));
            int cI = C("item"), cPr = C("property"), cRq = C("required"), cPp = C("proposed"), cRu = C("rule"), cRef = C("reference");
            foreach (var r in p.Rows("Compare"))
            {
                string G(int k) => k >= 0 && k < r.Length ? r[k].Trim() : "";
                string req = G(cRq), prop = G(cPp), rule = G(cRu).ToLowerInvariant();
                if (rule.Length == 0) rule = double.TryParse(req, NumberStyles.Float, Inv, out _) ? ">=" : "=";
                bool ok; string note;
                if (rule is ">=" or "<=" or ">" or "<")
                {
                    if (!double.TryParse(req, NumberStyles.Float, Inv, out var a) || !double.TryParse(prop, NumberStyles.Float, Inv, out var b)) { msgs.Add(CalcMessage.Warning("W-CC-003", $"[Compare] {G(cI)} {G(cPr)}: {rule} needs numbers.")); continue; }
                    ok = rule switch { ">=" => b >= a, "<=" => b <= a, ">" => b > a, _ => b < a };
                    note = ok ? "" : $"proposed {prop} is {(rule.StartsWith('>') ? "less" : "more")} than the required {req}";
                }
                else if (rule.StartsWith("one", StringComparison.Ordinal))
                {
                    var choices = req.Split('/').Select(x => x.Trim()).ToList();
                    ok = choices.Contains(prop, StringComparer.OrdinalIgnoreCase);
                    note = ok ? "" : $"{prop} is not one of {string.Join(", ", choices)}";
                }
                else { ok = string.Equals(req, prop, StringComparison.OrdinalIgnoreCase); note = ok ? "" : $"required {req}, proposed {prop}"; }
                found.Add(new(G(cI), G(cPr), req, prop, ok ? "OK" : "FAILS", note, G(cRef)));
            }
        }
        if (found.Count == 0 && scenarioRows.Count == 0) return ToolResult.Failed(title, CalcMessage.Error("E-CC-003", "Nothing to check: list the submittal under [Pipe], [Changes] or [Compare]."));

        // ---- disposition and letter
        int fails = found.Count(f => f.Result == "FAILS");
        bool materialFail = found.Any(f => f.Check == "Material" && f.Result == "FAILS");
        string disposition = materialFail ? "REJECTED" : fails > 0 ? "REVISE AND RESUBMIT" : found.Any(f => f.Result is "NOTE" or "CHECKED") ? "MAKE CORRECTIONS NOTED" : "NO EXCEPTIONS TAKEN";
        if (disposition == "MAKE CORRECTIONS NOTED" && !found.Any(f => f.Result == "NOTE")) disposition = "NO EXCEPTIONS TAKEN";
        var rows = found.Select((f, i) => (IReadOnlyList<object?>)[i + 1, f.Item, f.Check, f.Design, f.Proposed, f.Result, f.Note, f.Reference]).ToList();
        tables.Insert(0, new ToolTable("Submittal review", ["No.", "Item", "Check", "Design / required", "Proposed", "Result", "Note", "Reference"], rows));

        string sub = p.Text("submittal") ?? "Submittal", con = p.Text("contractor") ?? "the contractor", date = p.Text("date") ?? DateTime.Today.ToString("yyyy-MM-dd", Inv), reviewer = p.Text("reviewer") ?? context.Project?.PreparedBy ?? "";
        var body = new List<ReportBlock>
        {
            new ReportHeading(1, $"Response: {sub}"),
            new ReportParagraph($"Disposition: {disposition}."),
            new ReportParagraph($"We reviewed {con}'s submittal dated {date} against the design for {context.Project?.Name ?? "the project"}" + (net is not null ? $" (storm network {net.Name}, criteria: {(string.IsNullOrWhiteSpace(net.Criteria.Source) ? "as designed" : net.Criteria.Source)})" : "") + "."),
        };
        if (hydraulicVerdict is not null) body.Add(new ReportParagraph(hydraulicVerdict));
        var items = found.Where(f => f.Result is "FAILS" or "NOTE").ToList();
        if (items.Count > 0)
        {
            body.Add(new ReportHeading(2, "Comments"));
            int k = 1;
            foreach (var f in items)
                body.Add(new ReportParagraph($"{k++}. {f.Item}, {f.Check.ToLowerInvariant()}: {(f.Result == "NOTE" ? "" : "not acceptable — ")}{f.Note}." + (f.Reference.Length > 0 ? $" ({f.Reference})" : ""), true));
        }
        var oks = found.Where(f => f.Result is "OK" or "CHECKED").ToList();
        if (oks.Count > 0)
        {
            body.Add(new ReportHeading(2, "Accepted"));
            foreach (var f in oks) body.Add(new ReportParagraph($"{f.Item}, {f.Check.ToLowerInvariant()}: {f.Proposed}" + (f.Note.Length > 0 ? $" ({f.Note})" : "") + ".", true));
        }
        body.Add(new ReportParagraph("Review is for general conformance with the design concept and the contract documents only; the contractor remains responsible for dimensions, quantities, coordination and compliance with the contract documents."));
        if (scenarioRows.Count > 0) body.Add(new ReportTable(tables.First(t => t.Title.StartsWith("Hydraulic", StringComparison.Ordinal))));
        var letter = new ReportDocument($"Submittal response — {sub}", [("Project", context.Project?.Name ?? ""), ("Contractor", con), ("Submittal date", date), ("Disposition", disposition), ("Reviewed by", reviewer)], body, [new ReportTable(tables[0])])
        { TableOfContents = false };

        var summary = new List<string>
        {
            $"Disposition: {disposition}. {found.Count} item(s) checked: {fails} fail, {found.Count(f => f.Result == "NOTE")} note(s), {found.Count(f => f.Result is "OK" or "CHECKED")} acceptable.",
        };
        if (hydraulicVerdict is not null) summary.Add(hydraulicVerdict);
        summary.Add("Save… writes the response letter as Word or PDF.");
        return new ToolResult(title, summary, tables, msgs) { Report = letter };

        void Add(string item, string check, object? design, object? prop, bool ok, string bad, string reference) =>
            found.Add(new(item, check, design, prop, ok ? "OK" : "FAILS", ok ? "" : bad, reference));
    }

    /// <summary>The plan review's FAIL rows as "check: item" keys, so the design's own findings can be set aside.</summary>
    private static HashSet<string> Review(StormNetwork n, StormNetworkResult res, DesignToolContext context)
    {
        var set = new HashSet<string>();
        if (!res.Succeeded) return set;
        var tool = new PlanReviewTool();
        string input = context.ToolInputs.TryGetValue(tool.Id, out var saved) && !string.IsNullOrWhiteSpace(saved) ? saved : tool.SampleInput(context);
        var r = tool.Run(input, context with { Network = n, NetworkResult = res });
        foreach (var t in r.Tables)
            foreach (var row in t.Rows.Where(x => x.Count > 3 && x[0] is "FAIL"))
                set.Add($"{row[1]}: {DesignTools.Cell(row[3])}");
        return set;
    }

    [GeneratedRegex(@"^(.+?)\s+(upstream invert|downstream invert|size|rim)\s*=\s*([-+]?\d+(?:\.\d+)?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RegexChange();
}
