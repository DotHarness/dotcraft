using System.Diagnostics;
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
        if (await TryGetLiveHubAsync(cancellationToken) is { } live)
            return live;

        StartHubProcess();

        var deadline = DateTimeOffset.UtcNow + DefaultStartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryGetLiveHubAsync(cancellationToken) is { } info)
                return info;

            await Task.Delay(PollInterval, cancellationToken);
        }

        throw new HubClientException("hubUnavailable", "DotCraft Hub could not be started.");
    }

    private async Task<HubLockInfo?> TryGetLiveHubAsync(CancellationToken cancellationToken)
    {
        var info = HubLockFile.TryRead(_paths.LockFilePath);
        if (info is null || !info.IsProcessAlive())
            return null;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await http.GetAsync($"{info.ApiBaseUrl}/v1/status", cancellationToken);
            return response.IsSuccessStatusCode ? info : null;
        }
        catch
        {
            return null;
        }
    }

    private void StartHubProcess()
    {
        var dotcraftBin = ResolveDotCraftBinary();
        var psi = new ProcessStartInfo
        {
            FileName = dotcraftBin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? "dotnet"
                : dotcraftBin,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        if (dotcraftBin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(dotcraftBin);

        psi.ArgumentList.Add("hub");

        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new HubClientException("hubUnavailable", $"DotCraft Hub failed to start: {ex.Message}");
        }
    }

    private string ResolveDotCraftBinary()
    {
        if (!string.IsNullOrWhiteSpace(_dotcraftBin))
            return _dotcraftBin;

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && Path.GetFileNameWithoutExtension(processPath).Equals("dotcraft", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var assemblyPath = typeof(HubClient).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath))
            return assemblyPath;

        return processPath ?? throw new HubClientException("hubUnavailable", "Cannot determine dotcraft binary path.");
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
