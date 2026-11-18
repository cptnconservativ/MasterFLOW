using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public DesignToolsViewModel(MainViewModel main) { _main = main; _inputText = ""; }

    /// <summary>Selects the first tool when none is selected (the window calls this when it opens, so the sample uses the current network).</summary>
    public void EnsureSelection() { SelectedTool ??= Tools.FirstOrDefault(); }

    public IReadOnlyList<IDesignTool> Tools { get; } = DesignTools.All;
    [ObservableProperty] private IDesignTool? _selectedTool;
    [ObservableProperty] private string _inputText;
    [ObservableProperty] private ToolResult? _result;
    [ObservableProperty] private string _resultText = "Choose a tool, edit the input (Sample fills in an example) and press Run.";
    [ObservableProperty] private bool _canApply;
    public ObservableCollection<string> Messages { get; } = [];

    public string Description => SelectedTool?.Description ?? "";

    public DesignToolContext Context()
    {
        var net = _main.SelectedNetwork;
        StormNetwork? model = null;
        if (net is not null) { var (m, errs) = net.BuildModel(); if (errs.Count == 0) model = m; }
        return new DesignToolContext(model?.Units ?? _main.Selected?.Units ?? UnitSystem.USCustomary, model, net?.Result);
    }

    partial void OnSelectedToolChanged(IDesignTool? oldValue, IDesignTool? newValue)
    {
        if (oldValue is not null) _inputs[oldValue.Id] = InputText;
        _switching = true;
        InputText = newValue is null ? "" : _inputs.TryGetValue(newValue.Id, out var t) ? t : newValue.SampleInput(Context());
        _switching = false;
        Result = null; CanApply = false; Messages.Clear();
        ResultText = "Press Run.";
        OnPropertyChanged(nameof(Description));
    }

    partial void OnInputTextChanged(string value)
    {
        if (_switching || SelectedTool is null) return;
        _inputs[SelectedTool.Id] = value;
        _main.MarkDirtyFromTools();
    }

    [RelayCommand]
    private void LoadSample()
    {
        if (SelectedTool is null) return;
        InputText = SelectedTool.SampleInput(Context());
    }

    [RelayCommand]
    public void Run()
    {
        if (SelectedTool is null) return;
        ToolResult r;
        try { r = SelectedTool.Run(InputText, Context()); }
        catch (Exception ex) { r = ToolResult.Failed(SelectedTool.Name, Hydraulics.CalcMessage.Error("E-TL-999", "The tool stopped with an internal error: " + ex.Message)); }
        Result = r;
        ResultText = DesignTools.ToText(r);
        Messages.Clear();
        foreach (var m in r.Messages) Messages.Add($"{m.Severity} [{m.Code}] {m.Text}");
        CanApply = r.UpdatedNetwork is not null && _main.SelectedNetwork is not null;
        _main.StatusMessage = r.Succeeded ? $"{SelectedTool.Name}: done." + (CanApply ? " " + r.UpdateDescription + " Press Apply to network to use it." : "")
                                          : $"{SelectedTool.Name}: {r.Messages.Count(m => m.Severity == Hydraulics.MessageSeverity.Error)} error(s); see Messages.";
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

    [RelayCommand]
    private async Task SaveReport()
    {
        if (Result is null || _main.Dialogs is null) return;
        var path = await _main.Dialogs.PickSaveAsync(Safe(SelectedTool?.Name ?? "tool") + ".html", ".html", "HTML report", (".pdf", "PDF report"), (".xlsx", "Excel workbook"));
        if (path is null) return;
        if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) await File.WriteAllBytesAsync(path, BuildWorkbook()!);
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
        Result = null; CanApply = false; Messages.Clear();
    }
}
