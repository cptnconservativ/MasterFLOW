using System.Text.RegularExpressions;
using MasterFlow.Application.Hydrology;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Hydrograph network routing (Premium): design storms (Rapid City IDCM Table 4-2 frequency storm, NRCS Type II, or entered
/// rainfall), losses (NRCS curve number or Green–Ampt with IDCM Table 4-4 soils), NRCS dimensionless unit hydrograph with
/// TR-55 time of concentration, Muskingum–Cunge routing through pipes and channels, and level-pool routing through ponds
/// (the detention tool's stage–storage and outlet works). Peak flows can be written to the selected storm network.
/// </summary>
public sealed partial class HydrographRoutingTool : IDesignTool
{
    public string Id => "hydrograph-routing";
    public string Name => "Hydrograph network routing (NRCS UH, TR-55)";
    public string Description => "Runoff hydrographs for every subbasin (NRCS dimensionless unit hydrograph, lag = 0.6 tc, tc from TR-55 flow paths or entered; NRCS curve number or Green–Ampt losses with IDCM Table 4-4 soils) under the Rapid City frequency storms (IDCM §4.3.2, Table 4-2, peak at the first quartile), NRCS Type II or your own rainfall; routed through pipes and channels by Muskingum–Cunge (IDCM §4.3.8) and through ponds by storage indication (§4.3.9). With a storm network open and no [Reaches], its pipes are the reaches; Apply writes the routed peak flows into the pipes.";
    public bool UsesNetwork => true;

    private const double G = 32.174;

    public string SampleInput(DesignToolContext context)
    {
        var net = context.Network;
        if (net is not null && net.Pipes.Count > 0)
        {
            // Subbasins at the upstream ends of the selected network, 3 ac each.
            var starts = net.Structures.Where(s => net.Pipes.Any(p => p.From == s.Name)).Take(6).ToList();
            var rows = string.Join("\n", starts.Select((s, i) => $"B{i + 1}\t{s.Name}\t{3 + i}\t{80 + i % 3 * 4}\t\t{12 + 2 * i}"));
            return Header + "\n[Subbasins]\nName\tNode\tArea (ac)\tCN\tSoil\tTc (min)\n" + rows + "\n\n# The selected network's pipes are the reaches (Muskingum–Cunge, circular).\n";
        }
        return Header + """

            [Subbasins]
            # Losses: CN, or Green–Ampt from Soil (IDCM Table 4-4 class) or Ks/Suction; Ia (in), Deficit, Impervious (%) or MIA (%, Sutherland EIA).
            # Tc (min), Lag (min), or blank Tc with rows under [Tc flow paths].
            Name	Node	Area (ac)	CN	Soil	Ia (in)	Deficit	Impervious (%)	Tc (min)
            North	J1	18	78
            East	J1	9		silt loam	0.35	0.2	35	14
            South	J2	14	84					22
            Lot 4	POND	6	90					8

            [Tc flow paths]
            # TR-55 Chapter 3. Type: sheet (n, P2 in; ≤ 100 ft recommended), shallow (paved / unpaved), channel (n, R ft).
            Subbasin	Type	Length (ft)	Slope (ft/ft)	n	P2 (in)	R (ft)	Surface
            North	sheet	100	0.02	0.15	2.0
            North	shallow	900	0.015				unpaved
            North	channel	1600	0.008	0.035		1.2

            [Reaches]
            # Shape: circle (Size = diameter, ft) or trapezoid (Size = bottom width, Side slope = z H:V).
            Name	From	To	Length (ft)	Slope (ft/ft)	Shape	Size (ft)	Side slope	n
            Swale	J1	POND	1200	0.006	trapezoid	4	4	0.035
            P-10	POND	J2	350	0.008	circle	3		0.013
            P-11	J2	OUT	420	0.007	circle	3.5		0.013

            [Ponds]
            # A pond at a node routes everything arriving there; its outflow leaves by the node's reach.
            Name	Node	Top of berm (ft)
            Pond A	POND	106.5

            [Pond stages]
            Pond	Elevation (ft)	Area (ft²)
            Pond A	100	9000
            Pond A	102	15000
            Pond A	104	22000
            Pond A	106.5	31000

            [Pond outlets]
            # Same columns as the detention tool's [Outlets], after the pond name.
            Pond	Name	Type	Invert (ft)	Size (ft)	Height (ft)	C	Length (ft)	n	Outlet invert (ft)	Orifice C
            Pond A	Orifice	orifice	100	1.0		0.6
            Pond A	Weir	weir	103.5	6		3.0
            Pond A	Spillway	spillway	105.3	15	4	2.6
            """;
    }

    private const string Header = """
        # Hydrograph network routing, U.S. units (ac, ft, in, min, cfs).
        # rainfall = rapid-city: IDCM Table 4-2 frequency storm (2-, 10-, 100-yr), 5-min blocks, peak at the first quartile.
        # rainfall = type-ii: NRCS 24-h Type II; depth P<year> = in (default the Table 4-2 24-h depth).
        # rainfall = table: [Rainfall] Time (min) then one cumulative depth column (in) per storm, e.g. "100-yr (in)".
        storms = 2, 10, 100
        rainfall = rapid-city
        storm duration = 120      # min (rapid-city: 60 to 1440)
        time step = 1             # min, computation step
        simulation = 480          # min, total time simulated (≥ storm duration)
        # With a storm network open: routed peaks go into the pipes on Apply.
        network storm = 10        # peaks written as the pipes' design Q
        network minor storm = 2   # peaks written as the pipes' minor-storm Q
        """;

    private sealed record Subbasin(string Name, string Node, double AreaAc, string Loss, double Cn, double Ks, double Suction, double Deficit, double Ia, double Imp, double TcMin, double LagMin, string TcSource);
    private sealed record Reach(string Name, string From, string To, double Length, ReachGeometry Geo, string Size);
    private sealed record PondAt(string Name, string Node, Pond Pond, double? Berm);
    private sealed record StormDef(string Label, int? Year, double[] CumRain, string Basis);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Hydrograph network routing — NRCS unit hydrograph, Muskingum–Cunge, level pool";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-HR-001", "Hydrograph routing works in U.S. units (484 peak rate factor, TR-55 equations, IDCM Table 4-2 in inches). Switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        double dtMin = p.Number("time step", 1), simMin = p.Number("simulation", double.NaN), stormMin = p.Number("storm duration", 120);
        if (!(dtMin >= 0.1 && dtMin <= 15)) msgs.Add(CalcMessage.Error("E-HR-002", "time step must be between 0.1 and 15 min."));

        // ---- storms ----
        string rainMode = (p.Text("rainfall") ?? (p.Rows("Rainfall").Count > 0 ? "table" : "rapid-city")).ToLowerInvariant().Replace(" ", "-").Replace("typeii", "type-ii").Replace("type-2", "type-ii");
        var storms = new List<StormDef>();
        if (rainMode == "type-ii") stormMin = 1440;
        if (double.IsNaN(simMin)) simMin = rainMode == "type-ii" ? 1800 : stormMin + 360;
        int nSteps = dtMin > 0 && simMin > 0 ? (int)Math.Ceiling(simMin / dtMin) : 0;
        if (!(simMin >= stormMin)) msgs.Add(CalcMessage.Error("E-HR-003", $"simulation ({simMin:G4} min) must be at least the storm duration ({stormMin:G4} min)."));
        if (nSteps > 200000) msgs.Add(CalcMessage.Error("E-HR-002", "simulation / time step gives more than 200,000 steps; use a longer time step."));
        if (msgs.Count > 0) return ToolResult.Failed(title, [.. p.Errors, .. msgs]);

        var years = (p.Text("storms") ?? "2, 10, 100").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s.Replace("-yr", "").Replace("yr", ""), out int y) && y > 0 ? y : -1).ToList();
        if (rainMode is "rapid-city" or "frequency")
        {
            if (!(stormMin >= 60 && stormMin <= 1440) || Math.Abs(stormMin / 5 - Math.Round(stormMin / 5)) > 1e-9)
                msgs.Add(CalcMessage.Error("E-HR-004", "storm duration must be a multiple of 5 min from 60 to 1440 (IDCM §4.3.2: 1 h to 24 h from Table 4-2)."));
            else foreach (var y in years)
            {
                if (!DesignStorms.RapidCityDepths.TryGetValue(y, out var d)) { msgs.Add(CalcMessage.Error("E-HR-005", $"IDCM Table 4-2 gives the 2-, 10- and 100-yr storms only (storms = {y}). Use rainfall = type-ii or table for others.")); continue; }
                var blocks = DesignStorms.FrequencyStormBlocks(DesignStorms.RapidCityDurations, d, stormMin, 5, p.Number("peak position", 25) / 100);
                storms.Add(new($"{y}-yr", y, DesignStorms.Sample(DesignStorms.FromBlocks(blocks, 5), dtMin, nSteps),
                    $"IDCM Table 4-2 frequency storm, {stormMin:G4} min, 5-min blocks, peak at {p.Number("peak position", 25):G3} %"));
            }
        }
        else if (rainMode == "type-ii")
        {
            foreach (var y in years)
            {
                double depth = p.OptionalNumber($"P{y}") ?? (DesignStorms.RapidCityDepths.TryGetValue(y, out var d) ? d[^1] : double.NaN);
                if (!(depth > 0)) { msgs.Add(CalcMessage.Error("E-HR-006", $"Give the 24-h depth for the {y}-yr storm as P{y} = (in).")); continue; }
                var curve = DesignStorms.TypeII.Select(t => (t.Hour * 60, t.Fraction * depth)).ToList();
                storms.Add(new($"{y}-yr", y, DesignStorms.Sample(curve, dtMin, nSteps), $"NRCS 24-h Type II, P = {depth:G4} in"));
            }
        }
        else if (rainMode == "table")
        {
            var head = p.Header("Rainfall");
            var rows = p.Rows("Rainfall");
            if (head.Count < 2 || rows.Count < 2) msgs.Add(CalcMessage.Error("E-HR-007", "rainfall = table needs [Rainfall]: Time (min), then one cumulative depth column (in) per storm."));
            else for (int c = 1; c < head.Count; c++)
            {
                var pts = new List<(double, double)>();
                foreach (var r in rows)
                    if (p.Cell(r, 0, "time", "Rainfall") is { } t && p.Cell(r, c, "depth", $"Rainfall {head[c]}") is { } v) pts.Add((t, v));
                if (pts.Count < 2 || pts.Zip(pts.Skip(1)).Any(x => x.Second.Item1 <= x.First.Item1 || x.Second.Item2 < x.First.Item2 - 1e-9) || pts[0].Item2 < 0)
                { msgs.Add(CalcMessage.Error("E-HR-008", $"Rainfall column \"{head[c]}\": times must increase and cumulative depths must not decrease (and not be negative).")); continue; }
                if (pts[0].Item1 > 0) pts.Insert(0, (0, 0));
                var m = YearRx().Match(head[c]);
                storms.Add(new(head[c].Split('(')[0].Trim(), m.Success ? int.Parse(m.Groups[1].Value) : null, DesignStorms.Sample(pts, dtMin, nSteps), $"Entered cumulative rainfall, {pts[^1].Item2:G4} in"));
            }
        }
        else msgs.Add(CalcMessage.Error("E-HR-009", $"rainfall = \"{rainMode}\": use rapid-city, type-ii or table."));

        // ---- subbasins ----
        var subs = ParseSubbasins(p, msgs, out var tcRows);
        var reaches = ParseReaches(p, context.Network, msgs, out bool fromNetwork);
        var ponds = ParsePonds(p, msgs);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error) || p.Errors.Count > 0) return ToolResult.Failed(title, [.. p.Errors, .. msgs]);

        // ---- topology ----
        var nodes = subs.Select(s => s.Node).Concat(reaches.SelectMany(r => new[] { r.From, r.To })).Concat(ponds.Select(x => x.Node)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var n in nodes.Where(n => reaches.Count(r => Eq(r.From, n)) > 1))
            msgs.Add(CalcMessage.Error("E-HR-020", $"Node {n} has more than one outgoing reach; flow splits (diversions) are not supported."));
        foreach (var g in ponds.GroupBy(x => x.Node, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            msgs.Add(CalcMessage.Error("E-HR-021", $"Node {g.Key} has more than one pond."));
        var order = new List<string>();
        var pending = new HashSet<string>(nodes, StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0)
        {
            var ready = pending.Where(n => reaches.Where(r => Eq(r.To, n)).All(r => !pending.Contains(r.From))).ToList();
            if (ready.Count == 0) { msgs.Add(CalcMessage.Error("E-HR-022", "The reaches form a loop: " + string.Join(", ", pending) + ".")); break; }
            foreach (var n in ready) { order.Add(n); pending.Remove(n); }
        }
        var outfalls = nodes.Where(n => !reaches.Any(r => Eq(r.From, n))).ToList();
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, msgs.ToArray());

        double dtH = dtMin / 60, dtS = dtMin * 60;
        foreach (var s in subs.Where(s => dtMin > 0.29 * s.LagMin + 1e-9))
            msgs.Add(CalcMessage.Warning("W-HR-001", $"Subbasin {s.Name}: time step {dtMin:G3} min is more than 0.29 × lag ({0.29 * s.LagMin:G3} min; NEH 630 Ch. 16 asks D ≤ 0.133 tc). Use a shorter time step."));

        // ---- run each storm ----
        var subRows = new List<IReadOnlyList<object?>>(); var nodeRows = new List<IReadOnlyList<object?>>(); var reachRows = new List<IReadOnlyList<object?>>();
        var pondRows = new List<IReadOnlyList<object?>>(); var stormRows = new List<IReadOnlyList<object?>>();
        var outfallHydro = new Dictionary<string, (string Storm, double[] Q)[]>();
        var charts = new List<ToolChart>();
        var peaksIntoReach = new Dictionary<(string Storm, string Reach), double>();
        var outfallSeries = new List<ChartSeries>(); var pondSeries = new Dictionary<string, List<ChartSeries>>();
        var hydroTables = new List<ToolTable>();
        int color = 0;
        foreach (var st in storms)
        {
            double total = st.CumRain[^1];
            double maxBlock = Enumerable.Range(1, st.CumRain.Length - 1).Max(k => st.CumRain[k] - st.CumRain[k - 1]);
            stormRows.Add([st.Label, total, maxBlock / dtH, st.Basis]);
            var nodeIn = nodes.ToDictionary(n => n, _ => new double[nSteps + 1], StringComparer.OrdinalIgnoreCase);
            foreach (var s in subs)
            {
                double[] excess = s.Loss == "CN" ? Losses.CurveNumber(st.CumRain, s.Cn)
                    : Losses.GreenAmpt(st.CumRain, dtH, s.Ks, s.Suction, s.Deficit, s.Ia, s.Imp / 100);
                var uh = ScsUnitHydrograph.Ordinates(s.AreaAc, s.LagMin / 60, dtH);
                var q = ScsUnitHydrograph.Convolve(excess, uh, nSteps + 1);
                var (qp, kp) = Peak(q);
                var target = nodeIn[s.Node];
                for (int k = 0; k <= nSteps; k++) target[k] += q[k];
                double vol = Hydrograph.Volume(q, dtS) / 43560;
                subRows.Add([st.Label, s.Name, s.Node, s.AreaAc, s.Loss == "CN" ? $"CN {s.Cn:G3}" : $"G-A Ks {s.Ks:G3}, imp {s.Imp:G3} %", s.TcMin, s.LagMin, total, excess[^1], vol, qp, kp * dtMin]);
                if (excess[^1] > 0 && q[^1] > 0.01 * qp) msgs.Add(CalcMessage.Warning("W-HR-002", $"{st.Label}, subbasin {s.Name}: runoff is still {q[^1]:G3} cfs at the end of the simulation; increase simulation."));
            }
            var nodeOut = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in order)
            {
                var inflow = nodeIn[n];
                double[] outflow = inflow;
                if (ponds.FirstOrDefault(x => Eq(x.Node, n)) is { } pd)
                {
                    var steps = new List<RouteStep>();
                    var rr = pd.Pond.Route(inflow, dtS, 0, null, steps);
                    if (rr.Overtopped) { msgs.Add(CalcMessage.Error("E-HR-030", $"{st.Label}: pond {pd.Name} fills above its top stage; add stages, outlets or storage. Results stop here.")); return ToolResult.Failed(title, msgs.ToArray()); }
                    outflow = new double[nSteps + 1];
                    for (int k = 0; k < Math.Min(steps.Count, nSteps + 1); k++) outflow[k] = steps[k].Outflow;
                    double fb = pd.Berm is { } b ? b - rr.PeakWse : double.NaN;
                    pondRows.Add([st.Label, pd.Name, n, rr.PeakInflow, rr.PeakOutflow, rr.PeakInflow > 0 ? (1 - rr.PeakOutflow / rr.PeakInflow) * 100 : 0, rr.PeakWse, rr.PeakStorage / 43560, rr.PeakSpillway, fb, rr.TimeOfPeakOutflow / 60]);
                    if (!pondSeries.TryGetValue(pd.Name, out var ps)) pondSeries[pd.Name] = ps = [];
                    ps.Add(new ChartSeries($"{st.Label} inflow", Thin(inflow, dtMin), Dashed: true, Color: color));
                    ps.Add(new ChartSeries($"{st.Label} outflow", Thin(outflow, dtMin), Color: color));
                }
                nodeOut[n] = outflow;
                var (pi, ki) = Peak(inflow); var (po, ko) = Peak(outflow);
                nodeRows.Add([st.Label, n, pi, ki * dtMin, po, ko * dtMin, Hydrograph.Volume(outflow, dtS) / 43560, outfalls.Contains(n) ? "outfall" : ""]);
                if (reaches.FirstOrDefault(r => Eq(r.From, n)) is { } rch)
                {
                    var res = MuskingumCunge.Route(outflow, dtS, rch.Length, rch.Geo);
                    var (ri, kri) = Peak(outflow); var (ro, kro) = Peak(res.Outflow);
                    peaksIntoReach[(st.Label, rch.Name)] = ri;
                    double cap = rch.Geo.FullCapacity, ratio = rch.Geo.DepthFor(Math.Min(ri, rch.Geo.Circular ? rch.Geo.Flow(rch.Geo.MaxDepth) : ri)) / (rch.Geo.Circular ? rch.Geo.Size : 1);
                    reachRows.Add([st.Label, rch.Name, $"{rch.From} → {rch.To}", rch.Size, rch.Length, rch.Geo.Slope, double.IsFinite(cap) ? cap : null, ri, ro,
                        ri > 0 ? (1 - ro / ri) * 100 : 0, (kro - kri) * dtMin, rch.Geo.Circular ? ratio : null, res.Subreaches, res.VolumeErrorPercent, res.OverCapacity ? "OVER CAPACITY" : "OK"]);
                    if (res.OverCapacity) msgs.Add(CalcMessage.Warning("W-HR-010", $"{st.Label}: reach {rch.Name} peak inflow {ri:F1} cfs exceeds its full-flow capacity {cap:F1} cfs. Muskingum–Cunge routes it as open-channel flow at the 0.938 D rating; check the pipe's HGL in the storm network (surcharge storage is not modelled)."));
                    if (Math.Abs(res.VolumeErrorPercent) > 1 && res.Outflow[^1] < 0.01 * ro) msgs.Add(CalcMessage.Warning("W-HR-011", $"{st.Label}: reach {rch.Name} volume error {res.VolumeErrorPercent:F2} %; use a shorter time step."));
                    var to = nodeIn[rch.To];
                    for (int k = 0; k <= nSteps; k++) to[k] += res.Outflow[k];
                }
            }
            foreach (var o in outfalls)
            {
                outfallSeries.Add(new ChartSeries($"{st.Label} at {o}", Thin(nodeOut[o], dtMin), Color: color));
                hydroTables.Add(HydroTable($"Hydrograph at {o}, {st.Label}", nodeIn[o], nodeOut[o], st.CumRain, dtMin));
            }
            charts.Add(new ToolChart($"Rainfall intensity, {st.Label}", "Time (min)", "Intensity (in/h)",
                [new ChartSeries(st.Label, Enumerable.Range(1, Math.Min(nSteps, (int)Math.Ceiling(stormMin / dtMin) + 1)).Select(k => ((k - 0.5) * dtMin, (st.CumRain[Math.Min(k, nSteps)] - st.CumRain[k - 1]) / dtH)).ToList(), Color: color)]));
            color++;
        }

        // ---- result ----
        var tables = new List<ToolTable>
        {
            new("Design storms", ["Storm", "Depth (in)", "Peak intensity (in/h)", "Basis"], stormRows),
            new("Subbasin runoff", ["Storm", "Subbasin", "Node", "Area (ac)", "Losses", "Tc (min)", "Lag (min)", "Rain (in)", "Runoff (in)", "Volume (ac-ft)", "Peak (cfs)", "Time of peak (min)"], subRows,
                ["NRCS dimensionless unit hydrograph (NEH 630 Ch. 16, PRF 484), Tp = Δt/2 + lag, lag = 0.6 tc; unit hydrograph scaled to exactly 1 in of runoff.",
                 "CN losses: Q = (P − 0.2S)²/(P + 0.8S), S = 1000/CN − 10 (TR-55 Eq. 2-1 to 2-4, AMC II). Green–Ampt: pervious area fills Ia, then Green–Ampt capacity; impervious area has no loss (HEC-HMS)."]),
        };
        if (tcRows.Count > 0) tables.Add(new("Time of concentration (TR-55 Chapter 3)", ["Subbasin", "Segment", "Length (ft)", "Slope (ft/ft)", "Parameters", "Velocity (ft/s)", "Travel time (min)"], tcRows,
            ["Sheet: Tt = 0.007 (nL)^0.8 / (P2^0.5 s^0.4) (Eq. 3-3). Shallow: V = 16.1345 √s unpaved, 20.3282 √s paved (Fig. 3-1). Channel: V = 1.49 r^⅔ s^½ / n (Eq. 3-4)."]));
        tables.Add(new("Nodes", ["Storm", "Node", "Peak inflow (cfs)", "Time (min)", "Peak outflow (cfs)", "Time (min)", "Volume out (ac-ft)", "Note"], nodeRows,
            ["Hydrographs are added at nodes as they arrive, so peaks from different subbasins are not assumed to coincide."]));
        if (reachRows.Count > 0) tables.Add(new("Reaches (Muskingum–Cunge)", ["Storm", "Reach", "Nodes", "Size", "Length (ft)", "Slope", "Full capacity (cfs)", "Peak in (cfs)", "Peak out (cfs)", "Attenuation (%)", "Peak lag (min)", "Depth / D", "Subreaches", "Volume error (%)", "Status"], reachRows,
            ["Variable-parameter Muskingum–Cunge: K = Δx/c, X = ½(1 − Q/(T S c Δx)), c = dQ/dA from Manning; Δx set for a Courant number near 1 at half the peak flow. Volume error compares the volume out to the volume in over the simulation (negative = still draining)."]));
        if (pondRows.Count > 0) tables.Add(new("Ponds (storage indication)", ["Storm", "Pond", "Node", "Peak in (cfs)", "Peak out (cfs)", "Reduction (%)", "Peak WSE (ft)", "Peak storage (ac-ft)", "Spillway (cfs)", "Freeboard (ft)", "Time of peak out (min)"], pondRows,
            ["Level-pool storage indication, 2S₂/Δt + O₂ = I₁ + I₂ + 2S₁/Δt − O₁, outlets as in the detention tool. IDCM §4.10.4 asks 1 ft of freeboard; check outflow against allowable rates in the detention tool."]));
        tables.AddRange(hydroTables);
        charts.Insert(0, new ToolChart("Outfall hydrographs", "Time (min)", "Flow (cfs)", outfallSeries));
        foreach (var (name, series) in pondSeries) charts.Insert(1, new ToolChart($"Pond {name}: inflow and outflow", "Time (min)", "Flow (cfs)", series));

        var summary = new List<string>
        {
            $"{subs.Count} subbasins ({subs.Sum(s => s.AreaAc):G4} ac), {reaches.Count} reaches{(fromNetwork ? " (the selected network's pipes)" : "")}, {ponds.Count} pond(s); time step {dtMin:G3} min, {simMin:G4} min simulated.",
        };
        foreach (var o in outfalls)
            foreach (var r in nodeRows.Where(r => Eq((string)r[1]!, o)))
                summary.Add($"{r[0]}: peak at outfall {o} = {(double)r[4]!:F1} cfs at {(double)r[5]!:G4} min, volume {(double)r[6]!:F2} ac-ft.");
        msgs.Add(CalcMessage.Info("I-HR-001", "IDCM §4.2.1 requires HEC-HMS for basins over 160 ac without a Drainage Basin Plan and for Drainage Basin Plans; §4.3 names Green–Ampt losses and the Snyder unit hydrograph. This tool uses the NRCS unit hydrograph; use it for design and checking, and confirm against HEC-HMS where the IDCM requires it."));

        var result = new ToolResult(title, summary, tables, msgs) { Charts = charts };

        // ---- apply to network ----
        if (fromNetwork && context.Network is { } net)
        {
            string design = (p.Text("network storm") ?? "10").Replace("-yr", "").Trim() + "-yr", minor = (p.Text("network minor storm") ?? "2").Replace("-yr", "").Trim() + "-yr";
            bool hasDesign = storms.Any(s => s.Label == design), hasMinor = storms.Any(s => s.Label == minor);
            if (hasDesign)
            {
                var pipes = net.Pipes.Select(pp => pp with
                {
                    Discharge = peaksIntoReach.TryGetValue((design, pp.Name), out var q) ? Math.Round(q, 2) : pp.Discharge,
                    MinorDischarge = hasMinor && peaksIntoReach.TryGetValue((minor, pp.Name), out var qm) ? Math.Round(qm, 2) : pp.MinorDischarge,
                }).ToList();
                result = result with
                {
                    UpdatedNetwork = net with { Pipes = pipes, MinorStormName = hasMinor ? minor : net.MinorStormName },
                    UpdateDescription = $"Pipe design Q set to the routed {design} peak flow entering each pipe" + (hasMinor ? $", minor-storm Q to the {minor} peak." : "."),
                };
            }
            else msgs.Add(CalcMessage.Warning("W-HR-020", $"network storm = {design} is not among the storms run, so nothing can be applied to the network."));
        }
        return result;
    }

    // ------------------------------------------------------------------ parsing

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Column index whose header starts with any of <paramref name="keys"/> (case-insensitive), or −1.</summary>
    private static int Col(IReadOnlyList<string> head, params string[] keys)
    {
        for (int i = 0; i < head.Count; i++)
            foreach (var k in keys)
            {
                string h = head[i].Trim();
                // whole-word prefix: "n" matches "n" and "n (Manning)" but not "Name"
                if (h.StartsWith(k, StringComparison.OrdinalIgnoreCase) && (h.Length == k.Length || !char.IsLetter(k[^1]) || !char.IsLetter(h[k.Length]))) return i;
            }
        return -1;
    }

    private static string? Str(string[] r, int i) => i >= 0 && i < r.Length && !string.IsNullOrWhiteSpace(r[i]) ? r[i].Trim() : null;
    private static double? Num(ToolInput p, string[] r, int i, string what, string where) => i < 0 ? null : p.Cell(r, i, what, where);

    private static List<Subbasin> ParseSubbasins(ToolInput p, List<CalcMessage> msgs, out List<IReadOnlyList<object?>> tcRows)
    {
        tcRows = [];
        var head = p.Header("Subbasins");
        var rows = p.Rows("Subbasins");
        if (rows.Count == 0) { msgs.Add(CalcMessage.Error("E-HR-010", "[Subbasins] is empty: Name, Node, Area (ac), then CN or Green–Ampt columns and Tc (min).")); return []; }
        int cName = Col(head, "name"), cNode = Col(head, "node", "outlet"), cArea = Col(head, "area"), cCn = Col(head, "cn"), cSoil = Col(head, "soil"),
            cKs = Col(head, "ks"), cSuc = Col(head, "suction"), cDef = Col(head, "deficit"), cIa = Col(head, "ia"), cImp = Col(head, "imp"), cMia = Col(head, "mia"),
            cTc = Col(head, "tc"), cLag = Col(head, "lag");
        if (cName < 0) cName = 0;
        if (cNode < 0) cNode = 1;
        if (cArea < 0) cArea = 2;
        var paths = p.Rows("Tc flow paths");
        var ph = p.Header("Tc flow paths");
        int pSub = Math.Max(0, Col(ph, "sub")), pType = Col(ph, "type"), pLen = Col(ph, "length"), pSlope = Col(ph, "slope"), pN = Col(ph, "n"),
            pP2 = Col(ph, "p2"), pR = Col(ph, "r", "hydraulic"), pSurf = Col(ph, "surface");

        var list = new List<Subbasin>();
        foreach (var r in rows)
        {
            string name = Str(r, cName) ?? "?", where = $"Subbasin {name}";
            string? node = Str(r, cNode);
            double? area = Num(p, r, cArea, "area", where), cn = Num(p, r, cCn, "CN", where), ks = Num(p, r, cKs, "Ks", where), suc = Num(p, r, cSuc, "suction", where),
                def = Num(p, r, cDef, "deficit", where), ia = Num(p, r, cIa, "Ia", where), imp = Num(p, r, cImp, "impervious", where), mia = Num(p, r, cMia, "MIA", where),
                tc = Num(p, r, cTc, "Tc", where), lag = Num(p, r, cLag, "lag", where);
            string? soil = Str(r, cSoil);
            if (node is null || area is not > 0) { msgs.Add(CalcMessage.Error("E-HR-011", $"{where}: a node and an area greater than 0 are required.")); continue; }
            if (area > 2000) msgs.Add(CalcMessage.Warning("W-HR-003", $"{where}: {area:G4} ac is large for a single unit hydrograph; split it into subbasins (TR-55 and the IDCM expect subbasins routed by reaches)."));
            string loss;
            if (cn is not null)
            {
                if (cn is not (> 30 and <= 100)) { msgs.Add(CalcMessage.Error("E-HR-012", $"{where}: CN {cn:G4} must be between 30 and 100.")); continue; }
                loss = "CN";
            }
            else
            {
                if (soil is not null)
                {
                    if (!Losses.RapidCitySoils.TryGetValue(soil, out var sp)) { msgs.Add(CalcMessage.Error("E-HR-013", $"{where}: soil \"{soil}\" is not an IDCM Table 4-4 class ({string.Join(", ", Losses.RapidCitySoils.Keys)}).")); continue; }
                    ks ??= sp.Ks; suc ??= sp.Suction;
                }
                if (ks is not > 0 || suc is not >= 0) { msgs.Add(CalcMessage.Error("E-HR-014", $"{where}: give a CN, or Green–Ampt Ks (in/h) and suction (in) or an IDCM Table 4-4 soil.")); continue; }
                if (def is null) msgs.Add(CalcMessage.Info("I-HR-010", $"{where}: moisture deficit not given; 0.20 used (IDCM §4.3.6: 0.15 to 0.25)."));
                if (ia is null) msgs.Add(CalcMessage.Info("I-HR-011", $"{where}: initial abstraction not given; 0.35 in used (IDCM Table 4-3, lawn and grass)."));
                loss = "GA";
            }
            if (def is < 0 or > 0.6 || ia is < 0 || imp is < 0 or > 100 || mia is < 0 or > 100) { msgs.Add(CalcMessage.Error("E-HR-015", $"{where}: deficit must be 0–0.6, Ia ≥ 0, impervious and MIA 0–100 %.")); continue; }
            double impPct = imp ?? (mia is { } m ? Losses.Sutherland(m) : 0);
            if (imp is null && mia is { } m2) msgs.Add(CalcMessage.Info("I-HR-012", $"{where}: EIA = 0.1·MIA^1.5 = {impPct:F1} % from MIA {m2:G3} % (IDCM Eq. 4-3-1)."));

            string src;
            double tcMin, lagMin;
            var segs = paths.Where(x => Eq(Str(x, pSub) ?? "", name)).ToList();
            if (lag is > 0) { lagMin = lag.Value; tcMin = lag.Value / 0.6; src = "lag entered"; }
            else if (tc is > 0) { tcMin = tc.Value; lagMin = 0.6 * tcMin; src = "entered"; }
            else if (segs.Count > 0)
            {
                double sum = 0; bool bad = false;
                foreach (var s in segs)
                {
                    string type = (Str(s, pType) ?? "").ToLowerInvariant(), w = $"{where} flow path";
                    double? L = Num(p, s, pLen, "length", w), S = Num(p, s, pSlope, "slope", w), n = Num(p, s, pN, "n", w), p2 = Num(p, s, pP2, "P2", w), rr = Num(p, s, pR, "R", w);
                    if (L is not > 0 || S is not > 0) { msgs.Add(CalcMessage.Error("E-HR-016", $"{w}: length and slope greater than 0 are required.")); bad = true; continue; }
                    double minutes, v;
                    switch (type)
                    {
                        case "sheet":
                            double p2v = p2 ?? 2.0;
                            if (n is not > 0) { msgs.Add(CalcMessage.Error("E-HR-017", $"{w}: sheet flow needs Manning's n (TR-55 Table 3-1).")); bad = true; continue; }
                            if (L > 300) { msgs.Add(CalcMessage.Error("E-HR-018", $"{w}: sheet flow longer than 300 ft (TR-55 limit).")); bad = true; continue; }
                            if (L > 100) msgs.Add(CalcMessage.Warning("W-HR-004", $"{w}: sheet flow {L:G4} ft; NRCS (NEH 630 Ch. 15, 2010) limits sheet flow to 100 ft."));
                            if (p2 is null) msgs.Add(CalcMessage.Info("I-HR-013", $"{w}: P2 not given; 2.0 in used (IDCM Table 4-2, 2-yr 24-h)."));
                            minutes = Tr55.SheetHours(n.Value, L.Value, p2v, S.Value) * 60; v = L.Value / (minutes * 60);
                            tcRows.Add([name, "sheet", L, S, $"n {n:G3}, P2 {p2v:G3} in", v, minutes]);
                            break;
                        case "shallow":
                            bool paved = (Str(s, pSurf) ?? "unpaved").StartsWith("paved", StringComparison.OrdinalIgnoreCase);
                            v = Tr55.ShallowVelocity(paved, S.Value); minutes = L.Value / v / 60;
                            tcRows.Add([name, "shallow concentrated", L, S, paved ? "paved" : "unpaved", v, minutes]);
                            break;
                        case "channel":
                            if (n is not > 0 || rr is not > 0) { msgs.Add(CalcMessage.Error("E-HR-017", $"{w}: channel flow needs n and hydraulic radius R (ft).")); bad = true; continue; }
                            v = Tr55.ChannelVelocity(n.Value, rr.Value, S.Value); minutes = L.Value / v / 60;
                            tcRows.Add([name, "channel", L, S, $"n {n:G3}, R {rr:G3} ft", v, minutes]);
                            break;
                        default: msgs.Add(CalcMessage.Error("E-HR-019", $"{w}: type \"{type}\" must be sheet, shallow or channel.")); bad = true; continue;
                    }
                    sum += minutes;
                }
                if (bad) continue;
                tcRows.Add([name, "Tc", null, null, null, null, sum]);
                tcMin = sum; lagMin = 0.6 * sum; src = "TR-55";
            }
            else { msgs.Add(CalcMessage.Error("E-HR-019", $"{where}: give Tc (min), Lag (min) or rows under [Tc flow paths].")); continue; }
            if (tcMin < 6) msgs.Add(CalcMessage.Warning("W-HR-005", $"{where}: tc {tcMin:G3} min is below the 0.1 h (6 min) minimum TR-55 uses."));
            list.Add(new(name, node, area.Value, loss, cn ?? 0, ks ?? 0, suc ?? 0, def ?? 0.2, ia ?? 0.35, impPct, tcMin, lagMin, src));
        }
        foreach (var d in list.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            msgs.Add(CalcMessage.Error("E-HR-011", $"Subbasin {d.Key} is listed more than once."));
        return list;
    }

    private static List<Reach> ParseReaches(ToolInput p, StormNetwork? net, List<CalcMessage> msgs, out bool fromNetwork)
    {
        fromNetwork = false;
        var list = new List<Reach>();
        var rows = p.Rows("Reaches");
        if (rows.Count == 0 && net is not null && net.Pipes.Count > 0)
        {
            fromNetwork = true;
            foreach (var pp in net.Pipes)
            {
                double s = pp.Length > 0 ? (pp.UpstreamInvert - pp.DownstreamInvert) / pp.Length : double.NaN;
                if (!(s > 0)) { msgs.Add(CalcMessage.Warning("W-HR-012", $"Pipe {pp.Name}: slope {s:G3} is not downhill; 0.1 % used for routing.")); s = 0.001; }
                if (pp.Shape != NetworkPipeShape.Circular) msgs.Add(CalcMessage.Warning("W-HR-013", $"Pipe {pp.Name}: elliptical pipe routed as a circle of its rise."));
                list.Add(new(pp.Name, pp.From, pp.To, Math.Max(pp.Length, 1), new ReachGeometry(true, pp.Rise, 0, pp.ManningN, s), $"{pp.Rise * 12:G3} in"));
            }
            return list;
        }
        var head = p.Header("Reaches");
        int cName = Math.Max(0, Col(head, "name")), cFrom = Col(head, "from"), cTo = Col(head, "to"), cLen = Col(head, "length"), cS = Col(head, "slope"),
            cShape = Col(head, "shape"), cSize = Col(head, "size", "diameter", "bottom"), cZ = Col(head, "side"), cN = Col(head, "n");
        foreach (var r in rows)
        {
            string name = Str(r, cName) ?? "?", where = $"Reach {name}";
            string? from = Str(r, cFrom), to = Str(r, cTo), shape = Str(r, cShape)?.ToLowerInvariant();
            double? L = Num(p, r, cLen, "length", where), S = Num(p, r, cS, "slope", where), size = Num(p, r, cSize, "size", where), z = Num(p, r, cZ, "side slope", where), n = Num(p, r, cN, "n", where);
            if (from is null || to is null || Eq(from, to)) { msgs.Add(CalcMessage.Error("E-HR-023", $"{where}: From and To must name two different nodes.")); continue; }
            if (L is not > 0 || S is not > 0 || size is not > 0 || n is not > 0 || z is < 0) { msgs.Add(CalcMessage.Error("E-HR-024", $"{where}: length, slope, size and n must be greater than 0 (side slope ≥ 0).")); continue; }
            bool circle = shape is null or "circle" or "circular" or "pipe";
            if (!circle && shape is not ("trapezoid" or "trapezoidal" or "channel" or "prism")) { msgs.Add(CalcMessage.Error("E-HR-025", $"{where}: shape must be circle or trapezoid.")); continue; }
            list.Add(new(name, from, to, L.Value, new ReachGeometry(circle, size.Value, z ?? 0, n.Value, S.Value), circle ? $"{size * 12:G3} in" : $"b {size:G3} ft, z {z ?? 0:G3}"));
        }
        return list;
    }

    private static List<PondAt> ParsePonds(ToolInput p, List<CalcMessage> msgs)
    {
        var list = new List<PondAt>();
        var head = p.Header("Ponds");
        int cName = Math.Max(0, Col(head, "name")), cNode = Col(head, "node"), cBerm = Col(head, "top");
        foreach (var r in p.Rows("Ponds"))
        {
            string name = Str(r, cName) ?? "?", where = $"Pond {name}";
            string? node = Str(r, cNode);
            double? berm = Num(p, r, cBerm, "top of berm", where);
            if (node is null) { msgs.Add(CalcMessage.Error("E-HR-026", $"{where}: a node is required.")); continue; }
            var stages = new List<(double, double)>();
            foreach (var s in p.Rows("Pond stages").Where(s => Eq(Str(s, 0) ?? "", name)))
                if (p.Cell(s, 1, "elevation", where) is { } e && p.Cell(s, 2, "area", where) is { } a) stages.Add((e, a));
            if (stages.Count < 2 || stages.Zip(stages.Skip(1)).Any(x => x.Second.Item1 <= x.First.Item1) || stages.Any(x => x.Item2 < 0))
            { msgs.Add(CalcMessage.Error("E-HR-027", $"{where}: [Pond stages] needs at least two rows with rising elevations and areas ≥ 0.")); continue; }
            var outletRows = p.Rows("Pond outlets").Where(s => Eq(Str(s, 0) ?? "", name)).Select(s => s.Skip(1).ToArray()).ToList();
            var omsgs = new List<CalcMessage>();
            var outlets = DetentionTool.ParseOutlets(p, outletRows, omsgs);
            msgs.AddRange(omsgs.Select(m => m with { Text = $"{where}: {m.Text}" }));
            if (omsgs.Any(m => m.Severity == MessageSeverity.Error)) continue;
            list.Add(new(name, node, new Pond(stages, outlets, G, true), berm));
        }
        return list;
    }

    // ------------------------------------------------------------------ output helpers

    private static (double Q, int K) Peak(double[] q)
    {
        int k = 0;
        for (int i = 1; i < q.Length; i++) if (q[i] > q[k]) k = i;
        return (q[k], k);
    }

    /// <summary>At most ~400 chart points, always keeping the peak.</summary>
    private static List<(double, double)> Thin(double[] q, double dtMin)
    {
        int step = Math.Max(1, q.Length / 400), kp = Peak(q).K;
        var pts = new List<(double, double)>();
        for (int k = 0; k < q.Length; k += step)
        {
            if (kp > k - step && kp < k) pts.Add((kp * dtMin, q[kp]));
            pts.Add((k * dtMin, q[k]));
        }
        return pts;
    }

    private static ToolTable HydroTable(string title, double[] inflow, double[] outflow, double[] cumRain, double dtMin)
    {
        int last = Array.FindLastIndex(outflow, x => x > 0.005 * outflow.Max());
        last = Math.Min(outflow.Length - 1, Math.Max(last + 1, 1));
        int step = Math.Max(1, (int)Math.Ceiling(last / 60.0)), kp = Peak(outflow).K;
        var ks = Enumerable.Range(0, last / step + 1).Select(i => i * step).Append(kp).Append(last).Distinct().Order().ToList();
        return new(title, ["Time (min)", "Cumulative rain (in)", "Inflow (cfs)", "Outflow (cfs)"],
            ks.Select(k => (IReadOnlyList<object?>)[k * dtMin, cumRain[Math.Min(k, cumRain.Length - 1)], inflow[k], outflow[k]]).ToList());
    }

    [GeneratedRegex(@"(\d+)\s*-?\s*yr", RegexOptions.IgnoreCase)]
    private static partial Regex YearRx();
}
