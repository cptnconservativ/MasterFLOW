namespace MasterFlow.Application.Hydrology;

/// <summary>
/// Synthetic unit hydrographs besides the NRCS curve: Clark (time–area translation plus a linear reservoir, HEC-HMS
/// Technical Reference Manual), Snyder (peak and lag by Snyder's coefficients, shaped by a gamma curve through that peak),
/// and the Colorado Urban Hydrograph Procedure form (CUHP: Snyder-type peak with tp = Ct·(L·Lca/√S)^0.48). Each returns
/// ordinates in cfs per inch of excess in one time step, scaled to exactly 1 in of runoff over the area.
/// </summary>
public static class UnitHydrographs
{
    /// <summary>HEC-HMS synthetic time–area curve: cumulative area fraction at t/tc.</summary>
    public static double TimeArea(double tOverTc) => tOverTc <= 0 ? 0 : tOverTc >= 1 ? 1
        : tOverTc <= 0.5 ? 1.414 * Math.Pow(tOverTc, 1.5) : 1 - 1.414 * Math.Pow(1 - tOverTc, 1.5);

    /// <summary>Clark unit hydrograph for area (ac), tc and R (hours) and step dt (hours).</summary>
    public static double[] Clark(double areaAc, double tcH, double rH, double dtH)
    {
        double volFt3 = areaAc * 43560 / 12;   // 1 in of runoff
        int n = (int)Math.Ceiling((tcH + 10 * rH) / dtH) + 2;
        double ca = dtH / (rH + 0.5 * dtH), cb = 1 - ca;
        var o = new double[n + 1];
        for (int i = 1; i <= n; i++)
        {
            double inflow = (TimeArea(i * dtH / tcH) - TimeArea((i - 1) * dtH / tcH)) * volFt3 / (dtH * 3600);
            o[i] = ca * inflow + cb * o[i - 1];
        }
        var u = new double[n];
        for (int i = 1; i <= n; i++) u[i - 1] = 0.5 * (o[i] + o[i - 1]);
        return Normalize(u, areaAc, dtH);
    }

    /// <summary>Snyder lag (h): tp = Ct·(L·Lca)^0.3, L and Lca in miles.</summary>
    public static double SnyderLag(double ct, double lMi, double lcaMi) => ct * Math.Pow(lMi * lcaMi, 0.3);

    /// <summary>CUHP time to peak (h): tp = Ct·(L·Lca/√S)^0.48, S in ft/ft (MHFD USDCM Vol. 1 Ch. 6).</summary>
    public static double CuhpLag(double ct, double lMi, double lcaMi, double s) => ct * Math.Pow(lMi * lcaMi / Math.Sqrt(s), 0.48);

    /// <summary>
    /// Snyder-type unit hydrograph: standard duration tr = tp/5.5; for a step D, tpR = tp + (D − tr)/4; peak qp = 640·Cp·A/tpR (cfs,
    /// A in mi²); time to peak from the start of excess Tp = tpR + D/2. The shape is a gamma curve q/qp = (t/Tp)^m·e^(m(1 − t/Tp)) with
    /// m chosen so the volume is 1 in.
    /// </summary>
    public static (double[] U, double Qp, double Tp, double M) Snyder(double areaAc, double tpH, double cp, double dtH)
    {
        double a = areaAc / 640, tr = tpH / 5.5, tpr = tpH + (dtH - tr) / 4, qp = 640 * cp * a / tpr, tPeak = tpr + dtH / 2;
        double vol = areaAc * 43560 / 12;
        // Gamma UH volume: qp·Tp·3600·e^m·Γ(m+1)/m^(m+1). Solve for m (volume falls as m rises).
        double Vol(double m) => qp * tPeak * 3600 * Math.Exp(m + LogGamma(m + 1) - (m + 1) * Math.Log(m));
        double lo = 0.05, hi = 200;
        if (Vol(hi) > vol) lo = hi; else if (Vol(lo) < vol) hi = lo;
        else for (int i = 0; i < 200; i++) { double mid = Math.Sqrt(lo * hi); if (Vol(mid) > vol) lo = mid; else hi = mid; }
        double m = Math.Sqrt(lo * hi);
        int n = (int)Math.Ceiling(8 * tPeak / dtH) + 1;
        var u = new double[n];
        for (int k = 0; k < n; k++) { double x = (k + 1) * dtH / tPeak; u[k] = qp * Math.Pow(x, m) * Math.Exp(m * (1 - x)); }
        return (Normalize(u, areaAc, dtH), qp, tPeak, m);
    }

    /// <summary>Corps of Engineers widths of the Snyder unit hydrograph at 50 % and 75 % of the peak (h): 770/(qp/A)^1.08, 440/(qp/A)^1.08.</summary>
    public static (double W50, double W75) SnyderWidths(double qp, double areaSqMi) => (770 / Math.Pow(qp / areaSqMi, 1.08), 440 / Math.Pow(qp / areaSqMi, 1.08));

    private static double[] Normalize(double[] u, double areaAc, double dtH)
    {
        double vol = u.Sum() * dtH * 3600, target = areaAc * 43560 / 12;
        if (vol > 0) for (int k = 0; k < u.Length; k++) u[k] *= target / vol;
        return u;
    }

    /// <summary>Lanczos log-gamma.</summary>
    public static double LogGamma(double x)
    {
        double[] c = [676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
        if (x < 0.5) return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);
        x -= 1;
        double a = 0.99999999999980993, t = x + 7.5;
        for (int i = 0; i < 8; i++) a += c[i] / (x + i + 1);
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }
}
