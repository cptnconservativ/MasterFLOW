using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MasterFlow.Desktop.ViewModels;
using MasterFlow.Desktop.Views;

namespace MasterFlow.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = MainViewModel.CreateWithSample();
            var window = new MainWindow { DataContext = vm };
            if (desktop.Args is { Length: > 0 } args && File.Exists(args[0]))
                vm.LoadFrom(args[0]);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
