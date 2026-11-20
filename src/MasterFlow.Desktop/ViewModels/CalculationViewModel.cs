using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Desktop.ViewModels;

/// <summary>A message row shown in the UI.</summary>
public sealed record MessageItem(string Severity, string Code, string Text)
{
    public string Display => $"{Severity}: {Text}";
}

/// <summary>Snapshot of every editable field, used for undo/redo.</summary>
public sealed record FieldState(
    string Title, string Description, SectionShape Shape, UnitSystem Units, SolveFor SolveFor,
    string BottomWidth, string LeftSlope, string RightSlope, string ChannelDepth, string Diameter,
    bool EllipseStandard, EllipticalPipeSize EllipseSize, bool EllipseVertical, string EllipseRise, string EllipseSpan,
    string ManningN, string SlopePercent, string Discharge, string Depth,
    string PointsText = "", CompositeRoughnessMethod RoughnessMethod = CompositeRoughnessMethod.Conveyance, string InvertElevation = "", string Folder = "",
    string Lining = "", string PermissibleShear = "", string RequiredFreeboard = "", string MinVelocity = "", string MaxVelocity = "");

/// <summary>
/// Editable state of one calculation. Fields are held as text so that partially typed or invalid
/// entries are preserved and flagged rather than silently replaced. Recalculation happens immediately
/// after each edit; when any input is invalid, no numeric results are displayed.
/// </summary>
public sealed partial class CalculationViewModel : ObservableObject
{
    private static readonly string[] InputProps =
    [
        nameof(Title), nameof(Description), nameof(Shape), nameof(Units), nameof(SolveFor), nameof(BottomWidth), nameof(LeftSlope),
        nameof(RightSlope), nameof(ChannelDepth), nameof(Diameter), nameof(EllipseStandard), nameof(EllipseSize), nameof(EllipseVertical), nameof(EllipseRise), nameof(EllipseSpan), nameof(ManningN), nameof(SlopePercent), nameof(Discharge), nameof(Depth),
        nameof(PointsText), nameof(RoughnessMethod), nameof(InvertElevation), nameof(Folder),
        nameof(Lining), nameof(PermissibleShear), nameof(RequiredFreeboard), nameof(MinVelocity), nameof(MaxVelocity),
    ];

