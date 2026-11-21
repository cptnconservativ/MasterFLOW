using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Water surface profile along a ditch or swale made of reaches of different section, slope, n and flow (standard step,
/// mixed regime with hydraulic jumps located by specific force), run twice as the IDCM asks: with the capacity n for depth
/// and freeboard, and with the stability n for velocity, Froude number and shear. Each reach's lining is checked against
/// Rapid City IDCM Table 4-14 and FHWA HEC-15 (permissible shear, riprap D50).
/// </summary>
public sealed class ChannelProfileTool : IDesignTool
{
    public string Id => "channel-profile";
    public string Name => "Channel water surface profile and lining checks";
    public string Description => "Standard-step water surface profile (Chow 1959; HEC-RAS mixed regime) for a ditch or swale of several reaches, each with its own section, slope, flow, n and lining; run with the capacity n (depth, freeboard) and the stability n (velocity, Froude, shear) per IDCM §4.8 and Table 4-14; lining checked for permissible shear (FHWA HEC-15) and riprap D50.";
    public bool UsesNetwork => false;

    private const double G = 32.174, Gamma = 62.4;

    public string SampleInput(DesignToolContext context) => """
        # Channel profile, U.S. units (ft, cfs). Reaches listed from DOWNSTREAM to upstream.
        # Downstream boundary: normal (normal depth of the first reach), critical, or a water surface elevation.
        downstream = normal
        downstream invert = 3412.0
        step = 25                  # ft, maximum computation step
        contraction = 0.1          # transition loss coefficients (HEC-RAS defaults for gradual transitions)
        expansion = 0.3

        [Reaches]
        # Shape: trapezoid (b, zL, zR), rectangle (b), triangle (zL, zR). Depth = top of bank above the invert.
        # Lining: grass erosive, grass nonerosive, riprap, concrete, or custom (then give V max, Fr max and tau p).
        # n capacity / n stability: IDCM Table 4-14 (grass 0.035 / 0.030; riprap 0.040 / 0.030; concrete 0.013 / 0.011).
        # D50 (in) for riprap; tau p (lb/ft²) overrides the permissible shear.
        Name	Length (ft)	Slope (ft/ft)	Q (cfs)	Shape	b (ft)	zL	zR	Depth (ft)	Lining	n capacity	n stability	D50 (in)	tau p (lb/ft2)
        Outfall swale	300	0.004	240	trapezoid	8	4	4	4.5	grass nonerosive	0.035	0.030
        Riprap chute	150	0.010	240	trapezoid	8	3	3	4.5	riprap	0.040	0.030	12
        Upper swale	600	0.005	190	trapezoid	6	4	4	4.5	grass nonerosive	0.035	0.030
        """;

    private sealed record Reach(string Name, double Length, double Slope, double Q, ISectionGeometry Geo, string Shape, double B, double ZL, double ZR, double Bank,
        string Lining, double NCap, double NStab, double? D50In, double? TauP, double VMax, double FrMax, double MaxDepth, double Freeboard, double MaxSide, double MinSlope, double MaxSlope);

    private sealed record Station(int Reach, double X, double Bed);

    private sealed record Pt(double X, double Bed, double Y, double V, double Fr, double Sf, double E, double M, string Regime);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Channel water surface profile and lining checks (IDCM §4.8, HEC-15)";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-CP-001", "The channel profile uses the IDCM Table 4-14 limits (ft/s, ft) and HEC-15 U.S. equations; switch the project to U.S. units, or use the Backwater tool in SI."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        var reaches = ParseReaches(p, msgs);
        double step = p.Number("step", 25), cc = p.Number("contraction", 0.1), ce = p.Number("expansion", 0.3), inv0 = p.Number("downstream invert", 0);
        if (!(step > 0)) msgs.Add(CalcMessage.Error("E-CP-002", "step must be greater than 0."));
        msgs.InsertRange(0, p.Errors);
        if (reaches.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-CP-003", "[Reaches] is empty."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        // Stations from the downstream end (x = 0) upstream.
        var st = new List<Station>();
        double x = 0, bed = inv0;
        for (int r = 0; r < reaches.Count; r++)
        {
            var rc = reaches[r];
            int nSeg = Math.Max(2, (int)Math.Ceiling(rc.Length / step));
            for (int k = (r == 0 ? 0 : 1); k <= nSeg; k++)
                st.Add(new Station(r, x + rc.Length * k / nSeg, bed + rc.Slope * rc.Length * k / nSeg));
            x += rc.Length; bed += rc.Slope * rc.Length;
        }
        // At a reach boundary the station belongs to the downstream reach; the upstream reach starts with a duplicate station.
        var stations = new List<Station>();
        for (int i = 0; i < st.Count; i++)
        {
            stations.Add(st[i]);
            if (i + 1 < st.Count && st[i + 1].Reach != st[i].Reach) stations.Add(st[i] with { Reach = st[i + 1].Reach });
        }

        string ds = (p.Text("downstream") ?? "normal").ToLowerInvariant();
        var runs = new Dictionary<string, List<Pt>>();
        foreach (var (label, cap) in new[] { ("capacity", true), ("stability", false) })
        {
            double N(Reach r) => cap ? r.NCap : r.NStab;
            var pts = Profile(stations, reaches, N, ds, inv0, cc, ce, msgs, label);
            if (pts is null) return new ToolResult(title, [], [], msgs);
            runs[label] = pts;
        }

        // ---- tables ----
        var capRun = runs["capacity"]; var stabRun = runs["stability"];
        var rows = new List<IReadOnlyList<object?>>();
        int every = Math.Max(1, stations.Count / 60);
        for (int i = 0; i < stations.Count; i++)
        {
            if (i % every != 0 && i != stations.Count - 1 && !(i + 1 < stations.Count && stations[i + 1].Reach != stations[i].Reach)) continue;
            var c = capRun[i]; var s = stabRun[i]; var rc = reaches[stations[i].Reach];
            double fb = rc.Bank - c.Y;
            rows.Add([rc.Name, c.X, c.Bed, c.Y, c.Bed + c.Y, c.Bed + c.E, c.Regime, fb, s.Y, s.V, s.Fr, Gamma * HydRadius(rc.Geo, s.Y) * s.Sf, Gamma * s.Y * s.Sf]);
        }

        var checks = new List<IReadOnlyList<object?>>();
        void Check(string reach, string item, string req, object? val, bool ok, string reference)
        {
            checks.Add([reach, item, req, val, ok ? "OK" : "NO", reference]);
            if (!ok) msgs.Add(CalcMessage.Warning("W-CP-001", $"{reach}: {item} {DesignTools.Cell(val)} — {req} ({reference})."));
        }
        var reachRows = new List<IReadOnlyList<object?>>();
        for (int r = 0; r < reaches.Count; r++)
        {
            var rc = reaches[r];
            var idx = Enumerable.Range(0, stations.Count).Where(i => stations[i].Reach == r).ToList();
            var cs = idx.Select(i => capRun[i]).ToList(); var ss = idx.Select(i => stabRun[i]).ToList();
            double yMax = cs.Max(q => q.Y), vMaxStab = ss.Max(q => q.V), vMinCap = cs.Min(q => q.V), frMax = ss.Max(q => q.Fr);
            double tauMax = ss.Max(q => Gamma * q.Y * q.Sf);          // HEC-15 Eq. 2.3, τd = γ d S (maximum, at the deepest point)
            double fbMin = cs.Min(q => rc.Bank - q.Y);
            double vhead = cs.Max(q => q.V * q.V / (2 * G));
            double fbNeed = rc.Freeboard;
            reachRows.Add([rc.Name, rc.Lining, rc.Q, yMax, cs.Min(q => q.Y), vMaxStab, frMax, tauMax, fbMin, cs.Any(q => q.Regime == "super") ? "partly supercritical" : "subcritical"]);

            Check(rc.Name, "Max velocity (stability n)", $"≤ {rc.VMax:G3} ft/s", vMaxStab, vMaxStab <= rc.VMax + 1e-9, "IDCM Table 4-14");
            Check(rc.Name, "Min velocity (capacity n)", "≥ 2 ft/s", vMinCap, vMinCap >= 2 - 1e-9, "IDCM Table 4-14");
            if (double.IsFinite(rc.FrMax)) Check(rc.Name, "Max Froude (stability n)", $"≤ {rc.FrMax:G3}", frMax, frMax <= rc.FrMax + 1e-9, "IDCM Table 4-14");
            bool undular = ss.Concat(cs).Any(q => q.Fr is > 0.95 and < 1.05);
            Check(rc.Name, "Froude not in 0.95–1.05", "outside 0.95–1.05 at every station", ss.Concat(cs).Select(q => q.Fr).OrderBy(f => Math.Abs(f - 1)).First(), !undular, "IDCM §4.8.3");
            if (double.IsFinite(rc.MaxDepth)) Check(rc.Name, "Max depth (capacity n)", $"≤ {rc.MaxDepth:G3} ft", yMax, yMax <= rc.MaxDepth + 1e-9, "IDCM Table 4-14");
            Check(rc.Name, "Freeboard (capacity n)", $"≥ {fbNeed:G3} ft + velocity head ({vhead:F2} ft)", fbMin, fbMin >= fbNeed + vhead - 1e-9, "IDCM Table 4-14 note 2");
            Check(rc.Name, "Longitudinal slope", $"{rc.MinSlope * 100:G3}–{rc.MaxSlope * 100:G3} %", rc.Slope * 100, rc.Slope >= rc.MinSlope - 1e-12 && rc.Slope <= rc.MaxSlope + 1e-12, "IDCM Table 4-14");
            if (rc.Shape != "rectangle" && double.IsFinite(rc.MaxSide))
                Check(rc.Name, "Side slope", $"≥ {rc.MaxSide:G3}H:1V", Math.Min(rc.ZL, rc.ZR), Math.Min(rc.ZL, rc.ZR) >= rc.MaxSide - 1e-9, "IDCM Table 4-14");
            if (rc.TauP is { } tp)
                Check(rc.Name, "Shear τd = γ d S (stability n)", $"≤ τp {tp:G3} lb/ft²", tauMax, tauMax <= tp + 1e-9, "HEC-15 Eq. 2.3; τp entered or by lining");
            if (rc.Lining == "riprap")
            {
                // HEC-15 (2005) §6.1, slopes < 10 %: D50 ≥ SF·τd / (F*·(γs − γ)), F* = 0.047, SF = 1.0, γs = 165 lb/ft³.
                // IDCM §4.8.3(2)(c): at transitions use the velocity raised 20 % → shear (∝ V²) raised 44 %.
                double d50 = tauMax / (0.047 * (165 - Gamma)) * 12;
                if (rc.D50In is { } given)
                    Check(rc.Name, "Riprap D50", $"≥ {d50:F1} in. (HEC-15 §6.1, F* 0.047, SF 1.0)", given, given >= d50 - 1e-9, "HEC-15 Eq. 6.7");
                else msgs.Add(CalcMessage.Info("I-CP-001", $"{rc.Name}: riprap D50 ≥ {d50:F1} in. by HEC-15 §6.1 (τd {tauMax:F2} lb/ft²); {d50 * 1.44:F1} in. at transitions with IDCM's +20 % velocity."));
            }
        }

        // ---- charts ----
        var bedPts = capRun.Select(q => (q.X, q.Bed)).ToList();
        var bankPts = stations.Select((s, i) => (capRun[i].X, capRun[i].Bed + reaches[s.Reach].Bank)).ToList();
        var charts = new List<ToolChart>
        {
            new("Water surface profile", "Distance upstream (ft)", "Elevation (ft)",
            [
                new ChartSeries("Invert", bedPts, Color: 7),
                new ChartSeries("WSE, capacity n", capRun.Select(q => (q.X, q.Bed + q.Y)).ToList(), Color: 0),
                new ChartSeries("EGL, capacity n", capRun.Select(q => (q.X, q.Bed + q.E)).ToList(), Dashed: true, Color: 0),
                new ChartSeries("WSE, stability n", stabRun.Select(q => (q.X, q.Bed + q.Y)).ToList(), Color: 1),
                new ChartSeries("Top of bank", bankPts, Dashed: true, Color: 3),
            ]),
            new("Velocity and Froude number (stability n)", "Distance upstream (ft)", "V (ft/s) and Fr",
            [
                new ChartSeries("Velocity (ft/s)", stabRun.Select(q => (q.X, q.V)).ToList(), Color: 1),
                new ChartSeries("Froude number", stabRun.Select(q => (q.X, q.Fr)).ToList(), Dashed: true, Color: 2),
            ]),
        };
        // Typical sections with the capacity water surface at each reach's deepest station.
        var figures = new List<ToolFigure>();
        for (int r = 0; r < reaches.Count; r++)
        {
            var rc = reaches[r];
            var deep = Enumerable.Range(0, stations.Count).Where(i => stations[i].Reach == r).Select(i => capRun[i]).MaxBy(q => q.Y)!;
            var outline = rc.Geo.Outline(rc.Bank);
            var (l, rr) = rc.Geo.WaterSurfaceExtent(deep.Y, rc.Bank);
            var water = outline.Where(o => o.Y <= deep.Y + 1e-9).ToList();
            var poly = new List<(double, double)> { (l, deep.Y) };
            poly.AddRange(water.Where(o => o.X > l && o.X < rr));
            poly.Add((rr, deep.Y));
            figures.Add(new ToolFigure($"{rc.Name}: section at the deepest water (capacity n)",
                [new("Section", ToolSeriesKind.Ground, outline), new("Water", ToolSeriesKind.Water, poly)], "Offset (ft)", "Height above invert (ft)",
                [$"Depth {deep.Y:F2} ft, freeboard {rc.Bank - deep.Y:F2} ft, lining {rc.Lining}."]));
        }

        var summary = new List<string>
        {
            $"{reaches.Count} reach(es), {x:F0} ft; downstream boundary {ds}; standard step with {stations.Count} stations, transition losses {cc:G2}/{ce:G2}.",
            "Two runs (IDCM Table 4-14): capacity n for depth and freeboard, stability n for velocity, Froude and shear.",
            $"Checks: {checks.Count(c => (string)c[4]! == "OK")} OK, {checks.Count(c => (string)c[4]! == "NO")} not met.",
        };
        return new ToolResult(title, summary,
        [
            new("Reach summary", ["Reach", "Lining", "Q (cfs)", "Max depth (ft)", "Min depth (ft)", "Max V, stab. (ft/s)", "Max Fr, stab.", "Max τd (lb/ft²)", "Min freeboard (ft)", "Regime"], reachRows),
            new("Lining and criteria checks", ["Reach", "Item", "Requirement", "Value", "Result", "Reference"], checks),
            new("Profile (capacity n, with the stability-n depth, velocity, Froude and shear)", ["Reach", "Station (ft)", "Invert (ft)", "Depth (ft)", "WSE (ft)", "EGL (ft)", "Regime", "Freeboard (ft)", "Depth, stab. (ft)", "V, stab. (ft/s)", "Fr, stab.", "τ = γRSf (lb/ft²)", "τd = γdS (lb/ft²)"], rows,
                ["Stations are measured upstream from the downstream end. Energy: WS₂ + V₂²/2g = WS₁ + V₁²/2g + L·(Sf₁ + Sf₂)/2 + C·|ΔV²/2g| (C at reach changes only).", "Mixed regime: where a supercritical solution from upstream has greater specific force M = Q²/(gA) + A·ȳ than the subcritical one, flow is supercritical; the jump is where they cross."]),
        ], msgs) { Charts = charts, Figures = figures };
    }

    private static double HydRadius(ISectionGeometry g, double y) => g.Area(y) / g.WettedPerimeter(y);

    /// <summary>Specific force per unit weight, M = Q²/(gA) + A·ȳ, with A·ȳ = ∫₀ʸ T(h)(y − h) dh.</summary>
    private static double SpecificForce(ISectionGeometry g, double q, double y)
    {
        const int n = 40;
        double ay = 0;
        for (int i = 0; i < n; i++) { double h = (i + 0.5) * y / n; ay += g.TopWidth(h) * (y - h) * y / n; }
        return q * q / (G * g.Area(y)) + ay;
    }

    private static List<Pt>? Profile(List<Station> stations, List<Reach> reaches, Func<Reach, double> nOf, string ds, double inv0, double cc, double ce, List<CalcMessage> msgs, string label)
    {
        int count = stations.Count;
        double Yc(int i) => Crit(reaches[stations[i].Reach]);
        Pt Make(int i, double y, string regime)
        {
            var rc = reaches[stations[i].Reach];
            double a = rc.Geo.Area(y), v = rc.Q / a, t = rc.Geo.TopWidth(y);
            double sf = Math.Pow(nOf(rc) * v / (1.486 * Math.Pow(a / rc.Geo.WettedPerimeter(y), 2.0 / 3.0)), 2);
            return new Pt(stations[i].X, stations[i].Bed, y, v, v / Math.Sqrt(G * a / t), sf, y + v * v / (2 * G), SpecificForce(rc.Geo, rc.Q, y), regime);
        }
        // Energy step from station i (known) to j (unknown) on the branch above (sub) or below (super) critical depth.
        double? Step(int i, double yi, int j, bool sub)
        {
            var ri = reaches[stations[i].Reach]; var rj = reaches[stations[j].Reach];
            var pi = Make(i, yi, "");
            double dx = Math.Abs(stations[j].X - stations[i].X), dz = stations[j].Bed - stations[i].Bed;
            double hv(double y, Reach r) => Math.Pow(r.Q / r.Geo.Area(y), 2) / (2 * G);
            double F(double yj)
            {
                var pj = Make(j, yj, "");
                double loss = dx * 0.5 * (pi.Sf + pj.Sf);
                double dh = hv(yj, rj) - hv(yi, ri);
                if (!ReferenceEquals(ri, rj) || dx == 0) loss += ((dh > 0) == sub ? ce : cc) * Math.Abs(dh);   // contraction where the velocity head rises in the flow direction, expansion where it falls
                // sub: j upstream of i; WS_j + hv_j = WS_i + hv_i + loss. super: j downstream; WS_j + hv_j = WS_i + hv_i − loss.
                double lhs = dz + yj + hv(yj, rj), rhs = yi + hv(yi, ri) + (sub ? loss : -loss);
                return lhs - rhs;
            }
            double yc = Crit(rj);
            double lo = sub ? yc * (1 + 1e-6) : yc * 1e-3, hi = sub ? Math.Max(rj.Bank * 4, yc * 20) : yc * (1 - 1e-6);
            double flo = F(lo), fhi = F(hi);
            if (flo * fhi > 0) return null;
            for (int it = 0; it < 80; it++) { double m = 0.5 * (lo + hi), fm = F(m); if (fm * flo > 0) { lo = m; flo = fm; } else hi = m; }
            return 0.5 * (lo + hi);
        }

        // Subcritical pass, from the downstream end upstream.
        var sub = new Pt?[count];
        var r0 = reaches[stations[0].Reach];
        double y0;
        if (ds == "critical") y0 = Yc(0);
        else if (ds == "normal")
        {
            if (!(r0.Slope > 0)) { msgs.Add(CalcMessage.Error("E-CP-010", "downstream = normal needs a positive slope in the first reach; give a water surface elevation instead.")); return null; }
            var yn = UniformFlowCalculator.SolveNormalDepth(r0.Geo, r0.Q, nOf(r0), r0.Slope, 1.486);
            if (!yn.Converged) { msgs.Add(CalcMessage.Error("E-CP-011", $"Normal depth not found in {r0.Name}.")); return null; }
            y0 = yn.Root;
        }
        else if (ToolInput.TryNumber(ds, out double wse)) y0 = wse - inv0;
        else { msgs.Add(CalcMessage.Error("E-CP-012", "downstream must be normal, critical or a water surface elevation.")); return null; }
        if (!(y0 > 0)) { msgs.Add(CalcMessage.Error("E-CP-013", "The downstream water surface is at or below the invert.")); return null; }
        sub[0] = Make(0, Math.Max(y0, Yc(0)), y0 < Yc(0) ? "critical" : "sub");
        int critCount = 0;
        for (int j = 1; j < count; j++)
        {
            var y = Step(j - 1, sub[j - 1]!.Y, j, true);
            if (y is null) { critCount++; sub[j] = Make(j, Yc(j), "critical"); }
            else sub[j] = Make(j, y.Value, "sub");
        }
        // Supercritical pass, from the upstream end downstream; starts at critical depth where the top reach is steep.
        var sup = new Pt?[count];
        bool anySteep = reaches.Any(r => r.Slope > 0 && UniformFlowCalculator.SolveNormalDepth(r.Geo, r.Q, nOf(r), r.Slope, 1.486) is { Converged: true } yn && yn.Root < Crit(r));
        if (anySteep)
        {
            for (int j = count - 1; j >= 0; j--)
            {
                if (sup[Math.Min(j + 1, count - 1)] is not { } prev || j == count - 1)
                {
                    // start (or restart) at critical depth where the reach is steep
                    var rj = reaches[stations[j].Reach];
                    if (rj.Slope > 0 && UniformFlowCalculator.SolveNormalDepth(rj.Geo, rj.Q, nOf(rj), rj.Slope, 1.486) is { Converged: true } ynj && ynj.Root < Crit(rj))
                        sup[j] = Make(j, Crit(rj) * (1 - 1e-6), "super");
                    continue;
                }
                var y = Step(j + 1, prev.Y, j, false);
                sup[j] = y is null ? null : Make(j, y.Value, "super");
            }
        }
        // Mixed regime: the larger specific force governs.
        var pts = new List<Pt>();
        for (int i = 0; i < count; i++)
        {
            var s = sub[i]!; var u = sup[i];
            var use = u is not null && (s.Regime == "critical" || u.M > s.M) ? u : s;
            pts.Add(use);
        }
        if (critCount > 0 && !anySteep)
            msgs.Add(CalcMessage.Warning("W-CP-002", $"{label} n: no subcritical solution at {critCount} station(s); critical depth assumed there (HEC-RAS does the same). Check the slope and section."));
        int superCount = pts.Count(q => q.Regime == "super");
        if (superCount > 0)
            msgs.Add(CalcMessage.Info("I-CP-002", $"{label} n: supercritical over {superCount} of {count} stations; regime changes are located by specific force (hydraulic jump where the profiles' M values cross)."));
        return pts;
    }

    private static readonly Dictionary<ISectionGeometry, double> _crit = new(ReferenceEqualityComparer.Instance);
    private static double Crit(Reach r)
    {
        lock (_crit)
        {
            if (_crit.TryGetValue(r.Geo, out double v)) return v;
            var c = UniformFlowCalculator.SolveCriticalDepth(r.Geo, r.Q, G);
            v = c.Converged ? c.Root : double.NaN;
            if (_crit.Count > 1000) _crit.Clear();
            _crit[r.Geo] = v;
            return v;
        }
    }

    private static List<Reach> ParseReaches(ToolInput p, List<CalcMessage> msgs)
    {
        var head = p.Header("Reaches");
        int Col(params string[] keys) { for (int i = 0; i < head.Count; i++) foreach (var k in keys) if (head[i].Trim().StartsWith(k, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
        int cN = Col("name"), cL = Col("length"), cS = Col("slope"), cQ = Col("q"), cSh = Col("shape"), cB = Col("b "), cZl = Col("zl"), cZr = Col("zr"), cD = Col("depth"), cLin = Col("lining"),
            cNc = Col("n cap"), cNs = Col("n stab"), cD50 = Col("d50"), cTau = Col("tau", "τ"), cVmax = Col("v max"), cFr = Col("fr max");
        if (cB < 0) cB = Col("b");
        var list = new List<Reach>();
        foreach (var r in p.Rows("Reaches"))
        {
            string name = cN >= 0 && cN < r.Length ? r[cN] : $"Reach {list.Count + 1}", w = $"Reach {name}";
            double? L = p.Cell(r, cL, "length", w), S = p.Cell(r, cS, "slope", w), Q = p.Cell(r, cQ, "Q", w), b = cB < 0 ? null : p.Cell(r, cB, "b", w), zl = cZl < 0 ? null : p.Cell(r, cZl, "zL", w),
                zr = cZr < 0 ? null : p.Cell(r, cZr, "zR", w), d = p.Cell(r, cD, "depth", w), nc = cNc < 0 ? null : p.Cell(r, cNc, "n capacity", w), ns = cNs < 0 ? null : p.Cell(r, cNs, "n stability", w),
                d50 = cD50 < 0 ? null : p.Cell(r, cD50, "D50", w), tau = cTau < 0 ? null : p.Cell(r, cTau, "tau p", w), vmax = cVmax < 0 ? null : p.Cell(r, cVmax, "V max", w), frx = cFr < 0 ? null : p.Cell(r, cFr, "Fr max", w);
            string shape = (cSh >= 0 && cSh < r.Length ? r[cSh] : "trapezoid").Trim().ToLowerInvariant();
            string lining = (cLin >= 0 && cLin < r.Length ? r[cLin] : "custom").Trim().ToLowerInvariant();
            if (L is not > 0 || S is null || Q is not > 0 || d is not > 0) { msgs.Add(CalcMessage.Error("E-CP-004", $"{w}: length, slope, Q and depth (top of bank) are required, length, Q and depth > 0.")); continue; }
            ChannelSection? sec = shape switch
            {
                "trapezoid" or "trapezoidal" when b is >= 0 && (zl ?? 0) >= 0 && (zr ?? zl ?? 0) >= 0 && (b > 0 || (zl ?? 0) > 0) => new TrapezoidalSection(b!.Value, zl ?? 0, zr ?? zl ?? 0),
                "rectangle" or "rectangular" when b is > 0 => new RectangularSection(b.Value),
                "triangle" or "triangular" when zl is > 0 => new TriangularSection(zl.Value, zr ?? zl.Value),
                _ => null,
            };
            if (sec is null) { msgs.Add(CalcMessage.Error("E-CP-005", $"{w}: shape must be trapezoid (b, zL, zR), rectangle (b) or triangle (zL, zR) with valid dimensions.")); continue; }
            // IDCM Table 4-14 by lining.
            (double vmx, double frm, double dmax, double fb, double side, double smin, double smax, double nCapMax, double nStabMin, double? tauDefault) crit = lining switch
            {
                "grass erosive" => (5, 0.5, 5, 1, 4, 0.002, 0.006, 0.035, 0.03, null),
                "grass nonerosive" or "grass" => (7, 0.8, 5, 1, 4, 0.002, 0.006, 0.035, 0.03, null),
                "riprap" => (12, 0.8, double.PositiveInfinity, 2, 2.5, 0.002, 0.01, 0.04, 0.03, null),
                "concrete" => (18, double.PositiveInfinity, double.PositiveInfinity, 2, 1.5, 0, 1, 0.013, 0.011, null),
                "custom" => (vmax ?? double.PositiveInfinity, frx ?? double.PositiveInfinity, double.PositiveInfinity, 1, double.NaN, 0, 1, 1, 0, null),
                _ => (double.NaN, 0, 0, 0, 0, 0, 0, 0, 0, null),
            };
            if (double.IsNaN(crit.vmx)) { msgs.Add(CalcMessage.Error("E-CP-006", $"{w}: lining must be grass erosive, grass nonerosive, riprap, concrete or custom.")); continue; }
            double nCap = nc ?? crit.nCapMax, nStab = ns ?? crit.nStabMin;
            if (lining == "custom" && (nc is null || ns is null)) { msgs.Add(CalcMessage.Error("E-CP-007", $"{w}: a custom lining needs n capacity and n stability.")); continue; }
            if (!(nCap > 0) || !(nStab > 0) || nStab > nCap + 1e-12) { msgs.Add(CalcMessage.Error("E-CP-007", $"{w}: n values must be > 0 and n stability ≤ n capacity.")); continue; }
            if (lining != "custom")
            {
                if (nc is { } ncv && ncv > crit.nCapMax + 1e-12) msgs.Add(CalcMessage.Info("I-CP-003", $"{w}: n capacity {ncv:G3} is above IDCM Table 4-14's {crit.nCapMax:G3} for {lining}; your value is used (conservative for depth)."));
                if (ns is { } nsv && nsv < crit.nStabMin - 1e-12) msgs.Add(CalcMessage.Info("I-CP-003", $"{w}: n stability {nsv:G3} is below IDCM Table 4-14's {crit.nStabMin:G3}; your value is used (conservative for velocity)."));
                if (nc is { } ncv2 && ncv2 < crit.nCapMax - 1e-12) msgs.Add(CalcMessage.Warning("W-CP-003", $"{w}: n capacity {ncv2:G3} is below IDCM Table 4-14's maximum {crit.nCapMax:G3}; the capacity check should use {crit.nCapMax:G3}."));
                if (ns is { } nsv2 && nsv2 > crit.nStabMin + 1e-12) msgs.Add(CalcMessage.Warning("W-CP-003", $"{w}: n stability {nsv2:G3} is above IDCM Table 4-14's minimum {crit.nStabMin:G3}; the stability check should use {crit.nStabMin:G3}."));
            }
            if (lining == "riprap" && d50 is null) msgs.Add(CalcMessage.Info("I-CP-004", $"{w}: no D50 given; the required D50 is reported."));
            double? tp = tau;
            if (lining.StartsWith("grass") && tau is null)
                msgs.Add(CalcMessage.Info("I-CP-005", $"{w}: no permissible shear given for the grass lining; IDCM limits velocity and Froude number. To also check shear, give tau p (lb/ft²) from HEC-15 Ch. 4 (vegetation) or the lining manufacturer."));
            var geo = SectionGeometry.From(sec);
            list.Add(new Reach(name, L.Value, S.Value, Q.Value, geo, shape.StartsWith("trap") ? "trapezoid" : shape.StartsWith("rect") ? "rectangle" : "triangle", b ?? 0, zl ?? 0, zr ?? zl ?? 0, d.Value,
                lining, nCap, nStab, d50, tp, crit.vmx, crit.frm, crit.dmax, crit.fb, crit.side, crit.smin, crit.smax));
        }
        return list;
    }
}
