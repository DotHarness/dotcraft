using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using DotCraft.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

/// <summary>The Hub peer id is the host id every Agent-side surface uses.</summary>
internal interface IRemoteToolHostDirectory
{
    /// <summary>Endpoint identity used to detect that the Hub was replaced under a live session.</summary>
    string? CurrentEndpoint { get; }

    ValueTask<IReadOnlyList<RemoteToolHostDescriptor>> ListAsync(CancellationToken cancellationToken);

    ValueTask<RemoteToolHostConnection> ConnectAsync(string hostId, CancellationToken cancellationToken);
}

internal sealed class RemoteToolHostConnection(
    IClientTransport transport,
    string endpoint,
    ClientWebSocket? socket = null,
    Stream? stream = null) : IAsyncDisposable
{
    public IClientTransport Transport => transport;
    public string Endpoint => endpoint;
    public string? CloseDescription => socket?.CloseStatusDescription;

    public async ValueTask DisposeAsync()
    {
        if (stream is not null)
            await stream.DisposeAsync().ConfigureAwait(false);
        socket?.Dispose();
    }
}

internal sealed class HubRemoteToolHostDirectory(IHubEndpointProvider endpoints) : IRemoteToolHostDirectory, IDisposable
{
    private readonly HttpClient _http = new();

    public string? CurrentEndpoint => endpoints.TryResolve()?.BaseUrl.ToString();

    public async ValueTask<IReadOnlyList<RemoteToolHostDescriptor>> ListAsync(CancellationToken cancellationToken)
    {
        var hub = RequireHub();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(hub.BaseUrl, "/v1/satellites"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hub.Token);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new RemoteToolHostException(
                RemoteToolErrorCodes.HubUnavailable,
                $"The local Hub at {hub.BaseUrl} did not answer.",
                inner: ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new RemoteToolHostException(
                    RemoteToolErrorCodes.AuthenticationFailed,
                    "The local Hub rejected this process's Hub token.");
            if (!response.IsSuccessStatusCode)
                throw new RemoteToolHostException(
                    RemoteToolErrorCodes.HubUnavailable,
                    $"The local Hub returned {(int)response.StatusCode} for the paired machine list.");
            var peers = await response.Content
                .ReadFromJsonAsync<HubSatelliteEntry[]>(RemoteToolHostProtocol.JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? [];
            return [.. peers.Select(ToDescriptor)];
        }
    }

    public async ValueTask<RemoteToolHostConnection> ConnectAsync(
        string hostId,
        CancellationToken cancellationToken)
    {
        var hub = RequireHub();
        var uri = new UriBuilder(hub.BaseUrl)
        {
            Scheme = "ws",
            Path = $"/v1/satellites/{Uri.EscapeDataString(hostId)}/bridge",
            Query = "session=" + Guid.NewGuid().ToString("N")
        }.Uri;

        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + hub.Token);
        try
        {
            await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var status = socket.HttpStatusCode;
            socket.Dispose();
            throw status switch
            {
                HttpStatusCode.NotFound => new RemoteToolHostException(
                    RemoteToolErrorCodes.HostNotRegistered,
                    $"The local Hub has no paired machine '{hostId}'.",
                    inner: ex),
                HttpStatusCode.Unauthorized => new RemoteToolHostException(
                    RemoteToolErrorCodes.AuthenticationFailed,
                    "The local Hub rejected this process's Hub token.",
                    inner: ex),
                _ => new RemoteToolHostException(
                    RemoteToolErrorCodes.HubUnavailable,
                    $"The local Hub at {hub.BaseUrl} did not open a session bridge.",
                    inner: ex)
            };
        }

        var stream = WebSocketStream.Create(socket, WebSocketMessageType.Text, ownsWebSocket: false);
        return new RemoteToolHostConnection(
            new StreamClientTransport(stream, stream, loggerFactory: null),
            hub.BaseUrl.ToString(),
            socket,
            stream);
    }

    public void Dispose() => _http.Dispose();

    private HubEndpoint RequireHub() =>
        endpoints.TryResolve()
        ?? throw new RemoteToolHostException(
            RemoteToolErrorCodes.HubUnavailable,
            "No local Hub was found. Start DotCraft Hub before using a paired machine.");

    private static RemoteToolHostDescriptor ToDescriptor(HubSatelliteEntry entry) => new(
        entry.PeerId,
        entry.DisplayName,
        entry.Online,
        [
            .. entry.Workspaces.Select(workspace => new RemoteToolWorkspaceDescriptor(
                workspace.WorkspaceId,
                workspace.Path,
                !workspace.Busy,
                workspace.BusyOwner,
                workspace.LeaseExpiresAt))
        ],
        entry.Online ? null : RemoteToolErrorCodes.SatelliteOffline);

    private sealed record HubSatelliteEntry(
        string PeerId,
        string DisplayName,
        bool Online,
        IReadOnlyList<HubSatelliteWorkspace> Workspaces);

    private sealed record HubSatelliteWorkspace(
        string WorkspaceId,
        string Path,
        bool Busy,
        string? BusyOwner,
        DateTimeOffset? LeaseExpiresAt);
}
