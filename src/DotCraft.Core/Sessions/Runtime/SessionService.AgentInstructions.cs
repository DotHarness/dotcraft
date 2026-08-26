using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private readonly IContextPageManager _fallbackAgentInstructionContextPages = new ContextPageManager();

    private IContextPageManager AgentInstructionContextPages =>
        AgentFactory.RuntimeContext.ContextPageManager ?? _fallbackAgentInstructionContextPages;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetInstructionSourcesAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        var snapshot = ResolveAgentInstructions(thread);
        await ReconcilePersistedAgentInstructionsAsync(thread, snapshot, ct);
        RecordAgentInstructionsSnapshot(thread.Id, snapshot);
        return snapshot.Sources;
    }

    private ContextPageSnapshot ResolveAgentInstructions(SessionThread thread)
    {
        var context = ThreadWorkspaceResolver.Resolve(thread);
        var config = _appConfigMonitor?.Current ?? AgentFactory.RuntimeContext.Config;
        var globalConfigPath = string.IsNullOrWhiteSpace(config.GlobalConfigPath)
            ? string.Empty
            : Path.GetFullPath(config.GlobalConfigPath!);
        var variant = string.Join(
            "\0",
            Path.GetFullPath(context.Cwd),
            globalConfigPath,
            config.ProjectDocMaxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var shouldLoad = thread.Source.SubAgent is not { } subAgent
                         || string.Equals(
                subAgent.RuntimeType ?? string.Empty,
                             NativeSubAgentRuntime.RuntimeTypeName,
                             StringComparison.OrdinalIgnoreCase);

        return AgentInstructionContextPages.GetOrAdd(
            thread.Id,
            ContextPageKeys.AgentInstructions(variant),
            ContextPageLifecycle.StableUntilCompaction,
            () => shouldLoad
                ? new AgentInstructionsLoader(Logger).Load(context.Cwd, config).ToContextPageDocument()
                : ContextPageDocument.FromContent(string.Empty));
    }

    private async Task ReconcilePersistedAgentInstructionsAsync(
        SessionThread thread,
        ContextPageSnapshot snapshot,
        CancellationToken ct)
    {
        if (thread.HistoryMode != HistoryMode.Server)
            return;

        var history = await Persistence.LoadModelHistoryAsync(thread.Id, ct);
        var change = AgentInstructionsHistory.Reconcile(history, snapshot.Content);
        if (change == AgentInstructionsHistoryChange.None)
            return;

        var coveredTurn = thread.Turns
            .Where(static turn => turn.Status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled)
            .OrderBy(static turn => turn.StartedAt)
            .ThenBy(static turn => turn.Id, StringComparer.Ordinal)
            .LastOrDefault();
        if (coveredTurn == null)
            return;

        var tokens = MessageTokenEstimator.Estimate(history);
        await Persistence.AppendCompactionCheckpointAsync(
            thread.Id,
            coveredTurn.Id,
            history.Select(static message => message.Clone()).ToList(),
            trigger: "agents_md_instructions_changed",
            mode: "partial",
            tokensBefore: tokens,
            tokensAfter: tokens,
            ct);
        TryAdvanceResponsesContextWindowAfterReplacement(thread.Id);
        await TryReplaceResponsesProviderHistoryAsync(
            thread,
            history,
            "agents_md_instructions_changed",
            ct);
    }

    private ContextPageSnapshot ReloadAgentInstructionsAfterCompaction(
        SessionThread thread,
        IList<ChatMessage> history)
    {
        AgentInstructionContextPages.ReleaseStablePages(thread.Id);
        var snapshot = ResolveAgentInstructions(thread);
        AgentInstructionsHistory.Reconcile(history, snapshot.Content);
        RecordAgentInstructionsSnapshot(thread.Id, snapshot);
        return snapshot;
    }

    private void RecordAgentInstructionsSnapshot(string threadId, ContextPageSnapshot snapshot) =>
        TraceCollector?.RecordAgentInstructions(
            threadId,
            snapshot.Content,
            snapshot.Sources,
            snapshot.Fingerprint);

    private static List<ChatMessage> WithoutAgentInstructions(IEnumerable<ChatMessage> history) =>
        history
            .Where(static message => !AgentInstructionsHistory.IsInstructions(message))
            .Select(static message => message.Clone())
            .ToList();

    private static PromptRequestSnapshot? WithoutAgentInstructions(PromptRequestSnapshot? snapshot)
    {
        if (snapshot == null || !snapshot.Messages.Any(AgentInstructionsHistory.IsInstructions))
            return snapshot;

        var messages = WithoutAgentInstructions(snapshot.Messages);
        return snapshot with
        {
            Messages = messages,
            MessageFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(messages, messages.Count)
        };
    }
}
