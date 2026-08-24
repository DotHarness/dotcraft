using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using DotCraft.Oratorio.Sources;
using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.GitHub;

public sealed record GitHubInstallationDiscoveryResult(
    bool Succeeded,
    string Instance,
    string Owner,
    string Repository,
    string? InstallationId,
    string? Code,
    string? Message)
{
    public static GitHubInstallationDiscoveryResult Success(string instance, string owner, string repository, string installationId) =>
        new(true, instance, owner, repository, installationId, null, null);

    public static GitHubInstallationDiscoveryResult Failure(string instance, string owner, string repository, string code, string message) =>
        new(false, instance, owner, repository, null, code, message);
}

public interface IGitHubInstallationResolver
{
    string? ResolveConfiguredInstallationId(GitHubOptions options, GitHubRepositoryRef repository);
    Task<string?> ResolveInstallationIdAsync(GitHubOptions options, GitHubRepositoryRef repository, CancellationToken ct);
    Task<GitHubInstallationDiscoveryResult> DiscoverAsync(GitHubOptions options, GitHubRepositoryRef repository, CancellationToken ct);
}

public sealed class GitHubInstallationResolver(
    IHttpClientFactory httpClientFactory,
    IClock clock,
    IGitHubCredentialResolver credentials)
    : IGitHubInstallationResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public string? ResolveConfiguredInstallationId(GitHubOptions options, GitHubRepositoryRef repository)
    {
        var instance = SourceProjectKey.ResolveGitHubInstance(options.Endpoint);
        foreach (var profile in EffectiveProfiles(options))
        {
            if (string.Equals(NormalizeInstance(profile.Instance), instance, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(profile.Owner?.Trim(), repository.Owner, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(profile.InstallationId))
            {
                return credentials.ResolveValue(profile.InstallationId);
            }
        }

        return null;
    }

    public async Task<string?> ResolveInstallationIdAsync(GitHubOptions options, GitHubRepositoryRef repository, CancellationToken ct)
    {
        var configured = ResolveConfiguredInstallationId(options, repository);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var status = credentials.Resolve(options);
        if (!status.HasAppAuthentication)
        {
            return null;
        }

        var discovered = await DiscoverAsync(options, repository, ct);
        if (!discovered.Succeeded || string.IsNullOrWhiteSpace(discovered.InstallationId))
        {
            throw new InvalidOperationException(discovered.Message ?? $"GitHub installation profile is missing for {discovered.Instance}/{discovered.Owner}.");
        }

        return discovered.InstallationId;
    }

    public async Task<GitHubInstallationDiscoveryResult> DiscoverAsync(GitHubOptions discoveryOptions, GitHubRepositoryRef repository, CancellationToken ct)
    {
        var instance = SourceProjectKey.ResolveGitHubInstance(discoveryOptions.Endpoint);
        var status = credentials.Resolve(discoveryOptions);
        if (!status.HasAppAuthentication)
        {
            return GitHubInstallationDiscoveryResult.Failure(
                instance,
                repository.Owner,
                repository.FullName,
                "githubAppAuthRequired",
                "GitHub App ID and private key are required to detect installation profiles.");
        }

        try
        {
            var jwt = GitHubAppJwtFactory.CreateAppJwt(discoveryOptions, clock.UtcNow, credentials);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                GitHubAppJwtFactory.BuildUri(discoveryOptions, $"/repos/{repository.Owner}/{repository.Name}/installation"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("Oratorio-GitHubSource");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

            using var response = await httpClientFactory.CreateClient("GitHub").SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return GitHubInstallationDiscoveryResult.Failure(
                    instance,
                    repository.Owner,
                    repository.FullName,
                    "githubInstallationDiscoveryFailed",
                    $"GitHub installation discovery failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {responseText}");
            }

            var installation = JsonSerializer.Deserialize<GitHubRepositoryInstallationResponse>(responseText, JsonOptions);
            if (installation?.Id is null or <= 0)
            {
                return GitHubInstallationDiscoveryResult.Failure(
                    instance,
                    repository.Owner,
                    repository.FullName,
                    "githubInstallationDiscoveryInvalidResponse",
                    "GitHub installation discovery did not return an installation id.");
            }

            return GitHubInstallationDiscoveryResult.Success(instance, repository.Owner, repository.FullName, installation.Id.Value.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException or FormatException or IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            return GitHubInstallationDiscoveryResult.Failure(
                instance,
                repository.Owner,
                repository.FullName,
                "githubInstallationDiscoveryFailed",
                ex.Message);
        }
    }

    private static IEnumerable<GitHubInstallationProfileOptions> EffectiveProfiles(GitHubOptions options)
    {
        foreach (var profile in options.InstallationProfiles ?? [])
        {
            if (!string.IsNullOrWhiteSpace(profile.Owner) &&
                !string.IsNullOrWhiteSpace(profile.InstallationId))
            {
                yield return profile;
            }
        }
    }

    private static string NormalizeInstance(string? instance) =>
        string.IsNullOrWhiteSpace(instance) ? "github.com" : instance.Trim().ToLowerInvariant();

    private sealed record GitHubRepositoryInstallationResponse(long? Id);
}
