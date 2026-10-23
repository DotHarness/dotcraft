using System.IO.Compression;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using Xunit;

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
        Assert.Equal(
            ["example-marketplace"],
            Directory.GetDirectories(Path.Combine(craftHome, MarketplaceStore.InstalledMarketplacesDirectory))
                .Select(directory => Path.GetFileName(directory)!)
                .ToArray());

        var configured = Assert.Single(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
        Assert.Equal("example-marketplace", configured.Name);
        Assert.Equal("git", configured.SourceType);
        Assert.Equal(["plugins/example"], configured.SparsePaths);
    }

    [Fact]
    public async Task AddAsync_ReusesTheSameSourceRefetchesANewRefAndRejectsNameConflicts()
    {
        var craftHome = NewTempDir();
        var fetcher = new FakeGitFetcher(root => WriteMarketplace(root, "example-marketplace"));
        var manager = NewManager(craftHome, fetcher);

        await manager.AddAsync(new MarketplaceAddRequest("owner/repo", "main"), CancellationToken.None);
        var same = await manager.AddAsync(new MarketplaceAddRequest("owner/repo", "main"), CancellationToken.None);
        Assert.True(same.AlreadyAdded);
        Assert.Equal(1, fetcher.FetchCount);

        var newRef = await manager.AddAsync(new MarketplaceAddRequest("owner/repo", "release"), CancellationToken.None);
        Assert.False(newRef.AlreadyAdded);
        Assert.Equal(2, fetcher.FetchCount);

        var error = await Assert.ThrowsAsync<MarketplaceException>(
            () => manager.AddAsync(new MarketplaceAddRequest("other/repo"), CancellationToken.None));
        Assert.Equal(MarketplaceErrorCodes.NameConflict, error.Code);

        var configured = Assert.Single(PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
        Assert.Equal("release", configured.Ref);
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
    public async Task LocalSource_IsRecordedWithoutCopyingAndSurvivesRemoval()
    {
        var craftHome = NewTempDir();
        var sourceRoot = NewTempDir();
        WriteMarketplace(sourceRoot, "local-marketplace");
        var manager = NewManager(craftHome, new FakeGitFetcher(_ => throw new InvalidOperationException("must not fetch")));

        var result = await manager.AddAsync(new MarketplaceAddRequest(sourceRoot), CancellationToken.None);

        Assert.Equal(MarketplaceSourceKind.Local, result.Marketplace.Kind);
        Assert.Equal(Path.GetFullPath(sourceRoot), result.Marketplace.Root);
        Assert.False(Directory.Exists(Path.Combine(craftHome, MarketplaceStore.InstalledMarketplacesDirectory, "local-marketplace")));

        var removed = manager.Remove("local-marketplace");

        Assert.Null(removed.RemovedRoot);
        Assert.True(Directory.Exists(sourceRoot));
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
        var result = await manager.RefreshAsync(name: null, CancellationToken.None,
            new AppConfig.PluginsConfig { DisableDefaultPluginRegistry = true });

        Assert.Equal(["local-marketplace"], result.Marketplaces.Select(entry => entry.Name).ToArray());
        var failure = Assert.Single(result.Errors);
        Assert.Equal("example-marketplace", failure.Name);
        Assert.Equal(MarketplaceErrorCodes.RefNotFound, failure.Code);
    }

    [Fact]
    public async Task RemoveAndRefresh_RejectUnknownMarketplace()
    {
        var manager = NewManager(NewTempDir(), new FakeGitFetcher(_ => { }));

        var removeError = Assert.Throws<MarketplaceException>(() => manager.Remove("missing"));
        var refreshError = await Assert.ThrowsAsync<MarketplaceException>(
            () => manager.RefreshAsync("missing", CancellationToken.None));

        Assert.Equal(MarketplaceErrorCodes.NotFound, removeError.Code);
        Assert.Equal(MarketplaceErrorCodes.NotFound, refreshError.Code);
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
    public async Task Discovery_KeepsMarketplaceOnManagedInstallOnly()
    {
        var craftHome = NewTempDir();
        var manager = NewManager(craftHome, new FakeGitFetcher(root =>
        {
            WriteMarketplace(root, "example-marketplace", plugins: ["example-plugin", "owned-plugin"]);
            WritePlugin(Path.Combine(root, "plugins", "example-plugin"), "example-plugin");
            WritePlugin(Path.Combine(root, "plugins", "owned-plugin"), "owned-plugin");
        }));
        await manager.AddAsync(new MarketplaceAddRequest("owner/repo"), CancellationToken.None);

        var config = new AppConfig();
        config.Plugins.PluginRegistries.AddRange(
            PluginsConfigPersistence.ReadPluginRegistries(ConfigPath(craftHome)));
        var userPlugins = Path.Combine(craftHome, "plugins");
        Assert.DoesNotContain(
            new BuiltInPluginDeployer(userPlugins, [], config.Plugins, craftHome).DeployPlugin("example-plugin"),
            diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);
        WritePlugin(Path.Combine(userPlugins, "owned-plugin"), "owned-plugin");
        var workspace = NewTempDir();

        var plugins = new PluginDiscoveryService(userPlugins, [], craftHome)
            .DiscoverAll(config, workspace, Path.Combine(workspace, ".craft"))
            .Plugins;

        var installed = Assert.Single(plugins, plugin => plugin.Manifest.Id == "example-plugin");
        Assert.True(installed.Installed);
        Assert.Equal("example-marketplace", installed.MarketplaceName);
        var owned = Assert.Single(plugins, plugin => plugin.Manifest.Id == "owned-plugin");
        Assert.True(owned.Installed);
        Assert.Null(owned.MarketplaceName);
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
