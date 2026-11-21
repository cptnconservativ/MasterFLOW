using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Persistence;
using MasterFlow.Reporting;

namespace MasterFlow.Desktop.ViewModels;

/// <summary>Operations that need the window (file pickers, clipboard, shell). Implemented by MainWindow.</summary>
public interface IDialogService
{
    Task<string?> PickOpenProjectAsync();
    /// <summary>Save picker; <paramref name="alternatives"/> are further file types the user may choose (decided by the returned extension).</summary>
    Task<string?> PickSaveAsync(string suggestedName, string extension, string description, params (string Extension, string Description)[] alternatives);
    Task SetClipboardAsync(string text);
    void OpenWithShell(string path);
    Task<bool> ConfirmDiscardAsync(string message);
    Task<string?> PickImageAsync();
    /// <summary>Open picker for any data file (patterns such as "*.csv").</summary>
    Task<string?> PickOpenFileAsync(string title, string description, params string[] patterns);
    /// <summary>Start-up question about a recovery copy. Closing the dialog or pressing Escape must mean <see cref="RecoveryChoice.SetAside"/>.</summary>
    Task<RecoveryChoice> AskRecoveryAsync(string message);
}

/// <summary>What to do with unsaved work found at start-up. Only an explicit Discard deletes it.</summary>
public enum RecoveryChoice { Restore, SetAside, Discard }

public sealed partial class MainViewModel : ObservableObject
{
    public static string AppVersion => AppInfo.Version;

    public ObservableCollection<CalculationViewModel> Calculations { get; } = [];

    /// <summary>Drainage structures imported from pipe networks (listed and exported, not solved).</summary>
    public ObservableCollection<StructureRow> Structures { get; } = [];
    public bool HasStructures => Structures.Count > 0;

    /// <summary>Storm sewer networks solved for HGL/EGL (Storm networks window).</summary>
    public ObservableCollection<NetworkViewModel> Networks { get; } = [];
    [ObservableProperty] private NetworkViewModel? _selectedNetwork;
    public ObservableCollection<ChannelCalculation> RecycleBin { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];

    /// <summary>Per-user settings and autosave location; null in tests and when unavailable.</summary>
    public UserDataStore? Store
    {
        get => _store;
        set
        {
            _store = value;
            RecentFiles.Clear();
            if (value is not null) foreach (var f in value.LoadSettings().RecentFiles) RecentFiles.Add(f);
            License = Application.Licensing.LicenseKey.Check(value?.LoadSettings().LicenseKey, DateOnly.FromDateTime(DateTime.Now));
            RefreshTemplates();
        }
    }

    /// <summary>Built-in templates followed by the user's own (saved per user, not in the project).</summary>
    public ObservableCollection<CalculationTemplate> Templates { get; } = [];
    private List<ChannelCalculation> _userTemplates = [];

    private void RefreshTemplates()
    {
        _userTemplates = Store?.LoadUserTemplates().ToList() ?? _userTemplates;
        Templates.Clear();
        foreach (var t in BuiltInTemplates.All) Templates.Add(t);
        foreach (var u in _userTemplates)
            Templates.Add(new CalculationTemplate(u.Title, "My templates", u.Description, u));
    }
    private UserDataStore? _store;

    /// <summary>The checked license (Free when no key is stored). Premium design tools run only when it allows them.</summary>
    public Application.Licensing.LicenseState License
    {
        get => _license;
        set { _license = value; OnPropertyChanged(); _tools?.RefreshLicense(); }
    }
    private Application.Licensing.LicenseState _license = Application.Licensing.LicenseState.Free;

