using DotCraft.Channels;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public Task<TurnInputAdmission> StartOrSteerTurnAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null,
        TurnSettingsOverrides? settings = null)
    {
        var capturedContent = content.ToList();
        var capturedMessages = messages?.Select(static message => message.Clone()).ToArray();
        var capturedInput = CloneInputSnapshot(inputSnapshot);
        var capturedSettings = settings is null ? null : settings with
        {
            RuntimeWorkspaceRoots = settings.RuntimeWorkspaceRoots?.ToArray()
        };
        return InvokeThreadCommandAsync(threadId, commandCt => AdmitTurnInputAsync(
            threadId, capturedContent, sender, capturedMessages, capturedInput, capturedSettings, commandCt), ct);
    }

    private async Task<TurnInputAdmission> AdmitTurnInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender,
        ChatMessage[]? messages,
        SessionInputSnapshot? inputSnapshot,
        TurnSettingsOverrides? settings,
        CancellationToken ct)
    {
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            || _runtimeRegistry.IsPendingPermanentDeletion(threadId))
            throw new InvalidOperationException($"Thread '{threadId}' cannot accept input in its current runtime.");
        var thread = runtime.Thread;
        if (thread.Status != ThreadStatus.Active)
            throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot submit input.");
        ValidateTurnHistory(thread, messages);
        ThrowIfThreadMaintenanceActive(threadId);
        var prepared = PrepareTurnSettings(thread, settings, inputSnapshot?.Mode);
        var active = thread.Turns.LastOrDefault(turn =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (active is not null)
        {
            if (ToolPlanningThreadClassifier.Classify(thread) is ToolPlanningThreadKind.Internal
                || thread.Source.SubAgent is not null)
                throw new InvalidOperationException($"Thread '{threadId}' has a running Turn that cannot accept guidance.");
            if (content.Count == 0 && inputSnapshot?.MaterializedInputParts is not { Count: > 0 })
                throw new ArgumentException("Steer input must not be empty.", nameof(content));
            var acceptedActive = SnapshotAdmittedTurn(active);
            await ThreadQueue.SteerAsync(threadId, active.Id, content, sender, ct, inputSnapshot);
            if (prepared is not null && runtime.TryGetTurn(active.Id, out var activeRuntime)
                && activeRuntime.Context is { } activeContext)
                await ObserveTurnResourcesAsync(activeContext.Resources);
            await CommitTurnSettingsAsync(runtime, prepared, ct);
            return new(acceptedActive, Started: false);
        }

        await CommitTurnSettingsAsync(runtime, prepared, ct);
        var configuration = thread.Configuration is { } current
            ? ThreadConfigurationCloner.Clone(current)
            : new ThreadConfiguration();
        Task<TurnExecutionResources> resources;
        try
        {
            resources = Task.FromResult(await CaptureTurnExecutionResourcesAsync(runtime, ct).ConfigureAwait(false));
        }
        catch (Exception error)
        {
            // Resource preparation belongs to accepted execution; its failure is reported by the Turn.
            resources = Task.FromException<TurnExecutionResources>(error);
        }
        var context = new TurnExecutionContext(
            runtime.Generation,
            resources,
            configuration,
            ThreadWorkspaceResolver.Resolve(thread.WorkspacePath, configuration),
            ChannelSessionScope.Current,
            TurnTriggerScope.Current,
            SessionClientCapabilitiesScope.Current?.SupportsCommandExecutionStreaming == true,
            SessionClientCapabilitiesScope.Current?.SupportsToolExecutionLifecycle == true);
        var events = await StartTurnAsync(runtime, content, sender, messages, inputSnapshot, ct, context);
        var turn = thread.Turns.Last(turn => turn.Id == events.TurnId);
        return new(SnapshotAdmittedTurn(turn), Started: true, events.ReadAllAsync(CancellationToken.None));
    }

    private static async Task ObserveTurnResourcesAsync(Task<TurnExecutionResources> resources)
    {
        try { await resources.ConfigureAwait(false); }
        catch (Exception) { } // The active Turn owns preparation failures, not a subsequent settings update.
    }

    private static SessionTurn SnapshotAdmittedTurn(SessionTurn turn) => new()
    {
        Id = turn.Id,
        ThreadId = turn.ThreadId,
        Status = TurnStatus.Running,
        StartedAt = turn.StartedAt,
        OriginChannel = turn.OriginChannel,
        Initiator = turn.Initiator
    };
}
