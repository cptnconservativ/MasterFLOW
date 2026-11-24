using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Debris at bridge piers and hydraulic forces on the substructure and superstructure (FHWA HEC-9, NCHRP Report 653,
/// HEC-18 debris effective width, HDS-7 Ch. 7 and AASHTO LRFD 3.7.3): a design debris raft, the effective pier width and
/// the CSU pier scour with and without it, stream drag on the pier and the raft (longitudinal and lateral), and drag,
/// lift and buoyancy on an inundated deck.
/// </summary>
public sealed class BridgeDebrisTool : IDesignTool
{
    public string Id => "bridge-debris-forces";
    public string Name => "Bridge debris, pier scour with debris and hydraulic forces (HEC-9, HDS-7)";
    public string Description => "Design debris raft at a pier (width = design log length, thickness = half the flow depth by default, HEC-9 / NCHRP 653); effective pier width with debris a_d = [K1·T·W + (y − K1·T)·a]/y (K1 = 0.79 rectangular raft, 0.21 triangular; HEC-18) and the CSU pier scour with and without the raft; stream pressure on the pier and the raft F = ½·ρ·CD·V²·A with AASHTO LRFD Table 3.7.3.1 drag coefficients and the lateral coefficient by skew angle; deck drag, lift and buoyancy when the water surface reaches the low chord (HDS-7 Ch. 7), with the coefficients read from HDS-7 for the inundation ratio.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Bridge debris and hydraulic forces. SI: m, m/s.
        depth = 4.5               # approach flow depth at the pier, y1
        velocity = 2.8            # approach velocity at the pier, V1
        pier width = 1.0          # a
        pier length = 9.0         # L (for K2 and the lateral drag area)
        pier shape = round        # square | round | sharp (HEC-18 K1); drag: semicircular, square or wedged
        skew = 10                 # degrees between the flow and the pier axis
        bed = clear-water         # HEC-18 K3
        log length = 12           # design log length: the raft width W
        raft shape = rectangular  # rectangular | triangular
        # Deck (optional): low chord and deck depth above the bed datum, deck width (along the flow) and span.
        water surface = 6.2
        low chord = 5.8
        deck depth = 1.4
        deck width = 12
        span = 30
        deck drag coefficient = 2.0    # HDS-7 Fig. 7.x for the inundation ratio
        deck lift coefficient = -0.4   # negative = downward
        entrapped air = 0.5            # fraction of the space between girders holding air
        girder depth = 1.1
        """ : """
        # Bridge debris and hydraulic forces. U.S. units: ft, ft/s; forces in kips.
        depth = 15                # approach flow depth at the pier, y1
        velocity = 9              # approach velocity at the pier, V1
        pier width = 3            # a
        pier length = 30          # L (for K2 and the lateral drag area)
        pier shape = round        # square | round | sharp (HEC-18 K1); drag: semicircular, square or wedged
        skew = 10                 # degrees between the flow and the pier axis
        bed = clear-water         # HEC-18 K3
        log length = 40           # design log length: the raft width W
        raft shape = rectangular  # rectangular | triangular
        # Deck (optional): elevations above the bed at the pier, deck width (along the flow) and span.
        water surface = 20.5
        low chord = 19
        deck depth = 4.5
        deck width = 40
        span = 100
        deck drag coefficient = 2.0    # from HDS-7 for the inundation ratio
        deck lift coefficient = -0.4   # negative = downward
        entrapped air = 0.5            # fraction of the space between girders holding air
        girder depth = 3.5
        """;

    /// <summary>HEC-18 effective pier width with a debris raft: a_d = [K1·T·W + (y − K1·T)·a]/y.</summary>
    public static double EffectiveWidth(double k1, double t, double w, double y, double a) => (k1 * t * w + (y - k1 * t) * a) / y;

    /// <summary>CSU pier scour depth: ys = 2.0·K1·K2·K3·(y/a)^0.35·Fr^0.43·a.</summary>
    public static double Csu(double k1, double k2, double k3, double y, double a, double fr) => 2.0 * k1 * k2 * k3 * Math.Pow(y / a, 0.35) * Math.Pow(fr, 0.43) * a;

    /// <summary>AASHTO LRFD Table 3.7.3.2-1 lateral drag coefficient by angle between flow and pier axis (linear between entries).</summary>
    public static double LateralCoefficient(double deg)
    {
        double[] a = [0, 5, 10, 20, 30], c = [0, 0.5, 0.7, 0.9, 1.0];
        deg = Math.Abs(deg);
        if (deg >= 30) return 1.0;
        int i = 1; while (a[i] < deg) i++;
        return c[i - 1] + (c[i] - c[i - 1]) * (deg - a[i - 1]) / (a[i] - a[i - 1]);
    }

    /// <summary>AASHTO LRFD Table 3.7.3.1-1 longitudinal drag coefficient of a pier.</summary>
    public static double? PierDrag(string shape) => shape.ToLowerInvariant() switch
    {
        "round" or "round nose" or "semicircular" or "circular" or "cylinder" => 0.7,
        "square" or "square nose" or "square-ended" => 1.4,
        "sharp" or "sharp nose" or "wedged" => 0.8,
        _ => null,
    };

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Bridge debris and hydraulic forces";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.80665 : 32.174, rho = si ? 1000 : 1.94, gamma = rho * g, fScale = 1000;   // N → kN, lb → kips
        string L = si ? "m" : "ft", F = si ? "kN" : "kips";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double y = p.Number("depth", double.NaN), v = p.Number("velocity", double.NaN), a = p.Number("pier width", double.NaN), len = p.Number("pier length", double.NaN);
        string shape = p.Text("pier shape") ?? "round", bed = p.Text("bed") ?? "clear-water", raftShape = (p.Text("raft shape") ?? "rectangular").ToLowerInvariant();
        double skew = p.Number("skew", 0), logLen = p.Number("log length", double.NaN);
        double raftT = p.Number("raft thickness", 0.5 * y), cdRaft = p.Number("raft drag coefficient", 1.4);
        double? k1 = BridgeScourTool.ShapeFactor(shape), k3 = BridgeScourTool.BedFactor(bed), cdPier = PierDrag(shape);
        if (!(y > 0) || !(v > 0) || !(a > 0) || !(len >= a)) msgs.Add(CalcMessage.Error("E-BD-001", "Give depth, velocity and pier width above zero and the pier length at least the width."));
        if (k1 is null || cdPier is null) msgs.Add(CalcMessage.Error("E-BD-002", "pier shape must be square, round or sharp."));
        if (k3 is null) msgs.Add(CalcMessage.Error("E-BD-003", "bed must be clear-water, plane bed, antidune, small dunes, medium dunes or large dunes."));
        if (raftShape is not ("rectangular" or "triangular")) msgs.Add(CalcMessage.Error("E-BD-004", "raft shape must be rectangular or triangular."));
        if (!(logLen > 0)) msgs.Add(CalcMessage.Error("E-BD-005", $"Give the design log length ({L}) for the raft width; HEC-9 takes the longest sturdy log likely to reach the site, limited by the channel width upstream."));
        if (!(raftT > 0) || raftT > y || !(cdRaft > 0)) msgs.Add(CalcMessage.Error("E-BD-006", "raft thickness must be above zero and at most the depth; raft drag coefficient above zero."));
        double? ws = p.OptionalNumber("water surface"), lc = p.OptionalNumber("low chord"), dd = p.OptionalNumber("deck depth"), dw = p.OptionalNumber("deck width"), span = p.OptionalNumber("span");
        double? cdDeck = p.OptionalNumber("deck drag coefficient"), clDeck = p.OptionalNumber("deck lift coefficient");
        double air = p.Number("entrapped air", 0), girder = p.Number("girder depth", 0);
        bool deck = lc is not null;
        if (deck && (ws is null || dd is not > 0 || dw is not > 0 || span is not > 0)) msgs.Add(CalcMessage.Error("E-BD-007", "Deck forces need water surface, low chord, deck depth, deck width and span."));
        if (deck && ws > lc && (cdDeck is null || clDeck is null)) msgs.Add(CalcMessage.Error("E-BD-008", "The deck is inundated: give deck drag coefficient and deck lift coefficient from HDS-7 for the inundation ratio."));
        if (air is < 0 or > 1 || girder < 0 || (dd is { } d0 && girder > d0)) msgs.Add(CalcMessage.Error("E-BD-009", "entrapped air must be 0 to 1 and the girder depth between zero and the deck depth."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double fr = v / Math.Sqrt(g * y), th = skew * Math.PI / 180, la = Math.Min(len / a, 12);
        double k2 = Math.Pow(Math.Cos(th) + la * Math.Sin(th), 0.65);
        double kRaft = raftShape == "triangular" ? 0.21 : 0.79, w = logLen;
        double ad = EffectiveWidth(kRaft, raftT, w, y, a);
        double ysPier = Csu(k1!.Value, k2, k3!.Value, y, a, fr), ysDebris = Csu(1.0, 1.0, k3.Value, y, ad, fr);
        if (shape.StartsWith("square", StringComparison.OrdinalIgnoreCase) && skew > 5) msgs.Add(CalcMessage.Info("I-BD-001", "HEC-18: K1 = 1.0 for a square-nosed pier skewed more than 5°; the pier scour above keeps K1 = 1.1."));
        double q = 0.5 * rho * v * v;   // stream pressure per unit CD
        double fPier = q * cdPier!.Value * a * y / fScale;
        double cl = LateralCoefficient(skew), fLat = q * cl * len * y / fScale;
        double raftArea = raftShape == "triangular" ? 0.5 * w * raftT : w * raftT;
        double fRaft = q * cdRaft * raftArea / fScale;
        var summary = new List<string>
        {
            $"Design raft {w:G3} × {raftT:G3} {L} ({raftShape}): effective pier width {ad:F2} {L} against {a:G3} {L}; pier scour {ysPier:F1} {L} without debris, {ysDebris:F1} {L} with it.",
            $"Stream pressure: pier {fPier:F1} {F} longitudinal, {fLat:F1} {F} lateral at {skew:G3}°; debris raft {fRaft:F1} {F}.",
        };
        List<IReadOnlyList<object?>> scour =
        [
            ["Froude number Fr1", R(fr, 3), ""], ["K1 (pier shape)", k1.Value, ""], ["K2 (skew)", R(k2, 3), ""], ["K3 (bed)", k3.Value, ""],
            ["Pier scour without debris", R(ysPier, 2), L],
            ["Raft width W (design log length)", w, L], ["Raft thickness T", R(raftT, 2), L], [$"K1 for a {raftShape} raft", kRaft, ""],
            ["Effective width a_d = [K1·T·W + (y − K1·T)·a]/y", R(ad, 3), L],
            ["Pier scour with debris (a_d, K1 = K2 = 1)", R(ysDebris, 2), L],
        ];
        List<IReadOnlyList<object?>> force =
        [
            ["Stream pressure ½ρV²", R(q, 1), si ? "Pa" : "lb/ft²"],
            [$"Pier longitudinal drag (CD = {cdPier:G3}, area a·y)", R(fPier, 2), F],
            [$"Pier lateral drag (CL = {cl:G3}, area L·y)", R(fLat, 2), F],
            [$"Debris raft drag (CD = {cdRaft:G3}, area {R(raftArea, 1)} {L}²)", R(fRaft, 2), F],
            ["Pier plus debris, longitudinal", R(fPier + fRaft, 2), F],
        ];
        if (raftT >= 0.5 * y && !p.Has("raft thickness")) msgs.Add(CalcMessage.Info("I-BD-002", "Raft thickness defaults to half the flow depth; set raft thickness to change it."));

        if (deck)
        {
            double wsv = ws!.Value, lcv = lc!.Value, ddv = dd!.Value, dwv = dw!.Value, spv = span!.Value;
            double hInund = Math.Clamp(wsv - lcv, 0, ddv);
            double ratio = (wsv - lcv) / ddv;
            if (hInund <= 0) summary.Add($"The water surface is {lcv - wsv:F2} {L} below the low chord: no deck forces.");
            else
            {
                double fd = q * cdDeck!.Value * hInund * spv / fScale;
                double fl = q * clDeck!.Value * dwv * spv / fScale;
                // The girder zone (bottom girder depth) displaces water only where air (or girder) fills it; the slab above displaces fully.
                double subGirder = Math.Min(hInund, girder), subSlab = Math.Max(0, hInund - girder);
                double buoy = gamma * dwv * spv * (subGirder * air + subSlab) / fScale;
                force.Add([$"Inundation ratio h*/s = (WS − low chord)/deck depth", R(ratio, 3), ""]);
                force.Add([$"Deck drag (CD = {cdDeck:G3}, area {R(hInund, 2)} {L} × span)", R(fd, 2), F]);
                force.Add([$"Deck lift (CL = {clDeck:G3}, area deck width × span)", R(fl, 2), F]);
                force.Add([$"Buoyancy (slab below WS, plus {air * 100:G3} % air between girders)", R(buoy, 2), F]);
                summary.Add($"Deck inundated {hInund:F2} {L} (h*/s = {ratio:F2}): drag {fd:F0} {F}, lift {fl:F0} {F}, buoyancy {buoy:F0} {F}.");
                if (ratio > 1) msgs.Add(CalcMessage.Info("I-BD-004", "The deck is fully submerged (h*/s > 1); read CD and CL from HDS-7 for the submergence."));
            }
        }
        var tables = new List<ToolTable>
        {
            new("Debris and pier scour", ["Item", "Value", "Unit"], scour,
            [
                "CSU (HEC-18): ys = 2.0·K1·K2·K3·(y1/a)^0.35·Fr1^0.43·a; K2 = (cos θ + (L/a) sin θ)^0.65 with L/a ≤ 12.",
                "Debris (HEC-18 / NCHRP 653): a_d = [K1·T·W + (y − K1·T)·a]/y, K1 = 0.79 rectangular, 0.21 triangular; the raft faces the flow, so K1 = K2 = 1 with it.",
            ]),
            new("Hydraulic forces", ["Item", "Value", "Unit"], force,
            [
                "F = ½·ρ·C·V²·A (AASHTO LRFD 3.7.3; HDS-7 Ch. 7). Pier CD: semicircular-nosed 0.7, square-ended 1.4, wedged 0.8; debris lodged 1.4.",
                "Lateral CL by the angle between flow and pier axis: 0° 0, 5° 0.5, 10° 0.7, 20° 0.9, 30° or more 1.0.",
                "Deck drag acts on the inundated depth; lift on the deck plan area; buoyancy from the slab below the water surface and the entrapped air between girders.",
            ]),
        };
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => Math.Round(v, d) + 0.0;
}
