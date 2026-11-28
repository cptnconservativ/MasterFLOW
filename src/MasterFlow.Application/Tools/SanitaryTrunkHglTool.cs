using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Hydraulic grade line and surcharge of a sanitary trunk sewer at its design peak flow. The flows, sizes and inverts
/// come from the sanitary sewer network design (<see cref="SanitarySewerTool"/>, Rapid City IDCM 2012 §3.12) run on the
/// same input; the HGL and EGL are then computed upstream from the outlet with the storm network energy solver
/// (<see cref="StormNetworkSolver"/>: standard step in each pipe, junction and bend losses at each manhole from IDCM
/// Figures 4-6/4-7, or a K per manhole). The HGL is checked against the manhole rims and against the lowest
/// service or basement elevations the engineer enters, and the depth the backwater gives each pipe is compared with
/// the d/D ≤ 0.70 limit, which normal depth alone does not show for large mains running near it.
/// </summary>
public sealed class SanitaryTrunkHglTool : IDesignTool
{
    public string Id => "sanitary-trunk-hgl";
    public string Name => "Sanitary trunk sewer HGL and surcharge";
    public string Description => "HGL and EGL of a sanitary trunk or interceptor at the design peak flow (Rapid City IDCM 2012 §3.12 flows, sizes and inverts, as in Sanitary sewer network design), computed from the outlet upstream with manhole junction and bend losses (IDCM Figures 4-6/4-7, or K per manhole). Checks the backwater depth against d/D ≤ 0.70, surcharge above the crown, freeboard to the rims, and the HGL against the lowest service and basement elevations you enter.";
    public bool UsesNetwork => true;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double GpmPerCfs = 448.831;

    public string SampleInput(DesignToolContext context) => """
        # Sanitary trunk sewer HGL. U.S. units: diameters in inches, lengths and elevations in ft, flows in gpm.
        # Flows, sizes and inverts are worked out exactly as in "Sanitary sewer network design" (same keys and tables);
        # blank diameters and inverts are designed first, then the HGL is computed at the peak flow.
        criteria = Rapid City
        outlet water surface = normal  # normal (normal depth at peak in the outlet sewer), free, crown, or an elevation
        losses = IDCM                  # IDCM (Figures 4-6/4-7 cases from the pipe layout and deflection) or K
        manhole K = 0.15               # hs = K·V²/2g at every manhole when losses = K (a K column under [Manholes] overrides)
        flow factor = 1.0              # multiplies every peak flow (e.g. 1.25 for a wet-weather check)
        rim freeboard = 1.0            # ft, HGL below the manhole rim
        service freeboard = 1.0        # ft, HGL below the lowest service or basement floor drain
        infiltration = 200             # gpd per inch-diameter per mile

        [Manholes]
        Name	Rim	Deflection (deg)	K
        MH-10	3428.60	0
        MH-11	3425.40	0
        MH-12	3421.90	45
        MH-13	3420.80	0
        MH-14	3417.30	90
        MH-15	3414.70	0

        [Loads]
        Manhole	Land use	Area (ac)	DU/ac	Population	Flow (gpm)	PF
        MH-10	Residential	140	4.0
        MH-11	Residential	95	5.0
        MH-12	Multi-family	30	14.0
        MH-13	Commercial	60
        MH-13	Flow				180	1.5
        MH-14	Public	25

        [Pipes]
        Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n
        T-1	MH-10	MH-11	480	15	3415.20	3414.10	0.013
        T-2	MH-11	MH-12	495	15	3413.95	3412.85	0.013
        T-3	MH-12	MH-13	470	18	3412.60	3411.95	0.013
        T-4	MH-13	MH-14	490	18	3411.80	3411.15	0.013
        T-5	MH-14	MH-15	460	21	3410.90	3410.40	0.013

        [Services]
        Location	Station (ft)	Name	Elevation
        MH-12		1204 Elm St basement	3414.80
        T-4	250	Lot 7 lowest floor drain	3413.90
        MH-14		Lift-assisted building	3413.10
        """;

