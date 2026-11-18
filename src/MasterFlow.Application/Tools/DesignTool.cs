using System.Globalization;
using System.Text;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>What a design tool can see: the project's unit system and, when one is open, the selected storm network and its solution.</summary>
public sealed record DesignToolContext(UnitSystem Units, StormNetwork? Network = null, StormNetworkResult? NetworkResult = null);

/// <summary>A result table: shown in the window, the HTML report and as an Excel sheet. Cells are numbers or text.</summary>
public sealed record ToolTable(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<string>? Notes = null);

/// <summary>
/// The outcome of running a tool. Messages carry every error, warning and assumption; a tool with an error
/// returns no numbers for the failed part. A tool that can change the selected network returns it in
/// <see cref="UpdatedNetwork"/>; nothing is changed until the user applies it.
/// </summary>
public sealed record ToolResult(string Title, IReadOnlyList<string> Summary, IReadOnlyList<ToolTable> Tables, IReadOnlyList<CalcMessage> Messages)
{
    public StormNetwork? UpdatedNetwork { get; init; }
    public string? UpdateDescription { get; init; }
    public bool Succeeded => Messages.All(m => m.Severity != MessageSeverity.Error);

    public static ToolResult Failed(string title, params CalcMessage[] errors) => new(title, [], [], errors);
}

/// <summary>
/// A design tool: text input (key = value lines and tab- or comma-separated tables under [Section]
/// headings), a sample input, and a run that returns tables, a summary and messages. Tools are found by
/// reflection, so adding one needs no registration.
/// </summary>
public interface IDesignTool
{
    string Id { get; }
    string Name { get; }
    /// <summary>One or two sentences shown above the input: what the tool does and its source (e.g. Rapid City IDCM 2012 §4.2).</summary>
    string Description { get; }
    /// <summary>True when the tool reads or changes the selected storm network.</summary>
    bool UsesNetwork { get; }
    string SampleInput(DesignToolContext context);
    ToolResult Run(string input, DesignToolContext context);
}

