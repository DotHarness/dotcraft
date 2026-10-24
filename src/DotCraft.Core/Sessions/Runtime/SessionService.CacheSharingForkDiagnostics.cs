using System.Text.Json;
using DotCraft.Agents;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private void BindCacheSharingForkTrace(SessionThread thread)
    {
        if (CacheSharingFork.PurposeOf(thread) is not { } purpose || thread.ForkedFromId is not { } parentId)
            return;
        var rootId = traceCollector?.ResolveRootThreadId(parentId) ?? parentId;
        traceCollector?.BindChildSession(thread.Id, rootId, parentId,
            expectsSharedInputPrefix: true, comparePromptPrefix: true,
            forkMode: JsonNamingPolicy.CamelCase.ConvertName(purpose.ToString()));
    }

    private void RecordPromptSuggestionOutcome(SessionThread thread, SessionTurn turn, bool started = false)
    {
        if (CacheSharingFork.PurposeOf(thread) != ThreadForkPurpose.PromptSuggestion || traceCollector == null)
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
                ["parentTurnId"] = thread.Metadata.GetValueOrDefault(CacheSharingFork.ParentTurnKey),
                ["threadId"] = thread.Id,
                ["turnId"] = turn.Id,
                ["outcome"] = outcome,
                ["textLength"] = text?.Trim().Length ?? 0
            }));
    }
}
