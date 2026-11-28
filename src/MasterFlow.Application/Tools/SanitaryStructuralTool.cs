using System.Globalization;
using System.Text;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Structural check of a sanitary sewer design at its designed depths (premium). Runs the sanitary sewer design (same
/// input as <see cref="SanitarySewerTool"/>), takes each sewer's size and the cover at both manholes, and checks it with
/// the pipe structural methods of <see cref="PipeStructuralTool"/>: PVC (ASTM D3034 / F679) and HDPE by the Modified
/// Iowa deflection against the Ten States 5 % limit, choosing SDR 35 (PS 46) and stepping up to SDR 26 (PS 115) where
/// needed; reinforced concrete (Rinker precast, ASTM C76 wall B) by the indirect design D-load and class.
/// </summary>
public sealed class SanitaryStructuralTool : IDesignTool
{
    public string Id => "sanitary-deep-structural";
    public string Name => "Sanitary sewer structural check at designed depths";
    public string Description => "Runs the sanitary network design and checks every sewer at its designed cover: PVC SDR 35 / SDR 26 (ASTM D3034, F679) and HDPE by the Modified Iowa deflection against the Ten States 5 % limit (stepping up to SDR 26 where SDR 35 deflects too much), and reinforced concrete (Rinker precast, ASTM C76 wall B) by the AASHTO/ACPA indirect design D-load and class. Earth load at the deep end, HL-93 live load at the shallow end; deep sewers (15 ft and more) are listed separately. Writes the pipe-structural tool input too.";
    public bool UsesNetwork => true;

    private static readonly SanitarySewerTool Design = new();

    /// <summary>PVC gravity sewer outside diameters (in.): ASTM D3034 for 4–15 in., ASTM F679 for 18–36 in.</summary>
    public static readonly IReadOnlyDictionary<double, double> PvcOd = new Dictionary<double, double>
    {
        [6] = 6.275, [8] = 8.400, [10] = 10.500, [12] = 12.500, [15] = 15.300,
        [18] = 18.701, [21] = 22.047, [24] = 24.803, [27] = 28.032, [30] = 32.000, [36] = 38.300,
    };

    /// <summary>Corrugated HDPE (AASHTO M294 / ASTM F2306) minimum pipe stiffness (psi) by size, and approximate OD (in.).</summary>
    public static readonly IReadOnlyDictionary<double, (double Ps, double Od)> Hdpe = new Dictionary<double, (double, double)>
    {
        [12] = (50, 14.5), [15] = (42, 18), [18] = (40, 21), [24] = (34, 28), [30] = (28, 36), [36] = (22, 42), [42] = (20, 48), [48] = (18, 54), [60] = (14, 67),
    };

    public const double PvcSdr35Ps = 46, PvcSdr26Ps = 115;

    public string SampleInput(DesignToolContext context)
    {
        var sanitary = Design.SampleInput(context);
        // A deeper sample: the same network with the rims raised so the lower sewers sit 15–25 ft deep.
        sanitary = sanitary.Replace("MH-3	3407.80", "MH-3	3418.80").Replace("MH-5	3405.10", "MH-5	3421.10").Replace("MH-6	3402.40", "MH-6	3420.40");
        return """
            # Structural check of a sanitary sewer at its designed depths. The structural keys come first; everything after
            # them is the sanitary sewer design input (same keys and tables as "Sanitary sewer network design").
            material = auto             # auto (PVC to 36 in., reinforced concrete above), PVC, HDPE or RCP
            deflection limit = 5        # % (Ten States §33.85: flexible pipe deflection not over 5 %)
            embedment = gravel          # crushed rock, gravel/sand, silty sand, silt/lean clay (Howard 1977 E′)
            compaction = moderate       # dumped, slight, moderate (85–95 %), high (> 95 %)
            # E′ = 1000                 # psi; replaces the Howard value
            unit weight = 120           # pcf
            live load = HL-93           # HL-93 or none
            LLDF = 1.15
            installation = 2            # RCP Standard Installation Type 1–4
            deep cover = 15             # ft; sewers at or below this cover are listed as deep
            # [Materials]               # per-sewer overrides
            # Pipe	Material	PS (psi)	Installation	Wall (in)
            # S-5	RCP		2

            """ + sanitary;
    }

    private sealed record Check(string Pipe, double D, string Material, double? CovUp, double? CovDn, double Hmax, double Hmin, bool Deep, string Result, string Status, bool Ok, string Basis);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sanitary sewer structural check";
        var (dr, design) = Design.Design(input, context);
        var msgs = new List<CalcMessage>(dr.Messages.Where(m => m.Severity != MessageSeverity.Warning));
        if (design is null) return new(title, [], [], [.. dr.Messages]);
        var p = ToolInput.Parse(input);
        string mat = (p.Text("material") ?? "auto").Trim().ToUpperInvariant();
        double limit = p.Number("deflection limit", 5), gamma = p.Number("unit weight", 120), lldf = p.Number("LLDF", 1.15), dl = p.Number("DL", 1.0), deep = p.Number("deep cover", 15);
        double? eGiven = p.OptionalNumber("E′") ?? p.OptionalNumber("E'") ?? p.OptionalNumber("E prime");
        string soil = p.Text("embedment") ?? "gravel", comp = p.Text("compaction") ?? "moderate";
        double inst = p.Number("installation", 2);
        double? bfll = p.OptionalNumber("BfLL");
        string ll = (p.Text("live load") ?? "HL-93").Trim().ToLowerInvariant();
        bool live = ll is "hl-93" or "hl93" or "hs-20" or "hs20" or "yes";
        if (!live && ll is not ("none" or "no")) msgs.Add(CalcMessage.Error("E-SD-001", $"live load \"{ll}\" is not HL-93 or none."));
        if (mat is not ("AUTO" or "PVC" or "HDPE" or "RCP")) msgs.Add(CalcMessage.Error("E-SD-002", $"material \"{p.Text("material")}\" is not auto, PVC, HDPE or RCP."));
        if (!(gamma > 0) || !(lldf >= 1) || !(dl >= 1) || !(limit > 0)) msgs.Add(CalcMessage.Error("E-SD-003", "unit weight and deflection limit must be above zero; LLDF and DL must be 1.0 or more."));
        if (inst is not (1 or 2 or 3 or 4)) msgs.Add(CalcMessage.Error("E-SD-004", "installation must be Standard Installation Type 1, 2, 3 or 4."));
        double e = eGiven ?? PipeStructuralTool.EPrime(soil, comp);
        if (!(e >= 0)) msgs.Add(CalcMessage.Error("E-SD-005", $"embedment \"{soil}\" / compaction \"{comp}\" is not in Howard's table; give E′ directly."));
        var over = new Dictionary<string, (string? Mat, double? Ps, double? Inst, double? Wall)>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Materials"))
        {
            if (r.Length == 0 || design.Pipes.All(x => !x.Name.Equals(r[0], StringComparison.OrdinalIgnoreCase))) { msgs.Add(CalcMessage.Error("E-SD-006", $"[Materials] row \"{(r.Length > 0 ? r[0] : "")}\" is not a sewer of the design.")); continue; }
            string? m = r.Length > 1 && r[1].Length > 0 ? r[1].Trim().ToUpperInvariant() : null;
            if (m is not (null or "PVC" or "HDPE" or "RCP")) { msgs.Add(CalcMessage.Error("E-SD-006", $"{r[0]}: material \"{r[1]}\" is not PVC, HDPE or RCP.")); continue; }
            double? ps = p.Cell(r, 2, "PS", r[0]), ti = p.Cell(r, 3, "installation", r[0]), w = p.Cell(r, 4, "wall", r[0]);
            if (ps is <= 0 || w is <= 0 || ti is not (null or 1 or 2 or 3 or 4)) { msgs.Add(CalcMessage.Error("E-SD-006", $"{r[0]}: PS and wall must be above zero, installation 1–4.")); continue; }
            over[r[0]] = (m, ps, ti, w);
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var checks = new List<Check>();
        var rigidRows = new List<IReadOnlyList<object?>>();
        var flexRows = new List<IReadOnlyList<object?>>();
        var rigidIn = new List<string>(); var flexIn = new List<string>();
        int noCover = 0;
        foreach (var x in design.Pipes)
        {
            over.TryGetValue(x.Name, out var o);
            string m = o.Mat ?? (mat == "AUTO" ? (x.DiameterIn <= 36 ? "PVC" : "RCP") : mat);
            var mFrom = design.Manhole(x.From); var mTo = design.Manhole(x.To);
            if (m == "PVC" && !PvcOd.ContainsKey(x.DiameterIn) && o.Ps is null)
            {
                msgs.Add(CalcMessage.Warning("W-SD-004", $"{x.Name}: {x.DiameterIn:G3} in. PVC gravity sewer is not in ASTM D3034/F679 (6–36 in.); checked as reinforced concrete. Enter a [Materials] row with the PS to check it as PVC."));
                m = "RCP";
            }
            if (m == "HDPE" && !Hdpe.ContainsKey(x.DiameterIn) && o.Ps is null)
            {
                msgs.Add(CalcMessage.Warning("W-SD-004", $"{x.Name}: no AASHTO M294 stiffness listed for {x.DiameterIn:G3} in. HDPE; checked as reinforced concrete. Enter the PS under [Materials]."));
                m = "RCP";
            }
            double wall = m == "RCP" ? o.Wall ?? x.DiameterIn / 12 + 1 : 0;                                   // ASTM C76 wall B (Rinker standard)
            double od = m switch { "PVC" => PvcOd.TryGetValue(x.DiameterIn, out var v) ? v : x.DiameterIn * 1.05, "HDPE" => Hdpe.TryGetValue(x.DiameterIn, out var h) ? h.Od : x.DiameterIn * 1.15, _ => x.DiameterIn + 2 * wall };
            // Cover over the outside top of the pipe at each manhole: rim − (invert + ID + wall).
            double? cu = mFrom?.Rim is { } r1 ? r1 - (x.UpInvert + (x.DiameterIn + (m == "RCP" ? wall : (od - x.DiameterIn) / 2)) / 12) : null;
            double? cd = mTo?.Rim is { } r2 ? r2 - (x.DownInvert + (x.DiameterIn + (m == "RCP" ? wall : (od - x.DiameterIn) / 2)) / 12) : null;
            if (cu is null && cd is null) { noCover++; continue; }
            double hMax = Math.Max(cu ?? double.MinValue, cd ?? double.MinValue), hMin = Math.Min(cu ?? double.MaxValue, cd ?? double.MaxValue);
            if (hMin <= 0) { msgs.Add(CalcMessage.Warning("W-SD-005", $"{x.Name}: the pipe is not buried at one end (cover {hMin:F2} ft); checked at the deep end only.")); hMin = hMax; }
            bool isDeep = hMax >= deep - 1e-9;
            if (m == "RCP")
            {
                int ty = (int)(o.Inst ?? inst);
                var a = PipeStructuralTool.RigidDLoad(x.DiameterIn, wall, hMax, ty, bfll, gamma, live, lldf);
                var b = PipeStructuralTool.RigidDLoad(x.DiameterIn, wall, hMin, ty, bfll, gamma, live, lldf);
                var g = a.DLoad >= b.DLoad ? (H: hMax, R: a) : (H: hMin, R: b);
                string? cls = PipeStructuralTool.C76Class(g.R.DLoad);
                string res = cls is null ? "special design" : $"Class {cls}";
                checks.Add(new(x.Name, x.DiameterIn, $"RCP wall {(o.Wall is null ? "B" : F($"{wall:G3} in."))}, Type {ty}", cu, cd, hMax, hMin, isDeep, F($"D0.01 = {g.R.DLoad:F0} lb/ft/ft → {res}"), cls is null ? "above Class V" : "OK", cls is not null, F($"at {g.H:F1} ft")));
                rigidRows.Add([x.Name, x.DiameterIn, R(wall), ty, R(g.H), R(g.R.We, 0), R(g.R.Wf, 0), R(g.R.Wl, 0), R(g.R.Bfe), R(g.R.BfLL), R(g.R.DLoad, 0), res]);
                rigidIn.Add(F($"{x.Name}\t{x.DiameterIn:G4}\t{g.H:F2}\t{ty}\t{wall:G4}\t{(bfll is { } bb ? bb.ToString(CultureInfo.InvariantCulture) : "")}"));
                if (cls is null) msgs.Add(CalcMessage.Warning("W-SD-002", F($"{x.Name}: D-load {g.R.DLoad:F0} lb/ft/ft at {g.H:F1} ft cover is above Class V; use a special design (direct design, AASHTO LRFD 12.10.4.2), a better installation (Type 1) or a shallower sewer.")));
            }
            else
            {
                // PVC: SDR 35 (PS 46), stepping up to SDR 26 (PS 115) where SDR 35 deflects too much; HDPE: M294 minimum stiffness.
                var options = o.Ps is { } ps0 ? [(Label: F($"{m} PS {ps0:G3}"), Ps: ps0)]
                    : m == "PVC" ? new[] { (Label: x.DiameterIn <= 15 ? "PVC SDR 35 (D3034)" : "PVC PS 46 (F679)", Ps: PvcSdr35Ps), (Label: x.DiameterIn <= 15 ? "PVC SDR 26 (D3034)" : "PVC PS 115 (F679)", Ps: PvcSdr26Ps) }
                    : [(Label: F($"HDPE PS {Hdpe[x.DiameterIn].Ps:G3} (M294)"), Ps: Hdpe[x.DiameterIn].Ps)];
                (string Label, double Ps, double H, double Pe, double Pl, double D) pick = default;
                foreach (var opt in options)
                {
                    var a = PipeStructuralTool.FlexibleDeflection(hMax, od, opt.Ps, e, gamma, live, lldf, dl);
                    var b = PipeStructuralTool.FlexibleDeflection(hMin, od, opt.Ps, e, gamma, live, lldf, dl);
                    var g = a.Deflection >= b.Deflection ? (H: hMax, R: a) : (H: hMin, R: b);
                    pick = (opt.Label, opt.Ps, g.H, g.R.Pe, g.R.Pl, g.R.Deflection);
                    if (g.R.Deflection <= limit + 1e-9) break;
                }
                bool ok = pick.D <= limit + 1e-9;
                checks.Add(new(x.Name, x.DiameterIn, pick.Label ?? m, cu, cd, hMax, hMin, isDeep, F($"Δy/D = {pick.D:F2} %"), ok ? "OK" : $"over {limit:G3} %", ok, F($"at {pick.H:F1} ft, E′ {e:G4} psi")));
                flexRows.Add([x.Name, pick.Label, x.DiameterIn, R(od, 3), R(pick.H), pick.Ps, e, R(pick.Pe), R(pick.Pl), R(pick.D), limit, ok ? "OK" : "EXCEEDS"]);
                flexIn.Add(F($"{x.Name}\t{(m == "PVC" ? "PVC" : "HDPE")}\t{x.DiameterIn:G4}\t{od:G5}\t{pick.H:F2}\t{pick.Ps:G4}\t{soil}\t{comp}\t{(eGiven is { } eg ? eg.ToString(CultureInfo.InvariantCulture) : "")}\t{limit:G3}"));
                if (!ok) msgs.Add(CalcMessage.Warning("W-SD-001", F($"{x.Name}: {pick.Label} deflects {pick.D:F1} % at {pick.H:F1} ft cover, over the {limit:G3} % limit (Ten States §33.85); use stiffer pipe (PS 115 or C900/C905), crushed rock embedment compacted to over 95 %, or reinforced concrete.")));
                else if (pick.Ps > options[0].Ps) msgs.Add(CalcMessage.Info("I-SD-001", F($"{x.Name}: {options[0].Label} would deflect over {limit:G3} % at {hMax:F1} ft; {pick.Label} is needed ({pick.D:F2} %).")));
            }
            if (hMin < 1) msgs.Add(CalcMessage.Warning("W-SD-003", F($"{x.Name}: cover {hMin:F2} ft over the outside of the pipe; AASHTO and most agencies want at least 1 ft under traffic.")));
        }
        if (noCover > 0) msgs.Add(CalcMessage.Warning("W-SD-006", $"{noCover} sewer(s) have no rim at either manhole and were not checked; give the rims under [Manholes]."));
        if (checks.Any(c => c.Hmax > 30)) msgs.Add(CalcMessage.Info("I-SD-002", "Some sewers are more than 30 ft deep. Howard's E′ values do not rise with depth; for very deep flexible pipe check the AASHTO LRFD 12.12.3.5 constrained soil modulus Ms, wall buckling and strain with the manufacturer."));
        if (dr.Messages.Any(m => m.Severity == MessageSeverity.Warning))
            msgs.Add(CalcMessage.Info("I-SD-005", $"The sanitary design has {dr.Messages.Count(m => m.Severity == MessageSeverity.Warning)} hydraulic warning(s) (velocity, d/D, cover, drops); run \"Sanitary sewer network design\" with the same input to see them."));
        msgs.Add(CalcMessage.Info("I-SD-003", "Cover is measured from the rim at each manhole to the outside top of the pipe; the ground between manholes is taken as the straight line between the rims. Earth load uses the deeper end, live load the shallower end, and the worse of the two governs."));

        var deepOnes = checks.Where(c => c.Deep).ToList();
        var summary = new List<string>
        {
            F($"{checks.Count} sewer(s) checked at their designed cover ({design.Criteria} design); {deepOnes.Count} at {deep:G3} ft or deeper."),
            checks.All(c => c.Ok) ? "Every sewer meets its structural check." : $"{checks.Count(c => !c.Ok)} sewer(s) fail their structural check; see Messages.",
        };
        foreach (var c in deepOnes) summary.Add(F($"{c.Pipe} ({c.D:G3} in., {c.Hmax:F1} ft cover): {c.Material}, {c.Result} — {c.Status}."));
        var tables = new List<ToolTable>
        {
            new("Sewers at their designed depths", ["Sewer", "D (in)", "Pipe", "Cover up (ft)", "Cover down (ft)", "Deep", "Result", "Governing", "Check"],
                checks.Select(c => (IReadOnlyList<object?>)new object?[] { c.Pipe, c.D, c.Material, R(c.CovUp), R(c.CovDn), c.Deep ? "yes" : "", c.Result, c.Basis, c.Status }).ToList(),
                [F($"Flexible pipe: Modified Iowa Δy/D = (DL·K·Pe + K·PL)/(0.149·PS + 0.061·E′), K = 0.1, DL = {dl:G3}, limit {limit:G3} % (Ten States §33.85). PVC tries SDR 35 / PS 46 first, then SDR 26 / PS 115."),
                 "Reinforced concrete: AASHTO LRFD §12.10 / ACPA indirect design, ASTM C76 wall B (Rinker precast standard wall) on the chosen Standard Installation; BfLL 1.1 unless given."]),
        };
        if (rigidRows.Count > 0)
            tables.Add(new("Reinforced concrete pipe: indirect design", ["Sewer", "ID (in)", "Wall (in)", "Type", "Cover (ft)", "WE (lb/ft)", "WF (lb/ft)", "WL (lb/ft)", "Bfe", "BfLL", "D0.01 (lb/ft/ft)", "ASTM C76"], rigidRows));
        if (flexRows.Count > 0)
            tables.Add(new("Flexible pipe: Modified Iowa deflection", ["Sewer", "Pipe", "ID (in)", "OD (in)", "Cover (ft)", "PS (psi)", "E′ (psi)", "Pe (psi)", "PL (psi)", "Δy/D (%)", "Limit (%)", "Check"], flexRows,
                [eGiven is null ? $"E′ from Howard (1977): {soil}, {comp} compaction." : "E′ as given."]));

        // The same pipes as input for the Pipe structural design tool, so they can be adjusted there.
        var sb = new StringBuilder();
        sb.AppendLine("# Pipe structural design input written by the sanitary sewer structural check (governing cover of each sewer).");
        sb.AppendLine(F($"unit weight = {gamma:G4}")).AppendLine($"live load = {(live ? "HL-93" : "none")}").AppendLine(F($"LLDF = {lldf:G3}")).AppendLine(F($"DL = {dl:G3}"));
        if (rigidIn.Count > 0) { sb.AppendLine("[Rigid]").AppendLine("Name\tID\tCover\tInstallation\tWall\tBfLL"); foreach (var l in rigidIn) sb.AppendLine(l); }
        if (flexIn.Count > 0) { sb.AppendLine("[Flexible]").AppendLine("Name\tMaterial\tID\tOD\tCover\tPS\tSoil\tCompaction\tE′\tLimit (%)"); foreach (var l in flexIn) sb.AppendLine(l); }
        msgs.Add(CalcMessage.Info("I-SD-004", "The pipes are also written as input for the \"Pipe structural design\" tool (file pipe-structural input.txt)."));
        return new ToolResult(title, summary, tables, msgs)
        {
            Files = [new ToolFile("pipe-structural input.txt", "Input for the Pipe structural design tool", Encoding.UTF8.GetBytes(sb.ToString()))],
        };
    }

    private static double? R(double? v, int d = 2) => v is { } x && double.IsFinite(x) ? Math.Round(x, d) + 0.0 : null;
    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
