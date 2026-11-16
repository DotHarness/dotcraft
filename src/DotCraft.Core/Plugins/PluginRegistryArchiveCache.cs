using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Plugins.Marketplaces;

namespace DotCraft.Plugins;

internal sealed class PluginRegistryArchiveCache
{
    internal const string CacheDirectory = "plugin-registries";
    internal const string MetadataFileName = "metadata.json";
    internal const string SnapshotDirectoryName = "snapshot";
    internal const string UpdatedAtFileName = "updatedAt.txt";
    internal const string AttemptFileName = "attempt.json";
    internal static readonly TimeSpan StaleTemporaryDirectoryAge = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan MinimumRetryBackoff = TimeSpan.FromMinutes(1);

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

    public bool IsOlderThan(string sourceUrl, string marketplacePath, DateTime sourceWriteTimeUtc)
    {
        var updatedAt = ReadUpdatedAt(CacheRootFor(sourceUrl, marketplacePath));
        return updatedAt == null || updatedAt < new DateTimeOffset(sourceWriteTimeUtc, TimeSpan.Zero);
    }

    public bool ShouldAttempt(
        string sourceUrl,
        string marketplacePath,
        TimeSpan maximumBackoff,
        DateTimeOffset? now = null)
    {
        var attempt = TryReadAttempt(CacheRootFor(sourceUrl, marketplacePath));
        if (attempt == null)
            return true;

        // Clock rollback must not defer retries indefinitely.
        var age = (now ?? DateTimeOffset.UtcNow) - attempt.AttemptedAt;
        return age < TimeSpan.Zero || age >= BackoffFor(attempt.ConsecutiveFailures, maximumBackoff);
    }

    public void RecordFailedAttempt(string sourceUrl, string marketplacePath, DateTimeOffset? now = null)
    {
        var cacheRoot = CacheRootFor(sourceUrl, marketplacePath);
        var failures = (TryReadAttempt(cacheRoot)?.ConsecutiveFailures ?? 0) + 1;
        try
        {
            Directory.CreateDirectory(cacheRoot);
            File.WriteAllText(
                Path.Combine(cacheRoot, AttemptFileName),
                JsonSerializer.Serialize(
                    new ArchiveAttemptRecord(MetadataSchemaVersion, now ?? DateTimeOffset.UtcNow, failures),
                    JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _cleanupDiagnostic?.Invoke(
                cacheRoot,
                $"Failed to record plugin registry download attempt: {ex.Message}");
        }
    }

    public void Invalidate(string sourceUrl, string marketplacePath)
    {
        CleanStaleTemporaryDirectories();
        var cacheRoot = CacheRootFor(sourceUrl, marketplacePath);
        TryDeleteFile(Path.Combine(cacheRoot, UpdatedAtFileName));
        TryDeleteFile(Path.Combine(cacheRoot, AttemptFileName));
    }

    public string Activate(
        string sourceUrl,
        string marketplacePath,
        byte[] archiveBytes,
        DateTimeOffset? now = null,
        string? revision = null) =>
        ActivateSnapshot(sourceUrl, marketplacePath, snapshot =>
        {
            Directory.CreateDirectory(snapshot);
            ExtractArchive(archiveBytes, snapshot);
        }, now, revision);

    public string ActivateDirectory(
        string sourceUrl,
        string marketplacePath,
        string stagedSnapshot,
        string revision) =>
        ActivateSnapshot(sourceUrl, marketplacePath,
            snapshot => Directory.Move(stagedSnapshot, snapshot), now: null, revision);

    public string? RevisionFor(string sourceUrl, string marketplacePath) =>
        TryReadMetadata(CacheRootFor(sourceUrl, marketplacePath))?.Revision;

    public void MarkChecked(string sourceUrl, string marketplacePath)
    {
        var cacheRoot = CacheRootFor(sourceUrl, marketplacePath);
        var now = DateTimeOffset.UtcNow;
        var metadata = TryReadMetadata(cacheRoot)
            ?? throw new IOException("The plugin registry cache metadata is missing.");
        WriteMetadata(cacheRoot, metadata with { UpdatedAt = now });
        File.WriteAllText(Path.Combine(cacheRoot, UpdatedAtFileName), now.ToString("O"));
        TryDeleteFile(Path.Combine(cacheRoot, AttemptFileName));
    }

    private string ActivateSnapshot(
        string sourceUrl,
        string marketplacePath,
        Action<string> populateSnapshot,
        DateTimeOffset? now,
        string? revision)
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
            Directory.CreateDirectory(tempRoot);
            populateSnapshot(tempSnapshot);
            var (marketplaceName, _) = MarketplaceDocumentLoader.ValidateRoot(tempSnapshot, marketplacePath);
            WriteMetadata(tempRoot, new ArchiveCacheMetadata(
                MetadataSchemaVersion,
                marketplaceName,
                sourceKey,
                marketplacePath,
                activatedAt,
                revision));
            File.WriteAllText(
                Path.Combine(tempRoot, UpdatedAtFileName),
                activatedAt.ToString("O"));

            ReplaceAtomically(tempRoot, cacheRoot);
            PruneOtherVersions(marketplaceName, cacheRoot);
            return Path.Combine(cacheRoot, SnapshotDirectoryName);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    public void Prune(string sourceUrl, string marketplacePath, string marketplaceName)
    {
        CleanStaleTemporaryDirectories();
        PruneOtherVersions(marketplaceName, CacheRootFor(sourceUrl, marketplacePath));
    }

    public string? Remove(string sourceUrl, string marketplacePath, string? marketplaceName)
    {
        CleanStaleTemporaryDirectories();
        var cacheRoot = CacheRootFor(sourceUrl, marketplacePath);
        var existed = Directory.Exists(cacheRoot);
        TryDeleteDirectory(cacheRoot);

        if (!string.IsNullOrWhiteSpace(marketplaceName))
            PruneOtherVersions(marketplaceName.Trim(), currentCacheRoot: null);

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

    private void PruneOtherVersions(string marketplaceName, string? currentCacheRoot)
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

            var metadata = TryReadMetadata(candidate);
            if (metadata != null
                && string.Equals(metadata.MarketplaceName, marketplaceName, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(candidate);
            }
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

    private ArchiveAttemptRecord? TryReadAttempt(string cacheRoot)
    {
        var attemptPath = Path.Combine(cacheRoot, AttemptFileName);
        try
        {
            if (!File.Exists(attemptPath))
                return null;

            var record = JsonSerializer.Deserialize<ArchiveAttemptRecord>(
                File.ReadAllText(attemptPath),
                JsonOptions);
            if (record is { SchemaVersion: MetadataSchemaVersion, ConsecutiveFailures: > 0 })
                return record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        TryDeleteFile(attemptPath);
        return null;
    }

    private static TimeSpan BackoffFor(int consecutiveFailures, TimeSpan maximum)
    {
        var scaled = MinimumRetryBackoff * Math.Pow(2, Math.Min(consecutiveFailures - 1, 20));
        return scaled >= maximum ? maximum : scaled;
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

    internal void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
                     { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
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
        DateTimeOffset UpdatedAt,
        string? Revision = null);

    private sealed record ArchiveAttemptRecord(
        int SchemaVersion,
        DateTimeOffset AttemptedAt,
        int ConsecutiveFailures);
}