    private readonly Stack<FieldState> _undo = new();
    private readonly Stack<FieldState> _redo = new();
    private FieldState _committed;
    private string? _lastEditedProp;
    private DateTime _lastEditTime;
    private bool _restoring;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; private set; } = DateTimeOffset.UtcNow;

    [ObservableProperty] private string _title = "New channel";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private SectionShape _shape = SectionShape.Trapezoidal;
    [ObservableProperty] private UnitSystem _units = UnitSystem.USCustomary;
    [ObservableProperty] private SolveFor _solveFor = SolveFor.NormalDepth;
    [ObservableProperty] private string _bottomWidth = "10";
    [ObservableProperty] private string _leftSlope = "2";
    [ObservableProperty] private string _rightSlope = "2";
    [ObservableProperty] private string _channelDepth = "";
    [ObservableProperty] private string _diameter = "24";
    [ObservableProperty] private bool _ellipseStandard = true;
    [ObservableProperty] private EllipticalPipeSize _ellipseSize = EllipticalPipeCatalog.Sizes.Single(s => s.EquivalentRound == 36);
    [ObservableProperty] private bool _ellipseVertical;
    [ObservableProperty] private string _ellipseRise = "29";
    [ObservableProperty] private string _ellipseSpan = "45";
    [ObservableProperty] private string _pointsText = "0\t5\n10\t0\n20\t0\n30\t5";
    [ObservableProperty] private CompositeRoughnessMethod _roughnessMethod = CompositeRoughnessMethod.Conveyance;
    [ObservableProperty] private string _invertElevation = "";
    [ObservableProperty] private string _folder = "";

    // Design criteria (blank = not checked); values in the calculation's units.
    [ObservableProperty] private string _lining = "";
    [ObservableProperty] private string _permissibleShear = "";
    [ObservableProperty] private string _requiredFreeboard = "";
    [ObservableProperty] private string _minVelocity = "";
    [ObservableProperty] private string _maxVelocity = "";
    [ObservableProperty] private IReadOnlyList<DesignCheck> _checks = [];
    public bool HasChecks => Checks.Count > 0;
    partial void OnChecksChanged(IReadOnlyList<DesignCheck> value) => OnPropertyChanged(nameof(HasChecks));

    /// <summary>Selection for batch outputs; not an input, so it is not part of undo.</summary>
    [ObservableProperty] private bool _includeInBatch = true;

    /// <summary>Item number (reports, {item} title field); not an undoable input.</summary>
    [ObservableProperty] private string _itemNumber = "";
    partial void OnItemNumberChanged(string value)
    {
        if (_restoring) return;
        OnPropertyChanged(nameof(DisplayTitle)); OnPropertyChanged(nameof(ListLabel)); OnPropertyChanged(nameof(ListDetail));
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saved rating-table range; set when a rating table is generated for this calculation.</summary>
    [ObservableProperty] private RatingRange? _rating;
    partial void OnRatingChanged(RatingRange? value) { if (!_restoring) Edited?.Invoke(this, EventArgs.Empty); }

    /// <summary>When true, the report and the workbook include a sensitivity table for this calculation.</summary>
    [ObservableProperty] private bool _sensitivity;
    partial void OnSensitivityChanged(bool value) { if (!_restoring) Edited?.Invoke(this, EventArgs.Empty); }

    /// <summary>Sensitivity of the current result to n and slope (null without a successful result).</summary>
    public SensitivityTable? BuildSensitivity() => Result is { Succeeded: true } r ? SensitivityAnalysis.Build(r.Input) : null;

    /// <summary>This calculation's rating table from its saved range and current inputs (null when none is saved or there is no result).</summary>
    public RatingTable? BuildRatingTable() => Rating is { } rr && Result is { Succeeded: true } r ? RatingTableBuilder.Build(r.Input, rr) : null;
    partial void OnIncludeInBatchChanged(bool value) { if (!_restoring) Edited?.Invoke(this, EventArgs.Empty); }

    public static IReadOnlyList<RoughnessEntry> RoughnessEntries { get; } = ManningRoughnessTable.Entries;

    /// <summary>Picking a reference entry copies its normal n into the n field (an ordinary, undoable edit).</summary>
    public RoughnessEntry? RoughnessPick
    {
        get => null;
        set
        {
            if (value is null) return;
            ManningN = value.Normal.ToString("0.0###", CultureInfo.CurrentCulture);
            OnPropertyChanged();
        }
    }
    [ObservableProperty] private string _manningN = "0.013";
    [ObservableProperty] private string _slopePercent = "1";
    [ObservableProperty] private string _discharge = "100";
    [ObservableProperty] private string _depth = "1.5";

    [ObservableProperty] private UniformFlowResult? _result;
    [ObservableProperty] private IReadOnlyList<ResultLine> _resultLines = [];
    [ObservableProperty] private IReadOnlyList<MessageItem> _messages = [];
    /// <summary>The method choice that changes the answer (ellipse shape, conveyance method), shown with the results.</summary>
    [ObservableProperty] private string? _methodText;
    public bool HasMethodText => MethodText is not null;
    partial void OnMethodTextChanged(string? value) => OnPropertyChanged(nameof(HasMethodText));
    [ObservableProperty] private IReadOnlyList<string> _trace = [];
    [ObservableProperty] private CrossSectionDrawing? _drawing;
    [ObservableProperty] private ChannelSection? _currentSection;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasResult;

    public event EventHandler? Edited;

    public string StressUnit => Constants.Stress;
    public string VelocityUnit => Constants.Velocity;

    public bool CanConvertToIrregular => Shape is SectionShape.Rectangular or SectionShape.Trapezoidal or SectionShape.Triangular;

    /// <summary>Makes the current inputs the starting point: no undo history (used after building a calculation in code).</summary>
    public void ClearHistory()
    {
        _undo.Clear(); _redo.Clear(); _lastEditedProp = null;
        _committed = Capture();
        OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo));
    }

    public CalculationViewModel()
    {
        _committed = Capture();
        PropertyChanged += OnAnyPropertyChanged;
        Recalculate();
    }

    // ------------------------------------------------------------ presentation helpers

    public static IReadOnlyList<SectionShape> Shapes { get; } = [SectionShape.Rectangular, SectionShape.Trapezoidal, SectionShape.Triangular, SectionShape.Circular, SectionShape.Elliptical, SectionShape.Irregular];
    public static IReadOnlyList<CompositeRoughnessMethod> RoughnessMethods { get; } = [CompositeRoughnessMethod.Conveyance, CompositeRoughnessMethod.EqualVelocity, CompositeRoughnessMethod.PerSegment];
    public static IReadOnlyList<EllipticalPipeSize> EllipseSizes { get; } = EllipticalPipeCatalog.Sizes;
    public static IReadOnlyList<UnitSystem> UnitSystems { get; } = [UnitSystem.USCustomary, UnitSystem.SI];
    public static IReadOnlyList<SolveFor> SolveModes { get; } = [SolveFor.Discharge, SolveFor.NormalDepth, SolveFor.Slope, SolveFor.Roughness];

    public UnitConstants Constants => UnitConstants.For(Units);
    public string LengthUnit => Constants.Length;
    public string DischargeUnit => Constants.Discharge;
    public bool IsPipe => Shape == SectionShape.Circular;
    public bool IsEllipse => Shape == SectionShape.Elliptical;
    public bool ShowEllipseStandard => IsEllipse && EllipseStandard;
    public bool ShowEllipseCustom => IsEllipse && !EllipseStandard;
    public bool ShowBottomWidth => Shape is SectionShape.Rectangular or SectionShape.Trapezoidal;
    public bool ShowSideSlopes => Shape is SectionShape.Trapezoidal or SectionShape.Triangular;
    public bool IsIrregular => Shape == SectionShape.Irregular;
    public bool ShowChannelDepth => !IsPipe && !IsEllipse && !IsIrregular;
    public bool ShowInvertElevation => !IsIrregular;
    /// <summary>Pipe diameters are entered in inches (U.S.) or millimetres (SI) and converted once, here, to the base length unit.</summary>
    public string DiameterUnit => Units == UnitSystem.USCustomary ? "in" : "mm";
    private double DiameterToBase => Units == UnitSystem.USCustomary ? 1.0 / 12.0 : 1.0 / 1000.0;
    public bool DischargeIsInput => SolveFor != SolveFor.Discharge;
    public bool DepthIsInput => SolveFor != SolveFor.NormalDepth;
    public bool SlopeIsInput => SolveFor != SolveFor.Slope;
    public bool RoughnessIsInput => SolveFor != SolveFor.Roughness;
    public string ListLabel => string.IsNullOrWhiteSpace(Title) ? "(untitled)" : DisplayTitle;

    /// <summary>The title with any {field} expressions replaced by live values (see <see cref="TitleExpression"/>).</summary>
    public string DisplayTitle => TitleExpression.Evaluate(Title, Result, NormaliseFolder(Folder), item: ItemNumber.Trim());
    public bool TitleHasFields => TitleExpression.HasFields(Title);
    public static string TitleFieldHelp { get; } = "Title fields: " + string.Join(", ", TitleExpression.Fields.Select(f => "{" + f.Name + "}")) +
        ". Add a format after a colon, e.g. {Q:F1}. Use {{ and }} for literal braces.";
    public string ListDetail => (string.IsNullOrWhiteSpace(ItemNumber) ? "" : "No. " + ItemNumber.Trim() + " · ") + (string.IsNullOrWhiteSpace(Folder) ? "" : NormaliseFolder(Folder) + " · ") + (HasResult && Result is { } r
        ? $"{Shape} · Q {r.Discharge.ToString("F2", CultureInfo.CurrentCulture)} {DischargeUnit} · yn {r.Normal.Depth.ToString("F3", CultureInfo.CurrentCulture)} {LengthUnit}"
        : $"{Shape} · check inputs");

    /// <summary>The solved value, formatted, shown in place of the disabled input field.</summary>
    public string SolvedValueText => Result is { Succeeded: true } r
        ? SolveFor switch
        {
            SolveFor.Discharge => $"{r.Discharge.ToString("F3", CultureInfo.CurrentCulture)}  (solved)",
            SolveFor.NormalDepth => $"{r.Normal.Depth.ToString("F3", CultureInfo.CurrentCulture)}  (solved)",
            SolveFor.Slope => $"{(r.Slope * 100).ToString("F4", CultureInfo.CurrentCulture)}  (solved)",
            _ => $"{r.ManningN.ToString("F4", CultureInfo.CurrentCulture)}  (solved)",
        }
        : "—  (no result)";

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // ------------------------------------------------------------ model mapping

    public static CalculationViewModel FromModel(ChannelCalculation c)
    {
        var vm = new CalculationViewModel();
        vm._restoring = true;
        var i = c.Input;
        vm.Id = c.Id;
        vm.CreatedUtc = c.CreatedUtc;
        vm.Title = c.Title;
        vm.Description = c.Description;
        vm.Folder = c.Folder;
        vm.IncludeInBatch = c.IncludeInBatch;
        vm.ItemNumber = c.ItemNumber;
        vm.Rating = c.Rating;
        vm.Sensitivity = c.Sensitivity;
        if (c.Criteria is { } dc)
        {
            vm.Lining = dc.Lining;
            vm.PermissibleShear = dc.PermissibleShear is { } a ? N(a) : "";
            vm.RequiredFreeboard = dc.RequiredFreeboard is { } b ? N(b) : "";
            vm.MinVelocity = dc.MinVelocity is { } e ? N(e) : "";
            vm.MaxVelocity = dc.MaxVelocity is { } f ? N(f) : "";
        }
        vm.Units = i.Units;
        vm.SolveFor = i.SolveFor;
        vm.Shape = i.Section.Shape;
        switch (i.Section)
        {
            case RectangularSection r: vm.BottomWidth = N(r.BottomWidth); vm.LeftSlope = "0"; vm.RightSlope = "0"; break;
            case TrapezoidalSection t: vm.BottomWidth = N(t.BottomWidth); vm.LeftSlope = N(t.LeftSideSlope); vm.RightSlope = N(t.RightSideSlope); break;
            case TriangularSection v: vm.BottomWidth = "0"; vm.LeftSlope = N(v.LeftSideSlope); vm.RightSlope = N(v.RightSideSlope); break;
            case IrregularSection ir:
                vm.PointsText = StationPointText.Format(ir.Points);
                vm.RoughnessMethod = ir.RoughnessMethod;
                break;
            case CircularSection p: vm.Diameter = N(p.Diameter / vm.DiameterToBase); break;
            case EllipticalSection e:
            {
                double k = vm.Units == UnitSystem.USCustomary ? 1.0 / 12.0 : 0.0254;
                var match = e.EndRadius is { } r
                    ? EllipticalPipeCatalog.Sizes.FirstOrDefault(z => Math.Abs(z.R1In * k - r) < 1e-9 && (Math.Abs(z.RiseIn * k - Math.Min(e.Rise, e.Span)) < 1e-9))
                    : null;
                if (match is not null)
                {
                    vm.EllipseStandard = true; vm.EllipseSize = match; vm.EllipseVertical = !e.IsHorizontal;
                }
                else
                {
                    vm.EllipseStandard = false; // custom shapes (or unrecognised radii) are edited as rise/span
                }
                vm.EllipseRise = N(e.Rise / vm.DiameterToBase);
                vm.EllipseSpan = N(e.Span / vm.DiameterToBase);
                break;
            }
        }
        vm.ChannelDepth = i.Section.ChannelDepth is { } cd ? N(cd) : "";
        vm.InvertElevation = i.InvertElevation is { } ie ? N(ie) : "";
        vm.ManningN = N(i.ManningN);
        vm.SlopePercent = N(i.Slope * 100.0);
        vm.Discharge = N(i.Discharge);
        vm.Depth = N(i.Depth);
        vm._restoring = false;
        vm._committed = vm.Capture();
        vm.Recalculate();
        return vm;
    }

    /// <summary>Converts to the domain model. Unparseable fields are stored as NaN so the saved file round-trips the invalid state visibly.</summary>
    public ChannelCalculation ToModel() => new()
    {
        Id = Id,
        CreatedUtc = CreatedUtc,
        Title = Title,
        Description = Description,
        Folder = NormaliseFolder(Folder),
        IncludeInBatch = IncludeInBatch,
        ItemNumber = ItemNumber.Trim(),
        Rating = Rating,
        Sensitivity = Sensitivity,
        Criteria = BuildCriteria(out _),
        Input = BuildInput(out _),
    };

    /// <summary>Design criteria from the text fields; null when all are blank. Unreadable limits are reported and not checked.</summary>
    public DesignCriteria? BuildCriteria(out List<CalcMessage> errors)
    {
        var errs = new List<CalcMessage>();
        double? L(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (TryParse(text, out double v) && v >= 0) return v;
            errs.Add(new CalcMessage(MessageSeverity.Warning, "W-C-000", $"{label} \"{text}\" is not a number of zero or more; it is not checked."));
            return null;
        }
        var d = new DesignCriteria
        {
            Lining = Lining.Trim(),
            PermissibleShear = L(PermissibleShear, "Permissible shear"),
            RequiredFreeboard = L(RequiredFreeboard, "Required freeboard"),
            MinVelocity = L(MinVelocity, "Minimum velocity"),
            MaxVelocity = L(MaxVelocity, "Maximum velocity"),
        };
        errors = errs;
        return d.IsEmpty ? null : d;
    }

    /// <summary>Trims each folder level and drops empty levels: " Ditches / /Phase 1 " → "Ditches/Phase 1".</summary>
    public static string NormaliseFolder(string? folder) =>
        string.Join("/", (folder ?? "").Split('/', '\\').Select(p => p.Trim()).Where(p => p.Length > 0));

    private static string N(double v) => double.IsFinite(v) ? v.ToString("G10", CultureInfo.CurrentCulture) : "";

    private UniformFlowInput BuildInput(out List<CalcMessage> parseErrors)
    {
        var errs = new List<CalcMessage>();
        double P(string text, string label, bool required)
        {
            if (!required) return 0;
            if (TryParse(text, out double v)) return v;
            errs.Add(CalcMessage.Error("E-UI-001", $"{label}: \"{text}\" is not a number."));
            return double.NaN;
        }
        double b = P(BottomWidth, "Bottom width", ShowBottomWidth);
        double zl = P(LeftSlope, "Left side slope", ShowSideSlopes);
        double zr = P(RightSlope, "Right side slope", ShowSideSlopes);
        double dia = P(Diameter, "Pipe diameter", IsPipe) * DiameterToBase;
        double? cd = null;
        if (ShowChannelDepth && !string.IsNullOrWhiteSpace(ChannelDepth))
        {
            if (TryParse(ChannelDepth, out double v)) cd = v;
            else errs.Add(CalcMessage.Error("E-UI-001", $"Channel depth: \"{ChannelDepth}\" is not a number (leave blank for none)."));
        }
        ChannelSection section = Shape switch
        {
            SectionShape.Rectangular => new RectangularSection(b),
            SectionShape.Triangular => new TriangularSection(zl, zr),
            SectionShape.Circular => new CircularSection(dia),
            SectionShape.Elliptical when EllipseStandard => EllipticalPipeCatalog.Create(EllipseSize, EllipseVertical, Units),
            SectionShape.Irregular => BuildIrregular(errs),
            SectionShape.Elliptical => new EllipticalSection(P(EllipseRise, "Rise", true) * DiameterToBase, P(EllipseSpan, "Span", true) * DiameterToBase, null, "True ellipse (custom size)"),
            _ => new TrapezoidalSection(b, zl, zr),
        };
        section = section with { ChannelDepth = cd };
        double? invert = null;
        if (ShowInvertElevation && !string.IsNullOrWhiteSpace(InvertElevation))
        {
            if (TryParse(InvertElevation, out double v)) invert = v;
            else errs.Add(CalcMessage.Error("E-UI-001", $"Invert elevation: \"{InvertElevation}\" is not a number (leave blank for none)."));
        }
        var input = new UniformFlowInput
        {
            Section = section,
            Units = Units,
            SolveFor = SolveFor,
            ManningN = P(ManningN, "Manning's n", RoughnessIsInput),
            Slope = P(SlopePercent, "Slope", SlopeIsInput) / 100.0, // displayed as percent; converted here, labelled "%"
            Discharge = P(Discharge, "Discharge", DischargeIsInput),
            Depth = P(Depth, "Flow depth", DepthIsInput),
            InvertElevation = invert,
        };
        parseErrors = errs;
        return input;
    }

    private IrregularSection BuildIrregular(List<CalcMessage> errs)
    {
        var parsed = StationPointText.Parse(PointsText);
        foreach (var e in parsed.Errors) errs.Add(CalcMessage.Error("E-UI-002", "Points: " + e));
        return new IrregularSection(parsed.Points, RoughnessMethod);
    }

    public static bool TryParse(string? text, out double value)
    {
        value = double.NaN;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        return (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
               && double.IsFinite(value);
    }

    // ------------------------------------------------------------ calculation

    public void Recalculate()
    {
        var input = BuildInput(out var parseErrors);
        UniformFlowResult r;
        if (parseErrors.Count > 0)
            r = new UniformFlowResult { Input = input, Constants = UnitConstants.For(Units), Succeeded = false, Messages = parseErrors };
        else
            r = UniformFlowCalculator.Solve(input);

        Result = r;
        HasResult = r.Succeeded;
        ResultLines = ResultSummary.Lines(r);
        MethodText = ResultSummary.MethodText(input);
        var criteria = BuildCriteria(out var criteriaErrors);
        Checks = DesignChecks.Evaluate(criteria, r);
        Messages = r.Messages.Concat(criteriaErrors).Concat(DesignChecks.Messages(Checks)).Select(m => new MessageItem(m.Severity.ToString(), m.Code, m.Text)).ToList();
        Trace = r.Trace;
        CurrentSection = input.Section;
        bool geometryOk = parseErrors.Count == 0 && !r.Messages.Any(m => m.Code == MessageCodes.GeometryInvalid);
        Drawing = geometryOk ? CrossSectionDrawing.Create(input.Section, r.Succeeded ? r.Normal.Depth : null, r.Succeeded ? r.Critical.Depth : null) : null;
        Status = r.Succeeded
            ? $"Calculated · {ResultSummary.RegimeText(r)}"
            : $"No result · {r.Errors.Count()} input error(s)";
        OnPropertyChanged(nameof(ListDetail));
        OnPropertyChanged(nameof(SolvedValueText));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(ListLabel));
        OnPropertyChanged(nameof(TitleHasFields));
    }

    // ------------------------------------------------------------ undo / redo

    private FieldState Capture() => new(Title, Description, Shape, Units, SolveFor, BottomWidth, LeftSlope, RightSlope, ChannelDepth, Diameter, EllipseStandard, EllipseSize, EllipseVertical, EllipseRise, EllipseSpan, ManningN, SlopePercent, Discharge, Depth, PointsText, RoughnessMethod, InvertElevation, Folder, Lining, PermissibleShear, RequiredFreeboard, MinVelocity, MaxVelocity);

    private void Restore(FieldState s)
    {
        _restoring = true;
        (Title, Description, Shape, Units, SolveFor) = (s.Title, s.Description, s.Shape, s.Units, s.SolveFor);
        (BottomWidth, LeftSlope, RightSlope, ChannelDepth, Diameter) = (s.BottomWidth, s.LeftSlope, s.RightSlope, s.ChannelDepth, s.Diameter);
        (EllipseStandard, EllipseSize, EllipseVertical, EllipseRise, EllipseSpan) = (s.EllipseStandard, s.EllipseSize, s.EllipseVertical, s.EllipseRise, s.EllipseSpan);
        (ManningN, SlopePercent, Discharge, Depth) = (s.ManningN, s.SlopePercent, s.Discharge, s.Depth);
        (PointsText, RoughnessMethod, InvertElevation, Folder) = (s.PointsText, s.RoughnessMethod, s.InvertElevation, s.Folder);
        (Lining, PermissibleShear, RequiredFreeboard, MinVelocity, MaxVelocity) = (s.Lining, s.PermissibleShear, s.RequiredFreeboard, s.MinVelocity, s.MaxVelocity);
        _restoring = false;
        _committed = s;
        _lastEditedProp = null;
        AfterInputChange();
    }

    /// <summary>Makes the next edit its own undo step (for tool buttons, which are not keystrokes).</summary>
    public void BeginUndoStep() => _lastEditedProp = null;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Capture());
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Capture());
        Restore(_redo.Pop());
    }

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoring || e.PropertyName is null || Array.IndexOf(InputProps, e.PropertyName) < 0) return;

        // Coalesce consecutive keystrokes in the same field into one undo step.
        var now = DateTime.UtcNow;
        bool coalesce = _lastEditedProp == e.PropertyName && (now - _lastEditTime).TotalSeconds < 1.5;
        if (!coalesce) _undo.Push(_committed);
        _redo.Clear();
        _lastEditedProp = e.PropertyName;
        _lastEditTime = now;
        _committed = Capture();
        AfterInputChange();
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void AfterInputChange()
    {
        Recalculate();
        foreach (var p in new[] { nameof(Constants), nameof(StressUnit), nameof(VelocityUnit), nameof(LengthUnit), nameof(DischargeUnit), nameof(ShowBottomWidth), nameof(ShowSideSlopes), nameof(IsPipe), nameof(IsEllipse), nameof(ShowEllipseStandard), nameof(ShowEllipseCustom), nameof(ShowChannelDepth), nameof(IsIrregular), nameof(CanConvertToIrregular), nameof(ShowInvertElevation), nameof(DiameterUnit),
                     nameof(DischargeIsInput), nameof(DepthIsInput), nameof(SlopeIsInput), nameof(RoughnessIsInput), nameof(ListLabel), nameof(ListDetail), nameof(CanUndo), nameof(CanRedo) })
            OnPropertyChanged(p);
    }
}
