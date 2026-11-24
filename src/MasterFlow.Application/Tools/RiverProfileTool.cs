using System.Globalization;
using MasterFlow.Application.River;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Steady gradually varied flow through surveyed natural cross-sections, by the HEC-RAS method (Hydraulic Reference
/// Manual Ch. 2): energy equation between sections with discharge-weighted reach lengths, average-conveyance friction
/// slope, contraction/expansion losses on the change in velocity head, conveyance subdivision with α, ineffective flow
/// areas, critical depth by minimum specific energy, and sub-, super- or mixed-regime runs for several flow profiles.
/// </summary>
public sealed class RiverProfileTool : IDesignTool
{
    public string Id => "river-profile";
    public string Name => "Natural channel water surface profiles (HEC-RAS method, steady flow)";
    public string Description => "Water surface profiles through surveyed irregular cross-sections for several flows: energy equation with discharge-weighted LOB/channel/ROB reach lengths, average-conveyance friction slope, contraction and expansion losses, conveyance subdivision by n with α, ineffective flow areas, critical depth by minimum specific energy, subcritical, supercritical or mixed regime (HEC-RAS Hydraulic Reference Manual Ch. 2). Profile, cross-section plots and the HEC-RAS standard table.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Natural channel water surface profiles (HEC-RAS method). U.S. units (ft, cfs) or SI (m, m³/s) as the project.
        # River stations (RS) increase upstream. Reach lengths at a section are to the next section DOWNSTREAM.
        regime = subcritical          # subcritical, supercritical or mixed
        downstream = normal           # normal (give downstream slope), critical, or known (downstream ws = one per profile)
        downstream slope = 0.004
        # upstream = critical         # supercritical or mixed: critical, or known (upstream ws = ...)
        bridge low flow = highest     # energy, yarnell, or highest (the higher upstream W.S. of the two)

        [Flows]
        # Flow change locations: each applies at its RS and downstream until the next change.
        RS	10-yr	100-yr	500-yr
        2000	300	1200	2600

        [Sections]
        RS	Left bank	Right bank	LOB length	Ch length	ROB length	n LOB	n Ch	n ROB	Ineff left sta	Ineff left el	Ineff right sta	Ineff right el
        2000	160	210	520	500	480	0.045	0.035	0.050
        1500	160	210	520	500	480	0.045	0.035	0.050			270	3412.0
        1000	120	170	220	210	200	0.045	0.035	0.050
        790	160	210	50	50	50	0.045	0.035	0.050	150	3413.5	222	3413.5
        740	160	210	250	240	230	0.045	0.035	0.050	150	3413.5	222	3413.5
        500	160	210	520	500	480	0.045	0.035	0.050
        0	160	210	0	0	0	0.045	0.035	0.050

        # A bridge sits between its Upstream RS (HEC-RAS section 3, at the upstream face) and the next section downstream
        # (section 2, at the downstream face). High chord = top of deck / road; the road beyond the abutments is the weir.
        [Bridges]
        Name	Upstream RS	Low chord	High chord	Deck width	Left abutment	Right abutment	Pier shape	Weir C
        Main St bridge	790	3410.0	3413.5	40	150	222	semicircular	2.6

        [Bridge piers]
        Bridge	Station	Width
        Main St bridge	185	3

        [Points]
        # Station and elevation, left to right looking downstream (e.g. a Civil 3D sample-line section report). An n column, where
        # given, starts that n at the point and carries it to the right (overrides the subsection n).
        RS	Station	Elevation
        2000	0	3417.00
        2000	40	3414.00
        2000	120	3412.50
        2000	160	3412.00
        2000	170	3409.50
        2000	178	3408.00
        2000	192	3408.00
        2000	200	3409.50
        2000	210	3412.20
        2000	270	3413.00
        2000	320	3416.00
        1500	0	3415.00
        1500	40	3412.00
        1500	120	3410.50
        1500	160	3410.00
        1500	170	3407.50
        1500	178	3406.00
        1500	192	3406.00
        1500	200	3407.50
        1500	210	3410.20
        1500	270	3411.00
        1500	320	3414.00
        1000	0	3413.00
        1000	40	3410.00
        1000	80	3408.50
        1000	120	3408.00
        1000	130	3405.50
        1000	138	3404.00
        1000	152	3404.00
        1000	160	3405.50
        1000	170	3408.20
        1000	230	3409.00
        1000	280	3412.00
        790	0	3412.16
        790	40	3409.16
        790	120	3407.66
        790	160	3407.16
        790	170	3404.66
        790	178	3403.16
        790	192	3403.16
        790	200	3404.66
        790	210	3407.36
        790	270	3408.16
        790	320	3411.16
        740	0	3411.96
        740	40	3408.96
        740	120	3407.46
        740	160	3406.96
        740	170	3404.46
        740	178	3402.96
        740	192	3402.96
        740	200	3404.46
        740	210	3407.16
        740	270	3407.96
        740	320	3410.96
        500	0	3411.00
        500	40	3408.00
        500	120	3406.50
        500	160	3406.00
        500	170	3403.50
        500	178	3402.00
        500	192	3402.00
        500	200	3403.50
        500	210	3406.20
        500	270	3407.00
        500	320	3410.00
        0	0	3409.00
        0	40	3406.00
        0	120	3404.50
        0	160	3404.00
        0	170	3401.50
        0	178	3400.00
        0	192	3400.00
        0	200	3401.50
        0	210	3404.20
        0	270	3405.00
        0	320	3408.00
        """;

    /// <summary>The solution at one section for one profile.</summary>
    public sealed record Result(RiverSection Section, double Q, double Ws, double CritWs, double Eg, double Sf, SectionHydraulics H, string Regime, string Note)
    {
        public double QLob => H.Conveyance > 0 ? Q * H.KLob / H.Conveyance : 0;
        public double QCh => H.Conveyance > 0 ? Q * H.KCh / H.Conveyance : 0;
        public double QRob => H.Conveyance > 0 ? Q * H.KRob / H.Conveyance : 0;
        public double VCh => H.AreaCh > 0 ? QCh / H.AreaCh : 0;
        public double FroudeCh(double g) => H.AreaCh > 0 && H.TopCh > 0 ? VCh / Math.Sqrt(g * H.AreaCh / H.TopCh) : double.NaN;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Natural channel water surface profiles (HEC-RAS method)";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.80665 : 32.174, k = si ? 1.0 : 1.486;
        string L = si ? "m" : "ft", V = si ? "m/s" : "ft/s", Qu = si ? "m³/s" : "cfs";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        if (p.Text("hec-ras geometry") is { } geomPath)
        {
            if (p.HasSection("Sections") || p.HasSection("Points"))
                msgs.Add(CalcMessage.Error("E-RAS-001", "Use either hec-ras geometry = ... or [Sections] and [Points], not both."));
            else
            {
                string? text = null;
                try { text = File.ReadAllText(geomPath.Trim().Trim('"')); }
                catch (Exception ex) { msgs.Add(CalcMessage.Error("E-RAS-002", $"Cannot read the HEC-RAS geometry file \"{geomPath}\": {ex.Message}")); }
                if (text is not null)
                {
                    var (rasTables, notes) = ImportHecRas(text, p.Text("hec-ras reach"));
                    msgs.AddRange(notes);
                    if (rasTables is not null) p = ToolInput.Parse(input + "\n" + rasTables);
                }
            }
        }
        var sections = ParseSections(p, k, msgs);
        var bridges = ParseBridges(p, sections, msgs, si);
        string lowFlow = (p.Text("bridge low flow") ?? "energy").ToLowerInvariant();
        if (lowFlow is not ("energy" or "yarnell" or "highest")) msgs.Add(CalcMessage.Error("E-BR-006", "bridge low flow must be energy, yarnell or highest."));
        // Flow table.
        var fHead = p.Header("Flows");
        var profiles = fHead.Skip(1).Select(h => h.Length > 0 ? h : "Profile").ToList();
        var flowRows = new List<(double Rs, double[] Q)>();
        foreach (var r in p.Rows("Flows"))
        {
            if (p.Cell(r, 0, "RS", "Flows") is not { } rs) continue;
            var qs = profiles.Select((_, j) => p.Cell(r, j + 1, profiles[j], $"Flows RS {rs}") ?? double.NaN).ToArray();
            flowRows.Add((rs, qs));
        }
        if (p.OptionalNumber("Q") is { } q1 && profiles.Count == 0) { profiles.Add("Q"); flowRows.Add((double.PositiveInfinity, [q1])); }
        if (profiles.Count == 0) msgs.Add(CalcMessage.Error("E-RP-001", "Give the flows under [Flows] (RS, then one column per profile) or Q = ...."));
        if (flowRows.Any(f => f.Q.Any(x => !(x > 0)))) msgs.Add(CalcMessage.Error("E-RP-002", "Every flow under [Flows] must be above zero."));
        if (sections.Count < 2 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-RP-003", "Give at least two cross-sections under [Sections] and [Points]."));
        string regime = (p.Text("regime") ?? "subcritical").ToLowerInvariant();
        if (regime is not ("subcritical" or "supercritical" or "mixed")) msgs.Add(CalcMessage.Error("E-RP-004", "regime must be subcritical, supercritical or mixed."));
        string down = (p.Text("downstream") ?? "normal").ToLowerInvariant(), up = (p.Text("upstream") ?? "critical").ToLowerInvariant();
        double dSlope = p.Number("downstream slope", double.NaN);
        var dWs = Numbers(p.Text("downstream ws")); var uWs = Numbers(p.Text("upstream ws"));
        if (regime != "supercritical")
        {
            if (down == "normal" && !(dSlope > 0)) msgs.Add(CalcMessage.Error("E-RP-005", "downstream = normal needs downstream slope > 0 (the energy slope, ft/ft)."));
            if (down == "known" && dWs.Count != profiles.Count) msgs.Add(CalcMessage.Error("E-RP-006", $"downstream = known needs downstream ws = one elevation per profile ({profiles.Count})."));
            if (down is not ("normal" or "critical" or "known")) msgs.Add(CalcMessage.Error("E-RP-007", "downstream must be normal, critical or known."));
        }
        if (regime != "subcritical")
        {
            if (up == "known" && uWs.Count != profiles.Count) msgs.Add(CalcMessage.Error("E-RP-006", $"upstream = known needs upstream ws = one elevation per profile ({profiles.Count})."));
            if (up is not ("critical" or "known")) msgs.Add(CalcMessage.Error("E-RP-007", "upstream must be critical or known."));
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        sections.Sort((a, b) => a.RiverStation.CompareTo(b.RiverStation));   // downstream first
        if (!flowRows.Any(f => f.Rs >= sections[^1].RiverStation - 1e-9))
            return ToolResult.Failed(title, CalcMessage.Error("E-RP-008", $"The most upstream section (RS {sections[^1].Name}) needs a flow: list its RS (or one above it) under [Flows]."));
        double[] FlowAt(RiverSection s) =>
            flowRows.Where(f => f.Rs >= s.RiverStation - 1e-9).OrderBy(f => f.Rs).First().Q;

        var all = new List<(string Profile, List<Result> Res)>();
        var bridgeResults = new List<BridgeResult>();
        for (int pi = 0; pi < profiles.Count; pi++)
        {
            var qs = sections.Select(s => FlowAt(s)[pi]).ToArray();
            List<Result>? sub = null, sup = null;
            if (regime != "supercritical")
            {
                double? bc = down switch { "normal" => sections[0].Normal(qs[0], dSlope), "known" => dWs[pi], _ => null };
                sub = Subcritical(sections, qs, bc, g, msgs, profiles[pi], bridges, lowFlow, bridgeResults);
            }
            if (regime != "subcritical")
            {
                double? bc = up == "known" ? uWs[pi] : null;
                sup = Supercritical(sections, qs, bc, g, msgs, profiles[pi]);
            }
            List<Result> res;
            if (regime == "mixed")
            {
                res = [];
                for (int i = 0; i < sections.Count; i++)
                {
                    var a = sub![i]; var b = sup![i];
                    bool superValid = b.Ws < b.CritWs - 1e-6;
                    double ma = sections[i].SpecificForce(a.Ws, qs[i], g), mb = sections[i].SpecificForce(b.Ws, qs[i], g);
                    res.Add(superValid && mb > ma ? b with { Note = (b.Note + " supercritical governs (greater specific force)").Trim() } : a);
                }
            }
            else res = sub ?? sup!;
            all.Add((profiles[pi], res));
            Checks(res, profiles[pi], msgs, L);
        }

        // Output tables.
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var s in sections.AsEnumerable().Reverse())
            foreach (var (prof, res) in all)
            {
                var r = res.Single(x => x.Section == s);
                rows.Add([s.Name, prof, R(r.Q, 1), R(s.MinChannelElevation, 2), R(r.Ws, 2), R(r.CritWs, 2), R(r.Eg, 2), r.Sf, R(r.VCh, 2), R(r.H.Area, 1), R(r.H.TopWidth, 1), R(r.FroudeCh(g), 2),
                    R(r.QLob, 1), R(r.QCh, 1), R(r.QRob, 1), R(r.H.Alpha, 3), r.Regime, r.Note]);
            }
        var bankRows = new List<IReadOnlyList<object?>>();
        foreach (var s in sections.AsEnumerable().Reverse())
            foreach (var (prof, res) in all)
            {
                var r = res.Single(x => x.Section == s);
                bankRows.Add([s.Name, prof, R(r.Ws, 2), R(s.LeftBankElevation, 2), R(s.LeftBankElevation - r.Ws, 2), R(s.RightBankElevation, 2), R(s.RightBankElevation - r.Ws, 2),
                    r.Ws > Math.Min(s.LeftBankElevation, s.RightBankElevation) ? "out of bank" : "in bank", r.H.WallLeft || r.H.WallRight ? "WS above an end point" : ""]);
            }
        if (bridges.Count > 0 && regime != "subcritical")
            msgs.Add(CalcMessage.Warning("W-BR-002", "Bridges are computed in the subcritical pass only; in a supercritical run they are ignored, and in a mixed run the subcritical bridge solution is used upstream of each bridge."));
        var tables = new List<ToolTable>
        {
            new("Profile output (HEC-RAS standard table)", ["River Sta", "Profile", $"Q total ({Qu})", $"Min Ch El ({L})", $"W.S. Elev ({L})", $"Crit W.S. ({L})", $"E.G. Elev ({L})", "E.G. Slope", $"Vel Chnl ({V})", $"Flow Area ({L}²)", $"Top Width ({L})", "Froude # Chl", $"Q Left ({Qu})", $"Q Channel ({Qu})", $"Q Right ({Qu})", "Alpha", "Regime", "Notes"], rows,
                ["Energy: WS2 + α2V2²/2g = WS1 + α1V1²/2g + L·Sf + C·|α2V2²/2g − α1V1²/2g|; L = discharge-weighted LOB, channel and ROB lengths; Sf = ((Q1 + Q2)/(K1 + K2))² (average conveyance).",
                 "C = contraction coefficient where the velocity head increases downstream, expansion where it decreases. Conveyance by n segment, summed per subsection; α = Σ(Ki³/Ai²)/(Kt³/At²).",
                 "Crit W.S. = minimum specific energy. A section that cannot balance the energy equation on the regime's branch is set to critical depth and noted."]),
            new("Bank and end-point checks", ["River Sta", "Profile", $"W.S. Elev ({L})", $"Left bank ({L})", $"Left freeboard ({L})", $"Right bank ({L})", $"Right freeboard ({L})", "Flow", "Ends"], bankRows,
                ["Freeboard = bank elevation − W.S.; negative means the bank is overtopped. Water above an end point is held by a vertical wall (HEC-RAS); extend the section."]),
        };

        if (bridgeResults.Count > 0)
        {
            var brows = bridgeResults.Select(b => (IReadOnlyList<object?>)[b.Bridge.Name, b.Profile, b.FlowType, R(b.Upstream.Ws, 2), R(b.Upstream.Eg, 2), R(b.Downstream.Ws, 2), R(b.Downstream.Eg, 2),
                R(b.Upstream.Eg - b.Downstream.Eg, 2), R(b.QOpening, 1), R(b.QWeir, 1), R(b.OpeningVelocity, 2), b.EnergyWs is { } e ? R(e, 2) : null, b.YarnellWs is { } y ? R(y, 2) : null,
                R(b.Bridge.LowChord, 2), R(b.Bridge.LowChord - b.Upstream.Ws, 2), R(b.Bridge.HighChord, 2), b.QWeir > 0 ? R(b.Upstream.Eg - BridgeHydraulics.MinCrest(b.Bridge, sections.First(x => Math.Abs(x.RiverStation - b.Bridge.UpstreamRs) < 1e-6)), 2) : null]).ToList();
            tables.Add(new("Bridges (HEC-RAS methods)", ["Bridge", "Profile", "Flow type", $"W.S. US ({L})", $"E.G. US ({L})", $"W.S. DS ({L})", $"E.G. DS ({L})", $"Energy loss ({L})", $"Q opening ({Qu})", $"Q weir ({Qu})", $"Vel opening ({V})",
                $"Energy-method W.S. US ({L})", $"Yarnell W.S. US ({L})", $"Low chord ({L})", $"Low chord − W.S. US ({L})", $"High chord ({L})", $"Overtopping head ({L})"], brows,
                ["Low flow: energy method through the opening (sections BD and BU cut by the abutments and piers, deck width as reach length, bridge contraction/expansion coefficients), or Yarnell ΔH = 2K(K + 10ω − 0.6)(α + 15α⁴)·V²/2g for piers (class A).",
                 "High flow (upstream E.G. at or above the low chord): sluice gate Q = Cd·A·√(2g(Y3 − Z/2 + V3²/2g)), or orifice Q = C·A·√(2gH) when the downstream W.S. is above the low chord; weir Q = C·∫H^1.5 dx over the road with Villemonte submergence. Opening and weir flows are balanced at one upstream E.G.",
                 "US = the section just upstream (HEC-RAS section 3); DS = the section just downstream (section 2)."]));
            foreach (var b in bridgeResults.Where(b => b.Upstream.Ws > b.Bridge.LowChord - 1e-9 && b.QWeir <= 0))
                msgs.Add(CalcMessage.Warning("W-BR-003", $"{b.Profile}, {b.Bridge.Name}: the upstream W.S. {b.Upstream.Ws:F2} {L} is above the low chord {b.Bridge.LowChord:F2}: pressure flow."));
            foreach (var b in bridgeResults.Where(b => b.QWeir > 0))
                msgs.Add(CalcMessage.Warning("W-BR-004", $"{b.Profile}, {b.Bridge.Name}: the road is overtopped; {b.QWeir:F1} {Qu} flows over the weir."));
        }

        // Charts: longitudinal profile (channel distance from the downstream section).
        var dist = new double[sections.Count];
        for (int i = 1; i < sections.Count; i++) dist[i] = dist[i - 1] + sections[i].LengthCh;
        var series = new List<ChartSeries> { new("Min channel", sections.Select((s, i) => (dist[i], s.MinChannelElevation)).ToList()) };
        series.Add(new("Left bank", sections.Select((s, i) => (dist[i], s.LeftBankElevation)).ToList(), Dashed: true));
        series.Add(new("Right bank", sections.Select((s, i) => (dist[i], s.RightBankElevation)).ToList(), Dashed: true));
        foreach (var (prof, res) in all)
        {
            series.Add(new($"WS {prof}", res.Select((r, i) => (dist[i], r.Ws)).ToList()));
            series.Add(new($"EG {prof}", res.Select((r, i) => (dist[i], r.Eg)).ToList(), Dashed: true));
        }
        foreach (var br in bridges)
        {
            int iu = sections.FindIndex(x => Math.Abs(x.RiverStation - br.UpstreamRs) < 1e-6);
            if (iu < 1) continue;
            double x0 = dist[iu - 1], x1 = Math.Min(dist[iu], x0 + br.DeckWidth);
            series.Add(new($"{br.Name} low chord", [(x0, br.LowChord), (x1, br.LowChord)]));
            series.Add(new($"{br.Name} high chord", [(x0, br.HighChord), (x1, br.HighChord)]));
        }
        var charts = new List<ToolChart> { new("Water surface profile", $"Main channel distance from RS {sections[0].Name} ({L})", $"Elevation ({L})", series) };
        var figures = new List<ToolFigure>();
        foreach (var s in sections.AsEnumerable().Reverse())
        {
            var fs = new List<ToolFigureSeries> { new("Ground", ToolSeriesKind.Ground, s.Points.ToList()) };
            var (prof, res) = all[^1];
            var r = res.Single(x => x.Section == s);
            foreach (var poly in WaterPolygons(s, r.Ws)) fs.Add(new($"WS {prof}", ToolSeriesKind.Water, poly));
            foreach (var br in bridges.Where(b => Math.Abs(b.UpstreamRs - s.RiverStation) < 1e-6))
            {
                fs.Add(new($"{br.Name} deck", ToolSeriesKind.Limit, [(br.LeftAbutment, s.Interp(br.LeftAbutment)), (br.LeftAbutment, br.HighChord), (br.RightAbutment, br.HighChord), (br.RightAbutment, s.Interp(br.RightAbutment))]));
                fs.Add(new($"{br.Name} low chord", ToolSeriesKind.Limit, [(br.LeftAbutment, br.LowChord), (br.RightAbutment, br.LowChord)]));
                foreach (var (ps, pw) in br.Piers)
                    fs.Add(new($"{br.Name} pier", ToolSeriesKind.Limit, [(ps - pw / 2, s.Interp(ps)), (ps - pw / 2, br.LowChord), (ps + pw / 2, br.LowChord), (ps + pw / 2, s.Interp(ps))]));
            }
            fs.Add(new("Bank stations", ToolSeriesKind.Limit, [(s.LeftBank, s.LeftBankElevation), (s.LeftBank, s.MinElevation), (s.RightBank, s.MinElevation), (s.RightBank, s.RightBankElevation)]));
            figures.Add(new ToolFigure($"RS {s.Name}: cross-section, {prof} water surface", fs, $"Station ({L})", $"Elevation ({L})"));
        }

        var summary = new List<string>
        {
            $"{sections.Count} cross-sections, {profiles.Count} profile(s), {regime} regime; downstream boundary {(regime == "supercritical" ? "n/a" : down)}{(regime != "subcritical" ? $", upstream boundary {up}" : "")}.",
        };
        foreach (var (prof, res) in all)
        {
            var top = res[^1];
            int crit = res.Count(r => r.Note.Contains("critical depth assumed"));
            summary.Add($"{prof}: W.S. {res[0].Ws:F2} {L} at RS {res[0].Section.Name} to {top.Ws:F2} {L} at RS {top.Section.Name}; max channel velocity {res.Max(r => r.VCh):F1} {V}{(crit > 0 ? $"; critical depth assumed at {crit} section(s)" : "")}.");
        }
        return new ToolResult(title, summary, tables, msgs) { Charts = charts, Figures = figures };
    }

    /// <summary>Bridges under [Bridges], with [Bridge piers] and an optional [Road profile].</summary>
    public static List<Bridge> ParseBridges(ToolInput p, List<RiverSection> sections, List<CalcMessage> msgs, bool si)
    {
        var list = new List<Bridge>();
        if (!p.HasSection("Bridges")) return list;
        var head = p.Header("Bridges").Select(h => h.ToLowerInvariant()).ToList();
        int Col(string key) => head.FindIndex(h => h.StartsWith(key, StringComparison.Ordinal));
        int cRs = Col("upstream rs"), cLo = Col("low chord"), cHi = Col("high chord"), cW = Col("deck width"), cLa = Col("left abut"), cRa = Col("right abut"),
            cSh = Col("pier shape"), cC = Col("weir c"), cCc = Col("contraction"), cCe = Col("expansion"), cCd = Col("sluice"), cCo = Col("orifice");
        var piers = p.Rows("Bridge piers").Where(r => r.Length >= 3).GroupBy(r => r[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(gr => gr.Key, gr => gr.Select(r => (p.Cell(r, 1, "pier station", gr.Key), p.Cell(r, 2, "pier width", gr.Key))).ToList(), StringComparer.OrdinalIgnoreCase);
        var road = p.Rows("Road profile").Where(r => r.Length >= 3).GroupBy(r => r[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(gr => gr.Key, gr => gr.Select(r => (p.Cell(r, 1, "road station", gr.Key) ?? double.NaN, p.Cell(r, 2, "road elevation", gr.Key) ?? double.NaN)).OrderBy(t => t.Item1).ToList(), StringComparer.OrdinalIgnoreCase);
        var sorted = sections.OrderBy(x => x.RiverStation).ToList();
        foreach (var r in p.Rows("Bridges"))
        {
            string name = r.Length > 0 && r[0].Trim().Length > 0 ? r[0].Trim() : $"Bridge {list.Count + 1}";
            double? C(int i, string what) => i < 0 ? null : p.Cell(r, i, what, name);
            string T(int i) => i >= 0 && i < r.Length ? r[i].Trim() : "";
            double? rs = C(cRs, "upstream RS"), lo = C(cLo, "low chord"), hi = C(cHi, "high chord"), w = C(cW, "deck width"), la = C(cLa, "left abutment"), ra = C(cRa, "right abutment");
            int iu = rs is { } rr ? sorted.FindIndex(x => Math.Abs(x.RiverStation - rr) < 1e-6) : -1;
            if (iu < 1) { msgs.Add(CalcMessage.Error("E-BR-001", $"{name}: Upstream RS must be a cross-section with another section downstream of it (the bridge sits between them).")); continue; }
            if (lo is null || hi is null || lo >= hi) { msgs.Add(CalcMessage.Error("E-BR-002", $"{name}: give the low chord and high chord (top of deck), low below high.")); continue; }
            var up = sorted[iu];
            if (la is null || ra is null || la >= ra) { msgs.Add(CalcMessage.Error("E-BR-003", $"{name}: give the left and right abutment stations, left < right.")); continue; }
            if (w is not > 0) { msgs.Add(CalcMessage.Error("E-BR-004", $"{name}: deck width (bridge length along the flow) must be above zero.")); continue; }
            if (w > up.LengthCh) { msgs.Add(CalcMessage.Error("E-BR-007", $"{name}: the deck width {w} is longer than the channel reach length {up.LengthCh} from RS {up.Name} to the section below.")); continue; }
            var pl = new List<(double, double)>();
            if (piers.TryGetValue(name, out var pr))
                foreach (var (ps, pw) in pr)
                {
                    if (ps is null || pw is not > 0) { msgs.Add(CalcMessage.Error("E-BR-005", $"{name}: each pier needs a station and a width above zero.")); continue; }
                    pl.Add((ps.Value, pw.Value));
                }
            string shape = T(cSh).Length > 0 ? T(cSh) : "semicircular";
            if (Bridge.YarnellK(shape) is null) msgs.Add(CalcMessage.Error("E-BR-008", $"{name}: pier shape \"{shape}\" is not semicircular, twin cylinder (with diaphragm), triangular or square."));
            list.Add(new Bridge(name, up.RiverStation, lo.Value, hi.Value, w.Value, la.Value, ra.Value, pl, shape,
                C(cC, "weir C") ?? (si ? 1.44 : 2.6), C(cCc, "contraction") ?? 0.3, C(cCe, "expansion") ?? 0.5, C(cCd, "sluice Cd") ?? 0.5, C(cCo, "orifice C") ?? 0.8,
                road.TryGetValue(name, out var rd) ? rd : []));
        }
        foreach (var key in piers.Keys.Concat(road.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            if (list.All(b => !b.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) && !msgs.Any(m => m.Text.StartsWith(key + ":")))
                msgs.Add(CalcMessage.Error("E-BR-005", $"[Bridge piers] or [Road profile] names \"{key}\", which is not a bridge under [Bridges]."));
        return list;
    }

    private static List<double> Numbers(string? s) =>
        (s ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(t => ToolInput.TryNumber(t, out double v) ? v : double.NaN).ToList();

    /// <summary>
    /// Turns the text of a HEC-RAS geometry file into [Sections] and [Points] tables for one river and reach
    /// (<paramref name="reach"/> as "River, Reach"; the first reach when null). Returns null tables when nothing can be imported.
    /// </summary>
    public static (string? Tables, List<CalcMessage> Messages) ImportHecRas(string geometryText, string? reach)
    {
        var msgs = new List<CalcMessage>();
        HecRasGeometry.Result g;
        try { g = HecRasGeometry.Parse(geometryText); }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException)
        {
            msgs.Add(CalcMessage.Error("E-RAS-003", $"The HEC-RAS geometry file could not be read: {ex.Message}"));
            return (null, msgs);
        }
        var reaches = g.Sections.Select(s => (s.River, s.Reach)).Distinct().ToList();
        if (reaches.Count == 0) { msgs.Add(CalcMessage.Error("E-RAS-004", "The HEC-RAS geometry file has no cross-sections.")); return (null, msgs); }
        var pick = reaches[0];
        if (reach is not null)
        {
            var parts = reach.Split(',').Select(t => t.Trim()).ToArray();
            int idx = reaches.FindIndex(r => r.River.Equals(parts[0], StringComparison.OrdinalIgnoreCase)
                && (parts.Length < 2 || r.Reach.Equals(parts[1], StringComparison.OrdinalIgnoreCase)));
            if (idx < 0)
            {
                msgs.Add(CalcMessage.Error("E-RAS-005", $"hec-ras reach \"{reach}\" is not in the file. Reaches: {string.Join("; ", reaches.Select(r => $"{r.River}, {r.Reach}"))}."));
                return (null, msgs);
            }
            pick = reaches[idx];
        }
        else if (reaches.Count > 1)
            msgs.Add(CalcMessage.Warning("W-RAS-001", $"The file has {reaches.Count} reaches; {pick.River}, {pick.Reach} was imported. Set hec-ras reach = River, Reach for another. Junctions are not modeled."));
        var secs = g.Sections.Where(s => (s.River, s.Reach) == pick).Select(HecRasGeometry.WithManningPoints).ToList();
        foreach (var s in secs.Where(s => s.Manning.Count == 0))
            msgs.Add(CalcMessage.Warning("W-RAS-002", $"RS {s.Rs}: no Manning's n in the file."));
        foreach (var note in g.Skipped.Where(n => n.StartsWith($"{pick.River} {pick.Reach} ", StringComparison.Ordinal)))
            msgs.Add(CalcMessage.Warning("W-RAS-003", note + (note.Contains("bridge") || note.Contains("culvert") ? ". Enter bridges under [Bridges] if needed." : ".")));
        msgs.Add(CalcMessage.Info("I-RAS-001", $"Imported {secs.Count} cross-sections of {pick.River}, {pick.Reach}{(g.Title.Length > 0 ? $" from \"{g.Title}\"" : "")}. The file does not record its units: they must match the units chosen here."));
        return (HecRasGeometry.ToToolInput(secs), msgs);
    }

    public static List<RiverSection> ParseSections(ToolInput p, double k, List<CalcMessage> msgs)
    {
        var head = p.Header("Sections").Select(h => h.ToLowerInvariant()).ToList();
        int Col(string key) => head.FindIndex(h => h.StartsWith(key, StringComparison.Ordinal));
        int cLb = Col("left bank"), cRb = Col("right bank"), cLl = Col("lob length"), cLc = Col("ch length"), cLr = Col("rob length"),
            cNl = Col("n lob"), cNc = Col("n ch"), cNr = Col("n rob"), cCc = Col("contraction"), cCe = Col("expansion"),
            cIls = Col("ineff left sta"), cIle = Col("ineff left el"), cIrs = Col("ineff right sta"), cIre = Col("ineff right el");
        var pHead = p.Header("Points").Select(h => h.ToLowerInvariant()).ToList();
        int cPn = pHead.FindIndex(h => h == "n" || h.StartsWith("n ", StringComparison.Ordinal));
        var pts = new Dictionary<string, List<(double, double, double?)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in p.Rows("Points"))
        {
            if (r.Length < 3) { msgs.Add(CalcMessage.Error("E-RP-010", $"[Points] row \"{string.Join(",", r)}\" needs RS, station and elevation.")); continue; }
            string rs = r[0].Trim();
            if (p.Cell(r, 1, "station", $"RS {rs}") is not { } x || p.Cell(r, 2, "elevation", $"RS {rs}") is not { } z) continue;
            double? n = cPn >= 0 ? p.Cell(r, cPn, "n", $"RS {rs}") : null;
            if (!pts.TryGetValue(rs, out var l)) pts[rs] = l = [];
            l.Add((x, z, n));
        }
        var list = new List<RiverSection>();
        foreach (var r in p.Rows("Sections"))
        {
            string name = r.Length > 0 ? r[0].Trim() : "";
            if (!ToolInput.TryNumber(name, out double rs)) { msgs.Add(CalcMessage.Error("E-RP-011", $"[Sections]: river station \"{name}\" is not a number.")); continue; }
            double? C(int i, string what) => i < 0 ? null : p.Cell(r, i, what, $"RS {name}");
            if (!pts.TryGetValue(name, out var sp) || sp.Count < 3) { msgs.Add(CalcMessage.Error("E-RP-012", $"RS {name}: give at least 3 points under [Points].")); continue; }
            if (sp.Zip(sp.Skip(1)).Any(t => t.Second.Item1 < t.First.Item1 - 1e-9)) { msgs.Add(CalcMessage.Error("E-RP-013", $"RS {name}: stations must not decrease (left to right looking downstream).")); continue; }
            double lb = C(cLb, "left bank") ?? sp[0].Item1, rb = C(cRb, "right bank") ?? sp[^1].Item1;
            if (lb >= rb || lb < sp[0].Item1 - 1e-9 || rb > sp[^1].Item1 + 1e-9) { msgs.Add(CalcMessage.Error("E-RP-014", $"RS {name}: bank stations must lie on the section with left < right.")); continue; }
            double nl = C(cNl, "n LOB") ?? double.NaN, nc = C(cNc, "n Ch") ?? double.NaN, nr = C(cNr, "n ROB") ?? double.NaN;
            if (double.IsNaN(nc) && sp.All(t => t.Item3 is null)) { msgs.Add(CalcMessage.Error("E-RP-015", $"RS {name}: give n Ch (and n LOB, n ROB), or an n column under [Points].")); continue; }
            if (double.IsNaN(nl)) nl = nc; if (double.IsNaN(nr)) nr = nc;
            if (nl <= 0 || nc <= 0 || nr <= 0 || sp.Any(t => t.Item3 is <= 0)) { msgs.Add(CalcMessage.Error("E-RP-016", $"RS {name}: n must be above zero.")); continue; }
            (double, double)? il = C(cIls, "ineffective left station") is { } a && C(cIle, "ineffective left elevation") is { } b ? (a, b) : null;
            (double, double)? ir = C(cIrs, "ineffective right station") is { } c && C(cIre, "ineffective right elevation") is { } d ? (c, d) : null;
            double ll = C(cLl, "LOB length") ?? 0, lc = C(cLc, "Ch length") ?? 0, lr = C(cLr, "ROB length") ?? 0;
            if (ll < 0 || lc < 0 || lr < 0) { msgs.Add(CalcMessage.Error("E-RP-017", $"RS {name}: reach lengths cannot be negative.")); continue; }
            list.Add(new RiverSection(name, rs, sp, lb, rb, nl, double.IsNaN(nc) ? 0.035 : nc, nr, k, il, ir)
            {
                LengthLob = ll, LengthCh = lc, LengthRob = lr,
                Contraction = C(cCc, "contraction") ?? p.Number("contraction", 0.1), Expansion = C(cCe, "expansion") ?? p.Number("expansion", 0.3),
            });
        }
        foreach (var rs in pts.Keys.Where(k2 => list.All(s => !s.Name.Equals(k2, StringComparison.OrdinalIgnoreCase))))
            if (!msgs.Any(m => m.Text.Contains($"RS {rs}"))) msgs.Add(CalcMessage.Warning("W-RP-001", $"Points for RS {rs} have no row under [Sections] and are not used."));
        if (list.GroupBy(s => s.RiverStation).Any(gr => gr.Count() > 1)) msgs.Add(CalcMessage.Error("E-RP-018", "Two sections have the same river station."));
        return list;
    }

    /// <summary>Discharge-weighted reach length between an upstream and a downstream solution (HEC-RAS Eq. 2-2), using the upstream section's lengths.</summary>
    public static double ReachLength(RiverSection upstream, Result a, Result b)
    {
        double ql = 0.5 * (a.QLob + b.QLob), qc = 0.5 * (a.QCh + b.QCh), qr = 0.5 * (a.QRob + b.QRob), sum = ql + qc + qr;
        return sum > 0 ? (upstream.LengthLob * ql + upstream.LengthCh * qc + upstream.LengthRob * qr) / sum : upstream.LengthCh;
    }

    public static Result Make(RiverSection s, double q, double ws, double crit, double g, double sf, string regime, string note)
    {
        var h = s.At(ws);
        return new Result(s, q, ws, crit, ws + h.VelocityHead(q, g), sf, h, regime, note);
    }

    /// <summary>Head loss between upstream solution u and downstream solution d.</summary>
    public static double Loss(Result u, Result d, double g)
    {
        double len = ReachLength(u.Section, u, d);
        double kbar = u.H.Conveyance + d.H.Conveyance;
        double sf = Math.Pow((u.Q + d.Q) / kbar, 2);
        double hvu = u.H.VelocityHead(u.Q, g), hvd = d.H.VelocityHead(d.Q, g);
        double c = hvd > hvu ? u.Section.Contraction : u.Section.Expansion;
        return len * sf + c * Math.Abs(hvu - hvd);
    }

    /// <summary>
    /// One subcritical standard step: the water surface at upstream section <paramref name="s"/> that balances the energy
    /// equation with the downstream solution <paramref name="d"/>; critical depth (with <paramref name="critical"/> set) when there is none.
    /// </summary>
    public static Result StepUp(RiverSection s, double q, Result d, double g, out bool critical)
    {
        var (crit, _) = s.Critical(q, g);
        double target(double ws) { var u = Make(s, q, ws, crit, g, 0, "", ""); return u.Eg - (d.Eg + Loss(u, d, g)); }
        double lo = crit + 1e-6, hi = Math.Max(crit, d.Ws) + 1;
        Result r;
        critical = target(lo) > 0;
        if (critical) r = Make(s, q, crit, crit, g, 0, "subcritical", "critical depth assumed (no subcritical energy balance)");
        else
        {
            for (int it = 0; it < 60 && target(hi) < 0; it++) hi += 2 * (hi - crit) + 1;
            for (int it = 0; it < 200 && hi - lo > 1e-7; it++) { double m = 0.5 * (lo + hi); if (target(m) < 0) lo = m; else hi = m; }
            r = Make(s, q, 0.5 * (lo + hi), crit, g, 0, "subcritical", "");
        }
        return r with { Sf = Math.Pow((r.Q + d.Q) / (r.H.Conveyance + d.H.Conveyance), 2) };
    }

    private static List<Result> Subcritical(List<RiverSection> secs, double[] qs, double? bc, double g, List<CalcMessage> msgs, string prof,
        IReadOnlyList<Bridge> bridges, string lowFlow, List<BridgeResult> bridgeResults)
    {
        var res = new Result[secs.Count];
        var (c0, m0) = secs[0].Critical(qs[0], g);
        double ws0 = bc ?? c0;
        string note0 = bc is null ? "critical depth (boundary)" : "";
        if (ws0 < c0 - 1e-6) { msgs.Add(CalcMessage.Warning("W-RP-002", $"{prof}: the downstream boundary W.S. {ws0:F2} is below critical ({c0:F2}) for a subcritical run; critical depth used.")); ws0 = c0; note0 = "critical depth assumed"; }
        res[0] = Make(secs[0], qs[0], ws0, c0, g, 0, "subcritical", note0);
        if (m0 > 1) msgs.Add(CalcMessage.Info("I-RP-001", $"{prof}, RS {secs[0].Name}: {m0} specific-energy minima; the lowest is used for critical depth."));
        for (int i = 1; i < secs.Count; i++)
        {
            var s = secs[i]; var d = res[i - 1];
            if (bridges.FirstOrDefault(b => Math.Abs(b.UpstreamRs - s.RiverStation) < 1e-6) is { } br)
            {
                var bres = BridgeHydraulics.Solve(br, s, secs[i - 1], d, qs[i], g, lowFlow, prof, msgs);
                bridgeResults.Add(bres);
                var u = bres.Upstream;
                res[i] = u with { Sf = Math.Pow((u.Q + d.Q) / (u.H.Conveyance + d.H.Conveyance), 2), Regime = "subcritical", Note = $"upstream of {br.Name}: {bres.FlowType}" };
                continue;
            }
            var r = StepUp(s, qs[i], d, g, out bool crit);
            if (crit) msgs.Add(CalcMessage.Warning("W-RP-003", $"{prof}, RS {s.Name}: the energy equation has no subcritical solution; critical depth assumed (HEC-RAS default). Check the section spacing or run the mixed regime."));
            res[i] = r;
        }
        res[0] = res[0] with { Sf = secs.Count > 1 ? res[1].Sf : 0 };
        return [.. res];
    }

    private static List<Result> Supercritical(List<RiverSection> secs, double[] qs, double? bc, double g, List<CalcMessage> msgs, string prof)
    {
        int n = secs.Count;
        var res = new Result[n];
        var (cn, _) = secs[^1].Critical(qs[^1], g);
        double wsN = bc ?? cn;
        if (wsN > cn + 1e-6) { msgs.Add(CalcMessage.Warning("W-RP-002", $"{prof}: the upstream boundary W.S. {wsN:F2} is above critical ({cn:F2}) for a supercritical run; critical depth used.")); wsN = cn; }
        res[n - 1] = Make(secs[^1], qs[^1], wsN, cn, g, 0, "supercritical", bc is null ? "critical depth (boundary)" : "");
        for (int i = n - 2; i >= 0; i--)
        {
            var s = secs[i]; var u = res[i + 1];
            var (crit, _) = s.Critical(qs[i], g);
            double target(double ws) { var d = Make(s, qs[i], ws, crit, g, 0, "", ""); return d.Eg - (u.Eg - Loss(u, d, g)); }
            double lo = s.MinElevation + 1e-5, hi = crit - 1e-6;
            Result r;
            if (target(hi) > 0)
            {
                r = Make(s, qs[i], crit, crit, g, 0, "supercritical", "critical depth assumed (no supercritical energy balance)");
                msgs.Add(CalcMessage.Warning("W-RP-004", $"{prof}, RS {s.Name}: the energy equation has no supercritical solution; critical depth assumed."));
            }
            else
            {
                // E falls with depth on the supercritical branch; target is positive near the bed and negative near critical.
                for (int it = 0; it < 200 && hi - lo > 1e-7; it++) { double m = 0.5 * (lo + hi); if (target(m) > 0) lo = m; else hi = m; }
                r = Make(s, qs[i], 0.5 * (lo + hi), crit, g, 0, "supercritical", "");
            }
            res[i] = r with { Sf = Math.Pow((r.Q + u.Q) / (r.H.Conveyance + u.H.Conveyance), 2) };
        }
        res[n - 1] = res[n - 1] with { Sf = n > 1 ? res[n - 2].Sf : 0 };
        return [.. res];
    }

    private static void Checks(List<Result> res, string prof, List<CalcMessage> msgs, string L)
    {
        for (int i = 1; i < res.Count; i++)
        {
            double ratio = res[i].H.Conveyance / res[i - 1].H.Conveyance;
            if (ratio < 0.7 || ratio > 1.4)
                msgs.Add(CalcMessage.Warning("W-RP-005", $"{prof}: conveyance ratio {ratio:F2} between RS {res[i].Section.Name} and RS {res[i - 1].Section.Name} is outside 0.7–1.4; HEC-RAS suggests more cross-sections there."));
        }
        foreach (var r in res)
            if (r.H.WallLeft || r.H.WallRight)
                msgs.Add(CalcMessage.Warning("W-RP-006", $"{prof}, RS {r.Section.Name}: W.S. {r.Ws:F2} {L} is above the {(r.H.WallLeft ? "left" : "right")} end point; a vertical wall was assumed there. Extend the section."));
    }

    /// <summary>Closed water polygons for each wetted run of the section at a water surface.</summary>
    public static List<List<(double X, double Y)>> WaterPolygons(RiverSection s, double ws)
    {
        var polys = new List<List<(double, double)>>();
        var pts = s.Points;
        List<(double, double)>? cur = null;
        for (int i = 0; i < pts.Count; i++)
        {
            var (x, z) = pts[i];
            bool wet = z < ws;
            if (wet)
            {
                if (cur is null)
                {
                    cur = [];
                    if (i > 0) { var (x0, z0) = pts[i - 1]; cur.Add((x0 + (ws - z0) / (z - z0) * (x - x0), ws)); }
                    else cur.Add((x, ws));
                }
                cur.Add((x, z));
            }
            if (cur is not null && (!wet || i == pts.Count - 1))
            {
                if (!wet) { var (x0, z0) = pts[i - 1]; cur.Add((x0 + (ws - z0) / (z - z0) * (x - x0), ws)); }
                else cur.Add((x, ws));
                cur.Add(cur[0]);
                polys.Add(cur); cur = null;
            }
        }
        return polys;
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
