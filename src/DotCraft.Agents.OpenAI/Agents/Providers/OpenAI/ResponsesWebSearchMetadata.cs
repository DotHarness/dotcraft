using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Agents;

internal static class ResponsesWebSearchMetadata
{
    internal const string Key = "openai.responses.web_search";

    public static void Apply(ChatResponseUpdate update)
    {
        if (!update.Contents.Any(static content => content is WebSearchToolCallContent or WebSearchToolResultContent))
            return;

        update.Role = ChatRole.Assistant;
        foreach (var result in update.Contents.OfType<WebSearchToolResultContent>())
        {
            if (result.RawRepresentation is not WebSearchCallResponseItem raw)
                throw Invalid();
            result.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            result.AdditionalProperties[Key] = new JsonObject
            {
                ["version"] = 1,
                ["item"] = ReadNative(raw, result.CallId)
            };
        }
    }

    public static JsonObject ReadNative(WebSearchCallResponseItem raw, string callId)
    {
        try
        {
            var item = JsonNode.Parse(ModelReaderWriter.Write(raw).ToString()) as JsonObject
                       ?? throw Invalid();
            Validate(item, callId);
            return item;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or NotSupportedException)
        {
            throw Invalid();
        }
    }

    public static bool TryRead(WebSearchToolResultContent result, out JsonObject item)
    {
        item = null!;
        if (result.AdditionalProperties?.TryGetValue(Key, out var metadata) != true)
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
            Validate(native, result.CallId);
            item = (JsonObject)native.DeepClone();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid();
        }
    }

    private static void Validate(JsonObject item, string callId)
    {
        if (item["type"]?.GetValue<string>() != "web_search_call")
            throw Invalid();
        foreach (var key in new[] { "id", "status", "encrypted_content" })
            ValidateString(item, key);
        if (item["id"] is JsonValue id && id.GetValue<string>() != callId)
            throw Invalid();
        if (item["action"] is not { } action)
            return;
        if (action is not JsonObject details || details["type"] is not JsonValue type
            || !type.TryGetValue<string>(out _))
            throw Invalid();
        foreach (var key in new[] { "query", "url", "pattern" })
            ValidateString(details, key);
        if (details["queries"] is { } queries
            && (queries is not JsonArray array
                || array.Any(static query => query is not JsonValue value || !value.TryGetValue<string>(out _))))
            throw Invalid();
    }

    private static void ValidateString(JsonObject item, string key)
    {
        if (item[key] is { } node && (node is not JsonValue value || !value.TryGetValue<string>(out _)))
            throw Invalid();
    }

    internal static InvalidDataException Invalid() =>
        new("responses_web_search_metadata_invalid: Invalid or unsupported web search replay metadata.");
}
