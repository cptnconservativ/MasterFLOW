using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Underground detention bed: rows of arch chambers or perforated pipes in a stone bed. Lays out the bed, builds the
/// stage–storage curve (unit void + stone porosity × the stone around and over the units) and, given a required volume,
/// finds how many units per row are needed. The stage–storage table pastes into the detention routing tool.
/// </summary>
public sealed class UndergroundDetentionTool : IDesignTool
{
    public string Id => "underground-detention";
    public string Name => "Underground detention: chambers or pipe gallery in stone";
    public string Description => "Stone bed with rows of arch chambers (semi-ellipse section, or your manufacturer's stage–storage curve) or round pipes: bed size from rows, units per row, spacing and edge stone; storage at each depth = unit void + porosity × (bed volume − unit volume). Gives the stage–storage table, the total, and the units per row for a required volume, with a cover check.";
    public bool UsesNetwork => false;

    /// <summary>Area below depth <paramref name="y"/> of an upright semi-ellipse of width <paramref name="w"/> and height <paramref name="h"/> (base on the bottom).</summary>
    public static double ArchArea(double y, double w, double h)
    {
        if (y <= 0) return 0;
        double t = Math.Min(y / h, 1);
        return w / 2 * h * (Math.Asin(t) + t * Math.Sqrt(1 - t * t));
    }

