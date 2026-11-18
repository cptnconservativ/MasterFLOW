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
    /// <summary>Optional design criteria (lining shear, freeboard, velocity limits) checked against the results.</summary>
    public DesignCriteria? Criteria { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public required UniformFlowInput Input { get; init; }
}

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
}

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
