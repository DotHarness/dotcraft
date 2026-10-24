using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DotCraft.Oratorio.GitLab;

public sealed class GitLabTokenIdentity(
    IGitLabApiClient client,
    IOptionsMonitor<GitLabOptions> options,
    IGitLabCredentialResolver credentials,
    ILogger<GitLabTokenIdentity> logger)
{
    private readonly ConcurrentDictionary<string, long> _userIds = new(StringComparer.Ordinal);

    public async Task<long?> GetUserIdAsync(GitLabProjectRef project, CancellationToken ct)
    {
        var token = credentials.ResolveToken(options.CurrentValue, project);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (_userIds.TryGetValue(token, out var cached))
        {
            return cached;
        }

        try
        {
            var user = await client.GetTokenUserAsync(project, ct);
            if (user.Id is not { } id)
            {
                return null;
            }

            _userIds[token] = id;
            return id;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Oratorio could not resolve the GitLab user for {Project}.", project.ProjectPath);
            return null;
        }
    }

    public static bool IsAuthoredBy(GitLabUser? user, long? userId) =>
        userId is not null && user?.Id == userId;
}
