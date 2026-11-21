namespace MasterFlow.Application.Hydrology;

/// <summary>
/// NRCS TR-55 (1986, "Urban Hydrology for Small Watersheds") tables: runoff curve numbers (Tables 2-2a to 2-2d),
/// connected and unconnected impervious adjustments (Figures 2-3 and 2-4), the graphical peak discharge method
/// (Chapter 4: Exhibit 4 via the Appendix F Table F-1 coefficients, Table 4-2 pond and swamp factor).
/// </summary>
public static class Tr55Tables
{
    /// <summary>One cover row of Tables 2-2a to 2-2d: CN for hydrologic soil groups A, B, C, D (NaN where the table gives none).</summary>
    public sealed record Cover(string Key, string Description, string Table, double A, double B, double C, double D)
    {
        public double For(char hsg) => char.ToUpperInvariant(hsg) switch { 'A' => A, 'B' => B, 'C' => C, 'D' => D, _ => double.NaN };
    }

    private const double X = double.NaN;

    /// <summary>
    /// Cover types by short key. Keys are what the tool's Cover column accepts (case-insensitive; spaces, hyphens and
    /// underscores are interchangeable). Residential lots and commercial/industrial already include their average
    /// impervious percentage (connected), per the Table 2-2a footnotes.
    /// </summary>
    public static readonly IReadOnlyList<Cover> Covers =
    [
        // Table 2-2a, urban areas (fully developed, vegetation established)
        new("open space poor", "Open space (lawns, parks, golf courses, cemeteries), poor: grass cover < 50 %", "2-2a", 68, 79, 86, 89),
        new("open space fair", "Open space, fair: grass cover 50 to 75 %", "2-2a", 49, 69, 79, 84),
        new("open space good", "Open space, good: grass cover > 75 %", "2-2a", 39, 61, 74, 80),
        new("impervious", "Impervious areas: paved parking lots, roofs, driveways (excluding right-of-way)", "2-2a", 98, 98, 98, 98),
        new("paved curb", "Streets and roads: paved, curbs and storm sewers (excluding right-of-way)", "2-2a", 98, 98, 98, 98),
        new("paved ditch", "Streets and roads: paved, open ditches (including right-of-way)", "2-2a", 83, 89, 92, 93),
        new("gravel road", "Streets and roads: gravel (including right-of-way)", "2-2a", 76, 85, 89, 91),
        new("dirt road", "Streets and roads: dirt (including right-of-way)", "2-2a", 72, 82, 87, 89),
        new("desert natural", "Western desert urban areas: natural desert landscaping (pervious areas only)", "2-2a", 63, 77, 85, 88),
        new("desert artificial", "Western desert urban areas: artificial desert landscaping (impervious weed barrier, 1–2 in. sand or gravel mulch)", "2-2a", 96, 96, 96, 96),
        new("commercial", "Urban districts: commercial and business (85 % impervious)", "2-2a", 89, 92, 94, 95),
        new("industrial", "Urban districts: industrial (72 % impervious)", "2-2a", 81, 88, 91, 93),
        new("residential 1/8", "Residential, 1/8 ac or less, town houses (65 % impervious)", "2-2a", 77, 85, 90, 92),
        new("residential 1/4", "Residential, 1/4 ac lots (38 % impervious)", "2-2a", 61, 75, 83, 87),
        new("residential 1/3", "Residential, 1/3 ac lots (30 % impervious)", "2-2a", 57, 72, 81, 86),
        new("residential 1/2", "Residential, 1/2 ac lots (25 % impervious)", "2-2a", 54, 70, 80, 85),
        new("residential 1", "Residential, 1 ac lots (20 % impervious)", "2-2a", 51, 68, 79, 84),
        new("residential 2", "Residential, 2 ac lots (12 % impervious)", "2-2a", 46, 65, 77, 82),
        new("newly graded", "Developing urban areas: newly graded areas (pervious areas only, no vegetation)", "2-2a", 77, 86, 91, 94),
        // Table 2-2b, cultivated agricultural lands (SR = straight row, C = contoured)
        new("fallow bare", "Fallow, bare soil", "2-2b", 77, 86, 91, 94),
        new("row crops sr poor", "Row crops, straight row, poor", "2-2b", 72, 81, 88, 91),
        new("row crops sr good", "Row crops, straight row, good", "2-2b", 67, 78, 85, 89),
        new("row crops c poor", "Row crops, contoured, poor", "2-2b", 70, 79, 84, 88),
        new("row crops c good", "Row crops, contoured, good", "2-2b", 65, 75, 82, 86),
        new("small grain sr poor", "Small grain, straight row, poor", "2-2b", 65, 76, 84, 88),
        new("small grain sr good", "Small grain, straight row, good", "2-2b", 63, 75, 83, 87),
        // Table 2-2c, other agricultural lands
        new("pasture poor", "Pasture, grassland or range (continuous forage for grazing), poor", "2-2c", 68, 79, 86, 89),
        new("pasture fair", "Pasture, grassland or range, fair", "2-2c", 49, 69, 79, 84),
        new("pasture good", "Pasture, grassland or range, good", "2-2c", 39, 61, 74, 80),
        new("meadow", "Meadow: continuous grass, protected from grazing, mowed for hay", "2-2c", 30, 58, 71, 78),
        new("brush poor", "Brush (brush–weed–grass mixture, brush the major element), poor", "2-2c", 48, 67, 77, 83),
        new("brush fair", "Brush, fair", "2-2c", 35, 56, 70, 77),
        new("brush good", "Brush, good", "2-2c", 30, 48, 65, 73),
        new("woods grass poor", "Woods–grass combination (orchard or tree farm), poor", "2-2c", 57, 73, 82, 86),
        new("woods grass fair", "Woods–grass combination, fair", "2-2c", 43, 65, 76, 82),
        new("woods grass good", "Woods–grass combination, good", "2-2c", 32, 58, 72, 79),
        new("woods poor", "Woods, poor", "2-2c", 45, 66, 77, 83),
        new("woods fair", "Woods, fair", "2-2c", 36, 60, 73, 79),
        new("woods good", "Woods, good", "2-2c", 30, 55, 70, 77),
        new("farmsteads", "Farmsteads: buildings, lanes, driveways and surrounding lots", "2-2c", 59, 74, 82, 86),
        // Table 2-2d, arid and semiarid rangelands (no group A values except desert shrub)
        new("herbaceous poor", "Arid rangeland: herbaceous (grass, weeds, low brush), poor", "2-2d", X, 80, 87, 93),
        new("herbaceous fair", "Arid rangeland: herbaceous, fair", "2-2d", X, 71, 81, 89),
        new("herbaceous good", "Arid rangeland: herbaceous, good", "2-2d", X, 62, 74, 85),
        new("oak aspen poor", "Arid rangeland: oak–aspen (mountain brush), poor", "2-2d", X, 66, 74, 79),
        new("oak aspen fair", "Arid rangeland: oak–aspen, fair", "2-2d", X, 48, 57, 63),
        new("oak aspen good", "Arid rangeland: oak–aspen, good", "2-2d", X, 30, 41, 48),
        new("pinyon juniper poor", "Arid rangeland: pinyon–juniper, poor", "2-2d", X, 75, 85, 89),
        new("pinyon juniper fair", "Arid rangeland: pinyon–juniper, fair", "2-2d", X, 58, 73, 80),
        new("pinyon juniper good", "Arid rangeland: pinyon–juniper, good", "2-2d", X, 41, 61, 71),
        new("sagebrush poor", "Arid rangeland: sagebrush with grass understory, poor", "2-2d", X, 67, 80, 85),
        new("sagebrush fair", "Arid rangeland: sagebrush, fair", "2-2d", X, 51, 63, 70),
        new("sagebrush good", "Arid rangeland: sagebrush, good", "2-2d", X, 35, 47, 55),
        new("desert shrub poor", "Arid rangeland: desert shrub, poor", "2-2d", 63, 77, 85, 88),
        new("desert shrub fair", "Arid rangeland: desert shrub, fair", "2-2d", 55, 72, 81, 86),
        new("desert shrub good", "Arid rangeland: desert shrub, good", "2-2d", 49, 68, 79, 84),
    ];

