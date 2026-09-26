using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private ResponsesContextWindowRecord GetOrCreateResponsesContextWindow(string threadId)
    {
        try
        {
            return persistence.GetOrCreateResponsesContextWindow(threadId);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Falling back to a transient provider context window for thread {ThreadId}", threadId);
            return ResponsesContextWindowRecord.Transient(threadId);
        }
    }

    private void TryAdvanceResponsesContextWindowAfterReplacement(string threadId)
    {
        try
        {
            var record = persistence.AdvanceResponsesContextWindow(threadId);
            var context = ProviderRequestContextScope.Current?.ConversationState;
            if (context != null
                && string.Equals(
                    context.Identity.CurrentThreadId,
                    threadId,
                    StringComparison.Ordinal))
            {
                context.AdvanceContextWindow(record.CurrentWindowId);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to advance provider context window for thread {ThreadId}", threadId);
        }
    }

    private void TryReconcileResponsesContextWindow(string threadId, string committedWindowId)
    {
        try
        {
            var record = persistence.ReconcileResponsesContextWindow(threadId, committedWindowId);
            var context = ProviderRequestContextScope.Current?.ConversationState;
            if (context != null
                && string.Equals(
                    context.Identity.CurrentThreadId,
                    threadId,
                    StringComparison.Ordinal))
            {
                context.AdvanceContextWindow(record.CurrentWindowId);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(
                ex,
                "Failed to reconcile provider context window for thread {ThreadId}",
                threadId);
        }
    }

    private async Task<IProviderConversationHistory?> CreateResponsesProviderHistoryContextAsync(
        SessionThread thread,
        SessionTurn? turn,
        List<ChatMessage> session,
        CancellationToken ct,
        string? coveredThroughTurnIdOverride = null,
        string? forceReplacementReason = null)
    {
        if (thread.ProviderHistorySchemaVersion != ProviderHistorySchema.CurrentSchemaVersion
            || thread.HistoryMode != HistoryMode.Server)
            return null;
        var currentConfig = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;
        var runtime = agentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(
            currentConfig,
            thread.Configuration?.ProviderId,
            thread.Configuration?.Model);
        if (!string.Equals(
                runtime.Protocol,
                ModelProviderProtocols.OpenAIResponses,
                StringComparison.Ordinal))
        {
            return null;
        }

        if (!TrySnapshotInMemoryHistory(session, out var baselineHistory))
            baselineHistory = [];

        var activeIdentity = ProviderRequestContextScope.Current?.CurrentIdentity
            ?? throw new InvalidOperationException(
                "Responses provider history requires an active conversation identity.");
        var snapshot = thread.Ephemeral
            ? _runtimeRegistry.TryGetRuntime(thread.Id, out var ephemeralRuntime)
              && ephemeralRuntime.ResponsesProviderHistorySnapshot is { } inMemorySnapshot
                ? inMemorySnapshot
                : CreateEmptyOpaqueHistory(runtime, activeIdentity)
            : ToOpaqueHistory(
                runtime,
                activeIdentity,
                await persistence.LoadProviderHistoryAsync(
                        thread,
                        activeIdentity.ContextWindowId,
                        ct)
                    .ConfigureAwait(false));
        if (!string.Equals(
                snapshot.Identity.ContextWindowId,
                activeIdentity.ContextWindowId,
                StringComparison.Ordinal))
        {
            TryReconcileResponsesContextWindow(thread.Id, snapshot.Identity.ContextWindowId);
            activeIdentity = ProviderRequestContextScope.Current?.CurrentIdentity
                ?? activeIdentity with { ContextWindowId = snapshot.Identity.ContextWindowId };
        }
        var previousTurn = thread.Turns
            .Where(candidate =>
                (turn is null || !string.Equals(candidate.Id, turn.Id, StringComparison.Ordinal))
                && candidate.Status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled)
            .OrderBy(candidate => candidate.StartedAt)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .LastOrDefault();
        var requiresReplacement = !string.IsNullOrWhiteSpace(forceReplacementReason)
            || previousTurn != null
               && !string.Equals(
                   snapshot.CoveredThroughTurnId,
                   previousTurn.Id,
                   StringComparison.Ordinal);
        if (requiresReplacement)
        {
            TryAdvanceResponsesContextWindowAfterReplacement(thread.Id);
            activeIdentity = ProviderRequestContextScope.Current?.CurrentIdentity ?? activeIdentity;
            snapshot = CreateEmptyOpaqueHistory(runtime, activeIdentity);
        }

        var providerIdentity = string.IsNullOrWhiteSpace(coveredThroughTurnIdOverride)
            ? activeIdentity
            : activeIdentity with { TurnId = coveredThroughTurnIdOverride };
        var factory = agentFactory.RuntimeContext.ChatClientRegistry
            .GetProviderService<IProviderHistorySessionFactory>(runtime);
        if (factory == null)
            return null;
        var context = factory.CreateSession(
            providerIdentity,
            snapshot,
            baselineHistory,
            thread.Ephemeral ? null : new SessionProviderHistorySink(persistence));

        if (requiresReplacement)
        {
            await context.HistoryReplacedAsync(
                    baselineHistory,
                    options: null,
                    forceReplacementReason ?? "protocol_return",
                    ct)
                .ConfigureAwait(false);
        }

        return context;
    }

    private ThreadContextCarrier ResolveThreadContextCarrier(SessionThread thread)
    {
        var currentConfig = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;
        var runtime = agentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(
            currentConfig,
            thread.Configuration?.ProviderId,
            thread.Configuration?.Model);
        return ThreadContextItems.ResolveCarrier(runtime.IsOpenAIResponses);
    }

    private async Task TryReplaceResponsesProviderHistoryAsync(
        SessionThread thread,
        List<ChatMessage> session,
        string reason,
        CancellationToken ct)
    {
        if (thread.ProviderHistorySchemaVersion != ProviderHistorySchema.CurrentSchemaVersion
            || thread.Ephemeral)
        {
            return;
        }

        var currentConfig = _appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config;
        var runtime = agentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(
            currentConfig,
            thread.Configuration?.ProviderId,
            thread.Configuration?.Model);
        if (!string.Equals(runtime.Protocol, ModelProviderProtocols.OpenAIResponses, StringComparison.Ordinal)
            || !TrySnapshotInMemoryHistory(session, out var replacementHistory))
        {
            return;
        }

        var coveredTurn = ResolveNewestTerminalTurn(thread);
        var identity = ThreadConversationIdentity.Create(
            thread,
            coveredTurn,
            GetOrCreateResponsesContextWindow(thread.Id).CurrentWindowId,
            ProviderRequestKind.Compaction);
        var factory = agentFactory.RuntimeContext.ChatClientRegistry
            .GetProviderService<IProviderHistorySessionFactory>(runtime);
        if (factory == null)
            return;
        var context = factory.CreateSession(
            identity,
            CreateEmptyOpaqueHistory(runtime, identity),
            coveredMessages: [],
            new SessionProviderHistorySink(persistence));
        await context.HistoryReplacedAsync(
            replacementHistory,
            options: null,
            reason,
            ct).ConfigureAwait(false);
    }

    private static OpaqueProviderHistorySnapshot CreateEmptyOpaqueHistory(
        EffectiveModelRuntime runtime,
        ProviderConversationIdentity identity) =>
        new(
            new ProviderHistoryIdentity(
                runtime.ProviderId,
                runtime.Protocol,
                ProviderHistorySchema.CurrentSchemaVersion,
                identity.CurrentThreadId,
                identity.TurnId,
                identity.ContextWindowId,
                identity.ContextWindowId),
            [],
            CoveredThroughTurnId: null);

    private static OpaqueProviderHistorySnapshot ToOpaqueHistory(
        EffectiveModelRuntime runtime,
        ProviderConversationIdentity identity,
        ProviderHistorySnapshot snapshot) =>
        new(
            new ProviderHistoryIdentity(
                runtime.ProviderId,
                runtime.Protocol,
                ProviderHistorySchema.CurrentSchemaVersion,
                identity.CurrentThreadId,
                identity.TurnId,
                snapshot.GenerationId,
                snapshot.ContextWindowId),
            snapshot.Entries
                .Select(static entry => new ProviderHistoryItem(entry.EntryId, entry.Item.Clone()))
                .ToArray(),
            snapshot.CoveredThroughTurnId,
            snapshot.IsNativeCompacted);
}
