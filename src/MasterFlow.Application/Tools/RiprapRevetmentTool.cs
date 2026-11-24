using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Rock riprap for bank and channel revetment by FHWA HEC-11, <i>Design of Riprap Revetment</i> (1989): the HEC-11
/// D50 equation with the side-slope factor K1 and the specific-gravity and stability-factor corrections, layer
/// thickness, standard class, and Terzaghi granular filter criteria between riprap, filter and base soil.
/// </summary>
public sealed class RiprapRevetmentTool : IDesignTool
{
    public string Id => "riprap-revetment";
    public string Name => "Riprap bank and channel revetment (HEC-11)";
    public string Description => "FHWA HEC-11 (1989) rock riprap revetment: D50 = 0.001·C·Va³/(davg^0.5·K1^1.5) (U.S.; 0.00594 in SI) with the bank-angle factor K1 = √(1 − sin²θ/sin²φ), the specific-gravity correction Csg = 2.12/(Ss − 1)^1.5 and the stability-factor correction Csf = (SF/1.2)^1.5; layer thickness, the next standard class (HEC-23 DG 4) and the granular filter rules (D15/D85 < 5, 5 < D15/D15 < 40, D50/D50 < 40) between riprap, filter and bank soil.";
    public bool UsesNetwork => false;

    /// <summary>Standard riprap classes by nominal D50 (HEC-23, 3rd ed. 2009, Design Guideline 4 / NCHRP Report 568).</summary>
    public static readonly (string Class, double D50In, double D50Mm)[] Classes =
    [
        ("I", 6, 150), ("II", 9, 230), ("III", 12, 300), ("IV", 15, 380), ("V", 18, 460),
        ("VI", 21, 530), ("VII", 24, 610), ("VIII", 30, 760), ("IX", 36, 910), ("X", 42, 1070),
    ];

    /// <summary>Default angle of repose (degrees) for D50 of about 6 in. and larger, read from the flat upper part of HEC-11 Figure 4.</summary>
    public static double? ReposeAngle(string shape) => shape.Trim().ToLowerInvariant() switch
    {
        "angular" or "very angular" or "crushed" or "quarry" => 42,
        "subangular" or "sub-angular" => 41,
        "rounded" or "very rounded" or "round" or "cobble" or "cobbles" => 40,
        _ => null,
    };

    /// <summary>HEC-11 bank-angle correction K1 = √(1 − sin²θ/sin²φ); NaN when the bank is as steep as the angle of repose.</summary>
    public static double K1(double bankAngleDeg, double reposeDeg)
    {
        double s = Math.Sin(bankAngleDeg * Math.PI / 180), f = Math.Sin(reposeDeg * Math.PI / 180);
        double x = 1 - s * s / (f * f);
        return x > 0 ? Math.Sqrt(x) : double.NaN;
    }

    /// <summary>HEC-11 Eq. (D50 in ft or m): 0.001·C·V³/(d^0.5·K1^1.5) (U.S.) or 0.00594·C·V³/(d^0.5·K1^1.5) (SI).</summary>
    public static double D50(double v, double d, double k1, double ss, double sf, bool si)
    {
        double csg = 2.12 / Math.Pow(ss - 1, 1.5), csf = Math.Pow(sf / 1.2, 1.5);
        return (si ? 0.00594 : 0.001) * csg * csf * v * v * v / (Math.Sqrt(d) * Math.Pow(k1, 1.5));
    }

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Riprap revetment, FHWA HEC-11 (1989). SI: m, m/s; D50 results in mm.
        Ss = 2.65                # specific gravity of the stone
        shape = angular          # angular, subangular or rounded (or give phi in degrees)
        # SF: 1.0–1.2 straight reach, uniform flow; 1.3–1.6 moderate bends, waves or debris; 1.6–2.0 sharp bends, ice, turbulence.
        [Reaches]
        Name	V avg (m/s)	d avg (m)	Bank slope (H:1V)	SF	Placement
        Straight reach	2.4	2.0	2	1.2	dry
        Outside of bend	2.4	2.0	2	1.5	underwater
        [Soils]
        Layer	D15 (mm)	D50 (mm)	D85 (mm)
        Filter	6	20	45
        Bank soil	0.08	0.4	1.5
        """ : """
        # Riprap revetment, FHWA HEC-11 (1989). U.S. units: ft, ft/s; D50 results in inches.
        Ss = 2.65                # specific gravity of the stone
        shape = angular          # angular, subangular or rounded (or give phi in degrees)
        # phi = 42               # angle of repose, degrees (overrides shape)

        # V avg and d avg: average velocity and depth of the main channel at the design flow (from a profile run).
        # SF: 1.0–1.2 straight reach, uniform flow; 1.3–1.6 moderate bends, waves or debris; 1.6–2.0 sharp bends, ice, turbulence.
        # Placement: dry, or underwater (HEC-11 thickens the layer by 50 %).
        [Reaches]
        Name	V avg (ft/s)	d avg (ft)	Bank slope (H:1V)	SF	Placement
        Straight reach	8	6.5	2	1.2	dry
        Outside of bend	8	6.5	2	1.5	underwater

