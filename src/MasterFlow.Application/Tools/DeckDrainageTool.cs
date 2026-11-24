using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Bridge deck drainage by FHWA HEC-21, <i>Design of Bridge Deck Drainage</i> (1993): gutter capacity at the allowable
/// spread, the deck length that fills it (rational method), deck drain (grate or scupper) interception on grade by the
/// HEC-12/HEC-22 grate equations with clogging, the spacing of the drains along the bridge, and the flow carried off
/// the end of the bridge.
/// </summary>
public sealed class DeckDrainageTool : IDesignTool
{
    public string Id => "deck-drainage";
    public string Name => "Bridge deck drainage (HEC-21): deck drain spacing";
    public string Description => "FHWA HEC-21 (1993) bridge deck drainage: gutter capacity at the allowable spread (Q = (0.56/n)·Sx^1.67·S^0.5·T^2.67), deck length to the first drain L = 43,560·(Qallow − Qapproach)/(C·i·W), drain interception on grade by the HEC-12/HEC-22 grate equations (frontal Eo, splash-over Rf, side flow Rs) with clogging for scuppers and small grates, spacing of the following drains L = 43,560·E·Qallow/(C·i·W), drain locations along the bridge and the flow left at the end of the deck. U.S. units.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Bridge deck drainage, FHWA HEC-21 (1993). U.S. units: ft, in/hr, cfs.
        # One side of the deck, crown to gutter, draining downhill along the bridge.
        bridge length = 600         # ft, along the gutter
        width = 26                  # ft of deck draining to this gutter (crown to curb)
        Sx = 0.02                   # deck cross slope
        S = 0.01                    # longitudinal slope
        n = 0.016                   # deck surface
        C = 0.9                     # runoff coefficient
        i = 6.0                     # rainfall intensity, in/hr (HEC-21: 5-min duration for the design frequency)
        T allow = 8                 # allowable spread, ft (shoulder width)
        approach Q = 0              # cfs arriving at the start of the deck (0 when intercepted on the approach)

