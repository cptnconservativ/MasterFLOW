using System.Globalization;
using System.Text;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Swmm;

/// <summary>A SWMM subcatchment with the parameters MasterFlow writes (U.S. units: ac, ft, %, in, in/h).</summary>
public sealed record SwmmSubcatchment(string Name, string Outlet, double AreaAc, double ImperviousPct, double WidthFt, double SlopePct)
{
    public double NImperv { get; init; } = 0.015;
    public double NPerv { get; init; } = 0.24;
    public double DStoreImperv { get; init; } = 0.05;
    public double DStorePerv { get; init; } = 0.35;
    public double PctZero { get; init; } = 25;
    public double? CurveNumber { get; init; }
    public double Suction { get; init; } = 4.335;
    public double Ks { get; init; } = 0.429;
    public double Deficit { get; init; } = 0.20;
}

/// <summary>Options for writing a .inp file.</summary>
public sealed record SwmmExportOptions
{
    public string Title { get; init; } = "MasterFlow export";
    public double DurationHours { get; init; } = 6;
    public double RoutingStepSec { get; init; } = 5;
    public double ReportStepMin { get; init; } = 1;
    public double EntryLoss { get; init; }
    public double ExitLoss { get; init; }
    /// <summary>Write each structure's inflow as a constant SWMM inflow: "design", "minor" or "none".</summary>
    public string Inflows { get; init; } = "design";
    public double PondedArea { get; init; }
    public IReadOnlyList<SwmmSubcatchment> Subcatchments { get; init; } = [];
    /// <summary>Rainfall intensities (in/h) in equal blocks of <see cref="RainStepMin"/>; empty = no rain gage.</summary>
    public IReadOnlyList<double> RainIntensity { get; init; } = [];
    public double RainStepMin { get; init; } = 5;
    public string RainName { get; init; } = "Design storm";
}

