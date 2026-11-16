using System.IO.Compression;
using System.Net;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

public sealed class MarketplaceArchiveRefreshTests : IDisposable
{
    private const string MarketplaceName = "archive-marketplace";
    private const string SourceUrl = "https://registry.test/plugins.zip";
    private const string MarketplacePath = ".craft/plugins/marketplace.json";
    private readonly string _craftHome = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"marketplace-archive-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task RefreshAsync_RetainsDownloadFailureDetailsAndCachedContent()
    {
        const HttpStatusCode status = HttpStatusCode.Forbidden;
        var cache = SeedMarketplace();
        var document = Path.Combine(cache.SnapshotRootFor(SourceUrl, MarketplacePath), MarketplacePath);
        var original = File.ReadAllText(document);
        var handler = new ResponseHandler(() => new HttpResponseMessage(status));
        var manager = new MarketplaceManager(_craftHome,
            archiveSync: new PluginRegistrySyncService(_craftHome, handler: handler));

        var result = await manager.RefreshAsync(MarketplaceName, CancellationToken.None);

        Assert.Empty(result.Marketplaces);
        var failure = Assert.Single(result.Errors);
        Assert.Equal(MarketplaceName, failure.Name);
        Assert.Equal(MarketplaceErrorCodes.FetchFailed, failure.Code);
        Assert.Contains($"HTTP {(int)status}", failure.Message);
        Assert.Equal(original, File.ReadAllText(document));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task RefreshAsync_PropagatesCancellationWithoutDiscardingTheCachedCatalog()
    {
        var cache = SeedMarketplace();
        using var cancellation = new CancellationTokenSource();
        var handler = new ResponseHandler(() =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var manager = new MarketplaceManager(_craftHome,
            archiveSync: new PluginRegistrySyncService(_craftHome, handler: handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.RefreshAsync(MarketplaceName, cancellation.Token));

        Assert.True(File.Exists(Path.Combine(cache.SnapshotRootFor(SourceUrl, MarketplacePath),
            MarketplacePath)));
        Assert.Equal(1, handler.RequestCount);
    }

    private PluginRegistryArchiveCache SeedMarketplace()
    {
        PluginsConfigPersistence.WritePluginRegistries(Path.Combine(_craftHome, "config.json"),
            [new AppConfig.PluginRegistryConfig
            {
                Name = MarketplaceName,
                SourceType = "archive",
                Url = SourceUrl,
                MarketplacePath = MarketplacePath
            }]);
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(MarketplacePath);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{\"name\":\"archive-marketplace\",\"plugins\":[]}");
        }

        var cache = new PluginRegistryArchiveCache(_craftHome);
        cache.Activate(SourceUrl, MarketplacePath, bytes.ToArray());
        return cache;
    }

    public void Dispose() => Directory.Delete(_craftHome, recursive: true);

    private sealed class ResponseHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(respond());
        }
    }
}
