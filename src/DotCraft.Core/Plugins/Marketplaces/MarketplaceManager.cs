using DotCraft.Configuration;
using Microsoft.Extensions.Logging;

namespace DotCraft.Plugins.Marketplaces;

/// <summary>
/// A configured marketplace and the state of its materialized root.
/// </summary>
public sealed record MarketplaceEntry(
    string Name,
    string? DisplayName,
    MarketplaceSourceKind Kind,
    string Source,
    string? Ref,
    IReadOnlyList<string> SparsePaths,
    string MarketplacePath,
    string? Root,
    string? LastUpdated,
    string? Revision,
    bool Removable);

public sealed record MarketplaceAddRequest(
    string Source,
    string? Ref = null,
    IReadOnlyList<string>? SparsePaths = null,
    string? MarketplacePath = null);

public sealed record MarketplaceAddOutcome(MarketplaceEntry Marketplace, bool AlreadyAdded);

public sealed record MarketplaceRemoveOutcome(string Name, string? RemovedRoot);

public sealed record MarketplaceFailure(string Name, string Code, string Message);

public sealed record MarketplaceRefreshOutcome(
    IReadOnlyList<MarketplaceEntry> Marketplaces,
    IReadOnlyList<MarketplaceFailure> Errors);

/// <summary>
/// Adds, refreshes, and removes the user's plugin marketplace sources.
/// Sources are recorded once for the user; installing a plugin from a marketplace stays per workspace.
/// </summary>
public sealed class MarketplaceManager
{
    private readonly string _craftHome;
    private readonly string _configPath;
    private readonly IMarketplaceGitFetcher _fetcher;
    private readonly ILogger? _logger;

    public MarketplaceManager(
        string? craftHome = null,
        string? configPath = null,
        IMarketplaceGitFetcher? fetcher = null,
        ILogger? logger = null)
    {
        _craftHome = string.IsNullOrWhiteSpace(craftHome)
            ? MarketplacePaths.DefaultCraftHome()
            : Path.GetFullPath(craftHome);
        _configPath = string.IsNullOrWhiteSpace(configPath)
            ? Path.Combine(_craftHome, "config.json")
            : Path.GetFullPath(configPath);
        _fetcher = fetcher ?? new MarketplaceGitFetcher(logger);
        _logger = logger;
    }

    /// <summary>Lists configured marketplaces, newest configuration order preserved.</summary>
    public IReadOnlyList<MarketplaceEntry> List() =>
        PluginsConfigPersistence.ReadPluginRegistries(_configPath)
            .Select(ToEntry)
            .Where(entry => entry != null)
            .Select(entry => entry!)
            .ToList();

    /// <summary>
    /// Adds a marketplace from a repository source or a local directory. The marketplace name comes
    /// from the fetched marketplace document, never from the request.
    /// </summary>
    public async Task<MarketplaceAddOutcome> AddAsync(MarketplaceAddRequest request, CancellationToken ct)
    {
        var source = MarketplaceSourceParser.Parse(request.Source, request.Ref, request.SparsePaths);
        var marketplacePath = NormalizeMarketplacePath(request.MarketplacePath);
        var configured = PluginsConfigPersistence.ReadPluginRegistries(_configPath).ToList();

        var existingIndex = configured.FindIndex(entry => MatchesSource(entry, source, marketplacePath));
        if (existingIndex >= 0 && TryValidateConfiguredRoot(configured[existingIndex], out var existingName, out var existingDisplayName))
        {
            var refreshed = Record(configured, existingIndex, existingName, existingDisplayName, source, marketplacePath, configured[existingIndex].LastRevision);
            return new MarketplaceAddOutcome(refreshed, AlreadyAdded: true);
        }

        if (source.Kind == MarketplaceSourceKind.Local)
        {
            var (name, displayName) = MarketplaceDocumentLoader.ValidateRoot(source.Value, marketplacePath);
            EnsureNoNameConflict(configured, name, source, marketplacePath);
            var index = configured.FindIndex(entry => IsSameName(entry, name));
            return new MarketplaceAddOutcome(
                Record(configured, index, name, displayName, source, marketplacePath, revision: null),
                AlreadyAdded: false);
        }

        MarketplaceStore.CleanStagingDirectories(_craftHome);
        var staging = MarketplaceStore.CreateStagingDirectory(_craftHome);
        try
        {
            var revision = await _fetcher.FetchAsync(source, staging, ct).ConfigureAwait(false);
            var (name, displayName) = MarketplaceDocumentLoader.ValidateRoot(staging, marketplacePath);
            EnsureNoNameConflict(configured, name, source, marketplacePath);

            var destination = MarketplaceStore.ResolveRoot(_craftHome, name);
            ReplaceRootOrThrow(staging, destination);

            var index = configured.FindIndex(entry => IsSameName(entry, name));
            return new MarketplaceAddOutcome(
                Record(configured, index, name, displayName, source, marketplacePath, revision),
                AlreadyAdded: false);
        }
        finally
        {
            MarketplaceStore.TryDeleteDirectory(staging);
        }
    }

