using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// PKCE (RFC 7636) helper. Produces a high-entropy code_verifier and the
/// base64url-encoded SHA-256 of that verifier for use as code_challenge.
/// </summary>
internal static class Pkce
{
    /// <summary>
    /// Generates a 64-byte random code_verifier, base64url-encoded.
    /// </summary>
    public static string CreateCodeVerifier()
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.Encode(bytes);
    }

    /// <summary>
    /// Computes the S256 challenge for a given verifier.
    /// </summary>
    public static string CreateS256Challenge(string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifier);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url.Encode(hash);
    }
}

/// <summary>
/// Minimal base64url codec without padding.
/// </summary>
internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var b64 = Convert.ToBase64String(bytes);
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Decode(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