    private sealed record Service(string Location, double? Station, string Name, double Elevation);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sanitary trunk sewer HGL and surcharge";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-TH-001", "The trunk sewer HGL tool works in U.S. units (gpm, in., ft), as the Rapid City IDCM and Ten States Standards are written; switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        double factor = p.Number("flow factor", 1.0), rimFb = p.Number("rim freeboard", 1.0), svcFb = p.Number("service freeboard", 1.0);
        double maxDd = p.Number("max d/D", 0.70), kAll = p.Number("manhole K", 0.15);
        string lossText = (p.Text("losses") ?? "IDCM").Trim();
        bool kLosses = lossText.Equals("K", StringComparison.OrdinalIgnoreCase) || lossText.StartsWith("standard", StringComparison.OrdinalIgnoreCase);
        if (!kLosses && !lossText.StartsWith("IDCM", StringComparison.OrdinalIgnoreCase) && !lossText.StartsWith("Rapid", StringComparison.OrdinalIgnoreCase))
            msgs.Add(CalcMessage.Error("E-TH-002", $"losses \"{lossText}\" is not IDCM or K."));
        if (!(factor > 0)) msgs.Add(CalcMessage.Error("E-TH-002", "flow factor must be above zero."));
        if (!(rimFb >= 0) || !(svcFb >= 0) || !(kAll >= 0)) msgs.Add(CalcMessage.Error("E-TH-002", "rim freeboard, service freeboard and manhole K cannot be negative."));
        string twText = (p.Text("outlet water surface") ?? "normal").Trim();
        double? twElev = ToolInput.TryNumber(twText, out double twv) ? twv : null;
        string tw = twElev is not null ? "elevation" : twText.ToLowerInvariant();
        if (twElev is null && tw is not ("normal" or "free" or "crown"))
            msgs.Add(CalcMessage.Error("E-TH-002", $"outlet water surface \"{twText}\" is not normal, free, crown or an elevation."));

        // Per-manhole K (column found by its header) and services.
        var kAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var mh = p.Header("Manholes");
        int kCol = -1;
        for (int i = 1; i < mh.Count; i++) if (mh[i].Trim().Equals("K", StringComparison.OrdinalIgnoreCase) || mh[i].Trim().StartsWith("K ", StringComparison.OrdinalIgnoreCase)) kCol = i;
        if (kCol > 0)
            foreach (var r in p.Rows("Manholes"))
                if (r.Length > kCol && r[kCol].Length > 0 && p.Cell(r, kCol, "K", r[0]) is { } k)
                {
                    if (k < 0) msgs.Add(CalcMessage.Error("E-TH-002", $"{r[0]}: K cannot be negative."));
                    else kAt[r[0]] = k;
                }

