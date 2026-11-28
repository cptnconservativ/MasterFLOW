using System.Globalization;
using MasterFlow.Application.Drawing;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Plan and profile sheets of the selected storm network as a DXF drawing (premium): the plan at the structures'
/// coordinates and, for each run from an upstream structure to the outfall, a profile with rim, pipes, structures,
/// the HGL and EGL from the network solution, the IDCM §4.6.7 clearance check, a grid and a title block, split onto
/// sheets at the chosen scales. The same profile data comes back as tables and report figures.
/// </summary>
public sealed class PlanProfileSheetsTool : IDesignTool
{
    public string Id => "plan-profile-dxf";
    public string Name => "Plan and profile sheets (DXF) with HGL and EGL";
    public string Description => "DXF plan and profile sheets of the selected storm network: plan view at the structure coordinates; profiles from upstream structures to the outfall with rim, inverts and crowns, structures, the HGL and EGL from the solution and the Rapid City HGL clearance check, a station/elevation grid and a title block, split onto sheets at your horizontal and vertical scales. Opens in AutoCAD and Civil 3D (R12 DXF). Also reports the profile data as tables and figures.";
    public bool UsesNetwork => true;

    public string SampleInput(DesignToolContext context)
    {
        bool si = context.Network?.Units == UnitSystem.SI || (context.Network is null && context.Units == UnitSystem.SI);
        return si ? """
            # Plan and profile sheets (DXF) of the selected storm network. Save… writes the .dxf.
            runs = longest              # longest, all, or upstream structure names separated by commas
            horizontal scale = 500      # 1:500
            vertical scale = 50         # 1:50 (vertical exaggeration 10)
            sheet = 841 x 594           # mm (A1)
            plan = yes
            title = Storm sewer plan and profile
            # dxf file = C:\Projects\Storm\plan-profile.dxf   # optional: also write the file here
            """ : """
            # Plan and profile sheets (DXF) of the selected storm network, with the HGL and EGL from the solution.
            # Save… writes the .dxf (model space, plotted at the horizontal scale). Profiles are stationed from 0+00 at the
            # outfall, increasing upstream.
            runs = longest              # longest, all (every beginning-of-line structure), or names: CB-3, CB-4
            horizontal scale = 40       # ft per inch (1" = 40')
            vertical scale = 4          # ft per inch (1" = 4', vertical exaggeration 10)
            sheet = 34 x 22             # in. (ANSI D)
            plan = yes                  # draw the plan view (needs structure coordinates)
            title = Storm sewer plan and profile
            # dxf file = C:\Projects\Storm\plan-profile.dxf   # optional: also write the file here
            """;
    }

