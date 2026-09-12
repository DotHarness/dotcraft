using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.RemoteTools;

internal sealed class ExecutionSessionTerminalService(BackgroundTerminalService terminals)
    : IBackgroundTerminalService, IAsyncDisposable
{
    private readonly RemoteOperationScope _operations = new();
    private Task? _disposal;
    private readonly object _gate = new();
    internal event Action? Closed;
    public event Action<BackgroundTerminalEvent>? TerminalEvent
    {
        add { terminals.TerminalEvent += value; }
        remove { terminals.TerminalEvent -= value; }
    }

    public Task<BackgroundTerminalSnapshot> StartAsync(BackgroundTerminalStartRequest request, CancellationToken ct = default) =>
        Run(token => terminals.StartAsync(request with { ThreadId = ExecutionSessionTerminals.ThreadId }, token), ct);
    public Task<BackgroundTerminalSnapshot> ReadAsync(string sessionId, int waitMs = 0, int? maxOutputChars = null, CancellationToken ct = default) =>
        Run(token => terminals.ReadAsync(sessionId, waitMs, maxOutputChars, token), ct);
    public Task<BackgroundTerminalSnapshot> WriteStdinAsync(string sessionId, string input, int yieldTimeMs = 1000, int? maxOutputChars = null, CancellationToken ct = default) =>
        Run(token => terminals.WriteStdinAsync(sessionId, input, yieldTimeMs, maxOutputChars, token), ct);
    public Task<IReadOnlyList<BackgroundTerminalSnapshot>> ListAsync(string? threadId = null, CancellationToken ct = default) =>
        Run(token => terminals.ListAsync(threadId, token), ct);
    public Task<BackgroundTerminalSnapshot> StopAsync(string sessionId, CancellationToken ct = default) =>
        Run(token => terminals.StopAsync(sessionId, token), ct);
    public Task<IReadOnlyList<BackgroundTerminalSnapshot>> CleanThreadAsync(string threadId, CancellationToken ct = default) =>
        Run(token => terminals.CleanThreadAsync(threadId, token), ct);
    public Task<IReadOnlyList<string>> DeleteThreadArtifactsAsync(string threadId, CancellationToken ct = default) =>
        Run(token => terminals.DeleteThreadArtifactsAsync(threadId, token), ct);
    public Task<int> CleanupExpiredArtifactsAsync(CancellationToken ct = default) =>
        Run(terminals.CleanupExpiredArtifactsAsync, ct);

    private async Task<T> Run<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var operation = _operations.Enter(ct);
        return await action(operation.Token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _operations.DisposeAsync().ConfigureAwait(false);
        Closed?.Invoke();
        Closed = null;
        await terminals.DisposeAsync().ConfigureAwait(false);
    }
}