        var services = new List<Service>();
        foreach (var r in p.Rows("Services"))
        {
            string label = r.Length > 2 && r[2].Length > 0 ? r[2] : r.Length > 0 ? r[0] : "service";
            if (r.Length < 4 || r[0].Length == 0) { msgs.Add(CalcMessage.Error("E-TH-003", $"{label}: each service needs Location (manhole or pipe), Station (pipes), Name and Elevation.")); continue; }
            double? st = p.Cell(r, 1, "station", label), el = p.Cell(r, 3, "elevation", label);
            if (el is null) { msgs.Add(CalcMessage.Error("E-TH-003", $"{label}: give the lowest service or basement elevation.")); continue; }
            if (st is < 0) { msgs.Add(CalcMessage.Error("E-TH-003", $"{label}: the station cannot be negative.")); continue; }
            services.Add(new Service(r[0], st, r[2].Length > 0 ? r[2] : r[0], el.Value));
        }
        var qOverride = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Flows"))
        {
            if (r.Length < 2 || r[0].Length == 0) { msgs.Add(CalcMessage.Error("E-TH-004", "Each row under [Flows] needs a pipe and its peak flow (gpm).")); continue; }
            if (p.Cell(r, 1, "peak flow", r[0]) is not { } q || q < 0) { msgs.Add(CalcMessage.Error("E-TH-004", $"{r[0]}: the peak flow must be zero or more.")); continue; }
            qOverride[r[0]] = q;
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        // ---- flows, sizes and inverts from the sanitary sewer design ----
        var san = new SanitarySewerTool().Run("h2s = no\n" + input, context, out var design);
        if (design is null)
            return new(title, [], [], [CalcMessage.Error("E-TH-005", "The sanitary sewer design of this input failed; see the messages below."), .. san.Messages.Where(m => m.Severity == MessageSeverity.Error)]);
        foreach (var name in qOverride.Keys.Where(k => !design.Sewers.Any(s => s.Name.Equals(k, StringComparison.OrdinalIgnoreCase))))
            msgs.Add(CalcMessage.Error("E-TH-004", $"[Flows]: no pipe named {name}."));
        foreach (var s in services.Where(s => !design.Manholes.Any(m => m.Name.Equals(s.Location, StringComparison.OrdinalIgnoreCase)) && !design.Sewers.Any(x => x.Name.Equals(s.Location, StringComparison.OrdinalIgnoreCase))))
            msgs.Add(CalcMessage.Error("E-TH-003", $"Service \"{s.Name}\": {s.Location} is not a manhole or pipe."));
        foreach (var s in services.Where(s => design.Sewers.FirstOrDefault(x => x.Name.Equals(s.Location, StringComparison.OrdinalIgnoreCase)) is { } x && (s.Station is null || s.Station > x.Length + 1e-9)))
            msgs.Add(CalcMessage.Error("E-TH-003", $"Service \"{s.Name}\" on pipe {s.Location}: give a station from 0 to the pipe length."));
        foreach (var k in kAt.Keys.Where(k => !design.Manholes.Any(m => m.Name.Equals(k, StringComparison.OrdinalIgnoreCase))))
            msgs.Add(CalcMessage.Error("E-TH-002", $"K given for {k}, which is not a manhole of the network."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        int sanWarn = san.Messages.Count(m => m.Severity == MessageSeverity.Warning);

        double Qgpm(SanitarySewerTool.DesignedSewer x) => (qOverride.TryGetValue(x.Name, out var q) ? q : x.QpeakGpm) * factor;
        var sewers = design.Sewers;
        var byName = sewers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var used = design.Manholes.Where(m => sewers.Any(x => x.From.Equals(m.Name, StringComparison.OrdinalIgnoreCase) || x.To.Equals(m.Name, StringComparison.OrdinalIgnoreCase))).ToList();
        var outlets = sewers.Where(x => !sewers.Any(y => y.From.Equals(x.To, StringComparison.OrdinalIgnoreCase))).ToList();

        // ---- the network for the energy solver ----
        var rule = tw switch { "free" => TailwaterRule.FreeOutfall, "crown" => TailwaterRule.Crown, _ => TailwaterRule.Elevation };
        double? twe = twElev;
        if (tw == "normal")
        {
            if (outlets.Count > 1)
            {
                rule = TailwaterRule.FreeOutfall;
                msgs.Add(CalcMessage.Warning("W-TH-007", $"The network has {outlets.Count} outlets; normal depth needs one, so each outlet starts at critical depth (free outfall). Run each outlet's tree separately for normal depth."));
            }
            else
            {
                var o = outlets[0];
                double dFt = o.DiameterIn / 12, y = SanitarySewerTool.NormalDepth(Qgpm(o) / GpmPerCfs, dFt, o.N, o.Slope);
                twe = o.DownInvert + (double.IsFinite(y) ? y : dFt);
            }
        }
        StructureLossMethod Method(SanitarySewerTool.DesignedManhole m, int inflows)
        {
            if (kLosses || kAt.ContainsKey(m.Name)) return StructureLossMethod.StandardK;
            if (inflows != 1 || m.Deflection is not { } d || d < 11.25) return StructureLossMethod.Auto;
            return d <= 22.5 + 1e-9 ? StructureLossMethod.Bend22 : d <= 45 + 1e-9 ? StructureLossMethod.Bend45 : d <= 60 + 1e-9 ? StructureLossMethod.Bend60 : StructureLossMethod.Bend90;
        }
        var net = new StormNetwork
        {
            Name = "Sanitary trunk",
            Units = UnitSystem.USCustomary,
            Tailwater = rule,
            TailwaterElevation = twe,
            DefaultJunctionK = kAll,
            Criteria = new NetworkCriteria { AllowSurcharge = true, Source = "Sanitary trunk HGL" },
            Structures = used.Select(m => new NetworkStructure
            {
                Name = m.Name, Kind = "Manhole", RimElevation = m.Rim, Northing = m.Northing, Easting = m.Easting,
                LossMethod = Method(m, sewers.Count(x => x.To.Equals(m.Name, StringComparison.OrdinalIgnoreCase))),
                JunctionK = kAt.TryGetValue(m.Name, out var k) ? k : null,
            }).ToList(),
            Pipes = sewers.Select(x => new NetworkPipe
            {
                Name = x.Name, From = Canon(x.From), To = Canon(x.To), Rise = x.DiameterIn / 12, Span = x.DiameterIn / 12, Length = x.Run,
                UpstreamInvert = x.UpInvert, DownstreamInvert = x.DownInvert, ManningN = x.N, Discharge = Qgpm(x) / GpmPerCfs,
            }).ToList(),
        };
        string Canon(string name) => used.First(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Name;
        var res = StormNetworkSolver.Solve(net);
        foreach (var m in res.Messages.Where(m => m.Code != "I-NW-003")) msgs.Add(m);
        if (!res.Succeeded) return new(title, [], [], msgs);

        // ---- pipes ----
        var pipeRows = new List<IReadOnlyList<object?>>();
        var surcharged = new List<string>();
        var overDd = new List<string>();
        foreach (var x in sewers)
        {
            var r = res.Pipe(x.Name)!;
            double dFt = x.DiameterIn / 12, q = Qgpm(x);
            double yn = SanitarySewerTool.NormalDepth(q / GpmPerCfs, dFt, x.N, x.Slope);
            double ddN = double.IsFinite(yn) ? yn / dFt : double.NaN;
            double ddMax = r.Profile.Count > 0 ? r.Profile.Max(pt => (pt.Hgl - pt.Invert) / dFt) : Math.Max(r.DepthUp, r.DepthDown) / dFt;
            double surch = r.Profile.Count > 0 ? r.Profile.Max(pt => pt.Hgl - pt.Crown) : Math.Max(r.HglUp - x.UpInvert, r.HglDown - x.DownInvert) - dFt;
            var issues = new List<string>();
            if (surch > 0.005)
            {
                issues.Add("surcharged"); surcharged.Add(x.Name);
                msgs.Add(CalcMessage.Warning("W-TH-002", F($"{x.Name}: the HGL is up to {surch:F2} ft above the crown at {q:F0} gpm; the sewer runs under pressure.")));
            }
            else if (ddMax > maxDd + 0.005)
            {
                issues.Add(ddN > maxDd + 0.005 ? "d/D" : "d/D backwater"); overDd.Add(x.Name);
                msgs.Add(CalcMessage.Warning("W-TH-001", F($"{x.Name}: the depth reaches d/D = {ddMax:F2} with the backwater, above the {maxDd:G2} limit at peak flow (IDCM 3.12.4)") +
                    (ddN <= maxDd + 0.005 ? F($", although normal depth gives {ddN:F2}.") : F($"; normal depth gives {ddN:F2}."))));
            }
            pipeRows.Add([x.Name, x.From, x.To, x.DiameterIn, R(x.Slope * 100, 3), R(q, 1), R(q / GpmPerCfs, 3), R(ddN, 3), R(r.DepthUp / dFt, 3), R(r.DepthDown / dFt, 3), R(ddMax, 3),
                R(r.HglUp, 2), R(r.HglDown, 2), R(r.EglUp, 2), R(r.EglDown, 2), R(r.VelocityUp, 2), r.Condition, issues.Count == 0 ? "OK" : string.Join(", ", issues)]);
        }

        // ---- manholes ----
        var mhRows = new List<IReadOnlyList<object?>>();
        foreach (var name in NetworkSizer.UpstreamFirst(net.Pipes).Select(n => net.Pipes.First(pp => pp.Name == n).From).Concat(outlets.Select(o => Canon(o.To))).Distinct())
        {
            var s = res.Structure(name);
            if (s is null) continue;
            var issues = new List<string>();
            double? fb = s.Structure.RimElevation is { } rim ? rim - s.Hgl : null;
            if (fb is { } f0 && f0 < -0.005)
            {
                issues.Add("overflows");
                msgs.Add(CalcMessage.Warning("W-TH-004", F($"{name}: the HGL {s.Hgl:F2} is {-f0:F2} ft above the rim; sewage would overflow the manhole.")));
            }
            else if (fb is { } f1 && f1 < rimFb - 0.005)
            {
                issues.Add("rim freeboard");
                msgs.Add(CalcMessage.Warning("W-TH-003", F($"{name}: the HGL {s.Hgl:F2} is {f1:F2} ft below the rim, less than the {rimFb:G3} ft freeboard.")));
            }
            mhRows.Add([name, s.Structure.RimElevation, R(s.Hgl, 2), R(s.Egl, 2), s.IsOutfall ? "Outlet" : s.LossMethod, s.IsOutfall ? null : R(s.Loss, 3),
                double.IsFinite(s.InletVelocity) ? R(s.InletVelocity, 2) : null, R(s.OutletVelocity, 2), fb is { } fv ? R(fv, 2) : null, s.Structure.RimElevation is null ? "no rim" : issues.Count == 0 ? "OK" : string.Join(", ", issues)]);
        }

        // ---- services ----
        var svcRows = new List<IReadOnlyList<object?>>();
        int svcProblems = 0;
        foreach (var sv in services)
        {
            double hgl;
            string at;
            if (byName.TryGetValue(sv.Location, out var x))
            {
                var r = res.Pipe(x.Name)!;
                hgl = HglAt(r, Math.Min(sv.Station!.Value, x.Run), x.Run);
                at = F($"{x.Name} sta. {sv.Station:F0} ft");
            }
            else
            {
                hgl = res.Structure(Canon(sv.Location))!.Hgl;
                at = Canon(sv.Location);
            }
            double margin = sv.Elevation - hgl;
            string status = "OK";
            if (margin < -0.005)
            {
                status = "backs up"; svcProblems++;
                msgs.Add(CalcMessage.Warning("W-TH-006", F($"{sv.Name}: the HGL {hgl:F2} at {at} is {-margin:F2} ft above its elevation {sv.Elevation:F2}; sewage would back up into it at the peak flow. Raise the service, add a backwater valve, or relieve the trunk.")));
            }
            else if (margin < svcFb - 0.005)
            {
                status = "freeboard"; svcProblems++;
                msgs.Add(CalcMessage.Warning("W-TH-005", F($"{sv.Name}: the HGL {hgl:F2} at {at} is only {margin:F2} ft below its elevation {sv.Elevation:F2} (freeboard {svcFb:G3} ft).")));
            }
            svcRows.Add([sv.Name, at, R(sv.Elevation, 2), R(hgl, 2), R(margin, 2), status]);
        }

        // ---- summary ----
        var summary = new List<string>
        {
            F($"HGL and EGL at the design peak flow{(Math.Abs(factor - 1) > 1e-9 ? $" × {factor:G3}" : "")}, computed from the outlet upstream (standard step in each sewer, manhole losses {(kLosses ? $"hs = K·V²/2g, K = {kAll:G3}" : "by IDCM Figures 4-6/4-7")})."),
            "Outlet water surface: " + (rule switch
            {
                TailwaterRule.FreeOutfall => "critical depth in the outlet sewer (free outfall).",
                TailwaterRule.Crown => "outlet sewer crown.",
                _ => tw == "normal" ? F($"normal depth of the peak flow in {outlets[0].Name}, {twe:F2} (not below critical).") : F($"{twe:F2} as entered (not below critical depth)."),
            }),
        };
        foreach (var o in outlets) summary.Add(F($"At the outlet {o.To} ({o.Name}): peak {Qgpm(o):F0} gpm ({Qgpm(o) / GpmPerCfs:F3} cfs), {o.DiameterIn:G3} in. at {o.Slope * 100:F2} %."));
        summary.Add(surcharged.Count == 0 && overDd.Count == 0 ? F($"No sewer surcharges and every sewer stays within d/D ≤ {maxDd:G2} with the backwater.")
            : (surcharged.Count > 0 ? $"Surcharged: {string.Join(", ", surcharged)}. " : "") + (overDd.Count > 0 ? F($"Above d/D {maxDd:G2} at peak flow (with the backwater): {string.Join(", ", overDd)}.") : ""));
        if (services.Count > 0) summary.Add(svcProblems == 0 ? F($"All {services.Count} service and basement elevations are at least {svcFb:G3} ft above the HGL.") : $"{svcProblems} of {services.Count} services are within the freeboard or below the HGL; see Services.");
        if (sanWarn > 0) summary.Add($"The sanitary sewer design check of the same input has {sanWarn} warning(s); run Sanitary sewer network design for them.");

        msgs.Add(CalcMessage.Info("I-TH-001", "The IDCM gives no HGL criterion for sanitary sewers: it limits d/D to 0.70 at peak flow (3.12.4). The rim and service freeboards are this tool's inputs (D-722)."));
        if (!kLosses) msgs.Add(CalcMessage.Info("I-TH-002", "Manhole losses use the storm-sewer cases of IDCM Figures 4-6/4-7 (straight-through c = 0.5, junctions c = 0.25, upstream end 1.25·V²/2g, bends by deflection). Shaped sanitary inverts usually lose less; set losses = K for a channelled-manhole K (D-720)."));

        var tables = new List<ToolTable>
        {
            new("Sewer HGL at peak flow",
                ["Pipe", "From", "To", "D (in)", "Slope (%)", "Q peak (gpm)", "Q (cfs)", "d/D normal", "d/D up", "d/D down", "d/D max", "HGL up", "HGL down", "EGL up", "EGL down", "V up (ft/s)", "Flow", "Status"], pipeRows,
                ["d/D up, down and max are the HGL depth over the diameter along the sewer (values above 1 are pressure heads); d/D normal is the uniform-flow depth the sanitary design checks.",
                 F($"Limits: no surcharge above the crown; d/D ≤ {maxDd:G2} at peak flow including backwater (IDCM 3.12.4).")]),
            new("Manholes",
                ["Manhole", "Rim", "HGL", "EGL", "Loss method", "hj (ft)", "V in (ft/s)", "V out (ft/s)", "Rim − HGL (ft)", "Status"], mhRows,
                [F($"hj = rise of the HGL across the manhole. Freeboard required: {rimFb:G3} ft below the rim.")]),
        };
        if (svcRows.Count > 0)
            tables.Add(new("Services and basements", ["Service", "At", "Elevation", "HGL", "Margin (ft)", "Status"], svcRows,
                [F($"Elevation = lowest service invert, floor drain or basement floor served by gravity. Margin = elevation − HGL; at least {svcFb:G3} ft required. On a pipe, the HGL is interpolated along the computed profile.")]));

        // ---- profile ----
        var figures = new List<ToolFigure>();
        var path = LongestPath(sewers);
        if (path.Count > 0)
        {
            double x0 = 0;
            var ground = new List<(double, double)>(); var inv = new List<(double, double)>(); var crown = new List<(double, double)>();
            var hgl = new List<(double, double)>(); var egl = new List<(double, double)>();
            foreach (var x in path)
            {
                var r = res.Pipe(x.Name)!;
                if (byMh(x.From)?.Rim is { } r1) ground.Add((x0, r1));
                foreach (var pt in r.Profile)
                {
                    double xs = x0 + pt.Station;
                    inv.Add((xs, pt.Invert)); crown.Add((xs, pt.Crown)); hgl.Add((xs, pt.Hgl)); egl.Add((xs, pt.Egl));
                }
                x0 += x.Length;
                if (byMh(x.To)?.Rim is { } r2) ground.Add((x0, r2));
            }
            var series = new List<ToolFigureSeries>();
            if (ground.Count >= 2) series.Add(new("Ground (rim)", ToolSeriesKind.Ground, ground));
            series.Add(new("HGL at peak flow", ToolSeriesKind.Water, inv.Concat(Enumerable.Reverse(hgl)).ToList()));
            series.Add(new("Invert", ToolSeriesKind.Limit, inv));
            series.Add(new("Crown", ToolSeriesKind.Limit, crown));
            series.Add(new("EGL", ToolSeriesKind.Limit, egl));
            figures.Add(new ToolFigure($"HGL profile {path[0].From} to {path[^1].To}", series, "Distance (ft)", "Elevation (ft)",
                [$"Longest flow path: {string.Join(", ", path.Select(x => x.Name))}. Water fill = HGL at the peak flow."]));
        }
        SanitarySewerTool.DesignedManhole? byMh(string n) => design.Manholes.FirstOrDefault(m => m.Name.Equals(n, StringComparison.OrdinalIgnoreCase));

        return new ToolResult(title, summary, tables, msgs) { Figures = figures };
    }

    /// <summary>HGL at <paramref name="station"/> ft from the upstream end, interpolated along the computed profile.</summary>
    public static double HglAt(PipeHydraulicResult r, double station, double length)
    {
        var pts = r.Profile.OrderBy(pt => pt.Station).ToList();
        if (pts.Count == 0) return r.HglUp + (r.HglDown - r.HglUp) * station / length;
        if (station <= pts[0].Station) return pts[0].Hgl;
        for (int i = 1; i < pts.Count; i++)
            if (station <= pts[i].Station)
            {
                double t = (station - pts[i - 1].Station) / Math.Max(1e-9, pts[i].Station - pts[i - 1].Station);
                return pts[i - 1].Hgl + t * (pts[i].Hgl - pts[i - 1].Hgl);
            }
        return pts[^1].Hgl;
    }

    private static List<SanitarySewerTool.DesignedSewer> LongestPath(IReadOnlyList<SanitarySewerTool.DesignedSewer> pipes)
    {
        List<SanitarySewerTool.DesignedSewer> best = [];
        double bestLen = -1;
        foreach (var head in pipes.Where(x => !pipes.Any(y => y.To.Equals(x.From, StringComparison.OrdinalIgnoreCase))))
        {
            var path = new List<SanitarySewerTool.DesignedSewer> { head };
            var at = head;
            while (pipes.FirstOrDefault(y => y.From.Equals(at.To, StringComparison.OrdinalIgnoreCase)) is { } nx && path.Count <= pipes.Count) { path.Add(nx); at = nx; }
            double len = path.Sum(x => x.Length);
            if (len > bestLen) { bestLen = len; best = path; }
        }
        return best;
    }

    private static string F(FormattableString s) => s.ToString(Inv);
    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
