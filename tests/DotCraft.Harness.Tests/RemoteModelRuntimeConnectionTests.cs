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

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    public void ServiceManagedNativeProviderBlocksOnlyReportedAuthenticationFailure(bool? authenticated, bool usable)
    {
        const string providerId = "remote-provider";
        var config = new AppConfig { ModelService = new() { Endpoint = "https://service.test", Token = "caller" } };
        RemoteModelConfiguration.Apply(config, new ModelServiceCatalog("caller", [
            new RemoteModelProvider(providerId, "Remote provider", ModelProviderProtocols.OpenAIResponses,
                "https://provider.invalid/v1", ModelProviderAuthMethods.ApiKey, false, false,
                authenticated is { } reported ? new ProviderAuthenticationStatus(reported) : null)
        ]));

        Assert.Equal(usable, ModelProviderResolver.IsProviderUsable(config, providerId));
    }

    [Fact]
    public void RemoteChatGptCatalogSuppliesTheWorkersContextBudget()
    {
        const string model = "subscription-test";
        var budget = new ProviderContextBudget(850_000, 94, 760_000);
        var config = new AppConfig { ModelService = new() { Endpoint = "https://service.test", Token = "caller" } };
        RemoteModelConfiguration.Apply(config, new ModelServiceCatalog("caller", [
            new RemoteModelProvider("account", "Account", ModelProviderProtocols.OpenAIResponses,
                ModelProviderDefaults.ChatGptBackendEndpoint, ModelProviderAuthMethods.ChatGptOAuth,
                false, false, new ProviderAuthenticationStatus(true),
                Models: new Dictionary<string, ProviderRequestAdaptation>
                {
                    [model] = new(true, false, null, null) { ContextBudget = budget }
                })
        ]));

        var runtime = new ChatClientRegistry(new OpenAIClientProvider()).ResolveMainRuntime(config, "account", model);

        Assert.Equal(budget, runtime.ContextBudget);
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
