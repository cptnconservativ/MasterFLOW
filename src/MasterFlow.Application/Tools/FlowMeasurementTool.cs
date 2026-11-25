using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Discharge from a measured head at standard measuring devices, by the free-flow equations of the USBR <i>Water
/// Measurement Manual</i> (3rd ed., 2001): sharp-crested V-notch (Kindsvater–Shen), suppressed rectangular
/// (Kindsvater–Carter), fully contracted rectangular and Cipolletti weirs, and Parshall flumes with the submergence limit.
/// Gives the discharge at each measured head and a rating table for each device.
/// </summary>
public sealed class FlowMeasurementTool : IDesignTool
{
    public string Id => "flow-measurement";
    public string Name => "Flow measurement: weirs and Parshall flumes";
    public string Description => "USBR Water Measurement Manual free-flow equations (ft-s): V-notch Q = 4.28·Ce·tan(θ/2)·(h + kh)^2.5 (90°: Ce = 0.578, kh = 0.003 ft); suppressed rectangular Q = (3.22 + 0.40·h/P)·(L − 0.003)·(h + 0.003)^1.5; contracted rectangular Q = 3.33·(L − 0.2·h)·h^1.5; Cipolletti Q = 3.367·L·h^1.5; Parshall Q = C·Ha^n by throat width with the submergence limit Hb/Ha. Discharge at measured heads and rating tables.";
    public bool UsesNetwork => false;

    public enum Device { VNotch, Suppressed, Contracted, Cipolletti, Parshall }

    /// <summary>Parshall flume free-flow coefficient C, exponent n and submergence transition for throat width <paramref name="w"/> (ft); null when the width is not a standard size.</summary>
    public static (double C, double N, double Transition)? Parshall(double w)
    {
        static bool Is(double a, double b) => Math.Abs(a - b) < 1e-3;
        if (Is(w, 1.0 / 12)) return (0.338, 1.55, 0.50);
        if (Is(w, 2.0 / 12)) return (0.676, 1.55, 0.50);
        if (Is(w, 3.0 / 12)) return (0.992, 1.547, 0.50);
        if (Is(w, 0.5)) return (2.06, 1.58, 0.60);
        if (Is(w, 0.75)) return (3.07, 1.53, 0.60);
        if (w >= 1 - 1e-9 && w <= 8 + 1e-9 && (Is(w, Math.Round(w)) || Is(w, 1.5))) return (4 * w, 1.522 * Math.Pow(w, 0.026), 0.70);
        if (w >= 10 - 1e-9 && w <= 50 + 1e-9 && Is(w, Math.Round(w))) return (3.6875 * w + 2.5, 1.6, 0.80);
        return null;
    }

    /// <summary>Free-flow discharge (cfs) for head <paramref name="h"/> (ft). Size: notch angle (deg), crest length (ft) or throat width (ft).</summary>
    public static double Discharge(Device d, double size, double h, double p = double.NaN, double ce = double.NaN, double kh = double.NaN)
    {
        if (h <= 0) return 0;
        switch (d)
        {
            case Device.VNotch:
                double c = double.IsNaN(ce) ? 0.578 : ce, k = double.IsNaN(kh) ? 0.003 : kh;
                return 4.28 * c * Math.Tan(size * Math.PI / 360) * Math.Pow(h + k, 2.5);
            case Device.Suppressed:
                return (3.22 + 0.40 * h / p) * (size - 0.003) * Math.Pow(h + 0.003, 1.5);
            case Device.Contracted:
                return 3.33 * (size - 0.2 * h) * Math.Pow(h, 1.5);
            case Device.Cipolletti:
                return 3.367 * size * Math.Pow(h, 1.5);
            default:
                return Parshall(size) is { } f ? f.C * Math.Pow(h, f.N) : double.NaN;
        }
    }

    private static Device? Parse(string s) => s.Trim().ToLowerInvariant() switch
    {
        "v-notch" or "vnotch" or "v notch" or "triangular" => Device.VNotch,
        "suppressed" or "rectangular suppressed" or "full width" => Device.Suppressed,
        "contracted" or "rectangular" or "rectangular contracted" => Device.Contracted,
        "cipolletti" or "trapezoidal" => Device.Cipolletti,
        "parshall" or "parshall flume" => Device.Parshall,
        _ => null,
    };

