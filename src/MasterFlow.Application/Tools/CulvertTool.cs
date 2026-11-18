using System.Globalization;
using MasterFlow.Application.Culverts;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Hydraulics.Geometry;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Culvert analysis per FHWA HDS-5 (2012): inlet and outlet control headwater, the controlling regime, roadway
/// overtopping, outlet velocity and a performance curve, checked against Rapid City IDCM 2012 §4.7.2 when asked.
/// </summary>
public sealed class CulvertTool : IDesignTool
{
    public string Id => "hds5-culvert";
    public string Name => "Culvert analysis (FHWA HDS-5)";
    public string Description => "Headwater for a culvert by FHWA HDS-5 (2012): inlet control (unsubmerged Form 1/2, transition, submerged) and outlet control (full and partly full), the controlling headwater, flow over the roadway, outlet velocity and a performance curve. Checks Rapid City IDCM 2012 §4.7.2 (10-yr with 1 ft freeboard, 100-yr depth over the road, 20 fps). Inlet ids: " + string.Join(", ", CulvertInlets.All.Select(i => i.Id)) + ", or custom.";
    public bool UsesNetwork => false;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Culvert analysis, FHWA HDS-5 (2012). SI units (m, m³/s).
        # Barrel: shape = circular (D), box (span, rise) or elliptical (span, rise); barrels = number of identical barrels.
        shape = circular
        D = 1.2
        barrels = 1
        inlet = circ-conc-square-headwall    # HDS-5 Table A.1 row; or "custom" with form, K, M, c, Y, Ks
        # Ke = 0.5                           # entrance loss for outlet control; default from the inlet (HDS-5 Table C.2)
        n = 0.012
        length = 60
        upstream invert = 100.00
        downstream invert = 99.70
        Q design = 3.4      # Rapid City §4.7.2: 10-yr storm, 0.3 m (1 ft) freeboard
        Q check = 6.2       # 100-yr storm: depth over the road limited by Table 4-9
        # Tailwater: "tailwater depth = 0.9" (above the outlet invert), "tailwater elevation = 100.6",
        # a [Tailwater rating] table (Q, elevation), or the normal depth of a trapezoidal channel:
        tailwater = channel
        tw b = 1.8
        tw z = 2
        tw n = 0.035
        tw slope = 0.005
        outlet method = hds5     # hds5: HWo = ho + H − S·L (ho = max(TW, (dc + D)/2)); backwater: standard step in the barrel
        criteria = rapid-city
        street class = local     # local (18 in.) or collector / arterial (12 in.) over the road for the 100-yr storm
        weir coefficient = 1.66  # roadway overtopping, broad-crested (HDS-5 Chart 60; 3.0 in U.S. units)
        curve max Q = 8
        [Roadway]
        station, elevation
        0, 103.4
        30, 102.6
        60, 102.3
        90, 102.6
        120, 103.4
        """ : """
        # Culvert analysis, FHWA HDS-5 (2012). U.S. units (ft, cfs).
        # Barrel: shape = circular (D), box (span, rise) or elliptical (span, rise); barrels = number of identical barrels.
        shape = circular
        D = 4
        barrels = 1
        inlet = circ-conc-square-headwall    # HDS-5 Table A.1 row; or "custom" with form, K, M, c, Y, Ks
        # Ke = 0.5                           # entrance loss for outlet control; default from the inlet (HDS-5 Table C.2)
        n = 0.012
        length = 200
        upstream invert = 100.00
        downstream invert = 99.00
        Q design = 120      # Rapid City §4.7.2: 10-yr storm, 1 ft freeboard
        Q check = 220       # 100-yr storm: depth over the road limited by Table 4-9
        # Tailwater: "tailwater depth = 3.0" (above the outlet invert), "tailwater elevation = 102.0",
        # a [Tailwater rating] table (Q, elevation), or the normal depth of a trapezoidal channel:
        tailwater = channel
        tw b = 6
        tw z = 2
        tw n = 0.035
        tw slope = 0.005
        outlet method = hds5     # hds5: HWo = ho + H − S·L (ho = max(TW, (dc + D)/2)); backwater: standard step in the barrel
        criteria = rapid-city
        street class = local     # local (18 in.) or collector / arterial (12 in.) over the road for the 100-yr storm
        weir coefficient = 3.0   # roadway overtopping, broad-crested (HDS-5 Chart 60)
        curve max Q = 300
        [Roadway]
        station, elevation
        0, 111.0
        100, 108.5
        200, 107.5
        300, 108.5
        400, 111.0
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Culvert analysis (FHWA HDS-5)";
        var u = UnitConstants.For(context.Units);
        bool si = context.Units == UnitSystem.SI;
        string L = u.Length, Qu = u.Discharge;
        double ft = si ? 0.3048 : 1.0;
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        // ---- barrel
        string shapeText = (p.Text("shape") ?? "circular").ToLowerInvariant();
        BarrelShape? shape = shapeText switch { "circular" or "pipe" or "round" => BarrelShape.Circular, "box" or "rectangular" => BarrelShape.Box, "elliptical" or "ellipse" => BarrelShape.Elliptical, _ => null };
        if (shape is null) msgs.Add(CalcMessage.Error("E-CV-001", "shape must be circular, box or elliptical."));
        double rise = shape == BarrelShape.Circular ? p.Number("D", p.Number("diameter", double.NaN)) : p.Number("rise", double.NaN);
        double span = shape == BarrelShape.Circular ? rise : p.Number("span", double.NaN);
        if (!(rise > 0) || !(span > 0)) msgs.Add(CalcMessage.Error("E-CV-002", shape == BarrelShape.Circular ? "D (inside diameter) must be greater than zero." : "span and rise must be greater than zero."));
        double barrelsD = p.Number("barrels", 1);
        if (!(barrelsD >= 1) || Math.Abs(barrelsD - Math.Round(barrelsD)) > 1e-9) msgs.Add(CalcMessage.Error("E-CV-003", "barrels must be a whole number of 1 or more."));
        double n = p.Number("n", double.NaN), length = p.Number("length", double.NaN);
        double up = p.Number("upstream invert", double.NaN), dn = p.Number("downstream invert", double.NaN);
        if (!(n > 0) || !(length > 0)) msgs.Add(CalcMessage.Error("E-CV-004", "n and length must be greater than zero."));
        if (!double.IsFinite(up) || !double.IsFinite(dn)) msgs.Add(CalcMessage.Error("E-CV-005", "upstream invert and downstream invert are required."));

