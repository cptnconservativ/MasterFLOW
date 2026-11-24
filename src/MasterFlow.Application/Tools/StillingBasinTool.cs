using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Hydraulic jump and stilling basins by FHWA HEC-14 (3rd ed., 2006): the jump in a horizontal channel of any shape
/// (Ch. 6), the free-jump, USBR Type III, USBR Type IV and SAF basins with the expansion-and-depression geometry of
/// Section 8.1 (Ch. 8), and the USBR Type VI impact basin and CSU rigid boundary basin (Ch. 9). The Rapid City IDCM
/// §4.9.2(1) Figure 4-8 / Table 4-15 impact basin is shown beside the HEC-14 one as a local check.
/// </summary>
public sealed class StillingBasinTool : IDesignTool
{
    public string Id => "stilling-basin";
    public string Name => "Hydraulic jump and stilling basins (HEC-14)";
    public string Description => "Hydraulic jump in rectangular, trapezoidal or circular channels and the HEC-14 Ch. 8 stilling basins (free jump, USBR Type III, USBR Type IV, SAF) with the floor depression, transition and exit slopes, blocks, baffles and sills; HEC-14 Ch. 9 USBR Type VI impact basin (with the Rapid City IDCM Table 4-15 check) and CSU rigid boundary basin. Checks every flow in [Check flows] against a tailwater rating.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => context.Units == UnitSystem.SI ? """
        # Hydraulic jump and stilling basins, SI (m, m³/s). HEC-14 Section 8.1 design example.
        Q = 11.8
        outlet width = 3.0        # box culvert, B = 3.0 m
        basin width = 3.0
        approach slope = 6.5      # % (normal depth in the culvert sets yo)
        approach n = 0.015
        ground slope = 6.5        # % (streambed below the outlet)
        transition slope = 0.5    # ST (V:H)
        exit slope = 0.5          # SS (V:H)
        tw bottom width = 3.1     # tailwater channel (normal depth)
        tw side slope = 2
        tw slope = 6.5
        tw n = 0.030
        floor drop = auto
        """ : """
        # Hydraulic jump and stilling basins (FHWA HEC-14, 2006, Ch. 6, 8, 9). HEC-14 Section 8.1 design example:
        # 10 ft x 6 ft box culvert at 6.5 %, 417 cfs, into a trapezoidal channel.
        Q = 417
        outlet width = 10         # Wo (rectangular outlet); or side slope = z for a trapezoid, diameter = D for a pipe
        basin width = 10          # WB (rectangular basin)
        approach slope = 6.5      # % (normal depth in the culvert sets yo); or give y1, V1 or energy above floor
        approach n = 0.015
        ground slope = 6.5        # % So, the streambed continuing below the outlet
        transition slope = 0.5    # ST (V:H) down to the basin floor
        exit slope = 0.5          # SS (V:H) back up to the channel
        # Tailwater: a depth (tailwater = ...), a [Tailwater rating] table, or a channel at normal depth:
        tw bottom width = 10.2
        tw side slope = 2
        tw slope = 6.5            # %
        tw n = 0.030
        floor drop = auto         # basin floor below the outlet invert; auto = least that holds the jump, per basin

        # Pipe outlet into a USBR Type VI impact basin and a CSU rigid boundary basin (HEC-14 Ch. 9).
        outlet shape = circular
        outlet span = 3.5
        outlet slope = 3          # %
        outlet n = 0.013
        impact Q = 120
        csu Q = 120
        exit velocity = 6         # CSU target velocity leaving the basin (used when no tailwater is given)
        expansion ratio = auto    # CSU WB/Wo: 2-4, 5, 6, 7, 8 or auto

