using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

/// <summary>Hosting entry point for a Remote Tool Host, shared by the CLI verbs and the tray client.</summary>
public sealed partial class RemoteToolHostRuntime : IAsyncDisposable
{
    private const string DeepLinkHost = "satellite";
    private readonly RemoteToolHostStorage _storage;
    private readonly RemoteToolHostActivityMonitor _activity = new();
    private readonly string _displayName;
    private readonly TimeSpan? _heartbeatInterval;
    private readonly object _gate = new();
    private RemoteToolHostOutboundHost? _host;
    private CancellationTokenSource? _running;
    private Task? _runTask;
    private RemoteToolHostStatus _status = RemoteToolHostStatus.Offline;
    private IReadOnlyList<RemoteToolPeer> _peers = [];

    internal RemoteToolHostRuntime(
        RemoteToolHostStorage storage,
        string displayName,
        TimeSpan? heartbeatInterval = null)
    {
        _storage = storage;
        _displayName = displayName;
        _heartbeatInterval = heartbeatInterval;
        _activity.Changed += activity =>
        {
            ActivityChanged?.Invoke(this, activity);
            Refresh();
        };
        _peers = ReadPeers();
    }

    /// <summary>Creates a runtime bound to one DotCraft home directory.</summary>
    public static RemoteToolHostRuntime Create(RemoteToolHostRuntimeOptions? options = null)
    {
        var storage = new RemoteToolHostStorage(options?.CraftHome);
        var displayName = string.IsNullOrWhiteSpace(options?.DisplayName)
            ? Environment.MachineName
            : options.DisplayName.Trim();
        return new RemoteToolHostRuntime(storage, displayName);
    }

    /// <summary>Current lifecycle state.</summary>
    public RemoteToolHostStatus Status
    {
        get
        {
            lock (_gate)
                return _status;
        }
    }

    /// <summary>Machines this Host is paired with.</summary>
    public IReadOnlyList<RemoteToolPeer> Peers
    {
        get
        {
            lock (_gate)
                return _peers;
        }
    }

    /// <summary>The tool call currently running for a paired machine, if any.</summary>
    public RemoteToolActivity? CurrentActivity => _activity.Current;

    public event EventHandler<RemoteToolHostStatus>? StatusChanged;

    /// <summary>Raised when a pairing's control connection is established.</summary>
    public event EventHandler<RemoteToolPeer>? PeerConnected;

    /// <summary>Raised when a pairing's control connection ends.</summary>
    public event EventHandler<RemoteToolPeer>? PeerDisconnected;

    public event EventHandler<RemoteToolActivity?>? ActivityChanged;

    /// <summary>
    /// Takes the machine-wide serve lock and connects every pairing, raising setup and pairing
    /// problems before the returned task starts and completing that task only when the host stops.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_runTask is not null)
                return _runTask;
            var host = new RemoteToolHostOutboundHost(_storage, _activity, _heartbeatInterval);
            host.Prepare();
            host.Changed += Refresh;
            _host = host;
            _running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _running.Token;
            return _runTask = Task.Run(async () =>
            {
                try
                {
                    await host.RunAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    Refresh();
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>Stops the outbound host and closes every control connection.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? running;
        Task? runTask;
        RemoteToolHostOutboundHost? host;
        lock (_gate)
        {
            running = _running;
            runTask = _runTask;
            host = _host;
            _running = null;
            _runTask = null;
            _host = null;
        }
        if (running is not null)
            await running.CancelAsync().ConfigureAwait(false);
        if (runTask is not null)
        {
            try { await runTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception) { }
        }
        running?.Dispose();
        if (host is not null)
            await host.DisposeAsync().ConfigureAwait(false);
        Refresh();
    }

    /// <summary>
    /// Reads an invitation link without any network call or write, so a client may show the
    /// invitation before the machine owner decides.
    /// </summary>
    public static RemoteToolInvite ParseInvite(string inviteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteUrl);
        var url = inviteUrl.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out var deepLink)
            && string.Equals(deepLink.Scheme, "dotcraft", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(deepLink.Host, DeepLinkHost, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("The link is not a Remote Tool Host invitation.");
            var parameter = deepLink.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .FirstOrDefault(parts => parts.Length == 2
                                         && string.Equals(parts[0], "invite", StringComparison.Ordinal));
            if (parameter is null)
                throw new FormatException("The link carries no invitation.");
            url = Uri.UnescapeDataString(parameter[1]);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var invite)
            || (invite.Scheme != Uri.UriSchemeHttp && invite.Scheme != Uri.UriSchemeHttps))
        {
            throw new FormatException("An invitation link must be an absolute HTTP URL.");
        }

        var segments = invite.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2
            || !string.Equals(segments[0], "i", StringComparison.Ordinal)
            || !InviteIdPattern().IsMatch(segments[1]))
        {
            throw new FormatException("An invitation link must have the form http://host:port/i/<id>.");
        }

