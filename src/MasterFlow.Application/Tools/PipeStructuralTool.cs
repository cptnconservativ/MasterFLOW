using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Structural check of buried storm pipe. Rigid (reinforced concrete) pipe by the indirect design method of AASHTO LRFD
/// §12.10 and the ACPA Concrete Pipe Design Manual: earth load on a Standard Installation (prism load × vertical arching
/// factor), fluid load and AASHTO live load, divided by the bedding factors, gives the required 0.01-in. crack D-load and
/// the ASTM C76 class. Flexible pipe (HDPE, PVC, CMP) by the Modified Iowa formula (Spangler 1941, Watkins and Spangler
/// 1958) with Howard's (1977) E′ values: long-term deflection against the allowable.
/// </summary>
public sealed class PipeStructuralTool : IDesignTool
{
    public string Id => "pipe-structural";
    public string Name => "Pipe structural design: RCP D-load class and flexible pipe deflection";
    public string Description => "Rigid pipe: D0.01 = [(WE + WF)/Bfe + WL/BfLL]/Di with WE = VAF·w·[H + Do(4 − π)/8]·Do for AASHTO/ACPA Standard Installations Type 1–4, AASHTO LRFD live load (16-kip wheel, impact, multiple presence, LLDF spread), and the ASTM C76 class. Flexible pipe: Modified Iowa deflection Δ/D = (DL·K·Pe + K·PL)/(0.149·PS + 0.061·E′) with Howard (1977) E′ by embedment soil and compaction.";
    public bool UsesNetwork => false;

    /// <summary>Vertical arching factor for AASHTO/ACPA Standard Installations Type 1–4 (AASHTO LRFD Table 12.10.2.1-2).</summary>
    public static double Vaf(int type) => type switch { 1 => 1.35, 2 => 1.40, 3 => 1.40, 4 => 1.45, _ => double.NaN };

    private static readonly double[] BfeDiam = [12, 24, 36, 72, 144];
    private static readonly double[][] BfeTable =
    [
        [4.4, 4.2, 4.0, 3.8, 3.6],   // Type 1
        [3.2, 3.0, 2.9, 2.8, 2.8],   // Type 2
        [2.5, 2.4, 2.3, 2.2, 2.2],   // Type 3
        [1.7, 1.7, 1.7, 1.7, 1.7],   // Type 4
    ];

    /// <summary>Earth-load bedding factor Bfe for a Standard Installation (ACPA Design Manual), interpolated on the inside diameter (in.).</summary>
    public static double Bfe(int type, double diIn)
    {
        if (type is < 1 or > 4) return double.NaN;
        var t = BfeTable[type - 1];
        if (diIn <= BfeDiam[0]) return t[0];
        for (int i = 1; i < BfeDiam.Length; i++)
            if (diIn <= BfeDiam[i]) return t[i - 1] + (t[i] - t[i - 1]) * (diIn - BfeDiam[i - 1]) / (BfeDiam[i] - BfeDiam[i - 1]);
        return t[^1];
    }

    /// <summary>ASTM C76 classes by 0.01-in. crack D-load (lb/ft/ft) and the SI 0.3-mm crack D-load (N/m/mm) of C76M.</summary>
    public static readonly (string Class, double Us, double Si)[] Classes = [("I", 800, 40), ("II", 1000, 50), ("III", 1350, 65), ("IV", 2000, 100), ("V", 3000, 140)];

