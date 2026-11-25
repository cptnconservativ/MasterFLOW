using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Building roof drainage: flow to each roof drain from the roof area plus half the wall area draining onto it (IPC §1106.4),
/// vertical leader size from IPC Table 1106.2, horizontal building storm drain capacity by Manning full flow, and scuppers
/// as weirs with the ASCE 7 rain load R = 5.2(ds + dh) on the roof behind them.
/// </summary>
public sealed class RoofDrainageTool : IDesignTool
{
    public string Id => "roof-drainage";
    public string Name => "Roof drains, leaders and scuppers (IPC, ASCE 7 rain load)";
    public string Description => "Q = (roof area + ½ wall area)·i (IPC §1106.4, i = 100-yr 1-hr rainfall); vertical leader size from IPC Table 1106.2; horizontal building storm drain by Manning full flow (n = 0.015) at its slope; scupper head from the weir Q = C·b·h^1.5 (C = 3.22, FM Global DS 1-54) and the ASCE 7-16 rain load R = 5.2(ds + dh) psf.";
    public bool UsesNetwork => false;

    /// <summary>IPC Table 1106.2 capacities of circular vertical conductors and leaders (gpm) by nominal size (in.).</summary>
    public static readonly (double Size, double Gpm)[] Leaders = [(2, 30), (3, 92), (4, 192), (5, 360), (6, 563), (8, 1208)];

    /// <summary>Standard horizontal storm drain sizes (in.).</summary>
    public static readonly double[] DrainSizes = [3, 4, 5, 6, 8, 10, 12, 15];

    /// <summary>Manning full-flow capacity (gpm) of a round pipe of diameter <paramref name="dIn"/> (in.) at <paramref name="slopeInPerFt"/> (in./ft).</summary>
    public static double FullFlowGpm(double dIn, double slopeInPerFt, double n = 0.015)
    {
        double d = dIn / 12, a = Math.PI * d * d / 4, r = d / 4, s = slopeInPerFt / 12;
        return 1.486 / n * a * Math.Pow(r, 2.0 / 3) * Math.Sqrt(s) * 448.831;
    }

