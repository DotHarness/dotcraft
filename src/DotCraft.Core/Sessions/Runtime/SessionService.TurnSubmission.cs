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
            throw new KeyNotFoundException($"Thread '{threadId}' not found. Call CreateThreadAsync or ResumeThreadAsync first.");
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            throw new InvalidOperationException($"Thread '{threadId}' has no active runtime.");

        var capturedContent = content.ToList();
        var capturedMessages = messages?.Select(static message => message.Clone()).ToArray();
        var capturedInputSnapshot = CloneInputSnapshot(inputSnapshot);

        // Admission is a short, serialized Thread transition. The returned Turn continues
        // independently, so the command pump remains available for cancel/approval/queue work.
        var channel = runtime.Commands.InvokeAsync(
            _ => Task.FromResult(StartTurn(
                runtime,
                capturedContent,
                sender,
                capturedMessages,
                capturedInputSnapshot,
                ct)),
            ct).GetAwaiter().GetResult();
        return channel.ReadAllAsync(ct);
    }

    private SessionEventChannel StartTurn(
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

    private static SessionInputSnapshot? CloneInputSnapshot(SessionInputSnapshot? source) =>
        source == null
            ? null
            : source with
            {
                NativeInputParts = source.NativeInputParts?.ToArray(),
                MaterializedInputParts = source.MaterializedInputParts?.ToArray()
            };
}
