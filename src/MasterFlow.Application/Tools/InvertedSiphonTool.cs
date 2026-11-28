using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Sanitary inverted siphon (depressed sewer) by ASCE MOP 60 and Ten States Standards: two or more barrels flowing
/// full, staged by overflow weirs in the inlet chamber so the first barrel carries the low flows at a self-cleansing
/// 3 ft/s and later barrels join at the average-to-peak and wet-weather stages; full-flow head loss (Manning friction,
/// entrance, exit and bends) against the available head; inlet chamber weir crests and water surfaces; and the air
/// jumper between the inlet and outlet chambers.
/// </summary>
public sealed class InvertedSiphonTool : IDesignTool
{
    public string Id => "inverted-siphon";
    public string Name => "Inverted siphon";
    public string Description => "Sanitary inverted siphon (ASCE MOP 60, Ten States Standards): sizes two or more barrels so the first carries the low and average flows at 3 ft/s and later barrels join by overflow weirs at the peak and wet-weather stages; full-flow head loss (Manning friction, entrance, exit, bends) against the available head; flow split, velocities and upstream water surface from minimum to design flow; inlet chamber weir crests; capacity with the largest barrel out; and the air jumper size and air loss.";
    public bool UsesNetwork => false;

    private const double GpmPerCfs = 448.831, G = 32.174;
    /// <summary>Barrel sizes considered when designing (in.).</summary>
    public static readonly double[] Sizes = [6, 8, 10, 12, 15, 18, 21, 24, 27, 30, 33, 36, 42, 48, 54, 60];

    /// <summary>Head loss coefficient (ft per (ft/s)²) of a full barrel: (ΣK)/2g + n²L/(1.486²·R^(4/3)), so hL = coef·V².</summary>
    public static double LossCoefficient(double dIn, double length, double n, double kSum)
    {
        double r = dIn / 12 / 4;
        return kSum / (2 * G) + n * n * length / (1.486 * 1.486 * Math.Pow(r, 4.0 / 3));
    }

    /// <summary>Full-flow discharge (cfs) of a barrel at head <paramref name="h"/> ft: A·√(h/coef).</summary>
    public static double Capacity(double dIn, double h, double length, double n, double kSum)
        => h <= 0 ? 0 : Area(dIn) * Math.Sqrt(h / LossCoefficient(dIn, length, n, kSum));

    public static double Area(double dIn) => Math.PI * Math.Pow(dIn / 12, 2) / 4;

    /// <summary>
    /// Flow (cfs) in each barrel when the inlet chamber stands <paramref name="h"/> ft above the outlet water surface.
    /// Barrel 1 is fed at the chamber invert and runs full: c·√h. Barrel k is fed over a side weir with its crest
    /// <paramref name="crest"/>[k] ft above the outlet water surface and takes the lesser of the weir overflow
    /// <paramref name="cwL"/>·(h − crest)^1.5 and its own full flow c·√h. <paramref name="c"/>[k] = A/√(hL/V²).
    /// </summary>
    public static double[] Flows(double h, IReadOnlyList<double> c, IReadOnlyList<double> crest, double cwL)
    {
        var f = new double[c.Count];
        if (h <= 0) return f;
        for (int i = 0; i < c.Count; i++)
        {
            double full = c[i] * Math.Sqrt(h);
            f[i] = i == 0 ? full : h <= crest[i] ? 0 : Math.Min(full, cwL * Math.Pow(h - crest[i], 1.5));
        }
        return f;
    }

    public static double Total(double h, IReadOnlyList<double> c, IReadOnlyList<double> crest, double cwL) => Flows(h, c, crest, cwL).Sum();

