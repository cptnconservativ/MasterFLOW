using MasterFlow.Application.WaterQuality;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Water quality and LID sizing (premium): water quality capture volume per sub-basin (MHFD USDCM Vol. 3 Eq. 3-1/3-2)
/// and sizing of extended detention basins (WQ orifice plate), sand filters and bioretention (Darcy's law plus surface
/// storage) and infiltration basins or trenches, with the share of impervious area treated for MS4 reporting.
/// </summary>
public sealed class WaterQualityTool : IDesignTool
{
    public string Id => "water-quality";
    public string Name => "Water quality and LID sizing (WQCV, EDB, sand filter, bioretention, infiltration)";
    public string Description => "Water quality capture volume by sub-basin (MHFD USDCM Vol. 3 Eq. 3-1, drain-time coefficient Table 3-2, regional depth Eq. 3-2) and BMP sizing: extended detention basin orifice plate drained in the target time, sand filter and bioretention by Darcy's law and surface storage, infiltration basin or trench by design infiltration rate and drain time. Reports the share of impervious area treated.";
    public bool UsesNetwork => false;

    private static readonly string[] Types = ["edb", "sand filter", "bioretention", "infiltration"];

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Water quality capture volume (MHFD USDCM Vol. 3 Eq. 3-1) and BMP sizing, SI (ha, m, m³).
        # Confirm the water quality criteria the jurisdiction requires; these are the MHFD methods.
        regional depth = 10.9       # mm, d6 for Eq. 3-2; 10.9 mm (0.43 in.) = no adjustment

        [Subbasins]
        Name	Area (ha)	Imperviousness (%)	BMP
        A1	2.5	65	Pond 1
        A2	1.4	45	Pond 1
        B1	0.45	90	Rain garden
        E1	0.8	10

        [BMP Pond 1]
        type = edb
        wqcv drain time = 40        # h
        sediment allowance = 20     # %
        row spacing = 100           # mm
        Elevation	Area
        30.00	400
        30.50	700
        31.00	1000
        31.50	1350

        [BMP Rain garden]
        type = bioretention
        ponding depth = 300         # mm
        media depth = 450           # mm
        permeability = 0.15         # m/day
        """ : """
        # Water quality capture volume (MHFD USDCM Vol. 3 Eq. 3-1) and BMP sizing, U.S. units (ac, ft, cf).
        # Confirm the water quality criteria the jurisdiction requires; these are the MHFD methods.
        regional depth = 0.43       # in., d6 for Eq. 3-2 (0.43 = no adjustment)

        [Subbasins]
        Name	Area (ac)	Imperviousness (%)	BMP
        A1	6.2	65	Pond 1
        A2	3.4	45	Pond 1
        B1	1.1	90	Rain garden
        C1	0.8	80	Sand filter
        D1	0.5	95	Trench
        E1	2.0	10

        # Extended detention basin: stage–area (contours, conic method); the WQ plate starts at the bottom row.
        [BMP Pond 1]
        type = edb
        wqcv drain time = 40        # h (sets a, Table 3-2, and the plate)
        sediment allowance = 20     # % added to the WQCV
        row spacing = 4             # in. between orifice rows
        orifice coefficient = 0.6
        drained fraction = 99       # % of the volume for the drain time
        Elevation	Area
        100.0	4000
        101.0	6500
        102.0	9200
        103.0	12000
        104.0	15000

        [BMP Rain garden]
        type = bioretention
        ponding depth = 12          # in.
        media depth = 18            # in.
        permeability = 0.5          # ft/day, bioretention soil
        filter drain time = 48      # h

        [BMP Sand filter]
        type = sand filter
        ponding depth = 24          # in.
        media depth = 18            # in.
        permeability = 3.5          # ft/day, sand
        filter drain time = 40      # h

