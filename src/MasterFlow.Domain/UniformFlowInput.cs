namespace MasterFlow.Domain;

/// <summary>The unknown solved for in a steady uniform-flow (Manning) calculation.</summary>
public enum SolveFor
{
    Discharge,
    NormalDepth,
    Slope,
    Roughness,
}

/// <summary>
/// Inputs to a steady, uniform, open-channel flow calculation (Manning's equation). The value of the
/// field named by <see cref="SolveFor"/> is ignored. Slope is a dimensionless gradient (ft/ft or m/m),
/// not a percentage.
/// </summary>
public sealed record UniformFlowInput
{
    public required ChannelSection Section { get; init; }
    public UnitSystem Units { get; init; } = UnitSystem.USCustomary;
    public SolveFor SolveFor { get; init; } = SolveFor.Discharge;

    /// <summary>Manning's roughness coefficient n (s/m^(1/3) by convention; the same number is used in both unit systems).</summary>
    public double ManningN { get; init; }

    /// <summary>Longitudinal channel slope S (length/length).</summary>
    public double Slope { get; init; }

    /// <summary>Discharge Q in the unit system's discharge unit.</summary>
    public double Discharge { get; init; }

    /// <summary>Flow depth y measured vertically from the channel invert.</summary>
    public double Depth { get; init; }
}
