using System.Globalization;
using System.Text;
using MasterFlow.Application.Hydrology;
using MasterFlow.Application.Routing;
using MasterFlow.Application.Swmm;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Wet-weather flows in a designed sanitary sewer network: hourly diurnal dry-weather flow by land use, constant
/// groundwater infiltration, and rainfall-derived infiltration and inflow (RDII) from three RTK triangular unit
/// hydrographs (EPA SSOAP / SWMM 5) for a design storm. The hydrographs are routed by dynamic wave through the network
/// for surcharge and overflows, and the network is written as an EPA SWMM 5 .inp file with [DWF], [PATTERNS],
/// [HYDROGRAPHS] and [RDII] for a design-storm run in SWMM.
/// </summary>
public sealed class SanitaryWetWeatherTool : IDesignTool
{
    public string Id => "sanitary-wet-weather";
    public string Name => "Sanitary wet-weather flows (diurnal DWF, RTK RDII) and SWMM export";
    public string Description => "Dry-weather flow with hourly diurnal patterns by land use, groundwater infiltration, and rainfall-derived infiltration and inflow from RTK unit hydrographs (EPA SSOAP, SWMM 5) for a design storm, on the project's sanitary sewer design. Routes the hydrographs by dynamic wave for surcharge, freeboard and overflow volumes, compares the wet-weather peaks with the IDCM design peak flows, and exports an EPA SWMM 5 .inp file (DWF, patterns, RDII) for a design-storm surcharge run.";
    public bool UsesNetwork => false;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double GpmPerCfs = 448.831;
    /// <summary>1 acre-inch per hour in cfs (43,560 / 12 / 3,600).</summary>
    public const double CfsPerAcInPerHr = 43560.0 / 12 / 3600;

    /// <summary>One RTK triangle: R = fraction of the rain over the sewershed that enters the sewer, T = time to peak (h), K = recession / T.</summary>
    public sealed record Rtk(string Response, double R, double T, double K)
    {
        public double BaseHours => T * (1 + K);
        /// <summary>Unit hydrograph ordinate (1/h) at <paramref name="tau"/> h after an instantaneous unit of rain; area under it is 1.</summary>
        public double Ordinate(double tau)
        {
            if (tau <= 0 || tau >= BaseHours) return 0;
            double peak = 2 / BaseHours;
            return tau <= T ? peak * tau / T : peak * (BaseHours - tau) / (K * T);
        }
        /// <summary>Area under the ordinate from 0 to <paramref name="tau"/> h.</summary>
        public double Cumulative(double tau)
        {
            if (tau <= 0) return 0;
            if (tau >= BaseHours) return 1;
            double peak = 2 / BaseHours;
            if (tau <= T) return peak * tau * tau / (2 * T);
            double rest = BaseHours - tau;
            return 1 - peak * rest * rest / (2 * K * T);
        }
    }

    /// <summary>
    /// RDII (cfs) at times 0, dt, 2dt … from rain blocks (in/h for <paramref name="rainStepH"/> each, starting at t = 0) over
    /// <paramref name="areaAc"/> acres: Σ over triangles and blocks of R·A·i·[U(t − t₀) − U(t − t₀ − Δ)], U the cumulative
    /// unit hydrograph, which is the convolution of the block hyetograph with each triangle.
    /// </summary>
    public static double[] Rdii(IReadOnlyList<double> rainInPerHr, double rainStepH, double areaAc, IReadOnlyList<Rtk> rtk, double dtH, int steps)
    {
        var q = new double[steps];
        for (int k = 0; k < steps; k++)
        {
            double t = k * dtH, sum = 0;
            for (int b = 0; b < rainInPerHr.Count; b++)
            {
                double i = rainInPerHr[b];
                if (i <= 0) continue;
                double t0 = b * rainStepH;
                if (t <= t0) break;
                foreach (var u in rtk)
                    // Rain at rate i for Δ: the response is i·R·[U(t − t0) − U(t − t0 − Δ)] (ac-in/h per acre).
                    sum += u.R * i * (u.Cumulative(t - t0) - u.Cumulative(t - t0 - rainStepH));
            }
            q[k] = sum * areaAc * CfsPerAcInPerHr;
        }
        return q;
    }

