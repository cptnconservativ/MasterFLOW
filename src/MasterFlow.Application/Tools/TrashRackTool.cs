using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Trash rack on an outlet or inlet structure: open-area ratio, approach and through-rack velocities, and head loss by
/// Kirschmer's bar-shape equation (clean rack) and the Creager–Justin / USBR net-area equation (clean and partly clogged).
/// </summary>
public sealed class TrashRackTool : IDesignTool
{
    public string Id => "trash-rack";
    public string Name => "Trash rack head loss and clogging";
    public string Description => "Clean rack: Kirschmer h = Kt·(t/b)^(4/3)·sinθ·Va²/2g with bar-shape factor Kt. Clean and clogged: h = (1.45 − 0.45·R − R²)·Vn²/2g with R = net/gross open area and Vn the velocity through the net open area (Creager and Justin 1950, USBR). Reports the open-area ratio, velocities against your limit and the head the outlet rating must add.";
    public bool UsesNetwork => false;

    /// <summary>Kirschmer bar-shape factor Kt; NaN for an unknown shape.</summary>
    public static double Kt(string shape) => shape.Trim().ToLowerInvariant() switch
    {
        "rectangular" or "flat" or "flat bar" or "square" => 2.42,
        "round nose" or "rounded upstream" => 1.83,
        "round" or "circular" or "round bar" or "rod" => 1.79,
        "round both" or "rounded both ends" => 1.67,
        _ => double.NaN,
    };

    /// <summary>Kirschmer head loss (length units of <paramref name="va"/> and g).</summary>
    public static double Kirschmer(double kt, double t, double b, double angleDeg, double va, double g)
        => kt * Math.Pow(t / b, 4.0 / 3) * Math.Sin(angleDeg * Math.PI / 180) * va * va / (2 * g);

