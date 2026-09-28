using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed record SessionImportUserSettings(bool SyncEnabled, IReadOnlyList<string> Sources, ImportSelection Selection);

public sealed class SessionImportSettingsStore(string userConfigPath, string workspaceConfigPath)
{
    public string UserDataPath => Path.GetDirectoryName(userConfigPath)!;
    public string WorkspaceDataPath => Path.GetDirectoryName(workspaceConfigPath)!;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SessionImportUserSettings ReadUserSettings()
    {
        var section = Section(userConfigPath);
        var sources = Value(section, "Sources") is JsonArray values
            ? values.Select(v => v!.GetValue<string>()).Where(SessionImportSources.IsKnown).Distinct().ToArray()
            : [];
        return new(Value(section, "SyncEnabled")?.GetValue<bool>() == true, sources,
            Value(section, "Selection")?.Deserialize<ImportSelection>(JsonOptions) ?? new ImportSelection());
    }

    public bool ReadWorkspaceOptOut() => Value(Section(workspaceConfigPath), "SyncEnabled")?.GetValue<bool>() == false;

    public void WriteUserSettings(bool? syncEnabled, IReadOnlyList<string>? sources, ImportSelection? selection = null) =>
        AtomicConfigDocument.Update(userConfigPath, root =>
        {
            var section = AtomicConfigDocument.Object(root, "AgentImport");
            if (syncEnabled.HasValue) section[AtomicConfigDocument.Key(section, "SyncEnabled") ?? "SyncEnabled"] = syncEnabled.Value;
            if (sources != null) section[AtomicConfigDocument.Key(section, "Sources") ?? "Sources"] = new JsonArray(sources.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
            if (selection != null) section[AtomicConfigDocument.Key(section, "Selection") ?? "Selection"] = JsonSerializer.SerializeToNode(selection, JsonOptions);
        });

    private static JsonNode? Value(JsonObject? section, string name) => section == null ? null : section[AtomicConfigDocument.Key(section, name) ?? name];

    private static JsonObject? Section(string path)
    {
        var root = AtomicConfigDocument.Read(path);
        return root[AtomicConfigDocument.Key(root, "AgentImport") ?? "AgentImport"] as JsonObject;
    }
}
