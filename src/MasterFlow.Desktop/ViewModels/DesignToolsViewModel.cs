using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Reporting;

namespace MasterFlow.Desktop.ViewModels;

/// <summary>
/// The Design tools window: pick a tool, edit its text input (kept per tool and saved with the project), Run,
/// read the result, copy it, save it as HTML or Excel, and apply a network change when the tool offers one.
/// </summary>
public sealed partial class DesignToolsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Dictionary<string, string> _inputs = new();
    private bool _switching;

    public DesignToolsViewModel(MainViewModel main) { _main = main; _inputText = ""; RebuildToolList(); }

    /// <summary>Selects the first tool when none is selected (the window calls this when it opens, so the sample uses the current network).</summary>
    public void EnsureSelection() { SelectedTool ??= Tools.FirstOrDefault(); }

    public IReadOnlyList<IDesignTool> Tools { get; } = DesignTools.All;
    [ObservableProperty] private IDesignTool? _selectedTool;
    [ObservableProperty] private string _inputText;
    [ObservableProperty] private ToolResult? _result;
    [ObservableProperty] private string _resultText = "Choose a tool, edit the input (Sample fills in an example) and press Run.";
    [ObservableProperty] private bool _canApply;
    public ObservableCollection<string> Messages { get; } = [];

    public string Description => SelectedTool is null ? "" : (PremiumFeatures.IsPremium(SelectedTool) ? "★ Premium. " : "") + SelectedTool.Description;

    /// <summary>Selected tool's heading (name and category) for the panel above the input.</summary>
    public string Heading => SelectedTool is null ? "No tool selected" : SelectedTool.Name;
    public string CategoryText => SelectedTool is null ? "" : DesignToolCatalog.Category(SelectedTool);

    /// <summary>True when the selected Premium tool cannot run under the current license; the window shows how to unlock it.</summary>
    public bool IsLocked => SelectedTool is not null && PremiumFeatures.ForTool(SelectedTool.Id) is { } f && !_main.License.Allows(f.Id);
    public string LockedText => SelectedTool is not null && PremiumFeatures.ForTool(SelectedTool.Id) is { } f
        ? $"This is a Premium tool, part of \u201c{f.Name}\u201d. You can edit the input and load the sample; Run needs a Premium license key, entered under License at the foot of the tool list."
        : "";

    // ---- Tool list: grouped by subject, searchable, with Premium and locked marks ----

    /// <summary>One row of the tool list: a category heading (Tool is null) or a tool.</summary>
    public sealed record ToolListItem(string Text, IDesignTool? Tool, string Badge)
    {
        public bool IsHeader => Tool is null;
        public bool IsTool => Tool is not null;
        public bool HasBadge => Badge.Length > 0;
    }

    public ObservableCollection<ToolListItem> ToolList { get; } = [];
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ToolListItem? _selectedListItem;
    public bool NoToolsMatch => ToolList.Count == 0;

    partial void OnSearchTextChanged(string value) => RebuildToolList();

    partial void OnSelectedListItemChanged(ToolListItem? value)
    {
        if (value?.Tool is { } t) { if (!ReferenceEquals(t, SelectedTool)) SelectedTool = t; }
        else if (value is not null) SelectedListItem = ToolList.FirstOrDefault(i => ReferenceEquals(i.Tool, SelectedTool));   // headings are not selectable
    }

    private void RebuildToolList()
    {
        ToolList.Clear();
        string? last = null;
        foreach (var (cat, tool) in DesignToolCatalog.Grouped(Tools, SearchText))
        {
            if (cat != last) { ToolList.Add(new ToolListItem(cat.ToUpperInvariant(), null, "")); last = cat; }
            string badge = PremiumFeatures.ForTool(tool.Id) is not { } f ? "" : _main.License.Allows(f.Id) ? "★" : "★ locked";
            ToolList.Add(new ToolListItem(tool.Name, tool, badge));
        }
        SelectedListItem = ToolList.FirstOrDefault(i => i.Tool is not null && ReferenceEquals(i.Tool, SelectedTool));
        OnPropertyChanged(nameof(NoToolsMatch));
    }

    /// <summary>Enter in the search box: pick the first tool whose name matches, else the first match of any kind.</summary>
    public void SelectFirstMatch()
    {
        var tools = ToolList.Where(i => i.Tool is not null).ToList();
        var byName = tools.FirstOrDefault(i => SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => i.Text.Contains(w, StringComparison.OrdinalIgnoreCase)));
        if ((byName ?? tools.FirstOrDefault()) is { } first) SelectedListItem = first;
    }

    /// <summary>True after the input changed since the last Run, so the result on screen no longer matches it.</summary>
    [ObservableProperty] private bool _isStale;

    /// <summary>License key box (Premium); Activate checks it offline and keeps it in the user settings.</summary>
    [ObservableProperty] private string _licenseKeyText = "";
    public string LicenseSummary => _main.License.Summary;

    public void RefreshLicense()
    {
        OnPropertyChanged(nameof(LicenseSummary));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(LockedText));
        RebuildToolList();
    }

    [RelayCommand]
    private void ActivateLicense()
    {
        var state = _main.ActivateLicense(LicenseKeyText);
        _main.StatusMessage = state.Status switch
        {
            LicenseStatus.Valid => state.Summary,
            LicenseStatus.None => "License key cleared: MasterFlow Free.",
            _ => state.Message,
        };
        if (state.Status is LicenseStatus.Valid or LicenseStatus.None) LicenseKeyText = "";
        RefreshLicense();
    }

    public DesignToolContext Context()
    {
        var net = _main.SelectedNetwork;
        StormNetwork? model = null;
        if (net is not null) { var (m, errs) = net.BuildModel(); if (errs.Count == 0) model = m; }
        var project = _main.ToProject();
        return new DesignToolContext(model?.Units ?? _main.Selected?.Units ?? UnitSystem.USCustomary, model, net?.Result)
        {
            Project = project.Info,
            ToolInputs = SavedInputs(),
            Calculations = project.Calculations,
            Networks = _main.Networks.Select(n => { var (m, e) = n.BuildModel(); return (Model: e.Count == 0 ? m : null, n.Result); })
                .Where(x => x.Model is not null).Select(x => (x.Model!, x.Result)).ToList(),
            // Tools run from inside another tool (the report builder) follow the same license gate as the Run button.
            MayRun = t => PremiumFeatures.ForTool(t.Id) is not { } f || _main.License.Allows(f.Id),
            LoadProject = MasterFlow.Persistence.ProjectFile.Load,
        };
    }

    partial void OnSelectedToolChanged(IDesignTool? oldValue, IDesignTool? newValue)
    {
        if (oldValue is not null) _inputs[oldValue.Id] = InputText;
        _switching = true;
        InputText = newValue is null ? "" : _inputs.TryGetValue(newValue.Id, out var t) ? t : newValue.SampleInput(Context());
        _switching = false;
        Result = null; CanApply = false; Messages.Clear(); IsStale = false;
        ResultText = "Press Run (F5 or Ctrl+Enter) to calculate. Sample replaces the input with a worked example.";
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(CategoryText));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(LockedText));
        if (SelectedListItem?.Tool != newValue) SelectedListItem = ToolList.FirstOrDefault(i => i.Tool is not null && ReferenceEquals(i.Tool, newValue));
    }

    partial void OnInputTextChanged(string value)
    {
        if (_switching || SelectedTool is null) return;
        _inputs[SelectedTool.Id] = value;
        if (Result is not null) IsStale = true;
        _main.MarkDirtyFromTools();
    }

    [RelayCommand]
    private async Task LoadSample()
    {
        if (SelectedTool is null) return;
        var sample = SelectedTool.SampleInput(Context());
        // Edited input is project data: don't throw it away on a stray click.
        if (!string.IsNullOrWhiteSpace(InputText) && InputText.Trim() != sample.Trim() && _main.Dialogs is { } d
            && !await d.ConfirmDiscardAsync($"Replace your {SelectedTool.Name} input with the worked example? Your edits will be lost."))
            return;
        InputText = sample;
    }

    [RelayCommand]
    public void Run()
    {
        if (SelectedTool is null) return;
        ToolResult r;
        try { r = PremiumFeatures.Run(SelectedTool, InputText, Context(), _main.License); }
        catch (Exception ex) { r = ToolResult.Failed(SelectedTool.Name, Hydraulics.CalcMessage.Error("E-TL-999", "The tool stopped with an internal error: " + ex.Message)); }
        Result = r;
        IsStale = false;
        ResultText = DesignTools.ToText(r);
        Messages.Clear();
        foreach (var m in r.Messages) Messages.Add($"{m.Severity} [{m.Code}] {m.Text}");
        CanApply = r.UpdatedNetwork is not null && _main.SelectedNetwork is not null;
        _main.StatusMessage = r.Succeeded ? $"{SelectedTool.Name}: done." + (CanApply ? " " + r.UpdateDescription + " Press Apply to network to use it." : "")
                                          : $"{SelectedTool.Name}: {r.Messages.Count(m => m.Severity == Hydraulics.MessageSeverity.Error)} error(s); see Messages.";
    }

    /// <summary>Shows the full report (tables, charts and figures) in the default browser, without asking for a file name.</summary>
    [RelayCommand]
    private async Task OpenReport()
    {
        if (Result is null || _main.Dialogs is null) return;
        var dir = Path.Combine(Path.GetTempPath(), "MasterFlow", "reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Safe(SelectedTool?.Name ?? "tool") + ".html");
        await File.WriteAllTextAsync(path, BuildHtml()!);
        _main.Dialogs.OpenWithShell(path);
        _main.StatusMessage = "Report opened in your browser. Use Save… to keep a copy with the project.";
    }

    [RelayCommand]
    public void ApplyToNetwork()
    {
        if (Result?.UpdatedNetwork is not { } n || _main.SelectedNetwork is not { } vm) return;
        vm.ApplyChanges(n, Result.UpdateDescription ?? "Updated by a design tool.");
        CanApply = false;
        _main.StatusMessage = (Result.UpdateDescription ?? "Network updated.") + " The network was re-solved.";
    }

    [RelayCommand]
    private async Task Copy()
    {
        if (Result is null || _main.Dialogs is null) return;
        await _main.Dialogs.SetClipboardAsync(DesignTools.ToText(Result) + "\n" + DesignTools.ToDelimited(Result));
        _main.StatusMessage = "Tool result copied (text, then tab-separated tables).";
    }

    public string? BuildHtml() => Result is null ? null : ToolReport.ToHtml(_main.ToProject().Info, Result, InputText, MainViewModel.AppVersion);
    public byte[]? BuildPdf() => Result is null ? null : ToolReport.ToPdf(_main.ToProject().Info, Result, InputText, _main.NetworkReportOptions());
    public byte[]? BuildWorkbook() => Result is null ? null : XlsxWriter.ToBytes(ToolReport.ToSheets(Result));
    public byte[]? BuildDocx() => Result?.Report is { } doc ? DrainageReportWriter.ToDocx(_main.ToProject().Info, doc, _main.NetworkReportOptions()) : null;

    [RelayCommand]
    private async Task SaveReport()
    {
        if (Result is null || _main.Dialogs is null) return;
        var files = Result.Files.Select(f => (Path.GetExtension(f.FileName), f.Description)).ToArray();
        string? path;
        if (Result.Report is not null)
            path = await _main.Dialogs.PickSaveAsync(Safe(Result.Report.Title) + ".pdf", ".pdf", "PDF report", (".docx", "Word document"), (".html", "HTML report"), (".xlsx", "Excel workbook"));
        else if (Result.Files.Count > 0)   // a tool that makes a drawing offers it first
            path = await _main.Dialogs.PickSaveAsync(Result.Files[0].FileName, Path.GetExtension(Result.Files[0].FileName), Result.Files[0].Description,
                [.. files.Skip(1), (".html", "HTML report"), (".pdf", "PDF report"), (".xlsx", "Excel workbook")]);
        else
            path = await _main.Dialogs.PickSaveAsync(Safe(SelectedTool?.Name ?? "tool") + ".html", ".html", "HTML report", (".pdf", "PDF report"), (".xlsx", "Excel workbook"));
        if (path is null) return;
        if (Result.Files.FirstOrDefault(f => path.EndsWith(Path.GetExtension(f.FileName), StringComparison.OrdinalIgnoreCase)) is { } file)
        {
            await File.WriteAllBytesAsync(path, file.Content);
            _main.StatusMessage = $"Saved {path}.";
            return;
        }
        if (path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) await File.WriteAllBytesAsync(path, BuildDocx()!);
        else if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) await File.WriteAllBytesAsync(path, BuildWorkbook()!);
        else if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) await File.WriteAllBytesAsync(path, BuildPdf()!);
        else await File.WriteAllTextAsync(path, BuildHtml()!);
        _main.StatusMessage = $"Saved {path}.";
    }

    private static string Safe(string s) => new(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());

    public IReadOnlyDictionary<string, string> SavedInputs()
    {
        if (SelectedTool is not null) _inputs[SelectedTool.Id] = InputText;
        return new Dictionary<string, string>(_inputs);
    }

    public void LoadInputs(IReadOnlyDictionary<string, string> inputs)
    {
        _inputs.Clear();
        foreach (var (k, v) in inputs) _inputs[k] = v;
        _switching = true;
        InputText = SelectedTool is null ? "" : _inputs.TryGetValue(SelectedTool.Id, out var t) ? t : "";
        _switching = false;
        Result = null; CanApply = false; Messages.Clear(); IsStale = false;
    }
}
