using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Plan-review checklist for the selected storm network against Rapid City IDCM 2012 §4.6: pipe size minimums
/// (12 in., 18 in. with bends), material and n, manhole spacing (400 ft to 30 in., 600 ft above), larger pipe
/// into smaller, crown (soffit) matching or 0.8D matching, velocity not decreasing, slope above critical,
/// 2.5 fps at the 2-yr flow, 20 fps maximum on mains, HGL 1 ft below the rim, cover, and structures where the
/// outlet invert is above an inlet invert. Every row says what was checked, the value, the limit and the section.
/// </summary>
public sealed class PlanReviewTool : IDesignTool
{
    public string Id => "network-plan-review";
    public string Name => "Storm network plan review (IDCM §4.6 checklist)";
    public string Description => "Reviews the selected storm network the way a City reviewer would: sizes, spacing, crowns, inverts, cover, velocities and HGL against Rapid City IDCM 2012 §4.6, one row per finding with its reference. Solve the network first.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context) => """
        # Checks the selected storm network (solve it first). Limits default to Rapid City IDCM 2012 §4.6.
        minimum cover = 1.5        # ft (m) over the crown at each end, rim minus crown; use the pipe manufacturer's value (§4.6.2(2))
        # Bends: yes = treat every pipe as having bends (18 in. minimum); auto = detect from structure coordinates
        bends = auto
        bend angle = 10            # degrees of deflection at a structure counted as a bend (auto)
        show passing = no          # yes lists checks that pass too
        """;

    private sealed record Finding(string Check, string Reference, string Item, object? Value, object? Limit, string Result, string Note);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Storm network plan review — Rapid City IDCM 2012 §4.6";
        if (context.Network is not { } net || context.NetworkResult is not { Succeeded: true } res)
            return ToolResult.Failed(title, CalcMessage.Error("E-PR-001", "Select a storm network and solve it (Storm networks window) before the review."));
        var p = ToolInput.Parse(input);
        bool us = net.Units == UnitSystem.USCustomary;
        double toIn = us ? 12 : 1000;   // rise (ft or m) → in. or mm
        double ft = us ? 1 : 0.3048;                                       // IDCM feet in the network's length unit
        double minCover = p.Number("minimum cover", 1.5 * ft);
        string bendsMode = (p.Text("bends") ?? "auto").ToLowerInvariant();
        double bendAngle = p.Number("bend angle", 10);
        bool showPass = p.Flag("show passing", false);
        string sz = us ? "in." : "mm", L = us ? "ft" : "m", V = us ? "fps" : "m/s";
        var f = new List<Finding>();
        void Add(string check, string reference, string item, object? value, object? limit, bool ok, string note = "", bool warnOnly = false)
        {
            if (ok && !showPass) return;
            f.Add(new Finding(check, reference, item, value, limit, ok ? "OK" : warnOnly ? "REVIEW" : "FAIL", note));
        }
        var structs = net.Structures.ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);
        var pipeRes = res.Pipes.ToDictionary(x => x.Pipe.Name, x => x);
        double Crown(NetworkPipe x, bool up) => (up ? x.UpstreamInvert : x.DownstreamInvert) + x.Rise;

        // Bends: deflection at the upstream structure between the incoming main and this pipe (needs coordinates).
        bool coords = net.Structures.All(s => s.Northing is not null && s.Easting is not null);
        double? Deflection(NetworkPipe x)
        {
            if (!coords) return null;
            var inc = net.Pipes.Where(u => u.To.Equals(x.From, StringComparison.OrdinalIgnoreCase)).OrderByDescending(u => u.Rise).FirstOrDefault();
            if (inc is null) return null;
            var a = structs[inc.From]; var b = structs[x.From]; var c = structs[x.To];
            double h1 = Math.Atan2(b.Northing!.Value - a.Northing!.Value, b.Easting!.Value - a.Easting!.Value), h2 = Math.Atan2(c.Northing!.Value - b.Northing!.Value, c.Easting!.Value - b.Easting!.Value);
            double d = Math.Abs((h2 - h1) * 180 / Math.PI) % 360;
            return d > 180 ? 360 - d : d;
        }
        bool anyBends = bendsMode == "yes" || (bendsMode == "auto" && net.Pipes.Any(x => Deflection(x) > bendAngle));
        if (bendsMode == "auto" && !coords) f.Add(new Finding("Bends", "§4.6.2(2)", "network", null, null, "REVIEW", "No coordinates on every structure: bends could not be detected; the 12 in. minimum was applied. Set bends = yes if the system has bends."));

        foreach (var x in net.Pipes)
        {
            string item = $"{x.Name} ({x.From} → {x.To})";
            double dIn = x.Rise * toIn;
            // Size
            double minSize = (anyBends ? 18 : 12) * (us ? 1 : 25.4);
            Add("Minimum pipe size", "§4.6.2(2)", item, Math.Round(dIn, 1), minSize, dIn >= minSize - 0.01, anyBends ? $"18 {sz} where the system has bends" : "");
            // Material / n
            bool rcpSize = dIn > 18 * (us ? 1 : 25.4) + 0.01;
            if (rcpSize) Add("RCP above 18 in.; n ≥ 0.013", "§4.6.2(2)", item, x.ManningN, 0.013, x.ManningN >= 0.013 - 1e-9);
            else Add("Minimum n (PVC 0.010)", "§4.6.2(2)", item, x.ManningN, 0.010, x.ManningN >= 0.010 - 1e-9);
            // Spacing
            double maxLen = (dIn <= 30 * (us ? 1 : 25.4) + 0.01 ? 400 : 600) * ft;
            Add("Manhole/inlet spacing", "§4.6.3", item, x.Length, maxLen, x.Length <= maxLen + 1e-9, $"{(maxLen / ft):0} ft for {(dIn <= 30 * (us ? 1 : 25.4) + 0.01 ? "≤" : ">")} 30 in.");
            // Larger into smaller, crowns, velocity progression (pipes entering this pipe's upstream structure)
            foreach (var u in net.Pipes.Where(u => u.To.Equals(x.From, StringComparison.OrdinalIgnoreCase)))
            {
                Add("Larger pipe into smaller", "§4.6.6(2)", $"{u.Name} → {x.Name} at {x.From}", Math.Round(x.Rise * toIn, 1), Math.Round(u.Rise * toIn, 1), x.Rise >= u.Rise - 1e-9);
                double cu = Crown(u, false), cd = Crown(x, true);
                if (x.Rise > u.Rise + 1e-9)
                {
                    bool crownOk = cd <= cu + 0.005 * ft;
                    bool p8 = x.UpstreamInvert + 0.8 * x.Rise <= u.DownstreamInvert + 0.8 * u.Rise + 0.005 * ft;
                    Add("Crowns matched at a size increase", "§4.6.6(3)", $"{u.Name} → {x.Name} at {x.From}", Math.Round(cd - cu, 3), 0.0, crownOk || p8,
                        crownOk ? "" : p8 ? "0.8D points matched (allowed for minimal fall)" : $"outgoing crown {cd - cu:F2} {L} above the incoming crown", warnOnly: !crownOk && p8);
                }
                Add("Outlet invert not above inlet invert", "§4.6.6(3)", $"{u.Name} → {x.Name} at {x.From}", Math.Round(x.UpstreamInvert - u.DownstreamInvert, 3), 0.0, x.UpstreamInvert <= u.DownstreamInvert + 1e-6);
                if (pipeRes.TryGetValue(u.Name, out var ru) && pipeRes.TryGetValue(x.Name, out var rx) && ru.VelocityDown > 0 && rx.VelocityUp > 0)
                    Add("Velocity not decreasing", "§4.6.6(1)", $"{u.Name} → {x.Name}", Math.Round(rx.VelocityUp, 2), Math.Round(ru.VelocityDown, 2), rx.VelocityUp >= 0.9 * ru.VelocityDown, "more than 10 % drop flagged for review", warnOnly: true);
            }
            if (pipeRes.TryGetValue(x.Name, out var r))
            {
                if (r.Regime == "Steep") Add("Slope below critical", "§4.6.6(4)", item, Math.Round(r.Slope, 5), null, false, "steep: entrance control and a possible jump in the line", warnOnly: true);
                double vMin = 2.5 * ft, vCheck = double.IsFinite(r.MinorVelocity) ? r.MinorVelocity : r.NormalVelocity;
                if (double.IsFinite(vCheck)) Add("Minimum velocity at the 2-yr flow", "§4.6.2(3)a", item, Math.Round(vCheck, 2), vMin, vCheck >= vMin - 1e-9, double.IsFinite(r.MinorVelocity) ? $"at the {net.MinorStormName} flow" : "no minor-storm flow entered: checked at the design flow");
                bool lateral = !net.Pipes.Any(u => u.To.Equals(x.From, StringComparison.OrdinalIgnoreCase));
                double vMax = Math.Max(r.VelocityUp, r.VelocityDown);
                if (!lateral) Add("Maximum velocity, collectors and mains", "§4.6.2(3)b, Table 4-13", item, Math.Round(vMax, 2), 20 * ft, vMax <= 20 * ft + 1e-9);
            }
            // Cover at each end
            foreach (bool up in new[] { true, false })
            {
                var s = structs.GetValueOrDefault(up ? x.From : x.To);
                if (s?.RimElevation is not { } rim) continue;
                double cover = rim - Crown(x, up);
                Add("Minimum cover over the crown", "§4.6.2(2) (manufacturer)", $"{item} at {(up ? x.From : x.To)}", Math.Round(cover, 2), minCover, cover >= minCover - 1e-9);
            }
        }
        foreach (var s in res.Structures.Where(s => !s.IsOutfall && s.HglClearance is not null))
            Add("HGL at least 1 ft below the rim", "§4.6.7", s.Structure.Name, Math.Round(s.HglClearance!.Value, 2), 1.0 * ft, s.HglClearance >= 1.0 * ft - 1e-9);

        var rows = f.Select(x => (IReadOnlyList<object?>)[x.Result, x.Check, x.Reference, x.Item, x.Value, x.Limit, x.Note]).ToList();
        int fails = f.Count(x => x.Result == "FAIL"), reviews = f.Count(x => x.Result == "REVIEW");
        var msgs = new List<CalcMessage>(p.Errors);
        if (fails > 0) msgs.Add(CalcMessage.Warning("W-PR-001", $"{fails} item(s) do not meet IDCM §4.6; see the checklist."));
        var summary = new List<string>
        {
            $"Network \"{net.Name}\": {net.Pipes.Count} pipes, {net.Structures.Count} structures. {fails} fail, {reviews} to review" + (showPass ? $", {f.Count(x => x.Result == "OK")} pass." : " (passing checks hidden; set show passing = yes to list them)."),
            anyBends ? "Bends present (18 in. minimum applied)." : "No bends detected (12 in. minimum applied).",
        };
        return new ToolResult(title, summary,
        [
            new("Checklist", ["Result", "Check", "IDCM", "Item", "Value", "Limit", "Note"], rows,
                [$"Values in {L}, {V}, pipe sizes in {sz}. Velocities and HGL are from the last solve of the network. Cover = rim − crown at each end (the ground between structures is not known)."]),
        ], msgs);
    }
}
