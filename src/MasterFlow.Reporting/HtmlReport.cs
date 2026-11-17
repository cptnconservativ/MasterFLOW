using System.Globalization;
using System.Net;
using System.Text;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Reporting;

/// <summary>Options that control report presentation (not calculation).</summary>
public sealed record ReportOptions
{
    public string SoftwareVersion { get; init; } = "0.2.0";
    public string? Watermark { get; init; }
    public string? FooterText { get; init; }
    /// <summary>Optional company logo as a data: URI (PNG/SVG). Embedded, never fetched.</summary>
    public string? LogoDataUri { get; init; }
    public bool IncludeCalculationTrace { get; init; } = true;
    public RatingTable? RatingTable { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
}

/// <summary>
/// Renders a self-contained, printable HTML calculation report (original MasterFlow layout). Print to
/// PDF from any browser; native PDF export is planned (roadmap Phase F).
/// </summary>
public static class HtmlReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(ProjectInfo project, ChannelCalculation calc, UniformFlowResult result, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        var sb = new StringBuilder();
        string E(string s) => WebUtility.HtmlEncode(s);
        string date = (options.Timestamp ?? DateTimeOffset.Now).ToString("yyyy-MM-dd HH:mm", Inv);

        sb.Append("""
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
""");
        sb.Append($"<title>{E(calc.Title)} — MasterFlow calculation</title>");
        sb.Append("""
<style>
:root{--ink:#1d2733;--muted:#5b6876;--rule:#c9d1da;--accent:#1f5f8b;--water:#7fb7dc;--bg:#ffffff}
*{box-sizing:border-box}
body{font-family:"Segoe UI",Arial,sans-serif;color:var(--ink);background:var(--bg);margin:0;padding:24px;font-size:10.5pt;line-height:1.35}
.page{max-width:760px;margin:0 auto;position:relative}
header{display:grid;grid-template-columns:1fr auto;gap:12px;border-bottom:2px solid var(--accent);padding-bottom:8px}
header h1{font-size:15pt;margin:0 0 2px 0}
header .sub{color:var(--muted)}
.logo{max-height:56px;max-width:180px}
table{border-collapse:collapse;width:100%}
td,th{padding:3px 6px;border-bottom:1px solid var(--rule);text-align:left;vertical-align:top}
th{font-weight:600;color:var(--muted);font-size:9pt;text-transform:uppercase;letter-spacing:.03em}
td.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}
.meta{width:auto}.meta td{border:none;padding:1px 10px 1px 0}
td.k{white-space:nowrap}
h2{font-size:11.5pt;color:var(--accent);margin:18px 0 6px;border-bottom:1px solid var(--rule);padding-bottom:2px}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:16px}
.solved td{font-weight:700}
.msg{padding:4px 8px;margin:3px 0;border-left:4px solid var(--rule)}
.msg.Warning{border-color:#c98a00;background:#fff7e0}.msg.Error{border-color:#b3261e;background:#fdecea}.msg.Info{border-color:var(--accent);background:#eef5fb}
.trace{font-family:Consolas,monospace;font-size:8.5pt;color:var(--muted);white-space:pre-wrap}
.eq{font-family:Cambria,"Times New Roman",serif;font-size:11pt}
footer{margin-top:20px;border-top:1px solid var(--rule);padding-top:6px;color:var(--muted);font-size:8.5pt;display:flex;justify-content:space-between;gap:12px}
.watermark{position:fixed;top:40%;left:0;right:0;text-align:center;font-size:72pt;color:rgba(31,95,139,.08);transform:rotate(-30deg);pointer-events:none;z-index:0}
svg text{font-family:"Segoe UI",Arial,sans-serif}
@page{size:letter;margin:0.6in;@bottom-right{content:"Page " counter(page) " of " counter(pages)}}
@media print{body{padding:0}.page{max-width:none}}
</style></head><body><div class="page">
""");
        if (!string.IsNullOrWhiteSpace(options.Watermark))
            sb.Append($"<div class=\"watermark\">{E(options.Watermark!)}</div>");

        // Header
        sb.Append("<header><div>");
        sb.Append($"<h1>{E(calc.Title)}</h1><div class=\"sub\">{E(project.Name)}{(string.IsNullOrWhiteSpace(project.ProjectNumber) ? "" : " · No. " + E(project.ProjectNumber))}</div>");
        sb.Append("<table class=\"meta\">");
        Meta(sb, "Client", project.Client);
        Meta(sb, "Prepared by", project.PreparedBy);
        Meta(sb, "Checked by", project.CheckedBy);
        Meta(sb, "Date", date);
        Meta(sb, "Software", $"MasterFlow {options.SoftwareVersion}");
        sb.Append("</table></div>");
        if (!string.IsNullOrWhiteSpace(options.LogoDataUri) && options.LogoDataUri!.StartsWith("data:image/", StringComparison.Ordinal))
            sb.Append($"<img class=\"logo\" alt=\"Company logo\" src=\"{E(options.LogoDataUri)}\">");
        sb.Append("</header>");

        if (!string.IsNullOrWhiteSpace(calc.Description))
            sb.Append($"<p>{E(calc.Description)}</p>");

        // Method
        var c = result.Constants;
        sb.Append("<h2>Method</h2>");
        sb.Append($"<p>Steady, uniform open-channel flow by Manning's equation, <span class=\"eq\">Q = (k/n)·A·R<sup>2/3</sup>·S<sup>1/2</sup></span>, with k = {c.ManningK.ToString(Inv)} and R = A/P. ");
        sb.Append($"Critical depth satisfies <span class=\"eq\">Q²T/(gA³) = 1</span> with g = {c.Gravity.ToString(Inv)} {c.Length}/s². Froude number Fr = V/√(gD), D = A/T. ");
        sb.Append("Unknown depths are solved with Brent's bracketed method (relative depth tolerance 1×10⁻¹², discharge residual ≤ 1×10⁻⁸). Gradually varied, unsteady and pressurised flow are not modelled.</p>");

        // Inputs + section plot
        sb.Append("<div class=\"grid2\"><div><h2>Input</h2><table>");
        foreach (var (label, value) in ResultSummary.InputLines(result.Input))
            sb.Append($"<tr><td class=\"k\">{E(label)}</td><td class=\"num\">{E(value)}</td></tr>");
        sb.Append("</table></div><div><h2>Cross-section</h2>");
        var drawing = CrossSectionDrawing.Create(result.Input.Section, result.Succeeded ? result.Normal.Depth : null, result.Succeeded ? result.Critical.Depth : null);
        sb.Append(CrossSectionSvg.Render(drawing, result.Input.Section, c.Length));
        sb.Append("</div></div>");

        // Results
        sb.Append("<h2>Results</h2>");
        if (!result.Succeeded)
        {
            sb.Append("<p><strong>No results: the calculation did not complete.</strong> See messages below.</p>");
        }
        else
        {
            sb.Append($"<p>Flow regime: <strong>{E(ResultSummary.RegimeText(result))}</strong>. Values in bold were solved for.</p>");
            sb.Append("<div class=\"grid2\">");
            foreach (var group in ResultSummary.Lines(result).GroupBy(l => l.Group))
            {
                if (group.Key == "Critical flow") continue;
                ResultTable(sb, group.Key, group);
            }
            sb.Append("</div>");
            ResultTable(sb, "Critical flow (same discharge)", ResultSummary.Lines(result).Where(l => l.Group == "Critical flow"));
        }

        // Messages
        sb.Append("<h2>Warnings, notes and assumptions</h2>");
        foreach (var m in result.Messages)
            sb.Append($"<div class=\"msg {m.Severity}\"><strong>{m.Severity}</strong> [{E(m.Code)}] {E(m.Text)}</div>");
        sb.Append("<div class=\"msg Info\">Assumes a prismatic channel, uniform roughness across the wetted perimeter, small slope (depth measured vertically) and hydrostatic pressure. Manning's n is the engineer's selection; results are only as reliable as n.</div>");

        if (options.RatingTable is { Rows.Count: > 0 } rt)
        {
            sb.Append("<h2>Rating table</h2><table><tr>");
            foreach (var h in new[] { $"Depth ({c.Length})", $"Q ({c.Discharge})", $"A ({c.Area})", $"V ({c.Velocity})", $"T ({c.Length})", "Fr", $"yc ({c.Length})" })
                sb.Append($"<th>{E(h)}</th>");
            sb.Append("</tr>");
            foreach (var row in rt.Rows)
                sb.Append($"<tr><td class=\"num\">{row.Depth.ToString("F3", Inv)}</td><td class=\"num\">{row.Discharge.ToString("F3", Inv)}</td><td class=\"num\">{row.Area.ToString("F3", Inv)}</td><td class=\"num\">{row.Velocity.ToString("F3", Inv)}</td><td class=\"num\">{row.TopWidth.ToString("F3", Inv)}</td><td class=\"num\">{row.Froude.ToString("F3", Inv)}</td><td class=\"num\">{row.CriticalDepth.ToString("F3", Inv)}</td></tr>");
            sb.Append("</table>");
        }

        if (!string.IsNullOrWhiteSpace(project.Notes))
            sb.Append($"<h2>Notes</h2><p>{E(project.Notes)}</p>");

        if (options.IncludeCalculationTrace && result.Trace.Count > 0)
        {
            sb.Append("<h2>Calculation trace</h2><div class=\"trace\">");
            foreach (var t in result.Trace) sb.Append(E(t)).Append('\n');
            sb.Append("</div>");
        }

        sb.Append("<footer><span>Computed by MasterFlow. The responsible engineer must review inputs, assumptions and results before use.</span>");
        sb.Append($"<span>{E(options.FooterText ?? "")}</span></footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void Meta(StringBuilder sb, string k, string v)
    {
        if (!string.IsNullOrWhiteSpace(v))
            sb.Append($"<tr><td>{WebUtility.HtmlEncode(k)}:</td><td>{WebUtility.HtmlEncode(v)}</td></tr>");
    }

    private static void ResultTable(StringBuilder sb, string title, IEnumerable<ResultLine> lines)
    {
        sb.Append($"<div><table><tr><th colspan=\"3\">{WebUtility.HtmlEncode(title)}</th></tr>");
        foreach (var l in lines)
            sb.Append($"<tr{(l.IsSolved ? " class=\"solved\"" : "")}><td>{WebUtility.HtmlEncode(l.Label)} <span style=\"color:var(--muted)\">{WebUtility.HtmlEncode(l.Symbol)}</span></td><td class=\"num\">{l.Formatted}</td><td>{WebUtility.HtmlEncode(l.Unit)}</td></tr>");
        sb.Append("</table></div>");
    }
}

