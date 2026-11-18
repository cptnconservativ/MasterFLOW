using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MasterFlow.Application;
using MasterFlow.Domain;

namespace MasterFlow.Desktop.Controls;

/// <summary>
/// Draws a channel cross-section with the normal-depth water surface and a dashed critical-depth line.
/// Uses true aspect ratio when <see cref="TrueScale"/> is set; otherwise fills the area and states that
/// scales differ.
/// </summary>
public sealed class CrossSectionView : Control
{
    public static readonly StyledProperty<CrossSectionDrawing?> DrawingProperty = AvaloniaProperty.Register<CrossSectionView, CrossSectionDrawing?>(nameof(Drawing));
    public static readonly StyledProperty<ChannelSection?> SectionProperty = AvaloniaProperty.Register<CrossSectionView, ChannelSection?>(nameof(Section));
    public static readonly StyledProperty<string> LengthUnitProperty = AvaloniaProperty.Register<CrossSectionView, string>(nameof(LengthUnit), "ft");
    public static readonly StyledProperty<bool> TrueScaleProperty = AvaloniaProperty.Register<CrossSectionView, bool>(nameof(TrueScale));

    static CrossSectionView() => AffectsRender<CrossSectionView>(DrawingProperty, SectionProperty, LengthUnitProperty, TrueScaleProperty);

    public CrossSectionDrawing? Drawing { get => GetValue(DrawingProperty); set => SetValue(DrawingProperty, value); }
    public ChannelSection? Section { get => GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
    public string LengthUnit { get => GetValue(LengthUnitProperty); set => SetValue(LengthUnitProperty, value); }
    public bool TrueScale { get => GetValue(TrueScaleProperty); set => SetValue(TrueScaleProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        bool dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var ink = dark ? Color.Parse("#D6DEE6") : Color.Parse("#1D2733");
        var muted = dark ? Color.Parse("#8994A0") : Color.Parse("#6B7785");
        var grid = dark ? Color.Parse("#2A323C") : Color.Parse("#E3E8EE");
        var water = Color.Parse("#7FB7DC");
        var accent = dark ? Color.Parse("#9CCBEA") : Color.Parse("#1F5F8B");
        var crit = Color.Parse("#C0392B");

        var d = Drawing;
        if (d is null || Section is null)
        {
            Text(ctx, "Enter a valid section to see the cross-section.", new Point(16, 16), muted, 13);
            return;
        }

        const double padL = 52, padR = 120, padT = 34, padB = 40;
        double w = Math.Max(10, bounds.Width - padL - padR), h = Math.Max(10, bounds.Height - padT - padB);
        double sx = w / d.Width, sy = h / d.Height;
        bool trueScale = TrueScale || Section is CircularSection or EllipticalSection; // never distort a conduit
        if (trueScale) { double s = Math.Min(sx, sy); sx = sy = s; }
        double x0 = padL + (w - d.Width * sx) / 2, y0 = padT + h;
        double xMin = d.Outline.Min(p => p.X); // surveyed stations need not start at 0
        Point P(double x, double y) => new(x0 + (x - xMin) * sx, y0 - y * sy);

        // Grid with "nice" spacing in each direction.
        var gridPen = new Pen(new SolidColorBrush(grid), 1);
        double gy = Nice(d.Height / 5), gx = Nice(d.Width / 8);
        for (double y = 0; y <= d.Height + 1e-9; y += gy)
        {
            ctx.DrawLine(gridPen, P(xMin, y), P(xMin + d.Width, y));
            Text(ctx, y.ToString("0.##", CultureInfo.CurrentCulture), new Point(x0 - 40, P(xMin, y).Y - 8), muted, 11);
        }
        for (double x = Math.Ceiling(xMin / gx - 1e-9) * gx; x <= xMin + d.Width + 1e-9; x += gx)
        {
            ctx.DrawLine(gridPen, P(x, 0), P(x, d.Height));
            Text(ctx, (Math.Abs(x) < 1e-9 * gx ? 0 : x).ToString("0.##", CultureInfo.CurrentCulture), new Point(P(x, 0).X - 8, y0 + 6), muted, 11);
        }
        Text(ctx, Section is IrregularSection ? $"Station ({LengthUnit})" : $"Offset ({LengthUnit})", new Point(x0 + d.Width * sx / 2 - 30, y0 + 22), muted, 11);
        Text(ctx, Section is IrregularSection ir ? $"Height above lowest point {ir.MinElevation.ToString("0.###", CultureInfo.CurrentCulture)} ({LengthUnit})" : $"Depth ({LengthUnit})", new Point(4, 4), muted, 11);

        if (d.Water.Count > 2)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(d.Water[0].X, d.Water[0].Y), true);
                foreach (var p in d.Water.Skip(1)) g.LineTo(P(p.X, p.Y));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(water, 0.75), null, geo);
        }

        var outline = new StreamGeometry();
        using (var g = outline.Open())
        {
            g.BeginFigure(P(d.Outline[0].X, d.Outline[0].Y), false);
            foreach (var p in d.Outline.Skip(1)) g.LineTo(P(p.X, p.Y));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(ink), 2.5), outline);

        double? lastLabelY = null;
        void Level(double? y, Color color, string label, bool dashed)
        {
            if (y is not > 0) return;
            var (l, r) = d.ExtentAt(Section!, y.Value);
            var pen = new Pen(new SolidColorBrush(color), 1.6, dashed ? new DashStyle([5, 3], 0) : null);
            ctx.DrawLine(pen, P(l, y.Value), P(r, y.Value));
            double ty = P(r, y.Value).Y - 8;
            if (lastLabelY is { } prev && Math.Abs(ty - prev) < 15) ty = prev + 15; // keep labels from overlapping
            lastLabelY = ty;
            Text(ctx, $"{label} = {y.Value.ToString("F3", CultureInfo.CurrentCulture)} {LengthUnit}", new Point(P(r, y.Value).X + 6, ty), color, 12);
        }
        Level(d.ChannelDepth, muted, "Top", true);
        Level(d.WaterDepth, accent, "yn", false);
        Level(d.CriticalDepth, crit, "yc", true);
        if (!trueScale)
            Text(ctx, "Vertical exaggeration — not to scale", new Point(bounds.Width - padR - 190, 4), muted, 10);
    }

    internal static double Nice(double raw)
    {
        if (!(raw > 0)) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double f = raw / mag;
        return (f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10) * mag;
    }

    internal static void Text(DrawingContext ctx, string s, Point at, Color color, double size)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, new SolidColorBrush(color));
        ctx.DrawText(ft, at);
    }
}
