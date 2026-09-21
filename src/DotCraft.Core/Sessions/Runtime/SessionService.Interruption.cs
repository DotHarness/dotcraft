using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private bool InterruptMessageEnabled =>
        (_appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config).AgentInterruptMessageEnabled;

    private async Task CommitInterruptedTurnAsync(
        SessionThread thread,
        SessionTurn turn,
        TurnExecutionState execution,
        TurnCommitter committer,
        IProviderConversationHistory? providerHistory)
    {
        if (execution.IntentionallyInterrupted && InterruptMessageEnabled)
        {
            var marker = TurnInterruption.Create(turn.Id, ResolveThreadContextCarrier(thread));
            if (committer.Session == null)
            {
                var previous = thread.Ephemeral
                    && _runtimeRegistry.TryGetRuntime(thread.Id, out var existingRuntime)
                    && existingRuntime.EphemeralHistory is { } inMemory
                        ? inMemory.Select(message => message.Clone()).ToList()
                        : await persistence.LoadModelHistoryAsync(thread, turn.Id, CancellationToken.None);
                committer.PersistedModelHistoryCount = previous.Count;
                previous.AddRange(ThreadStore.BuildModelVisibleHistoryFromTurn(turn));
                committer.Session = previous;
            }
            await AppendProviderInterruptionAsync(thread, turn, marker, providerHistory, CancellationToken.None);
            committer.Session.Add(marker);
        }
        await committer.CommitAsync();
    }

    private async Task AppendProviderInterruptionAsync(
        SessionThread thread, SessionTurn turn, ChatMessage marker,
        IProviderConversationHistory? context, CancellationToken ct)
    {
        context ??= await LoadInterruptionProviderHistoryAsync(thread, turn, ct);
        if (context == null)
            return;
        var pending = context.CaptureOpaqueSnapshot().CoveredThroughTurnId == turn.Id
            ? new List<ChatMessage>()
            : ThreadStore.BuildModelVisibleHistoryFromTurn(turn).ToList();
        pending.Add(marker);
        await context.AppendLocalInputAsync(pending, ct);
        if (thread.Ephemeral && _runtimeRegistry.TryGetRuntime(thread.Id, out var runtime))
            runtime.ResponsesProviderHistorySnapshot = context.CaptureOpaqueSnapshot();
    }

    private async Task<IProviderConversationHistory?> LoadInterruptionProviderHistoryAsync(
        SessionThread thread, SessionTurn turn, CancellationToken ct)
    {
        var config = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;
        var model = agentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(
            config, thread.Configuration?.ProviderId, thread.Configuration?.Model);
        var factory = agentFactory.RuntimeContext.ChatClientRegistry.GetProviderService<IProviderHistorySessionFactory>(model);
        if (factory == null)
            return null;
        var identity = ThreadConversationIdentity.Create(thread, turn,
            GetOrCreateResponsesContextWindow(thread.Id).CurrentWindowId, ProviderRequestKind.Turn);
        var snapshot = thread.Ephemeral && _runtimeRegistry.TryGetRuntime(thread.Id, out var existing)
            && existing.ResponsesProviderHistorySnapshot is { } inMemory
                ? inMemory
                : ToOpaqueHistory(model, identity,
                    await persistence.LoadProviderHistoryAsync(thread, identity.ContextWindowId, ct));
        // An empty native baseline will be populated from neutral history on the next request.
        if (snapshot.Items.Count == 0)
            return null;
        identity = identity with { ContextWindowId = snapshot.Identity.ContextWindowId };
        return factory.CreateSession(identity, snapshot, [],
            thread.Ephemeral ? null : new SessionProviderHistorySink(persistence));
    }
}
