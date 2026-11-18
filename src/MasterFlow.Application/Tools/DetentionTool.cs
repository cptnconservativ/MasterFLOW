using System.Text.RegularExpressions;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Detention pond routing to Rapid City IDCM 2012 §4.10: stage–storage from contours, an elevation–volume table or basin
/// geometry; multi-stage outlet works (orifices, weirs, v-notches, risers, outlet pipe, emergency spillway); modified
/// rational inflow (critical duration) or entered inflow hydrographs; storage-indication routing; and the §4.10 criteria.
/// </summary>
public sealed partial class DetentionTool : IDesignTool
{
    public string Id => "detention-routing";
    public string Name => "Detention pond routing (IDCM §4.10)";
    public string Description => "Stage–storage from contours (conic or average end area), an elevation–volume table or basin geometry; outlet works with orifices, weirs, v-notches, risers (weir/orifice/pipe control), the outlet pipe and an emergency spillway; modified rational inflow (IDCM Table 4-2, critical duration) or your own inflow hydrographs; storage-indication (modified Puls) routing; and the §4.10 checks: peak outflow not above pre-development for the 2-, 10- and 100-yr storms, freeboard, 72 h drain, 18 in. outlet pipe and riser, 4H:1V side slopes, bottom slopes, trickle channel.";
    public bool UsesNetwork => false;

    private const double G = 32.174, Ft3PerAcFt = 43560;

    public string SampleInput(DesignToolContext context) => """
        # Detention pond routing, U.S. units (ft, ft², cfs, ac, min).
        # Inflow: "rational" builds modified rational hydrographs from IDCM Table 4-2 (critical duration);
        # "hydrograph" routes the hydrographs under [Inflow hydrograph] instead (see the tool doc).
        inflow = rational
        area = 12           # contributing area, ac
        C2 = 0.45
        C10 = 0.55
        C100 = 0.65
        tc = 15             # min
        time step = 1       # routing step, min
        # Allowed peak outflow (pre-development peaks), cfs
        allowable Q2 = 4
        allowable Q10 = 8
        allowable Q100 = 16
        top of berm = 107.0  # for the freeboard check
        # Optional checks (§4.10.4): graded side slope (H:V), bottom slopes (%), trickle channel capacity (cfs)
        side slope = 4
        longitudinal slope = 0.5
        cross slope = 2
        trickle capacity = 1.5

        # Stage storage: Elevation and Area (contours; storage method = conic or average end area), or Elevation and
        # Volume (ft³, or ac-ft when the header says ac-ft). Or replace this table with a [Basin] section:
        #   bottom elevation, bottom length, bottom width, side slope, top elevation.
        storage method = conic
        [Stage storage]
        Elevation (ft)	Area (ft²)
        100.0	6000
        101.0	9000
        102.0	12500
        103.0	16500
        104.0	21000
        105.0	26000
        106.5	34000
        107.0	37000

        # Type: orifice (Size = D, or width with Height), weir (Size = length), v-notch (Size = angle, deg), riser (crest at
        # Invert; Size = D, or width with Height for a box; C = weir C, Orifice C for the top opening), pipe (outlet barrel:
        # Size = D, Length, n, outlet invert; C = Ke), spillway (emergency: Size = bottom width, Height = side slope z; C ≈ 2.6).
        # Orifices, weirs, v-notches and risers add; the pipe limits their total; the spillway adds beside it.
        [Outlets]
        Name	Type	Invert (ft)	Size (ft)	Height (ft)	C	Length (ft)	n	Outlet invert (ft)	Orifice C
        Low-flow orifice	orifice	100.0	0.75		0.6
        Mid orifice	orifice	102.2	1.0	0.5	0.6
        Riser	riser	104.0	3.0		3.0				0.6
        Outlet pipe	pipe	99.5	2.0		0.5	80	0.013	98.7
        Emergency spillway	spillway	105.0	20	4	2.6
        """;

    private sealed record Storm(string Label, int? Year, IReadOnlyList<double> Inflow, double PeakInflow, double? DurationMin, Pond.RouteResult Result, List<RouteStep> Steps);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Detention pond routing — IDCM 2012 §4.10";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-DT-001", "Detention routing works in U.S. units: IDCM Table 4-2 intensities, weir coefficients (C 3.0, 2.5, 2.6) and the §4.10 criteria (1 ft, 18 in.) are U.S. values. Switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        // ---- inflow mode ----
        bool hasTable = p.Rows("Inflow hydrograph").Count > 0;
        string mode = (p.Text("inflow") ?? (hasTable ? "hydrograph" : "rational")).ToLowerInvariant();
        if (mode is not ("rational" or "hydrograph")) msgs.Add(CalcMessage.Error("E-DT-012", $"inflow = \"{mode}\": use rational or hydrograph."));
        bool rational = mode == "rational";
        double dtMin = p.Number("time step", 1);
        if (!(dtMin >= 0.1 && dtMin <= 10)) msgs.Add(CalcMessage.Error("E-DT-013", "Time step must be between 0.1 and 10 min."));
        double area = double.NaN, tc = double.NaN;
        if (rational)
        {
            area = p.Number("area", double.NaN); tc = p.Number("tc", double.NaN);
            if (!(area > 0) || !(tc >= 5)) msgs.Add(CalcMessage.Error("E-DT-002", "Area (ac) must be > 0 and tc at least 5 min."));
        }
        if (!rational && !hasTable) msgs.Add(CalcMessage.Error("E-DT-014", "inflow = hydrograph needs an [Inflow hydrograph] table: Time (min) then one flow column (cfs) per storm."));

