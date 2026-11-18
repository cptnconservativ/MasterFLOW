using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MasterFlow.Application;
using MasterFlow.Reporting;

namespace MasterFlow.Desktop.Controls;

/// <summary>
/// HGL/EGL profile along one flow path: pipe (invert to crown), structures with rims, rim criterion ticks,
/// HGL (solid) and EGL (dashed). Same frame and layers as the PDF and HTML report profiles.
/// </summary>
public sealed class NetworkProfileView : Control
{
    public static readonly StyledProperty<NetworkProfile?> ProfileProperty = AvaloniaProperty.Register<NetworkProfileView, NetworkProfile?>(nameof(Profile));
    public static readonly StyledProperty<string> LengthUnitProperty = AvaloniaProperty.Register<NetworkProfileView, string>(nameof(LengthUnit), "ft");

    static NetworkProfileView() => AffectsRender<NetworkProfileView>(ProfileProperty, LengthUnitProperty);

    public NetworkProfile? Profile { get => GetValue(ProfileProperty); set => SetValue(ProfileProperty, value); }
    public string LengthUnit { get => GetValue(LengthUnitProperty); set => SetValue(LengthUnitProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        bool dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var ink = dark ? Color.Parse("#D5DCE4") : Color.Parse("#1D2733");
        var muted = dark ? Color.Parse("#8994A0") : Color.Parse("#5B6876");
        var gridC = dark ? Color.Parse("#2A323C") : Color.Parse("#E3E8EE");
        var hglC = dark ? Color.Parse("#7DB8E8") : Color.Parse("#1F5F8B");
        var eglC = dark ? Color.Parse("#F08A80") : Color.Parse("#B3261E");
        var groundC = dark ? Color.Parse("#C49A5A") : Color.Parse("#7A5C2E");
        var critC = Color.Parse("#C98A00");
        var pipeFill = dark ? Color.Parse("#323B46") : Color.Parse("#DFE5EB");
        var p = Profile;
        if (p is null || p.Pipes.Count == 0)
        {
            CrossSectionView.Text(ctx, "Solve the network to see the HGL/EGL profile.", new Point(8, 8), muted, 12);
            return;
        }
        float w = (float)Math.Max(100, Bounds.Width);
        const float bandFont = 10f, textW = 42f;
        var rows = ProfileFrame.BandRows(p);
        var (rot, rh) = ProfileFrame.Of(p, w, 300, 128).BandLayout(p, textW, bandFont);
        float bandH = rows.Count * rh;
        float h = (float)Math.Max(80, Bounds.Height - bandH - 48);
        var g = ProfileFrame.Of(p, w, h, 128);
        Point P(double s, double z) { var (x, y) = g.Map(s, z); return new Point(x, y); }
        var gridPen = new Pen(new SolidColorBrush(gridC), 1);
        foreach (var z in g.ZTicks) { var a = P(g.S0, z); ctx.DrawLine(gridPen, a, P(g.S1, z)); CrossSectionView.Text(ctx, z.ToString("0.#", CultureInfo.CurrentCulture), new Point(2, a.Y - 7), muted, 10); }
        foreach (var s in g.STicks) { var a = P(s, g.Z0); CrossSectionView.Text(ctx, s.ToString("0", CultureInfo.CurrentCulture), new Point(a.X - 8, h - 16), muted, 10); }

        StreamGeometry Poly(IReadOnlyList<(double S, double Z)> pts, bool close = false, IReadOnlyList<(double S, double Z)>? back = null)
        {
            var geo = new StreamGeometry();
            using var gc = geo.Open();
            gc.BeginFigure(P(pts[0].S, pts[0].Z), close);
            foreach (var q in pts.Skip(1)) gc.LineTo(P(q.S, q.Z));
            if (back is not null) for (int i = back.Count - 1; i >= 0; i--) gc.LineTo(P(back[i].S, back[i].Z));
            gc.EndFigure(close);
            return geo;
        }
        void Line(IReadOnlyList<(double S, double Z)> pts, Color col, double width, IDashStyle? dash = null)
        {
            if (pts.Count < 2) return;
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(col), width, dash, lineJoin: PenLineJoin.Round), Poly(pts));
        }
        if (p.Invert.Count > 1) ctx.DrawGeometry(new SolidColorBrush(pipeFill), null, Poly(p.Invert, true, p.Crown));
        Line(p.Invert, ink, 1); Line(p.Crown, ink, 1);
        Line(p.Ground, groundC, 1, new DashStyle([5, 2], 0));
        var structPen = new Pen(new SolidColorBrush(muted), 4);
        var critPen = new Pen(new SolidColorBrush(critC), 1.5);
        foreach (var s in p.Structures)
        {
            double top = s.Rim ?? Math.Max(s.Hgl, s.Egl);
            var b = P(s.Station, top);
            ctx.DrawLine(structPen, P(s.Station, s.Bottom), b);
            if (s.CriteriaLine is { } cl) { var c = P(s.Station, cl); ctx.DrawLine(critPen, new Point(c.X - 8, c.Y), new Point(c.X + 8, c.Y)); }
            CrossSectionView.Text(ctx, s.Name, new Point(b.X - 3.2 * s.Name.Length, b.Y - 28), ink, 10);
            string t = "HGL " + s.Hgl.ToString("F2", CultureInfo.CurrentCulture);
            CrossSectionView.Text(ctx, t, new Point(b.X - 2.9 * t.Length, b.Y - 16), s.MeetsCriteria ? hglC : eglC, 10);
        }
        Line(p.Egl, eglC, 1.3, new DashStyle([4, 2.5], 0));
        Line(p.Hgl, hglC, 1.8);

