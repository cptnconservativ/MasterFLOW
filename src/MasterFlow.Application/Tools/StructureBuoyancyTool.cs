using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Flotation check of an empty precast or cast-in-place structure (manhole, inlet, vault, outlet structure) in high
/// groundwater: uplift = unit weight of water × outside volume below the water table; resistance = the concrete's weight
/// plus the soil standing on the base extension (moist above the water table, buoyant below). Reports the factor of safety,
/// the ballast concrete needed inside, and the base extension that would reach the required factor.
/// </summary>
public sealed class StructureBuoyancyTool : IDesignTool
{
    public string Id => "structure-buoyancy";
    public string Name => "Structure buoyancy (flotation) check";
    public string Description => "Empty round or rectangular structure in groundwater: uplift U = γw·(outside volume below the water table); resistance W = concrete + other dead load + soil on the base extension (γ moist above the water table, γsat − γw below); FS = W/U against the required factor (default 1.25). Gives the inside ballast concrete and the base extension that reach the required FS. Side friction is ignored.";
    public bool UsesNetwork => false;

    /// <summary>Geometry of one structure, all lengths in ft or m; inside width is ignored for round structures.</summary>
    public sealed record Structure(string Name, bool Round, double InsideL, double InsideW, double Wall, double Base, double Ext, double TopSlab, double Opening, double Bottom, double Top, double Gw);

    /// <summary>Plan area of the outside of the barrel, of the base slab, and the inside.</summary>
    public static (double Outer, double BaseArea, double Inner) Areas(Structure s, double ext)
    {
        if (s.Round)
        {
            double o = s.InsideL + 2 * s.Wall, b = o + 2 * ext;
            return (Math.PI * o * o / 4, Math.PI * b * b / 4, Math.PI * s.InsideL * s.InsideL / 4);
        }
        double lo = s.InsideL + 2 * s.Wall, wo = s.InsideW + 2 * s.Wall;
        return (lo * wo, (lo + 2 * ext) * (wo + 2 * ext), s.InsideL * s.InsideW);
    }

