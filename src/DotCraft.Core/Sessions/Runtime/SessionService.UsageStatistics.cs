using DotCraft.Configuration;
using DotCraft.Tracing;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private void ObserveUsageThread(SessionThread thread) => tokenUsageStore?.ObserveThread(thread);

    private void RecordTurnTokenUsage(SessionThread thread, SessionTurn turn, TurnCostTracker cost)
    {
        if (tokenUsageStore == null || turn.TokenUsage == null || string.IsNullOrWhiteSpace(turn.OriginChannel))
            return;

        var initiator = turn.Initiator;
        var hasSubjectUser = !string.IsNullOrWhiteSpace(initiator?.UserId);
        var subjectKind = hasSubjectUser ? TokenUsageSubjectKinds.User : TokenUsageSubjectKinds.Thread;
        var subjectId = hasSubjectUser
            ? initiator!.UserId!
            : thread.Id;
        var subjectLabel = hasSubjectUser
            ? initiator!.UserName ?? initiator.UserId!
            : thread.DisplayName ?? thread.Id;
        var hasGroupContext = !string.IsNullOrWhiteSpace(initiator?.GroupId);
        var lastResponse = traceCollector?.GetLastResponseModel(thread.Id);
        var rootThreadId = thread.Source?.SubAgent?.RootThreadId;
        var config = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;

        tokenUsageStore.Record(new TokenUsageRecord
        {
            Timestamp = turn.CompletedAt ?? DateTimeOffset.UtcNow,
            SourceId = turn.OriginChannel!,
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectLabel = subjectLabel,
            ContextKind = hasGroupContext ? TokenUsageContextKinds.Group : null,
            ContextId = hasGroupContext ? initiator!.GroupId : null,
            ContextLabel = hasGroupContext ? initiator!.GroupId : null,
            ThreadId = thread.Id,
            TurnId = turn.Id,
            RootThreadId = string.IsNullOrWhiteSpace(rootThreadId) ? thread.Id : rootThreadId,
            Model = lastResponse?.ModelId,
            ReasoningEffort = lastResponse?.ReasoningEffort,
            Speed = (thread.Configuration?.Speed ?? InferenceSpeed.Standard).ToString().ToLowerInvariant(),
            InputTokens = turn.TokenUsage.InputTokens,
            OutputTokens = turn.TokenUsage.OutputTokens,
            CachedInputTokens = turn.TokenUsage.CachedInputTokens,
            CacheWriteInputTokens = turn.TokenUsage.CacheWriteInputTokens,
            ReasoningOutputTokens = turn.TokenUsage.ReasoningOutputTokens,
            LlmCallCount = turn.TokenUsage.LlmCallCount,
            CostUsd = cost.Resolve(ModelCatalog.ResolvePricing(config, lastResponse?.ModelId))
        });
    }

    private void RecordTurnDurationTrace(string threadId, SessionTurn turn)
    {
        if (traceCollector == null || turn.CompletedAt == null)
            return;

        var durationMs = (turn.CompletedAt.Value - turn.StartedAt).TotalMilliseconds;
        if (durationMs > 0)
            traceCollector.RecordTurnCompleted(threadId, durationMs);
    }

}
