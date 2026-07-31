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
                    if (type is "input_image" or "output_image")
                        ApplyDataUrlMediaAdjustment(element, "image_url", "image/", ImageTokenCost, ref bytes);
                    else if (type is "image_generation_call")
                        ApplyEmbeddedPayloadAdjustment(element, "result", ImageTokenCost, ref bytes);
                    else if (type is "input_audio" or "audio")
                        ApplyAudioAdjustment(element, ref bytes);

                    if (type is "reasoning" or "compaction" or "context_compaction"
                        && TryGetString(element, "encrypted_content") is { Length: > 0 } protectedPayload)
                    {
                        bytes = SaturatingSubtract(bytes, Encoding.UTF8.GetByteCount(protectedPayload));
                        bytes = SaturatingAdd(bytes, EstimateDecodedPayloadBytes(protectedPayload));
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

    private static void ApplyAudioAdjustment(JsonElement element, ref long bytes)
    {
        if (TryGetString(element, "audio_url") is { Length: > 0 })
        {
            ApplyDataUrlMediaAdjustment(element, "audio_url", "audio/", AudioTokenCost, ref bytes);
            return;
        }

        if (TryGetString(element, "data") is { Length: > 0 })
        {
            ApplyAudioDataAdjustment(element, "data", ref bytes);
            return;
        }

        if (element.TryGetProperty("input_audio", out var inputAudio)
            && inputAudio.ValueKind == JsonValueKind.Object)
        {
            ApplyAudioDataAdjustment(inputAudio, "data", ref bytes);
        }
    }

    private static void ApplyAudioDataAdjustment(
        JsonElement element,
        string propertyName,
        ref long bytes)
    {
        if (TryGetString(element, propertyName) is not { Length: > 0 } value)
            return;

        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetBase64DataUrlPayload(value, "audio/", out var payload))
                bytes = SaturatingSubtract(bytes, Encoding.UTF8.GetByteCount(payload));
        }
        else
        {
            bytes = SaturatingSubtract(bytes, Encoding.UTF8.GetByteCount(value));
        }

        bytes = SaturatingAdd(bytes, AudioTokenCost * 4L);
    }

    private static void ApplyDataUrlMediaAdjustment(
        JsonElement element,
        string propertyName,
        string expectedMimePrefix,
        int tokenCost,
        ref long bytes)
    {
        if (TryGetString(element, propertyName) is not { Length: > 0 } value)
            return;

        if (TryGetBase64DataUrlPayload(value, expectedMimePrefix, out var payload))
            bytes = SaturatingSubtract(bytes, Encoding.UTF8.GetByteCount(payload));

        bytes = SaturatingAdd(bytes, tokenCost * 4L);
    }

    private static void ApplyEmbeddedPayloadAdjustment(
        JsonElement element,
        string propertyName,
        int tokenCost,
        ref long bytes)
    {
        if (TryGetString(element, propertyName) is not { Length: > 0 } payload)
            return;

        bytes = SaturatingSubtract(bytes, Encoding.UTF8.GetByteCount(payload));
        bytes = SaturatingAdd(bytes, tokenCost * 4L);
    }

    private static bool TryGetBase64DataUrlPayload(
        string value,
        string expectedMimePrefix,
        out string payload)
    {
        payload = string.Empty;
        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        var separator = value.IndexOf(',');
        if (separator < 0)
            return false;

        var metadata = value.AsSpan(5, separator - 5);
        var semicolon = metadata.IndexOf(';');
        var mimeType = semicolon >= 0 ? metadata[..semicolon] : metadata;
        if (!mimeType.StartsWith(expectedMimePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var parameters = semicolon >= 0 ? metadata[semicolon..] : ReadOnlySpan<char>.Empty;
        var hasBase64Marker = false;
        foreach (var parameterRange in parameters.Split(';'))
        {
            var parameter = parameters[parameterRange].Trim();
            if (parameter.Equals("base64", StringComparison.OrdinalIgnoreCase))
            {
                hasBase64Marker = true;
                break;
            }
        }
        if (!hasBase64Marker)
            return false;

        payload = value[(separator + 1)..];
        return true;
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right <= 0)
            return left;
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    private static long SaturatingSubtract(long left, long right) =>
        right <= 0 ? left : Math.Max(0, left - right);

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
