using Avalonia;
using MasterFlow.Persistence;

namespace MasterFlow.Desktop;

internal static class Program
{
    /// <summary>The single-instance lock, when this process holds it (null if another MasterFlow was already open).</summary>
    internal static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        string dir;
        try { dir = UserDataStore.DefaultDirectory; } catch (PlatformNotSupportedException) { dir = Path.Combine(Path.GetTempPath(), "MasterFlow"); }
        try { Instance = SingleInstance.TryAcquire(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Instance = null; }

        if (Instance is null)
        {
            // MasterFlow is already open: hand it the file (or just bring it forward) and stop here,
            // so a second window never sees the first window's autosave as crash debris.
            string message = args.Length > 0 && File.Exists(args[0]) ? Path.GetFullPath(args[0]) : "";
            if (SingleInstance.TrySend(dir, message)) return 0;
            // The open copy did not answer (busy or hung). Start anyway; recovery copies owned by a
            // running MasterFlow are never offered or deleted, so its work stays safe.
        }
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { Instance?.Dispose(); }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