        // ---- inlet
        string inletId = p.Text("inlet") ?? "";
        InletCoefficients? inlet;
        if (inletId.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            double form = p.Number("form", double.NaN);
            inlet = new InletCoefficients("custom", shape ?? BarrelShape.Circular, p.Text("inlet description") ?? "Custom coefficients", (int)form,
                p.Number("K", double.NaN), p.Number("M", double.NaN), p.Number("c", double.NaN), p.Number("Y", double.NaN), p.Number("Ks", -0.5), p.Number("Ke", 0.5), "entered by the user");
            if (form is not (1 or 2) || !(inlet.K > 0) || !(inlet.M > 0) || !(inlet.C > 0) || !(inlet.Y > 0))
                msgs.Add(CalcMessage.Error("E-CV-006", "A custom inlet needs form = 1 or 2 and K, M, c and Y greater than zero (Ks defaults to −0.5)."));
        }
        else
        {
            inlet = CulvertInlets.Find(inletId);
            if (inlet is null) msgs.Add(CalcMessage.Error("E-CV-007", $"inlet \"{inletId}\" is not known. Use one of: {string.Join(", ", CulvertInlets.All.Where(i => shape is null || i.Shape == shape).Select(i => i.Id))}, or custom."));
            else if (shape is { } sh && inlet.Shape != sh) msgs.Add(CalcMessage.Error("E-CV-008", $"inlet {inlet.Id} is for a {inlet.Shape.ToString().ToLowerInvariant()} barrel, not {sh.ToString().ToLowerInvariant()}."));
        }
        double ke = p.Number("Ke", inlet?.Ke ?? 0.5);
        if (!(ke >= 0)) msgs.Add(CalcMessage.Error("E-CV-009", "Ke must be zero or more."));

