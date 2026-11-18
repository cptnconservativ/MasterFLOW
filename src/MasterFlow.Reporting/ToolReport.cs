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
        foreach (var ch in r.Charts) sb.Append($"<h2>{E(ch.Title)}</h2>").Append(ChartSvg(ch));
        if (r.Messages.Count > 0)
        {
            sb.Append("<h2>Messages</h2>");
            foreach (var m in r.Messages) sb.Append($"<div class=\"msg {m.Severity}\"><strong>{m.Severity}</strong> [{E(m.Code)}] {E(m.Text)}</div>");
        }
        sb.Append($"<h2>Input</h2><pre>{E(input)}</pre>");
        sb.Append("<p class=\"note\">Computed by MasterFlow. The responsible engineer must review inputs, assumptions and results before use.</p></body></html>");
        return sb.ToString();
    }

    private static readonly string[] Palette = ["#1f5f8b", "#b7791f", "#2f855a", "#9b2c2c", "#6b46c1", "#4a5568"];

    /// <summary>An inline SVG line chart (linear or log axes; log axes use 1-2-5 gridlines).</summary>
    public static string ChartSvg(ToolChart ch)
    {
        static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var pts = ch.Series.SelectMany(s => s.Points).Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && (!ch.LogX || p.X > 0) && (!ch.LogY || p.Y > 0)).ToList();
        if (pts.Count == 0) return "";
        const double W = 680, H = 380, L = 64, R = 150, T = 14, B = 46;
        var (xs, x0, x1) = Axis(pts.Select(p => p.X), ch.LogX);
        var (ys, y0, y1) = Axis(pts.Select(p => p.Y), ch.LogY);
        double Tx(double v) => ch.LogX ? Math.Log10(v) : v;
        double Ty(double v) => ch.LogY ? Math.Log10(v) : v;
        double Px(double v) => L + (Tx(v) - Tx(x0)) / (Tx(x1) - Tx(x0)) * (W - L - R);
        double Py(double v) => H - B - (Ty(v) - Ty(y0)) / (Ty(y1) - Ty(y0)) * (H - T - B);
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(W)}\" height=\"{N(H)}\" viewBox=\"0 0 {N(W)} {N(H)}\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"11\">");
        sb.Append($"<rect x=\"{N(L)}\" y=\"{N(T)}\" width=\"{N(W - L - R)}\" height=\"{N(H - T - B)}\" fill=\"#fff\" stroke=\"#9aa7b4\"/>");
        foreach (var tx in xs)
        {
            double px = Px(tx); if (px < L - 0.5 || px > W - R + 0.5) continue;
            sb.Append($"<line x1=\"{N(px)}\" y1=\"{N(T)}\" x2=\"{N(px)}\" y2=\"{N(H - B)}\" stroke=\"#e3e8ee\"/><text x=\"{N(px)}\" y=\"{N(H - B + 14)}\" text-anchor=\"middle\" fill=\"#5b6876\">{N(tx)}</text>");
        }
        foreach (var ty in ys)
        {
            double py = Py(ty); if (py < T - 0.5 || py > H - B + 0.5) continue;
            sb.Append($"<line x1=\"{N(L)}\" y1=\"{N(py)}\" x2=\"{N(W - R)}\" y2=\"{N(py)}\" stroke=\"#e3e8ee\"/><text x=\"{N(L - 5)}\" y=\"{N(py + 4)}\" text-anchor=\"end\" fill=\"#5b6876\">{N(ty)}</text>");
        }
        sb.Append($"<text x=\"{N(L + (W - L - R) / 2)}\" y=\"{N(H - 8)}\" text-anchor=\"middle\">{WebUtility.HtmlEncode(ch.XLabel)}</text>");
        sb.Append($"<text transform=\"translate(14 {N(T + (H - T - B) / 2)}) rotate(-90)\" text-anchor=\"middle\">{WebUtility.HtmlEncode(ch.YLabel)}</text>");
        int k = 0;
        foreach (var s in ch.Series)
        {
            string col = Palette[k % Palette.Length];
            var sp = s.Points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && (!ch.LogX || p.X > 0) && (!ch.LogY || p.Y > 0)).ToList();
            if (s.MarkersOnly) foreach (var p in sp) sb.Append($"<circle cx=\"{N(Px(p.X))}\" cy=\"{N(Py(p.Y))}\" r=\"3.5\" fill=\"{col}\"/>");
            else if (sp.Count > 1) sb.Append($"<polyline fill=\"none\" stroke=\"{col}\" stroke-width=\"2\" points=\"{string.Join(" ", sp.Select(p => $"{N(Px(p.X))},{N(Py(p.Y))}"))}\"/>");
            double ly = T + 12 + 18 * k;
            sb.Append(s.MarkersOnly ? $"<circle cx=\"{N(W - R + 18)}\" cy=\"{N(ly - 4)}\" r=\"3.5\" fill=\"{col}\"/>" : $"<line x1=\"{N(W - R + 10)}\" y1=\"{N(ly - 4)}\" x2=\"{N(W - R + 26)}\" y2=\"{N(ly - 4)}\" stroke=\"{col}\" stroke-width=\"2\"/>");
            sb.Append($"<text x=\"{N(W - R + 32)}\" y=\"{N(ly)}\">{WebUtility.HtmlEncode(s.Name)}</text>");
            k++;
        }
        return sb.Append("</svg>").ToString();
    }

    /// <summary>Gridline values and axis range: 1-2-5 per decade on a log axis (range = the data); about 6 round steps from zero on a linear axis.</summary>
    private static (List<double> Ticks, double Lo, double Hi) Axis(IEnumerable<double> values, bool log)
    {
        var v = values.ToList();
        double lo = v.Min(), hi = v.Max();
        var ticks = new List<double>();
        if (log)
        {
            if (hi <= lo) { lo /= 2; hi *= 2; }
            for (int e = (int)Math.Floor(Math.Log10(lo)) - 1; e <= (int)Math.Ceiling(Math.Log10(hi)); e++)
                foreach (var m in new[] { 1.0, 2, 5 }) { double t = m * Math.Pow(10, e); if (t >= lo * (1 - 1e-9) && t <= hi * (1 + 1e-9)) ticks.Add(t); }
            return (ticks, lo, hi);
        }
        lo = Math.Min(0, lo);
        if (hi <= lo) hi = lo + 1;
        double step = Math.Pow(10, Math.Floor(Math.Log10((hi - lo) / 6)));
        foreach (var m in new[] { 1.0, 2, 5, 10 }) if ((hi - lo) / (step * m) <= 7) { step *= m; break; }
        hi = Math.Ceiling(hi / step) * step;
        for (double t = Math.Ceiling(lo / step) * step; t <= hi + 1e-9 * step; t += step) ticks.Add(Math.Round(t, 10));
        return (ticks, lo, hi);
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
