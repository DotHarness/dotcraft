using System.Security.Cryptography;
using System.Text;
using DotCraft.Auth.OpenAI;
using Xunit;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class PkceTests
{
    [Fact]
    public void CodeChallengeIsBase64UrlSha256OfAUniqueUnreservedVerifier()
    {
        var verifier = Pkce.CreateCodeVerifier();
        var challenge = Pkce.CreateS256Challenge(verifier);

        Assert.Equal(Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), challenge);
        Assert.NotEqual(verifier, Pkce.CreateCodeVerifier());
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

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
