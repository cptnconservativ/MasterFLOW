using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// FHWA HEC-20 (2012) Level 2 stream stability computations for a reach: hydraulics by Manning in a wide channel,
/// incipient motion (Shields) and the critical grain size, the equilibrium slope at which the bed stops moving and the
/// degradation it implies upstream of a fixed control, armoring depth from the bed gradation, specific stream power,
/// the Leopold–Wolman braiding threshold, sinuosity and width–depth classification, and a Lane's relation check of a
/// proposed change.
/// </summary>
public sealed class StreamStabilityTool : IDesignTool
{
    public string Id => "stream-stability";
    public string Name => "Stream stability at highway crossings (HEC-20 Level 2)";
    public string Description => "HEC-20 Level 2 stream stability: Manning hydraulics of the reach; Shields critical size Dc = τ/(F*·(γs − γ)); equilibrium slope Seq = [F*(Ss − 1)D]^(10/7)/(q·n/K)^(6/7) and the degradation (S0 − Seq)·L to a downstream control; armoring depth ys = ya·(1/Pc − 1) from the gradation; specific stream power γQS/W; Leopold–Wolman braiding threshold S = 0.06·Q^−0.44; sinuosity and width–depth ratio; Lane's relation Qs·D50 ∝ Q·S for a proposed change, telling whether the reach tends to aggrade or degrade.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Stream stability (HEC-20 Level 2). SI: m, m³/s, mm.
        Q = 85                    # m³/s, design (or bankfull) discharge
        bankfull Q = 40           # m³/s, for the braiding threshold (defaults to Q)
        width = 25                # m, top width of the active channel
        slope = 0.004             # m/m, bed slope S0
        n = 0.035
        D50 = 25                  # mm
        sinuosity = 1.3           # channel length / valley length
        control distance = 600    # m to a downstream grade control (bed rock, culvert apron, drop)
        Shields = 0.047
        armor multiple = 2        # armor layer thickness ya = multiple × Dc
        [Gradation]
        Size (mm), Percent finer
        1, 5
        5, 18
        12, 35
        25, 50
        50, 75
        90, 92
        150, 100
        [Change]
        Q = 1.2                   # ratios of the new to the existing value
        Qs = 0.5
        """ : """
        # Stream stability (HEC-20 Level 2). U.S. units: ft, cfs, mm for grain sizes.
        Q = 3000                  # cfs, design (or bankfull) discharge
        bankfull Q = 1400         # cfs, for the braiding threshold (defaults to Q)
        width = 80                # ft, top width of the active channel
        slope = 0.004             # ft/ft, bed slope S0
        n = 0.035
        D50 = 25                  # mm
        # D90 = 90                # mm (optional): also gives Seq for the coarse fraction, as HEC-20 Example 2 does
        sinuosity = 1.3           # channel length / valley length
        control distance = 2000   # ft to a downstream grade control (bed rock, culvert apron, drop)
        Shields = 0.047
        armor multiple = 2        # armor layer thickness ya = multiple × Dc
        [Gradation]
        Size (mm), Percent finer
        1, 5
        5, 18
        12, 35
        25, 50
        50, 75
        90, 92
        150, 100
        [Change]
        Q = 1.2                   # ratios of the new to the existing value (Q, Qs, D50, S)
        Qs = 0.5
        """;

    /// <summary>Normal depth of a wide rectangular channel: y = (q·n/(K·√S))^(3/5).</summary>
    public static double WideDepth(double q, double n, double s, double k) => Math.Pow(q * n / (k * Math.Sqrt(s)), 0.6);

    /// <summary>Equilibrium slope at incipient motion of size d (length units of q): Seq = [F*(Ss − 1)d]^(10/7)/(q·n/K)^(6/7).</summary>
    public static double EquilibriumSlope(double q, double n, double k, double dLen, double shields, double ss) =>
        Math.Pow(shields * (ss - 1) * dLen, 10.0 / 7) / Math.Pow(q * n / k, 6.0 / 7);

    /// <summary>Leopold–Wolman (1957) slope dividing meandering from braided channels, S = 0.06·Q^−0.44 with Q in cfs.</summary>
    public static double BraidingSlope(double qCfs) => 0.06 * Math.Pow(qCfs, -0.44);

    /// <summary>Percent finer at a size, log-linear interpolation of the gradation.</summary>
    public static double PercentFiner(IReadOnlyList<(double Size, double Finer)> g, double size)
    {
        if (size <= g[0].Size) return g[0].Finer * (g[0].Size > 0 ? Math.Min(1, size / g[0].Size) : 1);
        if (size >= g[^1].Size) return g[^1].Finer;
        int i = 1; while (g[i].Size < size) i++;
        var (s0, f0) = g[i - 1]; var (s1, f1) = g[i];
        return f0 + (f1 - f0) * Math.Log(size / s0) / Math.Log(s1 / s0);
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Stream stability (HEC-20 Level 2)";
        bool si = context.Units == UnitSystem.SI;
        double k = si ? 1.0 : 1.486, gamma = si ? 9810 : 62.4, mmToLen = si ? 0.001 : 1 / 304.8;
        string L = si ? "m" : "ft", Qu = si ? "m³/s" : "cfs", Tu = si ? "Pa" : "lb/ft²", Pu = si ? "W/m²" : "lb/(ft·s)";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double q = p.Number("Q", double.NaN), w = p.Number("width", double.NaN), s0 = p.Number("slope", double.NaN), n = p.Number("n", double.NaN);
        double d50 = p.Number("D50", double.NaN), shields = p.Number("Shields", 0.047), ss = p.Number("specific gravity", 2.65), mult = p.Number("armor multiple", 2);
        double qBank = p.Number("bankfull Q", q);
        double? sin = p.OptionalNumber("sinuosity"), ctrl = p.OptionalNumber("control distance");
        var grad = new List<(double, double)>();
        foreach (var r in p.Rows("Gradation"))
            if (p.Cell(r, 0, "size", "[Gradation]") is { } sz && p.Cell(r, 1, "percent finer", "[Gradation]") is { } pf) grad.Add((sz, pf));
        if (!(q > 0) || !(w > 0) || !(s0 > 0) || !(n > 0)) msgs.Add(CalcMessage.Error("E-SS-001", "Give Q, width, slope and n above zero."));
        if (!(d50 > 0) && grad.Count == 0) msgs.Add(CalcMessage.Error("E-SS-002", "Give D50 (mm) or a [Gradation] table."));
        if (!(shields > 0) || !(ss > 1) || !(mult > 0) || !(qBank > 0)) msgs.Add(CalcMessage.Error("E-SS-003", "Shields, armor multiple and bankfull Q must be above zero and the specific gravity above 1."));
        if (sin is < 1 || ctrl is <= 0) msgs.Add(CalcMessage.Error("E-SS-004", "Sinuosity must be at least 1 and the control distance above zero."));
        if (grad.Count is 1 || grad.Zip(grad.Skip(1)).Any(t => t.Second.Item1 <= t.First.Item1 || t.Second.Item2 < t.First.Item2) || grad.Any(g => g.Item1 <= 0 || g.Item2 is < 0 or > 100))
            msgs.Add(CalcMessage.Error("E-SS-005", "[Gradation] needs at least two rows with sizes increasing and percent finer not decreasing (0 to 100)."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        if (!(d50 > 0)) d50 = SizeAt(grad, 50);

        double qUnit = q / w, y = WideDepth(qUnit, n, s0, k), v = qUnit / y, g = si ? 9.80665 : 32.174, fr = v / Math.Sqrt(g * y);
        double tau = gamma * y * s0, dcLen = tau / (shields * (ss - 1) * gamma), dcMm = dcLen / mmToLen;
        double tauC50 = shields * (ss - 1) * gamma * d50 * mmToLen;
        double seq = EquilibriumSlope(qUnit, n, k, d50 * mmToLen, shields, ss);
        double power = gamma * q * s0 / w;
        double braid = BraidingSlope(si ? qBank * 35.3147 : qBank);
        double wd = w / y;
        var summary = new List<string>
        {
            $"Depth {y:F2} {L}, velocity {v:F2} {(si ? "m/s" : "ft/s")}, bed shear {tau:F2} {Tu} against {tauC50:F2} {Tu} to move D50 {d50:G3} mm: the bed {(tau > tauC50 ? "moves" : "is stable")} at this flow.",
        };
        List<IReadOnlyList<object?>> rows =
        [
            ["Unit discharge q = Q/W", R(qUnit, 3), si ? "m²/s" : "ft²/s"], ["Normal depth (wide channel)", R(y, 3), L], ["Velocity", R(v, 3), si ? "m/s" : "ft/s"],
            ["Froude number", R(fr, 3), ""], ["Bed shear τ = γ·y·S0", R(tau, 3), Tu], [$"Critical shear of D50 ({d50:G3} mm)", R(tauC50, 3), Tu],
            ["Critical size Dc = τ/(F*·(Ss − 1)·γ)", R(dcMm, 1), "mm"], ["Equilibrium slope Seq for D50", R(seq, 6), $"{L}/{L}"],
            .. (p.OptionalNumber("D90") is > 0 and var d90 ? new List<IReadOnlyList<object?>> { new object?[] { $"Equilibrium slope Seq for D90 ({d90:G3} mm, HEC-20 Eq. 6.17 as in Example 2)", R(EquilibriumSlope(qUnit, n, k, d90 * mmToLen, shields, ss), 6), $"{L}/{L}" } } : []),
            ["Specific stream power γ·Q·S/W", R(power, 2), Pu], ["Width–depth ratio", R(wd, 1), ""],
            [$"Leopold–Wolman braiding slope at bankfull {qBank:G4} {Qu}", R(braid, 5), $"{L}/{L}"],
        ];
        if (fr > 1) msgs.Add(CalcMessage.Warning("W-SS-001", $"Froude number {fr:F2}: supercritical flow in a movable bed channel is unusual; check n and the slope."));
        if (wd < 10) msgs.Add(CalcMessage.Info("I-SS-001", $"Width–depth ratio {wd:F1} is low; the wide-channel depth (hydraulic radius = depth) overstates the depth."));

        // Degradation to the equilibrium slope above a downstream control.
        if (seq < s0)
        {
            string deg = $"The reach is steeper than the equilibrium slope for D50 ({seq:G3} < {s0:G3}).";
            if (ctrl is { } cd)
            {
                double dz = (s0 - seq) * cd;
                rows.Add(["Degradation at the upstream end (S0 − Seq)·L", R(dz, 2), L]);
                deg += $" Degrading to Seq would lower the bed up to {dz:F2} {L} at {cd:G4} {L} above the control (unless armoring stops it first).";
            }
            summary.Add(deg);
        }
        else summary.Add($"The slope is at or below the equilibrium slope for D50 ({seq:G3}): no degradation at this flow.");

        // Armoring.
        if (grad.Count >= 2)
        {
            double pc = 1 - PercentFiner(grad, dcMm) / 100;   // fraction coarser than Dc
            rows.Add([$"Fraction coarser than Dc ({dcMm:F1} mm)", R(pc, 3), ""]);
            if (pc <= 0) { msgs.Add(CalcMessage.Warning("W-SS-002", "No part of the bed is coarser than Dc: the bed cannot armor at this flow.")); summary.Add("The bed cannot armor at this flow."); }
            else
            {
                double ya = mult * dcLen, ysArm = ya * (1 / pc - 1);
                rows.Add(["Armor layer thickness ya", R(ya, 3), L]);
                rows.Add(["Degradation to armor ys = ya·(1/Pc − 1)", R(ysArm, 2), L]);
                summary.Add($"Armoring would stop degradation after {ysArm:F2} {L} ({pc * 100:F0} % of the bed is coarser than Dc).");
            }
        }

        // Planform.
        var plan = new List<string>();
        plan.Add(s0 > braid ? $"Slope {s0:G3} is above the Leopold–Wolman threshold {braid:G3}: braided tendency." : $"Slope {s0:G3} is below the Leopold–Wolman threshold {braid:G3}: meandering or straight.");
        if (sin is { } sn) plan.Add(sn < 1.05 ? "Sinuosity below 1.05: straight." : sn < 1.5 ? $"Sinuosity {sn:G3}: sinuous." : $"Sinuosity {sn:G3}: meandering; check bend migration at the crossing.");
        summary.AddRange(plan);

        var tables = new List<ToolTable>
        {
            new("Reach hydraulics and bed stability", ["Item", "Value", "Unit"], rows,
            [
                $"Wide channel: y = (q·n/(K·√S0))^(3/5), K = {k:G4}. Shields F* = {shields:G3}, Ss = {ss:G3}.",
                "Seq from setting the bed shear of the normal depth equal to the critical shear of D50 (HEC-20 Ch. 5 incipient motion).",
                "Armoring: ys = ya·(1/Pc − 1), Pc = fraction of the bed coarser than Dc, ya = armor multiple × Dc.",
                "Leopold and Wolman (1957): S = 0.06·Q^−0.44 (Q bankfull, cfs).",
            ]),
        };

        // Lane's relation.
        if (p.HasSection("Change"))
        {
            double rq = p.Number("Change.Q", 1), rqs = p.Number("Change.Qs", 1), rd = p.Number("Change.D50", 1), rs = p.Number("Change.S", 1);
            if (new[] { rq, rqs, rd, rs }.Any(x => !(x > 0))) msgs.Add(CalcMessage.Error("E-SS-006", "[Change] ratios must be above zero."));
            else
            {
                double supply = rqs * rd, capacity = rq * rs;
                string tend = Math.Abs(supply / capacity - 1) < 0.05 ? "stays in balance" : supply > capacity ? "aggrades (more sediment than the flow can carry)" : "degrades (the flow can carry more sediment than it gets)";
                tables.Add(new("Lane's relation", ["Term", "Ratio new / existing"],
                    [["Qs·D50 (supply side)", R(supply, 3)], ["Q·S (transport side)", R(capacity, 3)], ["Supply / transport", R(supply / capacity, 3)]],
                    ["Lane (1955): Qs·D50 ∝ Q·S. A qualitative tendency, not a rate."]));
                summary.Add($"Lane's relation: the reach {tend}.");
            }
        }
        return new ToolResult(title, summary, tables, msgs);
    }

    /// <summary>Size (mm) at a percent finer, log-linear between gradation points.</summary>
    public static double SizeAt(IReadOnlyList<(double Size, double Finer)> g, double pct)
    {
        if (pct <= g[0].Finer) return g[0].Size;
        if (pct >= g[^1].Finer) return g[^1].Size;
        int i = 1; while (g[i].Finer < pct) i++;
        var (s0, f0) = g[i - 1]; var (s1, f1) = g[i];
        return f1 == f0 ? s1 : s0 * Math.Pow(s1 / s0, (pct - f0) / (f1 - f0));
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
