using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>Shared underdrain hydraulics: Manning full-pipe capacity and a numerical sloping-layer drainage model.</summary>
public static class Underdrain
{
    /// <summary>Full-flow capacity (cfs) of a circular pipe by Manning: (1.486/n)·A·R^(2/3)·S^½, D in inches.</summary>
    public static double PipeFull(double diaIn, double n, double slope)
    {
        double d = diaIn / 12, a = Math.PI * d * d / 4, r = d / 4;
        return 1.486 / n * a * Math.Pow(r, 2.0 / 3) * Math.Sqrt(slope);
    }

    /// <summary>
    /// Time (days) for a saturated layer of thickness <paramref name="h0"/> on a bed sloping <paramref name="s"/> toward a free
    /// drain at distance <paramref name="l"/> to reach drained fraction <paramref name="u"/>: the Boussinesq equation
    /// ne·∂h/∂t = ∂/∂x[k·h·(∂h/∂x − S)] (x up-slope from the drain) by explicit finite volumes, no flow at the upper end, h = 0 at the drain.
    /// Returns ∞ when the fraction is not reached in <paramref name="maxDays"/>.
    /// </summary>
    public static double DrainTime(double l, double s, double h0, double k, double ne, double u, double maxDays = 3650)
    {
        const int n = 80;
        double dx = l / n;
        var h = Enumerable.Repeat(h0, n).ToArray();   // cell centres, cell 0 next to the drain
        var q = new double[n + 1];                    // face fluxes, positive toward the drain (face 0 = drain)
        double dt = 0.2 * Math.Min(ne * dx * dx / (k * h0), s > 0 ? ne * dx / (k * s) : double.PositiveInfinity);
        double total = h0 * l, t = 0;
        for (long step = 0; t < maxDays && step < 20_000_000; step++)
        {
            // Drain face: h = 0 at x = 0, gradient over half a cell.
            q[0] = k * h[0] * (h[0] / (0.5 * dx) + s);
            for (int i = 1; i < n; i++)
            {
                double hf = 0.5 * (h[i - 1] + h[i]);
                q[i] = k * hf * ((h[i] - h[i - 1]) / dx + s);
            }
            q[n] = 0;
            for (int i = 0; i < n; i++) h[i] = Math.Max(0, h[i] + dt / (ne * dx) * (q[i + 1] - q[i]));
            t += dt;
            if (1 - h.Sum() * dx / total >= u) return t;
        }
        return double.PositiveInfinity;
    }
}

