using DotCraft.Sdk.Hub;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Integrations;

public interface IDotCraftAppServerEndpointResolver
{
    Task<DotCraftAppServerEndpoint?> ResolveAsync(string workspacePath, CancellationToken ct);

    /// <summary>
    /// Resolves the bearer token for a configured AppServer endpoint, unprotecting it when stored encrypted.
    /// Returns <c>null</c> when no token is configured. Use this when reconnecting to a stored endpoint URL
    /// that does not already carry a token in its query string.
    /// </summary>
    string? ResolveConfiguredToken();
}

/// <param name="Token">
/// Bearer token to present when connecting, or <c>null</c>. Populated for configuration-sourced endpoints;
/// Hub-sourced endpoints leave this null because their token is embedded in <see cref="Url"/>.
/// </param>
public sealed record DotCraftAppServerEndpoint(string Url, string Source, string? Token = null);

public sealed class DotCraftAppServerEndpointResolver(
    IOptionsMonitor<DotCraftOptions> options,
    IHttpClientFactory httpClientFactory,
    IConfigurationSecretProtector secretProtector,
    ILogger<DotCraftAppServerEndpointResolver> logger) : IDotCraftAppServerEndpointResolver
{
    public async Task<DotCraftAppServerEndpoint?> ResolveAsync(string workspacePath, CancellationToken ct)
    {
        var value = options.CurrentValue;
        if (value.HubDiscoveryEnabled)
        {
            var hubEndpoint = await ResolveFromHubAsync(value, workspacePath, ct);
            if (hubEndpoint is not null)
            {
                return hubEndpoint;
            }
        }

        return string.IsNullOrWhiteSpace(value.AppServerUrl)
            ? null
            : new DotCraftAppServerEndpoint(value.AppServerUrl, "configuration", ResolveConfiguredToken());
    }

    public string? ResolveConfiguredToken()
    {
        var raw = options.CurrentValue.AppServerToken;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var resolved = secretProtector.Unprotect(raw);
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }

    private async Task<DotCraftAppServerEndpoint?> ResolveFromHubAsync(DotCraftOptions value, string workspacePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return null;
        }

        try
        {
            var appServer = await CreateHubClient(value, startHubIfMissing: false)
                .GetAppServerByWorkspaceAsync(workspacePath, ct);
            if (appServer is null ||
                !string.Equals(appServer.State, HubAppServerStates.Running, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var appServerWebSocket = ResolveAppServerWebSocket(appServer);
            return string.IsNullOrWhiteSpace(appServerWebSocket)
                ? null
                : new DotCraftAppServerEndpoint(appServerWebSocket, "hub");
        }
        catch (Exception ex) when (ex is HubClientException or HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug(ex, "Could not query DotCraft Hub for workspace AppServer.");
            return null;
        }
    }

    internal static HubClient CreateHubClient(
        DotCraftOptions value,
        IHttpClientFactory httpClientFactory,
        bool startHubIfMissing) =>
        new(new DotCraftHubClientOptions
        {
            HubLockPath = value.HubLockPath,
            Executable = ResolveDotCraftBin(value),
            StartHubIfMissing = startHubIfMissing,
            HttpClientFactory = () => httpClientFactory.CreateClient("DotCraftHub")
        });

    internal static string? ResolveAppServerWebSocket(HubAppServerResponse response)
    {
        if (response.Endpoints.TryGetValue("appServerWebSocket", out var endpoint))
        {
            return endpoint;
        }

        return response.ServiceStatus.TryGetValue("appServerWebSocket", out var service)
            ? service.Url
            : null;
    }

    private HubClient CreateHubClient(DotCraftOptions value, bool startHubIfMissing) =>
        CreateHubClient(value, httpClientFactory, startHubIfMissing);

    private static string? ResolveDotCraftBin(DotCraftOptions value)
    {
        if (string.Equals(Path.GetFileNameWithoutExtension(value.Command), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return value.Arguments.FirstOrDefault(argument =>
                argument.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        }

        return string.IsNullOrWhiteSpace(value.Command) ? null : value.Command;
    }
}
