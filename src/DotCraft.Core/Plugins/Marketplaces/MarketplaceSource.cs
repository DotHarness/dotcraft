namespace DotCraft.Plugins.Marketplaces;

/// <summary>
/// Where a marketplace's document and plugin directories come from.
/// </summary>
public enum MarketplaceSourceKind
{
    /// <summary>Repository source materialized under the installed marketplace root.</summary>
    Git,

    /// <summary>Directory on this machine, read in place and never copied.</summary>
    Local,

    /// <summary>Archive URL or local archive file kept in a content-addressed snapshot cache.</summary>
    Archive
}

/// <summary>
/// A normalized marketplace source: what to fetch, at which reference, and which paths to check out.
/// </summary>
public sealed record MarketplaceSource(
    MarketplaceSourceKind Kind,
    string Value,
    string? Ref = null,
    IReadOnlyList<string>? SparsePaths = null)
{
    public IReadOnlyList<string> SparsePathList => SparsePaths ?? [];

    /// <summary>Human-readable source for diagnostics, including the reference when one is pinned.</summary>
    public string Display => Kind == MarketplaceSourceKind.Git && !string.IsNullOrEmpty(Ref)
        ? $"{Value}#{Ref}"
        : Value;

    /// <summary>True when this source and another describe the same checkout.</summary>
    public bool Matches(MarketplaceSource other) =>
        Kind == other.Kind
        && string.Equals(Value, other.Value, StringComparison.Ordinal)
        && string.Equals(Ref, other.Ref, StringComparison.Ordinal)
        && SparsePathList.SequenceEqual(other.SparsePathList, StringComparer.Ordinal);
}

/// <summary>
/// Parses and validates user-supplied marketplace sources.
/// </summary>
internal static class MarketplaceSourceParser
{
    private const string GitHubShorthandPrefix = "https://github.com/";

    /// <summary>
    /// Normalizes a user-supplied source into a marketplace source, or throws with a stable code.
    /// </summary>
    /// <param name="source">Repository shorthand, repository URL, or local directory path.</param>
    /// <param name="explicitRef">Reference supplied out of band; overrides a reference embedded in <paramref name="source"/>.</param>
    /// <param name="sparsePaths">Repository-relative paths to check out.</param>
    public static MarketplaceSource Parse(
        string? source,
        string? explicitRef = null,
        IReadOnlyList<string>? sparsePaths = null)
    {
        var trimmed = source?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw Invalid("Marketplace source must not be empty.");

        var (baseSource, embeddedRef) = SplitSourceRef(trimmed);
        var refName = Normalize(explicitRef) ?? embeddedRef;
        var paths = NormalizeSparsePaths(sparsePaths);

        if (LooksLikeLocalPath(baseSource))
        {
            if (refName != null)
                throw Invalid("A reference is only supported for repository marketplace sources.");
            if (paths.Count > 0)
                throw Invalid("Sparse paths are only supported for repository marketplace sources.");
            return new MarketplaceSource(MarketplaceSourceKind.Local, ResolveLocalPath(baseSource));
        }

        if (IsRepositoryUrl(baseSource))
        {
            EnsureNoEmbeddedCredentials(baseSource);
            return new MarketplaceSource(MarketplaceSourceKind.Git, NormalizeGitUrl(baseSource), refName, paths);
        }

        if (LooksLikeGitHubShorthand(baseSource))
            return new MarketplaceSource(MarketplaceSourceKind.Git, $"{GitHubShorthandPrefix}{baseSource}.git", refName, paths);

        throw Invalid("Marketplace source must be owner/repo, a repository URL, or a local marketplace directory.");
    }

    /// <summary>
    /// Rebuilds a source from persisted configuration. Unlike <see cref="Parse"/> this does not
    /// require a local directory to exist, so a marketplace whose directory disappeared still
    /// round-trips through configuration.
    /// </summary>
    public static MarketplaceSource FromConfigured(
        string? sourceType,
        string value,
        string? refName,
        IReadOnlyList<string>? sparsePaths)
    {
        var kind = ParseKind(sourceType, value);
        return kind switch
        {
            MarketplaceSourceKind.Git => new MarketplaceSource(
                MarketplaceSourceKind.Git,
                value.Trim(),
                Normalize(refName),
                NormalizeSparsePaths(sparsePaths)),
            MarketplaceSourceKind.Local => new MarketplaceSource(MarketplaceSourceKind.Local, value.Trim()),
            _ => new MarketplaceSource(MarketplaceSourceKind.Archive, value.Trim())
        };
    }

    private static MarketplaceSourceKind ParseKind(string? sourceType, string value)
    {
        var normalized = Normalize(sourceType)?.ToLowerInvariant();
        if (normalized == "git")
            return MarketplaceSourceKind.Git;
        if (normalized == "local")
            return MarketplaceSourceKind.Local;
        if (normalized == "archive")
            return MarketplaceSourceKind.Archive;

        // Legacy entries carry no kind: an existing directory or archive file is a local
        // snapshot, and anything else is an archive URL.
        var trimmed = value.Trim();
        return Directory.Exists(trimmed) || File.Exists(trimmed)
            ? MarketplaceSourceKind.Local
            : MarketplaceSourceKind.Archive;
    }

