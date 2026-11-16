using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

[Collection("Plugin registry environment")]
public sealed class MarketplaceOfficialRefreshTests : IDisposable
{
    private const string OfficialUrl = OfficialPluginRegistrySync.ArchiveUrl;
    private const string Revision = "1234567890abcdef1234567890abcdef12345678";
    private readonly string _craftHome = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"marketplace-official-{Guid.NewGuid():N}")).FullName;
    private readonly string? _previousDefault = Environment.GetEnvironmentVariable(
        PluginSourceRegistryCatalog.DefaultRegistryUrlEnvironmentVariableName);
    private readonly string? _previousAdditional = Environment.GetEnvironmentVariable(
        PluginSourceRegistryCatalog.AdditionalRegistriesEnvironmentVariableName);

    public MarketplaceOfficialRefreshTests()
    {
        Environment.SetEnvironmentVariable(PluginSourceRegistryCatalog.DefaultRegistryUrlEnvironmentVariableName, OfficialUrl);
        Environment.SetEnvironmentVariable(PluginSourceRegistryCatalog.AdditionalRegistriesEnvironmentVariableName, null);
    }

    [Fact]
    public async Task RefreshAsync_UsesOfficialFallbackAndReportsAnUnchangedRevisionAsSuccess()
    {
        var handler = new OfficialHandler();
        var gitAttempts = 0;
        var sync = new PluginRegistrySyncService(_craftHome, null, handler, (_, _, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            gitAttempts++;
            throw new Win32Exception("Git is unavailable.");
        });
        var manager = new MarketplaceManager(_craftHome, archiveSync: sync);

        var initial = await manager.RefreshAsync(name: null, CancellationToken.None);
        var refreshed = await manager.RefreshAsync("official-test", CancellationToken.None);

        Assert.Empty(initial.Errors);
        Assert.Empty(refreshed.Errors);
        var initialMarketplace = Assert.Single(initial.Marketplaces);
        var refreshedMarketplace = Assert.Single(refreshed.Marketplaces);
        Assert.Equal("official-test", refreshedMarketplace.Name);
        Assert.Equal(initialMarketplace.Root, refreshedMarketplace.Root);
        Assert.False(refreshedMarketplace.Removable);
        Assert.Equal(OfficialUrl, refreshedMarketplace.Source);
        Assert.Equal(2, gitAttempts);
        Assert.Equal(3, handler.RequestCount);
        Assert.Equal(1, handler.ArchiveCount);
        Assert.Equal(Revision, new PluginRegistryArchiveCache(_craftHome).RevisionFor(
            OfficialUrl, MarketplaceDocumentLoader.DefaultMarketplacePath));
    }

    [Fact]
    public async Task SyncDueAsync_DoesNotSignalANewSnapshotWhenTheOfficialRevisionIsUnchanged()
    {
        var handler = new OfficialHandler();
        var sync = new PluginRegistrySyncService(_craftHome, null, handler,
            (_, _, _, _) => throw new Win32Exception("Git is unavailable."));
        var config = new AppConfig.PluginsConfig();

        var initialActivation = await sync.SyncDueAsync(config, force: true, CancellationToken.None);
        var unchangedActivation = await sync.SyncDueAsync(config, force: true, CancellationToken.None);

        Assert.True(initialActivation);
        Assert.False(unchangedActivation);
        Assert.Equal(1, handler.ArchiveCount);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PluginSourceRegistryCatalog.DefaultRegistryUrlEnvironmentVariableName, _previousDefault);
        Environment.SetEnvironmentVariable(PluginSourceRegistryCatalog.AdditionalRegistriesEnvironmentVariableName, _previousAdditional);
        Directory.Delete(_craftHome, recursive: true);
    }

    private sealed class OfficialHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public int ArchiveCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            RequestCount++;
            var path = request.RequestUri!.AbsolutePath;
            HttpContent content;
            if (path.EndsWith($"/zipball/{Revision}", StringComparison.Ordinal))
            {
                ArchiveCount++;
                using var stream = new MemoryStream();
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var entry = archive.CreateEntry("repo/.craft/plugins/marketplace.json");
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write("{\"name\":\"official-test\",\"plugins\":[]}");
                }
                content = new ByteArrayContent(stream.ToArray());
            }
            else
            {
                Assert.EndsWith("/git/ref/heads/main", path);
                content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new { @object = new { sha = Revision } }));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
