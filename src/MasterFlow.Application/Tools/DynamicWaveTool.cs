using System.Globalization;
using MasterFlow.Application.Routing;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Dynamic-wave (unsteady, full St. Venant) routing of inflow hydrographs through the selected storm network: peak HGL at
/// every structure with its timing, surcharge and flooding, peak pipe flows, continuity, and a comparison with the
/// steady HGL/EGL run.
/// </summary>
public sealed class DynamicWaveTool : IDesignTool
{
    public string Id => "dynamic-wave";
    public string Name => "Dynamic-wave storm sewer routing (unsteady HGL)";
    public string Description => "Unsteady routing of inflow hydrographs through the selected storm network by the full dynamic wave (St. Venant) link-node method of EPA SWMM 5: storage in pipes and structures, backwater, surcharge and flooding at the rim, flow reversal. Reports the peak HGL and its time at every structure, surcharge and flood duration and volume, peak pipe flows and velocities, continuity, and the difference from the steady HGL run.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context)
    {
        string q = context.Units == UnitSystem.SI ? "m³/s" : "cfs", a = context.Units == UnitSystem.SI ? "1.17" : "12.57";
        return $"""
            # Dynamic-wave routing of the selected storm network. Flows in {q}; times in minutes unless noted.
            # By default each structure receives a triangular hydrograph peaking at its design inflow (the structure's
            # inflow, or the difference between its typed outgoing and incoming pipe flows).
            hydrograph = triangle     # triangle, or table (give [Hydrographs])
            time to peak = 15         # min
            recession ratio = 1.67    # recession time / time to peak (NRCS triangular)
            duration = 75             # min simulated
            time step =               # s; blank = automatic from the pipes' Courant condition
            manhole area = {a}        # plan area of each structure ({(context.Units == UnitSystem.SI ? "m²" : "ft²")}); 12.57 = 4-ft manhole
            junction losses = yes     # K·V²/2g at each pipe entrance (structure K or the network default)

            # Optional: your own hydrographs, one column per structure (set hydrograph = table).
            # [Hydrographs]
            # Time (min)	CB-3	CB-4
            # 0	0	0
            # 15	12	8
            # 40	0	0
            """;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Dynamic-wave storm sewer routing";
        var net = context.Network;
        if (net is null || net.Pipes.Count == 0) return ToolResult.Failed(title, CalcMessage.Error("E-DW-001", "Open or select a storm network with pipes; this tool routes hydrographs through it."));
        var c = UnitConstants.For(net.Units);
        string L = c.Length, Q = c.Discharge, V = c.Velocity, Vol = net.Units == UnitSystem.SI ? "m³" : "ft³";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        string mode = (p.Text("hydrograph") ?? (p.HasSection("Hydrographs") ? "table" : "triangle")).ToLowerInvariant();
        double tp = p.Number("time to peak", 15), ratio = p.Number("recession ratio", 1.67);
        double? durIn = p.OptionalNumber("duration"), dtIn = p.OptionalNumber("time step");
        double area = p.Number("manhole area", net.Units == UnitSystem.SI ? 1.17 : 12.57);
        bool losses = p.Flag("junction losses", true);
        if (mode is not ("triangle" or "table")) msgs.Add(CalcMessage.Error("E-DW-002", $"hydrograph = \"{mode}\" is not triangle or table."));
        if (!(tp > 0) || !(ratio > 0)) msgs.Add(CalcMessage.Error("E-DW-003", "time to peak and recession ratio must be above zero."));
        if (!(area > 0)) msgs.Add(CalcMessage.Error("E-DW-004", "manhole area must be above zero."));
        if (dtIn is not null and not (> 0 and <= 60)) msgs.Add(CalcMessage.Error("E-DW-005", "time step must be between 0 and 60 s."));
        if (durIn is not null and not > 0) msgs.Add(CalcMessage.Error("E-DW-006", "duration must be above zero."));
        foreach (var pp in net.Pipes)
            if (!(pp.Rise > 0) || !(pp.Length > 0) || !(pp.ManningN > 0)) msgs.Add(CalcMessage.Error("E-DW-007", $"Pipe {pp.Name}: rise, length and n must be above zero."));

        // Inflow hydrographs.
        var structures = net.Structures.Select(s => s.Name).Concat(net.Pipes.SelectMany(x => new[] { x.From, x.To })).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Func<string, double, double> inflow;
        double lastTime;
        var peaks = PeakInflows(net);
        if (mode == "table")
        {
            var head = p.Header("Hydrographs");
            var rows = p.Rows("Hydrographs");
            if (head.Count < 2 || rows.Count < 2) { msgs.Add(CalcMessage.Error("E-DW-008", "hydrograph = table needs [Hydrographs] with a Time (min) column, one column per structure, and at least two rows.")); inflow = (_, _) => 0; lastTime = 0; }
            else
            {
                var cols = new Dictionary<string, List<(double T, double Q)>>(StringComparer.OrdinalIgnoreCase);
                for (int j = 1; j < head.Count; j++)
                {
                    if (!structures.Contains(head[j], StringComparer.OrdinalIgnoreCase)) msgs.Add(CalcMessage.Error("E-DW-009", $"[Hydrographs] column \"{head[j]}\" is not a structure in {net.Name}."));
                    cols[head[j]] = [];
                }
                foreach (var r in rows)
                {
                    double? t = p.Cell(r, 0, "time", "hydrograph row");
                    if (t is null) continue;
                    for (int j = 1; j < head.Count; j++) cols[head[j]].Add((t.Value * 60, p.Cell(r, j, "flow", $"{head[j]} at {t} min") ?? 0));
                }
                lastTime = rows.Select(r => ToolInput.TryNumber(r[0], out double v) ? v : 0).Max();
                inflow = (name, t) => cols.TryGetValue(name, out var s) ? Interp(s, t) : 0;
                peaks = cols.ToDictionary(kv => kv.Key, kv => kv.Value.Count > 0 ? kv.Value.Max(x => x.Q) : 0, StringComparer.OrdinalIgnoreCase);
            }
        }
        else
        {
            double tpS = tp * 60, trS = ratio * tp * 60;
            inflow = (name, t) => peaks.TryGetValue(name, out double qp) && qp > 0 ? t <= tpS ? qp * t / tpS : t <= tpS + trS ? qp * (1 - (t - tpS) / trS) : 0 : 0;
            lastTime = tp * (1 + ratio);
            if (peaks.Values.All(v => v <= 0)) msgs.Add(CalcMessage.Error("E-DW-010", "No structure has a design inflow: give structure inflows or pipe flows in the network, or use hydrograph = table."));
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double autoDt = net.Pipes.Min(x =>
        {
            double d = x.Rise, af = x.Shape == NetworkPipeShape.Circular ? Math.PI * d * d / 4 : Math.PI * x.Rise * x.Span / 4;
            double vf = (double.IsFinite(x.Discharge) ? Math.Abs(x.Discharge) : 0) / af;
            return 0.5 * x.Length / (Math.Sqrt(c.Gravity * d) + Math.Max(vf, 3 * (net.Units == UnitSystem.SI ? 0.3048 : 1)));
        });
        double dt = dtIn ?? Math.Clamp(Math.Floor(autoDt * 4) / 4, 0.25, 5);
        double duration = (durIn ?? lastTime + 30) * 60;
        if (net.Structures.Any(s => s.RimElevation is null)) msgs.Add(CalcMessage.Warning("W-DW-001", "Structures without a rim elevation are treated as sealed: the HGL can rise without limit and no flooding is counted there."));

        var opts = new DynamicWaveOptions { TimeStep = dt, Duration = duration, ReportStep = duration <= 7200 ? 60 : 300, NodeArea = area, JunctionLosses = losses };
        var res = new DynamicWave(net, inflow, opts).Run();

        var steady = context.NetworkResult is { Succeeded: true } sr && sr.Network.Name == net.Name ? sr : null;
        var tables = new List<ToolTable>();
        var nodeRows = new List<IReadOnlyList<object?>>();
        double? hglCrit = net.Criteria.HglBelowRim;
        foreach (var n in res.Nodes)
        {
            var sh = steady?.Structure(n.Name);
            double? clearance = n.Rim is { } rim ? rim - n.MaxHead : null;
            nodeRows.Add([n.Name, R(n.Invert, 2), n.Rim is { } r0 ? R(r0, 2) : null, R(n.MaxHead, 2), R(n.TimeOfMax, 1), sh is not null ? R(sh.Hgl, 2) : null,
                sh is not null ? R(n.MaxHead - sh.Hgl, 2) : null, clearance is { } cl ? R(cl, 2) : null, R(n.SurchargeMinutes, 1), R(n.FloodMinutes, 1), R(n.FloodVolume, 0), R(n.MaxInflow, 2)]);
            if (n.FloodVolume > 0) msgs.Add(CalcMessage.Warning("W-DW-002", $"{n.Name} floods: {n.FloodVolume:F0} {Vol} over {n.FloodMinutes:F1} min leave the system at the rim."));
            else if (hglCrit is { } hc && clearance is { } cl2 && cl2 < hc - 1e-9) msgs.Add(CalcMessage.Warning("W-DW-003", $"{n.Name}: peak HGL is {cl2:F2} {L} below the rim; the criterion is {hc:F2} {L} ({net.Criteria.Source})."));
        }
        tables.Add(new("Structures: peak HGL", ["Structure", $"Invert ({L})", $"Rim ({L})", $"Peak HGL ({L})", "Time (min)", $"Steady HGL ({L})", $"Dynamic − steady ({L})", $"Rim − HGL ({L})", "Surcharged (min)", "Flooded (min)", $"Flood volume ({Vol})", $"Peak inflow ({Q})"], nodeRows,
            ["Invert = lowest connected pipe invert. Surcharged = HGL above the highest connected pipe crown. Steady HGL is the network's HGL/EGL run with its design flows, structure losses by IDCM Figures 4-6/4-7."]));

        var linkRows = new List<IReadOnlyList<object?>>();
        foreach (var l in res.Links)
        {
            var sp = steady?.Pipe(l.Name);
            linkRows.Add([l.Name, R(l.MaxFlow, 2), R(l.TimeOfMax, 1), sp is not null && double.IsFinite(sp.Pipe.Discharge) ? R(sp.Pipe.Discharge, 2) : null, R(l.MaxVelocity, 2), R(l.MaxDepthRatio, 2), R(l.FullFlowMinutes, 1),
                double.IsFinite(l.CourantStep) ? R(l.CourantStep, 1) : null]);
            if (double.IsFinite(l.CourantStep) && l.CourantStep < dt) msgs.Add(CalcMessage.Warning("W-DW-004", $"Pipe {l.Name}: the time step {dt:0.##} s exceeds its Courant time {l.CourantStep:F2} s; reduce time step if results oscillate."));
            if (l.MaxFlow < 0) msgs.Add(CalcMessage.Info("I-DW-001", $"Pipe {l.Name}: peak flow is reversed (negative), toward its upstream structure."));
        }
        tables.Add(new("Pipes: peak flow", ["Pipe", $"Peak Q ({Q})", "Time (min)", $"Design Q ({Q})", $"Peak V ({V})", "Peak depth / rise", "Full (min)", "Courant Δt (s)"], linkRows,
            ["Peak V is the mean velocity at mid-length. Courant Δt = L/(|V| + √(g·D)), the smallest during the run; the run step should not exceed it by much."]));

        double err = res.ContinuityError;
        tables.Add(new("Run and continuity", ["Item", "Value", "Unit"],
        [
            ["Time step", R(dt, 2), "s"], ["Duration", R(duration / 60, 1), "min"], ["Structures / pipes", $"{res.Nodes.Count} / {res.Links.Count}", ""],
            ["Inflow volume", R(res.InflowVolume, 0), Vol], ["Outfall volume", R(res.OutflowVolume, 0), Vol], ["Flood volume", R(res.FloodVolume, 0), Vol],
            ["Storage at end − start", R(res.FinalStorage - res.InitialStorage, 0), Vol], ["Continuity error", R(err, 2), "%"],
            ["Time steps not converged", res.NonConvergedSteps, ""],
        ], ["Continuity error = (inflow − outfall − flooding − change in storage)/inflow. SWMM considers about ±5 % acceptable and ±1 % good."]));
        if (Math.Abs(err) > 5) msgs.Add(CalcMessage.Warning("W-DW-005", $"Continuity error {err:F1} %: reduce the time step."));
        if (res.NonConvergedSteps > 0.05 * duration / dt) msgs.Add(CalcMessage.Warning("W-DW-006", $"{res.NonConvergedSteps} time steps did not converge within {opts.MaxTrials} trials: reduce the time step."));

        // Charts: HGL and flow hydrographs, and the peak HGL profile along the longest path.
        var charts = new List<ToolChart>
        {
            new("HGL at structures", "Time (min)", $"HGL ({L})", res.Nodes.Take(8).Select(n => new ChartSeries(n.Name, res.Times.Select((t, i) => (t, res.NodeHeads[n.Name][i])).ToList())).ToList()),
            new("Pipe flows", "Time (min)", $"Q ({Q})", res.Links.Take(8).Select(l => new ChartSeries(l.Name, res.Times.Select((t, i) => (t, res.LinkFlows[l.Name][i])).ToList())).ToList()),
        };
        if (LongestPath(net) is { Count: > 0 } path)
        {
            var inv = new List<(double, double)>(); var crown = new List<(double, double)>(); var dyn = new List<(double, double)>(); var st = new List<(double, double)>(); var rimS = new List<(double, double)>();
            double x = 0;
            foreach (var pipe in path)
            {
                var nu = res.Nodes.First(n => n.Name.Equals(pipe.From, StringComparison.OrdinalIgnoreCase));
                var nd = res.Nodes.First(n => n.Name.Equals(pipe.To, StringComparison.OrdinalIgnoreCase));
                inv.Add((x, pipe.UpstreamInvert)); crown.Add((x, pipe.UpstreamInvert + pipe.Rise)); dyn.Add((x, Math.Max(nu.MaxHead, pipe.UpstreamInvert)));
                if (nu.Rim is { } r1) rimS.Add((x, r1));
                if (steady?.Pipe(pipe.Name) is { } spp) st.Add((x, spp.HglUp));
                x += pipe.Length;
                inv.Add((x, pipe.DownstreamInvert)); crown.Add((x, pipe.DownstreamInvert + pipe.Rise)); dyn.Add((x, Math.Max(nd.MaxHead, pipe.DownstreamInvert)));
                if (nd.Rim is { } r2) rimS.Add((x, r2));
                if (steady?.Pipe(pipe.Name) is { } spp2) st.Add((x, spp2.HglDown));
            }
            var ser = new List<ChartSeries> { new("Invert", inv), new("Crown", crown, Dashed: true), new("Peak HGL (dynamic)", dyn) };
            if (st.Count > 0) ser.Add(new("HGL (steady)", st, Dashed: true));
            if (rimS.Count > 0) ser.Add(new("Rim", rimS, MarkersOnly: true));
            charts.Add(new ToolChart($"Peak HGL profile, {path[0].From} to {path[^1].To}", $"Distance ({L})", $"Elevation ({L})", ser));
        }

        var outfalls = res.Nodes.Where(n => !net.Pipes.Any(x => x.From.Equals(n.Name, StringComparison.OrdinalIgnoreCase))).Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outPipe = res.Links.Where(l => outfalls.Contains(net.Pipes.First(x => x.Name == l.Name).To)).ToList();
        var worst = res.Nodes.Where(n => n.Rim is not null && !outfalls.Contains(n.Name)).OrderBy(n => n.Rim!.Value - n.MaxHead).FirstOrDefault();
        var summary = new List<string>
        {
            $"{(mode == "table" ? "Entered hydrographs" : $"Triangular hydrographs, peak at {tp:0.#} min")}; Δt = {dt:0.##} s, {duration / 60:0} min; continuity error {err:F2} %.",
            $"Peak outfall flow {string.Join(", ", outPipe.Select(l => $"{l.Name} {l.MaxFlow:F2} {Q} at {l.TimeOfMax:F1} min"))}.",
        };
        if (worst is not null) summary.Add($"Least freeboard: {worst.Name}, peak HGL {worst.MaxHead:F2} ({worst.Rim!.Value - worst.MaxHead:F2} {L} below the rim).");
        int flooded = res.Nodes.Count(n => n.FloodVolume > 0);
        summary.Add(flooded > 0 ? $"{flooded} structure(s) flood; total {res.FloodVolume:F0} {Vol}." : "No flooding.");
        msgs.Add(CalcMessage.Info("I-DW-002", "Dynamic-wave HGLs include storage and timing, so they are usually below the steady HGL from summed peak flows; Rapid City IDCM §4.6.7 asks for the steady energy method, so use this run as a check or for detention-fed systems."));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    /// <summary>Peak inflow at each structure: its Inflow, or its typed outgoing pipe flow less its typed incoming pipe flows.</summary>
    public static Dictionary<string, double> PeakInflows(StormNetwork net)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in net.Structures.Select(x => x.Name).Concat(net.Pipes.SelectMany(x => new[] { x.From, x.To })).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var st = net.Structures.FirstOrDefault(x => x.Name.Equals(s, StringComparison.OrdinalIgnoreCase));
            if (st?.Inflow is { } qi) { d[s] = qi; continue; }
            double qout = net.Pipes.Where(x => x.From.Equals(s, StringComparison.OrdinalIgnoreCase)).Sum(x => double.IsFinite(x.Discharge) ? x.Discharge : 0);
            double qin = net.Pipes.Where(x => x.To.Equals(s, StringComparison.OrdinalIgnoreCase)).Sum(x => double.IsFinite(x.Discharge) ? x.Discharge : 0);
            d[s] = Math.Max(0, qout - qin);
        }
        return d;
    }

    /// <summary>The longest pipe path (by length) from an upstream end to an outfall.</summary>
    public static List<NetworkPipe> LongestPath(StormNetwork net)
    {
        List<NetworkPipe> best = [];
        double bestLen = -1;
        foreach (var start in net.Pipes.Where(x => !net.Pipes.Any(y => y.To.Equals(x.From, StringComparison.OrdinalIgnoreCase))))
        {
            var path = new List<NetworkPipe>();
            var seen = new HashSet<string>();
            for (var cur = start; cur is not null && seen.Add(cur.Name); cur = net.Pipes.FirstOrDefault(y => y.From.Equals(cur.To, StringComparison.OrdinalIgnoreCase))) path.Add(cur);
            double len = path.Sum(x => x.Length);
            if (len > bestLen) { bestLen = len; best = path; }
        }
        return best;
    }

    private static double Interp(List<(double T, double Q)> s, double t)
    {
        if (s.Count == 0) return 0;
        if (t <= s[0].T) return s[0].Q;
        for (int i = 1; i < s.Count; i++)
            if (t <= s[i].T) return s[i - 1].Q + (s[i].Q - s[i - 1].Q) * (t - s[i - 1].T) / (s[i].T - s[i - 1].T);
        return s[^1].Q;
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
