using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application;

/// <summary>One row of a side-by-side comparison: a quantity and its value in each alternative (null = not applicable or no result).</summary>
public sealed record ComparisonRow(string Group, string Label, string Symbol, string Unit, int Decimals, IReadOnlyList<double?> Values)
{
    public IReadOnlyList<string> Cells => Values.Select((_, i) => Format(i)).ToList();

    public string Format(int column) => Values[column] is { } v && double.IsFinite(v)
        ? v.ToString("F" + Decimals, System.Globalization.CultureInfo.CurrentCulture) : "—";
}

public sealed record ComparisonTable(IReadOnlyList<string> Titles, IReadOnlyList<string> Statuses, IReadOnlyList<ComparisonRow> Rows, IReadOnlyList<string> Notes);

/// <summary>
/// Side-by-side comparison of alternatives. Every quantity that appears in any alternative becomes a row,
/// in first-seen order. Values are only compared within one unit system: if alternatives mix systems the
/// unit column shows both and a note says so (no hidden conversion).
/// </summary>
public static class Comparison
{
    public static ComparisonTable Build(IReadOnlyList<(string Title, UniformFlowResult Result)> items)
    {
        var order = new List<(string Group, string Label)>();
        var meta = new Dictionary<(string, string), (string Symbol, HashSet<string> Units, int Decimals)>();
        var perItem = new List<Dictionary<(string, string), double>>();
        foreach (var (_, r) in items)
        {
            var map = new Dictionary<(string, string), double>();
            foreach (var l in ResultSummary.Lines(r))
            {
                var key = (l.Group, l.Label);
                if (!meta.TryGetValue(key, out var m)) { meta[key] = m = (l.Symbol, [], l.Decimals); order.Add(key); }
                m.Units.Add(l.Unit);
                map[key] = l.Value;
            }
            perItem.Add(map);
        }
        var rows = order.Select(k => new ComparisonRow(k.Group, k.Label, meta[k].Symbol, string.Join(" / ", meta[k].Units.Where(u => u.Length > 0)), meta[k].Decimals,
            perItem.Select(m => m.TryGetValue(k, out double v) ? v : (double?)null).ToArray())).ToList();

        var notes = new List<string>();
        if (items.Select(i => i.Result.Input.Units).Distinct().Count() > 1)
            notes.Add("Alternatives use different unit systems; values are shown in each alternative's own units and are not converted.");
        if (items.Any(i => !i.Result.Succeeded))
            notes.Add("Alternatives without a result show no values; see their messages.");
        var statuses = items.Select(i => i.Result.Succeeded
            ? $"{i.Result.Input.Section.ShapeName}; {ResultSummary.RegimeText(i.Result)}; {i.Result.Warnings.Count()} warning(s)"
            : $"{i.Result.Input.Section.ShapeName}; no result").ToList();
        return new ComparisonTable(items.Select(i => i.Title).ToList(), statuses, rows, notes);
    }
}