/// <summary>
/// Reads and writes EPA SWMM 5 input files (.inp) for a MasterFlow storm network: junctions, outfalls, circular and
/// elliptical conduits, losses, constant inflows, subcatchments with Green–Ampt or curve-number infiltration, a rain gage
/// and coordinates. Section and field order follow the SWMM 5.2 User's Manual Appendix D.
/// </summary>
public static class SwmmInp
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string F(double v) => double.IsFinite(v) ? Math.Round(v, 4).ToString("0.####", Inv) : "0";

    /// <summary>Invert of each structure: the lowest invert of the pipes that meet it (SWMM offsets are measured from it).</summary>
    public static Dictionary<string, double> NodeInverts(StormNetwork net)
    {
        var inv = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void Low(string node, double z) { if (!inv.TryGetValue(node, out var cur) || z < cur) inv[node] = z; }
        foreach (var p in net.Pipes) { Low(p.From, p.UpstreamInvert); Low(p.To, p.DownstreamInvert); }
        return inv;
    }

    public static string Write(StormNetwork net, StormNetworkResult? result, SwmmExportOptions o, List<CalcMessage> msgs)
    {
        bool si = net.Units == UnitSystem.SI;
        var sb = new StringBuilder();
        var inv = NodeInverts(net);
        var outfalls = net.Structures.Where(s => !net.Pipes.Any(p => Eq(p.From, s.Name)) && net.Pipes.Any(p => Eq(p.To, s.Name))).ToList();
        var junctions = net.Structures.Where(s => inv.ContainsKey(s.Name) && !outfalls.Contains(s)).ToList();
        foreach (var s in net.Structures.Where(s => !inv.ContainsKey(s.Name)))
            msgs.Add(CalcMessage.Warning("W-SW-001", $"Structure {s.Name} has no pipes and is not exported."));

        sb.AppendLine("[TITLE]").AppendLine(";;Project Title/Notes").AppendLine(o.Title)
          .AppendLine("Exported by MasterFlow. Check every value before relying on a SWMM run.").AppendLine();

        var end = new DateTime(2026, 1, 1).AddHours(o.DurationHours);
        sb.AppendLine("[OPTIONS]").AppendLine(";;Option             Value");
        void Opt(string k, string v) => sb.AppendLine($"{k,-20} {v}");
        Opt("FLOW_UNITS", si ? "CMS" : "CFS");
        Opt("INFILTRATION", o.Subcatchments.Any(s => s.CurveNumber is not null) ? "CURVE_NUMBER" : "GREEN_AMPT");
        Opt("FLOW_ROUTING", "DYNWAVE");
        Opt("LINK_OFFSETS", "DEPTH");
        Opt("ALLOW_PONDING", o.PondedArea > 0 ? "YES" : "NO");
        Opt("START_DATE", "01/01/2026"); Opt("START_TIME", "00:00:00");
        Opt("REPORT_START_DATE", "01/01/2026"); Opt("REPORT_START_TIME", "00:00:00");
        Opt("END_DATE", end.ToString("MM/dd/yyyy", Inv)); Opt("END_TIME", end.ToString("HH:mm:ss", Inv));
        Opt("REPORT_STEP", TimeSpan.FromMinutes(o.ReportStepMin).ToString(@"hh\:mm\:ss", Inv));
        Opt("WET_STEP", "00:01:00"); Opt("DRY_STEP", "01:00:00");
        Opt("ROUTING_STEP", F(o.RoutingStepSec));
        Opt("INERTIAL_DAMPING", "PARTIAL"); Opt("NORMAL_FLOW_LIMITED", "BOTH");
        Opt("VARIABLE_STEP", "0.75"); Opt("MAX_TRIALS", "8"); Opt("HEAD_TOLERANCE", si ? "0.0015" : "0.005");
        sb.AppendLine();

        if (o.RainIntensity.Count > 0 && o.Subcatchments.Count > 0)
        {
            var step = TimeSpan.FromMinutes(o.RainStepMin);
            sb.AppendLine("[RAINGAGES]").AppendLine(";;Name  Format  Interval  SCF  Source")
              .AppendLine($"RG1  INTENSITY  {step:hh\\:mm}  1.0  TIMESERIES  STORM").AppendLine();
        }

        if (o.Subcatchments.Count > 0)
        {
            sb.AppendLine("[SUBCATCHMENTS]").AppendLine(";;Name  RainGage  Outlet  Area  %Imperv  Width  %Slope  CurbLen");
            foreach (var s in o.Subcatchments) sb.AppendLine($"{s.Name}  RG1  {s.Outlet}  {F(s.AreaAc)}  {F(s.ImperviousPct)}  {F(s.WidthFt)}  {F(s.SlopePct)}  0");
            sb.AppendLine().AppendLine("[SUBAREAS]").AppendLine(";;Subcatchment  N-Imperv  N-Perv  S-Imperv  S-Perv  PctZero  RouteTo");
            foreach (var s in o.Subcatchments) sb.AppendLine($"{s.Name}  {F(s.NImperv)}  {F(s.NPerv)}  {F(s.DStoreImperv)}  {F(s.DStorePerv)}  {F(s.PctZero)}  OUTLET");
            sb.AppendLine().AppendLine("[INFILTRATION]");
            if (o.Subcatchments.Any(s => s.CurveNumber is not null))
            {
                sb.AppendLine(";;Subcatchment  CurveNum  (unused)  DryTime");
                foreach (var s in o.Subcatchments) sb.AppendLine($"{s.Name}  {F(s.CurveNumber ?? 80)}  0.5  7");
            }
            else
            {
                sb.AppendLine(";;Subcatchment  Suction  Ksat  IMD");
                foreach (var s in o.Subcatchments) sb.AppendLine($"{s.Name}  {F(s.Suction)}  {F(s.Ks)}  {F(s.Deficit)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("[JUNCTIONS]").AppendLine(";;Name  Elevation  MaxDepth  InitDepth  SurDepth  Aponded");
        foreach (var s in junctions)
        {
            double z = inv[s.Name], depth = s.RimElevation is { } rim ? Math.Max(0, rim - z) : 0;
            if (s.RimElevation is null) msgs.Add(CalcMessage.Info("I-SW-001", $"{s.Name} has no rim; MaxDepth 0 lets SWMM use the crown of its highest pipe."));
            sb.AppendLine($"{s.Name}  {F(z)}  {F(depth)}  0  0  {F(o.PondedArea)}");
        }
        sb.AppendLine().AppendLine("[OUTFALLS]").AppendLine(";;Name  Elevation  Type  StageData  Gated  RouteTo");
        foreach (var s in outfalls)
        {
            double z = inv[s.Name];
            string type;
            switch (net.Tailwater)
            {
                case TailwaterRule.FreeOutfall: type = "FREE"; break;
                case TailwaterRule.Elevation when net.TailwaterElevation is { } tw: type = $"FIXED  {F(tw)}"; break;
                default:
                    var sr = result?.Structures.FirstOrDefault(x => Eq(x.Structure.Name, s.Name));
                    if (sr is not null && double.IsFinite(sr.Hgl))
                    {
                        type = $"FIXED  {F(sr.Hgl)}";
                        msgs.Add(CalcMessage.Info("I-SW-002", $"Outfall {s.Name}: the {net.Tailwater} tailwater has no SWMM equivalent; written as a FIXED stage at MasterFlow's outfall HGL, {sr.Hgl:F2}."));
                    }
                    else
                    {
                        type = "FREE";
                        msgs.Add(CalcMessage.Warning("W-SW-002", $"Outfall {s.Name}: the {net.Tailwater} tailwater has no SWMM equivalent and the network is not solved; written as FREE."));
                    }
                    break;
            }
            sb.AppendLine($"{s.Name}  {F(z)}  {type}  NO");
        }

        var pipeQ = result?.Pipes.ToDictionary(p => p.Pipe.Name, p => p.Pipe.Discharge, StringComparer.OrdinalIgnoreCase);
        sb.AppendLine().AppendLine("[CONDUITS]").AppendLine(";;Name  FromNode  ToNode  Length  Roughness  InOffset  OutOffset  InitFlow  MaxFlow");
        foreach (var p in net.Pipes)
            sb.AppendLine($"{p.Name}  {p.From}  {p.To}  {F(p.Length)}  {F(p.ManningN)}  {F(p.UpstreamInvert - inv[p.From])}  {F(p.DownstreamInvert - inv[p.To])}  0  0");
        sb.AppendLine().AppendLine("[XSECTIONS]").AppendLine(";;Link  Shape  Geom1  Geom2  Geom3  Geom4  Barrels  Culvert");
        foreach (var p in net.Pipes)
        {
            string shape = p.Shape == NetworkPipeShape.Circular ? $"CIRCULAR  {F(p.Rise)}  0" :
                p.Span >= p.Rise ? $"HORIZ_ELLIPSE  {F(p.Rise)}  {F(p.Span)}" : $"VERT_ELLIPSE  {F(p.Rise)}  {F(p.Span)}";
            sb.AppendLine($"{p.Name}  {shape}  0  0  1");
        }

        // Losses: the network's entry/exit K, plus a standard-K structure's K as the entry loss of the pipe leaving it.
        var loss = new List<string>();
        foreach (var p in net.Pipes)
        {
            var from = net.Structures.FirstOrDefault(s => Eq(s.Name, p.From));
            double kin = from is { LossMethod: StructureLossMethod.StandardK } ? from.JunctionK ?? net.DefaultJunctionK : o.EntryLoss;
            if (kin > 0 || o.ExitLoss > 0) loss.Add($"{p.Name}  {F(kin)}  {F(o.ExitLoss)}  0  NO  0");
        }
        if (loss.Count > 0) { sb.AppendLine().AppendLine("[LOSSES]").AppendLine(";;Link  Kentry  Kexit  Kavg  FlapGate  Seepage"); loss.ForEach(l => sb.AppendLine(l)); }
        if (net.Structures.Any(s => s.LossMethod is not (StructureLossMethod.StandardK)) && net.Pipes.Count > 0)
            msgs.Add(CalcMessage.Info("I-SW-003", "IDCM Figure 4-6/4-7 structure losses (hj = V2²/2g − c·V1²/2g) have no SWMM equivalent. SWMM applies the entry and exit K you give to each conduit; standard-K structures carry their K as the entry loss of the pipe leaving them."));

        if (!o.Inflows.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            bool minor = o.Inflows.Equals("minor", StringComparison.OrdinalIgnoreCase);
            var flows = new List<(string Node, double Q)>();
            // Pipe flows (typed or accumulated by the solver): the node inflow is what leaves the node less what enters it.
            var src = result?.Pipes.Select(x => x.Pipe).ToList() ?? net.Pipes.ToList();
            double PQ(NetworkPipe x) => minor ? x.MinorDischarge : x.Discharge;
            bool fromPipes = net.Structures.All(st => (minor ? st.MinorInflow : st.Inflow) is null) && src.Any(x => double.IsFinite(PQ(x)) && PQ(x) > 0);
            foreach (var st in junctions)
            {
                double q;
                if (fromPipes)
                {
                    double outQ = src.Where(x => Eq(x.From, st.Name)).Sum(x => double.IsFinite(PQ(x)) ? PQ(x) : 0);
                    double inQ = src.Where(x => Eq(x.To, st.Name)).Sum(x => double.IsFinite(PQ(x)) ? PQ(x) : 0);
                    q = outQ - inQ;
                    if (q < -1e-9) msgs.Add(CalcMessage.Warning("W-SW-005", $"{st.Name}: the pipe leaving carries {outQ:G4}, less than the {inQ:G4} entering; no inflow is written there."));
                }
                else q = (minor ? st.MinorInflow : st.Inflow) ?? 0;
                if (q > 1e-9) flows.Add((st.Name, q));
            }
            if (fromPipes && flows.Count > 0) msgs.Add(CalcMessage.Info("I-SW-005", "The network's flows are on its pipes; each node's SWMM inflow is the flow of the pipe leaving it less the pipes entering it, so SWMM carries the same pipe flows."));
            if (flows.Count > 0)
            {
                sb.AppendLine().AppendLine("[INFLOWS]").AppendLine(";;Node  Constituent  TimeSeries  Type  Mfactor  Sfactor  Baseline  Pattern");
                foreach (var (node, q) in flows) sb.AppendLine($"{node}  FLOW  \"\"  FLOW  1.0  1.0  {F(q)}");
            }
            else msgs.Add(CalcMessage.Warning("W-SW-003", "No flows to write: give structure inflows, pipe flows or subcatchments."));
        }
        _ = pipeQ;

        if (o.RainIntensity.Count > 0 && o.Subcatchments.Count > 0)
        {
            sb.AppendLine().AppendLine("[TIMESERIES]").AppendLine($";;{o.RainName}: intensity (in/h) at the start of each {F(o.RainStepMin)}-min block");
            for (int i = 0; i < o.RainIntensity.Count; i++) sb.AppendLine($"STORM  {F(i * o.RainStepMin / 60)}  {F(o.RainIntensity[i])}");
            sb.AppendLine($"STORM  {F(o.RainIntensity.Count * o.RainStepMin / 60)}  0");
        }

        sb.AppendLine().AppendLine("[REPORT]").AppendLine("INPUT NO").AppendLine("CONTROLS NO").AppendLine("SUBCATCHMENTS ALL").AppendLine("NODES ALL").AppendLine("LINKS ALL");

        // Coordinates: survey northing/easting; a structure without them is laid out on a line so SWMM can draw it.
        sb.AppendLine().AppendLine("[COORDINATES]").AppendLine(";;Node  X-Coord  Y-Coord");
        int k = 0; bool missing = false;
        var nodes = junctions.Concat(outfalls).ToList();
        foreach (var s in nodes)
        {
            if (s.Easting is { } e && s.Northing is { } n) sb.AppendLine($"{s.Name}  {F(e)}  {F(n)}");
            else { sb.AppendLine($"{s.Name}  {F(100 * k)}  0"); missing = true; }
            k++;
        }
        if (missing) msgs.Add(CalcMessage.Info("I-SW-004", "Structures without northing/easting are placed 100 units apart on a line in [COORDINATES]; the map is schematic."));
        if (o.Subcatchments.Count > 0)
        {
            sb.AppendLine().AppendLine("[Polygons]").AppendLine(";;Subcatchment  X-Coord  Y-Coord");
            foreach (var s in o.Subcatchments)
            {
                var outlet = nodes.FirstOrDefault(x => Eq(x.Name, s.Outlet));
                double x0 = outlet?.Easting ?? 0, y0 = (outlet?.Northing ?? 0) + 50;
                sb.AppendLine($"{s.Name}  {F(x0 - 25)}  {F(y0)}").AppendLine($"{s.Name}  {F(x0 + 25)}  {F(y0)}").AppendLine($"{s.Name}  {F(x0 + 25)}  {F(y0 + 50)}").AppendLine($"{s.Name}  {F(x0 - 25)}  {F(y0 + 50)}");
            }
        }
        return sb.ToString();
    }

    /// <summary>A parsed .inp file: sections of whitespace-separated tokens, comments removed (quoted "" kept as a token).</summary>
    public static Dictionary<string, List<string[]>> Sections(string text)
    {
        var d = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        List<string[]>? cur = null;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            int c = raw.IndexOf(';');
            var line = (c >= 0 ? raw[..c] : raw).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { cur = []; d[line[1..^1].Trim()] = cur; continue; }
            cur?.Add(line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
        return d;
    }

    private static bool Num(string[] r, int i, out double v) { v = 0; return i < r.Length && double.TryParse(r[i], NumberStyles.Float, Inv, out v); }

    /// <summary>
    /// Builds a storm network from a .inp file: junctions and storage units (rim = invert + max depth), outfalls (FIXED
    /// stage → tailwater elevation, otherwise a free outfall), conduits with circular, force-main and elliptical sections,
    /// constant inflows and coordinates. Other link and section types are reported and left out.
    /// </summary>
    public static StormNetwork? Read(string text, StormNetwork template, List<CalcMessage> msgs)
    {
        var sec = Sections(text);
        if (!sec.ContainsKey("CONDUITS"))
        {
            msgs.Add(CalcMessage.Error("E-SW-010", "The text has no [CONDUITS] section; it is not a SWMM .inp file."));
            return null;
        }
        string units = sec.TryGetValue("OPTIONS", out var opts) ? opts.FirstOrDefault(r => r.Length > 1 && r[0].Equals("FLOW_UNITS", StringComparison.OrdinalIgnoreCase))?[1].ToUpperInvariant() ?? "CFS" : "CFS";
        bool fileSi = units is "CMS" or "LPS" or "MLD";
        if (fileSi != (template.Units == UnitSystem.SI))
        {
            msgs.Add(CalcMessage.Error("E-SW-011", $"The file is in {(fileSi ? "SI" : "U.S.")} units (FLOW_UNITS {units}) and the project is not; open it in a project with the same unit system."));
            return null;
        }
        double qf = units switch { "GPM" => 1 / 448.831, "MGD" => 1.54723, "LPS" => 0.001, "MLD" => 0.0115741, _ => 1.0 };
        bool elevOffsets = opts?.Any(r => r.Length > 1 && r[0].Equals("LINK_OFFSETS", StringComparison.OrdinalIgnoreCase) && r[1].Equals("ELEVATION", StringComparison.OrdinalIgnoreCase)) == true;

        var nodes = new Dictionary<string, NetworkStructure>(StringComparer.OrdinalIgnoreCase);
        var inv = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in sec.GetValueOrDefault("JUNCTIONS") ?? [])
            if (Num(r, 1, out double z))
            {
                Num(r, 2, out double md);
                inv[r[0]] = z;
                nodes[r[0]] = new NetworkStructure { Name = r[0], Kind = "Manhole", RimElevation = md > 0 ? z + md : null };
            }
        foreach (var r in sec.GetValueOrDefault("STORAGE") ?? [])
            if (Num(r, 1, out double z))
            {
                Num(r, 2, out double md);
                inv[r[0]] = z;
                nodes[r[0]] = new NetworkStructure { Name = r[0], Kind = "Storage", RimElevation = md > 0 ? z + md : null };
                msgs.Add(CalcMessage.Warning("W-SW-010", $"Storage unit {r[0]} is read as a structure; its storage curve is not used (route ponds with the detention or hydrograph tools)."));
            }
        var tailwater = template.Tailwater; double? twElev = template.TailwaterElevation;
        var fixedStages = new List<double>();
        foreach (var r in sec.GetValueOrDefault("OUTFALLS") ?? [])
            if (Num(r, 1, out double z))
            {
                inv[r[0]] = z;
                nodes[r[0]] = new NetworkStructure { Name = r[0], Kind = "Outfall" };
                string type = r.Length > 2 ? r[2].ToUpperInvariant() : "FREE";
                if (type == "FIXED" && Num(r, 3, out double st)) fixedStages.Add(st);
                else if (type is "TIDAL" or "TIMESERIES") msgs.Add(CalcMessage.Warning("W-SW-011", $"Outfall {r[0]}: a {type} boundary is not modelled; set the tailwater by hand."));
            }
        if (fixedStages.Count > 0)
        {
            tailwater = TailwaterRule.Elevation; twElev = fixedStages.Max();
            if (fixedStages.Distinct().Count() > 1) msgs.Add(CalcMessage.Warning("W-SW-012", $"Outfalls have different FIXED stages; the highest, {twElev:F2}, is used for the network."));
        }
        else if ((sec.GetValueOrDefault("OUTFALLS") ?? []).Count > 0) tailwater = TailwaterRule.FreeOutfall;
        foreach (var name in new[] { "DIVIDERS" })
            foreach (var r in sec.GetValueOrDefault(name) ?? [])
            {
                if (Num(r, 1, out double z)) { inv[r[0]] = z; nodes[r[0]] = new NetworkStructure { Name = r[0], Kind = "Divider" }; }
                msgs.Add(CalcMessage.Warning("W-SW-013", $"Divider {r[0]} is read as a plain structure; its flow split is not modelled."));
            }

        var xs = (sec.GetValueOrDefault("XSECTIONS") ?? []).Where(r => r.Length > 2).ToDictionary(r => r[0], r => r, StringComparer.OrdinalIgnoreCase);
        var pipes = new List<NetworkPipe>();
        foreach (var r in sec["CONDUITS"])
        {
            if (r.Length < 5 || !Num(r, 3, out double len) || !Num(r, 4, out double n)) { msgs.Add(CalcMessage.Warning("W-SW-014", $"Conduit line \"{string.Join(' ', r)}\" could not be read.")); continue; }
            Num(r, 5, out double off1); Num(r, 6, out double off2);
            if (!inv.TryGetValue(r[1], out double z1) || !inv.TryGetValue(r[2], out double z2)) { msgs.Add(CalcMessage.Warning("W-SW-015", $"Conduit {r[0]}: node {r[1]} or {r[2]} is not a junction, outfall or storage unit; left out.")); continue; }
            if (!xs.TryGetValue(r[0], out var x)) { msgs.Add(CalcMessage.Warning("W-SW-016", $"Conduit {r[0]} has no cross-section; left out.")); continue; }
            string shape = x[1].ToUpperInvariant();
            Num(x, 2, out double g1); Num(x, 3, out double g2);
            double barrels = Num(x, 6, out double b) && b > 1 ? b : 1;
            NetworkPipeShape ps; double rise = g1, span = g1;
            switch (shape)
            {
                case "CIRCULAR" or "FORCE_MAIN" or "FILLED_CIRCULAR": ps = NetworkPipeShape.Circular;
                    if (shape != "CIRCULAR") msgs.Add(CalcMessage.Info("I-SW-010", $"Conduit {r[0]}: {shape} is read as a plain circular pipe."));
                    break;
                case "HORIZ_ELLIPSE" or "VERT_ELLIPSE": ps = NetworkPipeShape.Elliptical; span = g2 > 0 ? g2 : g1; break;
                default:
                    msgs.Add(CalcMessage.Warning("W-SW-017", $"Conduit {r[0]}: shape {shape} is not a MasterFlow network shape (circular or elliptical); left out."));
                    continue;
            }
            if (barrels > 1) msgs.Add(CalcMessage.Warning("W-SW-018", $"Conduit {r[0]} has {barrels} barrels; MasterFlow networks carry one, so only one is read."));
            pipes.Add(new NetworkPipe
            {
                Name = r[0], From = r[1], To = r[2], Shape = ps, Rise = rise, Span = span, Length = len, ManningN = n,
                UpstreamInvert = elevOffsets ? off1 : z1 + off1, DownstreamInvert = elevOffsets ? off2 : z2 + off2, Discharge = double.NaN,
            });
        }
        foreach (var kind in new[] { "PUMPS", "ORIFICES", "WEIRS", "OUTLETS" })
            if (sec.GetValueOrDefault(kind) is { Count: > 0 } l) msgs.Add(CalcMessage.Warning("W-SW-019", $"{l.Count} {kind.ToLowerInvariant()} not read: a MasterFlow storm network has pipes only."));

        foreach (var r in sec.GetValueOrDefault("INFLOWS") ?? [])
        {
            if (r.Length < 2 || !r[1].Equals("FLOW", StringComparison.OrdinalIgnoreCase) || !nodes.TryGetValue(r[0], out var s)) continue;
            Num(r, 4, out double mf); Num(r, 6, out double baseline);
            string ts = r.Length > 2 ? r[2].Trim('"') : "";
            double peak = 0;
            if (ts.Length > 0 && sec.TryGetValue("TIMESERIES", out var tsl))
            {
                var vals = tsl.Where(t => t[0].Equals(ts, StringComparison.OrdinalIgnoreCase)).Select(t => Num(t, t.Length - 1, out double v) ? v : 0).ToList();
                peak = vals.Count > 0 ? vals.Max() : 0;
                msgs.Add(CalcMessage.Info("I-SW-011", $"{r[0]}: inflow time series {ts} is read as its peak ({peak * (mf == 0 ? 1 : mf):G4}) plus the baseline."));
            }
            double q = (baseline + peak * (mf == 0 ? 1 : mf)) * qf;
            nodes[r[0]] = s with { Inflow = (s.Inflow ?? 0) + q };
        }
        if (sec.ContainsKey("SUBCATCHMENTS")) msgs.Add(CalcMessage.Info("I-SW-012", "Subcatchments are not read into the network; use the hydrograph routing or rational method tools for inflows."));
        foreach (var r in sec.GetValueOrDefault("COORDINATES") ?? [])
            if (nodes.TryGetValue(r[0], out var s) && Num(r, 1, out double x) && Num(r, 2, out double y)) nodes[r[0]] = s with { Easting = x, Northing = y };

        // Junctions get the solver's default structure loss method; SWMM conduit entry and exit K are reported, not converted.
        if (sec.GetValueOrDefault("LOSSES") is { Count: > 0 }) msgs.Add(CalcMessage.Info("I-SW-013", "SWMM conduit losses are not converted; MasterFlow applies the IDCM structure losses (set them per structure)."));
        if (pipes.Count == 0) { msgs.Add(CalcMessage.Error("E-SW-012", "No conduit could be read.")); return null; }
        return template with
        {
            Structures = nodes.Values.Where(s => pipes.Any(p => Eq(p.From, s.Name) || Eq(p.To, s.Name))).ToList(),
            Pipes = pipes, Tailwater = tailwater, TailwaterElevation = twElev,
        };
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Peak results read from a SWMM 5 report file (.rpt).</summary>
public sealed record SwmmReport(
    IReadOnlyDictionary<string, (double MaxDepth, double MaxHgl)> Nodes,
    IReadOnlyDictionary<string, (double MaxFlow, double MaxVelocity, double MaxOverFullFlow, double MaxOverFullDepth)> Links,
    IReadOnlyDictionary<string, (double Hours, double MaxRate)> Flooding,
    IReadOnlyList<string> Errors)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Reads the Node Depth, Node Flooding and Link Flow summaries; tables run from the second dashed line to a blank line.</summary>
    public static SwmmReport Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var nodes = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
        var links = new Dictionary<string, (double, double, double, double)>(StringComparer.OrdinalIgnoreCase);
        var flood = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
        var errors = lines.Where(l => l.TrimStart().StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)).Select(l => l.Trim()).ToList();
        foreach (var (title, rows) in Tables(lines))
        {
            foreach (var t in rows)
            {
                bool N(int i, out double v) { v = double.NaN; return i < t.Length && double.TryParse(t[i], NumberStyles.Float, Inv, out v); }
                if (title.StartsWith("Node Depth Summary", StringComparison.OrdinalIgnoreCase) && N(3, out double md) && N(4, out double hgl)) nodes[t[0]] = (md, hgl);
                else if (title.StartsWith("Node Flooding Summary", StringComparison.OrdinalIgnoreCase) && N(1, out double hrs) && N(2, out double rate)) flood[t[0]] = (hrs, rate);
                else if (title.StartsWith("Link Flow Summary", StringComparison.OrdinalIgnoreCase) && N(2, out double q))
                {
                    N(5, out double v); N(6, out double ff); N(7, out double fd);
                    links[t[0]] = (q, v, ff, fd);
                }
            }
        }
        return new(nodes, links, flood, errors);
    }

    private static IEnumerable<(string Title, List<string[]> Rows)> Tables(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("****")) continue;
            if (i + 2 >= lines.Length || !lines[i + 2].TrimStart().StartsWith("****")) continue;
            string title = lines[i + 1].Trim();
            int dashes = 0, j = i + 3;
            for (; j < lines.Length && dashes < 2; j++) if (lines[j].TrimStart().StartsWith("-----")) dashes++;
            var rows = new List<string[]>();
            for (; j < lines.Length && lines[j].Trim().Length > 0 && !lines[j].TrimStart().StartsWith("****"); j++)
                rows.Add(lines[j].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            yield return (title, rows);
            i = j - 1;
        }
    }
}
