using System.Text;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using Xunit;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class JwtClaimsReaderTests
{
    [Fact]
    public void ParsesChatGptAccountAndPlanFromAuthClaim()
    {
        var payload = new
        {
            email = "user@example.com",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            chatgpt_auth = "ignored",
        };

        var jwt = BuildJwt(new Dictionary<string, object>
        {
            ["email"] = "user@example.com",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            ["https://api.openai.com/auth"] = new Dictionary<string, object>
            {
                ["chatgpt_account_id"] = "acct_test",
                ["chatgpt_plan_type"] = "plus",
                ["chatgpt_user_id"] = "user_test",
                ["chatgpt_account_is_fedramp"] = false
            }
        });

        var claims = JwtClaimsReader.Parse(jwt);
        Assert.Equal("acct_test", claims.AccountId);
        Assert.Equal("plus", claims.PlanType);
        Assert.Equal("user_test", claims.UserId);
        Assert.Equal("user@example.com", claims.Email);
        Assert.False(claims.IsFedramp);
        Assert.NotNull(claims.ExpiresAt);
    }

    [Fact]
    public void GracefullyReturnsNullsWhenAuthClaimMissing()
    {
        var jwt = BuildJwt(new Dictionary<string, object>
        {
            ["sub"] = "user",
            ["exp"] = 1
        });
        var claims = JwtClaimsReader.Parse(jwt);
        Assert.Null(claims.AccountId);
        Assert.Null(claims.PlanType);
    }

    [Fact]
    public void TryParseExpirationReadsExpClaim()
    {
        var exp = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        var jwt = BuildJwt(new Dictionary<string, object> { ["exp"] = exp });
        var parsed = JwtClaimsReader.TryParseExpiration(jwt);
        Assert.NotNull(parsed);
        Assert.Equal(exp, parsed!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void TryParseExpirationReturnsNullForGarbage()
    {
        Assert.Null(JwtClaimsReader.TryParseExpiration("not.a.jwt"));
        Assert.Null(JwtClaimsReader.TryParseExpiration("xxx"));
    }

    private static string BuildJwt(IDictionary<string, object> payload)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        var body = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        return $"{header}.{body}.signature-not-verified";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
