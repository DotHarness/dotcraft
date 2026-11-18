using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Configuration;

/// <summary>
/// Persists SubAgent profile config and enablement state in one configuration layer file.
/// </summary>
public static class SubAgentProfilesPersistence
{
    public static SubAgentConfigState LoadState(string configPath) => FromConfig(AppConfig.Load(configPath));

    public static SubAgentConfigState FromConfig(AppConfig config)
    {
        return new SubAgentConfigState(
            config.SubAgent.DisabledProfiles
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            config.SubAgent.EnableExternalCliSessionResume,
            NormalizeProviderPreferences(config.SubAgent.ProviderPreferences),
            config.SubAgentProfiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
                .Select(profile => profile.Clone())
                .ToArray());
    }

    public static void SaveState(
        string configPath,
        IReadOnlyCollection<string> disabledProfiles,
        bool enableExternalCliSessionResume,
        IReadOnlyCollection<SubAgentProfile> profiles,
        IReadOnlyDictionary<string, SubAgentModelPreference>? providerPreferences = null)
    {
        AtomicConfigDocument.Update(configPath, root =>
        {
            WriteDisabledProfiles(root, disabledProfiles);
            WriteEnableExternalCliSessionResume(root, enableExternalCliSessionResume);
            WriteProfiles(root, profiles);
            WriteProviderPreferences(root, providerPreferences);
        });
    }

    private static void WriteDisabledProfiles(JsonObject root, IReadOnlyCollection<string> disabledProfiles)
    {
        var normalized = disabledProfiles
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var section = SubAgentSection(root, create: normalized.Length > 0);
        if (section == null)
            return;

        var disabledKey = AtomicConfigDocument.Key(section, "DisabledProfiles");
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
        var section = SubAgentSection(root, create: enabled);
        if (section == null)
            return;

        var key = AtomicConfigDocument.Key(section, "EnableExternalCliSessionResume");
        if (!enabled)
        {
            if (key != null)
                section.Remove(key);
            RemoveConfigSectionIfEmpty(root, "SubAgent");
            return;
        }

        section[key ?? "EnableExternalCliSessionResume"] = enabled;
    }

    /// <summary>
    /// Writes complete native SubAgent preferences under <c>SubAgent.ProviderPreferences</c>.
    /// A null map preserves the existing key (no change); an empty map removes it.
    /// </summary>
    private static void WriteProviderPreferences(
        JsonObject root,
        IReadOnlyDictionary<string, SubAgentModelPreference>? providerPreferences)
    {
        if (providerPreferences == null)
            return;

        var normalized = NormalizeProviderPreferences(providerPreferences);
        var section = SubAgentSection(root, create: normalized.Count > 0);
        if (section == null)
            return;

        var key = AtomicConfigDocument.Key(section, "ProviderPreferences");
        if (normalized.Count == 0)
        {
            if (key != null)
                section.Remove(key);
            RemoveConfigSectionIfEmpty(root, "SubAgent");
            return;
        }

        var objectNode = new JsonObject();
        foreach (var kv in normalized.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            objectNode[kv.Key] = JsonSerializer.SerializeToNode(kv.Value, AppConfig.SerializerOptions);

        section[key ?? "ProviderPreferences"] = objectNode;
    }

    private static void WriteProfiles(JsonObject root, IReadOnlyCollection<SubAgentProfile> profiles)
    {
        var normalized = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var profilesKey = AtomicConfigDocument.Key(root, "SubAgentProfiles");
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

    /// <summary>
    /// Normalizes provider keys and drops incomplete preferences.
    /// </summary>
    private static Dictionary<string, SubAgentModelPreference> NormalizeProviderPreferences(
        IReadOnlyDictionary<string, SubAgentModelPreference> providerPreferences)
    {
        var result = new Dictionary<string, SubAgentModelPreference>(StringComparer.Ordinal);
        foreach (var kv in providerPreferences)
        {
            var providerId = kv.Key?.Trim();
            if (string.IsNullOrWhiteSpace(providerId))
                continue;
            if (kv.Value is null || string.IsNullOrWhiteSpace(kv.Value.Model)
                || string.Equals(kv.Value.Model.Trim(), "default", StringComparison.OrdinalIgnoreCase))
                continue;

            var preference = kv.Value.Clone();
            preference.Model = preference.Model.Trim();
            if (string.Equals(preference.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                preference.ProviderId = null;
            result[providerId] = preference;
        }

        return result;
    }

    private static JsonObject? SubAgentSection(JsonObject root, bool create) =>
        create
            ? AtomicConfigDocument.Object(root, "SubAgent")
            : AtomicConfigDocument.Value(root, "SubAgent") as JsonObject;

    private static void RemoveConfigSectionIfEmpty(JsonObject root, string canonicalKey)
    {
        var existingKey = AtomicConfigDocument.Key(root, canonicalKey);
        if (existingKey == null)
            return;

        if (root[existingKey] is JsonObject obj && obj.Count == 0)
            root.Remove(existingKey);
    }
}

public sealed record SubAgentConfigState(
    IReadOnlyList<string> DisabledProfiles,
    bool EnableExternalCliSessionResume,
    IReadOnlyDictionary<string, SubAgentModelPreference> ProviderPreferences,
    IReadOnlyList<SubAgentProfile> Profiles);
