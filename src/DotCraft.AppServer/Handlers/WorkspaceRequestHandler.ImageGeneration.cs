using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.AppServer;

internal sealed partial class WorkspaceRequestHandler
{
    private static ImageGenerationConfigUpdate ParseImageGenerationConfigUpdate(JsonElement paramsElement)
    {
        var hasEnabled = TryGetCaseInsensitiveProperty(paramsElement, "toolsImageGenerationEnabled", out var enabledEl);
        var hasProvider = TryGetCaseInsensitiveProperty(paramsElement, "toolsImageGenerationProvider", out var providerEl);
        return new ImageGenerationConfigUpdate(
            hasEnabled,
            hasEnabled ? ParseNullableBoolean(enabledEl, "toolsImageGenerationEnabled") : null,
            hasProvider,
            hasProvider
                ? NormalizeOptionalString(ParseNullableString(providerEl, "toolsImageGenerationProvider"))
                : null);
    }

    private static ImageGenerationConfigSaveResult ApplyImageGenerationConfigUpdate(
        JsonObject root,
        ImageGenerationConfigUpdate update)
    {
        var tools = GetOrCreateConfigSection(root, "Tools", createIfMissing: update.HasAny);
        var section = tools == null
            ? null
            : GetOrCreateConfigSection(tools, "ImageGeneration", createIfMissing: update.HasAny);
        var enabledKey = section == null ? null : FindCaseInsensitiveKey(section, "Enabled");
        var providerKey = section == null ? null : FindCaseInsensitiveKey(section, "Provider");
        var existingEnabled = ReadConfigBooleanValue(section, enabledKey);
        var existingProvider = NormalizeOptionalString(ReadConfigStringValue(section, providerKey));
        var enabledChanged = update.HasEnabled && existingEnabled != update.Enabled;
        var providerChanged = update.HasProvider
            && !string.Equals(existingProvider, update.Provider, StringComparison.Ordinal);

        if (update.HasAny)
        {
            if (update.HasEnabled)
                UpsertOrRemoveConfigValue(section!, enabledKey, "Enabled", update.Enabled);
            if (update.HasProvider)
                UpsertOrRemoveConfigValue(section!, providerKey, "Provider", update.Provider);
            RemoveConfigSectionIfEmpty(tools!, "ImageGeneration");
            RemoveConfigSectionIfEmpty(root, "Tools");
        }

        return new ImageGenerationConfigSaveResult(
            update.HasEnabled ? update.Enabled : existingEnabled,
            update.HasProvider ? update.Provider : existingProvider,
            enabledChanged || providerChanged);
    }

    private sealed record ImageGenerationConfigUpdate(
        bool HasEnabled,
        bool? Enabled,
        bool HasProvider,
        string? Provider)
    {
        public bool HasAny => HasEnabled || HasProvider;
    }

    private sealed record ImageGenerationConfigSaveResult(bool? Enabled, string? Provider, bool Changed);
}
