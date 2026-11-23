using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Hydraulic jump and stilling basins: the jump itself (sequent depth by momentum, length, loss, type, position against
/// the tailwater), the floor depression that holds it, USBR Type I, II, III and IV basins and the SAF basin (USBR EM-25;
/// FHWA HEC-14), the USBR Type VI impact basin by Rapid City IDCM §4.9.2(1), Figure 4-8 and Table 4-15, and the CSU
/// roughness-element basin by IDCM §4.9.6 and Figure 4-17. A tailwater rating or channel lets the whole flow range be
/// checked, as USBR design practice asks.
/// </summary>
public sealed class StillingBasinTool : IDesignTool
{
    public string Id => "stilling-basin";
    public string Name => "Hydraulic jump and stilling basins (USBR, SAF, impact, CSU)";
    public string Description => "Hydraulic jump (sequent depth by momentum, length, energy loss, jump type, position against the tailwater) and the basin floor depression that holds it; USBR Type I–IV and SAF stilling basins (USBR EM-25, HEC-14) with block and sill sizes; USBR Type VI impact basin by Rapid City IDCM Figure 4-8 and Table 4-15; CSU roughness-element basin by IDCM §4.9.6 and Figure 4-17. Checks every flow in [Check flows] against a tailwater rating.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Hydraulic jump and stilling basins, SI (m, m³/s).
        Q = 5.66
        basin width = 3.0
        approach slope = 8        # % (normal depth on the chute sets y1)
        approach n = 0.013
        tw bottom width = 3.0     # tailwater channel (normal depth)
        tw side slope = 2
        tw slope = 0.4
        tw n = 0.035
        floor drop = auto
        """ : """
        # Hydraulic jump and stilling basins (USBR EM-25, HEC-14, Rapid City IDCM §4.9).
        # Rectangular chute and basin: give the basin width and the entering flow as y1, V1, the chute's slope and n
        # (normal depth), or the total energy above the basin floor (energy above floor = ...).
        Q = 200
        basin width = 10
        approach slope = 8        # %
        approach n = 0.013
        # Tailwater: a depth above the channel bed (tailwater = ...), a [Tailwater rating] table, or a trapezoidal
        # channel at normal depth:
        tw bottom width = 10
        tw side slope = 3
        tw slope = 0.4            # %
        tw n = 0.035
        floor drop = auto         # basin floor below the channel bed; auto = least that holds the jump for each basin

        # Pipe outlet into a USBR Type VI impact basin (IDCM Figure 4-8, Table 4-15) and a CSU roughness-element basin
        # (IDCM §4.9.6, Figure 4-17). The outlet depth comes from its slope and n (or give outlet depth).
        outlet shape = circular
        outlet span = 3.5
        outlet slope = 3          # %
        outlet n = 0.013
        impact Q = 120
        csu Q = 120
        exit velocity = 6         # CSU: velocity leaving the basin (ft/s)
        expansion ratio = auto    # CSU WB/Wo: 2-4, 5, 6, 7, 8 or auto

        # Every flow the basin must handle (USBR: the jump must hold at all of them).
        [Check flows]
        Q
        50
        100
        150
        200
        """;

    // IDCM Table 4-15 (USBR Basin VI): pipe dia (in), max Q (cfs), W, H, L, a, b, c, d, e, f, g (in), tw, tf, tb, tp, K (in), riprap (in).
    public static readonly double[][] ImpactTable =
    [
        [18, 21, 66, 51, 88, 39, 49, 28, 11, 6, 18, 25, 6, 6.5, 6, 6, 3, 4.0],
        [24, 38, 81, 63, 108, 47, 61, 34, 14, 6, 24, 30, 6, 6.5, 6, 6, 3, 7.0],
        [30, 59, 96, 75, 128, 55, 73, 40, 16, 8, 30, 36, 6, 6.5, 7, 7, 3, 8.5],
        [36, 85, 111, 87, 148, 63, 85, 46, 19, 8, 36, 42, 7, 7.5, 8, 8, 3, 9.0],
        [42, 115, 126, 96, 168, 72, 96, 53, 21, 10, 36, 47, 8, 8.5, 9, 8, 4, 9.5],
        [48, 151, 141, 108, 188, 81, 107, 59, 24, 10, 36, 53, 9, 9.5, 10, 8, 4, 10.5],
        [54, 191, 156, 117, 208, 88, 120, 65, 26, 12, 36, 59, 10, 10.5, 10, 8, 4, 12.0],
        [60, 236, 171, 129, 228, 96, 132, 71, 29, 12, 36, 64, 11, 11.5, 11, 8, 6, 13.0],
        [72, 339, 198, 147, 264, 111, 153, 83, 33, 15, 36, 74, 12, 12.5, 12, 8, 6, 14.0],
    ];

    /// <summary>IDCM Figure 4-17 configurations: expansion ratio label, W1/Wo, rows Nr, elements N.</summary>
    public static readonly (string Ratio, double W1, int Nr, int N)[] CsuConfigs =
    [
        ("2-4", 0.57, 4, 14), ("2-4", 0.57, 5, 17), ("2-4", 0.57, 6, 21),
        ("5", 0.63, 4, 15), ("5", 0.63, 5, 19), ("5", 0.63, 6, 23),
        ("6", 0.60, 4, 17), ("6", 0.60, 5, 22), ("6", 0.60, 6, 27),
        ("7", 0.58, 5, 24), ("7", 0.58, 6, 30),
        ("8", 0.62, 6, 30),
    ];

    /// <summary>IDCM Figure 4-17 CB, by conduit, h/YA and L/h, one value per configuration above (NaN = not given).</summary>
    public static readonly (bool Circular, double HYa, double Lh, double[] Cb)[] CsuCb =
    [
        (false, 0.91, 6, [0.32, 0.28, 0.24, 0.32, 0.28, 0.24, 0.31, 0.27, 0.23, 0.26, 0.22, 0.22]),
        (false, 0.71, 6, [0.44, 0.40, 0.37, 0.42, 0.38, 0.35, 0.40, 0.36, 0.33, 0.34, 0.31, 0.29]),
        (false, 0.48, 12, [0.60, 0.55, 0.51, 0.56, 0.51, 0.47, 0.53, 0.48, 0.43, 0.46, 0.39, 0.35]),
        (false, 0.37, 12, [0.68, 0.66, 0.65, 0.65, 0.62, 0.60, 0.62, 0.58, 0.55, 0.54, 0.50, 0.45]),
        // Circular, 2-4 with 6 rows: the printed column (0.48, 0.40, 0.34, 0.25, 0.18) runs against every other column and
        // is not used (D-321). Circular with WB/Wo = 8 is blank in the figure.
        (true, 0.91, 6, [0.21, 0.20, double.NaN, 0.21, 0.19, 0.17, 0.21, 0.19, 0.17, 0.18, 0.16, double.NaN]),
        (true, 0.71, 6, [0.29, 0.27, double.NaN, 0.27, 0.25, 0.23, 0.25, 0.23, 0.22, 0.22, 0.20, double.NaN]),
        (true, 0.31, 6, [0.38, 0.36, double.NaN, 0.36, 0.34, 0.32, 0.34, 0.32, 0.30, 0.30, 0.28, double.NaN]),
        (true, 0.48, 12, [0.45, 0.42, double.NaN, 0.40, 0.38, 0.36, 0.36, 0.34, 0.32, 0.30, 0.28, double.NaN]),
        (true, 0.37, 12, [0.52, 0.50, double.NaN, 0.48, 0.46, 0.44, 0.44, 0.42, 0.40, 0.38, 0.36, double.NaN]),
    ];

    private enum EntryKind { Depth, Velocity, Normal, Energy }

    /// <summary>Prismatic section: bottom width b and side slope z (z = 0 rectangular).</summary>
    public readonly record struct Trap(double B, double Z)
    {
        public double Area(double y) => B * y + Z * y * y;
        public double Top(double y) => B + 2 * Z * y;
        public double Perimeter(double y) => B + 2 * y * Math.Sqrt(1 + Z * Z);
        /// <summary>First moment of area about the water surface, A·ȳ.</summary>
        public double Moment(double y) => B * y * y / 2 + Z * y * y * y / 3;
    }

    public static double Bisect(Func<double, double> f, double lo, double hi)
    {
        double flo = f(lo);
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi), fm = f(mid);
            if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; } else hi = mid;
            if (hi - lo < 1e-12 * Math.Max(1, hi)) break;
        }
        return 0.5 * (lo + hi);
    }

    private static double Upper(Func<double, double> f, double start)
    {
        double hi = start;
        for (int i = 0; i < 80 && f(hi) < 0; i++) hi *= 2;
        return hi;
    }

    public static double Critical(Trap s, double q, double g) =>
        Bisect(y => q * q * s.Top(y) / (g * Math.Pow(s.Area(y), 3)) - 1, 1e-9, Upper(y => 1 - q * q * s.Top(y) / (g * Math.Pow(s.Area(y), 3)), 1));

    public static double Normal(Trap s, double q, double n, double slope, double k) =>
        Bisect(y => k / n * s.Area(y) * Math.Pow(s.Area(y) / s.Perimeter(y), 2.0 / 3) * Math.Sqrt(slope) - q, 1e-9,
            Upper(y => k / n * s.Area(y) * Math.Pow(s.Area(y) / s.Perimeter(y), 2.0 / 3) * Math.Sqrt(slope) - q, 1));

    public static double Momentum(Trap s, double q, double y, double g) => q * q / (g * s.Area(y)) + s.Moment(y);
    public static double Energy(Trap s, double q, double y, double g) => y + q * q / (2 * g * s.Area(y) * s.Area(y));

    /// <summary>Sequent (conjugate) depth: the subcritical depth with the same specific force as y1.</summary>
    public static double Sequent(Trap s, double q, double y1, double g)
    {
        double yc = Critical(s, q, g), m1 = Momentum(s, q, y1, g);
        return Bisect(y => Momentum(s, q, y, g) - m1, yc, Upper(y => Momentum(s, q, y, g) - m1, Math.Max(2 * yc, 1)));
    }

    /// <summary>Supercritical depth with specific energy E (NaN when E is below critical energy).</summary>
    public static double SupercriticalDepth(Trap s, double q, double e, double g)
    {
        double yc = Critical(s, q, g);
        if (Energy(s, q, yc, g) > e) return double.NaN;
        return Bisect(y => Energy(s, q, y, g) - e, 1e-9 * yc, yc);
    }

    public static string JumpType(double fr) => fr switch
    {
        < 1 => "no jump (subcritical)",
        < 1.7 => "undular",
        < 2.5 => "weak",
        < 4.5 => "oscillating",
        < 9 => "steady",
        _ => "strong",
    };

    /// <summary>Tailwater depth a basin needs, as a fraction of the sequent depth (SAF: Blaisdell; USBR: EM-25).</summary>
    public static double TailwaterFactor(string basin, double fr) => basin switch
    {
        "SAF" => fr <= 5.5 ? 1.10 - fr * fr / 120 : fr <= 11 ? 0.85 : 1.00 - fr * fr / 800,
        "Type II" => 1.05,
        "Type IV" => 1.10,
        _ => 1.0,
    };

    private sealed record JumpState(double Drop, double Y1, double V1, double Fr1, double Y2, double E1, double E2);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Hydraulic jump and stilling basins";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.81 : 32.174, k = si ? 1.0 : 1.486;
        string L = si ? "m" : "ft", V = si ? "m/s" : "ft/s", Qu = si ? "m³/s" : "cfs", qU = si ? "m²/s" : "cfs/ft";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        var tables = new List<ToolTable>();
        var figures = new List<ToolFigure>();
        var charts = new List<ToolChart>();
        var summary = new List<string>();

        double? Q = p.OptionalNumber("Q", "Q");
        double? width = p.OptionalNumber("basin width", "basin width");
        double z = p.Number("side slope", 0, "side slope");
        bool jumpPart = width is not null;
        bool outletPart = p.Text("outlet span") is not null;
        if (!jumpPart && !outletPart && p.Errors.Count == 0)
            msgs.Add(CalcMessage.Error("E-SB-001", "Give a basin width (hydraulic jump and USBR/SAF basins) or an outlet span (impact and CSU basins)."));
        if (jumpPart && Q is not > 0) msgs.Add(CalcMessage.Error("E-SB-002", "Give Q above zero."));
        if (jumpPart && width is not > 0) msgs.Add(CalcMessage.Error("E-SB-003", "The basin width must be above zero."));
        if (z < 0) msgs.Add(CalcMessage.Error("E-SB-003", "The side slope cannot be negative."));

        // Entering flow.
        double? y1In = p.OptionalNumber("y1"), v1In = p.OptionalNumber("V1"), eIn = p.OptionalNumber("energy above floor"),
            aS = p.OptionalNumber("approach slope"), aN = p.OptionalNumber("approach n");
        EntryKind entry = y1In is not null ? EntryKind.Depth : v1In is not null ? EntryKind.Velocity : eIn is not null ? EntryKind.Energy : EntryKind.Normal;
        if (jumpPart && entry == EntryKind.Normal && (aS is not > 0 || aN is not > 0))
            msgs.Add(CalcMessage.Error("E-SB-004", "Give the entering flow: y1, V1, energy above floor, or approach slope (%) and approach n."));
        if (y1In is <= 0 || v1In is <= 0 || eIn is <= 0) msgs.Add(CalcMessage.Error("E-SB-004", "y1, V1 and the energy above the floor must be above zero."));

        // Tailwater.
        double? twDepth = p.OptionalNumber("tailwater");
        double? twB = p.OptionalNumber("tw bottom width"), twZ = p.OptionalNumber("tw side slope"), twS = p.OptionalNumber("tw slope"), twN = p.OptionalNumber("tw n");
        var rating = new List<(double Q, double Tw)>();
        foreach (var r in p.Rows("Tailwater rating"))
            if (p.Cell(r, 0, "Q", "Tailwater rating") is { } rq && p.Cell(r, 1, "tailwater", "Tailwater rating") is { } rt) rating.Add((rq, rt));
        rating.Sort((a, b) => a.Q.CompareTo(b.Q));
        bool twChannel = twB is not null || twS is not null || twN is not null;
        if (twChannel && (twB is not >= 0 || twS is not > 0 || twN is not > 0 || (twZ ?? 0) < 0 || (twB == 0 && (twZ ?? 0) == 0)))
            msgs.Add(CalcMessage.Error("E-SB-005", "The tailwater channel needs tw bottom width (≥ 0), tw side slope (≥ 0, not both zero), tw slope (%) > 0 and tw n > 0."));
        if (rating.Count == 1) msgs.Add(CalcMessage.Error("E-SB-005", "A tailwater rating needs at least two rows (Q, depth)."));
        if (twDepth is < 0) msgs.Add(CalcMessage.Error("E-SB-005", "The tailwater depth cannot be negative."));

        string dropText = (p.Text("floor drop") ?? "auto").ToLowerInvariant();
        double? fixedDrop = dropText == "auto" ? null : p.OptionalNumber("floor drop", "floor drop");
        if (fixedDrop is < 0) msgs.Add(CalcMessage.Error("E-SB-006", "The floor drop cannot be negative (a raised floor is not modelled)."));

        var checkQs = new List<double>();
        foreach (var r in p.Rows("Check flows"))
            if (p.Cell(r, 0, "Q", "Check flows") is { } cq) { if (cq > 0) checkQs.Add(cq); else msgs.Add(CalcMessage.Error("E-SB-007", "Check flows must be above zero.")); }

        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double? TailwaterAt(double q)
        {
            if (rating.Count >= 2)
            {
                if (q < rating[0].Q || q > rating[^1].Q) msgs.Add(CalcMessage.Warning("W-SB-001", $"Q = {q:G4} {Qu} is outside the tailwater rating; the nearest end was extended linearly."));
                int i = 1;
                while (i < rating.Count - 1 && rating[i].Q < q) i++;
                var (qa, ta) = rating[i - 1]; var (qb, tb) = rating[i];
                return ta + (tb - ta) * (q - qa) / (qb - qa);
            }
            if (twChannel) return Normal(new Trap(twB!.Value, twZ ?? 0), q, twN!.Value, twS!.Value / 100, k);
            return twDepth;
        }

        if (jumpPart)
        {
            var sec = new Trap(width!.Value, z);
            bool rect = z == 0;
            double q = Q!.Value;
            // Entering flow at the channel-bed level (drop 0); its energy is carried down any floor depression.
            double y10;
            switch (entry)
            {
                case EntryKind.Depth: y10 = y1In!.Value; break;
                case EntryKind.Velocity: y10 = SolveDepthForArea(sec, q / v1In!.Value); break;
                case EntryKind.Energy:
                    y10 = SupercriticalDepth(sec, q, eIn!.Value, g);
                    if (double.IsNaN(y10)) { msgs.Add(CalcMessage.Error("E-SB-008", $"The energy above the floor, {eIn:G4} {L}, is below critical energy for Q = {q:G4} {Qu}; no supercritical flow enters.")); return new(title, [], [], msgs); }
                    break;
                default: y10 = Normal(sec, q, aN!.Value, aS!.Value / 100, k); break;
            }
            double yc = Critical(sec, q, g);
            if (y10 >= yc)
            {
                msgs.Add(CalcMessage.Error("E-SB-009", $"The entering depth {y10:F3} {L} is not below critical depth {yc:F3} {L}: the flow is subcritical and no jump forms. A stilling basin is not needed; check the outlet with the outlet protection tool."));
                return new(title, [], [], msgs);
            }
            double e10 = Energy(sec, q, y10, g);

            JumpState State(double qq, double drop)
            {
                // Same flow: energy carried down the drop. Another flow: the same entry rule (normal depth on the chute, or
                // the same total head above the floor when y1, V1 or E were given for the design flow).
                double eBase = qq == q ? e10 : entry == EntryKind.Normal ? Energy(sec, qq, Normal(sec, qq, aN!.Value, aS!.Value / 100, k), g) : e10;
                double y1 = SupercriticalDepth(sec, qq, eBase + drop, g);
                if (double.IsNaN(y1)) return new(drop, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
                double a1 = sec.Area(y1), v1 = qq / a1, fr = v1 / Math.Sqrt(g * a1 / sec.Top(y1));
                double y2 = Sequent(sec, qq, y1, g);
                return new(drop, y1, v1, fr, y2, Energy(sec, qq, y1, g), Energy(sec, qq, y2, g));
            }

            var j0 = State(q, 0);
            double tw = TailwaterAt(q) ?? double.NaN;
            double lj = 6.9 * (j0.Y2 - j0.Y1);
            string position = double.IsNaN(tw) ? "tailwater not given"
                : tw < 0.98 * j0.Y2 ? "TW below y2: the jump is swept downstream (repelled)"
                : tw > 1.02 * j0.Y2 ? "TW above y2: the jump is pushed upstream (submerged)"
                : "TW ≈ y2: the jump forms at the toe";
            tables.Add(new("Hydraulic jump (no floor drop)", ["Item", "Value", "Unit"],
            [
                ["Q", q, Qu], ["Section", rect ? $"rectangular, b = {width:G4}" : $"trapezoidal, b = {width:G4}, z = {z:G3}", ""],
                ["Unit discharge q", rect ? R(q / width.Value, 3) : null, rect ? qU : ""],
                ["Critical depth yc", R(yc, 3), L], ["Entering depth y1", R(j0.Y1, 3), L], ["Entering velocity V1", R(j0.V1, 2), V],
                ["Froude number Fr1", R(j0.Fr1, 2), ""], ["Jump type", JumpType(j0.Fr1), ""],
                ["Sequent depth y2", R(j0.Y2, 3), L], ["y2/y1", R(j0.Y2 / j0.Y1, 2), ""],
                ["Energy E1", R(j0.E1, 3), L], ["Energy E2", R(j0.E2, 3), L], ["Energy loss ΔE", R(j0.E1 - j0.E2, 3), L],
                ["Efficiency E2/E1", R(j0.E2 / j0.E1, 3), ""], ["Jump length Lj = 6.9(y2 − y1)", R(lj, 2), L],
                ["Tailwater TW", double.IsNaN(tw) ? null : R(tw, 3), L], ["Jump position", position, ""],
            ],
            ["Sequent depth from equal specific force M = Q²/(gA) + A·ȳ (for a rectangle this is Bélanger's y2 = y1/2·(√(1 + 8Fr1²) − 1)).",
             "Fr1 = V1/√(g·A/T). Jump type by Fr1 (USBR EM-25; Chow 1959): 1–1.7 undular, 1.7–2.5 weak, 2.5–4.5 oscillating, 4.5–9 steady, above 9 strong.",
             "Length Lj = 6.9(y2 − y1) for a free jump on a horizontal rectangular floor (HEC-14 Ch. 6); for a trapezoid it is an estimate.",
             entry == EntryKind.Normal ? "y1 = normal depth on the approach chute." : entry == EntryKind.Energy ? "y1 = supercritical depth with the given energy above the floor (no loss on the chute)." : "y1 as given."]));
            if (!rect) msgs.Add(CalcMessage.Info("I-SB-001", "A trapezoidal section gets the jump analysis only; USBR and SAF basins are rectangular."));
            if (double.IsNaN(tw)) msgs.Add(CalcMessage.Warning("W-SB-002", "No tailwater given: the jump position and floor drops cannot be checked. Give tailwater, a [Tailwater rating], or a tailwater channel."));

            if (rect && !double.IsNaN(tw))
            {
                double qUnit = q / width.Value, vLim = si ? 18.3 : 60, qLim = si ? 18.6 : 200;
                var basins = new List<(string Name, string Range, Func<JumpState, bool> Ok, Func<JumpState, string> Why)>
                {
                    ("Type I", "free jump on a flat apron", st => st.Fr1 >= 1.7, st => st.Fr1 is >= 4.5 and <= 9 ? "steady jump" : "jump is not steady at this Fr; prefer a basin with blocks"),
                    ("Type II", "Fr1 ≥ 4.5", st => st.Fr1 >= 4.5, _ => "large structures, V1 above 60 ft/s allowed"),
                    ("Type III", $"Fr1 ≥ 4.5, V1 ≤ {vLim:G3} {V}, q ≤ {qLim:G3} {qU}", st => st.Fr1 >= 4.5 && st.V1 <= vLim && qUnit <= qLim, _ => "shortest USBR basin for small structures"),
                    ("Type IV", "2.5 ≤ Fr1 ≤ 4.5", st => st.Fr1 is >= 2.5 and <= 4.5, _ => "oscillating jump; wave suppression"),
                    ("SAF", "1.7 ≤ Fr1 ≤ 17", st => st.Fr1 is >= 1.7 and <= 17, _ => "culvert and small-channel outlets (HEC-14)"),
                };
                var rows = new List<IReadOnlyList<object?>>();
                var detail = new List<IReadOnlyList<object?>>();
                (string Name, double Len, JumpState S, double Drop)? best = null;
                foreach (var b in basins)
                {
                    var (drop, s) = FloorDrop(b.Name, fixedDrop, tw, d => State(q, d));
                    if (double.IsNaN(s.Y2)) continue;
                    double f = TailwaterFactor(b.Name, s.Fr1), req = f * s.Y2;
                    double len = BasinLength(b.Name, s);
                    double margin = tw + drop - req;
                    bool ok = b.Ok(s);
                    rows.Add([b.Name, b.Range, ok ? "yes" : "no", R(s.Y1, 3), R(s.Fr1, 2), R(s.Y2, 3), R(f, 3), R(req, 3), R(drop, 2), R(tw + drop, 3), R(margin, 3), R(len, 2), ok ? b.Why(s) : "outside its range at the floor"]);
                    if (ok)
                    {
                        detail.AddRange(Appurtenances(b.Name, s, len, L));
                        if (margin >= -1e-6 && (best is null || len < best.Value.Len)) best = (b.Name, len, s, drop);
                    }
                }
                tables.Add(new("Stilling basins (USBR EM-25, HEC-14)", ["Basin", "Range", "Applies at the floor", $"y1 ({L})", "Fr1", $"y2 ({L})", "TW/y2 needed", $"TW needed ({L})", $"Floor drop ({L})", $"TW + drop ({L})", $"Margin ({L})", $"Length ({L})", "Notes"], rows,
                    ["The floor drop lowers the basin floor below the channel bed. The entering energy is carried down the drop, so y1 falls and y2 rises; the drop is solved so that TW + drop = (TW/y2 needed)·y2.",
                     "TW needed: Type I and III 1.0·y2, Type II 1.05·y2, Type IV 1.1·y2 (EM-25); SAF y2' = (1.10 − Fr²/120)·y2 for Fr 1.7–5.5, 0.85·y2 for 5.5–11, (1.00 − Fr²/800)·y2 for 11–17 (Blaisdell; HEC-14 Ch. 8).",
                     "Lengths: Type I = Lj = 6.9(y2 − y1); Type II ≈ 4.3·y2; Type III ≈ 2.7·y2; Type IV = Lj; SAF LB = 4.5·y2/Fr1^0.76. The USBR lengths are read from the EM-25 curves at mid-range; check the curve for the final Fr1.",
                     fixedDrop is null ? "floor drop = auto: the least drop for each basin." : $"floor drop = {fixedDrop:G4} {L} for every basin."]));
                if (detail.Count > 0)
                    tables.Add(new("Blocks and sills", ["Basin", "Element", $"Height ({L})", $"Width ({L})", $"Spacing ({L})", $"Position ({L})", "Notes"], detail,
                        ["Type II: chute blocks h = w = s = y1; dentated end sill 0.2·y2 high, teeth and gaps 0.15·y2, 2:1 upstream face (EM-25 Fig. 12).",
                         "Type III: chute blocks h = w = s = y1; baffle piers h3 = y1(0.168·Fr1 + 0.63), width and spacing 0.75·h3, 0.8·y2 downstream of the chute blocks; solid end sill h4 = y1(0.0556·Fr1 + 1.0). These are linear fits of the EM-25 Fig. 11 curves (D-322).",
                         "Type IV: chute blocks 2·y1 high, width ≤ y1, spacing 2.5·width; solid end sill (EM-25 Fig. 15).",
                         "SAF: chute blocks h = y1, width and spacing 0.75·y1; floor blocks h = y1, width and spacing 0.75·y1, LB/3 from the chute blocks, covering 40–55 % of the width; end sill 0.07·y2; sidewalls y2/3 above the maximum tailwater (HEC-14 Ch. 8)."]));
                if (best is { } bb)
                {
                    summary.Add($"Fr1 = {j0.Fr1:F2} ({JumpType(j0.Fr1)} jump), y2 = {j0.Y2:F2} {L}, TW = {tw:F2} {L}. Shortest basin that holds the jump: {bb.Name}, {bb.Len:F1} {L} long with the floor {bb.Drop:F2} {L} below the channel bed.");
                    figures.Add(BasinFigure(bb.Name, bb.S, bb.Drop, bb.Len, tw, L));
                }
                else
                {
                    summary.Add($"Fr1 = {j0.Fr1:F2}, y2 = {j0.Y2:F2} {L}, TW = {tw:F2} {L}: no listed basin holds the jump with this floor drop.");
                    msgs.Add(CalcMessage.Warning("W-SB-003", "No applicable basin holds the jump at the design flow; lower the floor, widen the basin or raise the tailwater."));
                }
                if (j0.Fr1 < 1.7) msgs.Add(CalcMessage.Info("I-SB-002", "Fr1 < 1.7: the jump is undular and a stilling basin is not needed; a riprap apron (outlet protection tool) usually suffices."));
                if (j0.Fr1 > 17) msgs.Add(CalcMessage.Warning("W-SB-004", "Fr1 > 17 is beyond the SAF range; use a USBR Type II basin and check it against EM-25."));
                if (j0.V1 > (si ? 15.2 : 50) && best?.Name == "SAF") msgs.Add(CalcMessage.Info("I-SB-003", "V1 above 50 ft/s: check the SAF blocks for cavitation; USBR Type II has no floor blocks."));

                // Flow range check against the tailwater curve, with the chosen basin and its floor.
                if (checkQs.Count > 0 && best is { } chosen)
                {
                    var crow = new List<IReadOnlyList<object?>>();
                    var y2Curve = new List<(double, double)>();
                    var twCurve = new List<(double, double)>();
                    foreach (double cq in checkQs.Append(q).Distinct().OrderBy(x => x))
                    {
                        var s = State(cq, chosen.Drop);
                        double ctw = TailwaterAt(cq) ?? double.NaN;
                        if (double.IsNaN(s.Y1)) { crow.Add([cq, null, null, null, null, R(ctw, 3), null, "no supercritical entry"]); continue; }
                        double need = TailwaterFactor(chosen.Name, s.Fr1) * s.Y2, have = ctw + chosen.Drop;
                        crow.Add([cq, R(s.Y1, 3), R(s.Fr1, 2), R(s.Y2, 3), R(need, 3), R(ctw, 3), R(have, 3), have >= need - 1e-6 ? "holds" : "swept out"]);
                        y2Curve.Add((cq, need)); twCurve.Add((cq, have));
                        if (have < need - 1e-6) msgs.Add(CalcMessage.Warning("W-SB-005", $"Q = {cq:G4} {Qu}: TW + drop = {have:F2} {L} is below the {need:F2} {L} the {chosen.Name} basin needs; the jump is swept out of the basin."));
                    }
                    tables.Add(new($"Flow range check ({chosen.Name}, floor drop {chosen.Drop:F2} {L})", [$"Q ({Qu})", $"y1 ({L})", "Fr1", $"y2 ({L})", $"TW needed ({L})", $"TW ({L})", $"TW + drop ({L})", "Jump"], crow,
                        [entry == EntryKind.Normal ? "Each flow enters at normal depth on the approach chute." : "Each flow enters with the same total head above the floor as the design flow."]));
                    charts.Add(new("Tailwater and required depth over the flow range", $"Q ({Qu})", $"Depth above the basin floor ({L})",
                        [new("Tailwater + floor drop", twCurve), new($"Depth needed ({chosen.Name})", y2Curve, Dashed: true)]));
                }
            }
        }

        if (outletPart) OutletBasins(p, si, g, k, L, V, Qu, msgs, tables, summary, TailwaterAt);

        msgs.InsertRange(0, p.Errors.Where(e => !msgs.Contains(e)));
        if (summary.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) summary.Add("See the tables below.");
        return new ToolResult(title, summary, tables, msgs) { Figures = figures, Charts = charts };
    }

    private static double SolveDepthForArea(Trap s, double area) => Bisect(y => s.Area(y) - area, 0, Upper(y => s.Area(y) - area, 1));

    private static (double Drop, JumpState S) FloorDrop(string basin, double? fixedDrop, double tw, Func<double, JumpState> state)
    {
        if (fixedDrop is { } fd) return (fd, state(fd));
        double Gap(double d) { var s = state(d); return tw + d - TailwaterFactor(basin, s.Fr1) * s.Y2; }
        if (Gap(0) >= 0) return (0, state(0));
        double hi = 1;
        for (int i = 0; i < 60 && Gap(hi) < 0; i++) hi *= 2;
        double d = Bisect(Gap, 0, hi);
        return (d, state(d));
    }

    private static double BasinLength(string basin, JumpState s) => basin switch
    {
        "Type II" => 4.3 * s.Y2,
        "Type III" => 2.7 * s.Y2,
        "SAF" => 4.5 * s.Y2 / Math.Pow(s.Fr1, 0.76),
        _ => 6.9 * (s.Y2 - s.Y1),
    };

    private static IEnumerable<IReadOnlyList<object?>> Appurtenances(string basin, JumpState s, double len, string u)
    {
        double y1 = s.Y1, y2 = s.Y2, fr = s.Fr1;
        switch (basin)
        {
            case "Type II":
                yield return new object?[] { basin, "Chute blocks", R(y1, 2), R(y1, 2), R(y1, 2), "at the toe", "half-spaces y1/2 at the walls" };
                yield return new object?[] { basin, "Dentated end sill", R(0.2 * y2, 2), R(0.15 * y2, 2), R(0.15 * y2, 2), $"end of basin ({len:F1} {u})", "2:1 upstream face" };
                break;
            case "Type III":
                double h3 = y1 * (0.168 * fr + 0.63), h4 = y1 * (0.0556 * fr + 1.0);
                yield return new object?[] { basin, "Chute blocks", R(y1, 2), R(y1, 2), R(y1, 2), "at the toe", "" };
                yield return new object?[] { basin, "Baffle piers", R(h3, 2), R(0.75 * h3, 2), R(0.75 * h3, 2), R(0.8 * y2, 2), "staggered with the chute blocks" };
                yield return new object?[] { basin, "End sill", R(h4, 2), null, null, $"end of basin ({len:F1} {u})", "solid" };
                break;
            case "Type IV":
                yield return new object?[] { basin, "Chute blocks", R(2 * y1, 2), R(y1, 2), R(2.5 * y1, 2), "at the toe", "deflector blocks; width ≤ y1" };
                yield return new object?[] { basin, "End sill", null, null, null, $"end of basin ({len:F1} {u})", "solid; see EM-25 Fig. 15" };
                break;
            case "SAF":
                yield return new object?[] { basin, "Chute blocks", R(y1, 2), R(0.75 * y1, 2), R(0.75 * y1, 2), "at the toe", "" };
                yield return new object?[] { basin, "Floor blocks", R(y1, 2), R(0.75 * y1, 2), R(0.75 * y1, 2), R(len / 3, 2), "staggered; 40–55 % of the width" };
                yield return new object?[] { basin, "End sill", R(0.07 * y2, 2), null, null, $"end of basin ({len:F1} {u})", "" };
                yield return new object?[] { basin, "Sidewall freeboard", R(y2 / 3, 2), null, null, "above max tailwater", "" };
                break;
        }
    }

    private static ToolFigure BasinFigure(string name, JumpState s, double drop, double len, double tw, string u)
    {
        double up = Math.Max(2, 0.3 * len);
        var ground = new List<(double, double)> { (-up, drop > 0 ? 0 : 0) };
        if (drop > 0) ground.Add((-Math.Min(up * 0.8, 2 * drop), 0));
        ground.AddRange([(0, -drop), (len, -drop), (len, 0), (len + up, 0)]);
        double lj = Math.Min(len, 6.9 * (s.Y2 - s.Y1));
        var water = new List<(double, double)> { (0, -drop + s.Y1), (0.05 * lj, -drop + s.Y1), (lj, -drop + s.Y2), (len + up, -drop + s.Y2) };
        return new ToolFigure($"{name} basin: longitudinal section", [
            new("Floor and channel bed", ToolSeriesKind.Ground, ground),
            new("Jump (schematic)", ToolSeriesKind.Water, water),
            new("Tailwater", ToolSeriesKind.Limit, [(len, tw), (len + up, tw)]),
        ], $"Distance from the toe ({u})", $"Elevation above the channel bed ({u})", ["Schematic: the jump is drawn as a straight rise over its length; blocks and sills are in the table."]);
    }

    private static void OutletBasins(ToolInput p, bool si, double g, double k, string L, string V, string Qu, List<CalcMessage> msgs, List<ToolTable> tables, List<string> summary, Func<double, double?> tailwaterAt)
    {
        string shape = (p.Text("outlet shape") ?? "circular").ToLowerInvariant();
        bool box = shape.StartsWith("box") || shape.StartsWith("rect");
        double span = p.Number("outlet span", double.NaN), rise = box ? p.Number("outlet rise", span) : span;
        double? yoIn = p.OptionalNumber("outlet depth"), sl = p.OptionalNumber("outlet slope"), n = p.OptionalNumber("outlet n");
        if (!(span > 0) || !(rise > 0)) { msgs.Add(CalcMessage.Error("E-SB-010", "The outlet span (and rise for a box) must be above zero.")); return; }
        if (!box && !shape.StartsWith("circ")) { msgs.Add(CalcMessage.Error("E-SB-010", $"outlet shape \"{shape}\" is not circular or box.")); return; }
        double? qImpact = p.OptionalNumber("impact Q") ?? (p.Text("csu Q") is null ? p.OptionalNumber("Q") : null);
        double? qCsu = p.OptionalNumber("csu Q") ?? (p.Text("impact Q") is null && p.Text("exit velocity") is not null ? p.OptionalNumber("Q") : null);
        double toFt = si ? 1 / 0.3048 : 1, toCfs = si ? 35.3147 : 1;

        (double Y, double A, double Vel, string Basis)? Outlet(double q)
        {
            ChannelSection sec = box ? new RectangularSection(span) : new CircularSection(span);
            var geo = Hydraulics.Geometry.SectionGeometry.From(sec);
            double y; string basis;
            if (yoIn is > 0) { y = Math.Min(yoIn.Value, rise); basis = "given"; }
            else if (sl is > 0 && n is > 0)
            {
                var r = UniformFlowCalculator.SolveNormalDepth(geo, q, n.Value, sl.Value / 100, k);
                if (!r.Converged || r.Root >= rise) { y = rise; basis = "full (Q above open-channel capacity)"; }
                else { y = r.Root; basis = "normal depth"; }
                var c = UniformFlowCalculator.SolveCriticalDepth(geo, q, g);
                if (c.Converged && y > c.Root && basis == "normal depth") { y = c.Root; basis = "critical (mild barrel)"; }
            }
            else { msgs.Add(CalcMessage.Error("E-SB-011", "Give the outlet depth, or the outlet slope (%) and outlet n.")); return null; }
            double a = y >= rise ? geo.Area(rise * (1 - 1e-9)) : geo.Area(y);
            return (y, a, q / a, basis);
        }

        if (qImpact is > 0)
        {
            double q = qImpact.Value;
            int count = (int)Math.Max(1, p.Number("impact basins", 1));
            double qb = q / count;
            if (Outlet(qb) is { } o)
            {
                double dEq = Math.Sqrt(o.A), fr = o.Vel / Math.Sqrt(g * dEq);
                double wFr = 2.88 * Math.Pow(fr, 0.566) * dEq;            // ft or m
                double qcfs = qb * toCfs;
                double wTabIn = ImpactWidthFromTable(qcfs);                   // inches
                double wTab = wTabIn / 12 / toFt;
                double w = Math.Max(wFr, wTab);
                List<IReadOnlyList<object?>> rows =
                [
                    ["Q per basin", R(qb, 3), Qu], ["Outlet depth (" + o.Basis + ")", R(o.Y, 3), L], ["Flow area A", R(o.A, 3), si ? "m²" : "ft²"],
                    ["Entering velocity V", R(o.Vel, 2), V], ["D = √A", R(dEq, 3), L], ["Fr = V/√(gD)", R(fr, 2), ""],
                    ["W from Fr: 2.88·Fr^0.566·D", R(wFr, 2), L], ["W from Q (Table 4-15)", R(wTab, 2), L], ["Design W (larger)", R(w, 2), L],
                    ["H = 3/4 W", R(0.75 * w, 2), L], ["L = 4/3 W", R(4.0 / 3 * w, 2), L], ["a = 1/2 W", R(0.5 * w, 2), L], ["b = 3/8 W", R(0.375 * w, 2), L],
                    ["c = 1/2 W", R(0.5 * w, 2), L], ["d = 1/6 W", R(w / 6, 2), L], ["e = 1/12 W", R(w / 12, 2), L], ["t = 1/12 W (suggested minimum)", R(w / 12, 2), L],
                    ["Riprap stone diameter = 1/20 W", R(w / 20, 2), L],
                ];
                tables.Add(new("USBR Type VI impact basin (IDCM §4.9.2(1), Figure 4-8)", ["Item", "Value", "Unit"], rows,
                    ["Proportions from IDCM Figure 4-8. Design width: the larger of the Figure 4-8 'design width of basin' line (W/D = 2.88·Fr^0.566, the HEC-14 Eq. 9-1 fit of the same curve) and Table 4-15 for the discharge (log–log between rows).",
                     "D is the square root of the flow area entering the basin; Fr = V/√(gD) (Figure 4-8 notes)."]));
                double[]? row = ImpactTable.FirstOrDefault(r => r[1] >= qcfs - 1e-9 && r[2] / 12 / toFt >= w - 1e-9);
                if (row is not null)
                    tables.Add(new("IDCM Table 4-15 row to build", ["Pipe (in.)", "Max Q (cfs)", "W", "H", "L", "a", "b", "c", "d", "e", "f", "g", "tw (in.)", "tf (in.)", "tb (in.)", "tp (in.)", "K (in.)", "Riprap (in.)"],
                        [[row[0], row[1], FtIn(row[2]), FtIn(row[3]), FtIn(row[4]), FtIn(row[5]), FtIn(row[6]), FtIn(row[7]), FtIn(row[8]), FtIn(row[9]), FtIn(row[10]), FtIn(row[11]), row[12], row[13], row[14], row[15], row[16], row[17]]],
                        ["The smallest row whose maximum Q and width W both cover the design (the table's dimensions are in feet-inches; wall and slab thicknesses in inches).",
                         "Table note 1: the suggested pipe runs full at 12 ft/s or half full at 24 ft/s; the pipe size may differ, but the Q–dimension relation must be kept."]));
                else msgs.Add(CalcMessage.Info("I-SB-004", "The design width is beyond the largest Table 4-15 row; build to the proportional dimensions."));
                if (qcfs > 400) msgs.Add(CalcMessage.Warning("W-SB-006", $"{qcfs:F0} cfs per basin is above the 400 cfs the Type VI basin has been used for (IDCM §4.9.2(1)); set impact basins = 2 or more to place basins side by side."));
                if (qcfs < 21) msgs.Add(CalcMessage.Info("I-SB-005", "Below 21 cfs the table width is extended log–log from its first two rows; IDCM note 2 says to read W from the Figure 4-8 discharge-limits curve. Check it there."));
                if (qcfs > 339 && qcfs <= 400) msgs.Add(CalcMessage.Info("I-SB-006", "Above 339 cfs the table width is extended log–log from its last two rows; check it against the Figure 4-8 discharge-limits curve."));
                if (o.Vel * toFt > 50) msgs.Add(CalcMessage.Warning("W-SB-007", $"Entering velocity {o.Vel:F1} {V} is above 50 ft/s, the USBR limit for the impact basin."));
                if (fr < 1 || fr > 10) msgs.Add(CalcMessage.Info("I-SB-007", $"Fr = {fr:F2} is outside the 1–10 range of the Figure 4-8 design-width curve."));
                msgs.Add(CalcMessage.Info("I-SB-008", "Impact basin (IDCM §4.9.2(1)): design the baffle wall for the impact load and vibration; riprap the bottom and sides downstream of the end sill; 45° wingwalls help where the tailwater is shallow."));
                summary.Add($"USBR Type VI impact basin: W = {w:F2} {L}, L = {4.0 / 3 * w:F2} {L}, H = {0.75 * w:F2} {L}{(count > 1 ? $" (each of {count} basins)" : "")}.");
            }
        }

        if (qCsu is > 0)
        {
            double q = qCsu.Value;
            double? vb = p.OptionalNumber("exit velocity");
            if (vb is not > 0) { msgs.Add(CalcMessage.Error("E-SB-012", "The CSU basin needs exit velocity (the velocity leaving the basin) above zero.")); return; }
            double slopePct = p.Number("basin slope", 0);
            if (slopePct >= 10) msgs.Add(CalcMessage.Warning("W-SB-008", "IDCM §4.9.6: the CSU basin equation is for basin slopes under 10 %."));
            string ratioText = (p.Text("expansion ratio") ?? "auto").ToLowerInvariant();
            double? lc = p.OptionalNumber("anchor lever");
            if (Outlet(q) is not { } o) return;
            double wo = span, yo = o.Y, vo = o.Vel;
            double fr = vo / Math.Sqrt(g * yo);
            double gamma = si ? 9810 : 62.4, rho = gamma / g;
            double vaRatio = box ? 1.65 - 0.3 * fr : 1.65 - 0.45 * q / Math.Sqrt(g * Math.Pow(wo, 5));
            double va = vaRatio * vo;
            if (va <= 0) { msgs.Add(CalcMessage.Error("E-SB-013", $"VA/Vo = {vaRatio:F3} is not positive (Eq. {(box ? "4-9-4" : "4-9-5")}); the outlet is beyond the CSU tests.")); return; }
            var ratios = ratioText == "auto" ? new[] { "2-4", "5", "6", "7", "8" } : [ratioText is "2" or "3" or "4" or "2-4" ? "2-4" : ratioText];
            if (ratios.Any(r => r is not ("2-4" or "5" or "6" or "7" or "8"))) { msgs.Add(CalcMessage.Error("E-SB-014", "expansion ratio must be 2-4, 5, 6, 7, 8 or auto (Figure 4-17).")); return; }
            var rows = new List<IReadOnlyList<object?>>();
            (double Len, IReadOnlyList<object?> Row)? best = null;
            for (int ci = 0; ci < CsuConfigs.Length; ci++)
            {
                var cfg = CsuConfigs[ci];
                if (!ratios.Contains(cfg.Ratio)) continue;
                double wbRatio = cfg.Ratio == "2-4" ? (ratioText is "2" or "3" ? double.Parse(ratioText, CultureInfo.InvariantCulture) : 4) : double.Parse(cfg.Ratio, CultureInfo.InvariantCulture);
                double wb = wbRatio * wo;
                double ya = wbRatio <= 4 ? q / (va * wb) : yo * (box ? 0.19 + (fr - 1) * 0.145 : 0.21 + (fr - 1) * 0.19);
                double required = (rho * vo * q + 0.7 * gamma * yo * yo * wo / 2 - rho * vb.Value * q - gamma * q * q / (2 * vb.Value * vb.Value * wb)) / (rho * va * va / 2);
                foreach (var cb in CsuCb.Where(c => c.Circular == !box))
                {
                    double cB = cb.Cb[ci];
                    if (double.IsNaN(cB)) continue;
                    double h = cb.HYa * ya, spacing = cb.Lh * h, w1 = cfg.W1 * wo, af = w1 * h;
                    double provided = cB * af * cfg.N, len = 2 * wo + spacing * cfg.Nr;
                    bool w1hOk = w1 / h is >= 2 and <= 8;
                    bool ok = provided >= required && w1hOk;
                    double? fa = lc is > 0 ? rho / 2 * (h / lc.Value) * af * va * va : null;
                    var row = (IReadOnlyList<object?>)new object?[] { $"{(cfg.Ratio == "2-4" ? wbRatio.ToString("G3", CultureInfo.InvariantCulture) : cfg.Ratio)}", cfg.Nr, cfg.N, cb.HYa, cb.Lh, R(ya, 3), R(h, 3), R(spacing, 2), R(w1, 2), R(w1 / h, 2), R(wb, 2), cB, R(provided, 2), R(required, 2), R(len, 2), fa is null ? null : R(fa.Value, 0), ok ? "OK" : !w1hOk ? "W1/h outside 2–8" : "too little drag" };
                    rows.Add(row);
                    if (ok && (best is null || len < best.Value.Len)) best = (len, row);
                }
            }
            tables.Add(new("CSU roughness-element basin (IDCM §4.9.6, Figure 4-17)", ["WB/Wo", "Rows Nr", "Elements N", "h/YA", "L/h", $"YA ({L})", $"h ({L})", $"Row spacing L ({L})", $"W1 ({L})", "W1/h", $"WB ({L})", "CB", "CB·AF·N provided", "CB·AF·N required", $"Length Li ({L})", si ? "Anchor force FA (N)" : "Anchor force FA (lb)", "Result"], rows,
                [$"Outlet: depth Yo = {yo:F3} {L} ({o.Basis}), Vo = {vo:F2} {V}, Fr = Vo/√(g·Yo) = {fr:F2}; VA = {va:F2} {V} (Eq. {(box ? "4-9-4" : "4-9-5")}).",
                 "YA: Eq. 4-9-6 (rectangular) or 4-9-7 (round) for WB/Wo of 5–8; for 2–4, YA = Q/(VA·WB) at the full basin width, as §4.9.6 words it (D-324). That gives a thinner YA, smaller elements and less drag, so it errs safe.",
                 "Required CB·AF·N from Eq. 4-9-3: [ρVoQ + Cp·γ·Yo²·Wo/2 − ρVBQ − γQ²/(2VB²WB)] / (ρVA²/2), Cp = 0.7 (D-323). Provided = CB (Figure 4-17) × W1·h × N; it must be at least the required value.",
                 "Li = 2Wo + L·Nr. W1/h must be 2–8; at least half the rows need an element near the wall; alternate rows are staggered. Flare Ue = 4/7 + (10/7)(L/Wo) (Eq. 4-9-2, as printed). Riprap at least 10 ft downstream.",
                 "Figure 4-17's circular column for WB/Wo 2–4 with 6 rows runs against every other column and is not used (D-321)."]));
            if (best is { } bst)
                summary.Add($"CSU basin: shortest passing layout WB/Wo = {bst.Row[0]}, {bst.Row[1]} rows of {bst.Row[2]} elements, h = {bst.Row[6]} {L}, Li = {bst.Len:F1} {L}.");
            else
            {
                summary.Add("CSU basin: no Figure 4-17 layout gives enough drag; lower the exit velocity target or use another dissipator.");
                msgs.Add(CalcMessage.Warning("W-SB-009", "No Figure 4-17 CSU layout provides the CB·AF·N the momentum equation needs."));
            }
            _ = tailwaterAt;
        }
    }

    /// <summary>Table 4-15 basin width (in.) for a discharge (cfs), log–log between rows, extended from the end rows.</summary>
    public static double ImpactWidthFromTable(double qcfs)
    {
        var t = ImpactTable;
        int i = 1;
        while (i < t.Length - 1 && t[i][1] < qcfs) i++;
        double q0 = t[i - 1][1], q1 = t[i][1], w0 = t[i - 1][2], w1 = t[i][2];
        return w0 * Math.Pow(qcfs / q0, Math.Log(w1 / w0) / Math.Log(q1 / q0));
    }

    private static string FtIn(double inches) => $"{(int)(inches / 12)}-{inches % 12:0}";
    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