    private static (string BaseSource, string? Ref) SplitSourceRef(string source)
    {
        var hashIndex = source.LastIndexOf('#');
        if (hashIndex > 0)
            return (source[..hashIndex], Normalize(source[(hashIndex + 1)..]));

        // `owner/repo@ref` is a reference suffix, but `git@host:team/repo.git` is an address.
        if (!source.Contains("://", StringComparison.Ordinal) && !IsScpLikeUrl(source))
        {
            var atIndex = source.LastIndexOf('@');
            if (atIndex > 0)
                return (source[..atIndex], Normalize(source[(atIndex + 1)..]));
        }

        return (source, null);
    }

    private static IReadOnlyList<string> NormalizeSparsePaths(IReadOnlyList<string>? sparsePaths)
    {
        if (sparsePaths == null)
            return [];

        var result = new List<string>();
        foreach (var raw in sparsePaths)
        {
            var path = raw?.Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(path))
                continue;

            if (Path.IsPathRooted(path) || path.StartsWith('/'))
                throw Invalid($"Sparse path '{path}' must be relative to the repository root.");
            if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
                throw Invalid($"Sparse path '{path}' must not contain '..'.");

            if (!result.Contains(path, StringComparer.Ordinal))
                result.Add(path);
        }

        return result;
    }

    private static string ResolveLocalPath(string source)
    {
        var expanded = ExpandHome(source);
        string resolved;
        try
        {
            resolved = Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Invalid($"Local marketplace source path could not be resolved: {ex.Message}");
        }

        if (File.Exists(resolved))
            throw Invalid("Local marketplace source must be a directory, not a file.");
        if (!Directory.Exists(resolved))
            throw Invalid($"Local marketplace source directory does not exist: {resolved}");

        return resolved;
    }

    private static string ExpandHome(string source)
    {
        if (!source.StartsWith("~/", StringComparison.Ordinal) && !source.StartsWith("~\\", StringComparison.Ordinal))
            return source;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? source : Path.Combine(home, source[2..]);
    }

    // A password in the URL would be persisted to configuration in clear text, so it is always
    // rejected. A bare user name is rejected for web transports too, where it is a credential
    // hint rather than part of the address; `ssh://git@host/...` keeps its standard account name.
    private static void EnsureNoEmbeddedCredentials(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
            return;

        var isWebTransport = uri.Scheme is "http" or "https";
        if (isWebTransport || uri.UserInfo.Contains(':', StringComparison.Ordinal))
            throw Invalid("Marketplace source must not carry embedded credentials.");
    }

    private static string NormalizeGitUrl(string url)
    {
        var trimmed = url.TrimEnd('/');
        return trimmed.StartsWith(GitHubShorthandPrefix, StringComparison.OrdinalIgnoreCase)
               && !trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? $"{trimmed}.git"
            : trimmed;
    }

    private static bool LooksLikeLocalPath(string source) =>
        source is "." or ".."
        || source.StartsWith("./", StringComparison.Ordinal)
        || source.StartsWith(".\\", StringComparison.Ordinal)
        || source.StartsWith("../", StringComparison.Ordinal)
        || source.StartsWith("..\\", StringComparison.Ordinal)
        || source.StartsWith("~/", StringComparison.Ordinal)
        || source.StartsWith("~\\", StringComparison.Ordinal)
        || source.StartsWith(@"\\", StringComparison.Ordinal)
        || source.StartsWith('/')
        || LooksLikeWindowsAbsolutePath(source);

    // Windows drive paths must be recognized on every host so a source authored on Windows is
    // never mistaken for a repository shorthand elsewhere.
    private static bool LooksLikeWindowsAbsolutePath(string source) =>
        source.Length >= 3
        && char.IsAsciiLetter(source[0])
        && source[1] == ':'
        && source[2] is '\\' or '/';

    private static bool IsRepositoryUrl(string source) =>
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
        || IsScpLikeUrl(source);

    private static bool IsScpLikeUrl(string source)
    {
        var atIndex = source.IndexOf('@');
        if (atIndex <= 0)
            return false;
        var colonIndex = source.IndexOf(':', atIndex);
        return colonIndex > atIndex + 1;
    }

    private static bool LooksLikeGitHubShorthand(string source)
    {
        var segments = source.Split('/');
        return segments.Length == 2
               && segments.All(IsShorthandSegment);
    }

    private static bool IsShorthandSegment(string segment) =>
        segment.Length > 0
        && segment.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static MarketplaceException Invalid(string message) =>
        new(MarketplaceErrorCodes.SourceInvalid, message);
}
