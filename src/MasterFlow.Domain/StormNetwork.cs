namespace MasterFlow.Domain;

/// <summary>How the water surface at an outfall is set.</summary>
public enum TailwaterRule
{
    /// <summary>Free outfall: the outlet pipe ends at critical depth (or deeper if its own profile requires).</summary>
    FreeOutfall,
    /// <summary>Known tailwater elevation; the outlet depth is never taken below critical depth.</summary>
    Elevation,
    /// <summary>(yc + D) / 2 above the outlet invert (FHWA HEC-22 convention for a free outfall).</summary>
    HalfCriticalPlusDiameter,
    /// <summary>Outlet pipe crown (pipe just full at the outlet).</summary>
    Crown,
}

/// <summary>
/// A storm sewer network for an HGL/EGL computation. All values are in <see cref="Units"/> (ft or m;
/// pipe rise and span in ft or m, not inches); nothing is converted.
/// </summary>
public sealed record StormNetwork
{
    public string Name { get; init; } = "Storm network";
    public UnitSystem Units { get; init; } = UnitSystem.USCustomary;
    public IReadOnlyList<NetworkStructure> Structures { get; init; } = [];
    public IReadOnlyList<NetworkPipe> Pipes { get; init; } = [];
    public TailwaterRule Tailwater { get; init; } = TailwaterRule.HalfCriticalPlusDiameter;
    /// <summary>Tailwater elevation for <see cref="TailwaterRule.Elevation"/>.</summary>
    public double? TailwaterElevation { get; init; }
    /// <summary>K for structures using <see cref="StructureLossMethod.StandardK"/> without their own K (hs = K·Vo²/2g, IDCM Eq. 4-6-1).</summary>
    public double DefaultJunctionK { get; init; } = 0.5;
    /// <summary>Name of the minor (check) storm, e.g. "2-yr".</summary>
    public string MinorStormName { get; init; } = "minor storm";
    public NetworkCriteria Criteria { get; init; } = new();
}

/// <summary>Limits checked at structures and pipes; null = not checked. Units as the network.</summary>
public sealed record NetworkCriteria
{
    /// <summary>The HGL must be at least this far below the rim / ground / street surface (Rapid City IDCM 4.6.7: 1 ft).</summary>
    public double? HglBelowRim { get; init; }
    /// <summary>EGL must be at least this far below the rim (0 = EGL at or below the rim).</summary>
    public double? EglBelowRim { get; init; }
    public double? MinVelocity { get; init; }
    public double? MaxVelocity { get; init; }
    /// <summary>When false, any pipe flowing under pressure (HGL above the crown) is flagged.</summary>
    public bool AllowSurcharge { get; init; } = true;
    /// <summary>Smallest allowed pipe rise (ft or m); null = not checked.</summary>
    public double? MinPipeRise { get; init; }
    /// <summary>Flag pipes steeper than critical slope (IDCM 4.6.6 item 4).</summary>
    public bool FlagSupercritical { get; init; }
    /// <summary>Name of the criteria set shown on reports.</summary>
    public string Source { get; init; } = "";

    /// <summary>
    /// City of Rapid City Infrastructure Design Criteria Manual (January 2012), §4.6: HGL not closer than
    /// 1 ft to the ground or street surface (4.6.7); maximum 20 ft/s in collectors and mains (Table 4-13);
    /// minimum mean velocity 2.5 ft/s for the 2-year storm (4.6.2 3a); minimum diameter 12 in (4.6.2 2);
    /// open-channel design, so pressure flow is flagged (4.6.1); slope below critical (4.6.6 item 4).
    /// U.S. customary values.
    /// </summary>
    public static NetworkCriteria RapidCity2012 { get; } = new()
    {
        HglBelowRim = 1.0, MaxVelocity = 20, MinVelocity = 2.5, AllowSurcharge = false, MinPipeRise = 1.0, FlagSupercritical = true,
        Source = "City of Rapid City Infrastructure Design Criteria Manual (Jan. 2012) §4.6",
    };
}

