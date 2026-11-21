using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Cost estimate and bid tabulation (premium). Quantities come from the storm network takeoff (the same rules as the
/// free quantity takeoff) plus pavement removal and replacement over the trench and dewatering below a groundwater
/// elevation; they are priced in any number of columns (the engineer's estimate first, then bidders) side by side, with
/// escalation of the estimate by a cost index, mobilization, contingency and engineering on the estimate, bid totals,
/// ranks and the difference from the estimate, an unbalanced-bid review and the cost by category.
/// </summary>
public sealed class CostEstimateTool : IDesignTool
{
    public string Id => "cost-estimate";
    public string Name => "Cost estimate and bid tabulation (storm network)";
    public string Description => "Engineer's estimate and bid tab for the selected network: takeoff quantities plus pavement replacement and dewatering, priced in any number of columns (estimate, bidders), with cost-index escalation, mobilization, contingency and engineering, bid ranking against the estimate, an unbalanced-bid review and cost by category.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context)
    {
        bool si = (context.Network?.Units ?? context.Units) == UnitSystem.SI;
        string pipes = context.Network is { } n
            ? string.Join("\n", QuantityTool.Takeoff(n, new QuantityTool.TakeoffOptions(si ? 0.6 : 2, si ? 0.15 : 0.5, si ? 0.3 : 1, si ? 1.8 : 6, null)).Quantities.Keys
                .Where(k => k.StartsWith("Pipe", StringComparison.OrdinalIgnoreCase)).Select((k, i) => $"{i + 1}\t{k}\t{(si ? "m" : "LF")}\t\t{Price(k, si)}\t{Price(k, si) * 0.95:0}\t{Price(k, si) * 1.12:0}"))
            : si ? "1\tPipe 450 mm RCP\tm\t\t250\t240\t280" : "1\tPipe 18 in RCP\tLF\t\t80\t76\t90";
        int k0 = pipes.Split('\n').Length;
        string N(int i) => (k0 + i).ToString();
        return si ? $"""
            # Cost estimate and bid tab, SI. Quantities: blank = from the network takeoff; a number = as entered.
            side clearance = 0.6
            bedding depth = 0.15
            structure base depth = 0.3
            base structure depth = 1.8
            pavement = all              # all, none, or pipe names separated by commas
            pavement overcut = 0.3      # m beyond the trench on each side
            groundwater elevation =     # optional: pipes with an invert below it get dewatering
            mobilization = 5            # % of the estimate subtotal
            contingency = 15            # % (estimate only)
            engineering = 10            # % (estimate only)
            unbalanced threshold = 50   # % from the average of all columns

            # EXAMPLE PRICES ONLY. The first price column is the estimate; the others are bids.
            [Bid items]
            No	Item	Unit	Quantity	Estimate	Bidder A	Bidder B
            {pipes}
            {N(1)}	Manhole	each		5200	5000	6100
            {N(2)}	Inlet	each		4200	3900	4600
            {N(3)}	Outlet	each		3000	2800	3300
            {N(4)}	Manhole extra depth	m		900	850	950
            {N(5)}	Excavation	m3		30	26	34
            {N(6)}	Bedding	m3		55	50	60
            {N(7)}	Backfill	m3		25	22	27
            {N(8)}	Pavement removal and replacement	m2		95	88	99
            {N(9)}	Traffic control	LS	1	10000	8000	15000
            """ : $"""
            # Cost estimate and bid tab, U.S. units. Quantities: blank = from the network takeoff; a number = as entered.
            side clearance = 2.0
            bedding depth = 0.5
            structure base depth = 1.0
            base structure depth = 6
            pavement = all              # all, none, or pipe names separated by commas
            pavement overcut = 1.0      # ft beyond the trench on each side
            groundwater elevation =     # optional: pipes with an invert below it get dewatering
            mobilization = 5            # % of the estimate subtotal
            contingency = 15            # % (estimate only)
            engineering = 10            # % (estimate only)
            price index from =          # optional cost-index escalation of the estimate, e.g. ENR CCI then and now
            price index to =
            unbalanced threshold = 50   # % from the average of all columns

            # EXAMPLE PRICES ONLY. The first price column is the estimate; the others are bids.
            [Bid items]
            No	Item	Unit	Quantity	Estimate	Bidder A	Bidder B
            {pipes}
            {N(1)}	Manhole	each		4500	4300	5200
            {N(2)}	Inlet	each		3800	3500	4100
            {N(3)}	Outlet	each		2500	2400	2900
            {N(4)}	Manhole extra depth	VF		350	320	400
            {N(5)}	Excavation	CY		22	18	25
            {N(6)}	Bedding	CY		45	42	48
            {N(7)}	Backfill	CY		18	15	21
            {N(8)}	Pavement removal and replacement	SY		85	80	88
            {N(9)}	Traffic control	LS	1	8000	6500	12000
            """;
    }

    /// <summary>Example price for a pipe item: rises with size (planning-level only).</summary>
    private static double Price(string item, bool si)
    {
        var parts = item.Split(' ');
        double size = parts.Length > 1 && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (si ? 450 : 18);
        double inches = si ? size / 25.4 : size;
        double usd = Math.Round(20 + 3.2 * inches);
        return si ? Math.Round(usd * 3.28) : usd;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Cost estimate and bid tabulation";
        if (context.Network is not { } net || net.Pipes.Count == 0)
            return ToolResult.Failed(title, CalcMessage.Error("E-CE-001", "Select a storm network with pipes first."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        bool us = net.Units == UnitSystem.USCustomary;
        string L = us ? "ft" : "m", areaU = us ? "SY" : "m2", lenU = us ? "LF" : "m";
        double clear = p.Number("side clearance", us ? 2.0 : 0.6), bed = p.Number("bedding depth", us ? 0.5 : 0.15);
        double baseDepth = p.Number("structure base depth", us ? 1.0 : 0.3), baseStruct = p.Number("base structure depth", us ? 6 : 1.8);
        string wallText = p.Text("wall") ?? "auto";
        double? wall = wallText.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : p.Number("wall", double.NaN);
        double overcut = p.Number("pavement overcut", us ? 1.0 : 0.3), mob = p.Number("mobilization", 0), cont = p.Number("contingency", 0), eng = p.Number("engineering", 0);
        double? gw = p.OptionalNumber("groundwater elevation"), idxFrom = p.OptionalNumber("price index from"), idxTo = p.OptionalNumber("price index to");
        double threshold = p.Number("unbalanced threshold", 50);
        void NonNegative(string what, double v) { if (!(v >= 0)) msgs.Add(CalcMessage.Error("E-CE-002", $"{what} must be zero or more (got {v:G6}).")); }
        NonNegative("Side clearance", clear); NonNegative("Bedding depth", bed); NonNegative("Structure base depth", baseDepth); NonNegative("Base structure depth", baseStruct);
        NonNegative("Pavement overcut", overcut); NonNegative("Mobilization", mob); NonNegative("Contingency", cont); NonNegative("Engineering", eng);
        if (wall is { } w0) NonNegative("Wall", w0);
        if (!(threshold > 0)) msgs.Add(CalcMessage.Error("E-CE-002", $"Unbalanced threshold must be more than zero (got {threshold:G6})."));
        if ((idxFrom is null) != (idxTo is null)) msgs.Add(CalcMessage.Error("E-CE-003", "Give both price index from and price index to, or neither."));
        else if (idxFrom is { } f0 && idxTo is { } t0 && !(f0 > 0 && t0 > 0)) msgs.Add(CalcMessage.Error("E-CE-003", "Price indexes must be more than zero."));
        double esc = idxFrom is { } fi && idxTo is { } ti && fi > 0 ? ti / fi : 1;

        // Bid items: header names find No, Item, Unit, Quantity; every other column is a set of prices.
        var head = p.Header("Bid items");
        int Find(string w) => head.ToList().FindIndex(h => h.Trim().StartsWith(w, StringComparison.OrdinalIgnoreCase));
        int cNo = Find("no"), cItem = Find("item"), cUnit = Find("unit"), cQty = Find("quant");
        if (cItem < 0 || cUnit < 0) msgs.Add(CalcMessage.Error("E-CE-004", "[Bid items] needs a header with Item and Unit columns (and optionally No and Quantity), then one column per estimate or bidder."));
        var priceCols = Enumerable.Range(0, head.Count).Where(i => i != cNo && i != cItem && i != cUnit && i != cQty).ToList();
        if (cItem >= 0 && priceCols.Count == 0) msgs.Add(CalcMessage.Error("E-CE-004", "[Bid items] has no price column; add one column for the estimate (and one per bidder)."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);
        string[] names = priceCols.Select(i => head[i]).ToArray();

        // ---- quantities
        var take = QuantityTool.Takeoff(net, new QuantityTool.TakeoffOptions(clear, bed, baseDepth, baseStruct, wall));
        msgs.AddRange(take.Messages);
        var auto = new Dictionary<string, (string Unit, double Qty)>(take.Quantities, StringComparer.OrdinalIgnoreCase);
        string pav = (p.Text("pavement") ?? "all").Trim();
        var pavSet = pav.Equals("all", StringComparison.OrdinalIgnoreCase) ? net.Pipes.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : pav.Equals("none", StringComparison.OrdinalIgnoreCase) ? [] : pav.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var n in pavSet.Where(n => !net.Pipes.Any(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
            msgs.Add(CalcMessage.Warning("W-CE-001", $"Pavement: pipe \"{n}\" is not in the network; ignored."));
        double areaK = us ? 9 : 1, pavT = 0, dewT = 0;
        var pipeRows = new List<IReadOnlyList<object?>>();
        foreach (var t in take.Pipes)
        {
            double pavA = pavSet.Contains(t.Pipe.Name) ? (t.Width + 2 * overcut) * t.Pipe.Length / areaK : 0;
            bool dew = gw is { } g && Math.Min(t.Pipe.UpstreamInvert, t.Pipe.DownstreamInvert) - t.Wall - bed < g;
            pavT += pavA; if (dew) dewT += t.Pipe.Length;
            pipeRows.Add([t.Pipe.Name, $"{t.Pipe.From} → {t.Pipe.To}", t.Item, t.Pipe.Length, Math.Round(t.Width, 3), t.DepthUp is { } d1 ? Math.Round(d1, 2) : null, t.DepthDown is { } d2 ? Math.Round(d2, 2) : null,
                t.Excavation is { } ex ? Math.Round(ex, 2) : null, pavA > 0 ? Math.Round(pavA, 2) : null, dew ? "yes" : ""]);
        }
        if (pavT > 0) auto["Pavement removal and replacement"] = (areaU, pavT);
        if (dewT > 0) auto["Dewatering"] = (lenU, dewT);

        // ---- bid items
        var items = new List<(string No, string Item, string Unit, double Qty, string Source, double?[] Prices)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Bid items"))
        {
            string item = cItem < r.Length ? r[cItem].Trim() : "";
            if (item.Length == 0) continue;
            string unit = cUnit < r.Length ? r[cUnit].Trim() : "";
            string no = cNo >= 0 && cNo < r.Length ? r[cNo] : (items.Count + 1).ToString();
            double? qIn = cQty >= 0 ? p.Cell(r, cQty, "quantity", item) : null;
            double qty; string source;
            var match = auto.FirstOrDefault(k => QuantityTool.Norm(k.Key) == QuantityTool.Norm(item));
            if (qIn is { } q) { qty = q; source = "entered"; if (q < 0) msgs.Add(CalcMessage.Error("E-CE-005", $"{item}: quantity cannot be negative.")); }
            else if (match.Key is not null)
            {
                used.Add(match.Key);
                if (QuantityTool.ConvertPrice(1, match.Value.Unit, unit) is { } k)   // 1 per takeoff unit = k per bid unit → qty in bid unit = qty / k
                {
                    qty = match.Value.Qty / k; source = "takeoff";
                    if (!match.Value.Unit.Equals(unit, StringComparison.OrdinalIgnoreCase)) msgs.Add(CalcMessage.Info("I-CE-002", $"{item}: takeoff {match.Value.Qty:G6} {match.Value.Unit} converted to {qty:G6} {unit}."));
                }
                else { qty = 0; source = "unit?"; msgs.Add(CalcMessage.Error("E-CE-006", $"{item}: the bid unit {unit} does not convert from the takeoff unit {match.Value.Unit}.")); }
            }
            else { qty = 0; source = "none"; msgs.Add(CalcMessage.Warning("W-CE-002", $"{item}: no quantity entered and none from the takeoff; quantity 0.")); }
            qty = Math.Round(qty, unit.Equals("each", StringComparison.OrdinalIgnoreCase) || unit.Equals("EA", StringComparison.OrdinalIgnoreCase) || unit.Equals("LS", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
            var prices = priceCols.Select((c, j) =>
            {
                double? v = p.Cell(r, c, "price", $"{item}, {names[j]}");
                if (v is < 0) { msgs.Add(CalcMessage.Error("E-CE-005", $"{item}, {names[j]}: price cannot be negative.")); return null; }
                return j == 0 && v is { } e ? e * esc : v;
            }).ToArray();
            items.Add((no, item, unit, qty, source, prices));
        }
        msgs.InsertRange(0, p.Errors.Where(e => !msgs.Contains(e)));
        if (items.Count == 0) msgs.Add(CalcMessage.Error("E-CE-004", "List the bid items under [Bid items]."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);
        foreach (var k in auto.Keys.Where(k => !used.Contains(k) && !items.Any(i => QuantityTool.Norm(i.Item) == QuantityTool.Norm(k))))
            msgs.Add(CalcMessage.Warning("W-CE-003", $"Takeoff item \"{k}\" ({auto[k].Qty:G6} {auto[k].Unit}) is not in the bid items; add a line for it or the estimate leaves it out."));

        // ---- tabulation
        int nc = names.Length;
        var sub = new double[nc]; var missingPrice = new int[nc];
        var tabRows = new List<IReadOnlyList<object?>>();
        foreach (var it in items)
        {
            var row = new List<object?> { it.No, it.Item, it.Unit, it.Qty, it.Source };
            for (int j = 0; j < nc; j++)
            {
                if (it.Prices[j] is { } pr) { double amt = Math.Round(it.Qty * pr, 2); sub[j] += amt; row.Add(Math.Round(pr, 2)); row.Add(amt); }
                else { missingPrice[j]++; row.Add(null); row.Add(null); }
            }
            tabRows.Add(row);
        }
        var totals = new double[nc];
        var totRows = new List<IReadOnlyList<object?>>();
        double estMob = Math.Round(sub[0] * mob / 100, 2), estBase = sub[0] + estMob, estCont = Math.Round(estBase * cont / 100, 2), estEng = Math.Round((estBase + estCont) * eng / 100, 2);
        for (int j = 0; j < nc; j++) totals[j] = j == 0 ? estBase + estCont + estEng : sub[j];
        totRows.Add(Row("Bid items subtotal", sub.Select(x => (object?)Math.Round(x, 2))));
        totRows.Add(Row($"Mobilization ({mob:G4} %)", Enumerable.Range(0, nc).Select(j => j == 0 ? (object?)estMob : null)));
        totRows.Add(Row($"Contingency ({cont:G4} %)", Enumerable.Range(0, nc).Select(j => j == 0 ? (object?)estCont : null)));
        totRows.Add(Row($"Engineering ({eng:G4} %)", Enumerable.Range(0, nc).Select(j => j == 0 ? (object?)estEng : null)));
        totRows.Add(Row("Total", totals.Select(x => (object?)Math.Round(x, 2))));
        // Bids are compared with the estimate's construction cost (subtotal + mobilization), not with its contingency or engineering.
        totRows.Add(Row("Difference from the estimate's construction cost (%)", Enumerable.Range(0, nc).Select(j => j == 0 || estBase == 0 ? null : (object?)Math.Round(100 * (sub[j] - estBase) / estBase, 1))));
        var bidOrder = Enumerable.Range(1, nc - 1).Where(j => missingPrice[j] == 0).OrderBy(j => sub[j]).ToList();
        totRows.Add(Row("Rank", Enumerable.Range(0, nc).Select(j => j == 0 ? null : bidOrder.Contains(j) ? (object?)(bidOrder.IndexOf(j) + 1) : "incomplete")));
        for (int j = 0; j < nc; j++)
            if (missingPrice[j] > 0) msgs.Add(CalcMessage.Warning("W-CE-004", $"{names[j]}: {missingPrice[j]} item(s) unpriced; {(j == 0 ? "the estimate leaves them out" : "the bid is incomplete and not ranked")}."));

        // ---- unbalanced bid review: a bidder's unit price far from the average of every priced column for that item.
        var unbal = new List<IReadOnlyList<object?>>();
        if (nc > 1)
            foreach (var it in items)
            {
                var all = it.Prices.Where(x => x is not null).Select(x => x!.Value).ToList();
                if (all.Count < 2) continue;
                double avg = all.Average();
                if (!(avg > 0)) continue;
                for (int j = 1; j < nc; j++)
                    if (it.Prices[j] is { } pr && Math.Abs(pr - avg) / avg * 100 > threshold)
                        unbal.Add([names[j], it.No, it.Item, Math.Round(pr, 2), Math.Round(avg, 2), Math.Round(100 * (pr - avg) / avg, 1), Math.Round(it.Qty * (pr - avg), 2), pr > avg ? "HIGH" : "LOW"]);
            }

        // ---- cost by category (estimate column)
        string Cat(string item)
        {
            string k = item.ToLowerInvariant();
            if (k.StartsWith("pipe")) return "Pipe";
            if (k.StartsWith("manhole") || k.StartsWith("inlet") || k.StartsWith("outlet")) return "Structures";
            if (k is "excavation" or "bedding" or "backfill" || k.Contains("dewater")) return "Earthwork and dewatering";
            if (k.Contains("pavement") || k.Contains("curb") || k.Contains("sidewalk") || k.Contains("seed") || k.Contains("restor")) return "Surface restoration";
            return "Other";
        }
        var cats = items.GroupBy(i => Cat(i.Item)).Select(g => (Cat: g.Key, Amount: g.Sum(i => i.Prices[0] is { } pr ? Math.Round(i.Qty * pr, 2) : 0))).OrderByDescending(x => x.Amount).ToList();
        var catRows = cats.Select(c => (IReadOnlyList<object?>)[c.Cat, Math.Round(c.Amount, 2), sub[0] > 0 ? Math.Round(100 * c.Amount / sub[0], 1) : null]).ToList();
        double pipeLen = net.Pipes.Sum(x => x.Length);

        if (esc != 1) msgs.Add(CalcMessage.Info("I-CE-003", $"{names[0]} prices escalated by {idxTo:G6} / {idxFrom:G6} = {esc:F4}."));
        if (unbal.Count > 0) msgs.Add(CalcMessage.Warning("W-CE-005", $"{unbal.Count} unit price(s) differ from the item average by more than {threshold:G3} %; review for unbalanced bids (front-loaded or quantity-sensitive items)."));
        msgs.Add(CalcMessage.Info("I-CE-001", $"Quantities from the network takeoff (vertical-wall trench, ground straight between rims, wall " + (wall is null ? "D/12 + 1 in." : $"{wall} {L}") + $"); pavement over the trench width plus {overcut:G3} {L} each side" + (gw is { } gg ? $"; dewatering where the trench bottom is below {gg:G6}" : "") + ". Prices are the ones you entered; the sample prices are examples only."));

        var headers = new List<string> { "No", "Item", "Unit", "Quantity", "Quantity from" };
        foreach (var nm in names) { headers.Add($"{nm} unit price"); headers.Add($"{nm} amount"); }
        var summary = new List<string>
        {
            $"{names[0]}: {totals[0]:N0} (construction {estBase:N0} + contingency {estCont:N0} + engineering {estEng:N0}); {estBase / Math.Max(pipeLen, 1e-9):N0} per {lenU} of pipe.",
        };
        if (bidOrder.Count > 0)
        {
            int low = bidOrder[0];
            summary.Add($"Low bid: {names[low]} {sub[low]:N0}, {100 * (sub[low] - estBase) / Math.Max(estBase, 1e-9):+0.0;-0.0} % from the estimate's construction cost." + (bidOrder.Count > 1 ? $" Next: {names[bidOrder[1]]} {sub[bidOrder[1]]:N0}." : ""));
        }
        if (nc > 1) summary.Add(unbal.Count == 0 ? $"No unit price is more than {threshold:G3} % from its item average." : $"{unbal.Count} unit price(s) flagged for unbalanced-bid review.");

        var tables = new List<ToolTable>
        {
            new("Bid tabulation", headers, tabRows, ["Quantity from: takeoff = computed from the network; entered = typed in the bid items."]),
            new("Totals", ["", .. names], totRows, [$"Mobilization, contingency and engineering apply to {names[0]} only; bids are their bid item totals."]),
        };
        if (nc > 1) tables.Add(new("Unbalanced bid review", ["Bidder", "No", "Item", "Unit price", "Item average", "Difference (%)", "Effect (qty × difference)", "Flag"], unbal,
            [$"Average of every priced column for the item; flagged beyond ±{threshold:G3} %."]));
        tables.Add(new($"Cost by category ({names[0]})", ["Category", "Amount", "Share of bid items (%)"], catRows));
        tables.Add(new("Pipe, trench, pavement and dewatering", ["Pipe", "From → to", "Item", $"Length ({L})", $"Trench width ({L})", $"Depth up ({L})", $"Depth down ({L})", $"Excavation ({(us ? "CY" : "m3")})", $"Pavement ({areaU})", "Dewatering"], pipeRows));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static IReadOnlyList<object?> Row(string label, IEnumerable<object?> cells) => [label, .. cells];
}