    /// <summary>A length that may carry an "in" or "ft" suffix (e.g. "9 in", "3ft"); plain numbers are in the project length unit.</summary>
    private static double? Length(string s, bool si)
    {
        s = s.Trim().ToLowerInvariant();
        double mul = 1;
        if (s.EndsWith("in") || s.EndsWith("\"")) { mul = si ? 0.0254 : 1.0 / 12; s = s.TrimEnd('"').Replace("in", ""); }
        else if (s.EndsWith("ft") || s.EndsWith("'")) { mul = si ? 0.3048 : 1; s = s.TrimEnd('\'').Replace("ft", ""); }
        else if (s.EndsWith("mm")) { mul = si ? 0.001 : 1 / 304.8; s = s.Replace("mm", ""); }
        return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v * mul : null;
    }

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Flow measurement. SI: lengths and heads in m (or give "9 in"); Q in m³/s.
        rating max = 0.4
        rating step = 0.05
        [Devices]
        Name	Type	Size	P	Ha	Hb
        Pond outfall V-notch	v-notch	90		0.15
        Ditch flume	parshall	9 in		0.25	0.12
        Channel weir	suppressed	1.2	0.6	0.2
        """ : """
        # Flow measurement (free flow). U.S.: lengths and heads in ft (a size may be given as "9 in"); Q in cfs.
        # Type: v-notch (Size = notch angle, deg), suppressed (full-width rectangular; P = crest height above the approach floor),
        #       contracted (rectangular with end contractions; Size = crest length), cipolletti (Size = crest length),
        #       parshall (Size = throat width: 1, 2, 3, 6, 9 in or 1–8 ft, 10–50 ft).
        # Ha: measured head (weir head, or the Parshall upstream gauge); Hb: Parshall downstream gauge (submergence).
        # Ce, kh: V-notch coefficient and head correction for angles other than 90° (USBR WMM Fig. 7-5).
        rating max = 1.5
        rating step = 0.1
        [Devices]
        Name	Type	Size	P	Ha	Hb	Ce	kh
        Pond outfall V-notch	v-notch	90		0.48
        Ditch flume	parshall	9 in		0.8	0.4
        Channel weir	suppressed	4	2	0.65
        Cipolletti	cipolletti	3		0.5
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Flow measurement";
        bool si = context.Units == UnitSystem.SI;
        double toFt = si ? 1 / 0.3048 : 1, toQ = si ? 0.0283168 : 1;
        string L = si ? "m" : "ft", Uq = si ? "m³/s" : "cfs";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double rmax = p.Number("rating max", si ? 0.4 : 1.5, "rating max") * toFt, rstep = p.Number("rating step", si ? 0.05 : 0.1, "rating step") * toFt;
        if (!(rmax > 0) || !(rstep > 0) || rmax / rstep > 500) msgs.Add(CalcMessage.Error("E-WM-001", "rating max and rating step must be above zero, with at most 500 steps."));
        var devs = new List<(string N, Device D, double Size, double P, double? Ha, double? Hb, double Ce, double Kh)>();
        foreach (var r in p.Rows("Devices"))
        {
            string n = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Device {devs.Count + 1}";
            var d = Parse(r.Length > 1 ? r[1] : "");
            if (d is null) { msgs.Add(CalcMessage.Error("E-WM-002", $"{n}: type \"{(r.Length > 1 ? r[1] : "")}\" is not v-notch, suppressed, contracted, cipolletti or parshall.")); continue; }
            double? size = d == Device.VNotch ? p.Cell(r, 2, "notch angle", n) : r.Length > 2 ? Length(r[2], si) : null;
            if (size is not > 0 || (d == Device.VNotch && size >= 180)) { msgs.Add(CalcMessage.Error("E-WM-003", $"{n}: give the size (notch angle under 180°, crest length or throat width) above zero.")); continue; }
            double sz = d == Device.VNotch ? size.Value : size.Value * toFt;
            double? pp = p.Cell(r, 3, "P", n), ha = p.Cell(r, 4, "Ha", n), hb = p.Cell(r, 5, "Hb", n), ce = p.Cell(r, 6, "Ce", n), kh = p.Cell(r, 7, "kh", n);
            if (d == Device.Suppressed && pp is not > 0) { msgs.Add(CalcMessage.Error("E-WM-004", $"{n}: a suppressed weir needs P, the crest height above the approach channel floor.")); continue; }
            if (d == Device.Parshall && Parshall(sz) is null) { msgs.Add(CalcMessage.Error("E-WM-005", $"{n}: throat width {size:G4} {L} is not a standard Parshall size (1, 2, 3, 6, 9 in.; 1, 1.5, 2–8 ft; 10–50 ft).")); continue; }
            if (ha is < 0 || hb is < 0) { msgs.Add(CalcMessage.Error("E-WM-006", $"{n}: heads cannot be negative.")); continue; }
            if (d == Device.VNotch && Math.Abs(sz - 90) > 1e-6 && ce is null) msgs.Add(CalcMessage.Warning("W-WM-001", $"{n}: {sz:G3}° notch without Ce; the 90° values (Ce = 0.578, kh = 0.003 ft) are used. Read Ce and kh for this angle from USBR WMM Fig. 7-5."));
            devs.Add((n, d.Value, sz, (pp ?? double.NaN) * toFt, ha * toFt, hb * toFt, ce ?? double.NaN, (kh ?? double.NaN) * toFt));
        }
        if (devs.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-WM-007", "Give at least one row under [Devices]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var rows = new List<IReadOnlyList<object?>>();
        var summary = new List<string>();
        string Label(Device d) => d switch { Device.VNotch => "V-notch", Device.Suppressed => "Suppressed rectangular", Device.Contracted => "Contracted rectangular", Device.Cipolletti => "Cipolletti", _ => "Parshall flume" };
        foreach (var d in devs)
        {
            if (d.Ha is not { } ha) continue;
            string note = "";
            double q = Discharge(d.D, d.Size, ha, d.P, d.Ce, d.Kh);
            if (d.D == Device.Parshall && d.Hb is { } hb && ha > 0)
            {
                double s = hb / ha, tr = Parshall(d.Size)!.Value.Transition;
                if (s > tr)
                {
                    note = $"SUBMERGED (Hb/Ha = {s:F2} > {tr:F2})";
                    msgs.Add(CalcMessage.Warning("W-WM-002", $"{d.N}: submerged flow (Hb/Ha = {s:F2} exceeds {tr:F2}); the free-flow discharge is not valid. Apply the submerged-flow correction of USBR WMM Ch. 8 or lower the tailwater."));
                    q = double.NaN;
                }
                else note = $"free flow (Hb/Ha = {s:F2} ≤ {tr:F2})";
            }
            note = (note + Limits(d.D, d.Size, ha, d.P, d.N, msgs)).Trim(' ', ';');
            rows.Add([d.N, Label(d.D), R(d.D == Device.VNotch ? d.Size : d.Size / toFt, 3), R(ha / toFt, 3), R(q * toQ, 4), note]);
            summary.Add(double.IsNaN(q) ? $"{d.N}: submerged; no free-flow discharge." : $"{d.N}: Q = {q * toQ:F3} {Uq} at {ha / toFt:F3} {L}.");
        }
        var tables = new List<ToolTable>();
        if (rows.Count > 0) tables.Add(new("Discharge at the measured heads", ["Device", "Type", "Size", $"Ha ({L})", $"Q ({Uq})", "Notes"], rows,
            ["Size: notch angle (deg), crest length or throat width. Free-flow equations of the USBR Water Measurement Manual (2001), Ch. 7 and 8."]));
        // Rating tables: one column per device.
        var heads = new List<double>();
        for (double h = rstep; h <= rmax + 1e-9; h += rstep) heads.Add(h);
        var rrows = heads.Select(h => (IReadOnlyList<object?>)new object?[] { R(h / toFt, 3) }.Concat(devs.Select(d => (object?)R(Discharge(d.D, d.Size, h, d.P, d.Ce, d.Kh) * toQ, 4))).ToList()).ToList();
        tables.Add(new("Free-flow rating", [$"Head ({L})", .. devs.Select(d => $"{d.N} ({Uq})")], rrows,
            ["Free flow only. Contracted rectangular and Cipolletti heads above a third of the crest length, and V-notch heads outside 0.2–1.25 ft, are outside the equations' calibrated range."]));
        tables.Add(new("Devices", ["Device", "Type", "Equation"], devs.Select(d => (IReadOnlyList<object?>)[d.N, Label(d.D), d.D switch
        {
            Device.VNotch => $"Q = 4.28·{(double.IsNaN(d.Ce) ? 0.578 : d.Ce):G3}·tan({d.Size:G3}°/2)·(h + {(double.IsNaN(d.Kh) ? 0.003 : d.Kh):G3})^2.5",
            Device.Suppressed => $"Q = (3.22 + 0.40·h/{d.P:G4})·({d.Size:G4} − 0.003)·(h + 0.003)^1.5",
            Device.Contracted => $"Q = 3.33·({d.Size:G4} − 0.2·h)·h^1.5",
            Device.Cipolletti => $"Q = 3.367·{d.Size:G4}·h^1.5",
            _ => $"Q = {Parshall(d.Size)!.Value.C:G4}·Ha^{Parshall(d.Size)!.Value.N:G4}; submerged above Hb/Ha = {Parshall(d.Size)!.Value.Transition:G2}",
        }]).ToList(), [$"Equations in ft and cfs{(si ? "; SI input is converted to ft and the discharge back to m³/s" : "")}."]));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static string Limits(Device d, double size, double h, double p, string n, List<CalcMessage> msgs)
    {
        string? w = d switch
        {
            Device.VNotch when h < 0.2 || h > 1.25 => "head outside 0.2–1.25 ft",
            Device.Suppressed when h < 0.2 || h / p > 2.4 => "head under 0.2 ft or h/P above 2.4",
            Device.Contracted or Device.Cipolletti when h < 0.2 || h > size / 3 => "head under 0.2 ft or above L/3",
            _ => null,
        };
        if (w is null) return "";
        msgs.Add(CalcMessage.Warning("W-WM-003", $"{n}: {w}; outside the range the equation was calibrated for (USBR WMM Ch. 7)."));
        return "; " + w;
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