    /// <summary>Removes a configured marketplace and, for materialized kinds, its installed root.</summary>
    public MarketplaceRemoveOutcome Remove(string? name)
    {
        var marketplaceName = name?.Trim();
        if (string.IsNullOrEmpty(marketplaceName))
            throw new MarketplaceException(MarketplaceErrorCodes.NotFound, "Marketplace name is required.");

        var configured = PluginsConfigPersistence.ReadPluginRegistries(_configPath).ToList();
        var index = configured.FindIndex(entry => IsSameName(entry, marketplaceName));
        if (index < 0)
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.NotFound,
                $"Marketplace '{marketplaceName}' is not configured.");
        }

        var removed = configured[index];
        configured.RemoveAt(index);
        PluginsConfigPersistence.WritePluginRegistries(_configPath, configured);

        var source = MarketplaceSourceParser.FromConfigured(removed.SourceType, removed.Url!, removed.Ref, removed.SparsePaths);
        if (IsArchiveSource(source))
        {
            var removedRoot = new PluginRegistryArchiveCache(
                _craftHome,
                (path, message) => _logger?.LogWarning(
                    "{Message} Path: {Path}",
                    message,
                    path)).Remove(
                source.Value,
                NormalizeMarketplacePath(removed.MarketplacePath),
                marketplaceName);
            return new MarketplaceRemoveOutcome(marketplaceName, removedRoot);
        }

        if (source.Kind != MarketplaceSourceKind.Git)
            return new MarketplaceRemoveOutcome(marketplaceName, RemovedRoot: null);

        var root = MarketplaceStore.ResolveRoot(_craftHome, marketplaceName);
        var existed = Directory.Exists(root);
        MarketplaceStore.TryDeleteDirectory(root);
        return new MarketplaceRemoveOutcome(marketplaceName, existed ? root : null);
    }

    /// <summary>
    /// Re-fetches one marketplace, or every configured marketplace when <paramref name="name"/> is omitted.
    /// A failure for one marketplace is reported without failing the others.
    /// </summary>
    public async Task<MarketplaceRefreshOutcome> RefreshAsync(string? name, CancellationToken ct)
    {
        var configured = PluginsConfigPersistence.ReadPluginRegistries(_configPath).ToList();
        var marketplaceName = name?.Trim();
        var targets = string.IsNullOrEmpty(marketplaceName)
            ? Enumerable.Range(0, configured.Count).ToList()
            : [configured.FindIndex(entry => IsSameName(entry, marketplaceName))];

        if (!string.IsNullOrEmpty(marketplaceName) && targets[0] < 0)
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.NotFound,
                $"Marketplace '{marketplaceName}' is not configured.");
        }

        var refreshed = new List<MarketplaceEntry>();
        var errors = new List<MarketplaceFailure>();
        foreach (var index in targets)
        {
            var entry = configured[index];
            var entryName = entry.Name?.Trim() ?? string.Empty;
            try
            {
                refreshed.Add(await RefreshOneAsync(configured, index, ct).ConfigureAwait(false));
            }
            catch (MarketplaceException ex)
            {
                _logger?.LogWarning(ex, "Failed to refresh marketplace {Marketplace}.", entryName);
                errors.Add(new MarketplaceFailure(entryName, ex.Code, ex.Message));
            }
        }

        return new MarketplaceRefreshOutcome(refreshed, errors);
    }

    private async Task<MarketplaceEntry> RefreshOneAsync(
        List<AppConfig.PluginRegistryConfig> configured,
        int index,
        CancellationToken ct)
    {
        var entry = configured[index];
        var entryName = entry.Name?.Trim();
        if (string.IsNullOrEmpty(entryName) || string.IsNullOrWhiteSpace(entry.Url))
            throw new MarketplaceException(MarketplaceErrorCodes.SourceInvalid, "Marketplace entry is incomplete.");

        var marketplacePath = NormalizeMarketplacePath(entry.MarketplacePath);
        var source = MarketplaceSourceParser.FromConfigured(entry.SourceType, entry.Url, entry.Ref, entry.SparsePaths);

        if (IsArchiveSource(source))
        {
            PluginSourceRegistryCatalog.InvalidateArchiveCache(source.Value, marketplacePath, _craftHome);
            return Record(configured, index, entryName, null, source with { Kind = MarketplaceSourceKind.Archive }, marketplacePath, revision: null);
        }

        if (source.Kind == MarketplaceSourceKind.Local)
        {
            var (name, displayName) = MarketplaceDocumentLoader.ValidateRoot(source.Value, marketplacePath);
            EnsureRefreshKeepsName(entryName, name);
            return Record(configured, index, name, displayName, source, marketplacePath, revision: null);
        }

        MarketplaceStore.CleanStagingDirectories(_craftHome);
        var staging = MarketplaceStore.CreateStagingDirectory(_craftHome);
        try
        {
            var revision = await _fetcher.FetchAsync(source, staging, ct).ConfigureAwait(false);
            var (name, displayName) = MarketplaceDocumentLoader.ValidateRoot(staging, marketplacePath);
            EnsureRefreshKeepsName(entryName, name);
            ReplaceRootOrThrow(staging, MarketplaceStore.ResolveRoot(_craftHome, name));
            return Record(configured, index, name, displayName, source, marketplacePath, revision);
        }
        finally
        {
            MarketplaceStore.TryDeleteDirectory(staging);
        }
    }

    private MarketplaceEntry Record(
        List<AppConfig.PluginRegistryConfig> configured,
        int index,
        string name,
        string? displayName,
        MarketplaceSource source,
        string marketplacePath,
        string? revision)
    {
        var entry = new AppConfig.PluginRegistryConfig
        {
            Name = name,
            SourceType = ToConfigSourceType(source.Kind),
            Url = source.Value,
            Ref = source.Kind == MarketplaceSourceKind.Git ? source.Ref : null,
            SparsePaths = source.Kind == MarketplaceSourceKind.Git ? [.. source.SparsePathList] : [],
            MarketplacePath = marketplacePath,
            LastUpdated = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            LastRevision = revision
        };

        if (index >= 0 && index < configured.Count)
            configured[index] = entry;
        else
            configured.Add(entry);

        PluginsConfigPersistence.WritePluginRegistries(_configPath, configured);
        return ToEntry(entry, displayName)!;
    }

    private MarketplaceEntry? ToEntry(AppConfig.PluginRegistryConfig entry) => ToEntry(entry, displayNameOverride: null);

    private MarketplaceEntry? ToEntry(AppConfig.PluginRegistryConfig entry, string? displayNameOverride)
    {
        var name = entry.Name?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(entry.Url))
            return null;

        var marketplacePath = NormalizeMarketplacePath(entry.MarketplacePath);
        MarketplaceSource source;
        try
        {
            source = MarketplaceSourceParser.FromConfigured(entry.SourceType, entry.Url, entry.Ref, entry.SparsePaths);
        }
        catch (MarketplaceException)
        {
            return null;
        }

        var root = TryResolveRoot(name, source, marketplacePath, out var displayName);
        return new MarketplaceEntry(
            name,
            displayNameOverride ?? displayName,
            source.Kind,
            source.Value,
            source.Ref,
            source.SparsePathList,
            marketplacePath,
            root,
            entry.LastUpdated,
            entry.LastRevision,
            Removable: true);
    }

    private string? TryResolveRoot(
        string name,
        MarketplaceSource source,
        string marketplacePath,
        out string? displayName)
    {
        displayName = null;
        try
        {
            var candidate = source.Kind == MarketplaceSourceKind.Git
                ? MarketplaceStore.ResolveRoot(_craftHome, name)
                : source.Value;
            if (!Directory.Exists(candidate))
                return null;

            var resolved = MarketplaceDocumentLoader.ResolveRoot(candidate, marketplacePath);
            if (resolved == null)
                return null;

            var document = MarketplaceDocumentLoader.TryLoad(
                Path.Combine(resolved, MarketplaceDocumentLoader.NormalizeRelativePath(marketplacePath)),
                out _);
            displayName = string.IsNullOrWhiteSpace(document?.Interface?.DisplayName)
                ? null
                : document!.Interface!.DisplayName!.Trim();
            return resolved;
        }
        catch (MarketplaceException)
        {
            return null;
        }
    }

    private bool TryValidateConfiguredRoot(
        AppConfig.PluginRegistryConfig entry,
        out string name,
        out string? displayName)
    {
        name = string.Empty;
        displayName = null;
        var configuredName = entry.Name?.Trim();
        if (string.IsNullOrEmpty(configuredName) || string.IsNullOrWhiteSpace(entry.Url))
            return false;

        try
        {
            var source = MarketplaceSourceParser.FromConfigured(entry.SourceType, entry.Url, entry.Ref, entry.SparsePaths);
            var root = source.Kind == MarketplaceSourceKind.Git
                ? MarketplaceStore.ResolveRoot(_craftHome, configuredName)
                : source.Value;
            if (!Directory.Exists(root))
                return false;

            (name, displayName) = MarketplaceDocumentLoader.ValidateRoot(root, NormalizeMarketplacePath(entry.MarketplacePath));
            return true;
        }
        catch (MarketplaceException)
        {
            return false;
        }
    }

    private void ReplaceRootOrThrow(string stagedRoot, string destination)
    {
        try
        {
            MarketplaceStore.ReplaceRoot(stagedRoot, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.FetchFailed,
                $"Failed to install the fetched marketplace: {ex.Message}",
                ex);
        }
    }

    // Re-adding the same repository or directory at another reference or sparse path set
    // re-points the existing marketplace, so switching a branch does not require a removal.
    // A genuinely different source claiming the same name is the conflict worth rejecting.
    private static void EnsureNoNameConflict(
        List<AppConfig.PluginRegistryConfig> configured,
        string name,
        MarketplaceSource source,
        string marketplacePath)
    {
        var existing = configured.FirstOrDefault(entry => IsSameName(entry, name));
        if (existing == null
            || MatchesSource(existing, source, marketplacePath)
            || IsSameOrigin(existing, source))
        {
            return;
        }

        throw new MarketplaceException(
            MarketplaceErrorCodes.NameConflict,
            $"Marketplace '{name}' is already added from a different source; remove it before adding this source.");
    }

    private static bool IsSameOrigin(AppConfig.PluginRegistryConfig entry, MarketplaceSource source)
    {
        if (string.IsNullOrWhiteSpace(entry.Url))
            return false;

        try
        {
            var configured = MarketplaceSourceParser.FromConfigured(
                entry.SourceType,
                entry.Url,
                entry.Ref,
                entry.SparsePaths);
            return configured.Kind == source.Kind
                   && string.Equals(configured.Value, source.Value, StringComparison.Ordinal);
        }
        catch (MarketplaceException)
        {
            return false;
        }
    }

    private static void EnsureRefreshKeepsName(string configuredName, string documentName)
    {
        if (string.Equals(configuredName, documentName, StringComparison.OrdinalIgnoreCase))
            return;

        throw new MarketplaceException(
            MarketplaceErrorCodes.NameConflict,
            $"Marketplace source now declares the name '{documentName}' instead of '{configuredName}'; remove and re-add it.");
    }

    private static bool MatchesSource(
        AppConfig.PluginRegistryConfig entry,
        MarketplaceSource source,
        string marketplacePath)
    {
        if (string.IsNullOrWhiteSpace(entry.Url))
            return false;
        if (!string.Equals(NormalizeMarketplacePath(entry.MarketplacePath), marketplacePath, StringComparison.Ordinal))
            return false;

        try
        {
            return MarketplaceSourceParser
                .FromConfigured(entry.SourceType, entry.Url, entry.Ref, entry.SparsePaths)
                .Matches(source);
        }
        catch (MarketplaceException)
        {
            return false;
        }
    }

    private static bool IsSameName(AppConfig.PluginRegistryConfig entry, string name) =>
        string.Equals(entry.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase);

    private static bool IsArchiveSource(MarketplaceSource source) =>
        source.Kind == MarketplaceSourceKind.Archive
        || (source.Kind == MarketplaceSourceKind.Local && File.Exists(source.Value));

    private static string ToConfigSourceType(MarketplaceSourceKind kind) => kind switch
    {
        MarketplaceSourceKind.Git => "git",
        MarketplaceSourceKind.Local => "local",
        _ => "archive"
    };

    private static string NormalizeMarketplacePath(string? marketplacePath)
    {
        var trimmed = marketplacePath?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return MarketplaceDocumentLoader.DefaultMarketplacePath;

        var normalized = trimmed.Replace('\\', '/');
        if (!MarketplaceDocumentLoader.IsSafeRelativePath(normalized))
        {
            throw new MarketplaceException(
                MarketplaceErrorCodes.SourceInvalid,
                "Marketplace document path must be relative and stay inside the marketplace root.");
        }

        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }
}

/// <summary>
/// Shared resolution for the user-global craft home that holds marketplace state.
/// </summary>
internal static class MarketplacePaths
{
    public static string DefaultCraftHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Path.GetTempPath();
        return Path.Combine(home, ".craft");
    }
}
