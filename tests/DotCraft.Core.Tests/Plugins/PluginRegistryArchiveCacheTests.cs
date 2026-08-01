using System.IO.Compression;
using System.Text.Json;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;

namespace DotCraft.Core.Tests.Plugins;

public sealed class PluginRegistryArchiveCacheTests
{
    private const string MarketplacePath = ".craft/plugins/marketplace.json";

    [Fact]
    public void Activate_PrunesPreviousSourceForTheSameMarketplace()
    {
        var craftHome = NewTempDir();
        var cache = new PluginRegistryArchiveCache(craftHome);

        var first = cache.Activate(
            "https://example.test/marketplace-v1.zip",
            MarketplacePath,
            CreateArchive("example-marketplace", "v1"));
        var second = cache.Activate(
            "https://example.test/marketplace-v2.zip",
            MarketplacePath,
            CreateArchive("example-marketplace", "v2"));

        Assert.False(Directory.Exists(first));
        Assert.True(File.Exists(Path.Combine(second, "version.txt")));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(second, "version.txt")));
        Assert.Single(Directory.GetDirectories(cache.CacheBaseRoot));

        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(second)!,
            PluginRegistryArchiveCache.MetadataFileName)));
        Assert.Equal(1, metadata.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("example-marketplace", metadata.RootElement.GetProperty("marketplaceName").GetString());
        Assert.Equal(MarketplacePath, metadata.RootElement.GetProperty("marketplacePath").GetString());
    }

    [Fact]
    public void Activate_ReplacesTheExistingSnapshotForTheSameSource()
    {
        var cache = new PluginRegistryArchiveCache(NewTempDir());
        const string source = "https://example.test/marketplace.zip";

        var first = cache.Activate(source, MarketplacePath, CreateArchive("example-marketplace", "v1"));
        var second = cache.Activate(source, MarketplacePath, CreateArchive("example-marketplace", "v2"));

        Assert.Equal(first, second);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(second, "version.txt")));
        Assert.Single(Directory.GetDirectories(cache.CacheBaseRoot));
    }

    [Fact]
    public void Activate_KeepsSnapshotsForDifferentMarketplaces()
    {
        var cache = new PluginRegistryArchiveCache(NewTempDir());

        var first = cache.Activate(
            "https://example.test/first.zip",
            MarketplacePath,
            CreateArchive("first-marketplace", "first"));
        var second = cache.Activate(
            "https://example.test/second.zip",
            MarketplacePath,
            CreateArchive("second-marketplace", "second"));

        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
        Assert.Equal(2, Directory.GetDirectories(cache.CacheBaseRoot).Length);
    }

    [Fact]
    public void Activate_LeavesPreviousSnapshotWhenValidationFails()
    {
        var cache = new PluginRegistryArchiveCache(NewTempDir());
        const string source = "https://example.test/marketplace.zip";
        var current = cache.Activate(source, MarketplacePath, CreateArchive("example-marketplace", "current"));

        Assert.Throws<MarketplaceException>(() => cache.Activate(source, MarketplacePath, CreateInvalidArchive()));

        Assert.True(Directory.Exists(current));
        Assert.Equal("current", File.ReadAllText(Path.Combine(current, "version.txt")));
        Assert.Single(Directory.GetDirectories(cache.CacheBaseRoot));
    }

    [Fact]
    public void RegisterAndPrune_MigratesLegacySnapshotAndPrunesDuplicateGeneration()
    {
        var cache = new PluginRegistryArchiveCache(NewTempDir());
        const string oldSource = "https://example.test/old.zip";
        const string currentSource = "https://example.test/current.zip";
        var oldRoot = cache.CacheRootFor(oldSource, MarketplacePath);
        var currentRoot = cache.CacheRootFor(currentSource, MarketplacePath);
        WriteLegacySnapshot(oldRoot, "example-marketplace", "old");
        WriteLegacySnapshot(currentRoot, "example-marketplace", "current");

        cache.RegisterAndPrune(currentSource, MarketplacePath, "example-marketplace");

        Assert.False(Directory.Exists(oldRoot));
        Assert.True(Directory.Exists(currentRoot));
        Assert.True(File.Exists(Path.Combine(currentRoot, PluginRegistryArchiveCache.MetadataFileName)));
    }

    [Fact]
    public void CleanStaleTemporaryDirectories_OnlyRemovesOldManagedDirectories()
    {
        var cache = new PluginRegistryArchiveCache(NewTempDir());
        Directory.CreateDirectory(cache.CacheBaseRoot);
        var now = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero);
        var stale = Path.Combine(cache.CacheBaseRoot, ".source.111.tmp");
        var fresh = Path.Combine(cache.CacheBaseRoot, ".source.222.tmp");
        var unrelated = Path.Combine(cache.CacheBaseRoot, "ordinary.tmp");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(fresh);
        Directory.CreateDirectory(unrelated);
        Directory.SetLastWriteTimeUtc(stale, now.UtcDateTime.AddMinutes(-11));
        Directory.SetLastWriteTimeUtc(fresh, now.UtcDateTime.AddMinutes(-9));
        Directory.SetLastWriteTimeUtc(unrelated, now.UtcDateTime.AddHours(-1));

        cache.CleanStaleTemporaryDirectories(now);

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
        Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void Activate_UsesTheProvidedCraftHome()
    {
        var craftHome = NewTempDir();
        var cache = new PluginRegistryArchiveCache(craftHome);

        var snapshot = cache.Activate(
            "https://example.test/marketplace.zip",
            MarketplacePath,
            CreateArchive("example-marketplace", "current"));

        Assert.StartsWith(
            Path.Combine(Path.GetFullPath(craftHome), "cache", PluginRegistryArchiveCache.CacheDirectory),
            snapshot,
            StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreateArchive(string marketplaceName, string version)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var marketplace = archive.CreateEntry(MarketplacePath);
            using (var writer = new StreamWriter(marketplace.Open()))
            {
                writer.Write($$"""
{
  "name": "{{marketplaceName}}",
  "plugins": []
}
""");
            }

            var marker = archive.CreateEntry("version.txt");
            using var markerWriter = new StreamWriter(marker.Open());
            markerWriter.Write(version);
        }

        return stream.ToArray();
    }

    private static byte[] CreateInvalidArchive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var marker = archive.CreateEntry("version.txt");
            using var writer = new StreamWriter(marker.Open());
            writer.Write("invalid");
        }

        return stream.ToArray();
    }

    private static void WriteLegacySnapshot(string cacheRoot, string marketplaceName, string version)
    {
        var snapshot = Path.Combine(cacheRoot, PluginRegistryArchiveCache.SnapshotDirectoryName);
        var documentDirectory = Path.Combine(snapshot, ".craft", "plugins");
        Directory.CreateDirectory(documentDirectory);
        File.WriteAllText(
            Path.Combine(documentDirectory, "marketplace.json"),
            $$"""
{
  "name": "{{marketplaceName}}",
  "plugins": []
}
""");
        File.WriteAllText(Path.Combine(snapshot, "version.txt"), version);
        File.WriteAllText(Path.Combine(cacheRoot, PluginRegistryArchiveCache.UpdatedAtFileName), DateTimeOffset.UtcNow.ToString("O"));
    }

    private static string NewTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-registry-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
