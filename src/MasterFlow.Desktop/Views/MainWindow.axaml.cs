using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MasterFlow.Desktop.ViewModels;
using MasterFlow.Persistence;

namespace MasterFlow.Desktop.Views;

public partial class MainWindow : Window, IDialogService
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (DataContext is MainViewModel vm) vm.Dialogs = this; };
        // Autosave a recovery copy every minute while there are unsaved changes.
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += (_, _) => (DataContext as MainViewModel)?.Autosave();
        timer.Start();
        Closing += OnClosing;
    }

    private bool _closeConfirmed;
    private NetworkWindow? _networks;

    private DesignToolsWindow? _tools;

    private void OnOpenTools(object? sender, RoutedEventArgs e)
    {
        if (_tools is { } open) { open.Activate(); return; }
        _tools = new DesignToolsWindow { DataContext = DataContext };
        _tools.Closed += (_, _) => _tools = null;
        _tools.Show(this);
    }

    private void OnOpenNetworks(object? sender, RoutedEventArgs e)
    {
        if (_networks is { } open) { open.Activate(); return; }
        _networks = new NetworkWindow { DataContext = DataContext };
        _networks.Closed += (_, _) => _networks = null;
        _networks.Show(this);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainViewModel vm) return;
        e.Cancel = true;
        if (await vm.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    public async Task<string?> PickImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a logo image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickOpenFileAsync(string title, string description, params string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(description) { Patterns = patterns }, new FilePickerFileType("All files") { Patterns = ["*"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickOpenProjectAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open MasterFlow project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("MasterFlow project") { Patterns = ["*" + ProjectFile.Extension] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveAsync(string suggestedName, string extension, string description, params (string Extension, string Description)[] alternatives)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save",
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(description) { Patterns = ["*" + extension] },
                               .. alternatives.Select(a => new FilePickerFileType(a.Description) { Patterns = ["*" + a.Extension] })],
        });
        return file?.TryGetLocalPath();
    }

    public async Task SetClipboardAsync(string text)
    {
        if (Clipboard is { } cb) await cb.SetTextAsync(text);
    }

    public void OpenWithShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* no associated viewer; file is still saved */ }
    }

    public async Task<bool> ConfirmDiscardAsync(string message)
    {
        bool result = false;
        var dialog = new Window
        {
            Title = "MasterFlow", Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "Continue", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => { result = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16), Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }

    public async Task<RecoveryChoice> AskRecoveryAsync(string message)
    {
        var result = RecoveryChoice.SetAside; // closing the dialog keeps the work
        var dialog = new Window
        {
            Title = "MasterFlow - recover unsaved work", Width = 460, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var restore = new Button { Content = "Restore", IsDefault = true };
        var keep = new Button { Content = "Keep for later", IsCancel = true };
        var discard = new Button { Content = "Discard" };
        restore.Click += (_, _) => { result = RecoveryChoice.Restore; dialog.Close(); };
        keep.Click += (_, _) => { result = RecoveryChoice.SetAside; dialog.Close(); };
        discard.Click += (_, _) => { result = RecoveryChoice.Discard; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16), Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { restore, keep, discard } },
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }

    /// <summary>Brings this window to the front (used when MasterFlow is launched again).</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false;
    }
}
