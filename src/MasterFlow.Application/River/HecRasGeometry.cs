using System.Globalization;
using System.Text;

namespace MasterFlow.Application.River;

/// <summary>
/// Reads cross-sections from a HEC-RAS geometry file (.g01 … .g99, plain text): river and reach, river station, reach
/// lengths (LOB, channel, ROB), station–elevation points, Manning's n by station, bank stations, contraction and expansion
/// coefficients and normal ineffective flow areas. Bridges, culverts, inline structures and lateral structures are
/// listed but not imported. Numbers in the tables are in 8-character fixed-width fields, as HEC-RAS writes them.
/// </summary>
public static class HecRasGeometry
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record Section(string River, string Reach, string Rs, double LengthLob, double LengthCh, double LengthRob,
        List<(double Sta, double Elev)> Points, List<(double Sta, double N)> Manning, double LeftBank, double RightBank,
        double Contraction, double Expansion, (double Sta, double Elev)? IneffLeft, (double Sta, double Elev)? IneffRight);

    public sealed record Result(string Title, List<Section> Sections, List<string> Skipped);

    public static Result Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        string title = "", river = "", reach = "";
        var sections = new List<Section>();
        var skipped = new List<string>();
        Section? cur = null;
        int i = 0;
        double[] Fixed(int count)
        {
            var vals = new List<double>();
            while (vals.Count < count && i + 1 < lines.Length)
            {
                string l = lines[++i];
                for (int c = 0; c + 1 <= l.Length && vals.Count < count; c += 8)
                {
                    string f = l.Substring(c, Math.Min(8, l.Length - c)).Trim();
                    if (f.Length == 0) continue;
                    vals.Add(double.Parse(f, NumberStyles.Float, Inv));
                }
            }
            return [.. vals];
        }
        void Flush() { if (cur is not null) sections.Add(cur); cur = null; }
        for (; i < lines.Length; i++)
        {
            string line = lines[i];
            int eq = line.IndexOf('=');
            string key = eq > 0 ? line[..eq].Trim() : line.Trim(), val = eq > 0 ? line[(eq + 1)..] : "";
            switch (key)
            {
                case "Geom Title": title = val.Trim(); break;
                case "River Reach":
                {
                    Flush();
                    var parts = val.Split(',');
                    river = parts[0].Trim(); reach = parts.Length > 1 ? parts[1].Trim() : "";
                    break;
                }
                case "Type RM Length L Ch R":
                {
                    Flush();
                    var parts = val.Split(',').Select(s => s.Trim()).ToArray();
                    int type = int.Parse(parts[0], Inv);
                    string rs = parts.Length > 1 ? parts[1].TrimEnd('*').Trim() : "";
                    double Num(int k) => k < parts.Length && double.TryParse(parts[k], NumberStyles.Float, Inv, out double v) ? v : 0;
                    if (type == 1) cur = new Section(river, reach, rs, Num(2), Num(3), Num(4), [], [], double.NaN, double.NaN, 0.1, 0.3, null, null);
                    else skipped.Add($"{river} {reach} RS {rs}: {(type switch { 2 => "culvert", 3 => "bridge", 4 => "multiple opening", 5 => "inline structure", 6 => "lateral structure", _ => $"node type {type}" })} not imported");
                    break;
                }
                case "#Sta/Elev" when cur is not null:
                {
                    int n = int.Parse(val.Trim(), Inv);
                    var v = Fixed(2 * n);
                    for (int k = 0; k + 1 < v.Length; k += 2) cur.Points.Add((v[k], v[k + 1]));
                    break;
                }
                case "#Mann" when cur is not null:
                {
                    int n = int.Parse(val.Split(',')[0].Trim(), Inv);
                    var v = Fixed(3 * n);
                    for (int k = 0; k + 2 < v.Length + 1 && k + 1 < v.Length; k += 3) cur.Manning.Add((v[k], v[k + 1]));
                    break;
                }
                case "Bank Sta" when cur is not null:
                {
                    var b = val.Split(',').Select(s => double.Parse(s.Trim(), NumberStyles.Float, Inv)).ToArray();
                    cur = cur with { LeftBank = b[0], RightBank = b[1] };
                    break;
                }
                case "Exp/Cntr" when cur is not null:
                {
                    var b = val.Split(',').Select(s => double.Parse(s.Trim(), NumberStyles.Float, Inv)).ToArray();
                    cur = cur with { Expansion = b[0], Contraction = b[1] };
                    break;
                }
                case "#XS Ineff" when cur is not null:
                {
                    var hdr = val.Split(',');
                    int n = int.Parse(hdr[0].Trim(), Inv);
                    var v = Fixed(3 * n);
                    double first = cur.Points.Count > 0 ? cur.Points[0].Sta : double.NegativeInfinity, last = cur.Points.Count > 0 ? cur.Points[^1].Sta : double.PositiveInfinity;
                    for (int k = 0; k + 2 < v.Length; k += 3)
                    {
                        if (v[k] <= first + 1e-6) cur = cur with { IneffLeft = (v[k + 1], v[k + 2]) };
                        else if (v[k + 1] >= last - 1e-6) cur = cur with { IneffRight = (v[k], v[k + 2]) };
                        else skipped.Add($"{cur.River} {cur.Reach} RS {cur.Rs}: an ineffective area from {v[k]:G6} to {v[k + 1]:G6} inside the section is not imported (only areas from the ends)");
                    }
                    if (hdr.Length > 1 && hdr[1].Trim() == "-1") skipped.Add($"{cur.River} {cur.Reach} RS {cur.Rs}: blocked ineffective areas imported as normal ones");
                    break;
                }
                case "Levee" when cur is not null:
                    skipped.Add($"{cur.River} {cur.Reach} RS {cur.Rs}: levees not imported"); break;
            }
        }
        Flush();
        return new Result(title, sections, skipped);
    }

    /// <summary>The sections as river-profile tool input: [Sections] and [Points] (with an n column), for one river and reach.</summary>
    public static string ToToolInput(IEnumerable<Section> sections)
    {
        var sb = new StringBuilder();
        var list = sections.ToList();
        sb.AppendLine("[Sections]");
        sb.AppendLine("RS\tLeft bank\tRight bank\tLOB length\tCh length\tROB length\tn LOB\tn Ch\tn ROB\tIneff left sta\tIneff left el\tIneff right sta\tIneff right el\tContraction\tExpansion");
        foreach (var s in list)
            sb.AppendLine(string.Join('\t', s.Rs, F(s.LeftBank), F(s.RightBank), F(s.LengthLob), F(s.LengthCh), F(s.LengthRob), "", "", "",
                s.IneffLeft is { } a ? F(a.Sta) : "", s.IneffLeft is { } b ? F(b.Elev) : "", s.IneffRight is { } c ? F(c.Sta) : "", s.IneffRight is { } d ? F(d.Elev) : "", F(s.Contraction), F(s.Expansion)));
        sb.AppendLine().AppendLine("[Points]").AppendLine("RS\tStation\tElevation\tn");
        foreach (var s in list)
            foreach (var (sta, el) in s.Points)
            {
                var m = s.Manning.Where(x => Math.Abs(x.Sta - sta) < 1e-6).Select(x => (double?)x.N).FirstOrDefault();
                sb.AppendLine(string.Join('\t', s.Rs, F(sta), F(el), m is { } nn ? F(nn) : ""));
            }
        return sb.ToString();
    }

    /// <summary>Insert Manning break stations that are not survey points (HEC-RAS allows them) so the n column can start there.</summary>
    public static Section WithManningPoints(Section s)
    {
        var pts = s.Points.ToList();
        foreach (var (sta, _) in s.Manning)
        {
            if (pts.Any(p => Math.Abs(p.Sta - sta) < 1e-6) || sta < pts[0].Sta || sta > pts[^1].Sta) continue;
            int k = pts.FindIndex(p => p.Sta > sta);
            var (x0, z0) = pts[k - 1]; var (x1, z1) = pts[k];
            pts.Insert(k, (sta, z0 + (z1 - z0) * (sta - x0) / (x1 - x0)));
        }
        return s with { Points = pts };
    }

    private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("G10", Inv);
}
