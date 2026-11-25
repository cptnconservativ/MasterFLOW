using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Peak discharge of a past flood from surveyed cross-sections and high-water marks, by the slope-area method of
/// Dalrymple and Benson (1967), USGS TWRI Book 3 Ch. A2: conveyance by subsection (left overbank, channel, right overbank),
/// energy slope from the water-surface fall corrected for the change in velocity head (full recovery in contracting
/// reaches, half in expanding), solved for each reach and for the whole reach.
/// </summary>
public sealed class SlopeAreaTool : IDesignTool
{
    public string Id => "slope-area";
    public string Name => "Slope-area peak discharge from high-water marks";
    public string Description => "USGS TWRI 3-A2 (Dalrymple and Benson 1967): K = Σ (k/n)·A·R^(2/3) by subsection, α from the subsections, and for each reach Q = √(Ku·Kd)·√[(Δh + (1 − c)·Δhv)/L] with Δhv = hu − hd and c = 0 contracting, 0.5 expanding; solved for each reach and for all reaches together (Σ Q²·L/(Ku·Kd) = total fall + recovered velocity head). Flags the USGS reach criteria (fall, conveyance ratio, Froude number).";
    public bool UsesNetwork => false;

    /// <summary>Hydraulic properties of a section at a water surface: area, conveyance, α, top width and the channel's Froude number.</summary>
    public sealed record Props(double Area, double K, double Alpha, double TopWidth, double HydraulicDepth);

