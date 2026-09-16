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

    /// <summary>Fingerprint of request-shape fields outside the message list.</summary>
    public string? RequestFingerprint { get; init; }

    /// <summary>
    /// Fingerprint of context-usage-relevant request-shape fields, excluding
    /// base instructions so prompt text drift can be accounted for as a token delta.
    /// </summary>
    public string? ContextUsageFingerprint { get; init; }

    /// <summary>Rough token estimate for <see cref="BaseInstructions"/>.</summary>
    public int? BaseInstructionsTokenEstimate { get; init; }

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

        var toolFingerprint = PromptRequestFingerprints.ComputeToolFingerprint(tools);
        var baseInstructionsFingerprint = PromptRequestFingerprints.ComputeTextFingerprint(baseInstructions);
        var requestFingerprint = PromptRequestFingerprints.ComputeRequestFingerprint(
            providerId,
            options?.ModelId,
            mode,
            baseInstructionsFingerprint,
            toolFingerprint,
            options?.Reasoning,
            options?.ResponseFormat,
            options?.MaxOutputTokens,
            options?.AllowMultipleToolCalls,
            options?.ToolMode);
        var contextUsageFingerprint = PromptRequestFingerprints.ComputeContextUsageFingerprint(
            providerId,
            options?.ModelId,
            mode,
            toolFingerprint,
            options?.Reasoning,
            options?.ResponseFormat,
            options?.MaxOutputTokens,
            options?.AllowMultipleToolCalls,
            options?.ToolMode);

        return new PromptRequestSnapshot
        {
            ProviderId = providerId,
            ModelId = options?.ModelId,
            BaseInstructions = baseInstructions,
            BaseInstructionsFingerprint = baseInstructionsFingerprint,
            Messages = capturedMessages,
            MessageFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(capturedMessages, capturedMessages.Length),
            Tools = tools,
            ToolFingerprint = toolFingerprint,
            RequestFingerprint = requestFingerprint,
            ContextUsageFingerprint = contextUsageFingerprint,
            BaseInstructionsTokenEstimate = MessageTokenEstimator.RoughTokenCount(baseInstructions),
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
