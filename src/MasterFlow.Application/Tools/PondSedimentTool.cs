using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Sediment accumulation and clean-out planning for detention, retention and water quality ponds: the annual sediment
/// load (yield per acre, or a TSS event mean concentration on the annual runoff Rv·P·A), Brune trap efficiency
/// (Dendy 1974 fit of the median curve) or a given one, deposited density by Lara and Pemberton with Miller's
/// consolidation, the split between forebay and main pool, years to the clean-out trigger and the accumulation by year.
/// </summary>
public sealed class PondSedimentTool : IDesignTool
{
    public string Id => "pond-sediment";
    public string Name => "Pond sediment accumulation and clean-out interval (forebay and main pool)";
    public string Description => "Annual sediment load to a pond from a yield (t/ac/yr) by land use and a construction phase, or from a TSS event mean concentration on the annual runoff (Rv = 0.05 + 0.009·I, Schueler); trap efficiency from the Brune median curve (Dendy fit TE = 0.97^(0.19^log(C/I))) or given; deposited dry density by Lara and Pemberton from the sand, silt and clay fractions and the pond's operation type, with Miller's consolidation over time; the share caught in the forebay; years until the forebay and the main pool reach the clean-out trigger (half full by default), sediment depth per year, and accumulation over the design life.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Pond sediment and clean-out interval. U.S. units: acres, inches, ft³, lb/ft³.
        drainage area = 40          # ac
        impervious = 45             # % (for Rv = 0.05 + 0.009·I)
        annual precipitation = 17   # in/yr
        tss = 120                   # mg/L event mean concentration (or give the [Loads] table of yields)
        pool volume = 2.5           # ac-ft, permanent pool + storage below the outlet (for Brune C/I); or trap efficiency = 80
        operation = submerged       # submerged | drawdown | considerable drawdown | empty (dry pond)
        sand = 30                   # % of the trapped sediment by mass
        silt = 50
        clay = 20
        forebay volume = 1500       # ft³ of sediment storage in the forebay
        forebay area = 900          # ft² (for depth per year)
        forebay capture = 50        # % of the trapped sediment that settles in the forebay
        sediment storage = 6000     # ft³ set aside for sediment in the main pool
        pool area = 15000           # ft²
        wq volume = 30000           # ft³ water quality volume (reports the forebay as a share of it)
        cleanout at = 50            # % full that triggers a clean-out
        design life = 30            # years
        construction area = 12      # ac disturbed while the site builds out
        construction yield = 4      # t/ac/yr reaching the pond while disturbed, after the site's erosion controls (from RUSLE, e.g. the erosion and sediment control tool)
        construction years = 2
        """;

    /// <summary>Brune median trap efficiency (fraction), Dendy (1974) fit: TE = 0.97^(0.19^log10(C/I)).</summary>
    public static double BruneTrap(double capacityToInflow) =>
        capacityToInflow <= 0 ? 0 : Math.Min(1, Math.Pow(0.97, Math.Pow(0.19, Math.Log10(capacityToInflow))));

    /// <summary>Lara–Pemberton initial unit weights (lb/ft³) and Miller consolidation constants K for clay, silt and sand by operation type 1–4.</summary>
    public static (double Wc, double Wm, double Ws, double Kc, double Km, double Ks) LaraPemberton(int type) => type switch
    {
        1 => (26, 70, 97, 16, 5.7, 0),
        2 => (35, 71, 97, 10, 1.8, 0),
        3 => (40, 72, 97, 0, 0, 0),
        _ => (60, 73, 97, 0, 0, 0),
    };

    /// <summary>Average dry unit weight over T years (Miller 1953): W_T = W1 + 0.4343·K·[T/(T−1)·ln T − 1].</summary>
    public static double MillerAverage(double w1, double k, double years) =>
        years <= 1 ? w1 : w1 + 0.4343 * k * (years / (years - 1) * Math.Log(years) - 1);

    /// <summary>Annual runoff volume (ac-ft) by the simple method: Rv·P·A/12, Rv = 0.05 + 0.009·I.</summary>
    public static double AnnualRunoff(double areaAc, double imperviousPct, double precipIn) => (0.05 + 0.009 * imperviousPct) * precipIn * areaAc / 12;

    /// <summary>Annual TSS load (tons) from an event mean concentration on a runoff volume: 2.7194 lb per (mg/L·ac-ft).</summary>
    public static double TssTons(double emcMgL, double runoffAcFt) => emcMgL * runoffAcFt * 2.7194 / 2000;

    /// <summary>Deposited volume (ft³) after <paramref name="years"/> given a cumulative trapped mass (tons) function.</summary>
    public static double Volume(Func<double, double> tonsBy, double years, double w1, double k) =>
        years <= 0 ? 0 : tonsBy(years) * 2000 / MillerAverage(w1, k, Math.Max(1, years));

    /// <summary>Years until the deposited volume reaches <paramref name="target"/> ft³ (bisection), or ∞ beyond 500 years.</summary>
    public static double YearsTo(Func<double, double> tonsBy, double target, double w1, double k)
    {
        if (target <= 0) return 0;
        double lo = 0, hi = 500;
        if (Volume(tonsBy, hi, w1, k) < target) return double.PositiveInfinity;
        for (int i = 0; i < 100; i++) { double m = 0.5 * (lo + hi); if (Volume(tonsBy, m, w1, k) < target) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Pond sediment accumulation and clean-out interval";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        if (context.Units == UnitSystem.SI) msgs.Add(CalcMessage.Info("I-PSD-001", "This tool works in U.S. units: acres, inches, ft³, ft² and lb/ft³."));
        double area = p.Number("drainage area", double.NaN), imp = p.Number("impervious", 0), precip = p.Number("annual precipitation", double.NaN);
        double? tss = p.OptionalNumber("tss"), poolAcFt = p.OptionalNumber("pool volume"), teGiven = p.OptionalNumber("trap efficiency");
        string op = (p.Text("operation") ?? "submerged").ToLowerInvariant();
        int type = op switch { "submerged" => 1, "drawdown" => 2, "considerable drawdown" => 3, "empty" or "dry" => 4, _ => 0 };
        double sand = p.Number("sand", 30), silt = p.Number("silt", 50), clay = p.Number("clay", 20);
        double? fbVol = p.OptionalNumber("forebay volume"), fbArea = p.OptionalNumber("forebay area"), poolStore = p.OptionalNumber("sediment storage"),
            poolArea = p.OptionalNumber("pool area"), wqv = p.OptionalNumber("wq volume");
        double fbCapture = p.Number("forebay capture", fbVol is null ? 0 : 50) / 100, trigger = p.Number("cleanout at", 50) / 100, life = p.Number("design life", 25);
        double cArea = p.Number("construction area", 0), cYield = p.Number("construction yield", 0), cYears = p.Number("construction years", 0);

        var loads = new List<(string Name, double Ac, double Yield)>();
        foreach (var r in p.Rows("Loads"))
        {
            string name = r.Length > 0 ? r[0] : "";
            if (p.Cell(r, 1, "area", $"[Loads] {name}") is { } a && p.Cell(r, 2, "yield", $"[Loads] {name}") is { } y) loads.Add((name, a, y));
        }
        if (!(area > 0)) msgs.Add(CalcMessage.Error("E-PSD-001", "Give the drainage area (ac) above zero."));
        if (!(precip > 0)) msgs.Add(CalcMessage.Error("E-PSD-002", "Give the annual precipitation (in/yr) above zero."));
        if (imp is < 0 or > 100) msgs.Add(CalcMessage.Error("E-PSD-003", "impervious must be 0 to 100 %."));
        if (tss is null && loads.Count == 0) msgs.Add(CalcMessage.Error("E-PSD-004", "Give tss = (mg/L event mean concentration) or a [Loads] table (name, area ac, yield t/ac/yr)."));
        if (tss is not null && loads.Count > 0) msgs.Add(CalcMessage.Error("E-PSD-005", "Give either tss or [Loads], not both."));
        if (tss is <= 0 || loads.Any(l => l.Ac < 0 || l.Yield < 0)) msgs.Add(CalcMessage.Error("E-PSD-006", "Concentrations, areas and yields must not be negative (tss above zero)."));
        if (teGiven is null && !(poolAcFt > 0)) msgs.Add(CalcMessage.Error("E-PSD-007", "Give the pool volume (ac-ft) for the Brune trap efficiency, or trap efficiency = (%)."));
        if (teGiven is not (null or (> 0 and <= 100))) msgs.Add(CalcMessage.Error("E-PSD-008", "trap efficiency must be above 0 and at most 100 %."));
        if (type == 0) msgs.Add(CalcMessage.Error("E-PSD-009", "operation must be submerged, drawdown, considerable drawdown or empty."));
        if (sand < 0 || silt < 0 || clay < 0 || Math.Abs(sand + silt + clay - 100) > 0.5) msgs.Add(CalcMessage.Error("E-PSD-010", $"sand + silt + clay must add to 100 % (now {sand + silt + clay:G4})."));
        if (fbCapture is < 0 or > 1 || trigger is <= 0 or > 1) msgs.Add(CalcMessage.Error("E-PSD-011", "forebay capture must be 0 to 100 % and cleanout at above 0 and at most 100 %."));
        if (fbVol is <= 0 || poolStore is <= 0 || fbArea is <= 0 || poolArea is <= 0) msgs.Add(CalcMessage.Error("E-PSD-012", "Volumes and areas must be above zero."));
        if (fbVol is null && poolStore is null) msgs.Add(CalcMessage.Error("E-PSD-013", "Give the forebay volume, the main pool sediment storage, or both (ft³)."));
        if (fbCapture > 0 && fbVol is null) msgs.Add(CalcMessage.Error("E-PSD-014", "forebay capture needs a forebay volume."));
        if (!(life >= 1)) msgs.Add(CalcMessage.Error("E-PSD-015", "design life must be at least 1 year."));
        if (cArea < 0 || cYield < 0 || cYears < 0 || cArea > area) msgs.Add(CalcMessage.Error("E-PSD-016", "Construction area (at most the drainage area), yield and years must not be negative."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double runoff = AnnualRunoff(area, imp, precip);
        double rv = 0.05 + 0.009 * imp;
        double baseTons = tss is { } c ? TssTons(c, runoff) : loads.Sum(l => l.Ac * l.Yield);
        if (loads.Count > 0 && Math.Abs(loads.Sum(l => l.Ac) - area) > 0.01 * area)
            msgs.Add(CalcMessage.Warning("W-PSD-001", $"The [Loads] areas add to {loads.Sum(l => l.Ac):G4} ac, not the {area:G4} ac drainage area."));
        double extraTons = cYears > 0 ? cArea * cYield : 0;   // added on top of the developed load while disturbed
        double ci = poolAcFt is { } pv ? pv / runoff : double.NaN;
        double te = teGiven is { } tg ? tg / 100 : BruneTrap(ci);
        if (teGiven is null && type == 4)
            msgs.Add(CalcMessage.Warning("W-PSD-002", "The Brune curve is for reservoirs with a pool; a dry pond traps less. Give trap efficiency = (%) from a settling analysis for a dry pond."));
        if (teGiven is null && ci < 0.002)
            msgs.Add(CalcMessage.Info("I-PSD-002", $"C/I = {ci:G3} is at the low end of the Brune curve, where it is least reliable."));
        Func<double, double> tonsBy = t => te * (baseTons * t + extraTons * Math.Min(t, cYears));
        var lp = LaraPemberton(type);
        double w1 = (lp.Wc * clay + lp.Wm * silt + lp.Ws * sand) / 100, k = (lp.Kc * clay + lp.Km * silt + lp.Ks * sand) / 100;
        Func<double, double> fbTons = t => fbCapture * tonsBy(t), poolTons = t => (1 - fbCapture) * tonsBy(t);
        double annualTrap = te * baseTons;

        var summary = new List<string>
        {
            $"Annual runoff {runoff:F2} ac-ft (Rv {rv:F3}); sediment load {baseTons:F2} t/yr{(extraTons > 0 ? $" plus {extraTons:F1} t/yr for {cYears:G3} years of construction" : "")}; trap efficiency {te * 100:F0} %{(teGiven is null ? $" (Brune, C/I = {ci:G3})" : " (given)")}.",
        };
        List<IReadOnlyList<object?>> rows =
        [
            ["Annual runoff Rv·P·A/12", R(runoff, 3), "ac-ft/yr"], ["Runoff coefficient Rv = 0.05 + 0.009·I", R(rv, 3), ""],
            [tss is { } c2 ? $"Sediment load from TSS {c2:G4} mg/L" : "Sediment load from the [Loads] yields", R(baseTons, 3), "t/yr"],
        ];
        if (extraTons > 0) rows.Add([$"Construction load ({cArea:G4} ac × {cYield:G4} t/ac/yr) for {cYears:G3} years", R(extraTons, 2), "t/yr"]);
        if (teGiven is null) rows.Add(["Capacity to inflow ratio C/I", R(ci, 4), ""]);
        rows.Add(["Trap efficiency", R(te * 100, 1), "%"]);
        rows.Add(["Sediment trapped (developed)", R(annualTrap, 3), "t/yr"]);
        rows.Add(["Initial dry unit weight W1 (Lara–Pemberton)", R(w1, 1), "lb/ft³"]);
        rows.Add(["Consolidation constant K (Miller)", R(k, 2), "lb/ft³"]);
        rows.Add([$"Average unit weight over {life:G3} years", R(MillerAverage(w1, k, life), 1), "lb/ft³"]);
        rows.Add(["Deposited volume in the first year", R(Volume(tonsBy, 1, w1, k), 0), "ft³"]);
        var tables = new List<ToolTable>
        {
            new("Sediment load and trapping", ["Item", "Value", "Unit"], rows,
            [
                "TSS load = EMC (mg/L) × annual runoff (ac-ft) × 2.7194 lb/(mg/L·ac-ft) / 2000.",
                "Brune median curve, Dendy (1974) fit: TE = 0.97^(0.19^log10(C/I)), C = pool capacity, I = annual inflow.",
                "Lara and Pemberton (1963) initial unit weights by operation type; Miller (1953) average over T years: W_T = W1 + 0.4343·K·[T/(T−1)·ln T − 1].",
            ]),
        };

        // Clean-out triggers.
        var clean = new List<IReadOnlyList<object?>>();
        double? fbYears = null, poolYears = null;
        var post = new List<(string Where, double Years)>();
        void Trigger(string where, double vol, double? surf, double frac, Action<double> set)
        {
            Func<double, double> tons = t => frac * tonsBy(t), dev = t => frac * annualTrap * t;
            double target = trigger * vol, yrs = YearsTo(tons, target, w1, k), yrsDev = YearsTo(dev, target, w1, k);
            set(yrs);
            post.Add((where, yrsDev));
            double vol1 = Volume(dev, 1, w1, k);
            clean.Add([where, R(vol, 0), $"{frac * 100:G3} %", R(target, 0), double.IsFinite(yrs) ? R(yrs, 1) : null, double.IsFinite(yrsDev) ? R(yrsDev, 1) : null,
                R(vol1, 0), surf is { } s ? R(vol1 / s * 12, 2) : null]);
        }
        if (fbVol is { } fv) Trigger("Forebay", fv, fbArea, fbCapture, y => fbYears = y);
        if (poolStore is { } ps) Trigger("Main pool", ps, poolArea, 1 - fbCapture, y => poolYears = y);
        tables.Add(new("Clean-out interval", ["Storage", "Sediment volume (ft³)", "Share of trapped sediment", $"Trigger at {trigger * 100:G3} % (ft³)",
                "Years to trigger from today", "Interval once developed (years)", "Deposit per developed year (ft³)", "Depth per developed year (in)"], clean,
            ["Years from today include the construction load in the first years; the developed interval is the repeating interval after a clean-out at the end of construction.",
             "Both include the consolidation of older deposits (Miller)."]));
        string Yr(double? y) => y is { } v && double.IsFinite(v) ? $"{v:F1} years" : "more than 500 years";
        foreach (var (where, yrs) in post)
        {
            double first = where == "Forebay" ? fbYears!.Value : poolYears!.Value;
            summary.Add($"{where} reaches {trigger * 100:G3} % full in {Yr(first)}{(extraTons > 0 ? $"; once developed, every {Yr(yrs)}" : "")}.");
            if (yrs < 1) msgs.Add(CalcMessage.Warning("W-PSD-003", $"The {where.ToLowerInvariant()} fills to the trigger in {yrs:F1} years once developed; clean it more than once a year or enlarge it."));
            if (where == "Main pool" && first < life) msgs.Add(CalcMessage.Warning("W-PSD-004", $"The main pool needs a clean-out before the {life:G3}-year design life (in {first:F1} years); budget for it in the maintenance plan."));
        }
        if (extraTons > 0 && post.Any(x => (x.Where == "Forebay" ? fbYears : poolYears) < cYears))
            msgs.Add(CalcMessage.Info("I-PSD-003", "Storage fills during construction: plan clean-outs during and at the end of construction."));
        if (fbVol is { } fv2 && wqv is { } wq)
        {
            tables.Add(new("Forebay size", ["Item", "Value", "Unit"],
                [["Forebay sediment volume", R(fv2, 0), "ft³"], ["Water quality volume", R(wq, 0), "ft³"], ["Forebay as a share of the WQ volume", R(fv2 / wq * 100, 1), "%"]],
                ["Compare with the forebay size the governing manual requires."]));
        }

        // Accumulation by year.
        int n = (int)Math.Ceiling(life);
        var acc = new List<IReadOnlyList<object?>>();
        var sFb = new List<(double, double)>(); var sPool = new List<(double, double)>();
        for (int y = 0; y <= n; y++)
        {
            double vf = fbVol is null ? 0 : Volume(fbTons, y, w1, k), vp = poolStore is null ? 0 : Volume(poolTons, y, w1, k);
            acc.Add([y, R(tonsBy(y), 2), fbVol is null ? null : R(vf, 0), poolStore is null ? null : R(vp, 0)]);
            sFb.Add((y, vf)); sPool.Add((y, vp));
        }
        tables.Add(new("Accumulation without clean-out", ["Year", "Trapped (t)", "Forebay (ft³)", "Main pool (ft³)"], acc));
        var series = new List<ChartSeries>();
        var lines = new List<ChartLine>();
        if (fbVol is { } f3) { series.Add(new("Forebay", sFb)); lines.Add(new("Forebay trigger", trigger * f3)); }
        if (poolStore is { } p3) { series.Add(new("Main pool", sPool, Dashed: true)); lines.Add(new("Main pool trigger", trigger * p3)); }
        var charts = new List<ToolChart> { new("Sediment accumulation", "Year", "Deposited volume (ft³)", series, lines) };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