        // ---- storage ----
        Pond? pond = null;
        var outlets = ParseOutlets(p, msgs);
        bool hasBasin = p.HasSection("Basin"), hasStages = p.Rows("Stage storage").Count > 0;
        if (hasBasin && hasStages) msgs.Add(CalcMessage.Error("E-DT-015", "Give either [Stage storage] or [Basin], not both."));
        var method = (p.Text("storage method") ?? "conic").ToLowerInvariant().Replace("-", " ") switch
        {
            "conic" or "frustum" => StorageMethod.Conic,
            "average end area" or "average end" or "aea" => StorageMethod.AverageEndArea,
            var x => (StorageMethod?)null,
        };
        if (method is null) msgs.Add(CalcMessage.Error("E-DT-016", $"storage method = \"{p.Text("storage method")}\": use conic or average end area."));
        Func<IReadOnlyList<PondOutlet>, Pond>? makePond = null;
        if (hasBasin)
        {
            double be = p.Number("Basin.bottom elevation", double.NaN), bl = p.Number("Basin.bottom length", double.NaN), bw = p.Number("Basin.bottom width", double.NaN),
                   z = p.Number("Basin.side slope", double.NaN), te = p.Number("Basin.top elevation", double.NaN);
            if (!double.IsFinite(be) || !double.IsFinite(te) || !(te > be)) msgs.Add(CalcMessage.Error("E-DT-017", "[Basin]: bottom elevation and a higher top elevation are required."));
            else if (!(bl > 0) || !(bw > 0) || !(z >= 0)) msgs.Add(CalcMessage.Error("E-DT-017", "[Basin]: bottom length and width must be > 0 and side slope (H:V) ≥ 0."));
            else { var b = new BasinGeometry(be, bl, bw, z, te); makePond = o => Pond.FromBasin(b, o, G, true); }
        }
        else
        {
            var head = p.Header("Stage storage");
            int volCol = head.Select((h, i) => (h, i)).Where(x => x.i > 0 && Regex.IsMatch(x.h, "vol|stor", RegexOptions.IgnoreCase)).Select(x => x.i).DefaultIfEmpty(-1).First();
            bool acft = volCol > 0 && Regex.IsMatch(head[volCol], @"ac[\s-]*ft|acre", RegexOptions.IgnoreCase);
            int col = volCol > 0 ? volCol : 1;
            var rows = new List<(double, double)>();
            foreach (var r in p.Rows("Stage storage"))
                if (p.Cell(r, 0, "elevation", "Stage row") is { } e && p.Cell(r, col, volCol > 0 ? "volume" : "area", $"Stage {r[0]}") is { } v)
                {
                    if (rows.Count > 0 && e <= rows[^1].Item1) { msgs.Add(CalcMessage.Error("E-DT-003", $"Stage elevations must increase ({e:G6} follows {rows[^1].Item1:G6}).")); break; }
                    if (v < 0) { msgs.Add(CalcMessage.Error("E-DT-003", $"Stage {e:G6}: {(volCol > 0 ? "volume" : "area")} cannot be negative.")); break; }
                    if (volCol > 0 && rows.Count > 0 && v * (acft ? Ft3PerAcFt : 1) < rows[^1].Item2) { msgs.Add(CalcMessage.Error("E-DT-003", $"Stage {e:G6}: volume must not decrease with elevation.")); break; }
                    rows.Add((e, v * (acft ? Ft3PerAcFt : 1)));
                }
            if (rows.Count < 2) msgs.Add(CalcMessage.Error("E-DT-004", "At least two rows are needed under [Stage storage] (Elevation, Area or Volume), or give a [Basin]."));
            else if (volCol > 0)
            {
                if (rows[0].Item2 > 1e-9) msgs.Add(CalcMessage.Error("E-DT-003", "The first row of an elevation–volume table must have zero volume (the pond bottom)."));
                makePond = o => Pond.FromVolumes(rows, o, G, true);
            }
            else if (method is { } m) makePond = o => new Pond(rows, o, G, true, m);
        }

        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error) || makePond is null) return new ToolResult(title, [], [], msgs);
        pond = makePond(outlets);
        foreach (var o in outlets.Where(o => o.Type != "pipe" && o.Invert < pond.Bottom - 1e-9))
            msgs.Add(CalcMessage.Warning("W-DT-007", $"{o.Name}: invert {o.Invert:F2} is below the pond bottom {pond.Bottom:F2}; flow is computed from its invert, but storage starts at the bottom."));
        foreach (var o in outlets.Where(o => o.Type != "pipe" && o.Invert >= pond.Top - 1e-9))
            msgs.Add(CalcMessage.Warning("W-DT-008", $"{o.Name}: invert {o.Invert:F2} is at or above the top stage {pond.Top:F2}; it never flows."));

        double dt = dtMin * 60;
        var storms = new List<Storm>();
        if (rational) RunRational(p, pond, area, tc, dt, storms, msgs);
        else RunHydrographs(p, pond, dtMin, storms, msgs);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error && m.Code != "E-DT-011")) return new ToolResult(title, [], [], msgs);

        // ---- criteria ----
        var checks = new List<IReadOnlyList<object?>>();
        void Check(string item, string req, object? value, string result, string reference) => checks.Add([item, req, value, result, reference]);
        foreach (var s in storms)
        {
            double? allow = s.Year is { } y ? p.OptionalNumber($"allowable Q{y}") : null;
            if (s.Result.Overtopped) { Check($"{s.Label} peak outflow", allow is { } a0 ? $"≤ {a0:G4} cfs (pre-development)" : "≤ pre-development", null, "FAILS (overtops)", "§4.10.3(2), §4.10.4"); continue; }
            if (allow is { } al)
            {
                bool ok = s.Result.PeakOutflow <= al + 1e-9;
                Check($"{s.Label} peak outflow", $"≤ {al:G4} cfs (pre-development)", Math.Round(s.Result.PeakOutflow, 2), ok ? "OK" : "FAILS", "§4.10.3(2), §4.10.4");
                if (!ok) msgs.Add(CalcMessage.Warning("W-DT-001", $"{s.Label} peak outflow {s.Result.PeakOutflow:F2} cfs exceeds the pre-development {al:G4} cfs (§4.10.4)."));
            }
            else Check($"{s.Label} peak outflow", "≤ pre-development", Math.Round(s.Result.PeakOutflow, 2), "NOT CHECKED (enter allowable Q" + (s.Year?.ToString() ?? "") + ")", "§4.10.3(2), §4.10.4");
        }
        var s100 = storms.FirstOrDefault(s => s.Year == 100);
        if (s100 is null && storms.Count > 0) msgs.Add(CalcMessage.Info("I-DT-004", "No 100-yr storm was routed: the freeboard, drain-time and trickle-channel checks need one."));
        double? berm = p.OptionalNumber("top of berm");
        var spillways = outlets.Where(o => o.Type == "spillway").ToList();
        double? spillQ = null, spillWse = null;
        if (s100 is { Result.Overtopped: false } r100)
        {
            double wse100 = r100.Result.PeakWse;
            if (spillways.Count > 0)
            {
                spillQ = p.OptionalNumber("spillway design Q") ?? r100.PeakInflow;
                spillWse = SpillwayWse(pond, spillQ.Value);
            }
            double governing = Math.Max(wse100, spillWse ?? double.NegativeInfinity), required = governing + 1;
            string basis = spillWse is { } sw
                ? $"1 ft above the greater of the routed 100-yr WSE {wse100:F2} and the spillway WSE {sw:F2} at {spillQ:G4} cfs → top ≥ {required:F2}"
                : $"1 ft above the routed 100-yr WSE {wse100:F2} → top ≥ {required:F2}";
            if (berm is { } bm)
            {
                bool ok = bm >= required - 1e-9;
                Check("Freeboard", basis, Math.Round(bm - governing, 2), ok ? "OK" : "FAILS", spillways.Count > 0 ? "§4.10.3(2), §4.10.4(4)" : "§4.10.3(2)");
                if (!ok) msgs.Add(CalcMessage.Warning("W-DT-004", $"Freeboard is {bm - governing:F2} ft below the top of berm {bm:F2}; at least 1 ft is required ({basis})."));
            }
            else
            {
                Check("Freeboard", basis, null, "NOT CHECKED (enter top of berm)", "§4.10.3(2), §4.10.4(4)");
                msgs.Add(CalcMessage.Info("I-DT-003", $"Enter top of berm to check freeboard; required top of berm {required:F2}."));
            }
            if (spillways.Count > 0) msgs.Add(CalcMessage.Info("I-DT-005", $"Emergency spillway design flow {spillQ:G4} cfs (default: the 100-yr peak inflow, i.e. principal outlet blocked; set spillway design Q to change). The spillway alone passes it at {spillWse:F2}."));
            if (spillways.Count > 0 && r100.Result.PeakSpillway > 1e-6) msgs.Add(CalcMessage.Info("I-DT-006", $"The emergency spillway flows during the routed 100-yr storm (peak {r100.Result.PeakSpillway:F2} cfs)."));

            double drain = r100.Result.DrainHours;
            Check("Drain time after the 100-yr storm", "≤ 72 h by gravity through the principal outlets", double.IsNaN(drain) ? null : Math.Round(drain, 1), double.IsNaN(drain) ? "FAILS (does not drain)" : drain <= 72 ? "OK" : "FAILS", "§4.10.5");
            if (double.IsNaN(drain)) msgs.Add(CalcMessage.Warning("W-DT-002", "The pond does not drain within 7 days after the 100-yr storm (is the lowest outlet above the pond bottom?). IDCM §4.10.5 requires drain in 72 h."));
            else if (drain > 72) msgs.Add(CalcMessage.Warning("W-DT-003", $"Drain time after the 100-yr storm is {drain:F1} h, more than the 72 h allowed (§4.10.5)."));

            double trickleReq = 0.02 * r100.PeakInflow;
            if (p.OptionalNumber("trickle capacity") is { } tcap)
                Check("Trickle channel capacity", $"≥ 2 % of the 100-yr peak inflow = {trickleReq:F2} cfs, slope ≥ 0.2 %", tcap, tcap >= trickleReq - 1e-9 ? "OK" : "FAILS", "§4.10.4(3)");
            else Check("Trickle channel capacity", $"≥ 2 % of the 100-yr peak inflow = {trickleReq:F2} cfs, slope ≥ 0.2 %", null, "NOT CHECKED (enter trickle capacity)", "§4.10.4(3)");
            msgs.Add(CalcMessage.Info("I-DT-001", $"Inlet protection is designed for the inflow with no pond storage (§4.10.4(5)): 100-yr peak inflow {r100.PeakInflow:F2} cfs."));
        }

        if (pond.Barrel is { } pipe)
        {
            bool ok = pipe.Size >= 1.5 - 1e-9;
            Check("Outlet pipe diameter", "≥ 18 in.", Math.Round(pipe.Size * 12, 1), ok ? "OK" : "FAILS", "§4.10.5");
            if (!ok) msgs.Add(CalcMessage.Warning("W-DT-005", $"Outlet pipe {pipe.Size * 12:F0} in. is below the 18 in. minimum (§4.10.5)."));
            msgs.Add(CalcMessage.Info("I-DT-002", "Outlet pipe capacity: FHWA HDS-5 controlling headwater, the larger of inlet control (concrete pipe, square edge with headwall: unsubmerged, transition and submerged equations) and outlet control with a free outfall (ho = (dc + D)/2). The same engine as the Culvert analysis tool. Orifices, weirs and risers are taken as free-discharging (no tailwater)."));
        }
        else Check("Outlet pipe diameter", "≥ 18 in.", null, "NOT CHECKED (no pipe outlet)", "§4.10.5");
        foreach (var riser in outlets.Where(o => o.Type == "riser"))
        {
            double minDim = riser.Height > 0 ? Math.Min(riser.Size, riser.Height) : riser.Size;
            bool ok = minDim >= 1.5 - 1e-9;
            Check($"{riser.Name}: riser size", "≥ 18 in.", Math.Round(minDim * 12, 1), ok ? "OK" : "FAILS", "§4.10.5");
            if (!ok) msgs.Add(CalcMessage.Warning("W-DT-009", $"{riser.Name}: the riser is {minDim * 12:F0} in.; the minimum riser is 18 in. (§4.10.5)."));
            bool drawdown = outlets.Any(o => o.Type is "orifice" or "v-notch" && o.Invert <= pond.Bottom + 1e-9);
            Check($"{riser.Name}: drawdown", "a drawdown orifice at the pond bottom", null, drawdown ? "OK" : "FAILS", "§4.10.5");
            if (!drawdown) msgs.Add(CalcMessage.Warning("W-DT-010", $"{riser.Name}: no orifice or v-notch at the pond bottom; a riser needs a drawdown pipe that drains the facility completely (§4.10.5)."));
            if (pond.Barrel is { } b && riser.RiserArea > Math.PI * b.Size * b.Size / 4 * 1.0001)
                msgs.Add(CalcMessage.Info("I-DT-007", $"{riser.Name}: weir control C·P·h^1.5, orifice control {riser.OrificeC:G3}·A·√(2gh) over the top opening, and pipe control by {b.Name}; the least governs at each stage (§4.10.5)."));
        }
        if (outlets.Where(o => o.IsPrincipalInlet).Min(o => o.Invert) > pond.Bottom + 1e-9)
            msgs.Add(CalcMessage.Warning("W-DT-006", "The lowest principal outlet is above the pond bottom: the storage below it does not drain by gravity (§4.10.5)."));

        double? side = p.OptionalNumber("side slope") ?? pond.Basin?.SideSlope;
        if (side is { } zz)
        {
            bool ok = zz >= 4 - 1e-9;
            Check("Graded side slopes", "4H:1V or flatter", $"{zz:G3}H:1V", ok ? "OK" : "FAILS (needs City Engineer approval)", "§4.10.4(2)");
            if (!ok) msgs.Add(CalcMessage.Warning("W-DT-011", $"Side slopes {zz:G3}H:1V are steeper than 4H:1V; they need City Engineer approval and erosion protection (§4.10.4(2))."));
        }
        else Check("Graded side slopes", "4H:1V or flatter", null, "NOT CHECKED (enter side slope)", "§4.10.4(2)");
        foreach (var (key, label, min) in new[] { ("longitudinal slope", "Bottom slope, longitudinal", 0.5), ("cross slope", "Bottom slope, cross", 2.0) })
        {
            if (p.OptionalNumber(key) is { } sl)
            {
                bool ok = sl >= min - 1e-9;
                Check(label, $"≥ {min:0.0} %", sl, ok ? "OK" : "FAILS", "§4.10.4(1)");
                if (!ok) msgs.Add(CalcMessage.Warning("W-DT-012", $"{label} {sl:G3} % is below the {min:0.0} % minimum (§4.10.4(1))."));
            }
            else Check(label, $"≥ {min:0.0} %", null, $"NOT CHECKED (enter {key})", "§4.10.4(1)");
        }
        foreach (var sp in spillways.Where(o => o.Height > 0 && o.Height < 4 - 1e-9))
        {
            Check($"Spillway {sp.Name} side slopes", "4H:1V or flatter (earth channel)", $"{sp.Height:G3}H:1V", "FAILS", "§4.10.6");
            msgs.Add(CalcMessage.Warning("W-DT-013", $"Spillway {sp.Name}: side slopes {sp.Height:G3}H:1V are steeper than 4H:1V for an excavated earth channel (§4.10.6); ignore for a structural spillway."));
        }
        msgs.Add(CalcMessage.Info("I-DT-008", "Check whether the embankment and storage make the facility a dam under the Administrative Rules of South Dakota; dams are designed to state criteria (§4.10.3(1), §4.10.4)."));

        var allowables = storms.Where(s => s.Year is not null).Select(s => (s.Label, Q: p.OptionalNumber($"allowable Q{s.Year}"))).Where(x => x.Q is not null).ToDictionary(x => x.Label, x => x.Q!.Value);
        return BuildResult(title, pond, outlets, storms, rational, tc, berm, allowables, checks, msgs);
    }

    private static List<PondOutlet> ParseOutlets(ToolInput p, List<CalcMessage> msgs)
    {
        var outlets = new List<PondOutlet>();
        foreach (var r in p.Rows("Outlets"))
        {
            string name = r[0], type = (r.Length > 1 ? r[1] : "").ToLowerInvariant().Replace("vnotch", "v-notch").Replace("v notch", "v-notch"), where = $"Outlet {name}";
            double? inv = p.Cell(r, 2, "invert", where), size = p.Cell(r, 3, "size", where), ht = p.Cell(r, 4, "height", where), cc = p.Cell(r, 5, "C", where),
                    len = p.Cell(r, 6, "length", where), nn = p.Cell(r, 7, "n", where), oinv = p.Cell(r, 8, "outlet invert", where), oc = p.Cell(r, 9, "orifice C", where);
            if (!PondOutlet.Types.Contains(type)) { msgs.Add(CalcMessage.Error("E-DT-005", $"{where}: type must be one of {string.Join(", ", PondOutlet.Types)}.")); continue; }
            if (inv is null || size is not > 0) { msgs.Add(CalcMessage.Error("E-DT-006", $"{where}: invert and a size greater than 0 are required.")); continue; }
            if (ht is < 0) { msgs.Add(CalcMessage.Error("E-DT-006", $"{where}: height cannot be negative.")); continue; }
            if (cc is <= 0 || oc is <= 0) { msgs.Add(CalcMessage.Error("E-DT-006", $"{where}: coefficients must be greater than 0.")); continue; }
            if (type == "pipe" && (len is not > 0 || nn is not > 0)) { msgs.Add(CalcMessage.Error("E-DT-007", $"{where}: the outlet pipe needs length and n greater than 0.")); continue; }
            if (type == "v-notch" && size is not (> 0 and < 180)) { msgs.Add(CalcMessage.Error("E-DT-006", $"{where}: v-notch angle must be between 0 and 180 degrees.")); continue; }
            if ((type == "orifice" && cc is > 1) || oc is > 1) { msgs.Add(CalcMessage.Error("E-DT-006", $"{where}: an orifice coefficient must be at most 1.")); continue; }
            double c = cc ?? (type switch { "weir" or "riser" => 3.0, "v-notch" => 2.5, "spillway" => 2.6, "orifice" => 0.6, _ => 0.5 });
            outlets.Add(new PondOutlet(name, type, inv.Value, size.Value, ht ?? 0, c, len ?? 0, nn ?? 0.013, oinv ?? double.NaN, oc ?? 0.6));
        }
        if (!outlets.Any(o => o.IsPrincipalInlet)) msgs.Add(CalcMessage.Error("E-DT-008", "At least one orifice, weir, v-notch or riser is needed under [Outlets]."));
        if (outlets.Count(o => o.Type == "pipe") > 1) msgs.Add(CalcMessage.Error("E-DT-009", "Only one outlet pipe (barrel) is supported."));
        if (outlets.FirstOrDefault(o => o.Type == "pipe") is { } b && !double.IsNaN(b.OutletInvert) && b.OutletInvert > b.Invert + 1e-9)
            msgs.Add(CalcMessage.Error("E-DT-018", $"Outlet {b.Name}: outlet invert {b.OutletInvert:F2} is above the inlet invert {b.Invert:F2}; the pipe must fall toward its outlet."));
        return outlets;
    }

    private static void RunRational(ToolInput p, Pond pond, double area, double tc, double dt, List<Storm> storms, List<CalcMessage> msgs)
    {
        foreach (int storm in new[] { 2, 10, 100 })
        {
            double c = p.Number($"C{storm}", double.NaN);
            if (!(c > 0 && c <= 1)) { msgs.Add(CalcMessage.Error("E-DT-010", $"C{storm} must be between 0 and 1.")); continue; }
            Pond.RouteResult? best = null; double bestTd = 0, bestQ = 0; List<double>? bestHyd = null;
            var durations = new SortedSet<double> { tc };
            for (double d = 5 * Math.Ceiling(tc / 5); d <= 180; d += 5) durations.Add(d);
            foreach (double td in durations)
            {
                double q = c * RapidCityRational.Intensity(storm, td) * area;
                int n = (int)Math.Round((td + tc) * 60 / dt);
                var hyd = Enumerable.Range(0, n + 1).Select(k =>
                {
                    double t = k * dt / 60;
                    return t <= tc ? q * t / tc : t <= td ? q : Math.Max(0, q * (td + tc - t) / tc);
                }).ToList();
                var rr = pond.Route(hyd, dt, drainLimitHours: 2);   // enough to pass the peak; the drain time is found below
                if (rr.Overtopped) { best = rr; bestTd = td; bestQ = q; bestHyd = hyd; break; }
                if (best is null || rr.PeakWse > best.PeakWse) { best = rr; bestTd = td; bestQ = q; bestHyd = hyd; }
            }
            if (best is null || bestHyd is null) continue;
            var steps = new List<RouteStep>();
            best = pond.Route(bestHyd, dt, steps: steps);
            if (best.Overtopped)
                msgs.Add(CalcMessage.Error("E-DT-011", $"{storm}-yr: the pond fills above the top stage ({pond.Top:F2}) for a {bestTd:F0}-min storm. Extend the stage–storage table or enlarge the outlets."));
            storms.Add(new Storm($"{storm}-yr", storm, bestHyd, bestQ, bestTd, best, steps));
        }
    }

    [GeneratedRegex(@"(\d+)\s*-?\s*(yr|year)", RegexOptions.IgnoreCase)]
    private static partial Regex YearPattern();

    private static void RunHydrographs(ToolInput p, Pond pond, double dtMin, List<Storm> storms, List<CalcMessage> msgs)
    {
        var head = p.Header("Inflow hydrograph");
        if (head.Count < 2) { msgs.Add(CalcMessage.Error("E-DT-014", "[Inflow hydrograph] needs a header: Time (min), then one column per storm such as 2-yr (cfs).")); return; }
        if (Regex.IsMatch(head[0], @"\bh(ou)?rs?\b|\(h\)", RegexOptions.IgnoreCase))
            msgs.Add(CalcMessage.Error("E-DT-019", $"[Inflow hydrograph]: the time column \"{head[0]}\" looks like hours; enter time in minutes."));
        if (head.Skip(1).Any(h => Regex.IsMatch(h, @"m³/s|m3/s|cms|l/s", RegexOptions.IgnoreCase)))
            msgs.Add(CalcMessage.Error("E-DT-019", "[Inflow hydrograph]: flows must be in cfs (U.S. units)."));
        var series = Enumerable.Range(1, head.Count - 1).Select(_ => new List<(double, double)>()).ToList();
        double lastT = double.NegativeInfinity;
        foreach (var r in p.Rows("Inflow hydrograph"))
        {
            if (p.Cell(r, 0, "time", "Hydrograph row") is not { } t) continue;
            if (t < 0 || t <= lastT) { msgs.Add(CalcMessage.Error("E-DT-020", $"[Inflow hydrograph]: times must start at 0 or later and increase ({t:G6} after {lastT:G6}).")); return; }
            lastT = t;
            for (int c = 1; c < head.Count; c++)
                if (p.Cell(r, c, "flow", $"Hydrograph t = {t:G6}") is { } q)
                {
                    if (q < 0) { msgs.Add(CalcMessage.Error("E-DT-020", $"[Inflow hydrograph] {head[c]} at {t:G6} min: flow cannot be negative.")); return; }
                    series[c - 1].Add((t, q));
                }
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return;
        for (int c = 1; c < head.Count; c++)
        {
            var pts = series[c - 1];
            string label = head[c];
            int? year = YearPattern().Match(label) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
            if (year is { } y) label = $"{y}-yr";
            if (pts.Count < 2) { msgs.Add(CalcMessage.Error("E-DT-021", $"{head[c]}: at least two hydrograph points are needed.")); continue; }
            if (pts.Max(x => x.Item2) <= 0) { msgs.Add(CalcMessage.Error("E-DT-021", $"{head[c]}: every flow is zero.")); continue; }
            if (pts[^1].Item2 > 0.05 * pts.Max(x => x.Item2))
                msgs.Add(CalcMessage.Warning("W-DT-014", $"{label}: the hydrograph ends at {pts[^1].Item2:G4} cfs, more than 5 % of its peak; inflow is taken to stop at {pts[^1].Item1:G6} min. Extend the table through the recession."));
            if (pts[0].Item1 > 0) msgs.Add(CalcMessage.Info("I-DT-009", $"{label}: the hydrograph starts at {pts[0].Item1:G6} min; inflow rises linearly from 0 at t = 0."));
            var hyd = Hydrograph.Resample(pts, dtMin);
            double v0 = Hydrograph.Volume(pts), v1 = Hydrograph.Volume(hyd, dtMin * 60);
            if (Math.Abs(v1 - v0) > 0.01 * v0)
                msgs.Add(CalcMessage.Warning("W-DT-015", $"{label}: resampling to {dtMin:G3}-min steps changes the inflow volume by {(v1 - v0) / v0:P1}; use a time step that falls on the table's times."));
            var steps = new List<RouteStep>();
            var rr = pond.Route(hyd, dtMin * 60, steps: steps);
            if (rr.Overtopped)
                msgs.Add(CalcMessage.Error("E-DT-011", $"{label}: the pond fills above the top stage ({pond.Top:F2}). Extend the stage–storage table or enlarge the outlets."));
            storms.Add(new Storm(label, year, hyd, pts.Max(x => x.Item2), null, rr, steps));
        }
        if (storms.Count > 0 && !storms.Any(s => s.Year is not null))
            msgs.Add(CalcMessage.Info("I-DT-010", "Name hydrograph columns like \"100-yr (cfs)\" so each can be checked against its allowable Q."));
    }

    /// <summary>Water level at which the emergency spillway(s) alone pass <paramref name="q"/> (no storage, principal outlets blocked).</summary>
    public static double SpillwayWse(Pond pond, double q)
    {
        double lo = pond.Outlets.Where(o => o.Type == "spillway").Min(o => o.Invert), hi = lo + 1;
        while (pond.SpillwayOutflow(hi) < q && hi < lo + 1000) hi = lo + 2 * (hi - lo);
        for (int i = 0; i < 80; i++) { double m = 0.5 * (lo + hi); if (pond.SpillwayOutflow(m) < q) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    private static ToolResult BuildResult(string title, Pond pond, IReadOnlyList<PondOutlet> outlets, List<Storm> storms, bool rational, double tc,
        double? berm, Dictionary<string, double> allowables, List<IReadOnlyList<object?>> checks, List<CalcMessage> msgs)
    {
        var inlets = outlets.Where(o => o.Type != "pipe").ToList();
        // Stage–storage–discharge at the stage rows and every 0.5 ft.
        var elevs = pond.Stages.Select(s => s.Elev).Concat(Enumerable.Range(0, (int)Math.Floor((pond.Top - pond.Bottom) / 0.5 + 1e-9) + 1).Select(i => pond.Bottom + 0.5 * i))
            .Concat(inlets.Select(o => o.Invert).Where(e => e > pond.Bottom && e < pond.Top))
            .Select(e => Math.Round(e, 6)).Distinct().Order().ToList();
        var sTable = elevs.Select(e => (IReadOnlyList<object?>)[e, pond.Storage(e), pond.Storage(e) / Ft3PerAcFt, pond.Outflow(e),
            .. inlets.Select(o => (object?)o.Flow(e, G)), .. (pond.Barrel is { } b ? new object?[] { pond.BarrelAt(e) } : [])]).ToList();
        var sHead = new List<string> { "Elevation (ft)", "Storage (ft³)", "Storage (ac-ft)", "Total outflow (cfs)" };
        sHead.AddRange(inlets.Select(o => $"{o.Name} (cfs)"));
        if (pond.Barrel is { } bb) sHead.Add($"{bb.Name} capacity (cfs)");

        var rows = storms.Select(s => (IReadOnlyList<object?>)(s.Result.Overtopped
            ? [s.Label, s.DurationMin, s.PeakInflow, null, null, null, null, null, null, "overtops"]
            : [s.Label, s.DurationMin, s.PeakInflow, s.Result.PeakOutflow, s.Result.PeakWse, s.Result.PeakStorage / Ft3PerAcFt, s.Result.TimeOfPeakOutflow / 60,
               s.Result.PeakSpillway, s.Result.DrainHours, s.PeakInflow > 0 ? $"{1 - s.Result.PeakOutflow / s.PeakInflow:P0}" : ""])).ToList();
        string inflowNote = rational
            ? $"Modified rational inflow: Q = C·i(Td)·A for storm durations Td from tc ({tc:G4} min) to 180 min; rises over tc, holds to Td, falls over tc. i from IDCM Table 4-2. The critical duration gives the highest water level."
            : "Inflow hydrographs as entered, resampled to the routing step by linear interpolation.";
        var tables = new List<ToolTable>
        {
            new("Routing results", ["Storm", "Critical duration (min)", "Peak inflow (cfs)", "Peak outflow (cfs)", "Peak WSE (ft)", "Storage used (ac-ft)", "Time of peak outflow (min)", "Peak spillway flow (cfs)", "Drain time (h)", "Peak reduction"], rows,
            [
                inflowNote,
                $"Storage-indication (modified Puls, level-pool) routing: 2S₂/Δt + O₂ = I₁ + I₂ + 2S₁/Δt − O₁, Δt = {(storms.Count > 0 && storms[0].Steps.Count > 1 ? storms[0].Steps[1].TimeMin : 1):G3} min, pond empty at t = 0. Drain time is from the end of inflow to 0.1 % of the peak storage.",
            ]),
            new("Criteria checks (Rapid City IDCM 2012 §4.10)", ["Item", "Requirement", "Value", "Result", "Reference"], checks),
            new("Stage–storage–discharge", sHead, sTable, [pond.StorageBasis, "Orifices below their crown flow as weirs: Q = (2/3) C √(2g) B h^1.5. Risers: least of weir control C·P·h^1.5 and orifice control Co·A·√(2gh). The outlet pipe limits the principal outlets; the emergency spillway, Q = C (L h^1.5 + 0.8 z h^2.5), adds beside it."]),
        };
        tables.Add(new("Outlet works", ["Name", "Type", "Invert (ft)", "Size", "Height / side slope", "C", "Length (ft)", "n", "Outlet invert (ft)", "Orifice C"],
            outlets.Select(o => (IReadOnlyList<object?>)[o.Name, o.Type, o.Invert, o.Size, o.Height > 0 ? o.Height : null, o.Coefficient,
                o.Type == "pipe" ? o.Length : null, o.Type == "pipe" ? o.N : null, o.Type == "pipe" && !double.IsNaN(o.OutletInvert) ? o.OutletInvert : null, o.Type == "riser" ? o.OrificeC : null]).ToList()));

        var charts = new List<ToolChart>();
        foreach (var s in storms.Where(s => !s.Result.Overtopped))
        {
            var shown = Window(s.Steps);
            int every = Math.Max(1, (int)Math.Ceiling(shown.Count / 60.0));
            int peakIdx = shown.FindIndex(x => Math.Abs(x.TimeMin * 60 - s.Result.TimeOfPeakOutflow) < 1e-6);
            var tRows = shown.Where((x, i) => i % every == 0 || i == peakIdx || i == shown.Count - 1)
                .Select(x => (IReadOnlyList<object?>)[x.TimeMin, x.Inflow, x.Outflow, x.Wse, x.Storage / Ft3PerAcFt, x.Spillway]).ToList();
            tables.Add(new($"Hydrograph {s.Label}" + (s.DurationMin is { } d ? $" (Td {d:G4} min)" : ""), ["Time (min)", "Inflow (cfs)", "Outflow (cfs)", "WSE (ft)", "Storage (ac-ft)", "Spillway (cfs)"], tRows,
                [every > 1 ? $"Every {every} routing steps shown, plus the peak outflow; the routing used every step." : "Every routing step shown."]));
            double? allow = allowables.TryGetValue(s.Label, out var al) ? al : null;
            charts.Add(new ToolChart($"Inflow and outflow hydrographs, {s.Label}", "Time (min)", "Flow (cfs)",
                [new("Inflow", shown.Select(x => (x.TimeMin, x.Inflow)).ToList(), Dashed: true, Color: 0), new("Outflow", shown.Select(x => (x.TimeMin, x.Outflow)).ToList(), Color: 1)],
                allow is null ? null : [new ChartLine("Allowable", allow.Value)]));
        }
        var ok = storms.Where(s => !s.Result.Overtopped).ToList();
        if (ok.Count > 0)
        {
            var lines = new List<ChartLine>();
            if (berm is { } bm) lines.Add(new("Top of berm", bm));
            foreach (var sp in outlets.Where(o => o.Type == "spillway")) lines.Add(new($"{sp.Name} crest", sp.Invert));
            charts.Add(new ToolChart("Water surface elevation", "Time (min)", "Elevation (ft)",
                ok.Select((s, i) => new ChartSeries(s.Label, Window(s.Steps).Select(x => (x.TimeMin, x.Wse)).ToList(), Color: i)).ToList(), lines));
        }
        var fine = Enumerable.Range(0, 201).Select(i => pond.Bottom + (pond.Top - pond.Bottom) * i / 200).ToList();
        charts.Add(new ToolChart("Stage–discharge", "Outflow (cfs)", "Elevation (ft)",
            [new("Total outflow", fine.Select(e => (pond.Outflow(e), e)).ToList(), Color: 1), .. (pond.Barrel is { } pb ? new[] { new ChartSeries($"{pb.Name} capacity", fine.Select(e => (pond.BarrelAt(e), e)).ToList(), Dashed: true, Color: 5) } : [])],
            berm is { } bm2 ? [new ChartLine("Top of berm", bm2)] : null));
        charts.Add(new ToolChart("Stage–storage", "Storage (ac-ft)", "Elevation (ft)",
            [new("Storage", fine.Select(e => (pond.Storage(e) / Ft3PerAcFt, e)).ToList(), Color: 0)]));

        var summary = new List<string> { $"Pond {pond.Bottom:F2} to {pond.Top:F2} ft, {pond.MaxStorage / Ft3PerAcFt:F3} ac-ft at the top stage; {outlets.Count} outlet(s)." };
        foreach (var s in ok) summary.Add($"{s.Label}: peak inflow {s.PeakInflow:F2} cfs → outflow {s.Result.PeakOutflow:F2} cfs, peak WSE {s.Result.PeakWse:F2} ft, {s.Result.PeakStorage / Ft3PerAcFt:F3} ac-ft used.");
        int fails = checks.Count(c => c[3] is string r && r.StartsWith("FAILS"));
        summary.Add(fails == 0 ? "All entered §4.10 criteria are met." : $"{fails} §4.10 criteria not met; see Criteria checks.");
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    /// <summary>The part of a routed hydrograph worth plotting: through the end of inflow and until outflow falls to 10 % of its peak (at least 30 min after inflow ends, at most 24 h).</summary>
    private static List<RouteStep> Window(List<RouteStep> steps)
    {
        if (steps.Count == 0) return steps;
        double peak = steps.Max(x => x.Outflow);
        int lastIn = steps.FindLastIndex(x => x.Inflow > 0);
        double tEnd = lastIn >= 0 ? steps[lastIn].TimeMin : 0;
        int end = steps.FindIndex(x => x.TimeMin >= tEnd + 30 && x.Outflow <= 0.1 * peak);
        if (end < 0) end = steps.Count - 1;
        end = Math.Min(end, steps.FindLastIndex(x => x.TimeMin <= 1440));
        return steps.Take(end + 1).ToList();
    }
}
