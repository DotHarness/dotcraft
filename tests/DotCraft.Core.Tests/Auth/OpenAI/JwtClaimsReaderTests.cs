using System.Text;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using Xunit;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class JwtClaimsReaderTests
{
    [Fact]
    public void ParsesChatGptAccountAndPlanFromAuthClaimAndToleratesItsAbsence()
    {
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

        var withoutAuthClaim = JwtClaimsReader.Parse(BuildJwt(new Dictionary<string, object> { ["sub"] = "user", ["exp"] = 1 }));
        Assert.Null(withoutAuthClaim.AccountId);
        Assert.Null(withoutAuthClaim.PlanType);
    }

    [Fact]
    public void TryParseExpirationReadsExpClaimAndReturnsNullForGarbage()
    {
        var exp = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        var jwt = BuildJwt(new Dictionary<string, object> { ["exp"] = exp });
        var parsed = JwtClaimsReader.TryParseExpiration(jwt);
        Assert.NotNull(parsed);
        Assert.Equal(exp, parsed!.Value.ToUnixTimeSeconds());
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
