using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.Hub;

/// <summary>
/// Client for local DotCraft Hub discovery and AppServer management.
/// </summary>
public sealed class HubClient
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private readonly DotCraftHubClientOptions _options;

    /// <summary>
    /// Creates a Hub client.
    /// </summary>
    public HubClient(DotCraftHubClientOptions? options = null)
    {
        _options = options ?? new DotCraftHubClientOptions();
    }

    /// <summary>
    /// Resolves the default Hub lock path for a user profile.
    /// </summary>
    public static string ResolveHubLockPath(string? userProfilePath = null)
    {
        var profile = string.IsNullOrWhiteSpace(userProfilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfilePath;
        return Path.Combine(profile, ".craft", "hub", "hub.lock");
    }

    /// <summary>
    /// Resolves the default Chat workspace path for a user profile.
    /// </summary>
    public static string ResolveDefaultChatWorkspacePath(string? userProfilePath = null)
    {
        var profile = string.IsNullOrWhiteSpace(userProfilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfilePath;
        return Path.Combine(profile, ".craft", "workspaces", "chats");
    }

    /// <summary>
    /// Reads a Hub lock file and validates its required fields.
    /// </summary>
    public static HubLockInfo? ReadHubLock(string lockPath)
    {
        try
        {
            if (!File.Exists(lockPath))
            {
                return null;
            }

            using var stream = File.OpenRead(lockPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("pid", out var pidElement) ||
                pidElement.ValueKind != JsonValueKind.Number ||
                !root.TryGetProperty("apiBaseUrl", out var apiElement) ||
                apiElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("token", out var tokenElement) ||
                tokenElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return new HubLockInfo(
                pidElement.GetInt32(),
                apiElement.GetString() ?? "",
                tokenElement.GetString() ?? "",
                ReadDate(root, "startedAt"),
                ReadString(root, "version"),
                ReadString(root, "binaryPath"));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true when the process id appears to be live.
    /// </summary>
    public static bool IsProcessAlive(int pid)
    {
        try
        {
            return pid > 0 && !Process.GetProcessById(pid).HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that a Hub base URL is loopback HTTP with an explicit port.
    /// </summary>
    public static Uri ParseHubBaseUrl(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            throw new HubClientException("invalidHubLock", "Hub URL is invalid.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new HubClientException("invalidHubLock", "Hub URL must use http://.");
        }

        if (uri.Port <= 0 || uri.IsDefaultPort)
        {
            throw new HubClientException("invalidHubLock", "Hub URL must include a port.");
        }

        if (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
            (!IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address)))
        {
            throw new HubClientException("invalidHubLock", "Hub URL must be loopback.");
        }

        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new HubClientException("invalidHubLock", "Hub URL must not include path, query, or fragment.");
        }

        return uri;
    }

    /// <summary>
    /// Returns a live Hub lock if one is discoverable and healthy.
    /// </summary>
    public async Task<HubLockInfo?> TryGetLiveHubAsync(CancellationToken cancellationToken = default)
    {
        var lockInfo = ReadHubLock(GetLockPath());
        if (lockInfo is null || !IsProcessAlive(lockInfo.Pid))
        {
            return null;
        }

        try
        {
            ParseHubBaseUrl(lockInfo.ApiBaseUrl);
            using var http = CreateHttpClient();
            using var response = await http.Client.GetAsync($"{lockInfo.ApiBaseUrl.TrimEnd('/')}/v1/status", cancellationToken);
            return response.IsSuccessStatusCode ? lockInfo : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ensures a live Hub is available, starting one when configured.
    /// </summary>
    public async Task<HubLockInfo> EnsureHubAsync(CancellationToken cancellationToken = default)
    {
        if (await TryGetLiveHubAsync(cancellationToken) is { } live)
        {
            return live;
        }

        if (!_options.StartHubIfMissing)
        {
            throw new HubClientException("hubUnavailable", "DotCraft Hub is not running.");
        }

        StartHubProcess();
        var deadline = DateTimeOffset.UtcNow + _options.StartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await TryGetLiveHubAsync(cancellationToken) is { } started)
            {
                return started;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        throw new HubClientException("hubUnavailable", "DotCraft Hub could not be started.");
    }

    /// <summary>
    /// Queries Hub for a workspace AppServer without starting it.
    /// </summary>
    public async Task<HubAppServerResponse?> GetAppServerByWorkspaceAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var hub = await TryGetLiveHubAsync(cancellationToken);
        if (hub is null)
        {
            return null;
        }

        using var http = CreateHttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{hub.ApiBaseUrl.TrimEnd('/')}/v1/appservers/by-workspace?path={Uri.EscapeDataString(workspacePath)}");
        ApplyAuthorization(request, hub);
        using var response = await http.Client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ToClientExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<HubAppServerResponse>(DotCraftJson.Options, cancellationToken);
    }

    /// <summary>
    /// Ensures Hub has a running AppServer for the workspace.
    /// </summary>
    public async Task<HubAppServerResponse> EnsureAppServerAsync(
        string workspacePath,
        HubEnsureAppServerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var hub = await EnsureHubAsync(cancellationToken);
        using var http = CreateHttpClient();
        var payload = new
        {
            workspacePath,
            client = options?.Client,
            startIfMissing = options?.StartIfMissing ?? true,
            runtimeTools = options?.RuntimeTools
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{hub.ApiBaseUrl.TrimEnd('/')}/v1/appservers/ensure")
        {
            Content = JsonContent.Create(payload, options: DotCraftJson.Options)
        };
        ApplyAuthorization(request, hub);
        using var response = await http.Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToClientExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<HubAppServerResponse>(DotCraftJson.Options, cancellationToken)
               ?? throw new HubClientException("hubInvalidResponse", "Hub returned an empty AppServer response.");
    }

    /// <summary>
    /// Ensures the default Chat workspace AppServer using the existing Hub ensure endpoint.
    /// </summary>
    public Task<HubAppServerResponse> EnsureDefaultChatAppServerAsync(
        HubEnsureAppServerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = EnsureDefaultChatWorkspace(_options.UserProfilePath);
        return EnsureAppServerAsync(workspacePath, options, cancellationToken);
    }

    /// <summary>
    /// Ensures the default Chat workspace directory structure exists.
    /// </summary>
    public static string EnsureDefaultChatWorkspace(string? userProfilePath = null)
    {
        var workspacePath = Path.GetFullPath(ResolveDefaultChatWorkspacePath(userProfilePath));
        var craftPath = Path.Combine(workspacePath, ".craft");
        Directory.CreateDirectory(workspacePath);
        Directory.CreateDirectory(craftPath);
        Directory.CreateDirectory(Path.Combine(craftPath, "memory"));
        Directory.CreateDirectory(Path.Combine(craftPath, "skills"));
        Directory.CreateDirectory(Path.Combine(craftPath, "security"));

        var configPath = Path.Combine(craftPath, "config.json");
        if (!File.Exists(configPath))
            File.WriteAllText(configPath, "{}" + Environment.NewLine);

        return workspacePath;
    }

    private string GetLockPath() =>
        string.IsNullOrWhiteSpace(_options.HubLockPath)
            ? ResolveHubLockPath(_options.UserProfilePath)
            : Path.GetFullPath(_options.HubLockPath);

    private HubHttpClient CreateHttpClient()
    {
        var ownsClient = _options.HttpClientFactory is null;
        var http = _options.HttpClientFactory?.Invoke() ?? new HttpClient();
        if (ownsClient)
        {
            http.Timeout = TimeSpan.FromSeconds(60);
        }

        return new HubHttpClient(http, ownsClient);
    }

    private static void ApplyAuthorization(HttpRequestMessage request, HubLockInfo hub) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hub.Token);

    private void StartHubProcess()
    {
        var dotcraftBin = string.IsNullOrWhiteSpace(_options.DotCraftBin)
            ? "dotcraft"
            : _options.DotCraftBin!;
        var isDll = dotcraftBin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isDll ? "dotnet" : dotcraftBin,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        if (isDll)
        {
            startInfo.ArgumentList.Add(dotcraftBin);
        }

        startInfo.ArgumentList.Add("hub");
        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new HubClientException("hubUnavailable", $"DotCraft Hub failed to start: {ex.Message}", ex);
        }
    }

    private static async Task<HubClientException> ToClientExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var error = root.TryGetProperty("error", out var errorElement) ? errorElement : root;
            var code = ReadString(error, "code") ?? (response.StatusCode == HttpStatusCode.Unauthorized ? "unauthorized" : "hubRequestFailed");
            var message = ReadString(error, "message") ?? $"Hub request failed with HTTP {(int)response.StatusCode}.";
            return new HubClientException(code, message);
        }
        catch
        {
            return new HubClientException(
                response.StatusCode == HttpStatusCode.Unauthorized ? "unauthorized" : "hubRequestFailed",
                $"Hub request failed with HTTP {(int)response.StatusCode}.");
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private sealed class HubHttpClient(HttpClient client, bool ownsClient) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public void Dispose()
        {
            if (ownsClient)
            {
                Client.Dispose();
            }
        }
    }
}
