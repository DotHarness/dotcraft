using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.RemoteTools;

internal sealed class ExecutionSessionTerminals : IBackgroundTerminalService
{
    private static readonly AsyncLocal<Context?> Active = new();
    private readonly Dictionary<ExecutionSessionTerminalService, Action<BackgroundTerminalEvent>?> _observers = [];
    private static ExecutionSessionTerminalService Current => Active.Value?.Terminals
        ?? throw new InvalidOperationException("Terminal access requires an active remote execution session.");

    internal static string ThreadId => Active.Value?.ThreadId
        ?? throw new InvalidOperationException("Terminal creation requires an active execution Thread.");

    internal static IDisposable Enter(ExecutionSessionTerminalService terminals, string threadId)
    {
        var previous = Active.Value;
        Active.Value = new(terminals, threadId);
        return new Scope(() => Active.Value = previous);
    }

    public event Action<BackgroundTerminalEvent>? TerminalEvent
    {
        add
        {
            if (value is null) return;
            var owner = Current;
            lock (_observers)
            {
                if (!_observers.ContainsKey(owner))
                {
                    _observers.Add(owner, null);
                    owner.Closed += () => Detach(owner);
                }
                _observers[owner] += value;
                owner.TerminalEvent += value;
            }
        }
        remove
        {
            if (value is null) return;
            lock (_observers)
                foreach (var owner in _observers.Keys.ToArray())
                {
                    _observers[owner] -= value;
                    owner.TerminalEvent -= value;
                }
        }
    }

    private void Detach(ExecutionSessionTerminalService owner)
    {
        lock (_observers)
            if (_observers.Remove(owner, out var handlers)) owner.TerminalEvent -= handlers;
    }

    public Task<BackgroundTerminalSnapshot> StartAsync(BackgroundTerminalStartRequest request, CancellationToken ct = default) =>
        Current.StartAsync(request, ct);
    public Task<BackgroundTerminalSnapshot> ReadAsync(string sessionId, int waitMs = 0, int? maxOutputChars = null, CancellationToken ct = default) =>
        Current.ReadAsync(sessionId, waitMs, maxOutputChars, ct);
    public Task<BackgroundTerminalSnapshot> WriteStdinAsync(string sessionId, string input, int yieldTimeMs = 1000, int? maxOutputChars = null, CancellationToken ct = default) =>
        Current.WriteStdinAsync(sessionId, input, yieldTimeMs, maxOutputChars, ct);
    public Task<IReadOnlyList<BackgroundTerminalSnapshot>> ListAsync(string? threadId = null, CancellationToken ct = default) =>
        Current.ListAsync(threadId, ct);
    public Task<BackgroundTerminalSnapshot> StopAsync(string sessionId, CancellationToken ct = default) =>
        Current.StopAsync(sessionId, ct);
    public Task<IReadOnlyList<BackgroundTerminalSnapshot>> CleanThreadAsync(string threadId, CancellationToken ct = default) =>
        Current.CleanThreadAsync(threadId, ct);
    public Task<IReadOnlyList<string>> DeleteThreadArtifactsAsync(string threadId, CancellationToken ct = default) =>
        Current.DeleteThreadArtifactsAsync(threadId, ct);
    public Task<int> CleanupExpiredArtifactsAsync(CancellationToken ct = default) => Current.CleanupExpiredArtifactsAsync(ct);

    private sealed record Context(ExecutionSessionTerminalService Terminals, string ThreadId);

    private sealed class Scope(Action leave) : IDisposable
    {
        public void Dispose() => leave();
    }
}