/// <summary>
/// Pavement subsurface drainage (FHWA-NHI-99-028 / FHWA-HI-99-028, Moulton 1980; AASHTO 1993 quality of drainage):
/// Cedergren crack infiltration, the permeable base's steady capacity along the resultant flow path, time to drain 50 %
/// by a numerical sloping-layer model, and the edge drain pipe capacity and outlet spacing.
/// </summary>
public sealed class PavementEdgeDrainTool : IDesignTool
{
    public string Id => "pavement-edge-drain";
    public string Name => "Pavement edge drains and permeable base (time to drain, outlet spacing)";
    public string Description => "Pavement subsurface drainage to FHWA-NHI-99-028: infiltration qi = Ic·(Nc/W + Wc/(W·Cs)) + kp (Cedergren); resultant slope SR = √(S² + Sx²) and length LR = W·√(1 + (S/Sx)²); permeable base steady capacity k·H·SR against qi·LR; time to drain 50 % of the base by a numerical Boussinesq model of the sloping layer, rated by the AASHTO 1993 quality of drainage (excellent 2 h, good 1 day, fair 1 week, poor 1 month); edge drain pipe full-flow capacity by Manning and the maximum outlet spacing.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Pavement edge drain and permeable base. U.S. units: ft, in, ft/day.
        lanes = 2                   # lanes draining to this edge (Nc = lanes + 1 longitudinal cracks)
        width = 28                  # ft, crown to edge drain (lanes + paved shoulder), W
        cross slope = 0.02          # Sx, ft/ft
        grade = 0.03                # S, ft/ft (also the edge drain slope)
        crack infiltration = 2.4    # Ic, ft³/day per ft of crack (Cedergren)
        transverse crack spacing = 40   # Cs, ft
        pavement permeability = 0   # kp, ft/day
        base thickness = 4          # in, H
        base permeability = 1000    # k, ft/day (open-graded base)
        effective porosity = 0.25   # ne
        pipe diameter = 4           # in
        pipe n = 0.012              # smooth-wall; corrugated about 0.024
        outlet spacing = 250        # ft between outlets (optional; the maximum is reported)
        """;

    public static double Infiltration(double ic, double nc, double w, double cs, double kp) => ic * (nc / w + w / (w * cs)) + kp;

    public static string Quality(double hours) => hours <= 2 ? "Excellent" : hours <= 24 ? "Good" : hours <= 168 ? "Fair" : hours <= 720 ? "Poor" : "Very poor";

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Pavement edge drains and permeable base";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        if (context.Units == UnitSystem.SI) msgs.Add(CalcMessage.Info("I-ED-001", "This tool works in U.S. units: ft, in and ft/day."));
        double lanes = p.Number("lanes", double.NaN), w = p.Number("width", double.NaN), sx = p.Number("cross slope", double.NaN), s = p.Number("grade", double.NaN);
        double ic = p.Number("crack infiltration", 2.4), cs = p.Number("transverse crack spacing", 40), kp = p.Number("pavement permeability", 0);
        double hIn = p.Number("base thickness", double.NaN), k = p.Number("base permeability", double.NaN), ne = p.Number("effective porosity", 0.25);
        double dia = p.Number("pipe diameter", 4), n = p.Number("pipe n", 0.012);
        double? spacing = p.OptionalNumber("outlet spacing");
        if (!(lanes >= 1) || !(w > 0)) msgs.Add(CalcMessage.Error("E-ED-001", "Give lanes (at least 1) and the width from crown to edge drain (ft)."));
        if (!(sx > 0) || !(s >= 0)) msgs.Add(CalcMessage.Error("E-ED-002", "Give the cross slope above zero and the grade (zero or more), ft/ft."));
        if (!(hIn > 0) || !(k > 0) || ne is not (> 0 and < 1)) msgs.Add(CalcMessage.Error("E-ED-003", "Give the base thickness (in) and permeability (ft/day) above zero and the effective porosity between 0 and 1."));
        if (!(ic >= 0) || !(cs > 0) || kp < 0 || !(dia > 0) || !(n > 0) || spacing is <= 0) msgs.Add(CalcMessage.Error("E-ED-004", "Crack infiltration, permeability and pipe values must be positive (crack spacing, pipe diameter, n and outlet spacing above zero)."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double h = hIn / 12, nc = lanes + 1;
        double qi = Infiltration(ic, nc, w, cs, kp);
        double sr = Math.Sqrt(s * s + sx * sx), lr = w * Math.Sqrt(1 + Math.Pow(s / sx, 2));
        double demand = qi * lr, capacity = k * h * sr;
        double t50 = Underdrain.DrainTime(lr, sr, h, k, ne, 0.5);
        double hours = t50 * 24;
        string quality = double.IsFinite(hours) ? Quality(hours) : "Very poor";
        double perFt = qi * w;   // ft³/day per ft of edge drain
        double pipeCfs = s > 0 ? Underdrain.PipeFull(dia, n, s) : 0;
        double maxSpacing = perFt > 0 && pipeCfs > 0 ? pipeCfs * 86400 / perFt : double.PositiveInfinity;
        var summary = new List<string>
        {
            $"Infiltration qi = {qi:F3} ft³/day/ft²; the base carries {capacity:F2} ft³/day per ft against {demand:F2} needed ({(capacity >= demand ? "OK" : "not enough")}).",
            $"Time to drain 50 %: {(double.IsFinite(hours) ? $"{hours:F1} h" : "more than 10 years")}, AASHTO quality of drainage {quality}.",
            s > 0 ? $"A {dia:G3} in edge drain (n {n:G3}) at {s:G3} ft/ft carries {pipeCfs:F3} cfs: outlets at most {maxSpacing:N0} ft apart{(spacing is { } sp ? $" ({sp:G4} ft given: {(sp <= maxSpacing ? "OK" : "too far")})" : "")}." : "Flat grade: give the edge drain pipe slope as grade to check its capacity.",
        };
        if (capacity < demand) msgs.Add(CalcMessage.Warning("W-ED-001", $"The base needs k ≥ {demand / (h * sr):N0} ft/day (or more thickness) to carry the infiltration at steady state."));
        if (hours > 24) msgs.Add(CalcMessage.Warning("W-ED-002", $"Time to drain 50 % is {hours:F0} h ({quality}); FHWA recommends 1 h for interstates and freeways and 2 h for other roads."));
        if (spacing is { } sp2 && sp2 > maxSpacing) msgs.Add(CalcMessage.Warning("W-ED-003", $"Outlets {sp2:G4} ft apart exceed the {maxSpacing:N0} ft the pipe can carry."));
        if (s == 0) msgs.Add(CalcMessage.Warning("W-ED-004", "The edge drain needs a slope to drain; a zero grade gives no pipe capacity."));
        List<IReadOnlyList<object?>> rows =
        [
            ["Longitudinal cracks Nc = lanes + 1", nc, ""], ["Infiltration qi", R(qi, 4), "ft³/day/ft²"],
            ["Resultant slope SR", R(sr, 4), "ft/ft"], ["Resultant length LR", R(lr, 2), "ft"],
            ["Inflow to carry qi·LR", R(demand, 3), "ft³/day/ft"], ["Base steady capacity k·H·SR", R(capacity, 3), "ft³/day/ft"],
            ["Time factor m = ne·LR²/(k·H)", R(ne * lr * lr / (k * h), 3), "days"],
            ["Time to drain 50 %", double.IsFinite(hours) ? R(hours, 2) : null, "h"], ["AASHTO quality of drainage", quality, ""],
            ["Flow to the edge drain qi·W", R(perFt, 3), "ft³/day per ft"],
            ["Edge drain full-flow capacity", R(pipeCfs, 4), "cfs"], ["Maximum outlet spacing", double.IsFinite(maxSpacing) ? R(maxSpacing, 0) : null, "ft"],
        ];
        var tables = new List<ToolTable>
        {
            new("Permeable base and edge drain", ["Item", "Value", "Unit"], rows,
            [
                "qi = Ic·(Nc/W + Wc/(W·Cs)) + kp with Wc = W (Cedergren; FHWA-NHI-99-028).",
                "SR = √(S² + Sx²), LR = W·√(1 + (S/Sx)²). Steady capacity by Darcy with the gradient taken as SR.",
                "Time to drain: Boussinesq sloping-layer equation solved numerically from a saturated base to 50 % drained (the condition behind the Casagrande–Shannon and Barber–Sawyer charts).",
                "AASHTO (1993) quality of drainage: excellent ≤ 2 h, good ≤ 1 day, fair ≤ 1 week, poor ≤ 1 month.",
            ]),
        };
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => Math.Round(v, d) + 0.0;
}

/// <summary>
/// Bioretention underdrain: Darcy flow through the media, perforated underdrain capacity, an optional restricting orifice,
/// and the drain times of the ponded surface water and of the water held in the media and gravel.
/// </summary>
public sealed class BioretentionUnderdrainTool : IDesignTool
{
    public string Id => "bioretention-underdrain";
    public string Name => "Bioretention underdrain (media flow, pipe capacity, drain times)";
    public string Description => "Bioretention underdrain design: media flow by Darcy Q = k·A·(h + d)/d; perforated underdrain full-flow capacity by Manning against that flow; optional restricting orifice Q = Cd·a·√(2g·head); time for the ponded water to soak in (falling head through the media, plus native infiltration), and time for the gravel and the media's drainable water to empty through the underdrain, checked against 48 h for the surface and 72 h below it by default.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Bioretention underdrain. U.S. units: ft², ft, in, in/hr.
        area = 1200                 # ft², filter (media) surface
        ponding depth = 1.0         # ft
        media depth = 2.0           # ft
        media permeability = 2.0    # in/hr, saturated hydraulic conductivity of the planting media
        media drainable porosity = 0.20
        gravel depth = 1.0          # ft
        gravel porosity = 0.40
        native infiltration = 0     # in/hr below the gravel (0 for a lined cell)
        pipe diameter = 6           # in, perforated underdrain
        pipes = 1
        pipe slope = 0.005
        pipe n = 0.012
        orifice diameter = 1.0      # in, restricting orifice at the outlet (optional)
        orifice coefficient = 0.6
        surface drain limit = 48    # h
        subsurface drain limit = 72 # h
        """;

