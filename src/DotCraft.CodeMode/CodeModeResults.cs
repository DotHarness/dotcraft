using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.CodeMode;

public static class CodeModeResults
{
    internal const string ResultTooLargeErrorCode = "code_mode_result_too_large";

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
                return CheckSize(JsonNode.Parse(rawError.GetRawText()), definition.Name, maxBytes, contentEnvelope: true);
            var code = result.Error?.Code ?? ToolErrorCodes.ExecutionFailed;
            var message = result.Error?.Message ?? result.Content ?? "Tool execution failed.";
            if (result.Error is { } sourceError
                && sourceError.Parameters.TryGetValue("sourceErrorCode", out var sourceCode)
                && sourceCode.ValueKind == JsonValueKind.String
                && sourceCode.GetString() is { Length: > 0 } sourceCodeText
                && !string.Equals(sourceCodeText, code, StringComparison.Ordinal))
                message = $"{sourceCodeText}: {message}";
            return (null, $"{code}: {message}");
        }

        if (isMcp && result.RawSourceResult is { } raw)
            return CheckSize(JsonNode.Parse(raw.GetRawText()), definition.Name, maxBytes, contentEnvelope: true);
        if ((definition.OutputSchema is not null || CodeModeDeclarations.IsCommandExecution(definition))
            && result.StructuredContent is { } structured)
            return CheckSize(JsonNode.Parse(structured.GetRawText()), definition.Name, maxBytes);
        if (definition.OutputSchema is { } schema && !IsStringSchema(schema) && TryParse(result.SourceContent, out var parsed))
            return CheckSize(parsed, definition.Name, maxBytes);
        if (result.SourceContentItems?.Any(IsImage) == true)
            return CheckSize(ContentResult(result.SourceContentItems), definition.Name, maxBytes, contentEnvelope: true);
        return CheckSize(JsonValue.Create(result.SourceContent ?? string.Empty), definition.Name, maxBytes);
    }

    private static JsonObject ContentResult(IEnumerable<AIContent> items) => new()
    {
        ["content"] = new JsonArray(items.Select(static item => item switch
        {
            TextContent text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
            DataContent data when IsImage(data) => new JsonObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(data.Data.Span),
                ["mimeType"] = data.MediaType
            },
            _ => null
        }).OfType<JsonNode>().ToArray())
    };

    private static bool IsImage(AIContent item) => item is DataContent data && data.HasTopLevelMediaType("image");

    private static (JsonNode? Value, string? Error) CheckSize(
        JsonNode? value,
        ToolName name,
        int maxBytes,
        bool contentEnvelope = false)
    {
        var bytes = Encoding.UTF8.GetByteCount(value?.ToJsonString() ?? "null");
        if (contentEnvelope && value is JsonObject result && result["content"] is JsonArray content)
        {
            bytes -= content.OfType<JsonObject>()
                .Where(static block => (string?)block["type"] == "image")
                .Sum(static block => block["data"] is JsonValue data && data.TryGetValue<string>(out _)
                    ? Encoding.UTF8.GetByteCount(data.ToJsonString()) - 2
                    : 0);
        }
        if (bytes <= maxBytes)
            return (value, null);
        return (null, $"{ResultTooLargeErrorCode}: Tool '{name}' has already run, but its result exceeds the {maxBytes}-byte program limit. Request a smaller page or less data; do not automatically repeat calls with side effects.");
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
}
