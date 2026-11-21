using System.Globalization;
using MasterFlow.Application.Surfaces;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Pond stage–storage from a graded surface (TIN): a Civil 3D LandXML surface or pasted points and triangles, optionally
/// limited to a boundary polygon. Volumes are exact for the TIN (each triangle clipped at the water level); the conic
/// method on the same contour areas is shown beside them. Writes ready-to-paste [Stage storage] blocks for the
/// detention and outlet tools.
/// </summary>
public sealed class PondStorageTool : IDesignTool
{
    public string Id => "pond-storage";
    public string Name => "Pond stage–storage from a TIN surface (LandXML)";
    public string Description => "Stage–area–volume of a pond from a graded TIN surface: a Civil 3D LandXML file (surface by name) or pasted points and faces, optionally inside a boundary polygon. Volumes are exact for the TIN (triangles clipped at each water level), compared with the conic method on the same areas. Finds the lowest rim (spill) elevation and writes [Stage storage] blocks ready to paste into the detention pond and outlet designer tools.";
    public bool UsesNetwork => false;

    private const double Ft3PerAcFt = 43560, Ft2PerAc = 43560;

    public string SampleInput(DesignToolContext context)
    {
        bool si = context.Units == UnitSystem.SI;
        // A 4H:1V frustum pond: bottom 100 × 50 at 100.0, top 148 × 98 at 106.0 (SI: 30 × 15 m at 30.0, 3H:1V, 2 m deep).
        double l = si ? 30 : 100, w = si ? 15 : 50, z = si ? 3 : 4, d = si ? 2 : 6, b = si ? 30 : 100, x0 = si ? 1000 : 5000, y0 = si ? 2000 : 8000;
        string P(int id, double x, double y, double e) => string.Create(CultureInfo.InvariantCulture, $"{id}\t{x:F2}\t{y:F2}\t{e:F2}");
        var pts = new[]
        {
            P(1, x0, y0, b), P(2, x0 + l, y0, b), P(3, x0 + l, y0 + w, b), P(4, x0, y0 + w, b),
            P(5, x0 - z * d, y0 - z * d, b + d), P(6, x0 + l + z * d, y0 - z * d, b + d), P(7, x0 + l + z * d, y0 + w + z * d, b + d), P(8, x0 - z * d, y0 + w + z * d, b + d),
        };
        string u = si ? "m" : "ft";
        return $"""
            # Pond stage–storage from a graded surface (TIN). Either point to a Civil 3D LandXML export:
            #   surface file = C:\Projects\Pond\pond-grading.xml
            #   surface = Pond FG          # surface name in the file (the first TIN when left out)
            # or paste the TIN below: [TIN points] (Id, X = easting, Y = northing, Z) and [TIN faces] (three point Ids).
            # This sample is a {z}H:1V frustum pond, {l} × {w} {u} at the bottom, {d} {u} deep.
            interval = {(si ? "0.25" : "0.5")}              # stage interval, {u}
            # bottom = {b}              # optional; defaults to the lowest point inside the boundary
            # top = {b + d}                 # optional; defaults to the highest point inside the boundary

            [TIN points]
            Id	X ({u})	Y ({u})	Z ({u})
            {string.Join("\n", pts)}

            [TIN faces]
            A	B	C
            1	2	3
            1	3	4
            1	5	6
            1	6	2
            2	6	7
            2	7	3
            3	7	8
            3	8	4
            4	8	5
            4	5	1

            # Optional: only triangles whose centroid is inside this polygon count (e.g. the pond grading limits).
            # [Boundary]
            # X	Y
            """;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Pond stage–storage from a TIN";
        bool si = context.Units == UnitSystem.SI;
        string u = si ? "m" : "ft";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>(p.Errors);

        // Surface
        TinSurface? tin = null;
        if (p.Text("surface file") is { } file)
        {
            file = file.Trim('"');
            if (!File.Exists(file)) return ToolResult.Failed(title, CalcMessage.Error("E-PS-001", $"Surface file \"{file}\" was not found."));
            LandXmlSurfaces.Result read;
            using (var s = File.OpenRead(file)) read = LandXmlSurfaces.Read(s);
            foreach (var pr in read.Problems) msgs.Add(CalcMessage.Warning("W-PS-001", pr));
            if (read.Surfaces.Count == 0) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-PS-002", "No TIN surface could be read from the file.")]);
            string? want = p.Text("surface");
            tin = want is null ? read.Surfaces[0] : read.Surfaces.FirstOrDefault(x => x.Name.Equals(want, StringComparison.OrdinalIgnoreCase));
            if (tin is null) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-PS-003", $"Surface \"{want}\" is not in the file. Surfaces found: {string.Join(", ", read.Surfaces.Select(x => x.Name))}.")]);
            if (want is null && read.Surfaces.Count > 1) msgs.Add(CalcMessage.Info("I-PS-001", $"The file has {read.Surfaces.Count} surfaces; using the first, \"{tin.Name}\". Name another with surface = …"));
            if (read.LinearUnit is { } lu)
            {
                bool feet = lu.Contains("foot", StringComparison.OrdinalIgnoreCase) || lu.Contains("feet", StringComparison.OrdinalIgnoreCase);
                bool meter = lu.Contains("meter", StringComparison.OrdinalIgnoreCase) || lu.Contains("metre", StringComparison.OrdinalIgnoreCase);
                if ((si && !meter) || (!si && !feet)) msgs.Add(CalcMessage.Warning("W-PS-002", $"The file's linear unit is \"{lu}\" but the project is in {(si ? "metres" : "feet")}; coordinates are used as they are."));
            }
        }
        else if (p.Rows("TIN points").Count > 0)
        {
            var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var pts = new List<(double, double, double)>();
            foreach (var r in p.Rows("TIN points"))
            {
                string id = r.Length > 0 ? r[0] : "";
                if (p.Cell(r, 1, "X", $"Point {id}") is { } x && p.Cell(r, 2, "Y", $"Point {id}") is { } y && p.Cell(r, 3, "Z", $"Point {id}") is { } zz)
                {
                    if (!ids.TryAdd(id, pts.Count)) { msgs.Add(CalcMessage.Error("E-PS-004", $"Point Id \"{id}\" is repeated.")); continue; }
                    pts.Add((x, y, zz));
                }
            }
            var faces = new List<(int, int, int)>();
            foreach (var r in p.Rows("TIN faces"))
            {
                if (r.Length < 3 || !ids.TryGetValue(r[0], out int a) || !ids.TryGetValue(r[1], out int bb) || !ids.TryGetValue(r[2], out int c))
                { msgs.Add(CalcMessage.Error("E-PS-005", $"Face \"{string.Join(" ", r)}\" refers to a point that is not under [TIN points].")); continue; }
                faces.Add((a, bb, c));
            }
            if (faces.Count == 0) msgs.Add(CalcMessage.Error("E-PS-006", "[TIN faces] needs at least one triangle (three point Ids)."));
            tin = new TinSurface("Pasted TIN", pts, faces);
        }
        else return ToolResult.Failed(title, CalcMessage.Error("E-PS-007", "Give surface file = <LandXML path>, or paste [TIN points] and [TIN faces]."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        // Boundary
        Func<int, bool>? include = null;
        var poly = new List<(double X, double Y)>();
        foreach (var r in p.Rows("Boundary"))
            if (p.Cell(r, 0, "X", "Boundary vertex") is { } x && p.Cell(r, 1, "Y", "Boundary vertex") is { } y) poly.Add((x, y));
        if (poly.Count is > 0 and < 3) msgs.Add(CalcMessage.Error("E-PS-008", "[Boundary] needs at least three vertices (X, Y)."));
        if (poly.Count >= 3)
        {
            var t = tin;
            include = f => { var c = t.Centroid(f); return TinSurface.Inside(poly, c.X, c.Y); };
            if (!Enumerable.Range(0, tin.Faces.Count).Any(include)) msgs.Add(CalcMessage.Error("E-PS-009", "No triangle of the surface lies inside [Boundary]; check the coordinates (X = easting, Y = northing)."));
        }
        double interval = p.Number("interval", si ? 0.25 : 0.5, "Stage interval");
        if (!(interval > 0)) msgs.Add(CalcMessage.Error("E-PS-010", "The stage interval must be greater than zero."));
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double zmin = tin.MinZ(include), zmax = tin.MaxZ(include);
        double bottom = p.OptionalNumber("bottom") ?? zmin, top = p.OptionalNumber("top") ?? zmax;
        if (!(top > bottom)) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-PS-011", $"The top ({top:F2}) must be above the bottom ({bottom:F2}).")]);
        if (bottom < zmin - 1e-9) msgs.Add(CalcMessage.Info("I-PS-002", $"The bottom {bottom:F2} is below the lowest surface point {zmin:F2}; the first stages hold no water."));
        if ((top - bottom) / interval > 2000) { interval = (top - bottom) / 2000; msgs.Add(CalcMessage.Info("I-PS-003", $"The interval was widened to {interval:G3} {u} to keep the table to 2000 rows.")); }

        // Lowest rim point: the lowest vertex on the outer edge of the included triangles. Water above it spills out
        // of the graded area, so a level pool above it is not held by the surface.
        var edgeCount = new Dictionary<(int, int), int>();
        for (int f = 0; f < tin.Faces.Count; f++)
        {
            if (include is not null && !include(f)) continue;
            var (a, b, c) = tin.Faces[f];
            foreach (var (i, j) in new[] { (a, b), (b, c), (c, a) })
            { var k = i < j ? (i, j) : (j, i); edgeCount[k] = edgeCount.GetValueOrDefault(k) + 1; }
        }
        var rimEdges = edgeCount.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
        double rim = rimEdges.Count > 0 ? rimEdges.SelectMany(k => new[] { k.Item1, k.Item2 }).Min(i => tin.Points[i].Z) : double.NaN;
        if (rim < top - 1e-6)
            msgs.Add(CalcMessage.Warning("W-PS-003", $"The rim of the graded area is as low as {rim:F2}, below the top stage {top:F2}: water above {rim:F2} would spill out of the surface, so the volumes above it are not held. Set top = {rim:F2}, extend the surface or check the boundary."));

        // Stages
        var elev = new List<double>();
        for (int i = 0; ; i++) { double e = bottom + i * interval; if (e > top + 1e-9) break; elev.Add(Math.Round(e, 6)); }
        if (top - elev[^1] > 1e-6) elev.Add(top);
        if (rim > bottom && rim < top && elev.All(e => Math.Abs(e - rim) > 1e-6)) { elev.Add(rim); elev.Sort(); }
        var rows = elev.Select(e => (E: e, R: tin.Below(e, include))).ToList();

        // Conic method on the TIN areas, for comparison.
        var conic = new double[rows.Count];
        for (int i = 1; i < rows.Count; i++)
        {
            double a1 = rows[i - 1].R.Area, a2 = rows[i].R.Area, h = rows[i].E - rows[i - 1].E;
            conic[i] = conic[i - 1] + h / 3 * (a1 + a2 + Math.Sqrt(a1 * a2));
        }

        double volUnit = si ? 1 : Ft3PerAcFt;
        string volU = si ? "m³" : "ac-ft", areaU = si ? "m²" : "ft²", vol3 = si ? "m³" : "ft³";
        var table = rows.Select((r, i) => (IReadOnlyList<object?>)new object?[]
        {
            Math.Round(r.E, 3), Math.Round(r.E - bottom, 3), Math.Round(r.R.Area, 1), si ? Math.Round(r.R.Area / 10000, 4) : Math.Round(r.R.Area / Ft2PerAc, 4),
            Math.Round(r.R.Volume, 0), Math.Round(r.R.Volume / volUnit, 4), Math.Round(conic[i] / volUnit, 4),
            r.R.Volume > 0 ? Math.Round(100 * (conic[i] - r.R.Volume) / r.R.Volume, 2) + 0.0 : null,
            Math.Abs(r.E - rim) < 1e-6 ? "rim (spill)" : r.E > rim + 1e-6 ? "above rim" : "",
        }).ToList();
        var head = new[] { $"Elevation ({u})", $"Depth ({u})", $"Area ({areaU})", si ? "Area (ha)" : "Area (ac)", $"Volume ({vol3})", $"Volume ({volU})", $"Conic volume ({volU})", "Conic − TIN (%)", "Note" };

        var last = rows[^1].R;
        var volAtRim = !double.IsNaN(rim) && rim < top ? tin.Below(rim, include).Volume : last.Volume;
        var paste = new List<IReadOnlyList<object?>>();
        paste.Add(["# For the detention pond tool (elevation–volume, exact TIN storage):"]);
        paste.Add(["[Stage storage]"]);
        paste.Add([$"Elevation ({u})\tVolume ({vol3})"]);
        foreach (var r in rows) paste.Add([string.Create(CultureInfo.InvariantCulture, $"{r.E:F2}\t{r.R.Volume:F0}")]);
        paste.Add(["# For the outlet designer (elevation–area, conic storage between rows):"]);
        paste.Add(["[Stage storage]"]);
        paste.Add([$"Elevation ({u})\tArea ({areaU})"]);
        foreach (var r in rows.Where(r => r.R.Area > 0)) paste.Add([string.Create(CultureInfo.InvariantCulture, $"{r.E:F2}\t{r.R.Area:F0}")]);

        int nFaces = include is null ? tin.Faces.Count : Enumerable.Range(0, tin.Faces.Count).Count(include);
        var summary = new List<string>
        {
            $"Surface \"{tin.Name}\": {tin.Points.Count} points, {nFaces} triangles used{(poly.Count >= 3 ? " (inside the boundary)" : "")}; lowest point {zmin:F2}, highest {zmax:F2} {u}.",
            $"Storage {last.Volume / volUnit:F3} {volU} ({last.Volume:N0} {vol3}) at {top:F2}, water surface area {last.Area:N0} {areaU}.",
            double.IsNaN(rim) ? "The surface has no open edge (closed mesh)." : $"Lowest rim (spill) elevation {rim:F2} {u}{(rim < top - 1e-6 ? $"; storage held to the rim {volAtRim / volUnit:F3} {volU}" : "")}.",
        };
        if (last.Volume > 0) summary.Add($"Conic method on the same areas gives {conic[^1] / volUnit:F3} {volU} at the top ({Math.Round(100 * (conic[^1] - last.Volume) / last.Volume, 2) + 0.0:+0.00;-0.00;0.00} %).");

        var notes = new[]
        {
            "Volume below a level water surface, exact for the TIN: each triangle is clipped at the water level and ∫(WSE − z) dA = clipped area × (WSE − z at its centroid).",
            "Every low area of the surface below a stage counts (level-pool assumption); separate pockets that do not connect to the outlet should be left out with [Boundary].",
            "Conic: V = Σ h/3 (A1 + A2 + √(A1·A2)) on the TIN areas at the listed stages.",
        };
        var charts = new List<ToolChart>
        {
            new("Stage–storage", $"Storage ({volU})", $"Elevation ({u})",
                [new ChartSeries("TIN", rows.Select(r => (r.R.Volume / volUnit, r.E)).ToList()), new ChartSeries("Conic", rows.Select((r, i) => (conic[i] / volUnit, r.E)).ToList(), Dashed: true)],
                double.IsNaN(rim) ? null : [new ChartLine("Rim (spill)", rim)]),
            new("Stage–area", $"Area ({areaU})", $"Elevation ({u})", [new ChartSeries("Water surface area", rows.Select(r => (r.R.Area, r.E)).ToList())]),
        };
        if (rows.Count >= 2) msgs.Add(CalcMessage.Info("I-PS-004", "Paste the [Stage storage] block from the table below into the detention pond tool (volumes) or the outlet designer (areas)."));

        return new ToolResult(title, summary,
        [
            new ToolTable("Stage–area–volume", head, table, notes),
            new ToolTable("Ready to paste", ["Line"], paste),
        ], msgs) { Charts = charts };
    }
}
