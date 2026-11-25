using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// FHWA HDS-6 sediment transport capacity of a reach: Meyer-Peter and Müller bed load (with grain roughness),
/// Engelund–Hansen total load and Yang's (1973) sand unit stream power equation, in a wide channel; Rubey fall velocity;
/// and a sediment continuity budget over a series of reaches giving the aggradation or degradation over a flow duration.
/// </summary>
public sealed class SedimentTransportTool : IDesignTool
{
    public string Id => "sediment-transport";
    public string Name => "Sediment transport capacity and reach budget (HDS-6)";
    public string Description => "Sediment transport per HDS-6 in a wide channel: Meyer-Peter and Müller bed load Φ = 8(θ' − 0.047)^1.5 with θ' = (n'/n)^1.5·θ, n' = D90^(1/6)/26; Engelund–Hansen total load Φ = 0.1·θ^2.5/f, f = 2gRS/V²; Yang (1973) sand unit stream power log Ct = 5.435 − 0.286 log(ωd/ν) − 0.457 log(U*/ω) + (1.799 − 0.409 log(ωd/ν) − 0.314 log(U*/ω))·log((V − Vcr)S/ω); Rubey fall velocity; a continuity budget through [Reaches] giving the bed change ΔV = (Qs,in − Qs,out)·T/(1 − p) over a flow duration.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Sediment transport (HDS-6). SI: m, m³/s, mm, °C.
        Q = 60
        width = 30
        slope = 0.0015
        n = 0.028
        D50 = 0.8                 # mm
        D90 = 2.5                 # mm
        temperature = 15          # °C
        formula = yang            # yang | engelund-hansen | mpm, for the reach budget
        duration = 24             # h at this flow
        porosity = 0.4            # of the deposited bed
        [Reaches]
        Name, Width, Slope, n, Length
        Upstream, 30, 0.0015, 0.028, 800
        Bridge, 22, 0.0015, 0.028, 150
        Downstream, 40, 0.0010, 0.030, 800
        """ : """
        # Sediment transport (HDS-6). U.S. units: ft, cfs, mm for grain sizes, °F.
        Q = 2000
        width = 100
        slope = 0.0015
        n = 0.028
        D50 = 0.8                 # mm
        D90 = 2.5                 # mm
        temperature = 60          # °F
        formula = yang            # yang | engelund-hansen | mpm, for the reach budget
        duration = 24             # h at this flow
        porosity = 0.4            # of the deposited bed
        [Reaches]
        Name, Width, Slope, n, Length
        Upstream, 100, 0.0015, 0.028, 2500
        Bridge, 70, 0.0015, 0.028, 500
        Downstream, 130, 0.0010, 0.030, 2500
        """;

    public const double G = 9.80665;

    /// <summary>Kinematic viscosity of water (m²/s) at t °C: Vogel dynamic viscosity over the Thiesen density.</summary>
    public static double Viscosity(double tC)
    {
        double mu = 2.414e-5 * Math.Pow(10, 247.8 / (tC + 273.15 - 140));
        double rho = 1000 * (1 - (tC + 288.9414) / (508929.2 * (tC + 68.12963)) * Math.Pow(tC - 3.9863, 2));
        return mu / rho;
    }

    /// <summary>Rubey (1933) fall velocity (m/s) of a grain of diameter d (m).</summary>
    public static double FallVelocity(double d, double ss, double nu)
    {
        double a = 36 * nu * nu / (G * d * d * d * (ss - 1));
        return (Math.Sqrt(2.0 / 3 + a) - Math.Sqrt(a)) * Math.Sqrt((ss - 1) * G * d);
    }

    public sealed record Hydraulics(double Y, double V, double UStar, double Theta);

    /// <summary>Wide-channel normal depth hydraulics (SI): q m²/s.</summary>
    public static Hydraulics Wide(double q, double n, double s, double d50, double ss)
    {
        double y = Math.Pow(q * n / Math.Sqrt(s), 0.6), v = q / y;
        return new(y, v, Math.Sqrt(G * y * s), y * s / ((ss - 1) * d50));
    }

    /// <summary>Meyer-Peter and Müller bed load, m²/s per unit width (volume of solids).</summary>
    public static double Mpm(Hydraulics h, double n, double d50, double d90, double ss)
    {
        double nGrain = Math.Pow(d90, 1.0 / 6) / 26, thetaP = Math.Pow(Math.Min(1, nGrain / n), 1.5) * h.Theta;
        return thetaP <= 0.047 ? 0 : 8 * Math.Pow(thetaP - 0.047, 1.5) * Math.Sqrt((ss - 1) * G * d50 * d50 * d50);
    }

    /// <summary>Engelund–Hansen total load, m²/s per unit width.</summary>
    public static double EngelundHansen(Hydraulics h, double s, double d50, double ss)
    {
        double f = 2 * G * h.Y * s / (h.V * h.V);
        return 0.1 * Math.Pow(h.Theta, 2.5) / f * Math.Sqrt((ss - 1) * G * d50 * d50 * d50);
    }

    /// <summary>Yang (1973) sand concentration, ppm by weight.</summary>
    public static double YangPpm(Hydraulics h, double s, double d50, double ss, double nu)
    {
        double w = FallVelocity(d50, ss, nu), re = h.UStar * d50 / nu;
        double vcrW = re < 70 ? 2.5 / (Math.Log10(Math.Max(re, 1.2)) - 0.06) + 0.66 : 2.05;
        double x = (h.V * s - vcrW * w * s) / w;
        if (x <= 0) return 0;
        double a = Math.Log10(w * d50 / nu), b = Math.Log10(h.UStar / w);
        return Math.Pow(10, 5.435 - 0.286 * a - 0.457 * b + (1.799 - 0.409 * a - 0.314 * b) * Math.Log10(x));
    }

    /// <summary>Yang load, m²/s of solids per unit width, from the ppm by weight of the flow q.</summary>
    public static double Yang(Hydraulics h, double q, double s, double d50, double ss, double nu) => YangPpm(h, s, d50, ss, nu) * 1e-6 * q / ss;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sediment transport capacity (HDS-6)";
        bool si = context.Units == UnitSystem.SI;
        double len = si ? 1 : 0.3048, q3 = si ? 1 : 0.0283168;   // to SI
        string L = si ? "m" : "ft", Qu = si ? "m³/s" : "cfs", qu = si ? "m²/s" : "ft²/s";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double qIn = p.Number("Q", double.NaN), w = p.Number("width", double.NaN), s = p.Number("slope", double.NaN), n = p.Number("n", double.NaN);
        double d50mm = p.Number("D50", double.NaN), d90mm = p.Number("D90", double.NaN), ss = p.Number("specific gravity", 2.65);
        double temp = p.Number("temperature", si ? 15 : 60), dur = p.Number("duration", 24), por = p.Number("porosity", 0.4);
        string formula = (p.Text("formula") ?? "yang").ToLowerInvariant();
        if (!(qIn > 0) || !(w > 0) || !(s > 0) || !(n > 0)) msgs.Add(CalcMessage.Error("E-ST-001", "Give Q, width, slope and n above zero."));
        if (!(d50mm > 0)) msgs.Add(CalcMessage.Error("E-ST-002", "Give D50 (mm) above zero."));
        if (double.IsNaN(d90mm)) d90mm = 2.5 * d50mm;
        if (!(d90mm >= d50mm)) msgs.Add(CalcMessage.Error("E-ST-003", "D90 must be at least D50."));
        if (!(ss > 1) || !(dur > 0) || por is < 0 or >= 1) msgs.Add(CalcMessage.Error("E-ST-004", "Specific gravity above 1, duration above zero, porosity 0 to below 1."));
        if (formula is not ("yang" or "engelund-hansen" or "mpm")) msgs.Add(CalcMessage.Error("E-ST-005", "formula must be yang, engelund-hansen or mpm."));
        var reaches = new List<(string Name, double W, double S, double N, double Len)>();
        foreach (var r in p.Rows("Reaches"))
        {
            string name = r.Length > 0 ? r[0] : "";
            if (p.Cell(r, 1, "width", name) is { } rw && p.Cell(r, 2, "slope", name) is { } rs && p.Cell(r, 3, "n", name) is { } rn && p.Cell(r, 4, "length", name) is { } rl)
            {
                if (!(rw > 0 && rs > 0 && rn > 0 && rl > 0)) msgs.Add(CalcMessage.Error("E-ST-006", $"[Reaches] {name}: width, slope, n and length must be above zero."));
                reaches.Add((name, rw, rs, rn, rl));
            }
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double tC = si ? temp : (temp - 32) / 1.8, nu = Viscosity(tC);
        double d50 = d50mm / 1000, d90 = d90mm / 1000, nSi = n;   // Manning's n is the same number in both systems (K carries the unit)
        double Q = qIn * q3, W = w * len, qUnit = Q / W;
        var h = Wide(qUnit, nSi, s, d50, ss);
        double wFall = FallVelocity(d50, ss, nu);
        double mpm = Mpm(h, nSi, d50, d90, ss), eh = EngelundHansen(h, s, d50, ss), ppm = YangPpm(h, s, d50, ss, nu), yang = ppm * 1e-6 * qUnit / ss;
        double ToUser(double m2s) => m2s * W / q3;                       // total volume rate in the user's units
        double TonsDay(double m2s) => m2s * W * ss * 1000 * 86400 / 907.185; // short tons per day
        double Ppm(double m2s) => m2s * ss / qUnit * 1e6;
        var summary = new List<string>
        {
            $"Depth {h.Y / len:F2} {L}, velocity {h.V / len:F2} {(si ? "m/s" : "ft/s")}, Shields θ = {h.Theta:F3}, fall velocity {wFall * 1000:F1} mm/s.",
            $"Capacity: Yang {TonsDay(yang):N0} t/day, Engelund–Hansen {TonsDay(eh):N0} t/day, Meyer-Peter–Müller bed load {TonsDay(mpm):N0} t/day (short tons).",
        };
        List<IReadOnlyList<object?>> hyd =
        [
            ["Unit discharge q", R(qIn / w, 3), qu], ["Depth (wide channel)", R(h.Y / len, 3), L], ["Velocity", R(h.V / len, 3), si ? "m/s" : "ft/s"],
            ["Shear velocity U*", R(h.UStar / len, 4), si ? "m/s" : "ft/s"], ["Shields parameter θ", R(h.Theta, 4), ""],
            ["Kinematic viscosity ν", R(nu * 1e6, 4), "mm²/s"], ["Fall velocity ω (Rubey)", R(wFall * 1000, 2), "mm/s"],
            ["Grain Reynolds number U*·D50/ν", R(h.UStar * d50 / nu, 1), ""],
        ];
        List<IReadOnlyList<object?>> cap =
        [
            ["Yang (1973) sand", R(ppm, 0), R(ToUser(yang), 4), R(TonsDay(yang), 1), d50mm is >= 0.062 and <= 2 ? "sand, 0.062–2 mm" : "outside sand range"],
            ["Engelund–Hansen", R(Ppm(eh), 0), R(ToUser(eh), 4), R(TonsDay(eh), 1), d50mm is >= 0.15 and <= 5 ? "sand bed, D50 > 0.15 mm" : "outside its range"],
            ["Meyer-Peter and Müller (bed load)", R(Ppm(mpm), 0), R(ToUser(mpm), 4), R(TonsDay(mpm), 1), d50mm is >= 0.4 and <= 30 ? "gravel bed load, 0.4–29 mm" : "outside its range"],
        ];
        if (d50mm < 0.062) msgs.Add(CalcMessage.Warning("W-ST-001", "D50 is silt or clay: these capacity equations are for sand and gravel; fine sediment moves as wash load set by supply."));
        if (d50mm > 2 && formula == "yang") msgs.Add(CalcMessage.Warning("W-ST-002", "Yang's sand equation is for D50 up to 2 mm; use mpm for gravel."));
        var tables = new List<ToolTable>
        {
            new("Hydraulics", ["Item", "Value", "Unit"], hyd, ["Wide channel: R = y = (q·n/(K·√S))^(3/5). Rubey: ω = F·√((Ss − 1)gd), F = √(2/3 + 36ν²/(gd³(Ss − 1))) − √(36ν²/(gd³(Ss − 1)))."]),
            new("Transport capacity", ["Formula", "Concentration (ppm by weight)", $"Volume rate ({Qu} of solids)", "Load (short tons/day)", "Applies to"], cap,
            [
                $"MPM grain n' = D90^(1/6)/26 = {Math.Pow(d90, 1.0 / 6) / 26:F4}; θ' = (n'/n)^1.5·θ; Φ = 8(θ' − 0.047)^1.5.",
                "Engelund–Hansen: Φ = 0.1·θ^2.5/f, f = 2gRS/V². Φ = qs/√((Ss − 1)gD50³).",
                "Yang: Vcr/ω = 2.5/(log(U*d/ν) − 0.06) + 0.66 for 1.2 < U*d/ν < 70, otherwise 2.05.",
            ]),
        };

        // Reach budget.
        if (reaches.Count > 0)
        {
            double Cap((string Name, double W, double S, double N, double Len) r)
            {
                double wr = r.W * len, hq = Q / wr;
                var hr = Wide(hq, r.N, r.S, d50, ss);
                return wr * formula switch { "mpm" => Mpm(hr, r.N, d50, d90, ss), "engelund-hansen" => EngelundHansen(hr, r.S, d50, ss), _ => Yang(hr, hq, r.S, d50, ss, nu) };
            }
            var rows = new List<IReadOnlyList<object?>>();
            double supply = Cap(reaches[0]);
            double tSec = dur * 3600;
            foreach (var r in reaches)
            {
                double c = Cap(r), dv = (supply - c) * tSec / (1 - por), dz = dv / (r.W * len * r.Len * len);
                rows.Add([r.Name, R(supply / q3, 4), R(c / q3, 4), R(dv / (len * len * len), 0), R(dz / len, 3), dz > 1e-6 ? "aggrades" : dz < -1e-6 ? "degrades" : "in balance"]);
                if (dz < 0) summary.Add($"{r.Name}: degrades about {-dz / len:F2} {L} over {dur:G3} h.");
                else if (dz > 0) summary.Add($"{r.Name}: aggrades about {dz / len:F2} {L} over {dur:G3} h.");
                supply = c;
            }
            tables.Add(new($"Reach sediment budget ({formula}, {dur:G3} h at Q)", ["Reach", $"Inflow ({Qu})", $"Capacity ({Qu})", $"Bed change ({L}³)", $"Average bed change ({L})", "Tendency"], rows,
            [
                "The first reach is assumed to be in equilibrium (its supply equals its capacity). Each reach passes on its capacity.",
                "ΔV = (Qs,in − Qs,out)·T/(1 − p), spread over width × length. Limits such as armoring or exposed rock are not applied.",
            ]));
        }
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
