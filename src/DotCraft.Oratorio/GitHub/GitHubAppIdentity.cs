using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DotCraft.Oratorio.GitHub;

public sealed class GitHubAppIdentity(
    IGitHubApiClient client,
    IOptionsMonitor<GitHubOptions> options,
    IGitHubCredentialResolver credentials,
    ILogger<GitHubAppIdentity> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CachedLogin? _cached;

    public async Task<string?> GetBotLoginAsync(CancellationToken ct)
    {
        var current = options.CurrentValue;
        if (!credentials.Resolve(current).HasAppAuthentication)
        {
            return null;
        }

        var key = $"{current.Endpoint}|{credentials.ResolveValue(current.AppId)}";
        if (_cached is { } cached && cached.Key == key)
        {
            return cached.Login;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is { } again && again.Key == key)
            {
                return again.Login;
            }

            var slug = await client.GetAuthenticatedAppSlugAsync(ct);
            var login = $"{slug}[bot]";
            _cached = new CachedLogin(key, login);
            return login;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or CryptographicException or ArgumentException or IOException)
        {
            logger.LogWarning(ex, "Oratorio could not resolve its GitHub App identity.");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static bool IsAuthoredBy(GitHubUser? user, string? botLogin) =>
        botLogin is not null && string.Equals(user?.Login, botLogin, StringComparison.OrdinalIgnoreCase);

    private sealed record CachedLogin(string Key, string Login);
}
