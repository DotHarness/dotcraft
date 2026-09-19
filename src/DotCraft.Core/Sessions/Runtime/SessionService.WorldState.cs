using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.WorldState;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed record WorldStateTurnUpdate(
        IReadOnlyList<ChatMessage> Items,
        WorldStateRender Render,
        bool Full);

    private WorldStateTurnUpdate BuildWorldStateUpdate(
        SessionThread thread,
        IReadOnlyList<ChatMessage> history,
        ThreadContextCarrier carrier,
        AgentModeManager? modeManager,
        bool hasActivePlan,
        ThreadGoal? threadGoal,
        string? sessionStartHookContext)
    {
        var world = WorldStateComposer.Build(
            [
                .. agentFactory.RuntimeContext.WorldStateSections,
                .. agentFactory.RuntimeContext.Contributions?.Resolve<IWorldStateSection>(thread.Id) ?? []
            ],
            logger);

        var context = new WorldStateContext
        {
            Thread = thread,
            ModeManager = modeManager,
            WorkspacePath = thread.WorkspacePath,
            HasActivePlan = hasActivePlan,
            ThreadGoal = threadGoal,
            SessionStartHookContext = sessionStartHookContext
        };

        var baseline = _runtimeRegistry.TryGetRuntime(thread.Id, out var runtime)
            ? runtime.WorldStateBaseline
            : null;
        var render = baseline == null
            ? world.RenderFull(context)
            : world.Render(context, baseline, SectionIdsInHistory(history));

        var items = render.Fragments
            .Select(fragment => ThreadContextItems.Create(
                carrier,
                WorldStateComposer.ItemKind(fragment.SectionId),
                fragment.Text))
            .ToArray();

        return new WorldStateTurnUpdate(items, render, baseline == null);
    }

    private void CommitWorldStateUpdate(
        string threadId,
        string turnId,
        WorldStateTurnUpdate update,
        AgentModeManager? modeManager)
    {
        var origin = "none";
        if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
        {
            origin = runtime.WorldStateBaselineOrigin;
            runtime.WorldStateBaseline = update.Render.Snapshot;
            runtime.WorldStateBaselineOrigin = "memory";
        }

        RecordWorldStateDiagnostic(threadId, turnId, update, origin);

        // The mode section carries the exit-from-plan notice, so the one-shot is spent on delivery.
        if (modeManager?.JustSwitchedFromPlan == true)
            modeManager.AcknowledgeTransition();
    }

    private void RecordWorldStateDiagnostic(
        string threadId,
        string turnId,
        WorldStateTurnUpdate update,
        string baselineSource)
    {
        if (TraceCollector is not { } trace)
            return;

        var fragments = update.Render.Fragments;
        var status = fragments.Count == 0
            ? "unchanged"
            : update.Full ? "full" : "emitted";
        trace.RecordWorldState(
            threadId,
            turnId,
            status,
            [.. fragments.Select(static fragment => fragment.SectionId)],
            fragments.Count == 0
                ? null
                : string.Join("\n\n", fragments.Select(static fragment => fragment.Text)),
            update.Render.SilentSectionIds,
            [.. update.Render.Faults.Select(static fault =>
                new WorldStateSectionSuppression(fault.SectionId, fault.Reason))],
            baselineSource);
    }

    private async Task<IReadOnlyList<ChatMessage>> DrainWorldStateMessagesAsync(
        SessionThread thread,
        string turnId,
        IReadOnlyList<ChatMessage> history,
        ThreadContextCarrier carrier,
        AgentModeManager? modeManager,
        string? sessionStartHookContext,
        CancellationToken ct)
    {
        var hasActivePlan = agentFactory.PlanStore?.StructuredPlanExists(thread.Id) == true;
        var goal = GoalsEnabled
            ? await persistence.GetThreadGoalAsync(thread.Id, ct).ConfigureAwait(false)
            : null;

        var update = BuildWorldStateUpdate(
            thread,
            history,
            carrier,
            modeManager,
            hasActivePlan,
            goal,
            sessionStartHookContext);
        CommitWorldStateUpdate(thread.Id, turnId, update, modeManager);
        return update.Items;
    }

    private async Task RestoreWorldStateBaselineAsync(SessionThread thread, CancellationToken ct)
    {
        if (!_runtimeRegistry.TryGetRuntime(thread.Id, out var runtime) || runtime.WorldStateBaselineRestored)
            return;

        runtime.WorldStateBaselineRestored = true;
        if (thread.Ephemeral)
            return;

        var baseline = await persistence.LoadWorldStateBaselineAsync(thread, ct).ConfigureAwait(false);
        runtime.WorldStateBaseline = baseline;
        runtime.PersistedWorldStateBaseline = baseline;
        runtime.WorldStateBaselineOrigin = baseline == null ? "none" : "rollout";
    }

    private async Task PersistWorldStateAsync(SessionThread thread, string turnId)
    {
        if (thread.Ephemeral
            || !_runtimeRegistry.TryGetRuntime(thread.Id, out var runtime)
            || runtime.WorldStateBaseline is not { } current)
        {
            return;
        }

        var persisted = runtime.PersistedWorldStateBaseline;
        var full = persisted == null;
        var state = full ? current.ToJsonObject() : current.MergePatchFrom(persisted!);
        if (state == null)
            return;

        try
        {
            await persistence
                .AppendWorldStateAsync(thread.Id, turnId, full, state, CancellationToken.None)
                .ConfigureAwait(false);
            runtime.PersistedWorldStateBaseline = current;
        }
        catch (Exception ex)
        {
            // A missed record only costs the next resume a full restatement.
            logger?.LogWarning(ex, "Could not record the world state for thread {ThreadId}.", thread.Id);
        }
    }

    private void InheritWorldStateBaseline(
        string parentThreadId,
        string childThreadId,
        IReadOnlyList<ChatMessage> forkHistory)
    {
        if (!_runtimeRegistry.TryGetRuntime(parentThreadId, out var parent)
            || parent.WorldStateBaseline is not { } baseline
            || !_runtimeRegistry.TryGetRuntime(childThreadId, out var child))
        {
            return;
        }

        var inherited = SectionIdsInHistory(forkHistory);
        var state = baseline.ToJsonObject();
        foreach (var id in state.Select(static section => section.Key).ToArray())
        {
            if (!inherited.Contains(id))
                state.Remove(id);
        }

        child.WorldStateBaseline = state.Count == 0 ? null : WorldStateSnapshot.FromJsonObject(state);
        // The fork checkpoint is the newest one in the child rollout, so its first record is a full snapshot.
        child.WorldStateBaselineRestored = true;
        child.WorldStateBaselineOrigin = child.WorldStateBaseline == null ? "none" : "fork";
    }

    private void ResetWorldStateBaseline(string threadId, string reason)
    {
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            return;

        // Replay stops at the newest checkpoint, so the durable baseline restarts here too.
        runtime.WorldStateBaseline = null;
        runtime.PersistedWorldStateBaseline = null;
        runtime.WorldStateBaselineRestored = true;
        runtime.WorldStateBaselineOrigin = "none";
        logger?.LogDebug("Reset world state baseline for thread {ThreadId}: {Reason}", threadId, reason);
    }

    private static IReadOnlySet<string> SectionIdsInHistory(IReadOnlyList<ChatMessage> history)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in history)
        {
            if (WorldStateComposer.SectionIdFromItemKind(ThreadContextItems.GetKind(message)) is { } id)
                ids.Add(id);
        }

        return ids;
    }
}