    private static string Norm(string s) => string.Join(' ', s.ToLowerInvariant().Replace('-', ' ').Replace('_', ' ').Replace(",", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static Cover? Find(string key) { var k = Norm(key); return Covers.FirstOrDefault(c => c.Key == k); }

    /// <summary>
    /// Composite CN for a pervious cover with impervious area (TR-55 Figure 2-3, connected):
    /// CNc = CNp + (Pimp/100)(98 − CNp); and Figure 2-4 (unconnected, total impervious &lt; 30 %):
    /// CNc = CNp + (Pimp/100)(98 − CNp)(1 − 0.5 R), R = unconnected / total impervious.
    /// </summary>
    public static double CompositeImpervious(double cnPervious, double impPercent, double unconnectedFraction)
    {
        double f = impPercent / 100 * (98 - cnPervious);
        return cnPervious + (impPercent < 30 ? f * (1 - 0.5 * unconnectedFraction) : f);
    }

    /// <summary>Appendix F Table F-1: Ia/P and C0, C1, C2 for log qu = C0 + C1 log Tc + C2 (log Tc)², qu in csm/in, Tc in h.</summary>
    public static readonly IReadOnlyDictionary<string, (double IaP, double C0, double C1, double C2)[]> PeakCoefficients = new Dictionary<string, (double, double, double, double)[]>
    {
        ["I"] =
        [
            (0.10, 2.30550, -0.51429, -0.11750), (0.20, 2.23537, -0.50387, -0.08929), (0.25, 2.18219, -0.48488, -0.06589),
            (0.30, 2.10624, -0.45695, -0.02835), (0.35, 2.00303, -0.40769, 0.01983), (0.40, 1.87733, -0.32274, 0.05754),
            (0.45, 1.76312, -0.15644, 0.00453), (0.50, 1.67889, -0.06930, 0.0),
        ],
        ["IA"] =
        [
            (0.10, 2.03250, -0.31583, -0.13748), (0.20, 1.91978, -0.28215, -0.07020), (0.25, 1.83842, -0.25543, -0.02597),
            (0.30, 1.72657, -0.19826, 0.02633), (0.50, 1.63417, -0.09100, 0.0),
        ],
        ["II"] =
        [
            (0.10, 2.55323, -0.61512, -0.16403), (0.30, 2.46532, -0.62257, -0.11657), (0.35, 2.41896, -0.61594, -0.08820),
            (0.40, 2.36409, -0.59857, -0.05621), (0.45, 2.29238, -0.57005, -0.02281), (0.50, 2.20282, -0.51599, -0.01259),
        ],
        ["III"] =
        [
            (0.10, 2.47317, -0.51848, -0.17083), (0.30, 2.39628, -0.51202, -0.13245), (0.35, 2.35477, -0.49735, -0.11985),
            (0.40, 2.30726, -0.46541, -0.11094), (0.45, 2.24876, -0.41314, -0.11508), (0.50, 2.17772, -0.36803, -0.09525),
        ],
    };

    /// <summary>
    /// Unit peak discharge qu (csm/in) from Exhibit 4 (Table F-1 equation). <paramref name="iaP"/> is clamped to the
    /// table (TR-55: use the limiting value outside 0.1–0.5); between tabulated Ia/P, qu is interpolated linearly.
    /// <paramref name="tcHours"/> must be 0.1–10 h (the caller clamps the low end, as TR-55 directs).
    /// </summary>
    public static double UnitPeak(string type, double tcHours, double iaP)
    {
        var t = PeakCoefficients[type];
        iaP = Math.Clamp(iaP, t[0].IaP, t[^1].IaP);
        double Q((double IaP, double C0, double C1, double C2) c) { double l = Math.Log10(tcHours); return Math.Pow(10, c.C0 + c.C1 * l + c.C2 * l * l); }
        for (int i = 1; i < t.Length; i++)
            if (iaP <= t[i].IaP + 1e-12)
            {
                double f = (iaP - t[i - 1].IaP) / (t[i].IaP - t[i - 1].IaP);
                return Q(t[i - 1]) + f * (Q(t[i]) - Q(t[i - 1]));
            }
        return Q(t[^1]);
    }

    /// <summary>Table 4-2 pond and swamp adjustment factor Fp, linear between the tabulated percentages (0, 0.2, 1, 3, 5 %); NaN above 5 %.</summary>
    public static double PondFactor(double percent)
    {
        (double P, double F)[] t = [(0, 1.00), (0.2, 0.97), (1.0, 0.87), (3.0, 0.75), (5.0, 0.72)];
        if (percent < 0 || percent > 5 + 1e-9) return double.NaN;
        return DesignStorms.Interp(t.Select(x => (x.P, x.F)).ToList(), percent);
    }
}
