using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Plugins.Marketplaces;
using DotCraft.Sessions;

namespace DotCraft.Plugins;

/// <summary>
/// Owns archive marketplace snapshots under one Craft home.
/// A marketplace identity retains only its most recently activated snapshot.
/// </summary>
internal sealed class PluginRegistryArchiveCache
{
    internal const string CacheDirectory = "plugin-registries";
    internal const string MetadataFileName = "metadata.json";
    internal const string SnapshotDirectoryName = "snapshot";
    internal const string UpdatedAtFileName = "updatedAt.txt";
    internal static readonly TimeSpan StaleTemporaryDirectoryAge = TimeSpan.FromMinutes(10);

    private const int MetadataSchemaVersion = 1;
    private readonly string _cacheBaseRoot;
    private readonly Action<string, string>? _cleanupDiagnostic;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public PluginRegistryArchiveCache(
        string craftHome,
        Action<string, string>? cleanupDiagnostic = null)
    {
        _cacheBaseRoot = Path.Combine(Path.GetFullPath(craftHome), "cache", CacheDirectory);
        _cleanupDiagnostic = cleanupDiagnostic;
    }

    public string CacheBaseRoot => _cacheBaseRoot;

    public string CacheRootFor(string sourceUrl, string marketplacePath) =>
        Path.Combine(_cacheBaseRoot, SourceKeyFor(sourceUrl, marketplacePath));

    public string SnapshotRootFor(string sourceUrl, string marketplacePath) =>
        Path.Combine(CacheRootFor(sourceUrl, marketplacePath), SnapshotDirectoryName);