        [BMP Trench]
        type = infiltration
        infiltration rate = 1.0     # in./hr measured
        safety factor = 2
        void ratio = 0.4            # 0.4 stone; 1.0 open basin
        depth = 3                   # ft of storage
        maximum drain time = 72     # h
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
        double d6 = p.Number("regional depth", us ? 0.43 : 10.92) / inchToSmall;   // inches
        if (!(d6 > 0)) msgs.Add(CalcMessage.Error("E-WQ-001", "Regional depth (d6) must be more than zero."));

        // ---- sub-basins
        var head = p.Header("Subbasins");
        int Col(string word, int fallback)
        {
            for (int i = 0; i < head.Count; i++)
                if (head[i].Split([' ', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(word, StringComparison.OrdinalIgnoreCase))) return i;
            return fallback;
        }
        int cArea = Col("area", 1), cImp = Col("imperv", 2), cBmp = Col("bmp", 3);
        var subs = new List<(string Name, double Area, double Imp, string? Bmp)>();
        foreach (var r in p.Rows("Subbasins"))
        {
            string name = r[0];
            double? a = p.Cell(r, cArea, "area", name), i = p.Cell(r, cImp, "imperviousness", name);
            string? b = cBmp < r.Length && r[cBmp].Length > 0 ? r[cBmp] : null;
            if (a is not { } av || i is not { } iv) { if (!p.Errors.Any()) msgs.Add(CalcMessage.Error("E-WQ-002", $"Sub-basin {name}: area and imperviousness are both needed.")); continue; }
            if (!(av > 0)) { msgs.Add(CalcMessage.Error("E-WQ-002", $"Sub-basin {name}: area must be more than zero.")); continue; }
            if (iv < 0 || iv > 100) { msgs.Add(CalcMessage.Error("E-WQ-002", $"Sub-basin {name}: imperviousness {iv:G4} % must be 0 to 100 (percent).")); continue; }
            if (iv > 0 && iv <= 1 && !msgs.Any(m => m.Code == "W-WQ-001")) msgs.Add(CalcMessage.Warning("W-WQ-001", $"Sub-basin {name}: imperviousness {iv:G4} is read as percent ({iv:G4} %), not as a fraction."));
            subs.Add((name, av, iv / 100, b));
        }
        if (subs.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-WQ-003", "Add sub-basins under [Subbasins]: Name, Area, Imperviousness (%), BMP."));

        // ---- BMPs
        var bmpSections = p.Sections.Where(s => s.StartsWith("BMP ", StringComparison.OrdinalIgnoreCase)).ToList();
        var bmpNames = bmpSections.ToDictionary(s => s[4..].Trim(), s => s, StringComparer.OrdinalIgnoreCase);
        foreach (var s in subs.Where(s => s.Bmp is { } b && !bmpNames.ContainsKey(b)))
            msgs.Add(CalcMessage.Error("E-WQ-004", $"Sub-basin {s.Name} drains to BMP \"{s.Bmp}\", which has no [BMP {s.Bmp}] section."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        var subRows = new List<IReadOnlyList<object?>>();
        var bmpRows = new List<IReadOnlyList<object?>>();
        var tables = new List<ToolTable>();
        var charts = new List<ToolChart>();
        var drainOf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        string Type(string sec) => (p.Text(sec + ".type") ?? "").Trim().ToLowerInvariant();
        foreach (var (name, sec) in bmpNames)
            drainOf[name] = p.Number(sec + ".wqcv drain time", Type(sec) == "edb" ? 40 : 12, $"{name}: wqcv drain time");

        foreach (var s in subs)
        {
            double a = s.Bmp is { } b ? Wq.DrainCoefficient(drainOf[b]) : 1.0;
            double inches = Wq.WqcvInches(s.Imp, a, d6);
            subRows.Add([s.Name, s.Area, Math.Round(100 * s.Imp, 1), s.Bmp ?? "(untreated)", Math.Round(a, 3), Math.Round(inches * inchToSmall, 4), Math.Round(inches * (us ? 1.0 / 12 : 0.0254) * s.Area * areaK, 0)]);
        }

        foreach (var (name, sec) in bmpNames)
        {
            var mine = subs.Where(s => string.Equals(s.Bmp, name, StringComparison.OrdinalIgnoreCase)).ToList();
            string type = Type(sec);
            if (!Types.Contains(type)) { msgs.Add(CalcMessage.Error("E-WQ-005", $"BMP {name}: type \"{type}\" is not one of {string.Join(", ", Types)}.")); continue; }
            if (mine.Count == 0) { msgs.Add(CalcMessage.Warning("W-WQ-002", $"BMP {name}: no sub-basin drains to it; not sized.")); continue; }
            double area = mine.Sum(x => x.Area), imp = mine.Sum(x => x.Area * x.Imp) / area;    // composite imperviousness (MHFD practice)
            double hours = drainOf[name];
            if (!(hours > 0)) { msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: wqcv drain time must be more than zero.")); continue; }
            double a = Wq.DrainCoefficient(hours), inches = Wq.WqcvInches(imp, a, d6);
            double wqcv = inches * (us ? 1.0 / 12 : 0.0254) * area * areaK;   // ft³ or m³
            string sizeText; string check = "OK";
            double Num(string key, double def, string what) => p.Number(sec + "." + key, def, $"{name}: {what}");
            bool Bad(string what, double v, bool zeroOk = false)
            {
                if (double.IsFinite(v) && (v > 0 || (zeroOk && v == 0))) return false;
                msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: {what} must be {(zeroOk ? "zero or more" : "more than zero")} (got {v:G6}).")); return true;
            }
            switch (type)
            {
                case "edb":
                {
                    double sed = Num("sediment allowance", 20, "sediment allowance"), spacing = Num("row spacing", us ? 4 : 100, "row spacing") * smallK, cd = Num("orifice coefficient", 0.6, "orifice coefficient");
                    double frac = Num("drained fraction", 99, "drained fraction") / 100, maxHole = Num("maximum hole", us ? 2 : 50, "maximum hole") * smallK;
                    if (Bad("sediment allowance", sed, true) | Bad("row spacing", spacing) | Bad("orifice coefficient", cd) | Bad("maximum hole", maxHole)) continue;
                    if (!(frac > 0 && frac < 1)) { msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: drained fraction must be between 0 and 100 %.")); continue; }
                    var stages = new List<(double, double)>();
                    foreach (var r in p.Rows(sec))
                        if (p.Cell(r, 0, "elevation", name) is { } e && p.Cell(r, 1, "area", name) is { } ar)
                        {
                            if (stages.Count > 0 && e <= stages[^1].Item1) { msgs.Add(CalcMessage.Error("E-WQ-007", $"BMP {name}: stage elevations must increase.")); stages.Clear(); break; }
                            if (ar < 0) { msgs.Add(CalcMessage.Error("E-WQ-007", $"BMP {name}: stage area cannot be negative.")); stages.Clear(); break; }
                            stages.Add((e, ar));
                        }
                    if (stages.Count < 2) { if (!msgs.Any(m => m.Code == "E-WQ-007")) msgs.Add(CalcMessage.Error("E-WQ-007", $"BMP {name}: give at least two Elevation, Area rows.")); continue; }
                    var pond = new Pond(stages, [], c.Gravity, us);
                    double design = wqcv * (1 + sed / 100);
                    if (design > pond.MaxStorage) { msgs.Add(CalcMessage.Error("E-WQ-008", $"BMP {name}: the design volume {design:N0} {V} is more than the basin holds ({pond.MaxStorage:N0} {V} to {pond.Top:F2}). Enlarge the basin or extend the stage table.")); continue; }
                    var plate = new OrificePlate(pond, 0, spacing, cd, c.Gravity);
                    double wqLevel = plate.LevelAt(design);
                    plate = new OrificePlate(pond, wqLevel, spacing, cd, c.Gravity);
                    double rowArea = plate.Size(hours, frac);
                    double t50 = plate.DrainHours(0.5), tFull = plate.DrainHours(frac);
                    double dia = Math.Sqrt(4 * rowArea / Math.PI);
                    int cols = (int)Math.Ceiling(dia / maxHole - 1e-9);
                    double colDia = Math.Sqrt(4 * rowArea / cols / Math.PI);
                    double toSq = us ? 144 : 1e6, toSm = us ? 12 : 1000;
                    tables.Add(new($"Extended detention basin {name}", ["Item", "Value", "Unit"],
                    [
                        ["WQCV (composite)", Math.Round(wqcv, 0), V], ["Sediment allowance", sed, "%"], ["Design volume", Math.Round(design, 0), V],
                        ["Bottom (lowest row)", pond.Bottom, L], ["WQCV water surface", Math.Round(wqLevel, 3), L], ["Depth", Math.Round(wqLevel - pond.Bottom, 3), L],
                        ["Rows", plate.Rows.Count, ""], ["Row spacing", Math.Round(spacing * toSm, 3), small], ["Hole area per row", Math.Round(rowArea * toSq, 4), us ? "sq in." : "mm²"],
                        ["One hole per row: diameter", Math.Round(dia * toSm, 3), small], [$"Columns for holes ≤ {maxHole * toSm:G3} {small}", cols, ""], ["Hole diameter with those columns", Math.Round(colDia * toSm, 3), small],
                        ["Drain time, 50 % of the volume", Math.Round(t50, 2), "h"], [$"Drain time, {100 * frac:G3} % of the volume", Math.Round(tFull, 2), "h"],
                        ["Peak WQ outflow (at the WQCV level)", Math.Round(plate.Outflow(wqLevel), 4), us ? "cfs" : "m³/s"],
                    ],
                    [$"Orifice rows every {spacing * toSm:G4} {small} from the bottom up to the WQCV level, each Q = {cd:G3}·a·√(2g(h − z)) above its centre. The hole area is set so {100 * frac:G3} % of the design volume drains in {hours:G4} h (drain time ∝ 1/a). Storage: {pond.StorageBasis}"]));
                    var dd = plate.Drawdown(Math.Ceiling(Math.Max(hours, tFull) * 1.2));
                    charts.Add(new ToolChart($"Drawdown of {name}", "Time (h)", $"Water surface ({L})", [new("Water surface", dd)], [new("WQCV level", wqLevel), new("Bottom", pond.Bottom)]));
                    var ssd = stages.Select(x => x.Item1).Append(wqLevel).Distinct().OrderBy(x => x).Select(e => (IReadOnlyList<object?>)[Math.Round(e, 3), Math.Round(pond.Storage(e), 0), Math.Round(plate.Outflow(e), 4)]).ToList();
                    tables.Add(new($"Stage–storage–discharge, {name}", [$"Elevation ({L})", $"Storage ({V})", $"WQ plate outflow ({(us ? "cfs" : "m³/s")})"], ssd));
                    if (dia * toSm < (us ? 0.5 : 12)) msgs.Add(CalcMessage.Warning("W-WQ-003", $"BMP {name}: holes of {dia * toSm:F2} {small} clog easily; protect them with a well screen, or reduce the number of rows (wider spacing)."));
                    sizeText = $"{plate.Rows.Count} rows, {rowArea * toSq:G3} {(us ? "sq in." : "mm²")} per row (Ø {dia * toSm:F2} {small}), WQCV level {wqLevel:F2}";
                    bmpRows.Add([name, "EDB", Math.Round(area, 3), Math.Round(100 * imp, 1), hours, Math.Round(a, 3), Math.Round(inches * inchToSmall, 4), Math.Round(wqcv, 0), Math.Round(design, 0), sizeText, check]);
                    break;
                }
                case "sand filter" or "bioretention":
                {
                    bool bio = type == "bioretention";
                    double hMax = Num("ponding depth", bio ? (us ? 12 : 300) : (us ? 24 : 600), "ponding depth") * smallK, df = Num("media depth", us ? 18 : 450, "media depth") * smallK;
                    double k = Num("permeability", bio ? (us ? 0.5 : 0.15) : (us ? 3.5 : 1.07), "permeability"), tf = Num("filter drain time", bio ? 48 : 40, "filter drain time");
                    double n = Num("media porosity", 0, "media porosity");
                    if (Bad("ponding depth", hMax) | Bad("media depth", df) | Bad("permeability", k) | Bad("filter drain time", tf) | Bad("media porosity", n, true)) continue;
                    if (n >= 1) { msgs.Add(CalcMessage.Error("E-WQ-006", $"BMP {name}: media porosity must be below 1.")); continue; }
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
                    if (!p.Flag(sec + ".underdrain", true)) msgs.Add(CalcMessage.Info("I-WQ-003", $"BMP {name}: no underdrain; confirm the subgrade infiltrates the ponding within the drain time (use an infiltration BMP to size it)."));
                    sizeText = $"filter area {req:N0} {Ar} ({gov})";
                    bmpRows.Add([name, bio ? "Bioretention" : "Sand filter", Math.Round(area, 3), Math.Round(100 * imp, 1), hours, Math.Round(a, 3), Math.Round(inches * inchToSmall, 4), Math.Round(wqcv, 0), Math.Round(wqcv, 0), sizeText, check]);
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
                    sizeText = $"bottom {bottom:N0} {Ar}, {dUse:F2} {L} deep";
                    bmpRows.Add([name, "Infiltration", Math.Round(area, 3), Math.Round(100 * imp, 1), hours, Math.Round(a, 3), Math.Round(inches * inchToSmall, 4), Math.Round(wqcv, 0), Math.Round(wqcv, 0), sizeText, check]);
                    break;
                }
            }
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        double impTotal = subs.Sum(s => s.Area * s.Imp), impTreated = subs.Where(s => s.Bmp is not null).Sum(s => s.Area * s.Imp);
        double share = impTotal > 0 ? impTreated / impTotal : 1;
        var untreated = subs.Where(s => s.Bmp is null).Select(s => s.Name).ToList();
        if (untreated.Count > 0) msgs.Add(CalcMessage.Info("I-WQ-002", $"Untreated: {string.Join(", ", untreated)} ({100 * (1 - share):F1} % of the impervious area)."));
        msgs.Add(CalcMessage.Info("I-WQ-001", $"WQCV = a (0.91 i³ − 1.19 i² + 0.78 i) × d6 / 0.43 in watershed inches (MHFD USDCM Vol. 3 Eq. 3-1, 3-2), a from the drain time (Table 3-2: 12 h 0.8, 24 h 0.9, 40 h 1.0), composite imperviousness by area for each BMP. d6 = {d6 * inchToSmall:G4} {(us ? "in" : "mm")}. These are the MHFD methods; confirm the jurisdiction's water quality criteria (volume, drain time, allowed BMPs) before use."));

        var summary = new List<string>
        {
            $"{subs.Count} sub-basins, {subs.Sum(s => s.Area):G6} {A}; {bmpRows.Count} BMPs sized.",
            $"Impervious area treated: {100 * share:F1} % ({impTreated:G4} of {impTotal:G4} {A} impervious).",
        };
        summary.AddRange(bmpRows.Select(r => $"{r[0]} ({r[1]}): WQCV {r[7]:N0} {V}; {r[9]}."));
        tables.Insert(0, new("Water quality capture volume by sub-basin", ["Sub-basin", $"Area ({A})", "Imperviousness (%)", "BMP", "a", $"WQCV ({small})", $"WQCV ({V})"], subRows,
            ["a from the BMP's drain time (1.0 for untreated sub-basins, shown for reference)."]));
        tables.Insert(1, new("BMP summary", ["BMP", "Type", $"Area ({A})", "Composite imperviousness (%)", "WQCV drain time (h)", "a", $"WQCV ({small})", $"WQCV ({V})", $"Design volume ({V})", "Size", "Check"], bmpRows));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }
}
