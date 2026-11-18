using System.Text;
using SkiaSharp;

namespace MasterFlow.Reporting;

/// <summary>
/// Typefaces for PDF output with per-character fallback, so symbols such as ², ³, τ, √ or ᐟ are drawn
/// with whatever installed font has them instead of an empty box. Fonts are embedded (subset) by Skia.
/// </summary>
internal sealed class PdfFonts
{
    private static readonly string[] SansFamilies = ["Segoe UI", "Arial", "Liberation Sans", "DejaVu Sans", "Helvetica"];
    // Symbols (superscript minus, the fraction slash in ft⁸ᐟ³, Greek) on Windows and Linux.
    private static readonly string[] FallbackFamilies = ["Segoe UI Symbol", "Cambria Math", "Gadugi", "Euphemia", "Arial Unicode MS", "DejaVu Sans", "FreeSans", "Noto Sans", "Inter"];
    private static SKTypeface? Has(SKTypeface? tf, int cp) => tf is not null && tf.GetGlyph(cp) != 0 ? tf : null;
    private static readonly string[] MonoFamilies = ["Consolas", "Courier New", "Liberation Mono", "DejaVu Sans Mono"];

    public SKTypeface Regular { get; }
    public SKTypeface Bold { get; }
    public SKTypeface Mono { get; }
    private readonly Dictionary<(string, int, int), SKTypeface> _fallback = [];

    public PdfFonts()
    {
        Regular = Find(SansFamilies, SKFontStyle.Normal);
        Bold = Find(SansFamilies, SKFontStyle.Bold);
        Mono = Find(MonoFamilies, SKFontStyle.Normal);
    }

    private static SKTypeface Find(string[] families, SKFontStyle style)
    {
        foreach (var f in families)
        {
            var tf = SKTypeface.FromFamilyName(f, style);
            if (tf is not null && string.Equals(tf.FamilyName, f, StringComparison.OrdinalIgnoreCase)) return tf;
        }
        return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
    }

    /// <summary>Splits text into runs that one typeface can draw.</summary>
    public IEnumerable<(string Text, SKTypeface Face)> Runs(string s, SKTypeface primary)
    {
        var sb = new StringBuilder();
        SKTypeface? current = null;
        foreach (var rune in s.EnumerateRunes())
        {
            var face = FaceFor(rune.Value, primary);
            if (current is not null && face != current) { yield return (sb.ToString(), current); sb.Clear(); }
            current = face;
            sb.Append(rune.ToString());
        }
        if (current is not null && sb.Length > 0) yield return (sb.ToString(), current);
    }

    private SKTypeface FaceFor(int cp, SKTypeface primary)
    {
        if (cp < 0x250 || primary.GetGlyph(cp) != 0) return primary;
        var key = (primary.FamilyName, primary.FontWeight, cp);
        if (_fallback.TryGetValue(key, out var tf)) return tf;
        tf = Has(SKFontManager.Default.MatchCharacter(primary.FamilyName, primary.FontStyle, [], cp), cp)
             ?? Has(SKFontManager.Default.MatchCharacter(null, primary.FontStyle, [], cp), cp)
             ?? FallbackFamilies.Select(f => Has(SKTypeface.FromFamilyName(f, primary.FontStyle), cp)).FirstOrDefault(t => t is not null)
             ?? primary;
        _fallback[key] = tf;
        return tf;
    }

    /// <summary>Unhinted, linear metrics: PDF is resolution independent, and hinting rounds advances to whole points.</summary>
    private static SKFont Font(SKTypeface face, float size) =>
        new(face, size) { Hinting = SKFontHinting.None, LinearMetrics = true, Subpixel = true, Edging = SKFontEdging.Antialias };

    private static float Advance(SKFont font, string t)
    {
        int n = font.CountGlyphs(t);
        Span<ushort> glyphs = n <= 256 ? stackalloc ushort[n] : new ushort[n];
        font.GetGlyphs(t.AsSpan(), glyphs);
        return font.MeasureText(glyphs);
    }