    /// <summary>Head (ft, inlet above outlet water surface) at which the barrels pass <paramref name="q"/> cfs.</summary>
    public static double HeadFor(double q, IReadOnlyList<double> c, IReadOnlyList<double> crest, double cwL)
    {
        if (q <= 0) return 0;
        double lo = 0, hi = 1;
        while (Total(hi, c, crest, cwL) < q && hi < 1e5) hi *= 2;
        for (int i = 0; i < 200; i++) { double m = 0.5 * (lo + hi); if (Total(m, c, crest, cwL) < q) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    public string SampleInput(DesignToolContext context) => """
        # Inverted siphon under a drainage channel (ASCE MOP 60). U.S. units: flows gpm, lengths and elevations ft, diameters in.
        minimum flow = 350          # gpm, lowest daily flow in the early years
        average flow = 820          # gpm, design average
        peak flow = 2000            # gpm, design peak (dry weather)
        wet weather flow = 3000     # gpm, peak with inflow and infiltration; blank = peak flow
        upstream water surface = 3406.40     # highest allowed water surface in the inlet chamber at the design flow
        downstream water surface = 3403.40   # outlet chamber water surface at the design flow
        barrel length = 260
        manning n = 0.013           # IDCM §3.12.6: no lower n
        entrance K = 0.5
        exit K = 1.0
        bend K = 0.4                # sum for the two vertical bends of each barrel
        weir length = 3             # ft of side weir feeding each later barrel in the inlet chamber
        incoming sewer diameter = 18
        air jumper length = 280
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Inverted siphon";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-IS-001", "The inverted siphon tool works in U.S. units (gpm, ft, in.); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double? Need(string key, string what)
        {
            var v = p.OptionalNumber(key, what);
            if (v is null && !p.Has(key)) msgs.Add(CalcMessage.Error("E-IS-002", $"Give {what} ({key} = …)."));
            return v;
        }
        double? qAvgG = Need("average flow", "the average flow in gpm"), qPkG = Need("peak flow", "the peak flow in gpm");
        double qMinG = p.Number("minimum flow", qAvgG ?? 0), qWwG = p.Number("wet weather flow", qPkG ?? 0);
        double? head = p.OptionalNumber("available head"), wsUp = p.OptionalNumber("upstream water surface"), wsDn = p.OptionalNumber("downstream water surface");
        if (head is null && (wsUp is null || wsDn is null))
            msgs.Add(CalcMessage.Error("E-IS-002", "Give the available head (available head = …) or the upstream and downstream water surfaces."));
        double? len = Need("barrel length", "the barrel length in ft");
        double n = p.Number("manning n", 0.013), ke = p.Number("entrance K", 0.5), kx = p.Number("exit K", 1.0), kb = p.Number("bend K", 0);
        double vMin = p.Number("min velocity", 3.0), dMin = p.Number("min barrel diameter", 8);
        double? inSewer = p.OptionalNumber("incoming sewer diameter"), jumperD = p.OptionalNumber("air jumper diameter");
        double weirL = p.Number("weir length", 3.0), weirC = p.Number("weir coefficient", 3.0);
        double airRatio = p.Number("air ratio", 1.0), maxAirLoss = p.Number("max air loss", 0.5);
        double? jumperL = p.OptionalNumber("air jumper length");

        var given = new List<double>();
        foreach (var r in p.Rows("Barrels"))
        {
            double? dia = p.Cell(r, 0, "diameter", "barrels");
            if (dia is not > 0) { msgs.Add(CalcMessage.Error("E-IS-004", "Each [Barrels] row needs a diameter above zero (in.).")); continue; }
            given.Add(dia.Value);
        }
        if (qAvgG is <= 0 || qPkG is <= 0 || qMinG <= 0 || qPkG < qAvgG || qAvgG < qMinG || qWwG < (qPkG ?? 0))
            msgs.Add(CalcMessage.Error("E-IS-003", "Flows must be above zero and rise from minimum to average to peak to wet weather."));
        double dH = head ?? ((wsUp ?? 0) - (wsDn ?? 0));
        if (!(dH > 0) && msgs.All(m => m.Code != "E-IS-002")) msgs.Add(CalcMessage.Error("E-IS-003", "The available head must be above zero (upstream water surface above downstream)."));
        if (len is <= 0 || !(n > 0) || ke < 0 || kx < 0 || kb < 0 || !(vMin > 0) || !(dMin > 0) || !(airRatio >= 0) || !(maxAirLoss > 0) || jumperD is <= 0 || jumperL is <= 0 || !(weirL > 0) || !(weirC > 0))
            msgs.Add(CalcMessage.Error("E-IS-003", "Length, n, minimum velocity and diameter must be above zero; K values zero or more."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double L = len!.Value, kSum = ke + kx + kb;
        double qMin = qMinG / GpmPerCfs, qAvg = qAvgG!.Value / GpmPerCfs, qPk = qPkG!.Value / GpmPerCfs, qWw = qWwG / GpmPerCfs;
        double qDesign = Math.Max(qPk, qWw);
        double C(double d) => Area(d) / Math.Sqrt(LossCoefficient(d, L, n, kSum));   // Q = C·√H
        double cwL = weirC * weirL;

        // Barrels, the flow at which each joins, and its weir crest (head above the outlet water surface).
        var d = new List<double>(); var joinAt = new List<double>(); var crest = new List<double>();
        double Cap(IEnumerable<double> extra, double? extraCrest = null)
        {
            var cs = d.Select(C).Concat(extra.Select(C)).ToList();
            var cr = crest.Concat(extra.Select(_ => extraCrest ?? 0)).ToList();
            return Total(dH, cs, cr, cwL);
        }
        double CrestFor(double q) => d.Count == 0 ? 0 : Math.Max(crest[^1], HeadFor(q, d.Select(C).ToList(), crest, cwL));
        void Add(double dia, double join) { double cr = d.Count == 0 ? 0 : CrestFor(join); d.Add(dia); joinAt.Add(join); crest.Add(cr); }
        bool designed = given.Count == 0;
        if (designed)
        {
            // Barrel 1: the largest size that still runs the cleansing velocity at the average flow (Ten States).
            double d1 = Sizes.Where(s => s >= dMin - 1e-9 && Area(s) * vMin <= qAvg + 1e-12).DefaultIfEmpty(Sizes.First(s => s >= dMin - 1e-9)).Max();
            Add(d1, 0);
            if (Cap([]) >= qPk) Add(d1, qPk);   // a second barrel of the same size, joining above the peak (two barrels)
            else
            {
                double join = Math.Min(qAvg, Cap([]));
                double cr = CrestFor(join);
                double d2 = Sizes.FirstOrDefault(s => Cap([s], cr) >= qPk - 1e-12, Sizes[^1]);
                Add(d2, join);
            }
            if (qWw > Cap([]) + 1e-12)
            {
                double cr = CrestFor(qPk);
                double d3 = Sizes.FirstOrDefault(s => Cap([s], cr) >= qWw - 1e-12, Sizes[^1]);
                Add(d3, qPk);
            }
        }
        else
        {
            double[] stages = [qAvg, qPk, qWw];
            for (int k = 0; k < given.Count; k++) Add(given[k], k == 0 ? 0 : stages[Math.Min(k - 1, 2)]);
        }
        int nB = d.Count;
        var c = d.Select(C).ToList();

        double V(int i, double q) => q / Area(d[i]);
        (double H, double[] Q) Split(double q) { double h = HeadFor(q, c, crest, cwL); return (h, Flows(h, c, crest, cwL)); }
        var (hDes, fDes) = Split(qDesign);
        var (_, fAvg) = Split(qAvg);
        var (_, fMin) = Split(qMin);
        var (_, fPk) = Split(qPk);
        double capAll = Total(dH, c, crest, cwL);
        int largest = Enumerable.Range(0, nB).OrderByDescending(i => d[i]).First();
        double capOut = (c.Sum() - c[largest]) * Math.Sqrt(dH);   // remaining barrels at the full head, weirs bypassed

        // Checks
        if (nB < 2) msgs.Add(CalcMessage.Warning("W-IS-001", "An inverted siphon needs at least two barrels so either can be taken out of service for cleaning (Ten States Standards; ASCE MOP 60)."));
        for (int i = 0; i < nB; i++) if (d[i] < dMin - 1e-9) msgs.Add(CalcMessage.Warning("W-IS-002", $"Barrel {i + 1} ({d[i]:G3} in.) is below the {dMin:G3} in. minimum."));
        if (V(0, fAvg[0]) < vMin - 1e-9) msgs.Add(CalcMessage.Warning("W-IS-003", $"Barrel 1 runs {V(0, fAvg[0]):F2} ft/s at the average flow, below {vMin:G3} ft/s (Ten States Standards: at least 3 ft/s for average flows); use a smaller first barrel."));
        if (V(0, fMin[0]) < vMin - 1e-9) msgs.Add(CalcMessage.Info("I-IS-001", $"At the minimum flow barrel 1 runs {V(0, fMin[0]):F2} ft/s, below {vMin:G3} ft/s; solids settle overnight and are carried out when the flow rises. Plan routine flushing (ASCE MOP 60)."));
        for (int i = 1; i < nB; i++)
        {
            if (joinAt[i] >= qPk - 1e-9) continue;
            if (fPk[i] > 0 && V(i, fPk[i]) < vMin - 1e-9)
                msgs.Add(CalcMessage.Warning("W-IS-004", $"Barrel {i + 1} runs {V(i, fPk[i]):F2} ft/s at the peak flow, below {vMin:G3} ft/s; each daily barrel should reach the cleansing velocity once a day (ASCE MOP 60). Use a smaller barrel {i + 1} and add a wet-weather barrel."));
        }
        for (int i = 1; i < nB; i++)
            if (joinAt[i] >= qPk - 1e-9) msgs.Add(CalcMessage.Info("I-IS-002", $"Barrel {i + 1} carries only flows above the dry-weather peak; it stands full between storms, so flush it after each use or keep it dry with stop logs in the outlet chamber."));
        if (hDes > dH + 1e-6 || qDesign > capAll + 1e-9)
            msgs.Add(CalcMessage.Warning("W-IS-005", $"The design flow {qDesign * GpmPerCfs:F0} gpm needs {hDes:F2} ft of head; {dH:F2} ft is available (capacity {capAll * GpmPerCfs:F0} gpm). Add or enlarge a barrel, or allow more head."));
        if (n < 0.013 - 1e-9) msgs.Add(CalcMessage.Warning("W-IS-007", $"n = {n:G3} is below the 0.013 Rapid City allows (IDCM §3.12.6)."));
        if (nB >= 2 && capOut < qPk - 1e-9) msgs.Add(CalcMessage.Info("I-IS-003", $"With the largest barrel out of service the siphon passes {capOut * GpmPerCfs:F0} gpm at the available head, below the {qPk * GpmPerCfs:F0} gpm peak; clean it at low flow or provide a bypass."));

        // Air jumper
        double airD = jumperD ?? Sizes.First(s => s >= Math.Max(6, (inSewer ?? 12) / 2) - 1e-9);
        double airL = jumperL ?? L;
        double qAir = qDesign * airRatio;   // cfs of air
        double vAir = qAir / Area(airD);
        const double nuAir = 1.64e-4, rhoAir = 0.0752, eps = 5e-6;
        double re = vAir * airD / 12 / nuAir;
        double f = re < 2000 ? (re > 0 ? 64 / re : 0) : 0.25 / Math.Pow(Math.Log10(eps / (3.7 * airD / 12) + 5.74 / Math.Pow(re, 0.9)), 2);
        double hAir = (f * airL / (airD / 12) + 1.5) * vAir * vAir / (2 * G);   // ft of air, with entrance and exit
        double inWc = hAir * rhoAir / 62.4 * 12;
        if (inWc > maxAirLoss) msgs.Add(CalcMessage.Warning("W-IS-006", $"The {airD:G3} in. air jumper loses {inWc:F2} in. of water carrying {qAir * 60:F0} cfm, above {maxAirLoss:G3} in.; use a larger jumper."));
        msgs.Add(CalcMessage.Info("I-IS-004", "Air jumper (ASCE MOP 60): a pipe from the inlet chamber headspace to the outlet chamber headspace, so the air the incoming sewer carries is not forced out at the inlet as odor. Run it above the highest inlet water surface, sloped to drain, with no sags; the air flow is taken as the design water flow times the air ratio (a lower bound: the air the water displaces). Set the air ratio from an odor or ventilation study."));
        msgs.Add(CalcMessage.Info("I-IS-005", "Inlet chamber: barrel 1 takes the low flow; the others are fed over side weirs at the crests listed, so either barrel can be stopped off for cleaning (Ten States Standards). Give the chambers room for cleaning equipment, flushing connections and stop logs or gates on every barrel at both ends. The IDCM has no siphon criteria; drop manholes and deep structures still need City approval (§3.12.10)."));

        string BarrelText(int i) => $"{d[i]:G3} in.";
        var summary = new List<string>
        {
            $"{nB} barrels: {string.Join(", ", Enumerable.Range(0, nB).Select(BarrelText))}{(designed ? " (designed)" : "")}, {L:F0} ft, n = {n:G3}, K = {kSum:G3}.",
            $"Barrel 1: {V(0, fMin[0]):F2} ft/s at the minimum {qMinG:F0} gpm, {V(0, fAvg[0]):F2} ft/s at the average {qAvgG:F0} gpm.",
            $"Design flow {qDesign * GpmPerCfs:F0} gpm needs {hDes:F2} ft of head against {dH:F2} ft available; capacity {capAll * GpmPerCfs:F0} gpm (all barrels), {capOut * GpmPerCfs:F0} gpm with the largest out.",
            $"Air jumper {airD:G3} in., {airL:F0} ft: {vAir:F1} ft/s, {inWc:F2} in. of water at {qAir * 60:F0} cfm.",
        };

        // Rating table: the named flows plus even steps to the design flow.
        var flows = new SortedSet<double> { qMin, qAvg, qPk, qDesign };
        for (int i = 1; i <= 10; i++) flows.Add(qMin + (qDesign - qMin) * i / 10);
        var rating = new List<IReadOnlyList<object?>>();
        var hSeries = new List<(double, double)>();
        var vSeries = Enumerable.Range(0, nB).Select(_ => new List<(double, double)>()).ToList();
        foreach (var q in flows)
        {
            var (h, fl) = Split(q);
            string label = Math.Abs(q - qMin) < 1e-9 ? "minimum" : Math.Abs(q - qAvg) < 1e-9 ? "average" : Math.Abs(q - qPk) < 1e-9 ? "peak" : Math.Abs(q - qWw) < 1e-9 && qWw > qPk ? "wet weather" : "";
            var row = new List<object?> { label, R(q * GpmPerCfs, 0), R(q, 3) };
            for (int i = 0; i < nB; i++) { row.Add(R(fl[i] * GpmPerCfs, 0)); row.Add(R(V(i, fl[i]), 2)); vSeries[i].Add((R(q * GpmPerCfs, 0), R(V(i, fl[i]), 2))); }
            row.Add(R(h, 3));
            if (wsDn is { } dn) row.Add(R(dn + h, 2));
            rating.Add(row);
            hSeries.Add((R(q * GpmPerCfs, 0), R(h, 3)));
        }
        var headers = new List<string> { "Flow", "Q (gpm)", "Q (cfs)" };
        for (int i = 0; i < nB; i++) { headers.Add($"Barrel {i + 1} (gpm)"); headers.Add($"V{i + 1} (ft/s)"); }
        headers.Add("Head (ft)");
        if (wsDn is not null) headers.Add("Inlet WS (ft)");

        var barrelRows = new List<IReadOnlyList<object?>>();
        for (int i = 0; i < nB; i++)
            barrelRows.Add([i + 1, d[i], R(Area(d[i]), 3), R(LossCoefficient(d[i], L, n, kSum), 4), R(c[i] * Math.Sqrt(dH) * GpmPerCfs, 0), i == 0 ? "—" : R(joinAt[i] * GpmPerCfs, 0), i == 0 ? "invert" : R(crest[i], 3), wsDn is { } w && i > 0 ? R(w + crest[i], 2) : null, R(fDes[i] * GpmPerCfs, 0), R(V(i, fDes[i]), 2)]);

        var tables = new List<ToolTable>
        {
            new("Barrels and inlet weirs", ["Barrel", "D (in.)", "Area (ft²)", "hL / V² (s²/ft)", "Capacity at available head (gpm)", "Joins at (gpm)", "Weir crest above outlet WS (ft)", "Weir crest elev. (ft)", "Q at design (gpm)", "V at design (ft/s)"], barrelRows,
                [$"Full-flow loss hL = [(Ke + Kx + Kb)/2g + n²L/(1.486²R^4/3)]·V² with K = {ke:G3} + {kx:G3} + {kb:G3}. Barrel 1 is fed at the chamber invert; each later barrel over a side weir (Q = {weirC:G3}·{weirL:G3}·h^1.5, limited by the barrel's full flow) whose crest is the water surface at which the barrels before it carry its joining flow."]),
            new("Flow split and head, minimum to design flow", headers, rating,
                ["Head = inlet chamber water surface above the outlet water surface. A barrel fed over a weir runs part full until the weir overflow reaches its full-flow capacity."]),
            new("Air jumper", ["Item", "Value"],
            [
                ["Diameter (in.)", airD], ["Length (ft)", R(airL, 0)], ["Air flow (cfm)", R(qAir * 60, 0)], ["Air velocity (ft/s)", R(vAir, 2)], ["Friction factor", R(f, 4)], ["Loss (in. of water)", R(inWc, 3)], ["Limit (in. of water)", maxAirLoss],
            ], [jumperD is null ? $"Diameter: half the incoming sewer ({(inSewer is { } s ? s.ToString("G3") + " in." : "not given, 12 in. assumed")}), 6 in. minimum, rounded up." : "Diameter as given."]),
        };
        var charts = new List<ToolChart>
        {
            new("Siphon head", "Flow (gpm)", "Head (ft)", [new("Head used", hSeries)], [new ChartLine("Available head", dH)]),
            new("Barrel velocities", "Flow (gpm)", "Velocity (ft/s)", Enumerable.Range(0, nB).Select(i => new ChartSeries($"Barrel {i + 1} ({d[i]:G3} in.)", vSeries[i])).ToList(), [new ChartLine("Cleansing velocity", vMin)]),
        };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v, int dp) => double.IsFinite(v) ? Math.Round(v, dp) + 0.0 : double.NaN;
}