        # Optional granular filter check, top layer to bottom (riprap D15/D50/D85 are taken from the computed D50 when left out).
        [Soils]
        Layer	D15 (mm)	D50 (mm)	D85 (mm)
        Filter	6	20	45
        Bank soil	0.08	0.4	1.5
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Riprap revetment (HEC-11)";
        bool si = context.Units == UnitSystem.SI;
        string L = si ? "m" : "ft", V = si ? "m/s" : "ft/s", Dn = si ? "mm" : "in.";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double ss = p.Number("Ss", 2.65, "Ss");
        string shape = p.Text("shape") ?? "angular";
        double? phiIn = p.OptionalNumber("phi", "Angle of repose phi");
        double phi = phiIn ?? ReposeAngle(shape) ?? double.NaN;
        if (!(ss > 1)) msgs.Add(CalcMessage.Error("E-RR-001", "Ss must be above 1 (2.65 is typical of quarry stone)."));
        if (double.IsNaN(phi)) msgs.Add(CalcMessage.Error("E-RR-002", $"shape \"{shape}\" is not angular, subangular or rounded; give phi (degrees) instead."));
        else if (phi is < 30 or > 45) msgs.Add(CalcMessage.Warning("W-RR-001", $"Angle of repose {phi:F0}° is outside the 30–45° range of HEC-11 Figure 4."));

