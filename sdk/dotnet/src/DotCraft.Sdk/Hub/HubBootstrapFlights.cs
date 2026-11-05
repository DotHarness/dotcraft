namespace DotCraft.Sdk.Hub;

internal static class HubBootstrapFlights
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Selection, Flight> Pending = [];

    internal sealed record Selection(
        string LockPath,
        string Executable,
        string? ExpectedExecutable,
        HubBinaryMatchPolicy BinaryPolicy,
        bool StartIfMissing,
        TimeSpan StartupTimeout,
        TimeSpan ShutdownTimeout);

    public static async Task<HubLockInfo> RunAsync(
        Selection selection,
        Func<CancellationToken, Task<HubLockInfo>> start,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Flight flight;
        var created = false;
        lock (Gate)
        {
            if (!Pending.TryGetValue(selection, out flight!))
            {
                flight = new Flight();
                Pending.Add(selection, flight);
                created = true;
            }
            flight.Waiters++;
        }
        if (created)
            _ = CompleteAsync(selection, flight, start);

        try
        {
            return await flight.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (Gate)
            {
                flight.Waiters--;
                if (flight.Waiters == 0 && !flight.Finished)
                {
                    if (Pending.TryGetValue(selection, out var current) && ReferenceEquals(current, flight))
                        Pending.Remove(selection);
                    flight.Cancellation.Cancel();
                }
            }
        }
    }

    private static async Task CompleteAsync(
        Selection selection,
        Flight flight,
        Func<CancellationToken, Task<HubLockInfo>> start)
    {
        try
        {
            flight.Completion.TrySetResult(await start(flight.Cancellation.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException error)
        {
            flight.Completion.TrySetCanceled(error.CancellationToken);
        }
        catch (Exception error)
        {
            flight.Completion.TrySetException(error);
        }
        finally
        {
            lock (Gate)
            {
                flight.Finished = true;
                if (Pending.TryGetValue(selection, out var current) && ReferenceEquals(current, flight))
                    Pending.Remove(selection);
                flight.Cancellation.Dispose();
            }
        }
    }

    private sealed class Flight
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<HubLockInfo> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Waiters { get; set; }
        public bool Finished { get; set; }
    }
}
