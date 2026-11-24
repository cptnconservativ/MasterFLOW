using Avalonia.Controls;
using Avalonia.Input;
using MasterFlow.Desktop.ViewModels;

namespace MasterFlow.Desktop.Views;

/// <summary>Design tools: a searchable, grouped tool list on the left; the selected tool's input and result on the right.</summary>
public partial class DesignToolsWindow : Window
{
    public DesignToolsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => (DataContext as MainViewModel)?.Tools.EnsureSelection();
        // Tunnel so Ctrl+Enter reaches the window before the multi-line input box takes it as a new line.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DataContext is not MainViewModel vm) return;
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (e.Key == Key.F5 || (ctrl && e.Key == Key.Enter)) { vm.Tools.Run(); e.Handled = true; }
            else if (ctrl && e.Key == Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Enter && SearchBox.IsFocused) { vm.Tools.SelectFirstMatch(); e.Handled = true; }
            else if (e.Key == Key.Escape && SearchBox.IsFocused) { vm.Tools.SearchText = ""; e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
