using System.Text.Json;

namespace MasterFlow.Persistence;

/// <summary>Per-user application settings (not part of any project).</summary>
public sealed record AppSettings
{
    public const int MaxRecentFiles = 10;
    public IReadOnlyList<string> RecentFiles { get; init; } = [];

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
    public sealed record Recovery(string? OriginalPath, DateTimeOffset SavedUtc, string RecoveryFile);

    public void WriteRecovery(Domain.Project project, string? originalPath, string appVersion)
    {
        WriteAtomic(RecoveryPath, ProjectFile.Serialize(project, appVersion));
        WriteAtomic(RecoveryInfoPath, JsonSerializer.Serialize(new Recovery(originalPath, DateTimeOffset.UtcNow, RecoveryPath), Json));
    }

    public Recovery? FindRecovery()
    {
        try
        {
            if (!File.Exists(RecoveryPath) || !File.Exists(RecoveryInfoPath)) return null;
            return JsonSerializer.Deserialize<Recovery>(File.ReadAllText(RecoveryInfoPath), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
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

    public void ClearRecovery()
    {
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
