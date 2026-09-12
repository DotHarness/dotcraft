using DotCraft.Configuration;

namespace DotCraft.RemoteTools;

/// <summary>Owns remote execution sessions and their shared workspace lease identity for one process.</summary>
public sealed class RemoteExecutionClient(AppConfig? config = null) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RemoteExecutionSession> _sessions = new(StringComparer.Ordinal);
    private readonly RemoteOperationScope _opening = new();
    private readonly string _ownerId = "agent_" + Guid.NewGuid().ToString("N");
    private Task? _disposal;

    /// <summary>Takes ownership of an authenticated data connection; opening does not discover or start a broker.</summary>
    public async ValueTask<RemoteExecutionSession> OpenSessionAsync(string executionSessionId, string hostId,
        string workspaceId, RemoteToolHostConnection connection, CancellationToken cancellationToken = default)
    {
        RemoteOperationScope.Call operation;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executionSessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
            ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
            operation = _opening.Enter(cancellationToken);
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
        using var opening = operation;
        var session = await RemoteExecutionSession.CreateAsync(executionSessionId, hostId, workspaceId,
            connection, _ownerId, config ?? new AppConfig(), Remove, operation.Token).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                operation.Token.ThrowIfCancellationRequested();
                _sessions.Add(executionSessionId, session);
            }
            return session;
        }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private void Remove(RemoteExecutionSession session)
    {
        lock (_gate)
            if (_sessions.TryGetValue(session.Id, out var current) && ReferenceEquals(current, session))
                _sessions.Remove(session.Id);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _opening.DisposeAsync().ConfigureAwait(false);
        RemoteExecutionSession[] sessions;
        lock (_gate) sessions = _sessions.Values.ToArray();
        await Task.WhenAll(sessions.Select(async session => await session.DisposeAsync().ConfigureAwait(false)))
            .ConfigureAwait(false);
    }
}