    /// <summary>
    /// AASHTO LRFD live load at the top of a pipe under <paramref name="hFt"/> of cover, pipe axis across the traffic: a
    /// 16-kip dual wheel (20 × 10 in. contact), dynamic allowance IM = 33(1 − 0.125H) %, multiple presence 1.2, spread
    /// LLDF·H each way; the two wheels of an axle (6 ft apart) combine once their spread areas overlap. Returns the
    /// pressure (psf) and the line load on the pipe WL = w·L·min(a, Bc)/Le with Le = L + 1.75·(3/4)·Bc (ACPA), lb/ft.
    /// </summary>
    public static (double Pressure, double LineLoad) LiveLoad(double hFt, double bcFt, double lldf)
    {
        if (hFt <= 0) return (double.NaN, double.NaN);
        double im = Math.Max(0, 0.33 * (1 - 0.125 * hFt));
        double a = 10.0 / 12 + lldf * hFt, b = 20.0 / 12 + lldf * hFt, load = 16000;
        if (b > 6) { b += 6; load = 32000; }
        double w = load * (1 + im) * 1.2 / (a * b);
        double le = b + 1.75 * 0.75 * bcFt;
        return (w, w * b * Math.Min(a, bcFt) / le);
    }

    /// <summary>Howard (1977) modulus of soil reaction E′ (psi) by embedment soil and compaction; NaN when not tabulated.</summary>
    public static double EPrime(string soil, string compaction)
    {
        int c = compaction.Trim().ToLowerInvariant() switch
        {
            "dumped" => 0, "slight" or "<85" => 1, "moderate" or "85-95" => 2, "high" or ">95" => 3, _ => -1,
        };
        double[]? row = soil.Trim().ToLowerInvariant() switch
        {
            "crushed rock" or "crushed" or "rock" => [1000, 3000, 3000, 3000],
            "gravel" or "sand" or "coarse" or "gw" or "gp" or "sw" or "sp" => [200, 1000, 2000, 3000],
            "silty sand" or "clayey sand" or "coarse with fines" or "gm" or "gc" or "sm" or "sc" => [100, 400, 1000, 2000],
            "silt" or "lean clay" or "fine" or "ml" or "cl" => [50, 200, 400, 1000],
            "fat clay" or "elastic silt" or "ch" or "mh" => [0, 0, 0, 0],
            _ => null,
        };
        return row is null || c < 0 ? double.NaN : row[c];
    }

    /// <summary>Live load on the pipe (pressure psf, line load lb/ft), zero when there is none or when AASHTO LRFD 3.6.1.2.6
    /// lets it be neglected (cover over 8 ft and over the pipe OD).</summary>
    public static (double Pressure, double LineLoad) LiveLoadOrNone(double hFt, double bcFt, bool live, double lldf)
        => !live || (hFt > 8 && hFt > bcFt) ? (0, 0) : LiveLoad(hFt, bcFt, lldf);

    /// <summary>
    /// Indirect design of reinforced concrete pipe (U.S. units): earth, fluid and live loads (lb/ft), bedding factors and
    /// the 0.01-in. crack D-load (lb/ft/ft) for ID <paramref name="diIn"/>, wall <paramref name="wallIn"/> and cover
    /// <paramref name="hFt"/> over the top of pipe on Standard Installation <paramref name="type"/>.
    /// </summary>
    public static (double We, double Wf, double Wl, double Bfe, double BfLL, double DLoad) RigidDLoad(double diIn, double wallIn, double hFt, int type, double? bfLL, double gammaPcf, bool live, double lldf)
    {
        double doFt = (diIn + 2 * wallIn) / 12, diFt = diIn / 12;
        double we = Vaf(type) * gammaPcf * (hFt + doFt * (4 - Math.PI) / 8) * doFt;
        double wf = 62.4 * Math.PI * diFt * diFt / 4;
        var (_, wl) = LiveLoadOrNone(hFt, doFt, live, lldf);
        double bfe = Bfe(type, diIn), bfll = Math.Min(bfe, bfLL ?? 1.1);
        return (we, wf, wl, bfe, bfll, ((we + wf) / bfe + wl / bfll) / diFt);
    }

    /// <summary>Least ASTM C76 class (U.S. D-load) at or above <paramref name="dLoad"/> lb/ft/ft, or null above Class V.</summary>
    public static string? C76Class(double dLoad) => Classes.FirstOrDefault(c => c.Us >= dLoad - 1e-9).Class;

