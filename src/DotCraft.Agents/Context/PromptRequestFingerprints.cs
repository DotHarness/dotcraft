using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Context;
/// <summary>
/// Stable fingerprint helpers for prompt request prefix components.
/// </summary>
public static class PromptRequestFingerprints
{
    /// <summary>Computes a SHA-256 fingerprint for stable prompt text.</summary>
    public static string ComputeTextFingerprint(string? text) =>
        HashString(text ?? string.Empty);

    /// <summary>
    /// Computes an order-sensitive SHA-256 fingerprint for model-visible tool schemas.
    /// </summary>
    public static string ComputeToolFingerprint(IEnumerable<AITool>? tools)
    {
        var normalized = (tools ?? [])
            .Select(NormalizeTool)
            .ToArray();
        return HashString(JsonSerializer.Serialize(normalized, JsonOptions));
    }

    /// <summary>
    /// Computes a SHA-256 fingerprint for non-message request-shape fields.
    /// </summary>
    public static string ComputeRequestFingerprint(
        string? providerId,
        string? modelId,
        string? mode,
        string? baseInstructionsFingerprint,
        string? toolFingerprint,
        ReasoningOptions? reasoning,
        ChatResponseFormat? responseFormat,
        int? maxOutputTokens,
        bool? allowMultipleToolCalls,
        ChatToolMode? toolMode)
    {
        var normalized = new RequestFingerprintEntry(
            providerId ?? string.Empty,
            modelId ?? string.Empty,
            mode ?? string.Empty,
            baseInstructionsFingerprint ?? string.Empty,
            toolFingerprint ?? string.Empty,
            NormalizeOption(reasoning),
            NormalizeOption(responseFormat),
            maxOutputTokens,
            allowMultipleToolCalls,
            NormalizeOption(toolMode));
        return HashString(JsonSerializer.Serialize(normalized, JsonOptions));
    }

    /// <summary>
    /// Computes a SHA-256 fingerprint for request-shape fields that affect
    /// context usage independently from the base instruction text.
    /// </summary>
    public static string ComputeContextUsageFingerprint(
        string? providerId,
        string? modelId,
        string? mode,
        string? toolFingerprint,
        ReasoningOptions? reasoning,
        ChatResponseFormat? responseFormat,
        int? maxOutputTokens,
        bool? allowMultipleToolCalls,
        ChatToolMode? toolMode)
    {
        var normalized = new ContextUsageFingerprintEntry(
            providerId ?? string.Empty,
            modelId ?? string.Empty,
            mode ?? string.Empty,
            toolFingerprint ?? string.Empty,
            NormalizeOption(reasoning),
            NormalizeOption(responseFormat),
            maxOutputTokens,
            allowMultipleToolCalls,
            NormalizeOption(toolMode));
        return HashString(JsonSerializer.Serialize(normalized, JsonOptions));
    }

    private static ToolFingerprintEntry NormalizeTool(AITool tool)
    {
        string? jsonSchema = null;
        string? returnJsonSchema = null;

        if (tool is AIFunction function)
        {
            jsonSchema = Canonicalize(function.JsonSchema);
            returnJsonSchema = function.ReturnJsonSchema is { } schema
                ? Canonicalize(schema)
                : null;
        }
        else if (tool is AIFunctionDeclaration declaration)
        {
            jsonSchema = Canonicalize(declaration.JsonSchema);
            returnJsonSchema = declaration.ReturnJsonSchema is { } schema
                ? Canonicalize(schema)
                : null;
        }

        return new ToolFingerprintEntry(
            tool.GetType().FullName ?? tool.GetType().Name,
            tool.Name ?? string.Empty,
            tool.Description ?? string.Empty,
            jsonSchema,
            returnJsonSchema);
    }

    private static string Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalElement(writer, element);
        }

        return stream.TryGetBuffer(out var buffer) && buffer.Array is { } array
            ? Encoding.UTF8.GetString(array, buffer.Offset, buffer.Count)
            : Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonicalElement(writer, item);
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                element.WriteTo(writer);
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string HashString(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? NormalizeOption(object? value)
    {
        if (value is null)
            return null;

        try
        {
            return JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
        }
        catch
        {
            return value.ToString();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record ToolFingerprintEntry(
        string Type,
        string Name,
        string Description,
        string? JsonSchema,
        string? ReturnJsonSchema);

    private sealed record RequestFingerprintEntry(
        string ProviderId,
        string ModelId,
        string Mode,
        string BaseInstructionsFingerprint,
        string ToolFingerprint,
        string? Reasoning,
        string? ResponseFormat,
        int? MaxOutputTokens,
        bool? AllowMultipleToolCalls,
        string? ToolMode);

    private sealed record ContextUsageFingerprintEntry(
        string ProviderId,
        string ModelId,
        string Mode,
        string ToolFingerprint,
        string? Reasoning,
        string? ResponseFormat,
        int? MaxOutputTokens,
        bool? AllowMultipleToolCalls,
        string? ToolMode);
}