    public float Measure(string s, SKTypeface face, float size)
    {
        float w = 0;
        foreach (var (t, f) in Runs(s, face)) { using var font = Font(f, size); w += Advance(font, t); }
        return w;
    }

    public void Draw(SKCanvas c, string s, float x, float baseline, SKTypeface face, float size, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        foreach (var (t, f) in Runs(s, face))
        {
            using var font = Font(f, size);
            c.DrawText(t, x, baseline, font, paint);
            x += Advance(font, t);
        }
    }

    /// <summary>Greedy word wrap; words longer than the width are broken by character.</summary>
    public List<string> Wrap(string text, SKTypeface face, float size, float width)
    {
        var lines = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(candidate, face, size) <= width) { line = candidate; continue; }
                if (line.Length > 0) lines.Add(line);
                line = word;
                while (line.Length > 1 && Measure(line, face, size) > width)
                {
                    int k = line.Length - 1;
                    while (k > 1 && Measure(line[..k], face, size) > width) k--;
                    lines.Add(line[..k]);
                    line = line[k..];
                }
            }
            lines.Add(line);
        }
        return lines;
    }
}

internal static class PdfStyle
{
    public static readonly SKColor Ink = SKColor.Parse("#1d2733");
    public static readonly SKColor Muted = SKColor.Parse("#5b6876");
    public static readonly SKColor Rule = SKColor.Parse("#c9d1da");
    public static readonly SKColor Accent = SKColor.Parse("#1f5f8b");
    public static readonly SKColor Water = SKColor.Parse("#7fb7dc");
    public static readonly SKColor Red = SKColor.Parse("#b3261e");
    public static readonly SKColor Amber = SKColor.Parse("#c98a00");
    public static readonly SKColor PlotBg = SKColor.Parse("#f6f8fa");
    public const float Body = 9f, Small = 7.5f, H1 = 15f, H2 = 11f;
    public static float Line(float size) => size * 1.32f;
}

/// <summary>Something with a height at a given width that can be drawn at a position.</summary>
internal interface IPdfBlock
{
    float Height(float width);
    void Draw(SKCanvas c, float x, float y, float width);
}

internal sealed class DrawBlock(float height, Action<SKCanvas, float, float, float> draw) : IPdfBlock
{
    public float Height(float width) => height;
    public void Draw(SKCanvas c, float x, float y, float width) => draw(c, x, y, width);
}

internal sealed class TextBlock(PdfFonts fonts, string text, SKTypeface face, float size, SKColor color, float spaceAfter = 4) : IPdfBlock
{
    public float Height(float width) => fonts.Wrap(text, face, size, width).Count * PdfStyle.Line(size) + spaceAfter;
    public void Draw(SKCanvas c, float x, float y, float width)
    {
        foreach (var line in fonts.Wrap(text, face, size, width))
        {
            fonts.Draw(c, line, x, y + size, face, size, color);
            y += PdfStyle.Line(size);
        }
    }
}

/// <summary>Two blocks side by side (atomic: never split across pages).</summary>
internal sealed class ColumnsBlock(IPdfBlock left, IPdfBlock? right, float gap = 16) : IPdfBlock
{
    private float Half(float w) => (w - gap) / 2;
    public float Height(float width) => Math.Max(left.Height(Half(width)), right?.Height(Half(width)) ?? 0);
    public void Draw(SKCanvas c, float x, float y, float width)
    {
        left.Draw(c, x, y, Half(width));
        right?.Draw(c, x + Half(width) + gap, y, Half(width));
    }
}

