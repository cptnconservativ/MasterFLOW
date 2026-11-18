using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MasterFlow.Application;

namespace MasterFlow.Desktop.Controls;

/// <summary>Rating curve: discharge on the horizontal axis, depth on the vertical axis, one marker per table row.</summary>
public sealed class RatingChartView : Control
{
    public static readonly StyledProperty<RatingTable?> TableProperty = AvaloniaProperty.Register<RatingChartView, RatingTable?>(nameof(Table));
    public static readonly StyledProperty<string> LengthUnitProperty = AvaloniaProperty.Register<RatingChartView, string>(nameof(LengthUnit), "ft");
    public static readonly StyledProperty<string> DischargeUnitProperty = AvaloniaProperty.Register<RatingChartView, string>(nameof(DischargeUnit), "cfs");

    static RatingChartView() => AffectsRender<RatingChartView>(TableProperty, LengthUnitProperty, DischargeUnitProperty);

    public RatingTable? Table { get => GetValue(TableProperty); set => SetValue(TableProperty, value); }
    public string LengthUnit { get => GetValue(LengthUnitProperty); set => SetValue(LengthUnitProperty, value); }
    public string DischargeUnit { get => GetValue(DischargeUnitProperty); set => SetValue(DischargeUnitProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        bool dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var ink = dark ? Color.Parse("#9CCBEA") : Color.Parse("#1F5F8B");
        var muted = dark ? Color.Parse("#8994A0") : Color.Parse("#6B7785");
        var grid = dark ? Color.Parse("#2A323C") : Color.Parse("#E3E8EE");
        var rows = Table?.Rows;
        if (rows is null || rows.Count < 2)
        {
            CrossSectionView.Text(ctx, "Generate a table to see the rating curve.", new Point(8, 8), muted, 12);
            return;
        }
        const double padL = 44, padR = 12, padT = 18, padB = 32;
        double w = Math.Max(10, Bounds.Width - padL - padR), h = Math.Max(10, Bounds.Height - padT - padB);
        double qMax = rows.Max(r => r.Discharge), yMax = rows.Max(r => r.Depth);
        double gq = CrossSectionView.Nice(qMax / 5), gy = CrossSectionView.Nice(yMax / 4);
        qMax = Math.Ceiling(qMax / gq) * gq; yMax = Math.Ceiling(yMax / gy) * gy;
        Point P(double q, double y) => new(padL + q / qMax * w, padT + h - y / yMax * h);
        var gridPen = new Pen(new SolidColorBrush(grid), 1);
        for (double y = 0; y <= yMax + 1e-9; y += gy)
        {
            ctx.DrawLine(gridPen, P(0, y), P(qMax, y));
            CrossSectionView.Text(ctx, y.ToString("0.##", CultureInfo.CurrentCulture), new Point(4, P(0, y).Y - 8), muted, 10);
        }
        for (double q = 0; q <= qMax + 1e-9; q += gq)
        {
            ctx.DrawLine(gridPen, P(q, 0), P(q, yMax));
            CrossSectionView.Text(ctx, q.ToString("0.##", CultureInfo.CurrentCulture), new Point(P(q, 0).X - 10, padT + h + 4), muted, 10);
        }
        CrossSectionView.Text(ctx, $"Q ({DischargeUnit})", new Point(padL + w / 2 - 16, padT + h + 17), muted, 10);
        CrossSectionView.Text(ctx, $"Depth ({LengthUnit})", new Point(4, 0), muted, 10);
        var pen = new Pen(new SolidColorBrush(ink), 2);
        var brush = new SolidColorBrush(ink);
        var ordered = rows.OrderBy(r => r.Depth).ToList();
        for (int i = 1; i < ordered.Count; i++)
            ctx.DrawLine(pen, P(ordered[i - 1].Discharge, ordered[i - 1].Depth), P(ordered[i].Discharge, ordered[i].Depth));
        foreach (var r in ordered) ctx.DrawEllipse(brush, null, P(r.Discharge, r.Depth), 2.5, 2.5);
    }
}
