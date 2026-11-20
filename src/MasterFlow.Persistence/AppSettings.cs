using System.Text.Json;

namespace MasterFlow.Persistence;

/// <summary>Per-user application settings (not part of any project).</summary>
public sealed record AppSettings
{
    public const int MaxRecentFiles = 10;
    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    /// <summary>The MasterFlow Premium license key the user entered (checked offline at start-up); null for Free.</summary>
    public string? LicenseKey { get; init; }

    /// <summary>Returns settings with <paramref name="path"/> moved to the top of the recent list.</summary>
    public AppSettings WithRecent(string path)
    {
        string full = Path.GetFullPath(path);
        var list = new List<string> { full };
        list.AddRange(RecentFiles.Where(p => !string.Equals(p, full, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)));
        return this with { RecentFiles = list.Take(MaxRecentFiles).ToList() };
    }
}

/// <summary>
/// Stores <see cref="AppSettings"/> and the autosave/recovery copy in a per-user folder
/// (%APPDATA%\MasterFlow on Windows). Nothing leaves the machine. A corrupt settings file is ignored
/// and replaced by defaults rather than stopping the application.
/// </summary>
public sealed class UserDataStore(string directory)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "MasterFlow");

    public string Directory { get; } = directory;
    private string SettingsPath => Path.Combine(Directory, "settings.json");
    public string LogDirectory => Path.Combine(Directory, "logs");
    private string RecoveryPath => Path.Combine(Directory, "recovery" + ProjectFile.Extension);
    private string RecoveryInfoPath => Path.Combine(Directory, "recovery.json");

    public AppSettings LoadSettings()
    {
        try
        {
            return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new(); }
    }

    public void SaveSettings(AppSettings settings) => WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, Json));

    /// <summary>Information about an autosaved copy that was not cleared by a normal save or exit.</summary>
    /// <param name="OwnerProcessId">The MasterFlow process that wrote the copy (null in files from 0.12 and earlier).</param>
    /// <param name="OwnerStartUtc">That process's start time, so a reused process id is not mistaken for it.</param>
    public sealed record Recovery(string? OriginalPath, DateTimeOffset SavedUtc, string RecoveryFile, int? OwnerProcessId = null, DateTimeOffset? OwnerStartUtc = null);

    /// <summary>Writes the recovery copy. Returns false (and writes nothing) when another open MasterFlow owns the current copy.</summary>
    public bool WriteRecovery(Domain.Project project, string? originalPath, string appVersion)
    {
        try
        {
            if (File.Exists(RecoveryInfoPath) && JsonSerializer.Deserialize<Recovery>(File.ReadAllText(RecoveryInfoPath), Json) is { } r && IsOwnedByAnotherLiveProcess(r))
                return false;
        }
        catch (JsonException) { }
        WriteAtomic(RecoveryPath, ProjectFile.Serialize(project, appVersion));
        WriteAtomic(RecoveryInfoPath, JsonSerializer.Serialize(new Recovery(originalPath, DateTimeOffset.UtcNow, RecoveryPath, Environment.ProcessId, CurrentProcessStartUtc()), Json));
        return true;
    }

    /// <summary>
    /// The recovery copy left by a MasterFlow that is no longer running, or null. A copy written by another
    /// MasterFlow window that is still open is that window's live autosave, not crash debris, so it is never offered.
    /// </summary>
    public Recovery? FindRecovery()
    {
        try
        {
            if (!File.Exists(RecoveryPath) || !File.Exists(RecoveryInfoPath)) return null;
            var r = JsonSerializer.Deserialize<Recovery>(File.ReadAllText(RecoveryInfoPath), Json);
            return r is not null && IsOwnedByAnotherLiveProcess(r) ? null : r;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>True when the copy was written by a different MasterFlow process that is still running.</summary>
    public static bool IsOwnedByAnotherLiveProcess(Recovery r)
    {
        if (r.OwnerProcessId is not { } pid || pid == Environment.ProcessId) return false;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            if (p.HasExited) return false;
            if (r.OwnerStartUtc is { } start && Math.Abs((p.StartTime.ToUniversalTime() - start.UtcDateTime).TotalSeconds) > 2) return false;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return false; }
    }

    private static DateTimeOffset? CurrentProcessStartUtc()
    {
        try { using var p = System.Diagnostics.Process.GetCurrentProcess(); return p.StartTime.ToUniversalTime(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return null; }
    }

    /// <summary>Folder where recovery copies the user chose not to restore yet are kept.</summary>
    public string RecoveredDirectory => Path.Combine(Directory, "recovered");

    /// <summary>
    /// Moves the recovery copy to <see cref="RecoveredDirectory"/> under a dated name, so it is kept but no
    /// longer offered at start-up or overwritten by the next autosave. Returns the new path, or null.
    /// </summary>
    public string? SetRecoveryAside()
    {
        if (!File.Exists(RecoveryPath)) return null;
        var info = FindRecovery();
        string stamp = (info?.SavedUtc ?? DateTimeOffset.UtcNow).ToLocalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string name = info?.OriginalPath is { } o ? Path.GetFileNameWithoutExtension(o) : "unsaved project";
        System.IO.Directory.CreateDirectory(RecoveredDirectory);
        string target = Path.Combine(RecoveredDirectory, $"{name} (recovered {stamp}){ProjectFile.Extension}");
        for (int i = 2; File.Exists(target); i++)
            target = Path.Combine(RecoveredDirectory, $"{name} (recovered {stamp}) {i}{ProjectFile.Extension}");
        File.Move(RecoveryPath, target);
        try { File.Delete(RecoveryInfoPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return target;
    }

    public Domain.Project LoadRecovery() => ProjectFile.Load(RecoveryPath);

    private string TemplatesPath => Path.Combine(Directory, "templates" + ProjectFile.Extension);

    /// <summary>User templates are stored as an ordinary project file (one calculation per template; title = template name).</summary>
    public IReadOnlyList<Domain.ChannelCalculation> LoadUserTemplates()
    {
        try { return File.Exists(TemplatesPath) ? ProjectFile.Load(TemplatesPath).Calculations : []; }
        catch (ProjectFileException) { return []; }
    }

    public void SaveUserTemplates(IReadOnlyList<Domain.ChannelCalculation> templates, string appVersion)
    {
        System.IO.Directory.CreateDirectory(Directory);
        ProjectFile.Save(new Domain.Project { Info = new Domain.ProjectInfo { Name = "MasterFlow user templates" }, Calculations = templates }, TemplatesPath, appVersion);
    }

    /// <summary>Deletes this process's recovery copy. A copy that belongs to another open MasterFlow is left alone.</summary>
    public void ClearRecovery()
    {
        try
        {
            if (File.Exists(RecoveryInfoPath) && JsonSerializer.Deserialize<Recovery>(File.ReadAllText(RecoveryInfoPath), Json) is { } r && IsOwnedByAnotherLiveProcess(r))
                return;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        foreach (var p in new[] { RecoveryPath, RecoveryInfoPath })
            try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void WriteAtomic(string path, string text)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }
}
