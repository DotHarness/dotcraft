using System.Net;
using System.Text;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;
using DotCraft.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotCraft.Tests.Harness;

public sealed class RemoteModelRuntimeConnectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("deepseek")]
    public void RemoteCatalogPreservesOptionalSearchSupportWithoutUpstreamCredentials(string? backend)
    {
        var config = new AppConfig { ModelService = new() { Endpoint = "https://service.test", Token = "caller" } };
        RemoteModelConfiguration.Apply(config, new ModelServiceCatalog("caller", [
            new RemoteModelProvider("account", "Account", "anthropic", "https://api.deepseek.com/anthropic",
                "apiKey", false, false, new ProviderAuthenticationStatus(true), WebSearchBackend: backend)
        ]));
        var runtime = ModelProviderResolver.ResolveProvider(config, "account");
        Assert.Equal(backend, runtime.WebSearchBackend);
        Assert.True(runtime.IsRemote);
        Assert.Empty(runtime.ApiKey);
    }

    [Fact]
    public async Task CatalogRefresh_ReportsTheProviderRegistryRegionOnlyWhenTheCatalogChanges()
    {
        var config = new AppConfig
        {
            ModelService = new AppConfig.ModelServiceConfig { Endpoint = "https://models.test/", Token = "token" }
        };
        var monitor = new AppConfigMonitor(config);
        var changes = new List<AppConfigChangedEventArgs>();
        monitor.Changed += (_, change) => changes.Add(change);
        using var transport = new RemoteProviderTransport(
            new ModelServiceConnection(new Uri("https://models.test/"), "token"),
            new HttpClient(new CatalogHandler()));
        await using var connection = new RemoteModelRuntimeConnection(
            config,
            transport,
            monitor,
            NullLogger<RemoteModelRuntimeConnection>.Instance);

        await connection.RefreshAsync(CancellationToken.None);
        await connection.RefreshAsync(CancellationToken.None);

        Assert.Equal([ConfigChangeRegions.ProviderRegistry], Assert.Single(changes).Regions);
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "callerId": "caller", "providers": [] }""", Encoding.UTF8, "application/json")
            });
    }
}
