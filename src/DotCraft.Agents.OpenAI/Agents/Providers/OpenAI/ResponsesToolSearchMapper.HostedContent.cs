using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Agents;

internal static partial class ResponsesToolSearchMapper
{
    internal static bool TryCreateHostedImageGenerationContent(
        ResponseItem item,
        out HostedImageGenerationContent content)
    {
        content = null!;
        if (item is not ImageGenerationCallResponseItem image)
            return false;
        var status = image.Status switch
        {
            ImageGenerationCallStatus.Completed => "completed",
            ImageGenerationCallStatus.Failed => "failed",
            _ => null
        };
        if (status is null)
            return false;
        var bytes = image.ImageResultBytes?.ToArray();
        content = new HostedImageGenerationContent
        {
            Id = image.Id,
            Status = status,
            RevisedPrompt = image.RevisedPrompt,
            ImageBytes = status == "completed" ? bytes : null,
            MediaType = "image/png",
            ErrorMessage = status == "failed"
                ? (TryReadJsonObjectFromRaw(image, out var raw) ? ReadImageGenerationError(raw) : null) ?? "Image generation failed."
                : bytes is not { Length: > 0 } ? "Image generation completed without image data." : null
        };
        return true;
    }

    private static string? ReadImageGenerationError(JsonObject rawObject)
    {
        if (ReadJsonString(rawObject, "error") is { } textError)
            return textError;

        if (!rawObject.TryGetPropertyValue("error", out var errorNode) || errorNode is not JsonObject errorObject)
            return null;

        var message = ReadJsonString(errorObject, "message");
        var code = ReadJsonString(errorObject, "code");
        return string.IsNullOrWhiteSpace(code)
            ? message
            : string.IsNullOrWhiteSpace(message)
                ? code
                : $"{code}: {message}";
    }

    internal static bool TryGetFunctionCallNamespace(
        FunctionCallContent call,
        out string functionNamespace)
    {
        functionNamespace = string.Empty;

        if (TryReadString(call.AdditionalProperties, FunctionCallNamespaceMetadataKey, out functionNamespace)
            || TryReadString(call.AdditionalProperties, "namespace", out functionNamespace))
        {
            return true;
        }

        return TryReadJsonObjectFromRaw(call.RawRepresentation, out var rawObject)
               && TryReadString(rawObject, "namespace", out functionNamespace);
    }

    private static bool TryReadFunctionCallNamespace(
        ResponseItem item,
        out string callId,
        out string functionNamespace)
    {
        callId = string.Empty;
        functionNamespace = string.Empty;

        if (!TryReadJsonObjectFromRaw(item, out var rawObject))
            return false;
        if (!string.Equals(ReadJsonString(rawObject, "type"), "function_call", StringComparison.Ordinal))
            return false;
        if (!TryReadString(rawObject, "namespace", out functionNamespace))
            return false;

        callId = ReadJsonString(rawObject, "call_id")
            ?? ReadJsonString(rawObject, "id")
            ?? string.Empty;
        return !string.IsNullOrWhiteSpace(callId);
    }

    private static bool TryReadJsonObjectFromRaw(
        object? rawRepresentation,
        out JsonObject rawObject)
    {
        rawObject = null!;
        if (rawRepresentation == null)
            return false;

        try
        {
            if (rawRepresentation is JsonObject obj)
            {
                rawObject = obj;
                return true;
            }

            if (rawRepresentation is JsonElement { ValueKind: JsonValueKind.Object } element)
            {
                if (OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(element.GetRawText()) is not { } normalizedJson)
                    return false;

                rawObject = JsonNode.Parse(normalizedJson) as JsonObject ?? [];
                return true;
            }

            var rawJson = ModelReaderWriter.Write(rawRepresentation).ToString();
            if (OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(rawJson) is not { } normalizedRawJson)
                return false;

            rawObject = JsonNode.Parse(normalizedRawJson) as JsonObject ?? [];
            return rawObject.Count > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadString(
        AdditionalPropertiesDictionary? properties,
        string name,
        out string value)
    {
        value = string.Empty;
        if (properties == null || !properties.TryGetValue(name, out var propertyValue))
            return false;

        switch (propertyValue)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                value = text;
                return true;

            case JsonElement { ValueKind: JsonValueKind.String } element
                when !string.IsNullOrWhiteSpace(element.GetString()):
                value = element.GetString()!;
                return true;

            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var text)
                                      && !string.IsNullOrWhiteSpace(text):
                value = text;
                return true;

            default:
                return false;
        }
    }

    private static bool TryReadBool(
        AdditionalPropertiesDictionary? properties,
        string name,
        out bool value)
    {
        value = false;
        if (properties == null || !properties.TryGetValue(name, out var propertyValue))
            return false;

        switch (propertyValue)
        {
            case bool boolean:
                value = boolean;
                return true;

            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;

            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;

            case JsonValue jsonValue when jsonValue.TryGetValue<bool>(out var boolean):
                value = boolean;
                return true;

            case string text when bool.TryParse(text, out var boolean):
                value = boolean;
                return true;

            default:
                return false;
        }
    }

    private static bool TryReadString(
        JsonObject obj,
        string name,
        out string value)
    {
        value = string.Empty;
        var text = ReadJsonString(obj, name);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        value = text;
        return true;
    }

    private static JsonNode CloneJsonElement(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined
            ? new JsonObject { ["type"] = "object" }
            : JsonNode.Parse(element.GetRawText()) ?? new JsonObject { ["type"] = "object" };

    private static JsonElement GetJsonSchema(AITool tool) =>
        tool is AIFunction function
            ? function.JsonSchema
            : JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object" });

    private static JsonObject CloneJsonObject(JsonObject obj) =>
        JsonNode.Parse(obj.ToJsonString(JsonOptions)) as JsonObject ?? [];

    private static JsonArray CloneJsonArray(JsonArray array) =>
        JsonNode.Parse(array.ToJsonString(JsonOptions)) as JsonArray ?? [];

    private static void PatchValue<T>(CreateResponseOptions options, string path, T value)
    {
#pragma warning disable SCME0001
        options.Patch.Set(
            Encoding.UTF8.GetBytes(path),
            BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));
#pragma warning restore SCME0001
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string NormalizeReasoningEffortToken(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.ExtraHigh => "xhigh",
        _ => NormalizeEnumToken(effort.ToString())
    };

    private static string NormalizeEnumToken(string value)
    {
        var chars = value.Where(static ch => ch is not '-' and not '_' and not ' ').ToArray();
        return new string(chars).ToLowerInvariant();
    }

    private sealed record PromptCacheKeyResolution(string? Value, string? Source);
}
