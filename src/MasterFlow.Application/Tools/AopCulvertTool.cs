using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Culvert design for aquatic organism passage by FHWA HEC-26 (2010), stream simulation approach: embedded box or pipe
/// with a natural bed, composite roughness (Limerinos bed n, Horton composite), Einstein bed partition shear, Shields
/// stability of D50/D84/D95 at the high design flow, comparison with the natural channel, and the low passage flow
/// depth and velocity checks.
/// </summary>
public sealed class AopCulvertTool : IDesignTool
{
    public string Id => "aop-culvert";
    public string Name => "Culverts for aquatic organism passage (HEC-26)";
    public string Description => "FHWA HEC-26 (2010) stream-simulation culvert: span against bankfull width, embedment, slope match; for each flow the normal depth in the embedded barrel with a Limerinos bed n and a Horton composite n, bed shear by the Einstein partition, the smallest stable particle by Shields (F* 0.047) against D50, D84 and D95, the same for the natural channel, and low-flow depth and velocity limits for the target species.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Culvert for aquatic organism passage, FHWA HEC-26 (2010). SI: m, m³/s, m/s; bed sizes in mm.
        shape = box
        span = 4.3
        rise = 3.0
        embedment = 0.9
        wall n = 0.012
        slope = 0.015
        stream slope = 0.015
        bankfull width = 3.6
        stream bottom width = 2.4
        stream side slope = 2
        D50 = 90
        D84 = 280
        D95 = 400
        min depth = 0.15
        max velocity = 1.2
        [Flows]
        Name	Q	Use
        Q100 bed stability	21	stability
        High passage flow	3.4	high passage
        Low passage flow	0.07	low passage
        """ : """
        # Culvert for aquatic organism passage, FHWA HEC-26 (2010), stream simulation. U.S. units (ft, cfs, ft/s); bed sizes in mm.
        shape = box                 # box or pipe (circular)
        span = 14                   # box span or pipe diameter, ft
        rise = 10                   # box rise, ft (pipe: rise = diameter)
        embedment = 3               # depth of bed material inside the barrel, ft
        wall n = 0.012              # concrete 0.012, corrugated metal 0.024
        slope = 0.015               # barrel slope
        stream slope = 0.015        # reach slope; stream simulation matches it

        # Natural channel (reference reach): bankfull width, and a trapezoid for the comparison runs.
        bankfull width = 12
        stream bottom width = 8
        stream side slope = 2       # H:1V
        D50 = 90                    # mm
        D84 = 280                   # mm
        D95 = 400                   # mm

        # Passage criteria for the target species (state fish agency values).
        min depth = 0.5             # ft at the low passage flow
        max velocity = 4            # ft/s at the high passage flow

