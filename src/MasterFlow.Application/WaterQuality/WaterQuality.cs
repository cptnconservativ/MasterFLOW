using MasterFlow.Application.Tools;

namespace MasterFlow.Application.WaterQuality;

/// <summary>
/// Water quality capture volume and BMP sizing equations. Every function is a published equation or first principles;
/// the tool decides which applies and says so.
/// </summary>
public static class Wq
{
    /// <summary>
    /// WQCV coefficient a for a drain time, MHFD Urban Storm Drainage Criteria Manual Vol. 3, Table 3-2: 12 h → 0.8, 24 h → 0.9,
    /// 40 h → 1.0; linear between, and the end values outside.
    /// </summary>
    public static double DrainCoefficient(double hours) =>
        hours <= 12 ? 0.8 : hours <= 24 ? 0.8 + 0.1 * (hours - 12) / 12 : hours <= 40 ? 0.9 + 0.1 * (hours - 24) / 16 : 1.0;

    /// <summary>
    /// WQCV in watershed inches, MHFD USDCM Vol. 3 Eq. 3-1: WQCV = a (0.91 i³ − 1.19 i² + 0.78 i), i = imperviousness (fraction);
    /// scaled by d6 / 0.43 (Eq. 3-2) for a region whose average runoff-producing storm is d6 inches.
    /// </summary>
    public static double WqcvInches(double imperviousness, double a, double d6 = 0.43)
    {
        double i = Math.Clamp(imperviousness, 0, 1);
        return a * (0.91 * i * i * i - 1.19 * i * i + 0.78 * i) * d6 / 0.43;
    }

    /// <summary>
    /// Filter bed area from Darcy's law (Maryland Stormwater Design Manual 2000, filtering practices; Claytor and Schueler 1996):
    /// Af = V·df / (k·(hf + df)·tf), V volume to filter, df media depth, k media permeability, hf average head over the bed, tf drain time.
    /// Units: V in L³, df and hf in L, k in L/day, tf in days → Af in L².
    /// </summary>
    public static double DarcyFilterArea(double v, double df, double k, double hAvg, double tfDays) => v * df / (k * (hAvg + df) * tfDays);

    /// <summary>Darcy drain time (days) of volume V through a filter of area A.</summary>
    public static double DarcyDrainDays(double v, double df, double k, double hAvg, double area) => v * df / (k * (hAvg + df) * area);
}

/// <summary>
/// Extended detention basin water quality outlet: a perforated plate with rows of equal holes at a fixed spacing from the
/// bottom, sized so the WQCV drains in the target time. Outflow of a row is Cd·a·√(2g(h − z)) above the row centre (row
/// k centre at bottom + k·spacing, k = 0, 1, …, below the WQCV level). Because the total outflow is proportional to the
/// hole area a, the drain time is proportional to 1/a, so a = T(1)/T_target exactly; T(1) is ∫ A(h) dh / Q(h) from the
/// level where the drained fraction is reached up to the WQCV level.
/// </summary>
public sealed class OrificePlate
{
    private readonly Pond _pond;
    private readonly double _g, _cd, _spacing;
    public double Bottom { get; }
    public double WqLevel { get; }
    public IReadOnlyList<double> Rows { get; }
    /// <summary>Area of the holes in one row (L²).</summary>
    public double RowArea { get; private set; } = 1;

    public OrificePlate(Pond pond, double wqLevel, double spacing, double cd, double g)
    {
        _pond = pond; _g = g; _cd = cd; _spacing = spacing;
        Bottom = pond.Bottom; WqLevel = wqLevel;
        var rows = new List<double>();
        for (int k = 0; Bottom + k * spacing < wqLevel - 1e-9; k++) rows.Add(Bottom + k * spacing);
        Rows = rows;
    }

    public double Outflow(double e) => Rows.Sum(z => e > z ? _cd * RowArea * Math.Sqrt(2 * _g * (e - z)) : 0);

    /// <summary>Elevation at which the storage is <paramref name="v"/> (bisection).</summary>
    public double LevelAt(double v)
    {
        double lo = _pond.Bottom, hi = _pond.Top;
        for (int i = 0; i < 100; i++) { double m = 0.5 * (lo + hi); if (_pond.Storage(m) < v) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    /// <summary>Hours to draw down from the WQCV level until <paramref name="fraction"/> of the volume above the bottom has left.</summary>
    public double DrainHours(double fraction)
    {
        double vTop = _pond.Storage(WqLevel), eEnd = LevelAt(vTop * (1 - fraction));
        // t = ∫ dS / Q = Σ ΔS / Q(mid) on fine storage slices (midpoint rule; Q > 0 above the lowest row).
        const int n = 4000;
        double t = 0, prevE = eEnd, prevS = _pond.Storage(eEnd);
        for (int i = 1; i <= n; i++)
        {
            double e = eEnd + (WqLevel - eEnd) * i / n, s = _pond.Storage(e), q = Outflow(0.5 * (e + prevE));
            if (!(q > 0)) return double.PositiveInfinity;
            t += (s - prevS) / q;
            prevE = e; prevS = s;
        }
        return t / 3600;
    }

    /// <summary>Sets the row area so that <paramref name="fraction"/> of the WQCV drains in <paramref name="hours"/>.</summary>
    public double Size(double hours, double fraction)
    {
        RowArea = 1;
        double t1 = DrainHours(fraction);
        RowArea = t1 / hours;
        return RowArea;
    }

    /// <summary>Drawdown curve: (hours, elevation), stepping the storage down with fourth-order Runge–Kutta.</summary>
    public List<(double Hours, double Elev)> Drawdown(double untilHours, double dtHours = 0.1)
    {
        var pts = new List<(double, double)>();
        double s = _pond.Storage(WqLevel), dt = dtHours * 3600;
        double F(double v) => -Outflow(LevelAt(Math.Max(v, 0)));
        for (double t = 0; t <= untilHours + 1e-9; t += dtHours)
        {
            pts.Add((t, LevelAt(Math.Max(s, 0))));
            double k1 = F(s), k2 = F(s + dt / 2 * k1), k3 = F(s + dt / 2 * k2), k4 = F(s + dt * k3);
            s = Math.Max(0, s + dt / 6 * (k1 + 2 * k2 + 2 * k3 + k4));
        }
        return pts;
    }
}
