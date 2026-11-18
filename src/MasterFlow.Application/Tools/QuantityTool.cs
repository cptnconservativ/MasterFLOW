using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Quantity takeoff and opinion of probable cost for the selected storm network: pipe length by size and material
/// (PVC to 18 in., RCP above, per IDCM §4.6.2(2) unless the n says otherwise), structures by kind with depth,
/// trench excavation, bedding and backfill from a vertical-wall trench, priced from the user's unit price table.
/// Ground is the straight line between rims; quantities are planning-level and every assumption is listed.
/// </summary>
public sealed class QuantityTool : IDesignTool
{
    public string Id => "quantity-takeoff";
    public string Name => "Quantity takeoff and cost opinion (storm network)";
    public string Description => "Pipe by size and material, structures by kind and depth, trench excavation, bedding and backfill for the selected network, priced from your unit prices with a contingency. Planning-level: ground is a straight line between rims.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context) => (context.Network?.Units ?? context.Units) == UnitSystem.SI ? """
        # Quantity takeoff, SI (m, m³). Trench width = outside diameter + side clearance (both sides together).
        side clearance = 0.6
        bedding depth = 0.15        # below the pipe
        wall = auto                 # pipe wall thickness, m; auto = D/12 + 25 mm (RCP Wall B approximation)
        structure base depth = 0.3  # below the lowest invert, for structure depth
        contingency = 15            # percent

        # EXAMPLE PRICES ONLY: replace with your own bid tabulations. Items: "Pipe <size> mm <material>" (m),
        # Manhole / Inlet / Outlet (each, up to the base depth), "<kind> extra depth" (per m over base depth), Excavation, Bedding, Backfill (m³).
        base structure depth = 1.8
        [Unit prices]
        Item	Unit	Price
        Pipe 450 mm PVC	m	180
        Pipe 600 mm RCP	m	260
        Pipe 375 mm PVC	m	150
        Pipe 375 mm RCP	m	230
        Pipe 450 mm RCP	m	250
        Manhole	each	5200
        Inlet	each	4200
        Outlet	each	3000
        Manhole extra depth	m	900
        Excavation	m3	30
        Bedding	m3	55
        Backfill	m3	25
        """ : """
        # Quantity takeoff, U.S. units (ft, CY). Trench width = outside diameter + side clearance (both sides together).
        side clearance = 2.0
        bedding depth = 0.5         # ft below the pipe
        wall = auto                 # pipe wall thickness, ft; auto = D/12 + 1 in. (RCP Wall B approximation)
        structure base depth = 1.0  # ft below the lowest invert, for structure depth
        contingency = 15            # percent

