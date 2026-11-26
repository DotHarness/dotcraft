using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;
using DotCraft.Harness;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotCraft.Tests.Harness;

public sealed class RemoteHttpBackendTests
{
    [Fact]
    public async Task HarnessUsesRemoteBackendMetadataAndUsageWithoutLocalBackend()
    {
        const string providerId = "account";
        const string modelId = "maas/claude";
        const string backendId = "maas-test";
        var budget = new ProviderContextBudget(128_000);
        var usage = new ProviderUsageSnapshot(DateTimeOffset.UtcNow, new Dictionary<string, double> { ["usage"] = 5 });
        var model = new ModelCatalogEntry { Id = modelId, DisplayName = "Claude", MaxOutputTokens = 6_000 };
        var catalog = new ModelServiceCatalog("caller", [
            new RemoteModelProvider(providerId, "Account", "auto", "https://private-gateway.test",
                "personalToken", false, false, new ProviderAuthenticationStatus(true), MaxOutputTokens: 8_000,
                Models: new Dictionary<string, ProviderRequestAdaptation>
                {
                    [modelId] = new(false, false, null, null, model, ModelProviderProtocols.Anthropic)
                    {
                        ContextBudget = budget,
                        Capabilities = new ModelProviderCapabilities { ToolCalling = false }
                    }
                },
                BackendId: backendId,
                OpenRouterRouting: JsonSerializer.SerializeToElement(new { Only = new[] { "not-applicable" } }),
                CatalogState: new ModelCatalogResult { Success = true },
                Usage: usage)
        ]);
        var config = new AppConfig();
        var services = new ServiceCollection();
        services.AddDotCraftHarness(config, options =>
        {
            options.WorkspacePath = Path.Combine(Path.GetTempPath(), $"dotcraft-harness-{Guid.NewGuid():N}");
            options.ModelService = new ModelServiceConnection(new Uri("https://service.test"), "worker-token");
            options.InitialModelServiceCatalog = catalog;
            options.RefreshModelServiceCatalog = false;
        });
        await using var host = services.BuildServiceProvider();
        var clients = host.GetRequiredService<ChatClientRegistry>();
        var models = await ModelProviderCatalog.FetchAsync(config, clients.RuntimeResolver, providerId);
        Assert.Equal(modelId, Assert.Single(models.Models).Id);
        var runtime = clients.ResolveMainRuntime(config, providerId, modelId);
        Assert.Equal(ModelProviderProtocols.Anthropic, runtime.Protocol);
        Assert.NotEqual(new Uri(catalog.Providers[0].Endpoint).Host, new Uri(runtime.EndPoint).Host);
        Assert.Empty(runtime.ApiKey);
        Assert.Empty(config.Providers[providerId].BackendSettings);
        Assert.Equal(budget, runtime.ContextBudget);
        Assert.Equal(6_000, runtime.MaxOutputTokens);
        Assert.False(runtime.Capabilities.ToolCalling);

        var authentication = host.GetRequiredService<ProviderAuthenticationCoordinator>();
        Assert.True((await authentication.GetStatusAsync(config, providerId, default)).IsAuthenticated);
        var reportedUsage = await authentication.ReadUsageAsync(config, providerId, default);
        Assert.Equal(5, reportedUsage?.Values["usage"]);
        Assert.Null(await authentication.GetAccessTokenAsync(config, providerId, false, default));
        Assert.Throws<ArgumentException>(() => clients.ResolveMainRuntime(config, providerId, "missing-model"));

        RemoteModelConfiguration.Apply(config, new ModelServiceCatalog("caller", [
            catalog.Providers[0] with
            {
                Models = new Dictionary<string, ProviderRequestAdaptation>(),
                CatalogState = new ModelCatalogResult { ErrorCode = ModelCatalogErrorCode.Network }
            }
        ]));
        var refreshed = await ModelProviderCatalog.FetchAsync(config, clients.RuntimeResolver, providerId);
        Assert.False(refreshed.Success);
        Assert.Empty(refreshed.Models);
        Assert.Equal(ModelCatalogErrorCode.Network, refreshed.ErrorCode);
        Assert.Throws<ArgumentException>(() => clients.ResolveMainRuntime(config, providerId, modelId));
    }
}