        // Reading band: station, HGL, EGL and the criterion limit every reading interval.
        var readings = p.Readings();
        var tickPen = new Pen(new SolidColorBrush(gridC), 1, new DashStyle([2, 2], 0));
        foreach (var rd in readings) ctx.DrawLine(tickPen, P(rd.Station, g.Z1), P(rd.Station, g.Z0));
        double bandTop = h + 2;
        for (int i = 0; i < rows.Count; i++)
        {
            double ry = bandTop + i * rh;
            ctx.DrawLine(gridPen, new Point(0, ry), new Point(w, ry));
            var (label, value, isHgl) = rows[i];
            var ft = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Typeface.Default.FontFamily, weight: FontWeight.SemiBold), bandFont, new SolidColorBrush(ink));
            ctx.DrawText(ft, new Point(2, ry + (rh - ft.Height) / 2));
            foreach (var rd in readings)
            {
                string t = ProfileFrame.BandText(value(rd), i == 0);
                var col = isHgl && !rd.MeetsCriteria ? eglC : ink;
                var tx = new FormattedText(t, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, bandFont, new SolidColorBrush(col));
                double px = P(rd.Station, g.Z0).X;
                if (!rot) ctx.DrawText(tx, new Point(px - tx.Width / 2, ry + (rh - tx.Height) / 2));
                else
                    using (ctx.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(px - tx.Height / 2, ry + rh - 3)))
                        ctx.DrawText(tx, new Point(0, 0));
            }
        }
        ctx.DrawLine(gridPen, new Point(0, bandTop + bandH), new Point(w, bandTop + bandH));

        double lx = w - 290, ly = 8;
        void Key(string t, Color col, IDashStyle? dash, double off)
        {
            ctx.DrawLine(new Pen(new SolidColorBrush(col), 1.6, dash), new Point(lx + off, ly + 7), new Point(lx + off + 18, ly + 7));
            CrossSectionView.Text(ctx, t, new Point(lx + off + 22, ly), ink, 10);
        }
        Key("HGL", hglC, null, 0); Key("EGL", eglC, new DashStyle([4, 2.5], 0), 56); Key("Rim/ground", groundC, new DashStyle([5, 2], 0), 112);
        if (p.Structures.Any(s => s.CriteriaLine is not null)) Key("Criteria", critC, null, 210);
        CrossSectionView.Text(ctx, $"Station from the outfall ({LengthUnit}); elevation ({LengthUnit}); vertical scale exaggerated. Red HGL: criterion not met.",
            new Point(4, bandTop + bandH + 2), muted, 10);
        var cap = ProfileFrame.BandCaption(p, LengthUnit).Split('\n');
        for (int i = 0; i < cap.Length; i++) CrossSectionView.Text(ctx, cap[i], new Point(4, bandTop + bandH + 16 + 14 * i), muted, 10);
    }
}
