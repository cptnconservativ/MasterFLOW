using System.Globalization;
using System.Text;
using MasterFlow.Application.Hydrology;
using MasterFlow.Application.Swmm;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// EPA SWMM 5 exchange for the selected storm network: export a .inp file (dynamic wave, constant design inflows or
/// subcatchments under a Rapid City or NRCS Type II storm), import a .inp file into the network, and compare a SWMM
/// report's peak HGL and flows with MasterFlow's steady HGL.
/// </summary>
public sealed class SwmmExchangeTool : IDesignTool
{
    public string Id => "swmm-exchange";
    public string Name => "EPA SWMM 5 exchange: export, import, compare results";
    public string Description => "Writes the selected storm network to an EPA SWMM 5 input file (.inp) for a dynamic-wave run: junctions, outfalls, pipes, losses, constant design inflows or subcatchments (Green–Ampt per IDCM Table 4-4, or curve number) under the IDCM Table 4-2 frequency storm or an NRCS Type II storm. Reads a .inp file into the network (Apply to network). Compares a SWMM report (.rpt) with MasterFlow's HGL and pipe flows, structure by structure.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context)
    {
        string first = context.Network?.Structures.FirstOrDefault(s => context.Network.Pipes.Any(p => p.From == s.Name))?.Name ?? "MH-1";
        return $"""
            # mode = export (write a .inp), import (read a .inp into the selected network) or compare (SWMM report vs MasterFlow HGL).
            mode = export
            inflows = design          # constant structure inflows: design, minor or none
            duration = 6              # hours simulated
            routing step = 5          # s
            entry loss = 0            # SWMM conduit entry and exit K (standard-K structures carry their own K)
            exit loss = 0
            # For import or compare, give a file path, or paste the .inp / .rpt text below the keys:
            # file = C:\Projects\Main St\main-st.inp

            # Optional subcatchments (U.S. units); they replace the constant inflows. Infiltration: CN, or an IDCM Table 4-4
            # soil (Green–Ampt), or Suction (in) and Ks (in/h).
            # storm = rapid-city      # rapid-city (IDCM Table 4-2 frequency storm) or type-ii (give P24)
            # year = 10
            # storm duration = 120    # min (rapid-city)
            # [Subcatchments]
            # Name	Outlet	Area (ac)	Impervious (%)	Width (ft)	Slope (%)	Soil
            # A1	{first}	4.2	55	600	2.0	sandy loam
            """;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "EPA SWMM 5 exchange";
        var msgs = new List<CalcMessage>();
        string text = input ?? "";
        // Keys come before any pasted SWMM text; the SWMM text itself is read raw.
        int raw = FirstSwmmLine(text);
        var p = ToolInput.Parse(raw >= 0 ? text[..raw] : text);
        string mode = (p.Text("mode") ?? "export").ToLowerInvariant();
        string? body = raw >= 0 ? text[raw..] : null;
        if (p.Text("file") is { } path)
        {
            try { body = File.ReadAllText(path.Trim('"')); }
            catch (Exception ex) { return ToolResult.Failed(title, CalcMessage.Error("E-SW-001", $"Could not read {path}: {ex.Message}")); }
        }
        return mode switch
        {
            "export" => Export(p, context, msgs, title),
            "import" => Import(body, context, msgs, title),
            "compare" => Compare(body, context, msgs, title),
            _ => ToolResult.Failed(title, CalcMessage.Error("E-SW-002", $"mode \"{mode}\" is not export, import or compare.")),
        };
    }

