using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public sealed partial class StreamingFunctionInvokingChatClient
{
    private static async Task<StreamStep> MoveNextOrInterruptAsync(
        IAsyncEnumerator<ChatResponseUpdate> enumerator,
        InstantInterruptWindow? interruptWindow,
        CancellationTokenSource requestCancellation)
    {
        var moveNext = MoveNextCapturingAsync(enumerator);
        if (interruptWindow is { IsOpen: true } && await interruptWindow.InterruptsAsync(moveNext).ConfigureAwait(false))
            await requestCancellation.CancelAsync().ConfigureAwait(false);
        return await moveNext.ConfigureAwait(false);
    }

    private sealed class InstantInterruptWindow : IDisposable
    {
        private readonly CancellationTokenSource _watchCancellation;
        private readonly Task<bool> _triggered;

        private InstantInterruptWindow(Func<CancellationToken, Task> waitAsync, CancellationToken cancellationToken)
        {
            _watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _triggered = WatchAsync(waitAsync, _watchCancellation.Token);
        }

        public bool IsOpen { get; private set; } = true;

        public bool Interrupted { get; private set; }

        public bool HoldsOutput => IsOpen || Interrupted;

        public static InstantInterruptWindow? TryOpen(bool suppressed, CancellationToken cancellationToken) =>
            !suppressed && StreamingGuidanceRuntimeScope.Current?.WaitForInstantInterruptAsync is { } waitAsync
                ? new InstantInterruptWindow(waitAsync, cancellationToken)
                : null;

        public async Task<bool> InterruptsAsync(Task pending)
        {
            if (!IsOpen)
                return false;

            await Task.WhenAny(pending, _triggered).ConfigureAwait(false);
            if (pending.IsCompleted)
                return false;

            Interrupted = await _triggered.ConfigureAwait(false);
            Close();
            return Interrupted;
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            _watchCancellation.Cancel();
        }

        public void Dispose()
        {
            Close();
            _watchCancellation.Dispose();
        }

        private static async Task<bool> WatchAsync(Func<CancellationToken, Task> waitAsync, CancellationToken cancellationToken)
        {
            try
            {
                await waitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
