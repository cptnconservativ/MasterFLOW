using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Flow splitter (diversion structure) for an off-line water quality BMP: a low outlet (orifice or pipe) sends the water
/// quality flow to the BMP and an overflow weir bypasses the rest. Sizes the orifice for a given crest, or sets the crest
/// for a given orifice, then finds the water surface at the design storm, the flow that still reaches the BMP, and the
/// weir length that keeps the water surface under a limit.
/// </summary>
public sealed class FlowSplitterTool : IDesignTool
{
    public string Id => "flow-splitter";
    public string Name => "Water quality flow splitter (diversion structure)";
    public string Description => "Off-line BMP diversion: orifice Q = Cd·A·√(2g·h) to the BMP (h to the orifice centroid, free outfall) and bypass weir Q = Cw·L·H^1.5. Sizes the orifice (or sets the crest) so the water quality flow reaches the crest, solves the water surface where orifice + weir carry the design storm, reports the flow still sent to the BMP against its capacity, and the weir length for a water surface limit.";
    public bool UsesNetwork => false;

    /// <summary>Orifice discharge with head to the centroid; zero below the centroid.</summary>
    public static double Orifice(double cd, double d, double centroid, double wse, double g)
        => wse <= centroid ? 0 : cd * Math.PI * d * d / 4 * Math.Sqrt(2 * g * (wse - centroid));

    /// <summary>Weir discharge Cw·L·H^1.5; zero below the crest.</summary>
    public static double Weir(double cw, double l, double crest, double wse) => wse <= crest ? 0 : cw * l * Math.Pow(wse - crest, 1.5);