/// <summary>All design tools in this assembly, ordered by name.</summary>
public static class DesignTools
{
    private static readonly Lazy<IReadOnlyList<IDesignTool>> _all = new(() =>
        typeof(DesignTools).Assembly.GetTypes()
            .Where(t => typeof(IDesignTool).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false } && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IDesignTool)Activator.CreateInstance(t)!)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList());

    public static IReadOnlyList<IDesignTool> All => _all.Value;
    public static IDesignTool? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>Plain-text rendering with aligned columns, for the window and the clipboard.</summary>
    public static string ToText(ToolResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Title).AppendLine(new string('=', Math.Min(78, r.Title.Length)));
        foreach (var s in r.Summary) sb.AppendLine(s);
        foreach (var t in r.Tables)
        {
            sb.AppendLine().AppendLine(t.Title);
            var cells = new List<string[]> { t.Headers.ToArray() };
            cells.AddRange(t.Rows.Select(row => row.Select(Cell).ToArray()));
            int cols = cells.Max(c => c.Length);
            var w = Enumerable.Range(0, cols).Select(i => cells.Max(c => i < c.Length ? c[i].Length : 0)).ToArray();
            foreach (var (c, k) in cells.Select((c, k) => (c, k)))
            {
                sb.AppendLine(string.Join("  ", c.Select((x, i) => i == 0 || !IsNum(x) ? x.PadRight(w[i]) : x.PadLeft(w[i]))).TrimEnd());
                if (k == 0) sb.AppendLine(string.Join("  ", w.Select(x => new string('-', x))));
            }
            foreach (var n in t.Notes ?? []) sb.AppendLine("  " + n);
        }
        if (r.Messages.Count > 0)
        {
            sb.AppendLine().AppendLine("Messages");
            foreach (var m in r.Messages) sb.AppendLine($"  {m.Severity} [{m.Code}] {m.Text}");
        }
        return sb.ToString();
    }

    /// <summary>Tab-separated tables for pasting into Excel.</summary>
    public static string ToDelimited(ToolResult r)
    {
        var sb = new StringBuilder();
        foreach (var t in r.Tables)
        {
            sb.AppendLine(t.Title).AppendLine(string.Join('\t', t.Headers));
            foreach (var row in t.Rows) sb.AppendLine(string.Join('\t', row.Select(Cell)));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Cell(object? v) => v switch
    {
        null => "",
        double d when !double.IsFinite(d) => "—",
        double d => d.ToString(Math.Abs(d) >= 1000 ? "0.#" : Math.Abs(d) >= 1 || d == 0 ? "0.###" : "0.####", CultureInfo.InvariantCulture),
        bool b => b ? "yes" : "no",
        _ => v.ToString() ?? "",
    };

    private static bool IsNum(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || s == "—";
}

/// <summary>
/// Parsed tool input. Lines before the first [Section] and "key = value" lines inside sections are
/// parameters; other lines in a section form a table whose first line is the header. Columns are split on
/// tabs, or on commas when a line has no tab. Blank lines and lines starting with # are ignored, as is " # comment" after a value. Keys and
/// section names are case-insensitive.
/// </summary>
public sealed class ToolInput
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string[]>> _tables = new(StringComparer.OrdinalIgnoreCase);
    public List<CalcMessage> Errors { get; } = [];

    public static ToolInput Parse(string text)
    {
        var p = new ToolInput();
        string section = "";
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); p._tables.TryAdd(section, []); continue; }
            int eq = line.IndexOf('=');
            if (eq > 0 && !line.Contains('\t') && (section == "" || !line[..eq].Contains(',')))
            {
                string key = (section == "" ? "" : section + ".") + line[..eq].Trim();
                string value = line[(eq + 1)..];
                int hash = value.IndexOf(" #", StringComparison.Ordinal);   // trailing comment
                p._values[key] = (hash >= 0 ? value[..hash] : value).Trim();
                continue;
            }
            if (section == "") { p.Errors.Add(CalcMessage.Error("E-TL-001", $"\"{line}\" is not a \"key = value\" line and is not under a [Section] heading.")); continue; }
            var cols = (line.Contains('\t') ? raw.Split('\t') : line.Split(',')).Select(c => c.Trim()).ToArray();
            p._tables[section].Add(cols);
        }
        return p;
    }

    public bool Has(string key) => _values.ContainsKey(key);
    public string? Text(string key) => _values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    /// <summary>A number parameter; a missing value gives the default, an unreadable one is reported.</summary>
    public double Number(string key, double fallback, string? what = null)
    {
        if (Text(key) is not { } t) return fallback;
        if (TryNumber(t, out double v)) return v;
        Errors.Add(CalcMessage.Error("E-TL-002", $"{what ?? key} \"{t}\" is not a number."));
        return double.NaN;
    }

    public double? OptionalNumber(string key, string? what = null)
    {
        if (Text(key) is not { } t) return null;
        if (TryNumber(t, out double v)) return v;
        Errors.Add(CalcMessage.Error("E-TL-002", $"{what ?? key} \"{t}\" is not a number."));
        return null;
    }

    public bool Flag(string key, bool fallback) => Text(key)?.ToLowerInvariant() switch
    {
        "yes" or "y" or "true" or "1" or "on" => true,
        "no" or "n" or "false" or "0" or "off" => false,
        _ => fallback,
    };

    /// <summary>Rows of a table section, header excluded; empty when the section is absent.</summary>
    public IReadOnlyList<string[]> Rows(string section) => _tables.TryGetValue(section, out var t) && t.Count > 1 ? t.Skip(1).ToList() : [];
    public IReadOnlyList<string> Header(string section) => _tables.TryGetValue(section, out var t) && t.Count > 0 ? t[0] : [];
    public bool HasSection(string section) => _tables.ContainsKey(section);

    public static bool TryNumber(string? s, out double v)
    {
        s = s?.Trim().TrimEnd('%');
        return double.TryParse(s, NumberStyles.Float, Inv, out v) || double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);
    }

    /// <summary>Cell <paramref name="i"/> of a row as a number (null when blank); an unreadable cell is reported with its row label.</summary>
    public double? Cell(string[] row, int i, string what, string rowLabel)
    {
        if (i >= row.Length || string.IsNullOrWhiteSpace(row[i])) return null;
        if (TryNumber(row[i], out double v)) return v;
        Errors.Add(CalcMessage.Error("E-TL-003", $"{rowLabel}: {what} \"{row[i]}\" is not a number."));
        return null;
    }
}
