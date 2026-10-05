using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.AppServer;

internal sealed partial class WorkspaceRequestHandler
{
    private static InstantInterruptConfigUpdate ParseInstantInterruptConfigUpdate(JsonElement paramsElement)
    {
        var hasEnabled = TryGetCaseInsensitiveProperty(paramsElement, "instantInterruptEnabled", out var enabledEl);
        return new InstantInterruptConfigUpdate(
            hasEnabled,
            hasEnabled ? ParseNullableBoolean(enabledEl, "instantInterruptEnabled") : null);
    }

    private static InstantInterruptConfigSaveResult ApplyInstantInterruptConfigUpdate(
        JsonObject root,
        InstantInterruptConfigUpdate update)
    {
        var key = FindCaseInsensitiveKey(root, "InstantInterruptEnabled");
        var existing = ReadConfigBooleanValue(root, key);
        var changed = update.HasEnabled && existing != update.Enabled;

        if (update.HasEnabled)
            UpsertOrRemoveConfigValue(root, key, "InstantInterruptEnabled", update.Enabled);

        return new InstantInterruptConfigSaveResult(update.HasEnabled ? update.Enabled : existing, changed);
    }

    private sealed record InstantInterruptConfigUpdate(bool HasEnabled, bool? Enabled);

    private sealed record InstantInterruptConfigSaveResult(bool? Enabled, bool Changed);
}