    /// <summary>Index of the first line that starts SWMM content (an .inp section other than the tool's own, or a report banner).</summary>
    private static int FirstSwmmLine(string text)
    {
        string[] inpSections = ["[TITLE]", "[OPTIONS]", "[JUNCTIONS]", "[CONDUITS]", "[OUTFALLS]"];
        int pos = 0;
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (inpSections.Any(s => t.Equals(s, StringComparison.OrdinalIgnoreCase)) || t.StartsWith("EPA STORM WATER MANAGEMENT MODEL", StringComparison.OrdinalIgnoreCase) || t.StartsWith("*****"))
                return pos;
            pos += line.Length + 1;
        }
        return -1;
    }

    private static ToolResult Export(ToolInput p, DesignToolContext ctx, List<CalcMessage> msgs, string title)
    {
        if (ctx.Network is not { } net) return ToolResult.Failed(title, CalcMessage.Error("E-SW-003", "Select a storm network to export."));
        if (net.Pipes.Count == 0) return ToolResult.Failed(title, CalcMessage.Error("E-SW-004", "The selected network has no pipes."));
        bool si = net.Units == UnitSystem.SI;
        var subs = new List<SwmmSubcatchment>();
        var head = p.Header("Subcatchments").Select(h => h.ToLowerInvariant()).ToList();
        int Col(params string[] keys) => head.FindIndex(h => keys.Any(k => h.StartsWith(k, StringComparison.Ordinal)));
        int cOut = Col("outlet"), cA = Col("area"), cI = Col("imperv"), cW = Col("width"), cS = Col("slope"), cCn = Col("cn", "curve"),
            cSoil = Col("soil"), cSu = Col("suction"), cKs = Col("ks"), cDef = Col("deficit"), cNi = Col("n-imperv", "n imperv"), cNp = Col("n-perv", "n perv");
        foreach (var r in p.Rows("Subcatchments"))
        {
            string name = r.Length > 0 && r[0].Length > 0 ? r[0] : $"S{subs.Count + 1}";
            double? C(int i, string what) => i < 0 ? null : p.Cell(r, i, what, name);
            double? a = C(cA, "area"), imp = C(cI, "impervious"), w = C(cW, "width"), s = C(cS, "slope");
            string outlet = cOut >= 0 && cOut < r.Length ? r[cOut] : "";
            if (a is not > 0 || w is not > 0 || s is not > 0 || imp is not (>= 0 and <= 100) || outlet.Length == 0)
            { msgs.Add(CalcMessage.Error("E-SW-005", $"{name}: give an outlet, area > 0, impervious 0–100 %, width > 0 and slope > 0.")); continue; }
            if (!net.Structures.Any(x => x.Name.Equals(outlet, StringComparison.OrdinalIgnoreCase)))
            { msgs.Add(CalcMessage.Error("E-SW-006", $"{name}: outlet {outlet} is not a structure in the network.")); continue; }
            var sub = new SwmmSubcatchment(name, outlet, a.Value, imp.Value, w.Value, s.Value);
            if (C(cNi, "N-imperv") is { } ni) sub = sub with { NImperv = ni };
            if (C(cNp, "N-perv") is { } np) sub = sub with { NPerv = np };
            if (C(cCn, "CN") is { } cn)
            {
                if (cn is <= 0 or > 100) { msgs.Add(CalcMessage.Error("E-SW-007", $"{name}: CN must be 1–100.")); continue; }
                sub = sub with { CurveNumber = cn };
            }
            else if (cSoil >= 0 && cSoil < r.Length && r[cSoil].Length > 0)
            {
                if (!Losses.RapidCitySoils.TryGetValue(r[cSoil], out var soil)) { msgs.Add(CalcMessage.Error("E-SW-007", $"{name}: soil \"{r[cSoil]}\" is not an IDCM Table 4-4 class ({string.Join(", ", Losses.RapidCitySoils.Keys)}).")); continue; }
                sub = sub with { Suction = soil.Suction, Ks = soil.Ks };
            }
            else
            {
                if (C(cSu, "suction") is { } su) sub = sub with { Suction = su };
                if (C(cKs, "Ks") is { } ks) sub = sub with { Ks = ks };
            }
            if (C(cDef, "deficit") is { } def) sub = sub with { Deficit = def };
            subs.Add(sub);
        }
        if (subs.Any(s => s.CurveNumber is not null) && subs.Any(s => s.CurveNumber is null))
            msgs.Add(CalcMessage.Error("E-SW-008", "SWMM uses one infiltration method per model: give CN for every subcatchment or for none."));
        if (subs.Count > 0 && si) msgs.Add(CalcMessage.Error("E-SW-009", "Subcatchment export is U.S. units only (ac, ft, in); export the SI network without subcatchments."));

        var rain = new List<double>();
        string rainName = "";
        double rainStep = 5;
        if (subs.Count > 0)
        {
            string storm = (p.Text("storm") ?? "rapid-city").ToLowerInvariant();
            int year = (int)p.Number("year", 10);
            if (storm == "rapid-city")
            {
                if (!DesignStorms.RapidCityDepths.TryGetValue(year, out var depths)) msgs.Add(CalcMessage.Error("E-SW-020", "year must be 2, 10 or 100 for the IDCM Table 4-2 storm."));
                else
                {
                    double dur = p.Number("storm duration", 120);
                    if (dur is < 60 or > 1440) msgs.Add(CalcMessage.Error("E-SW-021", "storm duration must be 60–1440 min."));
                    else
                    {
                        rain = DesignStorms.FrequencyStormBlocks(DesignStorms.RapidCityDurations, depths, dur, 5, 0.25).Select(d => d * 12).ToList();
                        rainName = $"IDCM Table 4-2 {year}-yr frequency storm, {dur:F0} min, peak at the first quartile";
                    }
                }
            }
            else if (storm == "type-ii")
            {
                double p24 = p.Number("P24", DesignStorms.RapidCityDepths.TryGetValue(year, out var d) ? d[^1] : double.NaN);
                if (!(p24 > 0)) msgs.Add(CalcMessage.Error("E-SW-022", "Give P24 (24-h depth, in) for the Type II storm."));
                else
                {
                    var curve = DesignStorms.TypeII.Select(t => (t.Hour * 60, t.Fraction * p24)).ToList();
                    var cum = DesignStorms.Sample(curve, 5, 288);
                    rain = Enumerable.Range(0, 288).Select(k => (cum[k + 1] - cum[k]) * 12).ToList();
                    rainName = $"NRCS 24-h Type II storm, P24 = {p24:G3} in.";
                }
            }
            else msgs.Add(CalcMessage.Error("E-SW-023", $"storm \"{storm}\" is not rapid-city or type-ii."));
        }
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double stormHours = rain.Count * rainStep / 60;
        var opt = new SwmmExportOptions
        {
            Title = net.Name,
            DurationHours = p.Number("duration", Math.Max(6, Math.Ceiling(stormHours + 2))),
            RoutingStepSec = p.Number("routing step", 5),
            EntryLoss = p.Number("entry loss", 0), ExitLoss = p.Number("exit loss", 0),
            Inflows = p.Text("inflows") ?? (subs.Count > 0 ? "none" : "design"),
            PondedArea = p.Number("ponded area", 0),
            Subcatchments = subs, RainIntensity = rain, RainStepMin = rainStep, RainName = rainName,
        };
        if (opt.DurationHours <= 0 || opt.RoutingStepSec <= 0) return ToolResult.Failed(title, CalcMessage.Error("E-SW-024", "duration and routing step must be above zero."));
        if (opt.Inflows is not ("design" or "minor" or "none")) return ToolResult.Failed(title, CalcMessage.Error("E-SW-025", "inflows must be design, minor or none."));
        if (subs.Count > 0 && opt.Inflows != "none") msgs.Add(CalcMessage.Warning("W-SW-004", "Both subcatchments and constant structure inflows are written; SWMM adds them, which double counts if the inflows came from the same areas."));

        string inp = SwmmInp.Write(net, ctx.NetworkResult, opt, msgs);
        var inv = SwmmInp.NodeInverts(net);
        string L = net.Units == UnitSystem.SI ? "m" : "ft";
        var nodeRows = net.Structures.Where(s => inv.ContainsKey(s.Name)).Select(s => (IReadOnlyList<object?>)new object?[]
        {
            s.Name, net.Pipes.Any(x => x.From.Equals(s.Name, StringComparison.OrdinalIgnoreCase)) ? "JUNCTION" : "OUTFALL",
            R(inv[s.Name]), s.RimElevation is { } rim ? R(rim - inv[s.Name]) : null, (opt.Inflows == "minor" ? s.MinorInflow : opt.Inflows == "design" ? s.Inflow : null),
        }).ToList();
        var linkRows = net.Pipes.Select(x => (IReadOnlyList<object?>)new object?[]
        {
            x.Name, x.From, x.To, x.Shape == NetworkPipeShape.Circular ? "CIRCULAR" : "ELLIPSE", R(x.Rise), R(x.Length), x.ManningN, R(x.UpstreamInvert - inv[x.From]), R(x.DownstreamInvert - inv[x.To]),
        }).ToList();
        var tables = new List<ToolTable>
        {
            new("Nodes written", ["Node", "Type", $"Invert ({L})", $"Max depth ({L})", $"Inflow ({(net.Units == UnitSystem.SI ? "m³/s" : "cfs")})"], nodeRows,
                ["Node invert = the lowest pipe invert at the structure; max depth = rim − invert."]),
            new("Conduits written", ["Conduit", "From", "To", "Shape", $"Rise ({L})", $"Length ({L})", "n", $"In offset ({L})", $"Out offset ({L})"], linkRows,
                ["Offsets are depths above the node invert (LINK_OFFSETS DEPTH)."]),
        };
        if (subs.Count > 0)
            tables.Add(new("Subcatchments written", ["Name", "Outlet", "Area (ac)", "Impervious (%)", "Width (ft)", "Slope (%)", "Infiltration"],
                subs.Select(s => (IReadOnlyList<object?>)new object?[] { s.Name, s.Outlet, s.AreaAc, s.ImperviousPct, s.WidthFt, s.SlopePct, s.CurveNumber is { } cn ? $"CN {cn:G3}" : $"Green–Ampt ψ {s.Suction:G4} in, Ks {s.Ks:G4} in/h, IMD {s.Deficit:G3}" }).ToList(),
                [rainName, "N-imperv 0.015, N-perv 0.24, depression storage 0.05 / 0.35 in (IDCM Table 4-3 lawn Ia), 25 % impervious with no depression storage, unless given."]));
        var summary = new List<string>
        {
            $"{net.Name}: {nodeRows.Count} nodes and {linkRows.Count} conduits written for a {opt.DurationHours:G3}-h dynamic-wave run (routing step {opt.RoutingStepSec:G3} s).",
            subs.Count > 0 ? $"{subs.Count} subcatchment(s) under the {rainName}." : $"Inflows: {opt.Inflows} structure flows as constant SWMM inflows (steady state after the start-up).",
            "Save the result to get the .inp file; open it in EPA SWMM 5.2, run it, then paste or point to the .rpt with mode = compare.",
        };
        string file = string.Concat(net.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".inp";
        return new ToolResult(title, summary, tables, msgs) { Files = [new ToolFile(file, "EPA SWMM 5 input file", Encoding.UTF8.GetBytes(inp))] };
    }

    private static ToolResult Import(string? body, DesignToolContext ctx, List<CalcMessage> msgs, string title)
    {
        if (body is null) return ToolResult.Failed(title, CalcMessage.Error("E-SW-013", "Give file = path to a .inp file, or paste the .inp text below the keys."));
        var template = (ctx.Network ?? new StormNetwork { Units = ctx.Units }) with { Units = ctx.Network?.Units ?? ctx.Units };
        var net = SwmmInp.Read(body, template, msgs);
        if (net is null) return new(title, [], [], msgs);
        string L = net.Units == UnitSystem.SI ? "m" : "ft";
        var tables = new List<ToolTable>
        {
            new("Structures read", ["Structure", "Kind", $"Rim ({L})", "Inflow", "Easting", "Northing"],
                net.Structures.Select(s => (IReadOnlyList<object?>)new object?[] { s.Name, s.Kind, s.RimElevation, s.Inflow, s.Easting, s.Northing }).ToList()),
            new("Pipes read", ["Pipe", "From", "To", "Shape", $"Rise ({L})", $"Span ({L})", $"Length ({L})", "n", $"Up invert ({L})", $"Down invert ({L})"],
                net.Pipes.Select(x => (IReadOnlyList<object?>)new object?[] { x.Name, x.From, x.To, x.Shape.ToString(), x.Rise, x.Span, x.Length, x.ManningN, R(x.UpstreamInvert), R(x.DownstreamInvert) }).ToList()),
        };
        var summary = new List<string>
        {
            $"{net.Structures.Count} structures and {net.Pipes.Count} pipes read; tailwater {net.Tailwater}{(net.TailwaterElevation is { } t ? $" at {t:F2} {L}" : "")}.",
            ctx.Network is null ? "Create or select a storm network, then run again and press Apply to network to load it." : "Press Apply to network to replace the selected network's structures and pipes (its criteria and name are kept).",
        };
        return new ToolResult(title, summary, tables, msgs) { UpdatedNetwork = net, UpdateDescription = $"Imported {net.Pipes.Count} pipes from a SWMM file." };
    }

    private static ToolResult Compare(string? body, DesignToolContext ctx, List<CalcMessage> msgs, string title)
    {
        if (body is null) return ToolResult.Failed(title, CalcMessage.Error("E-SW-014", "Give file = path to a SWMM report (.rpt), or paste the report text below the keys."));
        if (ctx.NetworkResult is not { } res) return ToolResult.Failed(title, CalcMessage.Error("E-SW-015", "Select a solved storm network to compare with."));
        var rpt = SwmmReport.Parse(body);
        foreach (var e in rpt.Errors) msgs.Add(CalcMessage.Error("E-SW-016", "The SWMM run reported: " + e));
        if (rpt.Nodes.Count == 0) msgs.Add(CalcMessage.Error("E-SW-017", "No Node Depth Summary found in the report. Run SWMM with NODES ALL in [REPORT] (the exported file does)."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        string L = res.Network.Units == UnitSystem.SI ? "m" : "ft", Q = res.Network.Units == UnitSystem.SI ? "m³/s" : "cfs";
        double tol = res.Network.Units == UnitSystem.SI ? 0.03 : 0.1;
        var nodeRows = new List<IReadOnlyList<object?>>();
        var chartMf = new List<(double, double)>(); var chartSw = new List<(double, double)>();
        int k = 0, over = 0, missing = 0;
        foreach (var s in res.Structures)
        {
            if (!rpt.Nodes.TryGetValue(s.Structure.Name, out var n)) { missing++; continue; }
            double d = n.MaxHgl - s.Hgl;
            double? clr = s.Structure.RimElevation is { } rim ? rim - n.MaxHgl : null;
            var fl = rpt.Flooding.TryGetValue(s.Structure.Name, out var f) ? f : default;
            bool flag = Math.Abs(d) > tol;
            if (flag) over++;
            nodeRows.Add([s.Structure.Name, R(s.Hgl), R(n.MaxHgl), R(d), s.HglClearance is { } c0 ? R(c0) : null, clr is { } c1 ? R(c1) : null, fl.MaxRate > 0 ? R(fl.MaxRate) : null, fl.Hours > 0 ? fl.Hours : null, flag ? "check" : "agrees"]);
            chartMf.Add((k, s.Hgl)); chartSw.Add((k, n.MaxHgl)); k++;
        }
        var linkRows = new List<IReadOnlyList<object?>>();
        foreach (var x in res.Pipes)
        {
            if (!rpt.Links.TryGetValue(x.Pipe.Name, out var l)) continue;
            linkRows.Add([x.Pipe.Name, R(x.Pipe.Discharge), R(l.MaxFlow), double.IsFinite(x.Pipe.Discharge) && x.Pipe.Discharge > 0 ? R(100 * (l.MaxFlow / x.Pipe.Discharge - 1)) : null, R(l.MaxVelocity), l.MaxOverFullFlow, l.MaxOverFullDepth, l.MaxOverFullDepth >= 1 ? "surcharged" : ""]);
        }
        if (missing > 0) msgs.Add(CalcMessage.Warning("W-SW-020", $"{missing} structure(s) are not in the SWMM report (names must match)."));
        if (rpt.Flooding.Count > 0) msgs.Add(CalcMessage.Warning("W-SW-021", $"SWMM reports flooding at {rpt.Flooding.Count} node(s): {string.Join(", ", rpt.Flooding.Keys.Take(8))}."));
        var tables = new List<ToolTable>
        {
            new("HGL: MasterFlow steady vs SWMM dynamic-wave peak", ["Structure", $"MasterFlow HGL ({L})", $"SWMM max HGL ({L})", $"SWMM − MasterFlow ({L})", $"Rim − HGL, MasterFlow ({L})", $"Rim − HGL, SWMM ({L})", $"Flooding rate ({Q})", "Hours flooded", "Result"], nodeRows,
                [$"\"check\" where the two differ by more than {tol} {L}. Differences are expected: SWMM's peak includes storage and timing; MasterFlow's steady HGL uses IDCM structure losses that SWMM does not have."]),
            new("Pipe flows", ["Pipe", $"MasterFlow Q ({Q})", $"SWMM max Q ({Q})", "Difference (%)", "SWMM max V", "Max/full flow", "Max/full depth", ""], linkRows),
        };
        var charts = new List<ToolChart> { new("HGL at each structure", "Structure (order of the network result)", $"Elevation ({L})", [new("MasterFlow steady HGL", chartMf), new("SWMM max HGL", chartSw, Dashed: true)]) };
        var summary = new List<string> { $"{nodeRows.Count} structures compared: {over} differ by more than {tol} {L}; {rpt.Flooding.Count} flood in SWMM.", $"{linkRows.Count} pipes compared." };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v) => double.IsFinite(v) ? Math.Round(v, 3) + 0.0 : double.NaN;
}
