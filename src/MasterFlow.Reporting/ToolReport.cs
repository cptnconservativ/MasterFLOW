using System.Net;
using System.Text;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.Reporting;

/// <summary>Design tool results as a printable HTML page and as workbook sheets.</summary>
public static class ToolReport
{
    public static string ToHtml(ProjectInfo project, ToolResult r, string input, string softwareVersion, DateTimeOffset? when = null)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E(r.Title)).Append("</title><style>")
          .Append("body{font-family:Segoe UI,Arial,sans-serif;color:#1d2733;margin:24px;font-size:13px}h1{font-size:20px;margin:0 0 4px}h2{font-size:15px;color:#1f5f8b;border-bottom:1px solid #c9d3dd;padding-bottom:3px;margin-top:20px}")
          .Append("table{border-collapse:collapse;margin:6px 0}th,td{border-bottom:1px solid #dde3ea;padding:3px 7px;text-align:left;vertical-align:top}th{background:#eef3f8;font-weight:600}td.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}")
          .Append(".note{color:#5b6876;font-size:12px;margin:2px 0}.msg{padding:4px 8px;margin:3px 0;border-left:3px solid #1f5f8b;background:#eef5fb}.msg.Warning{border-color:#b7791f;background:#fff7e0}.msg.Error{border-color:#b42318;background:#fdecea}")
          .Append("pre{background:#f5f7f9;padding:8px;font-size:11px;white-space:pre-wrap}.sub{color:#5b6876}@media print{body{margin:0}}</style></head><body>");
        sb.Append($"<h1>{E(r.Title)}</h1><div class=\"sub\">{E(project.Name)} {E(project.ProjectNumber)} · Prepared by {E(project.PreparedBy)} · Checked by {E(project.CheckedBy)} · {(when ?? DateTimeOffset.Now):yyyy-MM-dd HH:mm} · MasterFlow {E(softwareVersion)}</div>");
        foreach (var s in r.Summary) sb.Append($"<p>{E(s)}</p>");
        foreach (var t in r.Tables)
        {
            sb.Append($"<h2>{E(t.Title)}</h2><table><tr>");
            foreach (var h in t.Headers) sb.Append($"<th>{E(h)}</th>");
            sb.Append("</tr>");
            foreach (var row in t.Rows)
            {
                sb.Append("<tr>");
                foreach (var c in row) sb.Append(c is double ? $"<td class=\"num\">{E(DesignTools.Cell(c))}</td>" : $"<td>{E(DesignTools.Cell(c))}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</table>");
            foreach (var n in t.Notes ?? []) sb.Append($"<div class=\"note\">{E(n)}</div>");
        }
        if (r.Messages.Count > 0)
        {
            sb.Append("<h2>Messages</h2>");
            foreach (var m in r.Messages) sb.Append($"<div class=\"msg {m.Severity}\"><strong>{m.Severity}</strong> [{E(m.Code)}] {E(m.Text)}</div>");
        }
        sb.Append($"<h2>Input</h2><pre>{E(input)}</pre>");
        sb.Append("<p class=\"note\">Computed by MasterFlow. The responsible engineer must review inputs, assumptions and results before use.</p></body></html>");
        return sb.ToString();
    }

    /// <summary>One sheet per table (names made unique and kept within Excel's 31 characters), then a messages sheet.</summary>
    public static IReadOnlyList<XlsxSheet> ToSheets(ToolResult r)
    {
        var sheets = new List<XlsxSheet>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in r.Tables)
        {
            var rows = new List<IReadOnlyList<object?>> { t.Headers.Cast<object?>().ToArray() };
            rows.AddRange(t.Rows.Select(row => (IReadOnlyList<object?>)row.Select(c => c is double d && !double.IsFinite(d) ? null : c).ToArray()));
            foreach (var n in t.Notes ?? []) rows.Add([n]);
            sheets.Add(new XlsxSheet(Unique(t.Title, used), rows, 1, t.Headers.Select(h => Math.Max(10.0, Math.Min(40, h.Length + 2))).ToList()));
        }
        var m = new List<IReadOnlyList<object?>> { new object?[] { r.Title } };
        foreach (var s in r.Summary) m.Add([s]);
        m.Add([]);
        m.Add(["Severity", "Code", "Message"]);
        foreach (var x in r.Messages) m.Add([x.Severity.ToString(), x.Code, x.Text]);
        sheets.Add(new XlsxSheet(Unique("Messages", used), m, 1, [12, 12, 100]));
        return sheets;
    }

    private static string Unique(string title, HashSet<string> used)
    {
        var clean = new string(title.Select(ch => "[]:*?/\\".Contains(ch) ? ' ' : ch).ToArray()).Trim();
        if (clean.Length > 31) clean = clean[..31].Trim();
        string name = clean;
        for (int i = 2; !used.Add(name); i++) name = (clean.Length > 27 ? clean[..27] : clean) + $" ({i})";
        return name;
    }
}
