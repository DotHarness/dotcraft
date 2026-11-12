using DotCraft.Channels;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    /// <inheritdoc/>
    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        if (!_runtimeRegistry.TryGetThread(threadId, out _))
            throw new ThreadNotFoundException(threadId, "Call CreateThreadAsync or ResumeThreadAsync first.");
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            throw new InvalidOperationException($"Thread '{threadId}' has no active runtime.");

        var capturedContent = content.ToList();
        var capturedMessages = messages?.Select(static message => message.Clone()).ToArray();
        var capturedInputSnapshot = CloneInputSnapshot(inputSnapshot);

        // Admission is a short, serialized Thread transition. The returned Turn continues
        // independently, so the command pump remains available for cancel/approval/queue work.
        var channel = runtime.Commands.InvokeAsync(
            _ => StartTurnAsync(
                runtime,
                capturedContent,
                sender,
                capturedMessages,
                capturedInputSnapshot,
                ct),
            ct).GetAwaiter().GetResult();
        return channel.ReadAllAsync(ct);
    }

    private async Task<SessionEventChannel> StartTurnAsync(
        ThreadRuntime runtime,
        IList<AIContent> content,
        SenderContext? sender,
        ChatMessage[]? messages,
        SessionInputSnapshot? inputSnapshot,
        CancellationToken callerCt)
    {
        var thread = runtime.Thread;
        if (!_runtimeRegistry.IsCurrent(thread.Id, runtime))
            throw new InvalidOperationException($"Thread '{thread.Id}' runtime was replaced before Turn admission.");
        if (_runtimeRegistry.IsPendingPermanentDeletion(thread.Id))
            throw new InvalidOperationException($"Thread '{thread.Id}' is being permanently deleted and cannot accept a new Turn.");

        ValidateTurnAdmission(thread, messages);
        if (inputSnapshot?.Mode is { } mode)
            await CommitThreadModeAsync(thread, mode, callerCt);

        var configuration = thread.Configuration == null
            ? new ThreadConfiguration()
            : ThreadConfigurationCloner.Clone(thread.Configuration);
        var turnContext = new TurnExecutionContext(
            runtime.Generation,
            CaptureTurnExecutionResourcesAsync(runtime, callerCt),
            configuration,
            ThreadWorkspaceResolver.Resolve(thread.WorkspacePath, configuration),
            ChannelSessionScope.Current,
            TurnTriggerScope.Current,
            SessionClientCapabilitiesScope.Current?.SupportsCommandExecutionStreaming == true,
            SessionClientCapabilitiesScope.Current?.SupportsToolExecutionLifecycle == true);

        return AdmitTurn(
            runtime,
            turnContext,
            content.ToList(),
            sender,
            messages?.Select(static message => message.Clone()).ToArray(),
            CloneInputSnapshot(inputSnapshot),
            callerCt);
    }

    private void ValidateTurnAdmission(SessionThread thread, ChatMessage[]? messages)
    {
        var threadId = thread.Id;
        if (thread.Status != ThreadStatus.Active)
            throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot submit input.");
        if (thread.Turns.Any(turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            throw new InvalidOperationException($"Thread '{threadId}' already has a running Turn. Wait for it to complete or cancel it first.");
        ThrowIfThreadMaintenanceActive(threadId);
        if (thread.HistoryMode == HistoryMode.Client && messages is not { Length: > 0 })
            throw new InvalidOperationException($"Thread '{threadId}' requires client-managed history, but no messages were provided.");
        if (thread.HistoryMode == HistoryMode.Server && messages is { Length: > 0 })
            throw new InvalidOperationException($"Thread '{threadId}' uses server-managed history and does not accept client-supplied messages.");
    }

    private static SessionInputSnapshot? CloneInputSnapshot(SessionInputSnapshot? source) =>
        source == null
            ? null
            : source with
            {
                NativeInputParts = source.NativeInputParts?.ToArray(),
                MaterializedInputParts = source.MaterializedInputParts?.ToArray()
            };
}
