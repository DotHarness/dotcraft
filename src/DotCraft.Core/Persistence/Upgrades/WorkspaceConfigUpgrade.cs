using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Persistence.Upgrades;

public static class WorkspaceConfigUpgrade
{
    private const string Version = "0.13.1";
    private const string MarkerFileName = "upgrade-version";

    private static readonly string[][] PersonalKeyPaths =
    [
        ["ProviderId"],
        ["ProviderPreferences"],
        ["Permissions", "DefaultApprovalPolicy"],
        ["Memory", "Enabled"],
        ["Memory", "AutoExtract"],
        ["Dreams", "Enabled"],
        ["Dreams", "AutoApply"],
        ["Dreams", "Interval"],
        ["PromptSuggestions", "Enabled"],
        ["Skills", "IncludeSharedSkills"],
        ["InstantInterruptEnabled"],
        ["Tools", "CodeMode", "Mode"],
        ["Tools", "ImageGeneration", "Enabled"],
        ["Tools", "ImageGeneration", "Provider"],
        ["Tools", "Lsp", "Enabled"],
        ["Tools", "Web", "SearchProvider"],
        ["Tools", "Web", "DeepSeekCredentialSource"],
        ["Tools", "Web", "DeepSeekProvider"],
        ["Worktrees", "BranchPrefix"],
        ["SubAgent", "DisabledProfiles"],
        ["SubAgent", "EnableExternalCliSessionResume"],
        ["SubAgent", "ProviderPreferences"],
        ["SubAgent", "Models"]
    ];

    public static void Apply(string craftPath, string userConfigPath)
    {
        var markerPath = Path.Combine(craftPath, MarkerFileName);
        if (File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == Version)
            return;
        var workspaceConfigPath = Path.Combine(craftPath, "config.json");
        try
        {
            AtomicConfigDocument.Update(workspaceConfigPath, workspace => Clean(workspace, userConfigPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return;
        }
        AtomicConfigDocument.Write(markerPath, Version + "\n");
    }

    private static void Clean(JsonObject workspace, string userConfigPath)
    {
        if (AtomicConfigDocument.Value(workspace, "ProviderId") is JsonValue providerValue
            && providerValue.TryGetValue<string>(out var providerId)
            && !string.IsNullOrWhiteSpace(providerId))
        {
            var preference = AtomicConfigDocument.Value(workspace, "ProviderPreferences") is JsonObject preferences
                && AtomicConfigDocument.Value(preferences, providerId) is { } entry
                    ? entry.DeepClone()
                    : null;
            AtomicConfigDocument.Update(userConfigPath, user =>
            {
                if (AtomicConfigDocument.Value(user, "ProviderId") is not null)
                    return;
                user["ProviderId"] = providerId;
                var userPreferences = AtomicConfigDocument.Object(user, "ProviderPreferences");
                if (preference != null && AtomicConfigDocument.Value(userPreferences, providerId) is null)
                    userPreferences[providerId] = preference;
            });
        }

        foreach (var keyPath in PersonalKeyPaths)
            Remove(workspace, keyPath, 0);
    }

    private static void Remove(JsonObject node, string[] keyPath, int index)
    {
        if (AtomicConfigDocument.Key(node, keyPath[index]) is not { } key)
            return;
        if (index == keyPath.Length - 1)
        {
            node.Remove(key);
            return;
        }
        if (node[key] is not JsonObject child)
            return;
        Remove(child, keyPath, index + 1);
        if (child.Count == 0)
            node.Remove(key);
    }
}
