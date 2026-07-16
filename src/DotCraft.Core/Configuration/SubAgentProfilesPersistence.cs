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
            ReadProviderModelsFromFile(configPath),
            SubAgentWaitAgentTimeoutOptions.FromConfig(config.SubAgent),
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
        SubAgentWaitAgentTimeoutOptions waitAgentTimeouts,
        IReadOnlyCollection<SubAgentProfile> profiles,
        IReadOnlyDictionary<string, string>? providerModels = null)
    {
        var configPath = Path.Combine(craftPath, "config.json");
        Directory.CreateDirectory(craftPath);
        var root = LoadWorkspaceConfigObject(configPath);

        WriteDisabledProfiles(root, disabledProfiles);
        WriteEnableExternalCliSessionResume(root, enableExternalCliSessionResume);
        WriteModel(root, model);
        WriteWaitAgentTimeouts(root, waitAgentTimeouts);
        WriteProfiles(root, profiles);
        WriteProviderModels(root, providerModels);

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

    /// <summary>
    /// Writes the per-provider native SubAgent model memory map under <c>SubAgent.ProviderModels</c>.
    /// A null map preserves the existing key (no change); an empty map removes it. This map is UI-side
    /// memory only and is never consumed by the runtime (which reads <c>SubAgent.Model</c>).
    /// </summary>
    private static void WriteProviderModels(JsonObject root, IReadOnlyDictionary<string, string>? providerModels)
    {
        if (providerModels == null)
            return;

        var normalized = NormalizeProviderModels(providerModels);
        var section = GetOrCreateConfigSection(root, "SubAgent", createIfMissing: normalized.Count > 0);
        if (section == null)
            return;

        var key = FindCaseInsensitiveKey(section, "ProviderModels");
        if (normalized.Count == 0)
        {
            if (key != null)
                section.Remove(key);
            RemoveConfigSectionIfEmpty(root, "SubAgent");
            return;
        }

        var objectNode = new JsonObject();
        foreach (var kv in normalized.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            objectNode[kv.Key] = JsonValue.Create(kv.Value);

        section[key ?? "ProviderModels"] = objectNode;
    }

    private static void WriteWaitAgentTimeouts(JsonObject root, SubAgentWaitAgentTimeoutOptions waitAgentTimeouts)
    {
        var errors = SubAgentWaitAgentTimeoutOptions.Validate(waitAgentTimeouts);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        var shouldWrite =
            waitAgentTimeouts.MinTimeoutMs != SubAgentWaitAgentTimeoutOptions.BuiltInMinTimeoutMs
            || waitAgentTimeouts.DefaultTimeoutMs != SubAgentWaitAgentTimeoutOptions.BuiltInDefaultTimeoutMs
            || waitAgentTimeouts.MaxTimeoutMs != SubAgentWaitAgentTimeoutOptions.BuiltInMaxTimeoutMs;
        var section = GetOrCreateConfigSection(root, "SubAgent", createIfMissing: shouldWrite);
        if (section == null)
            return;

        UpsertOrRemoveDefaultInt(
            section,
            "MinWaitTimeoutMs",
            waitAgentTimeouts.MinTimeoutMs,
            SubAgentWaitAgentTimeoutOptions.BuiltInMinTimeoutMs);
        UpsertOrRemoveDefaultInt(
            section,
            "DefaultWaitTimeoutMs",
            waitAgentTimeouts.DefaultTimeoutMs,
            SubAgentWaitAgentTimeoutOptions.BuiltInDefaultTimeoutMs);
        UpsertOrRemoveDefaultInt(
            section,
            "MaxWaitTimeoutMs",
            waitAgentTimeouts.MaxTimeoutMs,
            SubAgentWaitAgentTimeoutOptions.BuiltInMaxTimeoutMs);
        RemoveConfigSectionIfEmpty(root, "SubAgent");
    }

    private static void UpsertOrRemoveDefaultInt(JsonObject section, string canonicalKey, int value, int defaultValue)
    {
        var key = FindCaseInsensitiveKey(section, canonicalKey);
        if (value == defaultValue)
        {
            if (key != null)
                section.Remove(key);
            return;
        }

        section[key ?? canonicalKey] = value;
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

    /// <summary>
    /// Reads the per-provider native SubAgent model memory map from raw config JSON
    /// (<c>SubAgent.ProviderModels</c>). This is not a typed <see cref="AppConfig"/> field.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadProviderModelsFromFile(string configPath)
    {
        var root = LoadWorkspaceConfigObject(configPath);
        var subKey = FindCaseInsensitiveKey(root, "SubAgent");
        if (subKey == null || root[subKey] is not JsonObject section)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var pmKey = FindCaseInsensitiveKey(section, "ProviderModels");
        if (pmKey == null || section[pmKey] is not JsonObject obj)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in obj)
        {
            if (kv.Value is not JsonValue value || !value.TryGetValue<string>(out var rawModel))
                continue;
            raw[kv.Key] = rawModel;
        }

        return NormalizeProviderModels(raw);
    }

    /// <summary>
    /// Normalizes a provider-model map: trims keys/values and drops empty provider ids and
    /// empty or "default" model values.
    /// </summary>
    private static Dictionary<string, string> NormalizeProviderModels(IReadOnlyDictionary<string, string> providerModels)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in providerModels)
        {
            var providerId = kv.Key?.Trim();
            if (string.IsNullOrWhiteSpace(providerId))
                continue;
            var model = kv.Value?.Trim();
            if (string.IsNullOrWhiteSpace(model) ||
                string.Equals(model, "default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result[providerId] = model;
        }

        return result;
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
    IReadOnlyDictionary<string, string> ProviderModels,
    SubAgentWaitAgentTimeoutOptions WaitAgentTimeouts,
    IReadOnlyList<SubAgentProfile> Profiles);