        # EXAMPLE PRICES ONLY: replace with your own bid tabulations. Items: "Pipe <size> in <material>" (LF),
        # Manhole / Inlet / Outlet (each, up to the base structure depth), "<kind> extra depth" (VF over it),
        # Excavation, Bedding, Backfill (CY).
        base structure depth = 6
        [Unit prices]
        Item	Unit	Price
        Pipe 15 in PVC	LF	55
        Pipe 18 in PVC	LF	65
        Pipe 15 in RCP	LF	70
        Pipe 18 in RCP	LF	80
        Pipe 24 in RCP	LF	95
        Manhole	each	4500
        Inlet	each	3800
        Outlet	each	2500
        Manhole extra depth	VF	350
        Inlet extra depth	VF	300
        Excavation	CY	22
        Bedding	CY	45
        Backfill	CY	18
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Storm network quantities and opinion of probable cost";
        if (context.Network is not { } net || net.Pipes.Count == 0)
            return ToolResult.Failed(title, CalcMessage.Error("E-QT-001", "Select a storm network with pipes first."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        bool us = net.Units == UnitSystem.USCustomary;
        string L = us ? "ft" : "m", vol = us ? "CY" : "m3", len = us ? "LF" : "m", sizeU = us ? "in" : "mm", depthU = us ? "VF" : "m";
        double volK = us ? 27 : 1, toSize = us ? 12 : 1000;
        double clear = p.Number("side clearance", us ? 2.0 : 0.6), bed = p.Number("bedding depth", us ? 0.5 : 0.15);
        double baseDepth = p.Number("structure base depth", us ? 1.0 : 0.3), baseStruct = p.Number("base structure depth", us ? 6 : 1.8), cont = p.Number("contingency", 0);
        string wallText = p.Text("wall") ?? "auto";
        double? wallFixed = wallText.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : p.Number("wall", double.NaN);
        var prices = new Dictionary<string, (string Unit, double Price)>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Unit prices"))
            if (r.Length >= 3 && p.Cell(r, 2, "price", $"Unit price {r[0]}") is { } pr) prices[Norm(r[0])] = (r[1], pr);
        var rims = net.Structures.ToDictionary(s => s.Name, s => s.RimElevation, StringComparer.OrdinalIgnoreCase);

        var pipeRows = new List<IReadOnlyList<object?>>();
        var qty = new Dictionary<string, (string Unit, double Qty)>(StringComparer.OrdinalIgnoreCase);
        void Q(string item, string unit, double v) { var k = qty.GetValueOrDefault(item); qty[item] = (unit, k.Qty + v); }
        double exT = 0, bedT = 0, bfT = 0;
        foreach (var x in net.Pipes)
        {
            if (rims.GetValueOrDefault(x.From) is not { } r1 || rims.GetValueOrDefault(x.To) is not { } r2)
            { msgs.Add(CalcMessage.Warning("W-QT-001", $"Pipe {x.Name}: a rim is missing at {x.From} or {x.To}; trench quantities skipped for it (pipe length still counted).")); }
            double size = Math.Round(x.Rise * toSize);   // nearest inch or millimetre
            // PVC only where IDCM allows it (18 in. / 450 mm and smaller) and the n says plastic; otherwise RCP.
            string material = size <= (us ? 18 : 450) && x.ManningN < 0.013 ? "PVC" : "RCP";
            string pipeItem = x.Shape == NetworkPipeShape.Elliptical ? $"Pipe {size} x {Math.Round(x.Span * toSize)} {sizeU} RCP elliptical" : $"Pipe {size} {sizeU} {material}";
            Q(pipeItem, len, x.Length);
            double wall = wallFixed ?? x.Rise / 12 + (us ? 1.0 / 12 : 0.025);
            double od = x.Span + 2 * wall, odH = x.Rise + 2 * wall, width = od + clear;
            if (rims.GetValueOrDefault(x.From) is { } ru && rims.GetValueOrDefault(x.To) is { } rd)
            {
                // Depth from the ground (straight between rims) to the trench bottom (invert − wall − bedding); average of the ends.
                double d1 = ru - (x.UpstreamInvert - wall - bed), d2 = rd - (x.DownstreamInvert - wall - bed), dAvg = 0.5 * (d1 + d2);
                double ex = width * dAvg * x.Length / volK, bd = width * bed * x.Length / volK;
                double pipeVol = Math.PI * od * odH / 4 * x.Length / volK;
                double bf = Math.Max(0, ex - bd - pipeVol);
                exT += ex; bedT += bd; bfT += bf;
                pipeRows.Add([x.Name, $"{x.From} → {x.To}", pipeItem, x.Length, Math.Round(od, 3), Math.Round(width, 3), Math.Round(d1, 2), Math.Round(d2, 2), ex, bd, bf]);
            }
            else pipeRows.Add([x.Name, $"{x.From} → {x.To}", pipeItem, x.Length, Math.Round(od, 3), Math.Round(width, 3), null, null, null, null, null]);
        }
        Q("Excavation", vol, exT); Q("Bedding", vol, bedT); Q("Backfill", vol, bfT);

        var structRows = new List<IReadOnlyList<object?>>();
        foreach (var s in net.Structures)
        {
            string kind = Kind(s);
            var inv = net.Pipes.Where(x => x.From.Equals(s.Name, StringComparison.OrdinalIgnoreCase)).Select(x => x.UpstreamInvert)
                .Concat(net.Pipes.Where(x => x.To.Equals(s.Name, StringComparison.OrdinalIgnoreCase)).Select(x => x.DownstreamInvert)).ToList();
            double? depth = s.RimElevation is { } rim && inv.Count > 0 ? rim - inv.Min() + baseDepth : null;
            double extra = depth is { } dd && kind != "Outlet" ? Math.Max(0, dd - baseStruct) : 0;
            Q(kind, "each", 1);
            if (extra > 0) Q($"{kind} extra depth", depthU, extra);
            if (depth is null) msgs.Add(CalcMessage.Warning("W-QT-002", $"Structure {s.Name}: no rim or no connected pipe; depth unknown."));
            structRows.Add([s.Name, kind, s.RimElevation, inv.Count > 0 ? inv.Min() : null, depth is { } d0 ? Math.Round(d0, 2) : null, extra > 0 ? Math.Round(extra, 2) : null]);
        }

        var costRows = new List<IReadOnlyList<object?>>();
        double total = 0;
        var missing = new List<string>();
        foreach (var (item, (unit, q)) in qty.OrderBy(k => Order(k.Key)).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            double rq = Math.Round(q, unit == "each" ? 0 : 1);
            if (prices.TryGetValue(Norm(item), out var pr))
            {
                double? perUnit = ConvertPrice(pr.Price, pr.Unit, unit);
                if (perUnit is not { } up)
                {
                    msgs.Add(CalcMessage.Warning("W-QT-003", $"\"{item}\": the price is per {pr.Unit} but the quantity is in {unit}, and the units do not convert. The line is left unpriced and out of the subtotal."));
                    costRows.Add([item, unit, rq, null, null]); missing.Add(item);
                    continue;
                }
                if (!pr.Unit.Trim().Equals(unit, StringComparison.OrdinalIgnoreCase))
                    msgs.Add(CalcMessage.Info("I-QT-002", $"\"{item}\": price {pr.Price:G6} per {pr.Unit} converted to {up:G6} per {unit}."));
                double cost = rq * up;
                total += cost;
                costRows.Add([item, unit, rq, Math.Round(up, 4), Math.Round(cost, 2)]);
            }
            else { costRows.Add([item, unit, rq, null, null]); missing.Add(item); }
        }
        if (missing.Count > 0) msgs.Add(CalcMessage.Warning("W-QT-004", $"No unit price for: {string.Join(", ", missing)}. They are listed without cost and the total leaves them out."));
        double contAmt = Math.Round(total * cont / 100, 2);
        costRows.Add(["Subtotal", "", null, null, Math.Round(total, 2)]);
        costRows.Add([$"Contingency ({cont:G4} %)", "", null, null, contAmt]);
        costRows.Add(["Total opinion of probable cost", "", null, null, Math.Round(total + contAmt, 2)]);
        msgs.InsertRange(0, p.Errors);
        msgs.Add(CalcMessage.Info("I-QT-001", "Planning-level quantities: vertical-wall trench, ground straight between rims, pipe wall " + (wallFixed is null ? "D/12 + 1 in. (RCP Wall B approximation)" : $"{wallFixed} {L}") + ". Prices are the ones you entered; the sample prices are examples only."));
        return new ToolResult(title,
            [$"{net.Pipes.Count} pipes, {net.Pipes.Sum(x => x.Length):F0} {L}; {net.Structures.Count} structures. Subtotal {total:N0}, total with {cont:G4} % contingency {total + contAmt:N0}" + (missing.Count > 0 ? $" ({missing.Count} item(s) unpriced)." : ".")],
        [
            new("Opinion of probable cost", ["Item", "Unit", "Quantity", "Unit price", "Amount"], costRows),
            new("Pipe and trench schedule", ["Pipe", "From → to", "Item", $"Length ({L})", $"OD ({L})", $"Trench width ({L})", $"Depth up ({L})", $"Depth down ({L})", $"Excavation ({vol})", $"Bedding ({vol})", $"Backfill ({vol})"], pipeRows,
                [$"Trench width = OD + {clear:G4} {L}; depth = rim − (invert − wall − {bed:G4} {L} bedding) at each end, averaged. Backfill = excavation − bedding − pipe volume."]),
            new("Structure schedule", ["Structure", "Kind", $"Rim ({L})", $"Lowest invert ({L})", $"Depth ({L})", $"Extra depth ({depthU})"], structRows,
                [$"Depth = rim − lowest invert + {baseDepth:G4} {L} base; extra depth is over {baseStruct:G4} {L} (outlets not counted)."]),
        ], msgs);
    }

    private static string Kind(NetworkStructure s)
    {
        string k = s.Kind.ToLowerInvariant();
        return k.Contains("out") ? "Outlet" : k.Contains("inlet") || k.Contains("cb") || k.Contains("catch") || k.Contains("grate") || k.Contains("curb") ? "Inlet" : "Manhole";
    }

    // Unit sizes in a base unit of each kind: length in ft, volume in ft³, count in each.
    private static readonly Dictionary<string, (string Kind, double Size)> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LF"] = ("length", 1), ["ft"] = ("length", 1), ["VF"] = ("length", 1), ["m"] = ("length", 1 / 0.3048),
        ["CY"] = ("volume", 27), ["CF"] = ("volume", 1), ["ft3"] = ("volume", 1), ["m3"] = ("volume", 1 / Math.Pow(0.3048, 3)),
        ["each"] = ("count", 1), ["EA"] = ("count", 1),
    };

    /// <summary>Price per <paramref name="qtyUnit"/> from a price per <paramref name="priceUnit"/>; null when they are not the same kind of unit.</summary>
    public static double? ConvertPrice(double price, string priceUnit, string qtyUnit)
    {
        if (priceUnit.Trim().Equals(qtyUnit, StringComparison.OrdinalIgnoreCase)) return price;
        if (!Units.TryGetValue(priceUnit.Trim(), out var a) || !Units.TryGetValue(qtyUnit, out var b) || a.Kind != b.Kind) return null;
        return price * b.Size / a.Size;
    }

    private static int Order(string item) => item.StartsWith("Pipe", StringComparison.OrdinalIgnoreCase) ? 0 : item is "Excavation" or "Bedding" or "Backfill" ? 2 : 1;
    private static string Norm(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace("\"", " in").Replace("  ", " ").Trim().ToLowerInvariant();
}
