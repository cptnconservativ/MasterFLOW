using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// HEC-22 inlet equations (U.S. units, ft, cfs, fps) used by <see cref="InletNetworkTool"/>. Every function is a
/// published equation; the tool decides which applies. References: FHWA HEC-22 (3rd ed., §4.4.4–4.4.5) and
/// MHFD USDCM Vol. 1 Ch. 7 (2016) Table 7-6 for the splash-over constants.
/// </summary>
public static class InletHydraulics
{
    public const double G = 32.174;

    /// <summary>Splash-over constants (α, β, γ, η) for Vo = α + βL − γL² + ηL³ (Vo in fps, L = effective grate length in ft).</summary>
    public sealed record GrateType(string Key, string Name, double Alpha, double Beta, double Gamma, double Eta)
    {
        /// <summary>Splash-over velocity at effective length L. The polynomial is fitted up to 4 ft; beyond that Vo(4) + 0.2 fps per additional foot (MHFD USDCM Ch. 7).</summary>
        public double SplashOver(double l) => l <= 4 ? Poly(l) : Poly(4) + 0.2 * (l - 4);
        private double Poly(double l) => Alpha + Beta * l - Gamma * l * l + Eta * l * l * l;
    }

    /// <summary>
    /// Grates with published splash-over constants (MHFD USDCM Vol. 1 Ch. 7, Table 7-6; the first five also appear in HEC-22).
    /// HEC-22 names P-50 = P-1-7/8 (bars 1-7/8 in. on centre, crossbars 8 in.), P-50x100 = P-1-7/8-4 (crossbars 4 in.), P-30 = P-1-1/8.
    /// </summary>
    public static IReadOnlyList<GrateType> Grates { get; } =
    [
        new("P-50", "P-50 (P-1-7/8) parallel bar, crossbars at 8 in.", 2.22, 4.03, 0.65, 0.06),
        new("P-50x100", "P-50x100 (P-1-7/8-4) parallel bar, crossbars at 4 in.", 0.74, 2.44, 0.27, 0.02),
        new("P-30", "P-30 (P-1-1/8) parallel bar, crossbars at 8 in.", 1.76, 3.12, 0.45, 0.03),
        new("curved vane", "Curved (cast) vane", 0.30, 4.85, 1.31, 0.15),
        new("45 tilt bar", "45° tilt bar", 0.99, 2.64, 0.36, 0.03),
        new("30 tilt bar", "30° tilt bar", 0.51, 2.34, 0.20, 0.01),
        new("reticuline", "Reticuline", 0.28, 2.28, 0.18, 0.01),
    ];

