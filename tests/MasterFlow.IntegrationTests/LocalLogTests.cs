using MasterFlow.Persistence;

namespace MasterFlow.IntegrationTests;

public class LocalLogTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mf-log-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WritesLevelsAndExceptionChain_ToTodaysFile()
    {
        var dir = TempDir();
        try
        {
            var now = new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.FromHours(-7));
            var log = new LocalLog(dir, () => now);
            log.Info("started");
            log.Error("boom", new InvalidOperationException("outer", new IOException("inner")));
            Assert.EndsWith("masterflow-20261118.log", log.CurrentFile);
            var text = File.ReadAllText(log.CurrentFile);
            Assert.Contains("INFO  started", text);
            Assert.Contains("ERROR boom", text);
            Assert.Contains("System.InvalidOperationException: outer", text);
            Assert.Contains("System.IO.IOException: inner", text);
            Assert.Contains("2026-11-18 09:30:00.000 -07:00", text);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void PrunesFilesOlderThanKeepDays_OnlyItsOwn()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "masterflow-20261001.log"), "old");
            File.WriteAllText(Path.Combine(dir, "masterflow-20261110.log"), "recent");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "user file");
            _ = new LocalLog(dir, () => new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
            Assert.False(File.Exists(Path.Combine(dir, "masterflow-20261001.log")));
            Assert.True(File.Exists(Path.Combine(dir, "masterflow-20261110.log")));
            Assert.True(File.Exists(Path.Combine(dir, "notes.txt")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void NeverThrows_WhenTheFolderCannotBeCreated()
    {
        var file = Path.GetTempFileName(); // a file where the folder should be
        try
        {
            var log = new LocalLog(Path.Combine(file, "logs"));
            log.Info("ignored");
            log.Error("ignored", new Exception("x"));
        }
        finally { File.Delete(file); }
    }
}
