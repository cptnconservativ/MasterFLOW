using System.Globalization;
using MasterFlow.Application.Reports;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Climate stress test (premium, D-310). Reruns the storm network(s) with design flows scaled for future rainfall
/// (flow factor = (1 + rainfall change)^exponent; exponent 1 for the Rational method) and an optional tailwater rise,
/// reports each projection against the design, and scans the rainfall increase upward to find the order in which
/// structures and pipes fail (HGL criterion, EGL criterion, flooding, surcharge). The rainfall changes are the
/// user's inputs; no projection data are bundled.
/// </summary>
public sealed class ClimateStressTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Id => "climate-stress";
    public string Name => "Climate stress test (future rainfall: what fails first)";
    public string Description => "Reruns the storm network with design flows scaled for future rainfall projections (and a tailwater rise if the outfall is tidal or a river), compares each projection with the design, and finds the rainfall increase at which each structure and pipe first fails (HGL or EGL criterion, flooding, surcharge), so you can tell a client which pieces fail first and how much margin the design has.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context) => $"""
        # Climate stress test: the design flows are scaled by (1 + rainfall change)^exponent and the network is re-solved.
        # The rainfall changes below are EXAMPLES. Replace them with your projection source (for example a NOAA Atlas 15
        # value or a CMIP6 change factor for the design storm) and name it in the Source column.
        # Tailwater rise is added to a fixed tailwater elevation (outfall to a river, lake or tidal water).
        network = {context.Network?.Name ?? "all"}      # a network name, or all
        runoff exponent = 1.0      # 1 for the Rational method (Q = CiA); runoff from curve-number methods grows faster
        search up to = 100 %       # rainfall increase scanned to find the order of failure

        [Projections]
        Scenario	Rainfall change (%)	Tailwater rise (ft)	Source
        Design (today)	0	0	design storm
        Mid-century, moderate	10	0	example only
        Mid-century, high	15	0	example only
        End of century, high	30	0	example only
        """;

    private sealed record Failure(string Element, string Mode, int Rank, bool Hard = true);
    private sealed record Scan(string Net, string Element, string Mode, double? AtPct, int Rank, bool Hard);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Climate stress test";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        string netName = p.Text("network") ?? "";
        var all = context.Networks.Select(n => n.Network).ToList();
        if (all.Count == 0 && context.Network is not null) all.Add(context.Network);
        List<StormNetwork> nets = netName.Equals("all", StringComparison.OrdinalIgnoreCase) ? all
            : all.Where(n => netName.Length == 0 ? n.Name == context.Network?.Name : n.Name.Equals(netName, StringComparison.OrdinalIgnoreCase)).Take(1).ToList();
        if (nets.Count == 0 && netName.Length == 0 && context.Network is not null) nets.Add(context.Network);
        if (nets.Count == 0)
            return ToolResult.Failed(title, CalcMessage.Error("E-CS-001", netName.Length == 0 || netName.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? "There is no storm network to test. Add one in Storm networks, or select it." : $"No storm network is named \"{netName}\"."));

        double expo = p.Number("runoff exponent", 1.0);
        double maxPct = p.Number("search up to", 60);
        if (!(expo > 0)) { msgs.Add(CalcMessage.Error("E-CS-002", "runoff exponent must be more than zero.")); expo = 1; }
        if (!(maxPct > 0)) { msgs.Add(CalcMessage.Error("E-CS-003", "search up to must be a rainfall increase above 0 %.")); maxPct = 60; }
        maxPct = Math.Min(maxPct, 300);

        // ---- projections
        var proj = new List<(string Name, double Pct, double Tw, string Source)>();
        var h = p.Header("Projections").Select(x => x.Trim().ToLowerInvariant()).ToList();
        int C(params string[] names) => h.FindIndex(x => names.Any(n => x.StartsWith(n, StringComparison.Ordinal)));
        int cN = C("scenario", "name", "projection"), cR = C("rainfall", "change", "increase"), cT = C("tailwater"), cS = C("source");
        if (cN < 0) cN = 0;
        if (cR < 0) cR = 1;
        foreach (var r in p.Rows("Projections"))
        {
            string G(int k) => k >= 0 && k < r.Length ? r[k].Trim() : "";
            string name = G(cN);
            if (name.Length == 0) continue;
            if (!ToolInput.TryNumber(G(cR), out var pct)) { msgs.Add(CalcMessage.Error("E-CS-004", $"[Projections] {name}: rainfall change \"{G(cR)}\" is not a number (percent).")); continue; }
            if (pct <= -100) { msgs.Add(CalcMessage.Error("E-CS-004", $"[Projections] {name}: a rainfall change of {pct}% leaves no rain.")); continue; }
            double tw = G(cT).Length == 0 ? 0 : ToolInput.TryNumber(G(cT), out var t) ? t : double.NaN;
            if (double.IsNaN(tw)) { msgs.Add(CalcMessage.Error("E-CS-005", $"[Projections] {name}: tailwater rise \"{G(cT)}\" is not a number.")); continue; }
            proj.Add((name, pct, tw, G(cS)));
        }
        if (proj.Count == 0) proj.Add(("Design (today)", 0, 0, "design storm"));
        if (!msgs.Any(m => m.Severity == MessageSeverity.Error) && proj.All(x => x.Pct == 0 && x.Tw == 0))
            msgs.Add(CalcMessage.Warning("W-CS-001", "No projection changes the rainfall or the tailwater; only the failure-order scan tells you anything."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        double Factor(double pct) => Math.Pow(1 + pct / 100, expo);
        bool multi = nets.Count > 1;
        var scenRows = new List<IReadOnlyList<object?>>();
        var scans = new List<Scan>();
        var charts = new List<ToolChart>();
        var perScenarioNew = proj.Select(_ => new List<string>()).ToList();

        foreach (var net in nets)
        {
            bool si = net.Units == UnitSystem.SI;
            string lu = si ? "m" : "ft";
            string Pre(string e) => multi ? $"{net.Name}: {e}" : e;
            if (proj.Any(x => x.Tw != 0) && !(net.Tailwater == TailwaterRule.Elevation && net.TailwaterElevation is not null))
                msgs.Add(CalcMessage.Warning("W-CS-002", $"{net.Name}: the tailwater is not a fixed elevation ({net.Tailwater}), so the tailwater rise is not applied; it follows the scaled flows."));

            var baseRes = StormNetworkSolver.Solve(net);
            if (!baseRes.Succeeded) { msgs.Add(CalcMessage.Error("E-CS-006", $"{net.Name} does not solve at the design flows; fix the network first.")); continue; }
            var baseFails = Failures(net, baseRes).Select(f => (f.Element, f.Mode)).ToHashSet();

            // ---- each projection
            for (int i = 0; i < proj.Count; i++)
            {
                var (name, pct, tw, src) = proj[i];
                var n = Apply(net, Factor(pct), tw);
                var res = StormNetworkSolver.Solve(n);
                if (!res.Succeeded)
                {
                    scenRows.Add([multi ? net.Name : null, name, pct, Math.Round(Factor(pct), 3), tw, "does not solve", null, null, null, "network does not solve", "FAILS", src]);
                    perScenarioNew[i].Add(Pre("network does not solve"));
                    continue;
                }
                var fails = Failures(n, res);
                var newOnes = fails.Where(f => !baseFails.Contains((f.Element, f.Mode))).ToList();
                perScenarioNew[i].AddRange(newOnes.Where(f => f.Hard).Select(f => Pre($"{f.Element} {f.Mode.ToLowerInvariant()}")));
                var worst = res.Structures.Where(s => !s.IsOutfall && s.HglClearance is not null).MinBy(s => s.HglClearance!.Value);
                scenRows.Add([multi ? net.Name : null, name, pct, Math.Round(Factor(pct), 3), tw,
                    worst is null ? "" : $"{worst.Structure.Name}: {worst.HglClearance:0.00} {lu}",
                    fails.Count(f => f.Mode == "HGL criterion"), fails.Count(f => f.Mode == "Floods"), fails.Count(f => f.Mode == "Surcharged"),
                    newOnes.Count == 0 ? "" : string.Join("; ", newOnes.Take(6).Select(f => $"{f.Element} {f.Mode.ToLowerInvariant()}")) + (newOnes.Count > 6 ? $"; +{newOnes.Count - 6} more" : ""),
                    newOnes.Any(f => f.Hard) ? "FAILS" : newOnes.Count > 0 ? "OK (surcharge allowed)" : "OK", src]);
            }

            // ---- failure-order scan (rainfall only, design tailwater)
            var first = new Dictionary<(string, string), (double Pct, int Rank, bool Hard)>();
            var clearSeries = new Dictionary<string, List<(double, double)>>();
            double step = Math.Max(0.5, maxPct / 120);
            double prev = 0;
            HashSet<(string, string)> prevSet = [.. baseFails];
            bool broke = false;
            for (double pct = step; pct <= maxPct + 1e-9 && !broke; pct += step)
            {
                var n = Apply(net, Factor(pct), 0);
                var res = StormNetworkSolver.Solve(n);
                if (!res.Succeeded)
                {
                    first.TryAdd(("network", "Does not solve"), (pct, 9, true));
                    msgs.Add(CalcMessage.Warning("W-CS-003", $"{net.Name} stops solving at +{pct:0.#}% rainfall; the scan ends there."));
                    broke = true;
                    break;
                }
                foreach (var s in res.Structures.Where(s => !s.IsOutfall && s.HglClearance is not null))
                {
                    if (!clearSeries.TryGetValue(s.Structure.Name, out var l)) clearSeries[s.Structure.Name] = l = [(0, baseRes.Structures.First(b => b.Structure.Name == s.Structure.Name).HglClearance ?? double.NaN)];
                    l.Add((Math.Round(pct, 2), s.HglClearance!.Value));
                }
                var fails = Failures(n, res);
                foreach (var f in fails.Where(f => !baseFails.Contains((f.Element, f.Mode)) && !first.ContainsKey((f.Element, f.Mode))))
                {
                    // Bisect between the last clean step and this one to 0.1 %.
                    double lo = prev, hi = pct;
                    while (hi - lo > 0.1)
                    {
                        double mid = (lo + hi) / 2;
                        var mr = StormNetworkSolver.Solve(Apply(net, Factor(mid), 0));
                        if (!mr.Succeeded || Failures(mr.Network, mr).Any(x => x.Element == f.Element && x.Mode == f.Mode)) hi = mid; else lo = mid;
                    }
                    first[(f.Element, f.Mode)] = (hi, f.Rank, f.Hard);
                }
                prev = pct;
            }
            foreach (var f in Failures(net, baseRes))
                scans.Add(new(net.Name, Pre(f.Element), f.Mode, 0, f.Rank, f.Hard));
            foreach (var kv in first)
                scans.Add(new(net.Name, Pre(kv.Key.Item1), kv.Key.Item2, kv.Value.Pct, kv.Value.Rank, kv.Value.Hard));

            // ---- chart: rim − HGL for the structures closest to failing
            var chosen = clearSeries.OrderBy(kv => kv.Value.Min(x => x.Item2)).Take(8).ToList();
            if (chosen.Count > 0)
            {
                var lines = new List<ChartLine> { new("Rim (flooding)", 0) };
                if (net.Criteria.HglBelowRim is { } need && need > 0) lines.Add(new($"HGL criterion ({need:0.0#} {lu} below rim)", need));
                charts.Add(new ToolChart($"{net.Name}: rim − HGL as rainfall increases", "Rainfall increase (%)", $"Rim − HGL ({lu})",
                    [.. chosen.Select(kv => new ChartSeries(kv.Key, kv.Value))], lines));
            }
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        // ---- tables
        var tables = new List<ToolTable>();
        var head = new List<string> { "Network", "Scenario", "Rainfall change (%)", "Flow factor", "Tailwater rise", "Least rim − HGL", "Below HGL criterion", "Flooding", "Pipes surcharged", "New failures vs design", "Result", "Source" };
        if (!multi) { head.RemoveAt(0); scenRows = [.. scenRows.Select(r => (IReadOnlyList<object?>)r.Skip(1).ToList())]; }
        tables.Add(new ToolTable("Projections compared with the design", head, scenRows,
            [$"Flow factor = (1 + rainfall change)^{expo:0.##}; every typed pipe flow and structure inflow (major and minor storm) is scaled. Counts include failures the design already has; the last columns list only new ones."]));

        var ordered = scans.OrderBy(s => s.AtPct ?? double.MaxValue).ThenBy(s => s.Rank).ToList();
        var orderRows = new List<IReadOnlyList<object?>>();
        int k = 1;
        foreach (var s in ordered)
        {
            string when = s.AtPct is 0 ? "already in the design" : $"+{s.AtPct:0.0}%";
            string? hit = s.AtPct is 0 ? "design" : proj.Where(x => x.Pct >= s.AtPct - 1e-9).OrderBy(x => x.Pct).Select(x => x.Name).FirstOrDefault();
            orderRows.Add([k++, s.Element, s.Hard ? s.Mode : s.Mode + " (allowed by the criteria)", s.AtPct is 0 ? 0.0 : Math.Round(s.AtPct!.Value, 1), when, hit ?? "beyond every projection"]);
        }
        tables.Add(new ToolTable("Order of failure as rainfall increases", ["Rank", "Element", "Fails by", "Rainfall increase (%)", "When", "First projection that reaches it"], orderRows,
            [$"Scanned from 0 to +{maxPct:0.#}% rainfall at the design tailwater, refined to 0.1%. HGL/EGL criterion: water within the network's required distance of the rim (IDCM §4.6.7). Floods: HGL above the rim. Surcharged: flow above the pipe's full-flow capacity (a failure only where the criteria do not allow surcharge). Velocity above maximum: the criteria's maximum pipe velocity."]));

        // ---- summary
        var newScans = ordered.Where(s => s.AtPct is > 0 && s.Hard).ToList();
        var firstFull = ordered.FirstOrDefault(s => s.AtPct is > 0 && !s.Hard);
        var existing = ordered.Count(s => s.AtPct is 0 && s.Hard);
        var firstNew = newScans.FirstOrDefault();
        var firstFlood = newScans.FirstOrDefault(s => s.Mode == "Floods");
        var summary = new List<string>();
        string scope = multi ? $"{nets.Count} networks" : nets[0].Name;
        summary.Add(existing > 0 ? $"{scope} already has {existing} exception(s) at the design flows; they are listed first and not counted as new failures." : $"{scope} meets its criteria at the design flows.");
        summary.Add(firstNew is null
            ? $"Nothing new fails up to +{maxPct:0.#}% rainfall."
            : $"First to fail: {firstNew.Element} ({firstNew.Mode.ToLowerInvariant()}) at +{firstNew.AtPct:0.0}% rainfall, so the design absorbs about {firstNew.AtPct:0.0}% more rain before anything new fails.");
        if (firstFull is not null) summary.Add($"First pipe to run over its full-flow capacity: {firstFull.Element} at +{firstFull.AtPct:0.0}% (surcharge is allowed by the criteria while the HGL stays below them).");
        if (firstFlood is not null) summary.Add($"First flooding (HGL above a rim): {firstFlood.Element} at +{firstFlood.AtPct:0.0}% rainfall.");
        for (int i = 0; i < proj.Count; i++)
            if (proj[i].Pct != 0 || proj[i].Tw != 0)
                summary.Add($"{proj[i].Name} (+{proj[i].Pct:0.#}%{(proj[i].Tw != 0 ? $", tailwater +{proj[i].Tw:0.##}" : "")}): " +
                            (perScenarioNew[i].Count == 0 ? "no new failures." : $"{perScenarioNew[i].Count} new failure(s): {string.Join(", ", perScenarioNew[i].Take(4))}{(perScenarioNew[i].Count > 4 ? ", …" : "")}."));
        msgs.Add(CalcMessage.Info("I-CS-001", "The rainfall changes are your inputs; MasterFlow bundles no climate projections. Name the source of each in the Source column so the report carries it."));
        if (expo == 1) msgs.Add(CalcMessage.Info("I-CS-002", "Runoff exponent 1: flows scale with rainfall intensity as in the Rational method, with the same times of concentration. Curve-number runoff grows faster than rainfall; use a larger exponent or rerun the hydrology to bound it."));

        // ---- client memo
        var body = new List<ReportBlock>
        {
            new ReportHeading(1, "Climate resilience summary"),
            new ReportParagraph($"We re-ran the storm sewer design ({string.Join(", ", nets.Select(n => n.Name))}) with the design flows increased for future rainfall, using the projections listed below, and found the order in which its pieces would stop meeting the design criteria."),
        };
        body.AddRange(summary.Select(s => (ReportBlock)new ReportParagraph(s, true)));
        body.Add(new ReportParagraph("Method: each pipe flow and structure inflow was scaled by the projected change in design rainfall intensity" + (expo != 1 ? $" raised to the power {expo:0.##}" : "") + ", the hydraulic grade line was recomputed with the same solver and criteria as the design, and each structure and pipe was checked for the HGL criterion, flooding and surcharge. Rainfall projections carry wide uncertainty; the order of failure and the margin are more robust than any single projected value."));
        body.Add(new ReportTable(tables[0]));
        body.Add(new ReportTable(tables[1]));
        var memo = new ReportDocument("Climate stress test", [("Project", context.Project?.Name ?? ""), ("Networks", string.Join(", ", nets.Select(n => n.Name))), ("Runoff exponent", expo.ToString("0.##", Inv))], body, []) { TableOfContents = false };

        return new ToolResult(title, summary, tables, msgs) { Charts = charts, Report = memo };
    }

    /// <summary>The network with flows scaled by <paramref name="factor"/> and a fixed tailwater raised by <paramref name="twRise"/>.</summary>
    public static StormNetwork Apply(StormNetwork n, double factor, double twRise) => n with
    {
        Pipes = [.. n.Pipes.Select(x => x with { Discharge = x.Discharge * factor, MinorDischarge = x.MinorDischarge * factor })],
        Structures = [.. n.Structures.Select(s => s with { Inflow = s.Inflow * factor, MinorInflow = s.MinorInflow * factor })],
        TailwaterElevation = n.Tailwater == TailwaterRule.Elevation && n.TailwaterElevation is { } tw ? tw + twRise : n.TailwaterElevation,
    };

    private static List<Failure> Failures(StormNetwork n, StormNetworkResult res)
    {
        var list = new List<Failure>();
        foreach (var s in res.Structures.Where(s => !s.IsOutfall))
        {
            if (s.HglClearance is { } c)
            {
                if (c < 0) list.Add(new(s.Structure.Name, "Floods", 0));
                else if (n.Criteria.HglBelowRim is { } need && c < need - 1e-9) list.Add(new(s.Structure.Name, "HGL criterion", 2));
            }
            if (s.EglClearance is { } e && n.Criteria.EglBelowRim is { } eneed && e < eneed - 1e-9 && !(s.HglClearance < 0))
                list.Add(new(s.Structure.Name, "EGL criterion", 3));
        }
        foreach (var pr in res.Pipes)
            if (double.IsFinite(pr.FullFlowCapacity) && pr.Pipe.Discharge > pr.FullFlowCapacity + 1e-9)
                list.Add(new(pr.Pipe.Name, "Surcharged", 1, !n.Criteria.AllowSurcharge));
        if (n.Criteria.MaxVelocity is { } vmax)
            foreach (var pr in res.Pipes)
            {
                double v = double.IsFinite(pr.NormalVelocity) ? pr.NormalVelocity
                    : double.IsFinite(pr.FullFlowCapacity) && pr.FullFlowCapacity > 0 ? pr.FullFlowVelocity * pr.Pipe.Discharge / pr.FullFlowCapacity : double.NaN;
                if (v > vmax + 1e-9) list.Add(new(pr.Pipe.Name, "Velocity above maximum", 4));
            }
        return list;
    }
}
