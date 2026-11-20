using MasterFlow.Application.Optimization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Network optimizer (premium): least-cost or least-excavation pipe sizes and inverts for the selected storm network,
/// meeting cover, depth, capacity, velocity, critical-slope, size-progression, crown-matching and drop rules, then
/// the HGL clearance (IDCM §4.6.7) by lowering or upsizing the lines that fail. See <see cref="NetworkOptimizer"/>.
/// </summary>
public sealed class NetworkOptimizerTool : IDesignTool
{
    public string Id => "network-optimizer";
    public string Name => "Network optimizer (least-cost sizes and inverts)";
    public string Description => "Chooses pipe sizes and inverts for the selected network at least cost (pipe, trench excavation and structure depth) or least excavation, meeting cover, depth, capacity, velocity, critical slope, never larger into smaller, crown matching and drop rules (Rapid City IDCM §4.6), then the HGL clearance. Exact optimum on the invert grid.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context) => Sample(context) + OutfallRows(context);

    /// <summary>Example limit rows for the network's outfalls: the existing outfall invert and up to 3 ft (1 m) lower.</summary>
    private static string OutfallRows(DesignToolContext context)
    {
        if (context.Network is not { } net) return "";
        bool us = net.Units == UnitSystem.USCustomary;
        var rows = net.Structures.Where(s => !net.Pipes.Any(p => p.From == s.Name) && net.Pipes.Any(p => p.To == s.Name))
            .Select(s => (s.Name, Inv: net.Pipes.Where(p => p.To == s.Name).Min(p => p.DownstreamInvert))).ToList();
        if (rows.Count == 0) return "";
        return $"\n# EXAMPLE: the outfall invert defaults to the existing one; these rows let it go up to {(us ? "3 ft" : "1 m")} lower. Set them from the receiving channel.\n"
            + string.Join("\n", rows.Select(r => FormattableString.Invariant($"{r.Name}\t{r.Inv - (us ? 3 : 1):0.###}\t")));
    }