    private static readonly Dictionary<string, string> GrateAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["p-1-7/8"] = "P-50", ["p50"] = "P-50", ["p-50x100"] = "P-50x100", ["p50x100"] = "P-50x100", ["p-1-7/8-4"] = "P-50x100",
        ["p-1-1/8"] = "P-30", ["p30"] = "P-30", ["vane"] = "curved vane", ["cast vane"] = "curved vane", ["curved-vane"] = "curved vane",
        ["45 bar"] = "45 tilt bar", ["45°tilt bar"] = "45 tilt bar", ["45° tilt bar"] = "45 tilt bar", ["tilt bar 45"] = "45 tilt bar",
        ["30 bar"] = "30 tilt bar", ["30° tilt bar"] = "30 tilt bar", ["tilt bar 30"] = "30 tilt bar",
    };

    public static GrateType? FindGrate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string k = GrateAliases.TryGetValue(name.Trim(), out var a) ? a : name.Trim();
        return Grates.FirstOrDefault(g => g.Key.Equals(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Curb-opening length for 100 % interception on grade, HEC-22 Eq. 4-22: LT = Ku Q^0.42 S^0.3 (1/(n Se))^0.6, Ku = 0.6.</summary>
    public static double CurbLengthTotal(double q, double s, double n, double se) => 0.6 * Math.Pow(q, 0.42) * Math.Pow(s, 0.3) * Math.Pow(1 / (n * se), 0.6);

    /// <summary>Curb-opening efficiency for L &lt; LT, HEC-22 Eq. 4-23: E = 1 − (1 − L/LT)^1.8.</summary>
    public static double CurbEfficiency(double l, double lt) => !(lt > 0) || l >= lt ? 1 : 1 - Math.Pow(1 - l / lt, 1.8);

    /// <summary>Frontal flow interception, HEC-22 Eq. 4-18: Rf = 1 − Ku (V − Vo), Ku = 0.09; 1 when V ≤ Vo, never below 0.</summary>
    public static double FrontalInterception(double v, double vo) => v <= vo ? 1 : Math.Max(0, 1 - 0.09 * (v - vo));

    /// <summary>Side flow interception, HEC-22 Eq. 4-19: Rs = 1 / (1 + Ku V^1.8 / (Sx L^2.3)), Ku = 0.15.</summary>
    public static double SideInterception(double v, double sx, double l) => 1 / (1 + 0.15 * Math.Pow(v, 1.8) / (sx * Math.Pow(l, 2.3)));

    /// <summary>Sag grate weir, HEC-22 Eq. 4-26: Q = Cw P d^1.5, Cw = 3.0, P = L + 2W (curb side not counted).</summary>
    public static double GrateWeir(double d, double l, double w) => d > 0 ? 3.0 * (l + 2 * w) * Math.Pow(d, 1.5) : 0;
    /// <summary>Sag grate orifice, HEC-22 Eq. 4-27: Q = Co A √(2 g d), Co = 0.67, A = clear opening area.</summary>
    public static double GrateOrifice(double d, double area) => d > 0 ? 0.67 * area * Math.Sqrt(2 * G * d) : 0;
    /// <summary>Sag grate capacity: weir below d = 1.79 A/P and orifice above (the lesser of the two is exactly that rule).</summary>
    public static double GrateSag(double d, double l, double w, double area) => Math.Min(GrateWeir(d, l, w), GrateOrifice(d, area));

    /// <summary>
    /// Sag curb opening, HEC-22 §4.4.5.2. Weir: depressed Cw = 2.3, Q = Cw (L + 1.8 W) d^1.5 with d from the normal cross slope
    /// (L ≤ 12 ft); otherwise Q = 3.0 L d^1.5. Orifice (vertical throat): Q = 0.67 h L √(2 g (di − h/2)), di = depth at the lip.
    /// Weir while di ≤ h, orifice when di ≥ 1.4 h, linear between (HEC-22: "transition").
    /// </summary>
    /// <param name="dWeir">Depth for the weir equation, ft (measured from the normal cross slope when depressed).</param>
    /// <param name="di">Depth at the lip of the opening, ft.</param>
    /// <param name="depressed">True for a depressed opening (W &gt; 0 and some depression).</param>
    public static double CurbSag(double dWeir, double di, double l, double w, double h, bool depressed)
    {
        double Weir(double dw) => dw <= 0 ? 0 : depressed && l <= 12 ? 2.3 * (l + 1.8 * w) * Math.Pow(dw, 1.5) : 3.0 * l * Math.Pow(dw, 1.5);
        double Orifice(double dl) => dl <= h / 2 ? 0 : 0.67 * h * l * Math.Sqrt(2 * G * (dl - h / 2));
        if (!(h > 0) || di <= h) return Weir(dWeir);
        if (di >= 1.4 * h) return Orifice(di);
        // Between h and 1.4 h: interpolate between the weir value at di = h and the orifice value at di = 1.4 h.
        double off = dWeir - di;                       // weir depth tracks the lip depth with a constant offset
        double qw = Weir(h + off), qo = Orifice(1.4 * h);
        return qw + (qo - qw) * (di - h) / (0.4 * h);
    }

    /// <summary>Curb-opening orifice alone (used in a sag combination inlet in orifice flow, HEC-22 Eq. 4-34).</summary>
    public static double CurbOrifice(double di, double l, double h) => h > 0 && di > h ? 0.67 * h * l * Math.Sqrt(2 * G * (di - h / 2)) : 0;

    /// <summary>Smallest depth (ft) at which an increasing capacity function passes Q; NaN when 5 ft is not enough.</summary>
    public static double SolveDepth(double q, Func<double, double> capacity)
    {
        if (!(q > 0)) return 0;
        double lo = 0, hi = 5;
        if (capacity(hi) < q) return double.NaN;
        for (int i = 0; i < 200 && hi - lo > 1e-12; i++) { double m = 0.5 * (lo + hi); if (capacity(m) < q) lo = m; else hi = m; }
        return hi;
    }
}

/// <summary>
/// Inlet capacity and bypass through a storm network: every inlet on grade or in a sag (curb opening, grate, combination)
/// by HEC-22, with each inlet's bypass carried to the next inlet downstream. "Next downstream" is the inlet named in the
/// row's Bypass to column, or else the first inlet reached by following the network's pipes downstream. Inlets are
/// solved in bypass order (a topological sort), so several inlets may bypass to the same one.
/// </summary>
public sealed class InletNetworkTool : IDesignTool
{
    public string Id => "inlet-network";
    public string Name => "Inlet capacity and bypass routing (HEC-22)";
    public string Description => "HEC-22 interception of curb-opening, grate and combination inlets on grade and in sags, on a uniform or composite (depressed) gutter. Bypass is routed to the next downstream inlet in the network (or the inlet you name), inlets are solved in flow order, and a mass balance is reported. Optional minor-storm flows are routed the same way. Apply sets each inlet's intercepted flow as its network inflow.";
    public bool UsesNetwork => true;

    private static readonly Dictionary<string, (double Dry, string Text)> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["local"] = (0, "Local: spread may reach the crown"),
        ["collector"] = (12, "Collector / two-lane arterial: 12 ft of pavement free of water"),
        ["arterial3"] = (24, "Three-lane arterial: 24 ft free of water"),
        ["arterial4"] = (36, "Arterial with more than three lanes: 36 ft free of water"),
    };

    // Column keys (header text, lower case, units in parentheses dropped) → canonical name.
    private static readonly Dictionary<string, string> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["inlet"] = "inlet", ["name"] = "inlet", ["structure"] = "inlet",
        ["type"] = "type", ["location"] = "loc", ["loc"] = "loc",
        ["q local"] = "q", ["q"] = "q", ["q major"] = "q", ["q design"] = "q",
        ["q minor"] = "qminor", ["q 2-yr"] = "qminor", ["q 2yr"] = "qminor",
        ["s"] = "s", ["slope"] = "s",
        ["l"] = "l", ["w"] = "w", ["a"] = "a", ["h"] = "h", ["area"] = "area", ["a grate"] = "area",
        ["grate"] = "grate", ["vo"] = "grate", ["grate / vo"] = "grate", ["grate or vo"] = "grate",
        ["lc"] = "lc", ["l curb"] = "lc", ["clog"] = "clog", ["clogging"] = "clog",
        ["sx"] = "sx", ["n"] = "n", ["bypass to"] = "to", ["bypass"] = "to", ["to"] = "to",
    };

    public string SampleInput(DesignToolContext context)
    {
        var net = context.Network;
        var inlets = net?.Structures.Where(IsInletLike).ToList() ?? [];
        string N(int i)
        {
            if (i < inlets.Count) return inlets[i].Name;
            int k = i + 1;
            while (net?.Structures.Any(s => s.Name.Equals($"CB-{k}", StringComparison.OrdinalIgnoreCase)) == true || inlets.Take(3).Any(s => s.Name.Equals($"CB-{k}", StringComparison.OrdinalIgnoreCase))) k += 100;
            return $"CB-{k}";
        }
        string Q(int i, double dflt) => (i < inlets.Count && inlets[i].Inflow is > 0 ? inlets[i].Inflow!.Value : dflt).ToString("0.##", CultureInfo.InvariantCulture);
        return $"""
            # Inlet capacity and bypass routing (HEC-22). U.S. units: ft, cfs, fps.
            street class = collector   # local, collector, arterial3, arterial4 (IDCM Table 4-6 spread)
            pavement width = 40        # flowline to flowline, ft
            curb height = 0.5          # ft
            cross slope = 0.02         # Sx, ft/ft
            n = 0.016
            gutter width = 2           # W of a depressed (composite) gutter, ft; 0 = uniform cross slope
            gutter slope = 0.0833      # Sw, ft/ft (here a 2 ft gutter pan that falls 2 in.)

            # One row per inlet. Columns are found by their header, so any order works and unused ones can be left out.
            # Type: curb, grate, combination. Location: grade or sag. Q = runoff reaching the gutter at this inlet
            # (cfs, e.g. from the rational method tool); Q minor = the same for the minor (2-yr) storm (optional).
            # L, W = opening length and width, ft (W of a curb opening = local depression width); a = local depression, in.;
            # Grate = P-50, P-50x100, P-30, curved vane, 45 tilt bar, 30 tilt bar, reticuline, or a splash-over Vo in fps;
            # Area = grate clear opening, ft²; h = curb-opening height, ft; Lc = curb-opening length of a combination
            # inlet when it extends upstream of the grate (sweeper); Clog = clogging % (blank = IDCM Table 4-11).
            # Bypass to: blank = next inlet downstream through the pipes; or an inlet name; or "out" (leaves the system).
            [Inlets]
            Inlet	Type	Location	Q (cfs)	Q minor (cfs)	S (ft/ft)	L (ft)	W (ft)	a (in)	Grate	Area (ft2)	h (ft)	Bypass to
            {N(0)}	curb	grade	{Q(0, 4.0)}	1.6	0.015	10	2	2			0.5	{N(1)}
            {N(1)}	grate	grade	{Q(1, 2.5)}	1.0	0.012	3	2		P-50x100			{N(2)}
            {N(2)}	combination	sag	{Q(2, 1.5)}	0.6		3	2		P-50x100	3.2	0.5
            """;
    }

    private static bool IsInletLike(NetworkStructure s) =>
        s.Kind.Contains("inlet", StringComparison.OrdinalIgnoreCase) || s.Kind.Contains("catch", StringComparison.OrdinalIgnoreCase) ||
        s.Name.StartsWith("CB", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("IN", StringComparison.OrdinalIgnoreCase);

    private sealed record Inlet(int Row, string Name, string Type, string Loc, double Q, double? QMinor, double? S, double L, double? W, double ALocal,
        InletHydraulics.GrateType? Grate, double? Vo, double? Area, double? H, double? Lc, double Clog, double Sx, double N, string? To);

    private sealed record Outcome(double QIn, double T, double D, double V, double LEff, double E, double Qi, double Bypass, string Regime, string Details);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Inlet capacity and bypass routing — HEC-22";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-IN-001", "The HEC-22 U.S. coefficients (Ku = 0.6, 0.09, 0.15, Cw 3.0/2.3, Co 0.67) are in ft and cfs. Switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        string cls = p.Text("street class") ?? "local";
        if (!Classes.TryGetValue(cls, out var c)) { msgs.Add(CalcMessage.Error("E-IN-002", $"Street class \"{cls}\" must be local, collector, arterial3 or arterial4.")); c = Classes["local"]; }
        double width = p.Number("pavement width", double.NaN), curb = p.Number("curb height", 0.5), sx0 = p.Number("cross slope", 0.02), n0 = p.Number("n", 0.016);
        double gw = p.Number("gutter width", 0), gs = p.Number("gutter slope", 0), sDefault = p.Number("longitudinal slope", double.NaN);
        if (!(width > 0)) msgs.Add(CalcMessage.Error("E-IN-003", "Pavement width (flowline to flowline) is required and must be greater than zero."));
        if (!(curb > 0)) msgs.Add(CalcMessage.Error("E-IN-004", "Curb height must be greater than zero."));
        foreach (var prob in new GutterSection(sx0, n0, gw, gs).Problems()) msgs.Add(CalcMessage.Error("E-IN-005", prob));
        double tAllow = (width - c.Dry) / 2;
        if (width > 0 && !(tAllow > 0)) msgs.Add(CalcMessage.Error("E-IN-006", $"A {width} ft pavement cannot leave {c.Dry} ft dry."));

        var header = p.Header("Inlets");
        var rowsIn = p.Rows("Inlets");
        if (rowsIn.Count == 0) msgs.Add(CalcMessage.Error("E-IN-007", "No inlets: add a header line and one row per inlet under [Inlets]."));
        var col = new Dictionary<string, int>();
        for (int i = 0; i < header.Count; i++)
        {
            string key = Normalize(header[i]);
            if (ColumnAliases.TryGetValue(key, out var canon)) { if (!col.TryAdd(canon, i)) msgs.Add(CalcMessage.Error("E-IN-008", $"Column \"{header[i]}\" appears twice.")); }
            else msgs.Add(CalcMessage.Warning("W-IN-001", $"Column \"{header[i]}\" is not recognised and is ignored."));
        }
        foreach (var need in new[] { "inlet", "type", "loc", "q", "l" })
            if (rowsIn.Count > 0 && !col.ContainsKey(need)) msgs.Add(CalcMessage.Error("E-IN-009", $"The [Inlets] header needs a \"{need switch { "inlet" => "Inlet", "type" => "Type", "loc" => "Location", "q" => "Q", _ => "L" }}\" column."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], [.. p.Errors, .. msgs]);

        // ---- Read rows ----
        string? Txt(string[] r, string k) => col.TryGetValue(k, out int i) && i < r.Length && !string.IsNullOrWhiteSpace(r[i]) ? r[i].Trim() : null;
        double? Num(string[] r, string k, string what, string where) => col.TryGetValue(k, out int i) ? p.Cell(r, i, what, where) : null;
        var inlets = new List<Inlet>();
        var byName = new Dictionary<string, Inlet>(StringComparer.OrdinalIgnoreCase);
        bool anyMinor = false;
        foreach (var (r, k) in rowsIn.Select((r, k) => (r, k)))
        {
            string name = Txt(r, "inlet") ?? "", where = $"Inlet {name}";
            if (name.Length == 0) { msgs.Add(CalcMessage.Error("E-IN-010", $"Row {k + 1}: the inlet name is blank.")); continue; }
            if (byName.ContainsKey(name)) { msgs.Add(CalcMessage.Error("E-IN-011", $"{where} is listed twice.")); continue; }
            string type = (Txt(r, "type") ?? "").ToLowerInvariant(), loc = (Txt(r, "loc") ?? "grade").ToLowerInvariant();
            if (type == "combo") type = "combination";
            int before = msgs.Count + p.Errors.Count;
            if (type is not ("curb" or "grate" or "combination")) msgs.Add(CalcMessage.Error("E-IN-012", $"{where}: type \"{type}\" must be curb, grate or combination."));
            if (loc is not ("grade" or "sag")) msgs.Add(CalcMessage.Error("E-IN-013", $"{where}: location \"{loc}\" must be grade or sag."));
            double q = Num(r, "q", "Q", where) ?? 0;
            double? qMinor = Num(r, "qminor", "Q minor", where);
            double? s = Num(r, "s", "S", where), l = Num(r, "l", "L", where), w = Num(r, "w", "W", where), a = Num(r, "a", "a", where);
            double? area = Num(r, "area", "Area", where), h = Num(r, "h", "h", where), lc = Num(r, "lc", "Lc", where), clog = Num(r, "clog", "Clog", where);
            double sx = Num(r, "sx", "Sx", where) ?? sx0, n = Num(r, "n", "n", where) ?? n0;
            if (q < 0 || qMinor < 0) msgs.Add(CalcMessage.Error("E-IN-014", $"{where}: Q cannot be negative."));
            if (l is not > 0) msgs.Add(CalcMessage.Error("E-IN-015", $"{where}: opening length L must be greater than zero."));
            if (w < 0 || a < 0 || area < 0 || h < 0 || lc < 0) msgs.Add(CalcMessage.Error("E-IN-016", $"{where}: W, a, Area, h and Lc cannot be negative."));
            if (clog is < 0 or >= 100) msgs.Add(CalcMessage.Error("E-IN-017", $"{where}: clogging must be from 0 to less than 100 %."));
            if (!(sx > 0) || !(n > 0)) msgs.Add(CalcMessage.Error("E-IN-018", $"{where}: Sx and n must be greater than zero."));
            s ??= double.IsNaN(sDefault) ? null : sDefault;
            if (loc == "grade" && s is not > 0) msgs.Add(CalcMessage.Error("E-IN-019", $"{where}: a longitudinal slope S greater than zero is needed on grade (a zero or adverse grade is a sag)."));
            InletHydraulics.GrateType? grate = null; double? vo = null;
            if (Txt(r, "grate") is { } g)
            {
                if (ToolInput.TryNumber(g, out double v)) { if (v > 0) vo = v; else msgs.Add(CalcMessage.Error("E-IN-020", $"{where}: splash-over velocity Vo must be greater than zero.")); }
                else if ((grate = InletHydraulics.FindGrate(g)) is null) msgs.Add(CalcMessage.Error("E-IN-021", $"{where}: grate \"{g}\" is not known. Use {string.Join(", ", InletHydraulics.Grates.Select(x => x.Key))}, or enter its splash-over velocity Vo (fps)."));
            }
            if (type is "grate" or "combination")
            {
                if (w is not > 0) msgs.Add(CalcMessage.Error("E-IN-022", $"{where}: a grate needs its width W."));
                if (loc == "grade" && grate is null && vo is null && !msgs.Skip(before).Any()) msgs.Add(CalcMessage.Error("E-IN-023", $"{where}: a grate on grade needs its grate type or splash-over velocity Vo (Grate column)."));
                if (loc == "sag" && area is not > 0) msgs.Add(CalcMessage.Error("E-IN-024", $"{where}: a grate in a sag needs its clear opening area (Area column) for orifice flow."));
            }
            if (loc == "sag" && type is "curb" or "combination" && h is not > 0) msgs.Add(CalcMessage.Error("E-IN-025", $"{where}: a curb opening in a sag needs its opening height h."));
            if (type == "combination" && lc is { } lcv && l is { } lv && lcv < lv - 1e-9) msgs.Add(CalcMessage.Error("E-IN-026", $"{where}: the curb opening (Lc = {lcv}) of a combination inlet cannot be shorter than its grate (L = {lv})."));
            if (msgs.Count + p.Errors.Count > before) continue;
            // IDCM Table 4-11: grate 40 %, combination 33 % (a combination on grade is a grate, IDCM §4.5.2, so 40 %), curb 0 %.
            double clogPct = clog ?? type switch { "grate" => 40, "combination" => loc == "grade" ? 40 : 33, _ => 0 };
            anyMinor |= qMinor.HasValue;
            var inlet = new Inlet(k, name, type, loc, q, qMinor, s, l!.Value, w, (a ?? 0) / 12, grate, vo, area, h, lc, clogPct / 100, sx, n, Txt(r, "to"));
            inlets.Add(inlet); byName[name] = inlet;
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error) || p.Errors.Count > 0) return new ToolResult(title, [], [], [.. p.Errors, .. msgs]);

        // ---- Bypass targets ----
        var target = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var how = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in inlets)
        {
            if (i.To is { } t)
            {
                if (t.ToLowerInvariant() is "out" or "none" or "-" or "offsite" or "leaves") { target[i.Name] = null; how[i.Name] = "leaves (set)"; }
                else if (byName.TryGetValue(t, out var ti)) { if (ti == i) msgs.Add(CalcMessage.Error("E-IN-027", $"Inlet {i.Name} cannot bypass to itself.")); target[i.Name] = ti.Name; how[i.Name] = "named"; }
                else msgs.Add(CalcMessage.Error("E-IN-028", $"Inlet {i.Name}: bypass target \"{t}\" is not in the [Inlets] table (use \"out\" for flow that leaves the system)."));
            }
            else
            {
                var (found, path) = NextInletDownstream(context.Network, i.Name, byName);
                target[i.Name] = found; how[i.Name] = found is null ? (context.Network is null ? "leaves (no network)" : "leaves (no inlet downstream)") : $"pipes {path}";
            }
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        // ---- Topological order (Kahn) ----
        var indeg = inlets.ToDictionary(i => i.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var t in target.Values) if (t is not null) indeg[t]++;
        var ready = new Queue<Inlet>(inlets.Where(i => indeg[i.Name] == 0));
        var order = new List<Inlet>();
        while (ready.Count > 0)
        {
            var i = ready.Dequeue(); order.Add(i);
            if (target[i.Name] is { } t && --indeg[t] == 0) ready.Enqueue(byName[t]);
        }
        if (order.Count < inlets.Count)
            return new ToolResult(title, [], [], [.. msgs, CalcMessage.Error("E-IN-029", $"Bypass forms a loop through {string.Join(", ", inlets.Where(i => indeg[i.Name] > 0).Select(i => i.Name))}; gutter flow cannot return to an inlet it has passed. Name the bypass targets explicitly.")]);

        // ---- Solve (design storm, then minor storm) ----
        var storms = new List<(string Label, Func<Inlet, double> Q)> { ("Design storm", i => i.Q) };
        if (anyMinor) storms.Add(("Minor storm", i => i.QMinor ?? 0));
        var tables = new List<ToolTable>();
        var summary = new List<string>();
        var intercepted = new List<Dictionary<string, double>>();
        foreach (var (label, qOf) in storms)
        {
            var arriving = inlets.ToDictionary(i => i.Name, _ => 0.0, StringComparer.OrdinalIgnoreCase);
            var from = inlets.ToDictionary(i => i.Name, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            var rows = new List<IReadOnlyList<object?>>();
            var detail = new List<IReadOnlyList<object?>>();
            var qi = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double leaving = 0, local = 0;
            bool failed = false;
            foreach (var i in order)
            {
                double q = qOf(i) + arriving[i.Name];
                local += qOf(i);
                var gutter = new GutterSection(i.Sx, i.N, gw, gs);
                var o = Solve(i, q, gutter, out string? error);
                if (error is not null) { msgs.Add(CalcMessage.Error("E-IN-030", $"{label}, inlet {i.Name}: {error}")); failed = true; continue; }
                qi[i.Name] = o!.Qi;
                string to = target[i.Name] ?? "out";
                if (o.Bypass > 1e-9) { if (target[i.Name] is { } t) { arriving[t] += o.Bypass; from[t].Add($"{i.Name} {o.Bypass:F2}"); } else leaving += o.Bypass; }
                var check = new List<string>();
                if (o.D > curb + 1e-9) check.Add($"depth {o.D:F2} ft over the {curb:F2} ft curb");
                if (o.T > tAllow + 1e-9) check.Add($"spread {o.T:F1} ft over the {tAllow:F1} ft allowed");
                if (check.Count > 0) msgs.Add(CalcMessage.Warning("W-IN-002", $"{label}, inlet {i.Name}: {string.Join("; ", check)} (IDCM Table 4-6, {c.Text})."));
                rows.Add([i.Name, $"{i.Type} / {i.Loc}", qOf(i), arriving[i.Name], o.QIn, o.T, o.D, o.V, o.LEff, o.E, o.Qi, o.Bypass,
                    o.Bypass > 1e-9 || i.Loc == "grade" ? to : "—", from[i.Name].Count > 0 ? string.Join(", ", from[i.Name]) : "", check.Count == 0 ? "OK" : "FAILS: " + string.Join("; ", check)]);
                detail.Add([i.Name, o.Regime, o.Details]);
            }
            if (failed) continue;
            intercepted.Add(qi);
            double total = qi.Values.Sum();
            tables.Add(new ToolTable($"{label}: inlets in flow order", ["Inlet", "Type", "Q local (cfs)", "Q bypass in (cfs)", "Q gutter (cfs)", "Spread T (ft)", "Depth at curb (ft)", "V (fps)", "L effective (ft)", "E", "Q intercepted (cfs)", "Bypass (cfs)", "Bypass to", "Bypass from (cfs)", "Table 4-6 check"], rows,
            [
                $"Street: {c.Text}. Allowed spread ({width:0.#} − {c.Dry:0}) / 2 = {tAllow:F1} ft; curb {curb:F2} ft; Sx = {sx0}; n = {n0}" + (gw > 0 && gs > sx0 ? $"; composite gutter W = {gw} ft, Sw = {gs} (depression {gw * (gs - sx0):F3} ft)." : "; uniform cross slope."),
                "Rows are in flow order: an inlet is solved after every inlet that bypasses to it. Sag inlets intercept all of their flow at the ponded depth shown.",
                $"Mass balance: local {local:F3} cfs = intercepted {total:F3} + leaving the system {leaving:F3} (difference {local - total - leaving:E1}).",
            ]));
            tables.Add(new ToolTable($"{label}: hand-check values", ["Inlet", "Regime", "Values"], detail));
            summary.Add($"{label}: {rows.Count} inlet(s), local runoff {local:F2} cfs, intercepted {total:F2} cfs, {leaving:F2} cfs leaves the system as bypass.");
            if (leaving > 1e-6) msgs.Add(CalcMessage.Warning("W-IN-003", $"{label}: {leaving:F2} cfs of bypass leaves the system past the last inlet; confirm where it goes."));
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, summary, tables, [.. p.Errors, .. msgs]);

        tables.Add(new ToolTable("Bypass routing", ["Inlet", "Bypass to", "How the target was found"],
            order.Select(i => (IReadOnlyList<object?>)[i.Name, i.Loc == "sag" ? "— (sag)" : target[i.Name] ?? "out", i.Loc == "sag" ? "sag inlet: no bypass (all flow ponds to it)" : how[i.Name]]).ToList(),
            ["Blank Bypass to = the first inlet reached by following the pipes downstream from this structure (surface flow assumed to follow the sewer). Name the inlet where the street grade sends the water when it differs."]));
        msgs.Add(CalcMessage.Info("I-IN-001", "Clogging per IDCM Table 4-11 unless entered: grate 40 %, combination 40 % on grade (IDCM §4.5.2: Type B on grade is a grate) and 33 % in a sag, curb opening 0 %. Clogging shortens the grate and its open area (width unchanged); a combination's curb opening is not clogged."));

        var result = new ToolResult(title, summary, tables, msgs);
        if (context.Network is { } net && intercepted.Count > 0)
        {
            var design = intercepted[0];
            var minor = intercepted.Count > 1 ? intercepted[1] : null;
            int k = net.Structures.Count(s => design.ContainsKey(s.Name));
            var missing = design.Keys.Where(nm => !net.Structures.Any(s => s.Name.Equals(nm, StringComparison.OrdinalIgnoreCase))).ToList();
            if (missing.Count > 0) msgs.Add(CalcMessage.Info("I-IN-002", $"Not in network \"{net.Name}\" (not applied): {string.Join(", ", missing)}."));
            if (k > 0)
                return result with
                {
                    UpdatedNetwork = net with
                    {
                        Structures = net.Structures.Select(s => design.TryGetValue(s.Name, out var f)
                            ? s with { Inflow = Math.Round(f, 3), MinorInflow = minor is not null && minor.TryGetValue(s.Name, out var fm) ? Math.Round(fm, 3) : s.MinorInflow }
                            : s).ToList(),
                    },
                    UpdateDescription = $"Set the intercepted flow as the inflow at {k} inlet(s) of \"{net.Name}\"" + (minor is not null ? " (design and minor storm)." : "."),
                };
        }
        return result;
    }

    private static string Normalize(string h)
    {
        int paren = h.IndexOf('(');
        return (paren >= 0 ? h[..paren] : h).Trim().ToLowerInvariant();
    }

    /// <summary>Follow outlet pipes downstream from <paramref name="start"/> to the first structure that is in the inlet table.</summary>
    private static (string? Found, string Path) NextInletDownstream(StormNetwork? net, string start, Dictionary<string, Inlet> inlets)
    {
        if (net is null) return (null, "");
        var path = new List<string> { start };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
        string cur = start;
        while (true)
        {
            var pipe = net.Pipes.FirstOrDefault(pp => pp.From.Equals(cur, StringComparison.OrdinalIgnoreCase));
            if (pipe is null || !seen.Add(pipe.To)) return (null, "");
            path.Add(pipe.To);
            if (inlets.ContainsKey(pipe.To)) return (inlets[pipe.To].Name, string.Join(" → ", path));
            cur = pipe.To;
        }
    }

    private static string F(double v, string fmt = "0.###") => double.IsFinite(v) ? v.ToString(fmt, CultureInfo.InvariantCulture) : "—";

    /// <summary>One inlet at gutter flow <paramref name="q"/>.</summary>
    private static Outcome? Solve(Inlet i, double q, GutterSection gutter, out string? error)
    {
        error = null;
        double lEff = i.L * (1 - (i.Type == "curb" ? 0 : i.Clog));
        // Clogging shortens the grate and reduces its open area (MHFD Eqs. 7-41/7-42: Le = (1 − C) L, Ae = (1 − C) A); the width is unchanged.
        double wEff = i.W ?? 0;
        double aCont = gutter.Depression;
        if (i.Loc == "grade")
        {
            double s = i.S!.Value;
            if (!(q > 0)) return new Outcome(0, 0, 0, 0, lEff, double.NaN, 0, 0, "no flow", "Q = 0");
            double t = gutter.Spread(q, s), d = gutter.Depth(t), v = q / gutter.Area(t);
            if (i.Type == "curb")
            {
                double qi = CurbOnGrade(i, q, s, gutter, t, lEff, out string detCurb);
                return new Outcome(q, t, d, v, lEff, qi / q, qi, q - qi, "on grade, curb opening (HEC-22 Eqs. 4-22 to 4-24)", $"T = {F(t)} ft, V = {F(v)} fps; " + detCurb);
            }
            // Grate, or combination (grate plus any curb opening upstream of it).
            double qSweep = 0, qG = q, tG = t, vG = v; string sweep = "";
            double lc = i.Type == "combination" ? (i.Lc ?? i.L) : 0;
            if (i.Type == "combination" && lc > i.L + 1e-9)
            {
                var curbPart = i with { L = lc - i.L };
                qSweep = CurbOnGrade(curbPart, q, s, gutter, t, lc - i.L, out string detC);
                qG = q - qSweep; tG = gutter.Spread(qG, s); vG = qG > 0 ? qG / gutter.Area(tG) : 0;
                sweep = $"curb opening upstream of the grate, L = {F(lc - i.L)} ft: {detC}, Qi = {F(qSweep)} cfs; flow reaching the grate {F(qG)} cfs, T = {F(tG)} ft, V = {F(vG)} fps; ";
            }
            double vo = i.Vo ?? i.Grate!.SplashOver(lEff);
            double eo = qG > 0 ? gutter.FrontalRatio(tG, wEff) : 1;
            double rf = InletHydraulics.FrontalInterception(vG, vo), rs = InletHydraulics.SideInterception(vG, i.Sx, lEff);
            double eG = rf * eo + rs * (1 - eo);
            double qiG = eG * qG, qiT = qSweep + qiG;
            string vos = i.Vo is not null ? $"Vo = {F(vo)} fps (entered)" : $"Vo = {i.Grate!.Alpha} + {i.Grate.Beta}·L − {i.Grate.Gamma}·L² + {i.Grate.Eta}·L³ = {F(vo)} fps at L = {F(lEff)} ft ({i.Grate.Name})";
            string det2 = $"{sweep}T = {F(t)} ft, V = {F(v)} fps; grate L = {F(lEff)} ft, W = {F(wEff)} ft (L after {i.Clog * 100:0.#} % clogging); Eo = {F(eo, "0.0000")}; {vos}; Rf = {F(rf, "0.0000")}" + (vG > vo ? " (splash-over)" : "") +
                          $"; Rs = 1/(1 + 0.15·V^1.8/(Sx·L^2.3)) = {F(rs, "0.0000")}; E = Rf·Eo + Rs·(1 − Eo) = {F(eG, "0.0000")}; Qi = {F(qiT)} cfs" + (i.Type == "combination" && lc <= i.L + 1e-9 ? "; combination on grade with equal lengths = grate alone (HEC-22 §4.4.4.4, IDCM §4.5.2)" : "");
            return new Outcome(q, t, d, v, lEff, qiT / q, qiT, q - qiT, i.Type == "grate" ? "on grade, grate (HEC-22 Eqs. 4-16 to 4-21)" : "on grade, combination (HEC-22 §4.4.4.4)", det2);
        }

        // ---- Sag ----
        double h = i.H ?? 0, wc = i.W ?? 0, aLoc = i.ALocal, aTot = aCont + aLoc;
        bool depressed = aTot > 0 && (wc > 0 || gutter.W > 0);
        double wDep = wc > 0 ? wc : gutter.W;
        double GrateDepth(double dCurb) => Math.Max(0, dCurb + aLoc - (gutter.IsComposite && wEff <= gutter.W ? gutter.Sw : i.Sx) * wEff / 2);
        double CurbCap(double dCurb, double len) => InletHydraulics.CurbSag(Math.Max(0, dCurb - aCont), dCurb + aLoc, len, wDep, h, depressed);
        double GrateCap(double dCurb) => InletHydraulics.GrateSag(GrateDepth(dCurb), lEff, wEff, (i.Area ?? 0) * (1 - i.Clog));
        bool GrateWeirRegime(double dCurb) => InletHydraulics.GrateWeir(GrateDepth(dCurb), lEff, wEff) <= InletHydraulics.GrateOrifice(GrateDepth(dCurb), (i.Area ?? 0) * (1 - i.Clog));
        double lcS = i.Lc ?? i.L;
        Func<double, double> cap = i.Type switch
        {
            "curb" => dd => CurbCap(dd, i.L),
            "grate" => GrateCap,
            // HEC-22 §4.4.5.4: weir flow = grate alone; orifice flow = grate orifice + curb-opening orifice (Eq. 4-34).
            _ => dd => GrateWeirRegime(dd) ? GrateCap(dd) : GrateCap(dd) + InletHydraulics.CurbOrifice(dd + aLoc, lcS, h),
        };
        double dc = InletHydraulics.SolveDepth(q, cap);
        if (double.IsNaN(dc)) { error = $"{q:F2} cfs would need more than 5 ft of ponding; the inlet is far too small."; return null; }
        double ts = gutter.SpreadAtDepth(dc);
        string regime, det;
        if (i.Type == "curb")
        {
            double di = dc + aLoc;
            regime = !(h > 0) || di <= h ? "sag, curb opening, weir" : di >= 1.4 * h ? "sag, curb opening, orifice" : "sag, curb opening, transition (h < di < 1.4h)";
            det = $"depth at curb d = {F(dc)} ft, at the lip di = {F(di)} ft, h = {F(h)} ft; " + (depressed && i.L <= 12 ? $"weir Cw 2.3 (L + 1.8W), W = {F(wDep)} ft, d from normal cross slope = {F(Math.Max(0, dc - aCont))} ft" : "weir 3.0·L·d^1.5") + $"; orifice 0.67·h·L·√(2g(di − h/2)); T = {F(ts)} ft";
        }
        else
        {
            double dg = GrateDepth(dc), ag = (i.Area ?? 0) * (1 - i.Clog);
            bool weir = GrateWeirRegime(dc);
            regime = $"sag, {i.Type}, grate {(weir ? "weir" : "orifice")}";
            det = $"depth at curb d = {F(dc)} ft, average over grate {F(dg)} ft; grate L = {F(lEff)}, W = {F(wEff)}, A = {F(ag)} ft² after {i.Clog * 100:0.#} % clogging; P = L + 2W = {F(lEff + 2 * wEff)} ft; weir/orifice change at d = 1.79·A/P = {F(1.79 * ag / (lEff + 2 * wEff))} ft"
                  + (i.Type == "combination" ? (weir ? "; weir flow: grate alone (HEC-22 §4.4.5.4)" : $"; orifice flow: grate + curb orifice, Lc = {F(lcS)} ft, h = {F(h)} ft") : "") + $"; T = {F(ts)} ft";
        }
        return new Outcome(q, ts, dc, double.NaN, i.Type == "curb" ? i.L : lEff, q > 0 ? 1 : double.NaN, q, 0, regime, det);
    }

    /// <summary>Curb opening on grade: Se = Sx + S'w·Eo (HEC-22 Eq. 4-24), S'w = a/W over the depressed width; LT (Eq. 4-22); E (Eq. 4-23).</summary>
    private static double CurbOnGrade(Inlet i, double q, double s, GutterSection gutter, double t, double l, out string det)
    {
        double wDep = i.W is > 0 ? i.W.Value : gutter.W;
        double aTot = gutter.Depression + i.ALocal;
        double eo = wDep > 0 ? gutter.FrontalRatio(t, wDep) : 0;
        double swp = wDep > 0 ? aTot / wDep : 0;
        double se = i.Sx + swp * eo;
        double lt = InletHydraulics.CurbLengthTotal(q, s, i.N, se);
        double e = InletHydraulics.CurbEfficiency(l, lt);
        det = $"S'w = a/W = {F(aTot, "0.####")}/{F(wDep)} = {F(swp, "0.####")}, Eo = {F(eo, "0.0000")}, Se = {F(se, "0.0000")}; LT = 0.6·Q^0.42·S^0.3·(1/(n·Se))^0.6 = {F(lt)} ft; E = 1 − (1 − L/LT)^1.8 = {F(e, "0.0000")}";
        return e * q;
    }
}
