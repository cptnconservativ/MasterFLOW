using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Groundwater mounding under a rectangular infiltration basin by the Hantush (1967) solution, as in USGS SIR 2010-5102
/// (Carleton): h² − hi² = (w·ν·t/(2K))·ΣS*, ν = K·b/Sy, with the average saturated thickness b iterated, recovery after
/// recharge stops by superposition, the mound at the center and along the axes, and the separation to the basin floor.
/// </summary>
public sealed class GroundwaterMoundingTool : IDesignTool
{
    public string Id => "groundwater-mounding";
    public string Name => "Groundwater mounding under infiltration basins (Hantush)";
    public string Description => "Hantush (1967) groundwater mound under a rectangular recharge area, as programmed in USGS SIR 2010-5102: h² − hi² = (w·ν·t/2K)·[S*((L/2 + x)n, (W/2 + y)n) + … four terms], ν = K·b/Sy, n = (4νt)^−½, S*(α, β) = ∫₀¹ erf(α/√τ)·erf(β/√τ) dτ; b = hi + Δh/2 iterated; recovery by superposition after recharge stops. Reports the peak mound at the center, profiles along both axes, the time series, and the separation from the mounded water table to the basin floor.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Groundwater mounding, Hantush (1967). SI: m, m/day, days.
        length = 30
        width = 15
        recharge = 0.6             # m/day over the basin footprint while it infiltrates
        duration = 2               # days of recharge
        K = 6                      # horizontal hydraulic conductivity, m/day
        Sy = 0.20                  # specific yield
        saturated thickness = 8    # initial saturated thickness hi, m
        floor depth to water table = 2.0   # basin floor above the seasonal high water table, m
        required separation = 0.6  # m (state rule)
        """ : """
        # Groundwater mounding, Hantush (1967) as in USGS SIR 2010-5102. U.S. units: ft, ft/day, days.
        length = 100                 # basin length, ft (x direction)
        width = 50                   # basin width, ft (y direction)
        recharge = 2.0               # infiltration rate over the footprint while it drains, ft/day (1 in/hr = 2 ft/day)
        duration = 2                 # days of recharge (e.g. drain time of the water quality volume)
        K = 20                       # horizontal hydraulic conductivity, ft/day (often 10 × the vertical rate)
        Sy = 0.20                    # specific yield
        saturated thickness = 25     # initial saturated thickness hi, ft (water table to the impermeable base)
        floor depth to water table = 6.0   # basin floor above the seasonal high water table, ft
        required separation = 2.0    # ft between the mounded water table and the basin floor (state rule)
        """;

    /// <summary>Error function (Abramowitz and Stegun 7.1.26, |error| &lt; 1.5·10⁻⁷).</summary>
    public static double Erf(double x)
    {
        double s = Math.Sign(x); x = Math.Abs(x);
        double t = 1 / (1 + 0.3275911 * x);
        double y = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return s * y;
    }

    /// <summary>S*(α, β) = ∫₀¹ erf(α/√τ)·erf(β/√τ) dτ (Simpson's rule on τ = u², which removes the singular slope at 0).</summary>
    public static double SStar(double a, double b)
    {
        const int n = 400;
        double sum = 0;
        for (int i = 0; i <= n; i++)
        {
            double u = (double)i / n;
            double f = u == 0 ? 2 * 0 : 2 * u * Erf(a / u) * Erf(b / u);   // dτ = 2u du
            sum += f * (i == 0 || i == n ? 1 : i % 2 == 1 ? 4 : 2);
        }
        return sum / (3.0 * n);
    }

    /// <summary>Mound rise Δh (above hi) at (x, y) after t of continuous recharge; b iterated as hi + Δh/2.</summary>
    public static double Rise(double x, double y, double t, double l, double w, double rate, double k, double sy, double hi)
    {
        if (t <= 0) return 0;
        double dh = 0;
        for (int it = 0; it < 30; it++)
        {
            double h2 = H2(x, y, t, l, w, rate, k, sy, hi + dh / 2);
            double nd = Math.Sqrt(hi * hi + h2) - hi;
            if (Math.Abs(nd - dh) < 1e-6) { dh = nd; break; }
            dh = nd;
        }
        return dh;
    }

    /// <summary>h² − hi² for continuous recharge (Hantush), given the average saturated thickness b.</summary>
    public static double H2(double x, double y, double t, double l, double w, double rate, double k, double sy, double b)
    {
        if (t <= 0) return 0;
        double nu = k * b / sy, n = 1 / Math.Sqrt(4 * nu * t);
        double s = SStar((l / 2 + x) * n, (w / 2 + y) * n) + SStar((l / 2 + x) * n, (w / 2 - y) * n)
                 + SStar((l / 2 - x) * n, (w / 2 + y) * n) + SStar((l / 2 - x) * n, (w / 2 - y) * n);
        return rate * nu * t / (2 * k) * s;
    }

    /// <summary>Rise at time t for recharge lasting <paramref name="dur"/> (superposition of h² − hi² with an image well of −w from dur).</summary>
    public static double RiseWithRecovery(double x, double y, double t, double dur, double l, double w, double rate, double k, double sy, double hi)
    {
        if (t <= dur) return Rise(x, y, t, l, w, rate, k, sy, hi);
        double dh = Rise(x, y, dur, l, w, rate, k, sy, hi);
        for (int it = 0; it < 30; it++)
        {
            double b = hi + dh / 2;
            double h2 = H2(x, y, t, l, w, rate, k, sy, b) - H2(x, y, t - dur, l, w, rate, k, sy, b);
            double nd = Math.Sqrt(hi * hi + Math.Max(0, h2)) - hi;
            if (Math.Abs(nd - dh) < 1e-6) { dh = nd; break; }
            dh = nd;
        }
        return dh;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Groundwater mounding (Hantush)";
        bool si = context.Units == UnitSystem.SI;
        string L = si ? "m" : "ft";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double len = p.Number("length", double.NaN), wid = p.Number("width", double.NaN), rate = p.Number("recharge", double.NaN), dur = p.Number("duration", double.NaN);
        double k = p.Number("K", double.NaN), sy = p.Number("Sy", double.NaN), hi = p.Number("saturated thickness", double.NaN);
        double? depth = p.OptionalNumber("floor depth to water table"), req = p.OptionalNumber("required separation");
        if (!(len > 0) || !(wid > 0)) msgs.Add(CalcMessage.Error("E-GM-001", "Give the basin length and width above zero."));
        if (!(rate > 0) || !(dur > 0)) msgs.Add(CalcMessage.Error("E-GM-002", "Give the recharge rate and duration above zero."));
        if (!(k > 0) || !(sy is > 0 and < 1) || !(hi > 0)) msgs.Add(CalcMessage.Error("E-GM-003", "Give K above zero, Sy between 0 and 1 and the saturated thickness above zero."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        if (rate > k) msgs.Add(CalcMessage.Warning("W-GM-001", $"The recharge rate ({rate:G3}) exceeds K ({k:G3}); the Hantush solution assumes the mound stays well below the basin and that recharge is much less than K."));

        double peak = Rise(0, 0, dur, len, wid, rate, k, sy, hi);
        if (peak > 0.5 * hi) msgs.Add(CalcMessage.Warning("W-GM-002", $"The mound ({peak:F2} {L}) is more than half the initial saturated thickness; the Hantush linearization (b ≈ hi + Δh/2) loses accuracy. Use a numerical model (MODFLOW) for a final design."));
        var summary = new List<string> { $"Peak mound at the basin center after {dur:G3} days: {peak:F2} {L} above the initial water table." };
        var tables = new List<ToolTable>();
        List<IReadOnlyList<object?>> check =
        [
            ["Recharge rate w", rate, si ? "m/day" : "ft/day"], ["Recharge duration", dur, "days"], ["Horizontal K", k, si ? "m/day" : "ft/day"],
            ["Specific yield Sy", sy, ""], ["Initial saturated thickness hi", hi, L], ["Peak mound Δh at the center", R(peak, 3), L],
            ["Upper bound with no lateral spreading, w·t/Sy", R(rate * dur / sy, 3), L],
        ];
        if (depth is { } dd)
        {
            double sep = dd - peak;
            check.Add(["Separation from the mounded water table to the basin floor", R(sep, 2), L]);
            if (sep <= 0) { msgs.Add(CalcMessage.Warning("W-GM-003", $"The mound reaches the basin floor ({peak:F2} {L} rise against {dd:G3} {L} of separation): the basin will not drain as designed.")); summary.Add("The mound reaches the basin floor."); }
            else if (req is { } rq && sep < rq) { msgs.Add(CalcMessage.Warning("W-GM-004", $"Separation {sep:F2} {L} is less than the {rq:G3} {L} required.")); summary.Add($"Separation {sep:F2} {L} < required {rq:G3} {L}."); }
            else summary.Add($"Separation to the basin floor {sep:F2} {L}{(req is { } r2 ? $" (≥ {r2:G3} {L} required)" : "")}.");
        }
        tables.Add(new("Mound at the basin center", ["Item", "Value", "Unit"], check,
            ["Hantush (1967): h² − hi² = (w·ν·t/2K)·[S*((L/2+x)n,(W/2+y)n) + S*((L/2+x)n,(W/2−y)n) + S*((L/2−x)n,(W/2+y)n) + S*((L/2−x)n,(W/2−y)n)], ν = K·b/Sy, n = 1/√(4νt), b = hi + Δh/2 (iterated).",
             "Assumes a homogeneous, isotropic, unconfined aquifer of large extent on a flat impermeable base, uniform recharge over the rectangle, and a water table well below the basin (USGS SIR 2010-5102)."]));

        // Profiles along the axes at the end of recharge.
        var xs = Enumerable.Range(0, 21).Select(i => i * Math.Max(len, wid) * 1.5 / 20).ToList();
        var prof = xs.Select(x => (IReadOnlyList<object?>)[R(x, 1), R(Rise(x, 0, dur, len, wid, rate, k, sy, hi), 3), R(Rise(0, x, dur, len, wid, rate, k, sy, hi), 3)]).ToList();
        tables.Add(new($"Mound profile at {dur:G3} days", [$"Distance from center ({L})", $"Rise along length ({L})", $"Rise along width ({L})"], prof));
        // Time series at the center, through recovery.
        var ts = Enumerable.Range(0, 25).Select(i => i * dur * 3 / 24).ToList();
        var series = ts.Select(t => (t, RiseWithRecovery(0, 0, t, dur, len, wid, rate, k, sy, hi))).ToList();
        tables.Add(new("Mound at the center against time", ["Time (days)", $"Rise ({L})"], series.Select(s => (IReadOnlyList<object?>)[R(s.t, 2), R(s.Item2, 3)]).ToList(),
            ["After recharge stops the mound decays; recovery by superposing an equal negative recharge from the end of recharge."]));
        var charts = new List<ToolChart>
        {
            new($"Mound profile at {dur:G3} days", $"Distance from center ({L})", $"Rise ({L})",
                [new("Along length", prof.Select(r => ((double)r[0]!, (double)r[1]!)).ToList()), new("Along width", prof.Select(r => ((double)r[0]!, (double)r[2]!)).ToList(), Dashed: true)],
                depth is { } d2 ? [new("Basin floor", d2)] : null),
            new("Mound at the center", "Time (days)", $"Rise ({L})", [new("Center", series)]),
        };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
