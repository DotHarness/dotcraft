using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.AppServer;

internal sealed partial class WorkspaceRequestHandler
{
    private static CodeModeConfigUpdate ParseCodeModeConfigUpdate(JsonElement paramsElement)
    {
        var hasMode = TryGetCaseInsensitiveProperty(paramsElement, "toolsCodeModeMode", out var modeEl);
        return new CodeModeConfigUpdate(
            hasMode,
            hasMode ? NormalizeCodeMode(ParseNullableString(modeEl, "toolsCodeModeMode"), strict: true) : null);
    }

    private static CodeModeConfigSaveResult ApplyCodeModeConfigUpdate(JsonObject root, CodeModeConfigUpdate update)
    {
        var tools = GetOrCreateConfigSection(root, "Tools", createIfMissing: update.HasMode);
        var section = tools == null
            ? null
            : GetOrCreateConfigSection(tools, "CodeMode", createIfMissing: update.HasMode);
        var modeKey = section == null ? null : FindCaseInsensitiveKey(section, "Mode");
        var existingMode = NormalizeCodeMode(ReadConfigStringValue(section, modeKey), strict: false);
        var changed = update.HasMode && !string.Equals(existingMode, update.Mode, StringComparison.Ordinal);

        if (update.HasMode)
        {
            UpsertOrRemoveConfigValue(section!, modeKey, "Mode", update.Mode);
            RemoveConfigSectionIfEmpty(tools!, "CodeMode");
            RemoveConfigSectionIfEmpty(root, "Tools");
        }

        return new CodeModeConfigSaveResult(update.HasMode ? update.Mode : existingMode, changed);
    }

    private static string? NormalizeCodeMode(string? rawMode, bool strict)
    {
        var trimmed = rawMode?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.ToLowerInvariant() switch
        {
            "off" => "off",
            "on" => "on",
            "only" => "only",
            _ when strict => throw AppServerErrors.InvalidParams("'toolsCodeModeMode' must be 'off', 'on', 'only', or null."),
            _ => null
        };
    }

    private sealed record CodeModeConfigUpdate(bool HasMode, string? Mode);

    private sealed record CodeModeConfigSaveResult(string? Mode, bool Changed);
}
