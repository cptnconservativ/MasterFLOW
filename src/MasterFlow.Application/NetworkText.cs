using System.Globalization;
using System.Text;
using MasterFlow.Domain;

namespace MasterFlow.Application;

/// <summary>
/// Text grids for storm network pipes and structures (tab, comma or semicolon separated; a non-numeric
/// first line is a header). Pipe sizes are typed in inches (U.S.) or millimetres (SI), as everywhere else
/// in MasterFlow, and stored in ft or m; that is the only conversion and it is stated in the header.
/// </summary>
public static class NetworkText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static double SizeToBase(UnitSystem u) => u == UnitSystem.SI ? 0.001 : 1.0 / 12.0;
    public static string SizeUnit(UnitSystem u) => u == UnitSystem.SI ? "mm" : "in";

    public static string PipeHeader(UnitSystem u) =>
        $"Pipe\tFrom (upstream)\tTo (downstream)\tShape\tSize/rise ({SizeUnit(u)})\tSpan ({SizeUnit(u)})\tLength\tInvert up\tInvert down\tn\tQ ({UnitConstants.For(u).Discharge})";
    public const string StructureHeader = "Structure\tType\tRim\tLoss method\tK";

    public static string FormatPipes(StormNetwork n)
    {
        var sb = new StringBuilder(PipeHeader(n.Units)).Append('\n');
        double k = SizeToBase(n.Units);
        foreach (var p in n.Pipes)
            sb.Append(string.Join('\t', p.Name, p.From, p.To, p.Shape, N(p.Rise / k), p.Shape == NetworkPipeShape.Elliptical ? N(p.Span / k) : "",
                N(p.Length), N(p.UpstreamInvert), N(p.DownstreamInvert), N(p.ManningN), N(p.Discharge))).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    public static string FormatStructures(StormNetwork n)
    {
        var sb = new StringBuilder(StructureHeader).Append('\n');
        foreach (var s in n.Structures)
            sb.Append(string.Join('\t', s.Name, s.Kind, s.RimElevation is { } r ? N(r) : "", s.LossMethod, s.JunctionK is { } kk ? N(kk) : "")).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    private static string N(double v) => double.IsFinite(v) ? v.ToString("0.######", Inv) : "";

    private static IEnumerable<(int Line, string[] Cells)> Rows(string text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (string.IsNullOrWhiteSpace(l) || l.TrimStart().StartsWith('#')) continue;
            char sep = l.Contains('\t') ? '\t' : l.Contains(';') ? ';' : ',';
            yield return (i + 1, l.Split(sep).Select(c => c.Trim()).ToArray());
        }
    }

    private static bool Num(string s, out double v) => double.TryParse(s, NumberStyles.Float, Inv, out v) || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);

    public static (IReadOnlyList<NetworkPipe> Pipes, IReadOnlyList<string> Errors) ParsePipes(string text, UnitSystem u)
    {
        var list = new List<NetworkPipe>(); var errs = new List<string>();
        double k = SizeToBase(u);
        foreach (var (line, c) in Rows(text))
        {
            if (c.Length < 11) { if (!(c.Length > 4 && !Num(c[4], out _))) errs.Add($"Line {line}: 11 columns expected ({string.Join(", ", PipeHeader(u).Split('\t'))})."); continue; }
            if (!Num(c[4], out _) && !Num(c[6], out _)) continue; // header
            var shape = c[3].StartsWith("E", StringComparison.OrdinalIgnoreCase) ? NetworkPipeShape.Elliptical : NetworkPipeShape.Circular;
            double Get(int i, string what, bool required = true)
            {
                if (Num(c[i], out double v)) return v;
                if (required || c[i].Length > 0) errs.Add($"Line {line}: {what} \"{c[i]}\" is not a number.");
                return double.NaN;
            }
            double rise = Get(4, "size") * k;
            double span = shape == NetworkPipeShape.Elliptical ? Get(5, "span") * k : rise;
            list.Add(new NetworkPipe
            {
                Name = c[0], From = c[1], To = c[2], Shape = shape, Rise = rise, Span = span, Length = Get(6, "length"),
                UpstreamInvert = Get(7, "upstream invert"), DownstreamInvert = Get(8, "downstream invert"), ManningN = Get(9, "n"), Discharge = Get(10, "Q", required: false),
            });
        }
        return (list, errs);
    }

    public static (IReadOnlyList<NetworkStructure> Structures, IReadOnlyList<string> Errors) ParseStructures(string text, IReadOnlyList<NetworkStructure>? previous = null)
    {
        var list = new List<NetworkStructure>(); var errs = new List<string>();
        foreach (var (line, c) in Rows(text))
        {
            if (c.Length >= 1 && c[0].Equals("Structure", StringComparison.OrdinalIgnoreCase)) continue;
            string Cell(int i) => i < c.Length ? c[i] : "";
            double? rim = null, kk = null;
            if (Cell(2).Length > 0) { if (Num(Cell(2), out double r)) rim = r; else errs.Add($"Line {line}: rim \"{Cell(2)}\" is not a number."); }
            if (Cell(4).Length > 0) { if (Num(Cell(4), out double kv) && kv >= 0) kk = kv; else errs.Add($"Line {line}: K \"{Cell(4)}\" is not a number of zero or more."); }
            var method = StructureLossMethod.Auto;
            if (Cell(3).Length > 0 && !Enum.TryParse(Cell(3), true, out method))
                errs.Add($"Line {line}: loss method \"{Cell(3)}\" is not one of {string.Join(", ", Enum.GetNames<StructureLossMethod>())}.");
            var old = previous?.FirstOrDefault(s => s.Name == c[0]);
            list.Add(new NetworkStructure { Name = c[0], Kind = Cell(1), RimElevation = rim, LossMethod = method, JunctionK = kk, Northing = old?.Northing, Easting = old?.Easting });
        }
        return (list, errs);
    }
}
