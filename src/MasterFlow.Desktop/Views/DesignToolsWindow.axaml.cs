using Avalonia.Controls;
using Avalonia.Input;
using MasterFlow.Desktop.ViewModels;

namespace MasterFlow.Desktop.Views;

/// <summary>Design tools: rational method, inlets, detention, backwater, plan review and quantities.</summary>
public partial class DesignToolsWindow : Window
{
    public DesignToolsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => (DataContext as MainViewModel)?.Tools.EnsureSelection();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F5 && DataContext is MainViewModel vm) { vm.Tools.Run(); e.Handled = true; }
        };
    }
}
