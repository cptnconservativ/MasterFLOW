namespace MasterFlow.Application.Field;

/// <summary>
/// NAD83 State Plane Lambert conformal conic zones (Snyder 1987, eqs. 15-1 to 15-11 on the GRS 80 ellipsoid), for
/// placing phone GPS positions on a model drawn in state plane coordinates (D-251). Output in US survey feet or metres.
/// </summary>
public sealed record StatePlaneZone(string Id, string Name, double Phi1, double Phi2, double Phi0, double Lambda0, double FalseEastingM, double FalseNorthingM)
{
    private const double A = 6378137.0, Finv = 298.257222101;
    public const double UsSurveyFoot = 1200.0 / 3937.0;

    public static readonly IReadOnlyList<StatePlaneZone> Zones =
    [
        new("sd-south", "South Dakota South (FIPS 4002)", Deg(42, 50), Deg(44, 24), Deg(42, 20), -Deg(100, 20), 600000, 0),
        new("sd-north", "South Dakota North (FIPS 4001)", Deg(44, 25), Deg(45, 41), Deg(43, 50), -Deg(100, 0), 600000, 0),
    ];

    public static StatePlaneZone? Find(string id) => Zones.FirstOrDefault(z => z.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

    private static double Deg(double d, double m) => d + m / 60;

    /// <summary>Northing and easting (US survey ft when <paramref name="feet"/>, else m) of a latitude/longitude in degrees.</summary>
    public (double Northing, double Easting) Project(double latDeg, double lonDeg, bool feet = true)
    {
        double f = 1 / Finv, e2 = 2 * f - f * f, e = Math.Sqrt(e2);
        double R(double d) => d * Math.PI / 180;
        double M(double p) => Math.Cos(p) / Math.Sqrt(1 - e2 * Math.Sin(p) * Math.Sin(p));
        double T(double p) => Math.Tan(Math.PI / 4 - p / 2) / Math.Pow((1 - e * Math.Sin(p)) / (1 + e * Math.Sin(p)), e / 2);
        double p1 = R(Phi1), p2 = R(Phi2), p0 = R(Phi0);
        double m1 = M(p1), m2 = M(p2), t1 = T(p1), t2 = T(p2), t0 = T(p0);
        double n = (Math.Log(m1) - Math.Log(m2)) / (Math.Log(t1) - Math.Log(t2));
        double F = m1 / (n * Math.Pow(t1, n));
        double rho0 = A * F * Math.Pow(t0, n), rho = A * F * Math.Pow(T(R(latDeg)), n);
        double theta = n * R(lonDeg - Lambda0);
        double x = rho * Math.Sin(theta) + FalseEastingM, y = rho0 - rho * Math.Cos(theta) + FalseNorthingM;
        return feet ? (y / UsSurveyFoot, x / UsSurveyFoot) : (y, x);
    }
}
