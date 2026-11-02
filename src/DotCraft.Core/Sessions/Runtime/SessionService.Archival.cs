namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task WithThreadForArchiveAsync(
        string threadId,
        Func<SessionThread, bool, CancellationToken, Task> archive,
        CancellationToken ct)
    {
        threadId = NormalizeRequiredThreadId(threadId);
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (_runtimeRegistry.IsPendingPermanentDeletion(threadId))
                throw new InvalidOperationException($"Thread '{threadId}' is being permanently deleted.");

            if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            {
                runtime.Touch();
                try
                {
                    await runtime.Commands.InvokeAsync(commandCt => archive(runtime.Thread, true, commandCt), ct);
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
                if (_runtimeRegistry.IsPendingPermanentDeletion(threadId))
                    throw new InvalidOperationException($"Thread '{threadId}' is being permanently deleted.");
                if (_runtimeRegistry.TryGetRuntime(threadId, out _))
                    continue;

                var thread = (await persistence.ReadThreadSnapshotAsync(threadId, ct)).Thread;
                await archive(thread, false, ct);
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
}