    /// <summary>Water surface at which orifice and weir together pass <paramref name="q"/> (bisection; the total rises with the water surface).</summary>
    public static double Wse(double q, double cd, double d, double centroid, double cw, double l, double crest, double g)
    {
        double lo = centroid, hi = Math.Max(crest, centroid) + 1;
        double Total(double z) => Orifice(cd, d, centroid, z, g) + Weir(cw, l, crest, z);
        while (Total(hi) < q) hi += (hi - lo);
        for (int k = 0; k < 200; k++) { double m = 0.5 * (lo + hi); if (Total(m) >= q) hi = m; else lo = m; }
        return hi;
    }

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Flow splitter. SI: m³/s, m; orifice diameter in mm.
        Q design = 1.25
        Q wq = 0.05
        orifice invert = 30.00
        orifice diameter = 200
        weir length = 1.8
        weir C = 1.84
        Cd = 0.61
        max WSE = 31.20
        max treatment flow = 0.07
        """ : """
        # Flow splitter for an off-line water quality BMP. U.S.: cfs, ft; orifice diameter in inches.
        # Q wq: the water quality flow (Rapid City SWQM 2009, from the water quality storm); Q design: the storm the structure must pass.
        # Leave the orifice diameter blank to size it for the crest, or the crest blank to set it for the orifice.
        Q design = 45
        Q wq = 1.8
        orifice invert = 100.00
        orifice diameter = 8
        # crest = 101.60
        weir length = 6
        weir C = 3.0           # sharp-crested 3.3, broad-crested 2.6–3.0 (ft-s)
        Cd = 0.61
        max WSE = 104.0        # optional: e.g. the HGL limit at the structure or the upstream pipe crown
        max treatment flow = 2.5   # optional: the most the BMP can take
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Water quality flow splitter";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.80665 : 32.174, dl = si ? 1000 : 12;
        string L = si ? "m" : "ft", Uq = si ? "m³/s" : "cfs", Ud = si ? "mm" : "in.";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double qd = p.Number("Q design", double.NaN, "Q design"), qwq = p.Number("Q wq", double.NaN, "Q wq");
        double inv = p.Number("orifice invert", double.NaN, "orifice invert");
        double? dIn = p.OptionalNumber("orifice diameter", "orifice diameter"), crestIn = p.OptionalNumber("crest", "crest");
        double lw = p.Number("weir length", double.NaN, "weir length"), cw = p.Number("weir C", si ? 1.84 : 3.0, "weir C"), cd = p.Number("Cd", 0.61, "Cd");
        double? maxWse = p.OptionalNumber("max WSE", "max WSE"), maxTreat = p.OptionalNumber("max treatment flow", "max treatment flow");
        if (!(qd > 0) || !(qwq > 0) || qwq >= qd) msgs.Add(CalcMessage.Error("E-FS-001", "Give Q design and Q wq above zero, with Q wq less than Q design."));
        if (double.IsNaN(inv)) msgs.Add(CalcMessage.Error("E-FS-002", "Give the orifice invert elevation."));
        if (!(lw > 0) || !(cw > 0) || !(cd is > 0 and <= 1)) msgs.Add(CalcMessage.Error("E-FS-003", "weir length and weir C must be above zero, and Cd between 0 and 1."));
        if (dIn is null == crestIn is null) msgs.Add(CalcMessage.Error("E-FS-004", "Give either the orifice diameter (the crest is set for it) or the crest (the orifice is sized for it), not both or neither."));
        if (dIn is <= 0) msgs.Add(CalcMessage.Error("E-FS-005", "The orifice diameter must be above zero."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double d, crest;
        string how;
        if (dIn is { } dd)
        {
            d = dd / dl;
            double c0 = inv + d / 2;
            crest = c0 + Math.Pow(qwq / (cd * Math.PI * d * d / 4), 2) / (2 * g);
            how = $"crest set so the {dd:G4} {Ud} orifice passes Q wq";
        }
        else
        {
            crest = crestIn!.Value;
            // Diameter so that Q wq passes with the water at the crest: solve by bisection (the centroid moves with D).
            double lo = 1e-4, hi = 2 * (crest - inv);
            if (!(hi > 0)) return new(title, [], [], [CalcMessage.Error("E-FS-006", "The crest must be above the orifice invert.")]);
            // Q = Cd·(πD²/4)·√(2g(Hc − D/2)) rises with D up to D = 1.6·Hc, so the largest orifice under the crest passes the most.
            if (Orifice(cd, hi / 2, inv + hi / 4, crest, g) < qwq)
            {
                double dMax = crest - inv;
                return new(title, [], [], [CalcMessage.Error("E-FS-007", $"No orifice below the crest passes Q wq: even a {dMax * dl:F0} {Ud} orifice reaching the crest passes only {Orifice(cd, dMax, inv + dMax / 2, crest, g):F3} {Uq}. Raise the crest or use a larger outlet.")]);
            }
            hi = crest - inv;
            for (int k = 0; k < 200; k++) { double m = 0.5 * (lo + hi); if (Orifice(cd, m, inv + m / 2, crest, g) >= qwq) hi = m; else lo = m; }
            d = hi;
            how = "orifice sized to pass Q wq with the water at the crest";
        }
        double cen = inv + d / 2;
        if (crest < inv + d) msgs.Add(CalcMessage.Warning("W-FS-001", $"The crest ({crest:F2}) is below the top of the orifice ({inv + d:F2}); the orifice flows part full at Q wq and the orifice equation overstates its capacity. Use a smaller orifice and a higher crest."));
        double wse = Wse(qd, cd, d, cen, cw, lw, crest, g);
        double qT = Orifice(cd, d, cen, wse, g), qB = qd - qT;
        var summary = new List<string>
        {
            $"Orifice {d * dl:F1} {Ud} at invert {inv:F2}, bypass crest {crest:F2} {L} ({how}).",
            $"At Q design {qd:G4} {Uq}: water surface {wse:F2} {L}, {qT:F3} {Uq} to the BMP and {qB:F3} {Uq} over the weir ({wse - crest:F2} {L} over the crest).",
        };
        if (maxTreat is { } mt && qT > mt) msgs.Add(CalcMessage.Warning("W-FS-002", $"{qT:F3} {Uq} still reaches the BMP at the design storm, more than its {mt:G4} {Uq}; lengthen the weir, use a lower crest with a smaller orifice, or add a second bypass."));
        double? lReq = null;
        if (maxWse is { } mw)
        {
            if (mw <= crest) msgs.Add(CalcMessage.Warning("W-FS-003", $"max WSE {mw:F2} is at or below the crest {crest:F2}; no weir length can meet it."));
            else if (wse > mw)
            {
                double qWeir = qd - Orifice(cd, d, cen, mw, g);
                lReq = qWeir / (cw * Math.Pow(mw - crest, 1.5));
                msgs.Add(CalcMessage.Warning("W-FS-004", $"Water surface {wse:F2} is above max WSE {mw:F2}; a {lReq:F2} {L} weir keeps it at the limit."));
            }
        }
        var table = new ToolTable("Flow split at the design storm", ["Item", "Value", "Unit"],
        [
            ["Orifice diameter", R(d * dl, 2), Ud], ["Orifice invert", R(inv, 2), L], ["Orifice centroid", R(cen, 3), L],
            ["Bypass weir crest", R(crest, 3), L], ["Weir length", R(lw, 2), L], ["Water surface at Q design", R(wse, 3), L],
            ["Flow to the BMP at Q design", R(qT, 4), Uq], ["Bypass flow at Q design", R(qB, 4), Uq],
            ["BMP share of Q design", R(100 * qT / qd, 1), "%"],
            ["Weir length for max WSE", lReq is { } lr ? R(lr, 2) : null, L],
        ],
        [$"Orifice Q = {cd:G3}·A·√(2g·h), h to the centroid, free outfall to the BMP (a submerged outfall lowers this flow). Weir Q = {cw:G3}·L·H^1.5."]);
        var rows = new List<IReadOnlyList<object?>>();
        double top = Math.Max(wse, maxWse ?? wse) + 0.25 * (wse - inv);
        for (int i = 0; i <= 20; i++)
        {
            double z = inv + (top - inv) * i / 20;
            double a = Orifice(cd, d, cen, z, g), b = Weir(cw, lw, crest, z);
            rows.Add([R(z, 3), R(a, 4), R(b, 4), R(a + b, 4)]);
        }
        var rating = new ToolTable("Splitter rating", [$"Water surface ({L})", $"To BMP ({Uq})", $"Bypass ({Uq})", $"Total ({Uq})"], rows);
        msgs.Add(CalcMessage.Info("I-FS-001", "Check the bypass pipe and the BMP inlet hydraulics separately; tailwater from the BMP or the bypass pipe can raise the splitter water surface."));
        return new ToolResult(title, summary, [table, rating], msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
