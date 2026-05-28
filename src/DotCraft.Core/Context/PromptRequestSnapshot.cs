using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Context;

/// <summary>
/// Captures the provider-visible shape of a model request at the sampling boundary.
/// Maintenance forks reuse this shape so cache-sensitive request prefixes stay stable.
/// </summary>
public sealed record PromptRequestSnapshot
{
    /// <summary>Provider identifier when the caller can resolve it.</summary>
    public string? ProviderId { get; init; }

    /// <summary>Model identifier from the request options.</summary>
    public string? ModelId { get; init; }

    /// <summary>Stable base instructions visible to the model.</summary>
    public required string BaseInstructions { get; init; }

    /// <summary>Fingerprint of <see cref="BaseInstructions"/>.</summary>
    public required string BaseInstructionsFingerprint { get; init; }

    /// <summary>Final messages sent to the provider for this sampling request.</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>Order-sensitive fingerprint of <see cref="Messages"/>.</summary>
    public required string MessageFingerprint { get; init; }

    /// <summary>Final model-visible tools sent to the provider for this sampling request.</summary>
    public required IReadOnlyList<AITool> Tools { get; init; }

    /// <summary>Order-sensitive fingerprint of <see cref="Tools"/>.</summary>
    public required string ToolFingerprint { get; init; }

    /// <summary>Reasoning options requested for this sampling request.</summary>
    public ReasoningOptions? Reasoning { get; init; }

    /// <summary>Structured output format requested for this sampling request.</summary>
    public ChatResponseFormat? ResponseFormat { get; init; }

    /// <summary>Maximum output tokens requested for this sampling request.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Whether the provider may return multiple tool calls in one response.</summary>
    public bool? AllowMultipleToolCalls { get; init; }

    /// <summary>Tool selection mode requested for this sampling request.</summary>
    public ChatToolMode? ToolMode { get; init; }

    /// <summary>DotCraft mode associated with the turn, when available.</summary>
    public string? Mode { get; init; }

    /// <summary>Thread id associated with the request, when available.</summary>
    public string? ThreadId { get; init; }

    /// <summary>Turn id associated with the request, when available.</summary>
    public string? TurnId { get; init; }

    /// <summary>Estimated input tokens at capture time, when available.</summary>
    public int? EstimatedInputTokens { get; init; }

    /// <summary>How this snapshot was produced, when known.</summary>
    public string? SnapshotSource { get; init; }

    /// <summary>Reason a previous validation failed before this snapshot was derived, when applicable.</summary>
    public string? SnapshotInvalidReason { get; init; }

    /// <summary>
    /// Creates a snapshot from the final messages and options observed at the sampling boundary.
    /// </summary>
    public static PromptRequestSnapshot Capture(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        string? providerId = null,
        string? mode = null,
        string? threadId = null,
        string? turnId = null,
        int? estimatedInputTokens = null)
    {
        var baseInstructions = options?.Instructions ?? string.Empty;
        var tools = options?.Tools is { Count: > 0 }
            ? options.Tools.ToArray()
            : [];
        var capturedMessages = MessageGrouper
            .NormalizeFunctionCallArguments(messages)
            .Select(message => message.Clone())
            .ToArray();

        return new PromptRequestSnapshot
        {
            ProviderId = providerId,
            ModelId = options?.ModelId,
            BaseInstructions = baseInstructions,
            BaseInstructionsFingerprint = PromptRequestFingerprints.ComputeTextFingerprint(baseInstructions),
            Messages = capturedMessages,
            MessageFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(capturedMessages, capturedMessages.Length),
            Tools = tools,
            ToolFingerprint = PromptRequestFingerprints.ComputeToolFingerprint(tools),
            Reasoning = options?.Reasoning,
            ResponseFormat = options?.ResponseFormat,
            MaxOutputTokens = options?.MaxOutputTokens,
            AllowMultipleToolCalls = options?.AllowMultipleToolCalls,
            ToolMode = options?.ToolMode,
            Mode = mode,
            ThreadId = threadId,
            TurnId = turnId,
            EstimatedInputTokens = estimatedInputTokens,
            SnapshotSource = PromptRequestSnapshotSources.Captured
        };
    }
}

internal static class PromptRequestSnapshotSources
{
    public const string Captured = "captured";
    public const string ManualValid = "manual_valid";
    public const string ManualRebased = "manual_rebased";
}

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
}
