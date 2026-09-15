using System.IO.Compression;
using System.Net;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

public sealed class PluginRegistrySyncServiceTests
{
    private const string MarketplacePath = ".craft/plugins/marketplace.json";
    private const string RegistryUrl = "https://registry.test/registry.zip";

    [Fact]
    public async Task SyncAsync_ActivatesTheDownloadedSnapshot()
    {
        var handler = StubHandler.Serving(CreateArchive("synced-marketplace"));
        var craftHome = NewTempDir();
        var sync = new PluginRegistrySyncService(craftHome, handler: handler);

        var result = await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);

        Assert.Equal(PluginRegistrySyncResult.Activated, result);
        Assert.Equal(1, handler.RequestCount);
        Assert.True(Directory.Exists(
            new PluginRegistryArchiveCache(craftHome).SnapshotRootFor(RegistryUrl, MarketplacePath)));
    }

    [Fact]
    public async Task SyncAsync_DoesNotDownloadAgainWhileTheSnapshotIsFresh()
    {
        var handler = StubHandler.Serving(CreateArchive("synced-marketplace"));
        var sync = new PluginRegistrySyncService(NewTempDir(), handler: handler);

        await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);
        var second = await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);

        Assert.Equal(PluginRegistrySyncResult.Skipped, second);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SyncAsync_BacksOffInsteadOfRetryingAFailedDownload()
    {
        var handler = StubHandler.Failing(HttpStatusCode.InternalServerError);
        var sync = new PluginRegistrySyncService(NewTempDir(), handler: handler);

        var first = await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);
        var second = await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);
        var third = await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);

        Assert.Equal(PluginRegistrySyncResult.Failed, first);
        Assert.Equal(PluginRegistrySyncResult.Skipped, second);
        Assert.Equal(PluginRegistrySyncResult.Skipped, third);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SyncAsync_ForceOverridesTheFailureBackoff()
    {
        var handler = StubHandler.Failing(HttpStatusCode.InternalServerError);
        var sync = new PluginRegistrySyncService(NewTempDir(), handler: handler);

        await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: false, CancellationToken.None);
        await sync.SyncAsync(RegistryUrl, MarketplacePath, "Synced", force: true, CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task SyncAsync_RejectsANonHttpsSource()
    {
        var handler = StubHandler.Serving(CreateArchive("synced-marketplace"));
        var sync = new PluginRegistrySyncService(NewTempDir(), handler: handler);

        var result = await sync.SyncAsync(
            "http://registry.test/registry.zip",
            MarketplacePath,
            "Synced",
            force: true,
            CancellationToken.None);

        Assert.Equal(PluginRegistrySyncResult.Skipped, result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SyncDueAsync_CoversTheHostProvidedDefaultRegistry()
    {
        var handler = StubHandler.Serving(CreateArchive("default-marketplace"));
        var previous = Environment.GetEnvironmentVariable(
            PluginSourceRegistryCatalog.DefaultRegistryUrlEnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(
                PluginSourceRegistryCatalog.DefaultRegistryUrlEnvironmentVariableName,
                RegistryUrl);
            var craftHome = NewTempDir();

            var activated = await new PluginRegistrySyncService(craftHome, handler: handler)
                .SyncDueAsync(new AppConfig.PluginsConfig(), force: false, CancellationToken.None);

            Assert.True(activated);
            Assert.Equal(1, handler.RequestCount);
            Assert.True(Directory.Exists(
                new PluginRegistryArchiveCache(craftHome).SnapshotRootFor(RegistryUrl, MarketplacePath)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                PluginSourceRegistryCatalog.DefaultRegistryUrlEnvironmentVariableName,
                previous);
        }
    }

    private static byte[] CreateArchive(string marketplaceName)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(MarketplacePath);
            using var writer = new StreamWriter(entry.Open());
            writer.Write($$"""
{
  "name": "{{marketplaceName}}",
  "plugins": []
}
""");
        }

        return stream.ToArray();
    }

    private static string NewTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "dotcraft-registry-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body;
        private int _requestCount;

        private StubHandler(HttpStatusCode status, byte[] body)
        {
            _status = status;
            _body = body;
        }

        public static StubHandler Serving(byte[] body) => new(HttpStatusCode.OK, body);

        public static StubHandler Failing(HttpStatusCode status) => new(status, []);

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent(_body)
            });
        }
    }
}