    public bool ShouldRefresh(string sourceUrl, string marketplacePath, TimeSpan refreshInterval)
    {
        var markerPath = Path.Combine(CacheRootFor(sourceUrl, marketplacePath), UpdatedAtFileName);
        if (!File.Exists(markerPath))
            return true;

        var updatedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(markerPath), TimeSpan.Zero);
        return DateTimeOffset.UtcNow - updatedAt > refreshInterval;
    }

    public void Invalidate(string sourceUrl, string marketplacePath)
    {
        CleanStaleTemporaryDirectories();
        TryDeleteFile(Path.Combine(CacheRootFor(sourceUrl, marketplacePath), UpdatedAtFileName));
    }

    public string Activate(
        string sourceUrl,
        string marketplacePath,
        byte[] archiveBytes,
        DateTimeOffset? now = null)
    {
        CleanStaleTemporaryDirectories(now);
        Directory.CreateDirectory(_cacheBaseRoot);

        var sourceKey = SourceKeyFor(sourceUrl, marketplacePath);
        var cacheRoot = Path.Combine(_cacheBaseRoot, sourceKey);
        var tempRoot = Path.Combine(_cacheBaseRoot, $".{sourceKey}.{Guid.NewGuid():N}.tmp");
        var tempSnapshot = Path.Combine(tempRoot, SnapshotDirectoryName);
        var activatedAt = now ?? DateTimeOffset.UtcNow;

        try
        {
            Directory.CreateDirectory(tempSnapshot);
            ExtractArchive(archiveBytes, tempSnapshot);
            var (marketplaceName, _) = MarketplaceDocumentLoader.ValidateRoot(tempSnapshot, marketplacePath);
            WriteMetadata(tempRoot, new ArchiveCacheMetadata(
                MetadataSchemaVersion,
                marketplaceName,
                sourceKey,
                marketplacePath,
                activatedAt));
            File.WriteAllText(
                Path.Combine(tempRoot, UpdatedAtFileName),
                activatedAt.ToString("O"));

            ReplaceAtomically(tempRoot, cacheRoot);
            PruneOtherVersions(marketplaceName, cacheRoot, marketplacePath);
            return Path.Combine(cacheRoot, SnapshotDirectoryName);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    public void RegisterAndPrune(
        string sourceUrl,
        string marketplacePath,
        string marketplaceName,
        DateTimeOffset? now = null)
    {
        CleanStaleTemporaryDirectories(now);
        var cacheRoot = CacheRootFor(sourceUrl, marketplacePath);
        if (!Directory.Exists(Path.Combine(cacheRoot, SnapshotDirectoryName)))
            return;

        TryWriteMetadata(cacheRoot, new ArchiveCacheMetadata(
            MetadataSchemaVersion,
            marketplaceName,
            SourceKeyFor(sourceUrl, marketplacePath),
            marketplacePath,
            now ?? ReadUpdatedAt(cacheRoot) ?? DateTimeOffset.UtcNow));
        PruneOtherVersions(marketplaceName, cacheRoot, marketplacePath);
    }

    public string? Remove(string sourceUrl, string marketplacePath, string? marketplaceName)
    {
        CleanStaleTemporaryDirectories();
        var cacheRoot = CacheRootFor(sourceUrl, marketplacePath);
        var existed = Directory.Exists(cacheRoot);
        TryDeleteDirectory(cacheRoot);

        if (!string.IsNullOrWhiteSpace(marketplaceName))
            PruneOtherVersions(marketplaceName.Trim(), currentCacheRoot: null, marketplacePath);

        return existed ? cacheRoot : null;
    }

    public void CleanStaleTemporaryDirectories(DateTimeOffset? now = null)
    {
        if (!Directory.Exists(_cacheBaseRoot))
            return;

        var currentTime = now ?? DateTimeOffset.UtcNow;
        string[] directories;
        try
        {
            directories = Directory.GetDirectories(_cacheBaseRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!IsTemporaryDirectoryName(name))
                continue;

            DateTimeOffset modified;
            try
            {
                modified = new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory), TimeSpan.Zero);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (currentTime - modified >= StaleTemporaryDirectoryAge)
                TryDeleteDirectory(directory);
        }
    }

    private void PruneOtherVersions(
        string marketplaceName,
        string? currentCacheRoot,
        string marketplacePath)
    {
        if (!Directory.Exists(_cacheBaseRoot))
            return;

        string[] candidates;
        try
        {
            candidates = Directory.GetDirectories(_cacheBaseRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var currentFullPath = currentCacheRoot == null ? null : Path.GetFullPath(currentCacheRoot);
        foreach (var candidate in candidates)
        {
            var candidateName = Path.GetFileName(candidate);
            if (IsTemporaryDirectoryName(candidateName)
                || (currentFullPath != null
                    && string.Equals(Path.GetFullPath(candidate), currentFullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var metadata = TryReadMetadata(candidate)
                           ?? TryMigrateLegacyMetadata(candidate, marketplacePath)
                           ?? (string.Equals(marketplacePath, MarketplaceDocumentLoader.DefaultMarketplacePath, StringComparison.Ordinal)
                               ? null
                               : TryMigrateLegacyMetadata(candidate, MarketplaceDocumentLoader.DefaultMarketplacePath));
            if (metadata != null
                && string.Equals(metadata.MarketplaceName, marketplaceName, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(candidate);
            }
        }
    }

    private ArchiveCacheMetadata? TryMigrateLegacyMetadata(string cacheRoot, string marketplacePath)
    {
        var snapshotRoot = Path.Combine(cacheRoot, SnapshotDirectoryName);
        if (!Directory.Exists(snapshotRoot))
            return null;

        try
        {
            var (marketplaceName, _) = MarketplaceDocumentLoader.ValidateRoot(snapshotRoot, marketplacePath);
            var metadata = new ArchiveCacheMetadata(
                MetadataSchemaVersion,
                marketplaceName,
                Path.GetFileName(cacheRoot),
                marketplacePath,
                ReadUpdatedAt(cacheRoot) ?? DateTimeOffset.UtcNow);
            TryWriteMetadata(cacheRoot, metadata);
            return metadata;
        }
        catch (MarketplaceException)
        {
            return null;
        }
    }

    private static void ExtractArchive(byte[] archiveBytes, string snapshotRoot)
    {
        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
                continue;

            var destination = Path.GetFullPath(Path.Combine(snapshotRoot, entry.FullName));
            if (!IsPathWithin(destination, snapshotRoot))
                throw new InvalidDataException($"Zip entry '{entry.FullName}' escapes the marketplace snapshot root.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private void ReplaceAtomically(string stagedRoot, string destination)
    {
        var parent = Path.GetDirectoryName(destination)
                     ?? throw new IOException($"Plugin registry cache path has no parent: {destination}");
        Directory.CreateDirectory(parent);

        var backup = Path.Combine(parent, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.backup");
        var hasBackup = Directory.Exists(destination);
        if (hasBackup)
            Directory.Move(destination, backup);

        try
        {
            Directory.Move(stagedRoot, destination);
        }
        catch
        {
            if (hasBackup && !Directory.Exists(destination) && Directory.Exists(backup))
                Directory.Move(backup, destination);
            throw;
        }

        if (hasBackup)
            TryDeleteDirectory(backup);
    }

    private static ArchiveCacheMetadata? TryReadMetadata(string cacheRoot)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<ArchiveCacheMetadata>(
                File.ReadAllText(Path.Combine(cacheRoot, MetadataFileName)),
                JsonOptions);
            return metadata is { SchemaVersion: MetadataSchemaVersion }
                   && !string.IsNullOrWhiteSpace(metadata.MarketplaceName)
                ? metadata
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteMetadata(string cacheRoot, ArchiveCacheMetadata metadata)
    {
        File.WriteAllText(
            Path.Combine(cacheRoot, MetadataFileName),
            JsonSerializer.Serialize(metadata, JsonOptions));
    }

    private void TryWriteMetadata(string cacheRoot, ArchiveCacheMetadata metadata)
    {
        try
        {
            WriteMetadata(cacheRoot, metadata);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _cleanupDiagnostic?.Invoke(
                cacheRoot,
                $"Failed to update plugin registry cache metadata: {ex.Message}");
        }
    }

    private static DateTimeOffset? ReadUpdatedAt(string cacheRoot)
    {
        var markerPath = Path.Combine(cacheRoot, UpdatedAtFileName);
        if (!File.Exists(markerPath))
            return null;

        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(markerPath), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string SourceKeyFor(string sourceUrl, string marketplacePath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            sourceUrl + "\n" + marketplacePath))).ToLowerInvariant();

    private static bool IsTemporaryDirectoryName(string name) =>
        name.StartsWith(".", StringComparison.Ordinal)
        && (name.EndsWith(".tmp", StringComparison.Ordinal)
            || name.EndsWith(".backup", StringComparison.Ordinal));

    private static bool IsPathWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _cleanupDiagnostic?.Invoke(
                path,
                $"Failed to invalidate plugin registry cache: {ex.Message}");
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _cleanupDiagnostic?.Invoke(
                path,
                $"Failed to clean plugin registry cache: {ex.Message}");
        }
    }

    private sealed record ArchiveCacheMetadata(
        int SchemaVersion,
        string MarketplaceName,
        string SourceKey,
        string MarketplacePath,
        DateTimeOffset UpdatedAt);
}
