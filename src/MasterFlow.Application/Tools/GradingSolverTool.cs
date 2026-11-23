using System.Globalization;
using System.Text.RegularExpressions;
using MasterFlow.Application.Grading;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Grading solver (premium, D-260). Given existing ground on a grid and the grading requirements (tie-ins, building pads,
/// drainage away from buildings, ADA running and cross slopes, driveway and lawn slope limits, cut/fill limits), solves for
/// the finished grades that stay closest to existing ground and balance cut and fill (with shrink), and flags every spot
/// where the requirements cannot all be met.
/// </summary>
public sealed partial class GradingSolverTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public string Id => "grading-solver";
    public string Name => "Grading solver (finished grades from ADA, driveway and drainage constraints, balanced earthwork)";
    public string Description => "Solves for finished grades on a grid: building pads, tie-ins to existing ground, positive drainage away from buildings, ADA running and cross slopes, driveway, lawn and swale slope limits, maximum cut and fill, and the cut/fill balance with shrink. Stays as close to existing ground as the constraints allow, reports cut, fill and the balance, and lists every spot where the requirements cannot all be met and by how much.";
    public bool UsesNetwork => false;

    // Penalty tiers (D-261): a requirement is given up only when a higher tier needs it.
    // Weights are normalized to the mean grid-point area, so a penalty is the cost of a 1 ft move of a typical point.
    // Tie-ins are not constraints: their points get a very large weight and the tie-in elevation as their target.
    private const double Hard = 1e5, Firm = 1e3, Normal = 1e2, Soft = 3, Pinned = 1e7;

    public string SampleInput(DesignToolContext context)
    {
        // A 200 × 150 ft lot falling about 4 % to the south-east with a low swale through the middle.
        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < 16; r++)
        {
            var cells = new List<string>();
            for (int c = 0; c < 21; c++)
            {
                double x = c * 10, y = r * 10;
                double e = 3460 - 0.035 * y - 0.012 * x + 0.9 * Math.Exp(-Math.Pow((x - 100) / 30, 2)) * (y < 90 ? 1 : 0) - 0.6 * Math.Exp(-Math.Pow((x - 150) / 15, 2));
                cells.Add(e.ToString("0.00", Inv));
            }
            sb.AppendLine(string.Join("\t", cells));
        }
        return $"""
            # Grading solver. Existing ground on a grid (row 1 is the north edge, column A the west edge); grid points are
            # named like a spreadsheet (A1, B1 … ). Each requirement under [Constraints] applies to a range (A1:D4; several
            # ranges with ;). Types:
            #   fix    Range  Value = existing | elevation              tie-in (property line, back of walk)
            #   pad    Range  Value = elevation (optional), Min, Max      flat building pad (finished floor)
            #   away   Range  Value = slope % (default 5), Distance ft    ground falls away from the pad (IBC 1804.4: 6 in. in 10 ft)
            #   slope  Range  Min %, Max %, Direction (north/south/east/west, the way it falls; or any = Max both ways)
            #   cross  Range  Max %, Direction (of travel)                cross slope (ADA 2 %)
            # Grades stay as close to existing as the constraints allow; cut and fill balance after shrink.
            spacing = 10          # ft between grid points
            shrink = 15           # % of cut lost when compacted as fill
            import = 0            # cy of fill brought in (negative: export)
            max cut = 5           # ft
            max fill = 5          # ft
            section row = 9       # the east-west section drawn

            [Constraints]
            Type	Range	Value	Min	Max	Direction	Note
            fix	A1:U1; A16:U16	existing				property line north; street back of walk south
            pad	G4:L8		3458.0	3460.0		house; finished floor 0.67 ft above pad
            away	G4:L8	5				drainage away from the house
            slope	M9:N15		1	12	south		driveway
            cross	M9:N15			4	south		driveway cross slope
            slope	H9:I15		1	5	south		front walk (ADA running slope)
            cross	H9:I15			2	south		front walk (ADA cross slope)
            slope	A2:F15; O2:U15			25	any		lawn, 4:1 maximum

            [Existing]
            {sb.ToString().TrimEnd()}
            """;
    }

    private sealed record Req(int Row, string Type, string Range, List<int> Nodes, string? Value, double? Min, double? Max, string Dir, string Note);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Grading solver";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors.Where(e => !e.Text.Contains("[Existing]")));
        bool si = context.Units == UnitSystem.SI;
        string lu = si ? "m" : "ft";
        double h = p.Number("spacing", si ? 3 : 10), shrink = p.Number("shrink", 15) / 100, import = p.Number("import", 0);
        double? maxCut = p.OptionalNumber("max cut"), maxFill = p.OptionalNumber("max fill");
        double volUnit = si ? 1 : 27;      // ft³ per cy, m³ per m³
        string vu = si ? "m³" : "cy";

        // ---- existing grid
        var gridRows = new List<string[]>();
        var head = p.Header("Existing");
        if (head.Count > 0) gridRows.Add([.. head]);
        gridRows.AddRange(p.Rows("Existing"));
        gridRows = gridRows.Where(r => r.Length > 0 && r.Any(c => c.Trim().Length > 0)).ToList();
        int R = gridRows.Count, C = R == 0 ? 0 : gridRows.Max(r => r.Count(c => c.Trim().Length > 0));
        if (R < 2 || C < 2) return ToolResult.Failed(title, CalcMessage.Error("E-GS-001", "Give existing ground under [Existing]: one line per grid row (north to south), elevations west to east, at least 2 × 2."));
        if (R * C > 6000) return ToolResult.Failed(title, CalcMessage.Error("E-GS-002", $"{R} × {C} = {R * C} grid points; at most 6,000 (use a larger spacing)."));
        if (!(h > 0)) return ToolResult.Failed(title, CalcMessage.Error("E-GS-003", "spacing must be greater than 0."));
        var e = new double[R * C];
        for (int r = 0; r < R; r++)
        {
            var cells = gridRows[r].Where(c => c.Trim().Length > 0).ToArray();
            if (cells.Length != C) return ToolResult.Failed(title, CalcMessage.Error("E-GS-004", $"[Existing] row {r + 1} has {cells.Length} elevations; every row needs {C}."));
            for (int c = 0; c < C; c++)
                if (!double.TryParse(cells[c].Trim(), NumberStyles.Float, Inv, out e[r * C + c])) return ToolResult.Failed(title, CalcMessage.Error("E-GS-004", $"[Existing] row {r + 1}: \"{cells[c]}\" is not an elevation."));
        }
        int Id(int r, int c) => r * C + c;
        string NameOf(int i) => Col(i % C) + (i / C + 1).ToString(Inv);
        // Trapezoid-rule area each grid point stands for.
        var area = new double[R * C];
        for (int r = 0; r < R; r++) for (int c = 0; c < C; c++) area[Id(r, c)] = h * h * (r == 0 || r == R - 1 ? 0.5 : 1) * (c == 0 || c == C - 1 ? 0.5 : 1);

        // ---- requirements
        var reqs = new List<Req>();
        var ch = p.Header("Constraints").Select(x => x.Trim().ToLowerInvariant()).ToList();
        int K(string name) => ch.IndexOf(name);
        int kT = K("type"), kR = K("range"), kV = K("value"), kMin = K("min"), kMax = K("max"), kD = K("direction"), kN = K("note");
        if (p.Rows("Constraints").Count > 0 && (kT < 0 || kR < 0)) return ToolResult.Failed(title, CalcMessage.Error("E-GS-005", "[Constraints] needs a header row with at least Type and Range (and Value, Min, Max, Direction, Note as used)."));
        int row = 0;
        foreach (var x in p.Rows("Constraints"))
        {
            row++;
            string? Get(int k) => k >= 0 && k < x.Length && x[k].Trim().Length > 0 ? x[k].Trim() : null;
            double? Num(int k, string what)
            {
                var s = Get(k);
                if (s is null) return null;
                if (double.TryParse(s, NumberStyles.Float, Inv, out var v)) return v;
                msgs.Add(CalcMessage.Error("E-GS-006", $"[Constraints] row {row}: {what} \"{s}\" is not a number.")); return null;
            }
            string type = (Get(kT) ?? "").ToLowerInvariant(), range = Get(kR) ?? "";
            var nodes = Range(range, R, C, out var bad);
            if (bad is not null) { msgs.Add(CalcMessage.Error("E-GS-007", $"[Constraints] row {row}: {bad}")); continue; }
            if (type is not ("fix" or "pad" or "away" or "slope" or "cross")) { msgs.Add(CalcMessage.Error("E-GS-008", $"[Constraints] row {row}: type \"{type}\"; use fix, pad, away, slope or cross.")); continue; }
            reqs.Add(new(row, type, range, nodes, Get(kV), Num(kMin, "Min"), Num(kMax, "Max"), (Get(kD) ?? "any").ToLowerInvariant(), Get(kN) ?? ""));
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);

        // ---- constraints
        var cons = new List<GradeConstraint>();
        var pinned = new Dictionary<int, double>();
        void Le(string g, int a, int b, double bound, double pen, string where) => cons.Add(new(g, [a, b], [1, -1], bound, false, pen, where));   // z_a − z_b ≤ bound
        (int dr, int dc) Step(string dir) => dir switch { "north" or "n" => (-1, 0), "south" or "s" => (1, 0), "east" or "e" => (0, 1), "west" or "w" => (0, -1), _ => (0, 0) };
        var groups = new List<(string Key, Req Req, string Requirement, string Unit)>();
        foreach (var q in reqs)
        {
            string g = $"{q.Row}. {q.Type} {q.Range}" + (q.Note.Length > 0 ? $" ({q.Note})" : "");
            var set = q.Nodes.ToHashSet();
            switch (q.Type)
            {
                case "fix":
                    foreach (var i in q.Nodes)
                    {
                        double v = q.Value is null || q.Value.Equals("existing", StringComparison.OrdinalIgnoreCase) ? e[i] : double.TryParse(q.Value, NumberStyles.Float, Inv, out var fv) ? fv : double.NaN;
                        if (double.IsNaN(v)) { msgs.Add(CalcMessage.Error("E-GS-009", $"[Constraints] row {q.Row}: fix needs Value = existing or an elevation.")); break; }
                        pinned[i] = v;
                        cons.Add(new(g, [i], [1], v, true, 0, NameOf(i)));   // penalty 0: reported, never acted on
                    }
                    groups.Add((g, q, q.Value is null || q.Value.Equals("existing", StringComparison.OrdinalIgnoreCase) ? "= existing ground" : $"= {q.Value}", lu));
                    break;
                case "pad":
                {
                    int first = q.Nodes[0];
                    foreach (var i in q.Nodes.Skip(1)) cons.Add(new(g, [i, first], [1, -1], 0, true, Hard, NameOf(i)));
                    if (q.Value is not null && double.TryParse(q.Value, NumberStyles.Float, Inv, out var pv)) cons.Add(new(g, [first], [1], pv, true, Hard, NameOf(first)));
                    if (q.Min is { } mn) cons.Add(new(g, [first], [-1], -mn, false, Firm, NameOf(first) + " ≥ min"));
                    if (q.Max is { } mx) cons.Add(new(g, [first], [1], mx, false, Firm, NameOf(first) + " ≤ max"));
                    groups.Add((g, q, "flat" + (q.Value is not null ? $" at {q.Value}" : "") + (q.Min is not null ? $", ≥ {q.Min}" : "") + (q.Max is not null ? $", ≤ {q.Max}" : ""), lu));
                    break;
                }
                case "away":
                {
                    double s = (q.Value is not null && double.TryParse(q.Value, NumberStyles.Float, Inv, out var sv) ? sv : 5) / 100;
                    double dist = q.Min is { } dd && dd > 0 ? dd : (si ? 3 : 10);
                    int steps = Math.Max(1, (int)Math.Ceiling(dist / h - 1e-9));
                    foreach (var i in q.Nodes)
                        foreach (var (dr, dc) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                        {
                            int r0 = i / C, c0 = i % C;
                            if (set.Contains(Id(Math.Clamp(r0 + dr, 0, R - 1), Math.Clamp(c0 + dc, 0, C - 1))) && (r0 + dr is >= 0 && r0 + dr < R) && (c0 + dc is >= 0 && c0 + dc < C)) continue;  // interior side
                            for (int k = 1; k <= steps; k++)
                            {
                                int r1 = r0 + dr * k, c1 = c0 + dc * k;
                                if (r1 < 0 || r1 >= R || c1 < 0 || c1 >= C || set.Contains(Id(r1, c1))) break;
                                Le(g, Id(r1, c1), i, -s * h * k, Firm, $"{NameOf(Id(r1, c1))} below {NameOf(i)}");
                            }
                        }
                    groups.Add((g, q, $"falls ≥ {s * 100:0.#} % for {Math.Min(dist, steps * h):0.#} {lu}", "%"));
                    break;
                }
                case "slope":
                case "cross":
                {
                    var (dr, dc) = Step(q.Dir);
                    if (q.Type == "cross") { if (dr == 0 && dc == 0) { msgs.Add(CalcMessage.Error("E-GS-010", $"[Constraints] row {q.Row}: cross needs the Direction of travel.")); break; } (dr, dc) = (dc, dr); }
                    if (q.Type == "slope" && dr == 0 && dc == 0)
                    {
                        if (q.Max is not { } amax) { msgs.Add(CalcMessage.Error("E-GS-010", $"[Constraints] row {q.Row}: slope any needs Max.")); break; }
                        if (q.Min is not null) msgs.Add(CalcMessage.Warning("W-GS-001", $"[Constraints] row {q.Row}: Min needs a direction to fall in; ignored for any."));
                        foreach (var i in q.Nodes)
                            foreach (var (ar, ac) in new[] { (0, 1), (1, 0) })
                            {
                                int r1 = i / C + ar, c1 = i % C + ac;
                                if (r1 >= R || c1 >= C || !set.Contains(Id(r1, c1))) continue;
                                Le(g, i, Id(r1, c1), amax / 100 * h, Normal, $"{NameOf(i)}–{NameOf(Id(r1, c1))}");
                                Le(g, Id(r1, c1), i, amax / 100 * h, Normal, $"{NameOf(i)}–{NameOf(Id(r1, c1))}");
                            }
                        groups.Add((g, q, $"≤ {amax:0.##} % any direction", "%"));
                        break;
                    }
                    double? lo = q.Type == "cross" ? -(q.Max ?? 2) : q.Min, hi = q.Type == "cross" ? q.Max ?? 2 : q.Max;
                    if (q.Type == "slope" && lo is null && hi is { } hh) lo = -hh;
                    double pen = q.Type == "cross" || (hi is { } hv && hv <= 8.33) ? Firm : Normal;     // ADA routes outrank lawn and driveway
                    foreach (var i in q.Nodes)
                    {
                        int r1 = i / C + dr, c1 = i % C + dc;
                        if (r1 < 0 || r1 >= R || c1 < 0 || c1 >= C || !set.Contains(Id(r1, c1))) continue;
                        int j = Id(r1, c1);
                        if (hi is { } up) Le(g, i, j, up / 100 * h, pen, $"{NameOf(i)}→{NameOf(j)}");
                        if (lo is { } dn) Le(g, j, i, -dn / 100 * h, pen, $"{NameOf(i)}→{NameOf(j)}");
                    }
                    if (!cons.Any(c => c.Group == g)) msgs.Add(CalcMessage.Warning("W-GS-004", $"[Constraints] row {q.Row}: {q.Type} {q.Range} has no pairs of grid points {(q.Type == "cross" ? "across" : "along")} the direction inside the range, so nothing is checked{(q.Type == "cross" ? "; make the range at least two points wide" : "")}."));
                    groups.Add((g, q, q.Type == "cross" ? $"cross slope ≤ {hi:0.##} %" : $"falls {q.Dir} {lo:0.##} to {hi:0.##} %", "%"));
                    break;
                }
            }
        }
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. msgs]);
        for (int i = 0; i < e.Length; i++)
        {
            if (maxCut is { } mc) cons.Add(new("Max cut", [i], [-1], -(e[i] - mc), false, Normal, NameOf(i)));
            if (maxFill is { } mf) cons.Add(new("Max fill", [i], [1], e[i] + mf, false, Normal, NameOf(i)));
        }
        bool balance = p.Flag("balance", true);
        int balanceAt = cons.Count;
        if (balance) cons.Add(new("Balance", [.. Enumerable.Range(0, e.Length)], [.. area.Select(a => a / area.Average())], 0, true, Soft, "site"));

        // ---- solve; the balance target depends on the cut (shrink), so iterate it.
        double meanA = area.Average();
        var target = (double[])e.Clone();
        var weight = area.Select(a => a / meanA).ToArray();
        foreach (var (i, v) in pinned) { target[i] = v; weight[i] = Pinned; }
        GradingSolver.Result sol = GradingSolver.Solve(target, weight, cons);
        double Cut(double[] z) => Enumerable.Range(0, e.Length).Sum(i => area[i] * Math.Max(0, e[i] - z[i]));
        double Fill(double[] z) => Enumerable.Range(0, e.Length).Sum(i => area[i] * Math.Max(0, z[i] - e[i]));
        if (balance)
            for (int it = 0; it < 4; it++)
            {
                // Fill placed = cut × (1 − shrink) + import, so Σ A (z − e) = fill − cut = import − shrink × cut.
                double net = import * volUnit - shrink * Cut(sol.Z), sumE = Enumerable.Range(0, e.Length).Sum(i => area[i] * e[i]);
                cons[balanceAt] = cons[balanceAt] with { Bound = (sumE + net) / area.Average() };
                var next = GradingSolver.Solve(target, weight, cons);
                bool same = Math.Abs(Cut(next.Z) - Cut(sol.Z)) < 0.5 * volUnit;
                sol = next;
                if (same) break;
            }
        else msgs.Add(CalcMessage.Info("I-GS-001", "balance = no: the grades stay closest to existing ground without balancing cut and fill."));
        if (!sol.Converged) msgs.Add(CalcMessage.Warning("W-GS-002", $"The solver stopped after {sol.Sweeps} sweeps without settling to 0.00001 {lu}; results are close but check the conflicts table."));
        var z = sol.Z;

        // ---- results
        double cut = Cut(z), fill = Fill(z);
        double fillFromCut = cut * (1 - shrink), surplus = fillFromCut + import * volUnit - fill;
        var summary = new List<string>
        {
            FormattableString.Invariant($"Cut {cut / volUnit:N0} {vu}, fill {fill / volUnit:N0} {vu}; after {shrink * 100:0.#} % shrink the cut makes {fillFromCut / volUnit:N0} {vu} of fill: ") +
            (Math.Abs(surplus) < Math.Max(0.02 * fill, volUnit * 5) ? "the site balances." : surplus > 0 ? $"{surplus / volUnit:N0} {vu} to export." : $"{-surplus / volUnit:N0} {vu} to import."),
        };

        var conflictRows = new List<IReadOnlyList<object?>>();
        var groupRows = new List<IReadOnlyList<object?>>();
        int conflictCount = 0;
        foreach (var (key, q, requirement, unit) in groups)
        {
            var idx = Enumerable.Range(0, cons.Count).Where(k => cons[k].Group == key).ToList();
            var bad = idx.Where(k => sol.Violation[k] > (si ? 0.003 : 0.01)).OrderByDescending(k => sol.Violation[k]).ToList();
            conflictCount += bad.Count;
            // Steepest grade actually reached in slope-type groups.
            string reached = "";
            if (q.Type is "slope" or "cross")
            {
                var slopes = idx.Select(k => Math.Abs(z[cons[k].Nodes[0]] - z[cons[k].Nodes[1]]) / h * 100).ToList();
                if (slopes.Count > 0) reached = $"{slopes.Max():0.##} % steepest" + (q.Type == "slope" && q.Min is not null ? $", {idx.Select(k => cons[k]).Where(c => c.Bound < 0).Select(c => (z[c.Nodes[1]] - z[c.Nodes[0]]) / h * 100).DefaultIfEmpty(double.NaN).Min():0.##} % flattest" : "");
            }
            if (q.Type == "away")
            {
                double sReq = (q.Value is not null && double.TryParse(q.Value, NumberStyles.Float, Inv, out var sv2) ? sv2 : 5) / 100;
                var falls = idx.Select(k => (z[cons[k].Nodes[1]] - z[cons[k].Nodes[0]]) / (-cons[k].Bound / sReq) * 100).ToList();
                if (falls.Count > 0) reached = $"{falls.Min():0.##} % least fall";
            }
            if (q.Type == "pad") reached = $"pad at {z[q.Nodes[0]]:0.00} {lu}";
            groupRows.Add([key, requirement, idx.Count, reached, bad.Count == 0 ? "OK" : $"CANNOT BE MET at {bad.Count}", bad.Count == 0 ? "" : cons[bad[0]].Where + FormattableString.Invariant($" (short {sol.Violation[bad[0]] / (unit == "%" ? h / 100 : 1):0.##} {(unit == "%" ? "%" : lu)})")]);
            foreach (var k in bad.Take(15))
                conflictRows.Add([key, cons[k].Where, requirement, unit == "%" ? sol.Violation[k] / h * 100 : sol.Violation[k], unit == "%" ? "%" : lu]);
        }
        foreach (var gname in new[] { "Max cut", "Max fill" })
        {
            var bad = Enumerable.Range(0, cons.Count).Where(k => cons[k].Group == gname && sol.Violation[k] > 0.01).ToList();
            if (bad.Count == 0 && !cons.Any(c => c.Group == gname)) continue;
            groupRows.Add([gname, gname == "Max cut" ? $"≤ {maxCut} {lu}" : $"≤ {maxFill} {lu}", cons.Count(c => c.Group == gname), "", bad.Count == 0 ? "OK" : $"CANNOT BE MET at {bad.Count}", bad.Count == 0 ? "" : string.Join(", ", bad.Take(6).Select(k => cons[k].Where))]);
            conflictCount += bad.Count;
            foreach (var k in bad.Take(10)) conflictRows.Add([gname, cons[k].Where, gname, sol.Violation[k], lu]);
        }
        if (balance) groupRows.Add(["Balance", "fill = cut × (1 − shrink) + import", 1, FormattableString.Invariant($"{surplus / volUnit:+0;-0;0} {vu}"), Math.Abs(surplus) < Math.Max(0.02 * fill, volUnit * 5) ? "OK" : "NOT BALANCED", Math.Abs(surplus) < Math.Max(0.02 * fill, volUnit * 5) ? "" : "the grading requirements leave no room to balance; adjust pad elevations or import/export"]);
        summary.Add(conflictCount == 0
            ? "Every requirement is met."
            : $"{conflictCount} spot(s) where the requirements cannot all be met; see Conflicts. The solver keeps tie-ins and pads, then building drainage and ADA slopes, then driveway and lawn slopes and cut/fill limits, then the balance.");
        foreach (var gr in groupRows.Where(g => g[4] is string s && s.StartsWith("CANNOT", StringComparison.Ordinal)).Take(4))
            msgs.Add(CalcMessage.Warning("W-GS-003", $"{gr[0]}: {gr[1]} cannot be met at {((string)gr[4]!)["CANNOT BE MET at ".Length..]} place(s), worst {gr[5]}."));

        string[] colHead = ["Row", .. Enumerable.Range(0, C).Select(Col)];
        IReadOnlyList<object?> GridRow(int r, Func<int, object?> f) => [(r + 1).ToString(Inv), .. Enumerable.Range(0, C).Select(c => f(Id(r, c)))];
        var tables = new List<ToolTable>
        {
            new("Requirements", ["Requirement", "Rule", "Checks", "Result", "Status", "Worst"], groupRows),
            new("Earthwork", ["Item", "Volume (" + vu + ")"],
            [
                ["Cut", Math.Round(cut / volUnit)], ["Fill", Math.Round(fill / volUnit)], [$"Fill made by the cut ({shrink * 100:0.#} % shrink)", Math.Round(fillFromCut / volUnit)],
                ["Import (entered)", import], [surplus >= 0 ? "Export needed" : "Import needed", Math.Round(Math.Abs(surplus) / volUnit)],
                [$"Largest cut ({lu})", Enumerable.Range(0, e.Length).Max(i => e[i] - z[i])], [$"Largest fill ({lu})", Enumerable.Range(0, e.Length).Max(i => z[i] - e[i])],
            ], [$"Grid volumes by the trapezoid rule on the {h:0.##} {lu} grid; a finer grid refines them."]),
        };
        if (conflictRows.Count > 0) tables.Add(new("Conflicts", ["Requirement", "Where", "Rule", "Short by", "Unit"], conflictRows, ["Each row is a place where meeting this requirement would break one ranked above it. Short by: how far the grade misses the rule."]));
        tables.Add(new($"Finished grade ({lu})", colHead, Enumerable.Range(0, R).Select(r => GridRow(r, i => z[i].ToString("0.00", Inv))).ToList()));
        tables.Add(new($"Cut (−) and fill (+) ({lu})", colHead, Enumerable.Range(0, R).Select(r => GridRow(r, i => (z[i] - e[i]).ToString("+0.00;−0.00;0.00", Inv))).ToList()));
        tables.Add(new($"Existing ground ({lu})", colHead, Enumerable.Range(0, R).Select(r => GridRow(r, i => e[i].ToString("0.00", Inv))).ToList()));

        int sr = Math.Clamp((int)Math.Round(p.Number("section row", Math.Ceiling(R / 2.0))) - 1, 0, R - 1);
        var charts = new List<ToolChart>
        {
            new($"Section along row {sr + 1} (west to east)", $"Distance east ({lu})", $"Elevation ({lu})",
            [
                new("Existing ground", Enumerable.Range(0, C).Select(c => (c * h, e[Id(sr, c)])).ToList(), true),
                new("Finished grade", Enumerable.Range(0, C).Select(c => (c * h, z[Id(sr, c)])).ToList()),
            ]),
        };
        msgs.Add(CalcMessage.Info("I-GS-002", $"{e.Length} grid points, {cons.Count} constraints, {sol.Sweeps} solver sweeps."));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static string Col(int c) => c < 26 ? ((char)('A' + c)).ToString() : ((char)('A' + c / 26 - 1)).ToString() + (char)('A' + c % 26);

    private static int ColIndex(string s) { int v = 0; foreach (var ch in s.ToUpperInvariant()) v = v * 26 + (ch - 'A' + 1); return v - 1; }

    /// <summary>Grid point indices of "A1:D4; F2" style ranges.</summary>
    public static List<int> Range(string text, int rows, int cols, out string? error)
    {
        error = null;
        var list = new List<int>();
        foreach (var part in text.Split([';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var m = RegexRange().Match(part.Trim());
            if (!m.Success) { error = $"\"{part}\" is not a grid point or range (A1, or A1:D4)."; return []; }
            int c0 = ColIndex(m.Groups[1].Value), r0 = int.Parse(m.Groups[2].Value, Inv) - 1;
            int c1 = m.Groups[3].Success ? ColIndex(m.Groups[3].Value) : c0, r1 = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, Inv) - 1 : r0;
            if (c1 < c0) (c0, c1) = (c1, c0);
            if (r1 < r0) (r0, r1) = (r1, r0);
            if (r0 < 0 || c0 < 0 || r1 >= rows || c1 >= cols) { error = $"\"{part}\" is outside the {rows}-row × {cols}-column grid."; return []; }
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) if (!list.Contains(r * cols + c)) list.Add(r * cols + c);
        }
        if (list.Count == 0) error = "give a range, e.g. A1:D4.";
        return list;
    }

    [GeneratedRegex(@"^([A-Za-z]{1,2})(\d+)(?::([A-Za-z]{1,2})(\d+))?$")]
    private static partial Regex RegexRange();
}
