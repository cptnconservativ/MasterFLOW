using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Construction erosion and sediment control to the Rapid City Storm Water Quality Manual (2009) Chapter 1: RUSLE soil
/// loss (USDA AH 703) of each disturbed area, the Table 1.1 exposure limits, the Table 1.2 limits of perimeter controls
/// (silt fence, wattles, compost socks and berms, sod filter strips, silt ditch), and sediment trap and basin volume,
/// shape, drain time and clean-out (§1.3.3.5–1.3.3.7).
/// </summary>
public sealed class ErosionSedimentTool : IDesignTool
{
    public string Id => "erosion-sediment";
    public string Name => "Erosion and sediment control (RUSLE, silt fence, sediment basin)";
    public string Description => "RUSLE soil loss A = R·K·LS·C·P of each disturbed area (AH 703), checked against the Rapid City Storm Water Quality Manual (2009) Chapter 1: exposure periods of Table 1.1, slope stabilization, the Table 1.2 slope length and drainage area limits of silt fence, wattles, compost socks and berms, sod filter strips and silt ditch, and sediment traps and basins (3,600 ft³ per acre, basin above 10 ac, trap up to 5 ac, length ≥ 2 × width, 16 h minimum drain, clean-out at half full, months to half full from the RUSLE loads).";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Erosion and sediment control to the Rapid City Storm Water Quality Manual (2009) Chapter 1, U.S. units.
        # R = annual rainfall–runoff erosivity (AH 703 isoerodent map; western South Dakota is roughly 30–50 — use the
        # value for the site). R fraction = share of the annual R that falls while the area is exposed (1 = a full year).
        R = 40
        R fraction = 0.8
        sediment unit weight = 90      # pcf, deposited sediment (for volumes)

        # K = soil erodibility (NRCS Web Soil Survey, Kw); C = cover (bare 1.0; typical: seeded and mulched 0.05–0.1,
        # straw mulch 2 t/ac 0.05, erosion blanket 0.02–0.1, roughened 0.9); P = practice (1.0 none; contour roughening
        # 0.9). Stabilization = one of: roughening, mulch, temporary revegetation, permanent revegetation, stockpile,
        # road base (Table 1.1). Basin = the trap or basin it drains to.
        [Areas]
        Name	Area (ac)	Slope length (ft)	Slope (%)	K	C	P	Stabilization	Exposure (months)	Basin
        Lots 1-12	8.5	180	4	0.32	1.0	1.0	mulch	8	SB-1
        Cut slope	1.2	60	33	0.32	0.05	1.0	permanent revegetation	30	SB-1
        Stockpile	0.4	40	50	0.32	1.0	1.0	stockpile	3	ST-1
        Street ROW	2.0	120	2	0.28	0.9	1.0	roughening	2	ST-1

        # Type: silt fence, wattle 9, sock 8, sock 12, sock 18, sock 24, compost berm, sod filter strip, silt ditch.
        # Slope = tributary slope above the control, as H:V (e.g. 3:1) or %.
        [Perimeter controls]
        Name	Type	Length (ft)	Tributary area (ac)	Slope length (ft)	Slope
        SF-1	silt fence	600	2.5	150	5:1
        SF-2	silt fence	200	1.4	90	3:1
        W-1	wattle 9	300	0.3	40	3:1
        CS-1	sock 12	150	0.4	80	4:1
        SD-1	silt ditch	400	1.8
        SF-3	sod filter strip	100	0.2	45	8:1

