using MasterFlow.Application.WaterQuality;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Water quality and LID sizing (premium). By default it follows the City of Rapid City Storm Water Quality Manual (2009),
/// which the IDCM (2012) §4.1.7 makes the water quality standard (precedence IDCM, then the Storm Water Quality Manual,
/// then the Drainage Criteria Manual): WQCV by §2.2.2, design credits by §2.4.2, and the structural BMPs of §2.3 with
/// their drain times and limits. <c>criteria = mhfd</c> switches to the MHFD USDCM Vol. 3 equations with every value an input.
/// </summary>
public sealed class WaterQualityTool : IDesignTool
{
    public string Id => "water-quality";
    public string Name => "Water quality and LID sizing (WQCV, EDB, sand filter, wetland, bioretention, infiltration)";
    public string Description => "Water quality capture volume by sub-basin and BMP sizing to the Rapid City Storm Water Quality Manual (2009): WQCV §2.2.2 with design credits §2.4.2; porous landscape detention, extended detention basin orifice plate, sand filter extended detention basin, constructed wetland basin and bioretention (§2.3) with the manual's drain times and limits; infiltration is flagged as needing approval (§2.3.10). `criteria = mhfd` uses the MHFD USDCM Vol. 3 equations instead. Reports the share of impervious area treated.";
    public bool UsesNetwork => false;

    private static readonly string[] Types = ["edb", "sand filter", "constructed wetland", "porous landscape detention", "bioretention", "infiltration"];

    /// <summary>
    /// Rapid City SWQM drain time (h) of each WQCV BMP. Bioretention has none in §2.3.9; Kyle set 48 h for the surface
    /// ponding (2026-11-20), which takes a = 1.0, the manual's longest category (40 h).
    /// </summary>
    private static double? RapidCityDrain(string type) => type switch
    {
        "edb" or "sand filter" => 40, "constructed wetland" => 24, "porous landscape detention" => 12, "bioretention" => 48, _ => null,
    };

    private static string Section(string type) => type switch
    {
        "porous landscape detention" => "§2.3.1", "edb" => "§2.3.2", "sand filter" => "§2.3.3", "constructed wetland" => "§2.3.4", "bioretention" => "§2.3.9", _ => "§2.3.10",
    };

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Water quality capture volume and BMP sizing to the Rapid City Storm Water Quality Manual (2009), SI (ha, m, m³).
        criteria = rapid city       # or mhfd

        [Subbasins]
        Name	Area (ha)	Imperviousness (%)	Credit (ha)	BMP
        A1	2.5	65	0	Pond 1
        A2	1.4	45	0.2	Pond 1
        B1	0.45	90		Rain garden
        E1	0.8	10

        [BMP Pond 1]
        type = edb
        row spacing = 100           # mm
        Elevation	Area
        30.00	150
        30.50	330
        31.00	560
        31.50	840
        32.00	1170

        [BMP Rain garden]
        type = bioretention
        ponding depth = 300         # mm
        media depth = 1220          # mm (4 ft)
        permeability = 0.3          # m/day
        media porosity = 0.2
        """ : """
        # Water quality capture volume and BMP sizing to the Rapid City Storm Water Quality Manual (2009), U.S. units.
        # WQCV = 1.16·a·(0.91I³ − 1.191I² + 0.78I) (§2.2.2); drain times and limits from §2.3. IDCM §4.1.7 adopts this manual.
        criteria = rapid city       # or mhfd (MHFD USDCM Vol. 3, every value an input)

        # Credit = area routed through conservation areas, riparian or vegetated buffers or swales (§2.4.2); it is
        # taken off the area, not the imperviousness.
        [Subbasins]
        Name	Area (ac)	Imperviousness (%)	Credit (ac)	BMP
        A1	6.2	65	0	Pond 1
        A2	3.4	45	0.5	Pond 1
        W1	8.0	40	0	Wetland
        B1	1.1	90		Rain garden
        C1	0.8	80		Sand filter
        L1	0.6	85		Island
        E1	2.0	10

        # Extended detention basin (§2.3.2): 120 % of the WQCV above the lowest perforation; the plate releases the
        # WQCV over 40 h with no more than half of it in 12 h. Stage–area by contours (conic method).
        [BMP Pond 1]
        type = edb
        row spacing = 4             # in. between orifice rows (the manual's charts use 4 in.)
        orifice coefficient = 0.6
        Elevation	Area
        100.0	2000
        101.0	3500
        102.0	5200
        103.0	7200
        104.0	9500

        # Constructed wetland basin (§2.3.4): WQCV surcharge over 24 h above the permanent pool; pool ≥ 75 % of the
        # WQCV; surcharge ≤ 2 ft.
        [BMP Wetland]
        type = constructed wetland
        permanent pool = 100.0      # ft, water surface of the permanent pool (lowest perforation)
        Elevation	Area
        97.0	3000
        98.0	5000
        99.0	7000
        100.0	9000
        101.0	11000
        102.0	13000

        # Bioretention (§2.3.9): ponding ≤ 6 in., or ≤ 12 in. with collector pipes; planting soil ≥ 4 ft. The ponding
        # drains in 48 h (a = 1.0); with a drainable porosity or gravel depth, the bed is checked to drain within 72 h.
        [BMP Rain garden]
        type = bioretention
        ponding depth = 12          # in.
        media depth = 48            # in. of planting soil
        permeability = 1.0          # ft/day, planting soil
        media porosity = 0.2        # drainable porosity of the soil (omit to skip the bed check)
        gravel depth = 12           # in. of gravel bed around the underdrain
        runoff coefficient = 0.85   # Rational C, for the 5–7 % area guideline

