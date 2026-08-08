using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oratorio.Server.GitHub;

internal static class GitHubAppJwtFactory
{
    public static string CreateAppJwt(GitHubOptions options, DateTimeOffset now, IGitHubCredentialResolver credentials)
    {
        var appId = credentials.ResolveValue(options.AppId)
            ?? throw new InvalidOperationException("GitHub AppId is required.");
        var privateKeyPem = ResolvePrivateKey(options, credentials);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = now.AddSeconds(-30).ToUnixTimeSeconds(),
            exp = now.AddMinutes(9).ToUnixTimeSeconds(),
            iss = appId
        }));
        var signingInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
        var signature = rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    public static string ResolvePrivateKey(GitHubOptions options, IGitHubCredentialResolver credentials)
    {
        var inline = credentials.ResolveSecret(options.PrivateKey);
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return inline.Replace("\\n", "\n", StringComparison.Ordinal);
        }

        var path = credentials.ResolveSecret(options.PrivateKeyPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("GitHub private key or private key path is required.");
        }

        return File.ReadAllText(Path.GetFullPath(path));
    }

    public static Uri BuildUri(GitHubOptions options, string path)
    {
        var endpoint = Environment.ExpandEnvironmentVariables(options.Endpoint ?? "https://api.github.com");
        return new Uri(new Uri(endpoint.TrimEnd('/') + "/"), path.TrimStart('/'));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
