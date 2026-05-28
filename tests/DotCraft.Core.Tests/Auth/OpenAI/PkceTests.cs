using System.Security.Cryptography;
using System.Text;
using DotCraft.Auth.OpenAI;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class PkceTests
{
    [Fact]
    public void CodeChallengeIsBase64UrlSha256OfVerifier()
    {
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = Pkce.CreateS256Challenge(verifier);

        var expected = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        Assert.Equal(expected, challenge);
    }

    [Fact]
    public void GeneratedVerifierIsUnreservedBase64UrlCharsOnly()
    {
        var verifier = Pkce.CreateCodeVerifier();
        Assert.InRange(verifier.Length, 43, 128);
        Assert.DoesNotContain('=', verifier);
        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
        foreach (var c in verifier)
        {
            Assert.True(char.IsLetterOrDigit(c) || c is '-' or '_',
                $"Unexpected verifier char: {c}");
        }
    }

    [Fact]
    public void GeneratedVerifiersAreUnique()
    {
        var first = Pkce.CreateCodeVerifier();
        var second = Pkce.CreateCodeVerifier();
        Assert.NotEqual(first, second);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
