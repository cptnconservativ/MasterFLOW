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
        foreach (var f in r.Figures)
        {
            sb.Append($"<h2>{E(f.Title)}</h2>").Append(ToolFigureSvg.Render(f));
            foreach (var n in f.Notes ?? []) sb.Append($"<div class=\"note\">{E(n)}</div>");
        }
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

/// <summary>Draws a <see cref="ToolFigure"/> as inline SVG: true horizontal scale, vertical scale exaggerated to fit, gridded axes and a legend.</summary>
public static class ToolFigureSvg
{
    public static string Render(ToolFigure f, int width = 720, int height = 260)
    {
        var all = f.Series.SelectMany(s => s.Points).ToList();
        if (all.Count < 2) return "";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        const double padL = 52, padR = 16, padT = 12, padB = 54;
        double w = width - padL - padR, h = height - padT - padB;
        double xMin = all.Min(p => p.X), xMax = all.Max(p => p.X), yMin = Math.Min(0, all.Min(p => p.Y)), yMax = all.Max(p => p.Y);
        if (xMax <= xMin) xMax = xMin + 1;
        if (yMax <= yMin) yMax = yMin + 1;
        yMax += 0.08 * (yMax - yMin);
        static double Nice(double raw)
        {
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double r = raw / mag;
            return (r < 1.5 ? 1 : r < 3 ? 2 : r < 7 ? 5 : 10) * mag;
        }
        double gx = Nice((xMax - xMin) / 8), gy = Nice((yMax - yMin) / 4);
        string X(double x) => (padL + (x - xMin) / (xMax - xMin) * w).ToString("F1", inv);
        string Y(double y) => (padT + h - (y - yMin) / (yMax - yMin) * h).ToString("F1", inv);
        string Pts(IEnumerable<(double X, double Y)> ps) => string.Join(" ", ps.Select(p => $"{X(p.X)},{Y(p.Y)}"));
        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {width} {height}\" width=\"100%\" role=\"img\" aria-label=\"{WebUtility.HtmlEncode(f.Title)}\">");
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"#f6f8fa\"/>");
        for (double y = Math.Ceiling(yMin / gy) * gy; y <= yMax + 1e-9; y += gy)
            sb.Append($"<line x1=\"{X(xMin)}\" x2=\"{X(xMax)}\" y1=\"{Y(y)}\" y2=\"{Y(y)}\" stroke=\"#e3e8ee\"/><text x=\"{padL - 6}\" y=\"{Y(y)}\" dy=\"3\" font-size=\"10\" text-anchor=\"end\" fill=\"#5b6876\">{y.ToString("0.##", inv)}</text>");
        for (double x = Math.Ceiling(xMin / gx) * gx; x <= xMax + 1e-9; x += gx)
            sb.Append($"<line x1=\"{X(x)}\" x2=\"{X(x)}\" y1=\"{Y(yMin)}\" y2=\"{Y(yMax)}\" stroke=\"#e3e8ee\"/><text x=\"{X(x)}\" y=\"{(padT + h + 13).ToString("F1", inv)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#5b6876\">{x.ToString("0.##", inv)}</text>");
        string[] water = ["#a9d1ec", "#3f8fc7", "#1f5f8b"];
        int wi = 0;
        var legend = new List<(string Label, string Swatch)>();
        foreach (var s in f.Series.Where(s => s.Kind == ToolSeriesKind.Water))
        {
            string c = water[Math.Min(wi, water.Length - 1)];
            sb.Append($"<polygon points=\"{Pts(s.Points)}\" fill=\"{c}\" fill-opacity=\"0.85\"/>");
            if (!legend.Any(l => l.Label == s.Label)) { legend.Add((s.Label, $"<rect width=\"12\" height=\"8\" fill=\"{c}\"/>")); wi++; }
        }
        foreach (var s in f.Series.Where(s => s.Kind == ToolSeriesKind.Ground))
        {
            sb.Append($"<polyline points=\"{Pts(s.Points)}\" fill=\"none\" stroke=\"#1d2733\" stroke-width=\"2\"/>");
            legend.Add((s.Label, "<line x1=\"0\" x2=\"12\" y1=\"4\" y2=\"4\" stroke=\"#1d2733\" stroke-width=\"2\"/>"));
        }
        foreach (var s in f.Series.Where(s => s.Kind == ToolSeriesKind.Limit))
        {
            sb.Append($"<polyline points=\"{Pts(s.Points)}\" fill=\"none\" stroke=\"#b3261e\" stroke-width=\"1.3\" stroke-dasharray=\"5 3\"/>");
            if (s.Label.Length > 0) legend.Add((s.Label, "<line x1=\"0\" x2=\"12\" y1=\"4\" y2=\"4\" stroke=\"#b3261e\" stroke-width=\"1.3\" stroke-dasharray=\"4 2\"/>"));
        }
        sb.Append($"<text x=\"{(padL + w / 2).ToString("F1", inv)}\" y=\"{(padT + h + 27).ToString("F1", inv)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#5b6876\">{WebUtility.HtmlEncode(f.XLabel)}</text>");
        sb.Append($"<text x=\"12\" y=\"{(padT + h / 2).ToString("F1", inv)}\" font-size=\"10\" text-anchor=\"middle\" fill=\"#5b6876\" transform=\"rotate(-90 12 {(padT + h / 2).ToString("F1", inv)})\">{WebUtility.HtmlEncode(f.YLabel)}</text>");
        double lx = padL;
        foreach (var (label, swatch) in legend)
        {
            sb.Append($"<g transform=\"translate({lx.ToString("F1", inv)},{(height - 12).ToString("F1", inv)})\">{swatch}<text x=\"16\" y=\"8\" font-size=\"10\" fill=\"#1d2733\">{WebUtility.HtmlEncode(label)}</text></g>");
            lx += 24 + 5.5 * label.Length;
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