        // ---- flows
        double? qDesign = p.OptionalNumber("Q design") ?? p.OptionalNumber("Q");
        double? qCheck = p.OptionalNumber("Q check");
        if (qDesign is not > 0) msgs.Add(CalcMessage.Error("E-CV-010", "Q design must be greater than zero."));
        if (qCheck is { } qc0 && !(qc0 > 0)) msgs.Add(CalcMessage.Error("E-CV-011", "Q check must be greater than zero when given."));

        // ---- roadway
        RoadwayCrest? road = null;
        var roadRows = p.Rows("Roadway");
        if (roadRows.Count > 0)
        {
            var pts = new List<(double, double)>();
            foreach (var row in roadRows)
            {
                double? s = p.Cell(row, 0, "station", "Roadway"), z = p.Cell(row, 1, "elevation", "Roadway");
                if (s is { } ss && z is { } zz) pts.Add((ss, zz));
            }
            double cw = p.Number("weir coefficient", si ? 1.66 : 3.0);
            if (pts.Count < 2 || pts.Zip(pts.Skip(1)).Any(a => a.Second.Item1 <= a.First.Item1)) msgs.Add(CalcMessage.Error("E-CV-012", "[Roadway] needs at least two rows with stations increasing."));
            else if (!(cw > 0)) msgs.Add(CalcMessage.Error("E-CV-013", "weir coefficient must be greater than zero."));
            else road = new RoadwayCrest(pts, cw);
        }

        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new ToolResult(title, [], [], msgs);

        var culvert = new CulvertDefinition
        {
            Shape = shape!.Value, Rise = rise, Span = span, Barrels = (int)Math.Round(barrelsD), N = n, Length = length,
            UpstreamInvert = up, DownstreamInvert = dn, Inlet = inlet!, Ke = ke,
        };
        string method = (p.Text("outlet method") ?? "hds5").ToLowerInvariant();
        if (method is not ("hds5" or "backwater")) return ToolResult.Failed(title, CalcMessage.Error("E-CV-014", "outlet method must be hds5 or backwater."));
        var an = new CulvertAnalysis(culvert, context.Units) { UseBackwater = method == "backwater" };
        if (road is not null && road.LowPoint <= up + 1e-9)
            return ToolResult.Failed(title, CalcMessage.Error("E-CV-015", $"The roadway low point ({road.LowPoint:F2}) is at or below the upstream invert ({up:F2})."));
        if (culvert.Slope <= 0) msgs.Add(CalcMessage.Warning("W-CV-001", $"The barrel slope is {culvert.Slope:F4} (flat or adverse): normal depth does not exist; outlet control uses the full barrel."));
        if (culvert.Slope > 0.1) msgs.Add(CalcMessage.Warning("W-CV-002", "The barrel slope is over 10 %; the HDS-5 slope correction (Ks·S) was calibrated on flatter barrels."));