    /// <summary>Uplift, concrete weight and soil weight on the extension (force units of the unit weights given).</summary>
    public static (double Uplift, double Concrete, double Soil) Forces(Structure s, double ext, double gc, double gw, double gm, double gsat)
    {
        var (outer, baseA, inner) = Areas(s, ext);
        double h = s.Top - s.Bottom;
        double barrelH = h - s.Base;
        double sub = Math.Clamp(s.Gw - s.Bottom, 0, h);                       // submerged height
        double subBase = Math.Min(sub, s.Base), subBarrel = Math.Max(0, sub - s.Base);
        double uplift = gw * (baseA * subBase + outer * subBarrel);
        double open = s.Round ? Math.PI * s.Opening * s.Opening / 4 : s.Opening * s.Opening;
        double conc = baseA * s.Base + (outer - inner) * Math.Max(0, barrelH - s.TopSlab) + Math.Max(0, outer - open) * s.TopSlab;
        double ring = baseA - outer;
        double soilTop = s.Top, soilBot = s.Bottom + s.Base;
        double wet = Math.Clamp(s.Gw - soilBot, 0, soilTop - soilBot), dry = (soilTop - soilBot) - wet;
        double soil = ring * (dry * gm + wet * (gsat - gw));
        return (uplift, gc * conc, soil);
    }

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Structure buoyancy. SI: m and kN/m³; elevations in m. Top = rim/ground (soil is assumed up to it).
        concrete = 23.6
        water = 9.81
        soil moist = 18.85
        soil saturated = 19.65
        FS required = 1.25
        [Structures]
        Name	Shape	Inside L or D	Inside W	Wall	Base	Ext	Top slab	Opening	Bottom	Top	GW
        MH-4	round	1.5		0.15	0.2	0	0.2	0.6	29.0	33.0	32.5
        Outlet box	box	1.8	1.2	0.2	0.3	0.15	0.2	0.6	29.5	32.0	31.8
        """ : """
        # Structure buoyancy (empty structure, design high groundwater). U.S.: ft and pcf; elevations in ft.
        # Top = rim/ground elevation (soil is assumed up to it). Ext = base slab projection beyond the outside wall.
        # Opening = top slab opening (diameter for round, side of a square for box). Bottom = bottom of the base slab.
        concrete = 150         # pcf
        water = 62.4
        soil moist = 120       # above the water table
        soil saturated = 125   # below the water table (buoyant = 125 − 62.4)
        FS required = 1.25
        # other weight = 0     # castings, riser rings, cone: added to every structure, lb
        [Structures]
        Name	Shape	Inside L or D	Inside W	Wall	Base	Ext	Top slab	Opening	Bottom	Top	GW
        MH-4	round	5		0.5	0.67	0	0.67	2	95.0	108.0	106.5
        Outlet box	box	6	4	0.67	1.0	0.5	0.67	2	96.0	104.0	103.5
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Structure buoyancy";
        bool si = context.Units == UnitSystem.SI;
        string L = si ? "m" : "ft", F = si ? "kN" : "lb", V = si ? "m³" : "ft³";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double gc = p.Number("concrete", si ? 23.6 : 150, "concrete"), gw = p.Number("water", si ? 9.81 : 62.4, "water");
        double gm = p.Number("soil moist", si ? 18.85 : 120, "soil moist"), gs = p.Number("soil saturated", si ? 19.65 : 125, "soil saturated");
        double fsReq = p.Number("FS required", 1.25, "FS required"), other = p.Number("other weight", 0, "other weight");
        if (!(gc > gw) || !(gw > 0) || !(gm > 0) || !(gs >= gw)) msgs.Add(CalcMessage.Error("E-BY-001", "Unit weights must be positive, concrete heavier than water, and saturated soil at least as heavy as water."));
        if (!(fsReq >= 1)) msgs.Add(CalcMessage.Error("E-BY-002", "FS required must be 1.0 or more (1.1–1.5 is usual)."));
        if (!(other >= 0)) msgs.Add(CalcMessage.Error("E-BY-007", "other weight cannot be negative."));
        var list = new List<Structure>();
        foreach (var r in p.Rows("Structures"))
        {
            string n = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Structure {list.Count + 1}";
            string shape = (r.Length > 1 ? r[1] : "").Trim().ToLowerInvariant();
            double?[] v = Enumerable.Range(2, 10).Select(i => p.Cell(r, i, i switch { 2 => "inside L or D", 3 => "inside W", 4 => "wall", 5 => "base", 6 => "ext", 7 => "top slab", 8 => "opening", 9 => "bottom", 10 => "top", _ => "GW" }, n)).ToArray();
            bool round = shape is "round" or "circular" or "manhole";
            if (!round && shape is not ("box" or "rectangular" or "vault")) { msgs.Add(CalcMessage.Error("E-BY-003", $"{n}: shape \"{shape}\" is not round or box.")); continue; }
            if (v[0] is not > 0 || (!round && v[1] is not > 0) || v[2] is not > 0 || v[3] is not > 0 || v[7] is null || v[8] is null || v[9] is null)
            { msgs.Add(CalcMessage.Error("E-BY-004", $"{n}: give inside size, wall and base thickness (above zero), and the bottom, top and groundwater elevations.")); continue; }
            double ext = v[4] ?? 0, ts = v[5] ?? 0, op = v[6] ?? 0;
            if (ext < 0 || ts < 0 || op < 0 || v[8] <= v[7] || v[8] - v[7] <= v[3] + ts) { msgs.Add(CalcMessage.Error("E-BY-005", $"{n}: the top must be above the bottom by more than the base and top slabs, and Ext, Top slab and Opening cannot be negative.")); continue; }
            if (op >= v[0]!.Value + 2 * v[2]!.Value) { msgs.Add(CalcMessage.Error("E-BY-006", $"{n}: the top opening is wider than the structure.")); continue; }
            list.Add(new Structure(n, round, v[0]!.Value, round ? v[0]!.Value : v[1]!.Value, v[2]!.Value, v[3]!.Value, ext, ts, op, v[7]!.Value, v[8]!.Value, v[9]!.Value));
        }
        if (list.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-BY-008", "Give at least one row under [Structures]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var rows = new List<IReadOnlyList<object?>>();
        var summary = new List<string>();
        foreach (var s in list)
        {
            var (u, c, soil) = Forces(s, s.Ext, gc, gw, gm, gs);
            double w = c + soil + other;
            if (u <= 0)
            {
                rows.Add([s.Name, R(Math.Max(0, s.Gw - s.Bottom), 2), 0.0, R(c, 0), R(soil, 0), R(w, 0), null, "no uplift", 0.0, R(s.Ext, 2)]);
                summary.Add($"{s.Name}: groundwater is below the structure; no uplift.");
                continue;
            }
            double fs = w / u;
            bool ok = fs >= fsReq - 1e-9;
            // Ballast placed inside the structure adds weight without adding uplift.
            double ballast = ok ? 0 : (fsReq * u - w) / gc;
            // Base extension that reaches FS (the extension adds soil and concrete, and uplift on the slab).
            double extReq = s.Ext;
            if (!ok)
            {
                double Fs(double e) { var (uu, cc, ss) = Forces(s, e, gc, gw, gm, gs); return (cc + ss + other) / uu; }
                double hi = Math.Max(s.Ext, 0.1) * 2;
                for (int k = 0; k < 40 && Fs(hi) < fsReq; k++) hi *= 2;
                if (Fs(hi) < fsReq) extReq = double.NaN;
                else
                {
                    double lo = s.Ext;
                    for (int k = 0; k < 100; k++) { double mid = 0.5 * (lo + hi); if (Fs(mid) >= fsReq) hi = mid; else lo = mid; }
                    extReq = hi;
                }
                msgs.Add(CalcMessage.Warning("W-BY-001", $"{s.Name}: FS = {fs:F2} is below {fsReq:G3}; add {ballast:F2} {V} of ballast concrete inside, or widen the base extension to {extReq:F2} {L}, or anchor the structure."));
            }
            rows.Add([s.Name, R(Math.Clamp(s.Gw - s.Bottom, 0, s.Top - s.Bottom), 2), R(u, 0), R(c, 0), R(soil, 0), R(w, 0), R(fs, 2), ok ? "OK" : "FLOATS", R(ballast, 2), R(extReq, 2)]);
            summary.Add($"{s.Name}: FS = {fs:F2} ({(ok ? "OK" : $"below {fsReq:G3}")}).");
            if (s.Gw >= s.Top) msgs.Add(CalcMessage.Info("I-BY-001", $"{s.Name}: the water table is at or above the top; the whole structure is submerged."));
        }
        var t = new ToolTable("Flotation check (structure empty)", ["Structure", $"Submerged ({L})", $"Uplift U ({F})", $"Concrete ({F})", $"Soil on ext. ({F})", $"Resisting W ({F})", "FS = W/U", "Check", $"Ballast inside ({V})", $"Ext. for FS ({L})"], rows,
        [
            $"U = γw·(outside volume below the water table), γw = {gw:G4}. W = concrete (γc = {gc:G4}, top-slab opening deducted) + soil on the base extension (γ = {gm:G4} above, {gs:G4} − {gw:G4} below the water table){(other > 0 ? $" + {other:G4} {F} other" : "")}.",
            "Pipe openings in the walls are not deducted, and wall friction and the weight of any water inside are ignored. Ballast is concrete placed inside (no added uplift).",
        ]);
        return new ToolResult(title, summary, [t], msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
