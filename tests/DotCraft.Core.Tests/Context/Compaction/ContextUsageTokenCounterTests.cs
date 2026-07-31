using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context.Compaction;

public sealed class ContextUsageTokenCounterTests
{
    [Fact]
    public void EstimateFromAnchor_AddsOnlyMessagesAfterUsageBoundary()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant"),
            new(ChatRole.User, new string('u', 400))
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 190_000,
            MessageCount: 2);

        var tokens = ContextUsageTokenCounter.EstimateFromAnchor(anchor, messages);

        Assert.NotNull(tokens);
        Assert.True(tokens > 190_000);
        Assert.Equal(
            190_000 + MessageTokenEstimator.EstimateDelta([messages[2]]),
            tokens);
    }

    [Fact]
    public void EstimateFromAnchor_ValidatesPrefixFingerprint()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant"),
            new(ChatRole.User, "delta")
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 190_000,
            MessageCount: 2,
            PrefixFingerprint: MessageTokenEstimator.ComputePrefixFingerprint(messages, 2));

        Assert.NotNull(ContextUsageTokenCounter.EstimateFromAnchor(anchor, messages));

        messages[0] = new ChatMessage(ChatRole.User, "changed first user");
        Assert.Null(ContextUsageTokenCounter.EstimateFromAnchor(anchor, messages));
    }

    [Fact]
    public void ContextTokenUsageEstimator_UsesPersistedAnchorBeforeRawPersistedTokens()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant"),
            new(ChatRole.User, new string('d', 400))
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 10_000,
            MessageCount: 2,
            PrefixFingerprint: MessageTokenEstimator.ComputePrefixFingerprint(messages, 2),
            RequestFingerprint: "request-a");

        var estimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: null,
            persistedAnchor: anchor,
            latestContextTokens: 0,
            persistedDisplayTokens: 190_000,
            requestFingerprint: "request-a");

        Assert.Equal("persisted_anchor", estimate.Source);
        Assert.Equal(10_000 + MessageTokenEstimator.EstimateDelta([messages[2]]), estimate.Tokens);
        Assert.True(estimate.EligibleForAutoCompact);
    }

    [Fact]
    public void ContextTokenUsageEstimator_UsesRoughEstimateWhenAnchorsAreMissingAndProviderIsStale()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "current " + new string('u', 2_000)),
            new(ChatRole.Assistant, "reply " + new string('a', 2_000))
        };
        var rough = MessageTokenEstimator.Estimate(messages);

        var estimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: null,
            persistedAnchor: null,
            latestContextTokens: 0,
            persistedDisplayTokens: 20,
            requestFingerprint: "request-a");

        Assert.Equal("estimate", estimate.Source);
        Assert.Equal(rough, estimate.Tokens);
    }

    [Fact]
    public void ContextTokenUsageEstimator_DoesNotAutoCompactFromUnverifiedProviderContext()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, new string('u', 240_000)),
            new(ChatRole.Assistant, new string('a', 240_000))
        };
        var rough = MessageTokenEstimator.Estimate(messages);
        Assert.True(rough > 103_000);

        var estimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: null,
            persistedAnchor: null,
            latestContextTokens: 103_000,
            persistedDisplayTokens: 0,
            requestFingerprint: "request-a");

        Assert.Equal("unverified_provider_context", estimate.Source);
        Assert.Equal(103_000, estimate.Tokens);
        Assert.False(estimate.EligibleForAutoCompact);
        Assert.False(estimate.IsEstimate);
    }

    [Fact]
    public void ContextTokenUsageEstimator_AutoCompactsFromReplacementHistoryEstimate()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, new string('u', 240_000)),
            new(ChatRole.Assistant, new string('a', 240_000))
        };
        var rough = MessageTokenEstimator.Estimate(messages);
        Assert.True(rough > 103_000);

        var estimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: null,
            persistedAnchor: null,
            latestContextTokens: 0,
            persistedDisplayTokens: 103_000,
            requestFingerprint: "request-a",
            persistedDisplaySource: "history_estimate",
            persistedDisplayIsEstimate: true);

        Assert.Equal("history_estimate", estimate.Source);
        Assert.Equal(rough, estimate.Tokens);
        Assert.True(estimate.EligibleForAutoCompact);
        Assert.True(estimate.IsEstimate);
    }

    [Fact]
    public void ContextTokenUsageEstimator_TrustsPersistedReplacementEstimateWhenItIsHigher()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "short")
        };
        var rough = MessageTokenEstimator.Estimate(messages);

        var estimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: null,
            persistedAnchor: null,
            latestContextTokens: 0,
            persistedDisplayTokens: rough + 1_000,
            requestFingerprint: "request-a",
            persistedDisplaySource: "history_estimate",
            persistedDisplayIsEstimate: true);

        Assert.Equal("history_estimate", estimate.Source);
        Assert.Equal(rough + 1_000, estimate.Tokens);
        Assert.True(estimate.EligibleForAutoCompact);
        Assert.True(estimate.IsEstimate);
    }

    [Fact]
    public void ContextTokenUsageEstimator_DoesNotApplyNativeEstimateToNeutralHistory()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "short")
        };
        var rough = MessageTokenEstimator.Estimate(messages);

        var estimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: null,
            persistedAnchor: null,
            latestContextTokens: 0,
            persistedDisplayTokens: rough + 10_000,
            persistedDisplaySource: "provider_compacted_estimate",
            persistedDisplayIsEstimate: true);

        Assert.Equal("estimate", estimate.Source);
        Assert.Equal(rough, estimate.Tokens);
        Assert.True(estimate.EligibleForAutoCompact);
    }

    [Fact]
    public void ContextTokenUsageEstimator_UsesValidAnchorBeforeHistoryEstimate()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, new string('u', 240_000)),
            new(ChatRole.Assistant, new string('a', 240_000))
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 103_000,
            MessageCount: 1,
            PrefixFingerprint: MessageTokenEstimator.ComputePrefixFingerprint(messages, 1),
            RequestFingerprint: "request-a");

        var estimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: anchor,
            persistedAnchor: null,
            latestContextTokens: 104_000,
            persistedDisplayTokens: 0,
            requestFingerprint: "request-a");

        Assert.Equal("memory_anchor", estimate.Source);
        Assert.Equal(
            Math.Max(104_000, 103_000 + MessageTokenEstimator.EstimateDelta(messages, 1, 1)),
            estimate.Tokens);
        Assert.True(estimate.EligibleForAutoCompact);
        Assert.True(estimate.IsEstimate);
    }

    [Fact]
    public void ContextTokenUsageEstimator_UsesUnverifiedProviderHintButNotRawPersistedDisplayForAutoCompact()
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, "short") };
        var rough = MessageTokenEstimator.Estimate(messages);

        var providerEstimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: null,
            persistedAnchor: null,
            latestContextTokens: rough + 1_000,
            persistedDisplayTokens: 0,
            requestFingerprint: "request-a");
        var persistedEstimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: null,
            persistedAnchor: null,
            latestContextTokens: 0,
            persistedDisplayTokens: rough + 2_000,
            requestFingerprint: "request-a");

        Assert.Equal("unverified_provider_context", providerEstimate.Source);
        Assert.Equal(rough + 1_000, providerEstimate.Tokens);
        Assert.False(providerEstimate.EligibleForAutoCompact);
        Assert.False(providerEstimate.IsEstimate);
        Assert.Equal("estimate", persistedEstimate.Source);
        Assert.Equal(rough, persistedEstimate.Tokens);
        Assert.True(persistedEstimate.EligibleForAutoCompact);
    }

    [Fact]
    public void EstimateFromAnchor_ValidatesRequestFingerprint()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant")
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 190_000,
            MessageCount: 2,
            PrefixFingerprint: MessageTokenEstimator.ComputePrefixFingerprint(messages, 2),
            RequestFingerprint: "request-a");

        Assert.NotNull(ContextUsageTokenCounter.EstimateFromAnchor(
            anchor,
            messages,
            requestFingerprint: "request-a",
            requireRequestFingerprint: true));
        Assert.Null(ContextUsageTokenCounter.EstimateFromAnchor(
            anchor,
            messages,
            requestFingerprint: "request-b",
            requireRequestFingerprint: true));
        Assert.Null(ContextUsageTokenCounter.EstimateFromAnchor(
            anchor with { RequestFingerprint = null },
            messages,
            requestFingerprint: "request-a",
            requireRequestFingerprint: true));
    }

    [Fact]
    public void EstimateFromAnchor_AllowsBaseInstructionsDrift_WhenContextUsageFingerprintMatches()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant"),
            new(ChatRole.User, new string('d', 400))
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 103_000,
            MessageCount: 2,
            PrefixFingerprint: MessageTokenEstimator.ComputePrefixFingerprint(messages, 2),
            RequestFingerprint: "request-before-memory",
            ContextUsageFingerprint: "context-shape",
            BaseInstructionsTokenEstimate: 1_000);

        var estimate = ContextUsageTokenCounter.EstimateFromAnchorDetailed(
            anchor,
            messages,
            requestFingerprint: "request-after-memory",
            requireRequestFingerprint: true,
            contextUsageFingerprint: "context-shape",
            baseInstructionsTokenEstimate: 1_125);

        Assert.NotNull(estimate);
        Assert.True(estimate!.UsedBaseInstructionsAdjustment);
        Assert.Equal(
            103_000 + 125 + MessageTokenEstimator.EstimateDelta([messages[2]]),
            estimate.Tokens);
    }

    [Fact]
    public void ContextTokenUsageEstimator_AdjustsLatestProviderContext_WhenBaseInstructionsShrink()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "reply")
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 103_000,
            MessageCount: 1,
            PrefixFingerprint: MessageTokenEstimator.ComputePrefixFingerprint(messages, 1),
            RequestFingerprint: "request-before-memory",
            ContextUsageFingerprint: "context-shape",
            BaseInstructionsTokenEstimate: 1_000);

        var estimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: anchor,
            persistedAnchor: null,
            latestContextTokens: 105_000,
            persistedDisplayTokens: 0,
            requestFingerprint: "request-after-memory",
            contextUsageFingerprint: "context-shape",
            baseInstructionsTokenEstimate: 500);

        var expectedAnchored = 103_000
            + MessageTokenEstimator.EstimateDelta(messages, 1, 1)
            - 500;
        Assert.Equal("prefix_adjusted_anchor", estimate.Source);
        Assert.Equal(Math.Max(expectedAnchored, 104_500), estimate.Tokens);
        Assert.True(estimate.Tokens < 105_000);
        Assert.True(estimate.EligibleForAutoCompact);
    }

    [Fact]
    public void EstimateFromAnchor_RejectsBaseInstructionsAdjustment_WhenContextUsageFingerprintDiffers()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant")
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 103_000,
            MessageCount: 2,
            PrefixFingerprint: MessageTokenEstimator.ComputePrefixFingerprint(messages, 2),
            RequestFingerprint: "request-before-tool-change",
            ContextUsageFingerprint: "context-old",
            BaseInstructionsTokenEstimate: 1_000);

        var estimate = ContextUsageTokenCounter.EstimateFromAnchorDetailed(
            anchor,
            messages,
            requestFingerprint: "request-after-tool-change",
            requireRequestFingerprint: true,
            contextUsageFingerprint: "context-new",
            baseInstructionsTokenEstimate: 1_125);

        Assert.Null(estimate);
    }

    [Fact]
    public void ContextTokenUsageEstimator_UsesPersistedProviderFallbackWithoutAutoCompact_WhenLegacyAnchorCannotBeVerified()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, new string('u', 240_000)),
            new(ChatRole.Assistant, new string('a', 240_000))
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 103_000,
            MessageCount: 2,
            PrefixFingerprint: MessageTokenEstimator.ComputePrefixFingerprint(messages, 2),
            RequestFingerprint: "request-before-memory");
        var rough = MessageTokenEstimator.Estimate(messages);
        Assert.True(rough > 103_000);

        var estimate = ContextTokenUsageEstimator.Estimate(
            messages,
            memoryAnchor: null,
            persistedAnchor: anchor,
            latestContextTokens: 0,
            persistedDisplayTokens: 103_000,
            requestFingerprint: "request-after-memory",
            contextUsageFingerprint: "context-shape",
            baseInstructionsTokenEstimate: 1_125,
            persistedDisplaySource: "provider_context",
            persistedDisplayIsEstimate: false);

        Assert.Equal("persisted_provider_context", estimate.Source);
        Assert.Equal(103_000, estimate.Tokens);
        Assert.False(estimate.EligibleForAutoCompact);
        Assert.False(estimate.IsEstimate);
    }

    [Fact]
    public void EstimateFromAnchor_ImageToolResultDeltaDoesNotScaleWithImageBytes()
    {
        var imageBytes = new byte[1_000_000];
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "first user"),
            new(ChatRole.Assistant, "first assistant"),
            new(
                ChatRole.Tool,
                (IList<AIContent>)
                [
                    new FunctionResultContent(
                        "call-1",
                        (IList<AIContent>)
                        [
                            new TextContent("Image: screenshot.png (1,000,000 bytes, image/png)"),
                            new DataContent(imageBytes, "image/png")
                        ])
                ])
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 50_000,
            MessageCount: 2,
            PrefixFingerprint: MessageTokenEstimator.ComputePrefixFingerprint(messages, 2));

        var tokens = ContextUsageTokenCounter.EstimateFromAnchor(anchor, messages);

        Assert.NotNull(tokens);
        var delta = tokens.Value - anchor.Tokens;
        Assert.InRange(delta, 2_000, 20_000);
        Assert.True(delta < imageBytes.Length / 16);
    }

    [Fact]
    public void EstimateFromAnchor_ReturnsNull_WhenBoundaryNoLongerMatchesHistory()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "short")
        };
        var anchor = new ContextUsageAnchor(
            Tokens: 190_000,
            MessageCount: 2);

        Assert.Null(ContextUsageTokenCounter.EstimateFromAnchor(anchor, messages));
    }
}
