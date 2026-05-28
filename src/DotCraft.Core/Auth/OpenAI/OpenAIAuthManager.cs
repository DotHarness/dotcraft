using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Owns the lifecycle of OpenAI / ChatGPT OAuth tokens: PKCE login, refresh, revocation, and
/// thread-safe access for HTTP pipeline policies. One instance per process.
/// </summary>
public sealed class OpenAIAuthManager : IOpenAIAuthService
{
    private readonly OpenAITokenStore _store;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIAuthManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AuthDotJson? _cached;

    public event Action<OpenAIAuthStatus>? LoggedIn;
    public event Action? LoggedOut;

    public OpenAIAuthManager(
        OpenAITokenStore? store = null,
        HttpClient? httpClient = null,
        ILogger<OpenAIAuthManager>? logger = null)
    {
        _store = store ?? new OpenAITokenStore();
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _logger = logger ?? NullLogger<OpenAIAuthManager>.Instance;
        _cached = _store.Load();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DotCraft/Auth");
        client.DefaultRequestHeaders.Add(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);
        return client;
    }

    public bool IsAuthenticated => _cached?.Tokens?.AccessToken is { Length: > 0 };

    public string? GetAccountId() => GetClaimsSafe()?.AccountId ?? _cached?.Tokens?.AccountId;

    public OpenAIAuthStatus GetStatus()
    {
        if (_cached?.Tokens is null)
            return new OpenAIAuthStatus(false, null, null, null, null, null);

        var claims = GetClaimsSafe();
        return new OpenAIAuthStatus(
            LoggedIn: true,
            AccountId: claims?.AccountId ?? _cached.Tokens.AccountId,
            PlanType: claims?.PlanType,
            Email: claims?.Email,
            LastRefresh: _cached.LastRefresh,
            AccessTokenExpiresAt: JwtClaimsReader.TryParseExpiration(_cached.Tokens.AccessToken));
    }

