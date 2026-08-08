namespace Oratorio.Server.Sources;

/// <summary>
/// Represents a source-neutral project identity used for provider routing.
/// </summary>
public sealed record SourceProjectKey(string Provider, string Instance, string ProjectPath)
{
    public string Key => $"{Provider}:{Instance}/{ProjectPath}";

    public override string ToString() => Key;

    public static bool TryParse(string? value, out SourceProjectKey key)
    {
        key = new SourceProjectKey("", "", "");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Replace('\\', '/');
        var separator = normalized.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == normalized.Length - 1)
        {
            return false;
        }

        var provider = normalized[..separator].Trim().ToLowerInvariant();
        var rest = normalized[(separator + 1)..].Trim('/');
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == rest.Length - 1)
        {
            return false;
        }

        var instance = rest[..slash].Trim().ToLowerInvariant();
        var projectPath = NormalizeProjectPath(rest[(slash + 1)..]);
        if (!IsValidSegment(provider) || string.IsNullOrWhiteSpace(instance) || !IsValidProjectPath(projectPath))
        {
            return false;
        }

        key = new SourceProjectKey(provider, instance, projectPath);
        return true;
    }

    public static SourceProjectKey FromGitHubRepository(string repository, string? endpoint = null)
    {
        var projectPath = NormalizeGitHubRepository(repository);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("Repository must be in owner/name form.", nameof(repository));
        }

        return new SourceProjectKey("github", ResolveGitHubInstance(endpoint), projectPath);
    }

    public static SourceProjectKey FromGitLabProject(string projectPath, string? endpoint = null)
    {
        var normalizedPath = NormalizeGitLabProjectPath(projectPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("Project path must be a non-empty slash-separated GitLab path.", nameof(projectPath));
        }

        return new SourceProjectKey("gitlab", ResolveGitLabInstance(endpoint), normalizedPath);
    }

    public static bool TryNormalizeForProvider(string provider, string? value, string? endpoint, out SourceProjectKey key)
    {
        key = new SourceProjectKey("", "", "");
        if (TryParse(value, out var parsed))
        {
            if (!string.Equals(parsed.Provider, provider, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            key = parsed;
            return true;
        }

        if (string.Equals(provider, "github", StringComparison.OrdinalIgnoreCase))
        {
            var repository = NormalizeGitHubRepository(value);
            if (string.IsNullOrWhiteSpace(repository))
            {
                return false;
            }

            key = FromGitHubRepository(repository, endpoint);
            return true;
        }

        if (string.Equals(provider, "gitlab", StringComparison.OrdinalIgnoreCase))
        {
            var projectPath = NormalizeGitLabProjectPath(value);
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return false;
            }

            key = FromGitLabProject(projectPath, endpoint);
            return true;
        }

        return false;
    }

    public static bool AreEquivalent(string? left, string? right)
    {
        var leftCandidates = RoutingCandidates(left);
        var rightCandidates = RoutingCandidates(right);
        return leftCandidates.Count > 0 &&
            rightCandidates.Count > 0 &&
            leftCandidates.Overlaps(rightCandidates);
    }

    public static IReadOnlyCollection<string> RepositoryQueryCandidates(string? value) =>
        RoutingCandidates(value).ToArray();

    public static string? NormalizeGitHubRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return null;
        }

        var value = repository.Trim().Replace('\\', '/');
        if (TryParse(value, out var parsed) &&
            string.Equals(parsed.Provider, "github", StringComparison.OrdinalIgnoreCase))
        {
            value = parsed.ProjectPath;
        }
        else if (value.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            value = value["https://github.com/".Length..];
        }
        else if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            value = value["git@github.com:".Length..];
        }

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        value = NormalizeProjectPath(value);
        return IsOwnerName(value) ? value : null;
    }

    public static string ResolveGitHubInstance(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "github.com";
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return "github.com";
        }

        return string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
            ? "github.com"
            : uri.Host.ToLowerInvariant();
    }

    public static string? NormalizeGitLabProjectPath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        var value = projectPath.Trim().Replace('\\', '/');
        if (TryParse(value, out var parsed) &&
            string.Equals(parsed.Provider, "gitlab", StringComparison.OrdinalIgnoreCase))
        {
            value = parsed.ProjectPath;
        }
        else if (value.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            var separator = value.IndexOf(':', StringComparison.Ordinal);
            if (separator >= 0 && separator < value.Length - 1)
            {
                value = value[(separator + 1)..];
            }
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = uri.AbsolutePath.Trim('/');
        }

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        value = NormalizeProjectPath(value);
        return IsValidProjectPath(value) && value.Contains('/', StringComparison.Ordinal) ? value : null;
    }

    public static string ResolveGitLabInstance(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "gitlab.com";
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            return "gitlab.com";
        }

        return uri.Host.ToLowerInvariant();
    }

    private static HashSet<string> RoutingCandidates(string? value)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return candidates;
        }

        var normalized = value.Trim().Replace('\\', '/');
        if (TryParse(normalized, out var parsed))
        {
            candidates.Add(parsed.Key);
            if (string.Equals(parsed.Provider, "github", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(parsed.ProjectPath);
                candidates.Add(new SourceProjectKey("github", "github.com", parsed.ProjectPath).Key);
            }

            return candidates;
        }

        var repository = NormalizeGitHubRepository(normalized);
        if (!string.IsNullOrWhiteSpace(repository))
        {
            candidates.Add(repository);
            candidates.Add(new SourceProjectKey("github", "github.com", repository).Key);
        }

        return candidates;
    }

    private static string NormalizeProjectPath(string value) =>
        value.Trim().Replace('\\', '/').Trim('/');

    private static bool IsOwnerName(string value)
    {
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts.All(IsValidSegment);
    }

    private static bool IsValidProjectPath(string value)
    {
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(IsValidSegment);
    }

    private static bool IsValidSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Contains("..", StringComparison.Ordinal) &&
        !value.Contains(':', StringComparison.Ordinal) &&
        !value.Contains('\\', StringComparison.Ordinal);
}
