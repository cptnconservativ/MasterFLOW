using System.Globalization;
using MasterFlow.Application.Hydrology;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// NRCS TR-55 hydrology for one or more basins: Worksheet 2 (composite curve number and runoff depth), Worksheet 3
/// (time of concentration), Worksheet 4 (graphical peak discharge) and, for the 24-h Type II storm or the Rapid City
/// frequency storm, a full runoff hydrograph by the NRCS dimensionless unit hydrograph.
/// </summary>
public sealed class Tr55HydrologyTool : IDesignTool
{
    public string Id => "tr55-hydrology";
    public string Name => "TR-55 hydrology (curve number, Tc, peak discharge, unit hydrograph)";
    public string Description => "NRCS TR-55 (1986) worksheets for each basin: composite curve number from TR-55 Tables 2-2a–d with the Figure 2-3/2-4 impervious adjustments, runoff depth, time of concentration (sheet, shallow concentrated and channel flow), the graphical peak discharge (Exhibit 4, Table 4-2 pond factor), and the NRCS unit hydrograph under the 24-h Type II storm or the Rapid City IDCM Table 4-2 frequency storm.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # TR-55 hydrology, U.S. units (ac, ft, in, h/min, cfs).
        # Rainfall: 24-h depths by storm. Default: Rapid City IDCM Table 4-2 24-h depths (2-yr 2.00, 10-yr 3.07, 100-yr 4.55 in).
        storms = 2, 10, 100
        rainfall type = II          # I, IA, II or III (Rapid City is Type II)
        # P10 = 3.07                # override a 24-h depth (in), e.g. from NOAA Atlas 14
        hydrograph storm = type-ii  # type-ii (24 h), rapid-city (IDCM frequency storm) or none
        storm duration = 120        # min, rapid-city storm only
        criteria = rapid city       # flags IDCM §4.2.1 limits; "none" to turn off

        [Land cover]
        # Worksheet 2. Cover: a TR-55 Table 2-2 key (see the tool doc), or blank with a CN. HSG: A, B, C or D.
        # Impervious (%) and Unconnected (%) apply the Figure 2-3 / 2-4 adjustment to a pervious cover's CN.
        Basin	Cover	HSG	Area (ac)	CN	Impervious (%)	Unconnected (%)
        North	residential 1/4	C	42
        North	open space good	C	11
        North	commercial	C	6
        North	woods fair	C	14
        South	pasture fair	B	55
        South	open space fair	B	18	 	20	50
        South	gravel road	B	2.5

        [Tc flow paths]
        # Worksheet 3. Type: sheet (n from TR-55 Table 3-1, P2 = 2-yr 24-h depth; ≤ 100 ft), shallow (paved / unpaved), channel (n, R ft).
        Basin	Type	Length (ft)	Slope (ft/ft)	n	P2 (in)	R (ft)	Surface
        North	sheet	100	0.02	0.24	2.0
        North	shallow	1100	0.012				unpaved
        North	channel	2400	0.006	0.035		1.4
        South	sheet	100	0.03	0.15	2.0
        South	shallow	1600	0.02				unpaved
        South	channel	3100	0.008	0.04		1.1