        # Sand filter extended detention basin (§2.3.3): 100 % WQCV over 40 h, depth ≤ 3 ft, 18 in. of sand.
        [BMP Sand filter]
        type = sand filter
        ponding depth = 36          # in.
        media depth = 18            # in.
        permeability = 3.5          # ft/day, for the drain-time check

        # Porous landscape detention (§2.3.1): 12 h WQCV, average depth 6–12 in., 18 in. sand-peat.
        [BMP Island]
        type = porous landscape detention
        average depth = 9           # in.
        media depth = 18            # in.
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Water quality capture volume and BMP sizing";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        bool us = context.Units == UnitSystem.USCustomary;
        var c = UnitConstants.For(context.Units);
        // Base units: ft or m; volumes ft³ or m³. Input: ac or ha; in. or mm; ft/day or m/day; in./hr or mm/hr.
        string L = us ? "ft" : "m", V = us ? "cf" : "m³", A = us ? "ac" : "ha", Ar = us ? "sf" : "m²", small = us ? "in." : "mm", rate = us ? "in./hr" : "mm/hr", kU = us ? "ft/day" : "m/day";
        double areaK = us ? 43560 : 10000, smallK = us ? 1.0 / 12 : 0.001, inchToSmall = us ? 1 : 25.4;
        double Ft(double feet) => us ? feet : feet * 0.3048;                 // a manual limit in feet, in base units
        string crit = (p.Text("criteria") ?? "rapid city").Trim().ToLowerInvariant();
        bool rc = crit is "rapid city" or "rapidcity" or "rapid city 2009";
        if (!rc && crit != "mhfd") msgs.Add(CalcMessage.Error("E-WQ-010", $"criteria \"{crit}\" is not one of: rapid city, mhfd."));
        double d6 = p.Number("regional depth", us ? 0.43 : 10.92) / inchToSmall;   // inches (MHFD only)
        if (!rc && !(d6 > 0)) msgs.Add(CalcMessage.Error("E-WQ-001", "Regional depth (d6) must be more than zero."));
        if (rc && p.Has("regional depth")) msgs.Add(CalcMessage.Warning("W-WQ-008", "regional depth is ignored under the Rapid City criteria: the manual's equation has its own 1.16 factor (§2.2.2). Use criteria = mhfd to scale by d6."));