    private sealed record Station(string Name, double Sta, NetworkStructure S, StructureHydraulicResult? R, double? InvOut, double? InvIn, double Lowest);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Plan and profile sheets (DXF)";
        if (context.Network is not { } net || context.NetworkResult is not { Succeeded: true } res)
            return ToolResult.Failed(title, CalcMessage.Error("E-PP-001", "Select a storm network and solve it (Storm networks window) before drawing the sheets."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);
        bool us = net.Units == UnitSystem.USCustomary;
        string L = us ? "ft" : "m";
        double hs = p.Number("horizontal scale", us ? 40 : 500, "Horizontal scale"), vs = p.Number("vertical scale", us ? 4 : 50, "Vertical scale");
        if (!(hs > 0) || !(vs > 0)) msgs.Add(CalcMessage.Error("E-PP-002", "The horizontal and vertical scales must be above zero."));
        var (sheetW, sheetH) = Sheet(p.Text("sheet"), us);
        if (!(sheetW > 0) || !(sheetH > 0)) msgs.Add(CalcMessage.Error("E-PP-003", "sheet = width x height (in. or mm), e.g. 34 x 22."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);
        double paper = us ? hs : hs / 1000;             // model units per paper unit (in. or mm), horizontal
        double ve = us ? hs / vs : hs / vs;             // vertical exaggeration
        double txt = (us ? 0.1 : 2.5) * paper;          // 0.1 in. (2.5 mm) text on paper
        string drawTitle = p.Text("title") ?? "Storm sewer plan and profile";

        // Runs
        var pipesFrom = net.Pipes.GroupBy(x => x.From, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var heads = net.Structures.Where(s => pipesFrom.ContainsKey(s.Name) && !net.Pipes.Any(x => x.To.Equals(s.Name, StringComparison.OrdinalIgnoreCase))).Select(s => s.Name).ToList();
        string runsText = (p.Text("runs") ?? "longest").Trim();
        List<string> starts;
        if (runsText.Equals("all", StringComparison.OrdinalIgnoreCase)) starts = heads;
        else if (runsText.Equals("longest", StringComparison.OrdinalIgnoreCase))
            starts = heads.OrderByDescending(h => res.PathToOutfall(h).Sum(x => x.Pipe.Length)).Take(1).ToList();
        else
        {
            starts = [];
            foreach (var n in runsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var s = net.Structures.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                if (s is null || !pipesFrom.ContainsKey(s.Name)) msgs.Add(CalcMessage.Error("E-PP-004", $"Run start \"{n}\" is not a structure with an outlet pipe in network \"{net.Name}\"."));
                else starts.Add(s.Name);
            }
        }
        if (starts.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) msgs.Add(CalcMessage.Error("E-PP-005", "The network has no pipe run to draw."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var dxf = new DxfWriter();
        dxf.Layer("MF-PLAN-PIPE", 5); dxf.Layer("MF-PLAN-STRUCT", 3); dxf.Layer("MF-PLAN-TEXT", 7);
        dxf.Layer("MF-PROF-GRID", 8); dxf.Layer("MF-PROF-GRID-TEXT", 7); dxf.Layer("MF-PROF-GROUND", 34); dxf.Layer("MF-PROF-PIPE", 7);
        dxf.Layer("MF-PROF-STRUCT", 3); dxf.Layer("MF-PROF-HGL", 5); dxf.Layer("MF-PROF-EGL", 1, "DASHED"); dxf.Layer("MF-PROF-TEXT", 7);
        dxf.Layer("MF-PROF-FLAG", 1); dxf.Layer("MF-SHEET", 7);

        // Plan view at true coordinates.
        bool drawPlan = p.Flag("plan", true);
        var at = net.Structures.Where(s => s.Easting is not null && s.Northing is not null).ToDictionary(s => s.Name, s => (X: s.Easting!.Value, Y: s.Northing!.Value), StringComparer.OrdinalIgnoreCase);
        double planMinY = 0, planMinX = 0;
        if (drawPlan && at.Count > 0)
        {
            double r = 0.06 * paper;
            foreach (var x in net.Pipes)
            {
                if (!at.TryGetValue(x.From, out var a) || !at.TryGetValue(x.To, out var b)) continue;
                dxf.Line("MF-PLAN-PIPE", a.X, a.Y, b.X, b.Y);
                double ang = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
                if (ang > 90) ang -= 180; else if (ang < -90) ang += 180;
                var pr = res.Pipe(x.Name);
                // Labels above and below the pipe, offset along its normal.
                double nx = -Math.Sin(ang * Math.PI / 180), ny = Math.Cos(ang * Math.PI / 180), mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
                dxf.Text("MF-PLAN-TEXT", mx + 0.5 * txt * nx, my + 0.5 * txt * ny, txt, F($"{x.Name} {Size(x, us)} L={x.Length:F0} S={Slope(x):F2}%"), 1, ang);
                if (pr is not null) dxf.Text("MF-PLAN-TEXT", mx - 1.5 * txt * nx, my - 1.5 * txt * ny, txt, F($"Q={Q(pr):F1} {(us ? "cfs" : "m3/s")}"), 1, ang);
            }
            foreach (var s in net.Structures)
            {
                if (!at.TryGetValue(s.Name, out var c)) continue;
                dxf.Circle("MF-PLAN-STRUCT", c.X, c.Y, r);
                dxf.Text("MF-PLAN-TEXT", c.X + r * 1.5, c.Y + r * 1.5, txt, s.Name);
                if (s.RimElevation is { } rim) dxf.Text("MF-PLAN-TEXT", c.X + r * 1.5, c.Y + r * 1.5 - 1.5 * txt, txt, F($"RIM {rim:F2}"));
            }
            planMinY = at.Values.Min(v => v.Y) - 4 * paper; planMinX = at.Values.Min(v => v.X) - 2 * paper;
            int missing = net.Structures.Count - at.Count;
            if (missing > 0) msgs.Add(CalcMessage.Warning("W-PP-001", $"{missing} structure(s) have no coordinates; their pipes are left out of the plan view."));
        }
        else if (drawPlan) msgs.Add(CalcMessage.Info("I-PP-001", "No structure has coordinates (Northing/Easting), so the plan view is left out; import the network from LandXML or enter coordinates to draw it."));

        // Profiles, each split onto sheets.
        double sheetWm = sheetW * paper, sheetHm = sheetH * paper, margin = (us ? 1.0 : 25) * paper, titleH = (us ? 2.0 : 50) * paper;
        double usableW = sheetWm - 2 * margin - (us ? 1.0 : 25) * paper;  // room for elevation labels on both sides
        double gap = 2 * margin;
        double sheetX = planMinX, sheetY = planMinY - gap - sheetHm;
        int sheetNo = 0;
        var tables = new List<ToolTable>();
        var figures = new List<ToolFigure>();
        int flags = 0;
        double clearReq = net.Criteria.HglBelowRim ?? double.NaN;
        foreach (var start in starts)
        {
            var path = res.PathToOutfall(start);
            if (path.Count == 0) continue;
            // Stations from the outfall (0) upstream.
            var stations = new List<Station>();
            double sta = 0;
            var down = path.Reverse().ToList();   // outfall pipe first
            var outName = down[0].Pipe.To;
            stations.Add(MakeStation(outName, 0, null, down[0].Pipe.DownstreamInvert));
            var pipeSta = new List<(PipeHydraulicResult P, double Down, double Up)>();
            foreach (var pr in down)
            {
                pipeSta.Add((pr, sta, sta + pr.Pipe.Length));
                sta += pr.Pipe.Length;
                var upPipe = path.FirstOrDefault(x => x.Pipe.To.Equals(pr.Pipe.From, StringComparison.OrdinalIgnoreCase));
                stations.Add(MakeStation(pr.Pipe.From, sta, pr.Pipe.UpstreamInvert, upPipe?.Pipe.DownstreamInvert));
            }
            Station MakeStation(string name, double s, double? invOut, double? invIn)
            {
                var ns = net.Structures.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? new NetworkStructure { Name = name };
                // Lowest invert of every pipe at the structure (laterals too).
                var all = net.Pipes.Where(x => x.From.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(x => x.UpstreamInvert)
                    .Concat(net.Pipes.Where(x => x.To.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(x => x.DownstreamInvert)).DefaultIfEmpty(invOut ?? invIn ?? 0).Min();
                return new Station(name, s, ns, res.Structure(name), invOut, invIn, all);
            }
            // The outfall's inflow invert is the downstream invert of the first pipe.
            stations[0] = stations[0] with { InvIn = down[0].Pipe.DownstreamInvert, InvOut = null };

            double total = sta;
            double zMin = Math.Min(stations.Min(s => s.Lowest), pipeSta.Min(x => Math.Min(x.P.Pipe.DownstreamInvert, x.P.Pipe.UpstreamInvert)));
            double zMax = new[] { stations.Max(s => s.S.RimElevation ?? double.MinValue), stations.Max(s => s.R?.Egl ?? double.MinValue), pipeSta.Max(x => Math.Max(x.P.EglUp, x.P.EglDown)),
                pipeSta.Max(x => Math.Max(x.P.Pipe.UpstreamInvert, x.P.Pipe.DownstreamInvert) + x.P.Pipe.Rise) }.Max();
            double eStep = NiceStep(vs);
            double datum = Math.Floor((zMin - eStep) / eStep) * eStep, top = Math.Ceiling((zMax + eStep) / eStep) * eStep;
            double profH = (top - datum) * ve;
            double availH = sheetHm - 2 * margin - titleH;
            if (profH > availH) msgs.Add(CalcMessage.Warning("W-PP-002", $"Run from {start}: the profile ({top - datum:F1} {L} of elevation) is taller than the sheet at 1\" = {vs:G4}' vertical; use a smaller vertical exaggeration or a larger sheet."));
            double sStep = NiceStep(hs * (us ? 1 : 0.02) * 1.25);
            int nSheets = Math.Max(1, (int)Math.Ceiling(total / usableW - 1e-9));

            for (int k = 0; k < nSheets; k++)
            {
                sheetNo++;
                double s0 = k * usableW, s1 = Math.Min(total, s0 + usableW);
                double ox = sheetX + margin + 0.5 * (us ? 1.0 : 25) * paper - s0, oy = sheetY + margin + titleH + Math.Max(0, (availH - profH) / 2);   // model x of station 0, y of datum (profile centred on the sheet)
                double X(double s) => ox + s;
                double Y(double z) => oy + (z - datum) * ve;
                bool In(double s) => s >= s0 - 1e-6 && s <= s1 + 1e-6;
                // Border and title block
                dxf.Rectangle("MF-SHEET", sheetX, sheetY, sheetX + sheetWm, sheetY + sheetHm);
                dxf.Rectangle("MF-SHEET", sheetX + margin / 2, sheetY + margin / 2, sheetX + sheetWm - margin / 2, sheetY + sheetHm - margin / 2);
                dxf.Line("MF-SHEET", sheetX + margin / 2, sheetY + margin / 2 + titleH, sheetX + sheetWm - margin / 2, sheetY + margin / 2 + titleH);
                dxf.Text("MF-PROF-TEXT", sheetX + margin, sheetY + margin / 2 + titleH - 2 * txt, 2 * txt, drawTitle.ToUpperInvariant());
                dxf.Text("MF-PROF-TEXT", sheetX + margin, sheetY + margin / 2 + titleH - 4.5 * txt, txt, F($"Network {net.Name}; run {start} to {outName}; sta {Sta(s0, us)} to {Sta(s1, us)}"));
                dxf.Text("MF-PROF-TEXT", sheetX + margin, sheetY + margin / 2 + titleH - 6.5 * txt, txt,
                    us ? F($"Horizontal 1\" = {hs:G4}'; vertical 1\" = {vs:G4}'. HGL solid, EGL dashed. Rapid City IDCM 2012 4.6.7: HGL at least {(double.IsNaN(clearReq) ? 1.0 : clearReq):G3} ft below the rim.")
                       : F($"Horizontal 1:{hs:G4}; vertical 1:{vs:G4}. HGL solid, EGL dashed."));
                dxf.Text("MF-PROF-TEXT", sheetX + sheetWm - margin, sheetY + margin / 2 + titleH - 2 * txt, 2 * txt, F($"SHEET {sheetNo}"), 2);
                dxf.Text("MF-PROF-TEXT", sheetX + sheetWm - margin, sheetY + margin / 2 + titleH - 4.5 * txt, txt, "MasterFlow", 2);
                // Grid
                double gx0 = X(s0), gx1 = X(s0 + usableW);
                for (double z = datum; z <= top + 1e-9; z += eStep)
                {
                    dxf.Line("MF-PROF-GRID", gx0, Y(z), gx1, Y(z));
                    dxf.Text("MF-PROF-GRID-TEXT", gx0 - 0.5 * txt, Y(z) - 0.5 * txt, txt, F($"{z:F0}"), 2);
                    dxf.Text("MF-PROF-GRID-TEXT", gx1 + 0.5 * txt, Y(z) - 0.5 * txt, txt, F($"{z:F0}"));
                }
                for (double s = Math.Ceiling(s0 / sStep) * sStep; s <= s0 + usableW + 1e-9; s += sStep)
                {
                    dxf.Line("MF-PROF-GRID", X(s), Y(datum), X(s), Y(top));
                    dxf.Text("MF-PROF-GRID-TEXT", X(s), Y(datum) - 2 * txt, txt, Sta(s, us), 1);
                }
                // Ground (rim) line through the structures, clipped to the sheet.
                var rimPts = stations.Where(s => s.S.RimElevation is not null).Select(s => (s.Sta, s.S.RimElevation!.Value)).ToList();
                var rimClip = Clip(rimPts, s0, s1).Select(q => (X(q.Item1), Y(q.Item2))).ToList();
                dxf.Polyline("MF-PROF-GROUND", rimClip);
                // Pipes: invert and crown lines, HGL and EGL along the solved profile.
                foreach (var (pr, sd, su) in pipeSta)
                {
                    var inv = Clip([(sd, pr.Pipe.DownstreamInvert), (su, pr.Pipe.UpstreamInvert)], s0, s1);
                    if (inv.Count < 2) continue;
                    dxf.Polyline("MF-PROF-PIPE", inv.Select(q => (X(q.Item1), Y(q.Item2))).ToList());
                    dxf.Polyline("MF-PROF-PIPE", inv.Select(q => (X(q.Item1), Y(q.Item2 + pr.Pipe.Rise))).ToList());
                    var prof = pr.Profile.Count >= 2 ? pr.Profile.Select(q => (S: su - q.Station, q.Hgl, q.Egl)).ToList()
                        : [(su, pr.HglUp, pr.EglUp), (sd, pr.HglDown, pr.EglDown)];
                    dxf.Polyline("MF-PROF-HGL", Clip(prof.Select(q => (q.S, q.Hgl)).ToList(), s0, s1).Select(q => (X(q.Item1), Y(q.Item2))).ToList());
                    dxf.Polyline("MF-PROF-EGL", Clip(prof.Select(q => (q.S, q.Egl)).ToList(), s0, s1).Select(q => (X(q.Item1), Y(q.Item2))).ToList());
                    double sm = Math.Clamp((sd + su) / 2, s0, s1);
                    double zm = (pr.Pipe.DownstreamInvert + pr.Pipe.UpstreamInvert) / 2;
                    dxf.Text("MF-PROF-TEXT", X(sm), Y(zm) - 2 * txt, txt, F($"{pr.Pipe.Name} {Size(pr.Pipe, us)} L={pr.Pipe.Length:F1} S={Slope(pr.Pipe):F2}%"), 1);
                    dxf.Text("MF-PROF-TEXT", X(sm), Y(zm) - 3.6 * txt, txt, F($"Q={Q(pr):F2} V={pr.VelocityUp:F2} {(us ? "fps" : "m/s")} {pr.Regime}"), 1);
                }
                // Structures: box from the lowest invert to the rim, labels, HGL/EGL ticks and clearance flags.
                double bw = 0.08 * paper;
                foreach (var s in stations.Where(s => In(s.Sta)))
                {
                    double rim = s.S.RimElevation ?? (s.R?.Egl ?? s.Lowest) + 1;
                    dxf.Rectangle("MF-PROF-STRUCT", X(s.Sta) - bw / 2, Y(s.Lowest), X(s.Sta) + bw / 2, Y(rim));
                    double ty = Y(rim) + 1.0 * txt;
                    var lines = new List<string> { s.Name };
                    if (s.S.RimElevation is { } re) lines.Add(F($"RIM {re:F2}"));
                    if (s.InvIn is { } ii) lines.Add(F($"INV IN {ii:F2}"));
                    if (s.InvOut is { } io) lines.Add(F($"INV OUT {io:F2}"));
                    if (s.R is { } r) { lines.Add(F($"HGL {r.Hgl:F2}")); lines.Add(F($"EGL {r.Egl:F2}")); }
                    for (int i = lines.Count - 1, j = 0; i >= 0; i--, j++) dxf.Text("MF-PROF-TEXT", X(s.Sta), ty + j * 1.5 * txt, txt, lines[i], 1);
                    dxf.Text("MF-PROF-TEXT", X(s.Sta), Y(datum) - 3.6 * txt, txt, Sta(s.Sta, us), 1);
                    if (s.R is { } rr)
                    {
                        dxf.Line("MF-PROF-HGL", X(s.Sta) - bw, Y(rr.Hgl), X(s.Sta) + bw, Y(rr.Hgl));
                        dxf.Line("MF-PROF-EGL", X(s.Sta) - bw, Y(rr.Egl), X(s.Sta) + bw, Y(rr.Egl));
                        if (s.S.RimElevation is { } rim2 && !double.IsNaN(clearReq) && rim2 - rr.Hgl < clearReq - 1e-9)
                        {
                            dxf.Circle("MF-PROF-FLAG", X(s.Sta), Y(rr.Hgl), 3 * txt);
                            dxf.Text("MF-PROF-FLAG", X(s.Sta) + 3.5 * txt, Y(rr.Hgl), txt, F($"HGL {rim2 - rr.Hgl:F2} {L} BELOW RIM < {clearReq:G3}"));
                        }
                    }
                }
                sheetY -= sheetHm + gap;
            }

            // Tables and the report figure for this run.
            var sRows = stations.Select(s =>
            {
                double? clr = s.S.RimElevation is { } rim && s.R is { } r ? Math.Round(rim - r.Hgl, 2) + 0.0 : null;
                string check = clr is null || double.IsNaN(clearReq) ? "" : clr >= clearReq - 1e-9 ? "OK" : $"HGL within {clearReq:G3} {L} of rim";
                if (check.StartsWith("HGL")) flags++;
                return (IReadOnlyList<object?>)new object?[] { Sta(s.Sta, us), s.Name, s.S.RimElevation, s.InvIn, s.InvOut, s.R is { } a ? Math.Round(a.Hgl, 2) : null, s.R is { } b ? Math.Round(b.Egl, 2) : null, clr, check };
            }).ToList();
            tables.Add(new ToolTable($"Profile {start} to {outName}: structures", ["Station", "Structure", $"Rim ({L})", $"Inv in ({L})", $"Inv out ({L})", $"HGL ({L})", $"EGL ({L})", $"Rim − HGL ({L})", "Check"], sRows,
                [$"Stations from 0+00 at {outName}, increasing upstream. Check: {net.Criteria.Source} HGL clearance {(double.IsNaN(clearReq) ? "not set" : $"{clearReq:G3} {L}")}."]));
            var pRows = pipeSta.Select(x => (IReadOnlyList<object?>)new object?[]
            {
                x.P.Pipe.Name, $"{Sta(x.Down, us)} – {Sta(x.Up, us)}", Size(x.P.Pipe, us), x.P.Pipe.Length, Math.Round(Slope(x.P.Pipe), 3), Math.Round(Q(x.P), 2),
                Math.Round(x.P.VelocityUp, 2), Math.Round(x.P.HglUp, 2), Math.Round(x.P.HglDown, 2), Math.Round(x.P.EglUp, 2), Math.Round(x.P.EglDown, 2), x.P.Regime,
            }).ToList();
            tables.Add(new ToolTable($"Profile {start} to {outName}: pipes", ["Pipe", "Stations", "Size", $"Length ({L})", "Slope (%)", us ? "Q (cfs)" : "Q (m³/s)", us ? "V up (fps)" : "V up (m/s)", $"HGL up ({L})", $"HGL down ({L})", $"EGL up ({L})", $"EGL down ({L})", "Regime"], pRows));
            var hgl = new List<(double, double)>(); var egl = new List<(double, double)>();
            foreach (var (pr, sd, su) in pipeSta)
            {
                var prof = pr.Profile.Count >= 2 ? pr.Profile.Select(q => (su - q.Station, q.Hgl, q.Egl)).Reverse().ToList() : [(sd, pr.HglDown, pr.EglDown), (su, pr.HglUp, pr.EglUp)];
                foreach (var (s, h, e) in prof) { hgl.Add((s, h)); egl.Add((s, e)); }
            }
            var series = new List<ToolFigureSeries>
            {
                new("Rim / ground", ToolSeriesKind.Ground, stations.Where(s => s.S.RimElevation is not null).Select(s => (s.Sta, s.S.RimElevation!.Value)).ToList()),
                new("Pipe invert", ToolSeriesKind.Limit, pipeSta.SelectMany(x => new[] { (x.Down, x.P.Pipe.DownstreamInvert), (x.Up, x.P.Pipe.UpstreamInvert) }).ToList()),
                new("Pipe crown", ToolSeriesKind.Limit, pipeSta.SelectMany(x => new[] { (x.Down, x.P.Pipe.DownstreamInvert + x.P.Pipe.Rise), (x.Up, x.P.Pipe.UpstreamInvert + x.P.Pipe.Rise) }).ToList()),
                new("HGL", ToolSeriesKind.Water, hgl),
                new("EGL", ToolSeriesKind.Water, egl),
            };
            figures.Add(new ToolFigure($"Profile {start} to {outName}", series, $"Station ({L})", $"Elevation ({L})", ["The HGL and EGL rise across each structure by its loss (shown on the DXF as ticks at the structure)."]));
            sheetY -= gap;
        }
        if (flags > 0) msgs.Add(CalcMessage.Warning("W-PP-003", $"{flags} structure(s) along the drawn runs have the HGL closer to the rim than {clearReq:G3} {L} ({net.Criteria.Source}); they are circled on the sheets."));

        var bytes = dxf.ToBytes();
        string fileName = Safe($"{net.Name} plan and profile.dxf");
        if (p.Text("dxf file") is { } outPath)
        {
            try { File.WriteAllBytes(outPath.Trim('"'), bytes); msgs.Add(CalcMessage.Info("I-PP-002", $"Wrote {outPath.Trim('"')}.")); }
            catch (Exception ex) { msgs.Add(CalcMessage.Error("E-PP-006", $"Could not write {outPath}: {ex.Message}")); }
        }
        msgs.Add(CalcMessage.Info("I-PP-003", "Save… writes the DXF (choose .dxf). Model space is drawn at the horizontal scale: plot each sheet border at 1:1 sheet size."));
        var summary = new List<string>
        {
            $"Network \"{net.Name}\": {starts.Count} profile run(s) on {sheetNo} sheet(s) of {sheetW:G4} × {sheetH:G4} {(us ? "in." : "mm")}, horizontal {(us ? $"1\" = {hs:G4}'" : $"1:{hs:G4}")}, vertical {(us ? $"1\" = {vs:G4}'" : $"1:{vs:G4}")} (exaggeration {ve:G3}).",
            drawPlan && at.Count > 0 ? $"Plan view of {at.Count} structure(s) at their coordinates." : "No plan view.",
            $"DXF: {dxf.EntityCount} entities, {bytes.Length / 1024.0:F0} KB (R12, layers MF-PLAN-* and MF-PROF-*).",
        };
        return new ToolResult(title, summary, tables, msgs) { Figures = figures, Files = [new ToolFile(fileName, "DXF drawing (plan and profile sheets)", bytes)] };
    }

    private static double Q(PipeHydraulicResult pr) => pr.Pipe.Discharge;

    private static double Slope(NetworkPipe x) => x.Length > 0 ? 100 * (x.UpstreamInvert - x.DownstreamInvert) / x.Length : 0;

    private static string Size(NetworkPipe x, bool us) => x.Shape == NetworkPipeShape.Elliptical
        ? (us ? F($"{x.Rise * 12:F0}x{x.Span * 12:F0} in.") : F($"{x.Rise * 1000:F0}x{x.Span * 1000:F0} mm"))
        : (us ? F($"{x.Rise * 12:F0} in.") : F($"{x.Rise * 1000:F0} mm"));

    public static string Sta(double s, bool us)
    {
        double unit = us ? 100 : 1000;
        int major = (int)Math.Floor(s / unit + 1e-9);
        double minor = s - major * unit;
        if (minor < 0) minor = 0;
        return us ? F($"{major}+{minor:00.00}") : F($"{major}+{minor:000.00}");
    }

    internal static double NiceStep(double raw)
    {
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double f = raw / mag;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * mag;
    }

    /// <summary>Clips a polyline in (station, value) to stations s0..s1.</summary>
    internal static List<(double, double)> Clip(List<(double S, double Z)> pts, double s0, double s1)
    {
        var o = new List<(double, double)>();
        var sorted = pts.OrderBy(q => q.S).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var a = sorted[i];
            if (a.S >= s0 - 1e-9 && a.S <= s1 + 1e-9) o.Add(a);
            if (i + 1 < sorted.Count)
            {
                var b = sorted[i + 1];
                foreach (double c in new[] { s0, s1 })
                    if ((a.S < c - 1e-9 && b.S > c + 1e-9)) o.Add((c, a.Z + (b.Z - a.Z) * (c - a.S) / (b.S - a.S)));
            }
        }
        return o.OrderBy(q => q.Item1).ToList();
    }

    internal static (double W, double H) Sheet(string? text, bool us)
    {
        if (text is null) return us ? (34, 22) : (841, 594);
        var parts = text.ToLowerInvariant().Replace("×", "x").Split('x', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && ToolInput.TryNumber(parts[0], out double w) && ToolInput.TryNumber(parts[1], out double h) ? (w, h) : (double.NaN, double.NaN);
    }

    internal static string Safe(string s) => new(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
