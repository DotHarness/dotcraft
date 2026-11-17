using DotCraft.Agents;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    async Task<bool> INativeSubAgentForkMaterializationService.MaterializeNativeSubAgentForkAsync(
        SessionThread parentThread,
        SessionThread childThread,
        IReadOnlyList<ChatMessage> parentModelHistory,
        CancellationToken ct)
    {
        if (childThread.TurnCount == 0)
            return false;

        var forkHistory = BuildNativeSubAgentForkHistory(
            ResolveParentForkSource(parentThread.Id, parentModelHistory));
        if (forkHistory.Count == 0)
            return false;

        childThread.Metadata[ToolPlanningThreadClassifier.InheritedToolSurfaceKey] =
            ToolPlanningThreadClassifier.ClassifyToolSurface(parentThread).ToString();
        if (childThread.Source.SubAgent is { } source)
        {
            traceCollector?.BindChildSession(
                childThread.Id,
                string.IsNullOrWhiteSpace(source.RootThreadId) ? parentThread.Id : source.RootThreadId,
                parentThread.Id,
                expectsSharedInputPrefix: true,
                comparePromptPrefix: true,
                forkMode: source.ForkTurns);
        }

        var childContext = new List<ChatMessage>();
        NativeSubAgentGuidance.Reconcile(
            childThread,
            childContext,
            ResolveThreadContextCarrier(childThread),
            _subAgentGuidanceProviders);
        forkHistory.AddRange(childContext);

        AgentInstructionContextPages.TryForkStablePages(parentThread.Id, childThread.Id);

        await PersistThreadWithMaterializationAsync(childThread, ct);
        InheritWorldStateBaseline(parentThread.Id, childThread.Id, forkHistory);
        var inheritedTokens = await InheritSubAgentContextUsageAsync(parentThread.Id, childThread.Id, forkHistory, ct);
        if (!childThread.Ephemeral)
        {
            var coveredThroughTurnId = childThread.Turns[^1].Id;
            var tokens = inheritedTokens ?? MessageTokenEstimator.Estimate(forkHistory);
            await persistence.AppendCompactionCheckpointAsync(
                childThread.Id,
                coveredThroughTurnId,
                forkHistory,
                trigger: "subagent_fork",
                mode: "partial",
                tokensBefore: tokens,
                tokensAfter: tokens,
                ct);
        }

        var childWindow = GetOrCreateResponsesContextWindow(childThread.Id);
        var childIdentity = ThreadConversationIdentity.Create(
            childThread,
            childThread.Turns[^1],
            childWindow.CurrentWindowId,
            ProviderRequestKind.Turn);
        var childProviderState = new ProviderConversationState(childIdentity);
        using var childResponsesScope = ProviderRequestContextScope.Push(
            new ProviderRequestContext(childIdentity, ConversationState: childProviderState));
        await ForkSubAgentProviderHistoryAsync(parentThread.Id, childThread, forkHistory, ct);

        return true;
    }

    private async Task ForkSubAgentProviderHistoryAsync(
        string parentThreadId,
        SessionThread childThread,
        List<ChatMessage> forkHistory,
        CancellationToken ct)
    {
        var parentRequest = TryGetLastPromptRequestSnapshot(parentThreadId);
        if (parentRequest?.ProviderHistory is not { } sourceHistory)
        {
            await TryReplaceResponsesProviderHistoryAsync(childThread, forkHistory, ProviderHistoryReasons.Fork, ct);
            return;
        }

        var identity = ProviderRequestContextScope.Current!.CurrentIdentity;
        var inherited = sourceHistory with
        {
            Identity = sourceHistory.Identity with
            {
                ThreadId = childThread.Id,
                TurnId = identity.TurnId,
                GenerationId = identity.ContextWindowId,
                ContextWindowId = identity.ContextWindowId
            },
            CoveredThroughTurnId = identity.TurnId
        };
        if (childThread.Ephemeral)
        {
            _runtimeRegistry.SetThread(childThread).ResponsesProviderHistorySnapshot = inherited;
        }
        else
        {
            await new SessionProviderHistorySink(persistence).ReplaceAsync(
                inherited.Identity,
                inherited.CoveredThroughTurnId,
                inherited.IsNativeCompacted
                    ? ProviderHistoryReasons.ForkNativeCompaction
                    : ProviderHistoryReasons.Fork,
                inherited.Items,
                ct);
        }

        var history = (await CreateResponsesProviderHistoryContextAsync(
            childThread, turn: null, parentRequest.Messages.ToList(), ct))!;
        await history.AppendLocalInputAsync(forkHistory.Skip(parentRequest.Messages.Count).ToArray(), ct);
        if (childThread.Ephemeral)
            _runtimeRegistry.SetThread(childThread).ResponsesProviderHistorySnapshot = history.CaptureOpaqueSnapshot();
    }

    private async Task<long?> InheritSubAgentContextUsageAsync(
        string parentThreadId,
        string childThreadId,
        IReadOnlyList<ChatMessage> forkHistory,
        CancellationToken ct)
    {
        var snapshot = TryGetLastPromptRequestSnapshot(parentThreadId);
        var anchor = TryGetInMemoryContextUsageAnchor(parentThreadId)
            ?? persistence.LoadContextUsageAnchor(parentThreadId);
        if (snapshot is null || anchor is null)
            return null;

        var estimate = ContextUsageTokenCounter.EstimateFromAnchorDetailed(
            anchor,
            forkHistory,
            snapshot.RequestFingerprint,
            requireRequestFingerprint: true,
            snapshot.ContextUsageFingerprint,
            snapshot.BaseInstructionsTokenEstimate);
        if (estimate is null)
            return null;

        if (_runtimeRegistry.TryGetRuntime(childThreadId, out var runtime))
            runtime.LastPromptRequest = snapshot with { ThreadId = childThreadId, TurnId = null };
        await SaveContextUsageSnapshotAsync(
            childThreadId,
            estimate.Tokens,
            anchor,
            source: "estimate",
            isEstimate: true,
            ct);
        return estimate.Tokens;
    }

    private IReadOnlyList<ChatMessage> ResolveParentForkSource(
        string parentThreadId,
        IReadOnlyList<ChatMessage> fallback)
    {
        var snapshot = TryGetLastPromptRequestSnapshot(parentThreadId);
        return snapshot is { Messages.Count: > 0 } ? snapshot.Messages : fallback;
    }

    internal static List<ChatMessage> BuildNativeSubAgentForkHistory(
        IReadOnlyList<ChatMessage> parentModelHistory) =>
        ModelRequestHistorySanitizer.Sanitize(parentModelHistory)
            .Select(static message => message.Clone())
            .ToList();
}
