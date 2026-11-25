using System.Diagnostics;
using System.Text.Json;
using MasterFlow.Domain;
using MasterFlow.Persistence;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Found in review (2026-11-20): opening MasterFlow a second time offered the first window's
/// autosave as "unsaved work from an unexpected exit", and Cancel deleted it. These tests pin the fix.
/// </summary>
public class SingleInstanceTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mf-inst-" + Guid.NewGuid().ToString("N"));

    private static Project Sample(string title) => new()
    {
        Calculations = [new ChannelCalculation { Title = title, Input = new UniformFlowInput { Section = new TrapezoidalSection(10, 2, 2), ManningN = 0.013, Slope = 0.01, Discharge = 100, Depth = 1 } }],
    };

    private static Process StartLongRunningProcess() =>
        OperatingSystem.IsWindows()
            ? Process.Start(new ProcessStartInfo("ping", "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!
            : Process.Start(new ProcessStartInfo("sleep", "60") { UseShellExecute = false })!;

    [Fact]
    public void SecondAcquire_Fails_UntilFirstIsReleased()
    {
        var dir = TempDir();
        try
        {
            using (var first = SingleInstance.TryAcquire(dir))
            {
                Assert.NotNull(first);
                Assert.Null(SingleInstance.TryAcquire(dir));
            }
            using var again = SingleInstance.TryAcquire(dir);
            Assert.NotNull(again);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task SecondLaunch_HandsItsFileToTheRunningCopy()
    {
        var dir = TempDir();
        try
        {
            using var first = SingleInstance.TryAcquire(dir)!;
            var got = new TaskCompletionSource<string>();
            first.Listen(m => got.TrySetResult(m));
            Assert.True(SingleInstance.TrySend(dir, @"C:\jobs\Main St.mfproj"));
            var message = await got.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(@"C:\jobs\Main St.mfproj", message);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TrySend_ReturnsFalse_WhenNothingIsListening()
    {
        var dir = TempDir();
        try { Assert.False(SingleInstance.TrySend(dir, "", timeoutMs: 300)); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void RecoveryCopy_OfAnotherRunningWindow_IsNeverOfferedDeletedOrOverwritten()
    {
        var dir = TempDir();
        using var other = StartLongRunningProcess();
        try
        {
            var store = new UserDataStore(dir);
            Assert.True(store.WriteRecovery(Sample("Other window's work"), null, "test"));
            // Pretend the copy was written by the other (still running) process.
            var infoPath = Path.Combine(dir, "recovery.json");
            var info = new UserDataStore.Recovery(null, DateTimeOffset.UtcNow, Path.Combine(dir, "recovery" + ProjectFile.Extension), other.Id, other.StartTime.ToUniversalTime());
            File.WriteAllText(infoPath, JsonSerializer.Serialize(info, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            Assert.Null(store.FindRecovery());                       // not offered as crash debris
            store.ClearRecovery();                                   // closing this window...
            Assert.True(File.Exists(infoPath));                      // ...leaves the other window's copy alone
            Assert.False(store.WriteRecovery(Sample("Mine"), null, "test")); // and autosave does not overwrite it
            Assert.Contains("Other window's work", store.LoadRecovery().Calculations.Select(c => c.Title));

            other.Kill();
            other.WaitForExit();
            Assert.NotNull(store.FindRecovery());                    // once that process is gone it is real crash debris
        }
        finally
        {
            if (!other.HasExited) other.Kill();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SetRecoveryAside_KeepsTheWork_AsAnOpenableProject()
    {
        var dir = TempDir();
        try
        {
            var store = new UserDataStore(dir);
            store.WriteRecovery(Sample("Keep me"), @"C:\jobs\Main St.mfproj", "test");
            var kept = store.SetRecoveryAside();
            Assert.NotNull(kept);
            Assert.StartsWith(store.RecoveredDirectory, kept);
            Assert.Contains("Main St (recovered ", Path.GetFileName(kept));
            Assert.Null(store.FindRecovery());
            Assert.Equal("Keep me", ProjectFile.Load(kept!).Calculations.Single().Title);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void OldRecoveryInfo_WithoutOwner_StillLoads()
    {
        var dir = TempDir();
        try
        {
            var store = new UserDataStore(dir);
            store.WriteRecovery(Sample("Old"), null, "test");
            File.WriteAllText(Path.Combine(dir, "recovery.json"), """{ "originalPath": null, "savedUtc": "2026-11-18T10:00:00+00:00", "recoveryFile": "x" }""");
            Assert.NotNull(store.FindRecovery());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