        # Every flow the basin must handle (the jump must hold at all of them).
        [Check flows]
        Q
        100
        200
        300
        417
        """;

    // ---------------------------------------------------------------------------------------------------------------
    // Curves read from figures. HEC-14 prints these only as images; the values below were digitized from the figures
    // (D-327). Each can be replaced by a typed value read off the figure.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>HEC-14 Figure 8.2 free-jump curve (also the Type IV basin), LB/y2 against Fr1, digitized.</summary>
    public static readonly (double Fr, double Ratio)[] Fig82Free =
        [(1.8, 4.02), (2.1, 4.38), (2.4, 4.67), (2.6, 4.87), (3.0, 5.16), (3.4, 5.43), (3.8, 5.61), (4.2, 5.76), (4.7, 5.88),
         (5.1, 5.95), (6.1, 6.04), (7.0, 6.06), (10.0, 6.06), (12.0, 6.05), (14.1, 5.95), (15.1, 5.90), (16.1, 5.82), (17.1, 5.76),
         (18.1, 5.66), (19.1, 5.54), (19.6, 5.46)];

    /// <summary>HEC-14 Figure 8.2 USBR Type III curve, LB/y2 against Fr1, digitized (the curve ends near Fr 16.5).</summary>
    public static readonly (double Fr, double Ratio)[] Fig82TypeIII =
        [(4.5, 2.19), (5.1, 2.30), (5.5, 2.38), (6.1, 2.46), (7.1, 2.56), (8.1, 2.63), (9.1, 2.69), (10.1, 2.69), (11.1, 2.72),
         (12.1, 2.74), (13.1, 2.75), (14.1, 2.75), (15.1, 2.74), (16.1, 2.72)];

    /// <summary>HEC-14 Figure 6.5, jump length L/y1 against Fr1 in a horizontal rectangular channel, digitized.</summary>
    public static readonly (double Fr, double Ratio)[] Fig65 =
        [(1, 0), (2, 10), (3, 20), (4, 30), (5, 40), (6, 49.5), (7, 59), (8, 68), (9, 77), (10, 85.5), (11, 94), (12, 102),
         (13, 110), (14, 117.5), (15, 124), (16, 130)];

    /// <summary>HEC-14 Figure 6.8, jump length in a circular conduit with y2 &lt; D: Lj/y1 = 10·Fr1 (digitized; valid about 1.35–6, "do not extrapolate").</summary>
    public static double Fig68(double fr) => 10 * fr;

    /// <summary>HEC-14 Figure 4.3 (rectangular outlet) at L = 2B: yA/yo against Fr, digitized (Fr 1–3.2).</summary>
    public static double Fig43(double fr) => 0.187 + 0.150 * (fr - 1);

    /// <summary>HEC-14 Figure 4.4 (circular outlet) at L = 2D: yA/yo against Fr, digitized (Fr 1–3).</summary>
    public static double Fig44(double fr) => 0.161 + 0.212 * (fr - 1);

    /// <summary>HEC-14 Figure 9.4 pressure coefficient Cp. Rectangular: 0.70 (Fr 1.3–2.4). Circular: against KuQ/D^2.5 (Ku = 1.0 CU, 1.811 SI), 0.56 to 3.8 then falling to 0.30 at 5.9.</summary>
    public static double Fig94(bool box, double kuQd) => box ? 0.70 : kuQd <= 3.8 ? 0.56 : Math.Max(0.30, 0.56 - (kuQd - 3.8) * (0.26 / 2.1));

    /// <summary>HEC-14 Figure 9.14, Ho/WB against Fr for the Type VI basin, digitized (Fr 1–7).</summary>
    public static readonly (double Fr, double Ratio)[] Fig914Curve =
        [(1, 0.25), (1.5, 0.38), (2, 0.52), (2.5, 0.68), (3, 0.85), (3.5, 1.04), (4, 1.25), (4.5, 1.44), (5, 1.64), (5.5, 1.84),
         (6, 2.07), (6.5, 2.38), (7, 2.78)];

    /// <summary>HEC-14 Figure 9.15, HL/Ho against Fr for the Type VI basin, digitized (Fr 1.4–7).</summary>
    public static readonly (double Fr, double Ratio)[] Fig915Curve =
        [(1.4, 0.31), (2, 0.43), (2.5, 0.49), (3, 0.54), (3.5, 0.575), (4, 0.605), (4.5, 0.635), (5, 0.665), (5.5, 0.69), (6, 0.715),
         (6.5, 0.74), (7, 0.765)];

    public static double Fig914(double fr) => Curve(Fig914Curve, fr);
    public static double Fig915(double fr) => Curve(Fig915Curve, fr);

    /// <summary>
    /// HEC-14 Eq. 9.3 with the slope term on the outlet side (D-328): yA + 0.25(Q/(WA·yA))²/2g = 2Wo·So + yo + 0.25·Vo²/(2g).
    /// HEC-14 prints 2Wo·So on the yA side, which would make the bed rise toward section A. Returns the smaller root, or
    /// NaN when there is none.
    /// </summary>
    public static double Eq93(double q, double wo, double wa, double so, double yo, double vo, double g)
    {
        double h = 2 * wo * so + yo + 0.25 * vo * vo / (2 * g);
        double k = 0.25 * q * q / (wa * wa * 2 * g);
        double f(double y) => y + k / (y * y);
        double yMin = Math.Cbrt(2 * k);
        if (h <= 0 || f(yMin) > h) return double.NaN;
        double lo = 1e-9 * Math.Max(yMin, 1e-6), hi = yMin;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (f(mid) > h) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>Linear interpolation in a curve, held flat beyond its ends.</summary>
    public static double Curve((double X, double Y)[] c, double x)
    {
        if (x <= c[0].X) return c[0].Y;
        for (int i = 1; i < c.Length; i++)
            if (x <= c[i].X) return c[i - 1].Y + (c[i].Y - c[i - 1].Y) * (x - c[i - 1].X) / (c[i].X - c[i - 1].X);
        return c[^1].Y;
    }

    // IDCM Table 4-15 (USBR Basin VI): pipe dia (in), max Q (cfs), W, H, L, a, b, c, d, e, f, g (in), tw, tf, tb, tp, K (in), riprap (in).
    public static readonly double[][] ImpactTable =
    [
        [18, 21, 66, 51, 88, 39, 49, 28, 11, 6, 18, 25, 6, 6.5, 6, 6, 3, 4.0],
        [24, 38, 81, 63, 108, 47, 61, 34, 14, 6, 24, 30, 6, 6.5, 6, 6, 3, 7.0],
        [30, 59, 96, 75, 128, 55, 73, 40, 16, 8, 30, 36, 6, 6.5, 7, 7, 3, 8.5],
        [36, 85, 111, 87, 148, 63, 85, 46, 19, 8, 36, 42, 7, 7.5, 8, 8, 3, 9.0],
        [42, 115, 126, 96, 168, 72, 96, 53, 21, 10, 36, 47, 8, 8.5, 9, 8, 4, 9.5],
        [48, 151, 141, 108, 188, 81, 107, 59, 24, 10, 36, 53, 9, 9.5, 10, 8, 4, 10.5],
        [54, 191, 156, 117, 208, 88, 120, 65, 26, 12, 36, 59, 10, 10.5, 10, 8, 4, 12.0],
        [60, 236, 171, 129, 228, 96, 132, 71, 29, 12, 36, 64, 11, 11.5, 11, 8, 6, 13.0],
        [72, 339, 198, 147, 264, 111, 153, 83, 33, 15, 36, 74, 12, 12.5, 12, 8, 6, 14.0],
    ];

    /// <summary>HEC-14 Table 9.2 (CU), USBR Type VI dimensions in ft: WB, h1, h2, h3, h4, L, L1, L2, W1, W2, t1, t2, t3, t4, t5.</summary>
    public static readonly double[][] Table92Cu =
    [
        [4, 3.08, 1.50, 0.67, 1.67, 5.42, 2.33, 3.08, 0.33, 1.08, 0.50, 0.50, 0.50, 0.50, 0.25],
        [5, 3.83, 1.92, 0.83, 2.08, 6.67, 2.92, 3.83, 0.42, 1.42, 0.50, 0.50, 0.50, 0.50, 0.25],
        [6, 4.58, 2.25, 1.00, 2.50, 8.00, 3.42, 4.58, 0.50, 1.67, 0.50, 0.50, 0.50, 0.50, 0.25],
        [7, 5.42, 2.58, 1.17, 2.92, 9.42, 4.00, 5.42, 0.50, 1.92, 0.50, 0.50, 0.50, 0.50, 0.25],
        [8, 6.17, 3.00, 1.33, 3.33, 10.67, 4.58, 6.17, 0.58, 2.17, 0.50, 0.58, 0.58, 0.50, 0.25],
        [9, 6.92, 3.42, 1.50, 3.75, 12.00, 5.17, 6.92, 0.67, 2.50, 0.58, 0.58, 0.67, 0.58, 0.25],
        [10, 7.58, 3.75, 1.67, 4.17, 13.42, 5.75, 7.67, 0.75, 2.75, 0.67, 0.67, 0.75, 0.67, 0.25],
        [11, 8.42, 4.17, 1.83, 4.58, 14.58, 6.33, 8.42, 0.83, 3.00, 0.67, 0.75, 0.75, 0.67, 0.33],
        [12, 9.17, 4.50, 2.00, 5.00, 16.00, 6.83, 9.17, 0.92, 3.00, 0.67, 0.83, 0.83, 0.75, 0.33],
        [13, 10.17, 4.92, 2.17, 5.42, 17.33, 7.42, 10.00, 1.00, 3.00, 0.67, 0.92, 0.83, 0.83, 0.33],
        [14, 10.75, 5.25, 2.33, 5.83, 18.67, 8.00, 10.75, 1.08, 3.00, 0.67, 1.00, 0.92, 0.92, 0.42],
        [15, 11.50, 5.58, 2.50, 6.25, 20.00, 8.50, 11.50, 1.17, 3.00, 0.67, 1.00, 1.00, 1.00, 0.42],
        [16, 12.25, 6.00, 2.67, 6.67, 21.33, 9.08, 12.25, 1.25, 3.00, 0.75, 1.00, 1.00, 1.00, 0.50],
        [17, 13.00, 6.33, 2.83, 7.08, 21.50, 9.67, 13.00, 1.33, 3.00, 0.75, 1.08, 1.00, 1.00, 0.50],
        [18, 13.75, 6.67, 3.00, 7.50, 23.92, 10.25, 13.75, 1.33, 3.00, 0.75, 1.08, 1.08, 1.08, 0.58],
        [19, 14.58, 7.08, 3.17, 7.92, 25.33, 10.83, 14.58, 1.42, 3.00, 0.83, 1.17, 1.08, 1.08, 0.58],
        [20, 15.33, 7.50, 3.33, 8.33, 26.58, 11.42, 15.33, 1.50, 3.00, 0.83, 1.17, 1.17, 1.17, 0.67],
    ];

    /// <summary>HEC-14 Table 9.2 (SI), USBR Type VI dimensions in m, same columns as <see cref="Table92Cu"/>.</summary>
    public static readonly double[][] Table92Si =
    [
        [1.0, 0.79, 0.38, 0.17, 0.43, 1.40, 0.59, 0.79, 0.08, 0.26, 0.15, 0.15, 0.15, 0.15, 0.08],
        [1.5, 1.16, 0.57, 0.25, 0.62, 2.00, 0.88, 1.16, 0.13, 0.42, 0.15, 0.15, 0.15, 0.15, 0.08],
        [2.0, 1.54, 0.75, 0.33, 0.83, 2.68, 1.14, 1.54, 0.15, 0.55, 0.15, 0.15, 0.15, 0.15, 0.08],
        [2.5, 1.93, 0.94, 0.42, 1.04, 3.33, 1.43, 1.93, 0.18, 0.68, 0.16, 0.18, 0.18, 0.16, 0.08],
        [3.0, 2.30, 1.12, 0.50, 1.25, 4.02, 1.72, 2.30, 0.22, 0.83, 0.20, 0.20, 0.22, 0.20, 0.08],
        [3.5, 2.68, 1.32, 0.58, 1.46, 4.65, 2.00, 2.68, 0.26, 0.91, 0.20, 0.23, 0.23, 0.21, 0.10],
        [4.0, 3.12, 1.51, 0.67, 1.67, 5.33, 2.28, 3.08, 0.30, 0.91, 0.20, 0.28, 0.25, 0.25, 0.10],
        [4.5, 3.46, 1.68, 0.75, 1.88, 6.00, 2.56, 3.46, 0.36, 0.91, 0.20, 0.30, 0.30, 0.30, 0.13],
        [5.0, 3.82, 1.87, 0.83, 2.08, 6.52, 2.84, 3.82, 0.39, 0.91, 0.22, 0.31, 0.30, 0.30, 0.15],
        [5.5, 4.19, 2.03, 0.91, 2.29, 7.29, 3.12, 4.19, 0.41, 0.91, 0.22, 0.33, 0.33, 0.33, 0.18],
        [6.0, 4.60, 2.25, 1.00, 2.50, 7.98, 3.42, 4.60, 0.45, 0.91, 0.25, 0.36, 0.35, 0.35, 0.19],
    ];

    private static readonly string[] Table92Names = ["WB", "h1", "h2", "h3", "h4", "L", "L1", "L2", "W1", "W2", "t1", "t2", "t3", "t4", "t5"];

    /// <summary>HEC-14 Table 9.1 configurations: expansion ratio label, W1/Wo, rows Nr, elements N.</summary>
    public static readonly (string Ratio, double W1, int Nr, int N)[] CsuConfigs =
    [
        ("2-4", 0.57, 4, 14), ("2-4", 0.57, 5, 17), ("2-4", 0.57, 6, 21),
        ("5", 0.63, 4, 15), ("5", 0.63, 5, 19), ("5", 0.63, 6, 23),
        ("6", 0.60, 4, 17), ("6", 0.60, 5, 22), ("6", 0.60, 6, 27),
        ("7", 0.58, 5, 24), ("7", 0.58, 6, 30),
        ("8", 0.62, 6, 30),
    ];

    /// <summary>
    /// HEC-14 Table 9.1 (= IDCM Figure 4-17) basin drag coefficient CB, by conduit, h/yA and L/h, one value per
    /// configuration above (NaN = not given). The circular 2–4 / 6-row column is printed upside down in both sources;
    /// it is used here in the order that fits the rest of the table (D-321).
    /// </summary>
    public static readonly (bool Circular, double HYa, double Lh, double[] Cb)[] CsuCb =
    [
        (false, 0.91, 6, [0.32, 0.28, 0.24, 0.32, 0.28, 0.24, 0.31, 0.27, 0.23, 0.26, 0.22, 0.22]),
        (false, 0.71, 6, [0.44, 0.40, 0.37, 0.42, 0.38, 0.35, 0.40, 0.36, 0.33, 0.34, 0.31, 0.29]),
        (false, 0.48, 12, [0.60, 0.55, 0.51, 0.56, 0.51, 0.47, 0.53, 0.48, 0.43, 0.46, 0.39, 0.35]),
        (false, 0.37, 12, [0.68, 0.66, 0.65, 0.65, 0.62, 0.60, 0.62, 0.58, 0.55, 0.54, 0.50, 0.45]),
        (true, 0.91, 6, [0.21, 0.20, 0.18, 0.21, 0.19, 0.17, 0.21, 0.19, 0.17, 0.18, 0.16, double.NaN]),
        (true, 0.71, 6, [0.29, 0.27, 0.25, 0.27, 0.25, 0.23, 0.25, 0.23, 0.22, 0.22, 0.20, double.NaN]),
        (true, 0.31, 6, [0.38, 0.36, 0.34, 0.36, 0.34, 0.32, 0.34, 0.32, 0.30, 0.30, 0.28, double.NaN]),
        (true, 0.48, 12, [0.45, 0.42, 0.40, 0.40, 0.38, 0.36, 0.36, 0.34, 0.32, 0.30, 0.28, double.NaN]),
        (true, 0.37, 12, [0.52, 0.50, 0.48, 0.48, 0.46, 0.44, 0.44, 0.42, 0.40, 0.38, 0.36, double.NaN]),
    ];

    private enum EntryKind { Depth, Velocity, Normal, Energy }

    // ---------------------------------------------------------------------------------------------------------------
    // Sections and the momentum / energy functions (HEC-14 Ch. 6).
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>A channel cross-section for the jump: area, top width, wetted perimeter and A·ȳ (first moment about the water surface).</summary>
    public abstract record Shape
    {
        public abstract double Area(double y);
        public abstract double Top(double y);
        public abstract double Perimeter(double y);
        public abstract double Moment(double y);
        /// <summary>Depth above which the section is closed (∞ for an open channel).</summary>
        public virtual double Crown => double.PositiveInfinity;
    }

    /// <summary>Prismatic section: bottom width b and side slope z (z = 0 rectangular).</summary>
    public sealed record Trap(double B, double Z) : Shape
    {
        public override double Area(double y) => B * y + Z * y * y;
        public override double Top(double y) => B + 2 * Z * y;
        public override double Perimeter(double y) => B + 2 * y * Math.Sqrt(1 + Z * Z);
        public override double Moment(double y) => B * y * y / 2 + Z * y * y * y / 3;
    }

    /// <summary>
    /// Circular conduit of diameter D. Above the crown the section is full and the pressure head acts on the full area,
    /// A·ȳ = A(y − D/2), which is HEC-14 Eq. 6.5.
    /// </summary>
    public sealed record Circle(double D) : Shape
    {
        private double Alpha(double y) => Math.Acos(Math.Clamp(1 - 2 * y / D, -1, 1));
        public override double Area(double y)
        {
            double r = D / 2, a = Alpha(Math.Min(y, D));
            return r * r * (a - Math.Sin(a) * Math.Cos(a));
        }
        public override double Top(double y) => y >= D ? 0 : D * Math.Sin(Alpha(y));
        public override double Perimeter(double y) => D * Alpha(Math.Min(y, D));
        public override double Moment(double y)
        {
            double r = D / 2;
            if (y >= D) return Math.PI * r * r * (y - r);
            double a = Alpha(y), area = Area(y);
            if (area <= 0) return 0;
            double c = 4 * r * Math.Pow(Math.Sin(a), 3) / (3 * (2 * a - Math.Sin(2 * a)));   // segment centroid from the centre
            return area * (y - r + c);
        }
        public override double Crown => D;
    }

    public static double Bisect(Func<double, double> f, double lo, double hi)
    {
        double flo = f(lo);
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi), fm = f(mid);
            if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; } else hi = mid;
            if (hi - lo < 1e-12 * Math.Max(1, hi)) break;
        }
        return 0.5 * (lo + hi);
    }

    private static double Upper(Func<double, double> f, double start)
    {
        double hi = start;
        for (int i = 0; i < 80 && f(hi) < 0; i++) hi *= 2;
        return hi;
    }

    public static double Critical(Shape s, double q, double g)
    {
        double F(double y) => q * q * s.Top(y) / (g * Math.Pow(s.Area(y), 3)) - 1;
        double hi = double.IsFinite(s.Crown) ? s.Crown * (1 - 1e-9) : Upper(y => -F(y), 1);
        if (F(hi) > 0) return hi;   // critical depth at or above the crown
        return Bisect(F, 1e-9 * Math.Max(hi, 1), hi);
    }

    /// <summary>Normal depth by Manning's equation (k = 1.486 US, 1.0 SI); NaN when a conduit cannot carry q open-channel.</summary>
    public static double Normal(Shape s, double q, double n, double slope, double k)
    {
        double F(double y) => k / n * s.Area(y) * Math.Pow(s.Area(y) / s.Perimeter(y), 2.0 / 3) * Math.Sqrt(slope) - q;
        double hi = double.IsFinite(s.Crown) ? 0.938 * s.Crown : Upper(F, 1);
        if (F(hi) < 0) return double.NaN;
        return Bisect(F, 1e-9, hi);
    }

    public static double Momentum(Shape s, double q, double y, double g) => q * q / (g * s.Area(y)) + s.Moment(y);
    public static double Energy(Shape s, double q, double y, double g) => y + q * q / (2 * g * s.Area(y) * s.Area(y));
    public static double Froude(Shape s, double q, double y, double g) =>
        s.Top(y) <= 0 ? double.NaN : q / s.Area(y) / Math.Sqrt(g * s.Area(y) / s.Top(y));

    /// <summary>Sequent (conjugate) depth: the subcritical depth with the same specific force as y1 (HEC-14 Eq. 6.1).</summary>
    public static double Sequent(Shape s, double q, double y1, double g)
    {
        double yc = Critical(s, q, g), m1 = Momentum(s, q, y1, g);
        return Bisect(y => Momentum(s, q, y, g) - m1, yc, Upper(y => Momentum(s, q, y, g) - m1, Math.Max(2 * yc, 1)));
    }

    /// <summary>Supercritical depth with specific energy E (NaN when E is below critical energy).</summary>
    public static double SupercriticalDepth(Shape s, double q, double e, double g)
    {
        double yc = Critical(s, q, g);
        if (Energy(s, q, yc, g) > e) return double.NaN;
        return Bisect(y => Energy(s, q, y, g) - e, 1e-9 * yc, yc);
    }

    public static string JumpType(double fr) => fr switch
    {
        < 1 => "no jump (subcritical)",
        < 1.7 => "undular",
        < 2.5 => "pre-jump (weak)",
        < 4.5 => "oscillating",
        < 9 => "steady",
        _ => "strong",
    };

    /// <summary>C = TW/y2, the tailwater a basin needs as a fraction of the conjugate depth (HEC-14 Ch. 8; SAF Eq. 8.18).</summary>
    public static double TailwaterFactor(string basin, double fr) => basin switch
    {
        "SAF" => fr <= 5.5 ? 1.10 - fr * fr / 120 : fr <= 11 ? 0.85 : 1.00 - fr * fr / 800,
        "USBR Type IV" => 1.10,
        _ => 1.0,
    };

    /// <summary>HEC-14 Eq. 8.8 radius of the curved transition at a slope change: r = y / (e^(1.5/Fr²) − 1).</summary>
    public static double TransitionRadius(double y, double fr) => y / (Math.Exp(1.5 / (fr * fr)) - 1);

    /// <summary>Basin length LB (HEC-14 Figure 8.2, or Eq. 8.19 for SAF). y2 is the required depth C·y2' as HEC-14 uses it.</summary>
    public static double BasinLength(string basin, double fr, double y2, double c) => basin switch
    {
        "USBR Type III" => Curve(Fig82TypeIII, fr) * y2,
        "SAF" => 4.5 * y2 / (c * Math.Pow(fr, 0.76)),
        _ => Curve(Fig82Free, fr) * y2,
    };

    /// <summary>One basin state at a floor drop: entering flow, conjugate depth, required depth, lengths and exit rise.</summary>
    public sealed record BasinState(double Drop, double Y1, double V1, double Fr1, double Y2Conj, double C, double Y2, double Lt, double Lb, double Ls, double Rise)
    {
        public double Total => Lt + Lb + Ls;
    }

    /// <summary>
    /// HEC-14 Section 8.1 geometry: the floor sits <paramref name="drop"/> below the outlet invert, reached on a
    /// transition slope ST (LT = drop/ST, Eq. 8.5) and left on an exit slope SS back to the streambed, which continues
    /// at So (LS, Eq. 8.6; z3, Eq. 8.7). Returns z3 − z1, the rise from the basin floor to the channel at the exit.
    /// </summary>
    public static (double Lt, double Ls, double Rise) Geometry(double drop, double lb, double st, double ss, double so)
    {
        double lt = drop / st;
        double ls = (lt * (st - so) - lb * so) / (ss + so);
        if (ls < 0) ls = 0;
        return (lt, ls, drop - so * (lt + lb + ls));
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Hydraulic jump and stilling basins";
        bool si = context.Units == UnitSystem.SI;
        double g = si ? 9.81 : 32.174, k = si ? 1.0 : 1.486;
        string L = si ? "m" : "ft", V = si ? "m/s" : "ft/s", Qu = si ? "m³/s" : "cfs", qU = si ? "m²/s" : "cfs/ft";
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        var tables = new List<ToolTable>();
        var figures = new List<ToolFigure>();
        var charts = new List<ToolChart>();
        var summary = new List<string>();

        double? Q = p.OptionalNumber("Q", "Q");
        double? wb = p.OptionalNumber("basin width", "basin width");
        double? wo = p.OptionalNumber("outlet width", "outlet width") ?? wb;
        double? dia = p.OptionalNumber("diameter", "diameter");
        double z = p.Number("side slope", 0, "side slope");
        bool jumpPart = wo is not null || dia is not null;
        bool outletPart = p.Text("outlet span") is not null;
        if (!jumpPart && !outletPart && p.Errors.Count == 0)
            msgs.Add(CalcMessage.Error("E-SB-001", "Give a basin width or outlet width (hydraulic jump and HEC-14 Ch. 8 basins), a diameter (jump in a pipe), or an outlet span (impact and CSU basins)."));
        if (jumpPart && Q is not > 0) msgs.Add(CalcMessage.Error("E-SB-002", "Give Q above zero."));
        if (wb is <= 0 || (dia is null && wo is <= 0) || dia is <= 0) msgs.Add(CalcMessage.Error("E-SB-003", "Widths and the diameter must be above zero."));
        if (z < 0) msgs.Add(CalcMessage.Error("E-SB-003", "The side slope cannot be negative."));

        // Entering flow at the outlet (section o of HEC-14 Figure 8.1).
        double? y1In = p.OptionalNumber("y1"), v1In = p.OptionalNumber("V1"), eIn = p.OptionalNumber("energy above floor"),
            aS = p.OptionalNumber("approach slope"), aN = p.OptionalNumber("approach n");
        EntryKind entry = y1In is not null ? EntryKind.Depth : v1In is not null ? EntryKind.Velocity : eIn is not null ? EntryKind.Energy : EntryKind.Normal;
        if (jumpPart && entry == EntryKind.Normal && (aS is not > 0 || aN is not > 0))
            msgs.Add(CalcMessage.Error("E-SB-004", "Give the entering flow: y1, V1, energy above floor, or approach slope (%) and approach n."));
        if (y1In is <= 0 || v1In is <= 0 || eIn is <= 0) msgs.Add(CalcMessage.Error("E-SB-004", "y1, V1 and the energy above the floor must be above zero."));

        // Section 8.1 geometry.
        double so = p.Number("ground slope", 0, "ground slope") / 100, st = p.Number("transition slope", 0.5, "transition slope"), ss = p.Number("exit slope", 0.5, "exit slope");
        if (so < 0 || st <= 0 || ss <= 0) msgs.Add(CalcMessage.Error("E-SB-006", "ground slope cannot be negative; transition slope and exit slope (V:H) must be above zero."));
        double safFlare = p.Number("saf flare", 0, "saf flare");
        if (safFlare is < 0 or > 0.5) msgs.Add(CalcMessage.Error("E-SB-006", "saf flare (z:1) must be 0 to 0.5 (HEC-14 §8.5)."));

        // Tailwater.
        double? twDepth = p.OptionalNumber("tailwater");
        double? twB = p.OptionalNumber("tw bottom width"), twZ = p.OptionalNumber("tw side slope"), twS = p.OptionalNumber("tw slope"), twN = p.OptionalNumber("tw n");
        var rating = new List<(double Q, double Tw)>();
        foreach (var r in p.Rows("Tailwater rating"))
            if (p.Cell(r, 0, "Q", "Tailwater rating") is { } rq && p.Cell(r, 1, "tailwater", "Tailwater rating") is { } rt) rating.Add((rq, rt));
        rating.Sort((a, b) => a.Q.CompareTo(b.Q));
        bool twChannel = twB is not null || twS is not null || twN is not null;
        if (twChannel && (twB is not >= 0 || twS is not > 0 || twN is not > 0 || (twZ ?? 0) < 0 || (twB == 0 && (twZ ?? 0) == 0)))
            msgs.Add(CalcMessage.Error("E-SB-005", "The tailwater channel needs tw bottom width (≥ 0), tw side slope (≥ 0, not both zero), tw slope (%) > 0 and tw n > 0."));
        if (rating.Count == 1) msgs.Add(CalcMessage.Error("E-SB-005", "A tailwater rating needs at least two rows (Q, depth)."));
        if (twDepth is < 0) msgs.Add(CalcMessage.Error("E-SB-005", "The tailwater depth cannot be negative."));

        string dropText = (p.Text("floor drop") ?? "auto").ToLowerInvariant();
        double? fixedDrop = dropText == "auto" ? null : p.OptionalNumber("floor drop", "floor drop");
        if (fixedDrop is < 0) msgs.Add(CalcMessage.Error("E-SB-006", "The floor drop cannot be negative (a raised floor is not modelled)."));

        var checkQs = new List<double>();
        foreach (var r in p.Rows("Check flows"))
            if (p.Cell(r, 0, "Q", "Check flows") is { } cq) { if (cq > 0) checkQs.Add(cq); else msgs.Add(CalcMessage.Error("E-SB-007", "Check flows must be above zero.")); }

        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double? TailwaterAt(double q)
        {
            if (rating.Count >= 2)
            {
                if (q < rating[0].Q || q > rating[^1].Q) msgs.Add(CalcMessage.Warning("W-SB-001", $"Q = {q:G4} {Qu} is outside the tailwater rating; the nearest end was extended linearly."));
                int i = 1;
                while (i < rating.Count - 1 && rating[i].Q < q) i++;
                var (qa, ta) = rating[i - 1]; var (qb, tb) = rating[i];
                return ta + (tb - ta) * (q - qa) / (qb - qa);
            }
            if (twChannel) return Normal(new Trap(twB!.Value, twZ ?? 0), q, twN!.Value, twS!.Value / 100, k);
            return twDepth;
        }

        if (jumpPart) JumpAndBasins();
        if (outletPart) OutletBasins(p, si, g, k, L, V, Qu, msgs, tables, summary, TailwaterAt);

        msgs.InsertRange(0, p.Errors.Where(e => !msgs.Contains(e)));
        if (summary.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error)) summary.Add("See the tables below.");
        return new ToolResult(title, summary, tables, msgs) { Figures = figures, Charts = charts };

        void JumpAndBasins()
        {
            Shape sec = dia is { } d ? new Circle(d) : new Trap(wo!.Value, z);
            bool rect = sec is Trap { Z: 0 };
            double q = Q!.Value;
            double yo;
            switch (entry)
            {
                case EntryKind.Depth: yo = y1In!.Value; break;
                case EntryKind.Velocity: yo = SolveDepthForArea(sec, q / v1In!.Value); break;
                case EntryKind.Energy:
                    yo = SupercriticalDepth(sec, q, eIn!.Value, g);
                    if (double.IsNaN(yo)) { msgs.Add(CalcMessage.Error("E-SB-008", $"The energy above the floor, {eIn:G4} {L}, is below critical energy for Q = {q:G4} {Qu}; no supercritical flow enters.")); return; }
                    break;
                default:
                    yo = Normal(sec, q, aN!.Value, aS!.Value / 100, k);
                    if (double.IsNaN(yo)) { msgs.Add(CalcMessage.Error("E-SB-011", $"The conduit cannot carry {q:G4} {Qu} as open-channel flow at that slope and n.")); return; }
                    break;
            }
            if (yo >= sec.Crown) { msgs.Add(CalcMessage.Error("E-SB-009", "The entering depth is at or above the crown; the conduit flows full and no free-surface jump enters.")); return; }
            double yc = Critical(sec, q, g);
            if (yo >= yc)
            {
                msgs.Add(CalcMessage.Error("E-SB-009", $"The entering depth {yo:F3} {L} is not below critical depth {yc:F3} {L}: the flow is subcritical and no jump forms. A stilling basin is not needed; check the outlet with the outlet protection tool."));
                return;
            }
            double eo = Energy(sec, q, yo, g), vo = q / sec.Area(yo), fro = Froude(sec, q, yo, g);
            double eOf(double qq) => qq == q ? eo : entry == EntryKind.Normal
                ? (Normal(sec, qq, aN!.Value, aS!.Value / 100, k) is var yn && !double.IsNaN(yn) ? Energy(sec, qq, yn, g) : double.NaN)
                : entry == EntryKind.Energy ? eo : eo;   // y1 or V1 given for the design flow: the same total head for other flows

            // Step 3: the jump at the outlet with no basin (C = 1).
            double y2o = Sequent(sec, q, yo, g), e2o = Energy(sec, q, y2o, g);
            double tw = TailwaterAt(q) ?? double.NaN;
            double? lj; string ljNote;
            if (rect)
            {
                lj = Curve(Fig65, fro) * yo;
                ljNote = $"Lj = (L/y1)·y1, L/y1 = {Curve(Fig65, fro):F1} from HEC-14 Figure 6.5{(fro > 16 ? " (Fr1 above 16, the end of the figure; held at 130)" : "")}.";
            }
            else if (sec is Circle c)
            {
                bool inRange = fro >= 1.35 && fro <= 6.0;
                lj = y2o >= c.D ? 7 * (y2o - yo) : inRange ? Fig68(fro) * yo : null;
                ljNote = y2o >= c.D ? "Lj = 7(y2 − y1) since y2 ≥ D (HEC-14 §6.2.2)."
                    : inRange ? "y2 < D: Lj = (Lj/y1)·y1 with Lj/y1 = 10·Fr1 from HEC-14 Figure 6.8."
                    : $"y2 < D and Fr1 = {fro:F2} is outside Figure 6.8 (about 1.35–6, \"do not extrapolate\"); no length given.";
            }
            else { lj = 6.9 * (y2o - yo); ljNote = "Trapezoid: HEC-14 gives no length (model tests advised); Lj ≈ 6.9(y2 − y1) is shown as an estimate only."; }
            string position = double.IsNaN(tw) ? "tailwater not given"
                : tw < 0.98 * y2o ? "TW below y2: the jump is swept downstream; a basin is needed"
                : tw > 1.02 * y2o ? "TW above y2: the jump forms without a basin (pushed upstream)"
                : "TW ≈ y2: the jump forms at the outlet";
            string secText = sec switch
            {
                Circle c => $"circular, D = {c.D:G4}",
                Trap { Z: 0 } t => $"rectangular, b = {t.B:G4}",
                Trap t => $"trapezoidal, b = {t.B:G4}, z = {t.Z:G3}",
                _ => "",
            };
            tables.Add(new("Hydraulic jump at the outlet (no basin)", ["Item", "Value", "Unit"],
            [
                ["Q", q, Qu], ["Section", secText, ""],
                ["Unit discharge q", rect ? R(q / wo!.Value, 3) : null, rect ? qU : ""],
                ["Critical depth yc", R(yc, 3), L], ["Entering depth y1", R(yo, 3), L], ["Entering velocity V1", R(vo, 2), V],
                ["Froude number Fr1", R(fro, 2), ""], ["Jump type", JumpType(fro), ""],
                ["Sequent depth y2", R(y2o, 3), L], ["y2/y1", R(y2o / yo, 2), ""],
                ["Energy E1", R(eo, 3), L], ["Energy E2", R(e2o, 3), L], ["Energy loss ΔE", R(eo - e2o, 3), L],
                ["Relative loss HL/H1", R((eo - e2o) / eo, 3), ""], ["Jump length Lj", lj is { } l ? R(l, 2) : null, L],
                ["Tailwater TW", double.IsNaN(tw) ? null : R(tw, 3), L], ["Jump position", position, ""],
            ],
            ["Sequent depth from equal specific force M = Q²/(gA) + A·ȳ (HEC-14 Eq. 6.1, any shape; Eq. 6.3 / 8.4 for a rectangle; Eqs. 6.4–6.5 for a circle, with the pressure head on the full area once y2 ≥ D).",
             "Fr1 = V1/√(g·A/T). Jump forms by Fr1 (HEC-14 Figure 6.2): 1.7–2.5 pre-jump, 2.5–4.5 oscillating, 4.5–9 steady, above 9 strong.",
             ljNote,
             entry == EntryKind.Normal ? "y1 = normal depth on the approach (HEC-14 Step 1 for supercritical flow)." : entry == EntryKind.Energy ? "y1 = supercritical depth with the given energy (no loss)." : "y1 as given.",
             "This is HEC-14 Step 3: if TW ≥ y2 the jump forms without a basin."]));
            if (double.IsNaN(tw)) msgs.Add(CalcMessage.Warning("W-SB-002", "No tailwater given: the jump position and basins cannot be checked. Give tailwater, a [Tailwater rating], or a tailwater channel."));
            if (fro < 1.7) msgs.Add(CalcMessage.Info("I-SB-002", "Fr1 < 1.7: a stilling basin is not usually needed; riprap, wingwalls and an apron may be (HEC-14 §6.2.1)."));

            if (wb is null || double.IsNaN(tw)) return;
            double basinW = wb.Value;
            var bsec = new Trap(basinW, 0);
            if (dia is null && z == 0 && basinW < wo!.Value - 1e-9) msgs.Add(CalcMessage.Warning("W-SB-010", "The basin is narrower than the outlet; HEC-14 basins are as wide as or wider than the outlet."));
            double qUnit = q / basinW, vLim = si ? 18.3 : 60, qLim = si ? 18.6 : 200;

            BasinState State(string basin, double qq, double drop, double? builtRise = null)
            {
                double e1 = eOf(qq) + drop;   // Eq. 8.2: no loss from the outlet to the floor
                double y1 = double.IsNaN(e1) ? double.NaN : SupercriticalDepth(bsec, qq, e1, g);
                if (double.IsNaN(y1)) return new(drop, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
                double v1 = qq / (basinW * y1), fr = v1 / Math.Sqrt(g * y1);
                double conj = y1 / 2 * (Math.Sqrt(1 + 8 * fr * fr) - 1);   // Eq. 8.4 with C = 1
                double c = TailwaterFactor(basin, fr), y2 = c * conj;
                double lb = BasinLength(basin, fr, y2, c);
                var (lt, ls, rise) = Geometry(drop, lb, st, ss, so);
                return new(drop, y1, v1, fr, conj, c, y2, lt, lb, ls, builtRise ?? rise);
            }

            (BasinState S, bool Found) Solve(string basin)
            {
                if (fixedDrop is { } fd) return (State(basin, q, fd), true);
                double Gap(double dd) { var s = State(basin, q, dd); return s.Rise + tw - s.Y2; }
                if (Gap(0) >= 0) return (State(basin, q, 0), true);
                double hi = 1;
                for (int i = 0; i < 40 && !(Gap(hi) >= 0); i++) hi *= 2;
                if (!(Gap(hi) >= 0)) return (State(basin, q, hi), false);
                return (State(basin, q, Bisect(Gap, 0, hi)), true);
            }

            var basins = new List<(string Name, string Range, Func<BasinState, bool> Ok)>
            {
                ("Free jump", "Fr1 ≥ 1.7 (economical for 1.7–2.5)", s => s.Fr1 >= 1.7),
                ("USBR Type III", $"4.5 ≤ Fr1 ≤ 17, V1 ≤ {vLim:G3} {V}, q ≤ {qLim:G3} {qU}", s => s.Fr1 is >= 4.5 and <= 17 && s.V1 <= vLim && qUnit <= qLim),
                ("USBR Type IV", "2.5 ≤ Fr1 ≤ 4.5", s => s.Fr1 is >= 2.5 and <= 4.5),
                ("SAF", "1.7 ≤ Fr1 ≤ 17", s => s.Fr1 is >= 1.7 and <= 17),
            };
            var rows = new List<IReadOnlyList<object?>>();
            var detail = new List<IReadOnlyList<object?>>();
            (string Name, BasinState S)? best = null;
            foreach (var b in basins)
            {
                var (s, found) = Solve(b.Name);
                if (double.IsNaN(s.Y1)) continue;
                double have = s.Rise + tw, margin = have - s.Y2;
                bool ok = b.Ok(s), holds = found && margin >= -1e-6;
                rows.Add([b.Name, b.Range, ok ? "yes" : "no", R(s.Drop, 2), R(s.Y1, 3), R(s.V1, 2), R(s.Fr1, 2), R(s.Y2Conj, 3), R(s.C, 3), R(s.Y2, 3), R(s.Rise, 3), R(have, 3), R(margin, 3),
                    R(s.Lt, 2), R(s.Lb, 2), R(s.Ls, 2), R(s.Total, 2), !ok ? "outside its Fr range at the floor" : holds ? "holds the jump" : "tailwater short"]);
                if (ok)
                {
                    detail.AddRange(Appurtenances(b.Name, s, basinW, dia, q, g, si, L));
                    if (holds && (best is null || s.Total < best.Value.S.Total)) best = (b.Name, s);
                }
            }
            tables.Add(new("Stilling basins (HEC-14 Ch. 8)", ["Basin", "Range", "Applies at the floor", $"Floor drop zo − z1 ({L})", $"y1 ({L})", $"V1 ({V})", "Fr1", $"y2' conj. ({L})", "C = TW/y2'", $"y2 = C·y2' ({L})", $"z3 − z1 ({L})", $"z3 − z1 + TW ({L})", $"Margin ({L})", $"LT ({L})", $"LB ({L})", $"LS ({L})", $"Total ({L})", "Result"], rows,
                [$"HEC-14 §8.1–8.2: y1 from the energy at the floor, E1 = (zo − z1) + yo + Vo²/2g (Eq. 8.2), in a rectangular basin WB = {basinW:G4} {L}; y2 = C·y1/2·(√(1 + 8Fr1²) − 1) (Eq. 8.4).",
                 $"Geometry: LT = (zo − z1)/ST (Eq. 8.5), LS = [LT(ST − So) − LB·So]/(SS + So) (Eq. 8.6), z3 = z1 + LS·SS (Eq. 8.7). ST = {st:G3}, SS = {ss:G3}, So = {so * 100:G3} %. The jump holds when z1 + y2 ≤ z3 + TW.",
                 "C: free jump and Type III 1.0 (Type III may use 0.85 at a minimum), Type IV 1.1, SAF Eq. 8.18 (1.1 − Fr²/120 for 1.7–5.5, 0.85 for 5.5–11, 1.0 − Fr²/800 for 11–17).",
                 "LB: free jump and Type IV from the Figure 8.2 free-jump curve, Type III from its Figure 8.2 curve (digitized, D-327), SAF LB = 4.5·y2/(C·Fr1^0.76) (Eq. 8.19). Total = LT + LB + LS.",
                 fixedDrop is null ? "floor drop = auto: the least drop that holds the jump for each basin." : $"floor drop = {fixedDrop:G4} {L} for every basin."]));
            if (detail.Count > 0)
                tables.Add(new("Blocks, baffles and sills (HEC-14 Ch. 8)", ["Basin", "Element", "Count", $"Height ({L})", $"Width ({L})", $"Spacing ({L})", $"Wall gap ({L})", $"Position ({L})", "Notes"], detail,
                    ["Type III: chute blocks h1 = y1 (at least 0.2 m / 0.66 ft), Nc = WB/(2y1), W1 = W2 = WB/(2Nc) (Eqs. 8.9–8.10); baffles h3 = y1(0.168Fr1 + 0.58) (Eq. 8.11), NB = WB/(1.5h3), W3 = W4 = WB/(2NB) (Eqs. 8.12–8.13), top 0.2h3, 1:1 back slope, 0.8y2 from the chute blocks; end sill h4 = y1(0.0536Fr1 + 1.04) (Eq. 8.14), 0.5:1 fore slope.",
                     "Type IV: chute blocks h1 = 2y1, top sloped 5° downstream, Nc = WB/(2.625y1), W1 = WB/(3.5Nc) ≤ y1, W2 = 2.5W1 (Eqs. 8.15a–c); end sill h4 = y1(0.0536Fr1 + 1.04) (Eq. 8.16).",
                     "SAF: chute blocks h1 = y1, Nc = WB/(1.5y1), W1 = W2 = WB/(2Nc), half blocks at the walls (Eqs. 8.20–8.21); baffles h3 = y1, NB = WB2/(1.5y1), W3 = W4 = WB2/(2NB), LB/3 from the chute blocks, 40–55 % of WB2 blocked, no closer than 3y1/8 to a wall (Eqs. 8.22–8.24); end sill h4 = 0.07y2/C (Eq. 8.25); sidewalls h5 ≥ y2(1 + 1/(3C)) above the floor (Eq. 8.27).",
                     "Counts are rounded to the nearest integer as HEC-14 does; the wall gap is the width left over, split equally between the two walls."]));

            // Eq. 8.3 width limit and Eq. 8.8 radii for the chosen (or first) basin.
            var geomBasin = best?.S ?? Solve("Free jump").S;
            if (dia is null && z == 0 && basinW > wo!.Value + 1e-9)
            {
                double wmax = wo.Value + 2 * geomBasin.Lt * Math.Sqrt(st * st + 1) / (3 * fro);
                if (basinW > wmax + 1e-9) msgs.Add(CalcMessage.Warning("W-SB-011", $"WB = {basinW:G4} {L} is wider than the jet can spread on the transition, Wo + 2LT√(ST² + 1)/(3Fro) = {wmax:F2} {L} (HEC-14 Eq. 8.3)."));
            }
            double rO = TransitionRadius(yo, fro), r1 = double.IsNaN(geomBasin.Fr1) ? double.NaN : TransitionRadius(geomBasin.Y1, geomBasin.Fr1);
            tables.Add(new("Transition curvature (HEC-14 Eq. 8.8)", ["Slope change", "Depth y", "Fr", $"Radius r ({L})"],
                [["Outlet to transition", R(yo, 3), R(fro, 2), R(rO, 2)], ["Transition to basin floor", R(geomBasin.Y1, 3), R(geomBasin.Fr1, 2), R(r1, 2)]],
                [$"r = y/(e^(1.5/Fr²) − 1). Use a circular curve of this radius where the transition slope is 0.5:1 (V:H) or steeper (ST = {st:G3}{(st >= 0.5 ? ", so it is needed" : "")})."]));

            if (best is { } bb)
            {
                summary.Add($"At the outlet Fr = {fro:F2}, y2 = {y2o:F2} {L} against TW = {tw:F2} {L}{(tw >= y2o ? ": the jump forms without a basin" : ": a basin is needed")}. Shortest HEC-14 basin that holds the jump: {bb.Name}, {bb.S.Total:F1} {L} overall (LT {bb.S.Lt:F1} + LB {bb.S.Lb:F1} + LS {bb.S.Ls:F1}), floor {bb.S.Drop:F2} {L} below the outlet invert, Fr1 = {bb.S.Fr1:F2}.");
                figures.Add(BasinFigure(bb.Name, bb.S, yo, tw, so, L));
            }
            else
            {
                summary.Add($"At the outlet Fr = {fro:F2}, y2 = {y2o:F2} {L}, TW = {tw:F2} {L}: no HEC-14 basin holds the jump at this floor.");
                msgs.Add(CalcMessage.Warning("W-SB-003", "No applicable basin holds the jump at the design flow; lower the floor, widen the basin or raise the tailwater."));
            }
            if (best?.Name == "SAF" && dia is null && basinW > wo!.Value + 1e-9 && safFlare == 0)
                msgs.Add(CalcMessage.Info("I-SB-009", "HEC-14 §8.5: for a box culvert the SAF basin width equals the culvert width (flare it with saf flare instead)."));
            if (dia is { } dd && best?.Name == "SAF")
            {
                double wMin = Math.Max(dd, 1.7 * q / (Math.Sqrt(g) * Math.Pow(dd, 1.5)));
                if (basinW < wMin - 1e-9) msgs.Add(CalcMessage.Warning("W-SB-012", $"HEC-14 Eq. 8.17: an SAF basin below a {dd:G4} {L} pipe should be at least {wMin:F2} {L} wide."));
            }

            // Flow range check with the chosen basin as built (its floor and exit fixed).
            if (checkQs.Count > 0 && best is { } chosen)
            {
                var crow = new List<IReadOnlyList<object?>>();
                var needCurve = new List<(double, double)>();
                var haveCurve = new List<(double, double)>();
                foreach (double cq in checkQs.Append(q).Distinct().OrderBy(x => x))
                {
                    var s = State(chosen.Name, cq, chosen.S.Drop, chosen.S.Rise);
                    double ctw = TailwaterAt(cq) ?? double.NaN;
                    if (double.IsNaN(s.Y1)) { crow.Add([cq, null, null, null, null, R(ctw, 3), null, "no supercritical entry"]); continue; }
                    double have = ctw + s.Rise;
                    crow.Add([cq, R(s.Y1, 3), R(s.Fr1, 2), R(s.C, 3), R(s.Y2, 3), R(ctw, 3), R(have, 3), have >= s.Y2 - 1e-6 ? "holds" : "swept out"]);
                    needCurve.Add((cq, s.Y2)); haveCurve.Add((cq, have));
                    if (have < s.Y2 - 1e-6) msgs.Add(CalcMessage.Warning("W-SB-005", $"Q = {cq:G4} {Qu}: z3 − z1 + TW = {have:F2} {L} is below the {s.Y2:F2} {L} the {chosen.Name} basin needs; the jump is swept out."));
                }
                tables.Add(new($"Flow range check ({chosen.Name}, floor {chosen.S.Drop:F2} {L} below the outlet, z3 − z1 = {chosen.S.Rise:F2} {L})", [$"Q ({Qu})", $"y1 ({L})", "Fr1", "C", $"y2 needed ({L})", $"TW ({L})", $"z3 − z1 + TW ({L})", "Jump"], crow,
                    [entry == EntryKind.Normal ? "Each flow leaves the outlet at its normal depth." : "Each flow enters with the same total head as the design flow."]));
                charts.Add(new("Tailwater and required depth over the flow range", $"Q ({Qu})", $"Depth above the basin floor ({L})",
                    [new("Tailwater + exit rise", haveCurve), new($"Depth needed ({chosen.Name})", needCurve, Dashed: true)]));
            }
        }
    }

    private static double SolveDepthForArea(Shape s, double area)
    {
        double hi = double.IsFinite(s.Crown) ? s.Crown * (1 - 1e-9) : Upper(y => s.Area(y) - area, 1);
        return s.Area(hi) < area ? double.PositiveInfinity : Bisect(y => s.Area(y) - area, 0, hi);
    }

    /// <summary>Elements of one basin by HEC-14 Ch. 8: count, height, width, spacing, wall gap, position.</summary>
    public static IEnumerable<IReadOnlyList<object?>> Appurtenances(string basin, BasinState s, double wb, double? pipeD, double q, double g, bool si, string u)
    {
        double y1 = s.Y1, fr = s.Fr1;
        static int Count(double x) => Math.Max(1, (int)Math.Round(x, MidpointRounding.AwayFromZero));
        switch (basin)
        {
            case "USBR Type III":
            {
                double h1 = Math.Max(y1, si ? 0.2 : 0.66);
                int nc = Count(wb / (2 * y1)); double w1 = wb / (2 * nc);
                double h3 = y1 * (0.168 * fr + 0.58);
                int nb = Count(wb / (1.5 * h3)); double w3 = wb / (2 * nb);
                double h4 = y1 * (0.0536 * fr + 1.04);
                yield return new object?[] { basin, "Chute blocks", nc, R(h1, 2), R(w1, 2), R(w1, 2), R((wb - (2 * nc - 1) * w1) / 2, 2), "at the toe", "" };
                yield return new object?[] { basin, "Baffle blocks", nb, R(h3, 2), R(w3, 2), R(w3, 2), R((wb - (2 * nb - 1) * w3) / 2, 2), R(0.8 * s.Y2, 2), $"top {0.2 * h3:F2} {u}, 1:1 back slope; 0.8y2 from the chute blocks; square corners" };
                yield return new object?[] { basin, "End sill", 1, R(h4, 2), null, null, null, $"end of LB ({s.Lb:F1} {u})", "solid, 0.5:1 fore slope" };
                break;
            }
            case "USBR Type IV":
            {
                int nc = Count(wb / (2.625 * y1)); double w1 = wb / (3.5 * nc), w2 = 2.5 * w1;
                double h4 = y1 * (0.0536 * fr + 1.04);
                yield return new object?[] { basin, "Chute blocks", nc, R(2 * y1, 2), R(w1, 2), R(w2, 2), R((wb - nc * w1 - (nc - 1) * w2) / 2, 2), "at the toe", $"top sloped 5° downstream; W1 {(w1 <= y1 + 1e-9 ? "≤" : "> (should be ≤)")} y1" };
                yield return new object?[] { basin, "End sill", 1, R(h4, 2), null, null, null, $"end of LB ({s.Lb:F1} {u})", "solid, 0.5:1 fore slope; TW ≥ 1.1·y2'" };
                break;
            }
            case "SAF":
            {
                int nc = Count(wb / (1.5 * y1)); double w1 = wb / (2 * nc);
                int nb = Count(wb / (1.5 * y1)); double w3 = wb / (2 * nb);
                double blocked = nb * w3 / wb, gap = (wb - (2 * nb - 1) * w3) / 2;
                double h4 = 0.07 * s.Y2 / s.C, h5 = s.Y2 * (1 + 1 / (3 * s.C));
                yield return new object?[] { basin, "Chute blocks", nc, R(y1, 2), R(w1, 2), R(w1, 2), 0.0, "at the toe", "half block at each wall" };
                yield return new object?[] { basin, "Baffle blocks", nb, R(y1, 2), R(w3, 2), R(w3, 2), R(gap, 2), R(s.Lb / 3, 2), $"{blocked * 100:F0} % of the width blocked (40–55 %){(gap < 3 * y1 / 8 - 1e-9 ? "; closer to the wall than 3y1/8" : "")}; top 0.2h3, 1:1 back slope" };
                yield return new object?[] { basin, "End sill", 1, R(h4, 2), null, null, null, $"end of LB ({s.Lb:F1} {u})", "0.5:1 fore slope" };
                yield return new object?[] { basin, "Sidewalls", 2, R(h5, 2), null, null, null, "above the floor", "wingwalls the same height, 1H:1V top, best at 45°; cut-off wall at the end" };
                break;
            }
        }
        _ = pipeD; _ = q; _ = g;
    }

    private static ToolFigure BasinFigure(string name, BasinState s, double yo, double tw, double so, string u)
    {
        // Elevations relative to the outlet invert zo = 0; x from the outlet.
        double up = Math.Max(2, 0.15 * s.Total);
        double xFloor = s.Lt, xEnd = s.Lt + s.Lb, xExit = xEnd + s.Ls;
        double z1 = -s.Drop, z3 = z1 + s.Rise;
        var ground = new List<(double, double)> { (-up, so * up), (0, 0), (xFloor, z1), (xEnd, z1), (xExit, z3), (xExit + up, z3 - so * up) };
        double lj = Math.Min(s.Lb, Curve(Fig82Free, s.Fr1) * s.Y2Conj);
        var water = new List<(double, double)> { (-up, so * up + yo), (0, yo), (xFloor, z1 + s.Y1), (xFloor + 0.05 * lj, z1 + s.Y1), (xFloor + lj, z1 + s.Y2Conj), (xExit, z3 + tw), (xExit + up, z3 - so * up + tw) };
        return new ToolFigure($"{name} basin: longitudinal section (HEC-14 Figure 8.1)", [
            new("Outlet, transition, floor and exit", ToolSeriesKind.Ground, ground),
            new("Water surface (schematic)", ToolSeriesKind.Water, water),
            new("Tailwater", ToolSeriesKind.Limit, [(xExit, z3 + tw), (xExit + up, z3 + tw - so * up)]),
        ], $"Distance from the outlet ({u})", $"Elevation above the outlet invert ({u})", ["Schematic: the jump is drawn as a straight rise; blocks and sills are in the table."]);
    }

    private static void OutletBasins(ToolInput p, bool si, double g, double k, string L, string V, string Qu, List<CalcMessage> msgs, List<ToolTable> tables, List<string> summary, Func<double, double?> tailwaterAt)
    {
        string shape = (p.Text("outlet shape") ?? "circular").ToLowerInvariant();
        bool box = shape.StartsWith("box") || shape.StartsWith("rect");
        double span = p.Number("outlet span", double.NaN), rise = box ? p.Number("outlet rise", span) : span;
        double? yoIn = p.OptionalNumber("outlet depth"), sl = p.OptionalNumber("outlet slope"), n = p.OptionalNumber("outlet n");
        if (!(span > 0) || !(rise > 0)) { msgs.Add(CalcMessage.Error("E-SB-010", "The outlet span (and rise for a box) must be above zero.")); return; }
        if (!box && !shape.StartsWith("circ")) { msgs.Add(CalcMessage.Error("E-SB-010", $"outlet shape \"{shape}\" is not circular or box.")); return; }
        double? qImpact = p.OptionalNumber("impact Q") ?? (p.Text("csu Q") is null ? p.OptionalNumber("Q") : null);
        double? qCsu = p.OptionalNumber("csu Q") ?? (p.Text("impact Q") is null && p.Text("exit velocity") is not null ? p.OptionalNumber("Q") : null);
        double toFt = si ? 1 / 0.3048 : 1, toCfs = si ? 35.3147 : 1;

        (double Y, double A, double Vel, string Basis)? Outlet(double q)
        {
            ChannelSection sec = box ? new RectangularSection(span) : new CircularSection(span);
            var geo = Hydraulics.Geometry.SectionGeometry.From(sec);
            double y; string basis;
            if (yoIn is > 0) { y = Math.Min(yoIn.Value, rise); basis = "given"; }
            else if (sl is > 0 && n is > 0)
            {
                var r = UniformFlowCalculator.SolveNormalDepth(geo, q, n.Value, sl.Value / 100, k);
                if (!r.Converged || r.Root >= rise) { y = rise; basis = "full (Q above open-channel capacity)"; }
                else { y = r.Root; basis = "normal depth"; }
                var c = UniformFlowCalculator.SolveCriticalDepth(geo, q, g);
                if (c.Converged && y > c.Root && basis == "normal depth") { y = c.Root; basis = "critical (mild barrel)"; }
            }
            else { msgs.Add(CalcMessage.Error("E-SB-011", "Give the outlet depth, or the outlet slope (%) and outlet n.")); return null; }
            double a = y >= rise ? geo.Area(rise * (1 - 1e-9)) : geo.Area(y);
            return (y, a, q / a, basis);
        }

        if (qImpact is > 0) ImpactBasin(qImpact.Value);
        if (qCsu is > 0) CsuBasin(qCsu.Value);

        void ImpactBasin(double q)
        {
            int count = (int)Math.Max(1, p.Number("impact basins", 1));
            double qb = q / count;
            if (Outlet(qb) is not { } o) return;
            double? hoWbIn = p.OptionalNumber("impact Ho/WB"), hlIn = p.OptionalNumber("impact HL/Ho");
            // HEC-14 §9.4: ye = (A/2)^0.5 for a pipe or irregular conduit; the depth itself for a rectangular section.
            double ye = box ? o.Y : Math.Sqrt(o.A / 2), fr = o.Vel / Math.Sqrt(g * ye), ho = ye + o.Vel * o.Vel / (2 * g);
            double hoWb = hoWbIn ?? Fig914(fr), wReq = ho / hoWb;
            var table = si ? Table92Si : Table92Cu;
            double step = si ? 0.5 : 1;
            double wRow = Math.Round(wReq / step, MidpointRounding.AwayFromZero) * step;
            double[] dims;
            string rowNote;
            if (wRow < table[0][0] || wRow > table[^1][0])
            {
                var end = wRow < table[0][0] ? table[0] : table[^1];
                double f = wReq / end[0];
                dims = end.Select(v => v * f).ToArray();
                rowNote = $"WB = {wReq:F2} {L} is outside Table 9.2 ({table[0][0]:G3}–{table[^1][0]:G3} {L}); dimensions are scaled in proportion from the {end[0]:G3} {L} row. Check them.";
                msgs.Add(CalcMessage.Warning("W-SB-013", rowNote));
            }
            else
            {
                dims = table.Single(r => Math.Abs(r[0] - wRow) < 1e-9);
                rowNote = $"WB = {wReq:F2} {L} rounded to the nearest Table 9.2 entry, {wRow:G3} {L} (HEC-14: interpolation is not necessary).";
            }
            double wB = dims[0];
            double hl = hlIn ?? Fig915(fr), hb = ho * (1 - hl), qUnit = qb / wB;
            // HB = yB + VB²/2g with yB = q/VB: supercritical and subcritical roots.
            var rect = new Trap(wB, 0);
            double yB_c = Critical(rect, qb, g);
            double ySup = Energy(rect, qb, yB_c, g) > hb ? double.NaN : Bisect(y => Energy(rect, qb, y, g) - hb, 1e-9 * yB_c, yB_c);
            double ySub = double.IsNaN(ySup) ? double.NaN : Bisect(y => Energy(rect, qb, y, g) - hb, yB_c, Upper(y => Energy(rect, qb, y, g) - hb, 2 * yB_c));
            double? twImp = tailwaterAt(q);
            bool useSub = twImp is { } t && !double.IsNaN(ySub) && t >= ySub;
            double yB = useSub ? ySub : ySup, vB = qUnit / yB;
            List<IReadOnlyList<object?>> rows =
            [
                ["Q per basin", R(qb, 3), Qu], ["Outlet depth (" + o.Basis + ")", R(o.Y, 3), L], ["Flow area A", R(o.A, 3), si ? "m²" : "ft²"],
                ["Entering velocity Vo", R(o.Vel, 2), V], [box ? "Equivalent depth ye = y (rectangular)" : "Equivalent depth ye = (A/2)^0.5", R(ye, 3), L],
                ["Fr = Vo/(g·ye)^0.5", R(fr, 2), ""], ["Ho = ye + Vo²/2g", R(ho, 3), L],
                [hoWbIn is null ? "Ho/WB (Figure 9.14)" : "Ho/WB (given)", R(hoWb, 3), ""], ["Required width Ho/(Ho/WB)", R(wReq, 2), L],
                ["Design WB (Table 9.2)", R(wB, 2), L],
                [hlIn is null ? "HL/Ho (Figure 9.15)" : "HL/Ho (given)", R(hl, 3), ""], ["HB = Ho(1 − HL/Ho)", R(hb, 3), L],
                ["Exit depth yB, supercritical", R(ySup, 3), L], ["Exit depth yB, subcritical", R(ySub, 3), L],
                [$"Exit velocity VB ({(useSub ? "subcritical: tailwater sufficient" : "supercritical: low or no tailwater")})", R(vB, 2), V],
            ];
            tables.Add(new("USBR Type VI impact basin (HEC-14 §9.4)", ["Item", "Value", "Unit"], rows,
                ["HEC-14 §9.4 Steps 1–5. Fr and Ho use the equivalent depth ye; WB = Ho/(Ho/WB) from Figure 9.14, rounded to the nearest Table 9.2 entry; the exit velocity solves Q/(WB·VB) + VB²/2g = Ho(1 − HL/Ho), HL/Ho from Figure 9.15.",
                 "Figures 9.14 and 9.15 are lines through the HEC-14 worked examples (Fr 4.7–5.1) until the figures are digitized (D-327). Give impact Ho/WB and impact HL/Ho read from the figures to override them.",
                 rowNote]));
            var dimRows = Table92Names.Select((nm, i) => (IReadOnlyList<object?>)new object?[] { nm, R(dims[i], 2), L }).ToList();
            tables.Add(new("USBR Type VI dimensions (HEC-14 Table 9.2, Figure 9.13)", ["Dimension", "Value", "Unit"], dimRows,
                ["Names as in HEC-14 Figure 9.13. Use the alternative end sill and 45° wingwalls; set the sill as low as possible with the downstream channel at the top of the sill; slot the sill for drainage; add a cut-off wall.",
                 "Riprap at least four conduit widths downstream (size by HEC-14 Ch. 10 / the outlet protection tool)."]));
            double h2 = dims[2], h3 = dims[3];
            if (twImp is { } tw2 && tw2 > h3 + h2 / 2) msgs.Add(CalcMessage.Warning("W-SB-014", $"Tailwater {tw2:F2} {L} is above h3 + h2/2 = {h3 + h2 / 2:F2} {L}, half the baffle; HEC-14 §9.4 advises setting the basin so maximum tailwater stays below that."));
            if (qb * toCfs > 400) msgs.Add(CalcMessage.Warning("W-SB-006", $"{qb * toCfs:F0} cfs per basin is above the 400 cfs (11.3 m³/s) the Type VI basin was tested for (HEC-14 §9.4); set impact basins = 2 or more to place basins side by side."));
            if (o.Vel * toFt > 50) msgs.Add(CalcMessage.Warning("W-SB-007", $"Entering velocity {o.Vel:F1} {V} is above 50 ft/s (15.2 m/s), the HEC-14 limit for the impact basin."));
            if ((hoWbIn is null && (fr < 1.0 || fr > 7.0)) || (hlIn is null && (fr < 1.4 || fr > 7.0))) msgs.Add(CalcMessage.Warning("W-SB-015", $"Fr = {fr:F2} is beyond the ends of HEC-14 Figure 9.14 (Fr 1–7) or 9.15 (Fr 1.4–7); the end values are held. The Type VI basin is outside its tested range."));
            if (sl is > 27) msgs.Add(CalcMessage.Info("I-SB-010", "The entrance conduit is steeper than 15° (27 %): HEC-14 asks for a horizontal section at least four conduit widths long ahead of the basin (and a vent if a jump forms in a full pipe)."));
            msgs.Add(CalcMessage.Info("I-SB-008", "Type VI (HEC-14 §9.4): not recommended where debris or ice may clog it; a short support under the centre of the baffle adds strength and dissipation."));
            summary.Add($"USBR Type VI impact basin (HEC-14): WB = {wB:G3} {L}, L = {dims[5]:G3} {L}, exit velocity {vB:F1} {V}{(count > 1 ? $" (each of {count} basins)" : "")}.");

            // Rapid City IDCM check: Figure 4-8 width line and Table 4-15.
            double dEq = Math.Sqrt(o.A), frI = o.Vel / Math.Sqrt(g * dEq);
            double wFr = 2.88 * Math.Pow(frI, 0.566) * dEq, qcfs = qb * toCfs, wTab = ImpactWidthFromTable(qcfs) / 12 / toFt, wI = Math.Max(wFr, wTab);
            List<IReadOnlyList<object?>> idcm =
            [
                ["D = √A", R(dEq, 3), L], ["Fr = V/√(gD)", R(frI, 2), ""],
                ["W from Fr: 2.88·Fr^0.566·D (Figure 4-8)", R(wFr, 2), L], ["W from Q (Table 4-15)", R(wTab, 2), L], ["IDCM W (larger)", R(wI, 2), L],
                ["HEC-14 WB", R(wB, 2), L], ["HEC-14 WB − IDCM W", R(wB - wI, 2), L],
            ];
            double[]? row = ImpactTable.FirstOrDefault(r => r[1] >= qcfs - 1e-9 && r[2] / 12 / toFt >= wI - 1e-9);
            if (row is not null) idcm.Add(["IDCM Table 4-15 row (pipe, max Q, W)", $"{row[0]:G3} in., {row[1]:G3} cfs, {FtIn(row[2])}", ""]);
            tables.Add(new("USBR Type VI, Rapid City IDCM check (§4.9.2(1), Figure 4-8, Table 4-15)", ["Item", "Value", "Unit"], idcm,
                ["The IDCM sizes the same basin from its own Figure 4-8 and Table 4-15 (D = √A); HEC-14 is the design reference here, and this table shows whether the IDCM would ask for more."]));
            if (wI > wB + 1e-6) msgs.Add(CalcMessage.Info("I-SB-011", $"The Rapid City IDCM width ({wI:F2} {L}) is wider than the HEC-14 width ({wB:F2} {L}); a Rapid City submittal may need the IDCM basin."));
        }

        void CsuBasin(double q)
        {
            double? vbIn = p.OptionalNumber("exit velocity");
            double slopePct = p.Number("basin slope", 0);
            string ratioText = (p.Text("expansion ratio") ?? "auto").ToLowerInvariant();
            double? lc = p.OptionalNumber("anchor lever"), yaIn = p.OptionalNumber("csu yA"), cpIn = p.OptionalNumber("Cp");
            if (Outlet(q) is not { } o) return;
            double? twC = tailwaterAt(q);
            if (vbIn is not > 0 && twC is null)
            {
                msgs.Add(CalcMessage.Error("E-SB-012", "The CSU basin needs the downstream condition: a tailwater (depth, rating or channel) for HEC-14 option 1, or exit velocity (the velocity leaving the basin)."));
                return;
            }
            double wo = span, yo = o.Y, vo = o.Vel, fr = vo / Math.Sqrt(g * yo);
            double gamma = si ? 9810 : 62.4, rho = gamma / g, so = slopePct / 100;
            double kuQd = (si ? 1.811 : 1.0) * q / Math.Pow(wo, 2.5);
            double cp = cpIn ?? Fig94(box, kuQd);
            double vaRatio = box ? 1.65 - 0.3 * fr : 1.65 - 0.45 * q / Math.Sqrt(g * Math.Pow(wo, 5));
            double vaEq = vaRatio * vo;
            if (vaEq <= 0) { msgs.Add(CalcMessage.Error("E-SB-013", $"VA/Vo = {vaRatio:F3} is not positive (Eq. {(box ? "4.1" : "4.2")}); the outlet is beyond the tests.")); return; }
            double wa = wo * (4 / (3 * fr) + 1);   // HEC-14 Eq. 9.3 note, from Eqs. 4.3–4.4 at L = 2Wo
            // HEC-14 Eq. 9.3 (D-328): yA + 0.25(Q/(WA·yA))²/2g = 2Wo·So + yo + 0.25·Vo²/2g; the supercritical (smaller) root.
            double yaEq93 = Eq93(q, wo, wa, so, yo, vo, g);
            if (double.IsNaN(yaEq93) && yaIn is null && (so > 0.10 || ratioText is "auto" or "2" or "3" or "4" or "2-4"))
                msgs.Add(CalcMessage.Warning("W-SB-016", "HEC-14 Eq. 9.3 has no root for this outlet; the layouts that need it are skipped. Give csu yA to override."));
            var ratios = ratioText == "auto" ? new[] { "2-4", "5", "6", "7", "8" } : [ratioText is "2" or "3" or "4" or "2-4" ? "2-4" : ratioText];
            if (ratios.Any(r => r is not ("2-4" or "5" or "6" or "7" or "8"))) { msgs.Add(CalcMessage.Error("E-SB-014", "expansion ratio must be 2-4, 5, 6, 7, 8 or auto (HEC-14 Table 9.1).")); return; }
            var rows = new List<IReadOnlyList<object?>>();
            (double Len, IReadOnlyList<object?> Row)? best = null;
            bool option1 = twC is not null;
            for (int ci = 0; ci < CsuConfigs.Length; ci++)
            {
                var cfg = CsuConfigs[ci];
                if (!ratios.Contains(cfg.Ratio)) continue;
                double wbRatio = cfg.Ratio == "2-4" ? (ratioText is "2" or "3" ? double.Parse(ratioText, CultureInfo.InvariantCulture) : 4) : double.Parse(cfg.Ratio, CultureInfo.InvariantCulture);
                double wb = wbRatio * wo;
                bool steep = so > 0.10;
                double va = steep && !double.IsNaN(yaEq93) ? q / (wa * yaEq93) : vaEq;
                double ya = yaIn ?? (wbRatio < 4 || steep ? yaEq93 : yo * (box ? Fig43(fr) : Fig44(fr)));
                if (double.IsNaN(ya)) continue;
                // Downstream condition: option 1 (tailwater or normal depth) or a target exit velocity.
                double vbT = option1 ? q / (wb * twC!.Value) : vbIn!.Value;
                double Weight(double vb, int nr, double spacing) =>
                    gamma * ((yo * wo + ya * wa) * wo + 0.75 * spacing * q / vb * ((nr - 1) - (wbRatio - 3) * (1 - wa / wb) / 2));
                foreach (var cb in CsuCb.Where(c => c.Circular == !box))
                {
                    double cB = cb.Cb[ci];
                    if (double.IsNaN(cB)) continue;
                    double h = cb.HYa * ya, spacing = cb.Lh * h, w1 = cfg.W1 * wo, af = w1 * h;
                    double sinT = Math.Sin(Math.Atan(so));
                    double Lhs(double vb) => rho * vo * q + cp * gamma * yo * yo * wo / 2 + (steep ? Weight(vb, cfg.Nr, spacing) * sinT : 0);
                    double Out(double vb) => rho * vb * q + gamma * q * q / (2 * vb * vb * wb);
                    double required = (Lhs(vbT) - Out(vbT)) / (rho * va * va / 2);
                    double provided = cB * af * cfg.N, len = 2 * wo + spacing * cfg.Nr;
                    // Option 2: VB from Eq. 9.1/9.2 with this basin's drag; the larger positive root.
                    double F(double vb) => Lhs(vb) - provided * rho * va * va / 2 - Out(vb);
                    double vb2 = double.NaN;
                    {
                        double vMin = Math.Pow(gamma * q / (rho * wb), 1.0 / 3);   // minimum of Out(vb) (critical flow)
                        if (F(vMin) > 0) vb2 = Bisect(F, vMin, Upper(vb => -F(vb), 2 * vMin));
                    }
                    bool w1hOk = w1 / h is >= 2 and <= 8;
                    bool ok = provided >= required && w1hOk;
                    double? fa = lc is > 0 ? rho / 2 * (h / lc.Value) * af * va * va : null;
                    double fb = h + ya + 0.5 * Math.Pow(va * Math.Sin(Math.PI / 4), 2) / g;
                    double ue = 4.0 / 7 + 10.0 / 7 * spacing / wo;
                    var row = (IReadOnlyList<object?>)new object?[] { $"{(cfg.Ratio == "2-4" ? wbRatio.ToString("G3", CultureInfo.InvariantCulture) : cfg.Ratio)}", cfg.Nr, cfg.N, cb.HYa, cb.Lh, R(ya, 3), R(h, 3), R(spacing, 2), R(w1, 2), R(w1 / h, 2), R(wb, 2), cB, R(provided, 2), R(required, 2), R(len, 2), R(ue, 2), R(fb, 2), double.IsNaN(vb2) ? null : R(vb2, 2), double.IsNaN(vb2) ? null : R(q / (wb * vb2), 3), fa is null ? null : R(fa.Value, 0), ok ? "OK" : !w1hOk ? "W1/h outside 2–8" : "too little drag" };
                    rows.Add(row);
                    if (ok && (best is null || len < best.Value.Len)) best = (len, row);
                }
            }
            tables.Add(new("CSU rigid boundary basin (HEC-14 §9.1)", ["WB/Wo", "Rows Nr", "Elements N", "h/yA", "L/h", $"yA ({L})", $"h ({L})", $"Row spacing L ({L})", $"W1 ({L})", "W1/h", $"WB ({L})", "CB", "CB·AF·N provided", "CB·AF·N required", $"Length LB ({L})", "Flare ue", $"Freeboard FB ({L})", $"VB option 2 ({V})", $"yB option 2 ({L})", si ? "Anchor force FA (N)" : "Anchor force FA (lb)", "Result"], rows,
                [$"Outlet: yo = {yo:F3} {L} ({o.Basis}), Vo = {vo:F2} {V}, Fr = Vo/√(g·yo) = {fr:F2}; VA = {vaEq:F2} {V} (Eq. {(box ? "4.1" : "4.2")}){(so > 0.10 ? "; slope above 10 %, so VA and yA come from Eq. 9.3" : "")}.",
                 option1 ? $"Option 1 (HEC-14 Step 5): the basin exit is held at the tailwater depth {twC:F3} {L}, VB = Q/(WB·TW); required CB·AF·N from Eq. {(so > 0.10 ? "9.2" : "9.1")} must not exceed the basin's." : $"Exit velocity VB = {vbIn:G4} {V} as given; required CB·AF·N from Eq. {(so > 0.10 ? "9.2" : "9.1")}.",
                 $"Eq. 9.1: ρVoQ + Cp·γ·yo²·Wo/2 = CB·AF·N·ρVA²/2 + ρVBQ + γQ²/(2VB²WB); Cp = {cp:G3}{(cpIn is null ? (box ? " (Figure 9.4a, rectangular)" : $" (Figure 9.4b at KuQ/D^2.5 = {kuQd:F2}{(kuQd > 6 ? ", beyond the data; 0.30 held" : "")})") : " (given)")}. Option 2 solves it for VB with the basin's own drag (the larger, supercritical root) and yB = Q/(WB·VB); blank where the basin's drag exceeds what the flow can supply at critical depth, so the exit is subcritical and tailwater controls.",
                 yaIn is { } yg ? $"yA = {yg:G4} {L} as given." : (box ? $"yA: WB/Wo 4–8 from Figure 4.3 at L = 2Wo, yA/yo = 0.187 + 0.150(Fr − 1) = {Fig43(fr):F3}" : $"yA: WB/Wo 4–8 from Figure 4.4 at L = 2D, yA/yo = 0.161 + 0.212(Fr − 1) = {Fig44(fr):F3}") + "; WB/Wo < 4 and slopes above 10 % from Eq. 9.3 as printed (D-328). Give csu yA to override.",
                 "LB = 2Wo + L·Nr; W1/h 2–8; element spacing ≈ W1 and never under 0.75·W1; at least half the rows need an element near the wall; alternate rows staggered. Flare ue = 4/7 + (10/7)L/Wo (Eq. 9.5). Freeboard FB = h + yA + 0.5(VA·sin45°)²/g (Eq. 9.4).",
                 "Table 9.1's circular 2–4 / 6-row column is printed upside down; it is used reversed (D-321)."]));
            if (best is { } bst)
                summary.Add($"CSU basin: shortest passing layout WB/Wo = {bst.Row[0]}, {bst.Row[1]} rows of {bst.Row[2]} elements, h = {bst.Row[6]} {L}, LB = {bst.Len:F1} {L}.");
            else
            {
                summary.Add("CSU basin: no Table 9.1 layout gives enough drag; lower the exit target, widen the basin, or use another dissipator.");
                msgs.Add(CalcMessage.Warning("W-SB-009", "No HEC-14 Table 9.1 CSU layout provides the CB·AF·N the momentum equation needs."));
            }
            if (option1 && vbIn is > 0) msgs.Add(CalcMessage.Info("I-SB-012", "A tailwater is given, so the CSU basin uses HEC-14 option 1 (exit at the tailwater depth); exit velocity is ignored."));
            msgs.Add(CalcMessage.Info("I-SB-013", "CSU basin: if the exit does not match the downstream channel (option 2), riprap will likely be needed until the flow transitions (HEC-14 Step 7, Ch. 10)."));
        }
    }

    /// <summary>Table 4-15 basin width (in.) for a discharge (cfs), log–log between rows, extended from the end rows.</summary>
    public static double ImpactWidthFromTable(double qcfs)
    {
        var t = ImpactTable;
        int i = 1;
        while (i < t.Length - 1 && t[i][1] < qcfs) i++;
        double q0 = t[i - 1][1], q1 = t[i][1], w0 = t[i - 1][2], w1 = t[i][2];
        return w0 * Math.Pow(qcfs / q0, Math.Log(w1 / w0) / Math.Log(q1 / q0));
    }

    private static string FtIn(double inches) => $"{(int)(inches / 12)}-{inches % 12:0}";
    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
