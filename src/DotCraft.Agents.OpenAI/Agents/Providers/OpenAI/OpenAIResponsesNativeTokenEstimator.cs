using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal static class OpenAIResponsesNativeTokenEstimator
{
    private const double BytesPerToken = 4.0;
    private const int ImageTokenCost = 2000;
    private const int AudioTokenCost = 1000;

    private static readonly JsonSerializerOptions ContentJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static long Estimate(
        IReadOnlyList<JsonElement> items,
        IReadOnlyList<ChatMessage> pendingTail,
        ChatOptions? options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(pendingTail);

        long visibleBytes = 0;
        foreach (var item in items)
            visibleBytes = SaturatingAdd(visibleBytes, EstimateElementBytes(item));

        if (!string.IsNullOrWhiteSpace(options?.Instructions))
            visibleBytes = SaturatingAdd(visibleBytes, Encoding.UTF8.GetByteCount(options.Instructions));
        if (!string.IsNullOrWhiteSpace(options?.ModelId))
            visibleBytes = SaturatingAdd(visibleBytes, Encoding.UTF8.GetByteCount(options.ModelId));

        if (options?.Tools is { Count: > 0 } tools)
        {
            foreach (var tool in tools)
            {
                visibleBytes = SaturatingAdd(
                    visibleBytes,
                    Encoding.UTF8.GetByteCount(tool.Name ?? string.Empty));
                visibleBytes = SaturatingAdd(
                    visibleBytes,
                    Encoding.UTF8.GetByteCount(tool.Description ?? string.Empty));
                if (tool is AIFunctionDeclaration function)
                {
                    visibleBytes = SaturatingAdd(
                        visibleBytes,
                        Encoding.UTF8.GetByteCount(function.JsonSchema.GetRawText()));
                }
            }
        }
        if (options != null)
        {
            visibleBytes = SaturatingAdd(visibleBytes, EstimateOptionBytes(options.Reasoning));
            visibleBytes = SaturatingAdd(visibleBytes, EstimateOptionBytes(options.ResponseFormat));
            visibleBytes = SaturatingAdd(visibleBytes, EstimateOptionBytes(options.ToolMode));
            if (options.AllowMultipleToolCalls.HasValue)
                visibleBytes = SaturatingAdd(visibleBytes, 5);
            if (options.AdditionalProperties is { Count: > 0 } properties)
            {
                foreach (var property in properties)
                {
                    visibleBytes = SaturatingAdd(
                        visibleBytes,
                        Encoding.UTF8.GetByteCount(property.Key));
                    visibleBytes = SaturatingAdd(visibleBytes, EstimateOptionBytes(property.Value));
                }
            }
        }

        var nativeTokens = (long)Math.Ceiling(visibleBytes / BytesPerToken);
        return SaturatingAdd(nativeTokens, MessageTokenEstimator.EstimateDelta(pendingTail));
    }

    private static long EstimateElementBytes(JsonElement item) =>
        TryGetString(item, "type") switch
        {
            "message" => EstimateContentBytes(GetProperty(item, "content")),
            "agent_message" => TextBytes(item, "author") + TextBytes(item, "recipient")
                + EstimateContentBytes(GetProperty(item, "content")),
            "reasoning" => Math.Max(0, EncodedLength(item) * 3 / 4 - 650),
            // Replay caches inflate the encrypted compaction payload beyond its text content.
            "compaction" or "context_compaction" => (EncodedLength(item) + 2) / 3,
            "function_call" => EstimateCallBytes(item, "arguments"),
            "custom_tool_call" => EstimateCallBytes(item, "input"),
            "function_call_output" => EstimateOutputBytes(GetProperty(item, "output"))
                + TextBytes(item, "call_id") + TextBytes(item, "name") + TextBytes(item, "namespace"),
            "custom_tool_call_output" => EstimateOutputBytes(GetProperty(item, "output"))
                + TextBytes(item, "call_id") + TextBytes(item, "name"),
            "additional_tools" or "tool_search_output" => EstimateJsonBytes(GetProperty(item, "tools")),
            "tool_search_call" => EstimateJsonBytes(GetProperty(item, "arguments")),
            "local_shell_call" or "web_search_call" => EstimateJsonBytes(GetProperty(item, "action")),
            "image_generation_call" => TextBytes(item, "revised_prompt")
                + (TryGetString(item, "result") is { Length: > 0 } ? ImageTokenCost * 4L : 0),
            _ => 0
        };

    private static long EstimateCallBytes(JsonElement item, string inputProperty) =>
        TextBytes(item, "name")
        + Encoding.UTF8.GetByteCount(TryGetString(item, "namespace") ?? "functions")
        + TextBytes(item, inputProperty);

    private static long EstimateOutputBytes(JsonElement output) =>
        output.ValueKind == JsonValueKind.String
            ? Encoding.UTF8.GetByteCount(output.GetString() ?? string.Empty)
            : EstimateContentBytes(output);

    private static long EstimateContentBytes(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array)
            return 0;

        long bytes = 0;
        foreach (var part in content.EnumerateArray())
        {
            var partBytes = TryGetString(part, "type") switch
            {
                "input_text" or "output_text" => TextBytes(part, "text"),
                "input_image" or "output_image" => ImageTokenCost * 4L,
                "input_audio" or "audio" => AudioTokenCost * 4L,
                "encrypted_content" => (EncodedLength(part) * 9 + 15) / 16,
                _ => 0
            };
            bytes = SaturatingAdd(bytes, partBytes);
        }
        return bytes;
    }

    private static long EstimateJsonBytes(JsonElement content) =>
        content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? 0
            : JsonSerializer.SerializeToUtf8Bytes(content, ContentJsonOptions).LongLength;

    private static long EncodedLength(JsonElement item) =>
        TryGetString(item, "encrypted_content")?.Length ?? 0;

    private static long TextBytes(JsonElement item, string propertyName) =>
        Encoding.UTF8.GetByteCount(TryGetString(item, propertyName) ?? string.Empty);

    private static JsonElement GetProperty(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value
            : default;

    private static long SaturatingAdd(long left, long right)
    {
        if (right <= 0)
            return left;
        return left > long.MaxValue - right ? long.MaxValue : left + right;
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
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
