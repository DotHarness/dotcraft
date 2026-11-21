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

        var parentRequest = TryGetLastPromptRequestSnapshot(parentThread.Id);
        var forkHistory = BuildNativeSubAgentForkHistory(
            parentRequest is { Messages.Count: > 0 } ? parentRequest.Messages : parentModelHistory);
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
        var model = ResolveForkModel(childThread);
        var inheritedTokens = await InheritForkContextUsageAsync(parentThread, childThread, model, forkHistory, ct);
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

        if (await InstallForkProviderHistoryAsync(
                childThread, model, parentRequest?.ProviderHistory, forkHistory, childContext, ct) is null
            && !childThread.Ephemeral && model.IsOpenAIResponses)
        {
            var window = GetOrCreateResponsesContextWindow(childThread.Id).CurrentWindowId;
            var identity = ThreadConversationIdentity.Create(childThread, childThread.Turns[^1], window, ProviderRequestKind.Turn);
            using var scope = ProviderRequestContextScope.Push(new ProviderRequestContext(identity));
            await TryReplaceResponsesProviderHistoryAsync(childThread, forkHistory, ProviderHistoryReasons.Fork, ct);
        }

        return true;
    }

    internal static List<ChatMessage> BuildNativeSubAgentForkHistory(
        IReadOnlyList<ChatMessage> parentModelHistory) =>
        ModelRequestHistorySanitizer.Sanitize(parentModelHistory)
            .Select(static message => message.Clone())
            .ToList();
}
