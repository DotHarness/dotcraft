using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Oratorio.Server.Services;

namespace Oratorio.Server.GitHub;

public interface IGitHubTokenProvider
{
    Task<string?> GetBearerTokenAsync(GitHubRepositoryRef repository, CancellationToken ct);
}

public sealed class GitHubTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<GitHubOptions> options,
    IClock clock,
    IGitHubCredentialResolver credentials,
    IGitHubInstallationResolver installations)
    : IGitHubTokenProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CachedInstallationToken> _installationTokens = new(StringComparer.Ordinal);

    public async Task<string?> GetBearerTokenAsync(GitHubRepositoryRef repository, CancellationToken ct)
    {
        var current = options.CurrentValue;
        var status = credentials.Resolve(current);
        if (!status.HasAppAuthentication)
        {
            throw new GitHubAppAuthenticationRequiredException();
        }

        var installationId = await installations.ResolveInstallationIdAsync(current, repository, ct)
            ?? throw new InvalidOperationException($"GitHub installation profile is missing for {repository.Owner}.");

        if (_installationTokens.TryGetValue(installationId, out var cached) &&
            cached.ExpiresAt > clock.UtcNow.AddMinutes(5))
        {
            return cached.Token;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_installationTokens.TryGetValue(installationId, out cached) &&
                cached.ExpiresAt > clock.UtcNow.AddMinutes(5))
            {
                return cached.Token;
            }

            var jwt = GitHubAppJwtFactory.CreateAppJwt(current, clock.UtcNow, credentials);
            using var request = new HttpRequestMessage(HttpMethod.Post, GitHubAppJwtFactory.BuildUri(current, $"/app/installations/{installationId}/access_tokens"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("Oratorio-GitHubSource");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

            using var response = await httpClientFactory.CreateClient("GitHub").SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<GitHubInstallationTokenResponse>(JsonOptions, ct)
                ?? throw new InvalidOperationException("GitHub did not return an installation token.");
            _installationTokens[installationId] = new CachedInstallationToken(token.Token, token.ExpiresAt);
            return token.Token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record CachedInstallationToken(string Token, DateTimeOffset ExpiresAt);
}
