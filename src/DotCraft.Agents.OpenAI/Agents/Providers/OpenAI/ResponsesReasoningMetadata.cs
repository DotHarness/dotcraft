using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Agents;

internal static class ResponsesReasoningMetadata
{
    internal const string Key = "openai.responses.reasoning";

    public static JsonObject CreateEnvelope() => new()
    {
        ["version"] = 1,
        ["item"] = new JsonObject
        {
            ["type"] = "reasoning",
            ["content"] = new JsonArray(),
            ["summary"] = new JsonArray()
        }
    };

    public static JsonObject ReadNative(ReasoningResponseItem item) =>
        JsonNode.Parse(ModelReaderWriter.Write(item).ToString()) as JsonObject
        ?? throw Invalid();

    public static bool TryRead(TextReasoningContent content, out JsonObject item)
    {
        item = null!;
        if (content.AdditionalProperties?.TryGetValue(Key, out var metadata) != true)
            return false;

        try
        {
            var envelope = metadata switch
            {
                JsonObject obj => obj,
                JsonElement element => JsonNode.Parse(element.GetRawText()) as JsonObject,
                _ => null
            };
            if (envelope?["version"] is not JsonValue version
                || !version.TryGetValue<int>(out var number) || number != 1
                || envelope["item"] is not JsonObject native)
                throw Invalid();
            Validate(native);
            item = (JsonObject)native.DeepClone();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid();
        }
    }

    internal static void Validate(JsonObject item)
    {
        if (item["type"]?.GetValue<string>() != "reasoning")
            throw Invalid();
        ValidateParts(item, "content", "reasoning_text");
        ValidateParts(item, "summary", "summary_text");
        foreach (var key in new[] { "id", "encrypted_content" })
            if (item[key] is { } node && (node is not JsonValue value || !value.TryGetValue<string>(out _)))
                throw Invalid();
    }

    private static void ValidateParts(JsonObject item, string key, string type)
    {
        if (item[key] is null)
            return;
        if (item[key] is not JsonArray parts)
            throw Invalid();
        foreach (var part in parts)
        {
            if (part is not JsonObject obj
                || obj["text"] is not JsonValue text || !text.TryGetValue<string>(out _))
                throw Invalid();
            var partType = obj["type"]?.GetValue<string>();
            if (partType != type && (key != "content" || partType != "text"))
                throw Invalid();
        }
    }

    private static InvalidDataException Invalid() =>
        new("responses_reasoning_metadata_invalid: Invalid or unsupported reasoning replay metadata.");
}