/// <summary>A message box with a coloured left bar.</summary>
internal sealed class MessageBlock(PdfFonts fonts, string label, string text, SKColor bar, SKColor fill) : IPdfBlock
{
    private const float Pad = 4, BarW = 3;
    private string Full => label + "  " + text;
    public float Height(float width) => fonts.Wrap(Full, fonts.Regular, PdfStyle.Body, width - 2 * Pad - BarW).Count * PdfStyle.Line(PdfStyle.Body) + 2 * Pad + 3;
    public void Draw(SKCanvas c, float x, float y, float width)
    {
        float h = Height(width) - 3;
        using (var p = new SKPaint { Color = fill }) c.DrawRect(x, y, width, h, p);
        using (var p = new SKPaint { Color = bar }) c.DrawRect(x, y, BarW, h, p);
        float ty = y + Pad;
        bool first = true;
        foreach (var line in fonts.Wrap(Full, fonts.Regular, PdfStyle.Body, width - 2 * Pad - BarW))
        {
            float tx = x + BarW + Pad;
            if (first && line.StartsWith(label, StringComparison.Ordinal))
            {
                fonts.Draw(c, label, tx, ty + PdfStyle.Body, fonts.Bold, PdfStyle.Body, PdfStyle.Ink);
                fonts.Draw(c, line[label.Length..], tx + fonts.Measure(label, fonts.Bold, PdfStyle.Body), ty + PdfStyle.Body, fonts.Regular, PdfStyle.Body, PdfStyle.Ink);
            }
            else fonts.Draw(c, line, tx, ty + PdfStyle.Body, fonts.Regular, PdfStyle.Body, PdfStyle.Ink);
            first = false;
            ty += PdfStyle.Line(PdfStyle.Body);
        }
    }
}

/// <summary>A table cell: main text plus an optional muted suffix (e.g. a symbol).</summary>
internal readonly record struct PdfCell(string Text, string? Suffix = null)
{
    public static implicit operator PdfCell(string s) => new(s);
}

internal sealed record PdfColumn(float Weight, bool Right = false);

