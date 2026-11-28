using System.Globalization;
using MasterFlow.Application.Drawing;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Sanitary sewer plan and profile sheets as a DXF drawing (premium). Runs the sanitary sewer design (same input as
/// <see cref="SanitarySewerTool"/>) and draws, with the storm sheets' DXF conventions (R12, model space at true size,
/// stations from 0+00 at the outlet), a plan at the manhole coordinates and a profile of each run with rims, inverts,
/// crowns, manholes, the peak-flow water surface, d/D and velocity labels, and flags for the design exceptions and
/// criteria warnings. The same data comes back as tables and figures.
/// </summary>
public sealed class SanitarySheetsTool : IDesignTool
{
    public string Id => "sanitary-plan-profile-dxf";
    public string Name => "Sanitary sewer plan and profile sheets (DXF)";
    public string Description => "DXF plan and profile sheets of a sanitary sewer design: runs the sanitary network design (Rapid City IDCM §3.12 or Ten States) and draws the plan at the manhole coordinates and profiles from upstream manholes to the outlet with rims, inverts, crowns, manholes, the peak-flow water surface, d/D and velocity at peak, and flags for design exceptions (2 ft/s at peak), d/D, cover, surcharge and manhole drops. R12 DXF for AutoCAD and Civil 3D, split onto sheets at your scales; also reported as tables and figures.";
    public bool UsesNetwork => true;

    private static readonly SanitarySewerTool Design = new();

    public string SampleInput(DesignToolContext context) => """
        # Sanitary sewer plan and profile sheets (DXF). The sheet keys come first; everything after them is the sanitary
        # sewer design input (same keys and tables as "Sanitary sewer network design"). Save… writes the .dxf.
        runs = longest              # longest, all (every upstream manhole), or manhole names: MH-1, MH-4
        horizontal scale = 40       # ft per inch (1" = 40')
        vertical scale = 4          # ft per inch (1" = 4', vertical exaggeration 10)
        sheet = 34 x 22             # in. (ANSI D)
        plan = yes                  # draw the plan view (needs manhole coordinates, e.g. a network from LandXML)
        title = Sanitary sewer plan and profile
        # dxf file = C:\Projects\Sanitary\plan-profile.dxf   # optional: also write the file here

        """ + Design.SampleInput(context);

    private sealed record Sta(string Name, double S, SanitaryManholeResult? M, double? InvIn, double? InvOut, double Lowest);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sanitary sewer plan and profile sheets (DXF)";
        var (dr, design) = Design.Design(input, context);
        var msgs = new List<CalcMessage>(dr.Messages);
        if (design is null) return new(title, [], [], msgs);
        var p = ToolInput.Parse(input);
        double hs = p.Number("horizontal scale", 40, "Horizontal scale"), vs = p.Number("vertical scale", 4, "Vertical scale");
        if (!(hs > 0) || !(vs > 0)) msgs.Add(CalcMessage.Error("E-SP-001", "The horizontal and vertical scales must be above zero."));
        var (sheetW, sheetH) = PlanProfileSheetsTool.Sheet(p.Text("sheet"), true);
        if (!(sheetW > 0) || !(sheetH > 0)) msgs.Add(CalcMessage.Error("E-SP-002", "sheet = width x height in inches, e.g. 34 x 22."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        double paper = hs, ve = hs / vs, txt = 0.1 * paper;
        string drawTitle = p.Text("title") ?? "Sanitary sewer plan and profile";
        string netName = context.Network is { } nn && !p.HasSection("Pipes") ? nn.Name : "Sanitary sewer";

        // Runs
        var heads = design.Heads.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string runsText = (p.Text("runs") ?? "longest").Trim();
        List<string> starts;
        if (runsText.Equals("all", StringComparison.OrdinalIgnoreCase)) starts = heads;
        else if (runsText.Equals("longest", StringComparison.OrdinalIgnoreCase))
            starts = heads.OrderByDescending(h => design.PathToOutlet(h).Sum(x => x.Length)).Take(1).ToList();
        else
        {
            starts = [];
            foreach (var n in runsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (design.Outlet(n) is { } o) starts.Add(o.From);
                else msgs.Add(CalcMessage.Error("E-SP-003", $"Run start \"{n}\" is not a manhole with an outlet sewer."));
        }
        if (starts.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-SP-004", "The design has no sewer run to draw."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var dxf = new DxfWriter();
        dxf.Layer("MF-SAN-PLAN-PIPE", 6); dxf.Layer("MF-SAN-PLAN-MH", 3); dxf.Layer("MF-SAN-PLAN-TEXT", 7); dxf.Layer("MF-SAN-PLAN-FLAG", 1);
        dxf.Layer("MF-SAN-PROF-GRID", 8); dxf.Layer("MF-SAN-PROF-GRID-TEXT", 7); dxf.Layer("MF-SAN-PROF-GROUND", 34); dxf.Layer("MF-SAN-PROF-PIPE", 7);
        dxf.Layer("MF-SAN-PROF-MH", 3); dxf.Layer("MF-SAN-PROF-WS", 5); dxf.Layer("MF-SAN-PROF-TEXT", 7); dxf.Layer("MF-SAN-PROF-FLAG", 1); dxf.Layer("MF-SHEET", 7);

        // Flags: every pipe and manhole issue, with its text for the sheet and the table.
        var pipeFlags = design.Pipes.ToDictionary(x => x.Name, PipeFlags, StringComparer.OrdinalIgnoreCase);
        var flagRows = new List<IReadOnlyList<object?>>();

        // Plan view at true coordinates.
        bool drawPlan = p.Flag("plan", true);
        var at = design.Manholes.Where(m => m.Easting is not null && m.Northing is not null).ToDictionary(m => m.Name, m => (X: m.Easting!.Value, Y: m.Northing!.Value), StringComparer.OrdinalIgnoreCase);
        double planMinY = 0, planMinX = 0;
        if (drawPlan && at.Count > 0)
        {
            double r = 0.06 * paper;
            foreach (var x in design.Pipes)
            {
                if (!at.TryGetValue(x.From, out var a) || !at.TryGetValue(x.To, out var b)) continue;
                dxf.Line("MF-SAN-PLAN-PIPE", a.X, a.Y, b.X, b.Y);
                double ang = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
                if (ang > 90) ang -= 180; else if (ang < -90) ang += 180;
                double nx = -Math.Sin(ang * Math.PI / 180), ny = Math.Cos(ang * Math.PI / 180), mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
                dxf.Text("MF-SAN-PLAN-TEXT", mx + 0.5 * txt * nx, my + 0.5 * txt * ny, txt, F($"{x.Name} {x.DiameterIn:G3}\" L={x.Length:F0} S={x.Slope * 100:F2}%"), 1, ang);
                dxf.Text("MF-SAN-PLAN-TEXT", mx - 1.5 * txt * nx, my - 1.5 * txt * ny, txt, F($"Qpk={x.QpeakGpm:F0} gpm d/D={Dd(x)}"), 1, ang);
                if (pipeFlags[x.Name].Count > 0)
                    dxf.Text("MF-SAN-PLAN-FLAG", mx - 3 * txt * nx, my - 3 * txt * ny, txt, string.Join("; ", pipeFlags[x.Name]).ToUpperInvariant(), 1, ang);
            }
            foreach (var m in design.Manholes)
            {
                if (!at.TryGetValue(m.Name, out var c)) continue;
                dxf.Circle("MF-SAN-PLAN-MH", c.X, c.Y, r);
                dxf.Text("MF-SAN-PLAN-TEXT", c.X + r * 1.5, c.Y + r * 1.5, txt, m.Name);
                if (m.Rim is { } rim) dxf.Text("MF-SAN-PLAN-TEXT", c.X + r * 1.5, c.Y + r * 1.5 - 1.5 * txt, txt, F($"RIM {rim:F2}"));
            }
            planMinY = at.Values.Min(v => v.Y) - 4 * paper; planMinX = at.Values.Min(v => v.X) - 2 * paper;
            int missing = design.Manholes.Count - at.Count;
            if (missing > 0) msgs.Add(CalcMessage.Warning("W-SP-001", $"{missing} manhole(s) have no coordinates; their sewers are left out of the plan view."));
        }
        else if (drawPlan) msgs.Add(CalcMessage.Info("I-SP-001", "No manhole has coordinates (Northing/Easting), so the plan view is left out; select a network imported from LandXML to draw it."));

        double sheetWm = sheetW * paper, sheetHm = sheetH * paper, margin = 1.0 * paper, titleH = 2.0 * paper;
        double usableW = sheetWm - 2 * margin - 1.0 * paper;
        double gap = 2 * margin;
        double sheetX = planMinX, sheetY = planMinY - gap - sheetHm;
        int sheetNo = 0;
        var tables = new List<ToolTable>();
        var figures = new List<ToolFigure>();
        var drawnPipes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var drawnMh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in starts)
        {
            var path = design.PathToOutlet(start);
            if (path.Count == 0) continue;
            var down = Enumerable.Reverse(path).ToList();   // outlet sewer first
            string outName = down[0].To;
            var stations = new List<Sta> { MakeSta(outName, 0, down[0].DownInvert, null) };
            var pipeSta = new List<(SanitaryPipeResult P, double Down, double Up)>();
            double sta = 0;
            foreach (var x in down)
            {
                pipeSta.Add((x, sta, sta + x.Length));
                sta += x.Length;
                var upPipe = path.FirstOrDefault(y => y.To.Equals(x.From, StringComparison.OrdinalIgnoreCase));
                stations.Add(MakeSta(x.From, sta, upPipe?.DownInvert, x.UpInvert));
            }
            Sta MakeSta(string name, double s, double? invIn, double? invOut)
            {
                var m = design.Manhole(name);
                double low = m?.LowestInvert ?? invOut ?? invIn ?? 0;
                return new Sta(name, s, m, invIn, invOut, low);
            }
            double total = sta;
            double zMin = Math.Min(stations.Min(s => s.Lowest), pipeSta.Min(x => x.P.DownInvert));
            double zMax = Math.Max(stations.Max(s => s.M?.Rim ?? double.MinValue), pipeSta.Max(x => x.P.UpInvert + x.P.DiameterFt));
            double eStep = PlanProfileSheetsTool.NiceStep(vs);
            double datum = Math.Floor((zMin - eStep) / eStep) * eStep, top = Math.Ceiling((zMax + 2 * eStep) / eStep) * eStep;
            double profH = (top - datum) * ve, availH = sheetHm - 2 * margin - titleH;
            if (profH > availH) msgs.Add(CalcMessage.Warning("W-SP-002", $"Run from {start}: the profile ({top - datum:F1} ft of elevation) is taller than the sheet at 1\" = {vs:G4}' vertical; use a smaller vertical exaggeration or a larger sheet."));
            double sStep = PlanProfileSheetsTool.NiceStep(hs * 1.25);
            int nSheets = Math.Max(1, (int)Math.Ceiling(total / usableW - 1e-9));
            var sheetOfPipe = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sheetOfMh = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int k = 0; k < nSheets; k++)
            {
                sheetNo++;
                double s0 = k * usableW, s1 = Math.Min(total, s0 + usableW);
                double ox = sheetX + margin + 0.5 * paper - s0, oy = sheetY + margin + titleH + Math.Max(0, (availH - profH) / 2);
                double X(double s) => ox + s;
                double Y(double z) => oy + (z - datum) * ve;
                bool In(double s) => s >= s0 - 1e-6 && s <= s1 + 1e-6;
                dxf.Rectangle("MF-SHEET", sheetX, sheetY, sheetX + sheetWm, sheetY + sheetHm);
                dxf.Rectangle("MF-SHEET", sheetX + margin / 2, sheetY + margin / 2, sheetX + sheetWm - margin / 2, sheetY + sheetHm - margin / 2);
                dxf.Line("MF-SHEET", sheetX + margin / 2, sheetY + margin / 2 + titleH, sheetX + sheetWm - margin / 2, sheetY + margin / 2 + titleH);
                dxf.Text("MF-SAN-PROF-TEXT", sheetX + margin, sheetY + margin / 2 + titleH - 2 * txt, 2 * txt, drawTitle.ToUpperInvariant());
                dxf.Text("MF-SAN-PROF-TEXT", sheetX + margin, sheetY + margin / 2 + titleH - 4.5 * txt, txt, F($"{netName}; run {start} to {outName}; sta {PlanProfileSheetsTool.Sta(s0, true)} to {PlanProfileSheetsTool.Sta(s1, true)}"));
                dxf.Text("MF-SAN-PROF-TEXT", sheetX + margin, sheetY + margin / 2 + titleH - 6.5 * txt, txt,
                    F($"Horizontal 1\" = {hs:G4}'; vertical 1\" = {vs:G4}'. Water surface at peak-flow normal depth. {design.Criteria.Replace("§", "Sec. ")}: d/D <= {design.MaxDd:G2} and V >= {design.MinVelocity:G3} ft/s at {(design.VelocityAtPeak ? "peak" : "full")} flow; cover >= {design.MinCover:G3} ft."));
                dxf.Text("MF-SAN-PROF-TEXT", sheetX + sheetWm - margin, sheetY + margin / 2 + titleH - 2 * txt, 2 * txt, F($"SHEET {sheetNo}"), 2);
                dxf.Text("MF-SAN-PROF-TEXT", sheetX + sheetWm - margin, sheetY + margin / 2 + titleH - 4.5 * txt, txt, "MasterFlow", 2);
                double gx0 = X(s0), gx1 = X(s0 + usableW);
                for (double z = datum; z <= top + 1e-9; z += eStep)
                {
                    dxf.Line("MF-SAN-PROF-GRID", gx0, Y(z), gx1, Y(z));
                    dxf.Text("MF-SAN-PROF-GRID-TEXT", gx0 - 0.5 * txt, Y(z) - 0.5 * txt, txt, F($"{z:F0}"), 2);
                    dxf.Text("MF-SAN-PROF-GRID-TEXT", gx1 + 0.5 * txt, Y(z) - 0.5 * txt, txt, F($"{z:F0}"));
                }
                for (double s = Math.Ceiling(s0 / sStep) * sStep; s <= s0 + usableW + 1e-9; s += sStep)
                {
                    dxf.Line("MF-SAN-PROF-GRID", X(s), Y(datum), X(s), Y(top));
                    dxf.Text("MF-SAN-PROF-GRID-TEXT", X(s), Y(datum) - 2 * txt, txt, PlanProfileSheetsTool.Sta(s, true), 1);
                }
                var rimPts = stations.Where(s => s.M?.Rim is not null).Select(s => (s.S, s.M!.Rim!.Value)).ToList();
                dxf.Polyline("MF-SAN-PROF-GROUND", PlanProfileSheetsTool.Clip(rimPts, s0, s1).Select(q => (X(q.Item1), Y(q.Item2))).ToList());
                foreach (var (x, sd, su) in pipeSta)
                {
                    var inv = PlanProfileSheetsTool.Clip([(sd, x.DownInvert), (su, x.UpInvert)], s0, s1);
                    if (inv.Count < 2) continue;
                    sheetOfPipe.TryAdd(x.Name, sheetNo);
                    dxf.Polyline("MF-SAN-PROF-PIPE", inv.Select(q => (X(q.Item1), Y(q.Item2))).ToList());
                    dxf.Polyline("MF-SAN-PROF-PIPE", inv.Select(q => (X(q.Item1), Y(q.Item2 + x.DiameterFt))).ToList());
                    double depth = x.Surcharged ? x.DiameterFt : x.PeakDepth;
                    dxf.Polyline("MF-SAN-PROF-WS", inv.Select(q => (X(q.Item1), Y(q.Item2 + depth))).ToList());
                    double sm = Math.Clamp((sd + su) / 2, s0, s1), zm = (x.DownInvert + x.UpInvert) / 2;
                    dxf.Text("MF-SAN-PROF-TEXT", X(sm), Y(zm) - 2 * txt, txt, F($"{x.Name} {x.DiameterIn:G3}\" L={x.Length:F1} S={x.Slope * 100:F2}% n={x.N:G3}"), 1);
                    dxf.Text("MF-SAN-PROF-TEXT", X(sm), Y(zm) - 3.6 * txt, txt, F($"Qpk={x.QpeakGpm:F0} gpm V={x.PeakVelocity:F2} fps d/D={Dd(x)}"), 1);
                    var fl = pipeFlags[x.Name];
                    if (fl.Count > 0)
                    {
                        double zf = (Interp(sd, x.DownInvert, su, x.UpInvert, sm)) + depth;
                        dxf.Circle("MF-SAN-PROF-FLAG", X(sm), Y(zf), 3 * txt);
                        for (int i = 0; i < fl.Count; i++)
                            dxf.Text("MF-SAN-PROF-FLAG", X(sm), Y(zm) - (5.4 + 1.6 * i) * txt, txt, fl[i].ToUpperInvariant(), 1);
                    }
                }
                double bw = 0.08 * paper;
                foreach (var s in stations.Where(s => In(s.S)))
                {
                    sheetOfMh.TryAdd(s.Name, sheetNo);
                    double rim = s.M?.Rim ?? s.Lowest + 6;
                    dxf.Rectangle("MF-SAN-PROF-MH", X(s.S) - bw / 2, Y(s.Lowest), X(s.S) + bw / 2, Y(rim));
                    var lines = new List<string> { s.Name };
                    if (s.M?.Rim is { } re) lines.Add(F($"RIM {re:F2}"));
                    if (s.InvIn is { } ii) lines.Add(F($"INV IN {ii:F2}"));
                    if (s.InvOut is { } io) lines.Add(F($"INV OUT {io:F2}"));
                    if (s.M?.Depth is { } dp) lines.Add(F($"DEPTH {dp:F2}"));
                    double ty = Y(rim) + txt;
                    for (int i = lines.Count - 1, j = 0; i >= 0; i--, j++) dxf.Text("MF-SAN-PROF-TEXT", X(s.S), ty + j * 1.5 * txt, txt, lines[i], 1);
                    dxf.Text("MF-SAN-PROF-TEXT", X(s.S), Y(datum) - 3.6 * txt, txt, PlanProfileSheetsTool.Sta(s.S, true), 1);
                    if (s.M is { Flags.Count: > 0 } mf)
                    {
                        dxf.Circle("MF-SAN-PROF-FLAG", X(s.S), Y(s.Lowest), 3 * txt);
                        dxf.Text("MF-SAN-PROF-FLAG", X(s.S) + 3.5 * txt, Y(s.Lowest), txt, string.Join("; ", mf.Flags).ToUpperInvariant());
                    }
                }
                sheetY -= sheetHm + gap;
            }

            // Tables and the report figure for this run.
            tables.Add(new ToolTable($"Profile {start} to {outName}: manholes", ["Station", "Manhole", "Rim (ft)", "Inv in (ft)", "Inv out (ft)", "Depth (ft)", "Structure", "Flags"],
                stations.Select(s => (IReadOnlyList<object?>)new object?[] { PlanProfileSheetsTool.Sta(s.S, true), s.Name, s.M?.Rim, R(s.InvIn), R(s.InvOut), R(s.M?.Depth), s.M is { } m ? $"{m.Structure}{(m.Top.Length > 0 ? ", " + m.Top : "")}" : "", s.M is { Flags.Count: > 0 } f ? string.Join("; ", f.Flags) : "" }).ToList(),
                [$"Stations from 0+00 at {outName}, increasing upstream."]));
            tables.Add(new ToolTable($"Profile {start} to {outName}: sewers", ["Sewer", "Stations", "D (in)", "Length (ft)", "Slope (%)", "Q peak (gpm)", "Peak depth (ft)", "WS up (ft)", "WS down (ft)", "d/D", "V peak (ft/s)", "Cover min (ft)", "Flags"],
                pipeSta.Select(q =>
                {
                    var x = q.P; double dep = x.Surcharged ? x.DiameterFt : x.PeakDepth;
                    double? cov = x.CoverUp is null && x.CoverDown is null ? null : Math.Min(x.CoverUp ?? double.MaxValue, x.CoverDown ?? double.MaxValue);
                    return (IReadOnlyList<object?>)new object?[] { x.Name, $"{PlanProfileSheetsTool.Sta(q.Down, true)} – {PlanProfileSheetsTool.Sta(q.Up, true)}", x.DiameterIn, x.Length, Math.Round(x.Slope * 100, 3),
                        Math.Round(x.QpeakGpm, 1), Math.Round(dep, 3), Math.Round(x.UpInvert + dep, 2), Math.Round(x.DownInvert + dep, 2), x.Surcharged ? double.NaN : Math.Round(x.PeakDd, 3), Math.Round(x.PeakVelocity, 2), R(cov), string.Join("; ", pipeFlags[x.Name]) };
                }).ToList(),
                ["Water surface = invert + normal depth at the design peak flow (Manning); a surcharged sewer is drawn full and flagged."]));
            var series = new List<ToolFigureSeries>
            {
                new("Rim / ground", ToolSeriesKind.Ground, stations.Where(s => s.M?.Rim is not null).Select(s => (s.S, s.M!.Rim!.Value)).ToList()),
                new("Invert", ToolSeriesKind.Limit, pipeSta.SelectMany(q => new[] { (q.Down, q.P.DownInvert), (q.Up, q.P.UpInvert) }).ToList()),
                new("Crown", ToolSeriesKind.Limit, pipeSta.SelectMany(q => new[] { (q.Down, q.P.DownInvert + q.P.DiameterFt), (q.Up, q.P.UpInvert + q.P.DiameterFt) }).ToList()),
                new("Peak water surface", ToolSeriesKind.Water, pipeSta.SelectMany(q => { double d = q.P.Surcharged ? q.P.DiameterFt : q.P.PeakDepth; return new[] { (q.Down, q.P.DownInvert + d), (q.Up, q.P.UpInvert + d) }; }).ToList()),
            };
            figures.Add(new ToolFigure($"Sanitary profile {start} to {outName}", series, "Station (ft)", "Elevation (ft)", ["Stations from 0+00 at the outlet. Water surface at peak-flow normal depth."]));
            foreach (var (x, _, _) in pipeSta)
                if (drawnPipes.Add(x.Name))
                    foreach (var f in pipeFlags[x.Name]) flagRows.Add([x.Name, "sewer", f, sheetOfPipe.TryGetValue(x.Name, out int sn) ? sn : null]);
            foreach (var s in stations)
                if (drawnMh.Add(s.Name) && s.M is { } m)
                    foreach (var f in m.Flags) flagRows.Add([s.Name, "manhole", f, sheetOfMh.TryGetValue(s.Name, out int sn) ? sn : null]);
            sheetY -= gap;
        }
        if (flagRows.Count > 0)
        {
            tables.Add(new ToolTable("Flags on the sheets", ["Item", "Type", "Flag", "Sheet"], flagRows,
                ["Each flag is circled on the profile (layer MF-SAN-PROF-FLAG) and written beside the sewer on the plan (MF-SAN-PLAN-FLAG). \"Design exception\" marks a sewer below the minimum velocity at peak flow whose slope was left as designed (IDCM 3.12.6; see the sanitary design's Design exceptions table)."]));
            msgs.Add(CalcMessage.Warning("W-SP-003", $"{flagRows.Count} flag(s) on the drawn runs (design exceptions or criteria warnings); they are circled on the sheets and listed in \"Flags on the sheets\"."));
        }

        var bytes = dxf.ToBytes();
        string fileName = PlanProfileSheetsTool.Safe($"{netName} sanitary plan and profile.dxf");
        if (p.Text("dxf file") is { } outPath)
        {
            try { File.WriteAllBytes(outPath.Trim('"'), bytes); msgs.Add(CalcMessage.Info("I-SP-002", $"Wrote {outPath.Trim('"')}.")); }
            catch (Exception ex) { msgs.Add(CalcMessage.Error("E-SP-005", $"Could not write {outPath}: {ex.Message}")); }
        }
        msgs.Add(CalcMessage.Info("I-SP-003", "Save… writes the DXF (choose .dxf). Model space is drawn at the horizontal scale: plot each sheet border at 1:1 sheet size."));
        var summary = new List<string>
        {
            F($"{netName}: {starts.Count} profile run(s) on {sheetNo} sheet(s) of {sheetW:G4} × {sheetH:G4} in., horizontal 1\" = {hs:G4}', vertical 1\" = {vs:G4}' (exaggeration {ve:G3})."),
            drawPlan && at.Count > 0 ? $"Plan view of {at.Count} manhole(s) at their coordinates." : "No plan view.",
            $"{flagRows.Count} flag(s) on the sheets.",
            $"DXF: {dxf.EntityCount} entities, {bytes.Length / 1024.0:F0} KB (R12, layers MF-SAN-PLAN-* and MF-SAN-PROF-*).",
        };
        summary.AddRange(dr.Summary);
        return new ToolResult(title, summary, [.. tables, .. dr.Tables], msgs) { Figures = figures, Files = [new ToolFile(fileName, "DXF drawing (sanitary plan and profile sheets)", bytes)] };

        List<string> PipeFlags(SanitaryPipeResult x)
        {
            var f = new List<string>();
            if (x.Surcharged) f.Add("surcharged at peak");
            else if (x.PeakDd > design.MaxDd + 1e-9) f.Add(F($"d/D {x.PeakDd:F2} > {design.MaxDd:G2}"));
            if (x.DesignException) f.Add(F($"design exception: V {x.PeakVelocity:F2} < {design.MinVelocity:G3} fps at peak (needs S={x.SlopeForMinVelocity * 100:F2}%)"));
            else if (x.Issues.Contains("V full")) f.Add(F($"V full < {design.MinVelocity:G3} fps"));
            if (x.Issues.Contains("cover")) f.Add(F($"cover {Math.Min(x.CoverUp ?? 99, x.CoverDown ?? 99):F2} < {design.MinCover:G3} ft"));
            if (x.Issues.Contains("min slope")) f.Add("below Ten States min slope");
            if (x.Issues.Contains("V max")) f.Add("over max velocity");
            if (x.Issues.Contains("spacing")) f.Add("MH spacing");
            if (x.Issues.Contains("anchors")) f.Add("slope over 20%: anchors");
            if (x.Issues.Contains("larger upstream")) f.Add("larger pipe upstream");
            if (x.Issues.Contains("n")) f.Add("n below 0.013");
            if (x.Issues.Contains("min size")) f.Add("below min size");
            return f;
        }
    }

    private static string Dd(SanitaryPipeResult x) => x.Surcharged ? "FULL" : F($"{x.PeakDd:F2}");
    private static double Interp(double s0, double z0, double s1, double z1, double s) => Math.Abs(s1 - s0) < 1e-9 ? z0 : z0 + (z1 - z0) * (s - s0) / (s1 - s0);
    private static double? R(double? v) => v is { } x && double.IsFinite(x) ? Math.Round(x, 2) + 0.0 : null;
    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
