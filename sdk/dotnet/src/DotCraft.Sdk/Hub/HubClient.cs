using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.Hub;

/// <summary>
/// Client for local DotCraft Hub discovery and AppServer management.
/// </summary>
public sealed partial class HubClient
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
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

            using var stream = new FileStream(
                lockPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
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
        if (lockInfo is null || !HubBootstrap.IsOwnerAlive(lockInfo))
        {
            return null;
        }

        return await ProbeHubAsync(lockInfo, cancellationToken);
    }

    /// <summary>
    /// Ensures a live Hub is available, starting one when configured.
    /// </summary>
    public Task<HubLockInfo> EnsureHubAsync(CancellationToken cancellationToken = default)
    {
        var lockPath = Path.GetFullPath(GetLockPath());
        var normalize = OperatingSystem.IsWindows() ? lockPath.ToUpperInvariant() : lockPath;
        var selection = new HubBootstrapFlights.Selection(
            normalize,
            NormalizeExecutableSelection(string.IsNullOrWhiteSpace(_options.Executable) ? "dotcraft" : _options.Executable)!,
            NormalizeExecutableSelection(_options.ExpectedExecutable),
            _options.BinaryMatchPolicy,
            _options.StartHubIfMissing,
            _options.StartupTimeout,
            _options.ShutdownTimeout);
        return HubBootstrapFlights.RunAsync(
            selection,
            token => HubBootstrap.RunAsync(_options, lockPath, ProbeHubAsync,
                async (hub, stopToken) => { await PostJsonAsync(hub, "/v1/shutdown", new { }, stopToken); }, token),
            cancellationToken);
    }

    private static string? NormalizeExecutableSelection(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return null;
        var value = executable.Trim();
        if (Path.IsPathRooted(value) || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar) || value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            value = Path.GetFullPath(value);
        return OperatingSystem.IsWindows() ? value.ToUpperInvariant() : value;
    }

    private async Task<HubLockInfo?> ProbeHubAsync(HubLockInfo lockInfo, CancellationToken cancellationToken)
    {
        try
        {
            ParseHubBaseUrl(lockInfo.ApiBaseUrl);
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeCts.CancelAfter(ProbeTimeout);
            using var http = CreateHttpClient();
            using var response = await http.Client.GetAsync($"{lockInfo.ApiBaseUrl.TrimEnd('/')}/v1/status", probeCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var status = await response.Content.ReadFromJsonAsync<HubStatusResponse>(DotCraftJson.Options, probeCts.Token);
            return status is null ? null : lockInfo with
            {
                Version = status.HubVersion ?? lockInfo.Version,
                BinaryPath = status.BinaryPath ?? lockInfo.BinaryPath
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
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

    /// <summary>Restarts the AppServer for a workspace.</summary>
    public async Task<HubAppServerResponse> RestartAppServerAsync(
        string workspacePath,
        HubRuntimeToolsRequest? runtimeTools = null,
        CancellationToken cancellationToken = default)
    {
        var hub = await EnsureHubAsync(cancellationToken);
        return await PostAppServerAsync(hub, "/v1/appservers/restart", new { workspacePath, runtimeTools }, cancellationToken);
    }

    /// <summary>Stops the AppServer for a workspace.</summary>
    public async Task<HubAppServerResponse> StopAppServerAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        var hub = await EnsureHubAsync(cancellationToken);
        return await PostAppServerAsync(hub, "/v1/appservers/stop", new { workspacePath }, cancellationToken);
    }

    /// <summary>Lists all Hub AppServer registry entries.</summary>
    public async Task<IReadOnlyList<HubAppServerResponse>> ListAppServersAsync(
        CancellationToken cancellationToken = default)
    {
        var hub = await EnsureHubAsync(cancellationToken);
        using var http = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{hub.ApiBaseUrl.TrimEnd('/')}/v1/appservers");
        ApplyAuthorization(request, hub);
        using var response = await http.Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToClientExceptionAsync(response, cancellationToken);
        }
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<HubAppServerResponse>>(DotCraftJson.Options, cancellationToken)
               ?? [];
    }

    /// <summary>Gets the current Hub status.</summary>
    public async Task<HubStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var hub = await EnsureHubAsync(cancellationToken);
        using var http = CreateHttpClient();
        using var response = await http.Client.GetAsync($"{hub.ApiBaseUrl.TrimEnd('/')}/v1/status", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToClientExceptionAsync(response, cancellationToken);
        }
        return await response.Content.ReadFromJsonAsync<HubStatusResponse>(DotCraftJson.Options, cancellationToken)
               ?? throw new HubClientException("hubInvalidResponse", "Hub returned an empty status response.");
    }

    /// <summary>Stops a live Hub; does nothing when no live Hub exists.</summary>
    public async Task ShutdownHubAsync(CancellationToken cancellationToken = default)
    {
        if (await TryGetLiveHubAsync(cancellationToken) is not { } hub)
        {
            return;
        }
        await PostJsonAsync(hub, "/v1/shutdown", new { }, cancellationToken);
    }

    /// <summary>Streams Hub lifecycle events.</summary>
    public async IAsyncEnumerable<HubEvent> SubscribeEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var hub = await EnsureHubAsync(cancellationToken);
        using var http = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{hub.ApiBaseUrl.TrimEnd('/')}/v1/events");
        ApplyAuthorization(request, hub);
        using var response = await http.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToClientExceptionAsync(response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }
            if (line?.StartsWith("data:", StringComparison.Ordinal) != true)
            {
                continue;
            }
            var data = line["data:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(data))
            {
                continue;
            }
            HubEvent? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<HubEvent>(data, DotCraftJson.Options);
            }
            catch (JsonException)
            {
                continue;
            }
            if (parsed is not null)
            {
                yield return parsed;
            }
        }
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
            var details = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("details", out var detailsElement)
                ? detailsElement.Clone()
                : (JsonElement?)null;
            return new HubClientException(code, message, details);
        }
        catch
        {
            return new HubClientException(
                response.StatusCode == HttpStatusCode.Unauthorized ? "unauthorized" : "hubRequestFailed",
                $"Hub request failed with HTTP {(int)response.StatusCode}.");
        }
    }

    private async Task<HubAppServerResponse> PostAppServerAsync(
        HubLockInfo hub,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        var element = await PostJsonAsync(hub, path, body, cancellationToken);
        return element.Deserialize<HubAppServerResponse>(DotCraftJson.Options)
               ?? throw new HubClientException("hubInvalidResponse", "Hub returned an empty AppServer response.");
    }

    private async Task<JsonElement> PostJsonAsync(
        HubLockInfo hub,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var http = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{hub.ApiBaseUrl.TrimEnd('/')}{path}")
        {
            Content = JsonContent.Create(body, options: DotCraftJson.Options)
        };
        ApplyAuthorization(request, hub);
        using var response = await http.Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToClientExceptionAsync(response, cancellationToken);
        }
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(cancellationToken), DotCraftJson.Options);
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