/// <summary>
/// Table with wrapped cells. Drawn whole as a block (<see cref="IPdfBlock"/>) or row by row through
/// <see cref="PdfPager.AddTable"/>, which repeats the header on each new page.
/// </summary>
internal sealed class TableBlock(PdfFonts fonts, IReadOnlyList<PdfColumn> columns, IReadOnlyList<string>? header,
    IReadOnlyList<(IReadOnlyList<PdfCell> Cells, bool Bold)> rows, float size = PdfStyle.Body, string? caption = null) : IPdfBlock
{
    private const float PadX = 4, PadY = 2.5f;
    public IReadOnlyList<(IReadOnlyList<PdfCell> Cells, bool Bold)> Rows => rows;
    public string? Caption => caption;

    private float[] Widths(float width)
    {
        float total = columns.Sum(c => c.Weight);
        return columns.Select(c => width * c.Weight / total).ToArray();
    }

    private List<string> CellLines(PdfCell cell, SKTypeface face, float w) =>
        fonts.Wrap(cell.Suffix is null ? cell.Text : cell.Text + "  " + cell.Suffix, face, size, w - 2 * PadX);

    public float CaptionHeight => caption is null ? 0 : PdfStyle.Line(size) + 2 * PadY;
    public float HeaderHeight(float width) => header is null ? 0 : RowHeight(header.Select(h => new PdfCell(h.ToUpperInvariant())).ToList(), fonts.Bold, width, size * 0.9f);
    public float RowHeight(int i, float width) => RowHeight(rows[i].Cells, rows[i].Bold ? fonts.Bold : fonts.Regular, width, size);

    private float RowHeight(IReadOnlyList<PdfCell> cells, SKTypeface face, float width, float sz)
    {
        var w = Widths(width);
        int lines = 1;
        for (int k = 0; k < cells.Count && k < w.Length; k++)
            lines = Math.Max(lines, fonts.Wrap(cells[k].Suffix is null ? cells[k].Text : cells[k].Text + "  " + cells[k].Suffix, face, sz, w[k] - 2 * PadX).Count);
        return lines * PdfStyle.Line(sz) + 2 * PadY;
    }

    public float Height(float width) => CaptionHeight + HeaderHeight(width) + Enumerable.Range(0, rows.Count).Sum(i => RowHeight(i, width)) + 6;

    public void Draw(SKCanvas c, float x, float y, float width)
    {
        y = DrawCaption(c, x, y);
        y = DrawHeader(c, x, y, width);
        for (int i = 0; i < rows.Count; i++) y = DrawRow(c, i, x, y, width);
    }

    public float DrawCaption(SKCanvas c, float x, float y)
    {
        if (caption is null) return y;
        fonts.Draw(c, caption.ToUpperInvariant(), x + PadX, y + PadY + size * 0.9f, fonts.Bold, size * 0.9f, PdfStyle.Muted);
        return y + CaptionHeight;
    }

    public float DrawHeader(SKCanvas c, float x, float y, float width)
    {
        if (header is null) return y;
        float h = HeaderHeight(width);
        DrawCells(c, header.Select(s => new PdfCell(s.ToUpperInvariant())).ToList(), fonts.Bold, size * 0.9f, PdfStyle.Muted, x, y, width);
        Rule(c, x, y + h, width, 1f);
        return y + h;
    }

    public float DrawRow(SKCanvas c, int i, float x, float y, float width)
    {
        float h = RowHeight(i, width);
        DrawCells(c, rows[i].Cells, rows[i].Bold ? fonts.Bold : fonts.Regular, size, PdfStyle.Ink, x, y, width);
        Rule(c, x, y + h, width, 0.5f);
        return y + h;
    }

    private void DrawCells(SKCanvas c, IReadOnlyList<PdfCell> cells, SKTypeface face, float sz, SKColor color, float x, float y, float width)
    {
        var w = Widths(width);
        float cx = x;
        for (int k = 0; k < w.Length; k++)
        {
            if (k < cells.Count)
            {
                var cell = cells[k];
                float ly = y + PadY + sz;
                if (cell.Suffix is null)
                {
                    foreach (var line in fonts.Wrap(cell.Text, face, sz, w[k] - 2 * PadX))
                    {
                        float tx = columns[k].Right ? cx + w[k] - PadX - fonts.Measure(line, face, sz) : cx + PadX;
                        fonts.Draw(c, line, tx, ly, face, sz, color);
                        ly += PdfStyle.Line(sz);
                    }
                }
                else
                {
                    // Text then muted suffix; wraps as one string when too long.
                    var lines = fonts.Wrap(cell.Text + "  " + cell.Suffix, face, sz, w[k] - 2 * PadX);
                    if (lines.Count == 1)
                    {
                        fonts.Draw(c, cell.Text, cx + PadX, ly, face, sz, color);
                        fonts.Draw(c, "  " + cell.Suffix, cx + PadX + fonts.Measure(cell.Text, face, sz), ly, fonts.Regular, sz, PdfStyle.Muted);
                    }
                    else foreach (var line in lines) { fonts.Draw(c, line, cx + PadX, ly, face, sz, color); ly += PdfStyle.Line(sz); }
                }
            }
            cx += w[k];
        }
    }

    private static void Rule(SKCanvas c, float x, float y, float width, float stroke)
    {
        using var p = new SKPaint { Color = PdfStyle.Rule, StrokeWidth = stroke, IsAntialias = true };
        c.DrawLine(x, y, x + width, y, p);
    }
}

/// <summary>Collects drawing operations into pages; a block that does not fit starts a new page.</summary>
internal sealed class PdfPager(PdfFonts fonts, float pageWidth, float pageHeight, float margin = 43)
{
    private const float FooterHeight = 30, RunningHeaderHeight = 16;
    private sealed record Page(List<Action<SKCanvas>> Ops, string? RunningHeader, PageContext Context);
    public sealed record PageContext(string? Watermark, string? FooterText, string Disclaimer);

    private readonly List<Page> _pages = [];
    private float _y;
    private string? _runningHeader;
    private PageContext _context = new(null, null, "");

