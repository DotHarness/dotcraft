using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// One-shot fetcher for <see cref="OpenAIAuthConstants.ChatGptUsageUrl"/>. Reuses the auth manager
/// for tokens + account id and applies the same headers that the OpenAI client pipeline does:
/// <c>Authorization</c>, <c>chatgpt-account-id</c>, <c>originator</c>.
/// </summary>
public sealed class OpenAIUsageClient
{
    private readonly IOpenAIAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIUsageClient> _logger;

    public OpenAIUsageClient(
        IOpenAIAuthService authService,
        HttpClient? httpClient = null,
        ILogger<OpenAIUsageClient>? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _logger = logger ?? NullLogger<OpenAIUsageClient>.Instance;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DotCraft/Usage");
        return client;
    }

    /// <summary>
    /// Calls <c>GET wham/usage</c> and returns a deserialized snapshot. On HTTP 401 we
    /// force-refresh the access token and retry once; any other failure propagates as
    /// <see cref="OpenAIAuthException"/>.
    /// </summary>
    public async Task<OpenAIUsageSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        if (!_authService.IsAuthenticated)
            throw new OpenAIAuthException(OpenAIAuthFailureReason.NotSignedIn,
                "Cannot fetch ChatGPT usage — no account is signed in.");

        var snapshot = await TrySendAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (snapshot is not null)
            return snapshot;

        // 401 path — refresh once and retry.
        snapshot = await TrySendAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
        if (snapshot is not null)
            return snapshot;

        throw new OpenAIAuthException(OpenAIAuthFailureReason.Unknown,
            "ChatGPT usage endpoint returned 401 after a token refresh.");
    }

    private async Task<OpenAIUsageSnapshot?> TrySendAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var token = await _authService.GetAccessTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        var accountId = _authService.GetAccountId();

        using var request = new HttpRequestMessage(HttpMethod.Get, OpenAIAuthConstants.ChatGptUsageUrl);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.OriginatorHeader, OpenAIAuthConstants.Originator);
        if (!string.IsNullOrEmpty(accountId))
            request.Headers.TryAddWithoutValidation(OpenAIAuthConstants.AccountIdHeader, accountId);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new OpenAIAuthException(OpenAIAuthFailureReason.Network,
                "Network error while fetching ChatGPT usage.", ex);
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return null; // Caller will retry once with a refreshed token.

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("ChatGPT usage endpoint returned {Status}: {Body}", response.StatusCode, Truncate(body, 256));
                throw new OpenAIAuthException(OpenAIAuthFailureReason.Unknown,
                    $"ChatGPT usage endpoint returned HTTP {(int)response.StatusCode}.");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            UsageWire? wire;
            try
            {
                wire = await JsonSerializer.DeserializeAsync<UsageWire>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new OpenAIAuthException(OpenAIAuthFailureReason.Unknown,
                    "Could not parse ChatGPT usage response.", ex);
            }

            return Map(wire);
        }
        finally
        {
            response.Dispose();
        }
    }

    internal static OpenAIUsageSnapshot Map(UsageWire? wire)
    {
        if (wire is null)
            return new OpenAIUsageSnapshot("unknown", null, null, null, null, DateTimeOffset.UtcNow);

        var rate = wire.RateLimit;
        return new OpenAIUsageSnapshot(
            PlanType: string.IsNullOrWhiteSpace(wire.PlanType) ? "unknown" : wire.PlanType.Trim(),
            Primary: MapWindow(rate?.PrimaryWindow),
            Secondary: MapWindow(rate?.SecondaryWindow),
            Credits: MapCredits(wire.Credits),
            LimitReachedKind: NormalizeNullableString(wire.RateLimitReachedType?.Type),
            FetchedAt: DateTimeOffset.UtcNow);
    }

    private static RateLimitWindow? MapWindow(WindowWire? wire)
    {
        if (wire is null)
            return null;
        var resetAt = wire.ResetAt > 0
            ? DateTimeOffset.FromUnixTimeSeconds(wire.ResetAt)
            : DateTimeOffset.UtcNow.AddSeconds(wire.ResetAfterSeconds);
        return new RateLimitWindow(
            UsedPercent: Math.Clamp(wire.UsedPercent, 0, 100),
            WindowDuration: TimeSpan.FromSeconds(Math.Max(0, wire.LimitWindowSeconds)),
            ResetAt: resetAt);
    }

    private static CreditStatus? MapCredits(CreditsWire? wire)
    {
        if (wire is null)
            return null;
        return new CreditStatus(wire.HasCredits, wire.Unlimited, NormalizeNullableString(wire.Balance));
    }

    private static string? NormalizeNullableString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // Internal-but-testable wire types: lenient about missing fields so the snapshot survives
    // backend additions/renames without crashing.
    internal sealed class UsageWire
    {
        [JsonPropertyName("plan_type")] public string? PlanType { get; set; }
        [JsonPropertyName("rate_limit")] public RateLimitWire? RateLimit { get; set; }
        [JsonPropertyName("credits")] public CreditsWire? Credits { get; set; }
        [JsonPropertyName("rate_limit_reached_type")] public LimitReachedWire? RateLimitReachedType { get; set; }
    }

    internal sealed class RateLimitWire
    {
        [JsonPropertyName("primary_window")] public WindowWire? PrimaryWindow { get; set; }
        [JsonPropertyName("secondary_window")] public WindowWire? SecondaryWindow { get; set; }
    }

    internal sealed class WindowWire
    {
        [JsonPropertyName("used_percent")] public int UsedPercent { get; set; }
        [JsonPropertyName("limit_window_seconds")] public int LimitWindowSeconds { get; set; }
        [JsonPropertyName("reset_after_seconds")] public int ResetAfterSeconds { get; set; }
        [JsonPropertyName("reset_at")] public long ResetAt { get; set; }
    }

    internal sealed class CreditsWire
    {
        [JsonPropertyName("has_credits")] public bool HasCredits { get; set; }
        [JsonPropertyName("unlimited")] public bool Unlimited { get; set; }
        [JsonPropertyName("balance")] public string? Balance { get; set; }
    }

    internal sealed class LimitReachedWire
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
}
