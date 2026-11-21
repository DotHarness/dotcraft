using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Persistence.Upgrades;

public static class WorkspaceConfigUpgrade
{
    private const string Version = "0.14.0-channels";
    private const string MarkerFileName = "upgrade-version";
    private static readonly HashSet<string> OfficialChannels = new(
        ["telegram", "weixin", "qq", "wecom", "feishu"], StringComparer.OrdinalIgnoreCase);

    public static void Apply(string craftPath, string userConfigPath, string? dockerSelection = null)
    {
        var markerPath = Path.Combine(craftPath, MarkerFileName);
        if (File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == Version)
            return;
        var workspaceConfigPath = Path.Combine(craftPath, "config.json");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(workspaceConfigPath), Path.GetFullPath(userConfigPath), comparison))
            return;
        try
        {
            AtomicConfigDocument.Update(workspaceConfigPath, workspace => MigrateChannels(workspace, craftPath, dockerSelection));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return;
        }
        AtomicConfigDocument.Write(markerPath, Version + "\n");
    }

    private static void MigrateChannels(JsonObject workspace, string craftPath, string? dockerSelection)
    {
        if (AtomicConfigDocument.Key(workspace, "ExternalChannels") is not { } oldKey
            || workspace[oldKey] is not JsonObject previous)
            return;

        var dockerManaged = new List<string>();
        foreach (var (name, item) in previous)
        {
            if (item is not JsonObject entry || HasManualLauncher(entry))
                continue;
            var logicalName = name.ToLowerInvariant();
            var pluginId = ReadString(entry, "pluginId");
            var bundled = pluginId == null && OfficialChannels.Contains(logicalName)
                && string.Equals(ReadString(entry, "builtinModule"), "channel-" + logicalName, StringComparison.OrdinalIgnoreCase);
            if (!bundled && (pluginId == null || !string.Equals(ReadString(entry, "transport"), "managedWebsocket", StringComparison.OrdinalIgnoreCase)))
                continue;
            pluginId ??= "channel-" + logicalName;
            var enabled = ReadFlag(entry, "enabled");
            if (bundled && dockerSelection != null)
            {
                enabled = dockerSelection.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? logicalName != "weixin"
                    : dockerSelection.Split(',', StringSplitOptions.TrimEntries)
                        .Contains(logicalName, StringComparer.OrdinalIgnoreCase);
                dockerManaged.Add(logicalName);
            }

            var channels = AtomicConfigDocument.Object(workspace, "Channels");
            var key = AtomicConfigDocument.Key(channels, name) ?? name;
            if (channels[key] is JsonObject current)
            {
                if (string.Equals(ReadString(current, "pluginId"), pluginId, StringComparison.OrdinalIgnoreCase))
                    CopyAuthorization(current, entry, ReadFlag(current, "enabled") && bundled);
                continue;
            }

            var selection = new JsonObject { ["pluginId"] = pluginId, ["enabled"] = enabled };
            CopyAuthorization(selection, entry, enabled && bundled);
            channels[key] = selection;
        }

        workspace.Remove(oldKey);
        if (dockerManaged.Count > 0)
            TrackDockerChannels(Path.Combine(craftPath, "docker-channels.json"), dockerManaged);
    }

    private static bool HasManualLauncher(JsonObject entry) =>
        ReadString(entry, "command") != null || ReadString(entry, "workingDirectory") != null
        || AtomicConfigDocument.Value(entry, "args") is JsonArray { Count: > 0 }
        || AtomicConfigDocument.Value(entry, "env") is JsonObject { Count: > 0 };

    private static void CopyAuthorization(JsonObject destination, JsonObject source, bool restoreBuiltIn)
    {
        if (ReadString(destination, "pluginActivationId") != null)
            return;
        if (ReadString(source, "pluginActivationId") is { } grant)
            destination["pluginActivationId"] = grant;
        else if (restoreBuiltIn || ReadFlag(source, "restoreChannelPlugin"))
            destination["restoreChannelPlugin"] = true;
    }

    private static string? ReadString(JsonObject item, string key) =>
        AtomicConfigDocument.Value(item, key) is JsonValue value
        && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static bool ReadFlag(JsonObject item, string key) =>
        AtomicConfigDocument.Value(item, key) is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static void TrackDockerChannels(string path, IReadOnlyList<string> names)
    {
        AtomicConfigDocument.Update(path, tracking =>
        {
            if (tracking["managed"] is not JsonArray managed)
            {
                managed = [];
                tracking["managed"] = managed;
            }
            foreach (var name in names)
            {
                if (!managed.Any(node => node is JsonValue value && value.TryGetValue<string>(out var existing)
                    && string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                    managed.Add(name);
            }
        });
    }
}
