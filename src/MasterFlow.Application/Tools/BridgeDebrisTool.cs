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
    public string Description => "Design debris raft at a pier (width = design log length, the smallest of the upstream channel width, the sturdy-log length and 30 ft + W/4 (HEC-9 §3.3.2); height = the flow depth by default, HEC-9 Table 3.4); HEC-9 §4.6 debris loads (hydrostatic difference γ(hcu·Ahu − hcd·Ahd) plus drag with CD from HEC-9 Table 4.1 by blockage ratio and Froude number) when the upstream and downstream water surfaces are given; effective pier width with debris a_d = [K1·T·W + (y − K1·T)·a]/y (K1 = 0.79 rectangular raft, 0.21 triangular; HEC-18) and the CSU pier scour with and without the raft; stream pressure on the pier and the raft F = ½·ρ·CD·V²·A with AASHTO LRFD Table 3.7.3.1 drag coefficients and the lateral coefficient by skew angle; deck drag, lift and buoyancy when the water surface reaches the low chord (HDS-7 Ch. 7), with CD, CL and CM from the FHWA-HRT-09-028 envelope equations (cited by HDS-7 §11.1.4) for a six-girder, three-girder or streamlined deck unless entered.";
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
        deck type = six-girder         # six-girder | three-girder | streamlined (FHWA-HRT-09-028 coefficients)
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
        log length = 40           # design log length: the raft width W (or give channel width upstream)
        raft shape = rectangular  # rectangular | triangular
        # HEC-9 §4.6 debris loads (optional): elevations of the upstream and downstream water surfaces and the bottom of
        # the accumulation, and the unobstructed flow area of the contracted section for the blockage ratio.
        # upstream water surface = 214.25
        # downstream water surface = 211.82
        # debris bottom = 203.35
        # contracted area = 295
        # Deck (optional): elevations above the bed at the pier, deck width (along the flow) and span.
        water surface = 20.5
        low chord = 19
        deck depth = 4.5
        deck width = 40
        span = 100
        deck type = six-girder         # six-girder | three-girder | streamlined (FHWA-HRT-09-028 coefficients)
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

    /// <summary>
    /// HEC-9 §3.3.2 (Diehl 1997) design log length: the smallest of the upstream channel width, the maximum sturdy-log
    /// length (about 80 ft / 24 m in much of the U.S.) and, outside northern California and the Pacific Northwest,
    /// 30 ft (9 m) plus a quarter of the upstream channel width.
    /// </summary>
    public static double DesignLogLength(double channelWidth, double sturdyLog, bool pacificNorthwest, bool si) =>
        Math.Min(Math.Min(channelWidth, sturdyLog), pacificNorthwest ? double.PositiveInfinity : (si ? 9 : 30) + channelWidth / 4);

    /// <summary>HEC-9 Table 4.1 drag coefficient for debris on piers by blockage ratio B and Froude number (NCHRP 445, Parola).</summary>
    public static double DebrisDragPier(double b, double fr) =>
        b < 0.36 ? (fr < 0.4 ? 1.8 : 2.6 - 2.0 * Math.Min(fr, 0.8)) : b < 0.77 ? 3.1 - 3.6 * b : 1.4 - 1.4 * b;

    /// <summary>
    /// FHWA-HRT-09-028 (Kerenyi et al. 2009, cited by HDS-7 §11.1.4) envelope coefficients for an inundated deck at inundation
    /// ratio h* = (hu − hb)/s: drag CD = A·e^(−2h*²) − B·e^(−0.75h*²) + a (girder decks) or the streamlined form, lift
    /// CL = b·(e^(−3.5h*²) − e^(−c·h*^1.7)), moment CM = d·h*^α·e^(−f·h*²) + g. Returns (CD, CL, CM) for the upper and lower envelope.
    /// </summary>
    public static ((double Cd, double Cl, double Cm) Upper, (double Cd, double Cl, double Cm) Lower)? DeckCoefficients(string type, double hs)
    {
        hs = Math.Max(hs, 0);
        static double Cd(double A, double B, double a, double h) => A * Math.Exp(-2 * h * h) - B * Math.Exp(-0.75 * h * h) + a;
        static double Cl(double b, double c, double h) => b * (Math.Exp(-3.5 * h * h) - Math.Exp(-c * Math.Pow(h, 1.7)));
        static double Cm(double d, double al, double f, double g, double h) => d * Math.Pow(h, al) * Math.Exp(-f * h * h) + g;
        static double CdS(double m, double n, double h) => 0.8 * Math.Exp(-m * h * h * h) + 0.33 * Math.Pow(h, 0.6) * (1 - Math.Exp(-0.04 * Math.Pow(h, 5.55))) + n;
        static double ClS(double j, double h) => 1.5 * Math.Exp(-1.6 * h * h) + 0.4213 * Math.Pow(h, 0.4) * (1 - Math.Exp(-0.0626 * Math.Pow(h, 4.4147))) - j;
        return type.Trim().ToLowerInvariant().Replace(" ", "-") switch
        {
            "six-girder" or "6-girder" or "girder" => ((Cd(2.7, 2.7, 2.15, hs), Cl(2, 0.65, hs), Cm(1, 2, 1.4, 0.03, hs)), (Cd(2.7, 2.7, 1.75, hs), Cl(3, 0.62, hs), Cm(0.8, 2, 1.6, -0.07, hs))),
            "three-girder" or "3-girder" => ((Cd(1.8, 2, 1.95, hs), Cl(2, 0.6, hs), Cm(1, 2, 1.5, 0, hs)), (Cd(1.8, 2, 1.62, hs), Cl(3, 0.5, hs), Cm(0.8, 2, 1.5, -0.11, hs))),
            "streamlined" => ((CdS(1.2, 0.25, hs), ClS(1.4, hs), Cm(0.25, 3, 0.8, 0.05, hs)), (CdS(1.7, 0.11, hs), ClS(1.0, hs), Cm(0.25, 3, 0.8, 0, hs))),
            _ => null,
        };
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
        double? chanW = p.OptionalNumber("channel width upstream");
        bool pnw = (p.Text("pacific northwest") ?? "no").Trim().ToLowerInvariant() is "yes" or "true" or "y";
        double sturdy = p.Number("sturdy log length", pnw ? (si ? 45 : 150) : (si ? 24 : 80));
        bool logFromWidth = double.IsNaN(logLen) && chanW is > 0;
        if (logFromWidth) logLen = DesignLogLength(chanW!.Value, sturdy, pnw, si);
        // HEC-9 §4.6 loads: the accumulation from its bottom to the upstream water surface.
        double? wsUs = p.OptionalNumber("upstream water surface"), wsDs = p.OptionalNumber("downstream water surface"), dbel = p.OptionalNumber("debris bottom");
        double? ac = p.OptionalNumber("contracted area");
        double otherAd = p.Number("other debris area", 0);
        bool hec9 = wsUs is not null || wsDs is not null || dbel is not null;
        if (hec9 && (wsUs is null || wsDs is null || dbel is null || !(wsUs > dbel) || wsDs > wsUs))
            msgs.Add(CalcMessage.Error("E-BD-010", "HEC-9 debris loads need upstream water surface, downstream water surface and debris bottom, with the upstream surface above the debris bottom and not below the downstream surface."));
        if (ac is <= 0 || otherAd < 0) msgs.Add(CalcMessage.Error("E-BD-011", "contracted area must be above zero and other debris area zero or more."));
        double raftT = p.Number("raft thickness", hec9 && wsUs > dbel ? wsUs!.Value - dbel!.Value : y);
        double? cdRaftIn = p.OptionalNumber("raft drag coefficient");
        double vr = p.Number("reference velocity", v), yr = p.Number("reference depth", y);
        double? k1 = BridgeScourTool.ShapeFactor(shape), k3 = BridgeScourTool.BedFactor(bed), cdPier = PierDrag(shape);
        if (!(y > 0) || !(v > 0) || !(a > 0) || !(len >= a)) msgs.Add(CalcMessage.Error("E-BD-001", "Give depth, velocity and pier width above zero and the pier length at least the width."));
        if (k1 is null || cdPier is null) msgs.Add(CalcMessage.Error("E-BD-002", "pier shape must be square, round or sharp."));
        if (k3 is null) msgs.Add(CalcMessage.Error("E-BD-003", "bed must be clear-water, plane bed, antidune, small dunes, medium dunes or large dunes."));
        if (raftShape is not ("rectangular" or "triangular")) msgs.Add(CalcMessage.Error("E-BD-004", "raft shape must be rectangular or triangular."));
        if (!(logLen > 0)) msgs.Add(CalcMessage.Error("E-BD-005", $"Give the design log length ({L}) or the channel width upstream for the raft width (HEC-9 §3.3.2: the smallest of the channel width, the sturdy-log length and {(si ? "9 m" : "30 ft")} + width/4)."));
        if (!(raftT > 0) || (!hec9 && raftT > y + 1e-9) || cdRaftIn is <= 0) msgs.Add(CalcMessage.Error("E-BD-006", "raft thickness must be above zero and at most the depth; raft drag coefficient above zero."));
        if (!(vr > 0) || !(yr > 0)) msgs.Add(CalcMessage.Error("E-BD-012", "reference velocity and reference depth must be above zero."));
        double? ws = p.OptionalNumber("water surface"), lc = p.OptionalNumber("low chord"), dd = p.OptionalNumber("deck depth"), dw = p.OptionalNumber("deck width"), span = p.OptionalNumber("span");
        double? cdDeck = p.OptionalNumber("deck drag coefficient"), clDeck = p.OptionalNumber("deck lift coefficient"), cmDeck = p.OptionalNumber("deck moment coefficient");
        string deckType = p.Text("deck type") ?? "six-girder";
        double air = p.Number("entrapped air", 0), girder = p.Number("girder depth", 0);
        bool deck = lc is not null;
        if (deck && (ws is null || dd is not > 0 || dw is not > 0 || span is not > 0)) msgs.Add(CalcMessage.Error("E-BD-007", "Deck forces need water surface, low chord, deck depth, deck width and span."));
        if (deck && DeckCoefficients(deckType, 0) is null && (cdDeck is null || clDeck is null)) msgs.Add(CalcMessage.Error("E-BD-008", "deck type must be six-girder, three-girder or streamlined (FHWA-HRT-09-028), or give deck drag coefficient and deck lift coefficient."));
        if (air is < 0 or > 1 || girder < 0 || (dd is { } d0 && girder > d0)) msgs.Add(CalcMessage.Error("E-BD-009", "entrapped air must be 0 to 1 and the girder depth between zero and the deck depth."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double fr = v / Math.Sqrt(g * y), th = skew * Math.PI / 180, la = Math.Min(len / a, 12);
        double k2 = Math.Pow(Math.Cos(th) + la * Math.Sin(th), 0.65);
        double kRaft = raftShape == "triangular" ? 0.21 : 0.79, w = logLen;
        double ad = EffectiveWidth(kRaft, Math.Min(raftT, y), w, y, a);
        double ysPier = Csu(k1!.Value, k2, k3!.Value, y, a, fr), ysDebris = Csu(1.0, 1.0, k3.Value, y, ad, fr);
        if (shape.StartsWith("square", StringComparison.OrdinalIgnoreCase) && skew > 5) msgs.Add(CalcMessage.Info("I-BD-001", "HEC-18: K1 = 1.0 for a square-nosed pier skewed more than 5°; the pier scour above keeps K1 = 1.1."));
        double q = 0.5 * rho * v * v;   // stream pressure per unit CD
        double fPier = q * cdPier!.Value * a * y / fScale;
        double cl = LateralCoefficient(skew), fLat = q * cl * len * y / fScale;
        double raftArea = raftShape == "triangular" ? 0.5 * w * raftT : w * raftT;
        double frRef = vr / Math.Sqrt(g * yr), blockage = double.NaN;
        double cdRaft;
        string cdSource;
        if (cdRaftIn is { } cdi) { cdRaft = cdi; cdSource = "given"; }
        else if (ac is { } acv)
        {
            double adTot = raftArea + otherAd;
            blockage = adTot / (adTot + acv);
            cdRaft = DebrisDragPier(blockage, frRef);
            cdSource = $"HEC-9 Table 4.1, B = {blockage:F2}, Fr = {frRef:F2}";
            if (frRef >= (blockage < 0.36 ? 0.8 : 1.0)) msgs.Add(CalcMessage.Warning("W-BD-002", $"Fr = {frRef:F2} is beyond the range of HEC-9 Table 4.1 for B = {blockage:F2}; the coefficient is taken at the table's limit."));
        }
        else { cdRaft = 1.4; cdSource = "AASHTO LRFD 3.7.3.1, debris lodged; give contracted area for HEC-9 Table 4.1"; }
        double qr = 0.5 * rho * vr * vr;
        double fRaft = qr * cdRaft * raftArea / fScale;
        var summary = new List<string>
        {
            $"Design raft {w:G3} × {raftT:G3} {L} ({raftShape}): effective pier width {ad:F2} {L} against {a:G3} {L}; pier scour {ysPier:F1} {L} without debris, {ysDebris:F1} {L} with it.",
            $"Stream pressure: pier {fPier:F1} {F} longitudinal, {fLat:F1} {F} lateral at {skew:G3}°; debris raft {fRaft:F1} {F}.",
        };
        List<IReadOnlyList<object?>> scour =
        [
            ["Froude number Fr1", R(fr, 3), ""], ["K1 (pier shape)", k1.Value, ""], ["K2 (skew)", R(k2, 3), ""], ["K3 (bed)", k3.Value, ""],
            ["Pier scour without debris", R(ysPier, 2), L],
            ["Raft width W (design log length)", R(w, 2), L], ["Raft thickness T", R(raftT, 2), L], [$"K1 for a {raftShape} raft", kRaft, ""],
            ["Effective width a_d = [K1·T·W + (y − K1·T)·a]/y", R(ad, 3), L],
            ["Pier scour with debris (a_d, K1 = K2 = 1)", R(ysDebris, 2), L],
        ];
        List<IReadOnlyList<object?>> force =
        [
            ["Stream pressure ½ρV²", R(q, 1), si ? "Pa" : "lb/ft²"],
            [$"Pier longitudinal drag (CD = {cdPier:G3}, area a·y)", R(fPier, 2), F],
            [$"Pier lateral drag (CL = {cl:G3}, area L·y)", R(fLat, 2), F],
            [$"Debris raft drag (CD = {R(cdRaft, 3):G3}, area {R(raftArea, 1)} {L}²)", R(fRaft, 2), F],
            ["Pier plus debris, longitudinal", R(fPier + fRaft, 2), F],
        ];
        force.Insert(4, ["Debris drag coefficient source", cdSource, ""]);
        if (logFromWidth) scour.Insert(5, ["Design log length = min(channel width, sturdy log, 30 ft + W/4) (HEC-9 §3.3.2)", R(w, 2), L]);
        if (hec9 && wsUs > dbel)
        {
            double hu = wsUs!.Value - dbel!.Value, hd = Math.Max(0, wsDs!.Value - dbel.Value);
            double ahu = hu * w, ahd = hd * w;
            double fhu = gamma * 0.5 * hu * ahu / fScale, fhd = gamma * 0.5 * hd * ahd / fScale, fh = fhu - fhd;
            double elH = fh > 0 ? dbel.Value + (fhu * hu / 3 - fhd * hd / 3) / fh : double.NaN;
            double fd = qr * cdRaft * ahu / fScale;
            force.Add(["HEC-9: Ahu = (WSus − debris bottom)·W", R(ahu, 2), L + "²"]);
            force.Add(["HEC-9: Ahd = (WSds − debris bottom)·W", R(ahd, 2), L + "²"]);
            if (!double.IsNaN(blockage)) force.Add(["HEC-9: blockage ratio B = Ad/(Ad + Ac)", R(blockage, 3), ""]);
            force.Add(["HEC-9: Froude number Vr/√(g·yr)", R(frRef, 3), ""]);
            force.Add(["HEC-9: upstream hydrostatic γ·hcu·Ahu", R(fhu, 1), F]);
            force.Add(["HEC-9: downstream hydrostatic γ·hcd·Ahd", R(fhd, 1), F]);
            force.Add(["HEC-9: hydrostatic Fh (Eq. 4.4)", R(fh, 1), F]);
            force.Add(["HEC-9: drag FD = CD·γ·Ahu·Vr²/2g (Eq. 4.1)", R(fd, 1), F]);
            force.Add(["HEC-9: total debris load Fh + FD", R(fh + fd, 1), F]);
            force.Add(["HEC-9: elevation of Fh", R(elH, 2), L]);
            summary.Add($"HEC-9 debris load: hydrostatic {fh:F0} {F} + drag {fd:F0} {F} = {fh + fd:F0} {F} (CD {cdRaft:F2}); apply as a point load.");
        }
        if (!p.Has("raft thickness") && !hec9) msgs.Add(CalcMessage.Info("I-BD-002", "Raft thickness defaults to the full flow depth, the maximum extent of a pier accumulation in HEC-9 Table 3.4; set raft thickness to change it."));

        if (deck)
        {
            double wsv = ws!.Value, lcv = lc!.Value, ddv = dd!.Value, dwv = dw!.Value, spv = span!.Value;
            double hInund = Math.Clamp(wsv - lcv, 0, ddv);
            double ratio = (wsv - lcv) / ddv;
            if (hInund <= 0) summary.Add($"The water surface is {lcv - wsv:F2} {L} below the low chord: no deck forces.");
            else
            {
                var env = DeckCoefficients(deckType, ratio);
                double cdD = cdDeck ?? env!.Value.Upper.Cd, clD = clDeck ?? env!.Value.Lower.Cl, cmD = cmDeck ?? (env?.Upper.Cm ?? double.NaN);
                if (env is { } e)
                {
                    force.Add([$"FHWA-HRT-09-028 {deckType} CD upper / lower envelope", $"{e.Upper.Cd:F3} / {e.Lower.Cd:F3}", ""]);
                    force.Add([$"FHWA-HRT-09-028 {deckType} CL upper / lower envelope", $"{e.Upper.Cl:F3} / {e.Lower.Cl:F3}", ""]);
                    force.Add([$"FHWA-HRT-09-028 {deckType} CM upper / lower envelope", $"{e.Upper.Cm:F3} / {e.Lower.Cm:F3}", ""]);
                    double frD = v / Math.Sqrt(g * wsv);
                    if (frD is < 0.16 or > 0.32) msgs.Add(CalcMessage.Info("I-BD-005", $"Deck Froude number {frD:F2} is outside the 0.16 to 0.32 tested in FHWA-HRT-09-028; the envelope coefficients are an extrapolation."));
                }
                double fd = q * cdD * hInund * spv / fScale;
                double fl = q * clD * dwv * spv / fScale;
                double mDeck = q * cmD * dwv * dwv * spv / fScale;
                // The girder zone (bottom girder depth) displaces water only where air (or girder) fills it; the slab above displaces fully.
                double subGirder = Math.Min(hInund, girder), subSlab = Math.Max(0, hInund - girder);
                double buoy = gamma * dwv * spv * (subGirder * air + subSlab) / fScale;
                force.Add([$"Inundation ratio h*/s = (WS − low chord)/deck depth", R(ratio, 3), ""]);
                force.Add([$"Deck drag (CD = {R(cdD, 3):G4}, area {R(hInund, 2)} {L} × span)", R(fd, 2), F]);
                force.Add([$"Deck lift (CL = {R(clD, 3):G4}, area deck width × span)", R(fl, 2), F]);
                if (double.IsFinite(mDeck)) force.Add([$"Deck moment about the c.g. (CM = {R(cmD, 3):G4}, ½ρV²·CM·W²·span)", R(mDeck, 2), si ? "kN·m" : "kip·ft"]);
                force.Add([$"Buoyancy (slab below WS, plus {air * 100:G3} % air between girders)", R(buoy, 2), F]);
                summary.Add($"Deck inundated {hInund:F2} {L} (h*/s = {ratio:F2}): drag {fd:F0} {F}, lift {fl:F0} {F}, buoyancy {buoy:F0} {F}.");
                if (ratio > 1) msgs.Add(CalcMessage.Info("I-BD-004", "The deck is fully submerged (h* > 1): drag acts on the deck depth s."));
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
                "Deck drag acts on the inundated depth (s once h* > 1); lift on the deck plan area; buoyancy from the slab below the water surface and the entrapped air between girders.",
                "Deck coefficients unless entered: FHWA-HRT-09-028 envelopes (HDS-7 §11.1.4), drag from the upper envelope (largest), lift from the lower envelope (most negative, i.e. downward in that report's sign convention), moment from the upper envelope.",
                "HEC-9 §4.6 (Parola, NCHRP 445): Fh = γ(hcu·Ahu − hcd·Ahd), FD = CD·γ·Ahu·Vr²/2g with CD from HEC-9 Table 4.1; apply the resultant as a point load.",
            ]),
        };
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => Math.Round(v, d) + 0.0;
}
