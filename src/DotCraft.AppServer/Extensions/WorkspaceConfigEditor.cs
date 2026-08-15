using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.AppServer;

/// <summary>
/// Shared AppServer helper for locating, loading, and editing workspace/global config files.
/// Kept small and behavior-preserving so config-owning handlers can move out of the dispatcher
/// without each carrying its own JSON-file plumbing.
/// </summary>
internal sealed class WorkspaceConfigEditor(IAppConfigMonitor? appConfigMonitor, string? workspaceCraftPath)
{
    public string? EffectiveGlobalConfigPath => appConfigMonitor?.Current.GlobalConfigPath;

    public string? PersonalConfigPath =>
        !string.IsNullOrWhiteSpace(appConfigMonitor?.Current.GlobalConfigPath)
            ? appConfigMonitor.Current.GlobalConfigPath!
            : null;

    public string RequirePersonalConfigPath(string operation) =>
        PersonalConfigPath ?? throw new InvalidOperationException(
            $"UserDataPath is required for {operation}.");

    public AppConfig LoadCurrentMergedConfig()
    {
        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
            return AppConfig.LoadWithGlobalFallback(Path.Combine(workspaceCraftPath, "config.json"), EffectiveGlobalConfigPath);

        return appConfigMonitor?.Current ?? new AppConfig();
    }

    public static JsonObject LoadObject(string configPath)
    {
        if (!File.Exists(configPath))
            return new JsonObject();

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(configPath));
            return node as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    public static void WriteObject(string configPath, JsonObject root)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    public static string? FindCaseInsensitiveKey(JsonObject obj, string expectedKey)
    {
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, expectedKey, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }

        return null;
    }

    public static void UpsertOrRemoveValue(
        JsonObject root,
        string? existingKey,
        string canonicalKey,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (existingKey != null)
                root.Remove(existingKey);
            return;
        }

        root[existingKey ?? canonicalKey] = value;
    }

    public static void UpsertOrRemoveValue(
        JsonObject root,
        string? existingKey,
        string canonicalKey,
        bool? value)
    {
        if (!value.HasValue)
        {
            if (existingKey != null)
                root.Remove(existingKey);
            return;
        }

        root[existingKey ?? canonicalKey] = value.Value;
    }

    public static void UpsertOrRemoveValue(
        JsonObject root,
        string? existingKey,
        string canonicalKey,
        int? value)
    {
        if (!value.HasValue)
        {
            if (existingKey != null)
                root.Remove(existingKey);
            return;
        }

        root[existingKey ?? canonicalKey] = value.Value;
    }
}