        // ---- tailwater
        TailwaterFunction? tw = null;
        string twBasis;
        if (p.Text("tailwater depth") is not null)
        {
            double d = p.Number("tailwater depth", double.NaN);
            if (!(d >= 0)) return ToolResult.Failed(title, CalcMessage.Error("E-CV-016", "tailwater depth must be zero or more."));
            tw = _ => d; twBasis = $"constant {d:F2} {L} above the outlet invert";
        }
        else if (p.Text("tailwater elevation") is not null)
        {
            double e = p.Number("tailwater elevation", double.NaN);
            if (!double.IsFinite(e)) return ToolResult.Failed(title, CalcMessage.Error("E-CV-016", "tailwater elevation must be a number."));
            tw = _ => Math.Max(0, e - dn); twBasis = $"constant elevation {e:F2}";
        }
        else if (p.Rows("Tailwater rating").Count > 0)
        {
            var rating = new List<(double Q, double E)>();
            foreach (var row in p.Rows("Tailwater rating"))
                if (p.Cell(row, 0, "Q", "Tailwater rating") is { } rq && p.Cell(row, 1, "elevation", "Tailwater rating") is { } re) rating.Add((rq, re));
            rating.Sort((a, b) => a.Q.CompareTo(b.Q));
            if (rating.Count < 2 || p.Errors.Count > 0) return ToolResult.Failed(title, [.. p.Errors, CalcMessage.Error("E-CV-017", "[Tailwater rating] needs at least two rows of Q, elevation.")]);
            tw = q => Math.Max(0, Interp(rating, q) - dn);
            twBasis = "[Tailwater rating] table, linear between rows (held at the end rows outside it)";
        }
        else if ((p.Text("tailwater") ?? "").Equals("channel", StringComparison.OrdinalIgnoreCase))
        {
            double b = p.Number("tw b", double.NaN), z = p.Number("tw z", 0), tn = p.Number("tw n", double.NaN), ts = p.Number("tw slope", double.NaN);
            if (!(b >= 0) || !(z >= 0) || !(b + z > 0) || !(tn > 0) || !(ts > 0))
                return ToolResult.Failed(title, CalcMessage.Error("E-CV-018", "A tailwater channel needs tw b ≥ 0, tw z ≥ 0 (not both zero), tw n > 0 and tw slope > 0."));
            var g = SectionGeometry.From(new TrapezoidalSection(b, z, z));
            tw = q => q <= 0 ? 0 : UniformFlowCalculator.SolveNormalDepth(g, q, tn, ts, u.ManningK) is { Converged: true } r ? r.Root : double.NaN;
            twBasis = $"normal depth of the outlet channel (b = {b:G4} {L}, z = {z:G4}, n = {tn:G4}, S = {ts:G4}), channel bed at the outlet invert";
        }
        else
        {
            tw = _ => 0; twBasis = "none (free outfall)";
            msgs.Add(CalcMessage.Info("I-CV-001", "No tailwater given: free outfall assumed (TW = 0). Outlet control then uses ho = (dc + D)/2."));
        }

        // ---- solve the named flows
        var flows = new List<(string Label, double Q)> { ("Design", qDesign!.Value) };
        if (qCheck is { } qch) flows.Add(("Check", qch));
        var results = flows.Select(f => (f.Label, Point: an.Solve(f.Q, tw, road))).ToList();
        if (results.Any(r => !double.IsFinite(r.Point.TailwaterDepth)))
            return ToolResult.Failed(title, CalcMessage.Error("E-CV-019", "The tailwater channel's normal depth did not converge."));

        var sum = new List<string>();
        foreach (var (label, pt) in results)
        {
            string over = pt.OvertoppingFlow > 0 ? $", {pt.OvertoppingFlow:F1} {Qu} over the road" : "";
            sum.Add($"{label} Q = {pt.TotalFlow:G6} {Qu}: headwater {pt.Headwater:F2} {L} (elevation {pt.HeadwaterElevation:F2}, HW/D = {pt.Headwater / rise:F2}), {pt.Control} control{over}; outlet velocity {pt.OutletVelocity:F2} {u.Velocity}.");
        }

