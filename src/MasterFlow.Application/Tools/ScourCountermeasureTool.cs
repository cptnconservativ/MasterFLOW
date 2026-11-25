using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Bridge scour countermeasures by FHWA HEC-23, <i>Bridge Scour and Stream Instability Countermeasures</i> (3rd ed., 2009):
/// riprap at piers (Design Guideline 11, Isbash form) and riprap at abutments (Design Guideline 14, with the set-back
/// ratio for the characteristic velocity), each with its extent, thickness and standard class.
/// </summary>
public sealed class ScourCountermeasureTool : IDesignTool
{
    public string Id => "scour-countermeasures";
    public string Name => "Bridge scour countermeasures (HEC-23): pier and abutment riprap";
    public string Description => "FHWA HEC-23 (3rd ed., 2009) riprap countermeasures. Piers (DG 11): D50 = 0.692·(K·V)²/((Ss − 1)·2g), K = 1.5 round nose or 1.7 rectangular, V the local velocity or a multiplier (0.9–1.7) times the average; extent 2a from every face, thickness 3·D50. Abutments (DG 14): D50/y = K/(Ss − 1)·(V²/gy) for Fr ≤ 0.8 (K 0.89 spill-through, 1.02 vertical wall) or K/(Ss − 1)·(V²/gy)^0.14 for Fr > 0.8 (K 0.61, 0.69); V by the set-back ratio; apron 2y (≤ 25 ft) into the waterway, thickness max(1.5·D50, D100). Standard classes I–X.";
    public bool UsesNetwork => false;

    /// <summary>Standard riprap classes by nominal D50 (HEC-23 DG 4 / NCHRP Report 568).</summary>
    public static readonly (string Class, double D50In, double D50Mm)[] Classes =
    [
        ("I", 6, 150), ("II", 9, 230), ("III", 12, 300), ("IV", 15, 380), ("V", 18, 460),
        ("VI", 21, 530), ("VII", 24, 610), ("VIII", 30, 760), ("IX", 36, 910), ("X", 42, 1070),
    ];

    /// <summary>HEC-23 DG 11 (Eq. 11.1): D50 = 0.692·(K·V)²/((Ss − 1)·2g), in the length unit of V and g.</summary>
    public static double PierD50(double k, double v, double ss, double g) => 0.692 * Math.Pow(k * v, 2) / ((ss - 1) * 2 * g);

    /// <summary>HEC-23 DG 14 (Eqs. 14.1, 14.2): D50 for abutment riprap; spill-through or vertical wall.</summary>
    public static double AbutmentD50(double v, double y, double ss, double g, bool verticalWall)
    {
        double fr = v / Math.Sqrt(g * y);
        return fr <= 0.8
            ? y * (verticalWall ? 1.02 : 0.89) / (ss - 1) * (v * v / (g * y))
            : y * (verticalWall ? 0.69 : 0.61) / (ss - 1) * Math.Pow(v * v / (g * y), 0.14);
    }

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Bridge scour countermeasures, FHWA HEC-23 (2009). SI: m, m/s, m³/s; D50 results in mm.
        Ss = 2.65
        [Piers]
        Name	Shape	Width a	Local V	Average V	Multiplier
        Pier 1	round	1.2	2.6		
        Pier 2	rectangular	1.2		1.8	1.5
        [Abutments]
        Name	Type	Depth y	Velocity V	Q total	A total	SBR	Qf setback	Af setback
        Left	spill-through	1.5		300	180	8	40	45
        Right	vertical wall	2.0	1.9			
        """ : """
        # Bridge scour countermeasures, FHWA HEC-23 (3rd ed., 2009). U.S. units: ft, ft/s, cfs; D50 results in inches.
        Ss = 2.65                 # specific gravity of the stone

        # Piers (DG 11). Shape: round (K = 1.5) or rectangular (K = 1.7). Give the local velocity just upstream of the pier,
        # or the average channel velocity Q/A and a multiplier: 0.9 for a pier near the bank in a straight reach up to
        # 1.7 for a pier in the main current around a bend.
        [Piers]
        Name	Shape	Width a (ft)	Local V (ft/s)	Average V (ft/s)	Multiplier
        Pier 1	round	4	8.5		
        Pier 2	rectangular	4		6	1.5

        # Abutments (DG 14). Type: spill-through or vertical wall. y = flow depth at the abutment toe in the contracted
        # section. Velocity: give V, or Q and A of the whole bridge opening plus the set-back ratio SBR (set-back length /
        # average channel depth) and the set-back overbank Qf, Af. SBR < 5 uses Q/A; SBR ≥ 5 uses Qf/Af.
        [Abutments]
        Name	Type	Depth y (ft)	Velocity V (ft/s)	Q total (cfs)	A total (ft²)	SBR	Qf setback (cfs)	Af setback (ft²)
        Left	spill-through	5		10000	1800	8	1200	480
        Right	vertical wall	6.5	6.2				
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Bridge scour countermeasures (HEC-23)";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.81 : 32.2;
        string L = si ? "m" : "ft", Vu = si ? "m/s" : "ft/s", Dn = si ? "mm" : "in.";
        double toN = si ? 1000 : 12;
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double ss = p.Number("Ss", 2.65, "Ss");
        if (!(ss > 1)) msgs.Add(CalcMessage.Error("E-SC-001", "Ss must be above 1 (2.65 is typical)."));