    public PdfFonts Fonts => fonts;
    public float Left => margin;
    public float Width => pageWidth - 2 * margin;
    private float Top(Page p) => margin + (p.RunningHeader is null ? 0 : RunningHeaderHeight);
    private float Bottom => pageHeight - margin - FooterHeight;
    private float Remaining => Bottom - _y;
    private bool AtTop => _pages.Count > 0 && _y <= Top(_pages[^1]) + 0.01f;

    /// <summary>Starts a new page; <paramref name="runningHeader"/> is shown on later pages of the same section.</summary>
    public void StartSection(string runningHeader, PageContext context)
    {
        _context = context;
        _runningHeader = null;
        NewPage();
        _runningHeader = runningHeader + " (continued)";
    }

    public void NewPage()
    {
        _pages.Add(new Page([], _runningHeader, _context));
        _y = Top(_pages[^1]);
    }

    public void Space(float h) { if (!AtTop) _y = Math.Min(_y + h, Bottom); }

    public void Add(IPdfBlock block, float keepWithNext = 0)
    {
        float h = block.Height(Width);
        if (h + keepWithNext > Remaining && !AtTop) NewPage();
        float y = _y;
        _pages[^1].Ops.Add(c => block.Draw(c, Left, y, Width));
        _y += h;
    }

    /// <summary>Paragraph that may break between lines.</summary>
    public void Paragraph(string text, SKTypeface? face = null, float size = PdfStyle.Body, SKColor? color = null, float spaceAfter = 5)
    {
        face ??= fonts.Regular;
        var col = color ?? PdfStyle.Ink;
        foreach (var line in fonts.Wrap(text, face, size, Width))
        {
            var l = line;
            Add(new DrawBlock(PdfStyle.Line(size), (c, x, y, _) => fonts.Draw(c, l, x, y + size, face, size, col)));
        }
        Space(spaceAfter);
    }

    public void Heading(string text, float keepWithNext = 48)
    {
        Space(8);
        Add(new DrawBlock(PdfStyle.Line(PdfStyle.H2) + 5, (c, x, y, w) =>
        {
            fonts.Draw(c, text, x, y + PdfStyle.H2, fonts.Bold, PdfStyle.H2, PdfStyle.Accent);
            using var p = new SKPaint { Color = PdfStyle.Rule, StrokeWidth = 0.75f };
            c.DrawLine(x, y + PdfStyle.Line(PdfStyle.H2) + 1, x + w, y + PdfStyle.Line(PdfStyle.H2) + 1, p);
        }), keepWithNext);
    }

    /// <summary>Adds a table row by row, repeating its header after a page break.</summary>
    public void AddTable(TableBlock t)
    {
        float w = Width;
        float head = t.CaptionHeight + t.HeaderHeight(w);
        float first = t.Rows.Count > 0 ? t.RowHeight(0, w) : 0;
        if (head + first > Remaining && !AtTop) NewPage();
        void Header() { float y = _y; _pages[^1].Ops.Add(c => t.DrawHeader(c, Left, t.DrawCaption(c, Left, y), w)); _y += head; }
        Header();
        for (int i = 0; i < t.Rows.Count; i++)
        {
            float h = t.RowHeight(i, w);
            if (h > Remaining && !AtTop) { NewPage(); Header(); }
            float y = _y; int row = i;
            _pages[^1].Ops.Add(c => t.DrawRow(c, row, Left, y, w));
            _y += h;
        }
        Space(6);
    }

