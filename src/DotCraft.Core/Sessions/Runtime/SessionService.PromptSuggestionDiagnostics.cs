using DotCraft.Agents;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private void BindPromptSuggestionTrace(SessionThread thread)
    {
        if (!PromptSuggestionThread.IsSuggestion(thread) || thread.ForkedFromId is not { } parentId)
            return;
        var rootId = traceCollector?.ResolveRootThreadId(parentId) ?? parentId;
        traceCollector?.BindChildSession(thread.Id, rootId, parentId,
            expectsSharedInputPrefix: true, comparePromptPrefix: true);
    }

    private void RecordPromptSuggestionOutcome(SessionThread thread, SessionTurn turn, bool started = false)
    {
        if (!PromptSuggestionThread.IsSuggestion(thread) || traceCollector == null)
            return;
        var text = turn.Items.LastOrDefault(item => item.Type == ItemType.AgentMessage)?.AsAgentMessage?.Text;
        var outcome = started ? "started" : turn.Status switch
        {
            TurnStatus.Completed => string.IsNullOrWhiteSpace(text) ? "empty" : "text",
            TurnStatus.Cancelled => "cancelled",
            _ => "failed"
        };
        ((IModelRuntimeDiagnostics)traceCollector).Record(new ModelRuntimeDiagnostic(
            "prompt_suggestion.outcome", new Dictionary<string, object?>
            {
                ["sessionKey"] = thread.Id,
                ["parentThreadId"] = thread.ForkedFromId,
                ["parentTurnId"] = thread.Metadata.GetValueOrDefault(PromptSuggestionThread.ParentTurnKey),
                ["threadId"] = thread.Id,
                ["turnId"] = turn.Id,
                ["outcome"] = outcome,
                ["textLength"] = text?.Trim().Length ?? 0
            }));
    }
}
