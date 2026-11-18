using System.Text.Json;
using System.Text.Json.Serialization;
using MasterFlow.Domain;

namespace MasterFlow.Persistence;

/// <summary>Thrown when a project file cannot be read safely.</summary>
public sealed class ProjectFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Local-first project storage as human-readable JSON (extension .mfproj).
/// The file carries an explicit <c>schemaVersion</c>. Files from a newer schema are refused rather than
/// partially loaded; older schemas are upgraded through <see cref="Migrate"/>. Saves are atomic: the
/// file is written to a temporary sibling and then moved over the original.
/// </summary>
public static class ProjectFile
{
    public const int CurrentSchemaVersion = 1;
    public const string Extension = ".mfproj";
    public const string FormatId = "MasterFlow.Project";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private sealed record Envelope(string Format, int SchemaVersion, string SavedBy, DateTimeOffset SavedUtc, Project Project);

    public static string Serialize(Project project, string appVersion = "0.10.0") =>
        JsonSerializer.Serialize(new Envelope(FormatId, CurrentSchemaVersion, $"MasterFlow {appVersion}", DateTimeOffset.UtcNow, project), Options);

    public static Project Deserialize(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new ProjectFileException("The file is not valid JSON.", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var fmt) || fmt.GetString() != FormatId)
                throw new ProjectFileException("The file is not a MasterFlow project.");
            if (!root.TryGetProperty("schemaVersion", out var ver) || !ver.TryGetInt32(out int version))
                throw new ProjectFileException("The project file has no schema version.");
            if (version > CurrentSchemaVersion)
                throw new ProjectFileException($"The project was saved by a newer MasterFlow (schema {version}). This version reads schema {CurrentSchemaVersion} and older.");
            if (version < 1)
                throw new ProjectFileException($"Unknown schema version {version}.");

            var migrated = Migrate(root, version);
            try
            {
                var env = migrated.Deserialize<Envelope>(Options) ?? throw new ProjectFileException("The project file is empty.");
                return env.Project ?? throw new ProjectFileException("The project file has no project content.");
            }
            catch (JsonException ex) { throw new ProjectFileException($"The project file is damaged: {ex.Message}", ex); }
            catch (NotSupportedException ex) { throw new ProjectFileException($"The project file contains an unsupported item: {ex.Message}", ex); }
        }
    }

    /// <summary>Upgrades an older schema to the current one. Schema 1 is current, so this is the identity.</summary>
    private static JsonElement Migrate(JsonElement root, int version) => version switch
    {
        1 => root,
        _ => throw new ProjectFileException($"No migration from schema {version}."),
    };

    public static void Save(Project project, string path, string appVersion = "0.10.0")
    {
        string full = Path.GetFullPath(path);
        string tmp = full + ".tmp";
        File.WriteAllText(tmp, Serialize(project, appVersion));
        File.Move(tmp, full, overwrite: true);
    }

    public static Project Load(string path)
    {
        if (!File.Exists(path)) throw new ProjectFileException($"File not found: {path}");
        return Deserialize(File.ReadAllText(path));
    }
}
