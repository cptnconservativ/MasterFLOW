using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Smallest round manhole or inlet structure that fits its pipes: each pipe needs a wall opening (pipe outside diameter plus
/// a clearance on each side), and openings that overlap in elevation must leave a minimum leg of wall between them on the
/// inside face. The required inside diameter is solved exactly and rounded up to the next size the precaster makes.
/// </summary>
public sealed class ManholeSizingTool : IDesignTool
{
    public string Id => "manhole-sizing";
    public string Name => "Manhole and structure size from pipes";
    public string Description => "Smallest round structure for its pipes: opening = pipe OD + 2·clearance (OD from ASTM C76 wall B when the wall is blank), opening angle θ = 2·asin(w/D) on the inside face, and for every two openings that overlap in elevation the plan angle between them must be at least (θ1 + θ2)/2 + 2·asin(leg/D). Solves the least D and rounds up to your standard sizes. Pipe sizes, inverts and plan angles are read from the selected storm network where they are left blank.";
    public bool UsesNetwork => true;

    /// <summary>ASTM C76 wall B thickness: ID/12 + 1 in. (U.S., inches) or ID/12 + 25 mm (SI, millimetres).</summary>
    public static double WallB(double id, bool si) => id / 12 + (si ? 25 : 1);

    /// <summary>Plan angle (degrees, 0–180) between two directions.</summary>
    public static double Separation(double a, double b)
    {
        double d = Math.Abs(((a - b) % 360 + 360) % 360);
        return d > 180 ? 360 - d : d;
    }

    /// <summary>Angle (degrees) subtended on the inside face of a structure of diameter <paramref name="d"/> by a chord of length <paramref name="w"/>; NaN when it does not fit.</summary>
    public static double ChordAngle(double w, double d) => w < d ? 2 * Math.Asin(w / d) * 180 / Math.PI : double.NaN;

    /// <summary>Opening of one pipe: width (same units as the diameter) and its vertical band (bottom, top) when the invert is known.</summary>
    public sealed record Opening(string Pipe, double Id, double Wall, double Width, double Angle, double? Bottom, double? Top);

    /// <summary>True when two openings overlap in elevation, or come within one leg of each other (unknown inverts overlap).</summary>
    public static bool Interfere(Opening a, Opening b, double legElev)
        => a.Bottom is not { } ab || b.Bottom is not { } bb || !(a.Top + legElev <= bb || b.Top + legElev <= ab);

    /// <summary>Whether all pairs of interfering openings fit in diameter <paramref name="d"/> with leg <paramref name="leg"/>.</summary>
    public static bool Fits(IReadOnlyList<Opening> o, double d, double leg, double legElev)
    {
        if (o.Any(x => x.Width >= d) || leg >= d) return false;
        double phi = ChordAngle(leg, d);
        for (int i = 0; i < o.Count; i++)
            for (int j = i + 1; j < o.Count; j++)
                if (Interfere(o[i], o[j], legElev) && Separation(o[i].Angle, o[j].Angle) < (ChordAngle(o[i].Width, d) + ChordAngle(o[j].Width, d)) / 2 + phi - 1e-9)
                    return false;
        return true;
    }

    /// <summary>Least inside diameter that fits every opening (bisection; every constraint loosens as D grows), or NaN when no D up to 100× the largest opening fits.</summary>
    public static double RequiredDiameter(IReadOnlyList<Opening> o, double leg, double legElev)
    {
        double lo = Math.Max(o.Max(x => x.Width), leg), hi = 100 * lo;
        if (!Fits(o, hi, leg, legElev)) return double.NaN;
        for (int k = 0; k < 200 && hi - lo > 1e-9 * hi; k++)
        {
            double mid = 0.5 * (lo + hi);
            if (Fits(o, mid, leg, legElev)) hi = mid; else lo = mid;
        }
        return hi;
    }

    public string SampleInput(DesignToolContext context)
    {
        bool si = context.Units == UnitSystem.SI;
        string head = si ? """
            # Manhole and structure size. SI: pipe ID and wall in mm, inverts in m, angles in degrees clockwise (any reference, e.g. outlet = 0).
            min diameter = 1200       # smallest structure allowed (mm)
            sizes = 1200, 1500, 1800, 2100, 2400, 3000, 3600
            clearance = 50            # each side of the pipe OD (cored hole or boot), mm
            leg = 200                 # least wall left between openings on the inside face, mm
            """ : """
            # Manhole and structure size. U.S.: pipe ID and wall in inches, inverts in ft, angles in degrees clockwise (any reference, e.g. outlet = 0).
            # Blank cells are read from the selected storm network (ID, invert; the angle too when the structures have coordinates).
            # Wall blank = ASTM C76 wall B (ID/12 + 1 in.). Use the precaster's hole size table where it governs.
            min diameter = 48         # smallest structure the agency allows (in.)
            sizes = 48, 60, 72, 84, 96, 120, 144
            clearance = 2             # each side of the pipe OD (cored hole or boot), in.
            leg = 8                   # least wall left between openings on the inside face, in.
            """;
        var rows = new List<string>();
        if (context.Network is { } n && n.Pipes.Count > 0)
        {
            foreach (var s in n.Structures)
            {
                var pipes = n.Pipes.Where(p => p.To == s.Name || p.From == s.Name).ToList();
                if (pipes.Count < 2) continue;
                int k = 0;
                foreach (var p in pipes)
                {
                    string ang = Angle(n, s, p) is null ? (k == 0 ? "0" : (90 + 90 * k).ToString()) : "";
                    rows.Add($"{s.Name}\t{p.Name}\t\t\t{ang}\t");
                    k++;
                }
            }
        }
        if (rows.Count == 0)
            rows = si
                ? ["MH-1\tOut\t600\t\t0\t30.00", "MH-1\tWest\t450\t\t90\t30.15", "MH-1\tNorth\t450\t\t180\t30.15", "MH-1\tEast\t375\t\t270\t30.30"]
                : ["MH-1\tOut\t24\t\t0\t100.00", "MH-1\tWest\t18\t\t90\t100.50", "MH-1\tNorth\t18\t\t180\t100.50", "MH-1\tEast\t15\t\t270\t101.00"];
        return head + "\n[Pipes]\nStructure\tPipe\tID\tWall\tAngle (deg)\tInvert\n" + string.Join("\n", rows) + "\n";
    }

    /// <summary>Plan angle (degrees clockwise from north) from structure <paramref name="s"/> along pipe <paramref name="p"/>, when both ends have coordinates.</summary>
    private static double? Angle(StormNetwork n, NetworkStructure s, NetworkPipe p)
    {
        string other = p.To == s.Name ? p.From : p.To;
        var o = n.Structures.FirstOrDefault(x => x.Name == other);
        if (s.Northing is not { } n1 || s.Easting is not { } e1 || o?.Northing is not { } n2 || o.Easting is not { } e2) return null;
        if (Math.Abs(n2 - n1) + Math.Abs(e2 - e1) < 1e-9) return null;
        return (Math.Atan2(e2 - e1, n2 - n1) * 180 / Math.PI + 360) % 360;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Manhole and structure size";
        bool si = context.Units == UnitSystem.SI;
        string U = si ? "mm" : "in.", E = si ? "m" : "ft";
        double toElev = si ? 0.001 : 1.0 / 12;              // pipe units → elevation units
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double minD = p.Number("min diameter", si ? 1200 : 48, "min diameter");
        double clear = p.Number("clearance", si ? 50 : 2, "clearance");
        double leg = p.Number("leg", si ? 200 : 8, "leg");
        var sizes = new List<double>();
        foreach (var t in (p.Text("sizes") ?? (si ? "1200,1500,1800,2100,2400,3000,3600" : "48,60,72,84,96,120,144")).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ToolInput.TryNumber(t, out double v) && v > 0) sizes.Add(v);
            else msgs.Add(CalcMessage.Error("E-MH-001", $"Structure size \"{t}\" is not a positive number."));
        sizes.Sort();
        if (!(minD > 0) || !(leg > 0) || !(clear >= 0)) msgs.Add(CalcMessage.Error("E-MH-002", "min diameter and leg must be above zero and clearance zero or more."));

        var net = context.Network;
        var groups = new List<(string Structure, List<Opening> Openings)>();
        foreach (var r in p.Rows("Pipes"))
        {
            string sName = r.Length > 0 ? r[0] : "", pName = r.Length > 1 ? r[1] : "";
            string label = $"{sName} / {pName}";
            if (sName.Length == 0) { msgs.Add(CalcMessage.Error("E-MH-003", "Every row under [Pipes] needs a structure name.")); continue; }
            double? id = p.Cell(r, 2, "ID", label), wall = p.Cell(r, 3, "wall", label), ang = p.Cell(r, 4, "angle", label), inv = p.Cell(r, 5, "invert", label);
            var np = net?.Pipes.FirstOrDefault(x => x.Name == pName && (x.To == sName || x.From == sName));
            var ns = net?.Structures.FirstOrDefault(x => x.Name == sName);
            if (np is not null)
            {
                id ??= np.Rise * (si ? 1000 : 12);
                inv ??= np.To == sName ? np.DownstreamInvert : np.UpstreamInvert;
                if (ang is null && ns is not null) ang = Angle(net!, ns, np);
                if (np.Shape == NetworkPipeShape.Elliptical) msgs.Add(CalcMessage.Warning("W-MH-001", $"{label}: elliptical pipe; the opening is sized from the span ({np.Span * (si ? 1000 : 12):F0} {U}) as its width, not the rise."));
                if (np.Shape == NetworkPipeShape.Elliptical) id = np.Span * (si ? 1000 : 12);
            }
            if (id is not > 0) { msgs.Add(CalcMessage.Error("E-MH-004", $"{label}: give the pipe ID (or a pipe name in the selected network).")); continue; }
            if (ang is null) { msgs.Add(CalcMessage.Error("E-MH-005", $"{label}: give the plan angle (the network structures have no coordinates to compute it).")); continue; }
            if (wall is < 0) { msgs.Add(CalcMessage.Error("E-MH-006", $"{label}: the wall thickness cannot be negative.")); continue; }
            double t = wall ?? WallB(id.Value, si);
            double w = id.Value + 2 * t + 2 * clear;
            double? bottom = inv is { } iv ? iv - (t + clear) * toElev : null;
            double? top = inv is { } iv2 ? iv2 + (id.Value + t + clear) * toElev : null;
            var g = groups.FirstOrDefault(x => x.Structure == sName);
            if (g.Openings is null) { g = (sName, []); groups.Add(g); }
            g.Openings.Add(new Opening(pName.Length > 0 ? pName : $"pipe {g.Openings.Count + 1}", id.Value, t, w, ang.Value, bottom, top));
        }
        if (groups.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error))
            msgs.Add(CalcMessage.Error("E-MH-007", "Give at least one structure under [Pipes] (Structure, Pipe, ID, Wall, Angle, Invert)."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        var srows = new List<IReadOnlyList<object?>>();
        var orows = new List<IReadOnlyList<object?>>();
        var prows = new List<IReadOnlyList<object?>>();
        var summary = new List<string>();
        double legElev = leg * toElev;
        foreach (var (sName, o) in groups)
        {
            double req = RequiredDiameter(o, leg, legElev);
            double need = Math.Max(req, minD);
            double? std = sizes.Where(x => x >= need - 1e-9).Cast<double?>().FirstOrDefault();
            string governs;
            if (double.IsNaN(req)) governs = "no round structure fits";
            else if (minD >= req) governs = "agency minimum";
            else
            {
                // The pair with the least spare angle at the required D governs (or the widest opening when one pipe alone sets it).
                double phi = ChordAngle(leg, req);
                var pairs = from i in Enumerable.Range(0, o.Count) from j in Enumerable.Range(i + 1, o.Count - i - 1)
                            where Interfere(o[i], o[j], legElev)
                            select (o[i], o[j], Spare: Separation(o[i].Angle, o[j].Angle) - (ChordAngle(o[i].Width, req) + ChordAngle(o[j].Width, req)) / 2 - phi);
                var gp = pairs.OrderBy(x => x.Spare).FirstOrDefault();
                governs = gp.Item1 is null ? $"opening of {o.MaxBy(x => x.Width)!.Pipe}" : $"{gp.Item1.Pipe} and {gp.Item2.Pipe}";
            }
            if (std is null)
                msgs.Add(CalcMessage.Warning("W-MH-002", $"{sName}: needs {(double.IsNaN(req) ? "more than any round structure" : $"{need:F0} {U}")}, larger than the largest listed size; use a box or vault, or move pipes to different elevations or angles."));
            srows.Add([sName, o.Count, R(req, 1), R(need, 1), std, governs]);
            summary.Add(std is { } sd ? $"{sName}: {sd:G4} {U} structure (needs {need:F1} {U}; governed by {governs})." : $"{sName}: no listed size fits ({governs}).");
            double dChk = std ?? need;
            foreach (var x in o)
                orows.Add([sName, x.Pipe, x.Id, R(x.Wall, 2), R(x.Width, 2), R(x.Angle, 1), x.Bottom is { } b ? R(b, 2) : null, x.Top is { } tp ? R(tp, 2) : null, R(ChordAngle(x.Width, dChk), 1)]);
            double phiChk = ChordAngle(leg, dChk);
            for (int i = 0; i < o.Count; i++)
                for (int j = i + 1; j < o.Count; j++)
                {
                    bool inter = Interfere(o[i], o[j], legElev);
                    double sep = Separation(o[i].Angle, o[j].Angle);
                    double needAng = (ChordAngle(o[i].Width, dChk) + ChordAngle(o[j].Width, dChk)) / 2 + phiChk;
                    double legLeft = double.IsFinite(needAng) ? (sep - (needAng - phiChk)) * Math.PI / 180 * dChk / 2 : double.NaN;  // arc of wall left between the openings
                    prows.Add([sName, $"{o[i].Pipe} – {o[j].Pipe}", R(sep, 1), inter ? R(needAng, 1) : null, inter ? R(legLeft, 1) : null, !inter ? "different elevations" : sep >= needAng - 1e-9 ? "OK" : "TOO CLOSE"]);
                }
            if (o.Any(x => x.Bottom is null)) msgs.Add(CalcMessage.Info("I-MH-001", $"{sName}: an invert is missing, so every pair of openings is treated as overlapping in elevation."));
        }
        var tables = new List<ToolTable>
        {
            new("Structure sizes", ["Structure", "Pipes", $"Required D ({U})", $"With minimum ({U})", $"Standard size ({U})", "Governed by"], srows,
                [$"Openings that overlap in elevation (or come within one leg, {leg:G3} {U}) must leave {leg:G3} {U} of wall between them on the inside face.",
                 "Required D solves (θ1 + θ2)/2 + 2·asin(leg/D) ≤ plan angle for every such pair, with θ = 2·asin(w/D). Openings at different elevations are not checked against each other."]),
            new("Openings", ["Structure", "Pipe", $"ID ({U})", $"Wall ({U})", $"Opening w ({U})", "Angle (deg)", $"Bottom ({E})", $"Top ({E})", "θ at size (deg)"], orows,
                [$"w = ID + 2·wall + 2·clearance ({clear:G3} {U}); wall blank = ASTM C76 wall B. Bottom and top of the opening from the invert, wall and clearance."]),
            new("Opening pairs at the chosen size", ["Structure", "Pipes", "Plan angle (deg)", "Needed (deg)", $"Wall left ({U})", "Check"], prows),
        };
        msgs.Add(CalcMessage.Info("I-MH-002", "Check the result against the precaster's maximum hole size table and the structure's base and cone/top slab openings; this tool checks the barrel wall only."));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
