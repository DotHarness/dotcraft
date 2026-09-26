using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal interface ISubAgentInitialInputService
{
    IAsyncEnumerable<SessionEvent> SubmitSubAgentInitialInput(string childThreadId, IList<AIContent> content, CancellationToken ct);
}

internal interface ISubAgentStartupLifecycleService
{
    Task PersistPreparedSubAgentAsync(string parentThreadId, string childThreadId, CancellationToken ct);
    Task DiscardFailedSubAgentAsync(string parentThreadId, string childThreadId, CancellationToken ct);
}

internal sealed class SubAgentStartupScope(
    ISessionService sessionService,
    string parentThreadId,
    string childThreadId,
    Func<SessionThread, CancellationToken, Task>? releasePreparation) : IAsyncDisposable
{
    public TaskCompletionSource Admission { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public SessionThread? Child { get; set; }
    public CancellationTokenSource? Cancellation { get; set; }
    public Task? Completion { get; set; }
    public Exception? Failure { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (Admission.Task.IsCompletedSuccessfully) return;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            if (Cancellation != null) await Cancellation.CancelAsync().WaitAsync(cleanup.Token).ConfigureAwait(false);
            if (Completion != null)
            {
                try { await Completion.WaitAsync(cleanup.Token).ConfigureAwait(false); }
                catch (Exception) when (Completion.IsCompleted) { }
            }
            if (Admission.Task.IsCompletedSuccessfully) return;
            if (Child != null && releasePreparation != null)
            {
                try { await releasePreparation(Child, cleanup.Token).WaitAsync(cleanup.Token).ConfigureAwait(false); }
                catch (Exception ex) { ReportCleanupFailure(ex); }
            }
            if (sessionService is not ISubAgentStartupLifecycleService lifecycle)
                throw new InvalidOperationException("Session service does not support failed subagent startup cleanup.");
            await lifecycle.DiscardFailedSubAgentAsync(parentThreadId, childThreadId, cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ReportCleanupFailure(ex);
        }
        finally
        {
            Cancellation?.Dispose();
        }
    }

    private void ReportCleanupFailure(Exception error)
    {
        if (Failure != null)
        {
            const string key = "SubAgentStartupCleanup";
            Failure.Data[key] = Failure.Data[key] is { } previous
                ? $"{previous}{Environment.NewLine}{error}"
                : error.ToString();
        }
        Trace.TraceError("Failed to compensate subagent startup {0}: {1}", childThreadId, error);
    }
}
