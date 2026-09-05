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
    DateTimeOffset? ConnectedSince);

/// <summary>The tool call currently running for a paired machine.</summary>
public sealed record RemoteToolActivity(
    string PeerId,
    string ToolName,
    string? CommandPreview,
    DateTimeOffset StartedAt);

/// <summary>
/// A pairing invitation parsed from an invitation link, whose <see cref="InviteId"/> is the
/// one-time bearer presented on the first control connection.
/// </summary>
public sealed record RemoteToolInvite(
    string InviteId,
    string InviterDisplayName,
    string Purpose,
    string SuggestedWorkspacePath,
    Uri HubEndpoint,
    DateTimeOffset? ExpiresAt);

/// <summary>An accepted invitation together with the folder the machine owner chose to share.</summary>
public sealed record RemoteToolJoinDecision(RemoteToolInvite Invite, string WorkspacePath);

public sealed class RemoteToolHostRuntimeOptions
{
    /// <summary>DotCraft home directory; defaults to <c>~/.craft</c>.</summary>
    public string? CraftHome { get; set; }

    /// <summary>Name shown to paired machines; defaults to the machine name.</summary>
    public string? DisplayName { get; set; }
}

internal sealed class RemoteToolHostActivityMonitor
{
    private readonly object _gate = new();

    public RemoteToolActivity? Current { get; private set; }

    public event Action<RemoteToolActivity?>? Changed;

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
