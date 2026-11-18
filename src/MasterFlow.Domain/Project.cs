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
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public required UniformFlowInput Input { get; init; }
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
