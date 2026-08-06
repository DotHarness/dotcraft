using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Agents;

internal static partial class ResponsesToolSearchMapper
{
    internal static JsonElement NormalizeProviderHistoryItem(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || !string.Equals(
                type.GetString(),
                HostedImageGenerationContent.ToolName + "_call",
                StringComparison.Ordinal))
        {
            return item.Clone();
        }

        var projected = CreateImageGenerationCallItem(
            ReadJsonString(item, "status"),
            ReadJsonString(item, "id"),
            ReadJsonString(item, "revised_prompt"),
            ReadJsonString(item, "result"));
        return JsonSerializer.SerializeToElement(projected, JsonOptions);
    }

    private static JsonObject CreateImageGenerationCallItem(HostedImageGenerationContent content) =>
        CreateImageGenerationCallItem(
            content.Status,
            content.Id,
            content.RevisedPrompt,
            content.ImageBytes is { Length: > 0 }
                ? Convert.ToBase64String(content.ImageBytes)
                : null);

    private static JsonObject CreateImageGenerationCallItem(
        string? status,
        string? id,
        string? revisedPrompt,
        string? result)
    {
        var item = new JsonObject
        {
            ["type"] = HostedImageGenerationContent.ToolName + "_call",
            ["status"] = string.IsNullOrWhiteSpace(status) ? "completed" : status
        };

        if (!string.IsNullOrWhiteSpace(id))
            item["id"] = id;
        if (!string.IsNullOrWhiteSpace(revisedPrompt))
            item["revised_prompt"] = revisedPrompt;
        if (!string.IsNullOrWhiteSpace(result))
            item["result"] = result;
        return item;
    }

    private static JsonObject CreateFunctionCallOutputItem(FunctionResultContent result) =>
        new()
        {
            ["type"] = "function_call_output",
            ["call_id"] = result.CallId,
            ["output"] = SerializeResult(result.Result)
        };

    private static bool TryCreateReasoningItem(
        TextReasoningContent reasoning,
        out JsonObject item)
    {
        item = null!;

        if (TryCreateReasoningItemFromRaw(reasoning.RawRepresentation, out item))
        {
            if (!item.ContainsKey("encrypted_content") && !string.IsNullOrWhiteSpace(reasoning.ProtectedData))
                item["encrypted_content"] = reasoning.ProtectedData;
            EnsureReasoningDefaults(item);
            return true;
        }

        if (string.IsNullOrWhiteSpace(reasoning.ProtectedData))
            return false;

        item = new JsonObject
        {
            ["type"] = "reasoning",
            ["content"] = new JsonArray(),
            ["summary"] = new JsonArray(),
            ["encrypted_content"] = reasoning.ProtectedData
        };
        return true;
    }

    private static bool TryCreateReasoningItemFromRaw(
        object? rawRepresentation,
        out JsonObject item)
    {
        item = null!;
        if (rawRepresentation == null)
            return false;

        try
        {
            var rawJson = ModelReaderWriter.Write(rawRepresentation).ToString();
            if (JsonNode.Parse(rawJson) is not JsonObject obj)
                return false;
            if (!string.Equals(ReadJsonString(obj, "type"), "reasoning", StringComparison.Ordinal))
                return false;

            item = CloneJsonObject(obj);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException)
        {
            return false;
        }
    }

    private static void EnsureReasoningDefaults(JsonObject item)
    {
        item["type"] = "reasoning";
        if (!item.ContainsKey("content"))
            item["content"] = new JsonArray();
        if (!item.ContainsKey("summary"))
            item["summary"] = new JsonArray();
    }

    private static JsonObject CreateToolSearchOutputItem(FunctionResultContent result) =>
        new()
        {
            ["type"] = "tool_search_output",
            ["execution"] = "client",
            ["call_id"] = result.CallId,
            ["status"] = "completed",
            ["tools"] = ExtractToolSearchTools(result.Result)
        };

    private static JsonObject CreateToolSearchArgumentsObject(object? arguments)
    {
        var args = ArgumentsToJsonObject(arguments);
        var normalized = CloneJsonObject(args);

        if (!normalized.ContainsKey("query")
            && ReadJsonString(args, "q") is { } q)
        {
            normalized["query"] = q;
        }

        if (!normalized.ContainsKey("max_results")
            && ReadJsonInt(args, "maxResults") is { } maxResults)
        {
            normalized["max_results"] = maxResults;
        }

        if (!normalized.ContainsKey("query"))
            normalized["query"] = string.Empty;

        normalized.Remove("q");
        normalized.Remove("maxResults");
        return normalized;
    }

    private static Dictionary<string, object?> ReadToolSearchArguments(JsonElement item)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (TryGetProperty(item, "arguments", out var argumentsElement))
        {
            if (argumentsElement.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    argumentsElement.GetRawText(),
                    JsonOptions) ?? arguments;
            }

            if (argumentsElement.ValueKind == JsonValueKind.String
                && argumentsElement.GetString() is { } argumentsText
                && TryDeserializeArguments(argumentsText, out var parsedArguments))
            {
                return parsedArguments;
            }
        }

        if (ReadString(item, "query") is { } query)
            arguments["query"] = query;
        return arguments;
    }

    private static string ConcatenateInstructionContent(IEnumerable<AIContent> contents)
    {
        var sb = new StringBuilder();
        var needsSeparatorBeforeText = false;
        foreach (var content in contents)
        {
            if (content is TextContent text)
            {
                if (needsSeparatorBeforeText
                    && !string.IsNullOrEmpty(text.Text)
                    && sb.Length > 0
                    && !char.IsWhiteSpace(sb[sb.Length - 1]))
                {
                    sb.AppendLine();
                }

                sb.Append(text.Text);
                needsSeparatorBeforeText = false;
                continue;
            }

            if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]))
                sb.AppendLine();
            sb.Append(DescribeUnsupportedContent(content));
            needsSeparatorBeforeText = true;
        }

        return sb.ToString();
    }

    private static JsonArray ExtractToolSearchTools(object? result)
    {
        var node = JsonSerializer.SerializeToNode(result, JsonOptions);
        if (node is JsonObject obj
            && (obj["tools"] as JsonArray ?? obj["Tools"] as JsonArray) is { } tools)
        {
            return CloneJsonArray(tools);
        }

        return [];
    }

    private static bool IsToolSearchOutput(object? result) =>
        ExtractToolSearchTools(result).Count > 0;

    private static bool IsHostedImageGenerationEnabled(ChatOptions? options) =>
        TryReadBool(options?.AdditionalProperties, HostedImageGenerationEnabledAdditionalProperty, out var enabled) &&
        enabled;

    private static bool IsReservedImageGenerationFunction(AITool tool)
    {
        if (!string.Equals(tool.Name, OpenAIHostedToolNames.ImageGenerationFunction, StringComparison.Ordinal))
            return false;

        return ToolNamespaceMetadataResolver.TryGet(tool, out var toolNamespace) &&
               string.Equals(toolNamespace, OpenAIHostedToolNames.ImageGenerationNamespace, StringComparison.Ordinal);
    }

    private static string SerializeArguments(object? arguments)
    {
        if (arguments == null)
            return "{}";
        if (arguments is string text)
            return text;
        if (arguments is JsonElement element)
            return element.GetRawText();
        if (arguments is JsonObject node)
            return node.ToJsonString(JsonOptions);
        return JsonSerializer.Serialize(arguments, JsonOptions);
    }

    private static string SerializeResult(object? result)
    {
        if (result == null)
            return string.Empty;
        if (result is string text)
            return text;
        if (result is AIContent content)
            return SerializeAIContentResult([content]);
        if (result is IEnumerable<AIContent> contents)
            return SerializeAIContentResult(contents);
        if (result is JsonElement element)
            return element.GetRawText();
        if (result is JsonNode node)
            return node.ToJsonString(JsonOptions);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static string SerializeAIContentResult(IEnumerable<AIContent> contents)
    {
        var parts = new List<string>();
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    parts.Add(text.Text);
                    break;

                case DataContent data:
                    parts.Add(DescribeUnsupportedContent(data));
                    break;

                case UriContent uri:
                    parts.Add(DescribeUnsupportedContent(uri));
                    break;

                default:
                    parts.Add(DescribeUnsupportedContent(content));
                    break;
            }
        }

        return string.Join("\n", parts);
    }

    private static string DescribeUnsupportedContent(AIContent content)
    {
        var typeName = content.GetType().Name;
        var mediaType = content switch
        {
            DataContent data => NormalizeMediaType(data.MediaType),
            UriContent uri => NormalizeMediaType(uri.MediaType),
            _ => null
        };

        return mediaType == null
            ? $"[Unsupported content: {typeName}]"
            : $"[Unsupported content: {typeName} ({mediaType})]";
    }

    private static string NormalizeMediaType(string? mediaType) =>
        string.IsNullOrWhiteSpace(mediaType)
            ? "application/octet-stream"
            : mediaType.Trim();

    private static JsonObject ArgumentsToJsonObject(object? arguments)
    {
        if (arguments is JsonObject jsonObject)
            return CloneJsonObject(jsonObject);
        if (arguments is JsonElement element && element.ValueKind == JsonValueKind.Object)
            return JsonNode.Parse(element.GetRawText()) as JsonObject ?? [];
        if (arguments == null)
            return [];

        var node = JsonSerializer.SerializeToNode(arguments, JsonOptions);
        return node as JsonObject ?? [];
    }

    private static string? ReadJsonString(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var value) && value != null
            ? value.GetValue<string?>()
            : null;

    private static string? ReadJsonString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadJsonInt(JsonObject obj, string name)
    {
        if (!obj.TryGetPropertyValue(name, out var value) || value == null)
            return null;

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var number))
                return number;
            if (jsonValue.TryGetValue<long>(out var longNumber))
                return checked((int)longNumber);
            if (jsonValue.TryGetValue<string>(out var text) && int.TryParse(text, out number))
                return number;
        }

        return null;
    }

    private static bool TryDeserializeArguments(
        string argumentsText,
        out Dictionary<string, object?> arguments)
    {
        try
        {
            arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                argumentsText,
                JsonOptions) ?? [];
            return true;
        }
        catch (JsonException)
        {
            arguments = [];
            return false;
        }
    }

    private static bool TryCreateSyntheticToolSearchCall(
        ResponseItem item,
        out FunctionCallResponseItem functionCall)
    {
        functionCall = null!;

        try
        {
            var itemJson = ModelReaderWriter.Write(item).ToString();
            if (OpenAIResponsesRequestBodyCanonicalizer.NormalizeTopLevelObject(itemJson) is not { } normalizedJson)
                return false;

            using var document = JsonDocument.Parse(normalizedJson);
            var root = document.RootElement;
            if (!string.Equals(ReadString(root, "type"), "tool_search_call", StringComparison.Ordinal))
                return false;

            var callId = ReadString(root, "call_id")
                ?? ReadString(root, "id")
                ?? Guid.NewGuid().ToString("N");
            var arguments = ReadToolSearchArguments(root);
            functionCall = new FunctionCallResponseItem(
                callId,
                OpenAIHostedToolNames.ToolSearch,
                BinaryData.FromString(JsonSerializer.Serialize(arguments, JsonOptions)));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException or ArgumentException)
        {
            return false;
        }
    }

}