    /// <summary>
    /// Area, wetted perimeter and conveyance of each subsection below <paramref name="ws"/>. Subsections are split at the
    /// bank stations; <paramref name="n"/> has one value per subsection (left to right).
    /// </summary>
    public static Props Section(IReadOnlyList<(double X, double Z)> pts, double ws, IReadOnlyList<double> banks, IReadOnlyList<double> n, double k)
    {
        // Insert points at bank stations so every segment lies in one subsection.
        var p = new List<(double X, double Z)>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0)
                foreach (var b in banks)
                    if (b > pts[i - 1].X && b < pts[i].X)
                        p.Add((b, pts[i - 1].Z + (pts[i].Z - pts[i - 1].Z) * (b - pts[i - 1].X) / (pts[i].X - pts[i - 1].X)));
            p.Add(pts[i]);
        }
        int subs = banks.Count + 1;
        var a = new double[subs]; var wp = new double[subs];
        double top = 0;
        for (int i = 1; i < p.Count; i++)
        {
            var (x1, z1) = p[i - 1]; var (x2, z2) = p[i];
            if (z1 >= ws && z2 >= ws) continue;
            // Clip the segment to the part below the water surface.
            if (z1 > ws) { x1 += (x2 - x1) * (z1 - ws) / (z1 - z2); z1 = ws; }
            else if (z2 > ws) { x2 = x1 + (x2 - x1) * (ws - z1) / (z2 - z1); z2 = ws; }
            double mid = 0.5 * (x1 + x2);
            int s = banks.Count(b => mid > b);
            a[s] += (x2 - x1) * (ws - 0.5 * (z1 + z2));
            wp[s] += Math.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
            top += x2 - x1;
        }
        double at = 0, kt = 0, k3a2 = 0;
        for (int s = 0; s < subs; s++)
        {
            if (a[s] <= 0 || wp[s] <= 0) continue;
            double ks = k / n[s] * a[s] * Math.Pow(a[s] / wp[s], 2.0 / 3);
            at += a[s]; kt += ks; k3a2 += ks * ks * ks / (a[s] * a[s]);
        }
        double alpha = kt > 0 ? k3a2 / (kt * kt * kt / (at * at)) : double.NaN;
        return new Props(at, kt, alpha, top, top > 0 ? at / top : double.NaN);
    }

    /// <summary>One section as used in the solution: properties at its high-water elevation and the reach length to the next section downstream.</summary>
    public sealed record Xs(string Name, double Ws, double Length, Props P);

    /// <summary>
    /// Discharge through sections <paramref name="xs"/> (upstream to downstream). Velocity heads grow with Q², so the energy
    /// balance Q²·Σ Li/(Ki·Ki+1) = fall + Q²·Σ ci·(αu/2gAu² − αd/2gAd²) is solved directly (ci = 0.5 expanding, 1 contracting).
    /// Returns NaN when the energy slope is not positive.
    /// </summary>
    public static double Solve(IReadOnlyList<Xs> xs, double g)
    {
        double fall = xs[0].Ws - xs[^1].Ws;
        if (!(fall > 0)) return double.NaN;
        double sumLk = 0, rec = 0;
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            sumLk += xs[i].Length / (xs[i].P.K * xs[i + 1].P.K);
            double dc = Hv(xs[i].P, 1, g) - Hv(xs[i + 1].P, 1, g);       // Δhv per unit Q²
            rec += (dc > 0 ? 0.5 : 1.0) * dc;                              // expanding reach (velocity falls): half recovered
        }
        double den = sumLk - rec;
        return den > 0 ? Math.Sqrt(fall / den) : double.NaN;
    }

    /// <summary>Velocity head α·V²/2g.</summary>
    public static double Hv(Props p, double q, double g) => p.Alpha * Math.Pow(q / p.Area, 2) / (2 * g);

    public string SampleInput(DesignToolContext context)
    {
        bool si = context.Units == UnitSystem.SI;
        double f = si ? 0.3048 : 1;
        string F(double v) => (v * f).ToString(si ? "0.###" : "0.##", CultureInfo.InvariantCulture);
        var head = si ? "# Slope-area method. SI: stations, elevations and lengths in m.\n" : """
            # Slope-area peak discharge (USGS TWRI 3-A2). U.S.: stations, elevations and lengths in ft.
            # One [XS name] block per cross-section, upstream to downstream (at least two; three or more is preferred).
            # ws = high-water elevation at the section (average of the marks on both banks); length = reach length to the next section downstream.
            # n = Manning n for each subsection left to right; banks = the stations that split the subsections.

            """;
        string Block(string name, double ws, double len, double shift) => $"[{name}]\nws = {F(ws)}\nlength = {F(len)}\nn = 0.045, 0.035, 0.050\nbanks = {F(30)}, {F(70)}\nStation\tElevation\n"
            + string.Join("\n", new[] { (0.0, 108.0), (30.0, 104.5), (35.0, 100.0), (65.0, 99.5), (70.0, 104.0), (100.0, 107.5) }.Select(pt => $"{F(pt.Item1)}\t{F(pt.Item2 + shift)}")) + "\n";
        return head + Block("XS 1", 105.30, 200, 0.0) + "\n" + Block("XS 2", 104.95, 210, -0.30) + "\n" + Block("XS 3", 104.62, 0, -0.62);
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Slope-area peak discharge";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.80665 : 32.174, k = si ? 1.0 : 1.486;
        string L = si ? "m" : "ft", Uq = si ? "m³/s" : "cfs", A = si ? "m²" : "ft²";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        var xs = new List<Xs>();
        foreach (var name in p.Sections)
        {
            double ws = p.Number(name + ".ws", double.NaN, $"{name}: ws"), len = p.Number(name + ".length", 0, $"{name}: length");
            var nn = (p.Text(name + ".n") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => ToolInput.TryNumber(s, out double v) ? v : double.NaN).ToList();
            var banks = (p.Text(name + ".banks") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => ToolInput.TryNumber(s, out double v) ? v : double.NaN).ToList();
            var pts = new List<(double, double)>();
            foreach (var r in p.Rows(name))
            {
                double? x = p.Cell(r, 0, "station", name), z = p.Cell(r, 1, "elevation", name);
                if (x is { } xx && z is { } zz) pts.Add((xx, zz));
            }
            if (double.IsNaN(ws)) { msgs.Add(CalcMessage.Error("E-SA-001", $"{name}: give ws, the high-water elevation.")); continue; }
            if (pts.Count < 3 || pts.Zip(pts.Skip(1)).Any(t => t.Second.Item1 < t.First.Item1)) { msgs.Add(CalcMessage.Error("E-SA-002", $"{name}: give at least three station–elevation points with stations increasing.")); continue; }
            if (banks.Any(double.IsNaN) || banks.Zip(banks.Skip(1)).Any(t => t.Second <= t.First)) { msgs.Add(CalcMessage.Error("E-SA-003", $"{name}: banks must be increasing station numbers.")); continue; }
            if (nn.Count != banks.Count + 1 || nn.Any(v => !(v > 0))) { msgs.Add(CalcMessage.Error("E-SA-004", $"{name}: give {banks.Count + 1} Manning n value(s), one per subsection, all above zero.")); continue; }
            if (ws > pts[0].Item2 || ws > pts[^1].Item2) { msgs.Add(CalcMessage.Error("E-SA-005", $"{name}: the high-water elevation is above an end of the section; extend the section.")); continue; }
            if (len < 0) { msgs.Add(CalcMessage.Error("E-SA-006", $"{name}: the reach length cannot be negative.")); continue; }
            var pr = Section(pts, ws, banks, nn, k);
            if (!(pr.Area > 0)) { msgs.Add(CalcMessage.Error("E-SA-007", $"{name}: the high-water elevation is below the whole section.")); continue; }
            xs.Add(new Xs(name, ws, len, pr));
        }
        if (xs.Count < 2 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-SA-008", "Give at least two cross-sections ([XS 1], [XS 2], …) upstream to downstream."));
        if (xs.Take(xs.Count - 1).Any(x => !(x.Length > 0))) msgs.Add(CalcMessage.Error("E-SA-009", "Every section but the last needs the reach length to the next section downstream."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double qAll = Solve(xs, g);
        var srows = xs.Select(x => (IReadOnlyList<object?>)[x.Name, R(x.Ws, 2), R(x.P.Area, 1), R(x.P.TopWidth, 1), R(x.P.K, 0), R(x.P.Alpha, 3),
            double.IsNaN(qAll) ? null : R(qAll / x.P.Area, 2), double.IsNaN(qAll) ? null : R(Hv(x.P, qAll, g), 3),
            double.IsNaN(qAll) ? null : R(qAll / x.P.Area / Math.Sqrt(g * x.P.HydraulicDepth), 2), x.Length > 0 ? R(x.Length, 1) : null]).ToList();
        var rrows = new List<IReadOnlyList<object?>>();
        var summary = new List<string>();
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            var u = xs[i]; var d = xs[i + 1];
            double q = Solve([u, d], g);
            double fall = u.Ws - d.Ws, ratio = u.P.K / d.P.K;
            string kind = double.IsNaN(q) ? "" : Hv(u.P, q, g) > Hv(d.P, q, g) ? "expanding" : "contracting";
            var issues = new List<string>();
            if (!(fall > 0)) issues.Add("no fall");
            else if (fall < (si ? 0.15 : 0.5) && !double.IsNaN(q) && fall < Hv(u.P, q, g) && u.Length < 75 * u.P.HydraulicDepth) issues.Add("fall under 0.5 ft and the velocity head, reach under 75 depths");
            if (ratio is < 0.8 or > 1.25) issues.Add($"Ku/Kd = {ratio:F2} outside 0.8–1.25");
            if (kind == "expanding" && ratio < 1) issues.Add("expanding reach");
            rrows.Add([$"{u.Name} – {d.Name}", R(u.Length, 1), R(fall, 3), R(ratio, 3), kind, double.IsNaN(q) ? null : R(q, si ? 2 : 0), issues.Count == 0 ? "meets criteria" : string.Join("; ", issues)]);
            if (issues.Count > 0) msgs.Add(CalcMessage.Warning("W-SA-001", $"{u.Name} – {d.Name}: {string.Join("; ", issues)} (USGS TWRI 3-A2 reach criteria). Rate the result poor or choose other sections."));
            if (double.IsNaN(q)) msgs.Add(CalcMessage.Warning("W-SA-002", $"{u.Name} – {d.Name}: the energy slope is not positive; no discharge for this reach."));
        }
        if (double.IsNaN(qAll)) msgs.Add(CalcMessage.Error("E-SA-010", "The water surface does not fall through the reach once velocity heads are counted; check the high-water marks."));
        else
        {
            summary.Add($"Peak discharge Q = {qAll:F0} {Uq} through {xs.Count} sections ({xs[0].Name} to {xs[^1].Name}, fall {xs[0].Ws - xs[^1].Ws:F2} {L}).");
            var qs = rrows.Select(r => r[5]).OfType<double>().ToList();
            if (qs.Count > 1) summary.Add($"Reach discharges range {qs.Min():F0} to {qs.Max():F0} {Uq}; a spread over about 10 % points to a poor mark or an n value to revisit.");
            foreach (var x in xs)
                if (qAll / x.P.Area / Math.Sqrt(g * x.P.HydraulicDepth) > 1)
                    msgs.Add(CalcMessage.Warning("W-SA-003", $"{x.Name}: Froude number above 1 at the computed discharge; the slope-area method assumes subcritical flow, and high-water marks in supercritical flow are unreliable."));
        }
        var tables = new List<ToolTable>
        {
            new("Sections at the high-water marks", ["Section", $"WS ({L})", $"Area ({A})", $"Top width ({L})", "K", "α", $"V ({(si ? "m/s" : "ft/s")})", $"αV²/2g ({L})", "Froude", $"Length to next ({L})"], srows,
                [$"K = Σ ({k:G4}/n)·A·R^(2/3) by subsection; α = Σ(Ki³/Ai²)/(Kt³/At²). V, velocity head and Froude at the all-reach discharge."]),
            new("Reaches", ["Reach", $"Length ({L})", $"Fall ({L})", "Ku/Kd", "Type", $"Q ({Uq})", "Criteria"], rrows,
                ["Q = √(Ku·Kd)·√[(Δh + (1 − c)·Δhv)/L], Δhv = hv,u − hv,d; c = 0.5 in expanding reaches (Δhv > 0), 0 in contracting reaches.",
                 "USGS criteria: fall at least 0.5 ft or the velocity head, or a reach of 75 mean depths; conveyance ratio 0.8–1.25; contracting reaches preferred."]),
        };
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