        # Use: stability (bed must not move: e.g. the 100-yr flow), high passage, or low passage.
        [Flows]
        Name	Q (cfs)	Use
        Q100 bed stability	750	stability
        High passage flow	120	high passage
        Low passage flow	2.5	low passage
        """;

    /// <summary>Limerinos (1970) Manning n for a gravel bed: 0.0926·R^(1/6)/(1.16 + 2·log10(R/D84)), R and D84 in ft (0.1129 with m).</summary>
    public static double Limerinos(double r, double d84, bool si)
    {
        double den = 1.16 + 2 * Math.Log10(r / d84);
        return den > 0.1 ? (si ? 0.1129 : 0.0926) * Math.Pow(r, 1.0 / 6) / den : double.NaN;
    }

    /// <summary>Section above the bed at flow depth y: area, wall wetted perimeter, bed width, top width.</summary>
    public static (double A, double Pw, double B, double T) Section(bool box, double span, double rise, double e, double y)
    {
        if (box) return (span * y, 2 * y, span, span);
        double r = span / 2;
        double Seg(double h) => h <= 0 ? 0 : h >= 2 * r ? Math.PI * r * r : r * r * Math.Acos((r - h) / r) - (r - h) * Math.Sqrt(2 * r * h - h * h);
        double Arc(double h) => h <= 0 ? 0 : h >= 2 * r ? 2 * Math.PI * r : 2 * r * Math.Acos((r - h) / r);
        double Chord(double h) => h <= 0 || h >= 2 * r ? 0 : 2 * Math.Sqrt(r * r - (r - h) * (r - h));
        return (Seg(e + y) - Seg(e), Arc(e + y) - Arc(e), Chord(e), Chord(e + y));
    }

    public sealed record Hydraulics(double Y, double A, double V, double N, double NBed, double RBed, double TauBed, bool Full);

    /// <summary>Normal depth in the embedded barrel with the composite n (bed by Limerinos on the bed partition, walls fixed).</summary>
    public static Hydraulics? Barrel(bool box, double span, double rise, double e, double nWall, double s, double q, double d84, bool si)
    {
        double k = si ? 1.0 : 1.486, gamma = si ? 9810 : 62.4;
        double maxY = rise - e;
        Hydraulics? Solve(double y)
        {
            var (a, pw, b, _) = Section(box, span, rise, e, y);
            if (a <= 0) return null;
            // Iterate the bed n with the Einstein partition: equal velocity, Rb = (V·nb/(k·√S))^1.5.
            double nb = 0.035, nc = nb, v = 0, rb = a / (pw + b);
            for (int i = 0; i < 50; i++)
            {
                double pTot = pw + b;
                nc = Math.Pow((b * Math.Pow(nb, 1.5) + pw * Math.Pow(nWall, 1.5)) / pTot, 2.0 / 3);
                v = k / nc * Math.Pow(a / pTot, 2.0 / 3) * Math.Sqrt(s);
                rb = Math.Pow(v * nb / (k * Math.Sqrt(s)), 1.5);
                double nNew = Limerinos(rb, d84, si);
                if (double.IsNaN(nNew)) nNew = 0.07;
                if (Math.Abs(nNew - nb) < 1e-7) { nb = nNew; break; }
                nb = 0.5 * (nb + nNew);
            }
            return new(y, a, v, nc, nb, rb, gamma * rb * s, false);
        }
        var top = Solve(maxY * 0.999);
        if (top is null || top.A * top.V < q) return top is null ? null : top with { Full = true };
        double lo = 1e-6, hi = maxY * 0.999;
        for (int i = 0; i < 100; i++)
        {
            double mid = 0.5 * (lo + hi);
            var h = Solve(mid);
            if (h is null || h.A * h.V < q) lo = mid; else hi = mid;
        }
        return Solve(hi);
    }

    /// <summary>Normal depth in a trapezoidal natural channel with a Limerinos n.</summary>
    public static Hydraulics Stream(double bw, double z, double s, double q, double d84, bool si)
    {
        double k = si ? 1.0 : 1.486, gamma = si ? 9810 : 62.4;
        Hydraulics At(double y)
        {
            double a = (bw + z * y) * y, p = bw + 2 * y * Math.Sqrt(1 + z * z), r = a / p;
            double n = Limerinos(r, d84, si);
            if (double.IsNaN(n)) n = 0.07;
            double v = k / n * Math.Pow(r, 2.0 / 3) * Math.Sqrt(s);
            return new(y, a, v, n, n, r, gamma * r * s, false);
        }
        double lo = 1e-6, hi = 1;
        while (At(hi).A * At(hi).V < q && hi < 1e4) hi *= 2;
        for (int i = 0; i < 100; i++) { double mid = 0.5 * (lo + hi); if (At(mid).A * At(mid).V < q) lo = mid; else hi = mid; }
        return At(hi);
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Culvert for aquatic organism passage (HEC-26)";
        bool si = context.Units == UnitSystem.SI;
        string L = si ? "m" : "ft", Vu = si ? "m/s" : "ft/s", Tu = si ? "Pa" : "lb/ft²";
        double gammaS = si ? 2.65 * 9810 : 2.65 * 62.4, gamma = si ? 9810 : 62.4;
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        string shape = (p.Text("shape") ?? "box").ToLowerInvariant();
        bool box = shape is "box" or "rectangular" or "rectangle";
        if (!box && shape is not ("pipe" or "circular" or "round")) msgs.Add(CalcMessage.Error("E-AO-001", $"shape \"{shape}\" is not box or pipe."));
        double span = p.Number("span", double.NaN, "span"), rise = p.Number("rise", box ? double.NaN : span, "rise");
        if (!box) rise = span;
        double e = p.Number("embedment", double.NaN, "embedment"), nWall = p.Number("wall n", box ? 0.012 : 0.024, "wall n"), s = p.Number("slope", double.NaN, "slope");
        double? wbf = p.OptionalNumber("bankfull width"), sbw = p.OptionalNumber("stream bottom width"), sz = p.OptionalNumber("stream side slope");
        double? sStream = p.OptionalNumber("stream slope");
        double d50 = p.Number("D50", double.NaN, "D50"), d84 = p.Number("D84", double.NaN, "D84"), d95 = p.Number("D95", double.NaN, "D95");
        double fStar = p.Number("Shields", 0.047, "Shields parameter"), sf = p.Number("safety factor", 1.0, "safety factor");
        double? minDepth = p.OptionalNumber("min depth"), maxV = p.OptionalNumber("max velocity");
        if (!(span > 0) || !(rise > 0)) msgs.Add(CalcMessage.Error("E-AO-002", "Give the span (or diameter) and rise above zero."));
        if (!(e > 0) || e >= rise) msgs.Add(CalcMessage.Error("E-AO-003", "Give the embedment depth above zero and below the rise: stream simulation needs a natural bed in the barrel."));
        if (!(s > 0)) msgs.Add(CalcMessage.Error("E-AO-004", "Give the barrel slope above zero."));
        if (!(d50 > 0) || !(d84 >= d50)) msgs.Add(CalcMessage.Error("E-AO-005", "Give D50 and D84 (mm) of the bed, with D84 ≥ D50."));
        if (!(nWall > 0)) msgs.Add(CalcMessage.Error("E-AO-006", "wall n must be above zero."));
        var flows = new List<(string Name, double Q, string Use)>();
        foreach (var r in p.Rows("Flows"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Flow {flows.Count + 1}";
            double? q = p.Cell(r, 1, "Q", name);
            string use = (r.Length > 2 ? r[2] : "stability").Trim().ToLowerInvariant();
            if (q is not > 0) { msgs.Add(CalcMessage.Error("E-AO-007", $"{name}: Q must be above zero.")); continue; }
            if (use is not ("stability" or "high passage" or "low passage")) { msgs.Add(CalcMessage.Error("E-AO-008", $"{name}: use \"{use}\" is not stability, high passage or low passage.")); continue; }
            flows.Add((name, q.Value, use));
        }
        if (flows.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-AO-009", "Give at least one flow under [Flows]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double toL = si ? 1.0 / 1000 : 1.0 / 304.8;   // mm → m or ft
        double d50L = d50 * toL, d84L = d84 * toL, d95L = double.IsNaN(d95) ? double.NaN : d95 * toL;
        var tables = new List<ToolTable>();
        var summary = new List<string>();

        // Geometry checks (HEC-26 Ch. 5, stream simulation).
        var (_, _, bedW, _) = Section(box, span, rise, e, 0);
        var geo = new List<IReadOnlyList<object?>>();
        if (wbf is > 0)
        {
            double ratio = bedW / wbf.Value;
            geo.Add(["Bed width in the barrel / bankfull width", R(ratio, 2), "≥ 1.0", ratio >= 1 ? "OK" : "LOW"]);
            if (ratio < 1) msgs.Add(CalcMessage.Warning("W-AO-001", $"The bed width in the barrel ({bedW:F2} {L}) is narrower than the bankfull width ({wbf:F2} {L}); HEC-26 stream simulation sizes the bed to at least bankfull width so the channel and banks can form inside."));
        }
        else msgs.Add(CalcMessage.Info("I-AO-001", "No bankfull width: the width check and the natural channel comparison are skipped."));
        double ef = e / rise;
        geo.Add(["Embedment / rise", R(ef, 2), box ? "(bed ≥ 2·D95 below)" : "0.3–0.5", box ? "" : ef is >= 0.3 and <= 0.5 ? "OK" : "CHECK"]);
        if (!box && ef is < 0.3 or > 0.5) msgs.Add(CalcMessage.Warning("W-AO-002", $"Pipe embedment {ef:P0} of the diameter is outside the 30–50 % HEC-26 recommends for embedded round pipes."));
        if (!double.IsNaN(d95L))
        {
            bool deep = e >= 2 * d95L;
            geo.Add(["Bed depth / D95", R(e / d95L, 1), "≥ 2", deep ? "OK" : "LOW"]);
            if (!deep) msgs.Add(CalcMessage.Warning("W-AO-003", "The bed material is less than twice D95 deep; the largest stones would rest on the invert."));
        }
        if (sStream is > 0)
        {
            double sr = s / sStream.Value;
            geo.Add(["Barrel slope / stream slope", R(sr, 2), "0.75–1.25", sr is >= 0.75 and <= 1.25 ? "OK" : "CHECK"]);
            if (sr is < 0.75 or > 1.25) msgs.Add(CalcMessage.Warning("W-AO-004", "The barrel slope differs from the stream slope by more than 25 %; stream simulation follows the reach slope so the bed stays continuous."));
        }
        geo.Add(["Bed width in the barrel", R(bedW, 2), L, ""]);
        tables.Add(new("Geometry (HEC-26 stream simulation)", ["Check", "Value", "Target", "Result"], geo,
            ["Bed width: box span, or the chord at the embedment depth for a pipe. Targets are HEC-26 guidance; state fish-passage rules (e.g. 1.2·W + 2 ft) may be stricter."]));

        double tauC(double d) => fStar * (gammaS - gamma) * d;
        string Stable(double tau, double d) => double.IsNaN(d) ? "" : tau * sf <= tauC(d) ? "stable" : "MOBILE";
        var rows = new List<IReadOnlyList<object?>>();
        var srows = new List<IReadOnlyList<object?>>();
        foreach (var f in flows)
        {
            var h = Barrel(box, span, rise, e, nWall, s, f.Q, d84L, si);
            if (h is null) { msgs.Add(CalcMessage.Error("E-AO-010", $"{f.Name}: the barrel geometry has no flow area above the bed.")); continue; }
            if (h.Full) { msgs.Add(CalcMessage.Warning("W-AO-005", $"{f.Name}: {f.Q:G4} exceeds the barrel's open-channel capacity above the bed; the culvert flows full or under inlet control. Check headwater with the HDS-5 culvert tool.")); rows.Add([f.Name, f.Use, f.Q, null, null, null, null, null, null, null, null, null, "flows full"]); continue; }
            double dStable = sf * h.TauBed / (fStar * (gammaS - gamma));
            string note = "";
            if (f.Use == "stability")
            {
                string s84 = Stable(h.TauBed, d84L);
                note = $"D50 {Stable(h.TauBed, d50L)}, D84 {s84}{(double.IsNaN(d95L) ? "" : $", D95 {Stable(h.TauBed, d95L)}")}";
                if (s84 == "MOBILE") msgs.Add(CalcMessage.Warning("W-AO-006", $"{f.Name}: D84 moves at τb = {h.TauBed:F2} {Tu}; HEC-26 designs the bed so D84 is stable at the high design flow. Use coarser bed material (D84 ≥ {dStable / toL:F0} mm), add bed-retention sills, or widen the barrel."));
            }
            else if (f.Use == "low passage" && minDepth is { } md)
            {
                note = h.Y >= md ? $"depth ≥ {md:G3} OK" : $"depth below {md:G3}";
                if (h.Y < md) msgs.Add(CalcMessage.Warning("W-AO-007", $"{f.Name}: depth {h.Y:F2} {L} is below the {md:G3} {L} minimum; shape a low-flow channel in the bed (HEC-26 recommends a thalweg like the natural reach)."));
            }
            else if (f.Use == "high passage" && maxV is { } mv)
            {
                note = h.V <= mv ? $"V ≤ {mv:G3} OK" : $"V above {mv:G3}";
                if (h.V > mv) msgs.Add(CalcMessage.Warning("W-AO-008", $"{f.Name}: average velocity {h.V:F2} {Vu} exceeds {mv:G3} {Vu}; the bed's roughness and margins usually give slower paths, but confirm with the species criteria."));
            }
            rows.Add([f.Name, f.Use, f.Q, R(h.Y, 2), R(h.V, 2), R(h.NBed, 4), R(h.N, 4), R(h.RBed, 3), R(h.TauBed, 2), R(tauC(d50L), 2), R(tauC(d84L), 2), R(dStable / toL, 0), note]);
            if (sbw is > 0 && sz is >= 0)
            {
                var st = Stream(sbw.Value, sz.Value, sStream ?? s, f.Q, d84L, si);
                double ratioT = h.TauBed / st.TauBed;
                srows.Add([f.Name, f.Q, R(st.Y, 2), R(st.V, 2), R(st.N, 4), R(st.TauBed, 2), R(h.TauBed, 2), R(ratioT, 2), ratioT <= 1.25 ? "similar" : "culvert higher"]);
                if (ratioT > 1.25 && f.Use != "low passage") msgs.Add(CalcMessage.Warning("W-AO-009", $"{f.Name}: bed shear in the barrel is {ratioT:F2} × the natural channel's; the culvert will scour its bed faster than the stream. Widen the barrel."));
            }
        }
        tables.Add(new("Barrel hydraulics and bed stability", ["Flow", "Use", $"Q ({(si ? "m³/s" : "cfs")})", $"Depth above bed ({L})", $"V ({Vu})", "n bed", "n composite", $"R bed ({L})", $"τ bed ({Tu})", $"τc D50 ({Tu})", $"τc D84 ({Tu})", "Stable size (mm)", "Result"], rows,
        [
            "Bed n: Limerinos, n = 0.0926·R^(1/6)/(1.16 + 2·log(R/D84)) (R, D84 in ft). Composite n (Horton): n = [(Pb·nb^1.5 + Pw·nw^1.5)/P]^(2/3).",
            $"Bed shear τb = γ·Rb·S with the bed hydraulic radius Rb = (V·nb/(k·√S))^1.5 (Einstein partition). Critical τc = F*·(γs − γ)·D, F* = {fStar:G3} (Shields), γs = 2.65·γ; stable size = SF·τb/(F*·(γs − γ)), SF = {sf:G3}.",
            "Normal depth in the barrel (inlet and outlet losses ignored): run the HDS-5 culvert tool for headwater at the design flows.",
        ]));
        if (srows.Count > 0)
            tables.Add(new("Natural channel comparison (same flows)", ["Flow", "Q", $"Depth ({L})", $"V ({Vu})", "n", $"τ ({Tu})", $"τ barrel bed ({Tu})", "Barrel / stream", "Result"], srows,
                ["Trapezoidal reach section with a Limerinos n, normal depth. Above bankfull the reach section overstates depth (no floodplain); give a wider bottom or use the natural-channel profile tool for a better comparison."]));
        var stab = rows.Where(r => (string)r[1]! == "stability" && r[3] is not null).ToList();
        if (stab.Count > 0) summary.Add($"Bed stability: {string.Join("; ", stab.Select(r => $"{r[0]} {r[12]}"))}.");
        summary.Add($"Bed width in the barrel {bedW:F2} {L}{(wbf is > 0 ? $" vs bankfull {wbf:F2} {L}" : "")}, embedment {e:G3} {L} ({ef:P0} of the rise).");
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
