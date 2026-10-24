using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class WorktreeOperationNotifier(AppServerResponseWriter responseWriter, string? operationId)
{
    private readonly object _gate = new();
    private Task _pending = Task.CompletedTask;

    public Action<string, string>? Progress => operationId is { } id
        ? (step, status) => Enqueue(() => responseWriter.NotifyAsync(
            Contract.AppServerRpc.WorktreeCreateProgress,
            new Contract.WorktreeCreateProgressNotification { OperationId = id, Step = step, Status = status },
            CancellationToken.None))
        : null;

    public Action<string>? Output => operationId is { } id
        ? delta => Enqueue(() => responseWriter.NotifyAsync(
            Contract.AppServerRpc.WorktreeSetupOutput,
            new Contract.WorktreeSetupOutputNotification { OperationId = id, Delta = delta },
            CancellationToken.None))
        : null;

    public Task DrainAsync()
    {
        lock (_gate)
            return _pending;
    }

    private void Enqueue(Func<Task> send)
    {
        lock (_gate)
            _pending = _pending.ContinueWith(_ => send(), TaskScheduler.Default).Unwrap();
    }
}
