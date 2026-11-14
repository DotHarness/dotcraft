using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;

namespace DotCraft.CodexGateway.Relay;

internal sealed class RemoteControlApi : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly HttpClient _http;
    private readonly Func<bool, CancellationToken, Task<RemoteControlCredentials>> _credentials;
    private readonly string _installationId;

    public RemoteControlApi(Uri backendBaseUri, string installationId, string appServerVersion,
        Func<bool, CancellationToken, Task<RemoteControlCredentials>> credentials)
    {
        BaseUri = NormalizeBaseUri(backendBaseUri);
        _installationId = installationId;
        _credentials = credentials;
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DotCraftCodexGateway/" + appServerVersion);
    }

    public Uri BaseUri { get; }
    public Uri WebSocketUri => new UriBuilder(new Uri(BaseUri, "wham/remote/control/server"))
    {
        Scheme = BaseUri.Scheme == "https" ? "wss" : "ws"
    }.Uri;

    public async Task<T> AccountRequestAsync<T>(string action, object body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var credentials = await _credentials(attempt != 0, ct);
            using var request = Request(action, body, credentials.AccessToken);
            request.Headers.Add("chatgpt-account-id", credentials.AccountId);
            request.Headers.Add("x-codex-installation-id", _installationId);
            request.Headers.Add("originator", "codex_cli_rs");
            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                continue;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AuthenticationException("codex_remote_control.authentication_failed: ChatGPT authentication was rejected.");
            return await ReadAsync<T>(response, action, ct);
        }
    }

    public async Task<T> RelayRequestAsync<T>(string action, object body, string relayToken, CancellationToken ct)
    {
        using var request = Request(action, body, relayToken);
        using var response = await _http.SendAsync(request, ct);
        return await ReadAsync<T>(response, action, ct);
    }

    public static HttpMessageInvoker CreateWebSocketInvoker(Action<RelayRequestException> failed) =>
        new(new WebSocketResponseHandler(failed) { InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false } });

    private static HttpRequestMessage Request(string action, object body, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "wham/remote/control/server/" + action)
        {
            Content = JsonContent.Create(body, options: Json)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string action, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw await RelayRequestException.FromResponseAsync(response, action, ct);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                ?? throw new HttpRequestException("codex_remote_control.invalid_response: Empty relay response.");
        }
        catch (JsonException)
        {
            throw new HttpRequestException("codex_remote_control.invalid_response: Invalid relay JSON response.");
        }
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("codex_remote_control.invalid_backend: Expected an absolute backend URI.", nameof(uri));
        var official = uri.Host == "chatgpt.com";
        if (!(uri.Scheme == "https" && (official || uri.IsLoopback)
            || uri.Scheme == "http" && uri.IsLoopback) || uri.UserInfo.Length != 0)
            throw new ArgumentException("codex_remote_control.invalid_backend: Expected an official HTTPS backend or loopback test server.", nameof(uri));
        var builder = new UriBuilder(uri) { Query = "", Fragment = "" };
        if (!builder.Path.EndsWith('/'))
            builder.Path += "/";
        return builder.Uri;
    }

    public void Dispose() => _http.Dispose();

    private sealed class WebSocketResponseHandler(Action<RelayRequestException> failed) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.SwitchingProtocols && !response.IsSuccessStatusCode)
                failed(await RelayRequestException.FromResponseAsync(response, "connect", ct));
            return response;
        }
    }
}

internal sealed record EnrollmentResponse(
    string ServerId, string EnvironmentId, string RemoteControlToken, DateTimeOffset ExpiresAt);

internal sealed record PairingResponse(
    string PairingCode, string? ManualPairingCode, string ServerId, string EnvironmentId, DateTimeOffset ExpiresAt);

internal sealed record PairingStatusResponse(bool Claimed);

internal sealed class RelayRequestException(
    string action, HttpStatusCode status, TimeSpan? retryAfter, bool serverMissing)
    : HttpRequestException($"codex_remote_control.request_failed: {action} returned HTTP {(int)status}.", null, status)
{
    private readonly DateTimeOffset? _retryAt = retryAfter is { } delay ? DateTimeOffset.UtcNow + delay : null;
    public TimeSpan? RetryAfter => _retryAt is { } deadline
        ? TimeSpan.FromTicks(Math.Max(0, (deadline - DateTimeOffset.UtcNow).Ticks)) : null;
    public string Operation { get; } = action;
    public bool ServerMissing { get; } = serverMissing;
    public bool IsTransient => StatusCode == HttpStatusCode.TooManyRequests || (int?)StatusCode >= 500;

    public static async Task<RelayRequestException> FromResponseAsync(
        HttpResponseMessage response, string action, CancellationToken ct)
    {
        var missing = false;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            try
            {
                using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                missing = body.RootElement.ValueKind == JsonValueKind.Object
                    && body.RootElement.TryGetProperty("detail", out var detail)
                    && detail.ValueKind == JsonValueKind.String && detail.GetString() == "Remote app server not found";
            }
            catch (JsonException) { }
        }
        var retry = response.Headers.RetryAfter;
        var delay = retry?.Delta ?? (retry?.Date is { } deadline ? deadline - DateTimeOffset.UtcNow : null);
        if (delay is { } duration && duration < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        return new RelayRequestException(action, response.StatusCode, delay, missing);
    }
}
