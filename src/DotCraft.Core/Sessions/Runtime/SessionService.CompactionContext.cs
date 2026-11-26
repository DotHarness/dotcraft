using DotCraft.Agents;
using DotCraft.Channels;
using DotCraft.Configuration;
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
        ClientSurface? clientSurface = null,
        EffectiveModelRuntime? modelRuntime = null)
    {
        ReleaseStableContextPages(thread.Id);
        var modeName = _runtimeRegistry.TryGetRuntime(thread.Id, out var runtime)
            && runtime.TryGetTurn(turnId, out var turnRuntime)
            && turnRuntime.Context is { } turnContext
                ? turnContext.Configuration.Mode
                : thread.Configuration?.Mode;
        var mode = modeName?.Equals("plan", StringComparison.OrdinalIgnoreCase) == true
            ? AgentMode.Plan
            : AgentMode.Agent;
        var goal = GoalsEnabled ? await persistence.GetThreadGoalAsync(thread.Id, ct) : null;
        var carrier = ResolveThreadContextCarrier(thread, modelRuntime);
        var update = BuildWorldStateUpdate(
            thread, turnId, workspace.Cwd, workspace, previous, carrier,
            GetOrCreateModeManager(thread.Id, mode),
            agentFactory.PlanStore?.HasActivePlan(thread.Id) == true,
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
        NativeSubAgentGuidance.Reconcile(thread, items, carrier, _subAgentGuidanceProviders);
        var clientContextKind = WorldStateComposer.ItemKind(ClientContextSection.SectionId);
        if (thread.Source.SubAgent != null
            && previous.LastOrDefault(message => ThreadContextItems.IsKind(message, clientContextKind)) is { } inherited)
            items.Add(inherited.Clone());
        if (BuildCompactionTaskListReminder(thread, turnId, carrier) is { } taskList)
            items.Add(taskList);
        return update with { Items = items };
    }

    private ChatMessage? BuildCompactionTaskListReminder(SessionThread thread, string turnId, ThreadContextCarrier carrier)
    {
        if (thread.Turns.LastOrDefault(turn => turn.Id == turnId) is not { Status: TurnStatus.Running } turn)
            return null;

        var lastWrite = turn.Items
            .Where(static item => item.Payload is ToolResultPayload { Namespace: null, ToolName: "TodoWrite", Success: true })
            .Max(static item => item.CompletedAt);
        if (lastWrite is null
            || turn.Items.Any(item => item.CompletedAt > lastWrite && item.Payload is DynamicToolCallPayload
            {
                Namespace: "desktop_conversation",
                ToolName: "WrapUp",
                Success: true
            }))
            return null;

        var plan = agentFactory.PlanStore?.LoadStructuredPlan(thread.Id);
        return TaskListReminders.IsOpen(plan)
            ? ThreadContextItems.Create(carrier, TaskListReminders.CompactionKind, TaskListReminders.AfterCompaction(plan!))
            : null;
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
