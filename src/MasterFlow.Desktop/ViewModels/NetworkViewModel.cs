using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.Desktop.ViewModels;

/// <summary>One row of a results table, already formatted (same strings as the PDF and HTML reports).</summary>
public sealed record NetworkTableRow(IReadOnlyList<string> Cells, bool Fails);

/// <summary>
/// Editor and results for one storm network: pipes and structures as tab-separated text (paste from
/// Excel), tailwater and criteria, solved HGL/EGL tables, messages and the profile along a chosen path.
/// </summary>
public sealed partial class NetworkViewModel : ObservableObject
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private bool _loading;
    private IReadOnlyList<NetworkStructure> _structures = [];

    public static IReadOnlyList<TailwaterRule> TailwaterRules { get; } = Enum.GetValues<TailwaterRule>();
    public static IReadOnlyList<UnitSystem> UnitSystems { get; } = Enum.GetValues<UnitSystem>();
    public static string LossMethods { get; } = "Loss methods: " + string.Join(", ", Enum.GetNames<StructureLossMethod>()) +
        ". Auto: no inflow = beginning of line (1.25·V2²/2g); one inflow = inlet on main line (c = 0.5); several = branch lateral (inlets, c = 0.25) or manhole with 90° lateral (c = 0.25). StandardK uses hs = K·Vo²/2g with the K column or the default K.";

    /// <summary>Raised after any edit, so the project can be marked changed.</summary>
    public event EventHandler? Edited;

    [ObservableProperty] private string _name = "Storm network";
    [ObservableProperty] private UnitSystem _units = UnitSystem.USCustomary;
    [ObservableProperty] private string _pipesText = "";
    [ObservableProperty] private string _structuresText = "";
    [ObservableProperty] private TailwaterRule _tailwater = TailwaterRule.HalfCriticalPlusDiameter;
    [ObservableProperty] private string _tailwaterElevation = "";
    [ObservableProperty] private string _defaultJunctionK = "0.5";
    [ObservableProperty] private string _minorStormName = "minor storm";
    [ObservableProperty] private string _hglBelowRim = "";
    [ObservableProperty] private string _eglBelowRim = "";
    [ObservableProperty] private string _minVelocity = "";
    [ObservableProperty] private string _maxVelocity = "";
    [ObservableProperty] private bool _allowSurcharge = true;
    [ObservableProperty] private string _minPipeSize = "";
    [ObservableProperty] private bool _flagSupercritical;
    [ObservableProperty] private string _criteriaSource = "";

    [ObservableProperty] private StormNetworkResult? _result;
    [ObservableProperty] private string _status = "Not solved.";
    [ObservableProperty] private string? _selectedPath;
    [ObservableProperty] private NetworkProfile? _profile;

    public ObservableCollection<string> InputErrors { get; } = [];
    public ObservableCollection<NetworkTableRow> PipeRows { get; } = [];
    public ObservableCollection<NetworkTableRow> StructureRows { get; } = [];
    public ObservableCollection<CalcMessage> Messages { get; } = [];
    public ObservableCollection<string> Paths { get; } = [];
    public ObservableCollection<string> Trace { get; } = [];
    /// <summary>Changes and notes from the last automatic sizing.</summary>
    public ObservableCollection<string> SizingReport { get; } = [];
    private string? _pipesBeforeSizing;
    [ObservableProperty] private bool _canUndoSizing;

    public string PipesHeader => NetworkText.PipeHeader(Units).Replace('\t', '|');
    public string StructuresHeader => NetworkText.StructureHeader.Replace('\t', '|');
    public string LengthUnit => UnitConstants.For(Units).Length;
    public string VelocityUnit => UnitConstants.For(Units).Velocity;
    public string SizeUnit => NetworkText.SizeUnit(Units);
    public bool IsElevationTailwater => Tailwater == TailwaterRule.Elevation;
    public IReadOnlyList<string> PipeColumns => Result is null ? [] : NetworkTables.PipeHeader(Result.Constants, Result.Network.Units);
    public IReadOnlyList<string> StructureColumns => Result is null ? [] : NetworkTables.StructureHeader(Result.Constants);
    public string CriteriaSummary => Result is null ? "" : NetworkTables.CriteriaText(Result.Network, Result.Constants);
    public bool HasResult => Result is { Succeeded: true };

    public override string ToString() => Name;

    partial void OnUnitsChanged(UnitSystem value)
    {
        OnPropertyChanged(nameof(PipesHeader)); OnPropertyChanged(nameof(LengthUnit)); OnPropertyChanged(nameof(VelocityUnit)); OnPropertyChanged(nameof(SizeUnit));
    }
    partial void OnTailwaterChanged(TailwaterRule value) => OnPropertyChanged(nameof(IsElevationTailwater));
    partial void OnSelectedPathChanged(string? value) => Profile = Result is null || value is null ? null : NetworkProfile.Build(Result, value);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading) return;
        if (e.PropertyName is nameof(Result) or nameof(Status) or nameof(SelectedPath) or nameof(Profile) or nameof(PipesHeader) or nameof(StructuresHeader)
            or nameof(LengthUnit) or nameof(VelocityUnit) or nameof(SizeUnit) or nameof(IsElevationTailwater) or nameof(PipeColumns) or nameof(StructureColumns)
            or nameof(CriteriaSummary) or nameof(HasResult) or nameof(CanUndoSizing)) return;
        Edited?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ model mapping

    public static NetworkViewModel FromModel(StormNetwork n)
    {
        var vm = new NetworkViewModel();
        vm.Load(n);
        return vm;
    }

    private void Load(StormNetwork n)
    {
        _loading = true;
        string S(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("0.######", Inv) : "";
        Name = n.Name; Units = n.Units;
        PipesText = NetworkText.FormatPipes(n);
        _structures = n.Structures;
        StructuresText = NetworkText.FormatStructures(n);
        Tailwater = n.Tailwater; TailwaterElevation = S(n.TailwaterElevation); DefaultJunctionK = S(n.DefaultJunctionK); MinorStormName = n.MinorStormName;
        var c = n.Criteria;
        HglBelowRim = S(c.HglBelowRim); EglBelowRim = S(c.EglBelowRim); MinVelocity = S(c.MinVelocity); MaxVelocity = S(c.MaxVelocity);
        AllowSurcharge = c.AllowSurcharge; MinPipeSize = S(c.MinPipeRise / NetworkText.SizeToBase(n.Units)); FlagSupercritical = c.FlagSupercritical; CriteriaSource = c.Source;
        _loading = false;
    }

    private static double? Opt(string s, string what, List<string> errs)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) || double.TryParse(s, NumberStyles.Float, Inv, out v)) return v;
        errs.Add($"{what} \"{s}\" is not a number.");
        return null;
    }

    /// <summary>The network as entered, plus the input errors found while reading it.</summary>
    public (StormNetwork Network, IReadOnlyList<string> Errors) BuildModel()
    {
        var errs = new List<string>();
        var (pipes, pe) = NetworkText.ParsePipes(PipesText, Units);
        var (structs, se) = NetworkText.ParseStructures(StructuresText, _structures);
        errs.AddRange(pe.Select(e => "Pipes: " + e));
        errs.AddRange(se.Select(e => "Structures: " + e));
        var crit = new NetworkCriteria
        {
            HglBelowRim = Opt(HglBelowRim, "HGL below rim", errs), EglBelowRim = Opt(EglBelowRim, "EGL below rim", errs),
            MinVelocity = Opt(MinVelocity, "Minimum velocity", errs), MaxVelocity = Opt(MaxVelocity, "Maximum velocity", errs),
            AllowSurcharge = AllowSurcharge, MinPipeRise = Opt(MinPipeSize, "Minimum pipe size", errs) * NetworkText.SizeToBase(Units),
            FlagSupercritical = FlagSupercritical, Source = CriteriaSource.Trim(),
        };
        var n = new StormNetwork
        {
            Name = string.IsNullOrWhiteSpace(Name) ? "Storm network" : Name.Trim(), Units = Units, Pipes = pipes, Structures = structs,
            Tailwater = Tailwater, TailwaterElevation = Opt(TailwaterElevation, "Tailwater elevation", errs),
            DefaultJunctionK = Opt(DefaultJunctionK, "Default K", errs) ?? 0.5, Criteria = crit,
            MinorStormName = string.IsNullOrWhiteSpace(MinorStormName) ? "minor storm" : MinorStormName.Trim(),
        };
        return (n, errs);
    }

    public StormNetwork ToModel() => BuildModel().Network;

    // ------------------------------------------------------------ commands

    [RelayCommand]
    private void ApplyRapidCity()
    {
        var c = NetworkCriteria.RapidCity2012;
        string S(double? v) => v?.ToString("0.###", Inv) ?? "";
        bool si = Units == UnitSystem.SI;
        // The manual's values are U.S. customary (ft, fps, in.); converted values are shown for SI networks.
        HglBelowRim = si ? "0.305" : S(c.HglBelowRim);
        EglBelowRim = "";
        MaxVelocity = si ? "6.10" : S(c.MaxVelocity);
        MinVelocity = si ? "0.76" : S(c.MinVelocity);
        AllowSurcharge = c.AllowSurcharge;
        MinPipeSize = si ? "305" : "12";
        FlagSupercritical = c.FlagSupercritical;
        CriteriaSource = c.Source + (si ? " (converted from U.S. customary)" : "");
        MinorStormName = "2-yr";
        Status = "Rapid City IDCM 2012 criteria set: HGL at least 1 ft below the rim (§4.6.7); minimum 2.5 fps at the 2-yr flow (enter 2-yr inflows or Q minor; without them the design flow is used); " +
                 "maximum 20 fps (Table 4-13, collectors and mains; inlet laterals have no limit); 12 in. minimum (18 in. if the pipe has bends); slopes below critical; designed as open channel (no surcharge).";
    }

    [RelayCommand]
    public void Solve()
    {
        var (n, errs) = BuildModel();
        InputErrors.Clear();
        foreach (var e in errs) InputErrors.Add(e);
        var r = StormNetworkSolver.Solve(n);
        _structures = n.Structures;
        Result = r;
        PipeRows.Clear(); StructureRows.Clear(); Messages.Clear(); Paths.Clear(); Trace.Clear();
        foreach (var m in r.Messages) Messages.Add(m);
        foreach (var t in r.Trace) Trace.Add(t);
        if (r.Succeeded)
        {
            foreach (var row in NetworkTables.PipeRows(r)) PipeRows.Add(new NetworkTableRow(row, false));
            foreach (var row in NetworkTables.StructureRows(r)) StructureRows.Add(new NetworkTableRow(row, row[NetworkTables.StructureCheckColumn] == "FAILS"));
            foreach (var p in NetworkProfile.PathStarts(r)) Paths.Add(p);
        }
        SelectedPath = Paths.FirstOrDefault();
        Profile = SelectedPath is null ? null : NetworkProfile.Build(r, SelectedPath);
        int fails = StructureRows.Count(s => s.Fails);
        int warn = r.Messages.Count(m => m.Severity == MessageSeverity.Warning);
        Status = !r.Succeeded
            ? $"Not solved: {r.Messages.FirstOrDefault(m => m.Severity == MessageSeverity.Error)?.Text ?? "see messages."}"
            : $"Solved {r.Pipes.Count} pipe(s), {r.Structures.Count} structure(s). " +
              (n.Criteria.HglBelowRim is null ? "No HGL criterion set. " : fails == 0 ? "HGL criterion met at every structure with a rim. " : $"HGL criterion FAILS at {fails} structure(s). ") +
              (warn > 0 ? $"{warn} warning(s)." : "No warnings.") + (errs.Count > 0 ? $" {errs.Count} input problem(s) listed." : "");
        OnPropertyChanged(nameof(PipeColumns)); OnPropertyChanged(nameof(StructureColumns)); OnPropertyChanged(nameof(CriteriaSummary)); OnPropertyChanged(nameof(HasResult));
    }

    /// <summary>
    /// Sizes the circular pipes to the criteria (<see cref="NetworkSizer"/>), replaces the pipe sizes in the
    /// grid, solves, and lists every change; <see cref="UndoSizing"/> restores the previous grid.
    /// </summary>
    [RelayCommand]
    public void SizePipes()
    {
        var (n, errs) = BuildModel();
        SizingReport.Clear();
        if (errs.Count > 0) { Status = $"Not sized: {errs.Count} input problem(s); fix them first."; foreach (var e in errs) SizingReport.Add(e); return; }
        var r = NetworkSizer.Size(n);
        double k = NetworkText.SizeToBase(Units);
        string u = NetworkText.SizeUnit(Units);
        foreach (var c in r.Changes) SizingReport.Add($"{c.Pipe}: {(c.OldRise / k).ToString("0.##", Inv)} → {(c.NewRise / k).ToString("0.##", Inv)} {u} ({c.Reason})");
        foreach (var note in r.Notes) SizingReport.Add(note);
        if (r.Changes.Count > 0)
        {
            _pipesBeforeSizing = PipesText;
            PipesText = NetworkText.FormatPipes(r.Network);
            CanUndoSizing = true;
        }
        Solve();
        Status = (r.Changes.Count == 0 ? "Sizing: no changes needed or possible. " : $"Sized: {r.Changes.Count} pipe(s) changed (see Sizing). ") + Status;
    }

    [RelayCommand]
    public void UndoSizing()
    {
        if (_pipesBeforeSizing is null) return;
        PipesText = _pipesBeforeSizing;
        _pipesBeforeSizing = null;
        CanUndoSizing = false;
        SizingReport.Clear();
        Solve();
        Status = "Sizing undone. " + Status;
    }

    /// <summary>Tab-separated pipes and structures tables (for pasting into Excel).</summary>
    public string ResultsText()
    {
        if (Result is null) return "";
        var lines = new List<string> { string.Join('\t', PipeColumns) };
        lines.AddRange(PipeRows.Select(r => string.Join('\t', r.Cells)));
        lines.Add("");
        lines.Add(string.Join('\t', StructureColumns));
        lines.AddRange(StructureRows.Select(r => string.Join('\t', r.Cells)));
        lines.Add("");
        lines.Add(CriteriaSummary);
        return string.Join(Environment.NewLine, lines);
    }
}
