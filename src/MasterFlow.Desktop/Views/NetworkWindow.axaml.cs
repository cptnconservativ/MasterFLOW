using Avalonia.Controls;
using Avalonia.Input;
using MasterFlow.Desktop.ViewModels;

namespace MasterFlow.Desktop.Views;

/// <summary>Storm networks: pipe and structure input, HGL/EGL tables, messages and the profile graphic.</summary>
public partial class NetworkWindow : Window
{
    public NetworkWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F5 && DataContext is MainViewModel { SelectedNetwork: { } n }) { n.Solve(); e.Handled = true; }
        };
    }
}
