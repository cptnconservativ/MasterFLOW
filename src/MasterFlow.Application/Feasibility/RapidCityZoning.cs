namespace MasterFlow.Application.Feasibility;

/// <summary>What a district is built as, which sets the developed cover and how yield is counted.</summary>
public enum ZoningUse { Residential, Commercial, Industrial, Agricultural }

/// <summary>
/// Dimensional standards of a Rapid City zoning district (Rapid City Municipal Code Title 17). Lot areas in ft²;
/// <see cref="MinLotNoSewer"/> applies where no public sanitary sewer is available. For multi-family, a lot needs
/// <see cref="MultiBase"/> + <see cref="MultiPerUnit"/> ft² per dwelling unit. Null means the code sets no value
/// (or it was not confirmed, see <see cref="Verify"/>).
/// </summary>
public sealed record ZoningDistrict(
    string Code, string Name, string Section, ZoningUse Use,
    double? MinLot, double? MinLotNoSewer, double? MinWidth, double MaxCoverage,
    double Front, double Side, double Rear, double MaxHeight,
    double? MultiBase = null, double? MultiPerUnit = null, string? Verify = null);

/// <summary>
/// Rapid City Title 17 dimensional standards as published (Zoneomics mirror of the code through Ord. 6723, read
/// 2026-11-21; D-202). The zoning map itself is not published as a GIS service, so the district is entered.
/// Check the current code before relying on a value; entries the mirror did not show in full carry a Verify note.
/// </summary>
public static class RapidCityZoning
{
    private const double Acre = SiteGeometry.SqFtPerAcre;

    public static readonly IReadOnlyList<ZoningDistrict> Districts =
    [
        new("GAD", "General Agricultural District", "17.34.040", ZoningUse.Agricultural, 20 * Acre, 20 * Acre, 65, 0.25, 25, 8, 25, 35),
        new("PF", "Park Forest District", "17.08.040", ZoningUse.Residential, 3 * Acre, 3 * Acre, 100, 0.25, 25, 30, 50, 35),
        new("LDR-1", "Low Density Residential District", "17.10.050", ZoningUse.Residential, 6500, Acre, 50, 0.30, 20, 8, 25, 35),
        new("LDR-2", "Low Density Residential II District", "17.44.050", ZoningUse.Residential, 6500, Acre, 50, 0.30, 20, 8, 25, 35),
        new("MDR", "Medium Density Residential District", "17.12.050", ZoningUse.Residential, 6500, Acre, 50, 0.30, 20, 8, 25, 40, 5000, 1500),
        new("HDR", "High Density Residential District", "17.14.040", ZoningUse.Residential, 6500, Acre, 50, 0.30, 20, 8, 25, 65, 6000, 1500,
            "Multi-family side yards (12 or 16 ft) were not clear in the published text."),
        new("GC", "General Commercial District", "17.18.050", ZoningUse.Commercial, null, null, null, 0.75, 25, 0, 0, 45,
            Verify: "Minimum lot area and width were not confirmed; coverage, front yard and height are from a summary of the code."),
        new("LI", "Light Industrial District", "17.22.040", ZoningUse.Industrial, 20000, 20000, null, 0.75, 25, 25, 25, 45),
    ];

    public static ZoningDistrict? Find(string code) =>
        Districts.FirstOrDefault(d => string.Equals(d.Code, code.Trim(), StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(d.Code.Replace("-1", ""), code.Trim(), StringComparison.OrdinalIgnoreCase));
}
