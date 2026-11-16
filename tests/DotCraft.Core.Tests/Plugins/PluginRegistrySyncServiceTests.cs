using System.IO.Compression;
using System.Net;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

[CollectionDefinition("Plugin registry environment", DisableParallelization = true)]
public sealed class PluginRegistryEnvironmentCollection;

[Collection("Plugin registry environment")]
public sealed class PluginRegistrySyncServiceTests : IDisposable
{
    private const string MarketplacePath = ".craft/plugins/marketplace.json";
    private const string RegistryUrl = "https://registry.test/registry.zip";
    private readonly string _craftHome = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "registry-sync-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly string? _previousDefault = Environment.GetEnvironmentVariable(
        PluginSourceRegistryCatalog.DefaultRegistryUrlEnvironmentVariableName);
    private readonly string? _previousAdditional = Environment.GetEnvironmentVariable(
        PluginSourceRegistryCatalog.AdditionalRegistriesEnvironmentVariableName);

    public PluginRegistrySyncServiceTests()
    {
        Environment.SetEnvironmentVariable(PluginSourceRegistryCatalog.DefaultRegistryUrlEnvironmentVariableName, RegistryUrl);
        Environment.SetEnvironmentVariable(PluginSourceRegistryCatalog.AdditionalRegistriesEnvironmentVariableName, null);
    }

    [Fact]
    public async Task SyncAsync_ActivatesTheDownloadedSnapshotAndSkipsWhileItIsFresh()
    {
        var handler = new StubHandler(HttpStatusCode.OK, CreateArchive("synced-marketplace"));
        var sync = new PluginRegistrySyncService(_craftHome, handler: handler);

        var first = await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);
        var second = await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);

        Assert.Equal(PluginRegistrySyncResult.Activated, first.Result);
        Assert.Equal(PluginRegistrySyncResult.Skipped, second.Result);
        Assert.Equal(1, handler.RequestCount);
        Assert.True(File.Exists(Path.Combine(
            new PluginRegistryArchiveCache(_craftHome).SnapshotRootFor(RegistryUrl, MarketplacePath), MarketplacePath)));
    }

    [Fact]
    public async Task SyncAsync_CoalescesConcurrentDownloads()
    {
        var handler = new ControlledHandler(CreateArchive("coalesced-marketplace"));
        var sync = new PluginRegistrySyncService(_craftHome, handler: handler);
        var first = sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", true, CancellationToken.None);
        await handler.Started.Task;
        var second = sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", true, CancellationToken.None);

        handler.Release.TrySetResult();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal(PluginRegistrySyncResult.Activated, result.Result));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SyncAsync_BacksOffAfterAFailedDownloadUnlessForced()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError);
        var sync = new PluginRegistrySyncService(_craftHome, handler: handler);

        var first = await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);
        var second = await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);

        Assert.Equal(PluginRegistrySyncResult.Failed, first.Result);
        Assert.Equal(MarketplaceErrorCodes.FetchFailed, first.Code);
        Assert.Contains("HTTP 500", first.Message);
        Assert.Equal(PluginRegistrySyncResult.Skipped, second.Result);
        Assert.Equal(1, handler.RequestCount);

        await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: true, CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task SyncAsync_RejectsANonHttpsSource()
    {
        var handler = new StubHandler(HttpStatusCode.OK, CreateArchive("synced-marketplace"));
        var sync = new PluginRegistrySyncService(_craftHome, handler: handler);

        var result = await sync.SyncAsync(
            "http://registry.test/registry.zip",
            MarketplacePath,
            "Synced",
            force: true,
            CancellationToken.None);

        Assert.Equal(PluginRegistrySyncResult.Skipped, result.Result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SyncDueAsync_CoversTheHostProvidedDefaultRegistry()
    {
        var handler = new StubHandler(HttpStatusCode.OK, CreateArchive("default-marketplace"));

        var activated = await new PluginRegistrySyncService(_craftHome, handler: handler)
            .SyncDueAsync(new AppConfig.PluginsConfig(), force: false, CancellationToken.None);

        Assert.True(activated);
        Assert.Equal(1, handler.RequestCount);
        Assert.True(File.Exists(Path.Combine(
            new PluginRegistryArchiveCache(_craftHome).SnapshotRootFor(RegistryUrl, MarketplacePath), MarketplacePath)));
    }

    [Theory]
    [InlineData("default-marketplace", false)]
    [InlineData(null, true)]
    public async Task MarketplaceRefresh_RefreshesHostDefaultOnce(string? name, bool configured)
    {
        Environment.SetEnvironmentVariable(PluginSourceRegistryCatalog.AdditionalRegistriesEnvironmentVariableName, RegistryUrl);
        new PluginRegistryArchiveCache(_craftHome).Activate(RegistryUrl, MarketplacePath, CreateArchive("default-marketplace"));
        if (configured)
            PluginsConfigPersistence.WritePluginRegistries(Path.Combine(_craftHome, "config.json"),
                [new AppConfig.PluginRegistryConfig { Name = "default-marketplace", SourceType = "archive", Url = RegistryUrl }]);
        var handler = new StubHandler(HttpStatusCode.OK, CreateArchive("default-marketplace"));
        var manager = new MarketplaceManager(_craftHome,
            archiveSync: new PluginRegistrySyncService(_craftHome, handler: handler));

        var result = await manager.RefreshAsync(name, CancellationToken.None);

        Assert.Empty(result.Errors);
        var marketplace = Assert.Single(result.Marketplaces);
        Assert.Equal("default-marketplace", marketplace.Name);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task MarketplaceRefresh_ReportsHostDefaultFailureAndKeepsCachedSnapshot()
    {
        var snapshot = new PluginRegistryArchiveCache(_craftHome)
            .Activate(RegistryUrl, MarketplacePath, CreateArchive("default-marketplace"));
        var manager = new MarketplaceManager(_craftHome, archiveSync: new PluginRegistrySyncService(
            _craftHome, handler: new StubHandler(HttpStatusCode.InternalServerError)));

        var result = await manager.RefreshAsync("default-marketplace", CancellationToken.None);

        Assert.Empty(result.Marketplaces);
        var failure = Assert.Single(result.Errors);
        Assert.Equal("default-marketplace", failure.Name);
        Assert.Equal(MarketplaceErrorCodes.FetchFailed, failure.Code);
        Assert.Contains("HTTP 500", failure.Message);
        Assert.True(File.Exists(Path.Combine(snapshot, MarketplacePath)));
    }

    private static byte[] CreateArchive(string marketplaceName)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(MarketplacePath).Open());
            writer.Write($$"""{"name":"{{marketplaceName}}","plugins":[]}""");
        }
        return stream.ToArray();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PluginSourceRegistryCatalog.DefaultRegistryUrlEnvironmentVariableName, _previousDefault);
        Environment.SetEnvironmentVariable(PluginSourceRegistryCatalog.AdditionalRegistriesEnvironmentVariableName, _previousAdditional);
        Directory.Delete(_craftHome, recursive: true);
    }

    private sealed class ControlledHandler(byte[] bytes) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }

    private sealed class StubHandler(HttpStatusCode status, byte[]? bytes = null) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(bytes ?? [])
            });
        }
    }
}
