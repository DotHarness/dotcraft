using DotCraft.Sessions;

namespace DotCraft.RemoteTools;

/// <summary>Lifecycle state of the local Remote Tool Host, in priority order.</summary>
public enum RemoteToolHostStatus
{
    /// <summary>No pairing is reachable, or the host is not running.</summary>
    Offline,

    /// <summary>Connected to at least one Hub and idle.</summary>
    Standby,

    /// <summary>A remote workspace is currently leased.</summary>
    Connected,

    /// <summary>Connected, but sharing is paused by the machine owner.</summary>
    Paused
}

/// <summary>
/// One machine this Remote Tool Host shares a workspace with; Host workspace records and Host
/// policy, not <see cref="WorkspaceId"/>, decide what a paired Agent may reach.
/// </summary>
public sealed record RemoteToolPeer(
    string PeerId,
    string DisplayName,
    string WorkspaceId,
    string WorkspacePath,
    DateTimeOffset JoinedAt,
    DateTimeOffset? ConnectedSince,
    string? AuthorizationMode = null,
    int ScreenViewers = 0);

/// <summary>The tool call currently running for a paired machine.</summary>
public sealed record RemoteToolActivity(
    string PeerId,
    string ToolName,
    string? CommandPreview,
    DateTimeOffset StartedAt);

/// <summary>
/// The latest Turn a paired machine's Agent reported for one of its Threads routed here, under this
/// Host's session-scoped Thread id, with the tool calls it has run here so far.
/// </summary>
public sealed record RemoteToolTurn(
    string PeerId,
    string ThreadId,
    string TurnId,
    TurnStatus Status,
    int ToolCalls,
    DateTimeOffset Since);

/// <summary>
/// A pairing invitation parsed from an invitation link, whose <see cref="InviteId"/> is the
/// one-time bearer presented on the first control connection.
/// </summary>
public sealed record RemoteToolInvite(
    string InviteId,
    string InviterDisplayName,
    Uri HubEndpoint,
    DateTimeOffset? ExpiresAt);

/// <summary>An accepted invitation together with the folder the machine owner chose to share.</summary>
public sealed record RemoteToolJoinDecision(
    RemoteToolInvite Invite, string WorkspacePath,
    string AuthorizationMode = RemoteToolAuthorization.WorkspacePreferred,
    bool CreateWorkspace = false);

public sealed class RemoteToolHostRuntimeOptions
{
    /// <summary>DotCraft home directory; defaults to <c>~/.craft</c>.</summary>
    public string? CraftHome { get; set; }

    /// <summary>Local owner approval UI. Absent presenters decline requests.</summary>
    public IRemoteToolApprovalPresenter? ApprovalPresenter { get; set; }

    /// <summary>Name shown to paired machines; defaults to the machine name.</summary>
    public string? DisplayName { get; set; }
}

internal enum RemoteToolHostDiagnosticLevel
{
    Information,
    Warning,
    Error
}

internal sealed record RemoteToolHostDiagnostic(
    RemoteToolHostDiagnosticLevel Level,
    string EventName,
    string Message,
    Exception? Exception = null);

internal sealed class RemoteToolHostActivityMonitor
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string SessionId, RemoteToolTurn Turn)> _turns = new(StringComparer.Ordinal);
    // Counted apart from the reports, because a call can reach the Host before its Turn's report does.
    private readonly Dictionary<string, (string SessionId, string TurnId, int Count)> _calls = new(StringComparer.Ordinal);

    public RemoteToolActivity? Current { get; private set; }

    public IReadOnlyList<RemoteToolTurn> Turns
    {
        get
        {
            lock (_gate)
                return [.. _turns.Values.Select(entry => entry.Turn)];
        }
    }

    public event Action<RemoteToolActivity?>? Changed;

    public event Action? TurnsChanged;

    public void ReportTurn(string sessionId, string peerId, string threadId, string turnId, TurnStatus status)
    {
        lock (_gate)
        {
            var calls = _calls.TryGetValue(threadId, out var counted) && counted.TurnId == turnId ? counted.Count : 0;
            _turns[threadId] = (sessionId, new(peerId, threadId, turnId, status, calls, DateTimeOffset.UtcNow));
        }
        TurnsChanged?.Invoke();
    }

    public void CountCall(string sessionId, string threadId, string? turnId)
    {
        if (turnId is null) return;
        lock (_gate)
        {
            var count = _calls.TryGetValue(threadId, out var counted) && counted.TurnId == turnId ? counted.Count + 1 : 1;
            _calls[threadId] = (sessionId, turnId, count);
            if (!_turns.TryGetValue(threadId, out var known) || known.Turn.TurnId != turnId) return;
            _turns[threadId] = known with { Turn = known.Turn with { ToolCalls = count } };
        }
        TurnsChanged?.Invoke();
    }

    public void ForgetThread(string threadId) => Forget((key, _) => key == threadId);

    public void ForgetSession(string sessionId) => Forget((_, owner) => owner == sessionId);

    private void Forget(Func<string, string, bool> match)
    {
        lock (_gate)
        {
            foreach (var threadId in _calls.Where(pair => match(pair.Key, pair.Value.SessionId)).Select(pair => pair.Key).ToArray())
                _calls.Remove(threadId);
            var gone = _turns.Where(pair => match(pair.Key, pair.Value.SessionId)).Select(pair => pair.Key).ToArray();
            if (gone.Length == 0) return;
            foreach (var threadId in gone) _turns.Remove(threadId);
        }
        TurnsChanged?.Invoke();
    }

    public IDisposable Begin(string peerId, string toolName, string? commandPreview)
    {
        var activity = new RemoteToolActivity(peerId, toolName, commandPreview, DateTimeOffset.UtcNow);
        lock (_gate)
            Current = activity;
        Changed?.Invoke(activity);
        return new Scope(this, activity);
    }

    private void End(RemoteToolActivity activity)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(Current, activity))
                return;
            Current = null;
        }
        Changed?.Invoke(null);
    }

    private sealed class Scope(RemoteToolHostActivityMonitor monitor, RemoteToolActivity activity) : IDisposable
    {
        public void Dispose() => monitor.End(activity);
    }
}
