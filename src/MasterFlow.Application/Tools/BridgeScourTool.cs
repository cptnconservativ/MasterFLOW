using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Bridge scour by FHWA HEC-18 (5th ed., 2012): live-bed or clear-water contraction scour (modified Laursen equations),
/// local pier scour by the CSU equation with shape, skew, bed-form and wide-pier factors, and abutment scour by the
/// Froehlich and HIRE equations; total scour and the scoured bed elevation at each element.
/// </summary>
public sealed class BridgeScourTool : IDesignTool
{
    public string Id => "bridge-scour";
    public string Name => "Bridge scour (HEC-18): contraction, pier and abutment";
    public string Description => "FHWA HEC-18 (5th ed., 2012) bridge scour: critical velocity to decide live-bed or clear-water, contraction scour by the modified Laursen equations (Eqs. 6.1–6.4), local pier scour by the CSU equation with K1 shape, K2 skew, K3 bed condition and the wide-pier factor (Eqs. 7.1–7.4), abutment scour by Froehlich and HIRE (Eqs. 8.1–8.2), total scour and scoured bed elevations. Hydraulics from the natural channel profile tool or a HEC-RAS run.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Bridge scour, SI (m, m³/s, mm). Hydraulics for the design flood from a profile run.
        D50 = 2.0                # mm, bed material
        y1 = 2.4                 # approach main-channel depth
        V1 = 1.6                 # approach main-channel velocity
        Q1 = 140                 # approach main-channel flow
        W1 = 36                  # approach main-channel width
        Q2 = 200                 # flow through the bridge main channel
        W2 = 30                  # bridge main-channel width less pier widths
        y0 = 2.6                 # existing depth in the bridge section
        S1 = 0.002               # energy slope at the approach
        bed elevation = 1000.0   # bridge-section bed, for the scoured elevations
        [Piers]
        Name	Shape	Width	Length	Skew (deg)	Depth y1	Velocity V1	Bed
        Pier 1	round	1.2	12	10	2.8	2.2	plane
        [Abutments]
        Name	Type	Length L'	Depth ya	Velocity Ve	Skew (deg)
        Left	spill-through	40	1.0	0.8	90
        """ : """
        # Bridge scour, FHWA HEC-18 (2012). U.S. units (ft, cfs, mm for D50). Hydraulics for the design flood (and the
        # check flood, by a second run) from the natural channel profile tool or HEC-RAS.
        D50 = 2.0                # mm, bed material
        y1 = 8.0                 # approach main-channel depth, ft
        V1 = 5.2                 # approach main-channel velocity, ft/s
        Q1 = 5000                # approach main-channel flow, cfs
        W1 = 120                 # approach main-channel width, ft
        Q2 = 7000                # flow through the bridge main channel, cfs (incl. overbank flow forced in)
        W2 = 100                 # bridge main-channel width less pier widths, ft
        y0 = 8.5                 # existing depth in the bridge section, ft
        S1 = 0.002               # energy slope at the approach (live-bed k1)
        bed elevation = 3400.0   # bridge-section bed elevation, ft, for the scoured elevations

        # Shape: square, round, circular, group, sharp. Bed: clear-water, plane, small dunes, medium dunes, large dunes.
        [Piers]
        Name	Shape	Width (ft)	Length (ft)	Skew (deg)	Depth y1 (ft)	Velocity V1 (ft/s)	Bed
        Pier 1	round	4	40	0	9.0	7.0	plane
        Pier 2	round	4	40	10	7.5	6.0	plane

