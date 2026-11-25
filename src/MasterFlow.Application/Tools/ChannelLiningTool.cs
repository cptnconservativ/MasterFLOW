using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Flexible channel lining design by FHWA HEC-15, <i>Design of Roadside Channels with Flexible Linings</i> (3rd ed., 2005):
/// normal depth with a depth-dependent n, maximum shear on the bottom, side slopes (K1) and in bends (Kb), and the lining
/// checks: riprap D50 by Shields with the HEC-15 F*/SF by particle Reynolds number and the Blodgett n, vegetation by the
/// HEC-15 grass n (α·Cn·τo^−0.4) with the effective soil shear under the cover, and manufactured linings (RECP/TRM) against
/// their tested permissible shear.
/// </summary>
public sealed class ChannelLiningTool : IDesignTool
{
    public string Id => "channel-lining";
    public string Name => "Channel and swale linings (HEC-15): riprap, grass, RECP/TRM";
    public string Description => "FHWA HEC-15 (3rd ed., 2005) flexible linings for a trapezoidal or V channel: normal depth with the lining's own n (Blodgett for riprap, α·Cn·τo^−0.4 for grass), design shear τd = γ·d·S, side shear K1·τd, bend shear Kb·τd and the protection length below a bend; riprap D50 = SF·τd/(F*·(γs − γ)) with F*, SF by Re*, and the side-slope D50 (K1/K2); grass by the effective soil shear τe = τd·(1 − Cf)·(ns/n)²; RECP and TRM against the manufacturer's permissible shear. U.S. units.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Channel and swale linings, FHWA HEC-15 (2005). U.S. units: ft, cfs, lb/ft².
        # Shape: trapezoid (B = bottom width) or V (B = 0); Z = side slope H:1V. Rc = bend radius at the centerline (blank = straight).
        # Lining: riprap (D50 in. given, or blank to size it), grass (Cn, Cf, soil), recp (tau p from the product's tests).
        # Grass: Cn = grass roughness coefficient (HEC-15 Table 4.1/4.2 by stem height and density), Cf = cover factor
        # (HEC-15 Table 4.3), soil D75 (in.) for non-cohesive soil or tau soil (lb/ft²) for cohesive soil.
        [Reaches]
        Name	Q (cfs)	S (ft/ft)	B (ft)	Z	Rc (ft)	Lining	D50 (in)	Cn	Cf	D75 (in)	tau soil	tau p	n
        Swale A grass	12	0.02	4	4		grass		0.142	0.90	0.02
        Channel B riprap	150	0.04	6	3	60	riprap
        Ditch C TRM	40	0.05	2	3		recp						6.0	0.030
        """;

    /// <summary>HEC-15 side-slope shear factor K1: 0.77 (Z &lt; 1.5), 0.066·Z + 0.67 (1.5 ≤ Z &lt; 5), 1.0 (Z ≥ 5).</summary>
    public static double K1(double z) => z < 1.5 ? 0.77 : z < 5 ? 0.066 * z + 0.67 : 1.0;

    /// <summary>HEC-15 bend shear factor Kb from Rc/T: 2.0 (≤ 2), 2.38 − 0.206·r + 0.0073·r² (2–10), 1.05 (≥ 10).</summary>
    public static double Kb(double rcOverT) => rcOverT <= 2 ? 2.0 : rcOverT < 10 ? 2.38 - 0.206 * rcOverT + 0.0073 * rcOverT * rcOverT : 1.05;

    /// <summary>Blodgett riprap n (HEC-15 Eq. 6.1): n = 0.262·da^(1/6)/(2.25 + 5.23·log10(da/D50)), da and D50 in ft (0.319 in SI; 0.262 = 0.319 × 0.3048^(1/6)).</summary>
    public static double RiprapN(double da, double d50) => 0.262 * Math.Pow(da, 1.0 / 6) / (2.25 + 5.23 * Math.Log10(da / d50));

    /// <summary>Shields parameter and safety factor by particle Reynolds number (HEC-15 Table 6.1): 0.047 / 1.0 up to 4·10⁴, 0.15 / 1.5 from 2·10⁵, linear between.</summary>
    public static (double FStar, double Sf) ShieldsAndSafety(double reStar)
    {
        if (reStar <= 4e4) return (0.047, 1.0);
        if (reStar >= 2e5) return (0.15, 1.5);
        double f = (reStar - 4e4) / (2e5 - 4e4);
        return (0.047 + f * (0.15 - 0.047), 1.0 + f * 0.5);
    }

    private const double Gamma = 62.4, GammaS = 165, Nu = 1.217e-5;

    private static (double A, double P, double T, double R) Trap(double b, double z, double y)
    {
        double a = (b + z * y) * y, p = b + 2 * y * Math.Sqrt(1 + z * z);
        return (a, p, b + 2 * z * y, a / p);
    }

    /// <summary>Normal depth with an n that depends on the depth (fixed-point on n, bisection on depth).</summary>
    public static double NormalDepth(double q, double s, double b, double z, Func<double, double> nOfY)
    {
        double Q(double y) { var t = Trap(b, z, y); return 1.486 / nOfY(y) * t.A * Math.Pow(t.R, 2.0 / 3) * Math.Sqrt(s); }
        double lo = 1e-4, hi = 1;
        while (Q(hi) < q && hi < 200) hi *= 2;
        for (int i = 0; i < 100; i++) { double m = 0.5 * (lo + hi); if (Q(m) < q) lo = m; else hi = m; }
        return hi;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Channel linings (HEC-15)";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-CL-001", "The lining tool uses HEC-15's U.S. equations (ft, cfs, lb/ft²); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double phi = p.Number("phi", 42);
        var rows = new List<IReadOnlyList<object?>>();
        var summary = new List<string>();
        var detail = new List<IReadOnlyList<object?>>();
        foreach (var r in p.Rows("Reaches"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Reach {rows.Count + 1}";
            double? q = p.Cell(r, 1, "Q", name), s = p.Cell(r, 2, "S", name), b = p.Cell(r, 3, "B", name), z = p.Cell(r, 4, "Z", name), rc = p.Cell(r, 5, "Rc", name);
            string lining = (r.Length > 6 ? r[6] : "").Trim().ToLowerInvariant();
            double? d50In = p.Cell(r, 7, "D50", name), cn = p.Cell(r, 8, "Cn", name), cf = p.Cell(r, 9, "Cf", name), d75 = p.Cell(r, 10, "D75", name),
                tauSoil = p.Cell(r, 11, "tau soil", name), tauP = p.Cell(r, 12, "tau p", name), nIn = p.Cell(r, 13, "n", name);
            if (q is not > 0 || s is not > 0 || b is not >= 0 || z is not >= 0 || (b == 0 && z == 0)) { msgs.Add(CalcMessage.Error("E-CL-002", $"{name}: give Q, S above zero, B ≥ 0 and Z ≥ 0 (not both zero).")); continue; }
            double rcv = rc ?? double.PositiveInfinity;
            double k1 = z.Value > 0 ? K1(z.Value) : 1.0;
            double y, n, tauD, tauDesign; string result, need, used;
            switch (lining)
            {
                case "riprap":
                {
                    if (s > 0.10) msgs.Add(CalcMessage.Warning("W-CL-001", $"{name}: slope above 10 %; HEC-15 §6.3 (steep slope riprap, with the Bathurst n) applies, which this tool does not compute. The mild-slope D50 below is not conservative there."));
                    // Size D50 (ft) by fixed point: depth with the Blodgett n, τd = γ·d·S, D50 = SF·τd·Kmax/(F*·(γs − γ)).
                    double d50 = d50In is > 0 ? d50In.Value / 12 : 0.5;
                    bool sizing = d50In is not > 0;
                    double kMax = 1, fs = 0.047, sf = 1, reS = 0;
                    for (int it = 0; it < 60; it++)
                    {
                        double dd = d50;
                        y = NormalDepth(q.Value, s.Value, b.Value, z.Value, yy => RiprapN(Trap(b.Value, z.Value, yy).A / Trap(b.Value, z.Value, yy).T, dd));
                        tauD = Gamma * y * s.Value;
                        var tw = Trap(b.Value, z.Value, y).T;
                        kMax = Math.Max(1, double.IsInfinity(rcv) ? 1 : Kb(rcv / tw));
                        double vStar = Math.Sqrt(32.2 * y * s.Value);
                        reS = vStar * d50 / Nu;
                        (fs, sf) = ShieldsAndSafety(reS);
                        double need50 = sf * tauD * kMax / (fs * (GammaS - Gamma));
                        if (!sizing || Math.Abs(need50 - d50) < 1e-5) { if (sizing) d50 = need50; break; }
                        d50 = 0.5 * (d50 + need50);
                    }
                    double dFinal = d50;
                    y = NormalDepth(q.Value, s.Value, b.Value, z.Value, yy => RiprapN(Trap(b.Value, z.Value, yy).A / Trap(b.Value, z.Value, yy).T, dFinal));
                    var tr = Trap(b.Value, z.Value, y);
                    n = RiprapN(tr.A / tr.T, dFinal);
                    tauD = Gamma * y * s.Value;
                    kMax = Math.Max(1, double.IsInfinity(rcv) ? 1 : Kb(rcv / tr.T));
                    tauDesign = tauD * kMax;
                    double req = sf * tauDesign / (fs * (GammaS - Gamma));
                    // Side slope: D50 side = (K1/K2)·D50 bottom (HEC-15 Eq. 6.12), K2 = √(1 − sin²θ/sin²φ).
                    double th = z.Value > 0 ? Math.Atan(1 / z.Value) : Math.PI / 2;
                    double k2 = Math.Sqrt(Math.Max(0, 1 - Math.Pow(Math.Sin(th) / Math.Sin(phi * Math.PI / 180), 2)));
                    double reqSide = k2 > 0 ? req * k1 / k2 : double.PositiveInfinity;
                    double governs = Math.Max(req, reqSide);
                    need = $"D50 ≥ {governs * 12:F1} in. (bottom {req * 12:F1}, side {reqSide * 12:F1})";
                    used = sizing ? "sized" : $"D50 {d50In:G3} in.";
                    result = sizing ? "SIZED" : d50In!.Value / 12 >= governs - 1e-6 ? "OK" : "FAILS";
                    if (k2 == 0) msgs.Add(CalcMessage.Error("E-CL-003", $"{name}: side slope as steep as the angle of repose; riprap cannot stand."));
                    double daD = tr.A / tr.T / dFinal;
                    if (daD < 1.5) msgs.Add(CalcMessage.Warning("W-CL-002", $"{name}: da/D50 = {daD:F2} is below 1.5, outside the Blodgett n; HEC-15 uses the Bathurst n for such shallow flow over large stone."));
                    detail.Add([name, "F* (Shields) / SF by Re*", $"{fs:F3} / {sf:F2} (Re* = {reS:E2})"]);
                    detail.Add([name, "K1 side / K2 stability", $"{k1:F3} / {k2:F3}"]);
                    if (sizing) summary.Add($"{name}: riprap D50 ≥ {governs * 12:F1} in.");
                    break;
                }
                case "grass" or "vegetation" or "vegetative":
                {
                    if (cn is not > 0 || cf is not (>= 0 and < 1)) { msgs.Add(CalcMessage.Error("E-CL-004", $"{name}: grass needs Cn > 0 and the cover factor Cf (0 to under 1) from HEC-15 Tables 4.1–4.3.")); continue; }
                    // n = α·Cn·τo^−0.4 (α = 0.213 U.S.), τo = γ·R·S (mean boundary shear).
                    y = NormalDepth(q.Value, s.Value, b.Value, z.Value, yy => 0.213 * cn.Value * Math.Pow(Gamma * Trap(b.Value, z.Value, yy).R * s.Value, -0.4));
                    var tr = Trap(b.Value, z.Value, y);
                    n = 0.213 * cn.Value * Math.Pow(Gamma * tr.R * s.Value, -0.4);
                    tauD = Gamma * y * s.Value;
                    double kMax = double.IsInfinity(rcv) ? 1 : Kb(rcv / tr.T);
                    tauDesign = tauD * Math.Max(1, kMax);
                    double ns = d75 is > 0.05 ? 0.015 * Math.Pow(d75.Value, 1.0 / 6) : 0.016;
                    double tps = tauSoil ?? (d75 is > 0.05 ? 0.4 * d75.Value : 0.02);
                    if (tauSoil is null && d75 is null) msgs.Add(CalcMessage.Warning("W-CL-003", $"{name}: no soil D75 or tau soil; the fine non-cohesive minimum 0.02 lb/ft² is used. For cohesive soil give tau soil from HEC-15 Eq. 4.6."));
                    double tauE = tauDesign * (1 - cf.Value) * Math.Pow(ns / n, 2);
                    double tp = tps / (1 - cf.Value) * Math.Pow(n / ns, 2);
                    need = $"τp {tp:F2} lb/ft² (soil {tps:F3}, τe {tauE:F3})";
                    used = $"Cn {cn:G3}, Cf {cf:G3}";
                    result = tauE <= tps + 1e-9 ? "OK" : "FAILS";
                    detail.Add([name, "Grass n = 0.213·Cn·τo^−0.4", $"{n:F4}"]);
                    detail.Add([name, "Soil ns, τp,soil (lb/ft²)", $"{ns:F4}, {tps:F3}{(tauSoil is null ? (d75 is > 0.05 ? " = 0.4·D75" : " (fine soil)") : " given")}"]);
                    detail.Add([name, "Effective soil shear τe = τd·(1 − Cf)·(ns/n)²", $"{tauE:F3} lb/ft²"]);
                    if (s > 0.10) msgs.Add(CalcMessage.Info("I-CL-001", $"{name}: grass on a slope above 10 % is unusual; check with a TRM."));
                    break;
                }
                case "recp" or "trm" or "ecb":
                {
                    if (tauP is not > 0 || nIn is not > 0) { msgs.Add(CalcMessage.Error("E-CL-005", $"{name}: a RECP/TRM needs tau p (the product's tested permissible shear) and its n.")); continue; }
                    y = NormalDepth(q.Value, s.Value, b.Value, z.Value, _ => nIn.Value);
                    var tr = Trap(b.Value, z.Value, y);
                    n = nIn.Value;
                    tauD = Gamma * y * s.Value;
                    double kMax = double.IsInfinity(rcv) ? 1 : Kb(rcv / tr.T);
                    tauDesign = tauD * Math.Max(1, kMax);
                    need = $"τp {tauP:G3} lb/ft² (product)";
                    used = lining.ToUpperInvariant();
                    result = tauDesign <= tauP.Value + 1e-9 ? "OK" : "FAILS";
                    detail.Add([name, "Safety factor τp/τ", $"{tauP.Value / tauDesign:F2}"]);
                    msgs.Add(CalcMessage.Info("I-CL-002", $"{name}: HEC-15 §5 also checks the soil under the RECP with the product's tested soil shear; take τp from tests on a soil like the site's."));
                    break;
                }
                default:
                    msgs.Add(CalcMessage.Error("E-CL-006", $"{name}: lining \"{lining}\" is not riprap, grass or recp.")); continue;
            }
            var t2 = Trap(b.Value, z.Value, y);
            double v = q.Value / t2.A, fr = v / Math.Sqrt(32.2 * t2.A / t2.T);
            double tauSide = k1 * tauD;
            double kb = double.IsInfinity(rcv) ? double.NaN : Kb(rcv / t2.T);
            double lp = double.IsInfinity(rcv) ? double.NaN : 0.604 * Math.Pow(t2.R, 7.0 / 6) / n;
            rows.Add([name, lining, q, R(y, 2), R(v, 2), R(fr, 2), R(n, 4), R(tauD, 2), R(tauSide, 2), double.IsNaN(kb) ? null : R(kb, 2), double.IsNaN(kb) ? null : R(kb * tauD, 2), double.IsNaN(lp) ? null : R(lp, 0), used, need, result]);
            if (result == "FAILS") msgs.Add(CalcMessage.Warning("W-CL-004", $"{name}: the {lining} lining fails ({need})."));
            if (fr > 0.9 && fr < 1.1) msgs.Add(CalcMessage.Warning("W-CL-005", $"{name}: Froude number {fr:F2} is near critical; the surface will be unstable (HEC-15 suggests avoiding 0.9–1.1 or adding freeboard)."));
            if (result != "SIZED") summary.Add($"{name}: {lining} {result.ToLowerInvariant()}, depth {y:F2} ft, τd {tauD:F2} lb/ft²{(double.IsNaN(kb) ? "" : $", bend {kb * tauD:F2}")}.");
        }
        if (rows.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-CL-007", "Give at least one row under [Reaches]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        var tables = new List<ToolTable>
        {
            new("Linings (HEC-15)", ["Reach", "Lining", "Q (cfs)", "Depth (ft)", "V (ft/s)", "Fr", "n", "τd = γdS (lb/ft²)", "Side K1·τd (lb/ft²)", "Kb", "Bend Kb·τd (lb/ft²)", "Bend protection Lp (ft)", "Lining used", "Requirement", "Result"], rows,
            [
                "Normal depth with the lining's n at that depth. τd is the maximum shear at the deepest point; side shear K1·τd (K1 = 0.77, 0.066Z + 0.67 or 1.0); bend shear Kb·τd with Kb from Rc/T, applied over Lp = 0.604·R^(7/6)/n downstream of the bend.",
                "Riprap: D50 = SF·τ/(F*·(γs − γ)), γs = 165 lb/ft³, F* and SF from Re* = V*·D50/ν (0.047/1.0 to 0.15/1.5); side D50 = (K1/K2)·D50; n by Blodgett, 0.262·da^(1/6)/(2.25 + 5.23·log(da/D50)). Slopes ≤ 10 %.",
                "Grass: n = 0.213·Cn·τo^−0.4; τe = τd·(1 − Cf)·(ns/n)² against the soil's permissible shear (0.4·D75 for non-cohesive D75 > 0.05 in.). RECP/TRM: τd against the product's tested τp.",
            ]),
        };
        if (detail.Count > 0) tables.Add(new("Lining details", ["Reach", "Item", "Value"], detail));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
