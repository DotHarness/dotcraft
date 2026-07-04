using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Hooks;

internal sealed record HookParsedOutput(
    string? AdditionalContext,
    string? BlockReason,
    string? SystemMessage,
    string? RewakeSummary,
    bool Continue = true,
    bool SuppressOutput = false);

internal static class HookOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static HookParsedOutput Parse(string? stdout, string? stderr, HookEvent evt, bool rewakeCapable)
    {
        var json = TryFindFirstJsonObject(stdout);
        if (json == null)
        {
            var text = string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim();
            return new HookParsedOutput(
                AdditionalContext: text,
                BlockReason: rewakeCapable
                    ? FirstNonWhiteSpace(stderr, text)
                    : null,
                SystemMessage: null,
                RewakeSummary: null);
        }

        var decision = ReadString(json, "decision");
        var reason = ReadString(json, "reason") ?? ReadString(json, "stopReason");
        var continueValue = ReadBool(json, "continue");
        var suppressOutput = ReadBool(json, "suppressOutput") == true;
        var systemMessage = ReadString(json, "systemMessage");
        var rewakeSummary = ReadString(json, "rewakeSummary");
        var additionalContext = ReadAdditionalContext(json);

        if (string.Equals(decision, "block", StringComparison.OrdinalIgnoreCase))
            reason ??= additionalContext ?? FirstNonWhiteSpace(stderr, stdout);

        return new HookParsedOutput(
            AdditionalContext: additionalContext,
            BlockReason: reason,
            SystemMessage: systemMessage,
            RewakeSummary: rewakeSummary,
            Continue: continueValue != false,
            SuppressOutput: suppressOutput);
    }

    private static JsonObject? TryFindFirstJsonObject(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                continue;

            try
            {
                return JsonSerializer.Deserialize<JsonObject>(trimmed, JsonOptions);
            }
            catch (JsonException)
            {
                // Keep scanning; hooks sometimes write logs before the JSON line.
            }
        }

        var all = stdout.Trim();
        if (!all.StartsWith("{", StringComparison.Ordinal))
            return null;

        try
        {
            return JsonSerializer.Deserialize<JsonObject>(all, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadAdditionalContext(JsonObject json)
    {
        if (json["hookSpecificOutput"] is not JsonObject hookSpecific)
            return null;

        return ReadString(hookSpecific, "additionalContext");
    }

    private static string? ReadString(JsonObject json, string propertyName)
    {
        if (!json.TryGetPropertyValue(propertyName, out var node))
            return null;

        return node?.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : node?.ToJsonString();
    }

    private static bool? ReadBool(JsonObject json, string propertyName)
    {
        if (!json.TryGetPropertyValue(propertyName, out var node) || node == null)
            return null;

        return node.GetValueKind() == JsonValueKind.True
            ? true
            : node.GetValueKind() == JsonValueKind.False
                ? false
                : null;
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
