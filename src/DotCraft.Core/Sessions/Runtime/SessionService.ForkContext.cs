using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task MaterializeThreadForkContextAsync(
        SessionThread source,
        SessionThread fork,
        string? activeSourceTurnId,
        PromptRequestSnapshot? sourceRequest,
        IReadOnlyList<SessionItem> completedActiveItems,
        bool wholeTurnBoundary,
        CancellationToken ct)
    {
        var forkTurns = fork.Turns.ToArray();
        var lastTurn = forkTurns.LastOrDefault();
        var activeForkTurn = lastTurn?.Id == activeSourceTurnId ? lastTurn : null;
        var interruption = activeForkTurn?.Input is not null && InterruptMessageEnabled
            ? TurnInterruption.Create(activeForkTurn.Id, ResolveThreadContextCarrier(fork))
            : null;
        var materialization = await persistence.BuildForkModelHistoryMaterializationAsync(
            source, fork, ct, activeForkTurn?.Id, interruption);

        var history = materialization.History;
        var providerTail = history.Skip(materialization.WholeTurnMessageCount).ToArray();
        var model = ResolveForkModel(fork);
        OpaqueProviderHistorySnapshot? providerHistory;
        if (activeForkTurn is not null && wholeTurnBoundary
            && sourceRequest is { CompletedTurnItemIds: { } coveredItems }
            && sourceRequest.TurnId == activeForkTurn.Id
            && ExtendsForkPrefix(sourceRequest, history.Take(materialization.WholeTurnMessageCount).ToArray(), out var prefixLength))
        {
            var completedTail = ThreadStore.BuildModelVisibleHistoryFromItems(completedActiveItems, coveredItems);
            providerTail = interruption is null ? [.. completedTail] : [.. completedTail, interruption];
            materialization.TurnHistories[activeForkTurn.Id] = [.. sourceRequest.Messages.Skip(prefixLength), .. providerTail];
            history = [.. sourceRequest.Messages, .. providerTail];
            providerHistory = sourceRequest.ProviderHistory;
        }
        else
        {
            providerHistory = await LoadForkProviderPrefixAsync(source, materialization, model, ct);
        }

        if (fork.Ephemeral)
        {
            _runtimeRegistry.SetThread(fork).EphemeralHistory = history;
        }
        else
        {
            await PersistThreadWithMaterializationAsync(fork, ct);
            await PersistForkModelHistoryAsync(fork, forkTurns, materialization, ct);
        }

        var provider = await InstallForkProviderHistoryAsync(fork, model, providerHistory, history, providerTail, ct);
        var measured = await InheritForkContextUsageAsync(source, fork, model, history, ct);
        if (measured is not null || fork.Ephemeral)
            return;

        if (provider is not null && provider.TryEstimateActiveContextTokens(
            history, GetThreadAgentOrDefault(fork.Id).ChatOptions, out var nativeTokens))
        {
            await SaveContextUsageSnapshotAsync(fork.Id, nativeTokens, "provider_compacted_estimate", isEstimate: true, ct);
        }
        else
        {
            await SaveContextUsageSnapshotAsync(
                fork.Id, MessageTokenEstimator.Estimate(history),
                materialization.Checkpoint is not null ? "compacted_estimate" : "history_estimate",
                isEstimate: true, ct);
        }
    }

    private static bool ExtendsForkPrefix(
        PromptRequestSnapshot request,
        IReadOnlyList<ChatMessage> history,
        out int prefixLength)
    {
        var prefix = PrepareProviderVisibleHistory(MessageGrouper.NormalizeFunctionCallArguments(history));
        prefixLength = prefix.Count;
        return prefix.Count <= request.Messages.Count
            && MessageTokenEstimator.ComputePrefixFingerprint(prefix, prefix.Count)
                == MessageTokenEstimator.ComputePrefixFingerprint(request.Messages, prefix.Count);
    }

    private async Task PersistForkModelHistoryAsync(
        SessionThread fork,
        IReadOnlyList<SessionTurn> turns,
        ForkModelHistoryMaterialization materialization,
        CancellationToken ct)
    {
        if (materialization.Checkpoint is { } checkpoint)
        {
            await persistence.AppendCompactionCheckpointAsync(
                fork.Id,
                checkpoint.CoveredThroughTurnId,
                materialization.History.Take(checkpoint.ReplacementHistory.Count).ToArray(),
                trigger: "fork",
                mode: checkpoint.Mode,
                tokensBefore: checkpoint.TokensBefore,
                tokensAfter: checkpoint.TokensAfter,
                ct);
        }

        if (fork.HistoryMode == HistoryMode.Server)
        {
            foreach (var turn in turns)
            {
                if (materialization.TurnHistories.TryGetValue(turn.Id, out var messages))
                    await persistence.AppendModelHistoryAsync(fork.Id, messages, turn.Id, ct);
            }
        }
    }

    private async Task<OpaqueProviderHistorySnapshot?> LoadForkProviderPrefixAsync(
        SessionThread source,
        ForkModelHistoryMaterialization materialization,
        EffectiveModelRuntime model,
        CancellationToken ct)
    {
        if (!model.IsOpenAIResponses || !SupportsServerManagedProviderHistory(source)
            || materialization.WholeTurnIds.Count == 0)
        {
            return null;
        }

        var window = GetOrCreateResponsesContextWindow(source.Id).CurrentWindowId;
        var snapshot = await persistence.LoadForkProviderHistoryAsync(
            source.Id, window, materialization.WholeTurnIds.ToHashSet(StringComparer.Ordinal),
            materialization.Checkpoint?.CreatedAt, ct);
        return snapshot.CoveredThroughTurnId == materialization.WholeTurnIds[^1]
            ? ToOpaqueHistory(model, ThreadConversationIdentity.Create(source, null, window, ProviderRequestKind.Turn), snapshot)
            : null;
    }

    private EffectiveModelRuntime ResolveForkModel(SessionThread thread) =>
        AgentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(
            _appConfigMonitor?.Current ?? AgentFactory.RuntimeContext.Config,
            thread.Configuration?.ProviderId, thread.Configuration?.Model);

    private async Task<IProviderConversationHistory?> InstallForkProviderHistoryAsync(
        SessionThread fork,
        EffectiveModelRuntime model,
        OpaqueProviderHistorySnapshot? snapshot,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ChatMessage> tail,
        CancellationToken ct)
    {
        if (snapshot is not { Items.Count: > 0 } || !SupportsServerManagedProviderHistory(fork)
            || snapshot.Identity.ProviderId != model.ProviderId
            || snapshot.Identity.Protocol != model.Protocol
            || snapshot.Identity.Model != model.Model
            || AgentFactory.RuntimeContext.ChatClientRegistry.GetProviderService<IProviderHistorySessionFactory>(model) is not { } factory)
        {
            return null;
        }

        var window = GetOrCreateResponsesContextWindow(fork.Id).CurrentWindowId;
        var identity = ThreadConversationIdentity.Create(fork, fork.Turns.LastOrDefault(), window, ProviderRequestKind.Turn);
        var inherited = snapshot with
        {
            Identity = snapshot.Identity with
            {
                ThreadId = fork.Id,
                TurnId = identity.TurnId,
                GenerationId = window,
                ContextWindowId = window
            },
            CoveredThroughTurnId = identity.TurnId
        };

        var prefix = tail.Count == 0 ? history : history.Take(history.Count - tail.Count).ToArray();
        var context = factory.CreateSession(model, identity, inherited, prefix, sink: null);
        if (tail.Count > 0)
        {
            await context.AppendLocalInputAsync(tail, ct);
            inherited = context.CaptureOpaqueSnapshot();
        }
        if (fork.Ephemeral)
            _runtimeRegistry.SetThread(fork).ResponsesProviderHistorySnapshot = inherited;
        else
            await new SessionProviderHistorySink(persistence).ReplaceAsync(
                inherited.Identity, inherited.CoveredThroughTurnId,
                inherited.IsNativeCompacted ? ProviderHistoryReasons.ForkNativeCompaction : ProviderHistoryReasons.Fork,
                inherited.Items, ct);
        return context;
    }

    private async Task<long?> InheritForkContextUsageAsync(
        SessionThread source,
        SessionThread fork,
        EffectiveModelRuntime model,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct)
    {
        var anchor = TryGetInMemoryContextUsageAnchor(source.Id) ?? persistence.LoadContextUsageAnchor(source.Id);
        if (anchor is null)
            return null;

        var sourceModel = ResolveForkModel(source);
        if (sourceModel.ProviderId != model.ProviderId || sourceModel.Protocol != model.Protocol || sourceModel.Model != model.Model)
            return null;

        var visibleHistory = PrepareProviderVisibleHistory(MessageGrouper.NormalizeFunctionCallArguments(history));
        var tokens = ContextUsageTokenCounter.EstimateFromAnchor(
            anchor, visibleHistory, anchor.RequestFingerprint, requireRequestFingerprint: true);
        if (tokens is null)
            return null;

        await SaveContextUsageSnapshotAsync(fork.Id, tokens.Value, anchor, "estimate", isEstimate: true, ct);
        return tokens;
    }
}