    /// <summary>Two tables side by side that may break across pages together (rows stay whole).</summary>
    public void AddTablePair(TableBlock left, TableBlock? right, float gap = 16)
    {
        float half = (Width - gap) / 2;
        var tables = right is null ? new[] { left } : new[] { left, right };
        var next = new int[tables.Length];
        bool first = true;
        float minFirst = tables.Max(t => t.CaptionHeight + (t.Rows.Count > 0 ? t.RowHeight(0, half) : 0));
        if (minFirst > Remaining && !AtTop) NewPage();
        while (true)
        {
            float startY = _y, maxY = _y;
            bool progressed = false;
            for (int k = 0; k < tables.Length; k++)
            {
                var t = tables[k];
                float x = Left + k * (half + gap), y = startY;
                if (first) { float yy = y; _pages[^1].Ops.Add(c => t.DrawCaption(c, x, yy)); y += t.CaptionHeight; }
                while (next[k] < t.Rows.Count)
                {
                    float h = t.RowHeight(next[k], half);
                    if (y + h > Bottom && !(AtTop && !progressed)) break;
                    float yy = y; int row = next[k];
                    _pages[^1].Ops.Add(c => t.DrawRow(c, row, x, yy, half));
                    y += h; next[k]++; progressed = true;
                }
                maxY = Math.Max(maxY, y);
            }
            _y = maxY;
            first = false;
            if (Enumerable.Range(0, tables.Length).All(k => next[k] >= tables[k].Rows.Count)) break;
            NewPage();
        }
        Space(6);
    }

    /// <summary>Writes every page to a PDF document, drawing watermark, running header and footer.</summary>
    public void Render(SKDocument doc)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            var c = doc.BeginPage(pageWidth, pageHeight);
            if (!string.IsNullOrWhiteSpace(page.Context.Watermark)) Watermark(c, page.Context.Watermark!);
            if (page.RunningHeader is { } rh)
                fonts.Draw(c, rh, margin, margin + PdfStyle.Small, fonts.Regular, PdfStyle.Small, PdfStyle.Muted);
            foreach (var op in page.Ops) op(c);
            Footer(c, page.Context, i + 1, _pages.Count);
            doc.EndPage();
        }
    }

    public int PageCount => _pages.Count;

    private void Watermark(SKCanvas c, string text)
    {
        c.Save();
        c.Translate(pageWidth / 2, pageHeight / 2);
        c.RotateDegrees(-30);
        float size = 64;
        float w = fonts.Measure(text, fonts.Bold, size);
        if (w > pageWidth * 0.95f) { size *= pageWidth * 0.95f / w; w = fonts.Measure(text, fonts.Bold, size); }
        fonts.Draw(c, text, -w / 2, size / 3, fonts.Bold, size, PdfStyle.Accent.WithAlpha(0x1C));
        c.Restore();
    }

    private void Footer(SKCanvas c, PageContext ctx, int page, int pages)
    {
        float y = pageHeight - margin - FooterHeight + 8;
        using (var p = new SKPaint { Color = PdfStyle.Rule, StrokeWidth = 0.75f }) c.DrawLine(margin, y, pageWidth - margin, y, p);
        string right = $"Page {page} of {pages}";
        float rightW = fonts.Measure(right, fonts.Regular, PdfStyle.Small);
        fonts.Draw(c, right, pageWidth - margin - rightW, y + 4 + PdfStyle.Small, fonts.Regular, PdfStyle.Small, PdfStyle.Muted);
        float leftW = Width * 0.62f;
        float ly = y + 4 + PdfStyle.Small;
        foreach (var line in fonts.Wrap(ctx.Disclaimer, fonts.Regular, PdfStyle.Small, leftW).Take(2))
        {
            fonts.Draw(c, line, margin, ly, fonts.Regular, PdfStyle.Small, PdfStyle.Muted);
            ly += PdfStyle.Line(PdfStyle.Small);
        }
        if (!string.IsNullOrWhiteSpace(ctx.FooterText))
        {
            float fx = margin + leftW + 12, fw = pageWidth - margin - rightW - 12 - fx;
            float fy = y + 4 + PdfStyle.Small;
            foreach (var line in fonts.Wrap(ctx.FooterText!, fonts.Regular, PdfStyle.Small, Math.Max(fw, 20)).Take(2))
            {
                fonts.Draw(c, line, fx, fy, fonts.Regular, PdfStyle.Small, PdfStyle.Muted);
                fy += PdfStyle.Line(PdfStyle.Small);
            }
        }
    }
}
