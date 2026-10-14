using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

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
                return (BoundContent(JsonNode.Parse(rawError.GetRawText()), maxBytes), null);
            var code = result.Error?.Code ?? ToolErrorCodes.ExecutionFailed;
            var message = result.Error?.Message ?? result.Content ?? "Tool execution failed.";
            return (null, $"{code}: {message}");
        }

        if (isMcp && result.RawSourceResult is { } raw)
            return (BoundContent(JsonNode.Parse(raw.GetRawText()), maxBytes), null);
        if ((definition.OutputSchema is not null || CodeModeDeclarations.IsCommandExecution(definition))
            && result.StructuredContent is { } structured)
            return (Bound(JsonNode.Parse(structured.GetRawText()), maxBytes), null);
        if (definition.OutputSchema is { } schema && !IsStringSchema(schema) && TryParse(result.Content, out var parsed))
            return (Bound(parsed, maxBytes), null);
        if (result.ContentItems?.Any(IsImage) == true)
            return (BoundContent(ContentResult(result.ContentItems), maxBytes), null);
        return (Bound(JsonValue.Create(result.Content ?? string.Empty), maxBytes), null);
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

    private static JsonNode? BoundContent(JsonNode? value, int maxBytes)
    {
        if (value is not JsonObject result || result["content"] is not JsonArray content)
            return Bound(value, maxBytes);
        var images = content.OfType<JsonObject>().Where(static block => (string?)block["type"] == "image").ToList();
        var imageBytes = images.Sum(static image => Encoding.UTF8.GetByteCount((string?)image["data"] ?? string.Empty));
        if (Encoding.UTF8.GetByteCount(result.ToJsonString()) - imageBytes <= maxBytes)
            return result;
        var text = string.Join("\n", content.OfType<JsonObject>()
            .Where(static block => (string?)block["type"] == "text")
            .Select(static block => (string?)block["text"]));
        var bounded = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = Bound(JsonValue.Create(text), maxBytes) });
        foreach (var image in images)
            bounded.Add(image.DeepClone());
        return new JsonObject { ["content"] = bounded };
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
