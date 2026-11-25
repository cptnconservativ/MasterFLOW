using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Risk-based design of a highway crossing by FHWA HEC-17, <i>Highways in the River Environment: Floodplains, Extreme
/// Events, Risk, and Resilience</i> (2nd ed., 2016): the chance a flood is equalled or exceeded during the service life,
/// expected annual damage by integrating damage over exceedance probability, present worth, and the least total
/// expected cost (LTEC) alternative.
/// </summary>
public sealed class FloodplainRiskTool : IDesignTool
{
    public string Id => "floodplain-risk";
    public string Name => "Flood risk and least total expected cost (HEC-17)";
    public string Description => "FHWA HEC-17 (2nd ed., 2016) risk analysis for a crossing: risk R = 1 − (1 − 1/T)^n of a T-year flood during an n-year service life, expected annual damage EAD = ∫D dP (trapezoids between events, the largest event's damage carried to zero probability), present worth factor (1 − (1 + i)^−n)/i, and the total expected cost of each alternative (capital + PW of damage and upkeep); the least total expected cost alternative is reported. The floodway encroachment check is in the natural channel profile tool.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Flood risk and least total expected cost, FHWA HEC-17 (2016). Costs in dollars.
        service life = 50          # years
        discount rate = 3          # percent per year (real)
        design T = 100             # return period for the risk statement

        # Alternatives: capital cost and annual upkeep.
        [Alternatives]
        Name	Capital	Annual O&M
        3-cell 10x6 box	1450000	3000
        4-cell 10x6 box	1850000	3500
        Bridge 120 ft	2900000	6000

