using System.Globalization;
using System.Net;
using System.Text;
using MasterFlow.Application.Tools;

namespace MasterFlow.Reporting;

/// <summary>Axis ranges, ticks and mapping for a <see cref="ToolChart"/>, shared by the SVG (HTML) and PDF renderers.</summary>
internal sealed class ToolChartLayout
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static readonly string[] Palette = ["#1f5f8b", "#b3261e", "#c98a00", "#2e7d32", "#6a3d9a", "#5b6876"];

    public ToolChart Chart { get; }
    public double X0 { get; }
    public double X1 { get; }
    public double Y0 { get; }
    public double Y1 { get; }
    public IReadOnlyList<double> XTicks { get; }
    public IReadOnlyList<double> YTicks { get; }
    public float Width { get; }
    public float Height { get; }
    public const float PadL = 52, PadR = 12, PadT = 24, PadB = 34;

    public ToolChartLayout(ToolChart chart, float width, float height)
    {
        Chart = chart; Width = width; Height = height;
        var pts = chart.Series.SelectMany(s => s.Points).Where(Plottable).ToList();
        var ys = pts.Select(p => p.Y).Concat((chart.Lines ?? []).Select(l => l.Y).Where(y => !chart.LogY || y > 0)).ToList();
        double xa = pts.Count > 0 ? pts.Min(p => p.X) : 1, xb = pts.Count > 0 ? pts.Max(p => p.X) : 10;
        double ya = ys.Count > 0 ? ys.Min() : 1, yb = ys.Count > 0 ? ys.Max() : 10;
        if (!chart.LogX && xa > 0 && xa < 0.25 * xb) xa = 0;       // flows and times start from zero
        if (!chart.LogY && ya > 0 && ya < 0.25 * yb) ya = 0;
        (X0, X1, XTicks) = chart.LogX ? LogAxis(xa, xb) : Nice(xa, xb);
        (Y0, Y1, YTicks) = chart.LogY ? LogAxis(ya, yb) : Nice(ya, yb);
    }

    /// <summary>A finite point, positive on any log axis.</summary>
    public bool Plottable((double X, double Y) p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && (!Chart.LogX || p.X > 0) && (!Chart.LogY || p.Y > 0);

    /// <summary>Log axis: the data range with 1-2-5 gridlines per decade.</summary>
    public static (double Lo, double Hi, IReadOnlyList<double> Ticks) LogAxis(double lo, double hi)
    {
        if (!(lo > 0)) lo = hi > 0 ? hi / 10 : 1;
        if (!(hi > lo)) { lo /= 2; hi = lo * 4; }
        var ticks = new List<double>();
        for (int e = (int)Math.Floor(Math.Log10(lo)) - 1; e <= (int)Math.Ceiling(Math.Log10(hi)); e++)
            foreach (var m in new[] { 1.0, 2, 5 }) { double t = m * Math.Pow(10, e); if (t >= lo * (1 - 1e-9) && t <= hi * (1 + 1e-9)) ticks.Add(t); }
        return (lo, hi, ticks);
    }

    public static string Color(ChartSeries s, int index) => Palette[(s.Color >= 0 ? s.Color : index) % Palette.Length];

    public float PlotW => Width - PadL - PadR;
    public float PlotH => Height - PadT - PadB;
    private static double T(double v, bool log) => log ? Math.Log10(v) : v;
    public float MapX(double x) => PadL + (float)((T(x, Chart.LogX) - T(X0, Chart.LogX)) / (T(X1, Chart.LogX) - T(X0, Chart.LogX))) * PlotW;
    public float MapY(double y) => PadT + PlotH - (float)((T(y, Chart.LogY) - T(Y0, Chart.LogY)) / (T(Y1, Chart.LogY) - T(Y0, Chart.LogY))) * PlotH;

    public static string Tick(double v, IReadOnlyList<double> ticks)
    {
        if (ticks.Count > 2 && ticks[2] / ticks[1] > 1.5 && ticks[1] / ticks[0] > 1.5) return v.ToString("0.###", Inv);   // log axis
        double step = ticks.Count > 1 ? ticks[1] - ticks[0] : 1;
        int dec = step >= 1 ? 0 : Math.Min(4, (int)Math.Ceiling(-Math.Log10(step) - 1e-9));
        return v.ToString("F" + dec, Inv);
    }

    /// <summary>A range widened to round ticks (1, 2, 2.5 or 5 × 10ⁿ), about six of them.</summary>
    public static (double Lo, double Hi, IReadOnlyList<double> Ticks) Nice(double lo, double hi)
    {
        if (!(hi > lo)) { double d = Math.Abs(lo) > 0 ? Math.Abs(lo) * 0.05 : 1; lo -= d; hi += d; }
        double raw = (hi - lo) / 6, mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double step = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * mag).First(s => s >= raw * 0.999);
        double a = Math.Floor(lo / step + 1e-9) * step, b = Math.Ceiling(hi / step - 1e-9) * step;
        var ticks = new List<double>();
        for (double t = a; t <= b + step * 1e-6; t += step) ticks.Add(Math.Round(t, 10));
        return (a, b, ticks);
    }

    /// <summary>Inline SVG for the HTML report.</summary>
    public static string ToSvg(ToolChart chart, float width = 720, float height = 300)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        static string F(double v) => v.ToString("0.##", Inv);
        var g = new ToolChartLayout(chart, width, height);
        var sb = new StringBuilder();
        sb.Append($"<figure class=\"chart\"><figcaption>{E(chart.Title)}</figcaption><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(width)} {F(height)}\" width=\"{F(width)}\" height=\"{F(height)}\" font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"10\">");
        sb.Append($"<rect x=\"{F(PadL)}\" y=\"{F(PadT)}\" width=\"{F(g.PlotW)}\" height=\"{F(g.PlotH)}\" fill=\"#f6f8fa\"/>");
        foreach (var t in g.YTicks)
            sb.Append($"<line x1=\"{F(PadL)}\" x2=\"{F(PadL + g.PlotW)}\" y1=\"{F(g.MapY(t))}\" y2=\"{F(g.MapY(t))}\" stroke=\"#dde3ea\"/><text x=\"{F(PadL - 4)}\" y=\"{F(g.MapY(t) + 3.5)}\" text-anchor=\"end\" fill=\"#5b6876\">{Tick(t, g.YTicks)}</text>");
        foreach (var t in g.XTicks)
            sb.Append($"<line x1=\"{F(g.MapX(t))}\" x2=\"{F(g.MapX(t))}\" y1=\"{F(PadT)}\" y2=\"{F(PadT + g.PlotH)}\" stroke=\"#dde3ea\"/><text x=\"{F(g.MapX(t))}\" y=\"{F(PadT + g.PlotH + 13)}\" text-anchor=\"middle\" fill=\"#5b6876\">{Tick(t, g.XTicks)}</text>");
        sb.Append($"<text x=\"{F(PadL + g.PlotW / 2)}\" y=\"{F(height - 4)}\" text-anchor=\"middle\" fill=\"#1d2733\">{E(chart.XLabel)}</text>");
        sb.Append($"<text transform=\"translate(12 {F(PadT + g.PlotH / 2)}) rotate(-90)\" text-anchor=\"middle\" fill=\"#1d2733\">{E(chart.YLabel)}</text>");
        foreach (var l in (chart.Lines ?? []).Where(l => !chart.LogY || l.Y > 0))
            sb.Append($"<line x1=\"{F(PadL)}\" x2=\"{F(PadL + g.PlotW)}\" y1=\"{F(g.MapY(l.Y))}\" y2=\"{F(g.MapY(l.Y))}\" stroke=\"#c98a00\" stroke-dasharray=\"2 3\"/><text x=\"{F(PadL + g.PlotW - 4)}\" y=\"{F(g.MapY(l.Y) - 3)}\" text-anchor=\"end\" fill=\"#8a5a00\">{E(l.Label)} {F(l.Y)}</text>");
        for (int i = 0; i < chart.Series.Count; i++)
        {
            var s = chart.Series[i];
            var pts = s.Points.Where(g.Plottable).ToList();
            if (s.MarkersOnly) { foreach (var p in pts) sb.Append($"<circle cx=\"{F(g.MapX(p.X))}\" cy=\"{F(g.MapY(p.Y))}\" r=\"3.5\" fill=\"{Color(s, i)}\"/>"); continue; }
            if (pts.Count < 2) continue;
            string path = string.Join(" ", pts.Select((p, k) => (k == 0 ? "M" : "L") + F(g.MapX(p.X)) + "," + F(g.MapY(p.Y))));
            sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{Color(s, i)}\" stroke-width=\"1.6\"{(s.Dashed ? " stroke-dasharray=\"6 3\"" : "")}/>");
        }
        float lx = PadL, ly = 13;
        for (int i = 0; i < chart.Series.Count; i++)
        {
            var s = chart.Series[i];
            if (s.MarkersOnly) sb.Append($"<circle cx=\"{F(lx + 9)}\" cy=\"{F(ly - 3.5)}\" r=\"3.5\" fill=\"{Color(s, i)}\"/><text x=\"{F(lx + 22)}\" y=\"{F(ly)}\" fill=\"#1d2733\">{E(s.Name)}</text>");
            else sb.Append($"<line x1=\"{F(lx)}\" x2=\"{F(lx + 18)}\" y1=\"{F(ly - 3.5)}\" y2=\"{F(ly - 3.5)}\" stroke=\"{Color(s, i)}\" stroke-width=\"1.6\"{(s.Dashed ? " stroke-dasharray=\"6 3\"" : "")}/><text x=\"{F(lx + 22)}\" y=\"{F(ly)}\" fill=\"#1d2733\">{E(s.Name)}</text>");
            lx += 22 + 6.2f * s.Name.Length + 14;
        }
        sb.Append("</svg></figure>");
        return sb.ToString();
    }
}
