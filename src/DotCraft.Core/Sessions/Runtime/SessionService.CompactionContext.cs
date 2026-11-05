using DotCraft.Agents;
using DotCraft.Channels;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Context.WorldState;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task<WorldStateTurnUpdate> BuildCompactionContextAsync(
        SessionThread thread,
        string turnId,
        IReadOnlyList<ChatMessage> previous,
        ThreadWorkspaceContext workspace,
        CancellationToken ct,
        string? sessionStartHookContext = null,
        ClientSurface? clientSurface = null)
    {
        ReleaseStableContextPages(thread.Id);
        var mode = thread.Configuration?.Mode?.Equals("plan", StringComparison.OrdinalIgnoreCase) == true
            ? AgentMode.Plan
            : AgentMode.Agent;
        var goal = GoalsEnabled ? await persistence.GetThreadGoalAsync(thread.Id, ct) : null;
        var update = BuildWorldStateUpdate(
            thread, turnId, workspace.Cwd, workspace, previous, ResolveThreadContextCarrier(thread),
            GetOrCreateModeManager(thread.Id, mode),
            agentFactory.PlanStore?.StructuredPlanExists(thread.Id) == true,
            goal, sessionStartHookContext, clientSurface, forceFull: true);
        var items = update.Items.ToList();
        var hookContextKind = WorldStateComposer.ItemKind(SessionStartHookSection.SectionId);
        if (string.IsNullOrWhiteSpace(sessionStartHookContext)
            && previous.LastOrDefault(message => ThreadContextItems.IsKind(message, hookContextKind)) is { } deliveredHook)
        {
            items.Add(deliveredHook.Clone());
            var state = update.Render.Snapshot.ToJsonObject();
            var text = deliveredHook.Role == ChatRole.User
                ? ThreadContextItems.ReadText(deliveredHook)
                : deliveredHook.Text.Trim();
            state[SessionStartHookSection.SectionId] = SessionStartHookSection.CreateSnapshot(text);
            update = update with { Render = update.Render with { Snapshot = WorldStateSnapshot.FromJsonObject(state) } };
        }
        NativeSubAgentGuidance.Reconcile(thread, items, ResolveThreadContextCarrier(thread), _subAgentGuidanceProviders);
        var clientContextKind = WorldStateComposer.ItemKind(ClientContextSection.SectionId);
        if (thread.Source.SubAgent != null
            && previous.LastOrDefault(message => ThreadContextItems.IsKind(message, clientContextKind)) is { } inherited)
            items.Add(inherited.Clone());
        return update with { Items = items };
    }

    private static List<ChatMessage> RestoreCompactedHistory(
        IReadOnlyList<ChatMessage> compacted,
        WorldStateTurnUpdate update)
    {
        var history = compacted.Where(message => !CompactionHistoryPreparation.IsContext(message))
            .Select(message => message.Clone()).ToList();
        var index = history.FindLastIndex(message => message.Role == ChatRole.User
            && ThreadContextItems.GetKind(message) == null);
        history.InsertRange(index < 0 ? history.Count : index, update.Items);
        return history;
    }

    private static CompactionStatus EstimateRestoredCompaction(
        CompactionStatus status,
        CompactionCoordinator coordinator,
        CompactionReplacement replacement,
        IReadOnlyList<ChatMessage> history,
        ChatOptions? options)
    {
        long tokens;
        if (replacement is CompactionReplacement.Neutral neutral)
            tokens = MessageTokenEstimator.Estimate(history)
                + Math.Max(0, status.EstimatedTokensAfter - MessageTokenEstimator.Estimate(neutral.Messages));
        else if (ProviderRequestContextScope.Current?.History is not { } providerHistory
            || !providerHistory.TryEstimateActiveContextTokens(history, options, out tokens))
            return status;
        return status with
        {
            EstimatedTokensAfter = (int)Math.Min(int.MaxValue, tokens),
            ThresholdAfter = coordinator.EvaluateThreshold(tokens)
        };
    }

    private async Task CommitCompactionWorldStateAsync(
        SessionThread thread,
        string coveredTurnId,
        WorldStateTurnUpdate update,
        bool persistBaseline = true)
    {
        if (_runtimeRegistry.TryGetRuntime(thread.Id, out var runtime))
        {
            runtime.PersistedWorldStateBaseline = null;
            runtime.WorldStateBaselineRestored = true;
        }
        CommitWorldStateUpdate(thread.Id, coveredTurnId, update, runtime?.ModeManager);
        if (persistBaseline)
            await PersistWorldStateAsync(thread, coveredTurnId);
    }

    private async Task AppendNativeCompactionContextAsync(
        SessionThread thread,
        string coveredTurnId,
        List<ChatMessage> session,
        WorldStateTurnUpdate update,
        CancellationToken ct)
    {
        if (!thread.Ephemeral)
            await persistence.AppendModelHistoryAsync(thread.Id, update.Items, coveredTurnId, ct);
        session.AddRange(update.Items);
    }
}