        # Damages for each event: road and structure repair, traffic delay and detour costs, upstream property damage.
        # Return period T (years), then one column per alternative, in the order above. Start with an event that does no damage.
        [Damages]
        T	3-cell 10x6 box	4-cell 10x6 box	Bridge 120 ft
        10	0	0	0
        25	120000	0	0
        50	450000	90000	0
        100	1100000	380000	40000
        500	2600000	1500000	450000
        """;

    /// <summary>Chance a flood with annual exceedance probability 1/T is equalled or exceeded at least once in n years.</summary>
    public static double Risk(double t, double n) => 1 - Math.Pow(1 - 1 / t, n);

    /// <summary>Present worth factor of a uniform annual amount over n years at rate i (fraction).</summary>
    public static double PresentWorth(double i, double n) => i > 0 ? (1 - Math.Pow(1 + i, -n)) / i : n;

    /// <summary>Expected annual damage: trapezoids in exceedance probability between events, plus the rarest event's damage times its probability.</summary>
    public static double ExpectedAnnualDamage(IReadOnlyList<(double T, double D)> ev)
    {
        var pts = ev.OrderBy(e => e.T).Select(e => (P: 1 / e.T, e.D)).ToList();
        double ead = 0;
        for (int k = 0; k + 1 < pts.Count; k++) ead += 0.5 * (pts[k].D + pts[k + 1].D) * (pts[k].P - pts[k + 1].P);
        return ead + pts[^1].D * pts[^1].P;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Flood risk and least total expected cost (HEC-17)";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double n = p.Number("service life", 50), rate = p.Number("discount rate", 3) / 100, tDesign = p.Number("design T", 100);
        if (!(n >= 1)) msgs.Add(CalcMessage.Error("E-FR-001", "service life must be at least 1 year."));
        if (!(rate >= 0)) msgs.Add(CalcMessage.Error("E-FR-002", "discount rate cannot be negative."));
        if (!(tDesign > 1)) msgs.Add(CalcMessage.Error("E-FR-003", "design T must be above 1 year."));
        var alts = new List<(string Name, double Cap, double Om)>();
        foreach (var r in p.Rows("Alternatives"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Alternative {alts.Count + 1}";
            double? c = p.Cell(r, 1, "capital", name), o = p.Cell(r, 2, "annual O&M", name);
            if (c is not >= 0 || o is < 0) { msgs.Add(CalcMessage.Error("E-FR-004", $"{name}: give a capital cost (≥ 0) and an annual O&M that is not negative.")); continue; }
            alts.Add((name, c.Value, o ?? 0));
        }
        var events = new List<(double T, double[] D)>();
        foreach (var r in p.Rows("Damages"))
        {
            if (p.Cell(r, 0, "T", "Damages") is not { } t) continue;
            if (!(t > 1)) { msgs.Add(CalcMessage.Error("E-FR-005", $"Damages: return period {t:G4} must be above 1 year.")); continue; }
            var d = alts.Select((a, j) => p.Cell(r, j + 1, a.Name, $"Damages T {t:G4}") ?? double.NaN).ToArray();
            if (d.Any(x => !(x >= 0))) { msgs.Add(CalcMessage.Error("E-FR-006", $"Damages T {t:G4}: give a damage (≥ 0) for every alternative.")); continue; }
            events.Add((t, d));
        }
        if (events.GroupBy(e => e.T).Any(gr => gr.Count() > 1)) msgs.Add(CalcMessage.Error("E-FR-007", "Two damage rows have the same return period."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var tables = new List<ToolTable>();
        var summary = new List<string> { $"A {tDesign:G4}-year flood has a {Risk(tDesign, n):P1} chance of being equalled or exceeded during a {n:G3}-year service life." };
        double[] ts = [2, 5, 10, 25, 50, 100, 200, 500];
        double[] lives = [10, 25, 50, 75, 100];
        var riskRows = ts.Select(t => (IReadOnlyList<object?>)[t, R(100 / t, 2), .. lives.Select(l => (object?)R(100 * Risk(t, l), 1))]).ToList();
        tables.Add(new("Risk of exceedance during the service life (%)", ["Return period T (yr)", "Annual chance (%)", .. lives.Select(l => $"{l:G3}-yr life")], riskRows,
            ["R = 1 − (1 − 1/T)^n (HEC-17 Ch. 5). The 100-yr flood has a 39 % chance in 50 years; the design flood is not a rare event over a bridge's life."]));

        if (alts.Count > 0 && events.Count >= 2)
        {
            double pwf = PresentWorth(rate, n);
            var ordered = events.OrderBy(e => e.T).ToList();
            if (ordered[0].D.Any(d => d > 0)) msgs.Add(CalcMessage.Warning("W-FR-001", $"The most frequent event (T = {ordered[0].T:G4}) already does damage; more frequent floods are taken as doing none, so EAD may be low. Add a smaller event with no damage."));
            var rows = new List<IReadOnlyList<object?>>();
            var tec = new double[alts.Count];
            for (int j = 0; j < alts.Count; j++)
            {
                double ead = ExpectedAnnualDamage(ordered.Select(e => (e.T, e.D[j])).ToList());
                tec[j] = alts[j].Cap + (ead + alts[j].Om) * pwf;
                rows.Add([alts[j].Name, R(alts[j].Cap, 0), R(alts[j].Om, 0), R(ead, 0), R(ead * pwf, 0), R(alts[j].Om * pwf, 0), R(tec[j], 0), ""]);
            }
            int best = Array.IndexOf(tec, tec.Min());
            rows[best] = [.. rows[best].Take(7), "least total expected cost"];
            tables.Add(new("Least total expected cost (HEC-17)", ["Alternative", "Capital ($)", "Annual O&M ($)", "Expected annual damage ($/yr)", "PW of damage ($)", "PW of O&M ($)", "Total expected cost ($)", "Result"], rows,
                [$"EAD = Σ ½(D1 + D2)(P1 − P2) over the events, P = 1/T, plus D·P of the rarest event (damage held constant beyond it). PW factor = (1 − (1 + i)^−n)/i = {pwf:F3} for i = {rate:P1}, n = {n:G3} yr.",
                 "Damages should include repair of the road and structure, traffic delay and detour costs for the closure, and added upstream flood damage from the crossing (HEC-17 Ch. 6)."]));
            var dRows = ordered.Select(e => (IReadOnlyList<object?>)[e.T, R(1 / e.T, 4), .. e.D.Select(d => (object?)R(d, 0))]).ToList();
            tables.Add(new("Damage–probability input", ["T (yr)", "Exceedance P", .. alts.Select(a => $"{a.Name} ($)")], dRows));
            summary.Add($"Least total expected cost: {alts[best].Name}, {tec[best]:N0} dollars over {n:G3} years ({rate:P1} discount).");
            var chart = new ToolChart("Damage against exceedance probability", "Annual exceedance probability", "Damage ($)",
                alts.Select((a, j) => new ChartSeries(a.Name, ordered.Select(e => (1 / e.T, e.D[j])).ToList())).ToList());
            return new ToolResult(title, summary, tables, msgs) { Charts = [chart] };
        }
        msgs.Add(CalcMessage.Info("I-FR-001", "Give [Alternatives] and at least two [Damages] events for the least total expected cost analysis."));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