    public async Task<OpenAIAuthStatus> LoginAsync(
        bool openBrowser,
        Action<string>? onAuthorizationUrl,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var server = LoopbackOAuthServer.Start();
            var verifier = Pkce.CreateCodeVerifier();
            var challenge = Pkce.CreateS256Challenge(verifier);
            var state = RandomUrlSafeToken(32);

            var authorizeUrl = BuildAuthorizeUrl(server.RedirectUri, challenge, state);
            onAuthorizationUrl?.Invoke(authorizeUrl);
            if (openBrowser)
                TryOpenBrowser(authorizeUrl);

            var callbackTask = server.AwaitCallbackAsync(state, cancellationToken);
            var result = await callbackTask.ConfigureAwait(false);
            if (!result.Success || string.IsNullOrEmpty(result.AuthorizationCode))
            {
                var detail = result.ErrorDescription ?? result.Error ?? "Sign-in was not completed.";
                throw new OpenAIAuthException(OpenAIAuthFailureReason.Unknown, detail);
            }

            var tokenResponse = await ExchangeCodeForTokensAsync(
                result.AuthorizationCode, verifier, server.RedirectUri, cancellationToken).ConfigureAwait(false);

            var claims = JwtClaimsReader.Parse(tokenResponse.IdToken);
            var auth = new AuthDotJson
            {
                Tokens = new OpenAITokenSet
                {
                    IdToken = tokenResponse.IdToken,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    AccountId = claims.AccountId
                },
                LastRefresh = DateTimeOffset.UtcNow
            };
            _store.Save(auth);
            _cached = auth;
            var status = GetStatus();
            RaiseLoggedIn(status);
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RaiseLoggedIn(OpenAIAuthStatus status)
    {
        try { LoggedIn?.Invoke(status); }
        catch (Exception ex) { _logger.LogWarning(ex, "OpenAIAuthManager.LoggedIn subscriber threw."); }
    }

    private void RaiseLoggedOut()
    {
        try { LoggedOut?.Invoke(); }
        catch (Exception ex) { _logger.LogWarning(ex, "OpenAIAuthManager.LoggedOut subscriber threw."); }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var refreshToken = _cached?.Tokens?.RefreshToken;
            if (!string.IsNullOrEmpty(refreshToken))
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, OpenAIAuthConstants.RevokeUrl)
                    {
                        Content = JsonContent.Create(new
                        {
                            token = refreshToken,
                            token_type_hint = "refresh_token",
                            client_id = OpenAIAuthConstants.ClientId
                        })
                    };
                    using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        _logger.LogWarning("Failed to revoke OpenAI refresh token at {Url}: {Status} {Body}",
                            OpenAIAuthConstants.RevokeUrl, response.StatusCode, body);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    _logger.LogWarning(ex, "Revoke request to {Url} failed; proceeding with local logout.", OpenAIAuthConstants.RevokeUrl);
                }
            }

            _store.Delete();
            _cached = null;
        }
        finally
        {
            _gate.Release();
        }
        RaiseLoggedOut();
    }

    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached?.Tokens is null)
                throw new OpenAIAuthException(OpenAIAuthFailureReason.NotSignedIn,
                    "Not signed in to ChatGPT. Run `dotcraft auth openai login` or use the Desktop settings.");

            var shouldRefresh = forceRefresh || NeedsRefresh(_cached);
            if (shouldRefresh)
            {
                await RefreshLockedAsync(cancellationToken).ConfigureAwait(false);
            }

            return _cached!.Tokens!.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshLockedAsync(CancellationToken cancellationToken)
    {
        if (_cached?.Tokens is null)
            throw new OpenAIAuthException(OpenAIAuthFailureReason.NotSignedIn, "Not signed in to ChatGPT.");

        var refreshToken = _cached.Tokens.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
            throw new OpenAIAuthException(OpenAIAuthFailureReason.NotSignedIn, "Refresh token missing — please sign in again.");

        using var request = new HttpRequestMessage(HttpMethod.Post, OpenAIAuthConstants.TokenUrl)
        {
            Content = JsonContent.Create(new
            {
                client_id = OpenAIAuthConstants.ClientId,
                grant_type = "refresh_token",
                refresh_token = refreshToken
            })
        };

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new OpenAIAuthException(OpenAIAuthFailureReason.Network,
                "Network error while refreshing the ChatGPT access token.", ex);
        }

        try
        {
            if (response.IsSuccessStatusCode)
            {
                var refreshed = await response.Content.ReadFromJsonAsync<RefreshResponse>(
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                    ?? throw new OpenAIAuthException(OpenAIAuthFailureReason.Unknown, "Empty response from token endpoint.");

                var newTokens = new OpenAITokenSet
                {
                    IdToken = !string.IsNullOrEmpty(refreshed.IdToken) ? refreshed.IdToken : _cached.Tokens.IdToken,
                    AccessToken = !string.IsNullOrEmpty(refreshed.AccessToken) ? refreshed.AccessToken : _cached.Tokens.AccessToken,
                    RefreshToken = !string.IsNullOrEmpty(refreshed.RefreshToken) ? refreshed.RefreshToken : refreshToken,
                    AccountId = _cached.Tokens.AccountId
                };

                if (!string.IsNullOrEmpty(refreshed.IdToken))
                {
                    try
                    {
                        var claims = JwtClaimsReader.Parse(refreshed.IdToken);
                        if (!string.IsNullOrEmpty(claims.AccountId))
                            newTokens.AccountId = claims.AccountId;
                    }
                    catch (Exception ex) when (ex is FormatException or JsonException)
                    {
                        _logger.LogWarning(ex, "Failed to parse refreshed id_token claims.");
                    }
                }

                var newAuth = new AuthDotJson
                {
                    OpenAIApiKey = _cached.OpenAIApiKey,
                    Tokens = newTokens,
                    LastRefresh = DateTimeOffset.UtcNow
                };
                _store.Save(newAuth);
                _cached = newAuth;
                return;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var reason = ClassifyRefreshFailure(body);
                _logger.LogWarning("OpenAI refresh failed permanently: {Status} {Body}", response.StatusCode, body);
                throw new OpenAIAuthException(reason,
                    "ChatGPT credentials are no longer valid. Please sign in again.");
            }

            _logger.LogWarning("OpenAI refresh failed transiently: {Status} {Body}", response.StatusCode, body);
            throw new OpenAIAuthException(OpenAIAuthFailureReason.Network,
                $"Failed to refresh ChatGPT access token (HTTP {(int)response.StatusCode}).");
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static bool NeedsRefresh(AuthDotJson auth)
    {
        if (auth.Tokens is null)
            return false;

        if (auth.LastRefresh is null)
            return true;

        if (DateTimeOffset.UtcNow - auth.LastRefresh.Value >= OpenAIAuthConstants.RefreshInterval)
            return true;

        var expiry = JwtClaimsReader.TryParseExpiration(auth.Tokens.AccessToken);
        if (expiry is not null && expiry.Value - DateTimeOffset.UtcNow <= TimeSpan.FromMinutes(5))
            return true;

        return false;
    }

    private static OpenAIAuthFailureReason ClassifyRefreshFailure(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return OpenAIAuthFailureReason.Unknown;

        try
        {
            using var doc = JsonDocument.Parse(body);
            string? code = null;
            if (doc.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.ValueKind == JsonValueKind.Object &&
                    errorElement.TryGetProperty("code", out var codeElement) &&
                    codeElement.ValueKind == JsonValueKind.String)
                {
                    code = codeElement.GetString();
                }
                else if (errorElement.ValueKind == JsonValueKind.String)
                {
                    code = errorElement.GetString();
                }
            }
            return code?.ToLowerInvariant() switch
            {
                "refresh_token_expired" => OpenAIAuthFailureReason.RefreshTokenExpired,
                "refresh_token_reused" => OpenAIAuthFailureReason.RefreshTokenReused,
                "refresh_token_invalidated" => OpenAIAuthFailureReason.RefreshTokenRevoked,
                _ => OpenAIAuthFailureReason.Unknown
            };
        }
        catch (JsonException)
        {
            return OpenAIAuthFailureReason.Unknown;
        }
    }

    private static string BuildAuthorizeUrl(string redirectUri, string codeChallenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = OpenAIAuthConstants.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = OpenAIAuthConstants.Scopes,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true",
            ["state"] = state,
            ["originator"] = OpenAIAuthConstants.Originator
        };

        var sb = new System.Text.StringBuilder(OpenAIAuthConstants.AuthorizeUrl);
        sb.Append('?');
        var first = true;
        foreach (var pair in query)
        {
            if (!first) sb.Append('&');
            sb.Append(Uri.EscapeDataString(pair.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(pair.Value));
            first = false;
        }
        return sb.ToString();
    }

    private async Task<TokenExchangeResponse> ExchangeCodeForTokensAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OpenAIAuthConstants.TokenUrl);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = OpenAIAuthConstants.ClientId,
            ["code_verifier"] = codeVerifier
        };
        request.Content = new FormUrlEncodedContent(form);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new OpenAIAuthException(OpenAIAuthFailureReason.Unknown,
                $"Authorization code exchange failed (HTTP {(int)response.StatusCode}). {Truncate(body, 256)}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new OpenAIAuthException(OpenAIAuthFailureReason.Unknown, "Token endpoint returned an empty response.");

        if (string.IsNullOrEmpty(parsed.AccessToken) || string.IsNullOrEmpty(parsed.IdToken) || string.IsNullOrEmpty(parsed.RefreshToken))
            throw new OpenAIAuthException(OpenAIAuthFailureReason.Unknown, "Token endpoint response was missing required fields.");

        return parsed;
    }

    private OpenAIIdTokenClaims? GetClaimsSafe()
    {
        if (_cached?.Tokens?.IdToken is null)
            return null;
        try
        {
            return JwtClaimsReader.Parse(_cached.Tokens.IdToken);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            _logger.LogDebug(ex, "Failed to parse cached id_token claims.");
            return null;
        }
    }

    private static string RandomUrlSafeToken(int byteCount)
    {
        Span<byte> bytes = stackalloc byte[64];
        if (byteCount > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        RandomNumberGenerator.Fill(bytes[..byteCount]);
        return Base64Url.Encode(bytes[..byteCount]);
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception)
        {
            // Caller is responsible for printing the URL when the browser cannot be launched.
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max] + "...";
    }

    private sealed class TokenExchangeResponse
    {
        [JsonPropertyName("id_token")] public string IdToken { get; set; } = string.Empty;
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    }
}
