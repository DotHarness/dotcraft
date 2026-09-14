using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Agents;

internal static class ResponsesCompactionHistory
{
    internal const int RetainedTokenBudget = 64_000;

    internal static IReadOnlyList<JsonElement> Build(
        IReadOnlyList<ProviderHistoryItem> input,
        JsonElement compaction,
        int tokenBudget = RetainedTokenBudget)
    {
        var retained = new List<JsonElement>();
        long remaining = Math.Max(0, tokenBudget);
        foreach (var entry in input.Reverse())
        {
            var item = entry.Payload;
            if (!IsRetainedMessage(item))
                continue;
            if (remaining <= 0)
                break;
            var tokens = Estimate(item);
            if (tokens <= remaining)
            {
                retained.Add(item.Clone());
                remaining -= tokens;
                continue;
            }

            if (TruncateBoundary(item, remaining) is { } boundary)
                retained.Add(boundary);
            break;
        }
        retained.Reverse();
        retained.Add(compaction.Clone());
        return retained;
    }

    private static bool IsRetainedMessage(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
        && type.GetString() == "message"
        && item.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String
        && role.GetString() is "user" or "developer";

    private static JsonElement? TruncateBoundary(JsonElement item, long budget)
    {
        var message = JsonNode.Parse(item.GetRawText())!.AsObject();
        if (message["content"] is not JsonArray content)
            return null;
        var retained = new JsonArray();
        message["content"] = retained;
        foreach (var original in content.Reverse())
        {
            var candidate = original?.DeepClone();
            retained.Insert(0, candidate);
            if (Estimate(message) <= budget)
                continue;
            if (candidate is JsonObject part
                && part["type"]?.GetValue<string>() is "input_text" or "output_text"
                && part["text"] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                var low = 0;
                var high = text.Length;
                while (low < high)
                {
                    var middle = low + (high - low + 1) / 2;
                    part["text"] = Prefix(text, middle);
                    if (Estimate(message) <= budget)
                        low = middle;
                    else
                        high = middle - 1;
                }
                part["text"] = Prefix(text, low);
                if (low > 0 && Estimate(message) <= budget)
                    break;
            }
            retained.RemoveAt(0);
            break;
        }
        return retained.Count > 0 && Estimate(message) <= budget
            ? JsonSerializer.SerializeToElement(message) : null;
    }

    private static string Prefix(string text, int length)
    {
        if (length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]))
            length--;
        return text[..length];
    }

    private static long Estimate(JsonNode item) => Estimate(JsonSerializer.SerializeToElement(item));

    private static long Estimate(JsonElement item) =>
        OpenAIResponsesNativeTokenEstimator.Estimate([item], [], null);
}