        return new RemoteToolInvite(
            segments[1],
            invite.Host,
            string.Empty,
            string.Empty,
            new Uri($"{invite.Scheme}://{invite.Authority}"),
            null);
    }

    /// <summary>
    /// Enriches the invitation from the Hub that issued it without consuming it or writing
    /// anything, returning the invitation as parsed when the Hub does not answer.
    /// </summary>
    public static async Task<RemoteToolInvite> ResolveInviteAsync(
        RemoteToolInvite parsed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            var details = await http.GetFromJsonAsync<InviteDetails>(
                new Uri(parsed.HubEndpoint, SatelliteWire.InvitePathPrefix + parsed.InviteId),
                RemoteToolHostProtocol.JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (details is null)
                return parsed;
            return parsed with
            {
                InviterDisplayName = details.InviterDisplayName ?? parsed.InviterDisplayName,
                Purpose = details.Purpose ?? parsed.Purpose,
                SuggestedWorkspacePath = details.SuggestedFolder ?? parsed.SuggestedWorkspacePath,
                ExpiresAt = details.ExpiresAt ?? parsed.ExpiresAt
            };
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or TaskCanceledException
                                       or JsonException
                                       or NotSupportedException)
        {
            return parsed;
        }
    }

    /// <summary>
    /// Performs the pairing handshake and stores the peer record plus its credential, and may only
    /// be called after the machine owner accepted the invitation.
    /// </summary>
    public async Task<RemoteToolPeer> AcceptInviteAsync(
        RemoteToolJoinDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var workspacePath = CanonicalizeWorkspace(decision.WorkspacePath);
        var workspaceId = EnsureHostState(workspacePath);
        var hub = decision.Invite.HubEndpoint;

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + decision.Invite.InviteId);
        await socket.ConnectAsync(
            new Uri($"ws://{hub.Authority}{SatelliteWire.ControlPath}", UriKind.Absolute),
            cancellationToken).ConfigureAwait(false);
        await SatelliteWire.SendAsync(
            socket,
            new SatelliteFrame
            {
                Kind = SatelliteWire.Hello,
                DisplayName = _displayName,
                MachineName = Environment.MachineName,
                OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                UserName = Environment.UserName,
                BuildVersion = RemoteToolHostProtocol.BuildVersion,
                Workspaces = [new SatelliteWorkspaceInfo(workspaceId, workspacePath, false)]
            },
            cancellationToken).ConfigureAwait(false);

        var welcome = await SatelliteWire.ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
        if (welcome is null
            || !string.Equals(welcome.Kind, SatelliteWire.Welcome, StringComparison.Ordinal)
            || string.IsNullOrEmpty(welcome.PeerId)
            || string.IsNullOrEmpty(welcome.Credential))
        {
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.InviteInvalid,
                "The invitation was refused. Ask for a new invitation link.");
        }

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "joined", cancellationToken)
            .ConfigureAwait(false);

        var stored = _storage.AddPeer(
            new RemoteToolHubPeer
            {
                PeerId = welcome.PeerId,
                HubHost = hub.Host,
                HubPort = hub.Port,
                CredentialReference = RemoteToolHostStorage.PeerCredentialReference(welcome.PeerId),
                HubLabel = welcome.HubLabel ?? decision.Invite.InviterDisplayName,
                WorkspaceId = workspaceId,
                PairedAt = DateTimeOffset.UtcNow
            },
            welcome.Credential);
        Refresh();
        return ToPeer(stored, connectedSince: null);
    }

    /// <summary>Closes one pairing's control connection without removing the pairing.</summary>
    public Task DisconnectAsync(string peerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        foreach (var connector in _host?.Connectors ?? [])
        {
            if (string.Equals(connector.PeerId, peerId, StringComparison.Ordinal))
                connector.Stop();
        }
        Refresh();
        return Task.CompletedTask;
    }

    /// <summary>Removes one pairing and its stored credential on this machine.</summary>
    public Task RevokeAsync(string peerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peerId);
        _storage.RemovePeer(peerId);
        return DisconnectAsync(peerId);
    }

    /// <summary>
    /// Pauses or resumes sharing; while paused the Host stays reachable but offers no workspace and
    /// refuses new sessions, and leases already granted expire normally.
    /// </summary>
    public Task SetSharingPausedAsync(bool paused)
    {
        if (_host is not null)
            _host.Paused = paused;
        Refresh();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(StopAsync());

    private void Refresh()
    {
        var host = _host;
        var connectors = host?.Connectors ?? [];
        var connected = connectors
            .Where(connector => connector.IsConnected)
            .ToDictionary(connector => connector.PeerId, StringComparer.Ordinal);
        var peers = ReadPeers(connected);
        var status = host is null || connected.Count == 0
            ? RemoteToolHostStatus.Offline
            : host.Paused
                ? RemoteToolHostStatus.Paused
                : host.Leases.HasActiveLease
                    ? RemoteToolHostStatus.Connected
                    : RemoteToolHostStatus.Standby;

        RemoteToolHostStatus previousStatus;
        IReadOnlyList<RemoteToolPeer> previousPeers;
        lock (_gate)
        {
            previousStatus = _status;
            previousPeers = _peers;
            _status = status;
            _peers = peers;
        }

        foreach (var peer in peers.Where(peer => peer.ConnectedSince is not null))
        {
            if (!previousPeers.Any(item =>
                    string.Equals(item.PeerId, peer.PeerId, StringComparison.Ordinal)
                    && item.ConnectedSince is not null))
            {
                PeerConnected?.Invoke(this, peer);
            }
        }
        foreach (var peer in previousPeers.Where(peer => peer.ConnectedSince is not null))
        {
            if (!peers.Any(item =>
                    string.Equals(item.PeerId, peer.PeerId, StringComparison.Ordinal)
                    && item.ConnectedSince is not null))
            {
                PeerDisconnected?.Invoke(this, peer);
            }
        }
        if (previousStatus != status)
            StatusChanged?.Invoke(this, status);
    }

    private IReadOnlyList<RemoteToolPeer> ReadPeers(
        IReadOnlyDictionary<string, RemoteToolHostPeerConnector>? connected = null)
    {
        var state = _storage.LoadHostState();
        if (state is null)
            return [];
        return
        [
            .. state.Peers.Select(peer => ToPeer(
                peer,
                connected is not null && connected.TryGetValue(peer.PeerId, out var connector)
                    ? connector.ConnectedSince
                    : null,
                state))
        ];
    }

    private RemoteToolPeer ToPeer(
        RemoteToolHubPeer peer,
        DateTimeOffset? connectedSince,
        RemoteToolHostState? state = null)
    {
        var workspaces = (state ?? _storage.LoadHostState())?.Workspaces;
        var workspaceId = peer.WorkspaceId;
        var workspacePath = workspaces is not null && workspaces.TryGetValue(workspaceId, out var path)
            ? path
            : string.Empty;
        return new RemoteToolPeer(
            peer.PeerId,
            string.IsNullOrEmpty(peer.HubLabel) ? peer.HubHost : peer.HubLabel,
            workspaceId,
            workspacePath,
            peer.PairedAt,
            connectedSince);
    }

    private string EnsureHostState(string workspacePath)
    {
        var state = _storage.LoadHostState() ?? new RemoteToolHostState
        {
            HostId = "rth_" + Guid.NewGuid().ToString("N"),
            DisplayName = _displayName
        };
        var existing = state.Workspaces.FirstOrDefault(pair => PathsEqual(pair.Value, workspacePath));
        if (existing.Key is not null)
        {
            _storage.SaveHostState(state);
            return existing.Key;
        }

        var workspaceId = UniqueWorkspaceId(state, workspacePath);
        var workspaces = new Dictionary<string, string>(state.Workspaces, StringComparer.Ordinal)
        {
            [workspaceId] = workspacePath
        };
        _storage.SaveHostState(state with
        {
            Workspaces = workspaces,
            CatalogRevision = state.CatalogRevision + 1
        });
        return workspaceId;
    }

    private static string UniqueWorkspaceId(RemoteToolHostState state, string workspacePath)
    {
        var name = WorkspaceIdCleaner().Replace(
            Path.GetFileName(Path.TrimEndingDirectorySeparator(workspacePath)),
            "-").Trim('-').ToLowerInvariant();
        if (string.IsNullOrEmpty(name))
            name = "workspace";
        var candidate = name;
        for (var suffix = 2; state.Workspaces.ContainsKey(candidate); suffix++)
            candidate = $"{name}-{suffix}";
        return candidate;
    }

    private static string CanonicalizeWorkspace(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !Directory.Exists(path))
            throw new ArgumentException("The shared folder must be an existing absolute directory.", nameof(path));
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var info = new DirectoryInfo(fullPath);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0
            ? Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullPath))
            : fullPath;
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record InviteDetails(
        string? InviterDisplayName,
        string? Purpose,
        string? SuggestedFolder,
        DateTimeOffset? ExpiresAt);

    [GeneratedRegex("[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceIdCleaner();

    [GeneratedRegex("^[A-Za-z0-9_-]{8,256}$", RegexOptions.CultureInvariant)]
    private static partial Regex InviteIdPattern();
}