    /// <summary>Area below depth <paramref name="y"/> of a circle of diameter <paramref name="d"/>.</summary>
    public static double PipeArea(double y, double d)
    {
        if (y <= 0) return 0;
        if (y >= d) return Math.PI * d * d / 4;
        double r = d / 2, th = 2 * Math.Acos((r - y) / r);
        return r * r * (th - Math.Sin(th)) / 2;
    }

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Underground detention. SI: m and m³.
        unit = arch
        width = 1.3
        height = 0.76
        length = 2.1
        rows = 6
        per row = 12
        spacing = 0.15
        edge = 0.3
        stone below = 0.15
        stone above = 0.15
        porosity = 0.40
        bottom = 30.00
        cover = 0.6
        min cover = 0.45
        required volume = 300
        """ : """
        # Underground detention bed. U.S.: ft and ft³.
        # unit = arch (semi-ellipse chamber: width, height, installed length), pipe (diameter, length) or curve (your manufacturer's [Unit curve]).
        unit = arch
        width = 4.25           # chamber base width, ft
        height = 2.5           # chamber height, ft
        length = 7.0           # installed length per chamber, ft
        rows = 6
        per row = 12
        spacing = 0.5          # stone between rows, ft
        edge = 1.0             # stone beyond the outer rows and row ends, ft
        stone below = 0.5      # ft
        stone above = 0.5      # ft
        porosity = 0.40
        bottom = 100.00        # bed bottom elevation
        cover = 2.0            # fill over the top of stone, ft
        min cover = 1.5        # manufacturer's minimum cover, ft
        required volume = 10000   # ft³ (optional: finds the units per row)
        # For unit = curve, give the manufacturer's bare-unit table (depth from the unit bottom, cumulative volume per unit):
        # [Unit curve]
        # Depth	Volume
        # 0	0
        # 0.5	3.5
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Underground detention";
        bool si = context.Units == UnitSystem.SI;
        string L = si ? "m" : "ft", V = si ? "m³" : "ft³";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        string unit = (p.Text("unit") ?? "arch").Trim().ToLowerInvariant();
        double w = p.Number("width", double.NaN, "width"), h = p.Number("height", double.NaN, "height"), d = p.Number("diameter", double.NaN, "diameter");
        double len = p.Number("length", double.NaN, "length");
        int rows = (int)p.Number("rows", 1, "rows"), per = (int)p.Number("per row", double.NaN, "per row");
        double sp = p.Number("spacing", si ? 0.15 : 0.5, "spacing"), edge = p.Number("edge", si ? 0.3 : 1.0, "edge");
        double sb = p.Number("stone below", si ? 0.15 : 0.5, "stone below"), sa = p.Number("stone above", si ? 0.15 : 0.5, "stone above");
        double n = p.Number("porosity", 0.40, "porosity"), bottom = p.Number("bottom", 0, "bottom");
        double? cover = p.OptionalNumber("cover"), minCover = p.OptionalNumber("min cover", "min cover"), req = p.OptionalNumber("required volume", "required volume");
        var curve = new List<(double Y, double V)>();
        foreach (var r in p.Rows("Unit curve"))
        {
            double? y = p.Cell(r, 0, "depth", "Unit curve"), v = p.Cell(r, 1, "volume", "Unit curve");
            if (y is >= 0 && v is >= 0) curve.Add((y.Value, v.Value)); else msgs.Add(CalcMessage.Error("E-UD-001", "Unit curve rows need a depth and a volume, zero or more."));
        }
        curve.Sort((a, b) => a.Y.CompareTo(b.Y));
        double uw, uh;
        Func<double, double> unitVol;                                       // void volume of one unit below depth y (from the unit bottom)
        switch (unit)
        {
            case "arch":
                if (!(w > 0) || !(h > 0) || !(len > 0)) msgs.Add(CalcMessage.Error("E-UD-002", "An arch chamber needs width, height and length above zero."));
                uw = w; uh = h; unitVol = y => len * ArchArea(y, w, h);
                break;
            case "pipe":
                if (!(d > 0) || !(len > 0)) msgs.Add(CalcMessage.Error("E-UD-003", "A pipe needs diameter and length above zero."));
                uw = d; uh = d; unitVol = y => len * PipeArea(y, d);
                break;
            case "curve":
                if (curve.Count < 2 || curve[0].Y != 0 || curve.Zip(curve.Skip(1)).Any(t => t.Second.V < t.First.V || t.Second.Y <= t.First.Y))
                    msgs.Add(CalcMessage.Error("E-UD-004", "unit = curve needs a [Unit curve] starting at depth 0 with depths increasing and volumes not decreasing."));
                if (!(w > 0) || !(len > 0)) msgs.Add(CalcMessage.Error("E-UD-005", "unit = curve still needs the unit width and installed length for the bed layout."));
                uw = w; uh = curve.Count > 0 ? curve[^1].Y : double.NaN;
                unitVol = y => Interp(curve, y);
                break;
            default:
                msgs.Add(CalcMessage.Error("E-UD-006", $"unit \"{unit}\" is not arch, pipe or curve."));
                uw = uh = double.NaN; unitVol = _ => double.NaN;
                break;
        }
        if (rows < 1 || per < 1) msgs.Add(CalcMessage.Error("E-UD-007", "rows and per row must be 1 or more."));
        if (!(n is > 0 and < 1)) msgs.Add(CalcMessage.Error("E-UD-008", "porosity must be between 0 and 1 (0.40 is usual for clean washed stone)."));
        if (sp < 0 || edge < 0 || sb < 0 || sa < 0) msgs.Add(CalcMessage.Error("E-UD-009", "spacing, edge and stone depths cannot be negative."));
        if (req is <= 0) msgs.Add(CalcMessage.Error("E-UD-010", "required volume must be above zero."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double depth = sb + uh + sa;
        double Storage(int perRow, double y)
        {
            double bedL = perRow * len + 2 * edge, bedW = rows * uw + (rows - 1) * sp + 2 * edge;
            double units = rows * perRow * unitVol(Math.Clamp(y - sb, 0, uh));
            return units + n * (bedL * bedW * y - units);
        }
        double bedLen = per * len + 2 * edge, bedWid = rows * uw + (rows - 1) * sp + 2 * edge;
        double total = Storage(per, depth), unitsAll = rows * per * unitVol(uh);
        var summary = new List<string>
        {
            $"{rows} rows × {per} = {rows * per} {(unit == "pipe" ? "pipes" : "chambers")}; bed {bedLen:F1} × {bedWid:F1} {L}, {depth:F2} {L} deep ({bottom:F2} to {bottom + depth:F2}).",
            $"Storage {total:F0} {V} ({unitsAll:F0} in the units, {total - unitsAll:F0} in the stone).",
        };
        if (req is { } rq)
        {
            int need = per;
            if (total < rq) { while (Storage(need, depth) < rq && need < 10000) need++; }
            else { while (need > 1 && Storage(need - 1, depth) >= rq) need--; }
            summary.Add(need >= 10000 ? $"Required {rq:F0} {V}: not reached; add rows." : $"Required {rq:F0} {V}: {need} per row ({rows * need} units, bed {need * len + 2 * edge:F1} {L} long) gives {Storage(need, depth):F0} {V}.");
            if (total < rq) msgs.Add(CalcMessage.Warning("W-UD-001", $"The bed as laid out stores {total:F0} {V}, less than the {rq:F0} {V} required; use {need} per row or add rows."));
        }
        if (cover is { } cv && minCover is { } mc && cv < mc) msgs.Add(CalcMessage.Warning("W-UD-002", $"Cover {cv:G3} {L} is under the {mc:G3} {L} minimum; lower the bed or use a lower-profile unit."));
        if (cover is null) msgs.Add(CalcMessage.Info("I-UD-001", "No cover given: check the manufacturer's minimum and maximum cover for the traffic load."));
        var rowsT = new List<IReadOnlyList<object?>>();
        var pts = new List<(double X, double Y)>();
        int steps = 20;
        for (int i = 0; i <= steps; i++)
        {
            double y = depth * i / steps;
            double u = rows * per * unitVol(Math.Clamp(y - sb, 0, uh)), s = Storage(per, y);
            rowsT.Add([R(bottom + y, 2), R(y, 3), R(u, 0), R(s - u, 0), R(s, 0)]);
            pts.Add((s, bottom + y));
        }
        var tables = new List<ToolTable>
        {
            new("Bed layout", ["Item", "Value", "Unit"],
            [
                ["Units", rows * per, ""], ["Rows × per row", $"{rows} × {per}", ""], ["Unit width × height × length", $"{uw:G4} × {uh:G4} × {len:G4}", L],
                ["Bed length", R(bedLen, 2), L], ["Bed width", R(bedWid, 2), L], ["Bed depth", R(depth, 2), L],
                ["Bed footprint", R(bedLen * bedWid, 0), si ? "m²" : "ft²"], ["Stone porosity", n, ""],
                ["Storage in units", R(unitsAll, 0), V], ["Storage in stone", R(total - unitsAll, 0), V], ["Total storage", R(total, 0), V],
                ["Total storage", R(si ? total : total / 43560, si ? 0 : 3), si ? V : "ac-ft"],
            ],
            [$"Bed length = per row × unit length + 2 × edge; width = rows × unit width + (rows − 1) × spacing + 2 × edge.",
             unit switch { "arch" => "Arch chamber section taken as a semi-ellipse (base width × height); use unit = curve with the manufacturer's table for the exact shape.", "pipe" => "Round pipe: exact circular segment areas; perforations and end caps not deducted.", _ => "Unit volume from your curve, interpolated linearly." }]),
            new("Stage–storage", [$"Elevation ({L})", $"Depth ({L})", $"In units ({V})", $"In stone ({V})", $"Total ({V})"], rowsT,
                [$"Storage = units' void + {n:G3} × (bed volume − units' volume) at each depth; unit walls taken as thin. Paste Elevation and Total into the detention routing tool."]),
        };
        msgs.Add(CalcMessage.Info("I-UD-002", "Infiltration from the bed bottom, the isolator/sediment row and inlet/outlet manifolds are not modelled; add them in routing and in the manufacturer's layout."));
        return new ToolResult(title, summary, tables, msgs)
        {
            Charts = [new ToolChart("Stage–storage", $"Storage ({V})", $"Elevation ({L})", [new ChartSeries("Total storage", pts)])],
        };
    }

    private static double Interp(List<(double Y, double V)> c, double y)
    {
        if (y <= c[0].Y) return c[0].V;
        for (int i = 1; i < c.Count; i++)
            if (y <= c[i].Y) return c[i - 1].V + (c[i].V - c[i - 1].V) * (y - c[i - 1].Y) / (c[i].Y - c[i - 1].Y);
        return c[^1].V;
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
