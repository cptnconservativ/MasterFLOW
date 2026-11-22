using System.Globalization;
using System.Text.RegularExpressions;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Reports;

/// <summary>One live value used in a report's narrative: what it points at, what it resolves to now, and where it came from.</summary>
public sealed record LiveValue(string Reference, string? Text, double? Number, string Source, string? Problem)
{
    public bool Resolved => Problem is null;
}

/// <summary>
/// Live values for the living drainage report (D-230). A narrative sentence can carry a reference instead of a typed
/// number, and the reference is resolved from the current calculations every time the report is built:
/// <list type="bullet">
/// <item><c>{value: tool-id | table title | row | column}</c>: a cell of a design tool's result. The table is matched by the
/// start of its title (blank: the first table), the row by its first cell (or any cell), the column by its header.
/// Row can also be max, min, sum, avg, count, first, last or #n (nth row).</item>
/// <item><c>{network: name | pipe or structure | field}</c>: a solved storm network value. Fields: q, capacity, v, vup,
/// vfull, depth, hglup, hgldown, eglup, egldown, size, slope, length for pipes; rim, hgl, egl, clearance,
/// eglclearance, loss for structures. The item can be max, min, sum, avg or count over every pipe or structure.</item>
/// </list>
/// A number format can follow a # at the end: <c>{value: rational-rapid-city | Results | Basin A | Q100 #F1}</c>.
/// </summary>
public static partial class LiveValues
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Aggregates = ["max", "min", "sum", "avg", "count", "first", "last"];

    /// <summary>The canonical form of a reference (no braces, single spaces around separators, lower-case kind) used to match issued values.</summary>
    public static string Normalize(string reference)
    {
        var s = reference.Trim();
        if (s.StartsWith('{') && s.EndsWith('}')) s = s[1..^1];
        int colon = s.IndexOf(':');
        if (colon < 0) return s.Trim();
        var parts = s[(colon + 1)..].Split('|').Select(p => RegexSpace().Replace(p.Trim(), " "));
        return s[..colon].Trim().ToLowerInvariant() + ": " + string.Join(" | ", parts);
    }

    /// <summary>Resolves one reference (the text between the braces).</summary>
    public static LiveValue Resolve(string reference, Func<string, (IDesignTool Tool, ToolResult Result)?> tool,
        IReadOnlyList<(StormNetwork Network, StormNetworkResult? Result)> networks)
    {
        string norm = Normalize(reference);
        int colon = norm.IndexOf(':');
        string kind = norm[..colon], spec = norm[(colon + 1)..].Trim();
        string? format = null;
        int hash = spec.LastIndexOf('#');
        if (hash > spec.LastIndexOf('|') && hash < spec.Length - 1)
        {
            format = spec[(hash + 1)..].Trim(); spec = spec[..hash].Trim();
        }
        var parts = spec.Split('|').Select(p => p.Trim()).ToArray();
        LiveValue Fail(string why, string source = "") => new(norm, null, null, source, why);
        try
        {
            return kind switch
            {
                "value" => ToolValue(norm, parts, format, tool, Fail),
                "network" => NetworkValue(norm, parts, format, networks, Fail),
                _ => Fail($"unknown kind \"{kind}\"; use value: or network:"),
            };
        }
        catch (FormatException) { return Fail($"\"{format}\" is not a .NET number format (try F1, F2, N0 or 0.00)"); }
    }

    private static LiveValue ToolValue(string norm, string[] p, string? format, Func<string, (IDesignTool Tool, ToolResult Result)?> tool, Func<string, string, LiveValue> fail)
    {
        if (p.Length != 4) return fail("give tool id | table | row | column", "");
        var t = tool(p[0]);
        if (t is null) return fail($"{p[0]} has no saved input, is not licensed, or is not a design tool id", p[0]);
        var (tl, r) = t.Value;
        if (!r.Succeeded) return fail($"{tl.Name} has errors with its saved input", tl.Name);
        var table = p[1].Length == 0 ? r.Tables.FirstOrDefault()
            : r.Tables.FirstOrDefault(x => x.Title.Equals(p[1], StringComparison.OrdinalIgnoreCase))
              ?? r.Tables.FirstOrDefault(x => x.Title.StartsWith(p[1], StringComparison.OrdinalIgnoreCase));
        if (table is null) return fail($"no table of {tl.Name} starts with \"{p[1]}\" (tables: {string.Join("; ", r.Tables.Select(x => x.Title))})", tl.Name);
        int col = Column(table.Headers, p[3]);
        if (col < 0) return fail($"table \"{table.Title}\" has no column \"{p[3]}\" (columns: {string.Join("; ", table.Headers)})", tl.Name);
        string source = $"{tl.Name}: {table.Title}";
        string row = p[2].ToLowerInvariant();
        if (Aggregates.Contains(row) || RegexNth().IsMatch(row))
        {
            var rows = table.Rows.Where(x => col < x.Count).ToList();
            if (row == "count") return Done(norm, rows.Count, "N0", source);
            if (row == "first" || row == "last" || row.StartsWith('#'))
            {
                int i = row == "first" ? 0 : row == "last" ? rows.Count - 1 : int.Parse(row[1..], Inv) - 1;
                if (i < 0 || i >= rows.Count) return fail($"table \"{table.Title}\" has {rows.Count} rows", source);
                return Cell(norm, rows[i][col], format, source + ", " + DesignTools.Cell(rows[i][0]));
            }
            var nums = rows.Select(x => Num(x[col])).Where(x => x is not null).Select(x => x!.Value).ToList();
            if (nums.Count == 0) return fail($"column \"{table.Headers[col]}\" has no numbers", source);
            double v = row switch { "max" => nums.Max(), "min" => nums.Min(), "sum" => nums.Sum(), _ => nums.Average() };
            if (row is "max" or "min")
            {
                var at = rows.First(x => Num(x[col]) == v);
                return Cell(norm, at[col], format, source + $", {row} ({DesignTools.Cell(at[0])})");
            }
            return Done(norm, v, format, source + $", {row} of {nums.Count}");
        }
        var match = table.Rows.FirstOrDefault(x => x.Count > 0 && Same(x[0], p[2]))
                    ?? table.Rows.FirstOrDefault(x => x.Any(c => Same(c, p[2])));
        if (match is null) return fail($"table \"{table.Title}\" has no row \"{p[2]}\"", source);
        if (col >= match.Count) return fail($"row \"{p[2]}\" has no {table.Headers[col]} cell", source);
        return Cell(norm, match[col], format, source + ", " + DesignTools.Cell(match[0]));
    }

    private static readonly string[] PipeFields = ["q", "capacity", "v", "vup", "vfull", "depth", "hglup", "hgldown", "eglup", "egldown", "size", "slope", "length"];
    private static readonly string[] StructureFields = ["rim", "hgl", "egl", "clearance", "eglclearance", "loss"];

    private static LiveValue NetworkValue(string norm, string[] p, string? format, IReadOnlyList<(StormNetwork Network, StormNetworkResult? Result)> networks, Func<string, string, LiveValue> fail)
    {
        if (p.Length != 3) return fail("give network name | pipe or structure | field", "");
        var hits = networks.Where(n => p[0].Length == 0 || p[0] == "*" || n.Network.Name.Equals(p[0], StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count == 0) return fail($"no storm network is named \"{p[0]}\"", "");
        if (hits.Count > 1) return fail("the project has several storm networks; name one", "");
        var (net, res) = hits[0];
        string source = "Storm network " + net.Name;
        if (res is not { Succeeded: true }) return fail($"{net.Name} is not solved", source);
        string field = p[2].ToLowerInvariant().Replace(" ", "");
        bool pipe = PipeFields.Contains(field), structure = StructureFields.Contains(field);
        if (!pipe && !structure) return fail($"unknown field \"{p[2]}\" (pipes: {string.Join(", ", PipeFields)}; structures: {string.Join(", ", StructureFields)})", source);
        var values = pipe
            ? res.Pipes.Select(x => (x.Pipe.Name, V: PipeField(x, field)))
            : res.Structures.Select(x => (x.Structure.Name, V: StructureField(x, field)));
        var list = values.Where(x => x.V is { } d && double.IsFinite(d)).Select(x => (x.Name, V: x.V!.Value)).ToList();
        string item = p[1].ToLowerInvariant();
        if (Aggregates.Contains(item) && item is not "first" and not "last")
        {
            if (item == "count") return Done(norm, list.Count, "N0", source);
            if (list.Count == 0) return fail($"no {(pipe ? "pipe" : "structure")} has a {field} value", source);
            if (item is "max" or "min")
            {
                var at = item == "max" ? list.MaxBy(x => x.V) : list.MinBy(x => x.V);
                return Done(norm, at.V, format, $"{source}, {item} {field} ({at.Name})");
            }
            return Done(norm, item == "sum" ? list.Sum(x => x.V) : list.Average(x => x.V), format, $"{source}, {item} {field} of {list.Count}");
        }
        var named = values.FirstOrDefault(x => x.Name.Equals(p[1], StringComparison.OrdinalIgnoreCase));
        if (named.Name is null) return fail($"{net.Name} has no {(pipe ? "pipe" : "structure")} \"{p[1]}\"", source);
        if (named.V is not { } nv || !double.IsFinite(nv)) return fail($"{named.Name} has no {field} value", source);
        return Done(norm, nv, format, $"{source}, {named.Name} {field}");
    }

    private static double? PipeField(PipeHydraulicResult r, string f) => f switch
    {
        "q" => r.Pipe.Discharge, "capacity" => r.FullFlowCapacity, "v" => r.VelocityDown, "vup" => r.VelocityUp, "vfull" => r.FullFlowVelocity,
        "depth" => r.NormalDepth, "hglup" => r.HglUp, "hgldown" => r.HglDown, "eglup" => r.EglUp, "egldown" => r.EglDown,
        "size" => r.Pipe.Rise, "slope" => r.Slope, "length" => r.Pipe.Length, _ => null,
    };

    private static double? StructureField(StructureHydraulicResult r, string f) => f switch
    {
        "rim" => r.Structure.RimElevation, "hgl" => r.Hgl, "egl" => r.Egl, "clearance" => r.HglClearance, "eglclearance" => r.EglClearance, "loss" => r.Loss, _ => null,
    };

    private static int Column(IReadOnlyList<string> headers, string want)
    {
        for (int i = 0; i < headers.Count; i++) if (headers[i].Equals(want, StringComparison.OrdinalIgnoreCase)) return i;
        // "Q100" matches "Q100 (cfs)": header text before its unit.
        for (int i = 0; i < headers.Count; i++)
        {
            int paren = headers[i].IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0 && headers[i][..paren].Equals(want, StringComparison.OrdinalIgnoreCase)) return i;
        }
        for (int i = 0; i < headers.Count; i++) if (headers[i].StartsWith(want, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static bool Same(object? cell, string want) => DesignTools.Cell(cell).Trim().Equals(want, StringComparison.OrdinalIgnoreCase);

    /// <summary>The number in a table cell, or null (text cells such as "OK" or "—").</summary>
    public static double? Num(object? cell) => cell switch
    {
        double d when double.IsFinite(d) => d,
        float f => f, int i => i, long l => l, decimal m => (double)m,
        string s when double.TryParse(s.Trim(), NumberStyles.Float, Inv, out var d) => d,
        _ => null,
    };

    private static LiveValue Cell(string norm, object? cell, string? format, string source) =>
        Num(cell) is { } d ? Done(norm, d, format, source)
        : cell is null || DesignTools.Cell(cell).Length == 0 ? new(norm, null, null, source, "the cell is blank")
        : new(norm, DesignTools.Cell(cell), null, source, null);

    private static LiveValue Done(string norm, double v, string? format, string source) =>
        new(norm, format is null ? DesignTools.Cell(v) : v.ToString(format, Inv), v, source, null);

    /// <summary>True when the issued text and the current text show the same value (numbers compared as numbers, so 12.40 = 12.4).</summary>
    public static bool SameValue(string issued, string current)
    {
        if (string.Equals(issued.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return double.TryParse(issued.Replace(",", ""), NumberStyles.Float, Inv, out var a)
            && double.TryParse(current.Replace(",", ""), NumberStyles.Float, Inv, out var b) && a == b;
    }

    /// <summary>The [Issued values] lines of a template head: "reference = value", reference with or without braces.</summary>
    public static Dictionary<string, string> Issued(string head)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool inside = false;
        foreach (var raw in head.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { inside = line[1..^1].Trim().Equals("Issued values", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inside || line.Length == 0 || line.StartsWith('#')) continue;
            int close = line.IndexOf('}'), eq = line.IndexOf('=', Math.Max(close, 0));
            if (eq <= 0) continue;
            map[Normalize(line[..eq])] = line[(eq + 1)..].Trim();
        }
        return map;
    }

    /// <summary>Numbers typed in the narrative with an engineering unit after them (12.4 cfs, 0.74 ac-ft, 5.2 ft/s ...).</summary>
    public static IEnumerable<(string Number, string Unit, int Decimals)> TypedNumbers(string text)
    {
        foreach (Match m in RegexTyped().Matches(RegexLive().Replace(text, " ")))
        {
            string n = m.Groups[1].Value.Replace(",", "");
            int dot = n.IndexOf('.');
            yield return (n, m.Groups[2].Value, dot < 0 ? 0 : n.Length - dot - 1);
        }
    }

    [GeneratedRegex(@"\{\s*(value|network)\s*:([^{}]*)\}", RegexOptions.IgnoreCase)]
    public static partial Regex RegexLive();
    [GeneratedRegex(@"(?<![\w.§-])(\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s*(cfs|ac-ft|acre-feet|cf|cu\.? ?ft|acres|acre|ac|ft/s|fps|in/hr|cy)(?![\w/-])", RegexOptions.IgnoreCase)]
    private static partial Regex RegexTyped();
    [GeneratedRegex(@"^#\d+$")]
    private static partial Regex RegexNth();
    [GeneratedRegex(@"\s+")]
    private static partial Regex RegexSpace();
}