    public string SampleInput(DesignToolContext context)
    {
        bool saved = context.ToolInputs.TryGetValue("sanitary-network", out var s) && !string.IsNullOrWhiteSpace(s);
        string tables = saved ? """
            # The project's Sanitary sewer network design is used (its pipes, sizes, inverts and loads). To run this tool on
            # its own, paste a sanitary-network input ([Manholes], [Loads], [Pipes] and its keys) at the end.
            """ : "# No saved Sanitary sewer network design in this project: the network below is the sanitary-network sample.\n" + new SanitarySewerTool().SampleInput(context);
        return $"""
            # Sanitary wet-weather flows. U.S. units: flows in gpm, rain in inches, times in hours.
            # Dry-weather flow (DWF) = each load's average flow × the hourly pattern of its land use (24 multipliers,
            # midnight–1 AM first; "residential", "commercial", "flat", or your metered values).
            residential pattern = residential
            commercial pattern = commercial
            industrial pattern = flat
            public pattern = commercial
            # RDII by three RTK triangles (EPA SSOAP / SWMM): R = fraction of the rain on the sewershed entering the sewer,
            # T = hours to peak, K = recession time / T. These defaults are placeholders: calibrate R, T and K to flow
            # monitoring of this or a similar sewershed.
            rtk short = 0.010, 1, 1
            rtk medium = 0.015, 4, 2
            rtk long = 0.020, 10, 3
            # Design storm: Rapid City 24-hour depth for the return period (2, 10 or 100 yr), or a storm depth; NRCS Type II.
            return period = 10
            storm depth =                 # in.; blank = Rapid City 24-hour depth for the return period
            storm duration = 24           # h
            distribution = Type II        # Type II, or uniform
            storm start = 24              # h after the start of the run (the run starts at midnight)
            duration = 72                 # h simulated
            time step = 15                # min, for the hydrographs and the rain
            routing = dynamic wave        # dynamic wave, or none (sums the upstream hydrographs without attenuation)
            # RDII sewershed area at each manhole: default = the load areas there. Give [Sewersheds] to replace them.
            # [Sewersheds]
            # Manhole	Area (ac)
            # MH-1	12.0
            {tables}
            """;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sanitary wet-weather flows";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-WW-001", "The sanitary tools work in U.S. units (gpm, in., ft); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        // ---- the sanitary design: from this input's tables, or the project's saved sanitary-network input ----
        (ToolResult Result, SanitaryFlowDesign? Design) sd;
        bool own = p.HasSection("Pipes") || p.HasSection("Loads");
        if (own) sd = SanitarySewerTool.FlowDesign(input, context);
        else if (SanitaryFlowDesign.FromProject(context) is { } fp) sd = fp;
        else return ToolResult.Failed(title, CalcMessage.Error("E-WW-002", "Design the sewers first with the Sanitary sewer network design tool (its input is saved with the project), or paste a sanitary-network input with [Manholes], [Loads] and [Pipes] here."));
        if (sd.Design is not { } design)
        {
            msgs.Add(CalcMessage.Error("E-WW-003", "The sanitary sewer design has errors; fix them in the sanitary-network input:"));
            msgs.AddRange(sd.Result.Messages.Where(m => m.Severity == MessageSeverity.Error));
            return new(title, [], [], msgs);
        }
        msgs.Add(CalcMessage.Info("I-WW-001", own ? "Sanitary network, sizes, inverts and loads from the tables in this input (designed by the sanitary-network method)." : "Sanitary network, sizes, inverts and loads from the project's Sanitary sewer network design."));

        // ---- patterns ----
        var patRes = SanitaryDiurnal.Parse(p.Text("residential pattern"), SanitaryDiurnal.Residential, "residential pattern", msgs, "E-WW-004");
        var patCom = SanitaryDiurnal.Parse(p.Text("commercial pattern"), SanitaryDiurnal.Commercial, "commercial pattern", msgs, "E-WW-004");
        var patInd = SanitaryDiurnal.Parse(p.Text("industrial pattern"), SanitaryDiurnal.Flat, "industrial pattern", msgs, "E-WW-004");
        var patPub = SanitaryDiurnal.Parse(p.Text("public pattern"), SanitaryDiurnal.Commercial, "public pattern", msgs, "E-WW-004");
        var patFlow = SanitaryDiurnal.Parse(p.Text("point pattern"), SanitaryDiurnal.Flat, "point pattern", msgs, "E-WW-004");
        double[] PatternOf(SanitaryLandUse u) => u switch
        {
            SanitaryLandUse.Residential or SanitaryLandUse.MultiFamily => patRes,
            SanitaryLandUse.Commercial => patCom,
            SanitaryLandUse.Industrial => patInd,
            SanitaryLandUse.Public => patPub,
            _ => patFlow,
        };

        // ---- RTK ----
        var rtk = new List<Rtk>();
        foreach (var (key, label, def) in new[] { ("rtk short", "Short", "0.010, 1, 1"), ("rtk medium", "Medium", "0.015, 4, 2"), ("rtk long", "Long", "0.020, 10, 3") })
        {
            string text = p.Text(key) ?? def;
            if (text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) || text.Trim() == "0") continue;
            var v = text.Split([',', ' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries).Select(x => ToolInput.TryNumber(x, out double d) ? d : double.NaN).ToArray();
            if (v.Length != 3 || v.Any(x => !double.IsFinite(x)) || v[0] < 0 || v[0] > 1 || !(v[1] > 0) || !(v[2] > 0))
            { msgs.Add(CalcMessage.Error("E-WW-005", $"{key} = \"{text}\": give R (0 to 1), T (h, above 0) and K (above 0), separated by commas, or none.")); continue; }
            if (v[0] > 0) rtk.Add(new Rtk(label, v[0], v[1], v[2]));
        }
        double rTotal = rtk.Sum(x => x.R);
        if (rTotal > 1) msgs.Add(CalcMessage.Error("E-WW-005", $"R1 + R2 + R3 = {rTotal:G3}: more than all of the rain cannot enter the sewer."));
        if (!p.Has("rtk short") && !p.Has("rtk medium") && !p.Has("rtk long"))
            msgs.Add(CalcMessage.Warning("W-WW-005", "RTK parameters are MasterFlow's placeholders (R = 0.010/0.015/0.020, T = 1/4/10 h, K = 1/2/3; 4.5 % of the rain). RDII varies tenfold between sewersheds: calibrate R, T and K to flow monitoring before relying on the result (D-762)."));

        // ---- storm ----
        int rp = (int)Math.Round(p.Number("return period", 10));
        double stormDur = p.Number("storm duration", 24), stormStart = p.Number("storm start", 24), runH = p.Number("duration", 72), dtMin = p.Number("time step", 15);
        string dist = (p.Text("distribution") ?? "Type II").Trim().ToLowerInvariant().Replace(" ", "");
        double? depthIn = p.OptionalNumber("storm depth");
        if (!(stormDur > 0) || stormStart < 0 || !(runH > 0) || !(dtMin is >= 1 and <= 60)) msgs.Add(CalcMessage.Error("E-WW-006", "storm duration and duration must be above zero, storm start not negative, and time step 1 to 60 min."));
        if (dist is not ("typeii" or "type2" or "ii" or "uniform")) msgs.Add(CalcMessage.Error("E-WW-006", $"distribution \"{p.Text("distribution")}\" is not Type II or uniform."));
        bool typeII = dist != "uniform";
        if (typeII && Math.Abs(stormDur - 24) > 1e-9) msgs.Add(CalcMessage.Error("E-WW-006", "The NRCS Type II distribution is a 24-hour storm: set storm duration = 24, or distribution = uniform."));
        double depth;
        string stormName;
        if (depthIn is { } dIn)
        {
            depth = dIn; stormName = $"{dIn:G3} in., {stormDur:G3}-hour {(typeII ? "NRCS Type II" : "uniform")}";
            if (dIn < 0) msgs.Add(CalcMessage.Error("E-WW-006", "storm depth cannot be negative."));
        }
        else if (DesignStorms.RapidCityDepths.TryGetValue(rp, out var depths))
        {
            depth = DesignStorms.DepthAt(DesignStorms.RapidCityDurations, depths, stormDur * 60);
            if (!double.IsFinite(depth)) { msgs.Add(CalcMessage.Error("E-WW-006", "The Rapid City depth table covers 5 min to 24 h; enter a storm depth for other durations.")); depth = 0; }
            stormName = $"Rapid City {rp}-yr {stormDur:G3}-hour, {depth:F2} in., {(typeII ? "NRCS Type II" : "uniform")}";
        }
        else { msgs.Add(CalcMessage.Error("E-WW-006", $"return period {rp}: the Rapid City table has 2, 10 and 100 years; enter a storm depth instead.")); depth = 0; stormName = ""; }
        if (stormStart + stormDur > runH + 1e-9) msgs.Add(CalcMessage.Warning("W-WW-006", $"The storm ends at {stormStart + stormDur:G3} h, after the end of the run ({runH:G3} h)."));

        // ---- sewersheds ----
        var areas = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (p.HasSection("Sewersheds"))
        {
            foreach (var r in p.Rows("Sewersheds"))
            {
                if (r.Length < 2 || design.Manhole(r[0]) is null) { msgs.Add(CalcMessage.Error("E-WW-007", $"[Sewersheds] row \"{string.Join(" ", r)}\": give a manhole of the network and its area (ac).")); continue; }
                double? a = p.Cell(r, 1, "area", r[0]);
                if (a is not >= 0) { msgs.Add(CalcMessage.Error("E-WW-007", $"[Sewersheds] {r[0]}: the area must be a number ≥ 0.")); continue; }
                areas[r[0]] = areas.GetValueOrDefault(r[0]) + a.Value;
            }
        }
        else
            foreach (var l in design.Loads) areas[l.Manhole] = areas.GetValueOrDefault(l.Manhole) + l.AreaAc;
        string routing = (p.Text("routing") ?? "dynamic wave").Trim().ToLowerInvariant();
        bool dyn = routing.StartsWith("dyn");
        if (!dyn && !routing.StartsWith("none") && !routing.StartsWith("no")) msgs.Add(CalcMessage.Error("E-WW-008", $"routing \"{routing}\" is not dynamic wave or none."));

        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        if (areas.Values.Sum() <= 0 && rtk.Count > 0) msgs.Add(CalcMessage.Warning("W-WW-007", "No sewershed area: the loads have no areas (point flows or population only). Give [Sewersheds] for RDII."));

        // ---- rainfall blocks from t = 0 ----
        double dtH = dtMin / 60;
        int steps = (int)Math.Floor(runH / dtH + 1e-9) + 1;
        int stormBlocks = (int)Math.Ceiling(stormDur / dtH - 1e-9);
        int lead = (int)Math.Round(stormStart / dtH);
        var rain = new double[lead + stormBlocks];
        for (int b = 0; b < stormBlocks; b++)
        {
            double t0 = b * dtH, t1 = Math.Min(stormDur, (b + 1) * dtH);
            double f0 = typeII ? DesignStorms.Interp(DesignStorms.TypeII, t0) : t0 / stormDur, f1 = typeII ? DesignStorms.Interp(DesignStorms.TypeII, t1) : t1 / stormDur;
            rain[lead + b] = depth * (f1 - f0) / dtH;
        }

        // ---- node inflows (cfs): DWF by pattern, constant infiltration, RDII ----
        var nodes = design.Manholes.Select(m => m.Name).ToList();
        var dwfAve = nodes.ToDictionary(n => n, n => design.Loads.Where(l => l.Manhole.Equals(n, StringComparison.OrdinalIgnoreCase)).Sum(l => l.QaveGpm), StringComparer.OrdinalIgnoreCase);
        var nodePattern = nodes.ToDictionary(n => n, n => SanitaryDiurnal.Blend(design.Loads.Where(l => l.Manhole.Equals(n, StringComparison.OrdinalIgnoreCase)).Select(l => (l.QaveGpm, (IReadOnlyList<double>)PatternOf(l.Use)))), StringComparer.OrdinalIgnoreCase);
        // Infiltration of each pipe itself (its I/I less the inflow part and less the infiltration of the pipes entering its From manhole), entered at its From manhole.
        var gwi = nodes.ToDictionary(n => n, _ => 0.0, StringComparer.OrdinalIgnoreCase);
        foreach (var x in design.Pipes)
        {
            double own0 = x.IiGpm - x.InflowGpm - design.Pipes.Where(y => y.To.Equals(x.From, StringComparison.OrdinalIgnoreCase)).Sum(y => y.IiGpm - y.InflowGpm);
            gwi[x.From] += Math.Max(0, own0);
        }
        if (design.Pipes.Any(x => x.InflowGpm > 1e-9))
            msgs.Add(CalcMessage.Info("I-WW-002", "The design's steady inflow allowance (gpd/ac) is not added here: the RTK hydrographs model the rainfall-derived inflow. The infiltration allowance (gpd/in-mi) is carried as a constant groundwater infiltration."));
        var rdii = nodes.ToDictionary(n => n, n => areas.GetValueOrDefault(n) > 0 && rtk.Count > 0 ? Rdii(rain, dtH, areas[n], rtk, dtH, steps) : new double[steps], StringComparer.OrdinalIgnoreCase);
        double DwfGpm(string n, double h) => dwfAve[n] * SanitaryDiurnal.At(nodePattern[n], h);
        double RdiiCfs(string n, double h)
        {
            var a = rdii[n]; double k = h / dtH; int i = (int)Math.Floor(k);
            if (i >= steps - 1) return a[^1];
            if (i < 0) return 0;
            return a[i] + (a[i + 1] - a[i]) * (k - i);
        }
        double NodeCfs(string n, double h) => (DwfGpm(n, h) + gwi[n]) / GpmPerCfs + RdiiCfs(n, h);

        // ---- accumulated hydrographs (no attenuation) ----
        var times = Enumerable.Range(0, steps).Select(k => k * dtH).ToArray();
        var upNodes = design.Pipes.ToDictionary(x => x.Name, x => design.Upstream(x).Select(y => y.From).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var sumDwf = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        var sumRdii = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in design.Pipes)
        {
            var ups = upNodes[x.Name];
            sumDwf[x.Name] = times.Select(h => ups.Sum(n => DwfGpm(n, h) + gwi[n])).ToArray();
            sumRdii[x.Name] = times.Select((_, k) => ups.Sum(n => rdii[n][k]) * GpmPerCfs).ToArray();
        }

        // ---- dynamic wave ----
        var net = ToNetwork(design);
        DynamicWaveResult? dw = null;
        double dwDt = 0;
        if (dyn)
        {
            double autoDt = net.Pipes.Min(x => 0.5 * x.Length / (Math.Sqrt(32.174 * x.Rise) + 3));
            dwDt = Math.Clamp(Math.Floor(autoDt * 4) / 4, 0.25, 5);
            var opts = new DynamicWaveOptions { TimeStep = dwDt, Duration = runH * 3600, ReportStep = dtMin * 60, NodeArea = Math.PI * design.ManholeDiameterFt * design.ManholeDiameterFt / 4, JunctionLosses = false };
            dw = new DynamicWave(net, (n, tSec) => gwi.ContainsKey(n) ? NodeCfs(n, tSec / 3600) : 0, opts).Run();
        }

        // ---- tables ----
        var tables = new List<ToolTable>();
        var charts = new List<ToolChart>();
        var nodeRows = new List<IReadOnlyList<object?>>();
        foreach (var n in nodes)
        {
            var pat = nodePattern[n];
            int hp = Array.IndexOf(pat, pat.Max()), hl = Array.IndexOf(pat, pat.Min());
            double rdiiPk = rdii[n].Max() * GpmPerCfs;
            if (dwfAve[n] <= 0 && gwi[n] <= 0 && rdiiPk <= 0) continue;
            nodeRows.Add([n, R(dwfAve[n], 2), dwfAve[n] > 0 ? R(pat.Max(), 2) : null, dwfAve[n] > 0 ? SanitaryDiurnal.Clock(hp) : null, dwfAve[n] > 0 ? R(pat.Min(), 2) : null, dwfAve[n] > 0 ? SanitaryDiurnal.Clock(hl) : null,
                R(gwi[n], 2), R(areas.GetValueOrDefault(n), 2), R(rdiiPk, 1), R(rdii[n].Sum() * dtH * 3600 * 7.48052, 0)]);
        }
        tables.Add(new("Inflows by manhole", ["Manhole", "DWF average (gpm)", "Peak multiplier", "Peak hour", "Low multiplier", "Low hour", "Infiltration (gpm)", "Sewershed (ac)", "RDII peak (gpm)", "RDII volume (gal)"], nodeRows,
            ["DWF = Σ load average flow × the hourly pattern of its land use (the manhole's multipliers are the flow-weighted blend). Infiltration = the design's gpd per inch-mile allowance of the pipe leaving the manhole, constant.",
             $"RDII = Σ R·A·i convolved with the RTK triangles. Storm: {stormName}, starting {stormStart:G3} h after midnight of day 1."]));

        var patRows = Enumerable.Range(0, 24).Select(h => (IReadOnlyList<object?>)[$"{SanitaryDiurnal.Clock(h)}–{SanitaryDiurnal.Clock(h + 1)}", R(patRes[h], 3), R(patCom[h], 3), R(patInd[h], 3), R(patPub[h], 3)]).ToList();
        tables.Add(new("Hourly dry-weather flow patterns", ["Hour", "Residential", "Commercial", "Industrial", "Public"], patRows,
            ["Multipliers of the average daily flow, applied as steps by clock hour (as a SWMM HOURLY pattern); each averages 1.000 so a day's volume is the average daily flow. Replace the defaults with metered data where there is any (D-761)."]));

        tables.Add(new("RDII unit hydrographs (RTK)", ["Response", "R", "T (h)", "K", "Base time (h)", "Peak (gpm per ac per in. of rain in 1 h)"],
            rtk.Select(u => (IReadOnlyList<object?>)[u.Response, u.R, u.T, u.K, R(u.BaseHours, 2), R(u.R * 2 / u.BaseHours * CfsPerAcInPerHr * GpmPerCfs, 2)]).ToList(),
            [$"Total R = {rTotal:G3}: {rTotal * 100:G3} % of the rain on the sewershed enters the sewer. RDII volume = R × rain depth × sewershed area. Triangle peak = 2·R/(T·(1 + K)) per unit rain (EPA SSOAP; SWMM 5 Hydrology Reference Manual Ch. 10)."]));

        var pipeRows = new List<IReadOnlyList<object?>>();
        var mhRows = new List<IReadOnlyList<object?>>();
        int surchargedPipes = 0, overflowing = 0, overDesign = 0;
        foreach (var x in design.Pipes)
        {
            double dFt = x.DiameterIn / 12, qFull = SanitarySewerTool.QFull(dFt, x.N, x.Slope) * GpmPerCfs;
            var dwfS = sumDwf[x.Name]; var rdS = sumRdii[x.Name];
            int kPk = Enumerable.Range(0, steps).MaxBy(k => dwfS[k] + rdS[k]);
            double sumPk = dwfS[kPk] + rdS[kPk], dwfPk = dwfS.Max(), rdPk = rdS.Max();
            double design0 = x.QpeakGpm + x.IiGpm;
            var link = dw?.Links.FirstOrDefault(l => l.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase));
            double peak = link is not null ? link.MaxFlow * GpmPerCfs : sumPk;
            var issues = new List<string>();
            if (link is not null ? link.MaxDepthRatio >= 0.999 : sumPk > SanitarySewerTool.Discharge(0.9382 * dFt, dFt, x.N, x.Slope) * GpmPerCfs)
            {
                issues.Add("surcharged"); surchargedPipes++;
                msgs.Add(CalcMessage.Warning("W-WW-001", $"{x.Name}: surcharges in the design storm (peak {peak:F0} gpm against {qFull:F0} gpm full" + (link is not null ? $", full for {link.FullFlowMinutes / 60:F1} h)." : ").")));
            }
            if (peak > design0 * 1.0 + 1e-6) { issues.Add("above design peak"); overDesign++; }
            pipeRows.Add([x.Name, x.DiameterIn, R(qFull, 0), R(design0, 1), R(dwfPk, 1), R(rdPk, 1), R(sumPk, 1), DayClock(times[kPk]),
                link is not null ? R(peak, 1) : null, link is not null ? DayClock(link.TimeOfMax / 60) : null, link is not null ? R(link.MaxDepthRatio, 2) : null, R(peak / qFull, 2), issues.Count == 0 ? "OK" : string.Join(", ", issues)]);
        }
        tables.Add(new("Pipes: wet-weather peak flows", ["Pipe", "D (in)", "Q full (gpm)", "IDCM design peak (gpm)", "DWF peak hour (gpm)", "RDII peak (gpm)", "DWF + RDII, summed (gpm)", "Time (day hh:mm)",
                dyn ? "Dynamic-wave peak (gpm)" : "Dynamic wave", "Time", "Peak depth / D", "Peak / Q full", "Status"], pipeRows,
            ["Summed = the DWF, infiltration and RDII of every manhole upstream added at the same moment (no storage or travel time), normally at or above the routed peak. The dynamic-wave peak includes storage, travel time and backwater.",
             "IDCM design peak = the sanitary-network Qpeak (Σ Qave × PF × RF) plus its I/I allowance. \"above design peak\": the wet-weather peak exceeds the flow the pipe was sized for."]));
        if (overDesign > 0) msgs.Add(CalcMessage.Warning("W-WW-003", $"{overDesign} pipe(s) carry more in the design storm than the IDCM design peak flow they were sized for; see \"Pipes: wet-weather peak flows\". IDCM 3.12.2 has no wet-weather allowance, so state the RDII basis in the design report."));

        if (dw is not null)
        {
            foreach (var n in dw.Nodes)
            {
                double? fb = n.Rim is { } rim ? rim - n.MaxHead : null;
                double crown = design.Pipes.Where(x => x.From.Equals(n.Name, StringComparison.OrdinalIgnoreCase) || x.To.Equals(n.Name, StringComparison.OrdinalIgnoreCase))
                    .Max(x => (x.From.Equals(n.Name, StringComparison.OrdinalIgnoreCase) ? x.UpInvert : x.DownInvert) + x.DiameterIn / 12);
                bool outfall = !design.Pipes.Any(x => x.From.Equals(n.Name, StringComparison.OrdinalIgnoreCase));
                mhRows.Add([n.Name, n.Rim is { } r0 ? R(r0, 2) : null, R(n.Invert, 2), R(n.MaxHead, 2), DayClock(n.TimeOfMax / 60), R(n.MaxHead - crown, 2), fb is { } f ? R(f, 2) : null, R(n.SurchargeMinutes / 60, 2), R(n.FloodVolume * 7.48052, 0), outfall ? "outlet (free)" : ""]);
                if (n.FloodVolume > 0) { overflowing++; msgs.Add(CalcMessage.Warning("W-WW-002", $"{n.Name} overflows at the rim: {n.FloodVolume * 7.48052:F0} gal over {n.FloodMinutes / 60:F1} h — a sanitary sewer overflow in the design storm.")); }
                else if (fb is { } f2 && f2 < design.MinimumCoverFt && n.MaxHead > crown + 0.005 && !outfall)
                    msgs.Add(CalcMessage.Warning("W-WW-004", $"{n.Name}: the peak HGL is {f2:F2} ft below the rim; basements and services below that level can back up."));
            }
            tables.Add(new("Manholes: peak HGL and overflows (dynamic wave)", ["Manhole", "Rim", "Invert", "Peak HGL", "Time (day hh:mm)", "HGL − crown (ft)", "Rim − HGL (ft)", "Surcharged (h)", "Overflow (gal)", "Note"], mhRows,
                [$"Dynamic wave (St. Venant, SWMM 5 link-node method), Δt = {dwDt:0.##} s, {runH:G3} h from an empty network (the first hours fill it). Surcharged = HGL above the highest pipe crown at the manhole. The outlet is a free outfall: give its HGL by running the downstream system in SWMM."]));
            tables.Add(new("Run and continuity", ["Item", "Value", "Unit"],
            [
                ["Inflow volume", R(dw.InflowVolume * 7.48052, 0), "gal"], ["Outflow volume", R(dw.OutflowVolume * 7.48052, 0), "gal"], ["Overflow volume", R(dw.FloodVolume * 7.48052, 0), "gal"],
                ["Continuity error", R(dw.ContinuityError, 2), "%"], ["Time steps not converged", dw.NonConvergedSteps, ""],
            ]));
            if (Math.Abs(dw.ContinuityError) > 5) msgs.Add(CalcMessage.Warning("W-WW-008", $"Dynamic-wave continuity error {dw.ContinuityError:F1} %: shorten the pipes' Courant step (finer time step) or check the inverts."));
        }

        // ---- charts ----
        var outlets = design.Outlets.ToList();
        foreach (var o in outlets.Take(2))
        {
            var ser = new List<ChartSeries>
            {
                new("DWF + infiltration", times.Select((t, k) => (t, sumDwf[o.Name][k])).ToList()),
                new("RDII", times.Select((t, k) => (t, sumRdii[o.Name][k])).ToList(), Dashed: true),
                new("Total, summed", times.Select((t, k) => (t, sumDwf[o.Name][k] + sumRdii[o.Name][k])).ToList()),
            };
            if (dw is not null && dw.LinkFlows.TryGetValue(o.Name, out var lf)) ser.Add(new("Dynamic wave", dw.Times.Select((t, i) => (t / 60, lf[i] * GpmPerCfs)).ToList()));
            charts.Add(new ToolChart($"Outlet {o.Name} to {o.To}: hydrograph", "Time (h from midnight, day 1)", "Q (gpm)", ser,
                [new ChartLine("IDCM design peak", o.QpeakGpm + o.IiGpm), new ChartLine("Q full", SanitarySewerTool.QFull(o.DiameterIn / 12, o.N, o.Slope) * GpmPerCfs)]));
        }
        charts.Add(new ToolChart("Design storm", "Time (h from midnight, day 1)", "Intensity (in/h)",
            [new ChartSeries(stormName, rain.SelectMany((v, b) => new[] { (b * dtH, v), ((b + 1) * dtH, v) }).ToList())]));
        charts.Add(new ToolChart("Hourly patterns", "Hour", "Multiplier of average flow",
        [
            new("Residential", Steps(patRes)), new("Commercial", Steps(patCom), Dashed: true), new("Public", Steps(patPub)), new("Industrial", Steps(patInd), Dashed: true),
        ]));

        // ---- SWMM file ----
        var swMsgs = new List<CalcMessage>();
        var patterns = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        var dwfList = new List<SwmmDwf>();
        foreach (var n in nodes.Where(n => dwfAve[n] > 0))
        {
            string key = patterns.FirstOrDefault(kv => kv.Value.Zip(nodePattern[n]).All(z => Math.Abs(z.First - z.Second) < 1e-6)).Key ?? $"DWF{patterns.Count + 1}";
            patterns.TryAdd(key, nodePattern[n]);
            dwfList.Add(new SwmmDwf(n, dwfAve[n] / GpmPerCfs, key));
        }
        var san = new SwmmSanitary(dwfList, patterns, nodes.Select(n => (n, gwi[n] / GpmPerCfs)).ToList(), rtk.Select(u => new SwmmRtk(u.Response.ToUpperInvariant(), u.R, u.T, u.K)).ToList(),
            nodes.Where(n => areas.GetValueOrDefault(n) > 0).Select(n => (n, areas[n])).ToList());
        var swOpts = new SwmmExportOptions
        {
            Title = $"MasterFlow sanitary wet-weather run: {stormName}", DurationHours = runH, RoutingStepSec = 5, ReportStepMin = Math.Max(1, Math.Round(dtMin / 3)),
            Inflows = "none", RainIntensity = rain, RainStepMin = dtMin, RainName = stormName, Sanitary = san,
        };
        string inp = SwmmInp.Write(net, null, swOpts, swMsgs);
        msgs.AddRange(swMsgs.Where(m => m.Severity != MessageSeverity.Info));
        msgs.Add(CalcMessage.Info("I-WW-003", "SWMM file: junctions at the manhole rims (overflow at the rim), conduits with the designed sizes and inverts, [DWF] with one HOURLY pattern per land-use mix, the infiltration as [INFLOWS] baselines, [HYDROGRAPHS] RTK1 with the three triangles on rain gage RG1, and [RDII] areas. The outlet is FREE; replace it with the receiving sewer's HGL if it can surcharge."));

        // ---- summary ----
        var summary = new List<string> { $"Design storm {stormName}; RDII {rTotal * 100:G3} % of rain (R = {string.Join(" / ", rtk.Select(u => u.R.ToString("G3", Inv)))}); {(dyn ? "dynamic-wave routing" : "summed hydrographs, no routing")}." };
        foreach (var o in outlets)
        {
            var dwfS = sumDwf[o.Name]; var rdS = sumRdii[o.Name];
            double peak = dw?.Links.FirstOrDefault(l => l.Name == o.Name) is { } lk ? lk.MaxFlow * GpmPerCfs : Enumerable.Range(0, steps).Max(k => dwfS[k] + rdS[k]);
            summary.Add($"Outlet {o.Name}: DWF average {o.QaveGpm + (o.IiGpm - o.InflowGpm):F0} gpm, peak hour {dwfS.Max():F0} gpm; RDII peak {rdS.Max():F0} gpm; wet-weather peak {peak:F0} gpm against the IDCM design peak {o.QpeakGpm + o.IiGpm:F0} gpm (ratio {peak / (o.QpeakGpm + o.IiGpm):F2}).");
        }
        summary.Add(surchargedPipes == 0 && overflowing == 0 ? "No pipe surcharges and no manhole overflows in the design storm." : $"{surchargedPipes} pipe(s) surcharge and {overflowing} manhole(s) overflow in the design storm; see Messages.");
        summary.Add("The SWMM .inp file is offered with the report for a design-storm run in EPA SWMM 5.");

        return new ToolResult(title, summary, tables, msgs)
        {
            Charts = charts,
            Files = [new ToolFile("sanitary-wet-weather.inp", "EPA SWMM 5 input file: sanitary network with DWF patterns and RTK RDII", Encoding.UTF8.GetBytes(inp))],
        };

        static List<(double, double)> Steps(double[] pat) => Enumerable.Range(0, 24).SelectMany(h => new[] { ((double)h, pat[h]), (h + 1.0, pat[h]) }).ToList();
    }

    /// <summary>The designed sanitary network as a storm network (for dynamic-wave routing and SWMM): rims, sizes (ft), inverts, free outfall.</summary>
    public static StormNetwork ToNetwork(SanitaryFlowDesign d) => new()
    {
        Name = "Sanitary sewer",
        Structures = d.Manholes.Select(m => new NetworkStructure { Name = m.Name, Kind = "Manhole", RimElevation = m.Rim, Northing = m.Northing, Easting = m.Easting }).ToList(),
        Pipes = d.Pipes.Select(x => new NetworkPipe { Name = x.Name, From = x.From, To = x.To, Rise = x.DiameterIn / 12, Span = x.DiameterIn / 12, Length = x.Length, UpstreamInvert = x.UpInvert, DownstreamInvert = x.DownInvert, ManningN = x.N, Discharge = double.NaN }).ToList(),
        Tailwater = TailwaterRule.FreeOutfall,
    };

    /// <summary>"day 2 13:30" for hours from midnight of day 1.</summary>
    public static string DayClock(double h) => $"day {(int)Math.Floor(h / 24 + 1e-9) + 1} {SanitaryDiurnal.Clock(h)}";

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
