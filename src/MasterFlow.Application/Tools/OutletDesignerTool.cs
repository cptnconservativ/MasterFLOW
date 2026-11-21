using System.Globalization;
using System.Text.RegularExpressions;
using MasterFlow.Application.WaterQuality;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Multi-stage detention outlet designer (Premium). Sizes, from the bottom up, a water quality orifice plate that drains
/// the WQCV in its target time, then one opening per storm (orifice, slot or overflow weir) so that each routed peak
/// outflow equals its allowable (pre-development) release, picks or checks the outlet pipe, and checks the emergency
/// spillway, freeboard and drain time against Rapid City IDCM §4.10 and the 2009 Storm Water Quality Manual.
/// </summary>
public sealed partial class OutletDesignerTool : IDesignTool
{
    public string Id => "outlet-designer";
    public string Name => "Multi-stage outlet designer (WQCV, 2-, 10-, 100-yr)";
    public string Description => "Designs a detention pond's outlet structure: a WQCV orifice plate (Rapid City SWQM 2009 §2.3.2, 40 h), then a stage for each storm sized so its routed peak equals the allowable release (IDCM §4.10.3–4.10.4: 2-, 10- and 100-yr limited to pre-development), the outlet pipe (18 in. minimum, §4.10.5), and checks of the spillway, 1 ft freeboard and the 72 h drain time. Inflow by the modified rational method or entered hydrographs.";
    public bool UsesNetwork => false;

    private const double G = 32.174, Ft3PerAcFt = 43560;
    private static readonly double[] PipeSizesIn = [18, 21, 24, 27, 30, 33, 36, 42, 48, 54, 60, 66, 72, 78, 84, 90, 96];

    public string SampleInput(DesignToolContext context) => """
        # Multi-stage outlet designer, U.S. units (ft, ft², cfs, ac, min).
        # Inflow: modified rational (area, tc, C per storm, IDCM Table 4-2 intensities, critical duration searched)
        # or [Inflow hydrograph] with Time (min) and one column per storm named like "10-yr (cfs)".
        area = 30            # ac
        tc = 15              # min
        C2 = 0.45
        C10 = 0.55
        C100 = 0.70
        # Allowable release (pre-development peaks, IDCM §4.10.3): allowable Q10 = cfs, or pre-development rational C.
        pre C2 = 0.08
        pre C10 = 0.15
        pre C100 = 0.30
        pre tc = 25          # min, pre-development time of concentration (default: tc)

        # Water quality capture volume (SWQM 2009 §2.2.2): from imperviousness, or wqcv = ac-ft.
        impervious = 55      # % of the tributary area
        wq drain time = 40   # h (extended detention basin, SWQM §2.3.2)
        sediment allowance = 20    # % of WQCV (SWQM: storage 120 % of the WQCV above the lowest row)
        row spacing = 4      # in.

        top of berm = 5208.0
        time step = 2        # min

        [Stage storage]
        Elevation (ft)	Area (ft²)
        5200.0	4000
        5201.0	12000
        5202.0	18000
        5203.0	24000
        5204.0	30000
        5205.0	36000
        5206.0	42000
        5207.0	48000
        5208.0	54000

        [Stages]
        # One stage per storm, bottom up. Type: orifice (circular, diameter sized), slot (rectangular, Height ft fixed, width sized)
        # or weir (crest length sized). Invert blank: the previous storm's peak water level (the WQ plate top for the first stage).
        Storm	Type	Invert (ft)	Height (ft)	C
        2	orifice		 	0.6
        10	slot		0.5	0.6
        100	weir		 	3.0

        [Outlet pipe]
        # Leave Diameter blank to pick the smallest standard size (18 in. minimum) that does not throttle the stages.
        Invert (ft)	Length (ft)	n	Outlet invert (ft)	Ke	Diameter (in)
        5199.5	80	0.013	5198.7	0.5

        [Spillway]
        Crest (ft)	Bottom width (ft)	Side slope	C
        5206.3	40	4	2.6
        """;

    private sealed record StageSpec(int Year, string Type, double? Invert, double Height, double C);
    private sealed record StormIn(string Label, int Year, Func<Pond, (Pond.RouteResult R, List<double> Hyd, string Note)> Route, double PeakInflow)
    {
        /// <summary>Peak-only routing for sizing (drain time not followed).</summary>
        public Func<Pond, Pond.RouteResult> Fast { get; init; } = p => Route(p).R;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Multi-stage outlet design — WQCV plate and storm stages";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-OD-001", "The outlet designer works in U.S. units (IDCM Table 4-2, SWQM equations in inches and acres). Switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double dtMin = p.Number("time step", 1), dt = dtMin * 60;
        if (!(dtMin is > 0 and <= 10)) msgs.Add(CalcMessage.Error("E-OD-002", "time step must be between 0 and 10 min."));

