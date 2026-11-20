namespace MasterFlow.Domain;

/// <summary>Project-level information that appears on reports.</summary>
public sealed record ProjectInfo
{
    public string Name { get; init; } = "Untitled project";
    public string ProjectNumber { get; init; } = "";
    public string Client { get; init; } = "";
    public string PreparedBy { get; init; } = "";
    public string CheckedBy { get; init; } = "";
    public string Notes { get; init; } = "";
}

/// <summary>One named calculation inside a project.</summary>
public sealed record ChannelCalculation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "New channel";
    public string Description { get; init; } = "";
    /// <summary>Folder path in the project tree, "/"-separated (e.g. "Ditches/Phase 1"); empty = top level.</summary>
    public string Folder { get; init; } = "";
    /// <summary>Included in batch reports, the comparison and the Excel workbook (additive; older files load as true).</summary>
    public bool IncludeInBatch { get; init; } = true;
    /// <summary>Optional item number shown on reports (e.g. a drainage-structure or sheet reference).</summary>
    public string ItemNumber { get; init; } = "";
    /// <summary>Saved rating-table range; when set, reports and the workbook include this calculation's rating table.</summary>
    public RatingRange? Rating { get; init; }
    /// <summary>When true, reports and the workbook include a sensitivity table (n and slope varied) for this calculation.</summary>
    public bool Sensitivity { get; init; }
    /// <summary>Optional design criteria (lining shear, freeboard, velocity limits) checked against the results.</summary>
    public DesignCriteria? Criteria { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>How the slope is typed and shown for this calculation (the stored slope is always ft/ft or m/m).</summary>
    public SlopeUnit SlopeUnit { get; init; } = SlopeUnit.Percent;
    public required UniformFlowInput Input { get; init; }
}

/// <summary>Unit for typing a slope: percent (0.16 = 0.16 %) or rise over run (0.0016 ft/ft).</summary>
public enum SlopeUnit { Percent, RiseOverRun }

/// <summary>
/// Engineer-entered design limits, in the calculation's unit system (no conversion). A null limit is not
/// checked. MasterFlow supplies no permissible-shear values: the engineer enters the value for the lining
/// from the governing manual (e.g. FHWA HEC-15 or the agency's drainage manual).
/// </summary>
public sealed record DesignCriteria
{
    /// <summary>Lining description shown on the report (e.g. "Riprap D50 = 6 in").</summary>
    public string Lining { get; init; } = "";
    /// <summary>Permissible shear τp (lb/ft² or N/m²).</summary>
    public double? PermissibleShear { get; init; }
    /// <summary>Required freeboard above the normal water surface (ft or m).</summary>
    public double? RequiredFreeboard { get; init; }
    /// <summary>Minimum velocity, e.g. self-cleansing (ft/s or m/s).</summary>
    public double? MinVelocity { get; init; }
    /// <summary>Maximum velocity for the lining (ft/s or m/s).</summary>
    public double? MaxVelocity { get; init; }

    public bool IsEmpty => Lining.Length == 0 && PermissibleShear is null && RequiredFreeboard is null && MinVelocity is null && MaxVelocity is null;
}

/// <summary>
/// A MasterFlow project: a local, self-contained collection of calculations. Persisted as JSON with
/// an explicit schema version (see MasterFlow.Persistence).
/// </summary>
public sealed record Project
{
    public ProjectInfo Info { get; init; } = new();
    public IReadOnlyList<ChannelCalculation> Calculations { get; init; } = [];

    /// <summary>Deleted calculations, kept with the project until the recycle bin is emptied.</summary>
    public IReadOnlyList<ChannelCalculation> RecycleBin { get; init; } = [];

    public ReportSettings Report { get; init; } = new();

    /// <summary>Drainage structures (inlets, manholes, outlets) imported from a pipe network; listed and exported, not solved.</summary>
    public IReadOnlyList<DrainageStructure> Structures { get; init; } = [];

    /// <summary>Storm sewer networks for HGL/EGL computation (additive; older files have none).</summary>
    public IReadOnlyList<StormNetwork> Networks { get; init; } = [];

    /// <summary>Last input of each design tool, by tool id (additive; older files have none).</summary>
    public IReadOnlyDictionary<string, string> ToolInputs { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// A drainage structure as read from a LandXML pipe network, in the file's units. Coordinates are
/// northing/easting as written in the file; null where the file gives no value.
/// </summary>
public sealed record DrainageStructure
{
    public string Network { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Inlet, Outlet, Manhole, Structure or Connection, from the LandXML element type.</summary>
    public string Kind { get; init; } = "";
    public string Description { get; init; } = "";
    public double? Northing { get; init; }
    public double? Easting { get; init; }
    public double? RimElevation { get; init; }
    public double? SumpElevation { get; init; }
    public IReadOnlyList<StructureInvert> Inverts { get; init; } = [];
    /// <summary>Station and offset (right positive) on the nearest imported alignment, when one was imported.</summary>
    public string? Alignment { get; init; }
    public double? Station { get; init; }
    public double? Offset { get; init; }
}

/// <summary>One pipe connection at a structure: pipe name, "in" or "out", and invert elevation.</summary>
public sealed record StructureInvert(string Pipe, string FlowDirection, double Elevation);

/// <summary>Report appearance saved with the project.</summary>
public sealed record ReportSettings
{
    public string Watermark { get; init; } = "";
    public string FooterText { get; init; } = "";
    /// <summary>Company logo as a data: URI (PNG/JPEG/SVG), embedded so the project file stays self-contained.</summary>
    public string? LogoDataUri { get; init; }
    public bool IncludeCalculationTrace { get; init; } = true;
}

/// <summary>Direction of a rating table.</summary>
public enum RatingDirection
{
    /// <summary>Rows at fixed depths; discharge computed.</summary>
    DischargeFromDepth,
    /// <summary>Rows at fixed discharges; normal depth solved.</summary>
    DepthFromDischarge,
}

/// <summary>A saved rating-table range (values in the calculation's units).</summary>
public sealed record RatingRange(RatingDirection Direction, double Start, double End, double Step);