        var reaches = new List<(string Name, double V, double D, double Z, double Sf, bool Under)>();
        foreach (var r in p.Rows("Reaches"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Reach {reaches.Count + 1}";
            double? v = p.Cell(r, 1, "V avg", name), d = p.Cell(r, 2, "d avg", name), z = p.Cell(r, 3, "bank slope", name), sf = p.Cell(r, 4, "SF", name);
            string place = (r.Length > 5 ? r[5] : "dry").Trim().ToLowerInvariant();
            if (v is not > 0 || d is not > 0 || z is not > 0) { msgs.Add(CalcMessage.Error("E-RR-003", $"{name}: give V avg, d avg and the bank slope (H:1V), all above zero.")); continue; }
            if (sf is { } s && s < 1) { msgs.Add(CalcMessage.Error("E-RR-004", $"{name}: the stability factor must be 1.0 or more (HEC-11 uses 1.0–2.0).")); continue; }
            if (place is not ("" or "dry" or "underwater" or "under water" or "wet")) { msgs.Add(CalcMessage.Error("E-RR-005", $"{name}: placement \"{place}\" is not dry or underwater.")); continue; }
            reaches.Add((name, v.Value, d.Value, z.Value, sf ?? 1.2, place is "underwater" or "under water" or "wet"));
        }
        var soils = new List<(string Name, double D15, double D50, double D85)>();
        foreach (var r in p.Rows("Soils"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Layer {soils.Count + 1}";
            double? a = p.Cell(r, 1, "D15", name), b = p.Cell(r, 2, "D50", name), c = p.Cell(r, 3, "D85", name);
            if (a is not > 0 || b is not > 0 || c is not > 0 || a > b || b > c) { msgs.Add(CalcMessage.Error("E-RR-008", $"{name}: give D15 ≤ D50 ≤ D85 in mm, all above zero.")); continue; }
            soils.Add((name, a.Value, b.Value, c.Value));
        }
        if (reaches.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error))
            msgs.Add(CalcMessage.Error("E-RR-006", "Give at least one row under [Reaches]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double csg = 2.12 / Math.Pow(ss - 1, 1.5);
        var rows = new List<IReadOnlyList<object?>>();
        var summary = new List<string>();
        double maxD50 = 0;
        foreach (var rc in reaches)
        {
            double theta = Math.Atan(1 / rc.Z) * 180 / Math.PI;
            double k1 = K1(theta, phi);
            if (double.IsNaN(k1))
            {
                msgs.Add(CalcMessage.Error("E-RR-007", $"{rc.Name}: the bank (1V:{rc.Z:G3}H, {theta:F1}°) is as steep as or steeper than the angle of repose ({phi:F0}°); riprap will not stand. Flatten the bank."));
                rows.Add([rc.Name, rc.V, rc.D, rc.Z, R(theta, 1), null, rc.Sf, null, null, null, null, null, "bank too steep"]);
                continue;
            }
            double csf = Math.Pow(rc.Sf / 1.2, 1.5);
            double d50 = D50(rc.V, rc.D, k1, ss, rc.Sf, si);                 // ft or m
            double d50n = si ? d50 * 1000 : d50 * 12;                          // mm or in.
            var cls = Classes.FirstOrDefault(c => (si ? c.D50Mm : c.D50In) >= d50n - 1e-9);
            double useN = cls.Class is null ? d50n : si ? cls.D50Mm : cls.D50In;
            double use = si ? useN / 1000 : useN / 12;
            double minT = si ? 0.30 : 1.0;
            double t = Math.Max(Math.Max(2 * use, 1.5 * use), minT);           // D100 ≈ 2·D50 governs over 1.5·D50
            if (rc.Under) t *= 1.5;
            maxD50 = Math.Max(maxD50, useN);
            string note = rc.Under ? "underwater placement: thickness × 1.5" : "";
            if (cls.Class is null) { note = (note + "; above Class X: use a special design").TrimStart(';', ' '); msgs.Add(CalcMessage.Warning("W-RR-002", $"{rc.Name}: D50 {d50n:F1} {Dn} is above Class X ({(si ? "1070 mm" : "42 in.")}); consider a flatter bank, a gabion or concrete lining, or grouted riprap.")); }
            if (rc.Z < 1.5) { note = (note + "; bank steeper than 1V:1.5H").TrimStart(';', ' '); msgs.Add(CalcMessage.Warning("W-RR-003", $"{rc.Name}: HEC-11 recommends banks no steeper than 1V:1.5H for rock riprap.")); }
            rows.Add([rc.Name, rc.V, rc.D, rc.Z, R(theta, 1), R(k1, 3), rc.Sf, R(csg * csf, 3), R(d50n, si ? 0 : 1), cls.Class ?? "—", R(useN, 0), R(t, 2), note]);
            summary.Add($"{rc.Name}: D50 ≥ {d50n:F1} {Dn} → Class {cls.Class ?? "special"}{(cls.Class is null ? "" : $" ({useN:G3} {Dn})")}, layer {t:F2} {L} thick.");
            if (rc.V > (si ? 4.5 : 15)) msgs.Add(CalcMessage.Info("I-RR-001", $"{rc.Name}: velocity above {(si ? "4.5 m/s" : "15 ft/s")}; HEC-11 was developed for typical channel velocities. Confirm with HEC-15 §6 (steep slope) or a site-specific design."));
        }
        var tables = new List<ToolTable>
        {
            new("Riprap size and layer (HEC-11)", ["Reach", $"V avg ({V})", $"d avg ({L})", "Bank H:1V", "θ (deg)", "K1", "SF", "C = Csg·Csf", $"D50 req. ({Dn})", "Class", $"Class D50 ({Dn})", $"Thickness ({L})", "Notes"], rows,
            [
                $"D50 = {(si ? "0.00594" : "0.001")}·C·Va³/(davg^0.5·K1^1.5); K1 = √(1 − sin²θ/sin²φ), φ = {phi:F0}° ({(phiIn is null ? shape : "given")}); Csg = 2.12/(Ss − 1)^1.5 = {csg:F3} (Ss = {ss:G3}); Csf = (SF/1.2)^1.5.",
                $"Class: smallest standard class (HEC-23 DG 4) at or above the required D50. Gradation D100 ≈ 2·D50. Thickness = max(D100, 1.5·D50, {(si ? "0.30 m" : "12 in.")}), × 1.5 underwater (HEC-11 §IV).",
                "Extend the revetment below the expected scour (toe trench or launching apron) and above the design water surface plus freeboard; HEC-11 Ch. IV.",
            ]),
        };

        // Filter check: riprap (from the class D50) over the first layer, then each layer over the next.
        if (soils.Count > 0 && maxD50 > 0)
        {
            double rd50 = si ? maxD50 : maxD50 * 25.4;
            // Riprap gradation for the check: D15 ≈ 0.5·D50, D85 ≈ 1.5·D50 (middle of the usual class bands).
            var layers = new List<(string Name, double D15, double D50, double D85)> { ("Riprap (largest class above)", 0.5 * rd50, rd50, 1.5 * rd50) };
            layers.AddRange(soils);
            var frows = new List<IReadOnlyList<object?>>();
            for (int i = 0; i + 1 < layers.Count; i++)
            {
                var (up, dn) = (layers[i], layers[i + 1]);
                double piping = up.D15 / dn.D85, perm = up.D15 / dn.D15, grad = up.D50 / dn.D50;
                bool ok = piping < 5 && perm > 5 && perm < 40 && grad < 40;
                frows.Add([$"{up.Name} over {dn.Name}", R(piping, 2), R(perm, 2), R(grad, 2), ok ? "OK" : "FAILS"]);
                if (!ok) msgs.Add(CalcMessage.Warning("W-RR-004", $"{up.Name} over {dn.Name}: the filter rules are not met; add a granular layer or a geotextile between them."));
            }
            tables.Add(new("Granular filter check (HEC-11, Terzaghi)", ["Interface", "D15 coarse / D85 fine (< 5)", "D15 coarse / D15 fine (5–40)", "D50 coarse / D50 fine (< 40)", "Result"], frows,
                ["Riprap D15 and D85 are taken as 0.5 and 1.5 × the class D50 when the stone gradation is not given. A geotextile may replace a granular filter; size it by AOS and permeability (HEC-11 Ch. IV)."]));
        }
        else if (soils.Count == 0)
            msgs.Add(CalcMessage.Info("I-RR-002", "No [Soils] given: the filter is not checked. HEC-11 requires a granular or geotextile filter under riprap."));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
