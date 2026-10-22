using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    internal static readonly TimeSpan ThreadIdleUnloadDelay = TimeSpan.FromMinutes(30);

    public IAsyncDisposable StartIdleThreadUnloading() => new IdleThreadUnloader(this);

    internal async Task<int> UnloadIdleThreadsAsync(DateTimeOffset now, TimeSpan idleFor)
    {
        var unloaded = 0;
        foreach (var runtime in _runtimeRegistry.Values)
        {
            if (!CanUnload(runtime, now, idleFor))
                continue;

            bool removed;
            try
            {
                removed = await runtime.Commands.InvokeAsync(_ =>
                {
                    if (!CanUnload(runtime, now, idleFor) || !_runtimeRegistry.TryRemove(runtime))
                        return Task.FromResult(false);
                    runtime.Commands.Close();
                    return Task.FromResult(true);
                }).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                continue;
            }

            if (!removed)
                continue;
            await runtime.DisposeAsync().ConfigureAwait(false);
            unloaded++;
        }

        return unloaded;
    }

    private bool CanUnload(ThreadRuntime runtime, DateTimeOffset now, TimeSpan idleFor)
    {
        var thread = runtime.Thread;
        if (!runtime.Materialized
            || thread.Ephemeral
            || IsPendingPermanentDeletion(thread.Id)
            || !runtime.Turns.IsEmpty
            || runtime.Maintenance != null
            || runtime.GoalContinuationStarting
            || runtime.Broker.HasSubscribers
            || runtime.GetBindingMcpServers().Count > 0
            || thread.Turns.Any(static turn =>
                turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
        {
            return false;
        }

        var lastActivity = runtime.LastAccessAt > runtime.Broker.LastActivityAt
            ? runtime.LastAccessAt
            : runtime.Broker.LastActivityAt;
        return now - lastActivity >= idleFor;
    }

    private sealed class IdleThreadUnloader : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public IdleThreadUnloader(SessionService owner) => _loop = RunAsync(owner, _stop.Token);

        private static async Task RunAsync(SessionService owner, CancellationToken ct)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            try
            {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        await owner.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow, ThreadIdleUnloadDelay).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        owner.Logger?.LogWarning(ex, "Unloading idle threads failed.");
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            await _loop.ConfigureAwait(false);
            _stop.Dispose();
        }
    }
}
