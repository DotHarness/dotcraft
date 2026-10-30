using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotCraft.Common;

namespace DotCraft.Hub;

/// <summary>
/// Local client for discovering or starting DotCraft Hub and requesting managed AppServer endpoints.
/// </summary>
public sealed class HubClient(string? dotcraftBin = null, HubPaths? paths = null)
{
    private static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PreviousHubShutdownTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly string? _dotcraftBin = dotcraftBin;
    private readonly HubPaths _paths = paths ?? HubPaths.ForCurrentUser();

    /// <summary>
    /// Ensures a Hub-managed AppServer exists for a workspace and returns its connection metadata.
    /// </summary>
    public async Task<HubAppServerResponse> EnsureAppServerAsync(
        string workspacePath,
        string clientName,
        CancellationToken cancellationToken = default)
    {
        var info = await EnsureHubAsync(cancellationToken);
        using var http = CreateHttpClient(info);
        var response = await http.PostAsJsonAsync(
            $"{info.ApiBaseUrl}/v1/appservers/ensure",
            new EnsureAppServerRequest
            {
                WorkspacePath = workspacePath,
                Client = new HubClientInfo
                {
                    Name = clientName,
                    Version = AppVersion.Informational
                },
                StartIfMissing = true
            },
            HubJson.Options,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<HubAppServerResponse>(HubJson.Options, cancellationToken)
                   ?? throw new HubClientException("hubInvalidResponse", "Hub returned an empty AppServer response.");
        }

        throw await ToClientExceptionAsync(response, cancellationToken);
    }

    /// <summary>
    /// Ensures the current user's default Chat workspace AppServer and returns its connection metadata.
    /// </summary>
    public Task<HubAppServerResponse> EnsureDefaultChatAppServerAsync(
        string clientName,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = DefaultChatWorkspace.Ensure(_paths);
        return EnsureAppServerAsync(workspacePath, clientName, cancellationToken);
    }

    /// <summary>
    /// Restarts the Hub-managed AppServer for a workspace and returns the replacement endpoint metadata.
    /// </summary>
    public async Task<HubAppServerResponse> RestartAppServerAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var info = await EnsureHubAsync(cancellationToken);
        using var http = CreateHttpClient(info);
        var response = await http.PostAsJsonAsync(
            $"{info.ApiBaseUrl}/v1/appservers/restart",
            new WorkspacePathRequest { WorkspacePath = workspacePath },
            HubJson.Options,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<HubAppServerResponse>(HubJson.Options, cancellationToken)
                   ?? throw new HubClientException("hubInvalidResponse", "Hub returned an empty AppServer response.");
        }

        throw await ToClientExceptionAsync(response, cancellationToken);
    }

    private async Task<HubLockInfo> EnsureHubAsync(CancellationToken cancellationToken)
    {
        var shutdownDeadline = DateTimeOffset.UtcNow + PreviousHubShutdownTimeout;
        DateTimeOffset? startupDeadline = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var discovery = await TryGetLiveHubAsync(cancellationToken);
            if (discovery.Info is { } live)
                return live;

            if (discovery.Owner is { } owner && startupDeadline is null)
            {
                if (DateTimeOffset.UtcNow >= shutdownDeadline)
                {
                    throw new HubClientException(
                        "hubUnavailable",
                        $"The previous DotCraft Hub (pid {owner.Pid}) did not stop within " +
                        $"{PreviousHubShutdownTimeout.TotalSeconds:N0}s. " +
                        $"Last status: {discovery.Status} Hub lock: {_paths.LockFilePath}");
                }
            }
            else if (discovery.Owner is null && startupDeadline is null)
            {
                StartHubProcess();
                startupDeadline = DateTimeOffset.UtcNow + DefaultStartupTimeout;
            }

            if (DateTimeOffset.UtcNow >= startupDeadline)
            {
                throw new HubClientException(
                    "hubUnavailable",
                    $"DotCraft Hub could not be started within {DefaultStartupTimeout.TotalSeconds:N0}s. " +
                    $"Last status: {discovery.Status} Hub lock: {_paths.LockFilePath}");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task<HubDiscoveryResult> TryGetLiveHubAsync(CancellationToken cancellationToken)
    {
        var info = HubLockFile.TryRead(_paths.LockFilePath);
        if (info is null)
            return new HubDiscoveryResult(null, null, "No hub.lock was found.");

        var ownerStatus = info.GetOwnerProcessStatus();
        if (ownerStatus != HubLockOwnerStatus.Alive)
            return new HubDiscoveryResult(null, null, $"hub.lock owner is not live ({ownerStatus}, pid {info.Pid}).");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await http.GetAsync($"{info.ApiBaseUrl}/v1/status", cancellationToken);
            return response.IsSuccessStatusCode
                ? new HubDiscoveryResult(info, info, $"Hub status check succeeded at {info.ApiBaseUrl}.")
                : new HubDiscoveryResult(null, info, $"Hub status check returned HTTP {(int)response.StatusCode} at {info.ApiBaseUrl}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new HubDiscoveryResult(null, info, $"Hub status check failed at {info.ApiBaseUrl}: {ex.Message}");
        }
    }

    private void StartHubProcess()
    {
        try
        {
            HubLauncher.StartLauncher(
                string.IsNullOrWhiteSpace(_dotcraftBin) ? HubLauncher.CurrentBinary() : _dotcraftBin,
                _paths);
        }
        catch (Exception ex) when (ex is not HubClientException)
        {
            throw new HubClientException("hubUnavailable", $"DotCraft Hub failed to start: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient(HubLockInfo info)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        return http;
    }

    private static async Task<HubClientException> ToClientExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<HubErrorResponse>(HubJson.Options, cancellationToken);
            if (body is not null)
                return new HubClientException(body.Error.Code, body.Error.Message);
        }
        catch
        {
            // Fall through to a status-based error.
        }

        var code = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? "unauthorized"
            : "hubRequestFailed";
        return new HubClientException(code, $"Hub request failed with HTTP {(int)response.StatusCode}.");
    }

    private sealed record HubDiscoveryResult(HubLockInfo? Info, HubLockInfo? Owner, string Status);
}

/// <summary>
/// Error raised while discovering Hub or calling Hub Protocol.
/// </summary>
public sealed class HubClientException(string code, string message) : Exception(message)
{
    /// <summary>
    /// Stable Hub/client error code suitable for user-facing mapping.
    /// </summary>
    public string Code { get; } = code;
}