        # Type: basin or trap. Volume = storage provided below the overflow (ft³). Depth = storage depth (ft).
        # Orifice = dewatering orifice diameter (in.) at the bottom; leave blank to get the largest that meets 16 h.
        [Basins]
        Name	Type	Tributary disturbed area (ac)	Volume (ft³)	Length (ft)	Width (ft)	Depth (ft)	Orifice (in.)
        SB-1	basin	12.5	48000	160	70	4.5	2
        ST-1	trap	2.4	9000	60	35	3
        """;

    private sealed record Area(string Name, double Acres, double Lambda, double SlopePct, double K, double C, double P, string? Stab, double? Months, string? Basin);

    /// <summary>RUSLE LS (AH 703 Eq. 4-1–4-5, moderate rill/interrill ratio): λ in ft, slope in percent.</summary>
    public static (double L, double S, double LS, double M) Ls(double lambdaFt, double slopePct)
    {
        double th = Math.Atan(slopePct / 100), sin = Math.Sin(th);
        double beta = sin / 0.0896 / (3 * Math.Pow(sin, 0.8) + 0.56);
        double m = beta / (1 + beta);
        double l = Math.Pow(lambdaFt / 72.6, m);
        double s = lambdaFt < 15 ? 3.0 * Math.Pow(sin, 0.8) + 0.56 : slopePct < 9 ? 10.8 * sin + 0.03 : 16.8 * sin - 0.50;
        return (l, s, l * s, m);
    }

    /// <summary>SWQM Table 1.1: maximum exposure (months) per stabilization method; null when unlimited.</summary>
    private static (double? Max, string Label)? Exposure(string s) => s.ToLowerInvariant() switch
    {
        var x when x.StartsWith("rough") => (1, "Surface roughening"),
        var x when x.StartsWith("mulch") => (12, "Mulching"),
        var x when x.StartsWith("temp") => (24, "Temporary revegetation"),
        var x when x.StartsWith("perm") => (null, "Permanent revegetation"),
        var x when x.StartsWith("stock") => (2, "Soil stockpile revegetation"),
        var x when x.StartsWith("road") || x.StartsWith("early") => (1, "Early application of road base"),
        _ => null,
    };

    /// <summary>SWQM Table 1.2 maximum tributary slope length (ft) by slope H:V (2, 3, 4, 5, flatter); NaN = not allowed.</summary>
    private static readonly Dictionary<string, (string Label, double[] Lengths)> Table12 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["silt fence"] = ("Silt fence", [50, 75, 125, 175, 200]),
        ["wattle 9"] = ("Straw wattle, 9 in.", [20, 20, 20, 50, 75]),
        ["sock 8"] = ("Compost sock, 8 in.", [25, 50, 75, 100, 125]),
        ["sock 12"] = ("Compost sock, 12 in.", [25, 50, 100, 125, 250]),
        ["sock 18"] = ("Compost sock, 18 in.", [50, 75, 150, 200, 300]),
        ["sock 24"] = ("Compost sock, 24 in.", [75, 100, 200, 250, 350]),
        ["compost berm"] = ("Compost berm", [25, 50, 100, 125, 250]),
    };

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Erosion and sediment control (SWQM 2009 Ch. 1)";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        if (context.Units == UnitSystem.SI) msgs.Add(CalcMessage.Info("I-ES-001", "RUSLE and the Rapid City limits are in U.S. units; enter acres, feet and the U.S. R and K values."));
        double rAnnual = p.Number("R", double.NaN, "R"), rFrac = p.Number("R fraction", 1.0, "R fraction"), gammaSed = p.Number("sediment unit weight", 90, "Sediment unit weight");

        var hd = p.Header("Areas").Select(h => h.ToLowerInvariant()).ToList();
        int C(List<string> h, string name, int fb) { int i = h.FindIndex(x => x == name || x.StartsWith(name + " ", StringComparison.Ordinal) || (name.Length > 1 && x.StartsWith(name, StringComparison.Ordinal))); return i >= 0 ? i : fb; }
        var areas = new List<Area>();
        foreach (var r in p.Rows("Areas"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Area {areas.Count + 1}";
            double? a = p.Cell(r, C(hd, "area", 1), "area", name), lam = p.Cell(r, C(hd, "slope length", 2), "slope length", name), s = p.Cell(r, C(hd, "slope (", 3), "slope", name),
                k = p.Cell(r, C(hd, "k", 4), "K", name), c = p.Cell(r, C(hd, "c", 5), "C", name), pp = p.Cell(r, C(hd, "p", 6), "P", name);
            if (a is not > 0 || lam is not > 0 || s is not >= 0 || k is not >= 0) { msgs.Add(CalcMessage.Error("E-ES-001", $"{name}: give the area, slope length, slope (%) and K.")); continue; }
            int cs = C(hd, "stabilization", 7), cb = C(hd, "basin", 9);
            areas.Add(new(name, a.Value, lam.Value, s.Value, k.Value, c ?? 1.0, pp ?? 1.0, cs < r.Length && r[cs].Length > 0 ? r[cs] : null, p.Cell(r, C(hd, "exposure", 8), "exposure", name), cb < r.Length && r[cb].Length > 0 ? r[cb] : null));
        }
        if (areas.Count > 0 && !(rAnnual > 0)) msgs.Add(CalcMessage.Error("E-ES-002", "Give R (annual rainfall–runoff erosivity, AH 703 isoerodent map) for the RUSLE soil loss."));

        msgs.InsertRange(0, p.Errors);
        if (areas.Count == 0 && p.Rows("Perimeter controls").Count == 0 && p.Rows("Basins").Count == 0)
            msgs.Add(CalcMessage.Error("E-ES-003", "Give at least one row under [Areas], [Perimeter controls] or [Basins]."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        // RUSLE
        var areaRows = new List<IReadOnlyList<object?>>();
        var toBasin = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);   // tons over the exposure period
        double totalTons = 0;
        foreach (var a in areas)
        {
            var (l, s, ls, m) = Ls(a.Lambda, a.SlopePct);
            double aYr = rAnnual * a.K * ls * a.C * a.P;           // t/ac/yr
            double tons = aYr * rFrac * a.Acres;
            totalTons += tons;
            if (a.Basin is not null) toBasin[a.Basin] = toBasin.GetValueOrDefault(a.Basin) + tons;
            string check = "";
            if (a.Stab is { } st)
            {
                var ex = Exposure(st);
                if (ex is null) msgs.Add(CalcMessage.Warning("W-ES-001", $"{a.Name}: stabilization \"{st}\" is not a Table 1.1 method (roughening, mulch, temporary or permanent revegetation, stockpile, road base)."));
                else if (ex.Value.Max is { } mx && a.Months is { } mo && mo > mx)
                {
                    check = $"{ex.Value.Label}: {mo:G3} months > {mx:G3} allowed";
                    msgs.Add(CalcMessage.Warning("W-ES-002", $"{a.Name}: {ex.Value.Label.ToLowerInvariant()} is allowed for at most {mx:G3} month(s) of exposure (SWQM Table 1.1), not {mo:G3}. Use a longer-lasting method."));
                }
                else check = ex.Value.Max is { } mx2 ? $"{ex.Value.Label} ≤ {mx2:G3} months: OK" : $"{ex.Value.Label}: OK";
            }
            if (a.SlopePct >= 100.0 / 3 - 1e-9 && a.C >= 0.2)
                msgs.Add(CalcMessage.Warning("W-ES-003", $"{a.Name}: slope 3:1 or steeper needs an erosion control blanket or bonded fiber matrix (SWQM §1.2.2); C = {a.C:G3} suggests bare or lightly covered soil."));
            areaRows.Add([a.Name, a.Acres, a.Lambda, a.SlopePct, R(m, 3), R(l, 3), R(s, 3), R(ls, 3), a.K, a.C, a.P, R(aYr, 2), R(tons, 1), R(tons * 2000 / gammaSed / 27, 1), check]);
        }

        // Perimeter controls (Table 1.2)
        var ctrlRows = new List<IReadOnlyList<object?>>();
        var hc = p.Header("Perimeter controls").Select(h => h.ToLowerInvariant()).ToList();
        foreach (var r in p.Rows("Perimeter controls"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Control {ctrlRows.Count + 1}";
            int ct = C(hc, "type", 1);
            string type = (ct < r.Length ? r[ct] : "").Trim().ToLowerInvariant().Replace("\"", "").Replace("”", "").Replace(" in.", "").Replace("-inch", "");
            type = type.Replace("compost sock", "sock").Replace("straw wattle", "wattle");
            double? len = p.Cell(r, C(hc, "length", 2), "length", name), trib = p.Cell(r, C(hc, "tributary area", 3), "tributary area", name), sl = p.Cell(r, C(hc, "slope length", 4), "slope length", name);
            int cslope = C(hc, "slope", 5); if (cslope == C(hc, "slope length", 4)) cslope = hc.FindIndex(x => x == "slope" || (x.StartsWith("slope") && !x.StartsWith("slope length"))); if (cslope < 0) cslope = 5;
            double? hv = cslope < r.Length ? SlopeHv(r[cslope]) : null;
            var issues = new List<string>();
            string limit = "";
            if (type is "silt fence" or "silt ditch")
            {
                if (len is > 0 && trib is { } t)
                {
                    double maxArea = 0.5 * len.Value / 100;
                    limit = $"≤ {maxArea:G3} ac (0.5 ac per 100 ft)";
                    if (t > maxArea + 1e-9) issues.Add($"tributary area {t:G3} ac > {maxArea:G3} ac; needs ≥ {Math.Ceiling(t / 0.5 * 100):F0} ft or a sediment trap");
                }
                if (type == "silt ditch" && trib is { } t2) limit += $"; volume ≥ {3600 * t2:N0} ft³ (3,600 ft³/ac)";
            }
            if (Table12.TryGetValue(type, out var lim))
            {
                if (hv is { } z)
                {
                    if (z < 2 - 1e-9) issues.Add($"slope steeper than 2:1 is outside Table 1.2");
                    else
                    {
                        int i = z >= 5 + 1e-9 ? 4 : z >= 5 - 1e-9 ? 3 : z >= 4 - 1e-9 ? 2 : z >= 3 - 1e-9 ? 1 : 0;
                        double maxL = lim.Lengths[i];
                        limit = (limit.Length > 0 ? limit + "; " : "") + $"slope length ≤ {maxL:G3} ft at {(i == 4 ? "flatter than 5:1" : $"{i + 2}:1")}";
                        if (sl is { } s && s > maxL + 1e-9) issues.Add($"slope length {s:G3} ft > {maxL:G3} ft");
                    }
                }
                else if (sl is not null) issues.Add("give the tributary slope to check the slope length");
            }
            else if (type is "sod filter strip" or "filter strip")
            {
                limit = "slope length ≤ 50 ft, slope 6:1 or flatter, width ≥ 20 ft";
                if (sl is > 50) issues.Add($"slope length {sl:G3} ft > 50 ft");
                if (hv is < 6 - 1e-9) issues.Add($"slope {hv:G3}:1 steeper than 6:1");
            }
            else if (type is not ("silt fence" or "silt ditch"))
            {
                msgs.Add(CalcMessage.Error("E-ES-004", $"{name}: type \"{type}\" is not in SWQM Table 1.2 (silt fence, wattle 9, sock 8/12/18/24, compost berm, sod filter strip, silt ditch)."));
                continue;
            }
            if (issues.Count > 0) msgs.Add(CalcMessage.Warning("W-ES-004", $"{name}: {string.Join("; ", issues)} (SWQM Table 1.2)."));
            ctrlRows.Add([name, type, len, trib, sl, hv is { } hh ? $"{hh:G3}:1" : null, limit, issues.Count == 0 ? "OK" : "NOT OK: " + string.Join("; ", issues)]);
        }

        // Sediment traps and basins (§1.3.3.5–1.3.3.6)
        var basinRows = new List<IReadOnlyList<object?>>();
        var hb = p.Header("Basins").Select(h => h.ToLowerInvariant()).ToList();
        foreach (var r in p.Rows("Basins"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Basin {basinRows.Count + 1}";
            int ct = C(hb, "type", 1);
            bool trap = ct < r.Length && r[ct].Trim().StartsWith("trap", StringComparison.OrdinalIgnoreCase);
            double? area = p.Cell(r, C(hb, "tributary", 2), "tributary area", name), vol = p.Cell(r, C(hb, "volume", 3), "volume", name),
                len = p.Cell(r, C(hb, "length", 4), "length", name), wid = p.Cell(r, C(hb, "width", 5), "width", name), dep = p.Cell(r, C(hb, "depth", 6), "depth", name),
                orf = p.Cell(r, C(hb, "orifice", 7), "orifice", name);
            if (area is not > 0) { msgs.Add(CalcMessage.Error("E-ES-005", $"{name}: give the tributary disturbed area (ac).")); continue; }
            double req = Math.Max(3600 * area.Value, trap ? 0.5 / 12 * 43560 * area.Value : 0);
            var issues = new List<string>();
            if (vol is { } v && v < req - 1e-6) issues.Add($"volume {v:N0} ft³ < {req:N0} ft³ required");
            if (trap && area > 5) issues.Add("a sediment trap serves at most 5 ac (Table 1.2); use a basin");
            if (!trap && area <= 10) msgs.Add(CalcMessage.Info("I-ES-002", $"{name}: {area:G3} ac is not above 10 ac, so a basin is not mandatory (§1.3.3.6); traps or a basin with 3,600 ft³/ac may be required."));
            if (!trap && area > 10 && vol is null) issues.Add("give the volume provided");
            if (!trap && len is > 0 && wid is > 0 && len < 2 * wid - 1e-9) issues.Add($"length {len:G3} ft < 2 × width {wid:G3} ft (when practicable)");
            // Drain time (basins: ≥ 16 h): falling head through a bottom orifice, Cd 0.6, prismatic storage of plan area V/depth.
            string drain = "";
            if (!trap && vol is > 0 && dep is > 0)
            {
                double asurf = vol.Value / dep.Value;
                double Hours(double dIn) { double ao = Math.PI / 4 * Math.Pow(dIn / 12, 2); return 2 * asurf * Math.Sqrt(dep.Value) / (0.6 * ao * Math.Sqrt(2 * 32.174)) / 3600; }
                if (orf is > 0)
                {
                    double h = Hours(orf.Value);
                    drain = $"{h:F1} h with a {orf:G3} in. orifice";
                    if (h < 16) issues.Add($"drains in {h:F1} h < 16 h; use an orifice ≤ {MaxOrifice(Hours):F2} in.");
                }
                else drain = $"largest orifice for 16 h: {MaxOrifice(Hours):F2} in.";
            }
            string cleanout = vol is > 0 ? $"clean out at {vol.Value / 2:N0} ft³ (half full)" : "";
            if (vol is > 0 && toBasin.TryGetValue(name, out double tons) && tons > 0)
            {
                double sedFt3 = tons * 2000 / gammaSed;
                double months = vol.Value / 2 / sedFt3 * 12 * rFrac;   // tons is over the exposure share of the year
                cleanout += $"; RUSLE load {sedFt3:N0} ft³ over the period, about {months:F1} months to half full";
                toBasin.Remove(name);
            }
            if (issues.Count > 0) msgs.Add(CalcMessage.Warning("W-ES-005", $"{name}: {string.Join("; ", issues)} (SWQM §{(trap ? "1.3.3.5" : "1.3.3.6")})."));
            basinRows.Add([name, trap ? "trap" : "basin", area, R(req, 0), vol, len, wid, dep, drain, cleanout, issues.Count == 0 ? "OK" : "NOT OK: " + string.Join("; ", issues)]);
        }
        foreach (var b in toBasin.Keys) msgs.Add(CalcMessage.Warning("W-ES-006", $"Areas drain to \"{b}\", which is not under [Basins]."));
        double disturbed = areas.Sum(a => a.Acres);
        if (disturbed > 10 && !basinRows.Any(b => (string?)b[1] == "basin")) msgs.Add(CalcMessage.Warning("W-ES-007", $"{disturbed:G3} ac is disturbed; any location draining more than 10 ac must be routed through a sediment basin (§1.3.3.6)."));

        var tables = new List<ToolTable>();
        if (areaRows.Count > 0)
            tables.Add(new("RUSLE soil loss", ["Area", "Area (ac)", "λ (ft)", "Slope (%)", "m", "L", "S", "LS", "K", "C", "P", "A (t/ac/yr)", "Soil loss (t)", "Volume (yd³)", "Table 1.1 exposure"], areaRows,
                [$"A = R·K·LS·C·P with R = {rAnnual:G4}; soil loss over the exposure = A × {rFrac:G3} of the annual R × area; volume at {gammaSed:G3} pcf.",
                 "LS by RUSLE (AH 703, moderate rill/interrill): L = (λ/72.6)^m, m = β/(1+β), β = (sinθ/0.0896)/(3 sin^0.8 θ + 0.56); S = 10.8 sinθ + 0.03 below 9 %, 16.8 sinθ − 0.50 at 9 % or more, 3.0 sin^0.8 θ + 0.56 for λ < 15 ft.",
                 "SWQM §1.2: stabilize within 14 days of final grade, or of rough grading when the area stays dormant more than 21 more days."]));
        if (ctrlRows.Count > 0)
            tables.Add(new("Perimeter controls (SWQM Table 1.2)", ["Control", "Type", "Length (ft)", "Tributary area (ac)", "Slope length (ft)", "Slope (H:V)", "Limit", "Check"], ctrlRows,
                ["Silt fence and silt ditch: 0.5 ac per 100 ft; silt ditch 3,600 ft³ per acre. Silt fence is not for use in a channel or swale (§1.3.3.1)."]));
        if (basinRows.Count > 0)
            tables.Add(new("Sediment traps and basins (SWQM §1.3.3.5–1.3.3.6)", ["Name", "Type", "Area (ac)", "Required (ft³)", "Provided (ft³)", "Length (ft)", "Width (ft)", "Depth (ft)", "Drain time", "Clean-out", "Check"], basinRows,
                ["Required: 3,600 ft³ per tributary disturbed acre (traps: or ½ in. of runoff, whichever is greater). Basins above 10 ac; traps up to 5 ac with an open-channel spillway.",
                 "Basin drain ≥ 16 h: falling head through one bottom orifice, Cd = 0.6, plan area = volume / depth. Basin length ≥ 2 × width when practicable. Clean out before half full."]));

        var summary = new List<string>();
        if (areas.Count > 0) summary.Add($"{disturbed:G4} ac disturbed: RUSLE soil loss {totalTons:F1} t ({totalTons * 2000 / gammaSed / 27:F0} yd³) over the exposure ({rFrac:G3} of the annual R = {rAnnual:G4}).");
        if (ctrlRows.Count > 0) summary.Add($"{ctrlRows.Count} perimeter control(s): {ctrlRows.Count(c => (string?)c[7] == "OK")} within the Table 1.2 limits.");
        if (basinRows.Count > 0) summary.Add($"{basinRows.Count} trap(s)/basin(s): {basinRows.Count(c => (string?)c[10] == "OK")} meet §1.3.3.");
        var charts = new List<ToolChart>();
        if (areaRows.Count > 0)
            charts.Add(new ToolChart("Soil loss by area", "Area (index)", "Soil loss (t)", [new ChartSeries("Soil loss over the exposure", areaRows.Select((r, i) => ((double)(i + 1), (double)r[12]!)).ToList(), MarkersOnly: true)]));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double MaxOrifice(Func<double, double> hours)
    {
        double lo = 0.05, hi = 24;
        for (int i = 0; i < 60; i++) { double mid = 0.5 * (lo + hi); if (hours(mid) >= 16) lo = mid; else hi = mid; }
        return lo;
    }

    /// <summary>A slope as H:V (e.g. "3:1") or percent ("33%", or a number above 0 without a colon taken as percent).</summary>
    public static double? SlopeHv(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        int c = s.IndexOf(':');
        if (c > 0 && ToolInput.TryNumber(s[..c], out double h) && ToolInput.TryNumber(s[(c + 1)..], out double v) && v > 0) return h / v;
        if (ToolInput.TryNumber(s, out double pct) && pct > 0) return 100 / pct;
        return null;
    }

    private static double R(double v, int d) => Math.Round(v, d) + 0.0;
}