        // ---- tables
        var data = new List<IReadOnlyList<object?>>
        {
            new object?[] { "Barrel", $"{culvert.Barrels} × {ShapeText(culvert, L)}" },
            new object?[] { "n, length, slope", $"{n:G4}, {length:G6} {L}, {culvert.Slope:F4}" },
            new object?[] { "Inverts (up / down)", $"{up:F2} / {dn:F2}" },
            new object?[] { "Full area A, D, R (per barrel)", $"{an.FullArea:F3} {u.Area}, {rise:G4} {L}, {an.FullHydraulicRadius:F3} {L}" },
            new object?[] { "Inlet", $"{inlet!.Description} ({inlet.Id})" },
            new object?[] { "Inlet coefficients", $"Form {inlet.Form}, K = {inlet.K:G4}, M = {inlet.M:G4}, c = {inlet.C:G4}, Y = {inlet.Y:G4}, Ks = {inlet.Ks:+0.0;-0.0}; Ku = {CulvertInlets.Ku(si):G4} ({inlet.Source})" },
            new object?[] { "Entrance loss Ke", ke },
            new object?[] { "Friction Kf = 2g n² L/(k² R^4/3)", an.FrictionCoefficient },
            new object?[] { "Tailwater", twBasis },
            new object?[] { "Outlet control method", method == "backwater" ? "backwater (standard step through the barrel)" : "HDS-5 equation, ho = max(TW, (dc + D)/2)" },
            new object?[] { "Roadway", road is null ? "none given (no overtopping computed)" : $"low point {road.LowPoint:F2}, Cd = {road.WeirCoefficient:G4}, {road.Profile.Count} crest points" },
        };