    /// <summary>Creager–Justin / USBR head loss for net/gross open-area ratio <paramref name="r"/> and net velocity <paramref name="vn"/>.</summary>
    public static double NetArea(double r, double vn, double g) => (1.45 - 0.45 * r - r * r) * vn * vn / (2 * g);

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Trash rack. SI: Q in m³/s; width, height in m; bar thickness and clear spacing in mm.
        clogging = 50          # % of the open area blocked for the design check
        max velocity = 0.6     # m/s through the clogged net area (your criterion)
        [Racks]
        Name	Q	Width	Height	Bar t	Clear b	Angle	Shape
        Pond outlet	1.2	1.8	1.2	10	75	90	rectangular
        Culvert inlet	2.5	3.0	1.5	25	150	60	round
        """ : """
        # Trash rack. U.S.: Q in cfs; width and submerged height of the rack in ft; bar thickness t and clear spacing b in inches.
        # Angle: rack angle from horizontal (90 = vertical). Shape: rectangular, round nose, round, round both (Kirschmer).
        clogging = 50          # % of the open area blocked for the design check (50 % is common for outlet racks)
        max velocity = 2       # ft/s through the clogged net area (your criterion)
        [Racks]
        Name	Q	Width	Height	Bar t	Clear b	Angle	Shape
        Pond outlet	42	6	4	0.375	3	90	rectangular
        Culvert inlet	90	10	5	1	6	60	round
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Trash rack";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.80665 : 32.174, tb = si ? 1000 : 12;
        string L = si ? "m" : "ft", V = si ? "m/s" : "ft/s", A = si ? "m²" : "ft²";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double clog = p.Number("clogging", 50, "clogging") / 100;
        double? vmax = p.OptionalNumber("max velocity", "max velocity");
        if (!(clog >= 0 && clog < 1)) msgs.Add(CalcMessage.Error("E-TK-001", "clogging must be 0 to under 100 %."));
        if (vmax is <= 0) msgs.Add(CalcMessage.Error("E-TK-002", "max velocity must be above zero."));
        var racks = new List<(string N, double Q, double W, double H, double T, double B, double Ang, string Shape)>();
        foreach (var r in p.Rows("Racks"))
        {
            string n = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Rack {racks.Count + 1}";
            double? q = p.Cell(r, 1, "Q", n), w = p.Cell(r, 2, "width", n), h = p.Cell(r, 3, "height", n), t = p.Cell(r, 4, "bar t", n), b = p.Cell(r, 5, "clear b", n), a = p.Cell(r, 6, "angle", n);
            string shape = r.Length > 7 && r[7].Trim().Length > 0 ? r[7] : "rectangular";
            if (q is not > 0 || w is not > 0 || h is not > 0 || t is not > 0 || b is not > 0) { msgs.Add(CalcMessage.Error("E-TK-003", $"{n}: give Q, width, height, bar thickness and clear spacing, all above zero.")); continue; }
            double ang = a ?? 90;
            if (ang is <= 0 or > 90) { msgs.Add(CalcMessage.Error("E-TK-004", $"{n}: the angle from horizontal must be above 0° and at most 90°.")); continue; }
            if (double.IsNaN(Kt(shape))) { msgs.Add(CalcMessage.Error("E-TK-005", $"{n}: bar shape \"{shape}\" is not rectangular, round nose, round or round both.")); continue; }
            racks.Add((n, q.Value, w.Value, h.Value, t.Value / tb, b.Value / tb, ang, shape));
        }
        if (racks.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-TK-006", "Give at least one row under [Racks]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var rows = new List<IReadOnlyList<object?>>();
        var summary = new List<string>();
        foreach (var k in racks)
        {
            // Gross area: the rack face, measured along the rack (inclined racks are longer than the flow depth).
            double ag = k.W * k.H / Math.Sin(k.Ang * Math.PI / 180);
            double ratio = k.B / (k.B + k.T);
            double an = ag * ratio, anC = an * (1 - clog);
            double va = k.Q / (k.W * k.H), vn = k.Q / an, vnC = k.Q / anC;
            double hk = Kirschmer(Kt(k.Shape), k.T, k.B, k.Ang, va, g);
            double hc = NetArea(ratio, vn, g);
            double rC = anC / ag, hcC = NetArea(rC, vnC, g);
            bool vOk = vmax is not { } vm || vnC <= vm;
            if (!vOk) msgs.Add(CalcMessage.Warning("W-TK-001", $"{k.N}: {vnC:F2} {V} through the clogged rack exceeds {vmax:G3} {V}; enlarge the rack (needs {k.Q / vmax!.Value / ratio / (1 - clog):F1} {A} gross)."));
            if (k.Ang > 75) msgs.Add(CalcMessage.Info("I-TK-001", $"{k.N}: a near-vertical rack; sloping it (e.g. 3H:1V or flatter) lets debris ride up and reduces clogging."));
            rows.Add([k.N, k.Q, R(ag, 2), R(ratio, 3), R(va, 2), R(vn, 2), R(vnC, 2), R(hk, 3), R(hc, 3), R(hcC, 3), vmax is null ? "" : vOk ? "OK" : "TOO FAST"]);
            summary.Add($"{k.N}: head loss {hk:F3} {L} clean (Kirschmer), {hcC:F3} {L} at {clog * 100:F0} % clogged; {vnC:F2} {V} through the clogged rack.");
        }
        var table = new ToolTable("Trash rack hydraulics", ["Rack", si ? "Q (m³/s)" : "Q (cfs)", $"Gross area ({A})", "Open ratio b/(b+t)", $"Va approach ({V})", $"Vn clean ({V})", $"Vn clogged ({V})", $"h Kirschmer clean ({L})", $"h USBR clean ({L})", $"h USBR clogged ({L})", "Velocity"], rows,
        [
            "Gross area = width × submerged height / sin(angle). Open ratio from the bar thickness and clear spacing; clogging blocks that share of the open area.",
            "Kirschmer: h = Kt·(t/b)^(4/3)·sinθ·Va²/2g, Kt = 2.42 rectangular, 1.83 round nose, 1.79 round, 1.67 round both ends (clean rack only).",
            "USBR / Creager–Justin: h = (1.45 − 0.45·R − R²)·Vn²/2g with R = net/gross area (clogged R = open ratio × (1 − clogging)).",
            "Add the clogged head loss to the outlet's rating (orifice or weir head) when routing the design storm.",
        ]);
        return new ToolResult(title, summary, [table], msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