        [Basins]
        # Optional: Tc (min) to override the flow paths, pond and swamp area (% of basin) for Table 4-2.
        Basin	Tc (min)	Pond and swamp (%)
        South		0.5
        """;

    private sealed record CoverRow(string Basin, string Cover, string Hsg, double Area, double Cn, string Note);
    private sealed record Basin(string Name, double Area, double Cn, double TcMin, string TcSource, double PondPct);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "TR-55 hydrology — curve number, time of concentration, peak discharge and unit hydrograph";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title, CalcMessage.Error("E-TR-001", "TR-55 works in U.S. units (its equations, Exhibit 4 and the 484 peak rate factor are in ft, in, mi² and cfs). Switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        string type = (p.Text("rainfall type") ?? "II").ToUpperInvariant().Replace("TYPE", "").Trim();
        if (!Tr55Tables.PeakCoefficients.ContainsKey(type)) msgs.Add(CalcMessage.Error("E-TR-002", $"rainfall type \"{p.Text("rainfall type")}\" must be I, IA, II or III."));
        bool rapidCity = !(p.Text("criteria") ?? "rapid city").StartsWith("none", StringComparison.OrdinalIgnoreCase);

        // ---- storms ----
        var storms = new List<(string Label, int? Year, double P)>();
        foreach (var s in (p.Text("storms") ?? "2, 10, 100").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(s.Replace("-yr", "").Replace("yr", ""), out int y) || y <= 0) { msgs.Add(CalcMessage.Error("E-TR-003", $"storms: \"{s}\" is not a return period in years.")); continue; }
            double depth = p.OptionalNumber($"P{y}") ?? (DesignStorms.RapidCityDepths.TryGetValue(y, out var d) ? d[^1] : double.NaN);
            if (!(depth > 0)) { msgs.Add(CalcMessage.Error("E-TR-004", $"Give the {y}-yr 24-h depth as P{y} = (in); IDCM Table 4-2 has the 2-, 10- and 100-yr only.")); continue; }
            if (p.OptionalNumber($"P{y}") is null) msgs.Add(CalcMessage.Info("I-TR-001", $"{y}-yr 24-h depth {depth:G3} in from Rapid City IDCM Table 4-2."));
            storms.Add(($"{y}-yr", y, depth));
        }

        // ---- Worksheet 2: cover and CN ----
        var lc = p.Header("Land cover"); var lcRows = p.Rows("Land cover");
        int cB = Col(lc, "basin"), cCov = Col(lc, "cover"), cH = Col(lc, "hsg", "soil"), cA = Col(lc, "area"), cCn = Col(lc, "cn"), cImp = Col(lc, "imp"), cUn = Col(lc, "uncon");
        if (lcRows.Count == 0) msgs.Add(CalcMessage.Error("E-TR-005", "[Land cover] is empty: Basin, Cover, HSG, Area (ac), and optionally CN, Impervious (%), Unconnected (%)."));
        var covers = new List<CoverRow>();
        foreach (var r in lcRows)
        {
            string basin = Str(r, cB) ?? "?", where = $"Land cover {basin}";
            string? cov = Str(r, cCov), hsg = Str(r, cH);
            double? area = Num(p, r, cA, "area", where), cn = Num(p, r, cCn, "CN", where), imp = Num(p, r, cImp, "impervious", where), un = Num(p, r, cUn, "unconnected", where);
            if (area is not > 0) { msgs.Add(CalcMessage.Error("E-TR-006", $"{where} ({cov ?? "CN"}): area must be greater than 0.")); continue; }
            string note = "";
            double cnv;
            if (cn is { } given)
            {
                cnv = given; note = cov is null ? "entered" : $"entered ({cov})";
            }
            else
            {
                var c = cov is null ? null : Tr55Tables.Find(cov);
                if (c is null) { msgs.Add(CalcMessage.Error("E-TR-007", $"{where}: cover \"{cov}\" is not a TR-55 Table 2-2 key; enter a CN instead, or use a key from the tool doc (e.g. open space good, residential 1/4, woods fair).")); continue; }
                if (hsg is null || hsg.Length != 1 || "ABCD".IndexOf(char.ToUpperInvariant(hsg[0])) < 0) { msgs.Add(CalcMessage.Error("E-TR-008", $"{where} ({c.Key}): HSG must be A, B, C or D (dual groups such as B/D: use the drained or undrained group, TR-55 Appendix A).")); continue; }
                cnv = c.For(hsg[0]);
                if (double.IsNaN(cnv)) { msgs.Add(CalcMessage.Error("E-TR-008", $"{where}: TR-55 Table {c.Table} gives no CN for {c.Key} in HSG {hsg}.")); continue; }
                note = $"Table {c.Table}: {c.Description}";
            }
            if (imp is { } ip)
            {
                if (ip is < 0 or > 100 || un is < 0 or > 100) { msgs.Add(CalcMessage.Error("E-TR-009", $"{where}: impervious and unconnected must be 0–100 %.")); continue; }
                double rr = (un ?? 0) / 100;
                double adj = Tr55Tables.CompositeImpervious(cnv, ip, rr);
                note += $"; {ip:G3} % impervious{(rr > 0 ? $", {un:G3} % of it unconnected" : "")}: Fig. {(ip < 30 && rr > 0 ? "2-4" : "2-3")} CN {cnv:G3} → {adj:F1}";
                if (rr > 0 && ip >= 30) msgs.Add(CalcMessage.Info("I-TR-002", $"{where}: unconnected impervious area is credited only when total impervious is under 30 % (TR-55 Fig. 2-4); Fig. 2-3 used."));
                cnv = adj;
            }
            if (cnv is < 30 or > 100) { msgs.Add(CalcMessage.Error("E-TR-010", $"{where}: CN {cnv:G4} is outside 30–100.")); continue; }
            covers.Add(new(basin, cov ?? "", hsg ?? "", area.Value, cnv, note));
        }

        // ---- Worksheet 3: Tc ----
        var tcRows = new List<IReadOnlyList<object?>>();
        var ph = p.Header("Tc flow paths"); var paths = p.Rows("Tc flow paths");
        int pB = Math.Max(0, Col(ph, "basin", "sub")), pT = Col(ph, "type"), pL = Col(ph, "length"), pS = Col(ph, "slope"), pN = Col(ph, "n"), pP2 = Col(ph, "p2"), pR = Col(ph, "r", "hydraulic"), pSurf = Col(ph, "surface");
        var bh = p.Header("Basins"); var bRows = p.Rows("Basins");
        int bB = Math.Max(0, Col(bh, "basin", "name")), bTc = Col(bh, "tc"), bPond = Col(bh, "pond");
        double p2Default = storms.FirstOrDefault(s => s.Year == 2).P is > 0 and var v2 ? v2 : DesignStorms.RapidCityDepths[2][^1];

        var basins = new List<Basin>();
        var cnRows = new List<IReadOnlyList<object?>>();
        foreach (var g in covers.GroupBy(c => c.Basin, StringComparer.OrdinalIgnoreCase))
        {
            string name = g.First().Basin;
            double area = g.Sum(c => c.Area), cn = g.Sum(c => c.Area * c.Cn) / area;
            foreach (var c in g) cnRows.Add([name, c.Cover == "" ? "(entered CN)" : c.Cover, c.Hsg, c.Cn, c.Area, c.Area * c.Cn, c.Note]);
            cnRows.Add([name, "Composite", null, Math.Round(cn, 1), area, area * cn, $"Σ(CN × A) / ΣA = {area * cn:F1} / {area:G6}; {area / 640:F4} mi²"]);
            if (g.Select(c => c.Cn).Distinct().Count() > 1 && g.Max(c => c.Cn) - g.Min(c => c.Cn) > 5 && g.Any(c => c.Cn >= 98) && g.Any(c => c.Cn < 70))
                msgs.Add(CalcMessage.Info("I-TR-003", $"Basin {name}: CNs differ by more than 5 and impervious and pervious covers are mixed; TR-55 (Ch. 2, limitations) suggests computing runoff for each cover separately where the difference matters, especially at low P."));

            var brow = bRows.FirstOrDefault(r => Eq(Str(r, bB) ?? "", name));
            double? tcIn = brow is null ? null : Num(p, brow, bTc, "Tc", $"Basins {name}");
            double pond = brow is null ? 0 : Num(p, brow, bPond, "pond and swamp", $"Basins {name}") ?? 0;
            double tcMin; string src;
            var segs = paths.Where(x => Eq(Str(x, pB) ?? "", name)).ToList();
            if (tcIn is > 0) { tcMin = tcIn.Value; src = "entered"; }
            else if (segs.Count > 0)
            {
                double sum = 0; bool bad = false;
                foreach (var s in segs)
                {
                    string kind = (Str(s, pT) ?? "").ToLowerInvariant(), w = $"Tc flow path {name}";
                    double? L = Num(p, s, pL, "length", w), S = Num(p, s, pS, "slope", w), n = Num(p, s, pN, "n", w), p2 = Num(p, s, pP2, "P2", w), rr = Num(p, s, pR, "R", w);
                    if (L is not > 0 || S is not > 0) { msgs.Add(CalcMessage.Error("E-TR-011", $"{w}: length and slope greater than 0 are required.")); bad = true; continue; }
                    double minutes, vel; string detail;
                    switch (kind)
                    {
                        case "sheet":
                            if (n is not > 0) { msgs.Add(CalcMessage.Error("E-TR-012", $"{w}: sheet flow needs Manning's n (TR-55 Table 3-1).")); bad = true; continue; }
                            if (L > 300) { msgs.Add(CalcMessage.Error("E-TR-013", $"{w}: sheet flow {L:G4} ft is longer than the TR-55 limit of 300 ft.")); bad = true; continue; }
                            if (L > 100) msgs.Add(CalcMessage.Warning("W-TR-001", $"{w}: sheet flow {L:G4} ft; NRCS NEH 630 Ch. 15 (2010) limits sheet flow to 100 ft."));
                            double p2v = p2 ?? p2Default;
                            minutes = Tr55.SheetHours(n.Value, L.Value, p2v, S.Value) * 60; vel = L.Value / (minutes * 60);
                            detail = $"Eq. 3-3: 0.007 ({n:G3} × {L:G4})^0.8 / ({p2v:G3}^0.5 × {S:G3}^0.4)";
                            break;
                        case "shallow":
                            bool paved = (Str(s, pSurf) ?? "unpaved").StartsWith("paved", StringComparison.OrdinalIgnoreCase);
                            vel = Tr55.ShallowVelocity(paved, S.Value); minutes = L.Value / vel / 60;
                            detail = $"Fig. 3-1, {(paved ? "paved: V = 20.3282 √s" : "unpaved: V = 16.1345 √s")}";
                            break;
                        case "channel":
                            if (n is not > 0 || rr is not > 0) { msgs.Add(CalcMessage.Error("E-TR-012", $"{w}: channel flow needs n and hydraulic radius R (ft), bankfull.")); bad = true; continue; }
                            vel = Tr55.ChannelVelocity(n.Value, rr.Value, S.Value); minutes = L.Value / vel / 60;
                            detail = $"Eq. 3-4: V = 1.49 × {rr:G3}^⅔ × {S:G3}^½ / {n:G3}";
                            break;
                        default: msgs.Add(CalcMessage.Error("E-TR-014", $"{w}: type \"{kind}\" must be sheet, shallow or channel.")); bad = true; continue;
                    }
                    tcRows.Add([name, kind, L, S, vel, minutes / 60, minutes, detail]);
                    sum += minutes;
                }
                if (bad) continue;
                tcRows.Add([name, "Tc", segs.Sum(s => Num(p, s, pL, "length", "") ?? 0), null, null, sum / 60, sum, "Σ Tt"]);
                tcMin = sum; src = "TR-55 Ch. 3";
            }
            else { msgs.Add(CalcMessage.Error("E-TR-015", $"Basin {name}: give Tc (min) under [Basins] or flow paths under [Tc flow paths].")); continue; }
            if (pond is < 0) { msgs.Add(CalcMessage.Error("E-TR-016", $"Basin {name}: pond and swamp area must be ≥ 0 %.")); continue; }
            basins.Add(new(name, area, cn, tcMin, src, pond));
        }
        foreach (var r in bRows.Where(r => !covers.Any(c => Eq(c.Basin, Str(r, bB) ?? ""))))
            msgs.Add(CalcMessage.Warning("W-TR-002", $"[Basins] row \"{Str(r, bB)}\" has no land cover and is ignored."));
        foreach (var r in paths.Where(r => !covers.Any(c => Eq(c.Basin, Str(r, pB) ?? ""))))
            msgs.Add(CalcMessage.Warning("W-TR-002", $"[Tc flow paths] row for \"{Str(r, pB)}\" has no land cover and is ignored."));
        if (p.Errors.Count > 0 || msgs.Any(m => m.Severity == MessageSeverity.Error)) return ToolResult.Failed(title, [.. p.Errors, .. msgs]);

        // ---- Worksheet 4: graphical peak discharge, and the unit hydrograph ----
        string hydroMode = (p.Text("hydrograph storm") ?? "type-ii").ToLowerInvariant().Replace(" ", "-").Replace("type-2", "type-ii").Replace("typeii", "type-ii");
        double stormMin = p.Number("storm duration", 120);
        if (hydroMode == "type-ii" && type != "II")
        {
            msgs.Add(CalcMessage.Warning("W-TR-003", $"Hydrographs use the 24-h Type II distribution only; rainfall type is {type}, so hydrographs are not computed."));
            hydroMode = "none";
        }
        if (hydroMode is not ("type-ii" or "rapid-city" or "none")) { msgs.Add(CalcMessage.Error("E-TR-017", "hydrograph storm must be type-ii, rapid-city or none.")); return ToolResult.Failed(title, [.. msgs]); }
        if (hydroMode == "rapid-city" && (!(stormMin is >= 60 and <= 1440) || Math.Abs(stormMin / 5 - Math.Round(stormMin / 5)) > 1e-9))
        { msgs.Add(CalcMessage.Error("E-TR-018", "storm duration must be a multiple of 5 min from 60 to 1440 (IDCM §4.3.2).")); return ToolResult.Failed(title, [.. msgs]); }

        var peakRows = new List<IReadOnlyList<object?>>();
        var hydroRows = new List<IReadOnlyList<object?>>();
        var charts = new List<ToolChart>();
        var hydroSummary = new List<string>();
        int color = 0;
        foreach (var b in basins)
        {
            double am = b.Area / 640, tcH = b.TcMin / 60, s = 1000 / b.Cn - 10, ia = 0.2 * s;
            double tcUsed = Math.Max(0.1, tcH);
            if (tcH < 0.1) msgs.Add(CalcMessage.Info("I-TR-004", $"Basin {b.Name}: Tc {b.TcMin:G3} min is below 0.1 h; 0.1 h used for the graphical method (TR-55 Ch. 4 limitation)."));
            if (tcH > 10) msgs.Add(CalcMessage.Warning("W-TR-004", $"Basin {b.Name}: Tc {tcH:G3} h exceeds the 10 h limit of the graphical method; use the hydrograph."));
            if (b.Cn < 40) msgs.Add(CalcMessage.Warning("W-TR-005", $"Basin {b.Name}: CN {b.Cn:F1} is below 40, outside the graphical method's range (TR-55 Ch. 4)."));
            double fp = Tr55Tables.PondFactor(b.PondPct);
            if (double.IsNaN(fp)) { msgs.Add(CalcMessage.Warning("W-TR-006", $"Basin {b.Name}: pond and swamp area {b.PondPct:G3} % exceeds 5 %; the graphical method does not apply (TR-55 Ch. 4). Route the storage instead; no Fp applied.")); fp = 1; }
            if (rapidCity && b.Area > 160)
                msgs.Add(CalcMessage.Info("I-TR-005", $"Basin {b.Name}: {b.Area:G5} ac is over 160 ac. Rapid City IDCM §4.2.1 requires HEC-HMS (Green–Ampt losses, §4.3.3) here; TR-55 results are for comparison or non-City review."));
            if (b.Area > 2000 * 1.0 && b.Area / 640 > 25) msgs.Add(CalcMessage.Warning("W-TR-007", $"Basin {b.Name}: {am:G4} mi² exceeds the 25 mi² limit of TR-55 (Ch. 4 and 5)."));
            foreach (var st in storms)
            {
                double q = Losses.CurveNumberRunoff(st.P, b.Cn);
                double iaP = ia / st.P;
                string iaNote = iaP < 0.1 ? " (0.10 used)" : iaP > 0.5 ? " (0.50 used)" : "";
                double qu = Tr55Tables.UnitPeak(type, Math.Min(tcUsed, 10), iaP);
                double qp = tcH > 10 ? double.NaN : qu * am * q * fp;
                if (iaP > 0.5) msgs.Add(CalcMessage.Warning("W-TR-008", $"Basin {b.Name}, {st.Label}: Ia/P = {iaP:F3} exceeds 0.50; TR-55 uses 0.50 but notes the peak is then poorly defined. Check with the hydrograph."));
                peakRows.Add([b.Name, st.Label, st.P, b.Cn, s, ia, iaP.ToString("F3", CultureInfo.InvariantCulture) + iaNote, q, b.Area * q / 12, tcH, qu, b.PondPct, fp, qp]);
            }
        }

        if (hydroMode != "none")
        {
            double dtMin = Math.Clamp(Math.Floor(basins.Min(b => b.TcMin) * 0.133 * 10) / 10, 0.1, 6);   // NEH 630 Ch. 16: ΔD ≤ 0.133 Tc
            if (hydroMode == "rapid-city") dtMin = Math.Min(dtMin, 5);
            double simMin = hydroMode == "type-ii" ? 1440 + Math.Max(360, 6 * basins.Max(b => b.TcMin)) : stormMin + Math.Max(360, 6 * basins.Max(b => b.TcMin));
            int nSteps = (int)Math.Ceiling(simMin / dtMin);
            if (nSteps > 200000) { dtMin = simMin / 200000; nSteps = 200000; }
            var stormCurves = new List<(string Label, double[] Cum, string Basis)>();
            foreach (var st in storms)
            {
                if (hydroMode == "type-ii")
                    stormCurves.Add((st.Label, DesignStorms.Sample(DesignStorms.TypeII.Select(t => (t.Hour * 60, t.Fraction * st.P)).ToList(), dtMin, nSteps), $"NRCS 24-h Type II, P = {st.P:G4} in"));
                else if (st.Year is { } y && DesignStorms.RapidCityDepths.TryGetValue(y, out var dd))
                {
                    var blocks = DesignStorms.FrequencyStormBlocks(DesignStorms.RapidCityDurations, dd, stormMin, 5, 0.25);
                    stormCurves.Add((st.Label, DesignStorms.Sample(DesignStorms.FromBlocks(blocks, 5), dtMin, nSteps), $"IDCM Table 4-2 frequency storm, {stormMin:G4} min, peak at the first quartile"));
                }
                else msgs.Add(CalcMessage.Warning("W-TR-009", $"{st.Label}: IDCM Table 4-2 has no depths for this storm; no Rapid City hydrograph."));
            }
            hydroSummary.Add($"Hydrographs: NRCS dimensionless unit hydrograph (NEH 630 Ch. 16, peak rate factor 484), lag = 0.6 Tc, Δt = {dtMin:G3} min (≤ 0.133 Tc), {stormCurves.FirstOrDefault().Basis?.Split(',')[0] ?? ""}.");
            foreach (var (label, cum, basis) in stormCurves)
            {
                var series = new List<ChartSeries>();
                int ci = 0;
                foreach (var b in basins)
                {
                    var excess = Losses.CurveNumber(cum, b.Cn);
                    var uh = ScsUnitHydrograph.Ordinates(b.Area, 0.6 * b.TcMin / 60, dtMin / 60);
                    var hq = ScsUnitHydrograph.Convolve(excess, uh, nSteps + 1);
                    int ipk = Array.IndexOf(hq, hq.Max());
                    double vol = hq.Sum() * dtMin * 60 / 43560;
                    var graphical = peakRows.FirstOrDefault(r => (string)r[0]! == b.Name && (string)r[1]! == label);
                    double gq = graphical is null ? double.NaN : (double)graphical[^1]!;
                    hydroRows.Add([b.Name, label, hq[ipk], ipk * dtMin / 60, vol, excess[^1], gq, double.IsFinite(gq) && gq > 0 ? (hq[ipk] - gq) / gq * 100 : double.NaN]);
                    series.Add(new ChartSeries(b.Name, Thin(hq, dtMin), Color: ci++));
                }
                charts.Add(new ToolChart($"Runoff hydrographs, {label} ({basis})", "Time (h)", "Flow (cfs)", series));
                color++;
            }
        }

        // ---- result ----
        var summary = new List<string>
        {
            $"{basins.Count} basin(s); rainfall type {type}; storms {string.Join(", ", storms.Select(s => $"{s.Label} {s.P:G3} in"))} (24 h).",
        };
        foreach (var b in basins)
        {
            var pk = peakRows.Where(r => (string)r[0]! == b.Name).Select(r => $"{r[1]} {DesignTools.Cell(r[^1])} cfs");
            summary.Add($"{b.Name}: {b.Area:G6} ac, CN {b.Cn:F1}, Tc {b.TcMin:F1} min ({b.TcSource}); graphical peaks {string.Join(", ", pk)}.");
        }
        summary.AddRange(hydroSummary);
        if (rapidCity)
            msgs.Add(CalcMessage.Info("I-TR-006", "Rapid City IDCM §4.2.1 and §4.3: the rational method (≤ 160 ac) or HEC-HMS with Green–Ampt losses and the Table 4-2 frequency storm is the City's method; NRCS curve number results are for comparison, SDDOT or other agencies. The Hydrograph network routing tool runs the City's Green–Ampt method."));

        var tables = new List<ToolTable>
        {
            new("Worksheet 2 — runoff curve number", ["Basin", "Cover", "HSG", "CN", "Area (ac)", "CN × A", "Source"], cnRows,
                ["CNs from TR-55 (1986) Tables 2-2a–2-2d, average antecedent runoff condition (ARC II), Ia = 0.2 S.", "Composite CN is area-weighted (Worksheet 2); Figures 2-3 and 2-4 adjust a pervious cover for its impervious share."]),
        };
        if (tcRows.Count > 0)
            tables.Add(new("Worksheet 3 — time of concentration", ["Basin", "Flow", "Length (ft)", "Slope (ft/ft)", "V (ft/s)", "Tt (h)", "Tt (min)", "Equation"], tcRows,
                ["Sheet flow Eq. 3-3 with the 2-yr 24-h depth P2; shallow concentrated Figure 3-1 equations; open channel Manning, Eq. 3-4."]));
        tables.Add(new($"Worksheet 4 — graphical peak discharge (Type {type})", ["Basin", "Storm", "P (in)", "CN", "S (in)", "Ia (in)", "Ia/P", "Q (in)", "Runoff (ac-ft)", "Tc (h)", "qu (csm/in)", "Pond (%)", "Fp", "qp (cfs)"], peakRows,
            ["Q = (P − Ia)² / (P − Ia + S), S = 1000/CN − 10, Ia = 0.2 S (Eqs. 2-1 to 2-4).", "qu from Exhibit 4 (Appendix F, Table F-1: log qu = C0 + C1 log Tc + C2 (log Tc)², linear between Ia/P), Tc limited to 0.1–10 h, Ia/P to 0.1–0.5.", "qp = qu · Am · Q · Fp (Eq. 4-1); Fp from Table 4-2."]));
        if (hydroRows.Count > 0)
            tables.Add(new("Unit hydrograph results", ["Basin", "Storm", "Peak (cfs)", "Time to peak (h)", "Volume (ac-ft)", "Runoff (in)", "Graphical qp (cfs)", "Difference (%)"], hydroRows,
                ["Volume is the hydrograph integral and equals Q × A within the unit hydrograph's rounding (it is scaled to hold exactly 1 in).", "Difference = (UH peak − graphical qp) / graphical qp; TR-55's graphical method and the convolution differ by design (Exhibit 4 is a fit to TR-20 runs)."]));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static List<(double X, double Y)> Thin(double[] q, double dtMin)
    {
        int every = Math.Max(1, q.Length / 600);
        var pts = new List<(double, double)>();
        for (int i = 0; i < q.Length; i += every) pts.Add((i * dtMin / 60, q[i]));
        return pts;
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static int Col(IReadOnlyList<string> head, params string[] keys)
    {
        for (int i = 0; i < head.Count; i++)
            foreach (var k in keys)
                if (head[i].Trim().StartsWith(k, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
    private static string? Str(string[] r, int i) => i >= 0 && i < r.Length && !string.IsNullOrWhiteSpace(r[i]) ? r[i].Trim() : null;
    private static double? Num(ToolInput p, string[] r, int i, string what, string where) => i < 0 ? null : p.Cell(r, i, what, where);
}