        var res = results.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Label, r.Point.TotalFlow, r.Point.CulvertFlow, r.Point.OvertoppingFlow, r.Point.TailwaterDepth, r.Point.Inlet.Headwater,
            method == "backwater" ? r.Point.Outlet.HeadwaterBackwater : r.Point.Outlet.Headwater, r.Point.Headwater, r.Point.HeadwaterElevation,
            r.Point.Headwater / rise, r.Point.Control, r.Point.OutletDepth, r.Point.OutletVelocity,
        }).ToList();

        // Hand-check values, one column per flow.
        var hc = new List<(string Item, Func<CulvertPoint, object?> Get)>
        {
            ($"Q per barrel ({Qu})", pt => pt.CulvertFlow / culvert.Barrels),
            ("Ku·Q/(A·D^0.5)", pt => pt.Inlet.X),
            ("Inlet regime", pt => pt.Inlet.Regime),
            ($"dc ({L})", pt => pt.Inlet.Dc),
            ($"Hc = dc + Vc²/2g ({L})", pt => pt.Inlet.Hc),
            ("Inlet equation", pt => pt.Inlet.Equation),
            ($"HWi ({L})", pt => pt.Inlet.Headwater),
            ($"TW ({L})", pt => pt.TailwaterDepth),
            ($"V full ({u.Velocity})", pt => pt.CulvertFlow / culvert.Barrels / an.FullArea),
            ($"V²/2g ({L})", pt => pt.Outlet.VelocityHead),
            ($"H = (1 + Ke + Kf)·V²/2g ({L})", pt => pt.Outlet.H),
            ($"ho ({L})", pt => pt.Outlet.Ho),
            ($"S·L ({L})", _ => up - dn),
            ($"HWo = ho + H − S·L ({L})", pt => pt.Outlet.Headwater),
            ($"HWo by backwater ({L})", pt => pt.Outlet.HeadwaterBackwater),
            ("Backwater note", pt => pt.Outlet.BackwaterNote),
            ($"yn in barrel ({L})", pt => pt.NormalDepth >= rise - 1e-9 ? "full" : pt.NormalDepth),
            ($"Outlet depth ({L})", pt => pt.OutletDepth),
            ("Outlet depth basis", pt => pt.OutletBasis),
            ($"Outlet velocity ({u.Velocity})", pt => pt.OutletVelocity),
        };
        var hcRows = hc.Select(h => (IReadOnlyList<object?>)new object?[] { h.Item }.Concat(results.Select(r => h.Get(r.Point))).ToArray()).ToList();

        // ---- criteria
        var checks = new List<IReadOnlyList<object?>>();
        bool rapid = (p.Text("criteria") ?? "").Replace(" ", "-").Equals("rapid-city", StringComparison.OrdinalIgnoreCase);
        double? maxHwD = p.OptionalNumber("max HW/D");
        double? maxHwElev = p.OptionalNumber("max HW elevation");
        foreach (var (label, pt) in results)
        {
            if (maxHwD is { } lim) checks.Add([$"{label}: HW/D", $"≤ {lim:G3}", pt.Headwater / rise, pt.Headwater / rise <= lim + 1e-9 ? "PASS" : "FAIL"]);
            if (maxHwElev is { } le) checks.Add([$"{label}: headwater elevation", $"≤ {le:F2}", pt.HeadwaterElevation, pt.HeadwaterElevation <= le + 1e-9 ? "PASS" : "FAIL"]);
        }
        if (rapid)
        {
            double fb = 1.0 * ft, vmax = 20.0 * ft;
            var d = results[0].Point;
            if (road is not null)
            {
                double freeboard = road.LowPoint - d.HeadwaterElevation;
                checks.Add(["Design (10-yr) freeboard to the road low point (§4.7.2(1))", $"≥ {fb:F2} {L}", freeboard, freeboard >= fb - 1e-9 ? "PASS" : "FAIL"]);
            }
            else msgs.Add(CalcMessage.Warning("W-CV-003", "Rapid City §4.7.2(1) requires 1 ft of freeboard for the 10-yr storm; give a [Roadway] crest to check it."));
            if (results.Count > 1)
            {
                var c = results[1].Point;
                string cls = (p.Text("street class") ?? "local").ToLowerInvariant();
                double allowed = (cls == "local" ? 1.5 : 1.0) * ft;
                double depth = road is null ? 0 : Math.Max(0, c.HeadwaterElevation - road.LowPoint);
                checks.Add([$"Check (100-yr) depth over the road low point, {cls} street (Table 4-9)", $"≤ {allowed:F2} {L}", depth, depth <= allowed + 1e-9 ? "PASS" : "FAIL"]);
            }
            foreach (var (label, pt) in results)
                checks.Add([$"{label}: outlet velocity (§4.7.2(2))", $"≤ {vmax:F1} {u.Velocity}", pt.OutletVelocity, pt.OutletVelocity <= vmax + 1e-9 ? "PASS" : "FAIL"]);
            if (inlet.Id.Contains("cmp", StringComparison.OrdinalIgnoreCase))
                checks.Add(["Material (§4.7.2(3))", "concrete", "corrugated metal", "REVIEW: CMP only under residential driveways, or by exception"]);
            msgs.Add(CalcMessage.Info("I-CV-002", "Rapid City §4.7.2(2): inlet and outlet treatments are required on all culverts (§4.9); size outlet protection from the outlet velocity above. Table 4-9 limits the depth over the gutter flow line; the depth over the roadway crest low point is used here."));
        }

        // ---- performance curve
        double qMax = p.Number("curve max Q", Math.Max(qDesign.Value, qCheck ?? 0) * 1.5);
        int steps = (int)Math.Clamp(p.Number("curve steps", 20), 4, 200);
        var curve = new List<IReadOnlyList<object?>>();
        var sIn = new List<(double, double)>(); var sOut = new List<(double, double)>(); var sHw = new List<(double, double)>();
        if (qMax > 0)
        {
            for (int i = 1; i <= steps; i++)
            {
                double q = qMax * i / steps;
                var pt = an.Solve(q, tw, road);
                if (!double.IsFinite(pt.TailwaterDepth)) continue;
                double ho = method == "backwater" ? pt.Outlet.HeadwaterBackwater : pt.Outlet.Headwater;
                curve.Add([q, pt.CulvertFlow, pt.OvertoppingFlow, pt.TailwaterDepth, pt.Inlet.Headwater, ho, pt.Headwater, pt.HeadwaterElevation, pt.Control, pt.Inlet.Regime, pt.OutletDepth, pt.OutletVelocity]);
                sIn.Add((q, up + pt.Inlet.Headwater)); sOut.Add((q, up + ho)); sHw.Add((q, pt.HeadwaterElevation));
            }
        }
        var series = new List<ToolSeries> { new("Inlet control", sIn), new("Outlet control", sOut), new("Controlling headwater", sHw, Envelope: true) };
        if (road is not null && sHw.Count > 0) series.Add(new("Roadway low point", [(0, road.LowPoint), (qMax, road.LowPoint)], Reference: true));
        if (sHw.Count > 0) series.Add(new("Top of barrel at inlet", [(0, up + rise), (qMax, up + rise)], Reference: true));

        if (method == "hds5" && results.Any(r => r.Point.Control == "outlet" && r.Point.TailwaterDepth < rise && Math.Abs(r.Point.Outlet.HeadwaterBackwater - r.Point.Outlet.Headwater) > 0.05 * rise))
            msgs.Add(CalcMessage.Info("I-CV-003", "Outlet control with a partly full outlet: the HDS-5 equation (ho = (dc + D)/2) and the backwater computation differ by more than 5 % of D. HDS-5 recommends the backwater computation when HW < D + (1 + Ke)V²/2g; set outlet method = backwater to use it."));
        if (results.Any(r => r.Point.OvertoppingFlow > 0))
            msgs.Add(CalcMessage.Info("I-CV-004", "Roadway overtopping: broad-crested weir Q = Cd·L·h^1.5 over the crest profile; where the tailwater rises above the crest the flow is reduced by the Villemonte factor (1 − (ht/h)^1.5)^0.385 in place of the HDS-5 Chart 60 kt curve."));
        msgs.Add(CalcMessage.Info("I-CV-005", $"Inlet coefficients: {inlet.Source}. Transition between KuQ/AD^0.5 = 3.5 and 4.0 is linear between the unsubmerged and submerged equations. Approach velocity is neglected."));

        var tables = new List<ToolTable>
        {
            new("Culvert data", ["Item", "Value"], data),
            new("Results", ["Flow", $"Q total ({Qu})", $"Q culvert ({Qu})", $"Q over road ({Qu})", $"TW ({L})", $"HWi ({L})", $"HWo ({L})", $"HW ({L})", "HW elevation", "HW/D", "Control", $"Outlet depth ({L})", $"Outlet V ({u.Velocity})"], res,
                ["HW is measured from the upstream invert. Q culvert is the total of all barrels; inlet and outlet control use Q per barrel."]),
            new("Hand-check values", ["Item", .. results.Select(r => $"{r.Label} ({r.Point.TotalFlow:G6} {Qu})")], hcRows),
        };
        if (checks.Count > 0) tables.Add(new ToolTable(rapid ? "Criteria (Rapid City IDCM 2012 §4.7.2)" : "Criteria", ["Check", "Required", "Computed", "Result"], checks));
        if (curve.Count > 0)
            tables.Add(new ToolTable("Performance curve", [$"Q total ({Qu})", $"Q culvert ({Qu})", $"Q over road ({Qu})", $"TW ({L})", $"HWi ({L})", $"HWo ({L})", $"HW ({L})", "HW elevation", "Control", "Inlet regime", $"Outlet depth ({L})", $"Outlet V ({u.Velocity})"], curve));
        foreach (var c in checks.Where(c => c[3] is "FAIL")) msgs.Add(CalcMessage.Warning("W-CV-004", $"{c[0]}: {DesignTools.Cell(c[2])} (required {c[1]})."));

        return new ToolResult(title, sum, tables, msgs)
        {
            Charts = curve.Count > 0 ? [new ToolChart("Performance curve", $"Total flow ({Qu})", "Headwater elevation", series)] : [],
        };
    }

    private static string ShapeText(CulvertDefinition c, string L) => c.Shape switch
    {
        BarrelShape.Circular => $"circular D = {c.Rise:G4} {L}",
        BarrelShape.Box => $"box {c.Span:G4} {L} span × {c.Rise:G4} {L} rise",
        _ => $"elliptical {c.Span:G4} {L} span × {c.Rise:G4} {L} rise",
    };

    private static double Interp(List<(double Q, double E)> t, double q)
    {
        if (q <= t[0].Q) return t[0].E;
        if (q >= t[^1].Q) return t[^1].E;
        int i = t.FindIndex(r => r.Q >= q);
        var (q0, e0) = t[i - 1]; var (q1, e1) = t[i];
        return e0 + (e1 - e0) * (q - q0) / (q1 - q0);
    }
}