    /// <summary>Checks <paramref name="key"/>, keeps it in the user settings when it is valid (or cleared), and returns the new state.</summary>
    public Application.Licensing.LicenseState ActivateLicense(string? key)
    {
        var state = Application.Licensing.LicenseKey.Check(key, DateOnly.FromDateTime(DateTime.Now));
        if (state.Status is Application.Licensing.LicenseStatus.Valid or Application.Licensing.LicenseStatus.None) License = state;
        if (Store is not null && state.Status is Application.Licensing.LicenseStatus.Valid or Application.Licensing.LicenseStatus.None)
            try { Store.SaveSettings(Store.LoadSettings() with { LicenseKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim() }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the key still works for this session */ }
        return state;
    }

    public MainViewModel()
    {
        RefreshTemplates();
        Calculations.CollectionChanged += (_, _) => { if (!_loading) RefreshComparison(); };
        RecentFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoRecentFiles));
    }

    public bool NoRecentFiles => RecentFiles.Count == 0;

    [ObservableProperty] private CalculationViewModel? _selected;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusMessage = "Ready";

    /// <summary>Local-only activity and error log; null in tests.</summary>
    public LocalLog? Log { get; set; }

    // Status messages double as the activity trail (saves, exports and failures) in the local log.
    partial void OnStatusMessageChanged(string value) => Log?.Info(value);

    /// <summary>Last-resort handling for an unexpected error: log it, save a recovery copy, tell the user.</summary>
    public void ReportUnexpectedError(Exception ex, string where)
    {
        Log?.Error($"Unexpected error ({where}).", ex);
        bool saved = false;
        try { saved = Autosave(); } catch (Exception inner) { Log?.Error("Recovery save after the error also failed.", inner); }
        StatusMessage = $"Unexpected error: {ex.Message}" + (saved ? " Unsaved work was copied for recovery." : "") +
                        (Log is null ? "" : $" Details are in {Log.CurrentFile}.");
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        if (Log is null || Dialogs is null) return;
        try { Directory.CreateDirectory(Log.Directory); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        Dialogs.OpenWithShell(Log.Directory);
    }

    // Project information (appears on reports)
    [ObservableProperty] private string _projectName = "Untitled project";
    [ObservableProperty] private string _projectNumber = "";
    [ObservableProperty] private string _client = "";
    [ObservableProperty] private string _preparedBy = "";
    [ObservableProperty] private string _checkedBy = "";
    [ObservableProperty] private string _notes = "";

    // Report options
    [ObservableProperty] private string _watermark = "";
    [ObservableProperty] private string _footerText = "";
    [ObservableProperty] private string? _logoDataUri;
    [ObservableProperty] private bool _includeCalculationTrace = true;

    // Rating table
    [ObservableProperty] private RatingTableMode _ratingMode = RatingTableMode.DischargeFromDepth;
    [ObservableProperty] private string _ratingStart = "0.5";
    [ObservableProperty] private string _ratingEnd = "5";
    [ObservableProperty] private string _ratingIncrement = "0.5";
    [ObservableProperty] private RatingTable? _ratingTable;
    [ObservableProperty] private ComparisonTable? _comparison;
    [ObservableProperty] private string _ratingStatus = "Choose a range and press Generate.";
    [ObservableProperty] private SensitivityTable? _sensitivityTable;

    public IDialogService? Dialogs { get; set; }

    public static IReadOnlyList<RatingTableMode> RatingModes { get; } = [RatingTableMode.DischargeFromDepth, RatingTableMode.DepthFromDischarge];

    public string WindowTitle => $"{(IsDirty ? "• " : "")}{(FilePath is null ? ProjectName : Path.GetFileName(FilePath))} — MasterFlow";
    public bool HasSelection => Selected is not null;
    public string RatingStartLabel => RatingMode == RatingTableMode.DischargeFromDepth ? $"Start depth ({Selected?.LengthUnit})" : $"Start Q ({Selected?.DischargeUnit})";
    public string RatingEndLabel => RatingMode == RatingTableMode.DischargeFromDepth ? $"End depth ({Selected?.LengthUnit})" : $"End Q ({Selected?.DischargeUnit})";

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnFilePathChanged(string? value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnProjectNameChanged(string value) { MarkDirty(); OnPropertyChanged(nameof(WindowTitle)); }
    partial void OnProjectNumberChanged(string value) => MarkDirty();
    partial void OnClientChanged(string value) => MarkDirty();
    partial void OnPreparedByChanged(string value) => MarkDirty();
    partial void OnCheckedByChanged(string value) => MarkDirty();
    partial void OnNotesChanged(string value) => MarkDirty();
    partial void OnWatermarkChanged(string value) => MarkDirty();
    partial void OnFooterTextChanged(string value) => MarkDirty();
    partial void OnLogoDataUriChanged(string? value) { MarkDirty(); OnPropertyChanged(nameof(HasLogo)); }
    partial void OnIncludeCalculationTraceChanged(bool value) => MarkDirty();
    public bool HasLogo => LogoDataUri is not null;
    public bool HasRecycleBin => RecycleBin.Count > 0;
    public string RecycleBinLabel => $"Recycle bin ({RecycleBin.Count})";
    partial void OnRatingModeChanged(RatingTableMode value) { OnPropertyChanged(nameof(RatingStartLabel)); OnPropertyChanged(nameof(RatingEndLabel)); }

    partial void OnSelectedChanged(CalculationViewModel? value)
    {
        SensitivityTable = value?.BuildSensitivity();
        RatingTable = null;
        RatingStatus = "Choose a range and press Generate.";
        if (value?.Rating is { } rr)
        {
            var inv = CultureInfo.CurrentCulture;
            (RatingMode, RatingStart, RatingEnd, RatingIncrement) = (RatingTableBuilder.ModeOf(rr.Direction), rr.Start.ToString("G10", inv), rr.End.ToString("G10", inv), rr.Step.ToString("G10", inv));
            RatingTable = value.BuildRatingTable();
            RatingStatus = RatingTable is null ? "Saved range; the calculation has no result yet." : $"{RatingTable.Rows.Count} rows (saved with this calculation).";
        }
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(RatingStartLabel));
        OnPropertyChanged(nameof(RatingEndLabel));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool _loading;
    private void MarkDirty() { if (!_loading) IsDirty = true; }
    internal void MarkDirtyFromTools() => MarkDirty();

    private DesignToolsViewModel? _tools;
    /// <summary>The Design tools window's state (tool inputs are saved with the project).</summary>
    public DesignToolsViewModel Tools => _tools ??= new DesignToolsViewModel(this);

    // ------------------------------------------------------------ construction

    public static MainViewModel CreateWithSample()
    {
        var vm = new MainViewModel();
        vm.LoadProject(SampleProject(), null);
        return vm;
    }

    public static Project SampleProject() => new()
    {
        Info = new ProjectInfo { Name = "Sample project" },
        Calculations =
        [
            new ChannelCalculation
            {
                Title = "Trapezoidal ditch — normal depth",
                Description = "Chow (1959) Example 6-2 geometry: b = 20 ft, z = 2, S = 0.0016, n = 0.025, Q = 400 cfs.",
                Input = new UniformFlowInput { Section = new TrapezoidalSection(20, 2, 2) { ChannelDepth = 5 }, SolveFor = SolveFor.NormalDepth, ManningN = 0.025, Slope = 0.0016, Discharge = 400, Depth = 3 },
            },
            new ChannelCalculation
            {
                Title = "Concrete flume — capacity",
                Input = new UniformFlowInput { Section = new RectangularSection(10) { ChannelDepth = 3 }, SolveFor = SolveFor.Discharge, ManningN = 0.013, Slope = 0.001, Depth = 2 },
            },
            new ChannelCalculation
            {
                Title = "36-in RCP — near-full check",
                Description = "Discharge close to the maximum free-surface capacity: two normal depths exist.",
                Input = new UniformFlowInput { Section = new CircularSection(3.0), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = 71.37, Depth = 2 },
            },
            new ChannelCalculation
            {
                Title = "HE 36-in RCP (29 x 45) — normal depth",
                Description = "ASTM C507 horizontal elliptical pipe, standard four-centred shape.",
                Input = new UniformFlowInput { Section = EllipticalPipeCatalog.Create(EllipticalPipeCatalog.Sizes.Single(s => s.EquivalentRound == 36), false, UnitSystem.USCustomary), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.005, Discharge = 20, Depth = 1 },
            },
            new ChannelCalculation
            {
                Title = "Roadside V-ditch",
                Input = new UniformFlowInput { Section = new TriangularSection(4, 3) { ChannelDepth = 1.5 }, SolveFor = SolveFor.NormalDepth, ManningN = 0.030, Slope = 0.02, Discharge = 12, Depth = 1 },
            },
            new ChannelCalculation
            {
                Title = "Creek with overbanks",
                Description = "Surveyed section; main channel n = 0.035, grassed overbanks n = 0.06; conveyance subdivision.",
                Input = new UniformFlowInput
                {
                    Section = new IrregularSection(
                    [
                        new(0, 108, 0.06, "Left overbank"), new(40, 105, 0.06), new(50, 105, 0.035, "Left bank"), new(55, 100, 0.035),
                        new(65, 100, 0.035), new(70, 105, 0.06, "Right bank"), new(80, 105, 0.06), new(120, 108, null, "Right overbank"),
                    ]),
                    SolveFor = SolveFor.NormalDepth, ManningN = 0.06, Slope = 0.001, Discharge = 500, Depth = 6,
                },
            },
        ],
    };

    private void LoadProject(Project p, string? path)
    {
        _loading = true;
        foreach (var c in Calculations) c.Edited -= OnCalcEdited;
        Calculations.Clear();
        foreach (var c in p.Calculations) Add(CalculationViewModel.FromModel(c));
        (ProjectName, ProjectNumber, Client, PreparedBy, CheckedBy, Notes) = (p.Info.Name, p.Info.ProjectNumber, p.Info.Client, p.Info.PreparedBy, p.Info.CheckedBy, p.Info.Notes);
        (Watermark, FooterText, LogoDataUri, IncludeCalculationTrace) = (p.Report.Watermark, p.Report.FooterText, p.Report.LogoDataUri, p.Report.IncludeCalculationTrace);
        RecycleBin.Clear();
        foreach (var c in p.RecycleBin) RecycleBin.Add(c);
        Structures.Clear();
        foreach (var s in p.Structures) Structures.Add(new StructureRow(s));
        OnPropertyChanged(nameof(HasStructures));
        foreach (var n in Networks) n.Edited -= OnNetworkEdited;
        Networks.Clear();
        foreach (var n in p.Networks) AddNetwork(NetworkViewModel.FromModel(n));
        SelectedNetwork = Networks.FirstOrDefault();
        Tools.LoadInputs(p.ToolInputs);
        NotifyBin();
        Selected = Calculations.FirstOrDefault();
        FilePath = path;
        _loading = false;
        IsDirty = false;
        RefreshComparison();
    }

    private void Add(CalculationViewModel c)
    {
        c.Edited += OnCalcEdited;
        Calculations.Add(c);
    }

    /// <summary>Rebuilds the side-by-side comparison of every calculation in the project.</summary>
    /// <summary>Solved calculations ticked for batch outputs (comparison, batch report, workbook).</summary>
    public IEnumerable<CalculationViewModel> BatchCalculations => Calculations.Where(c => c.IncludeInBatch && c.Result is not null);

    public void RefreshComparison() =>
        Comparison = Application.Comparison.Build(BatchCalculations.Select(c => (c.ListLabel, c.Result!)).ToList());

    private void OnCalcEdited(object? sender, EventArgs e)
    {
        RefreshComparison();
        if (ReferenceEquals(sender, Selected)) SensitivityTable = Selected!.BuildSensitivity();
        MarkDirty();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public Project ToProject() => new()
    {
        Info = new ProjectInfo { Name = ProjectName, ProjectNumber = ProjectNumber, Client = Client, PreparedBy = PreparedBy, CheckedBy = CheckedBy, Notes = Notes },
        Calculations = Calculations.Select(c => c.ToModel()).ToList(),
        RecycleBin = RecycleBin.ToList(),
        Structures = Structures.Select(r => r.Structure).ToList(),
        Networks = Networks.Select(n => n.ToModel()).ToList(),
        ToolInputs = Tools.SavedInputs(),
        Report = new ReportSettings { Watermark = Watermark, FooterText = FooterText, LogoDataUri = LogoDataUri, IncludeCalculationTrace = IncludeCalculationTrace },
    };

    private void NotifyBin()
    {
        OnPropertyChanged(nameof(HasRecycleBin));
        OnPropertyChanged(nameof(RecycleBinLabel));
    }

    private void Remember(string path)
    {
        if (Store is null) return;
        var s = Store.LoadSettings().WithRecent(path);
        try { Store.SaveSettings(s); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* settings are a convenience */ }
        RecentFiles.Clear();
        foreach (var f in s.RecentFiles) RecentFiles.Add(f);
    }

    // ------------------------------------------------------------ storm networks (HGL/EGL)

    private void AddNetwork(NetworkViewModel n)
    {
        n.License = () => License;
        n.Edited += OnNetworkEdited;
        Networks.Add(n);
    }

    private void OnNetworkEdited(object? sender, EventArgs e) => MarkDirty();

    [RelayCommand]
    private void NewNetwork()
    {
        var units = Selected?.Units ?? UnitSystem.USCustomary;
        var n = NetworkViewModel.FromModel(new StormNetwork { Name = $"Storm network {Networks.Count + 1}", Units = units });
        AddNetwork(n);
        SelectedNetwork = n;
        MarkDirty();
    }

    [RelayCommand]
    private async Task DeleteNetwork()
    {
        if (SelectedNetwork is not { } n) return;
        if (Dialogs is not null && !await Dialogs.ConfirmDiscardAsync($"Delete the storm network \"{n.Name}\"? This cannot be undone.")) return;
        n.Edited -= OnNetworkEdited;
        int i = Networks.IndexOf(n);
        Networks.Remove(n);
        SelectedNetwork = Networks.Count == 0 ? null : Networks[Math.Min(i, Networks.Count - 1)];
        MarkDirty();
    }

    /// <summary>One storm network per LandXML pipe network (replacing one of the same name); returns how many.</summary>
    private int AddNetworksFromImport(LandXmlNetworkReadResult read, UnitSystem units)
    {
        int count = 0;
        foreach (var g in read.Pipes.GroupBy(p => p.Network))
        {
            string name = g.Key.Length > 0 ? g.Key : "Imported network";
            var structs = read.Structures.Where(s => s.Network == g.Key).Select(s => new NetworkStructure
            {
                Name = s.Name, Kind = s.Kind, RimElevation = s.RimElevation, Northing = s.Northing, Easting = s.Easting,
                Alignment = s.Alignment, Station = s.Station, Offset = s.Offset,
            }).ToList();
            var pipes = g.Select(p => new NetworkPipe
            {
                Name = p.Name, From = p.StartStructure, To = p.EndStructure, Shape = p.Shape, Rise = p.Rise, Span = p.Span,
                Length = p.Length ?? double.NaN, UpstreamInvert = p.StartInvert ?? double.NaN, DownstreamInvert = p.EndInvert ?? double.NaN,
                ManningN = 0.013, Discharge = double.NaN,
            }).ToList();
            var model = new StormNetwork { Name = name, Units = units, Pipes = pipes, Structures = structs };
            var old = Networks.FirstOrDefault(n => n.Name == name);
            if (old is not null) { old.Edited -= OnNetworkEdited; Networks.Remove(old); }
            var vm = NetworkViewModel.FromModel(model);
            AddNetwork(vm);
            SelectedNetwork = vm;
            count++;
        }
        return count;
    }

    internal ReportOptions NetworkReportOptions() => new()
    {
        SoftwareVersion = AppVersion,
        Watermark = string.IsNullOrWhiteSpace(Watermark) ? null : Watermark,
        FooterText = string.IsNullOrWhiteSpace(FooterText) ? null : FooterText,
        LogoDataUri = LogoDataUri,
        IncludeCalculationTrace = IncludeCalculationTrace,
    };

    /// <summary>PDF (landscape) of the selected network: profiles, pipes and structures tables, messages. Public for tests.</summary>
    public byte[]? BuildNetworkPdf(NetworkViewModel n)
    {
        n.Solve();
        return n.Result is { Succeeded: true } r ? NetworkReport.ToPdf(ToProject().Info, r, NetworkReportOptions()) : null;
    }

    public string? BuildNetworkHtml(NetworkViewModel n)
    {
        n.Solve();
        return n.Result is { Succeeded: true } r ? NetworkReport.ToHtml(ToProject().Info, r, NetworkReportOptions()) : null;
    }

    [RelayCommand]
    private async Task ExportNetworkReport()
    {
        if (SelectedNetwork is not { } n || Dialogs is null) return;
        n.Solve();
        if (!n.HasResult) { StatusMessage = "The network did not solve; see its messages."; return; }
        var path = await Dialogs.PickSaveAsync(SafeName(n.Name) + " HGL.pdf", ".pdf", "PDF network report", HtmlChoice);
        if (path is null) return;
        try
        {
            if (IsHtml(path)) await File.WriteAllTextAsync(path, BuildNetworkHtml(n)!);
            else await File.WriteAllBytesAsync(path, BuildNetworkPdf(n)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Report not saved: {ex.Message}"; return; }
        StatusMessage = $"Network report saved to {path}.{LogoNote(path)}";
        Dialogs.OpenWithShell(path);
    }

    /// <summary>
    /// LandXML pipe network for Civil 3D (sizes, inverts, structures; HGL/EGL as Feature properties). Premium
    /// (Civil 3D export): null, with the reason in the network's Status, when the license does not cover it. Public for tests.
    /// </summary>
    public (string Xml, IReadOnlyList<string> Warnings)? BuildNetworkLandXml(NetworkViewModel n)
    {
        if (!n.MayUse(Application.Licensing.PremiumFeatures.Civil3DExport, "LandXML pipe network export")) return null;
        n.Solve();
        return LandXmlPipeNetworkWriter.Write(n.ToModel(), n.Result is { Succeeded: true } r ? r : null, AppVersion);
    }

    [RelayCommand]
    private async Task ExportNetworkLandXml()
    {
        if (SelectedNetwork is not { } n || Dialogs is null) return;
        if (!n.MayUse(Application.Licensing.PremiumFeatures.Civil3DExport, "LandXML pipe network export")) { StatusMessage = n.Status; return; }
        var path = await Dialogs.PickSaveAsync(SafeName(n.Name) + ".xml", ".xml", "LandXML pipe network (Civil 3D)");
        if (path is null) return;
        if (BuildNetworkLandXml(n) is not { } built) { StatusMessage = n.Status; return; }
        var (xml, warnings) = built;
        try { await File.WriteAllTextAsync(path, xml, new System.Text.UTF8Encoding(false)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"LandXML not saved: {ex.Message}"; return; }
        n.Status = $"LandXML saved to {path}. In Civil 3D: Insert → LandXML, then choose a parts list with these sizes." +
                   (warnings.Count > 0 ? $" {warnings.Count} warning(s): {warnings[0]}" : "");
        StatusMessage = n.Status;
    }

    /// <summary>
    /// Solves the network and builds the Civil 3D write-back files: an AutoLISP command (MFHGL) that sets the
    /// HGL/EGL and flow rate on the drawing's pipes by name, and a CSV of every pipe and structure result.
    /// Null (with the reason in Status) when the network does not solve or the license does not cover Civil 3D export (premium).
    /// </summary>
    public (string Lisp, string Csv, IReadOnlyList<string> Warnings)? BuildCivil3DResults(NetworkViewModel n)
    {
        if (!n.MayUse(Application.Licensing.PremiumFeatures.Civil3DExport, "Civil 3D HGL/EGL write-back")) return null;
        n.Solve();
        if (n.Result is not { Succeeded: true } r)
        {
            n.Status = "Civil 3D results not written: the network did not solve. Fix the errors listed and solve again.";
            return null;
        }
        var model = n.ToModel();
        var (lisp, warnings) = Civil3DWriteBack.AutoLisp(model, r, AppVersion);
        return (lisp, Civil3DWriteBack.Csv(model, r), warnings);
    }

    [RelayCommand]
    private async Task ExportCivil3DResults()
    {
        if (SelectedNetwork is not { } n || Dialogs is null) return;
        if (BuildCivil3DResults(n) is not { } built) { StatusMessage = n.Status; return; }
        var path = await Dialogs.PickSaveAsync(SafeName(n.Name) + " HGL.lsp", ".lsp", "AutoLISP for Civil 3D (command MFHGL)");
        if (path is null) return;
        string csv = Path.ChangeExtension(path, ".csv");
        try
        {
            await File.WriteAllTextAsync(path, built.Lisp, new System.Text.UTF8Encoding(false));
            await File.WriteAllTextAsync(csv, built.Csv, new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Civil 3D results not saved: {ex.Message}"; return; }
        n.Status = $"Saved {Path.GetFileName(path)} and {Path.GetFileName(csv)}. In Civil 3D: APPLOAD the .lsp, then type MFHGL to set HGL/EGL and flow on the pipes by name." +
                   (built.Warnings.Count > 0 ? $" {built.Warnings.Count} warning(s): {built.Warnings[0]}" : "");
        StatusMessage = n.Status;
    }

    [RelayCommand]
    private async Task CopyNetworkResults()
    {
        if (SelectedNetwork is not { } n || Dialogs is null) return;
        if (n.Result is null) n.Solve();
        await Dialogs.SetClipboardAsync(n.ResultsText());
        StatusMessage = "Network tables copied (tab-separated).";
    }

    // ------------------------------------------------------------ autosave and recovery

    /// <summary>Writes a recovery copy when there are unsaved changes. Called on a timer by the window.</summary>
    public bool Autosave()
    {
        if (!IsDirty || Store is null) return false;
        try { return Store.WriteRecovery(ToProject(), FilePath, AppVersion); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Autosave failed: {ex.Message}"; return false; }
    }

    /// <summary>At start-up: offers to restore an autosaved copy left by a crash or forced exit.</summary>
    public async Task<bool> CheckRecoveryAsync()
    {
        if (Store?.FindRecovery() is not { } r) return false;
        string when = r.SavedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture);
        string what = r.OriginalPath is null ? "an unsaved project" : Path.GetFileName(r.OriginalPath);
        var choice = Dialogs is null ? RecoveryChoice.Restore
            : await Dialogs.AskRecoveryAsync($"MasterFlow found unsaved work in {what} from {when}, left by a MasterFlow window that closed unexpectedly. " +
                                             "Restore opens it now. Keep for later saves it as a project file you can open any time. Discard deletes it.");
        if (choice == RecoveryChoice.Discard) { Store.ClearRecovery(); StatusMessage = "Recovered work discarded."; return false; }
        if (choice == RecoveryChoice.SetAside)
        {
            try
            {
                var kept = Store.SetRecoveryAside();
                StatusMessage = kept is null ? "No recovery copy to keep." : $"Unsaved work from {when} kept as {kept}. Use Open to load it.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"The recovery copy was left in place ({ex.Message}); MasterFlow will offer it again next time."; }
            return false;
        }
        try
        {
            LoadProject(Store.LoadRecovery(), r.OriginalPath);
            IsDirty = true;
            StatusMessage = $"Restored unsaved work from {when}. Save to keep it.";
            return true;
        }
        catch (ProjectFileException ex)
        {
            StatusMessage = $"The recovery copy could not be read: {ex.Message}";
            Store.ClearRecovery();
            return false;
        }
    }

    /// <summary>Called when the window is closing. Returns false to keep the window open.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (IsDirty && Dialogs is not null && !await Dialogs.ConfirmDiscardAsync("Close without saving? Unsaved changes will be lost.")) return false;
        Store?.ClearRecovery();
        return true;
    }

    public void LoadFrom(string path)
    {
        try
        {
            LoadProject(ProjectFile.Load(path), path);
            StatusMessage = $"Opened {path}";
            Remember(path);
        }
        catch (ProjectFileException ex)
        {
            StatusMessage = $"Could not open the project: {ex.Message}";
        }
    }

    // ------------------------------------------------------------ commands: project

    [RelayCommand]
    private async Task NewProject()
    {
        if (IsDirty && Dialogs is not null && !await Dialogs.ConfirmDiscardAsync("Discard unsaved changes and start a new project?")) return;
        LoadProject(new Project { Calculations = [new ChannelCalculation { Title = "Channel 1", Input = new UniformFlowInput { Section = new TrapezoidalSection(10, 2, 2), SolveFor = SolveFor.NormalDepth, ManningN = 0.013, Slope = 0.01, Discharge = 100, Depth = 1 } }] }, null);
        StatusMessage = "New project";
    }

    [RelayCommand]
    private async Task Open()
    {
        if (Dialogs is null) return;
        if (IsDirty && !await Dialogs.ConfirmDiscardAsync("Discard unsaved changes and open another project?")) return;
        var path = await Dialogs.PickOpenProjectAsync();
        if (path is not null) LoadFrom(path);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (FilePath is null) { await SaveAs(); return; }
        SaveTo(FilePath);
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        if (Dialogs is null) return;
        var path = await Dialogs.PickSaveAsync(SafeName(ProjectName) + ProjectFile.Extension, ProjectFile.Extension, "MasterFlow project");
        if (path is not null) SaveTo(path);
    }

    public void SaveTo(string path)
    {
        try
        {
            ProjectFile.Save(ToProject(), path, AppVersion);
            FilePath = path;
            IsDirty = false;
            StatusMessage = $"Saved {path}";
            Store?.ClearRecovery();
            Remember(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    // ------------------------------------------------------------ commands: calculations

    [RelayCommand]
    private void AddCalculation()
    {
        var c = new CalculationViewModel { Title = $"Channel {Calculations.Count + 1}" };
        Add(c);
        Selected = c;
        MarkDirty();
    }

    /// <summary>Adds a new calculation copied from a template.</summary>
    [RelayCommand]
    private void NewFromTemplate(CalculationTemplate? template)
    {
        if (template is null) return;
        var c = CalculationViewModel.FromModel(BuiltInTemplates.Instantiate(template));
        Add(c);
        Selected = c;
        MarkDirty();
        StatusMessage = $"Added \"{template.Name}\" from templates.";
    }

    /// <summary>Saves the selected calculation as a user template (replacing one with the same title).</summary>
    [RelayCommand]
    private void SaveAsTemplate()
    {
        if (Selected is null) return;
        var model = Selected.ToModel() with { Folder = "" };
        _userTemplates.RemoveAll(t => string.Equals(t.Title, model.Title, StringComparison.OrdinalIgnoreCase));
        _userTemplates.Add(model);
        if (!PersistUserTemplates()) return;
        RefreshTemplates();
        StatusMessage = $"Saved \"{model.Title}\" to My templates.";
    }

    [RelayCommand]
    private void DeleteUserTemplate(CalculationTemplate? template)
    {
        if (template is null || template.BuiltIn) return;
        _userTemplates.RemoveAll(t => t.Id == template.Calculation.Id);
        if (!PersistUserTemplates()) return;
        RefreshTemplates();
        StatusMessage = $"Removed \"{template.Name}\" from My templates.";
    }

    private bool PersistUserTemplates()
    {
        if (Store is null) return true; // tests / no user folder: keep in memory only
        try { Store.SaveUserTemplates(_userTemplates, AppVersion); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Template could not be saved: {ex.Message}";
            return false;
        }
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is null) return;
        var model = Selected.ToModel() with { Id = Guid.NewGuid(), Title = Selected.Title + " (copy)", CreatedUtc = DateTimeOffset.UtcNow };
        var c = CalculationViewModel.FromModel(model);
        Add(c);
        Selected = c;
        MarkDirty();
    }

    /// <summary>Moves the selected calculation to the project's recycle bin (restorable; saved with the project).</summary>
    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        int i = Calculations.IndexOf(Selected);
        var removed = Selected;
        removed.Edited -= OnCalcEdited;
        Calculations.RemoveAt(i);
        RecycleBin.Add(removed.ToModel());
        NotifyBin();
        Selected = Calculations.Count == 0 ? null : Calculations[Math.Min(i, Calculations.Count - 1)];
        StatusMessage = $"\"{removed.Title}\" moved to the recycle bin.";
        MarkDirty();
    }

    /// <summary>Restores a calculation from the recycle bin (the most recently deleted when none is given).</summary>
    [RelayCommand]
    private void Restore(ChannelCalculation? item)
    {
        item ??= RecycleBin.LastOrDefault();
        if (item is null || !RecycleBin.Remove(item)) return;
        var c = CalculationViewModel.FromModel(item);
        Add(c);
        Selected = c;
        NotifyBin();
        StatusMessage = $"Restored \"{item.Title}\".";
        MarkDirty();
    }

    [RelayCommand]
    private async Task EmptyRecycleBin()
    {
        if (RecycleBin.Count == 0) return;
        if (Dialogs is not null && !await Dialogs.ConfirmDiscardAsync($"Permanently remove {RecycleBin.Count} deleted calculation(s) from this project? This cannot be undone after saving.")) return;
        RecycleBin.Clear();
        NotifyBin();
        MarkDirty();
    }

    /// <summary>Sorts calculations by folder, then title, using natural number order ("Ditch 2" before "Ditch 10").</summary>
    [RelayCommand]
    private void Sort()
    {
        var sel = Selected;
        var sorted = Calculations.OrderBy(c => CalculationViewModel.NormaliseFolder(c.Folder), NaturalComparer.Instance)
                                 .ThenBy(c => c.Title, NaturalComparer.Instance).ToList();
        if (sorted.SequenceEqual(Calculations)) return;
        _loading = true;
        Calculations.Clear();
        foreach (var c in sorted) Calculations.Add(c);
        _loading = false;
        Selected = sel;
        RefreshComparison();
        MarkDirty();
    }

    /// <summary>A project double-clicked while MasterFlow is already open: opened here (after the usual unsaved-changes check).</summary>
    public Task OpenFromAnotherLaunchAsync(string path) => OpenRecent(path);

    [RelayCommand]
    private async Task OpenRecent(string? path)
    {
        if (path is null) return;
        if (!File.Exists(path))
        {
            StatusMessage = $"File not found: {path}";
            RecentFiles.Remove(path);
            return;
        }
        if (IsDirty && Dialogs is not null && !await Dialogs.ConfirmDiscardAsync("Discard unsaved changes and open another project?")) return;
        LoadFrom(path);
    }

    [RelayCommand]
    private async Task PickLogo()
    {
        if (Dialogs is null) return;
        var path = await Dialogs.PickImageAsync();
        if (path is null) return;
        try { LogoDataUri = LogoData.FromFile(path); StatusMessage = "Logo added to the report header."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { StatusMessage = $"Logo not added: {ex.Message}"; }
    }

    [RelayCommand]
    private void ClearLogo() => LogoDataUri = null;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() { Selected?.Undo(); UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged(); }
    private bool CanUndo() => Selected?.CanUndo == true;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() { Selected?.Redo(); UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged(); }
    private bool CanRedo() => Selected?.CanRedo == true;

    [RelayCommand]
    private async Task CopyResults()
    {
        if (Selected?.Result is not { Succeeded: true } r || Dialogs is null) { StatusMessage = "Nothing to copy: no valid result."; return; }
        var lines = ResultSummary.Lines(r).Select(l => $"{l.Label}\t{l.Symbol}\t{l.Formatted}\t{l.Unit}");
        await Dialogs.SetClipboardAsync(string.Join(Environment.NewLine, lines));
        StatusMessage = "Results copied to the clipboard (tab-separated).";
    }

    // ------------------------------------------------------------ sensitivity

    /// <summary>Recomputes the sensitivity table for the selected calculation's current result.</summary>
    [RelayCommand]
    private void RefreshSensitivity()
    {
        SensitivityTable = Selected?.BuildSensitivity();
        StatusMessage = SensitivityTable is null ? "Solve the calculation first; the sensitivity table varies its inputs." : $"Sensitivity: {SensitivityTable.Rows.Count} cases.";
    }

    [RelayCommand]
    private async Task CopySensitivity()
    {
        if (Selected is null || Dialogs is null) return;
        SensitivityTable ??= Selected.BuildSensitivity();
        if (SensitivityTable is null) return;
        await Dialogs.SetClipboardAsync(SensitivityAnalysis.ToDelimited(SensitivityTable, Selected.Constants));
        StatusMessage = "Sensitivity table copied (tab-separated; paste into Excel).";
    }

    // ------------------------------------------------------------ rating table

    [RelayCommand]
    private void GenerateRatingTable()
    {
        if (Selected?.Result is not { } r) return;
        bool ok = CalculationViewModel.TryParse(RatingStart, out double s) & CalculationViewModel.TryParse(RatingEnd, out double e) & CalculationViewModel.TryParse(RatingIncrement, out double inc);
        if (!ok) { RatingTable = null; RatingStatus = "Start, end and increment must be numbers."; return; }
        var table = RatingTableBuilder.Build(new RatingTableRequest(r.Input, RatingMode, s, e, inc));
        RatingTable = table;
        // Keep the range with the calculation so reports and the workbook include its table.
        if (table.Rows.Count > 0) Selected.Rating = new RatingRange(RatingTableBuilder.DirectionOf(RatingMode), s, e, inc);
        var errors = table.Messages.Where(m => m.Severity == Hydraulics.MessageSeverity.Error).ToList();
        RatingStatus = errors.Count > 0
            ? $"{table.Rows.Count} rows. {errors.Count} problem(s): {errors[0].Text}"
            : $"{table.Rows.Count} rows." + (table.Messages.Count > 0 ? " " + table.Messages[0].Text : "");
    }

    /// <summary>Stops including a rating table for the selected calculation.</summary>
    [RelayCommand]
    private void RemoveRatingTable()
    {
        if (Selected is null) return;
        Selected.Rating = null;
        RatingTable = null;
        RatingStatus = "Rating table removed from this calculation.";
    }

    [RelayCommand]
    private async Task CopyRatingTable()
    {
        if (RatingTable is null || Selected is null || Dialogs is null) return;
        await Dialogs.SetClipboardAsync(RatingTableBuilder.ToDelimited(RatingTable, Selected.Constants));
        StatusMessage = "Rating table copied (tab-separated; paste into Excel).";
    }

    [RelayCommand]
    private async Task ExportRatingTableCsv()
    {
        if (RatingTable is null || Selected is null || Dialogs is null) return;
        var path = await Dialogs.PickSaveAsync(SafeName(Selected.Title) + " rating.csv", ".csv", "CSV file");
        if (path is null) return;
        await File.WriteAllTextAsync(path, RatingTableBuilder.ToDelimited(RatingTable, Selected.Constants, ','));
        StatusMessage = $"Exported {path}";
    }

    // ------------------------------------------------------------ report

    private ReportOptions ReportOptionsFor(CalculationViewModel calc) => new()
    {
        SoftwareVersion = AppVersion,
        Watermark = string.IsNullOrWhiteSpace(Watermark) ? null : Watermark,
        FooterText = string.IsNullOrWhiteSpace(FooterText) ? null : FooterText,
        LogoDataUri = LogoDataUri,
        IncludeCalculationTrace = IncludeCalculationTrace,
        RatingTable = calc.BuildRatingTable(),
        Sensitivity = calc.Sensitivity ? calc.BuildSensitivity() : null,
    };

    private ChannelCalculation ReportModel(CalculationViewModel calc) => calc.ToModel() with { Title = calc.DisplayTitle };

    public string BuildReportHtml(CalculationViewModel calc) =>
        HtmlReport.Render(ToProject().Info, ReportModel(calc), calc.Result!, ReportOptionsFor(calc));

    /// <summary>Native PDF of the given calculations (one document, each starting on a new page).</summary>
    public byte[] BuildReportPdf(IEnumerable<CalculationViewModel> calcs) =>
        PdfReport.ToBytes(ToProject().Info, calcs.Where(c => c.Result is not null).Select(c => new PdfReportItem(ReportModel(c), c.Result!, ReportOptionsFor(c))).ToList());

    private static bool IsHtml(string path) => path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
    private static readonly (string, string) HtmlChoice = (".html", "HTML report (print from a browser)");

    private string LogoNote(string path) =>
        !IsHtml(path) && !string.IsNullOrWhiteSpace(LogoDataUri) && !PdfReport.LogoSupported(LogoDataUri)
            ? " The logo (SVG) cannot be drawn in PDF; it appears in HTML reports only. Use PNG or JPEG for PDF."
            : "";

    [RelayCommand]
    private async Task ExportReport()
    {
        if (Selected?.Result is null || Dialogs is null) return;
        var path = await Dialogs.PickSaveAsync(SafeName(Selected.DisplayTitle) + ".pdf", ".pdf", "PDF report", HtmlChoice);
        if (path is null) return;
        try
        {
            if (IsHtml(path)) await File.WriteAllTextAsync(path, BuildReportHtml(Selected));
            else await File.WriteAllBytesAsync(path, BuildReportPdf([Selected]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Report not saved: {ex.Message}"; return; }
        StatusMessage = $"Report saved to {path}.{LogoNote(path)}";
        Dialogs.OpenWithShell(path);
    }

    [RelayCommand]
    private async Task ExportAllReports()
    {
        if (Dialogs is null) return;
        if (!BatchCalculations.Any()) { StatusMessage = "No solved calculations are ticked for the batch report."; return; }
        var path = await Dialogs.PickSaveAsync(SafeName(ProjectName) + " reports.pdf", ".pdf", "PDF report (all calculations)", HtmlChoice);
        if (path is null) return;
        var calcs = BatchCalculations.ToList();
        try
        {
            if (IsHtml(path))
            {
                // Batch report: each calculation is a full report separated by a page break.
                var parts = calcs.Select(BuildReportHtml).ToList();
                string Body(string html) { int s = html.IndexOf("<div class=\"page\">", StringComparison.Ordinal); int e = html.LastIndexOf("</body>", StringComparison.Ordinal); return html[s..e]; }
                string head = parts[0][..parts[0].IndexOf("<div class=\"page\">", StringComparison.Ordinal)];
                await File.WriteAllTextAsync(path, head + string.Join("<div style=\"page-break-after:always\"></div>", parts.Select(Body)) + "</body></html>");
            }
            else await File.WriteAllBytesAsync(path, BuildReportPdf(calcs));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Report not saved: {ex.Message}"; return; }
        StatusMessage = $"Batch report ({calcs.Count} calculations) saved to {path}.{LogoNote(path)}";
        Dialogs.OpenWithShell(path);
    }

    [RelayCommand]
    private async Task ExportWorkbook()
    {
        if (Dialogs is null || (Calculations.Count == 0 && Networks.Count == 0)) return;
        var path = await Dialogs.PickSaveAsync(SafeName(ProjectName) + ".xlsx", ".xlsx", "Excel workbook");
        if (path is null) return;
        try { await File.WriteAllBytesAsync(path, BuildWorkbook()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Workbook not saved: {ex.Message}"; return; }
        StatusMessage = $"Workbook saved to {path} (comparison, one sheet per calculation, saved rating tables and storm network pipes, structures and HGL/EGL profile points).";
    }

    public byte[] BuildWorkbook()
    {
        var items = BatchCalculations.Select(c => (c.ToModel() with { Title = c.DisplayTitle }, c.Result!)).ToList();
        var ratings = BatchCalculations.Select(c => (Title: SafeName(c.DisplayTitle) + " rating", Table: c.BuildRatingTable())).Where(t => t.Table is not null).Select(t => (t.Title, t.Table!)).ToList();
        var networks = Networks.Select(n => { n.Solve(); return n.Result; }).Where(r => r is { Succeeded: true }).Select(r => r!).ToList();
        return XlsxWriter.ToBytes(ProjectWorkbook.Build(ToProject().Info, items, ratings, Structures.Select(r => r.Structure).ToList(), networks));
    }

    [RelayCommand]
    private async Task ExportComparisonReport()
    {
        if (Dialogs is null || Calculations.Count == 0) return;
        RefreshComparison();
        var path = await Dialogs.PickSaveAsync(SafeName(ProjectName) + " comparison.pdf", ".pdf", "PDF comparison report", (".html", "HTML comparison report"));
        if (path is null) return;
        var options = new ReportOptions { SoftwareVersion = AppVersion, Watermark = string.IsNullOrWhiteSpace(Watermark) ? null : Watermark, FooterText = string.IsNullOrWhiteSpace(FooterText) ? null : FooterText };
        try
        {
            if (IsHtml(path)) await File.WriteAllTextAsync(path, HtmlReport.RenderComparison(ToProject().Info, Comparison!, options));
            else await File.WriteAllBytesAsync(path, PdfReport.ComparisonToBytes(ToProject().Info, Comparison!, options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Comparison not saved: {ex.Message}"; return; }
        StatusMessage = $"Comparison saved to {path}.";
        Dialogs.OpenWithShell(path);
    }

    // ------------------------------------------------------------ section points (irregular)

    /// <summary>Amount for Shift stations / Shift elevations, in the calculation's length unit.</summary>
    [ObservableProperty] private string _pointShift = "0";

    private bool EditPoints(Func<IReadOnlyList<StationPoint>, IReadOnlyList<StationPoint>> op, string done)
    {
        if (Selected is not { IsIrregular: true } c) { StatusMessage = "Point tools apply to irregular sections."; return false; }
        var parsed = StationPointText.Parse(c.PointsText);
        if (parsed.Errors.Count > 0) { StatusMessage = "Fix the points first: " + parsed.Errors[0]; return false; }
        c.BeginUndoStep();
        c.PointsText = StationPointText.Format(op(parsed.Points));
        c.BeginUndoStep();
        StatusMessage = done;
        return true;
    }

    private bool TryShift(out double d)
    {
        if (CalculationViewModel.TryParse(PointShift, out d)) return true;
        StatusMessage = "Enter the shift amount as a number.";
        return false;
    }

    [RelayCommand] private void ShiftStations() { if (TryShift(out var d)) EditPoints(p => SectionPoints.ShiftStations(p, d), $"Stations shifted by {d.ToString("G10", CultureInfo.CurrentCulture)}."); }
    [RelayCommand] private void ShiftElevations() { if (TryShift(out var d)) EditPoints(p => SectionPoints.ShiftElevations(p, d), $"Elevations shifted by {d.ToString("G10", CultureInfo.CurrentCulture)}."); }
    [RelayCommand] private void ZeroStations() => EditPoints(SectionPoints.ZeroStations, "First station set to 0.");
    [RelayCommand] private void ZeroElevations() => EditPoints(SectionPoints.ZeroElevations, "Lowest point set to elevation 0.");
    [RelayCommand] private void SortPoints() => EditPoints(SectionPoints.SortByStation, "Points sorted by station.");
    [RelayCommand] private void ReversePoints() => EditPoints(SectionPoints.Reverse, "Section mirrored (left and right swapped).");

    /// <summary>Replaces a rectangular, trapezoidal or triangular section by the equivalent station/elevation points.</summary>
    [RelayCommand]
    private void ConvertToIrregular()
    {
        if (Selected is null) return;
        var section = Selected.ToModel().Input.Section;
        if (!SectionPoints.CanConvertToIrregular(section)) { StatusMessage = "Only rectangular, trapezoidal and triangular channels can be converted to points."; return; }
        double height = section.ChannelDepth is double cd && cd > 0 && double.IsFinite(cd) ? cd : DefaultHeight(Selected.Result);
        var pts = SectionPoints.FromRegular(section, height);
        Selected.BeginUndoStep();
        Selected.PointsText = StationPointText.Format(pts);
        Selected.Shape = SectionShape.Irregular;
        Selected.BeginUndoStep();
        StatusMessage = $"Converted to {pts.Count} points, {height.ToString("G6", CultureInfo.CurrentCulture)} {Selected.LengthUnit} high" +
                        (section.ChannelDepth is null ? " (no channel depth was set, so the height is 1.5 × the larger of yn and yc, rounded up)." : " (the channel depth).");
    }

    private static double DefaultHeight(Hydraulics.UniformFlowResult? r)
    {
        double y = r is { Succeeded: true } ? Math.Max(r.Normal.Depth, r.Critical.Depth) : double.NaN;
        if (!(y > 0) || !double.IsFinite(y)) return 1;
        double h = 1.5 * y, mag = Math.Pow(10, Math.Floor(Math.Log10(h)) - 1);
        return Math.Ceiling(h / mag) * mag;
    }

    /// <summary>Reads points from a text/CSV file (same rules as pasting) into the selected irregular section.</summary>
    [RelayCommand]
    private async Task ImportPoints()
    {
        if (Dialogs is null || Selected is not { IsIrregular: true }) { StatusMessage = "Select an irregular section to import points into (or use Import LandXML…)."; return; }
        var path = await Dialogs.PickOpenFileAsync("Import section points", "Text or CSV", "*.csv", "*.txt", "*.tsv", "*.dat");
        if (path is null) return;
        string text;
        try { text = await File.ReadAllTextAsync(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Could not read {path}: {ex.Message}"; return; }
        var parsed = StationPointText.Parse(text);
        if (parsed.Errors.Count > 0) { StatusMessage = $"Not imported: {parsed.Errors.Count} problem(s). {parsed.Errors[0]}"; return; }
        Selected.BeginUndoStep();
        Selected.PointsText = StationPointText.Format(parsed.Points);
        Selected.BeginUndoStep();
        StatusMessage = $"Imported {parsed.Points.Count} points from {Path.GetFileName(path)}.";
    }

    [RelayCommand]
    private async Task ExportPoints()
    {
        if (Dialogs is null || Selected is not { IsIrregular: true } c) return;
        var parsed = StationPointText.Parse(c.PointsText);
        if (parsed.Errors.Count > 0) { StatusMessage = "Fix the points first: " + parsed.Errors[0]; return; }
        var path = await Dialogs.PickSaveAsync(SafeName(c.DisplayTitle) + " points.csv", ".csv", "CSV file");
        if (path is null) return;
        try { await File.WriteAllTextAsync(path, StationPointText.ToCsv(parsed.Points)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Points not saved: {ex.Message}"; return; }
        StatusMessage = $"{parsed.Points.Count} points saved to {path}.";
    }

    /// <summary>
    /// Imports every cross-section in a LandXML file (e.g. Civil 3D sample lines) as a new irregular
    /// calculation, in a folder named after the alignment. Values are not converted.
    /// </summary>
    [RelayCommand]
    private async Task ImportLandXml()
    {
        if (Dialogs is null) return;
        var path = await Dialogs.PickOpenFileAsync("Import cross-sections from LandXML", "LandXML", "*.xml", "*.landxml");
        if (path is null) return;
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = $"Could not read {path}: {ex.Message}"; return; }
        StatusMessage = ImportLandXmlBytes(bytes, Path.GetFileName(path));
    }

    /// <summary>Imports cross-sections and pipe networks from one LandXML file; returns the status text. Public for tests.</summary>
    public string ImportLandXmlBytes(byte[] bytes, string fileName)
    {
        var sections = LandXmlCrossSections.Read(new MemoryStream(bytes));
        var networks = LandXmlPipeNetworks.Read(new MemoryStream(bytes));
        var alignments = LandXmlAlignments.Read(new MemoryStream(bytes));
        bool hasSections = sections.Sections.Count > 0, hasNetworks = networks.Pipes.Count > 0 || networks.Structures.Count > 0, hasAlignments = alignments.Alignments.Count > 0;
        if (!hasSections && !hasNetworks && !hasAlignments)
            return $"Nothing imported from {fileName}: it has no cross-sections, pipe networks or alignments. " + (sections.Problems.FirstOrDefault(p => !p.Contains("no cross-sections")) ?? "");
        var parts = new List<string>();
        if (hasSections) parts.Add(ImportCrossSections(sections, fileName));
        if (hasNetworks) parts.Add(ImportPipeNetwork(networks, fileName));
        if (hasAlignments || alignments.Problems.Count > 0) parts.Add(ApplyAlignments(alignments, fileName));
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Stations every structure with coordinates (imported list and storm networks) on the nearest alignment in
    /// the file; returns the status text. Alignments are not kept; import again after they change. Public for tests.
    /// </summary>
    public string ApplyAlignments(LandXmlAlignmentReadResult read, string fileName)
    {
        if (read.Alignments.Count == 0)
            return "No alignments imported. " + (read.Problems.FirstOrDefault() ?? "");
        int n = 0;
        for (int i = 0; i < Structures.Count; i++)
        {
            var s = Structures[i].Structure;
            if (s.Northing is not { } north || s.Easting is not { } east) continue;
            if (Alignment.Nearest(read.Alignments, north, east) is not { } so) continue;
            Structures[i] = new StructureRow(s with { Alignment = so.Alignment, Station = so.Station, Offset = so.Offset });
            n++;
        }
        int m = Networks.Sum(net => net.ApplyStations((north, east) => Alignment.Nearest(read.Alignments, north, east)));
        if (n + m > 0) MarkDirty();
        string probs = read.Problems.Count > 0 ? $" {read.Problems.Count} alignment problem(s), first: {read.Problems[0]}" : "";
        return $"{read.Alignments.Count} alignment(s) from {fileName} ({string.Join(", ", read.Alignments.Select(a => a.Name))}): stationed {n} listed structure(s) and {m} network structure(s).{probs}";
    }

    /// <summary>
    /// Adds one pipe calculation per circular or elliptical pipe (folder "network/Pipes", item no. = pipe
    /// name, slope from the inverts, depth set to the full rise so Solve for Q gives full-flow capacity)
    /// and lists the network's structures. Values are not converted. Returns the status text.
    /// </summary>
    public string ImportPipeNetwork(LandXmlNetworkReadResult read, string fileName)
    {
        if (read.Pipes.Count == 0 && read.Structures.Count == 0)
            return "No pipe network imported. " + (read.Problems.Count > 0 ? read.Problems[0] : "");
        var units = read.Units ?? Selected?.Units ?? UnitSystem.USCustomary;
        var inv = CultureInfo.InvariantCulture;
        CalculationViewModel? first = null;
        foreach (var p in read.Pipes)
        {
            ChannelSection section = p.Shape == NetworkPipeShape.Circular ? new CircularSection(p.Rise) : new EllipticalSection(p.Rise, p.Span);
            string L(double? v) => v is { } x ? x.ToString("0.###", inv) : "?";
            var model = new ChannelCalculation
            {
                Title = $"{p.Name} ({p.StartStructure} → {p.EndStructure})",
                Folder = (p.Network.Length > 0 ? p.Network + "/" : "") + "Pipes",
                ItemNumber = p.Name,
                Description = $"Pipe imported from {fileName}, network \"{p.Network}\": {p.StartStructure} → {p.EndStructure}, length {L(p.Length)}, inverts {L(p.StartInvert)} → {L(p.EndInvert)}" +
                              (p.Material.Length > 0 ? $", {p.Material}" : "") + ". Slope from the inverts; n = 0.013 assumed (check for the material). Depth is set to the full rise, so Solve for Q gives the full-flow capacity; enter the design Q and solve for normal depth to check it.",
                Input = new UniformFlowInput
                {
                    Units = units, Section = section, SolveFor = SolveFor.Discharge, ManningN = 0.013,
                    Slope = p.Slope ?? double.NaN, Depth = p.Rise, Discharge = double.NaN, InvertElevation = p.StartInvert,
                },
            };
            var c = CalculationViewModel.FromModel(model);
            c.ClearHistory();
            Add(c);
            first ??= c;
        }
        foreach (var s in read.Structures)
        {
            var existing = Structures.FirstOrDefault(r => r.Structure.Network == s.Network && r.Structure.Name == s.Name);
            if (existing is not null) Structures.Remove(existing);
            Structures.Add(new StructureRow(s));
        }
        OnPropertyChanged(nameof(HasStructures));
        int nets = AddNetworksFromImport(read, units);
        if (first is not null) Selected = first;
        MarkDirty();
        RefreshComparison();
        int inlets = read.Structures.Count(s => s.Kind == "Inlet");
        string unitNote = read.Units is null ? $" Unit system not stated in the file; set to {(units == UnitSystem.SI ? "SI" : "U.S. customary")}, please check." : "";
        var other = read.Problems.Where(p => !p.StartsWith("The file does not state") && !p.StartsWith("Linear unit")).ToList();
        string probNote = other.Count > 0 ? $" {other.Count} problem(s), first: {other[0]}" : "";
        string netNote = nets > 0 ? $" {nets} storm network(s) set up for HGL/EGL (Storm networks window; enter the inflow at each inlet, or a Q for each pipe)." : "";
        return $"Imported {read.Pipes.Count} pipe(s) and {read.Structures.Count} structure(s) ({inlets} inlet(s)) from {fileName}.{netNote}{unitNote}{probNote}";
    }

    /// <summary>Adds one calculation per cross-section; returns the status text. Public for tests.</summary>
    public string ImportCrossSections(LandXmlReadResult read, string fileName)
    {
        if (read.Sections.Count == 0)
            return "No cross-sections imported. " + (read.Problems.Count > 0 ? read.Problems[0] : "");
        var units = read.Units ?? Selected?.Units ?? UnitSystem.USCustomary;
        CalculationViewModel? first = null;
        foreach (var x in read.Sections)
        {
            var c = new CalculationViewModel
            {
                Title = $"{(x.Alignment.Length > 0 ? x.Alignment + " " : "")}{LandXmlCrossSections.StationText(x.Station)}{(x.Surface.Length > 0 ? " (" + x.Surface + ")" : "")}",
                Folder = x.Alignment,
                Description = $"Cross-section imported from {fileName}: alignment \"{x.Alignment}\", station {LandXmlCrossSections.StationText(x.Station)}, surface \"{x.Surface}\". Set n, slope and discharge.",
                Units = units,
                ManningN = "0.035",
                PointsText = StationPointText.Format(x.Points),
                Shape = SectionShape.Irregular,
            };
            c.ClearHistory();
            Add(c);
            first ??= c;
        }
        Selected = first;
        MarkDirty();
        RefreshComparison();
        string unitNote = read.Units is null ? $" Unit system not stated in the file; set to {(units == UnitSystem.SI ? "SI" : "U.S. customary")}, please check." : "";
        string probNote = read.Problems.Count > 0 && read.Units is not null ? $" {read.Problems.Count} problem(s), first: {read.Problems[0]}" :
                          read.Problems.Count > 1 ? $" {read.Problems.Count - 1} other problem(s), first: {read.Problems.First(p => !p.StartsWith("The file does not state") && !p.StartsWith("Linear unit"))}" : "";
        return $"Imported {read.Sections.Count} cross-section(s) from {fileName}.{unitNote}{probNote}";
    }

    private static string SafeName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(s.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "MasterFlow" : cleaned;
    }
}

/// <summary>Display row for an imported drainage structure.</summary>
public sealed record StructureRow(DrainageStructure Structure)
{
    private static string F(double? v) => v is { } x ? x.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture) : "—";
    public string Name => Structure.Name;
    public string Kind => Structure.Kind;
    public string Network => Structure.Network;
    public string Location => Structure.Northing is null ? "—" : $"N {F(Structure.Northing)}  E {F(Structure.Easting)}";
    public string Elevations => $"Rim {F(Structure.RimElevation)} · Sump {F(Structure.SumpElevation)}";
    public string Inverts => string.Join("; ", Structure.Inverts.Select(i => $"{i.Pipe} {i.FlowDirection} {F(i.Elevation)}"));
    public string StationText => Structure.Station is { } st ? $"{Structure.Alignment}  station {F(st)}  offset {F(Structure.Offset)} {(Structure.Offset >= 0 ? "R" : "L")}" : "";
    public bool HasStation => Structure.Station is not null;
}
