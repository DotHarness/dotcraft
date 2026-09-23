using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.SessionImport;

public sealed record SessionImportUserSettings(bool SyncEnabled, IReadOnlyList<string> Sources);

public sealed class SessionImportSettingsStore(string userConfigPath, string workspaceConfigPath)
{
    private const string SectionKey = "SessionImport";
    private const string SyncEnabledKey = "SyncEnabled";
    private const string SourcesKey = "Sources";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public SessionImportUserSettings ReadUserSettings()
    {
        var section = TryReadSection(userConfigPath);
        var syncEnabled = section is not null
            && FindValue(section, SyncEnabledKey) is JsonValue enabled
            && enabled.GetValueKind() == JsonValueKind.True;
        IReadOnlyList<string> sources = section is not null && FindValue(section, SourcesKey) is JsonArray values
            ? values
                .Where(static value => value?.GetValueKind() == JsonValueKind.String)
                .Select(static value => value!.GetValue<string>())
                .Where(SessionImportSources.IsKnown)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : SessionImportSources.All;
        return new SessionImportUserSettings(syncEnabled, sources);
    }

    public bool ReadWorkspaceOptOut() =>
        TryReadSection(workspaceConfigPath) is { } section
        && FindValue(section, SyncEnabledKey) is JsonValue enabled
        && enabled.GetValueKind() == JsonValueKind.False;

    public void WriteUserSettings(bool? syncEnabled, IReadOnlyList<string>? sources)
    {
        var root = File.Exists(userConfigPath)
            ? JsonNode.Parse(File.ReadAllText(userConfigPath)) as JsonObject
                ?? throw new InvalidDataException("The user configuration root is not a JSON object.")
            : new JsonObject();
        var sectionKey = FindKey(root, SectionKey) ?? SectionKey;
        if (root[sectionKey] is not JsonObject section)
        {
            section = new JsonObject();
            root[sectionKey] = section;
        }

        if (syncEnabled is { } enabled)
            section[FindKey(section, SyncEnabledKey) ?? SyncEnabledKey] = enabled;
        if (sources is not null)
            section[FindKey(section, SourcesKey) ?? SourcesKey] = new JsonArray(sources.Select(static source => (JsonNode?)JsonValue.Create(source)).ToArray());

        WriteAtomic(root);
    }

    private void WriteAtomic(JsonObject root)
    {
        var directory = Path.GetDirectoryName(userConfigPath)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(userConfigPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, root.ToJsonString(WriteOptions) + Environment.NewLine, new UTF8Encoding(false));
            File.Move(tempPath, userConfigPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static JsonObject? TryReadSection(string path)
    {
        try
        {
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject root
                ? FindValue(root, SectionKey) as JsonObject
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonNode? FindValue(JsonObject parent, string key) =>
        FindKey(parent, key) is { } actual ? parent[actual] : null;

    private static string? FindKey(JsonObject parent, string key) =>
        parent.Select(static pair => pair.Key).FirstOrDefault(name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase));
}