        // ---- storage ----
        var stages = new List<(double E, double A)>();
        foreach (var r in p.Rows("Stage storage"))
            if (p.Cell(r, 0, "elevation", "Stage") is { } e && p.Cell(r, 1, "area", $"Stage {r[0]}") is { } a)
            {
                if (stages.Count > 0 && e <= stages[^1].E) { msgs.Add(CalcMessage.Error("E-OD-003", "Stage elevations must increase.")); break; }
                if (a < 0) { msgs.Add(CalcMessage.Error("E-OD-003", "Stage areas cannot be negative.")); break; }
                stages.Add((e, a));
            }
        if (stages.Count < 2) msgs.Add(CalcMessage.Error("E-OD-004", "[Stage storage] needs at least two rows: Elevation (ft), Area (ft²)."));

        // ---- stage specs ----
        var specs = new List<StageSpec>();
        foreach (var r in p.Rows("Stages"))
        {
            string w = $"Stage {(r.Length > 0 ? r[0] : "?")}";
            if (!int.TryParse((r.Length > 0 ? r[0] : "").Replace("-yr", "").Trim(), out int y) || y <= 0) { msgs.Add(CalcMessage.Error("E-OD-005", $"{w}: Storm must be a return period (2, 10, 100).")); continue; }
            string type = (r.Length > 1 ? r[1] : "orifice").Trim().ToLowerInvariant();
            if (type is not ("orifice" or "slot" or "weir")) { msgs.Add(CalcMessage.Error("E-OD-006", $"{w}: type must be orifice, slot or weir.")); continue; }
            double? inv = p.Cell(r, 2, "invert", w), ht = p.Cell(r, 3, "height", w), c = p.Cell(r, 4, "C", w);
            if (type == "slot" && ht is not > 0) { msgs.Add(CalcMessage.Error("E-OD-007", $"{w}: a slot needs its Height (ft); its width is sized.")); continue; }
            specs.Add(new(y, type, inv, ht ?? 0, c ?? (type == "weir" ? 3.0 : 0.6)));
        }
        if (specs.Count == 0) specs = [new(2, "orifice", null, 0, 0.6), new(10, "slot", null, 0.5, 0.6), new(100, "weir", null, 0, 3.0)];
        specs = specs.OrderBy(s => s.Year).ToList();
        if (specs.Select(s => s.Year).Distinct().Count() != specs.Count) msgs.Add(CalcMessage.Error("E-OD-005", "Each storm may have one stage only."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        Pond MakePond(IReadOnlyList<PondOutlet> o) => new(stages.Select(s => (s.E, s.A)).ToList(), o, G, true);
        var empty = MakePond([new PondOutlet("none", "weir", stages[^1].E + 100, 1, 0, 3)]);
        double bottom = empty.Bottom, top = empty.Top;
        double LevelAt(double v) { double lo = bottom, hi = top; for (int i = 0; i < 100; i++) { double m = 0.5 * (lo + hi); if (empty.Storage(m) < v) lo = m; else hi = m; } return 0.5 * (lo + hi); }

        // ---- storms (inflow) ----
        double area = p.Number("area", double.NaN), tc = p.Number("tc", double.NaN);
        var storms = new List<StormIn>();
        var allow = new Dictionary<int, (double Q, string Basis)>();
        bool rational = p.Rows("Inflow hydrograph").Count == 0;
        if (rational)
        {
            if (!(area > 0) || !(tc >= 5)) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-OD-008", "Modified rational inflow needs area (ac) > 0 and tc ≥ 5 min, or give an [Inflow hydrograph].")]);
            double preTc = p.Number("pre tc", tc);
            foreach (var s in specs)
            {
                double c = p.Number($"C{s.Year}", double.NaN);
                if (!(c is > 0 and <= 1)) { msgs.Add(CalcMessage.Error("E-OD-009", $"C{s.Year} must be between 0 and 1.")); continue; }
                if (double.IsNaN(RapidCityRational.Intensity(s.Year, tc))) { msgs.Add(CalcMessage.Error("E-OD-010", $"IDCM Table 4-2 has no {s.Year}-yr storm; use an [Inflow hydrograph].")); continue; }
                double y = s.Year, tcv = tc;
                storms.Add(new($"{s.Year}-yr", s.Year, pond => RouteRational(pond, c, area, tcv, (int)y, dt, true), c * RapidCityRational.Intensity(s.Year, tc) * area) { Fast = pond => RouteRational(pond, c, area, tcv, (int)y, dt, false).Item1 });
                if (p.OptionalNumber($"pre C{s.Year}") is { } pc)
                    allow[s.Year] = (pc * RapidCityRational.Intensity(s.Year, preTc) * area, $"pre-development rational, C {pc:G3}, tc {preTc:G3} min: {pc:G3} × {RapidCityRational.Intensity(s.Year, preTc):F2} in/h × {area:G4} ac");
            }
        }
        else
        {
            var head = p.Header("Inflow hydrograph");
            for (int c = 1; c < head.Count; c++)
            {
                var m = YearRx().Match(head[c]);
                if (!m.Success) { msgs.Add(CalcMessage.Error("E-OD-011", $"Hydrograph column \"{head[c]}\": name it like \"10-yr (cfs)\".")); continue; }
                int y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var pts = new List<(double, double)>();
                foreach (var r in p.Rows("Inflow hydrograph"))
                    if (p.Cell(r, 0, "time", "Hydrograph") is { } t && p.Cell(r, c, "flow", $"Hydrograph {head[c]}") is { } q) pts.Add((t, q));
                var hyd = Hydrograph.Resample(pts, dtMin);
                storms.Add(new($"{y}-yr", y, pond => (pond.Route(hyd, dt), hyd, "entered hydrograph"), hyd.Max()) { Fast = pond => pond.Route(hyd, dt, drainLimitHours: 3) });
            }
        }
        foreach (var s in specs)
        {
            if (p.OptionalNumber($"allowable Q{s.Year}") is { } aq) allow[s.Year] = (aq, "entered");
            if (!allow.ContainsKey(s.Year)) msgs.Add(CalcMessage.Error("E-OD-012", $"Give allowable Q{s.Year} = (cfs) or pre C{s.Year} = for the {s.Year}-yr stage (IDCM §4.10.3: pre-development peak)."));
            if (!storms.Any(x => x.Year == s.Year)) msgs.Add(CalcMessage.Error("E-OD-013", $"No inflow for the {s.Year}-yr storm."));
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        // ---- WQCV plate ----
        double wqDrain = p.Number("wq drain time", 40), sed = p.Number("sediment allowance", 20) / 100, spacing = p.Number("row spacing", 4) / 12;
        double wqcvFt3;
        string wqBasis;
        if (p.OptionalNumber("wqcv") is { } wq) { wqcvFt3 = wq * Ft3PerAcFt; wqBasis = $"entered {wq:G4} ac-ft"; }
        else if (p.OptionalNumber("impervious") is { } imp && area > 0)
        {
            double a = Wq.RapidCityDrainCoefficient(wqDrain), inches = Wq.WqcvRapidCity(imp / 100, a);
            wqcvFt3 = inches / 12 * area * Ft3PerAcFt;
            wqBasis = $"SWQM §2.2.2: 1.16 × {a:G3} × (0.91 I³ − 1.191 I² + 0.78 I), I = {imp / 100:G3} → {inches:F3} in over {area:G4} ac";
        }
        else { wqcvFt3 = 0; wqBasis = "none (no water quality stage)"; msgs.Add(CalcMessage.Info("I-OD-001", "No impervious or wqcv given: no water quality plate; the first stage starts at the pond bottom.")); }

        var outlets = new List<PondOutlet>();
        var schedule = new List<IReadOnlyList<object?>>();
        var checks = new List<IReadOnlyList<object?>>();
        void Check(string item, string req, object? val, bool ok, string reference, bool warnOnly = true)
        {
            checks.Add([item, req, val, ok ? "OK" : "NO", reference]);
            if (!ok) msgs.Add(warnOnly ? CalcMessage.Warning("W-OD-001", $"{item}: {req} not met ({DesignTools.Cell(val)}). {reference}.") : CalcMessage.Error("E-OD-020", $"{item}: {req} not met."));
        }
        double wqTop = bottom;
        OrificePlate? plate = null;
        if (wqcvFt3 > 0)
        {
            double vStore = wqcvFt3 * (1 + sed);
            if (vStore >= empty.MaxStorage) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-OD-014", $"The WQCV plus sediment allowance ({vStore / Ft3PerAcFt:F3} ac-ft) does not fit in the pond ({empty.MaxStorage / Ft3PerAcFt:F3} ac-ft).")]);
            wqTop = LevelAt(vStore);
            plate = new OrificePlate(empty.Storage, bottom, top, wqTop, spacing, 0.6, G);
            double rowArea = plate.Size(wqDrain, 1 / (1 + sed));
            double dHole = Math.Sqrt(4 * rowArea / Math.PI) * 12;
            double half = plate.DrainHours(0.5 / (1 + sed));
            foreach (var (z, k) in plate.Rows.Select((z, k) => (z, k)))
                outlets.Add(new PondOutlet($"WQ row {k + 1}", "orifice", z - Math.Sqrt(4 * rowArea / Math.PI) / 2, Math.Sqrt(4 * rowArea / Math.PI), 0, 0.6));
            schedule.Add(["WQCV", "orifice plate", bottom, wqTop, $"{plate.Rows.Count} rows at {spacing * 12:G3} in., {rowArea * 144:F3} in² per row (one {dHole:F2} in. hole, or several of the same total)", $"{wqcvFt3 / Ft3PerAcFt:F3} ac-ft in {wqDrain:G3} h", wqBasis]);
            Check("WQCV drain time", $"{wqDrain:G3} h for the WQCV (SWQM §2.3.2)", plate.DrainHours(1 / (1 + sed)), true, "Plate sized exactly to it");
            Check("WQCV half drain", "≥ 12 h for 50 % of the WQCV (SWQM §2.3.2)", half, half >= 12 - 1e-6, "Increase the drain time or the row spacing");
            if (dHole < 0.25) msgs.Add(CalcMessage.Warning("W-OD-002", $"WQ plate holes of {dHole:F2} in. are below 1/4 in.; use fewer rows (larger spacing) or a well screen (SWQM §2.3.2 charts start at 1/4 in.)."));
        }

        // ---- storm stages, bottom up ----
        var results = new Dictionary<int, (Pond.RouteResult R, List<double> Hyd)>();
        double prevLevel = wqTop;
        foreach (var s in specs)
        {
            var storm = storms.First(x => x.Year == s.Year);
            double allowQ = allow[s.Year].Q;
            double inv = s.Invert ?? prevLevel;
            if (inv < bottom) inv = bottom;
            PondOutlet Make(double size) => s.Type switch
            {
                "orifice" => new PondOutlet($"{s.Year}-yr orifice", "orifice", inv, size, 0, s.C),
                "slot" => new PondOutlet($"{s.Year}-yr slot", "orifice", inv, size, s.Height, s.C),
                _ => new PondOutlet($"{s.Year}-yr weir", "weir", inv, size, 0, s.C),
            };
            var baseRes = storm.Route(MakePond([.. outlets, Make(1e-6)]));
            if (!baseRes.R.Overtopped && baseRes.R.PeakOutflow > allowQ * 1.005) msgs.Add(CalcMessage.Warning("W-OD-008", $"{s.Year}-yr: the lower stages alone release {baseRes.R.PeakOutflow:F2} cfs, above the allowable {allowQ:F2} cfs."));
            if (!baseRes.R.Overtopped && baseRes.R.PeakWse <= inv + 1e-6)
            {
                schedule.Add([$"{s.Year}-yr", "none", inv, null, $"the {s.Year}-yr level {baseRes.R.PeakWse:F2} ft stays below this invert", $"{allowQ:F2} cfs", allow[s.Year].Basis]);
                msgs.Add(CalcMessage.Info("I-OD-003", $"{s.Year}-yr: the water peaks at {baseRes.R.PeakWse:F2} ft, below this stage's invert {inv:F2} ft, releasing {baseRes.R.PeakOutflow:F2} cfs (allowable {allowQ:F2}). Lower the invert to release more and store less."));
                results[s.Year] = (baseRes.R, baseRes.Hyd);
                continue;
            }
            // Size so that no storm from this one up releases more than its allowable with the stages so far: for each
            // storm j ≥ this one, the size whose routed peak equals allowable j (peak outflow rises with size); the stage
            // takes the smallest. Higher stages then fill the remaining room for the larger storms.
            double hi = double.PositiveInfinity; int governs = s.Year;
            foreach (var sj in specs.Where(x => x.Year >= s.Year))
            {
                var stj = storms.First(x => x.Year == sj.Year);
                double aj = allow[sj.Year].Q;
                Pond.RouteResult Peak(double size) => stj.Fast(MakePond([.. outlets, Make(size)]));
                var r0 = Peak(1e-6);
                if (!r0.Overtopped && r0.PeakOutflow >= aj) { hi = 1e-6; governs = sj.Year; break; }
                double lo = 1e-6, h = s.Type == "weir" ? 2 : 0.5;
                int guard = 0;
                while ((Peak(h) is var rh) && (rh.Overtopped || rh.PeakOutflow < aj) && guard++ < 10) h *= 2;
                if (guard >= 10)
                {
                    if (sj.Year == s.Year) { msgs.Add(CalcMessage.Error("E-OD-016", $"{s.Year}-yr: no {s.Type} size releases {allowQ:F2} cfs before the pond overtops its top stage ({top:F2} ft). Enlarge the pond (add stages) or raise the allowable.")); hi = double.NaN; break; }
                    continue;   // this larger storm never reaches its allowable through this stage: no limit from it
                }
                for (int it = 0; it < 40 && h - lo > 5e-4 * h; it++)
                {
                    double m = 0.5 * (lo + h);
                    var r = Peak(m);
                    if (r.Overtopped || r.PeakOutflow < aj) lo = m; else h = m;
                }
                if (h < hi) { hi = h; governs = sj.Year; }
            }
            if (double.IsNaN(hi)) break;
            if (hi <= 1e-6)
            {
                var rn = storm.Route(MakePond(outlets));
                results[s.Year] = (rn.R, rn.Hyd); prevLevel = Math.Max(prevLevel, rn.R.PeakWse);
                schedule.Add([$"{s.Year}-yr", "none", inv, null, $"lower stages release the {governs}-yr allowable", $"{allowQ:F2} cfs", allow[s.Year].Basis]);
                msgs.Add(governs == s.Year && rn.R.PeakOutflow <= allowQ * 1.005
                    ? CalcMessage.Info("I-OD-005", $"{s.Year}-yr: the lower stages already release the allowable {allowQ:F2} cfs (they were sized to the larger storm's limit); no {s.Year}-yr opening is needed.")
                    : CalcMessage.Warning("W-OD-007", $"{s.Year}-yr stage: the lower stages alone release at least the {governs}-yr allowable ({allow[governs].Q:F2} cfs); no {s.Year}-yr opening is added. Shrink the lower stages or raise their inverts."));
                continue;
            }
            if (governs != s.Year)
                msgs.Add(CalcMessage.Info("I-OD-004", $"{s.Year}-yr stage: sized by the {governs}-yr allowable ({allow[governs].Q:F2} cfs), which a larger opening would exceed; the {s.Year}-yr release is below its own allowable."));
            var stage = Make(hi);
            outlets.Add(stage);
            var res = storm.Route(MakePond(outlets));
            results[s.Year] = (res.R, res.Hyd);
            string size = s.Type switch
            {
                "orifice" => $"{hi * 12:F2} in. diameter",
                "slot" => $"{hi * 12:F2} in. wide × {s.Height * 12:F1} in. high",
                _ => $"crest {hi:F2} ft long",
            };
            schedule.Add([$"{s.Year}-yr", s.Type, inv, s.Type == "slot" ? inv + s.Height : s.Type == "orifice" ? inv + hi : (double?)null, size, $"{allowQ:F2} cfs", allow[s.Year].Basis + $"; C = {s.C:G3}"]);
            prevLevel = res.R.PeakWse;
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], schedule.Count > 0 ? [new("Outlet schedule so far", ["Stage", "Type", "Invert (ft)", "Top (ft)", "Size", "Target", "Basis"], schedule)] : [], msgs);

        // ---- outlet pipe ----
        var pr = p.Rows("Outlet pipe");
        PondOutlet? barrel = null;
        double peak100Wse = results.Values.Max(r => r.R.PeakWse);
        if (pr.Count > 0)
        {
            var r = pr[0];
            double? inv = p.Cell(r, 0, "invert", "Outlet pipe"), len = p.Cell(r, 1, "length", "Outlet pipe"), n = p.Cell(r, 2, "n", "Outlet pipe"),
                outInv = p.Cell(r, 3, "outlet invert", "Outlet pipe"), ke = p.Cell(r, 4, "Ke", "Outlet pipe"), dia = p.Cell(r, 5, "diameter", "Outlet pipe");
            if (inv is null || len is not > 0) msgs.Add(CalcMessage.Error("E-OD-017", "[Outlet pipe]: invert and length are required."));
            else
            {
                PondOutlet Barrel(double dIn) => new("Outlet pipe", "pipe", inv.Value, dIn / 12, 0, ke ?? 0.5, len.Value, n ?? 0.013, outInv ?? double.NaN);
                bool Throttles(double dIn)
                {
                    var pd = MakePond([.. outlets, Barrel(dIn)]);
                    var free = MakePond(outlets);
                    for (double e = bottom; e <= peak100Wse + 1e-9; e += Math.Max(0.05, (peak100Wse - bottom) / 60))
                        if (pd.PrincipalOutflow(e) < free.PrincipalOutflow(e) * 0.999) return true;
                    return false;
                }
                double chosen;
                if (dia is { } d0)
                {
                    chosen = d0;
                    Check("Outlet pipe diameter", "≥ 18 in. (IDCM §4.10.5)", d0, d0 >= 18, "IDCM §4.10.5");
                    if (Throttles(d0)) msgs.Add(CalcMessage.Warning("W-OD-003", $"The {d0:G3} in. outlet pipe limits the stages below the 100-yr level; the routed results below include its control. A larger pipe lets the stages control as designed."));
                }
                else
                {
                    chosen = PipeSizesIn.FirstOrDefault(d => !Throttles(d));
                    if (chosen == 0) { chosen = PipeSizesIn[^1]; msgs.Add(CalcMessage.Warning("W-OD-004", "Even a 96 in. pipe throttles the stages; check the pipe invert and length.")); }
                    msgs.Add(CalcMessage.Info("I-OD-002", $"Outlet pipe: {chosen:G3} in., the smallest standard size from 18 in. (IDCM §4.10.5) whose HDS-5 capacity does not throttle the stages up to the 100-yr level."));
                }
                barrel = Barrel(chosen);
                schedule.Add(["Outlet pipe", "pipe (barrel)", inv.Value, null, $"{chosen:G3} in., {len:G4} ft, n {n ?? 0.013:G3}", "", "IDCM §4.10.5: RCP, 18 in. minimum; HDS-5 inlet/outlet control, free outfall"]);
            }
        }
        else msgs.Add(CalcMessage.Warning("W-OD-005", "No [Outlet pipe]: the stages discharge freely. IDCM §4.10.5 requires an outlet pipe of at least 18 in."));

        // ---- spillway ----
        double? berm = p.OptionalNumber("top of berm");
        var sp = p.Rows("Spillway");
        var final = new List<PondOutlet>(outlets);
        if (barrel is not null) final.Add(barrel);
        if (sp.Count > 0)
        {
            var r = sp[0];
            double? crest = p.Cell(r, 0, "crest", "Spillway"), bw = p.Cell(r, 1, "width", "Spillway"), z = p.Cell(r, 2, "side slope", "Spillway"), c = p.Cell(r, 3, "C", "Spillway");
            if (crest is null || bw is not > 0) msgs.Add(CalcMessage.Error("E-OD-018", "[Spillway]: crest and bottom width are required."));
            else final.Add(new PondOutlet("Emergency spillway", "spillway", crest.Value, bw.Value, z ?? 4, c ?? 2.6));
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);
        var pond = MakePond(final);

        // ---- final routing and checks ----
        var routeRows = new List<IReadOnlyList<object?>>();
        var charts = new List<ToolChart>();
        int color = 0;
        var hydroSeries = new List<ChartSeries>();
        Pond.RouteResult? r100 = null; double in100 = 0;
        foreach (var s in specs)
        {
            var storm = storms.First(x => x.Year == s.Year);
            var (rr, hyd, note) = storm.Route(pond);
            var steps = new List<RouteStep>();
            rr = pond.Route(hyd, dt, steps: steps);
            double a = allow[s.Year].Q;
            routeRows.Add([$"{s.Year}-yr", rr.PeakInflow, rr.PeakOutflow, a, rr.PeakWse, rr.PeakStorage / Ft3PerAcFt, rr.PeakSpillway, rr.DrainHours, note]);
            Check($"{s.Year}-yr release", $"≤ {a:F2} cfs (IDCM §4.10.3)", rr.PeakOutflow, !rr.Overtopped && rr.PeakOutflow <= a * 1.005, "Pre-development peak");
            if (rr.PeakSpillway > 0.01) msgs.Add(CalcMessage.Warning("W-OD-006", $"{s.Year}-yr: the emergency spillway flows ({rr.PeakSpillway:F2} cfs); raise its crest above the {s.Year}-yr level ({rr.PeakWse:F2} ft)."));
            if (s.Year == specs[^1].Year) { r100 = rr; in100 = rr.PeakInflow; }
            int every = Math.Max(1, steps.Count / 400);
            var pts = steps.Where((x, i) => i % every == 0 && x.TimeMin <= Math.Max(steps.Where(q => q.Inflow > 0).Select(q => q.TimeMin).DefaultIfEmpty(60).Max() * 3, 60)).ToList();
            hydroSeries.Add(new ChartSeries($"{s.Year}-yr inflow", pts.Select(x => (x.TimeMin, x.Inflow)).ToList(), Dashed: true, Color: color));
            hydroSeries.Add(new ChartSeries($"{s.Year}-yr outflow", pts.Select(x => (x.TimeMin, x.Outflow)).ToList(), Color: color));
            color++;
        }
        if (r100 is { } top100)
        {
            if (berm is { } b)
            {
                Check("Freeboard, 100-yr", "≥ 1 ft from the 100-yr level to the top of berm (IDCM §4.10.3)", b - top100.PeakWse, b - top100.PeakWse >= 1 - 1e-6, "IDCM §4.10.3(2)");
                if (pond.Outlets.Any(o => o.Type == "spillway"))
                {
                    double spW = DetentionTool.SpillwayWse(pond, in100);
                    Check("Freeboard, spillway", "≥ 1 ft above the spillway passing the 100-yr peak inflow (IDCM §4.10.4(4))", b - spW, b - spW >= 1 - 1e-6, $"Spillway alone at {in100:F1} cfs: WSE {spW:F2} ft");
                }
            }
            Check("Drain time", "≤ 72 h after the 100-yr inflow ends (IDCM §4.10.5)", top100.DrainHours, top100.DrainHours <= 72, "Principal outlets");
        }

        // rating table and chart
        var rating = new List<IReadOnlyList<object?>>();
        var ratingPts = new List<(double, double)>();
        int nr = 30;
        for (int i = 0; i <= nr; i++)
        {
            double e = bottom + (top - bottom) * i / nr;
            var row = new List<object?> { e, pond.Storage(e) / Ft3PerAcFt };
            double plateQ = outlets.Where(o => o.Name.StartsWith("WQ row")).Sum(o => o.Flow(e, G));
            row.Add(plateQ);
            foreach (var s in outlets.Where(o => !o.Name.StartsWith("WQ row"))) row.Add(s.Flow(e, G));
            row.Add(barrel is null ? null : pond.BarrelAt(e));
            row.Add(pond.SpillwayOutflow(e));
            row.Add(pond.Outflow(e));
            rating.Add(row);
            ratingPts.Add((pond.Outflow(e), e));
        }
        var ratingHead = new List<string> { "Elevation (ft)", "Storage (ac-ft)", "WQ plate (cfs)" };
        ratingHead.AddRange(outlets.Where(o => !o.Name.StartsWith("WQ row")).Select(o => $"{o.Name} (cfs)"));
        ratingHead.AddRange(["Pipe capacity (cfs)", "Spillway (cfs)", "Total (cfs)"]);

        var lines = new List<ChartLine>();
        if (plate is not null) lines.Add(new("WQCV + sediment", wqTop));
        foreach (var s in specs) if (results.TryGetValue(s.Year, out var rs)) lines.Add(new($"{s.Year}-yr", rs.R.PeakWse));
        if (berm is { } bb) lines.Add(new("Top of berm", bb));
        charts.Add(new ToolChart("Stage–discharge rating", "Outflow (cfs)", "Elevation (ft)", [new ChartSeries("Total outflow", ratingPts)], lines));
        charts.Add(new ToolChart("Inflow and outflow hydrographs", "Time (min)", "Flow (cfs)", hydroSeries));
        if (plate is not null)
        {
            var dd = plate.Drawdown(wqDrain * 1.5, 0.25);
            charts.Add(new ToolChart("WQCV drawdown (plate only)", "Time (h)", "Elevation (ft)", [new ChartSeries("Water level", dd.Select(x => (x.Hours, x.Elev)).ToList())],
                [new ChartLine($"Level when the WQCV has drained ({wqDrain:G3} h)", LevelAt(wqcvFt3 * sed))]));
        }

        var figure = StructureFigure(outlets, plate, spacing, barrel, bottom, top, berm, lines);
        var summary = new List<string>
        {
            $"Pond {bottom:F2}–{top:F2} ft, {empty.MaxStorage / Ft3PerAcFt:F3} ac-ft; WQCV {wqcvFt3 / Ft3PerAcFt:F3} ac-ft ({wqBasis.Split(':')[0]}), plate top {wqTop:F2} ft.",
            "Stages: " + string.Join("; ", schedule.Select(r => $"{r[0]} {r[1]} {r[4]}")) + ".",
            string.Join("; ", routeRows.Select(r => $"{r[0]}: out {DesignTools.Cell(r[2])} cfs (allowed {DesignTools.Cell(r[3])}), WSE {((double)r[4]!):F2} ft")) + ".",
        };
        return new ToolResult(title, summary,
        [
            new("Outlet schedule", ["Stage", "Type", "Invert (ft)", "Top (ft)", "Size", "Target", "Basis"], schedule,
                ["Each storm stage is sized by bisection so the routed peak outflow (storage indication, all lower stages in place) equals the target.", "Orifices: Q = C A √(2g h) to the centroid, weir flow below the top; weirs Q = C L h^1.5."]),
            new("Routing results", ["Storm", "Peak in (cfs)", "Peak out (cfs)", "Allowable (cfs)", "Peak WSE (ft)", "Storage (ac-ft)", "Spillway (cfs)", "Drain (h)", "Inflow"], routeRows,
                [rational ? "Modified rational: IDCM Table 4-2 intensities, trapezoidal hydrographs, the duration giving the highest peak outflow (5-min steps to 180 min)." : "Entered hydrographs, resampled to the time step."]),
            new("Criteria checks", ["Item", "Requirement", "Value", "Result", "Reference"], checks),
            new("Stage–storage–discharge", ratingHead, rating),
        ], msgs) { Charts = charts, Figures = [figure] };
    }

    private static (Pond.RouteResult, List<double>, string) RouteRational(Pond pond, double c, double area, double tc, int year, double dt, bool full)
    {
        Pond.RouteResult? best = null; List<double>? bestHyd = null; double bestTd = tc; int falling = 0;
        var durations = new SortedSet<double> { tc };
        for (double d = 5 * Math.Ceiling(tc / 5); d <= 180; d += 5) durations.Add(d);
        foreach (double td in durations)
        {
            double q = c * RapidCityRational.Intensity(year, td) * area;
            int n = (int)Math.Round((td + tc) * 60 / dt);
            var hyd = Enumerable.Range(0, n + 1).Select(k => { double t = k * dt / 60; return t <= tc ? q * t / tc : t <= td ? q : Math.Max(0, q * (td + tc - t) / tc); }).ToList();
            var rr = pond.Route(hyd, dt, drainLimitHours: 2);
            if (rr.Overtopped) return (rr, hyd, $"critical duration {td:G4} min (overtops)");
            if (best is null || rr.PeakOutflow > best.PeakOutflow + 1e-9) { best = rr; bestHyd = hyd; bestTd = td; falling = 0; }
            else if (!full && ++falling >= 4) break;   // peak outflow vs duration is single-peaked; stop 4 durations past it when sizing
        }
        return (full ? pond.Route(bestHyd!, dt) : best!, bestHyd!, $"modified rational, critical duration {bestTd:G4} min");
    }

    /// <summary>Front view of the outlet structure: wall, plate holes, stage openings, weir crest and the design water levels.</summary>
    private static ToolFigure StructureFigure(List<PondOutlet> outlets, OrificePlate? plate, double spacing, PondOutlet? barrel, double bottom, double top, double? berm, List<ChartLine> levels)
    {
        var series = new List<ToolFigureSeries>();
        double width = Math.Max(4, outlets.Where(o => o.Type == "weir").Select(o => o.Size + 1).DefaultIfEmpty(0).Max());
        double wallTop = outlets.Where(o => o.Type == "weir").Select(o => o.Invert).DefaultIfEmpty(top).Min();
        double baseE = Math.Min(bottom, barrel?.Invert ?? bottom) - 0.5;
        series.Add(new("Structure", ToolSeriesKind.Ground, [(0, baseE), (0, wallTop), (width, wallTop), (width, baseE), (0, baseE)]));
        double x = 0.6;
        foreach (var o in outlets.Where(o => o.Type == "orifice"))
        {
            bool round = o.Height <= 0;
            double w = round ? o.Size : o.Size, h = round ? o.Size : o.Height;
            double cx = o.Name.StartsWith("WQ row") ? 0.4 : (x += Math.Max(0.6, w + 0.3)) - w / 2;
            if (round) series.Add(new(o.Name, ToolSeriesKind.Ground, Enumerable.Range(0, 17).Select(k => (cx + w / 2 * Math.Cos(k * Math.PI / 8), o.Invert + h / 2 + h / 2 * Math.Sin(k * Math.PI / 8))).ToList()));
            else series.Add(new(o.Name, ToolSeriesKind.Ground, [(cx - w / 2, o.Invert), (cx - w / 2, o.Invert + h), (cx + w / 2, o.Invert + h), (cx + w / 2, o.Invert), (cx - w / 2, o.Invert)]));
        }
        if (barrel is not null)
            series.Add(new("Outlet pipe", ToolSeriesKind.Ground, Enumerable.Range(0, 17).Select(k => (width / 2 + barrel.Size / 2 * Math.Cos(k * Math.PI / 8), barrel.Invert + barrel.Size / 2 + barrel.Size / 2 * Math.Sin(k * Math.PI / 8))).ToList()));
        foreach (var l in levels) series.Add(new(l.Label, ToolSeriesKind.Limit, [(-1, l.Y), (width + 1, l.Y)]));
        return new ToolFigure("Outlet structure, front view (schematic)", series, "Width (ft)", "Elevation (ft)",
            [plate is null ? "No water quality plate." : $"WQ plate: {plate.Rows.Count} rows at {spacing * 12:G3} in. from {bottom:F2} ft (holes drawn to scale at the left edge).", "Weir crest = top of the structure walls; levels are routed peak water surfaces."]);
    }

    [GeneratedRegex(@"(\d+)\s*-?\s*(yr|year)", RegexOptions.IgnoreCase)]
    private static partial Regex YearRx();
}
