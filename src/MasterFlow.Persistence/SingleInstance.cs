using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace MasterFlow.Persistence;

/// <summary>
/// Keeps MasterFlow to one window per user. The first copy holds an exclusive lock on a file in the
/// per-user folder (the operating system releases it if the process dies, so a crash never leaves a stale
/// lock) and listens on a named pipe. A second launch sends its command line (a project path or nothing)
/// to the first copy, which brings its window forward and opens the file, and then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;

    private SingleInstance(FileStream lockStream, string pipeName) { _lock = lockStream; _pipeName = pipeName; }

    /// <summary>Pipe name for a user data folder (one MasterFlow per user and data folder).</summary>
    public static string PipeNameFor(string directory)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant()));
        return "MasterFlow-" + Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>Takes the instance lock, or returns null when another MasterFlow already holds it.</summary>
    public static SingleInstance? TryAcquire(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            var fs = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            fs.SetLength(0);
            var pid = Encoding.ASCII.GetBytes(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            fs.Write(pid); fs.Flush();
            return new SingleInstance(fs, PipeNameFor(directory));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Second launch: hands <paramref name="message"/> (a file path, or "" to just show the window) to the running copy.</summary>
    public static bool TrySend(string directory, string message, int timeoutMs = 3000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeNameFor(directory), PipeDirection.Out);
            client.Connect(timeoutMs);
            var bytes = Encoding.UTF8.GetBytes(message);
            client.Write(bytes);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Starts listening for later launches; <paramref name="onMessage"/> runs on a background thread.</summary>
    public void Listen(Action<string> onMessage)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var ms = new MemoryStream();
                    await server.CopyToAsync(ms, token).ConfigureAwait(false);
                    onMessage(Encoding.UTF8.GetString(ms.ToArray()));
                }
                catch (OperationCanceledException) { return; }
                catch (IOException) { await Task.Delay(200, CancellationToken.None).ConfigureAwait(false); }
            }
        }, token);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _lock.Dispose();
    }
}
