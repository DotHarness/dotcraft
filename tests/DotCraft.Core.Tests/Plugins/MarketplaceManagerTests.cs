using System.IO.Compression;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using DotCraft.Sessions;
using Xunit;
using DotCraft.Tools;

namespace DotCraft.Core.Tests.Plugins;

public sealed class MarketplaceManagerTests
{
    [Fact]
    public async Task AddAsync_MaterializesRepositorySourceAndRecordsConfiguredEntry()
    {
        var craftHome = NewTempDir();
        var fetcher = new FakeGitFetcher(root => WriteMarketplace(root, "example-marketplace", "Example Plugins"));
        var manager = NewManager(craftHome, fetcher);

        var result = await manager.AddAsync(new MarketplaceAddRequest("owner/repo", "main", ["plugins/example"]), CancellationToken.None);

        Assert.False(result.AlreadyAdded);
        Assert.Equal("example-marketplace", result.Marketplace.Name);
        Assert.Equal("Example Plugins", result.Marketplace.DisplayName);
        Assert.Equal(MarketplaceSourceKind.Git, result.Marketplace.Kind);
        Assert.Equal("https://github.com/owner/repo.git", result.Marketplace.Source);
        Assert.Equal("main", result.Marketplace.Ref);
        Assert.Equal(["plugins/example"], result.Marketplace.SparsePaths);
        Assert.Equal("fetched-revision", result.Marketplace.Revision);
        Assert.True(File.Exists(Path.Combine(
            craftHome,
            MarketplaceStore.InstalledMarketplacesDirectory,
            "example-marketplace",
            ".craft",
            "plugins",
            "marketplace.json")));

        var configured = Assert.Single(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
        Assert.Equal("example-marketplace", configured.Name);
        Assert.Equal("git", configured.SourceType);
        Assert.Equal(["plugins/example"], configured.SparsePaths);
    }

    [Fact]
    public async Task AddAsync_LeavesNoStagingDirectoryBehind()
    {
        var craftHome = NewTempDir();
        var manager = NewManager(craftHome, new FakeGitFetcher(root => WriteMarketplace(root, "example-marketplace")));

        await manager.AddAsync(new MarketplaceAddRequest("owner/repo"), CancellationToken.None);

        var installRoot = Path.Combine(craftHome, MarketplaceStore.InstalledMarketplacesDirectory);
        Assert.Equal(["example-marketplace"], Directory.GetDirectories(installRoot).Select(directory => Path.GetFileName(directory)!).ToArray());
    }

    [Fact]
    public async Task AddAsync_IsIdempotentForTheSameSource()
    {
        var craftHome = NewTempDir();
        var fetcher = new FakeGitFetcher(root => WriteMarketplace(root, "example-marketplace"));
        var manager = NewManager(craftHome, fetcher);

        await manager.AddAsync(new MarketplaceAddRequest("owner/repo", "main"), CancellationToken.None);
        var second = await manager.AddAsync(new MarketplaceAddRequest("owner/repo", "main"), CancellationToken.None);

        Assert.True(second.AlreadyAdded);
        Assert.Equal(1, fetcher.FetchCount);
        Assert.Single(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
    }

    [Fact]
    public async Task AddAsync_RefetchesWhenTheReferenceDiffers()
    {
        var craftHome = NewTempDir();
        var fetcher = new FakeGitFetcher(root => WriteMarketplace(root, "example-marketplace"));
        var manager = NewManager(craftHome, fetcher);

        await manager.AddAsync(new MarketplaceAddRequest("owner/repo", "main"), CancellationToken.None);
        var second = await manager.AddAsync(new MarketplaceAddRequest("owner/repo", "release"), CancellationToken.None);

        Assert.False(second.AlreadyAdded);
        Assert.Equal(2, fetcher.FetchCount);
        var configured = Assert.Single(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
        Assert.Equal("release", configured.Ref);
    }

    [Fact]
    public async Task AddAsync_RejectsNameAlreadyAddedFromAnotherSource()
    {
        var craftHome = NewTempDir();
        var fetcher = new FakeGitFetcher(root => WriteMarketplace(root, "example-marketplace"));
        var manager = NewManager(craftHome, fetcher);
        await manager.AddAsync(new MarketplaceAddRequest("owner/repo"), CancellationToken.None);

        var error = await Assert.ThrowsAsync<MarketplaceException>(
            () => manager.AddAsync(new MarketplaceAddRequest("other/repo"), CancellationToken.None));

        Assert.Equal(MarketplaceErrorCodes.NameConflict, error.Code);
        Assert.Single(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
    }

    [Fact]
    public async Task AddAsync_RejectsSourceWithoutMarketplaceDocumentAndWritesNothing()
    {
        var craftHome = NewTempDir();
        var manager = NewManager(craftHome, new FakeGitFetcher(_ => { }));

        var error = await Assert.ThrowsAsync<MarketplaceException>(
            () => manager.AddAsync(new MarketplaceAddRequest("owner/repo"), CancellationToken.None));

        Assert.Equal(MarketplaceErrorCodes.DocumentMissing, error.Code);
        Assert.Empty(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
        Assert.Empty(Directory.GetDirectories(Path.Combine(craftHome, MarketplaceStore.InstalledMarketplacesDirectory)));
    }

    [Fact]
    public async Task AddAsync_RecordsLocalSourceWithoutCopyingIt()
    {
        var craftHome = NewTempDir();
        var sourceRoot = NewTempDir();
        WriteMarketplace(sourceRoot, "local-marketplace");
        var manager = NewManager(craftHome, new FakeGitFetcher(_ => throw new InvalidOperationException("must not fetch")));

        var result = await manager.AddAsync(new MarketplaceAddRequest(sourceRoot), CancellationToken.None);

        Assert.Equal(MarketplaceSourceKind.Local, result.Marketplace.Kind);
        Assert.Equal(Path.GetFullPath(sourceRoot), result.Marketplace.Root);
        Assert.False(Directory.Exists(Path.Combine(craftHome, MarketplaceStore.InstalledMarketplacesDirectory, "local-marketplace")));
    }

    [Fact]
    public async Task Remove_DropsConfiguredEntryAndMaterializedRoot()
    {
        var craftHome = NewTempDir();
        var manager = NewManager(craftHome, new FakeGitFetcher(root => WriteMarketplace(root, "example-marketplace")));
        await manager.AddAsync(new MarketplaceAddRequest("owner/repo"), CancellationToken.None);

        var removed = manager.Remove("example-marketplace");

        Assert.Equal("example-marketplace", removed.Name);
        Assert.NotNull(removed.RemovedRoot);
        Assert.False(Directory.Exists(removed.RemovedRoot!));
        Assert.Empty(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
    }

    [Fact]
    public async Task Remove_KeepsLocalSourceDirectory()
    {
        var craftHome = NewTempDir();
        var sourceRoot = NewTempDir();
        WriteMarketplace(sourceRoot, "local-marketplace");
        var manager = NewManager(craftHome, new FakeGitFetcher(_ => { }));
        await manager.AddAsync(new MarketplaceAddRequest(sourceRoot), CancellationToken.None);

        var removed = manager.Remove("local-marketplace");

        Assert.Null(removed.RemovedRoot);
        Assert.True(Directory.Exists(sourceRoot));
    }

    [Fact]
    public void Remove_DeletesArchiveMarketplaceCache()
    {
        var craftHome = NewTempDir();
        const string source = "https://example.test/archive-marketplace.zip";
        var cache = new PluginRegistryArchiveCache(craftHome);
        var archiveRoot = NewTempDir();
        WriteMarketplace(archiveRoot, "archive-marketplace");
        var archivePath = Path.Combine(NewTempDir(), "marketplace.zip");
        ZipFile.CreateFromDirectory(archiveRoot, archivePath);
        var snapshot = cache.Activate(
            source,
            MarketplaceDocumentLoader.DefaultMarketplacePath,
            File.ReadAllBytes(archivePath));
        PluginsConfigPersistence.WritePluginRegistries(
            ConfigPath(craftHome),
            [new AppConfig.PluginRegistryConfig
            {
                Name = "archive-marketplace",
                SourceType = "archive",
                Url = source,
                MarketplacePath = MarketplaceDocumentLoader.DefaultMarketplacePath
            }]);
        var manager = NewManager(craftHome, new FakeGitFetcher(_ => { }));

        var removed = manager.Remove("archive-marketplace");

        Assert.Equal(Path.GetDirectoryName(snapshot), removed.RemovedRoot);
        Assert.False(Directory.Exists(Path.GetDirectoryName(snapshot)));
        Assert.Empty(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
    }

    [Fact]
    public void Remove_DeletesLegacyLocalArchiveCache()
    {
        var craftHome = NewTempDir();
        var archiveRoot = NewTempDir();
        WriteMarketplace(archiveRoot, "legacy-archive-marketplace");
        var archivePath = Path.Combine(NewTempDir(), "marketplace.zip");
        ZipFile.CreateFromDirectory(archiveRoot, archivePath);
        var cache = new PluginRegistryArchiveCache(craftHome);
        var snapshot = cache.Activate(
            archivePath,
            MarketplaceDocumentLoader.DefaultMarketplacePath,
            File.ReadAllBytes(archivePath));
        PluginsConfigPersistence.WritePluginRegistries(
            ConfigPath(craftHome),
            [new AppConfig.PluginRegistryConfig
            {
                Name = "legacy-archive-marketplace",
                Url = archivePath,
                MarketplacePath = MarketplaceDocumentLoader.DefaultMarketplacePath
            }]);
        var manager = NewManager(craftHome, new FakeGitFetcher(_ => { }));

        manager.Remove("legacy-archive-marketplace");

        Assert.False(Directory.Exists(Path.GetDirectoryName(snapshot)));
        Assert.True(File.Exists(archivePath));
    }

    [Fact]
    public void Remove_RejectsUnknownMarketplace()
    {
        var manager = NewManager(NewTempDir(), new FakeGitFetcher(_ => { }));

        var error = Assert.Throws<MarketplaceException>(() => manager.Remove("missing"));

        Assert.Equal(MarketplaceErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task RefreshAsync_ReportsPerSourceFailuresWithoutFailingTheOthers()
    {
        var craftHome = NewTempDir();
        var healthy = NewTempDir();
        WriteMarketplace(healthy, "local-marketplace");
        var fetcher = new FakeGitFetcher(root => WriteMarketplace(root, "example-marketplace"));
        var manager = NewManager(craftHome, fetcher);
        await manager.AddAsync(new MarketplaceAddRequest("owner/repo"), CancellationToken.None);
        await manager.AddAsync(new MarketplaceAddRequest(healthy), CancellationToken.None);

        fetcher.FailWith = new MarketplaceException(MarketplaceErrorCodes.RefNotFound, "missing ref");
        var result = await manager.RefreshAsync(name: null, CancellationToken.None);

        Assert.Equal(["local-marketplace"], result.Marketplaces.Select(entry => entry.Name).ToArray());
        var failure = Assert.Single(result.Errors);
        Assert.Equal("example-marketplace", failure.Name);
        Assert.Equal(MarketplaceErrorCodes.RefNotFound, failure.Code);
    }

    [Fact]
    public async Task RefreshAsync_RejectsUnknownMarketplace()
    {
        var manager = NewManager(NewTempDir(), new FakeGitFetcher(_ => { }));

        var error = await Assert.ThrowsAsync<MarketplaceException>(
            () => manager.RefreshAsync("missing", CancellationToken.None));

        Assert.Equal(MarketplaceErrorCodes.NotFound, error.Code);
    }

    [Fact]
    public async Task RefreshAsync_MigratesLegacyLocalArchiveAndInvalidatesItsCache()
    {
        var craftHome = NewTempDir();
        var archiveRoot = NewTempDir();
        WriteMarketplace(archiveRoot, "legacy-archive-marketplace");
        var archivePath = Path.Combine(NewTempDir(), "marketplace.zip");
        ZipFile.CreateFromDirectory(archiveRoot, archivePath);
        var cache = new PluginRegistryArchiveCache(craftHome);
        cache.Activate(
            archivePath,
            MarketplaceDocumentLoader.DefaultMarketplacePath,
            File.ReadAllBytes(archivePath));
        PluginsConfigPersistence.WritePluginRegistries(
            ConfigPath(craftHome),
            [new AppConfig.PluginRegistryConfig
            {
                Name = "legacy-archive-marketplace",
                Url = archivePath,
                MarketplacePath = MarketplaceDocumentLoader.DefaultMarketplacePath
            }]);
        var manager = NewManager(craftHome, new FakeGitFetcher(_ => { }));

        var result = await manager.RefreshAsync("legacy-archive-marketplace", CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Equal(MarketplaceSourceKind.Archive, Assert.Single(result.Marketplaces).Kind);
        var configured = Assert.Single(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
        Assert.Equal("archive", configured.SourceType);
        Assert.False(File.Exists(Path.Combine(
            cache.CacheRootFor(archivePath, MarketplaceDocumentLoader.DefaultMarketplacePath),
            PluginRegistryArchiveCache.UpdatedAtFileName)));
    }

    [Fact]
    public async Task Discovery_ReadsMaterializedMarketplaceWithoutFetching()
    {
        var craftHome = NewTempDir();
        var manager = NewManager(craftHome, new FakeGitFetcher(root =>
        {
            WriteMarketplace(root, "example-marketplace", plugins: ["example-plugin"]);
            WritePlugin(Path.Combine(root, "plugins", "example-plugin"), "example-plugin");
        }));
        await manager.AddAsync(new MarketplaceAddRequest("owner/repo"), CancellationToken.None);

        var config = new AppConfig();
        config.Plugins.PluginRegistries.AddRange(
            PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
        var catalog = new BuiltInPluginCatalog([], config.Plugins, craftHome).Discover();

        var plugin = Assert.Single(catalog.Plugins);
        Assert.Equal("example-plugin", plugin.Manifest.Id);
        Assert.Equal("example-marketplace", plugin.MarketplaceName);
        Assert.True(plugin.Installable);
        Assert.False(plugin.Installed);
    }

    [Fact]
    public void Discovery_ReportsMissingMaterializedRootInsteadOfFetching()
    {
        var craftHome = NewTempDir();
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig
        {
            Name = "example-marketplace",
            SourceType = "git",
            Url = "https://github.com/owner/repo.git"
        });

        var catalog = new BuiltInPluginCatalog([], config.Plugins, craftHome).Discover();

        Assert.Empty(catalog.Plugins);
        Assert.Contains(catalog.Diagnostics, d => d.Code == "PluginRegistrySnapshotMissing");
    }

    private static MarketplaceManager NewManager(string craftHome, IMarketplaceGitFetcher fetcher) =>
        new(craftHome, ConfigPath(craftHome), fetcher);

    private static string ConfigPath(string craftHome) => Path.Combine(craftHome, "config.json");

    private static void WriteMarketplace(
        string root,
        string name,
        string? displayName = null,
        IReadOnlyList<string>? plugins = null)
    {
        var documentDirectory = Path.Combine(root, ".craft", "plugins");
        Directory.CreateDirectory(documentDirectory);
        var interfaceBlock = displayName == null
            ? string.Empty
            : $$"""
  "interface": { "displayName": "{{displayName}}" },

""";
        var entries = string.Join(",\n", (plugins ?? []).Select(plugin => $$"""
    {
      "name": "{{plugin}}",
      "source": { "source": "local", "path": "./plugins/{{plugin}}" },
      "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" }
    }
"""));

        File.WriteAllText(
            Path.Combine(documentDirectory, "marketplace.json"),
            $$"""
{
  "name": "{{name}}",
{{interfaceBlock}}  "plugins": [
{{entries}}
  ]
}
""");
    }

    private static void WritePlugin(string pluginRoot, string id)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"),
            "---\nname: demo-skill\ndescription: Demo skill\n---\n# Demo");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "Example",
  "description": "Example plugin.",
  "capabilities": ["skill"],
  "skills": "./skills/"
}
""");
    }

    private static string NewTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-marketplace-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeGitFetcher(Action<string> populate) : IMarketplaceGitFetcher
    {
        public int FetchCount { get; private set; }

        public MarketplaceException? FailWith { get; set; }

        public Task<string?> FetchAsync(MarketplaceSource source, string destination, CancellationToken ct)
        {
            FetchCount++;
            if (FailWith != null)
                throw FailWith;

            populate(destination);
            return Task.FromResult<string?>("fetched-revision");
        }
    }
}
