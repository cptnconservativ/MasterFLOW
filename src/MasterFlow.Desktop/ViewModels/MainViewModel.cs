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
    Task<string?> PickSaveAsync(string suggestedName, string extension, string description);
    Task SetClipboardAsync(string text);
    void OpenWithShell(string path);
    Task<bool> ConfirmDiscardAsync(string message);
}

public sealed partial class MainViewModel : ObservableObject
{
    public const string AppVersion = "0.4.0";

    public ObservableCollection<CalculationViewModel> Calculations { get; } = [];

    [ObservableProperty] private CalculationViewModel? _selected;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _statusMessage = "Ready";

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

    // Rating table
    [ObservableProperty] private RatingTableMode _ratingMode = RatingTableMode.DischargeFromDepth;
    [ObservableProperty] private string _ratingStart = "0.5";
    [ObservableProperty] private string _ratingEnd = "5";
    [ObservableProperty] private string _ratingIncrement = "0.5";
    [ObservableProperty] private RatingTable? _ratingTable;
    [ObservableProperty] private string _ratingStatus = "Choose a range and press Generate.";

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
    partial void OnRatingModeChanged(RatingTableMode value) { OnPropertyChanged(nameof(RatingStartLabel)); OnPropertyChanged(nameof(RatingEndLabel)); }

    partial void OnSelectedChanged(CalculationViewModel? value)
    {
        RatingTable = null;
        RatingStatus = "Choose a range and press Generate.";
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(RatingStartLabel));
        OnPropertyChanged(nameof(RatingEndLabel));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool _loading;
    private void MarkDirty() { if (!_loading) IsDirty = true; }

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
        Selected = Calculations.FirstOrDefault();
        FilePath = path;
        _loading = false;
        IsDirty = false;
    }

    private void Add(CalculationViewModel c)
    {
        c.Edited += OnCalcEdited;
        Calculations.Add(c);
    }

    private void OnCalcEdited(object? sender, EventArgs e)
    {
        MarkDirty();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public Project ToProject() => new()
    {
        Info = new ProjectInfo { Name = ProjectName, ProjectNumber = ProjectNumber, Client = Client, PreparedBy = PreparedBy, CheckedBy = CheckedBy, Notes = Notes },
        Calculations = Calculations.Select(c => c.ToModel()).ToList(),
    };

    public void LoadFrom(string path)
    {
        try
        {
            LoadProject(ProjectFile.Load(path), path);
            StatusMessage = $"Opened {path}";
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

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is null) return;
        if (Dialogs is not null && !await Dialogs.ConfirmDiscardAsync($"Delete \"{Selected.Title}\"? You can undo by closing without saving.")) return;
        int i = Calculations.IndexOf(Selected);
        Selected.Edited -= OnCalcEdited;
        Calculations.RemoveAt(i);
        Selected = Calculations.Count == 0 ? null : Calculations[Math.Min(i, Calculations.Count - 1)];
        MarkDirty();
    }

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

    // ------------------------------------------------------------ rating table

    [RelayCommand]
    private void GenerateRatingTable()
    {
        if (Selected?.Result is not { } r) return;
        bool ok = CalculationViewModel.TryParse(RatingStart, out double s) & CalculationViewModel.TryParse(RatingEnd, out double e) & CalculationViewModel.TryParse(RatingIncrement, out double inc);
        if (!ok) { RatingTable = null; RatingStatus = "Start, end and increment must be numbers."; return; }
        var table = RatingTableBuilder.Build(new RatingTableRequest(r.Input, RatingMode, s, e, inc));
        RatingTable = table;
        var errors = table.Messages.Where(m => m.Severity == Hydraulics.MessageSeverity.Error).ToList();
        RatingStatus = errors.Count > 0
            ? $"{table.Rows.Count} rows. {errors.Count} problem(s): {errors[0].Text}"
            : $"{table.Rows.Count} rows." + (table.Messages.Count > 0 ? " " + table.Messages[0].Text : "");
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

    public string BuildReportHtml(CalculationViewModel calc) =>
        HtmlReport.Render(ToProject().Info, calc.ToModel(), calc.Result!, new ReportOptions
        {
            SoftwareVersion = AppVersion,
            Watermark = string.IsNullOrWhiteSpace(Watermark) ? null : Watermark,
            FooterText = string.IsNullOrWhiteSpace(FooterText) ? null : FooterText,
            RatingTable = ReferenceEquals(calc, Selected) ? RatingTable : null,
        });

    [RelayCommand]
    private async Task ExportReport()
    {
        if (Selected?.Result is null || Dialogs is null) return;
        var path = await Dialogs.PickSaveAsync(SafeName(Selected.Title) + ".html", ".html", "HTML report");
        if (path is null) return;
        await File.WriteAllTextAsync(path, BuildReportHtml(Selected));
        StatusMessage = $"Report saved to {path}. Opening it — use the browser's Print → Save as PDF.";
        Dialogs.OpenWithShell(path);
    }

    [RelayCommand]
    private async Task ExportAllReports()
    {
        if (Dialogs is null || Calculations.Count == 0) return;
        var path = await Dialogs.PickSaveAsync(SafeName(ProjectName) + " reports.html", ".html", "HTML report (all calculations)");
        if (path is null) return;
        // Batch report: each calculation is a full report separated by a page break.
        var parts = Calculations.Select(c => BuildReportHtml(c)).ToList();
        string Body(string html) { int s = html.IndexOf("<div class=\"page\">", StringComparison.Ordinal); int e = html.LastIndexOf("</body>", StringComparison.Ordinal); return html[s..e]; }
        string head = parts[0][..parts[0].IndexOf("<div class=\"page\">", StringComparison.Ordinal)];
        var all = head + string.Join("<div style=\"page-break-after:always\"></div>", parts.Select(Body)) + "</body></html>";
        await File.WriteAllTextAsync(path, all);
        StatusMessage = $"Batch report ({parts.Count} calculations) saved to {path}.";
        Dialogs.OpenWithShell(path);
    }

    private static string SafeName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(s.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "MasterFlow" : cleaned;
    }
}