    /// <summary>Darcy flow through the media (cfs): k in in/hr, area ft², head h and depth d in ft.</summary>
    public static double MediaFlow(double kInHr, double area, double h, double d) => kInHr / 12 / 3600 * area * (h + d) / d;

    /// <summary>Orifice flow (cfs): diameter in inches, head in ft above its centre.</summary>
    public static double Orifice(double diaIn, double cd, double head) =>
        head <= 0 ? 0 : cd * Math.PI * Math.Pow(diaIn / 12, 2) / 4 * Math.Sqrt(2 * 32.174 * head);

    public sealed record Times(double SurfaceHours, double SubsurfaceHours);

    /// <summary>
    /// Drain times. The surface drains by Darcy through saturated media (limited by the orifice when given) plus native
    /// infiltration; then the drainable water in the media and the gravel empties through the orifice (or freely), with
    /// the media releasing water no faster than k·A (unit gradient).
    /// </summary>
    public static Times Drain(double area, double pond, double d, double kInHr, double nm, double g, double ng, double fInHr, double? orificeIn, double cd)
    {
        double k = kInHr / 12 / 3600, f = fInHr / 12 / 3600, dt = 30;   // seconds
        double OrificeOr(double head, double other) => orificeIn is { } o ? Math.Min(other, Orifice(o, cd, head)) : other;
        double h = pond, t = 0;
        while (h > 1e-6 && t < 3600 * 24 * 365)
        {
            double qMedia = MediaFlow(kInHr, area, h, d), q = OrificeOr(h + d + g - (orificeIn ?? 0) / 24, qMedia) + f * area;
            h -= q * dt / area; t += dt;
        }
        double surface = t / 3600;
        // Water level y above the bottom of the gravel: gravel to g, media above to g + d.
        double t2;
        if (orificeIn is not { } od) t2 = d * nm / (k + f);   // gravel empties at once; the media releases k·A (unit gradient)
        else
        {
            double y = g + d; t2 = 0;
            double floor = od / 24;   // water below the orifice centre stays in the gravel
            while (y > floor + 1e-4 && t2 < 3600 * 24 * 365)
            {
                double porosity = y > g ? nm : ng;
                double q = Math.Min(Orifice(od, cd, y - od / 24), y > g ? k * area : double.PositiveInfinity) + f * area;
                if (q <= 1e-12) { t2 = double.PositiveInfinity; break; }
                y -= q * dt / (porosity * area); t2 += dt;
            }
        }
        return new(surface, double.IsFinite(t2) ? t2 / 3600 : double.PositiveInfinity);
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Bioretention underdrain";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        if (context.Units == UnitSystem.SI) msgs.Add(CalcMessage.Info("I-BU-001", "This tool works in U.S. units: ft², ft, in and in/hr."));
        double area = p.Number("area", double.NaN), pond = p.Number("ponding depth", double.NaN), d = p.Number("media depth", double.NaN), k = p.Number("media permeability", double.NaN);
        double nm = p.Number("media drainable porosity", 0.2), g = p.Number("gravel depth", 1.0), ng = p.Number("gravel porosity", 0.4), f = p.Number("native infiltration", 0);
        double dia = p.Number("pipe diameter", 6), pipes = p.Number("pipes", 1), slope = p.Number("pipe slope", 0.005), n = p.Number("pipe n", 0.012);
        double? orifice = p.OptionalNumber("orifice diameter");
        double cd = p.Number("orifice coefficient", 0.6), surfLimit = p.Number("surface drain limit", 48), subLimit = p.Number("subsurface drain limit", 72);
        if (!(area > 0) || !(pond >= 0) || !(d > 0) || !(k > 0)) msgs.Add(CalcMessage.Error("E-BU-001", "Give the area, media depth and media permeability above zero and the ponding depth (zero or more)."));
        if (nm is not (> 0 and < 1) || ng is not (> 0 and < 1) || g < 0 || f < 0) msgs.Add(CalcMessage.Error("E-BU-002", "Porosities must be between 0 and 1; gravel depth and native infiltration not negative."));
        if (!(dia > 0) || !(pipes >= 1) || !(slope > 0) || !(n > 0) || orifice is <= 0 || !(cd > 0)) msgs.Add(CalcMessage.Error("E-BU-003", "Pipe diameter, slope, n, orifice diameter and coefficient must be above zero, with at least one pipe."));
        if (orifice is { } od && od >= dia) msgs.Add(CalcMessage.Error("E-BU-004", "The orifice must be smaller than the underdrain pipe."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double qMax = MediaFlow(k, area, pond, d);
        double pipeCap = pipes * Underdrain.PipeFull(dia, n, slope);
        double orificeCap = orifice is { } o ? Orifice(o, cd, pond + d + g - o / 24) : double.NaN;
        var times = Drain(area, pond, d, k, nm, g, ng, f, orifice, cd);
        var summary = new List<string>
        {
            $"Media flow at full ponding {qMax:F3} cfs; underdrain capacity {pipeCap:F3} cfs ({(pipeCap >= qMax ? "OK" : "too small")}){(orifice is { } o2 ? $"; the {o2:G3} in orifice passes {orificeCap:F3} cfs and controls" + (orificeCap < qMax ? "" : " only below full ponding") : "")}.",
            $"Ponded water soaks in within {times.SurfaceHours:F1} h (limit {surfLimit:G3} h); media and gravel drain in {(double.IsFinite(times.SubsurfaceHours) ? $"{times.SubsurfaceHours:F1} h" : "never")} (limit {subLimit:G3} h).",
        };
        if (pipeCap < qMax) msgs.Add(CalcMessage.Warning("W-BU-001", $"The underdrain carries {pipeCap:F3} cfs, less than the {qMax:F3} cfs the media can pass: use a larger or another pipe."));
        if (times.SurfaceHours > surfLimit) msgs.Add(CalcMessage.Warning("W-BU-002", $"Surface drain time {times.SurfaceHours:F1} h exceeds {surfLimit:G3} h."));
        if (times.SubsurfaceHours > subLimit) msgs.Add(CalcMessage.Warning("W-BU-003", $"Media and gravel drain time {times.SubsurfaceHours:F1} h exceeds {subLimit:G3} h: enlarge the orifice or the media permeability."));
        if (orifice is null && times.SurfaceHours < 12) msgs.Add(CalcMessage.Info("I-BU-002", $"Without an orifice the cell drains in {times.SurfaceHours:F1} h; a restricting orifice lengthens the contact time if the manual asks for one."));
        List<IReadOnlyList<object?>> rows =
        [
            ["Media flow at full ponding k·A·(h + d)/d", R(qMax, 4), "cfs"],
            ["Media flow at zero ponding k·A", R(MediaFlow(k, area, 0, d), 4), "cfs"],
            [$"Underdrain capacity ({pipes:G3} × {dia:G3} in, n {n:G3}, S {slope:G3})", R(pipeCap, 4), "cfs"],
            ["Orifice capacity at full ponding", orifice is null ? null : R(orificeCap, 4), "cfs"],
            ["Surface ponding volume", R(area * pond, 0), "ft³"],
            ["Drainable water in media and gravel", R(area * (d * nm + g * ng), 0), "ft³"],
            ["Surface drain time", R(times.SurfaceHours, 1), "h"],
            ["Media and gravel drain time", double.IsFinite(times.SubsurfaceHours) ? R(times.SubsurfaceHours, 1) : null, "h"],
        ];
        var tables = new List<ToolTable>
        {
            new("Underdrain", ["Item", "Value", "Unit"], rows,
            [
                "Darcy through saturated media: Q = k·A·(h + d)/d, falling head stepped every 30 s; outflow limited by the orifice when it is smaller.",
                "Below the surface the media releases its drainable water no faster than k·A (unit gradient); the gravel drains through the orifice (or at once without one). Native infiltration adds at every stage.",
                "Underdrain pipe full-flow capacity by Manning; orifice Q = Cd·a·√(2g·head), head to the orifice centre at the bottom of the gravel.",
            ]),
        };
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => Math.Round(v, d) + 0.0;
}
