using System.Text;
using System.Text.Json;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal static class OpenAIResponsesNativeTokenEstimator
{
    private const double BytesPerToken = 4.0;
    private const int ImageTokenCost = 2000;
    private const int AudioTokenCost = 1000;

    public static long Estimate(
        IReadOnlyList<JsonElement> items,
        IReadOnlyList<ChatMessage> pendingTail,
        ChatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(pendingTail);

        long visibleBytes = 0;
        foreach (var item in items)
            visibleBytes += EstimateElementBytes(item);

        if (!string.IsNullOrWhiteSpace(options?.Instructions))
            visibleBytes += Encoding.UTF8.GetByteCount(options.Instructions);
        if (!string.IsNullOrWhiteSpace(options?.ModelId))
            visibleBytes += Encoding.UTF8.GetByteCount(options.ModelId);

        if (options?.Tools is { Count: > 0 } tools)
        {
            foreach (var tool in tools)
            {
                visibleBytes += Encoding.UTF8.GetByteCount(tool.Name ?? string.Empty);
                visibleBytes += Encoding.UTF8.GetByteCount(tool.Description ?? string.Empty);
                if (tool is AIFunctionDeclaration function)
                    visibleBytes += Encoding.UTF8.GetByteCount(function.JsonSchema.GetRawText());
            }
        }
        if (options != null)
        {
            visibleBytes += EstimateOptionBytes(options.Reasoning);
            visibleBytes += EstimateOptionBytes(options.ResponseFormat);
            visibleBytes += EstimateOptionBytes(options.ToolMode);
            if (options.AllowMultipleToolCalls.HasValue)
                visibleBytes += 5;
            if (options.AdditionalProperties is { Count: > 0 } properties)
            {
                foreach (var property in properties)
                {
                    visibleBytes += Encoding.UTF8.GetByteCount(property.Key);
                    visibleBytes += EstimateOptionBytes(property.Value);
                }
            }
        }

        var nativeTokens = (long)Math.Ceiling(visibleBytes / BytesPerToken);
        return Math.Max(0, nativeTokens + MessageTokenEstimator.EstimateDelta(pendingTail));
    }

    private static long EstimateElementBytes(JsonElement element)
    {
        if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            return Encoding.UTF8.GetByteCount(element.GetRawText());

        long bytes = Encoding.UTF8.GetByteCount(element.GetRawText());
        Visit(element, ref bytes);
        return bytes;
    }

    private static void Visit(JsonElement element, ref long bytes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var type = TryGetString(element, "type");
                if (type is "input_image" or "output_image" or "image_generation_call")
                    bytes += ImageTokenCost * 4L;
                else if (type is "input_audio" or "audio")
                    bytes += AudioTokenCost * 4L;

                if (type is "reasoning" or "compaction" or "context_compaction"
                    && TryGetString(element, "encrypted_content") is { Length: > 0 } protectedPayload)
                {
                    bytes -= Encoding.UTF8.GetByteCount(protectedPayload);
                    bytes += EstimateDecodedPayloadBytes(protectedPayload);
                }

                foreach (var property in element.EnumerateObject())
                    Visit(property.Value, ref bytes);
                break;
            }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Visit(item, ref bytes);
                break;
        }
    }

    private static long EstimateDecodedPayloadBytes(string value)
    {
        try
        {
            var padding = value.EndsWith("==", StringComparison.Ordinal)
                ? 2
                : value.EndsWith("=", StringComparison.Ordinal)
                    ? 1
                    : 0;
            return Math.Max(0, value.Length * 3L / 4L - padding);
        }
        catch
        {
            return Encoding.UTF8.GetByteCount(value);
        }
    }

    private static long EstimateOptionBytes(object? value)
    {
        if (value is null)
            return 0;
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(value, value.GetType()).Length;
        }
        catch
        {
            return Encoding.UTF8.GetByteCount(value.ToString() ?? string.Empty);
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
