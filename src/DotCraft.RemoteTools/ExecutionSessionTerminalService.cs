using System.Text.RegularExpressions;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.RemoteTools;

internal sealed class ExecutionSessionTerminalService : IBackgroundTerminalService, IAsyncDisposable
{
    private readonly BackgroundTerminalService _terminals;
    private readonly RemoteOperationScope _operations = new();
    private Task? _disposal;
    private readonly object _gate = new();

    public ExecutionSessionTerminalService(BackgroundTerminalService terminals)
    {
        _terminals = terminals;
        _terminals.TerminalEvent += Forward;
    }

    internal event Action? Closed;

    internal event Action<BackgroundTerminalEvent>? Reported;

    public event Action<BackgroundTerminalEvent>? TerminalEvent
    {
        add { _terminals.TerminalEvent += value; }
        remove { _terminals.TerminalEvent -= value; }
    }

    // Every terminal is armed because a model on the Agent is always waiting on its Exec.
    public Task<BackgroundTerminalSnapshot> StartAsync(BackgroundTerminalStartRequest request, CancellationToken ct = default) =>
        Run(async token =>
        {
            var snapshot = await _terminals.StartAsync(
                request with { ThreadId = ExecutionSessionTerminals.ThreadId, NotifyOnCompletion = true },
                token).ConfigureAwait(false);
            ExecutionSessionTerminals.RecordStarted(snapshot);
            return snapshot;
        }, ct);
    public Task<BackgroundTerminalSnapshot> ReadAsync(string sessionId, int waitMs = 0, int? maxOutputChars = null, CancellationToken ct = default) =>
        Run(token => _terminals.ReadAsync(sessionId, waitMs, maxOutputChars, token), ct);
    public Task<BackgroundTerminalSnapshot> WriteStdinAsync(string sessionId, string input, int yieldTimeMs = 1000, int? maxOutputChars = null, CancellationToken ct = default) =>
        Run(token => _terminals.WriteStdinAsync(sessionId, input, yieldTimeMs, maxOutputChars, token), ct);
    public Task<BackgroundTerminalSnapshot> InteractAsync(string sessionId, string input, int waitMs, int? maxOutputChars = null, Regex? waitFor = null, CancellationToken ct = default) =>
        Run(token => _terminals.InteractAsync(sessionId, input, waitMs, maxOutputChars, waitFor, token), ct);
    public ShellStdinSession? GetStdinSession(string sessionId) => _terminals.GetStdinSession(sessionId);
    public Task<IReadOnlyList<BackgroundTerminalSnapshot>> ListAsync(string? threadId = null, CancellationToken ct = default) =>
        Run(token => _terminals.ListAsync(threadId, token), ct);
    public Task<BackgroundTerminalSnapshot> StopAsync(string sessionId, CancellationToken ct = default) =>
        Run(token => _terminals.StopAsync(sessionId, token), ct);
    public Task<IReadOnlyList<BackgroundTerminalSnapshot>> CleanThreadAsync(string threadId, CancellationToken ct = default) =>
        Run(token => _terminals.CleanThreadAsync(threadId, token), ct);
    public Task<IReadOnlyList<string>> DeleteThreadArtifactsAsync(string threadId, CancellationToken ct = default) =>
        Run(token => _terminals.DeleteThreadArtifactsAsync(threadId, token), ct);
    public Task<int> CleanupExpiredArtifactsAsync(CancellationToken ct = default) =>
        Run(_terminals.CleanupExpiredArtifactsAsync, ct);

    private async Task<T> Run<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var operation = _operations.Enter(ct);
        return await action(operation.Token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_disposal ??= DisposeCoreAsync());
    }

    private void Forward(BackgroundTerminalEvent evt)
    {
        if (evt.EventType is BackgroundTerminalEventTypes.Completed
            or BackgroundTerminalEventTypes.CompletionNotice
            or BackgroundTerminalEventTypes.StallNotice)
            Reported?.Invoke(evt);
    }

    private async Task DisposeCoreAsync()
    {
        _terminals.TerminalEvent -= Forward;
        await _operations.DisposeAsync().ConfigureAwait(false);
        Closed?.Invoke();
        Closed = null;
        await _terminals.DisposeAsync().ConfigureAwait(false);
    }
}
