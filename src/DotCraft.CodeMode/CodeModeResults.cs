using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;

namespace DotCraft.CodeMode;

public static class CodeModeResults
{
    public static (JsonNode? Value, string? Error) ToScriptValue(
        ToolRegistration registration,
        ToolExecutionResult result,
        int maxBytes)
    {
        var definition = registration.Definition;
        var isMcp = CodeModeDeclarations.IsMcp(definition);
        if (!result.Success)
        {
            if (isMcp && result.RawSourceResult is { } rawError && result.Error?.Code == ToolErrorCodes.ExecutionFailed)
                return (Bound(JsonNode.Parse(rawError.GetRawText()), maxBytes), null);
            var code = result.Error?.Code ?? ToolErrorCodes.ExecutionFailed;
            var message = result.Error?.Message ?? result.Content ?? "Tool execution failed.";
            return (null, $"{code}: {message}");
        }

        JsonNode? value;
        if (isMcp && result.RawSourceResult is { } raw)
            value = JsonNode.Parse(raw.GetRawText());
        else if ((definition.OutputSchema is not null || CodeModeDeclarations.IsCommandExecution(definition))
                 && result.StructuredContent is { } structured)
            value = JsonNode.Parse(structured.GetRawText());
        else if (definition.OutputSchema is { } schema && !IsStringSchema(schema) && TryParse(result.Content, out var parsed))
            value = parsed;
        else
            value = JsonValue.Create(result.Content ?? string.Empty);
        return (Bound(value, maxBytes), null);
    }

    private static bool IsStringSchema(JsonElement schema) =>
        schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "string";

    private static bool TryParse(string? content, out JsonNode? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(content))
            return false;
        try
        {
            value = JsonNode.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonNode? Bound(JsonNode? value, int maxBytes)
    {
        var text = value is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.String
            ? scalar.GetValue<string>()
            : null;
        var json = value?.ToJsonString() ?? "null";
        if (Encoding.UTF8.GetByteCount(json) <= maxBytes)
            return value;

        var marker = $"\n[Result truncated to {maxBytes / 1024} KiB; the complete result is in the tool call item.]";
        return JsonValue.Create(Truncate(text ?? json, maxBytes - Encoding.UTF8.GetByteCount(marker)) + marker);
    }

    private static string Truncate(string text, int maxBytes)
    {
        var length = Math.Min(text.Length, Math.Max(0, maxBytes));
        while (length > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) > maxBytes)
            length = Math.Max(0, length - Math.Max(1, (Encoding.UTF8.GetByteCount(text.AsSpan(0, length)) - maxBytes) / 3));
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;
        return text[..length];
    }
}