    /// <summary>Head (in.) on a scupper of width <paramref name="bIn"/> (in.) passing <paramref name="gpm"/>, from Q = C·b·h^1.5 in ft-s units.</summary>
    public static double ScupperHead(double gpm, double bIn, double c = 3.22) => 12 * Math.Pow(gpm / 448.831 / (c * bIn / 12), 2.0 / 3);

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Roof drainage. SI: areas in m², intensity in mm/h, sizes in mm, slope in %, heads in mm.
        intensity = 75             # 100-yr 1-hour rainfall, mm/h (IPC §1106.1)
        [Drains]
        Name	Roof area	Wall area	Leader	Horizontal	Slope (%)
        RD-1	550	40	100	150	1.0
        RD-2	900	0	125	150	2.0
        [Scuppers]
        Name	Roof area	Wall area	Width	ds
        OF-1	550	40	300	50
        """ : """
        # Roof drainage. U.S.: areas in ft², intensity in in./h, sizes in inches, slope in in./ft, heads in inches.
        # Intensity: 100-yr 1-hour rainfall (IPC §1106.1). Rapid City IDCM Table 4-2 gives 2.95 in./h for the 100-yr 60-min storm.
        intensity = 2.95
        # Wall area: vertical wall that sheds water onto the roof area; half of it is added (IPC §1106.4).
        # Leader and Horizontal: the sizes shown on the plans (blank = size them).
        [Drains]
        Name	Roof area	Wall area	Leader	Horizontal	Slope (in/ft)
        RD-1	6000	400	4	6	1/8
        RD-2	9500	0	5	6	1/4
        # Secondary (overflow) scuppers: each sized for the full flow of its area with the primary drains blocked (IPC §1108).
        # ds: depth of water from the roof drain inlet to the scupper invert (static head) for the ASCE 7 rain load.
        [Scuppers]
        Name	Roof area	Wall area	Width	ds
        OF-1	6000	400	12	2
        """;

    private static bool TrySlope(string s, out double v)
    {
        s = s.Trim();
        int slash = s.IndexOf('/');
        if (slash > 0 && ToolInput.TryNumber(s[..slash], out double a) && ToolInput.TryNumber(s[(slash + 1)..], out double b) && b != 0) { v = a / b; return true; }
        return ToolInput.TryNumber(s, out v);
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Roof drainage";
        bool si = context.Units == UnitSystem.SI;
        // Computed in U.S. units; SI is converted on entry and back on output.
        double aF = si ? 10.7639 : 1, iF = si ? 1 / 25.4 : 1, dF = si ? 1 / 25.4 : 1;
        string Ua = si ? "m²" : "ft²", Uq = si ? "L/s" : "gpm", Ud = si ? "mm" : "in.", Uh = si ? "mm" : "in.", Ur = si ? "kPa" : "psf";
        double Q(double gpm) => si ? gpm * 0.0630902 : gpm;
        double Sz(double inch) => si ? inch switch { 2 => 50, 3 => 75, 4 => 100, 5 => 125, 6 => 150, 8 => 200, 10 => 250, 12 => 300, 15 => 375, _ => inch * 25 } : inch;
        double FromSz(double mm) => si ? mm switch { 50 => 2, 75 => 3, 100 => 4, 125 => 5, 150 => 6, 200 => 8, 250 => 10, 300 => 12, 375 => 15, _ => mm / 25.4 } : mm;
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double i = p.Number("intensity", double.NaN, "intensity") * iF;
        double c = p.Number("weir C", 3.22, "weir C");
        if (!(i > 0)) msgs.Add(CalcMessage.Error("E-RF-001", "Give intensity: the 100-year 1-hour rainfall (IPC §1106.1)."));
        if (!(c > 0)) msgs.Add(CalcMessage.Error("E-RF-002", "weir C must be above zero."));

        var drains = new List<(string N, double A, double Leader, double Horiz, double Slope)>();
        foreach (var r in p.Rows("Drains"))
        {
            string n = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Drain {drains.Count + 1}";
            double? a = p.Cell(r, 1, "roof area", n), w = p.Cell(r, 2, "wall area", n), le = p.Cell(r, 3, "leader", n), ho = p.Cell(r, 4, "horizontal", n);
            double slope = double.NaN;
            if (r.Length > 5 && r[5].Trim().Length > 0)
            {
                if (!TrySlope(r[5], out slope)) { msgs.Add(CalcMessage.Error("E-RF-003", $"{n}: slope \"{r[5]}\" is not a number or a fraction such as 1/8.")); continue; }
                if (si) slope = slope / 100 * 12;                                    // % → in./ft
            }
            if (a is not > 0 || w is < 0) { msgs.Add(CalcMessage.Error("E-RF-004", $"{n}: give the roof area above zero (wall area zero or more).")); continue; }
            if (ho is > 0 && !(slope > 0)) { msgs.Add(CalcMessage.Error("E-RF-005", $"{n}: give the slope of the horizontal drain.")); continue; }
            drains.Add((n, (a.Value + 0.5 * (w ?? 0)) * aF, le is > 0 ? FromSz(le.Value) : double.NaN, ho is > 0 ? FromSz(ho.Value) : double.NaN, slope));
        }
        var scuppers = new List<(string N, double A, double B, double Ds)>();
        foreach (var r in p.Rows("Scuppers"))
        {
            string n = r.Length > 0 && r[0].Length > 0 ? r[0] : $"Scupper {scuppers.Count + 1}";
            double? a = p.Cell(r, 1, "roof area", n), w = p.Cell(r, 2, "wall area", n), b = p.Cell(r, 3, "width", n), ds = p.Cell(r, 4, "ds", n);
            if (a is not > 0 || b is not > 0 || w is < 0 || ds is < 0) { msgs.Add(CalcMessage.Error("E-RF-006", $"{n}: give the roof area and scupper width above zero (wall area and ds zero or more).")); continue; }
            scuppers.Add((n, (a.Value + 0.5 * (w ?? 0)) * aF, b.Value * dF, (ds ?? 0) * dF));
        }
        if (drains.Count + scuppers.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error))
            msgs.Add(CalcMessage.Error("E-RF-007", "Give at least one row under [Drains] or [Scuppers]."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        const double GpmPerSfIn = 0.0103889;                                         // 1 in./h on 1 ft² = 0.0104 gpm
        var tables = new List<ToolTable>();
        var summary = new List<string>();
        if (drains.Count > 0)
        {
            var rows = new List<IReadOnlyList<object?>>();
            foreach (var d in drains)
            {
                double gpm = d.A * i * GpmPerSfIn;
                var need = Leaders.FirstOrDefault(x => x.Gpm >= gpm);
                string leaderReq = need.Size > 0 ? Sz(need.Size).ToString("G4") : "> " + Sz(8);
                double capL = double.IsNaN(d.Leader) ? double.NaN : Leaders.FirstOrDefault(x => Math.Abs(x.Size - d.Leader) < 1e-6).Gpm;
                if (!double.IsNaN(d.Leader) && capL == 0) { capL = double.NaN; msgs.Add(CalcMessage.Warning("W-RF-001", $"{d.N}: leader {Sz(d.Leader):G4} {Ud} is not a size in IPC Table 1106.2.")); }
                string lChk = double.IsNaN(d.Leader) ? "" : double.IsNaN(capL) ? "?" : capL >= gpm ? "OK" : "TOO SMALL";
                double hReq = double.IsNaN(d.Slope) ? double.NaN : DrainSizes.FirstOrDefault(s => FullFlowGpm(s, d.Slope) >= gpm);
                double capH = double.IsNaN(d.Horiz) ? double.NaN : FullFlowGpm(d.Horiz, d.Slope);
                string hChk = double.IsNaN(d.Horiz) ? "" : capH >= gpm ? "OK" : "TOO SMALL";
                if (lChk == "TOO SMALL") msgs.Add(CalcMessage.Warning("W-RF-002", $"{d.N}: the {Sz(d.Leader):G4} {Ud} leader carries {Q(capL):F0} {Uq}, less than {Q(gpm):F0} {Uq}; use {leaderReq} {Ud}."));
                if (hChk == "TOO SMALL") msgs.Add(CalcMessage.Warning("W-RF-003", $"{d.N}: the {Sz(d.Horiz):G4} {Ud} horizontal drain carries {Q(capH):F0} {Uq} full, less than {Q(gpm):F0} {Uq}."));
                rows.Add([d.N, R(d.A / aF, 0), R(Q(gpm), si ? 2 : 1), leaderReq, double.IsNaN(d.Leader) ? null : (object)Sz(d.Leader), R(Q(capL), 1), lChk,
                          double.IsNaN(d.Slope) ? null : (object)R(si ? d.Slope / 12 * 100 : d.Slope, 4), hReq > 0 ? (object)Sz(hReq) : double.IsNaN(hReq) ? null : "> " + Sz(15), double.IsNaN(d.Horiz) ? null : (object)Sz(d.Horiz), R(Q(capH), 1), hChk]);
                summary.Add($"{d.N}: {Q(gpm):F0} {Uq} → leader {leaderReq} {Ud} minimum{(lChk.Length > 0 ? $" ({lChk} as drawn)" : "")}{(hChk.Length > 0 ? $"; horizontal drain {hChk}" : "")}.");
            }
            tables.Add(new("Roof drains, leaders and horizontal storm drains", ["Drain", $"Area ({Ua})", $"Q ({Uq})", $"Leader req. ({Ud})", $"Leader drawn ({Ud})", $"Leader cap. ({Uq})", "Leader", si ? "Slope (%)" : "Slope (in/ft)", $"Horiz. req. ({Ud})", $"Horiz. drawn ({Ud})", $"Horiz. cap. ({Uq})", "Horizontal"], rows,
                [$"Area = roof + ½ wall area (IPC §1106.4). Q = area · i, i = {(si ? i * 25.4 : i):G4} {(si ? "mm/h" : "in./h")}.",
                 "Leader capacity: IPC Table 1106.2 (2 in. 30, 3 in. 92, 4 in. 192, 5 in. 360, 6 in. 563, 8 in. 1,208 gpm).",
                 "Horizontal capacity: Manning full flow, n = 0.015 (the basis of the IPC horizontal storm drain table). Check the roof drain body's own rating from the manufacturer."]));
        }
        if (scuppers.Count > 0)
        {
            var rows = new List<IReadOnlyList<object?>>();
            foreach (var s in scuppers)
            {
                double gpm = s.A * i * GpmPerSfIn;
                double dh = ScupperHead(gpm, s.B, c);
                double rl = 5.2 * (s.Ds + dh);                                     // psf
                if (dh > s.B / 2) msgs.Add(CalcMessage.Warning("W-RF-004", $"{s.N}: head {dh * (si ? 25.4 : 1):F1} {Uh} is more than half the scupper width; a wider scupper keeps the rain load down."));
                if (dh > 4) msgs.Add(CalcMessage.Info("I-RF-001", $"{s.N}: scupper head above 4 in.; check that the scupper opening is at least that deep plus freeboard (IPC §1108.3)."));
                rows.Add([s.N, R(s.A / aF, 0), R(Q(gpm), si ? 2 : 1), R(si ? s.B * 25.4 : s.B, 1), R(si ? s.Ds * 25.4 : s.Ds, 2), R(si ? dh * 25.4 : dh, 2), R(si ? rl * 0.0478803 : rl, si ? 3 : 1)]);
                summary.Add($"{s.N}: head {dh * (si ? 25.4 : 1):F1} {Uh}; rain load R = {(si ? rl * 0.0478803 : rl):F2} {Ur}.");
            }
            tables.Add(new("Scuppers (secondary drainage) and ASCE 7 rain load", ["Scupper", $"Area ({Ua})", $"Q ({Uq})", $"Width ({Ud})", $"ds ({Uh})", $"dh ({Uh})", $"R ({Ur})"], rows,
                [$"dh from Q = C·b·h^1.5 with C = {c:G3} (ft-s; equals Q(gpm) = 2.9·b·h^1.5 with b and h in inches, FM Global DS 1-54).",
                 "R = 5.2(ds + dh) psf, ds and dh in inches (ASCE 7-16 Eq. 8.3-1). ASCE 7-22 adds the ponding head dp and uses the 15-min rainfall; enter ds to include dp if you design to 7-22."]));
        }
        msgs.Add(CalcMessage.Info("I-RF-002", "Secondary drains or scuppers must be sized for the same rainfall as the primary drains and discharge where they are visible (IPC §1108)."));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
