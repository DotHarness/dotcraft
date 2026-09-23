using System.Text.Json;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Parses ChatGPT-specific claims from an OpenAI <c>id_token</c>.
/// Only reads the payload; signature is not verified (the token issuer is trusted because we
/// just received it over TLS from auth.openai.com).
/// </summary>
public static class JwtClaimsReader
{
    public static OpenAIIdTokenClaims Parse(string jwt)
    {
        ArgumentException.ThrowIfNullOrEmpty(jwt);

        var parts = jwt.Split('.');
        if (parts.Length < 2)
            throw new FormatException("Invalid JWT: missing payload segment.");

        var payload = Base64Url.Decode(parts[1]);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        string? accountId = null;
        string? planType = null;
        string? userId = null;
        var fedramp = false;

        if (root.TryGetProperty(OpenAIAuthConstants.ChatGptAuthClaim, out var auth) &&
            auth.ValueKind == JsonValueKind.Object)
        {
            if (auth.TryGetProperty("chatgpt_account_id", out var accountIdElement) &&
                accountIdElement.ValueKind == JsonValueKind.String)
            {
                accountId = accountIdElement.GetString();
            }
            if (auth.TryGetProperty("chatgpt_plan_type", out var planElement) &&
                planElement.ValueKind == JsonValueKind.String)
            {
                planType = planElement.GetString();
            }
            if (auth.TryGetProperty("chatgpt_user_id", out var userIdElement) &&
                userIdElement.ValueKind == JsonValueKind.String)
            {
                userId = userIdElement.GetString();
            }
            if (auth.TryGetProperty("chatgpt_account_is_fedramp", out var fedrampElement) &&
                fedrampElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                fedramp = fedrampElement.GetBoolean();
            }
        }

        string? email = null;
        if (root.TryGetProperty("email", out var emailElement) && emailElement.ValueKind == JsonValueKind.String)
            email = emailElement.GetString();

        DateTimeOffset? expiresAt = null;
        if (root.TryGetProperty("exp", out var expElement) && expElement.ValueKind == JsonValueKind.Number &&
            expElement.TryGetInt64(out var exp))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp);
        }

        return new OpenAIIdTokenClaims(accountId, planType, userId, email, fedramp, expiresAt);
    }

    /// <summary>
    /// Parses just the expiration of any JWT.
    /// </summary>
    public static DateTimeOffset? TryParseExpiration(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
                return null;

            var payload = Base64Url.Decode(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("exp", out var expElement) &&
                expElement.ValueKind == JsonValueKind.Number &&
                expElement.TryGetInt64(out var exp))
            {
                return DateTimeOffset.FromUnixTimeSeconds(exp);
            }
        }
        catch (FormatException) { }
        catch (JsonException) { }
        return null;
    }
}

public sealed record OpenAIIdTokenClaims(
    string? AccountId,
    string? PlanType,
    string? UserId,
    string? Email,
    bool IsFedramp,
    DateTimeOffset? ExpiresAt);
