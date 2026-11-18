using System.Globalization;
using System.Net;
using System.Text;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using SkiaSharp;

namespace MasterFlow.Reporting;

/// <summary>Rows shared by the PDF, HTML and Excel network outputs, so all three show the same numbers.</summary>
public static class NetworkTables
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string F(double v, int d) => double.IsFinite(v) ? v.ToString("F" + d, Inv) : "—";

    public static IReadOnlyList<string> PipeHeader(UnitConstants c, UnitSystem u) =>
    [
        "Pipe", "From → To", $"Size ({NetworkText.SizeUnit(u)})", $"L ({c.Length})", "S (%)", "n", $"Q ({c.Discharge})", $"Qfull ({c.Discharge})",
        $"yn ({c.Length})", $"yc ({c.Length})", "Regime / flow", $"V dn ({c.Velocity})", $"V up ({c.Velocity})",
        $"HGL dn", $"HGL up", $"EGL dn", $"EGL up", $"hf ({c.Length})",
    ];

    public static IReadOnlyList<IReadOnlyList<string>> PipeRows(StormNetworkResult r)
    {
        double k = NetworkText.SizeToBase(r.Network.Units);
        return r.Pipes.Select(p => (IReadOnlyList<string>)
        [
            p.Pipe.Name, $"{p.Pipe.From} → {p.Pipe.To}",
            p.Pipe.Shape == NetworkPipeShape.Circular ? F(p.Pipe.Rise / k, 0) : $"{F(p.Pipe.Rise / k, 0)}×{F(p.Pipe.Span / k, 0)}",
            F(p.Pipe.Length, 1), F(100 * p.Slope, 3), F(p.Pipe.ManningN, 3), F(p.Pipe.Discharge, 2) + (r.AccumulatedPipes.Contains(p.Pipe.Name) ? " Σ" : ""), F(p.FullFlowCapacity, 2),
            F(p.NormalDepth, 3), F(p.CriticalDepth, 3), $"{p.Regime}; {p.Condition}", F(p.VelocityDown, 2), F(p.VelocityUp, 2),
            F(p.HglDown, 2), F(p.HglUp, 2), F(p.EglDown, 2), F(p.EglUp, 2), F(p.EnergyLoss, 3),
        ]).ToList();
    }

    /// <summary>Index of the OK/FAILS column in <see cref="StructureRows"/>.</summary>
    public const int StructureCheckColumn = 8;

    public static IReadOnlyList<string> StructureHeader(UnitConstants c) =>
        ["Structure", "Type", $"Inflow ({c.Discharge})", "Rim", "HGL", "EGL", $"Rim − HGL ({c.Length})", $"Required ({c.Length})", "Check", "Loss method", "Equation", $"hj ({c.Length})", $"V2 out ({c.Velocity})", $"V1 in ({c.Velocity})"];

    public static IReadOnlyList<IReadOnlyList<string>> StructureRows(StormNetworkResult r)
    {
        var need = r.Network.Criteria.HglBelowRim;
        return r.Structures.Select(s => (IReadOnlyList<string>)
        [
            s.Structure.Name, s.Structure.Kind, s.Structure.Inflow is { } qi ? F(qi, 2) : "—", s.Structure.RimElevation is { } rim ? F(rim, 2) : "—", F(s.Hgl, 2), F(s.Egl, 2),
            s.HglClearance is { } cl ? F(cl, 2) : "—", need is { } n ? F(n, 2) : "—",
            need is null || s.HglClearance is null ? "—" : s.HglClearance >= need ? "OK" : "FAILS",
            s.LossMethod, s.LossEquation, s.IsOutfall ? "—" : F(s.Loss, 3), s.IsOutfall ? "—" : F(s.OutletVelocity, 2), double.IsFinite(s.InletVelocity) ? F(s.InletVelocity, 2) : "—",
        ]).ToList();
    }

    public static string CriteriaText(StormNetwork n, UnitConstants c)
    {
        var k = n.Criteria;
        var parts = new List<string>();
        if (k.HglBelowRim is { } h) parts.Add($"HGL at least {h.ToString("0.##", Inv)} {c.Length} below the rim");
        if (k.EglBelowRim is { } e) parts.Add($"EGL at least {e.ToString("0.##", Inv)} {c.Length} below the rim");
        if (k.MaxVelocity is { } mx) parts.Add($"V ≤ {mx.ToString("0.##", Inv)} {c.Velocity}");
        if (k.MinVelocity is { } mn) parts.Add($"V ≥ {mn.ToString("0.##", Inv)} {c.Velocity}");
        if (!k.AllowSurcharge) parts.Add("no pressure flow");
        if (k.MinPipeRise is { } mr) parts.Add($"size ≥ {(mr / NetworkText.SizeToBase(n.Units)).ToString("0", Inv)} {NetworkText.SizeUnit(n.Units)}");
        if (k.FlagSupercritical) parts.Add("slopes below critical");
        string src = k.Source.Length > 0 ? k.Source + ": " : "";
        return parts.Count == 0 ? "No criteria set." : src + string.Join("; ", parts) + ".";
    }

    public static string TailwaterText(StormNetwork n) => n.Tailwater switch
    {
        TailwaterRule.FreeOutfall => "Free outfall: critical depth at the outlet.",
        TailwaterRule.HalfCriticalPlusDiameter => "(yc + D)/2 above the outlet invert (FHWA HEC-22 convention).",
        TailwaterRule.Crown => "Outlet pipe crown.",
        _ => $"Tailwater elevation {n.TailwaterElevation?.ToString("0.###", Inv) ?? "?"} (outlet depth not below critical).",
    };

    public const string Method =
        "Energy (Bernoulli) equation integrated upstream from each outfall by the standard step, z₁ + y₁ + V₁²/2g = z₂ + y₂ + V₂²/2g + S̄f·Δx, " +
        "Sf = (Q n / (k A R^⅔))². Above the crown A and P are the full-pipe values (pressure flow). The depth at a pipe outlet is not taken below critical depth. " +
        "Steep pipes with no subcritical solution are computed supercritical from critical depth at the entrance; a hydraulic jump is placed where the momentum functions are equal. " +
        "At each structure the HGL rises by hj (Rapid City IDCM 2012 Figs. 4-6/4-7, hj = V2²/2g − c·V1²/2g, beginning of line 1.25·V2²/2g; or Eq. 4-6-1, hs = K·Vo²/2g); " +
        "V2 is the outlet pipe velocity, V1 the velocity of the inflow pipe carrying the largest flow (the IDCM does not name the inflow; this is the confirmed project reading, D-042); the structure water level is the starting water surface for every inflow pipe.";
}

/// <summary>Native PDF and self-contained HTML reports for a storm network HGL/EGL computation.</summary>
public static class NetworkReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static byte[] ToPdf(ProjectInfo project, StormNetworkResult r, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        var c = r.Constants;
        var fonts = new PdfFonts();
        var pager = new PdfPager(fonts, PdfReport.LetterHeight, PdfReport.LetterWidth); // landscape
        pager.StartSection($"Storm sewer HGL/EGL — {r.Network.Name} — {project.Name}", PdfReport.Context(options));
        pager.Add(new TextBlock(fonts, $"Storm sewer HGL / EGL: {r.Network.Name}", fonts.Bold, PdfStyle.H1, PdfStyle.Ink, 2));
        string sub = string.Join(" · ", new[] { project.Name, project.ProjectNumber, string.IsNullOrWhiteSpace(project.PreparedBy) ? "" : "Prepared by " + project.PreparedBy,
            string.IsNullOrWhiteSpace(project.CheckedBy) ? "" : "Checked by " + project.CheckedBy, "MasterFlow " + options.SoftwareVersion, PdfReport.Stamp(options) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        pager.Add(new TextBlock(fonts, sub, fonts.Regular, PdfStyle.Body, PdfStyle.Muted, 6));
        pager.Paragraph("Method: " + NetworkTables.Method, size: PdfStyle.Small);
        pager.Paragraph("Tailwater: " + NetworkTables.TailwaterText(r.Network) + "  Criteria: " + NetworkTables.CriteriaText(r.Network, c), size: PdfStyle.Small);

        if (!r.Succeeded)
        {
            pager.Paragraph("No results: the network could not be computed. See messages.", fonts.Bold);
        }
        else
        {
            foreach (var start in NetworkProfile.PathStarts(r))
                if (NetworkProfile.Build(r, start) is { } prof)
                {
                    var block = new ProfileBlock(fonts, prof, c.Length);
                    pager.Heading($"Profile: {prof.From} to {prof.To}", keepWithNext: block.Height(pager.Width));
                    pager.Add(block);
                }
            pager.Heading("Pipes");
            pager.AddTable(new TableBlock(fonts, [new(0.8f), new(1.5f), new(0.6f, true), new(0.6f, true), new(0.6f, true), new(0.55f, true), new(0.6f, true), new(0.65f, true),
                new(0.55f, true), new(0.55f, true), new(2.1f), new(0.6f, true), new(0.6f, true), new(0.8f, true), new(0.8f, true), new(0.8f, true), new(0.8f, true), new(0.65f, true)],
                NetworkTables.PipeHeader(c, r.Network.Units), NetworkTables.PipeRows(r).Select(row => ((IReadOnlyList<PdfCell>)row.Select(x => new PdfCell(x)).ToList(), false)).ToList(), PdfStyle.Small));
            if (r.AccumulatedPipes.Count > 0) pager.Paragraph("Σ: Q accumulated from structure inflows (inflow at the upstream structure plus every pipe into it); see the trace.", size: PdfStyle.Small, color: PdfStyle.Muted);
            pager.Heading("Structures (HGL check against the rim)");
            var srows = NetworkTables.StructureRows(r);
            pager.AddTable(new TableBlock(fonts, [new(0.9f), new(1f), new(0.6f, true), new(0.7f, true), new(0.7f, true), new(0.7f, true), new(0.8f, true), new(0.8f, true), new(0.6f), new(2.2f), new(1.7f), new(0.6f, true), new(0.7f, true), new(0.7f, true)],
                NetworkTables.StructureHeader(c), srows.Select(row => ((IReadOnlyList<PdfCell>)row.Select(x => new PdfCell(x)).ToList(), row[NetworkTables.StructureCheckColumn] == "FAILS")).ToList(), PdfStyle.Small));
        }
        pager.Heading("Warnings and notes");
        if (r.Messages.Count == 0) pager.Paragraph("None.");
        foreach (var m in r.Messages)
        {
            var (bar, fill) = m.Severity switch
            {
                MessageSeverity.Error => (PdfStyle.Red, SKColor.Parse("#fdecea")),
                MessageSeverity.Warning => (PdfStyle.Amber, SKColor.Parse("#fff7e0")),
                _ => (PdfStyle.Accent, SKColor.Parse("#eef5fb")),
            };
            pager.Add(new MessageBlock(fonts, $"{m.Severity} [{m.Code}]", m.Text, bar, fill));
        }
        if (options.IncludeCalculationTrace && r.Trace.Count > 0)
        {
            pager.Heading("Calculation trace");
            foreach (var t in r.Trace) pager.Paragraph(t, size: PdfStyle.Small, color: PdfStyle.Muted, spaceAfter: 1);
        }
        using var ms = new MemoryStream();
        var when = (options.Timestamp ?? DateTimeOffset.Now).UtcDateTime;
        using (var doc = SKDocument.CreatePdf(ms, new SKDocumentPdfMetadata
        {
            Title = $"Storm sewer HGL/EGL — {r.Network.Name}", Author = project.PreparedBy, Subject = project.Name,
            Creator = "MasterFlow " + options.SoftwareVersion, Producer = "MasterFlow " + options.SoftwareVersion, Creation = when, Modified = when, RasterDpi = 300,
        }))
        {
            pager.Render(doc);
            doc.Close();
        }
        return ms.ToArray();
    }

    public static string ToHtml(ProjectInfo project, StormNetworkResult r, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        var c = r.Constants;
        string E(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{E(r.Network.Name)} — HGL/EGL</title><style>body{{font-family:\"Segoe UI\",Arial,sans-serif;color:#1d2733;margin:24px;font-size:9.5pt}}h1{{font-size:14pt;margin:0}}h2{{font-size:10.5pt;color:#1f5f8b;margin:16px 0 4px}}" +
                  "table{border-collapse:collapse;width:100%}td,th{padding:2px 5px;border-bottom:1px solid #c9d1da;text-align:left;vertical-align:top}th{background:#f2f5f8}td.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}" +
                  ".sub{color:#5b6876}.fail td{font-weight:700;background:#fff7e0}.msg{padding:3px 8px;margin:3px 0;border-left:4px solid #c9d1da}.Warning{border-color:#c98a00;background:#fff7e0}.Error{border-color:#b3261e;background:#fdecea}.Info{border-color:#1f5f8b;background:#eef5fb}" +
                  "svg text{font-family:\"Segoe UI\",Arial,sans-serif}@page{size:landscape}</style></head><body>");
        sb.Append($"<h1>Storm sewer HGL / EGL: {E(r.Network.Name)}</h1><div class=\"sub\">{E(project.Name)} {E(project.ProjectNumber)} · Prepared by {E(project.PreparedBy)} · MasterFlow {E(options.SoftwareVersion)} · {PdfReport.Stamp(options)}</div>");
        sb.Append($"<p class=\"sub\">Method: {E(NetworkTables.Method)}</p><p class=\"sub\">Tailwater: {E(NetworkTables.TailwaterText(r.Network))} Criteria: {E(NetworkTables.CriteriaText(r.Network, c))}</p>");
        if (r.Succeeded)
        {
            foreach (var start in NetworkProfile.PathStarts(r))
                if (NetworkProfile.Build(r, start) is { } prof)
                    sb.Append($"<h2>Profile: {E(prof.From)} to {E(prof.To)}</h2>").Append(ProfileSvg.Render(prof, c.Length));
            void Table(IReadOnlyList<string> head, IReadOnlyList<IReadOnlyList<string>> rows, int failCol)
            {
                sb.Append("<table><tr>");
                foreach (var h in head) sb.Append($"<th>{E(h)}</th>");
                sb.Append("</tr>");
                foreach (var row in rows)
                {
                    sb.Append(failCol >= 0 && row[failCol] == "FAILS" ? "<tr class=\"fail\">" : "<tr>");
                    foreach (var x in row) sb.Append(double.TryParse(x, NumberStyles.Float, Inv, out _) ? $"<td class=\"num\">{E(x)}</td>" : $"<td>{E(x)}</td>");
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
            }
            sb.Append("<h2>Pipes</h2>"); Table(NetworkTables.PipeHeader(c, r.Network.Units), NetworkTables.PipeRows(r), -1);
            if (r.AccumulatedPipes.Count > 0) sb.Append("<p class=\"sub\">Σ: Q accumulated from structure inflows (inflow at the upstream structure plus every pipe into it).</p>");
            sb.Append("<h2>Structures (HGL check against the rim)</h2>"); Table(NetworkTables.StructureHeader(c), NetworkTables.StructureRows(r), NetworkTables.StructureCheckColumn);
        }
        else sb.Append("<p><strong>No results: the network could not be computed.</strong></p>");
        sb.Append("<h2>Warnings and notes</h2>");
        foreach (var m in r.Messages) sb.Append($"<div class=\"msg {m.Severity}\"><strong>{m.Severity}</strong> [{E(m.Code)}] {E(m.Text)}</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>The same profile drawing as the PDF: ground, structures, pipe, HGL (solid) and EGL (dashed).</summary>
    private sealed class ProfileBlock(PdfFonts fonts, NetworkProfile p, string unit) : IPdfBlock
    {
        private const float BandFont = 6.5f;
        private float TextW => fonts.Measure("0000.00", fonts.Regular, BandFont);
        private float BandHeight(float width) => ProfileFrame.BandRows(p).Count * ProfileFrame.Of(p, width, 230).BandLayout(p, TextW, BandFont).RowHeight;
        public float Height(float width) => 262 + BandHeight(width) + 24;
        public void Draw(SKCanvas c, float x, float y, float width)
        {
            var g = ProfileFrame.Of(p, width, 230);
            DrawBand(c, x, y + 236, width, g);
            using (var bg = new SKPaint { Color = PdfStyle.PlotBg }) c.DrawRect(x, y, width, 230, bg);
            SKPoint P(double s, double z) { var (px, py) = g.Map(s, z); return new(x + px, y + py); }
            void Line(IReadOnlyList<(double S, double Z)> pts, SKColor col, float w, float[]? dash = null)
            {
                if (pts.Count < 2) return;
                using var path = new SKPath();
                path.MoveTo(P(pts[0].S, pts[0].Z));
                foreach (var q in pts.Skip(1)) path.LineTo(P(q.S, q.Z));
                using var paint = new SKPaint { Color = col, StrokeWidth = w, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
                if (dash is not null) paint.PathEffect = SKPathEffect.CreateDash(dash, 0);
                c.DrawPath(path, paint);
            }
            using var small = new SKPaint();
            // grid
            using (var gp = new SKPaint { Color = PdfStyle.Rule, StrokeWidth = 0.4f })
            {
                foreach (var z in g.ZTicks) { var a = P(g.S0, z); var b = P(g.S1, z); c.DrawLine(a, b, gp); fonts.Draw(c, z.ToString("0.#", Inv), x + 2, a.Y + 2.5f, fonts.Regular, 6.5f, PdfStyle.Muted); }
                foreach (var s in g.STicks) { var a = P(s, g.Z0); fonts.Draw(c, s.ToString("0", Inv), a.X - 6, y + 228, fonts.Regular, 6.5f, PdfStyle.Muted); }
            }
            // pipe: fill between invert and crown
            if (p.Invert.Count > 1)
            {
                using var path = new SKPath();
                path.MoveTo(P(p.Invert[0].S, p.Invert[0].Z));
                foreach (var q in p.Invert.Skip(1)) path.LineTo(P(q.S, q.Z));
                for (int i = p.Crown.Count - 1; i >= 0; i--) path.LineTo(P(p.Crown[i].S, p.Crown[i].Z));
                path.Close();
                using var fill = new SKPaint { Color = SKColor.Parse("#dfe5eb"), Style = SKPaintStyle.Fill, IsAntialias = true };
                c.DrawPath(path, fill);
            }
            Line(p.Invert, PdfStyle.Ink, 1f); Line(p.Crown, PdfStyle.Ink, 1f);
            Line(p.Ground, SKColor.Parse("#7a5c2e"), 1f, [5f, 2f]);
            foreach (var s in p.Structures)
            {
                double top = s.Rim ?? Math.Max(s.Hgl, s.Egl);
                var a = P(s.Station, s.Bottom); var b = P(s.Station, top);
                using var sp = new SKPaint { Color = PdfStyle.Muted, StrokeWidth = 3f, IsAntialias = true };
                c.DrawLine(a, b, sp);
                if (s.CriteriaLine is { } cl)
                {
                    var l1 = P(s.Station, cl);
                    using var cp = new SKPaint { Color = PdfStyle.Amber, StrokeWidth = 1.2f };
                    c.DrawLine(l1.X - 7, l1.Y, l1.X + 7, l1.Y, cp);
                }
                fonts.Draw(c, s.Name, b.X - fonts.Measure(s.Name, fonts.Bold, 7f) / 2, b.Y - 9, fonts.Bold, 7f, PdfStyle.Ink);
                string hglTxt = "HGL " + s.Hgl.ToString("F2", Inv);
                fonts.Draw(c, hglTxt, b.X - fonts.Measure(hglTxt, fonts.Regular, 6.5f) / 2, b.Y - 2, fonts.Regular, 6.5f, s.MeetsCriteria ? PdfStyle.Accent : PdfStyle.Red);
            }
            Line(p.Egl, PdfStyle.Red, 1.1f, [4f, 2.5f]);
            Line(p.Hgl, PdfStyle.Accent, 1.4f);
            // legend
            float lx = x + width - 250, ly = y + 12;
            void Key(string t, SKColor col, float[]? dash, float off)
            {
                using var kp = new SKPaint { Color = col, StrokeWidth = 1.4f, IsAntialias = true };
                if (dash is not null) kp.PathEffect = SKPathEffect.CreateDash(dash, 0);
                c.DrawLine(lx + off, ly, lx + off + 16, ly, kp);
                fonts.Draw(c, t, lx + off + 19, ly + 2.5f, fonts.Regular, 7f, PdfStyle.Ink);
            }
            Key("HGL", PdfStyle.Accent, null, 0); Key("EGL", PdfStyle.Red, [4f, 2.5f], 48); Key("Rim/ground", SKColor.Parse("#7a5c2e"), [5f, 2f], 96);
            if (p.Structures.Any(s => s.CriteriaLine is not null)) Key("Criteria", PdfStyle.Amber, null, 178);
            float cy = y + 236 + BandHeight(width) + 10;
            fonts.Draw(c, $"Station from the outfall ({unit}); elevation ({unit}); vertical scale exaggerated.", x + 4, cy, fonts.Regular, PdfStyle.Small, PdfStyle.Muted);
            var cap = ProfileFrame.BandCaption(p, unit).Split('\n');
            for (int i = 0; i < cap.Length; i++) fonts.Draw(c, cap[i], x + 4, cy + 10 * (i + 1), fonts.Regular, PdfStyle.Small, PdfStyle.Muted);
        }

        /// <summary>Station, HGL, EGL and limit values at each reading station, under the plot.</summary>
        private void DrawBand(SKCanvas c, float x, float top, float width, ProfileFrame g)
        {
            var rows = ProfileFrame.BandRows(p);
            var (rot, rh) = g.BandLayout(p, TextW, BandFont);
            var readings = p.Readings();
            using var rule = new SKPaint { Color = PdfStyle.Rule, StrokeWidth = 0.5f };
            using var tick = new SKPaint { Color = PdfStyle.Rule, StrokeWidth = 0.4f, PathEffect = SKPathEffect.CreateDash([2f, 2f], 0) };
            foreach (var rd in readings) { var a = g.Map(rd.Station, g.Z1); var b = g.Map(rd.Station, g.Z0); c.DrawLine(x + a.X, top - 236 + a.Y, x + b.X, top - 236 + b.Y, tick); }
            for (int i = 0; i < rows.Count; i++)
            {
                float ry = top + i * rh;
                c.DrawLine(x, ry, x + width, ry, rule);
                var (label, value, isHgl) = rows[i];
                fonts.Draw(c, label, x + 2, ry + (rot ? rh / 2 + 2.5f : rh - 4), fonts.Bold, BandFont, PdfStyle.Ink);
                foreach (var rd in readings)
                {
                    string t = ProfileFrame.BandText(value(rd), i == 0);
                    var col = isHgl && !rd.MeetsCriteria ? PdfStyle.Red : PdfStyle.Ink;
                    float px = x + g.Map(rd.Station, g.Z0).X;
                    float tw = fonts.Measure(t, fonts.Regular, BandFont);
                    if (!rot) fonts.Draw(c, t, px - tw / 2, ry + rh - 4, fonts.Regular, BandFont, col);
                    else
                    {
                        c.Save();
                        c.Translate(px + 2.3f, ry + rh - 3);
                        c.RotateDegrees(-90);
                        fonts.Draw(c, t, 0, 0, fonts.Regular, BandFont, col);
                        c.Restore();
                    }
                }
            }
            c.DrawLine(x, top + rows.Count * rh, x + width, top + rows.Count * rh, rule);
        }
    }
}

/// <summary>Scales shared by the PDF block, the SVG and the app control.</summary>
public sealed record ProfileFrame(double S0, double S1, double Z0, double Z1, float Width, float Height, float PadL, float PadR, float PadT, float PadB)
{
    public static ProfileFrame Of(NetworkProfile p, float width, float height, float padLeft = 86)
    {
        double s1 = Math.Max(p.Length, 1), z0 = p.MinZ, z1 = p.MaxZ;
        double pad = Math.Max(0.5, 0.08 * (z1 - z0));
        return new ProfileFrame(0, s1, Math.Floor(z0 - pad), Math.Ceiling(z1 + pad), width, height, padLeft, 52, 22, 18);
    }
    public (float X, float Y) Map(double s, double z) =>
        (PadL + (float)((s - S0) / (S1 - S0)) * (Width - PadL - PadR), PadT + (float)((Z1 - z) / (Z1 - Z0)) * (Height - PadT - PadB));
    private static double Step(double range, int n) { double raw = range / n, m = Math.Pow(10, Math.Floor(Math.Log10(raw))); double f = raw / m; return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * m; }
    public IEnumerable<double> ZTicks { get { double st = Step(Z1 - Z0, 6); for (double z = Math.Ceiling(Z0 / st) * st; z <= Z1 + 1e-9; z += st) yield return z; } }
    public IEnumerable<double> STicks { get { double st = Step(S1 - S0, 8); for (double s = 0; s <= S1 + 1e-9; s += st) yield return s; } }

    /// <summary>Rows of the reading band under the profile: station, HGL, EGL and (when a criterion is set) the limit.</summary>
    public static IReadOnlyList<(string Label, Func<ProfileReading, double?> Value, bool IsHgl)> BandRows(NetworkProfile p)
    {
        var rows = new List<(string, Func<ProfileReading, double?>, bool)>
        {
            ("Station", r => r.Station, false), ("HGL", r => r.Hgl, true), ("EGL", r => r.Egl, false),
        };
        if (p.LimitBelowGround is not null) rows.Add((p.LimitLabel, r => r.Limit, false));
        return rows;
    }

    public static string BandText(double? v, bool station) =>
        v is not { } x || !double.IsFinite(x) ? "—" : station ? x.ToString("0.#", CultureInfo.InvariantCulture) : x.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>Horizontal labels when a reading's column is wide enough, otherwise rotated (as on plan-profile sheets).</summary>
    public (bool Rotated, float RowHeight) BandLayout(NetworkProfile p, float textWidth, float fontSize)
    {
        var st = p.Readings().Select(r => r.Station).ToList();
        double gap = st.Count < 2 ? p.ReadingInterval : st.Zip(st.Skip(1), (a, b) => b - a).Min();
        float dx = Map(gap, Z0).X - Map(0, Z0).X;
        bool rot = dx < textWidth + 4;
        return (rot, rot ? textWidth + 6 : fontSize + 5);
    }

    public static string BandCaption(NetworkProfile p, string unit) =>
        $"Readings every {p.ReadingInterval.ToString("0.#", CultureInfo.InvariantCulture)} {unit} and at the upstream end (unless within {(0.2 * p.ReadingInterval).ToString("0.#", CultureInfo.InvariantCulture)} {unit} of the last); at a structure the value on its upstream side." +
        (p.LimitBelowGround is { } c ? $"\n{p.LimitLabel} = rim/ground − {c.ToString("0.##", CultureInfo.InvariantCulture)} {unit} (maximum HGL), ground taken straight between rims; HGL in red where it is above the limit." : "");
}

public static class ProfileSvg
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static string Render(NetworkProfile p, string unit, int width = 980, int height = 300)
    {
        var g = ProfileFrame.Of(p, width, height);
        string Pt(double s, double z) { var (x, y) = g.Map(s, z); return x.ToString("F1", Inv) + "," + y.ToString("F1", Inv); }
        string Poly(IReadOnlyList<(double S, double Z)> pts, string stroke, double w, string dash = "") =>
            pts.Count < 2 ? "" : $"<polyline fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{w.ToString(Inv)}\"{(dash.Length > 0 ? $" stroke-dasharray=\"{dash}\"" : "")} points=\"{string.Join(" ", pts.Select(q => Pt(q.S, q.Z)))}\"/>";
        var rows = ProfileFrame.BandRows(p);
        var (rot, rh) = g.BandLayout(p, 34f, 9f);
        float bandH = rows.Count * rh;
        var readings = p.Readings();
        var sb = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} {(height + bandH + 47).ToString("F0", Inv)}\" width=\"100%\" role=\"img\" aria-label=\"HGL and EGL profile from {WebUtility.HtmlEncode(p.From)} to {WebUtility.HtmlEncode(p.To)}\">");
        sb.Append($"<rect width=\"{width}\" height=\"{height}\" fill=\"#f6f8fa\"/>");
        foreach (var z in g.ZTicks) { var (_, y) = g.Map(0, z); sb.Append($"<line x1=\"{g.PadL}\" x2=\"{width - g.PadR}\" y1=\"{y.ToString("F1", Inv)}\" y2=\"{y.ToString("F1", Inv)}\" stroke=\"#c9d1da\" stroke-width=\"0.5\"/><text x=\"2\" y=\"{(y + 3).ToString("F1", Inv)}\" font-size=\"9\" fill=\"#5b6876\">{z.ToString("0.#", Inv)}</text>"); }
        foreach (var s in g.STicks) { var (x, _) = g.Map(s, g.Z0); sb.Append($"<text x=\"{(x - 6).ToString("F1", Inv)}\" y=\"{height - 4}\" font-size=\"9\" fill=\"#5b6876\">{s.ToString("0", Inv)}</text>"); }
        if (p.Invert.Count > 1)
            sb.Append($"<polygon fill=\"#dfe5eb\" points=\"{string.Join(" ", p.Invert.Select(q => Pt(q.S, q.Z)).Concat(p.Crown.Reverse().Select(q => Pt(q.S, q.Z))))}\"/>");
        sb.Append(Poly(p.Invert, "#1d2733", 1)).Append(Poly(p.Crown, "#1d2733", 1)).Append(Poly(p.Ground, "#7a5c2e", 1, "5 2"));
        foreach (var s in p.Structures)
        {
            double top = s.Rim ?? Math.Max(s.Hgl, s.Egl);
            var (x, yb) = g.Map(s.Station, s.Bottom); var (_, yt) = g.Map(s.Station, top);
            sb.Append($"<line x1=\"{x.ToString("F1", Inv)}\" x2=\"{x.ToString("F1", Inv)}\" y1=\"{yb.ToString("F1", Inv)}\" y2=\"{yt.ToString("F1", Inv)}\" stroke=\"#5b6876\" stroke-width=\"4\"/>");
            if (s.CriteriaLine is { } cl) { var (_, yc) = g.Map(s.Station, cl); sb.Append($"<line x1=\"{(x - 8).ToString("F1", Inv)}\" x2=\"{(x + 8).ToString("F1", Inv)}\" y1=\"{yc.ToString("F1", Inv)}\" y2=\"{yc.ToString("F1", Inv)}\" stroke=\"#c98a00\" stroke-width=\"1.5\"/>"); }
            sb.Append($"<text x=\"{x.ToString("F1", Inv)}\" y=\"{(yt - 11).ToString("F1", Inv)}\" font-size=\"9\" font-weight=\"700\" text-anchor=\"middle\">{WebUtility.HtmlEncode(s.Name)}</text>");
            sb.Append($"<text x=\"{x.ToString("F1", Inv)}\" y=\"{(yt - 2).ToString("F1", Inv)}\" font-size=\"8.5\" text-anchor=\"middle\" fill=\"{(s.MeetsCriteria ? "#1f5f8b" : "#b3261e")}\">HGL {s.Hgl.ToString("F2", Inv)}</text>");
        }
        sb.Append(Poly(p.Egl, "#b3261e", 1.2, "4 2.5")).Append(Poly(p.Hgl, "#1f5f8b", 1.6));
        sb.Append($"<text x=\"{width - 300}\" y=\"14\" font-size=\"9\"><tspan fill=\"#1f5f8b\">━ HGL</tspan>  <tspan fill=\"#b3261e\">╍ EGL</tspan>  <tspan fill=\"#7a5c2e\">╍ Rim/ground</tspan>  <tspan fill=\"#c98a00\">━ Criteria</tspan></text>");
        foreach (var rd in readings)
        {
            var (x, y0) = g.Map(rd.Station, g.Z1); var (_, y1) = g.Map(rd.Station, g.Z0);
            sb.Append($"<line x1=\"{x.ToString("F1", Inv)}\" x2=\"{x.ToString("F1", Inv)}\" y1=\"{y0.ToString("F1", Inv)}\" y2=\"{y1.ToString("F1", Inv)}\" stroke=\"#c9d1da\" stroke-width=\"0.5\" stroke-dasharray=\"2 2\"/>");
        }
        float bandTop = height + 4;
        for (int i = 0; i < rows.Count; i++)
        {
            float ry = bandTop + i * rh;
            var (label, value, isHgl) = rows[i];
            sb.Append($"<line x1=\"0\" x2=\"{width}\" y1=\"{ry.ToString("F1", Inv)}\" y2=\"{ry.ToString("F1", Inv)}\" stroke=\"#c9d1da\" stroke-width=\"0.5\"/>");
            sb.Append($"<text x=\"2\" y=\"{(ry + (rot ? rh / 2 + 3 : rh - 4)).ToString("F1", Inv)}\" font-size=\"9\" font-weight=\"700\">{WebUtility.HtmlEncode(label)}</text>");
            foreach (var rd in readings)
            {
                var (x, _) = g.Map(rd.Station, g.Z0);
                string t = ProfileFrame.BandText(value(rd), i == 0);
                string fill = isHgl && !rd.MeetsCriteria ? "#b3261e" : "#1d2733";
                sb.Append(rot
                    ? $"<text transform=\"translate({(x + 3).ToString("F1", Inv)},{(ry + rh - 3).ToString("F1", Inv)}) rotate(-90)\" font-size=\"9\" fill=\"{fill}\">{t}</text>"
                    : $"<text x=\"{x.ToString("F1", Inv)}\" y=\"{(ry + rh - 4).ToString("F1", Inv)}\" font-size=\"9\" text-anchor=\"middle\" fill=\"{fill}\">{t}</text>");
            }
        }
        float by = bandTop + bandH;
        sb.Append($"<line x1=\"0\" x2=\"{width}\" y1=\"{by.ToString("F1", Inv)}\" y2=\"{by.ToString("F1", Inv)}\" stroke=\"#c9d1da\" stroke-width=\"0.5\"/>");
        sb.Append($"<text x=\"4\" y=\"{(by + 13).ToString("F1", Inv)}\" font-size=\"9\" fill=\"#5b6876\">Station from the outfall ({unit}); elevation ({unit}); vertical scale exaggerated.</text>");
        var cap = ProfileFrame.BandCaption(p, unit).Split('\n');
        for (int i = 0; i < cap.Length; i++)
            sb.Append($"<text x=\"4\" y=\"{(by + 26 + 13 * i).ToString("F1", Inv)}\" font-size=\"9\" fill=\"#5b6876\">{WebUtility.HtmlEncode(cap[i])}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}
