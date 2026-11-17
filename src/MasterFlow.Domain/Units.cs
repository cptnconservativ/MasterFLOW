namespace MasterFlow.Domain;

/// <summary>
/// The consistent unit system a calculation is performed in. The hydraulic engine never converts
/// between systems: every length, area, discharge and velocity in a calculation is expressed in the
/// base units of the selected system. Display-only conversions (for example inches for pipe
/// diameters) are handled by <see cref="UnitFormat"/> at the presentation layer.
/// </summary>
public enum UnitSystem
{
    /// <summary>U.S. customary: feet, seconds, cubic feet per second. Manning k = 1.486.</summary>
    USCustomary = 0,

    /// <summary>SI: metres, seconds, cubic metres per second. Manning k = 1.0.</summary>
    SI = 1,
}

/// <summary>Physical constants and unit labels for a <see cref="UnitSystem"/>.</summary>
public sealed record UnitConstants(
    UnitSystem System,
    double ManningK,
    double Gravity,
    string Length,
    string Area,
    string Discharge,
    string Velocity,
    string Conveyance)
{
    /// <summary>
    /// U.S. customary constants. k = 1.486 is the conventional value printed in U.S. references
    /// (Chow 1959; FHWA HDS-4). The exact conversion (1 m / 0.3048 m)^(1/3) = 1.48592 differs by 0.005 %.
    /// g = 32.174 ft/s² (standard gravity 9.80665 m/s² converted exactly and rounded).
    /// </summary>
    public static readonly UnitConstants USCustomary =
        new(UnitSystem.USCustomary, 1.486, 32.174, "ft", "ft²", "cfs", "ft/s", "cfs");

    /// <summary>SI constants. k = 1.0 exactly; g = 9.80665 m/s² (standard gravity).</summary>
    public static readonly UnitConstants SI =
        new(UnitSystem.SI, 1.0, 9.80665, "m", "m²", "m³/s", "m/s", "m³/s");

    public static UnitConstants For(UnitSystem system) => system switch
    {
        UnitSystem.USCustomary => USCustomary,
        UnitSystem.SI => SI,
        _ => throw new ArgumentOutOfRangeException(nameof(system), system, "Unknown unit system."),
    };
}

/// <summary>Display helpers. These never feed values back into the engine.</summary>
public static class UnitFormat
{
    public static string Slope(double slopeFtPerFt, int decimals = 4) =>
        (slopeFtPerFt * 100.0).ToString("F" + Math.Max(0, decimals - 2), System.Globalization.CultureInfo.InvariantCulture) + " %";

    public static string Number(double value, int decimals) =>
        double.IsFinite(value)
            ? value.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture)
            : "—";
}