/// <summary>
/// Head loss at a structure. The case methods are Rapid City IDCM (2012) Figures 4-6 and 4-7 (source: City of
/// Waco storm drainage criteria): hj = V2²/2g − c·V1²/2g, where V2 is the outlet pipe velocity and V1 the
/// velocity of the main inflow pipe; hj is the rise of the hydraulic grade line across the structure.
/// </summary>
public enum StructureLossMethod
{
    /// <summary>No inflow: beginning of line (Case VI); one inflow: Case I (c = 0.5); several: Case II for inlets, Case IV for manholes (c = 0.25).</summary>
    Auto,
    /// <summary>Case VI, inlet or manhole at beginning of line: hj = 1.25·V2²/2g.</summary>
    BeginningOfLine,
    /// <summary>Case I, inlet on main line: c = 0.5.</summary>
    InletOnMainLine,
    /// <summary>Case II, inlet on main line with branch lateral: c = 0.25.</summary>
    InletWithBranchLateral,
    /// <summary>Case III, manhole on main line with 45° branch lateral: c = 0.5.</summary>
    ManholeWith45Lateral,
    /// <summary>Case IV, manhole on main line with 90° branch lateral: c = 0.25.</summary>
    ManholeWith90Lateral,
    /// <summary>Note to Cases III/IV, 60° lateral: c = 0.35.</summary>
    ManholeWith60Lateral,
    /// <summary>Note to Cases III/IV, 22½° lateral: c = 0.75.</summary>
    ManholeWith22Lateral,
    /// <summary>Case V, 45° wye connection or cut-in: c = 0.75.</summary>
    Wye45,
    /// <summary>Case VIII, bend with radius = pipe diameter, 90°: hj = 0.50·V2²/2g.</summary>
    Bend90,
    /// <summary>Case VIII, 60° bend: 0.43·V2²/2g.</summary>
    Bend60,
    /// <summary>Case VIII, 45° bend: 0.35·V2²/2g.</summary>
    Bend45,
    /// <summary>Case VIII, 22½° bend: 0.20·V2²/2g.</summary>
    Bend22,
    /// <summary>Standard method, IDCM Eq. 4-6-1: hs = K·Vo²/2g with a user-entered K.</summary>
    StandardK,
}

public sealed record NetworkStructure
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    /// <summary>Rim, grate or gutter elevation the HGL/EGL is checked against.</summary>
    public double? RimElevation { get; init; }
    /// <summary>How the head loss at this structure is computed (default: chosen from the pipe layout).</summary>
    public StructureLossMethod LossMethod { get; init; } = StructureLossMethod.Auto;
    /// <summary>K for <see cref="StructureLossMethod.StandardK"/>; null uses the network default.</summary>
    public double? JunctionK { get; init; }
    /// <summary>Flow entering the network at this structure (e.g. inlet captured flow). Pipes with no typed Q carry the sum of the inflows upstream.</summary>
    public double? Inflow { get; init; }
    /// <summary>Inflow for the minor (check) storm, e.g. the 2-year storm for Rapid City's minimum velocity.</summary>
    public double? MinorInflow { get; init; }
    public double? Northing { get; init; }
    public double? Easting { get; init; }
}

public enum NetworkPipeShape { Circular, Elliptical }

public sealed record NetworkPipe
{
    public string Name { get; init; } = "";
    /// <summary>Upstream structure.</summary>
    public string From { get; init; } = "";
    /// <summary>Downstream structure.</summary>
    public string To { get; init; } = "";
    public NetworkPipeShape Shape { get; init; } = NetworkPipeShape.Circular;
    /// <summary>Diameter or rise (ft or m).</summary>
    public double Rise { get; init; }
    /// <summary>Span for elliptical pipes (ft or m); equals rise for circular.</summary>
    public double Span { get; init; }
    public double Length { get; init; }
    public double UpstreamInvert { get; init; }
    public double DownstreamInvert { get; init; }
    public double ManningN { get; init; } = 0.013;
    /// <summary>Design discharge in the pipe (cfs or m³/s); NaN (blank) = accumulate from structure inflows upstream.</summary>
    public double Discharge { get; init; }
    /// <summary>Minor-storm (check) discharge used only for the minimum-velocity check; NaN = accumulate from minor inflows, or none.</summary>
    public double MinorDischarge { get; init; } = double.NaN;
}