        # Type: vertical wall, vertical wall with wing walls, spill-through. L' = length of active flow blocked, ya = average
        # overbank depth, Ve = Qe/Ae of the blocked flow, skew = angle of the embankment to the flow (90 = perpendicular).
        [Abutments]
        Name	Type	Length L' (ft)	Depth ya (ft)	Velocity Ve (ft/s)	Skew (deg)
        Left	spill-through	150	3.0	2.5	90
        Right	spill-through	80	2.5	2.0	90
        """;

    /// <summary>Fall velocity of a quartz sphere (Rubey 1933) at 20 °C, ft/s or m/s.</summary>
    public static double FallVelocity(double d, bool si)
    {
        double g = si ? 9.81 : 32.2, nu = si ? 1.0e-6 : 1.08e-5, s1 = 1.65;
        double a = 36 * nu * nu / (s1 * g * d * d * d);
        return (Math.Sqrt(2.0 / 3 + a) - Math.Sqrt(a)) * Math.Sqrt(s1 * g * d);
    }

    /// <summary>HEC-18 Eq. 6.1: Vc = Ku·y^(1/6)·D50^(1/3), Ku = 11.17 (U.S., ft) or 6.19 (SI, m).</summary>
    public static double CriticalVelocity(double y, double d50, bool si) => (si ? 6.19 : 11.17) * Math.Pow(y, 1.0 / 6) * Math.Pow(d50, 1.0 / 3);

    /// <summary>HEC-18 live-bed exponent k1 from V*/T.</summary>
    public static double K1(double vStarOverT) => vStarOverT < 0.5 ? 0.59 : vStarOverT <= 2.0 ? 0.64 : 0.69;

    /// <summary>CSU pier shape factor K1 (HEC-18 Table 7.1).</summary>
    public static double? ShapeFactor(string shape) => shape.ToLowerInvariant() switch
    {
        "square" or "square nose" => 1.1,
        "round" or "round nose" or "circular" or "cylinder" or "group" or "group of cylinders" => 1.0,
        "sharp" or "sharp nose" => 0.9,
        _ => null,
    };

    /// <summary>Bed condition factor K3 (HEC-18 Table 7.3); medium dunes use the upper value.</summary>
    public static double? BedFactor(string bed) => bed.ToLowerInvariant() switch
    {
        "clear-water" or "clear water" or "plane" or "plane bed" or "antidune" or "small dunes" => 1.1,
        "medium dunes" => 1.2,
        "large dunes" => 1.3,
        _ => null,
    };

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Bridge scour (HEC-18)";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.81 : 32.2;
        string L = si ? "m" : "ft", V = si ? "m/s" : "ft/s";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double d50mm = p.Number("D50", double.NaN, "D50");
        double? y1 = p.OptionalNumber("y1"), v1 = p.OptionalNumber("V1"), q1 = p.OptionalNumber("Q1"), w1 = p.OptionalNumber("W1"),
            q2 = p.OptionalNumber("Q2"), w2 = p.OptionalNumber("W2"), y0 = p.OptionalNumber("y0"), s1 = p.OptionalNumber("S1"), bed = p.OptionalNumber("bed elevation");
        if (!(d50mm > 0)) msgs.Add(CalcMessage.Error("E-BS-001", "Give D50 (mm) above zero."));
        bool contraction = q2 is not null || w2 is not null;
        if (y1 is not > 0 || v1 is not > 0) msgs.Add(CalcMessage.Error("E-BS-002", "Give the approach depth y1 and velocity V1, both above zero."));
        if (contraction && (q1 is not > 0 || w1 is not > 0 || q2 is not > 0 || w2 is not > 0 || y0 is not > 0))
            msgs.Add(CalcMessage.Error("E-BS-003", "Contraction scour needs Q1, W1, Q2, W2 and y0, all above zero."));
        var piers = new List<(string Name, double K1, double A, double Len, double Skew, double Y, double Vp, double K3, string Shape)>();
        foreach (var r in p.Rows("Piers"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Pier {piers.Count + 1}";
            string shape = r.Length > 1 ? r[1] : "round", bedf = r.Length > 7 ? r[7] : "plane";
            double? a = p.Cell(r, 2, "width", name), len = p.Cell(r, 3, "length", name), sk = p.Cell(r, 4, "skew", name), yy = p.Cell(r, 5, "depth", name), vv = p.Cell(r, 6, "velocity", name);
            if (ShapeFactor(shape) is not { } k1) { msgs.Add(CalcMessage.Error("E-BS-004", $"{name}: shape \"{shape}\" is not square, round, circular, group or sharp.")); continue; }
            if (BedFactor(bedf) is not { } k3) { msgs.Add(CalcMessage.Error("E-BS-005", $"{name}: bed \"{bedf}\" is not clear-water, plane, small dunes, medium dunes or large dunes.")); continue; }
            if (a is not > 0 || yy is not > 0 || vv is not > 0) { msgs.Add(CalcMessage.Error("E-BS-006", $"{name}: give the pier width, and the depth and velocity just upstream of it, all above zero.")); continue; }
            if (sk is < 0 or > 90) { msgs.Add(CalcMessage.Error("E-BS-007", $"{name}: skew must be 0–90°.")); continue; }
            piers.Add((name, k1, a.Value, len ?? a.Value, sk ?? 0, yy.Value, vv.Value, k3, shape));
        }
        var abuts = new List<(string Name, double K1, double Len, double Ya, double Ve, double Skew, string Type)>();
        foreach (var r in p.Rows("Abutments"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Abutment {abuts.Count + 1}";
            string type = (r.Length > 1 ? r[1] : "").ToLowerInvariant();
            double? k1 = type switch { "vertical wall" => 1.0, "vertical wall with wing walls" or "wing wall" or "wing walls" => 0.82, "spill-through" or "spill through" => 0.55, _ => null };
            double? len = p.Cell(r, 2, "length", name), ya = p.Cell(r, 3, "depth", name), ve = p.Cell(r, 4, "velocity", name), sk = p.Cell(r, 5, "skew", name);
            if (k1 is null) { msgs.Add(CalcMessage.Error("E-BS-008", $"{name}: type \"{type}\" is not vertical wall, vertical wall with wing walls or spill-through.")); continue; }
            if (len is not > 0 || ya is not > 0 || ve is not > 0) { msgs.Add(CalcMessage.Error("E-BS-009", $"{name}: give L', ya and Ve, all above zero.")); continue; }
            if (sk is <= 0 or >= 180) { msgs.Add(CalcMessage.Error("E-BS-010", $"{name}: skew must be between 0 and 180° (90 = perpendicular).")); continue; }
            abuts.Add((name, k1.Value, len.Value, ya.Value, ve.Value, sk ?? 90, type));
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double d50 = d50mm / 1000 / (si ? 1 : 0.3048);   // ft or m
        double vc = CriticalVelocity(y1!.Value, d50, si);
        bool liveBed = v1!.Value > vc;
        var tables = new List<ToolTable>();
        var summary = new List<string>();
        double ysC = 0;
        List<IReadOnlyList<object?>> cond =
        [
            ["D50", d50mm, "mm"], ["Critical velocity Vc = Ku·y1^(1/6)·D50^(1/3)", R(vc, 2), V], ["Approach velocity V1", v1, V],
            ["Condition", liveBed ? "live-bed (V1 > Vc)" : "clear-water (V1 ≤ Vc)", ""],
        ];
        if (contraction)
        {
            double y2;
            if (liveBed)
            {
                double vstar = Math.Sqrt(g * y1.Value * (s1 ?? double.NaN)), t = FallVelocity(d50, si);
                double ratio = vstar / t, k1 = double.IsNaN(ratio) ? 0.64 : K1(ratio);
                if (s1 is null) msgs.Add(CalcMessage.Warning("W-BS-001", "No energy slope S1: k1 = 0.64 (mixed load) is assumed for live-bed contraction scour."));
                y2 = y1.Value * Math.Pow(q2!.Value / q1!.Value, 6.0 / 7) * Math.Pow(w1!.Value / w2!.Value, k1);
                cond.AddRange([["Shear velocity V* = √(g·y1·S1)", R(vstar, 3), V], ["Fall velocity T of D50 (Rubey, 20 °C)", R(t, 3), V], ["V*/T", R(ratio, 2), ""], ["k1", k1, ""],
                    ["y2 = y1·(Q2/Q1)^(6/7)·(W1/W2)^k1", R(y2, 2), L]]);
            }
            else
            {
                double ku = si ? 0.025 : 0.0077, dm = 1.25 * d50;
                y2 = Math.Pow(ku * q2!.Value * q2.Value / (Math.Pow(dm, 2.0 / 3) * w2!.Value * w2.Value), 3.0 / 7);
                cond.AddRange([["Dm = 1.25·D50", R(dm * (si ? 1000 : 304.8), 2), "mm"], ["y2 = [Ku·Q2²/(Dm^(2/3)·W2²)]^(3/7)", R(y2, 2), L]]);
                if (d50mm < 0.2) msgs.Add(CalcMessage.Warning("W-BS-002", "D50 below 0.2 mm: clear-water scour in fine material is unlikely; HEC-18 treats fine beds as live-bed or cohesive."));
            }
            ysC = Math.Max(0, y2 - y0!.Value);
            cond.AddRange([["Existing depth y0", y0, L], ["Contraction scour ys = y2 − y0", R(ysC, 2), L]]);
            if (y2 < y0.Value) msgs.Add(CalcMessage.Info("I-BS-001", "y2 is less than the existing depth: no contraction scour (ys = 0)."));
            summary.Add($"Contraction scour ({(liveBed ? "live-bed" : "clear-water")}): {ysC:F2} {L}.");
        }
        tables.Add(new("Contraction scour (HEC-18 Ch. 6)", ["Item", "Value", "Unit"], cond,
            [$"Ku = {(si ? "6.19" : "11.17")} in Eq. 6.1 ({(si ? "m" : "ft")}); clear-water Ku = {(si ? "0.025" : "0.0077")} in Eq. 6.4. k1 = 0.59 (V*/T < 0.5, contact load), 0.64 (0.5–2.0), 0.69 (> 2.0, suspended load)."]));

        var pierRows = new List<IReadOnlyList<object?>>();
        foreach (var pr in piers)
        {
            double fr = pr.Vp / Math.Sqrt(g * pr.Y);
            double la = Math.Min(pr.Len / pr.A, 12);
            double k1 = pr.Skew > 5 ? 1.0 : pr.K1;
            double th = pr.Skew * Math.PI / 180;
            double k2 = Math.Pow(Math.Cos(th) + la * Math.Sin(th), 0.65);
            double k3 = liveBed ? pr.K3 : 1.1;
            // Wide pier factor (Eq. 7.4): y/a < 0.8, a/D50 > 50 and Fr < 1.
            double kw = 1;
            double vcp = CriticalVelocity(pr.Y, d50, si);
            if (pr.Y / pr.A < 0.8 && pr.A / d50 > 50 && fr < 1)
                kw = pr.Vp / vcp < 1 ? 2.58 * Math.Pow(pr.Y / pr.A, 0.34) * Math.Pow(fr, 0.65) : Math.Pow(pr.Y / pr.A, 0.13) * Math.Pow(fr, 0.25);
            double ys = 2.0 * k1 * k2 * k3 * kw * Math.Pow(pr.Y / pr.A, 0.35) * Math.Pow(fr, 0.43) * pr.A;
            double cap = (fr <= 0.8 ? 2.4 : 3.0) * pr.A;
            string note = pr.Skew > 5 ? "K1 = 1.0 (skew > 5°)" : "";
            if (ys > cap && pr.Shape.ToLowerInvariant() is "round" or "round nose" or "circular" or "cylinder" && pr.Skew <= 5) { ys = cap; note = $"limited to {(fr <= 0.8 ? 2.4 : 3.0)}·a (round nose aligned with flow)"; }
            if (kw < 1) note = (note + $" wide-pier factor Kw = {kw:F2}").Trim();
            double total = ysC + ys;
            pierRows.Add([pr.Name, pr.A, R(fr, 3), k1, R(k2, 3), k3, R(kw, 3), R(ys, 2), R(ysC, 2), R(total, 2), bed is { } b ? R(b - total, 2) : null, note]);
        }
        if (pierRows.Count > 0)
        {
            tables.Add(new("Pier scour (CSU, HEC-18 Eq. 7.1)", ["Pier", $"a ({L})", "Fr1", "K1", "K2", "K3", "Kw", $"Local ys ({L})", $"Contraction ({L})", $"Total ({L})", $"Scoured bed ({L})", "Notes"], pierRows,
                ["ys/a = 2.0·K1·K2·K3·(y1/a)^0.35·Fr1^0.43; K2 = (cos θ + (L/a)·sin θ)^0.65 with L/a ≤ 12; K3 = 1.1 for clear-water and plane bed, 1.1–1.3 by dune size.",
                 "For round-nose piers aligned with the flow ys ≤ 2.4·a (Fr ≤ 0.8) or 3.0·a (Fr > 0.8). Wide piers in shallow flow (y/a < 0.8, a/D50 > 50, Fr < 1): Kw by Eq. 7.4.",
                 "Coarse-bed (D50 ≥ 20 mm) and debris effects are not computed; add a debris raft as a wider effective pier if needed."]));
            summary.Add($"Pier scour (local + contraction): up to {pierRows.Max(r => (double)r[9]!):F2} {L}.");
        }

        var abutRows = new List<IReadOnlyList<object?>>();
        foreach (var ab in abuts)
        {
            double fr = ab.Ve / Math.Sqrt(g * ab.Ya);
            double k2 = Math.Pow(ab.Skew / 90, 0.13);
            double froe = ab.Ya * (2.27 * ab.K1 * k2 * Math.Pow(ab.Len / ab.Ya, 0.43) * Math.Pow(fr, 0.61) + 1);
            double? hire = ab.Len / ab.Ya > 25 ? ab.Ya * 4 * Math.Pow(fr, 0.33) * ab.K1 / 0.55 * k2 : null;
            double governs = hire is { } h ? h : froe;
            abutRows.Add([ab.Name, ab.Type, ab.K1, R(k2, 3), R(ab.Len / ab.Ya, 1), R(fr, 3), R(froe, 2), hire is { } hh ? R(hh, 2) : null, R(governs, 2), R(governs + ysC, 2),
                hire is null ? "Froehlich (L'/ya ≤ 25)" : "HIRE (L'/ya > 25)"]);
        }
        if (abutRows.Count > 0)
        {
            tables.Add(new("Abutment scour (HEC-18 Eqs. 8.1, 8.2)", ["Abutment", "Type", "K1", "K2", "L'/ya", "Fr", $"Froehlich ys ({L})", $"HIRE ys ({L})", $"Governing ys ({L})", $"With contraction ({L})", "Method"], abutRows,
                ["Froehlich: ys/ya = 2.27·K1·K2·(L'/ya)^0.43·Fr^0.61 + 1 (the +1 is the safety factor HEC-18 includes for design). HIRE: ys/ya = 4·Fr^0.33·(K1/0.55)·K2, for L'/ya > 25.",
                 "K1 = 1.00 vertical wall, 0.82 vertical wall with wing walls, 0.55 spill-through; K2 = (θ/90)^0.13.",
                 "HEC-18 (2012) also gives the NCHRP 24-20 method, which reads its amplification factor from charts; it is not included (D-351). Abutment scour is usually countered with riprap (HEC-23) rather than designed for."]));
            summary.Add($"Abutment scour: up to {abutRows.Max(r => (double)r[8]!):F2} {L} local.");
        }
        if (summary.Count == 0) summary.Add($"{(liveBed ? "Live-bed" : "Clear-water")} conditions; give Q2, W2 for contraction scour, or [Piers] / [Abutments].");
        msgs.Add(CalcMessage.Info("I-BS-002", "HEC-18 asks for scour from the 100-yr (or overtopping) flood for design and the 500-yr (or 0.2 % chance) flood as a check; run the tool for each with its hydraulics."));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
