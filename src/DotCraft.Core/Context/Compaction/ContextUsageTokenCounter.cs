using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Known model-visible boundaries for provider usage anchors.
/// </summary>
public static class ContextUsageAnchorBoundary
{
    public const string Request = "request";
}

/// <summary>
/// Captures the provider-reported context usage at a known request boundary.
/// </summary>
public sealed record ContextUsageAnchor(
    long Tokens,
    int MessageCount,
    string? PrefixFingerprint = null,
    string? RequestFingerprint = null,
    string? ContextUsageFingerprint = null,
    int? BaseInstructionsTokenEstimate = null,
    string? BoundaryKind = ContextUsageAnchorBoundary.Request);

/// <summary>
/// Result of applying a provider usage anchor to the current model-visible history.
/// </summary>
public sealed record ContextUsageAnchorEstimate(long Tokens, bool UsedBaseInstructionsAdjustment);

/// <summary>
/// Counts current context pressure from the last provider usage plus an
/// estimate for messages appended after that usage-bearing request.
/// </summary>
public static class ContextUsageTokenCounter
{
    /// <summary>
    /// Returns the anchored token count, or <c>null</c> when the anchor no
    /// longer lines up with the supplied history.
    /// </summary>
    public static long? EstimateFromAnchor(
        ContextUsageAnchor? anchor,
        IReadOnlyList<ChatMessage> modelVisibleHistory,
        string? requestFingerprint = null,
        bool requireRequestFingerprint = false)
        => EstimateFromAnchorDetailed(
            anchor,
            modelVisibleHistory,
            requestFingerprint,
            requireRequestFingerprint)?.Tokens;

    /// <summary>
    /// Returns the anchored token count plus diagnostic information, or
    /// <c>null</c> when the anchor no longer lines up with the supplied history.
    /// </summary>
    public static ContextUsageAnchorEstimate? EstimateFromAnchorDetailed(
        ContextUsageAnchor? anchor,
        IReadOnlyList<ChatMessage> modelVisibleHistory,
        string? requestFingerprint = null,
        bool requireRequestFingerprint = false,
        string? contextUsageFingerprint = null,
        int? baseInstructionsTokenEstimate = null)
    {
        if (anchor is null)
            return null;

        if (!string.IsNullOrWhiteSpace(anchor.BoundaryKind)
            && !string.Equals(anchor.BoundaryKind, ContextUsageAnchorBoundary.Request, StringComparison.Ordinal))
        {
            return null;
        }

        var usedBaseInstructionsAdjustment = false;
        if (requireRequestFingerprint || !string.IsNullOrWhiteSpace(requestFingerprint))
        {
            var requestMatches = !string.IsNullOrWhiteSpace(requestFingerprint)
                && !string.IsNullOrWhiteSpace(anchor.RequestFingerprint)
                && string.Equals(anchor.RequestFingerprint, requestFingerprint, StringComparison.Ordinal);
            if (!requestMatches)
            {
                var contextMatches = !string.IsNullOrWhiteSpace(contextUsageFingerprint)
                    && !string.IsNullOrWhiteSpace(anchor.ContextUsageFingerprint)
                    && string.Equals(anchor.ContextUsageFingerprint, contextUsageFingerprint, StringComparison.Ordinal);
                if (!contextMatches
                    || anchor.BaseInstructionsTokenEstimate is not { } anchorBaseInstructionsTokens
                    || baseInstructionsTokenEstimate is not { } currentBaseInstructionsTokens)
                {
                    return null;
                }

                usedBaseInstructionsAdjustment = true;
            }
        }

        if (anchor.MessageCount < 0 || anchor.MessageCount > modelVisibleHistory.Count)
            return null;

        if (!string.IsNullOrEmpty(anchor.PrefixFingerprint))
        {
            var currentFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(
                modelVisibleHistory,
                anchor.MessageCount);
            if (!string.Equals(currentFingerprint, anchor.PrefixFingerprint, StringComparison.Ordinal))
                return null;
        }

        var deltaTokens = MessageTokenEstimator.EstimateDelta(
            modelVisibleHistory,
            anchor.MessageCount,
            modelVisibleHistory.Count - anchor.MessageCount);
        var baseInstructionsDelta = usedBaseInstructionsAdjustment
            ? baseInstructionsTokenEstimate!.Value - anchor.BaseInstructionsTokenEstimate!.Value
            : 0;
        return new ContextUsageAnchorEstimate(
            Math.Max(0, anchor.Tokens + deltaTokens + baseInstructionsDelta),
            usedBaseInstructionsAdjustment);
    }
}
