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
            try
            {
                vm.Store = new Persistence.UserDataStore(Persistence.UserDataStore.DefaultDirectory);
                vm.Log = new Persistence.LocalLog(vm.Store.LogDirectory);
                vm.Log.Info($"MasterFlow {MainViewModel.AppVersion} started on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, .NET {Environment.Version}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { /* run without settings */ }

            // Unexpected errors: keep the app running where possible, log locally, save a recovery copy.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) => { vm.ReportUnexpectedError(e.Exception, "user interface"); e.Handled = true; };
            TaskScheduler.UnobservedTaskException += (_, e) => { vm.Log?.Error("Unobserved background task error.", e.Exception); e.SetObserved(); };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                vm.Log?.Error("Fatal error; MasterFlow is closing.", e.ExceptionObject as Exception);
                try { vm.Autosave(); } catch { /* nothing more can be done */ }
            };
            desktop.Exit += (_, _) => vm.Log?.Info("MasterFlow closed.");
            var window = new MainWindow { DataContext = vm };
            bool opened = false;
            if (desktop.Args is { Length: > 0 } args && File.Exists(args[0]))
            {
                vm.LoadFrom(args[0]);
                opened = true;
            }
            desktop.MainWindow = window;
            if (!opened) window.Opened += async (_, _) => await vm.CheckRecoveryAsync();

            // Later launches (double-clicking a project, the Start menu) come here instead of opening a second window.
            Program.Instance?.Listen(message => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                window.BringToFront();
                if (message.Length > 0) await vm.OpenFromAnotherLaunchAsync(message);
            }));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
