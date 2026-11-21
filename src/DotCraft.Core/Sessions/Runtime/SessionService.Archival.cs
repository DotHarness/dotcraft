namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private Task WithThreadForArchiveAsync(
        string threadId,
        Func<SessionThread, bool, CancellationToken, Task> archive,
        CancellationToken ct) =>
        WithExistingThreadAsync(threadId, (thread, loaded, commandCt) =>
            archive(thread ?? throw new ThreadNotFoundException(threadId), loaded, commandCt), ct);

    private async Task WithExistingThreadAsync(
        string threadId,
        Func<SessionThread?, bool, CancellationToken, Task> action,
        CancellationToken ct,
        bool forDeletion = false)
    {
        threadId = NormalizeRequiredThreadId(threadId);
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfPendingPermanentDeletion(threadId);

            if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            {
                runtime.Touch();
                try
                {
                    await runtime.Commands.InvokeAsync(commandCt =>
                    {
                        ThrowIfPendingPermanentDeletion(threadId);
                        return action(runtime.Thread, true, commandCt);
                    }, ct);
                    return;
                }
                catch (ObjectDisposedException) when (attempt == 0 && runtime.Commands.IsClosed)
                {
                    continue;
                }
            }

            var loadGate = AddThreadLoadGateReference(threadId);
            var gateAcquired = false;
            try
            {
                await loadGate.Semaphore.WaitAsync(ct);
                gateAcquired = true;
                ThrowIfPendingPermanentDeletion(threadId);
                if (_runtimeRegistry.TryGetRuntime(threadId, out _))
                    continue;

                var thread = await persistence.ReadThreadLifecycleMetadataAsync(threadId, ct, forDeletion);
                await action(thread, false, ct);
                return;
            }
            finally
            {
                if (gateAcquired)
                    loadGate.Semaphore.Release();
                ReleaseThreadLoadGateReference(threadId, loadGate);
            }
        }
    }

    private void ThrowIfPendingPermanentDeletion(string threadId)
    {
        if (_runtimeRegistry.IsPendingPermanentDeletion(threadId))
            throw new InvalidOperationException($"Thread '{threadId}' is being permanently deleted.");
    }
}