    /// <summary>
    /// Modified Iowa long-term deflection (%) of flexible pipe (U.S. units): prism earth pressure Pe and live-load pressure
    /// PL at the crown (psi), Δy/D = (DL·K·Pe + K·PL)/(0.149·PS + 0.061·E′) with K = 0.1.
    /// </summary>
    public static (double Pe, double Pl, double Deflection) FlexibleDeflection(double hFt, double odIn, double psPsi, double ePrimePsi, double gammaPcf, bool live, double lldf, double dl)
    {
        double pe = gammaPcf * hFt / 144;
        var (w, _) = LiveLoadOrNone(hFt, odIn / 12, live, lldf);
        double pl = double.IsFinite(w) ? w / 144 : 0;
        return (pe, pl, 100 * (dl * 0.1 * pe + 0.1 * pl) / (0.149 * psPsi + 0.061 * ePrimePsi));
    }

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Pipe structural design. SI: ID in mm, cover in m, unit weight kN/m³, PS and E′ in kPa.
        unit weight = 18.85        # soil over the pipe, kN/m³
        live load = HL-93          # HL-93 (AASHTO design truck wheel) or none
        LLDF = 1.15                # 1.15 select granular fill, 1.0 other soils (AASHTO LRFD 3.6.1.2.6)
        [Rigid]
        Name	ID	Cover	Installation	Wall	BfLL
        Under street	600	0.9	2
        Deep trunk	1200	6.0	2
        [Flexible]
        Name	Material	ID	OD	Cover	PS	Soil	Compaction	E′	Limit (%)
        HDPE lateral	HDPE	450	530	1.2	276	gravel	moderate
        PVC SDR 35	PVC	375	400	3.0	317	silty sand	high
        """ : """
        # Pipe structural design. U.S.: ID and wall in inches, cover in ft over the top of pipe, unit weight in pcf, PS and E′ in psi.
        unit weight = 120          # soil over the pipe, pcf
        live load = HL-93          # HL-93 (AASHTO design truck wheel) or none
        LLDF = 1.15                # 1.15 select granular fill, 1.0 other soils (AASHTO LRFD 3.6.1.2.6)
        # DL = 1.0                 # deflection lag factor on the prism earth load (1.0 with prism load; 1.5 is Spangler's original)

        # Reinforced concrete pipe. Installation: AASHTO/ACPA Standard Installation Type 1–4 (Type 2 is common for storm sewer).
        # Wall blank = ASTM C76 wall B (ID/12 + 1 in.). BfLL: from the ACPA HS20 live-load bedding factor table; blank uses 1.1 (conservative).
        [Rigid]
        Name	ID	Cover	Installation	Wall	BfLL
        Under street	24	3	2
        Deep trunk	48	20	2

        # Flexible pipe. PS = pipe stiffness at 5 % deflection (ASTM D2412; AASHTO M294 / ASTM F2306 HDPE, ASTM D3034 PVC SDR 35 = 46 psi).
        # Soil: crushed rock, gravel/sand (little fines), silty sand/clayey sand (> 12 % fines), silt/lean clay, fat clay. Compaction: dumped, slight, moderate, high.
        [Flexible]
        Name	Material	ID	OD	Cover	PS	Soil	Compaction	E′	Limit (%)
        HDPE lateral	HDPE	18	21	4	40	gravel	moderate
        PVC SDR 35	PVC	15	16	10	46	silty sand	high
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Pipe structural design";
        bool si = context.Units == UnitSystem.SI;
        // Everything is computed in U.S. units; SI input is converted on entry and the D-load converted back.
        double inPer = si ? 1 / 25.4 : 1, ftPer = si ? 1 / 0.3048 : 1, psiPer = si ? 1 / 6.894757 : 1;
        string Ul = si ? "m" : "ft", Ud = si ? "mm" : "in.", Up = si ? "kPa" : "psi", Dl = si ? "N/m/mm" : "lb/ft/ft";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double gamma = p.Number("unit weight", si ? 18.85 : 120, "unit weight") * (si ? 1000 / 157.087 : 1);  // kN/m³ → pcf
        string ll = (p.Text("live load") ?? "HL-93").Trim().ToLowerInvariant();
        double lldf = p.Number("LLDF", 1.15, "LLDF"), dl = p.Number("DL", 1.0, "DL");
        bool live = ll is "hl-93" or "hl93" or "hs-20" or "hs20" or "yes";
        if (!live && ll is not ("none" or "no")) msgs.Add(CalcMessage.Error("E-PST-001", $"live load \"{ll}\" is not HL-93 or none."));
        if (!(gamma > 0) || !(lldf >= 1) || !(dl >= 1)) msgs.Add(CalcMessage.Error("E-PST-002", "unit weight must be above zero; LLDF and DL must be 1.0 or more."));

