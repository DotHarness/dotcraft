using System.Net.WebSockets;
using DotCraft.Sdk.Hub;
using Microsoft.Extensions.Options;

namespace Oratorio.Server.DotCraft;

public interface IDotCraftAppServerProcessManager
{
    Task<DotCraftAppServerEndpoint> EnsureAvailableAsync(string workspacePath, CancellationToken ct);
    Task<DotCraftAppServerProbeResult> ProbeAsync(string workspacePath, CancellationToken ct);
}

public sealed record DotCraftAppServerProbeResult(
    DotCraftAppServerEndpoint? Endpoint,
    bool Connected,
    string Reason,
    string Message);

public sealed class DotCraftAppServerProcessManager(
    IDotCraftAppServerEndpointResolver endpointResolver,
    IOptionsMonitor<DotCraftOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<DotCraftAppServerProcessManager> logger) : IDotCraftAppServerProcessManager
{
    public async Task<DotCraftAppServerEndpoint> EnsureAvailableAsync(string workspacePath, CancellationToken ct)
    {
        var probe = await ProbeAsync(workspacePath, ct);
        if (probe.Connected && probe.Endpoint is not null)
        {
            return probe.Endpoint;
        }

        var started = await TryEnsureFromHubAsync(workspacePath, ct);
        if (started is not null)
        {
            return started;
        }

        throw new InvalidOperationException(probe.Message);
    }

    public async Task<DotCraftAppServerProbeResult> ProbeAsync(string workspacePath, CancellationToken ct)
    {
        var endpoint = await endpointResolver.ResolveAsync(workspacePath, ct);
        if (endpoint is null)
        {
            return new DotCraftAppServerProbeResult(
                null,
                false,
                "workspaceNotRegisteredInHub",
                $"DotCraft AppServer endpoint is not configured or discoverable for workspace '{workspacePath}'. Start or select that workspace AppServer through DotCraft Hub, then retry.");
        }

        using var socket = new ClientWebSocket();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(1.5));
        try
        {
            await socket.ConnectAsync(BuildProbeUri(endpoint), timeout.Token);
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "probe", CancellationToken.None);
            }

            return new DotCraftAppServerProbeResult(endpoint, true, "ok", "DotCraft AppServer is reachable.");
        }
        catch
        {
            return new DotCraftAppServerProbeResult(
                endpoint,
                false,
                "unreachable",
                "DotCraft AppServer is not reachable. Start or select the workspace AppServer through DotCraft Hub, then retry.");
        }
    }

    private static Uri BuildProbeUri(DotCraftAppServerEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Token))
        {
            return new Uri(endpoint.Url);
        }

        var builder = new UriBuilder(endpoint.Url);
        var tokenPair = "token=" + Uri.EscapeDataString(endpoint.Token);
        var existing = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(existing) ? tokenPair : existing + "&" + tokenPair;
        return builder.Uri;
    }

    private async Task<DotCraftAppServerEndpoint?> TryEnsureFromHubAsync(string workspacePath, CancellationToken ct)
    {
        var value = options.CurrentValue;
        if (!value.HubDiscoveryEnabled)
        {
            return null;
        }

        try
        {
            var appServer = await DotCraftAppServerEndpointResolver
                .CreateHubClient(value, httpClientFactory, startHubIfMissing: value.AutoStart)
                .EnsureAppServerAsync(
                    workspacePath,
                    new HubEnsureAppServerOptions
                    {
                        Client = new HubClientInfo
                        {
                            Name = "oratorio",
                            Version = "0.5.2"
                        },
                        StartIfMissing = true
                    },
                    ct);
            var appServerWebSocket = DotCraftAppServerEndpointResolver.ResolveAppServerWebSocket(appServer);
            return string.IsNullOrWhiteSpace(appServerWebSocket)
                ? null
                : new DotCraftAppServerEndpoint(appServerWebSocket, "hub");
        }
        catch (Exception ex) when (ex is HubClientException or HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug(ex, "Could not ask DotCraft Hub to ensure workspace AppServer.");
            return null;
        }
    }
}
