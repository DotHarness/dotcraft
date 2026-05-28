using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Configuration;

/// <summary>
/// Persists workspace-scoped SubAgent profile config and enablement state to <c>.craft/config.json</c>.
/// </summary>
public static class SubAgentProfilesPersistence
{
    public static SubAgentWorkspaceState LoadWorkspaceState(string craftPath)
    {
        var configPath = Path.Combine(craftPath, "config.json");
        var config = AppConfig.Load(configPath);
        return new SubAgentWorkspaceState(
            config.SubAgent.DisabledProfiles
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            config.SubAgent.EnableExternalCliSessionResume,
            config.SubAgent.Model,
            config.SubAgentProfiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
                .Select(profile => profile.Clone())
                .ToArray());
    }

    public static void SaveWorkspaceState(
        string craftPath,
        IReadOnlyCollection<string> disabledProfiles,
        bool enableExternalCliSessionResume,
        string? model,
        IReadOnlyCollection<SubAgentProfile> profiles)
    {
        var configPath = Path.Combine(craftPath, "config.json");
        Directory.CreateDirectory(craftPath);
        var root = LoadWorkspaceConfigObject(configPath);

        WriteDisabledProfiles(root, disabledProfiles);
        WriteEnableExternalCliSessionResume(root, enableExternalCliSessionResume);
        WriteModel(root, model);
        WriteProfiles(root, profiles);

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private static void WriteDisabledProfiles(JsonObject root, IReadOnlyCollection<string> disabledProfiles)
    {
        var normalized = disabledProfiles
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var section = GetOrCreateConfigSection(root, "SubAgent", createIfMissing: normalized.Length > 0);
        if (section == null)
            return;

        var disabledKey = FindCaseInsensitiveKey(section, "DisabledProfiles");
        if (normalized.Length == 0)
        {
            if (disabledKey != null)
                section.Remove(disabledKey);
            RemoveConfigSectionIfEmpty(root, "SubAgent");
            return;
        }

        var array = new JsonArray();
        foreach (var profileName in normalized)
            array.Add(profileName);

        section[disabledKey ?? "DisabledProfiles"] = array;
    }

    private static void WriteEnableExternalCliSessionResume(JsonObject root, bool enabled)
    {
        var section = GetOrCreateConfigSection(root, "SubAgent", createIfMissing: enabled);
        if (section == null)
            return;

        var key = FindCaseInsensitiveKey(section, "EnableExternalCliSessionResume");
        if (!enabled)
        {
            if (key != null)
                section.Remove(key);
            RemoveConfigSectionIfEmpty(root, "SubAgent");
            return;
        }

        section[key ?? "EnableExternalCliSessionResume"] = enabled;
    }

    private static void WriteModel(JsonObject root, string? model)
    {
        var normalized = model?.Trim() ?? string.Empty;
        var section = GetOrCreateConfigSection(root, "SubAgent", createIfMissing: normalized.Length > 0);
        if (section == null)
            return;

        var key = FindCaseInsensitiveKey(section, "Model");
        if (normalized.Length == 0)
        {
            if (key != null)
                section.Remove(key);
            RemoveConfigSectionIfEmpty(root, "SubAgent");
            return;
        }

        section[key ?? "Model"] = normalized;
    }

    private static void WriteProfiles(JsonObject root, IReadOnlyCollection<SubAgentProfile> profiles)
    {
        var normalized = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var profilesKey = FindCaseInsensitiveKey(root, "SubAgentProfiles");
        if (normalized.Length == 0)
        {
            if (profilesKey != null)
                root.Remove(profilesKey);
            return;
        }

        var objectNode = new JsonObject();
        foreach (var profile in normalized)
        {
            var node = JsonSerializer.SerializeToNode(profile, AppConfig.SerializerOptions);
            if (node != null)
                objectNode[profile.Name] = node;
        }

        root[profilesKey ?? "SubAgentProfiles"] = objectNode;
    }

    private static JsonObject LoadWorkspaceConfigObject(string configPath)
    {
        if (!File.Exists(configPath))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static string? FindCaseInsensitiveKey(JsonObject obj, string expectedKey)
    {
        foreach (var kvp in obj)
        {
            if (string.Equals(kvp.Key, expectedKey, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }

        return null;
    }

    private static JsonObject? GetOrCreateConfigSection(JsonObject root, string canonicalKey, bool createIfMissing)
    {
        var existingKey = FindCaseInsensitiveKey(root, canonicalKey);
        if (existingKey != null)
        {
            if (root[existingKey] is JsonObject existingSection)
                return existingSection;

            if (!createIfMissing)
                return null;

            var replacement = new JsonObject();
            root[existingKey] = replacement;
            return replacement;
        }

        if (!createIfMissing)
            return null;

        var section = new JsonObject();
        root[canonicalKey] = section;
        return section;
    }

    private static void RemoveConfigSectionIfEmpty(JsonObject root, string canonicalKey)
    {
        var existingKey = FindCaseInsensitiveKey(root, canonicalKey);
        if (existingKey == null)
            return;

        if (root[existingKey] is JsonObject obj && obj.Count == 0)
            root.Remove(existingKey);
    }
}

public sealed record SubAgentWorkspaceState(
    IReadOnlyList<string> DisabledProfiles,
    bool EnableExternalCliSessionResume,
    string Model,
    IReadOnlyList<SubAgentProfile> Profiles);
