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

    public Task<TurnStartOrEnqueueAdmission> StartOrEnqueueTurnAsync(
        string threadId,
        IList<AIContent> content,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null,
        string? model = null,
        TurnReasoningOverrides? reasoning = null)
    {
        var capturedContent = content.ToList();
        var capturedInput = CloneInputSnapshot(inputSnapshot);
        var settings = new TurnSettingsOverrides { Model = model, Reasoning = reasoning };
        return InvokeThreadCommandAsync(threadId, async commandCt =>
        {
            var runtime = GetInputRuntime(threadId);
            var thread = runtime.Thread;
            ValidateTurnHistory(thread, messages: null);
            if (capturedContent.Count == 0 && capturedInput?.MaterializedInputParts is not { Count: > 0 })
                throw new ArgumentException("Follow-up input must not be empty.", nameof(content));

            var prepared = PrepareTurnSettings(thread, settings, inputMode: null);
            var state = ThreadSummaryRuntime.FromThread(thread, runtime.Maintenance?.Kind);
            if (state.Busy || state.WaitingOnPlanConfirmation)
            {
                await CommitTurnSettingsAsync(runtime, prepared, commandCt);
                var queued = await ThreadQueue.EnqueueAsync(threadId, capturedContent, null, commandCt, capturedInput);
                return new TurnStartOrEnqueueAdmission(QueuedInput: queued);
            }

            var admission = await StartAdmittedTurnAsync(runtime, capturedContent, null, null, capturedInput, prepared, commandCt);
            return new TurnStartOrEnqueueAdmission(Turn: admission.Turn, Events: admission.Events);
        }, ct);
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
        var runtime = GetInputRuntime(threadId);
        var thread = runtime.Thread;
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

        return await StartAdmittedTurnAsync(runtime, content, sender, messages, inputSnapshot, prepared, ct);
    }

    private ThreadRuntime GetInputRuntime(string threadId)
    {
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            || _runtimeRegistry.IsPendingPermanentDeletion(threadId))
            throw new InvalidOperationException($"Thread '{threadId}' cannot accept input in its current runtime.");
        if (runtime.Thread.Status != ThreadStatus.Active)
            throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {runtime.Thread.Status}). Cannot submit input.");
        return runtime;
    }

    private async Task<TurnInputAdmission> StartAdmittedTurnAsync(
        ThreadRuntime runtime,
        IList<AIContent> content,
        SenderContext? sender,
        ChatMessage[]? messages,
        SessionInputSnapshot? inputSnapshot,
        PreparedTurnSettings? prepared,
        CancellationToken ct)
    {
        await CommitTurnSettingsAsync(runtime, prepared, ct);
        var thread = runtime.Thread;
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
