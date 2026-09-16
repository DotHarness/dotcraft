namespace DotCraft.Oratorio.Integrations;

internal enum AppServerRunTerminationReason
{
    None,
    TimedOut,
    Stalled
}

internal sealed class ActiveRunExecution(CancellationToken stoppingToken, TimeSpan timeout) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = CreateCancellation(stoppingToken, timeout);
    private int _terminationReason;

    public CancellationToken Token => _cancellation.Token;
    public Task Completion { get; set; } = Task.CompletedTask;
    public AppServerRunTerminationReason TerminationReason =>
        (AppServerRunTerminationReason)Volatile.Read(ref _terminationReason);

    public bool TryRequestStalledTermination()
    {
        if (Interlocked.CompareExchange(
                ref _terminationReason,
                (int)AppServerRunTerminationReason.Stalled,
                (int)AppServerRunTerminationReason.None) != (int)AppServerRunTerminationReason.None)
        {
            return false;
        }

        _cancellation.Cancel();
        return true;
    }

    public void Dispose() => _cancellation.Dispose();

    private static CancellationTokenSource CreateCancellation(CancellationToken stoppingToken, TimeSpan timeout)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cancellation.CancelAfter(timeout);
        return cancellation;
    }
}
