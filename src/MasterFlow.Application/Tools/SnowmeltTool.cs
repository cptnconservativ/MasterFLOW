using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Daily snowpack accounting by the temperature-index (degree-day) method with the USACE EM 1110-2-1406 rain-on-snow
/// equations: precipitation splits into rain and snow at a threshold temperature, dry-day melt is Cm·(Ta − Tb), rain-day
/// melt uses the open/partly forested or heavily forested equation with wind and rain, the pack holds liquid water up to a
/// fraction of its water equivalent, and the water available for runoff (rain passing through plus outflow from the pack)
/// is reported day by day.
/// </summary>
public sealed class SnowmeltTool : IDesignTool
{
    public string Id => "snowmelt";
    public string Name => "Snowmelt and rain-on-snow (degree-day, USACE EM 1110-2-1406)";
    public string Description => "Daily snowpack water balance: precipitation is snow at or below the rain/snow threshold and rain above it; dry-day melt M = Cm·(Ta − Tb) (degree-day); rain-on-snow melt by EM 1110-2-1406: open or partly forested M = (0.029 + 0.0084·k·v + 0.007·Pr)(Ta − 32) + 0.09, heavily forested M = (0.074 + 0.007·Pr)(Ta − 32) + 0.05 (in/day, °F, mph, in); liquid water held in the pack up to a fraction of the snow water equivalent. Reports snow water equivalent, melt and the water available for runoff each day, with the peak day for use as the rainfall input of a runoff model.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Snowmelt and rain-on-snow. U.S. units: in, °F, mph.
        initial swe = 6.0          # snow water equivalent on the ground at the start, in
        melt factor = 0.06         # Cm, in/°F/day for dry days (about 0.03 to 0.10; higher late in the season)
        base temperature = 32      # Tb, °F
        rain snow threshold = 34   # °F: precipitation is snow at or below, rain above
        forest cover = 20          # % canopy (0 to 60 % uses the open equation, above 60 % the forested one)
        wind = 10                  # mph at 50 ft above the snow, rain days
        wind coefficient = 1.0     # k: 1.0 open plains, about 0.3 dense forest
        liquid capacity = 3        # % of the snow water equivalent held as liquid water before outflow
        [Days]
        Day, Tavg (°F), Precip (in)
        1, 28, 0.3
        2, 30, 0.0
        3, 36, 0.0
        4, 40, 0.0
        5, 42, 1.2
        6, 45, 1.8
        7, 44, 0.4
        8, 41, 0.0
        9, 38, 0.0
        10, 35, 0.0
        """;

    /// <summary>Degree-day melt (in/day) on a day without rain.</summary>
    public static double DegreeDay(double cm, double ta, double tb) => Math.Max(0, cm * (ta - tb));

    /// <summary>EM 1110-2-1406 rain-on-snow melt (in/day): ta °F, pr in/day of rain, v mph at 50 ft, k basin wind coefficient.</summary>
    public static double RainOnSnow(double ta, double pr, double v, double k, bool forested) =>
        ta <= 32 ? 0 : forested ? (0.074 + 0.007 * pr) * (ta - 32) + 0.05 : (0.029 + 0.0084 * k * v + 0.007 * pr) * (ta - 32) + 0.09;

    public sealed record Day(string Name, double Ta, double P, double Snow, double Rain, double Melt, double Swe, double Liquid, double Runoff, string Method);

    /// <summary>Runs the daily accounting. SWE includes the liquid water held in the pack.</summary>
    public static List<Day> Simulate(IEnumerable<(string Name, double Ta, double P)> days, double swe0, double cm, double tb, double tpx,
        bool forested, double wind, double k, double liquidFrac)
    {
        double ice = swe0, liquid = 0;
        var res = new List<Day>();
        foreach (var (name, ta, p) in days)
        {
            double snow = ta <= tpx ? p : 0, rain = p - snow;
            ice += snow;
            double melt = 0; string how = "";
            if (ice > 0)
            {
                if (rain > 0 && ta > 32) { melt = RainOnSnow(ta, rain, wind, k, forested); how = forested ? "rain-on-snow, forested" : "rain-on-snow, open"; }
                else if (ta > tb) { melt = DegreeDay(cm, ta, tb); how = "degree-day"; }
                melt = Math.Min(melt, ice);
            }
            ice -= melt;
            double runoff;
            if (ice > 0)
            {
                liquid += melt + rain;
                double cap = liquidFrac * ice;
                runoff = Math.Max(0, liquid - cap);
                liquid -= runoff;
            }
            else { runoff = liquid + melt + rain; liquid = 0; }
            if (ice <= 0 && how.Length == 0 && rain > 0) how = "no snow";
            res.Add(new(name, ta, p, snow, rain, melt, ice + liquid, liquid, runoff, how));
        }
        return res;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Snowmelt and rain-on-snow";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        if (context.Units == UnitSystem.SI) msgs.Add(CalcMessage.Info("I-SM-001", "The EM 1110-2-1406 equations are in U.S. units: enter inches, °F and mph."));
        double swe0 = p.Number("initial swe", 0), cm = p.Number("melt factor", 0.06), tb = p.Number("base temperature", 32), tpx = p.Number("rain snow threshold", 34);
        double cover = p.Number("forest cover", 0), wind = p.Number("wind", 10), k = p.Number("wind coefficient", 1.0), liq = p.Number("liquid capacity", 3) / 100;
        var days = new List<(string, double, double)>();
        foreach (var r in p.Rows("Days"))
        {
            string name = r.Length > 0 ? r[0] : "";
            if (p.Cell(r, 1, "Tavg", $"[Days] {name}") is { } t && p.Cell(r, 2, "precipitation", $"[Days] {name}") is { } pr)
            {
                if (pr < 0) msgs.Add(CalcMessage.Error("E-SM-002", $"[Days] {name}: precipitation cannot be negative."));
                days.Add((name, t, pr));
            }
        }
        if (days.Count == 0) msgs.Add(CalcMessage.Error("E-SM-001", "Give the days under [Days]: day, average air temperature (°F), precipitation (in)."));
        if (swe0 < 0) msgs.Add(CalcMessage.Error("E-SM-003", "initial swe cannot be negative."));
        if (!(cm > 0)) msgs.Add(CalcMessage.Error("E-SM-004", "melt factor must be above zero."));
        if (cover is < 0 or > 100 || wind < 0 || k < 0 || liq is < 0 or > 0.5) msgs.Add(CalcMessage.Error("E-SM-005", "forest cover 0–100 %, wind and wind coefficient not negative, liquid capacity 0–50 %."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        if (cm is < 0.02 or > 0.15) msgs.Add(CalcMessage.Warning("W-SM-001", $"Melt factor {cm:G3} in/°F/day is outside the usual 0.02 to 0.15 range."));
        if (wind > 0 && cover > 60) msgs.Add(CalcMessage.Info("I-SM-002", "Above 60 % forest cover the forested rain-on-snow equation is used, which has no wind term."));

        bool forested = cover > 60;
        var res = Simulate(days, swe0, cm, tb, tpx, forested, wind, k, liq);
        var rows = res.Select(d => (IReadOnlyList<object?>)[d.Name, d.Ta, d.P, R(d.Snow), R(d.Rain), R(d.Melt), R(d.Swe), R(d.Liquid), R(d.Runoff), d.Method]).ToList();
        var peak = res.MaxBy(d => d.Runoff)!;
        double totP = res.Sum(d => d.P), totMelt = res.Sum(d => d.Melt), totRun = res.Sum(d => d.Runoff);
        var summary = new List<string>
        {
            $"Peak water available for runoff {peak.Runoff:F2} in on day {peak.Name} ({peak.Rain:F2} in rain, {peak.Melt:F2} in melt).",
            $"Over the period: {totP:F2} in precipitation, {totMelt:F2} in melt, {totRun:F2} in water available for runoff; {res[^1].Swe:F2} in SWE left.",
        };
        var balance = swe0 + totP - totRun - res[^1].Swe;
        if (Math.Abs(balance) > 1e-6) msgs.Add(CalcMessage.Warning("W-SM-002", $"Water balance closes to {balance:G3} in."));
        if (res.Any(d => d.Method.StartsWith("rain-on-snow") && d.Ta < 34))
            msgs.Add(CalcMessage.Info("I-SM-003", "Rain-on-snow equations assume saturated air near the temperature given; on cold rain days the melt is small and the pack may still be below freezing (cold content is not modeled)."));
        var tables = new List<ToolTable>
        {
            new("Daily snowpack", ["Day", "Tavg (°F)", "Precip (in)", "Snow (in)", "Rain (in)", "Melt (in)", "SWE (in)", "Liquid held (in)", "Water for runoff (in)", "Melt method"], rows,
            [
                $"Snow at or below {tpx:G3} °F, rain above. Dry days: M = {cm:G3}·(Ta − {tb:G3}).",
                forested ? "Rain days, heavily forested (EM 1110-2-1406): M = (0.074 + 0.007·Pr)(Ta − 32) + 0.05."
                         : $"Rain days, open or partly forested (EM 1110-2-1406): M = (0.029 + 0.0084·k·v + 0.007·Pr)(Ta − 32) + 0.09, k = {k:G3}, v = {wind:G3} mph.",
                $"The pack holds liquid water up to {liq * 100:G3} % of its ice content; the rest drains as water for runoff. Cold content and refreezing are not modeled.",
            ]),
        };
        var charts = new List<ToolChart>
        {
            new("Snowpack and water for runoff", "Day", "Inches",
                [new("SWE", res.Select((d, i) => ((double)(i + 1), d.Swe)).ToList()), new("Water for runoff", res.Select((d, i) => ((double)(i + 1), d.Runoff)).ToList(), Dashed: true)]),
        };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v) => Math.Round(v, 3) + 0.0;
}