/// <summary>Draws a <see cref="CrossSectionDrawing"/> as inline SVG with true aspect ratio optional.</summary>
public static class CrossSectionSvg
{
    public static string Render(CrossSectionDrawing d, ChannelSection section, string lengthUnit, int width = 360, int height = 200)
    {
        var inv = CultureInfo.InvariantCulture;
        const double pad = 28, padR = 96;
        double sx = (width - pad - padR) / d.Width, sy = (height - 2 * pad) / d.Height;
        string P(double x, double y) => $"{(pad + x * sx).ToString("F1", inv)},{(height - pad - y * sy).ToString("F1", inv)}";
        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {width} {height}\" width=\"100%\" role=\"img\" aria-label=\"Cross-section of the {section.ShapeName.ToLowerInvariant()} channel\">");
        sb.Append($"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"#f6f8fa\"/>");
        if (d.Water.Count > 2)
            sb.Append($"<polygon points=\"{string.Join(" ", d.Water.Select(p => P(p.X, p.Y)))}\" fill=\"#7fb7dc\" fill-opacity=\"0.85\"/>");
        sb.Append($"<polyline points=\"{string.Join(" ", d.Outline.Select(p => P(p.X, p.Y)))}\" fill=\"none\" stroke=\"#1d2733\" stroke-width=\"2\"/>");
        void Level(double? y, string color, string label, bool dashed)
        {
            if (y is not > 0) return;
            var (l, r) = d.ExtentAt(section, y.Value);
            sb.Append($"<line x1=\"{(pad + l * sx).ToString("F1", inv)}\" x2=\"{(pad + r * sx).ToString("F1", inv)}\" y1=\"{(height - pad - y.Value * sy).ToString("F1", inv)}\" y2=\"{(height - pad - y.Value * sy).ToString("F1", inv)}\" stroke=\"{color}\" stroke-width=\"1.5\"{(dashed ? " stroke-dasharray=\"5 3\"" : "")}/>");
            sb.Append($"<text x=\"{(pad + r * sx + 4).ToString("F1", inv)}\" y=\"{(height - pad - y.Value * sy + 4).ToString("F1", inv)}\" font-size=\"10\" fill=\"{color}\">{label} = {y.Value.ToString("F3", inv)} {lengthUnit}</text>");
        }
        Level(d.WaterDepth, "#1f5f8b", "yn", false);
        Level(d.CriticalDepth, "#b3261e", "yc", true);
        sb.Append($"<text x=\"{pad}\" y=\"{height - 8}\" font-size=\"9\" fill=\"#5b6876\">Not to scale (horizontal and vertical scales differ). Units: {lengthUnit}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}
