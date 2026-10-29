using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal static class ResponsesCompactRequestFitter
{
    internal const string TruncatedOutputNotice = "Output exceeded the available model context and was truncated";

    public static IReadOnlyList<JsonElement> Fit(
        IReadOnlyList<JsonElement> items,
        ChatOptions? options,
        int? requestTokenLimit)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (requestTokenLimit is not { } limit || limit <= 0 || items.Count == 0)
            return items;

        var itemTokens = items.Select(EstimateItem).ToList();
        var total = OpenAIResponsesNativeTokenEstimator.Estimate([], [], options) + itemTokens.Sum();
        if (total <= limit)
            return items;

        var fitted = items.ToList();
        for (var index = fitted.Count - 1; index >= 0 && total > limit; index--)
        {
            if (TryTruncateToolOutput(fitted[index]) is not { } truncated)
                break;
            var tokens = EstimateItem(truncated);
            total += tokens - itemTokens[index];
            fitted[index] = truncated;
            itemTokens[index] = tokens;
        }

        var start = 0;
        while (total > limit && NextUserMessage(fitted, start + 1) is { } next)
        {
            for (var index = start; index < next; index++)
                total -= itemTokens[index];
            start = next;
        }

        return start == 0 ? fitted : fitted.GetRange(start, fitted.Count - start);
    }

    private static long EstimateItem(JsonElement item) =>
        OpenAIResponsesNativeTokenEstimator.Estimate([item], [], null);

    private static JsonElement? TryTruncateToolOutput(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || ReadString(item, "type") is not { } type)
            return null;
        if (type is not ("function_call_output" or "custom_tool_call_output" or "tool_search_output"))
            return null;

        var node = JsonNode.Parse(item.GetRawText())!.AsObject();
        if (type == "tool_search_output")
            node["tools"] = new JsonArray();
        else
            node["output"] = TruncatedOutputNotice;
        return JsonSerializer.SerializeToElement(node);
    }

    private static int? NextUserMessage(IReadOnlyList<JsonElement> items, int from)
    {
        for (var index = from; index < items.Count; index++)
        {
            var item = items[index];
            if (item.ValueKind == JsonValueKind.Object
                && ReadString(item, "role") == "user"
                && ReadString(item, "type") is null or "message")
                return index;
        }
        return null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
