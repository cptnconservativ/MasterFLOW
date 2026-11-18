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
            try { vm.Store = new Persistence.UserDataStore(Persistence.UserDataStore.DefaultDirectory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { /* run without settings */ }
            var window = new MainWindow { DataContext = vm };
            bool opened = false;
            if (desktop.Args is { Length: > 0 } args && File.Exists(args[0]))
            {
                vm.LoadFrom(args[0]);
                opened = true;
            }
            desktop.MainWindow = window;
            if (!opened) window.Opened += async (_, _) => await vm.CheckRecoveryAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