        // ---- sub-basins
        var head = p.Header("Subbasins");
        int Col(string word, int fallback)
        {
            for (int i = 0; i < head.Count; i++)
                if (head[i].Split([' ', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(word, StringComparison.OrdinalIgnoreCase))) return i;
            return fallback;
        }
        int cCredit = Col("credit", -1);
        int cArea = Col("area", 1), cImp = Col("imperv", 2), cBmp = Col("bmp", cCredit == 3 ? 4 : 3);
        var subs = new List<(string Name, double Area, double Imp, double Credit, string? Bmp)>();
        foreach (var r in p.Rows("Subbasins"))
        {
            string name = r[0];
            double? a = p.Cell(r, cArea, "area", name), i = p.Cell(r, cImp, "imperviousness", name);
            double cr = cCredit >= 0 && cCredit < r.Length && r[cCredit].Length > 0 ? p.Cell(r, cCredit, "credit", name) ?? double.NaN : 0;
            string? b = cBmp < r.Length && r[cBmp].Length > 0 ? r[cBmp] : null;
            if (a is not { } av || i is not { } iv || double.IsNaN(cr)) { if (!p.Errors.Any()) msgs.Add(CalcMessage.Error("E-WQ-002", $"Sub-basin {name}: area and imperviousness are both needed.")); continue; }
            if (!(av > 0)) { msgs.Add(CalcMessage.Error("E-WQ-002", $"Sub-basin {name}: area must be more than zero.")); continue; }
            if (iv < 0 || iv > 100) { msgs.Add(CalcMessage.Error("E-WQ-002", $"Sub-basin {name}: imperviousness {iv:G4} % must be 0 to 100 (percent).")); continue; }
            if (cr < 0 || cr >= av) { msgs.Add(CalcMessage.Error("E-WQ-002", $"Sub-basin {name}: design credit area {cr:G4} must be zero or more and less than the area {av:G4} (§2.4.2).")); continue; }
            if (iv > 0 && iv <= 1 && !msgs.Any(m => m.Code == "W-WQ-001")) msgs.Add(CalcMessage.Warning("W-WQ-001", $"Sub-basin {name}: imperviousness {iv:G4} is read as percent ({iv:G4} %), not as a fraction."));
            subs.Add((name, av, iv / 100, cr, b));
        }
        if (subs.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-WQ-003", "Add sub-basins under [Subbasins]: Name, Area, Imperviousness (%), Credit (optional), BMP."));

        // ---- BMPs
        var bmpSections = p.Sections.Where(s => s.StartsWith("BMP ", StringComparison.OrdinalIgnoreCase)).ToList();
        var bmpNames = bmpSections.ToDictionary(s => s[4..].Trim(), s => s, StringComparer.OrdinalIgnoreCase);
        foreach (var s in subs.Where(s => s.Bmp is { } b && !bmpNames.ContainsKey(b)))
            msgs.Add(CalcMessage.Error("E-WQ-004", $"Sub-basin {s.Name} drains to BMP \"{s.Bmp}\", which has no [BMP {s.Bmp}] section."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        string Type(string sec)
        {
            string t = (p.Text(sec + ".type") ?? "").Trim().ToLowerInvariant();
            return t switch { "wetland" or "wetland basin" or "constructed wetland basin" => "constructed wetland", "pld" or "landscape detention" => "porous landscape detention", "sand filter edb" or "sand filter extended detention basin" => "sand filter", "extended detention basin" => "edb", _ => t };
        }
        double Coef(double hours) => rc ? Wq.RapidCityDrainCoefficient(hours) : Wq.DrainCoefficient(hours);
        double Inches(double imp, double a) => rc ? Wq.WqcvRapidCity(imp, a) : Wq.WqcvInches(imp, a, d6);
        double ToVolume(double inches, double area) => inches * (us ? 1.0 / 12 : 0.0254) * area * areaK;
        var drainOf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, sec) in bmpNames)
        {
            string type = Type(sec);
            double def = rc ? RapidCityDrain(type) ?? 12 : type switch { "edb" => 40, "constructed wetland" => 24, _ => 12 };   // MHFD: 12 h filters and rain gardens
            double given = p.Number(sec + ".wqcv drain time", def, $"{name}: wqcv drain time");
            if (rc && RapidCityDrain(type) is { } fixedH && p.Has(sec + ".wqcv drain time") && Math.Abs(given - fixedH) > 1e-9)
            {
                msgs.Add(CalcMessage.Warning("W-WQ-007", $"BMP {name}: the Storm Water Quality Manual sets a {fixedH:G4} h drain time for this BMP ({Section(type)}); {given:G4} h is ignored."));
                given = fixedH;
            }
            drainOf[name] = given;
        }

        var subRows = new List<IReadOnlyList<object?>>();
        var bmpRows = new List<IReadOnlyList<object?>>();
        var tables = new List<ToolTable>();
        var charts = new List<ToolChart>();
        foreach (var s in subs)
        {
            double a = s.Bmp is { } b ? Coef(drainOf[b]) : 1.0;
            double inches = Inches(s.Imp, a);
            subRows.Add([s.Name, s.Area, s.Credit, Math.Round(s.Area - s.Credit, 4), Math.Round(100 * s.Imp, 1), s.Bmp ?? "(untreated)", Math.Round(a, 3), Math.Round(inches * inchToSmall, 4), Math.Round(ToVolume(inches, s.Area - s.Credit), 0)]);
        }

        foreach (var (name, sec) in bmpNames)
        {
            var mine = subs.Where(s => string.Equals(s.Bmp, name, StringComparison.OrdinalIgnoreCase)).ToList();
            string type = Type(sec);
            if (!Types.Contains(type)) { msgs.Add(CalcMessage.Error("E-WQ-005", $"BMP {name}: type \"{type}\" is not one of {string.Join(", ", Types)}.")); continue; }
            if (mine.Count == 0) { msgs.Add(CalcMessage.Warning("W-WQ-002", $"BMP {name}: no sub-basin drains to it; not sized.")); continue; }
            // Composite imperviousness over the mapped area (§2.2.2 step 2); the volume uses the effective area (§2.4.2).
            double area = mine.Sum(x => x.Area), effArea = mine.Sum(x => x.Area - x.Credit), imp = mine.Sum(x => x.Area * x.Imp) / area;
            double hours = drainOf[name];
            if (!(hours > 0)) { msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: wqcv drain time must be more than zero.")); continue; }
            double a = Coef(hours), inches = Inches(imp, a);
            double wqcv = ToVolume(inches, effArea);   // ft³ or m³
            string sizeText; string check = "OK";
            string sect = rc ? $" (SWQM {Section(type)})" : "";
            double Num(string key, double def, string what) => p.Number(sec + "." + key, def, $"{name}: {what}");
            bool Bad(string what, double v, bool zeroOk = false)
            {
                if (double.IsFinite(v) && (v > 0 || (zeroOk && v == 0))) return false;
                msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: {what} must be {(zeroOk ? "zero or more" : "more than zero")} (got {v:G6}).")); return true;
            }
            void Limit(string text) { check = "CHECK"; msgs.Add(CalcMessage.Warning("W-WQ-010", $"BMP {name}: {text}")); }
            List<(double, double)>? Stages()
            {
                var stages = new List<(double, double)>();
                foreach (var r in p.Rows(sec))
                    if (p.Cell(r, 0, "elevation", name) is { } e && p.Cell(r, 1, "area", name) is { } ar)
                    {
                        if (stages.Count > 0 && e <= stages[^1].Item1) { msgs.Add(CalcMessage.Error("E-WQ-007", $"BMP {name}: stage elevations must increase.")); return null; }
                        if (ar < 0) { msgs.Add(CalcMessage.Error("E-WQ-007", $"BMP {name}: stage area cannot be negative.")); return null; }
                        stages.Add((e, ar));
                    }
                if (stages.Count < 2) { msgs.Add(CalcMessage.Error("E-WQ-007", $"BMP {name}: give at least two Elevation, Area rows.")); return null; }
                return stages;
            }
            double toSq = us ? 144 : 1e6, toSm = us ? 12 : 1000;
            object[] Row(string label, double design, string size) => [name, label, Math.Round(effArea, 3), Math.Round(100 * imp, 1), hours, Math.Round(a, 3), Math.Round(inches * inchToSmall, 4), Math.Round(wqcv, 0), Math.Round(design, 0), size, check];

            switch (type)
            {
                case "edb" or "constructed wetland":
                {
                    bool wet = type == "constructed wetland";
                    double sed = wet ? 0 : Num("sediment allowance", 20, "sediment allowance");
                    if (rc && !wet && p.Has(sec + ".sediment allowance") && Math.Abs(sed - 20) > 1e-9) { msgs.Add(CalcMessage.Warning("W-WQ-007", $"BMP {name}: the manual requires storage of 120 % of the WQCV (§2.3.2 step 1); a sediment allowance of {sed:G4} % is ignored.")); sed = 20; }
                    double spacing = Num("row spacing", us ? 4 : 100, "row spacing") * smallK, cd = Num("orifice coefficient", 0.6, "orifice coefficient");
                    double maxHole = Num("maximum hole", us ? 2 : 50, "maximum hole") * smallK;
                    // Fraction of the stored volume that must leave in the drain time: Rapid City EDB releases the WQCV (not the
                    // 120 % design volume) over 40 h (§2.3.2 step 2); otherwise the drained fraction (99 %) of the volume.
                    double frac = rc && !wet ? 1 / (1 + sed / 100) : Num("drained fraction", 99, "drained fraction") / 100;
                    if (Bad("sediment allowance", sed, true) | Bad("row spacing", spacing) | Bad("orifice coefficient", cd) | Bad("maximum hole", maxHole)) continue;
                    if (!(frac > 0 && frac < 1)) { msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: drained fraction must be between 0 and 100 %.")); continue; }
                    if (Stages() is not { } stages) continue;
                    var pond = new Pond(stages, [], c.Gravity, us);
                    double pool = pond.Bottom;
                    if (wet)
                    {
                        if (!p.Has(sec + ".permanent pool")) { msgs.Add(CalcMessage.Error("E-WQ-011", $"BMP {name}: give the permanent pool elevation (permanent pool = …).")); continue; }
                        pool = Num("permanent pool", double.NaN, "permanent pool");
                        if (!(pool >= pond.Bottom && pool < pond.Top)) { msgs.Add(CalcMessage.Error("E-WQ-011", $"BMP {name}: permanent pool {pool:G6} must be within the stage table ({pond.Bottom:F2} to {pond.Top:F2}) and below its top.")); continue; }
                    }
                    double poolVol = pond.Storage(pool);
                    double S(double e) => e <= pool ? 0 : pond.Storage(e) - poolVol;
                    double design = wqcv * (1 + sed / 100), room = pond.MaxStorage - poolVol;
                    if (design > room) { msgs.Add(CalcMessage.Error("E-WQ-008", $"BMP {name}: the design volume {design:N0} {V} is more than the basin holds above {pool:F2} ({room:N0} {V} to {pond.Top:F2}). Enlarge the basin or extend the stage table.")); continue; }
                    var plate = new OrificePlate(S, pool, pond.Top, pool, spacing, cd, c.Gravity);
                    double wqLevel = plate.LevelAt(design);
                    plate = new OrificePlate(S, pool, pond.Top, wqLevel, spacing, cd, c.Gravity);
                    double rowArea = plate.Size(hours, frac);
                    double tFull = plate.DrainHours(frac);
                    double halfFrac = rc && !wet ? 0.5 * frac : 0.5, tHalf = plate.DrainHours(halfFrac);
                    double dia = Math.Sqrt(4 * rowArea / Math.PI);
                    int cols = (int)Math.Ceiling(dia / maxHole - 1e-9);
                    double colDia = Math.Sqrt(4 * rowArea / cols / Math.PI);
                    string what = wet ? "Constructed wetland basin" : "Extended detention basin";
                    string halfLabel = rc && !wet ? "Drain time, 50 % of the WQCV" : "Drain time, 50 % of the volume";
                    string fullLabel = rc && !wet ? $"Drain time, the WQCV ({100 * frac:G3} % of the design volume)" : $"Drain time, {100 * frac:G3} % of the volume";
                    var items = new List<IReadOnlyList<object?>>
                    {
                        new object?[] { "WQCV (composite)", Math.Round(wqcv, 0), V },
                    };
                    if (wet)
                    {
                        items.Add(["Permanent pool elevation", pool, L]);
                        items.Add(["Permanent pool volume", Math.Round(poolVol, 0), V]);
                        items.Add(["Pool volume / WQCV", Math.Round(100 * poolVol / wqcv, 1), "%"]);
                    }
                    else { items.Add(["Sediment allowance", sed, "%"]); items.Add(["Design volume", Math.Round(design, 0), V]); }
                    items.AddRange(new List<IReadOnlyList<object?>>
                    {
                        new object?[] { wet ? "Lowest row (permanent pool)" : "Bottom (lowest row)", pool, L }, new object?[] { "WQCV water surface", Math.Round(wqLevel, 3), L }, new object?[] { wet ? "Surcharge depth" : "Depth", Math.Round(wqLevel - pool, 3), L },
                        new object?[] { "Rows", plate.Rows.Count, "" }, new object?[] { "Row spacing", Math.Round(spacing * toSm, 3), small }, new object?[] { "Hole area per row", Math.Round(rowArea * toSq, 4), us ? "sq in." : "mm²" },
                        new object?[] { "One hole per row: diameter", Math.Round(dia * toSm, 3), small }, new object?[] { $"Columns for holes ≤ {maxHole * toSm:G3} {small}", cols, "" }, new object?[] { "Hole diameter with those columns", Math.Round(colDia * toSm, 3), small },
                        new object?[] { halfLabel, Math.Round(tHalf, 2), "h" }, new object?[] { fullLabel, Math.Round(tFull, 2), "h" },
                        new object?[] { "Peak WQ outflow (at the WQCV level)", Math.Round(plate.Outflow(wqLevel), 4), us ? "cfs" : "m³/s" },
                    });
                    string basis = rc && !wet
                        ? $"Rapid City SWQM §2.3.2: storage of 120 % of the WQCV above the lowest perforation; the plate releases the WQCV in {hours:G4} h from the design level, no more than half of it in 12 h."
                        : rc ? $"Rapid City SWQM §2.3.4: WQCV surcharge above the permanent pool, released in no less than {hours:G4} h ({100 * frac:G3} % of it in {hours:G4} h here); pool ≥ 75 % of the WQCV; surcharge ≤ 2 ft."
                        : $"The hole area is set so {100 * frac:G3} % of the design volume drains in {hours:G4} h.";
                    tables.Add(new($"{what} {name}", ["Item", "Value", "Unit"], items,
                    [$"Orifice rows every {spacing * toSm:G4} {small} from the lowest row up to the WQCV level, each Q = {cd:G3}·a·√(2g(h − z)) above its centre; drain time ∝ 1/a, so the hole area is exact. {basis} Storage: {pond.StorageBasis}"]));
                    var dd = plate.Drawdown(Math.Ceiling(Math.Max(hours, tFull) * 1.2));
                    charts.Add(new ToolChart($"Drawdown of {name}", "Time (h)", $"Water surface ({L})", [new("Water surface", dd)], [new("WQCV level", wqLevel), new(wet ? "Permanent pool" : "Bottom", pool)]));
                    var ssd = stages.Select(x => x.Item1).Append(wqLevel).Append(pool).Distinct().OrderBy(x => x).Select(e => (IReadOnlyList<object?>)[Math.Round(e, 3), Math.Round(pond.Storage(e), 0), Math.Round(plate.Outflow(e), 4)]).ToList();
                    tables.Add(new($"Stage–storage–discharge, {name}", [$"Elevation ({L})", $"Storage ({V})", $"WQ plate outflow ({(us ? "cfs" : "m³/s")})"], ssd));
                    if (dia * toSm < (us ? 0.5 : 12)) msgs.Add(CalcMessage.Warning("W-WQ-003", $"BMP {name}: holes of {dia * toSm:F2} {small} clog easily; protect them with a well screen, or reduce the number of rows (wider spacing)."));
                    if (rc && !wet && tHalf < 12 - 1e-6) { check = "CHECK"; msgs.Add(CalcMessage.Warning("W-WQ-009", $"BMP {name}: half of the WQCV leaves in {tHalf:F1} h; the manual allows no more than 50 % in 12 h (§2.3.2 step 2). Use fewer rows near the bottom or a deeper, narrower basin.")); }
                    if (rc && wet && poolVol < 0.75 * wqcv - 1e-6) Limit($"the permanent pool holds {poolVol:N0} {V}, less than 75 % of the WQCV ({0.75 * wqcv:N0} {V}) (§2.3.4 step 2).");
                    if (rc && wet && wqLevel - pool > Ft(2) + 1e-9) Limit($"the WQCV surcharge is {wqLevel - pool:F2} {L} deep; the manual allows {Ft(2):G3} {L} (§2.3.4 step 3).");
                    sizeText = $"{plate.Rows.Count} rows, {rowArea * toSq:G3} {(us ? "sq in." : "mm²")} per row (Ø {dia * toSm:F2} {small}), WQCV level {wqLevel:F2}";
                    bmpRows.Add(Row(wet ? "Wetland basin" : "EDB", design, sizeText));
                    break;
                }
                case "sand filter" or "bioretention":
                {
                    bool bio = type == "bioretention";
                    bool under = p.Flag(sec + ".underdrain", true);
                    double hMax = Num("ponding depth", bio ? (us ? 12 : 300) : (us ? 24 : 600), "ponding depth") * smallK, df = Num("media depth", bio ? (rc ? (us ? 48 : 1220) : (us ? 18 : 450)) : (us ? 18 : 450), "media depth") * smallK;
                    double k = Num("permeability", bio ? (us ? 0.5 : 0.15) : (us ? 3.5 : 1.07), "permeability"), tf = Num("filter drain time", bio ? 48 : 40, "filter drain time");
                    double n = Num("media porosity", 0, "media porosity");
                    if (Bad("ponding depth", hMax) | Bad("media depth", df) | Bad("permeability", k) | Bad("filter drain time", tf) | Bad("media porosity", n, true)) continue;
                    if (n >= 1) { msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: media porosity must be below 1.")); continue; }
                    if (rc)
                    {
                        // Rapid City: the WQCV is stored above the media (§2.3.3 step 1, §2.3.9), within the manual's depth limit.
                        double maxDepth = bio ? Ft(under ? 1 : 0.5) : Ft(3);
                        if (hMax > maxDepth + 1e-9)
                        {
                            Limit(bio ? $"ponding of {hMax / smallK:G4} {small} is more than the {maxDepth / smallK:G3} {small} the manual allows {(under ? "with collector pipes" : "without collector pipes")} (§2.3.9); {maxDepth / smallK:G3} {small} is used."
                                      : $"ponding of {hMax / smallK:G4} {small} is more than the 3 ft maximum design volume depth (§2.3.3 step 2); 3 ft is used.");
                            hMax = maxDepth;
                        }
                        double minMedia = bio ? Ft(4) : Ft(1.5);
                        if (df < minMedia - 1e-9) Limit(bio ? $"planting soil of {df / smallK:G4} {small} is less than the 4 ft the manual recommends (§2.3.9; 1 to 2 ft is allowed on-line for drainage areas under 2 ac)."
                                                          : $"sand of {df / smallK:G4} {small} is less than the 18 in. layer the manual requires (§2.3.3 step 4).");
                        var rows = new List<IReadOnlyList<object?>>
                        {
                            new object?[] { "WQCV (composite)", Math.Round(wqcv, 0), V }, new object?[] { bio ? "WQCV drain time (surface ponding; sets a)" : "WQCV drain time (sets a)", hours, "h" },
                            new object?[] { bio ? "Ponding depth used" : "Design volume depth used (≤ 3 ft)", Math.Round(hMax / smallK, 2), small },
                            new object?[] { bio ? "Planting soil depth" : "Sand depth", Math.Round(df / smallK, 2), small },
                        };
                        double req, tAct;
                        if (bio)
                        {
                            // Kyle (2026-11-20): the surface ponding drains in 48 h; the soil and gravel bed, when modeled, in 72 h.
                            if (p.Has(sec + ".filter drain time") && Math.Abs(tf - 48) > 1e-9) { msgs.Add(CalcMessage.Warning("W-WQ-007", $"BMP {name}: Rapid City bioretention ponding drains in 48 h; a filter drain time of {tf:G4} h is ignored.")); tf = 48; }
                            double aStore = wqcv / hMax, aDarcy = Wq.DarcyFilterArea(wqcv, df, k, hMax / 2, tf / 24);
                            req = Math.Max(aStore, aDarcy);
                            tAct = Wq.DarcyDrainDays(wqcv, df, k, hMax / 2, req) * 24;
                            rows.Add(["Area to hold the WQCV in the ponding, WQCV / ponding", Math.Round(aStore, 1), Ar]);
                            rows.Add([$"Area to drain the ponding in {tf:G3} h by Darcy, V·df / (k (hf + df) t)", Math.Round(aDarcy, 1), Ar]);
                            rows.Add(["Media permeability k", k, kU]);
                            rows.Add(["Required area", Math.Round(req, 1), Ar]);
                            rows.Add(["Governs", aDarcy > aStore ? $"{tf:G3} h surface drain" : "ponding storage", ""]);
                            rows.Add(["Surface drain time at the required area", Math.Round(tAct, 1), "h"]);
                            // Bed drainage, when modeled: the drainable pore water then leaves at the media rate (unit gradient).
                            // With an underdrain the gravel empties to the pipe; without one, soil and gravel drain to the subgrade.
                            double ng = Num("gravel porosity", 0.4, "gravel porosity"), dg = Num("gravel depth", 0, "gravel depth") * smallK;
                            if (Bad("gravel porosity", ng) | Bad("gravel depth", dg, true)) continue;
                            if (n > 0 || dg > 0)
                            {
                                double rateDay = k;
                                string via = "the media (k, unit gradient)";
                                if (!under)
                                {
                                    double fSub = Num("subgrade infiltration rate", double.NaN, "subgrade infiltration rate"), sfSub = Num("safety factor", 2, "safety factor");
                                    if (double.IsNaN(fSub)) { msgs.Add(CalcMessage.Error("E-WQ-009", $"BMP {name}: without an underdrain, give the subgrade infiltration rate ({rate}) to check the bed drainage.")); continue; }
                                    if (Bad("subgrade infiltration rate", fSub) | Bad("safety factor", sfSub)) continue;
                                    double fDay = fSub / sfSub * smallK * 24;
                                    if (fDay < rateDay) { rateDay = fDay; via = $"the subgrade ({fSub:G3} {rate} / {sfSub:G3})"; }
                                }
                                double stored = n * df + (under ? 0 : ng * dg);
                                double tBed = stored / rateDay * 24, tTotal = tAct + tBed;
                                rows.Add(["Drainable porosity of the soil", n, ""]);
                                if (dg > 0) rows.Add([$"Gravel bed, depth × porosity{(under ? " (to the underdrain)" : "")}", $"{dg / smallK:G4} {small} × {ng:G3}", ""]);
                                rows.Add([$"Bed drainage after the ponding, stored depth / rate through {via}", Math.Round(tBed, 1), "h"]);
                                rows.Add(["Time to be ready for the next storm (surface + bed)", Math.Round(tTotal, 1), "h"]);
                                if (tTotal > 72 + 1e-6) Limit($"the soil and gravel bed drain {tTotal:F1} h after the ponding fills, more than 72 h. Raise the media permeability, add an underdrain or enlarge the area.");
                            }
                        }
                        else
                        {
                            req = wqcv / (hMax + n * df);
                            tAct = Wq.DarcyDrainDays(wqcv, df, k, hMax / 2, req) * 24;
                            rows.Add(["Required area, WQCV / depth", Math.Round(req, 1), Ar]);
                            rows.Add(["Media permeability k (check only)", k, kU]);
                            rows.Add(["Darcy drain time at that area (check only)", Math.Round(tAct, 1), "h"]);
                        }
                        if (bio && p.Has(sec + ".runoff coefficient"))
                        {
                            double cR = Num("runoff coefficient", 0, "runoff coefficient");
                            if (!(cR > 0 && cR <= 1)) { msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: runoff coefficient must be more than 0 and at most 1.")); continue; }
                            double lo = 0.05 * cR * area * areaK, hi = 0.07 * cR * area * areaK;
                            rows.Add(["Guideline area, 5–7 % × C × drainage area", $"{lo:N0} – {hi:N0}", Ar]);
                            if (req < lo) msgs.Add(CalcMessage.Info("I-WQ-004", $"BMP {name}: the area {req:N0} {Ar} is below the 5–7 % × C guideline ({lo:N0} – {hi:N0} {Ar}, §2.3.9); the manual allows smaller and larger areas."));
                        }
                        tables.Add(new($"{(bio ? "Bioretention" : "Sand filter")} {name}", ["Item", "Value", "Unit"], rows,
                        [bio ? "Rapid City SWQM §2.3.9: bioretention meets the WQCV criteria; the WQCV is held in the ponding area (≤ 6 in., ≤ 12 in. with collector pipes). §2.3.9 gives no drain time; by the project's design criteria (set 2026-11-20) the ponding drains in 48 h (a = 1.0, the longest category) and the soil and gravel bed, when modeled, within 72 h."
                             : "Rapid City SWQM §2.3.3: 100 % of the WQCV (40 h) above the sand bed; As = design volume / 3 ft at the 3 ft maximum depth; 18 in. sand over 8 in. gravel.",
                         bio ? "Surface drain by Darcy, t = V·df / (k (hf + df) A), hf = half the ponding. Bed drainage (when a drainable porosity or gravel depth is given): stored depth / rate, the rate being k, or the subgrade design rate when lower and there is no underdrain."
                             : "The Darcy drain time (Af·k·(hf + df)/df, hf = half the depth) is a check, not a manual requirement."]));
                        if (!bio && tAct > 72) msgs.Add(CalcMessage.Info("I-WQ-005", $"BMP {name}: by Darcy the media drains the WQCV in {tAct:F0} h at that area; check the media permeability."));
                        sizeText = $"area {req:N0} {Ar}, {hMax / smallK:G3} {small} deep";
                        bmpRows.Add(Row(bio ? "Bioretention" : "Sand filter EDB", wqcv, sizeText));
                    }
                    else
                    {
                        double aDarcy = Wq.DarcyFilterArea(wqcv, df, k, hMax / 2, tf / 24);
                        double aStore = wqcv / (hMax + n * df);
                        double req = Math.Max(aDarcy, aStore);
                        string gov = aDarcy >= aStore ? "Darcy filtration" : "surface storage";
                        double tAct = Wq.DarcyDrainDays(wqcv, df, k, hMax / 2, req) * 24;
                        tables.Add(new($"{(bio ? "Bioretention" : "Sand filter")} {name}", ["Item", "Value", "Unit"],
                        [
                            ["WQCV (composite) = volume to filter", Math.Round(wqcv, 0), V], ["Ponding depth (maximum)", Math.Round(hMax / smallK, 2), small], ["Average head hf = ponding / 2", Math.Round(hMax / 2 / smallK, 2), small],
                            ["Media depth df", Math.Round(df / smallK, 2), small], ["Media permeability k", k, kU], ["Filter drain time tf", tf, "h"],
                            ["Area by Darcy, V·df / (k (hf + df) tf)", Math.Round(aDarcy, 1), Ar], ["Area to store the WQCV, V / (ponding + porosity × df)", Math.Round(aStore, 1), Ar],
                            ["Required filter area", Math.Round(req, 1), Ar], ["Governs", gov, ""], ["Drain time at the required area", Math.Round(tAct, 1), "h"],
                        ],
                        ["Darcy's law for filtering practices (Maryland Stormwater Design Manual 2000; Claytor and Schueler 1996), average head half the ponding depth. The storage check holds the whole WQCV above the media (plus the media voids when a porosity is given)."]));
                        sizeText = $"filter area {req:N0} {Ar} ({gov})";
                        bmpRows.Add(Row(bio ? "Bioretention" : "Sand filter", wqcv, sizeText));
                    }
                    if (!under) msgs.Add(CalcMessage.Info("I-WQ-003", $"BMP {name}: no underdrain; confirm the subgrade infiltrates the ponding within the drain time (use an infiltration BMP to size it)."));
                    break;
                }
                case "porous landscape detention":
                {
                    double dav = Num("average depth", us ? 12 : 300, "average depth") * smallK, df = Num("media depth", us ? 18 : 450, "media depth") * smallK;
                    if (Bad("average depth", dav) | Bad("media depth", df)) continue;
                    if (dav < Ft(0.5) - 1e-9 || dav > Ft(1) + 1e-9) Limit($"an average WQCV depth of {dav / smallK:G4} {small} is outside the 6 to 12 in. the manual requires (§2.3.1 step 2).");
                    if (df < Ft(1.5) - 1e-9) Limit($"sand-peat media of {df / smallK:G4} {small} is less than the 18 in. minimum (§2.3.1 step 3; 36 in. in NRCS Type D or expansive soils without an underdrain).");
                    double req = wqcv / dav;
                    tables.Add(new($"Porous landscape detention {name}", ["Item", "Value", "Unit"],
                    [
                        ["WQCV (composite) = design volume", Math.Round(wqcv, 0), V], ["WQCV drain time (sets a)", hours, "h"], ["Average depth dav", Math.Round(dav / smallK, 2), small],
                        ["Sand-peat media depth", Math.Round(df / smallK, 2), small], ["Minimum surface area, design volume / dav", Math.Round(req, 1), Ar],
                    ],
                    ["Rapid City SWQM §2.3.1: storage for a 12 h drain time; average depth (volume / water surface area) 6 to 12 in.; at least 18 in. of 2/3 sand, 1/3 peat over 8 in. granular subbase; side slopes no steeper than 4:1."]));
                    sizeText = $"surface area {req:N0} {Ar} at {dav / smallK:G3} {small} average depth";
                    bmpRows.Add(Row("Porous landscape detention", wqcv, sizeText));
                    break;
                }
                default:   // infiltration
                {
                    double fMeas = Num("infiltration rate", double.NaN, "infiltration rate"), sf = Num("safety factor", 2, "safety factor"), nVoid = Num("void ratio", 0.4, "void ratio");
                    double depth = Num("depth", double.NaN, "depth"), tMax = Num("maximum drain time", 72, "maximum drain time"), fMin = Num("minimum infiltration rate", us ? 0.5 : 13, "minimum infiltration rate");
                    if (double.IsNaN(fMeas)) { msgs.Add(CalcMessage.Error("E-WQ-009", $"BMP {name}: give the measured infiltration rate ({rate}).")); continue; }
                    if (Bad("infiltration rate", fMeas) | Bad("safety factor", sf) | Bad("void ratio", nVoid) | Bad("maximum drain time", tMax)) continue;
                    if (sf < 1) { msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: safety factor must be at least 1.")); continue; }
                    if (nVoid > 1) { msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: void ratio must be at most 1.")); continue; }
                    double fDesign = fMeas / sf * smallK;                    // ft/hr or m/hr
                    double dMax = fDesign * tMax / nVoid;                     // storage depth that drains in tMax through the bottom
                    double dUse = double.IsNaN(depth) ? dMax : depth;
                    if (Bad("depth", dUse)) continue;
                    if (dUse > dMax + 1e-9) { msgs.Add(CalcMessage.Warning("W-WQ-004", $"BMP {name}: a depth of {dUse:G4} {L} drains in {dUse * nVoid / fDesign:F1} h, longer than {tMax:G4} h; the depth used is {dMax:F2} {L}.")); dUse = dMax; }
                    double bottom = wqcv / (nVoid * dUse), tAct = dUse * nVoid / fDesign;
                    tables.Add(new($"Infiltration {name}", ["Item", "Value", "Unit"],
                    [
                        ["WQCV (composite)", Math.Round(wqcv, 0), V], ["Measured infiltration rate", fMeas, rate], ["Safety factor", sf, ""], ["Design infiltration rate", Math.Round(fDesign / smallK, 3), rate],
                        ["Void ratio of the storage", nVoid, ""], ["Maximum drain time", tMax, "h"], ["Deepest storage that drains in that time, f·T / n", Math.Round(dMax, 2), L],
                        ["Storage depth used", Math.Round(dUse, 2), L], ["Bottom area, V / (n·d)", Math.Round(bottom, 1), Ar], ["Drain time", Math.Round(tAct, 1), "h"],
                    ],
                    ["Infiltration through the bottom only (sides ignored, conservative). Confirm the separation to groundwater and bedrock and the distance from foundations required by the jurisdiction."]));
                    if (fMeas < fMin) { check = "LOW RATE"; msgs.Add(CalcMessage.Warning("W-WQ-005", $"BMP {name}: measured infiltration rate {fMeas:G3} {rate} is below {fMin:G3} {rate}; infiltration is not usually allowed there. Use a filter with an underdrain.")); }
                    if (rc) { check = check == "OK" ? "APPROVAL" : check; msgs.Add(CalcMessage.Warning("W-WQ-006", $"BMP {name}: an infiltration basin or trench is not one of the Storm Water Quality Manual's WQCV BMPs; it needs approval by the Growth Management Director case by case (§2.3.10).")); }
                    sizeText = $"bottom {bottom:N0} {Ar}, {dUse:F2} {L} deep";
                    bmpRows.Add(Row("Infiltration", wqcv, sizeText));
                    break;
                }
            }
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        double impTotal = subs.Sum(s => s.Area * s.Imp), impTreated = subs.Where(s => s.Bmp is not null).Sum(s => s.Area * s.Imp);
        double share = impTotal > 0 ? impTreated / impTotal : 1;
        var untreated = subs.Where(s => s.Bmp is null).Select(s => s.Name).ToList();
        if (untreated.Count > 0) msgs.Add(CalcMessage.Info("I-WQ-002", $"Untreated: {string.Join(", ", untreated)} ({100 * (1 - share):F1} % of the impervious area)."));
        msgs.Add(rc
            ? CalcMessage.Info("I-WQ-001", "Rapid City Storm Water Quality Manual (2009), adopted by IDCM (2012) §4.1.7: WQCV = 1.16·a·(0.91I³ − 1.191I² + 0.78I) watershed inches (§2.2.2), a = 0.7, 0.8, 0.9, 1.0 for 6, 12, 24, 40 h drain times; I = mapped impervious fraction, area-weighted for each BMP; volume = WQCV/12 × effective area (total less design credit areas, §2.4.2). Drain times: porous landscape detention 12 h, EDB 40 h, sand filter 40 h, constructed wetland 24 h, bioretention 48 h surface ponding (a = 1.0) and 72 h bed drainage when modeled (§2.3.9 gives none; project design criteria).")
            : CalcMessage.Info("I-WQ-001", $"WQCV = a (0.91 i³ − 1.19 i² + 0.78 i) × d6 / 0.43 in watershed inches (MHFD USDCM Vol. 3 Eq. 3-1, 3-2), a from the drain time (Table 3-2: 12 h 0.8, 24 h 0.9, 40 h 1.0), composite imperviousness by area for each BMP. d6 = {d6 * inchToSmall:G4} {(us ? "in" : "mm")}. These are the MHFD methods; confirm the jurisdiction's water quality criteria (volume, drain time, allowed BMPs) before use."));

        var summary = new List<string>
        {
            rc ? "Criteria: City of Rapid City Storm Water Quality Manual (2009), per IDCM §4.1.7." : "Criteria: MHFD USDCM Vol. 3 (inputs as entered).",
            $"{subs.Count} sub-basins, {subs.Sum(s => s.Area):G6} {A}; {bmpRows.Count} BMPs sized.",
            $"Impervious area treated: {100 * share:F1} % ({impTreated:G4} of {impTotal:G4} {A} impervious).",
        };
        summary.AddRange(bmpRows.Select(r => $"{r[0]} ({r[1]}): WQCV {r[7]:N0} {V}; {r[9]}{((string)r[10]! == "OK" ? "" : $" [{r[10]}]")}."));
        tables.Insert(0, new("Water quality capture volume by sub-basin", ["Sub-basin", $"Area ({A})", $"Design credit ({A})", $"Effective area ({A})", "Imperviousness (%)", "BMP", "a", $"WQCV ({small})", $"WQCV ({V})"], subRows,
            ["a from the BMP's drain time (1.0 for untreated sub-basins, shown for reference). Volume = WQCV depth × effective area (area less design credits)."]));
        tables.Insert(1, new("BMP summary", ["BMP", "Type", $"Effective area ({A})", "Composite imperviousness (%)", "WQCV drain time (h)", "a", $"WQCV ({small})", $"WQCV ({V})", $"Design volume ({V})", "Size", "Check"], bmpRows));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }
}