        var rigid = new List<(string Name, double Di, double H, int Type, double T, double? BfLL)>();
        foreach (var r in p.Rows("Rigid"))
        {
            string n = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Rigid {rigid.Count + 1}";
            double? di = p.Cell(r, 1, "ID", n), h = p.Cell(r, 2, "cover", n), ty = p.Cell(r, 3, "installation", n), t = p.Cell(r, 4, "wall", n), bf = p.Cell(r, 5, "BfLL", n);
            if (di is not > 0 || h is not > 0) { msgs.Add(CalcMessage.Error("E-PST-003", $"{n}: give the ID and the cover over the top of the pipe, both above zero.")); continue; }
            if (ty is not (1 or 2 or 3 or 4)) { msgs.Add(CalcMessage.Error("E-PST-004", $"{n}: installation must be Standard Installation Type 1, 2, 3 or 4.")); continue; }
            if (t is <= 0 || bf is <= 0) { msgs.Add(CalcMessage.Error("E-PST-005", $"{n}: wall and BfLL must be above zero when given.")); continue; }
            double diIn = di.Value * inPer;
            rigid.Add((n, diIn, h.Value * ftPer, (int)ty.Value, t is { } tt ? tt * inPer : diIn / 12 + 1, bf));
        }
        var flex = new List<(string Name, string Mat, double Di, double Do, double H, double Ps, double E, string Esrc, double Lim)>();
        foreach (var r in p.Rows("Flexible"))
        {
            string n = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Flexible {flex.Count + 1}";
            string mat = (r.Length > 1 ? r[1] : "").Trim().ToUpperInvariant();
            double? di = p.Cell(r, 2, "ID", n), od = p.Cell(r, 3, "OD", n), h = p.Cell(r, 4, "cover", n), ps = p.Cell(r, 5, "PS", n), ep = p.Cell(r, 8, "E′", n), lim = p.Cell(r, 9, "limit", n);
            string soil = r.Length > 6 ? r[6] : "", comp = r.Length > 7 ? r[7] : "";
            if (mat is not ("HDPE" or "PVC" or "CMP" or "STEEL" or "ALUMINUM" or "PP")) { msgs.Add(CalcMessage.Error("E-PST-006", $"{n}: material \"{mat}\" is not HDPE, PP, PVC, CMP, steel or aluminum.")); continue; }
            if (di is not > 0 || h is not > 0 || ps is not > 0) { msgs.Add(CalcMessage.Error("E-PST-007", $"{n}: give the ID, cover and pipe stiffness PS, all above zero.")); continue; }
            double e = ep is { } x ? x * psiPer : EPrime(soil, comp);
            if (!(e >= 0)) { msgs.Add(CalcMessage.Error("E-PST-008", $"{n}: soil \"{soil}\" / compaction \"{comp}\" is not in Howard's table; give E′ directly.")); continue; }
            if (od is { } o && o < di) { msgs.Add(CalcMessage.Error("E-PST-009", $"{n}: OD is smaller than ID.")); continue; }
            double diIn = di.Value * inPer;
            flex.Add((n, mat, diIn, od is { } oo ? oo * inPer : diIn * 1.1, h.Value * ftPer, ps.Value * psiPer, e, ep is null ? $"Howard: {soil}, {comp}" : "given", lim ?? 5));
            if (od is null) msgs.Add(CalcMessage.Info("I-PST-001", $"{n}: OD not given; taken as 1.1 × ID for the live-load width."));
        }
        if (rigid.Count + flex.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error))
            msgs.Add(CalcMessage.Error("E-PST-010", "Give at least one pipe under [Rigid] or [Flexible]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var summary = new List<string>();
        var tables = new List<ToolTable>();
        double Len(double ft) => si ? ft * 0.3048 : ft;
        (double W, double Wl) Live(double h, double bc, string n)
        {
            if (!live) return (0, 0);
            if (h > 8 && h > bc) return (0, 0);                               // AASHTO LRFD 3.6.1.2.6: live load may be neglected
            if (h < 2) msgs.Add(CalcMessage.Warning("W-PST-001", $"{n}: cover under 2 ft; AASHTO LRFD treats the wheel load as on a slab (3.6.1.3.3). The spread used here is an approximation."));
            return LiveLoad(h, bc, lldf);
        }
        if (rigid.Count > 0)
        {
            var rows = new List<IReadOnlyList<object?>>();
            foreach (var x in rigid)
            {
                double doFt = (x.Di + 2 * x.T) / 12, diFt = x.Di / 12;
                double we = Vaf(x.Type) * gamma * (x.H + doFt * (4 - Math.PI) / 8) * doFt;
                double wf = 62.4 * Math.PI * diFt * diFt / 4;
                var (wPsf, wl) = Live(x.H, doFt, x.Name);
                double bfe = Bfe(x.Type, x.Di), bfll = Math.Min(bfe, x.BfLL ?? 1.1);
                double dload = ((we + wf) / bfe + wl / bfll) / diFt;
                double dOut = si ? dload * 0.0478803 : dload;
                var cls = Classes.FirstOrDefault(c => (si ? c.Si : c.Us) >= dOut - 1e-9);
                string cn = cls.Class is null ? "special design" : $"Class {cls.Class}";
                if (x.H < 1) msgs.Add(CalcMessage.Warning("W-PST-002", $"{x.Name}: cover under 1 ft; most agencies require 1 ft over RCP under pavement or a concrete cap."));
                if (x.BfLL is null && wl > 0) msgs.Add(CalcMessage.Info("I-PST-002", $"{x.Name}: BfLL not given; 1.1 used (low end of the ACPA HS20 table). Enter the table value for {x.Di:F0} in. at {x.H:F1} ft cover for a less conservative D-load."));
                if (cls.Class is null) msgs.Add(CalcMessage.Warning("W-PST-003", $"{x.Name}: D-load {dOut:F0} {Dl} is above Class V; use a special design (direct design, AASHTO LRFD 12.10.4.2) or a better installation."));
                rows.Add([x.Name, R(x.Di / inPer, 0), R(Len(x.H), 2), x.Type, R(x.T / inPer, 2), Vaf(x.Type), R(we * (si ? 0.0145939 : 1), 0), R(wf * (si ? 0.0145939 : 1), 0), R(wl * (si ? 0.0145939 : 1), 0), R(bfe, 2), R(bfll, 2), R(dOut, si ? 1 : 0), cn]);
                summary.Add($"{x.Name}: D0.01 = {dOut:F0} {Dl} → {cn} ({x.Di / inPer:F0} {Ud}, {Len(x.H):F1} {Ul} cover, Type {x.Type}).");
            }
            string F = si ? "kN/m" : "lb/ft";
            tables.Add(new("Reinforced concrete pipe: indirect design (AASHTO LRFD §12.10, ACPA)", ["Pipe", $"ID ({Ud})", $"Cover ({Ul})", "Type", $"Wall ({Ud})", "VAF", $"WE ({F})", $"WF ({F})", $"WL ({F})", "Bfe", "BfLL", $"D0.01 ({Dl})", "ASTM C76"], rows,
                [$"WE = VAF·w·[H + Do(4 − π)/8]·Do, w = {gamma:F1} pcf; WF = 62.4·π·Di²/4 (pipe full); D0.01 = [(WE + WF)/Bfe + WL/BfLL]/Di with Di in ft, F.S. = 1.0 for the 0.01-in. crack.",
                 "Bfe interpolated on the diameter from the ACPA Standard Installation bedding factor table; BfLL is never taken above Bfe.",
                 live ? $"Live load: 16-kip wheel (20 × 10 in.), IM = 33(1 − 0.125H) %, m = 1.2, LLDF = {lldf:G3}; the axle's two wheels combine when their spread areas overlap; neglected above 8 ft of cover when the cover exceeds the pipe OD." : "No live load."]));
        }
        if (flex.Count > 0)
        {
            var rows = new List<IReadOnlyList<object?>>();
            foreach (var x in flex)
            {
                double pe = gamma * x.H / 144;                                       // prism earth pressure at the crown, psi
                var (wPsf, _) = Live(x.H, x.Do / 12, x.Name);
                double pl = live && double.IsFinite(wPsf) ? wPsf / 144 : 0;
                double defl = 100 * (dl * 0.1 * pe + 0.1 * pl) / (0.149 * x.Ps + 0.061 * x.E);
                bool ok = defl <= x.Lim;
                double minCover = Math.Max(1, x.Di / 8 / 12);
                if (x.H < minCover) msgs.Add(CalcMessage.Warning("W-PST-004", $"{x.Name}: cover {Len(x.H):F2} {Ul} is under the usual minimum for flexible pipe under traffic (the larger of 1 ft and ID/8, AASHTO LRFD 12.6.6.3)."));
                if (!ok) msgs.Add(CalcMessage.Warning("W-PST-005", $"{x.Name}: predicted deflection {defl:F1} % exceeds the {x.Lim:G3} % limit; use stiffer pipe, better embedment or more compaction."));
                if (x.E == 0) msgs.Add(CalcMessage.Warning("W-PST-006", $"{x.Name}: E′ = 0 (fat clay or elastic silt embedment); Howard gives no support for these soils. Replace the embedment."));
                rows.Add([x.Name, x.Mat, R(x.Di / inPer, 0), R(Len(x.H), 2), R(x.Ps / psiPer, 0), R(x.E / psiPer, 0), x.Esrc, R(pe / psiPer, 2), R(pl / psiPer, 2), R(defl, 2), x.Lim, ok ? "OK" : "EXCEEDS"]);
                summary.Add($"{x.Name}: long-term deflection {defl:F1} % ({(ok ? "within" : "over")} {x.Lim:G3} %).");
            }
            tables.Add(new("Flexible pipe: Modified Iowa deflection", ["Pipe", "Material", $"ID ({Ud})", $"Cover ({Ul})", $"PS ({Up})", $"E′ ({Up})", "E′ source", $"Pe ({Up})", $"PL ({Up})", "Δy/D (%)", "Limit (%)", "Check"], rows,
                [$"Δy/D = (DL·K·Pe + K·PL)/(0.149·PS + 0.061·E′), K = 0.1, DL = {dl:G3} on the prism earth pressure Pe = w·H; PL = the live-load pressure at the crown.",
                 "Limit: 5 % unless given (AASHTO LRFD and most agency specifications for thermoplastic pipe; ASTM D2321 allows 7.5 % long-term). Buckling, wall stress and strain are not checked."]));
        }
        msgs.Add(CalcMessage.Info("I-PST-003", "Pipe axis is taken across the traffic (the usual worst case for a single axle). Check construction equipment loads separately."));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
