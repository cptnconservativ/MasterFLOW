using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Reach-by-reach sulfide build-up along a long sanitary sewer run (gravity reaches by Pomeroy and Parkhurst 1977,
/// full-flowing reaches and force mains by Pomeroy's full-pipe equation, as given in EPA 625/1-85/018 and ASCE MOP 69),
/// converted to the H2S flux to the exposed pipe wall and a concrete corrosion rate (Pomeroy 1976, EPA 1985), and a
/// material or lining recommendation for the Rapid City IDCM §3.12.1 75-year design life (§3.12.8 item 2).
/// </summary>
public sealed class SewerSulfideTool : IDesignTool
{
    public string Id => "sewer-sulfide";
    public string Name => "Sewer sulfide build-up and corrosion";
    public string Description => "Sulfide build-up reach by reach along a long sanitary sewer or force main (Pomeroy–Parkhurst for gravity reaches, Pomeroy full-pipe equation for force mains and surcharged reaches; EPA 1985 Odor and Corrosion Control manual, ASCE MOP 69), the H2S flux to the exposed wall, the concrete corrosion rate 11.5·k·φ/A, and a material or lining recommendation for the IDCM 75-year design life.";
    public bool UsesNetwork => false;

    private const double GpmPerCfs = 448.831, FtToM = 0.3048, MmPerIn = 25.4;

    /// <summary>Effective BOD (mg/L): BOD5·1.07^(T−20), T in °C.</summary>
    public static double Ebod(double bod, double tempC) => bod * Math.Pow(1.07, tempC - 20);

    /// <summary>First ionization constant pK1 of H2S at <paramref name="tempC"/> °C: 6.98 at 25 °C rising about 0.017 per °C cooler (fitted to tabulated values 7.24 at 10 °C, 7.06 at 20 °C).</summary>
    public static double PK1(double tempC) => 6.98 + 0.017 * (25 - tempC);

    /// <summary>Fraction j of dissolved sulfide present as molecular H2S at the given pH.</summary>
    public static double H2SFraction(double pH, double pK1) => 1 / (1 + Math.Pow(10, pH - pK1));

    /// <summary>
    /// Pomeroy–Parkhurst gravity reach (SI): d[S]/dt = M'·EBOD/r − m·(s·u)^(3/8)·[S]/dm. Returns the limiting sulfide
    /// and the concentration after <paramref name="hours"/>, solved exactly for constant hydraulics.
    /// r and dm in m, u in m/s, s m/m, M' in m/h.
    /// </summary>
    public static (double Limit, double Out) Gravity(double sIn, double ebod, double r, double dm, double s, double u, double mPrime, double m, double hours)
    {
        double gen = mPrime * ebod / r;                       // mg/L/h
        double loss = m * Math.Pow(s * u, 3.0 / 8.0) / dm;     // 1/h
        double lim = gen / loss;
        return (lim, lim + (sIn - lim) * Math.Exp(-loss * hours));
    }

    /// <summary>Pomeroy full-pipe (force main) build-up: ΔS = M·t·EBOD·(4/d + 1.57), d in m, t in h, M in m/h.</summary>
    public static double FullPipe(double sIn, double ebod, double dM, double mFull, double hours) => sIn + mFull * hours * ebod * (4 / dM + 1.57);

    /// <summary>H2S flux to the exposed wall (g/m²·h): 0.69·(s·u)^(3/8)·j·[DS]·b/P′ (SI).</summary>
    public static double WallFlux(double s, double u, double j, double ds, double b, double pExposed) => pExposed <= 0 ? 0 : 0.69 * Math.Pow(s * u, 3.0 / 8.0) * j * ds * b / pExposed;

    /// <summary>Concrete corrosion rate (mm/yr): 11.5·k·φsw/A.</summary>
    public static double CorrosionRate(double k, double flux, double alkalinity) => 11.5 * k * flux / alkalinity;

    private enum Kind { Gravity, ForceMain }

    private static bool Resistant(string mat) => mat.Length > 0 && new[] { "pvc", "hdpe", "pe", "pp", "polypropylene", "polyethylene", "vcp", "clay", "frp", "grp", "fiberglass", "lined", "liner", "t-lock" }
        .Any(w => mat.Contains(w, StringComparison.OrdinalIgnoreCase));

    public string SampleInput(DesignToolContext context) => """
        # Sewer sulfide build-up and corrosion. U.S. units: lengths ft, diameters in., slopes %, flows gpm.
        # List reaches in flow order, upstream first. Type: gravity or force main. Flow = average daily flow in the reach;
        # where it rises, the added flow comes in with "lateral sulfide".
        BOD = 250                   # mg/L BOD5 of the wastewater
        temperature = 22            # °C, warm-season wastewater (give "temperature F" instead for °F)
        pH = 7.2
        initial sulfide = 0         # mg/L total sulfide entering the first reach
        lateral sulfide = 0         # mg/L in flow added along the run
        dissolved fraction = 0.8    # dissolved share of total sulfide
        M gravity = 0.00032         # m/h, Pomeroy–Parkhurst M' (EPA 1985)
        m loss = 0.64               # Pomeroy–Parkhurst m; 0.96 predicts less sulfide
        k = 0.7                     # acid reaction factor, 0.3–1.0 (EPA 1985)
        alkalinity = 0.20           # concrete alkalinity as CaCO3 fraction: 0.17–0.24 siliceous aggregate, about 0.9 calcareous
        design life = 75            # years (Rapid City IDCM §3.12.1)
        allowable loss = 1.0        # in. of concrete that may be lost (cover over the reinforcing)

        [Reaches]
        Reach	Type	Length (ft)	Diameter (in)	Slope (%)	n	Flow (gpm)	Material
        FM-1	force main	4200	8		0.013	260	PVC
        T-1	gravity	1800	15	0.15	0.013	260	RCP
        T-2	gravity	2400	18	0.10	0.013	420	RCP
        T-3	gravity	2600	21	0.08	0.013	520	RCP
        T-4	gravity	2200	21	0.08	0.013	560	PVC
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sewer sulfide build-up and corrosion";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-SC-001", "The sulfide tool works in U.S. units (ft, in., gpm); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double bod = p.Number("BOD", 250);
        double tempC = p.OptionalNumber("temperature F") is { } tf ? (tf - 32) / 1.8 : p.Number("temperature", 20);
        double pH = p.Number("pH", 7.0);
        double pK1 = p.Number("pK1", PK1(tempC));
        double s0 = p.Number("initial sulfide", 0), sLat = p.Number("lateral sulfide", 0), fDs = p.Number("dissolved fraction", 0.8);
        double mPrime = p.Number("M gravity", 0.32e-3), mLoss = p.Number("m loss", 0.64);
        double? mFullIn = p.OptionalNumber("M force main");
        double k = p.Number("k", 0.7), alk = p.Number("alkalinity", 0.20), life = p.Number("design life", 75), allow = p.Number("allowable loss", 1.0);
        double calcAlk = p.Number("calcareous alkalinity", 0.9);
        double dsWarn = p.Number("sulfide warning", 0.5);

        if (!(bod > 0) || !(fDs > 0 && fDs <= 1) || !(mPrime > 0) || !(mLoss > 0) || !(k > 0 && k <= 1) || !(alk > 0 && alk <= 1.2) || !(life > 0) || !(allow > 0) || s0 < 0 || sLat < 0 || mFullIn is <= 0)
            msgs.Add(CalcMessage.Error("E-SC-002", "BOD, M, m, design life and allowable loss must be above zero; dissolved fraction and k in 0–1; alkalinity 0–1.2; sulfide zero or more."));
        if (!(pH >= 4 && pH <= 11)) msgs.Add(CalcMessage.Error("E-SC-002", "pH must be between 4 and 11."));
        if (!(tempC > 0 && tempC < 40)) msgs.Add(CalcMessage.Error("E-SC-002", "Wastewater temperature must be between 0 and 40 °C (32–104 °F)."));

        var reaches = new List<(string Name, Kind Kind, double L, double D, double S, double N, double Q, string Mat)>();
        var rows = p.Rows("Reaches");
        if (rows.Count == 0) msgs.Add(CalcMessage.Error("E-SC-003", "Give the run under [Reaches]: Reach, Type, Length (ft), Diameter (in), Slope (%), n, Flow (gpm), Material."));
        foreach (var r in rows)
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Reach {reaches.Count + 1}";
            string type = r.Length > 1 ? r[1].Trim().ToLowerInvariant() : "";
            Kind kind = type.StartsWith("f") || type.StartsWith("p") || type.Contains("siphon") ? Kind.ForceMain : Kind.Gravity;
            if (type.Length > 0 && !type.StartsWith("g") && kind == Kind.Gravity) msgs.Add(CalcMessage.Error("E-SC-003", $"{name}: type \"{r[1]}\" is not gravity or force main."));
            double? l = p.Cell(r, 2, "length", name), d = p.Cell(r, 3, "diameter", name), s = p.Cell(r, 4, "slope", name), n = p.Cell(r, 5, "n", name), q = p.Cell(r, 6, "flow", name);
            string mat = r.Length > 7 ? r[7].Trim() : "";
            if (l is not > 0 || d is not > 0 || q is not > 0) { msgs.Add(CalcMessage.Error("E-SC-004", $"{name}: length, diameter and flow must be above zero.")); continue; }
            if (kind == Kind.Gravity && (s is not > 0 || n is not > 0)) { msgs.Add(CalcMessage.Error("E-SC-004", $"{name}: a gravity reach needs a slope (%) and Manning n above zero.")); continue; }
            reaches.Add((name, kind, l.Value, d.Value, (s ?? 0) / 100, n ?? 0.013, q.Value, mat));
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double ebod = Ebod(bod, tempC), j = H2SFraction(pH, pK1);
        double allowRate = allow / life;   // in/yr
        double sCur = s0, qPrev = reaches[0].Q, sta = 0;
        var resRows = new List<IReadOnlyList<object?>>();
        var lineRows = new List<IReadOnlyList<object?>>();
        var sPts = new List<(double, double)> { (0, R(s0, 3)) };
        var limPts = new List<(double, double)>();
        var cPts = new List<(double, double)>();
        int nLine = 0, nCalc = 0;
        double worstC = 0; string worstName = "";
        foreach (var x in reaches)
        {
            // Mixing with the added flow.
            if (x.Q > qPrev + 1e-9) sCur = (sCur * qPrev + sLat * (x.Q - qPrev)) / x.Q;
            else if (x.Q < qPrev - 1e-9) msgs.Add(CalcMessage.Warning("W-SC-005", $"{x.Name}: flow {x.Q:G4} gpm is less than the reach above ({qPrev:G4}); the concentration is carried on unchanged."));
            qPrev = x.Q;
            double sIn = sCur, dFt = x.D / 12, qCfs = x.Q / GpmPerCfs, dM = dFt * FtToM;
            double hours, sOut, lim = double.NaN, uFt, yD = 1, flux = 0, rate = 0, z = double.NaN;
            bool full = x.Kind == Kind.ForceMain;
            double y = 0;
            if (!full)
            {
                y = SanitarySewerTool.NormalDepth(qCfs, dFt, x.N, x.S);
                if (!double.IsFinite(y))
                {
                    full = true;
                    msgs.Add(CalcMessage.Warning("W-SC-004", $"{x.Name}: {x.Q:G4} gpm exceeds the gravity capacity of {x.D:G3} in. at {x.S * 100:G3} %; treated as flowing full (full-pipe equation, no wall exposed)."));
                }
            }
            if (full)
            {
                double area = Math.PI * dFt * dFt / 4;
                uFt = qCfs / area;
                hours = x.L / uFt / 3600;
                double mFull = mFullIn ?? (x.D <= 6 + 1e-9 ? 0.3e-3 : 1.0e-3);
                sOut = FullPipe(sIn, ebod, dM, mFull, hours);
            }
            else
            {
                double a = SanitarySewerTool.Area(y, dFt), pw = SanitarySewerTool.Perimeter(y, dFt), b = SanitarySewerTool.TopWidth(y, dFt);
                uFt = qCfs / a; yD = y / dFt;
                hours = x.L / uFt / 3600;
                double rM = a / pw * FtToM, dmM = a / b * FtToM, uM = uFt * FtToM;
                (lim, sOut) = Gravity(sIn, ebod, rM, dmM, x.S, uM, mPrime, mLoss, hours);
                double sMax = Math.Max(sIn, sOut);
                double pExp = Math.PI * dFt - pw;
                flux = WallFlux(x.S, uM, j, fDs * sMax, b * FtToM, pExp * FtToM);
                rate = CorrosionRate(k, flux, alk) / MmPerIn;   // in/yr
                if (x.D <= 24 + 1e-9) z = SanitarySewerTool.ZValue(ebod, x.S, qCfs, pw, b, false);
                limPts.Add((sta, R(lim, 3))); limPts.Add((sta + x.L, R(lim, 3)));
                cPts.Add((sta, R(rate * 1000, 2))); cPts.Add((sta + x.L, R(rate * 1000, 2)));
            }
            sCur = sOut;
            sta += x.L;
            sPts.Add((sta, R(sOut, 3)));

            // Recommendation.
            string rec;
            double loss = rate * life;
            bool resistant = Resistant(x.Mat);
            if (full)
                rec = resistant || x.Mat.Length == 0 ? "Full pipe: no exposed wall. Protect the discharge manhole (sulfide is released there)." : "Full pipe: no exposed wall in the main; protect the discharge manhole.";
            else if (resistant)
                rec = rate > allowRate ? "Material resists H2S; line or coat the manholes on this reach." : "Material resists H2S.";
            else if (loss <= allow + 1e-12)
                rec = "Plain concrete meets the design life.";
            else if (rate * alk / calcAlk * life <= allow + 1e-12)
            {
                rec = $"Calcareous aggregate (A ≈ {calcAlk:G2}) or {loss - allow:F2} in. of added sacrificial concrete; or a plastic liner.";
                nCalc++;
            }
            else
            {
                rec = "Use PVC, HDPE or PP pipe, or RCP with a cast-in PVC/HDPE liner; line or coat the manholes.";
                nLine++;
            }
            if (!full && !resistant && loss > allow + 1e-12)
                msgs.Add(CalcMessage.Warning("W-SC-001", $"{x.Name}: concrete corrosion {rate:F3} in/yr loses {loss:F2} in. in {life:G3} years, more than the {allow:G3} in. allowed (IDCM §3.12.1 design life, §3.12.8 item 2). {rec}"));
            if (!full && rate > worstC) { worstC = rate; worstName = x.Name; }
            double dsOut = fDs * sOut;
            if (dsOut > dsWarn) msgs.Add(CalcMessage.Warning("W-SC-002", $"{x.Name}: dissolved sulfide {dsOut:F2} mg/L leaving the reach exceeds {dsWarn:G3} mg/L; expect odor and corrosion at manholes, drops and junctions downstream."));

            resRows.Add([x.Name, full ? (x.Kind == Kind.ForceMain ? "Force main" : "Gravity (full)") : "Gravity", R(sta, 0), R(x.L, 0), x.D, full ? null : R(x.S * 100, 3), R(x.Q, 0), full ? null : R(yD, 2), R(uFt, 2), R(hours * 60, 1),
                R(sIn, 3), full ? null : R(lim, 3), R(sOut, 3), R(dsOut, 3), full ? null : R(flux, 4), full ? null : R(rate, 4), full ? null : R(loss, 2), full || rate <= 0 ? null : R(allow / rate, 0), double.IsFinite(z) ? R(z, 0) : null]);
            lineRows.Add([x.Name, x.Mat.Length == 0 ? "—" : x.Mat, rec]);
        }
        if (reaches.Any(x => x.Kind == Kind.ForceMain))
            msgs.Add(CalcMessage.Info("I-SC-003", $"Force mains: Pomeroy full-pipe equation ΔS = M·t·EBOD·(4/d + 1.57), M = {(mFullIn is { } mf ? $"{mf:G3} m/h" : "1.0×10⁻³ m/h over 6 in., 0.3×10⁻³ m/h at 6 in. and smaller")}; no oxygen and no losses assumed."));
        if (reaches.Any(x => x.D > 24)) msgs.Add(CalcMessage.Info("I-SC-004", "Reaches over 24 in. are the sewers IDCM §3.12.14 item 5 sends to the ASCE MOP 60 sulfide analysis; this reach-by-reach prediction is that analysis."));
        msgs.Add(CalcMessage.Info("I-SC-001", $"Gravity reaches: Pomeroy–Parkhurst d[S]/dt = M'·EBOD/r − m·(s·u)^⅜·[S]/dm, M' = {mPrime:G3} m/h, m = {mLoss:G3}, solved exactly per reach at the average flow (normal depth). EBOD = BOD·1.07^(T−20) = {ebod:F0} mg/L at {tempC:F1} °C."));
        msgs.Add(CalcMessage.Info("I-SC-002", $"Wall: φsw = 0.69·(s·u)^⅜·j·[DS]·b/P′ with j = {j:F2} (pH {pH:G3}, pK1 {pK1:F2}) and [DS] = {fDs:G2}·[S] at the worse end of the reach; corrosion = 11.5·k·φsw/A mm/yr, k = {k:G2}, A = {alk:G2}. Turbulent release at drops, junctions and force main discharges is not modelled; those structures need their own protection."));

        var summary = new List<string>
        {
            $"EBOD {ebod:F0} mg/L at {tempC:F1} °C; H2S share of dissolved sulfide j = {j:F2} at pH {pH:G3}.",
            $"Total sulfide {s0:F2} mg/L entering, {sCur:F2} mg/L leaving the run ({sta:F0} ft, {reaches.Count} reaches).",
            worstC > 0 ? $"Worst concrete corrosion {worstC:F3} in/yr ({worstC * MmPerIn:F2} mm/yr) on {worstName}: {worstC * life:F2} in. over {life:G3} years against {allow:G3} in. allowed." : "No gravity reach has an exposed concrete wall under attack.",
            nLine + nCalc == 0 ? "No concrete reach needs lining or added protection for the design life." : $"{nLine} reach(es) need corrosion-resistant pipe or a liner; {nCalc} can use calcareous aggregate or added sacrificial concrete.",
        };
        var tables = new List<ToolTable>
        {
            new("Sulfide build-up by reach", ["Reach", "Type", "Station end (ft)", "Length (ft)", "D (in)", "Slope (%)", "Q avg (gpm)", "d/D", "V (ft/s)", "Travel (min)", "S in (mg/L)", "S limit (mg/L)", "S out (mg/L)", "DS out (mg/L)", "Wall flux (g/m²·h)", "Corrosion (in/yr)", $"Loss in {life:G3} yr (in)", "Years to allowable loss", "Z (IDCM formula)"], resRows,
                [$"S = total sulfide; DS = dissolved sulfide ({fDs:G2}·S). Corrosion and flux use the higher of S in and S out. Z is the IDCM §3.12.14 formula as printed at this reach's slope and average flow, for reference only; the IDCM screening (flattest slope, 1–105 % of peak) is in the sanitary network tool."]),
            new("Material and lining recommendation", ["Reach", "Material", "Recommendation"], lineRows,
                [$"Design life {life:G3} years (IDCM §3.12.1); allowable loss {allow:G3} in. ({allowRate:F4} in/yr). Plastic, vitrified clay, fiberglass and lined pipe are taken as resistant to H2S acid; manholes on those reaches still need lining or coating where the concrete rate would be excessive."]),
        };
        var charts = new List<ToolChart>
        {
            new("Sulfide along the run", "Station (ft)", "Total sulfide (mg/L)", [new("Total sulfide", sPts), new("Limiting sulfide (gravity)", limPts, Dashed: true)]),
        };
        if (cPts.Count > 0)
            charts.Add(new ToolChart("Concrete corrosion rate", "Station (ft)", "Corrosion (0.001 in/yr)", [new("Corrosion rate", cPts)], [new ChartLine($"Allowable for {life:G3} yr", R(allowRate * 1000, 2))]));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
