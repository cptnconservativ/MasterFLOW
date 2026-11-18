using System.Globalization;
using System.Text;

namespace MasterFlow.Persistence;

/// <summary>
/// Plain-text log kept only on this computer (one file per day in the user data folder, older files
/// removed after <see cref="KeepDays"/> days). Nothing is sent anywhere (D-012). Writing never throws:
/// a log that cannot be written must not stop an engineer's work.
/// </summary>
public sealed class LocalLog
{
    public const int KeepDays = 14;
    public const long MaxBytesPerFile = 2_000_000;
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;

    public string Directory { get; }

    public LocalLog(string directory, Func<DateTimeOffset>? clock = null)
    {
        Directory = directory;
        _clock = clock ?? (() => DateTimeOffset.Now);
        Prune();
    }

    public string CurrentFile => Path.Combine(Directory, $"masterflow-{_clock():yyyyMMdd}.log");

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message, Exception? ex = null)
    {
        var sb = new StringBuilder(message);
        for (var e = ex; e is not null; e = e.InnerException)
            sb.Append(Environment.NewLine).Append("  ").Append(e.GetType().FullName).Append(": ").Append(e.Message)
              .Append(Environment.NewLine).Append(e.StackTrace);
        Write("ERROR", sb.ToString());
    }

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var path = CurrentFile;
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytesPerFile) return; // runaway loop guard
                File.AppendAllText(path, $"{_clock().ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* logging is best effort */ }
    }

    private void Prune()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            var cutoff = _clock().AddDays(-KeepDays).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            foreach (var f in System.IO.Directory.GetFiles(Directory, "masterflow-*.log"))
            {
                var stamp = Path.GetFileNameWithoutExtension(f)["masterflow-".Length..];
                if (stamp.Length == 8 && string.CompareOrdinal(stamp, cutoff) < 0) File.Delete(f);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