        var piers = new List<(string Name, string Shape, double K, double A, double V, string VNote)>();
        foreach (var r in p.Rows("Piers"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Pier {piers.Count + 1}";
            string shape = (r.Length > 1 ? r[1] : "round").Trim().ToLowerInvariant();
            double? k = shape switch { "round" or "round nose" or "round-nose" or "circular" => 1.5, "rectangular" or "square" or "square nose" => 1.7, _ => null };
            double? a = p.Cell(r, 2, "width", name), vl = p.Cell(r, 3, "local V", name), va = p.Cell(r, 4, "average V", name), kv = p.Cell(r, 5, "multiplier", name);
            if (k is null) { msgs.Add(CalcMessage.Error("E-SC-002", $"{name}: shape \"{shape}\" is not round or rectangular.")); continue; }
            if (a is not > 0) { msgs.Add(CalcMessage.Error("E-SC-003", $"{name}: give the pier width above zero.")); continue; }
            if (vl is > 0) { piers.Add((name, shape, k.Value, a.Value, vl.Value, "local")); continue; }
            if (va is not > 0) { msgs.Add(CalcMessage.Error("E-SC-004", $"{name}: give the local velocity, or the average velocity and a multiplier.")); continue; }
            double m = kv ?? 1.7;
            if (kv is null) msgs.Add(CalcMessage.Warning("W-SC-001", $"{name}: no velocity multiplier; 1.7 (pier in the main current around a bend, HEC-23's upper value) is used."));
            else if (m is < 0.9 or > 1.7) msgs.Add(CalcMessage.Warning("W-SC-002", $"{name}: multiplier {m:G3} is outside HEC-23's 0.9–1.7 range."));
            piers.Add((name, shape, k.Value, a.Value, m * va.Value, $"{m:G3} × {va:G3}"));
        }

        var abuts = new List<(string Name, bool Vertical, double Y, double V, string VNote)>();
        foreach (var r in p.Rows("Abutments"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Abutment {abuts.Count + 1}";
            string type = (r.Length > 1 ? r[1] : "").Trim().ToLowerInvariant();
            bool? vert = type switch { "spill-through" or "spill through" => false, "vertical wall" or "vertical" or "wing wall" or "vertical wall with wing walls" => true, _ => null };
            double? y = p.Cell(r, 2, "depth", name), v = p.Cell(r, 3, "velocity", name), q = p.Cell(r, 4, "Q total", name), ar = p.Cell(r, 5, "A total", name),
                sbr = p.Cell(r, 6, "SBR", name), qf = p.Cell(r, 7, "Qf", name), af = p.Cell(r, 8, "Af", name);
            if (vert is null) { msgs.Add(CalcMessage.Error("E-SC-005", $"{name}: type \"{type}\" is not spill-through or vertical wall.")); continue; }
            if (y is not > 0) { msgs.Add(CalcMessage.Error("E-SC-006", $"{name}: give the flow depth y at the abutment above zero.")); continue; }
            if (v is > 0) { abuts.Add((name, vert.Value, y.Value, v.Value, "given")); continue; }
            if (sbr is >= 5)
            {
                if (qf is not > 0 || af is not > 0) { msgs.Add(CalcMessage.Error("E-SC-007", $"{name}: SBR ≥ 5 needs the set-back overbank Qf and Af.")); continue; }
                abuts.Add((name, vert.Value, y.Value, qf.Value / af.Value, $"Qf/Af (SBR {sbr:G3} ≥ 5)"));
                continue;
            }
            if (q is not > 0 || ar is not > 0) { msgs.Add(CalcMessage.Error("E-SC-008", $"{name}: give V, or Q and A of the bridge opening (and SBR, Qf, Af for a set-back abutment).")); continue; }
            if (sbr is null) msgs.Add(CalcMessage.Info("I-SC-001", $"{name}: no SBR given; V = Q/A of the whole opening (HEC-23's rule for SBR < 5)."));
            abuts.Add((name, vert.Value, y.Value, q.Value / ar.Value, sbr is null ? "Q/A" : $"Q/A (SBR {sbr:G3} < 5)"));
        }
        if (piers.Count == 0 && abuts.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error))
            msgs.Add(CalcMessage.Error("E-SC-009", "Give at least one row under [Piers] or [Abutments]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        (string? Cls, double D50) Pick(double d50n)
        {
            var c = Classes.FirstOrDefault(x => (si ? x.D50Mm : x.D50In) >= d50n - 1e-9);
            return c.Class is null ? (null, d50n) : (c.Class, si ? c.D50Mm : c.D50In);
        }

        var tables = new List<ToolTable>();
        var summary = new List<string>();
        var prow = new List<IReadOnlyList<object?>>();
        foreach (var pr in piers)
        {
            double d50 = PierD50(pr.K, pr.V, ss, g), d50n = d50 * toN;
            var (cls, use) = Pick(d50n);
            double useL = use / toN;
            double thick = 3 * useL, extent = 2 * pr.A;
            if (cls is null) msgs.Add(CalcMessage.Warning("W-SC-003", $"{pr.Name}: D50 {d50n:F1} {Dn} is above Class X; consider partially grouted riprap, articulating concrete blocks or a deeper foundation (HEC-23 DG 7, 8, 9)."));
            prow.Add([pr.Name, pr.Shape, pr.K, pr.A, R(pr.V, 2), pr.VNote, R(d50n, si ? 0 : 1), cls ?? "—", R(use, 0), R(thick, 2), R(extent, 2), R(pr.A + 2 * extent, 2)]);
            summary.Add($"{pr.Name}: D50 ≥ {d50n:F1} {Dn} → Class {cls ?? "special"}, {thick:F2} {L} thick, {extent:F1} {L} out from each face.");
        }
        if (prow.Count > 0)
            tables.Add(new("Riprap at piers (HEC-23 DG 11)", ["Pier", "Shape", "K", $"a ({L})", $"V used ({Vu})", "V from", $"D50 req. ({Dn})", "Class", $"Class D50 ({Dn})", $"Thickness 3·D50 ({L})", $"Extent from face 2a ({L})", $"Width across pier ({L})"], prow,
            [
                "D50 = 0.692·(K·V)²/((Ss − 1)·2g); K = 1.5 round nose, 1.7 rectangular. V is the local velocity upstream of the pier, or the average Q/A times 0.9 (near the bank, straight reach) to 1.7 (main current at a bend).",
                "Extend the riprap at least 2a from the pier face in every direction; thickness at least 3·D50; top of the riprap at the streambed (or below it, with the thickness measured down from there) and the bottom below the contraction scour depth.",
                "Place a geotextile or granular filter under the riprap (HEC-23 DG 16); do not extend the filter to the riprap's outer edge, so the edge can launch into a scour hole.",
            ]));

        var arow = new List<IReadOnlyList<object?>>();
        foreach (var ab in abuts)
        {
            double fr = ab.V / Math.Sqrt(g * ab.Y);
            double d50 = AbutmentD50(ab.V, ab.Y, ss, g, ab.Vertical), d50n = d50 * toN;
            var (cls, use) = Pick(d50n);
            double useL = use / toN;
            double thick = Math.Max(1.5 * useL, 2 * useL);
            double apron = Math.Min(2 * ab.Y, si ? 7.6 : 25);
            double k = fr <= 0.8 ? (ab.Vertical ? 1.02 : 0.89) : (ab.Vertical ? 0.69 : 0.61);
            if (cls is null) msgs.Add(CalcMessage.Warning("W-SC-003", $"{ab.Name}: D50 {d50n:F1} {Dn} is above Class X; consider a guide bank to move the scour away from the abutment (HEC-23 DG 15) or a special design."));
            arow.Add([ab.Name, ab.Vertical ? "vertical wall" : "spill-through", ab.Y, R(ab.V, 2), ab.VNote, R(fr, 3), k, fr <= 0.8 ? "Eq. 14.1" : "Eq. 14.2", R(d50n, si ? 0 : 1), cls ?? "—", R(use, 0), R(thick, 2), R(apron, 2)]);
            summary.Add($"{ab.Name}: D50 ≥ {d50n:F1} {Dn} → Class {cls ?? "special"}, {thick:F2} {L} thick, apron {apron:F1} {L} into the waterway.");
        }
        if (arow.Count > 0)
            tables.Add(new("Riprap at abutments (HEC-23 DG 14)", ["Abutment", "Type", $"y ({L})", $"V ({Vu})", "V from", "Fr", "K", "Equation", $"D50 req. ({Dn})", "Class", $"Class D50 ({Dn})", $"Thickness ({L})", $"Toe apron ({L})"], arow,
            [
                "Fr ≤ 0.8: D50/y = K/(Ss − 1)·(V²/gy), K = 0.89 spill-through, 1.02 vertical wall. Fr > 0.8: D50/y = K/(Ss − 1)·(V²/gy)^0.14, K = 0.61 spill-through, 0.69 vertical wall.",
                "V: set-back ratio SBR = set-back length / average channel depth. SBR < 5: V = Q/A of the whole contracted opening; SBR ≥ 5: V = Qf/Af of the set-back overbank.",
                $"Thickness = max(1.5·D50, D100) with D100 ≈ 2·D50. The toe apron runs the full length of the abutment toe and around its curved ends, and out into the waterway 2y but not more than {(si ? "7.6 m" : "25 ft")}. Carry riprap up the abutment slope to 2 ft above the design high water.",
            ]));
        msgs.Add(CalcMessage.Info("I-SC-002", "HEC-23 sizes countermeasures for the design flood of the bridge foundation (often the 100-yr or 500-yr flood); bank revetment between abutments uses the riprap revetment tool (HEC-11, DG 12)."));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