    private static string Sample(DesignToolContext context) => (context.Network?.Units ?? context.Units) == UnitSystem.SI ? """
        # Network optimizer, SI (m, m³/s). Limits default to the network's criteria where it has them.
        objective = cost            # cost (pipe + excavation + structure depth) or depth (least excavation)
        grid step = 0.01            # m; every invert is a multiple of it
        minimum cover = 0.6         # m, rim to outside top of pipe (not checked at an outfall)
        maximum depth = 6           # m, rim to invert
        minimum drop = 0.03         # m across a structure
        subcritical = yes           # slope not steeper than critical (IDCM 4.6.6 item 4)
        hgl check = yes             # lower or upsize lines whose HGL is too close to the rim

        # Trench and structures (as the quantity takeoff): width = OD + side clearance.
        side clearance = 0.6
        bedding depth = 0.15
        excavation price = 30       # per m³ (excavation and backfill together)
        structure base depth = 0.3
        base structure depth = 1.8
        structure depth price = 900 # per m deeper than the base structure depth

        # EXAMPLE PRICES ONLY: price per m installed, by size (mm). Only these sizes are used.
        [Pipe prices]
        Size	Price
        300	140
        375	150
        450	180
        525	220
        600	260
        750	330
        900	420

        # Optional: limits on every invert at a structure (e.g. a utility crossing, or the outfall channel).
        [Invert limits]
        Structure	Minimum invert	Maximum invert
        """ : """
        # Network optimizer, U.S. units (ft, cfs). Limits default to the network's criteria where it has them.
        objective = cost            # cost (pipe + excavation + structure depth) or depth (least excavation)
        grid step = 0.05            # ft; every invert is a multiple of it
        minimum cover = 2.0         # ft, rim to outside top of pipe (not checked at an outfall)
        maximum depth = 20          # ft, rim to invert
        minimum drop = 0.1          # ft across a structure
        subcritical = yes           # slope not steeper than critical (IDCM 4.6.6 item 4)
        hgl check = yes             # lower or upsize lines whose HGL is too close to the rim

        # Trench and structures (as the quantity takeoff): width = OD + side clearance.
        side clearance = 2.0
        bedding depth = 0.5
        excavation price = 22       # per CY (excavation and backfill together)
        structure base depth = 1.0
        base structure depth = 6
        structure depth price = 350 # per VF deeper than the base structure depth

        # EXAMPLE PRICES ONLY: price per LF installed, by size (in.). Only these sizes are used.
        [Pipe prices]
        Size	Price
        12	45
        15	55
        18	65
        21	75
        24	95
        30	125
        36	160
        42	200
        48	250

        # Optional: limits on every invert at a structure (e.g. a utility crossing, or the outfall channel).
        [Invert limits]
        Structure	Minimum invert	Maximum invert
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Network optimizer: least-cost sizes and inverts";
        if (context.Network is not { } net || net.Pipes.Count == 0)
            return ToolResult.Failed(title, CalcMessage.Error("E-NO-001", "Select a storm network with pipes first."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        bool us = net.Units == UnitSystem.USCustomary;
        string L = us ? "ft" : "m", Qu = us ? "cfs" : "m³/s", Vu = us ? "ft/s" : "m/s", sizeU = us ? "in." : "mm";
        double toSize = us ? 12 : 1000;
        var k = net.Criteria;

        // ---- network checks
        if (net.Pipes.FirstOrDefault(x => x.Shape != NetworkPipeShape.Circular) is { } ell)
            msgs.Add(CalcMessage.Error("E-NO-002", $"Pipe {ell.Name} is elliptical. The optimizer sizes circular pipes only; make it circular or leave it out of the network."));
        foreach (var s in net.Structures.Where(s => s.RimElevation is null))
            msgs.Add(CalcMessage.Error("E-NO-003", $"Structure {s.Name} has no rim elevation; the optimizer needs every rim for cover and excavation."));
        foreach (var g in net.Pipes.GroupBy(x => x.From).Where(g => g.Count() > 1))
            msgs.Add(CalcMessage.Error("E-NO-004", $"Structure {g.Key} has {g.Count()} outlet pipes ({string.Join(", ", g.Select(x => x.Name))}); the optimizer needs a branching (dendritic) network with one outlet per structure."));

        // ---- parameters
        string objective = (p.Text("objective") ?? "cost").ToLowerInvariant();
        bool leastCost = !objective.StartsWith("depth") && !objective.StartsWith("exc");
        if (objective is not ("cost" or "depth" or "excavation" or "least cost" or "least depth" or "least excavation"))
            msgs.Add(CalcMessage.Error("E-NO-005", $"Objective \"{objective}\" is not cost or depth."));
        double step = p.Number("grid step", us ? 0.05 : 0.01), cover = p.Number("minimum cover", us ? 2.0 : 0.6), maxDepth = p.Number("maximum depth", us ? 20 : 6);
        double drop = p.Number("minimum drop", us ? 0.1 : 0.03), clear = p.Number("side clearance", us ? 2.0 : 0.6), bed = p.Number("bedding depth", us ? 0.5 : 0.15);
        double exPrice = p.Number("excavation price", us ? 22 : 30), stBase = p.Number("structure base depth", us ? 1.0 : 0.3), baseStruct = p.Number("base structure depth", us ? 6 : 1.8);
        double stDepthPrice = p.Number("structure depth price", us ? 350 : 900), fill = p.Number("maximum fill", 1.0);
        double minSlope = p.Number("minimum slope", 0) / 100;
        double? maxSlope = p.OptionalNumber("maximum slope") is { } ms ? ms / 100 : null;
        double minSize = p.OptionalNumber("minimum size") is { } mz ? mz / toSize : k.MinPipeRise ?? 0;
        double? vMin = p.OptionalNumber("minimum velocity") ?? k.MinVelocity, vMax = p.OptionalNumber("maximum velocity") ?? k.MaxVelocity;
        bool sub = p.Flag("subcritical", k.FlagSupercritical), hglCheck = p.Flag("hgl check", true);
        string? wallText = p.Text("wall");
        double? wall = wallText is null || wallText.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : p.Number("wall", double.NaN);
        void Positive(string what, double v, bool allowZero = false)
        {
            if (!double.IsFinite(v) || v < 0 || (!allowZero && v == 0)) msgs.Add(CalcMessage.Error("E-NO-005", $"{what} must be {(allowZero ? "zero or more" : "more than zero")} (got {v:G6})."));
        }
        Positive("Grid step", step); Positive("Minimum cover", cover, true); Positive("Maximum depth", maxDepth); Positive("Minimum drop", drop, true);
        Positive("Side clearance", clear, true); Positive("Bedding depth", bed, true); Positive("Excavation price", exPrice, true); Positive("Structure base depth", stBase, true);
        Positive("Base structure depth", baseStruct, true); Positive("Structure depth price", stDepthPrice, true); Positive("Minimum slope", minSlope, true); Positive("Minimum size", minSize, true);
        if (wall is { } w0) Positive("Wall", w0, true);
        if (maxSlope is { } mxs) Positive("Maximum slope", mxs);
        if (!(fill > 0 && fill <= 1)) msgs.Add(CalcMessage.Error("E-NO-005", $"Maximum fill must be more than 0 and at most 1 (got {fill:G6})."));
        if (vMin is { } a0 && vMax is { } b0 && a0 > b0) msgs.Add(CalcMessage.Error("E-NO-005", $"Minimum velocity {a0:G4} is above the maximum {b0:G4}."));
        if (maxDepth <= cover && cover >= 0 && maxDepth > 0) msgs.Add(CalcMessage.Error("E-NO-005", $"Maximum depth {maxDepth:G4} {L} leaves no room below the minimum cover {cover:G4} {L}."));
        if (step > 0 && double.IsFinite(step) && maxDepth / step > 5000) msgs.Add(CalcMessage.Error("E-NO-007", $"A grid step of {step:G4} {L} gives {maxDepth / step:N0} invert levels per structure; use a step of at least {maxDepth / 5000:G2} {L}."));

        var sizes = new List<(double Size, double Price)>();
        foreach (var r in p.Rows("Pipe prices"))
        {
            if (p.Cell(r, 0, "size", "Pipe prices") is not { } sz) continue;
            double pr = p.Cell(r, 1, "price", $"Pipe prices {r[0]}") ?? double.NaN;
            if (!(sz > 0)) { msgs.Add(CalcMessage.Error("E-NO-005", $"Pipe size {sz:G6} must be more than zero.")); continue; }
            if (leastCost && !(pr >= 0)) { msgs.Add(CalcMessage.Error("E-NO-005", $"Pipe {sz:G6} {sizeU}: the price must be zero or more.")); continue; }
            sizes.Add((sz / toSize, leastCost ? pr : 0));
        }
        if (sizes.Count == 0)
        {
            if (leastCost) msgs.Add(CalcMessage.Error("E-NO-006", "Least cost needs a [Pipe prices] table (size and price per length). Use objective = depth to minimize excavation with the standard sizes."));
            else sizes = NetworkSizer.StandardSizes(net.Units).Select(x => (x / toSize, 0.0)).ToList();
        }
        var limits = new Dictionary<string, (double?, double?)>(StringComparer.Ordinal);
        var names = net.Structures.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var r in p.Rows("Invert limits"))
        {
            if (r.Length == 0 || r[0].Length == 0) continue;
            string sn = net.Structures.FirstOrDefault(s => s.Name.Equals(r[0], StringComparison.OrdinalIgnoreCase))?.Name ?? r[0];
            if (!names.Contains(sn)) { msgs.Add(CalcMessage.Warning("W-NO-001", $"Invert limits: structure \"{r[0]}\" is not in the network; ignored.")); continue; }
            double? lo = p.Cell(r, 1, "minimum invert", sn), hi = p.Cell(r, 2, "maximum invert", sn);
            if (lo is { } l1 && hi is { } h1 && l1 > h1) msgs.Add(CalcMessage.Error("E-NO-005", $"Invert limits at {sn}: minimum {l1:G6} is above maximum {h1:G6}."));
            limits[sn] = (lo, hi);
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        var solved0 = context.NetworkResult is { Succeeded: true } nr && ReferenceEquals(nr.Network, net) ? nr : StormNetworkSolver.Solve(net);
        if (!solved0.Succeeded)
            return ToolResult.Failed(title, [CalcMessage.Error("E-NO-008", "The network does not solve as entered (flows are taken from the solution); fix its errors first."), .. solved0.Messages.Where(m => m.Severity == MessageSeverity.Error)]);

        var opts = new OptimizerOptions
        {
            Sizes = sizes, GridStep = step, MinCover = cover, MaxDepth = maxDepth, MinDrop = drop, Wall = wall, SideClearance = clear, Bedding = bed,
            ExcavationPrice = exPrice, StructureBaseDepth = stBase, BaseStructureDepth = baseStruct, StructureDepthPrice = leastCost ? stDepthPrice : 0,
            MinSize = minSize, MaxVelocity = vMax, MinVelocity = vMin, Subcritical = sub, MaxFill = fill, MinSlope = minSlope, MaxSlope = maxSlope, InvertLimits = limits,
        };
        if (!leastCost) opts = opts with { ExcavationPrice = 1 };
        if (net.Pipes.Any(x => !(solved0.Pipe(x.Name)?.MinorDischarge > 0)) && vMin is not null)
            msgs.Add(CalcMessage.Info("I-NO-002", "Pipes without a minor-storm flow use the design flow for the minimum velocity; enter the 2-year flows for the IDCM 4.6.2(3a) check."));

        // ---- optimize, then the HGL loop
        var opt = new NetworkOptimizer(net, solved0, opts);
        var res = opt.Optimize();
        if (!res.Feasible)
            return ToolResult.Failed(title, [.. msgs, .. res.Notes.Select(n => CalcMessage.Error("E-NO-010", n))]);
        var extra = new Dictionary<string, double>(StringComparer.Ordinal);
        var minPipe = new Dictionary<string, double>(StringComparer.Ordinal);
        var stuck = new HashSet<string>(StringComparer.Ordinal);
        var hglActions = new List<string>();
        StormNetworkResult solved = StormNetworkSolver.Solve(res.Network);
        double? need = k.HglBelowRim;
        double CostOf(OptimizerResult o) => o.Cost?.Total ?? double.PositiveInfinity;
        // HGL loop: take the structure with the largest shortfall; try one size up on each of the pipes on its path to the
        // outfall with the largest energy loss, and lowering its outlet by the shortfall; keep the change that gains the
        // most clearance there per unit of added cost. A structure no change helps is left and reported.
        for (int it = 0; hglCheck && need is { } nd && it < 30 && solved.Succeeded; it++)
        {
            var worst = solved.Structures.Where(x => !x.IsOutfall && x.HglClearance is { } cl && cl < nd - 1e-6 && !stuck.Contains(x.Structure.Name))
                .OrderBy(x => x.HglClearance).FirstOrDefault();
            if (worst is null) break;
            string s = worst.Structure.Name;
            double deficit = nd - worst.HglClearance!.Value;
            var cands = new List<(string What, Dictionary<string, double> Extra, Dictionary<string, double> Min)>();
            foreach (var pp in solved.PathToOutfall(s).OrderByDescending(x => x.EnergyLoss).Take(6))
                if (sizes.Select(x => x.Size).Where(x => x > pp.Pipe.Rise + 1e-9).DefaultIfEmpty(double.NaN).Min() is var up && double.IsFinite(up))
                    cands.Add(($"upsized {pp.Pipe.Name} to {up * toSize:0.##} {sizeU}", extra, new Dictionary<string, double>(minPipe) { [pp.Pipe.Name] = up }));
            double lower = Math.Ceiling(deficit / step) * step;
            cands.Add(($"lowered the outlet of {s} by {lower:G4} {L}", new Dictionary<string, double>(extra) { [s] = extra.GetValueOrDefault(s) + lower }, minPipe));
            (string What, Dictionary<string, double> Extra, Dictionary<string, double> Min, OptimizerResult Res, StormNetworkResult Sol, double Score)? best = null;
            foreach (var (what, ex, mp) in cands)
            {
                var tr = new NetworkOptimizer(net, solved0, opts with { ExtraCover = ex, MinPipeSize = mp }).Optimize();
                if (!tr.Feasible) continue;
                var ts = StormNetworkSolver.Solve(tr.Network);
                if (!ts.Succeeded || ts.Structure(s)?.HglClearance is not { } c1) continue;
                double gain = c1 - worst.HglClearance!.Value;
                if (gain < 0.005) continue;
                double score = gain / Math.Max(CostOf(tr) - CostOf(res), 1e-6 * Math.Abs(CostOf(res)) + 1e-9);
                if (best is null || score > best.Value.Score) best = (what, ex, mp, tr, ts, score);
            }
            if (best is not { } b) { stuck.Add(s); continue; }
            extra = b.Extra; minPipe = b.Min; res = b.Res; solved = b.Sol;
            hglActions.Add($"{b.What} (HGL at {s} was {deficit:F2} {L} too high)");
        }
        var finalOpts = opts with { ExtraCover = extra, MinPipeSize = minPipe };
        var finalOpt = new NetworkOptimizer(net, solved0, finalOpts);
        var cost = finalOpt.Evaluate(res.Network.Pipes);
        var existing = new NetworkOptimizer(net, solved0, opts).Evaluate(net.Pipes);   // same prices and rules, without the HGL loop's additions
        foreach (var n in res.Notes) msgs.Add(CalcMessage.Warning("W-NO-009", n));

        // ---- tables
        var oldPipes = net.Pipes.ToDictionary(x => x.Name);
        var pipeRows = new List<IReadOnlyList<object?>>();
        var hydRows = new List<IReadOnlyList<object?>>();
        foreach (var x in res.Network.Pipes)
        {
            var o = oldPipes[x.Name];
            var h = solved.Pipe(x.Name)!;
            double s = (x.UpstreamInvert - x.DownstreamInvert) / x.Length, wl = finalOpt.WallOf(x.Rise);
            var win = finalOpt.Window(x, x.Rise);
            double rimU = net.Structures.First(z => z.Name == x.From).RimElevation!.Value, rimD = net.Structures.First(z => z.Name == x.To).RimElevation!.Value;
            pipeRows.Add([x.Name, $"{x.From} → {x.To}", Math.Round(o.Rise * toSize, 1), Math.Round(x.Rise * toSize, 1), o.UpstreamInvert, x.UpstreamInvert, o.DownstreamInvert, x.DownstreamInvert,
                Math.Round(100 * s, 3), Math.Round(rimU - (x.UpstreamInvert + x.Rise + wl), 2), finalOpt.IsOutfall(x.To) ? null : Math.Round(rimD - (x.DownstreamInvert + x.Rise + wl), 2)]);
            hydRows.Add([x.Name, h.Pipe.Discharge, h.FullFlowCapacity, h.FullFlowCapacity > 0 ? Math.Round(h.Pipe.Discharge / h.FullFlowCapacity, 3) : null,
                h.NormalVelocity, double.IsNaN(h.MinorVelocity) ? null : h.MinorVelocity, Math.Round(100 * win.Min, 3), win.Max >= 1 ? null : Math.Round(100 * win.Max, 3),
                $"{win.MinReason} / {win.MaxReason}", h.Condition]);
        }
        var structRows = new List<IReadOnlyList<object?>>();
        int hglFail = 0;
        foreach (var s in net.Structures)
        {
            var outO = net.Pipes.FirstOrDefault(x => x.From == s.Name);
            var outN = res.Network.Pipes.FirstOrDefault(x => x.From == s.Name);
            var ins = res.Network.Pipes.Where(x => x.To == s.Name).ToList();
            var hs = solved.Structure(s.Name);
            double? low = outN?.UpstreamInvert ?? (ins.Count > 0 ? ins.Min(x => x.DownstreamInvert) : null);
            double? dropIn = outN is not null && ins.Count > 0 ? ins.Max(x => x.DownstreamInvert) - outN.UpstreamInvert : null;
            bool ok = hs is null || hs.IsOutfall || need is not { } nd || hs.HglClearance is not { } cl || cl >= nd - 1e-6;
            if (!ok) hglFail++;
            structRows.Add([s.Name, finalOpt.IsOutfall(s.Name) ? "Outfall" : s.Kind, s.RimElevation, outO?.UpstreamInvert ?? (net.Pipes.Where(x => x.To == s.Name).Select(x => (double?)x.DownstreamInvert).Min()), low,
                low is { } lw ? Math.Round(s.RimElevation!.Value - lw, 2) : null, dropIn is { } dd ? Math.Round(dd, 3) : null,
                hs?.Hgl, hs?.HglClearance is { } c0 ? Math.Round(c0, 2) : null, hs is null || hs.IsOutfall || need is null ? "—" : ok ? "OK" : "FAILS"]);
        }
        string money = leastCost ? "" : $" ({(us ? "CY" : "m³")})";
        var costRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "Pipe (price × length)", Math.Round(existing.PipeMaterial, 2), Math.Round(cost.PipeMaterial, 2), Math.Round(cost.PipeMaterial - existing.PipeMaterial, 2) },
            new object?[] { "Trench excavation" + money, Math.Round(existing.Excavation, 2), Math.Round(cost.Excavation, 2), Math.Round(cost.Excavation - existing.Excavation, 2) },
            new object?[] { "Structure extra depth", Math.Round(existing.StructureDepth, 2), Math.Round(cost.StructureDepth, 2), Math.Round(cost.StructureDepth - existing.StructureDepth, 2) },
            new object?[] { "Total", Math.Round(existing.Total, 2), Math.Round(cost.Total, 2), Math.Round(cost.Total - existing.Total, 2) },
            new object?[] { "Rules broken", existing.Violations.Count, cost.Violations.Count, null },
        };
        if (existing.Violations.Count > 0)
            msgs.Add(CalcMessage.Info("I-NO-003", $"The existing design breaks {existing.Violations.Count} of these rules: " + string.Join("; ", existing.Violations.Take(8)) + (existing.Violations.Count > 8 ? $"; and {existing.Violations.Count - 8} more." : ".")));
        foreach (var a in hglActions) msgs.Add(CalcMessage.Info("I-NO-004", "HGL check: " + a + "."));
        if (hglFail > 0) msgs.Add(CalcMessage.Warning("W-NO-002", $"The HGL is closer than {need:G3} {L} to the rim at {hglFail} structure(s) after the HGL loop. The tailwater or the outfall invert likely controls: lower the outfall, raise the rims or add larger sizes."));
        var fullLeft = k.AllowSurcharge ? [] : solved.Pipes.Where(x => x.FlowsFull).Select(x => x.Pipe.Name).ToList();
        if (fullLeft.Count > 0) msgs.Add(CalcMessage.Warning("W-NO-003", $"Flowing full under backwater (pressure flow is flagged by the network criteria): {string.Join(", ", fullLeft)}. Set maximum fill below 1 (e.g. 0.85) to leave room for junction losses, or lower the tailwater."));
        msgs.Add(CalcMessage.Info("I-NO-001", $"Exact optimum on a {step:G4} {L} invert grid over sizes ({sizeU}) {string.Join(", ", sizes.Select(x => (x.Size * toSize).ToString("0.##")))}. Cover {cover:G4} {L} to the outside top (wall " + (wall is null ? (us ? "D/12 + 1 in." : "D/12 + 25 mm") : $"{wall} {L}") + $"), maximum depth {maxDepth:G4} {L}, drop {drop:G4} {L}, fill {fill:G3}" +
            (vMin is { } a1 ? $", V ≥ {a1:G3} {Vu} (minor storm)" : "") + (vMax is { } b1 ? $", V ≤ {b1:G3} {Vu}" : "") + (sub ? ", slope below critical" : "") +
            ". Ground is the straight line between rims. Flows are the network's design flows, unchanged. Prices are the ones you entered; the sample prices are examples only."));

        // ---- chart: profile of the longest path
        var charts = new List<ToolChart>();
        var start = net.Structures.Where(s => !net.Pipes.Any(x => x.To == s.Name) && net.Pipes.Any(x => x.From == s.Name))
            .OrderByDescending(s => solved.PathToOutfall(s.Name).Sum(x => x.Pipe.Length)).FirstOrDefault();
        if (start is not null)
        {
            var path = solved.PathToOutfall(start.Name);
            var ground = new List<(double, double)>(); var oldInv = new List<(double, double)>(); var newInv = new List<(double, double)>(); var crown = new List<(double, double)>(); var hgl = new List<(double, double)>();
            double st = 0;
            foreach (var h in path)
            {
                var o = oldPipes[h.Pipe.Name];
                double rimU = net.Structures.First(z => z.Name == h.Pipe.From).RimElevation!.Value, rimD = net.Structures.First(z => z.Name == h.Pipe.To).RimElevation!.Value;
                ground.Add((st, rimU)); oldInv.Add((st, o.UpstreamInvert)); newInv.Add((st, h.Pipe.UpstreamInvert)); crown.Add((st, h.Pipe.UpstreamInvert + h.Pipe.Rise)); hgl.Add((st, h.HglUp));
                st += h.Pipe.Length;
                ground.Add((st, rimD)); oldInv.Add((st, o.DownstreamInvert)); newInv.Add((st, h.Pipe.DownstreamInvert)); crown.Add((st, h.Pipe.DownstreamInvert + h.Pipe.Rise)); hgl.Add((st, h.HglDown));
            }
            charts.Add(new ToolChart($"Profile {start.Name} to {path[^1].Pipe.To}: existing and optimized", $"Distance ({L})", $"Elevation ({L})",
            [
                new("Rim / ground", ground), new("Existing invert", oldInv, Dashed: true), new("Optimized invert", newInv), new("Optimized crown", crown), new("HGL (optimized)", hgl, Dashed: true),
            ]));
        }

        int changed = res.Network.Pipes.Count(x => Math.Abs(x.Rise - oldPipes[x.Name].Rise) > 1e-9 || Math.Abs(x.UpstreamInvert - oldPipes[x.Name].UpstreamInvert) > 1e-6 || Math.Abs(x.DownstreamInvert - oldPipes[x.Name].DownstreamInvert) > 1e-6);
        string unit = leastCost ? "" : (us ? " CY" : " m³");
        var summary = new List<string>
        {
            (leastCost ? "Least cost" : "Least excavation") + $": {cost.Total:N0}{unit} against {existing.Total:N0}{unit} for the existing design priced the same way ({(existing.Total > 0 ? 100 * (cost.Total - existing.Total) / existing.Total : 0):+0.0;-0.0} %). {changed} of {net.Pipes.Count} pipes change.",
            cost.Feasible ? "Every pipe meets the cover, depth, capacity, velocity" + (sub ? ", critical slope" : "") + ", size progression, crown matching and drop rules." : "Some rules are broken; see the messages.",
            need is null ? "No HGL clearance criterion is set on the network." : hglFail == 0 ? $"The HGL is at least {need:G3} {L} below every rim." : $"The HGL is too close to the rim at {hglFail} structure(s).",
        };
        if (existing.Violations.Count > 0) summary.Add($"The existing design breaks {existing.Violations.Count} of the same rules.");

        return new ToolResult(title, summary,
        [
            new("Pipe design", ["Pipe", "From → to", $"Size was ({sizeU})", $"Size ({sizeU})", $"Up invert was ({L})", $"Up invert ({L})", $"Down invert was ({L})", $"Down invert ({L})", "Slope (%)",
                $"Cover up ({L})", $"Cover down ({L})"], pipeRows, ["Cover is rim to the outside top of the pipe; not checked at an outfall."]),
            new("Pipe hydraulics (optimized)", ["Pipe", $"Q ({Qu})", $"Qfull ({Qu})", "Q/Qfull", $"V ({Vu})", $"V minor ({Vu})", "Min slope (%)", "Max slope (%)", "Set by (min / max)", "Flow"], hydRows,
                ["V and the flow condition are from the HGL solution of the optimized network. The slope window is for the chosen size; a blank maximum means no limit."]),
            new("Structures", ["Structure", "Kind", $"Rim ({L})", $"Lowest invert was ({L})", $"Lowest invert ({L})", $"Depth ({L})", $"Highest inflow above outlet ({L})", $"HGL ({L})", $"Rim − HGL ({L})", "HGL check"], structRows),
            new("Cost comparison" + (leastCost ? "" : " (excavation volume)"), ["Item", "Existing", "Optimized", "Change"], costRows,
                [leastCost ? "Both designs priced with the same pipe prices, trench excavation and structure depth prices. Base structure prices are the same for both and left out." : "Objective is trench excavation volume only."]),
        ], msgs)
        {
            Charts = charts,
            UpdatedNetwork = res.Network,
            UpdateDescription = $"Set the sizes and inverts of {changed} pipe(s) from the optimizer.",
        };
    }
}
