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
        foreach (var ch in r.Charts) sb.Append($"<h2>{E(ch.Title)}</h2>").Append(ChartSvg(ch));
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

    private static readonly string[] SeriesColors = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300"];

    /// <summary>
    /// A static SVG line chart: one y axis, recessive grid, legend, the last point of each series labelled, and a
    /// tooltip (SVG title) on every point. Reference series are drawn dashed in grey and labelled at their left end.
    /// </summary>
    public static string ChartSvg(ToolChart ch)
    {
        static string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var pts = ch.Series.SelectMany(s => s.Points).Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
        if (pts.Count < 2) return "";
        const double W = 760, H = 360, L = 64, R = 190, T = 14, B = 46;
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        if (x1 <= x0) x1 = x0 + 1;
        if (y1 <= y0) y1 = y0 + 1;
        var (xt, xs) = Ticks(x0, x1); var (yt, ys) = Ticks(y0, y1);
        x0 = xt[0]; x1 = xt[^1]; y0 = yt[0]; y1 = yt[^1];
        double X(double v) => L + (v - x0) / (x1 - x0) * (W - L - R);
        double Y(double v) => H - B - (v - y0) / (y1 - y0) * (H - T - B);
        var sb = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W} {H}\" width=\"{W}\" style=\"max-width:100%;height:auto;font-family:Segoe UI,Arial,sans-serif;font-size:11px\" role=\"img\" aria-label=\"{E(ch.Title)}\">");
        foreach (var t in yt) sb.Append($"<line x1=\"{L}\" x2=\"{W - R}\" y1=\"{F(Y(t))}\" y2=\"{F(Y(t))}\" stroke=\"#e3e8ee\"/><text x=\"{L - 6}\" y=\"{F(Y(t) + 4)}\" text-anchor=\"end\" fill=\"#5b6876\">{F(t)}</text>");
        foreach (var t in xt) sb.Append($"<text x=\"{F(X(t))}\" y=\"{H - B + 16}\" text-anchor=\"middle\" fill=\"#5b6876\">{F(t)}</text>");
        sb.Append($"<line x1=\"{L}\" x2=\"{W - R}\" y1=\"{H - B}\" y2=\"{H - B}\" stroke=\"#9aa7b4\"/>");
        sb.Append($"<text x=\"{F((L + W - R) / 2)}\" y=\"{H - 8}\" text-anchor=\"middle\" fill=\"#1d2733\">{E(ch.XLabel)}</text>");
        sb.Append($"<text transform=\"translate(14 {F((T + H - B) / 2)}) rotate(-90)\" text-anchor=\"middle\" fill=\"#1d2733\">{E(ch.YLabel)}</text>");
        int li = 0;
        // Envelopes first so the lines they summarise draw on top.
        foreach (var s in ch.Series.Where(x => x.Envelope).Concat(ch.Series.Where(x => !x.Envelope)))
        {
            string color = s.Reference ? "#7d8a97" : SeriesColors[ch.Series.Where(x => !x.Reference).ToList().IndexOf(s) % SeriesColors.Length];
            var good = s.Points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)).ToList();
            if (good.Count == 0) continue;
            var d = new StringBuilder();
            bool pen = false;
            foreach (var p in s.Points)
            {
                if (!double.IsFinite(p.Y) || !double.IsFinite(p.X)) { pen = false; continue; }
                d.Append(pen ? " L" : " M").Append(F(X(p.X))).Append(' ').Append(F(Y(p.Y)));
                pen = true;
            }
            sb.Append($"<path d=\"{d}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{(s.Envelope ? 8 : 2)}\" stroke-linejoin=\"round\" stroke-linecap=\"round\"{(s.Envelope ? " stroke-opacity=\"0.3\"" : "")}{(s.Reference ? " stroke-dasharray=\"6 4\"" : "")}/>");
            if (s.Reference)
            {
                var first = good[0];
                sb.Append($"<text x=\"{F(X(first.X) + 4)}\" y=\"{F(Y(first.Y) - 5)}\" fill=\"#5b6876\">{E(s.Name)}</text>");
                continue;
            }
            if (!s.Envelope)
                foreach (var p in good)
                    sb.Append($"<circle cx=\"{F(X(p.X))}\" cy=\"{F(Y(p.Y))}\" r=\"4\" fill=\"{color}\" stroke=\"#fff\" stroke-width=\"2\"><title>{E(s.Name)}: {E(ch.XLabel)} {F(p.X)}, {E(ch.YLabel)} {F(p.Y)}</title></circle>");
            double ly = T + 6 + 18 * li++;
            sb.Append($"<line x1=\"{W - R + 12}\" x2=\"{W - R + 32}\" y1=\"{F(ly)}\" y2=\"{F(ly)}\" stroke=\"{color}\" stroke-width=\"{(s.Envelope ? 8 : 2)}\"{(s.Envelope ? " stroke-opacity=\"0.3\"" : "")}/><text x=\"{W - R + 38}\" y=\"{F(ly + 4)}\" fill=\"#1d2733\">{E(s.Name)}</text>");
        }
        return sb.Append("</svg>").ToString();

        static (double[] Ticks, double Step) Ticks(double lo, double hi)
        {
            double raw = (hi - lo) / 6, mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), n = raw / mag;
            double step = (n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10) * mag;
            double a = Math.Floor(lo / step) * step, b = Math.Ceiling(hi / step) * step;
            var t = new List<double>();
            for (double v = a; v <= b + step * 1e-9; v += step) t.Add(Math.Round(v / step) * step);
            return (t.ToArray(), step);
        }
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