        # Drain: grate or scupper. Grate: length L and width W (ft) and the splash-over velocity Vo (ft/s, HEC-22 for the
        # grate type). Scupper: diameter D (ft), taken as a square opening D × D. Clogging (%) shortens the effective
        # length; HEC-21 warns that small deck drains clog easily (50 % is common for scuppers).
        drain = grate
        L = 2.0
        W = 1.5
        Vo = 6.0
        clogging = 50
        """;

    /// <summary>Deck length (ft) that produces Q (cfs) from width W (ft): L = 43,560·Q/(C·i·W).</summary>
    public static double LengthFor(double q, double c, double i, double w) => 43560 * q / (c * i * w);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Bridge deck drainage (HEC-21)";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-DD-001", "The deck drainage tool uses HEC-21's U.S. equations (ft, cfs, in/hr); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double len = p.Number("bridge length", double.NaN), w = p.Number("width", double.NaN), sx = p.Number("Sx", double.NaN), s = p.Number("S", double.NaN);
        double n = p.Number("n", 0.016), c = p.Number("C", 0.9), i = p.Number("i", double.NaN), tAllow = p.Number("T allow", double.NaN), qApp = p.Number("approach Q", 0);
        string drain = (p.Text("drain") ?? "grate").ToLowerInvariant();
        double gl = p.Number("L", double.NaN), gw = p.Number("W", double.NaN), vo = p.Number("Vo", double.NaN), dia = p.Number("D", double.NaN), clog = p.Number("clogging", drain == "scupper" ? 50 : 0);
        if (!(len > 0) || !(w > 0)) msgs.Add(CalcMessage.Error("E-DD-002", "Give the bridge length and the drained width, both above zero."));
        if (!(sx > 0) || !(s > 0)) msgs.Add(CalcMessage.Error("E-DD-003", "Give the cross slope Sx and the longitudinal slope S above zero (a sag on the deck needs a drain at the low point, sized as a sag inlet)."));
        if (!(n > 0) || !(c is > 0 and <= 1) || !(i > 0) || !(tAllow > 0)) msgs.Add(CalcMessage.Error("E-DD-004", "Give n, C (0–1), the intensity i and the allowable spread T allow, all above zero."));
        if (qApp < 0) msgs.Add(CalcMessage.Error("E-DD-005", "approach Q cannot be negative."));
        if (clog is < 0 or >= 100) msgs.Add(CalcMessage.Error("E-DD-006", "clogging must be 0 to under 100 %."));
        if (drain == "scupper") { if (!(dia > 0)) msgs.Add(CalcMessage.Error("E-DD-007", "A scupper needs its diameter D (ft).")); else { gl = dia; gw = dia; } if (double.IsNaN(vo)) vo = 3.0; }
        else if (drain == "grate") { if (!(gl > 0) || !(gw > 0) || !(vo > 0)) msgs.Add(CalcMessage.Error("E-DD-008", "A grate needs L, W (ft) and the splash-over velocity Vo (ft/s).")); }
        else msgs.Add(CalcMessage.Error("E-DD-009", $"drain \"{drain}\" is not grate or scupper."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        if (drain == "scupper" && p.Text("Vo") is null) msgs.Add(CalcMessage.Info("I-DD-001", "Scupper: Vo = 3 ft/s is assumed for splash-over (a small round opening captures little frontal flow at speed); give Vo to change it."));

        double qAllow = GutterHydraulics.Capacity(tAllow, n, sx, s);
        double vAllow = GutterHydraulics.Velocity(tAllow, n, sx, s);
        double lEff = gl * (1 - clog / 100);
        double e = GutterHydraulics.GrateEfficiency(lEff, gw, tAllow, vAllow, vo, sx);
        double qi = e * qAllow;
        double l1 = LengthFor(qAllow - qApp, c, i, w);
        double lNext = LengthFor(qi, c, i, w);
        var summary = new List<string>();
        List<IReadOnlyList<object?>> design =
        [
            ["Gutter capacity at T allow, Qallow = (0.56/n)·Sx^1.67·S^0.5·T^2.67", R(qAllow, 3), "cfs"],
            ["Depth at the curb, T·Sx", R(tAllow * sx * 12, 2), "in."],
            ["Gutter velocity at T allow", R(vAllow, 2), "ft/s"],
            ["Runoff per foot of deck, C·i·W/43,560", R(c * i * w / 43560, 5), "cfs/ft"],
            ["Effective drain length (clogging)", R(lEff, 2), "ft"],
            ["Frontal flow ratio Eo = 1 − (1 − W/T)^2.67", R(GutterHydraulics.FrontalRatio(gw, tAllow), 3), ""],
            ["Drain efficiency E (at Qallow)", R(e, 3), ""],
            ["Interception per drain E·Qallow", R(qi, 3), "cfs"],
            ["Length to the first drain L1 = 43,560·(Qallow − Qapproach)/(C·i·W)", R(l1, 1), "ft"],
            ["Spacing of later drains L = 43,560·E·Qallow/(C·i·W)", R(lNext, 1), "ft"],
        ];
        var tables = new List<ToolTable> { new("Design values (HEC-21)", ["Item", "Value", "Unit"], design,
            ["Spread and velocity by the uniform cross-slope gutter equations; drain efficiency by HEC-22 Eqs. 4-18 to 4-21 (HEC-21 uses the HEC-12 forms of the same equations).",
             "Each drain passes (1 − E)·Qallow on; runoff from the next L ft of deck brings the gutter back to Qallow, so later drains are spaced L apart."]) };

        var loc = new List<IReadOnlyList<object?>>();
        if (qApp >= qAllow)
        {
            msgs.Add(CalcMessage.Warning("W-DD-001", $"The approach flow ({qApp:G3} cfs) already fills the gutter to the allowable spread; HEC-21 asks that approach flow be intercepted before the bridge."));
            l1 = 0;
        }
        if (l1 >= len)
        {
            double qEnd = qApp + c * i * w * len / 43560;
            summary.Add($"No deck drains needed: the gutter reaches {GutterHydraulics.Spread(qEnd, n, sx, s):F1} ft of spread at the end of the {len:G4} ft deck (allowable {tAllow:G3} ft, which it would reach at {l1:F0} ft).");
            msgs.Add(CalcMessage.Info("I-DD-002", "HEC-21 recommends as few deck drains as possible: carry the flow off the bridge and intercept it on the downstream approach."));
            loc.Add(["End of bridge", R(len, 1), R(qEnd, 3), R(GutterHydraulics.Spread(qEnd, n, sx, s), 2), null, null, "flow leaves the deck"]);
        }
        else
        {
            if (lNext < 1) return new(title, [], tables, [.. msgs, CalcMessage.Error("E-DD-010", $"The drain intercepts only {qi:G3} cfs; spacing would be under 1 ft. Use a larger grate.")]);
            double x = l1; int k = 0;
            while (x < len - 1e-9 && k < 500)
            {
                k++;
                loc.Add([$"Drain {k}", R(x, 1), R(qAllow, 3), R(tAllow, 2), R(qi, 3), R(qAllow - qi, 3), ""]);
                x += lNext;
            }
            double last = (double)loc[^1][1]!;
            double qEnd = qAllow - qi + c * i * w * (len - last) / 43560;
            double tEnd = GutterHydraulics.Spread(qEnd, n, sx, s);
            loc.Add(["End of bridge", R(len, 1), R(qEnd, 3), R(tEnd, 2), null, null, "carried onto the approach; intercept it there"]);
            summary.Add($"{k} {(drain == "scupper" ? "scuppers" : "grates")}: first at {l1:F0} ft, then every {lNext:F0} ft; {qEnd:F2} cfs leaves the deck.");
            if (k > 1 && lNext < 20) msgs.Add(CalcMessage.Warning("W-DD-002", $"Drains every {lNext:F0} ft: HEC-21 notes closely spaced small drains are costly and clog; consider a larger grate, a wider shoulder or a flatter cross slope check."));
            if (e < 0.3) msgs.Add(CalcMessage.Warning("W-DD-003", $"Each drain catches only {e:P0} of the gutter flow; HEC-21 finds small scuppers ineffective on grade. Consider larger grates or carrying the flow off the deck."));
        }
        tables.Add(new("Drain locations along the deck", ["Point", "Station from start (ft)", "Gutter Q (cfs)", "Spread (ft)", "Intercepted (cfs)", "Bypass (cfs)", "Notes"], loc,
            ["Stations run downhill from the start of the deck. Avoid drains over traffic lanes, railways or embankments below the bridge (HEC-21 Ch. 3); shift a drain a few feet and keep the spacing no greater than shown."]));
        msgs.Add(CalcMessage.Info("I-DD-003", "A sag vertical curve on the bridge needs a drain at the low point and flanking drains; size the sag drain with the sag ponding or gutter inlet tool."));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
