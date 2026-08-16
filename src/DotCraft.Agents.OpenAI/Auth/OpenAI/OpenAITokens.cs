using System.Text.Json.Serialization;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// On-disk shape of <c>auth.json</c> in the configured user data root.
/// </summary>
public sealed class AuthDotJson
{
    [JsonPropertyName("OPENAI_API_KEY")]
    public string? OpenAIApiKey { get; set; }

    [JsonPropertyName("tokens")]
    public OpenAITokenSet? Tokens { get; set; }

    [JsonPropertyName("last_refresh")]
    public DateTimeOffset? LastRefresh { get; set; }
}

/// <summary>
/// Triple of OAuth tokens for one ChatGPT account.
/// </summary>
public sealed class OpenAITokenSet
{
    [JsonPropertyName("id_token")]
    public string IdToken { get; set; } = string.Empty;

    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }
}

/// <summary>
/// Snapshot returned to UI/CLI for status display.
/// </summary>
public sealed record OpenAIAuthStatus(
    bool LoggedIn,
    string? AccountId,
    string? PlanType,
    string? Email,
    DateTimeOffset? LastRefresh,
    DateTimeOffset? AccessTokenExpiresAt);
