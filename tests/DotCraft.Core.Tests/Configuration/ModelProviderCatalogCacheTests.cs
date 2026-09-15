using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ModelProviderCatalogCacheTests
{
    [Fact]
    public async Task FetchAsync_ReusesCatalogForRepeatedListings()
    {
        var provider = new CountingCatalogProvider();
        var registry = new ModelProviderRegistry([provider]);
        var config = CreateConfig(apiKey: "key-a");

        var first = await ModelProviderCatalog.FetchAsync(config, registry, "test-provider");
        var second = await ModelProviderCatalog.FetchAsync(config, registry, "test-provider");

        Assert.Equal(1, provider.FetchCount);
        Assert.Equal(["model-a"], second.Models.Select(model => model.Id));
        Assert.Equal("test-provider", second.ProviderId);
        Assert.NotSame(first.Models, second.Models);
    }

    [Fact]
    public async Task FetchAsync_QueriesProviderForEveryOnlineRequest()
    {
        var provider = new CountingCatalogProvider();
        var registry = new ModelProviderRegistry([provider]);
        var config = CreateConfig(apiKey: "key-b");

        await ModelProviderCatalog.FetchAsync(config, registry, "test-provider", ModelCatalogRefreshStrategy.Online);
        await ModelProviderCatalog.FetchAsync(config, registry, "test-provider", ModelCatalogRefreshStrategy.Online);

        Assert.Equal(2, provider.FetchCount);
    }

    [Fact]
    public async Task FetchAsync_RefetchesWhenCredentialsChange()
    {
        var provider = new CountingCatalogProvider();
        var registry = new ModelProviderRegistry([provider]);

        await ModelProviderCatalog.FetchAsync(config: CreateConfig(apiKey: "key-c"), registry, "test-provider");
        await ModelProviderCatalog.FetchAsync(config: CreateConfig(apiKey: "key-c-rotated"), registry, "test-provider");

        Assert.Equal(2, provider.FetchCount);
    }

    [Fact]
    public async Task GetOrFetchAsync_RefetchesAfterCatalogExpires()
    {
        var clock = new TestClock();
        var cache = new ModelProviderCatalogCache(clock);
        var fetches = 0;

        await cache.GetOrFetchAsync("identity", ModelCatalogRefreshStrategy.OnlineIfUncached, _ =>
        {
            fetches++;
            return Task.FromResult(SuccessResult("model-a"));
        }, CancellationToken.None);

        clock.Advance(ModelProviderCatalogCache.CatalogTtl + TimeSpan.FromSeconds(1));
        await cache.GetOrFetchAsync("identity", ModelCatalogRefreshStrategy.OnlineIfUncached, _ =>
        {
            fetches++;
            return Task.FromResult(SuccessResult("model-b"));
        }, CancellationToken.None);

        Assert.Equal(2, fetches);
    }

    [Fact]
    public async Task GetOrFetchAsync_KeepsServingLastCatalogWhenRefreshFails()
    {
        var clock = new TestClock();
        var cache = new ModelProviderCatalogCache(clock);
        var fetches = 0;

        await cache.GetOrFetchAsync("identity", ModelCatalogRefreshStrategy.OnlineIfUncached, _ =>
        {
            fetches++;
            return Task.FromResult(SuccessResult("model-a"));
        }, CancellationToken.None);

        clock.Advance(ModelProviderCatalogCache.CatalogTtl + TimeSpan.FromSeconds(1));
        var afterFailure = await cache.GetOrFetchAsync("identity", ModelCatalogRefreshStrategy.OnlineIfUncached, _ =>
        {
            fetches++;
            return Task.FromResult(FailureResult());
        }, CancellationToken.None);

        var throttled = await cache.GetOrFetchAsync("identity", ModelCatalogRefreshStrategy.OnlineIfUncached, _ =>
        {
            fetches++;
            return Task.FromResult(FailureResult());
        }, CancellationToken.None);

        Assert.Equal(2, fetches);
        Assert.True(afterFailure.Success);
        Assert.Equal(["model-a"], afterFailure.Models.Select(model => model.Id));
        Assert.Equal(["model-a"], throttled.Models.Select(model => model.Id));
    }

    [Fact]
    public async Task GetOrFetchAsync_ThrottlesEndpointThatKeepsFailing()
    {
        var clock = new TestClock();
        var cache = new ModelProviderCatalogCache(clock);
        var fetches = 0;

        Task<ModelCatalogResult> Fetch(CancellationToken _)
        {
            fetches++;
            return Task.FromResult(FailureResult());
        }

        var first = await cache.GetOrFetchAsync("identity", ModelCatalogRefreshStrategy.OnlineIfUncached, Fetch, CancellationToken.None);
        await cache.GetOrFetchAsync("identity", ModelCatalogRefreshStrategy.OnlineIfUncached, Fetch, CancellationToken.None);
        Assert.Equal(1, fetches);
        Assert.False(first.Success);
        Assert.Equal(ModelCatalogErrorCode.EndpointNotSupported, first.ErrorCode);

        clock.Advance(ModelProviderCatalogCache.FailureRetryDelay + TimeSpan.FromSeconds(1));
        await cache.GetOrFetchAsync("identity", ModelCatalogRefreshStrategy.OnlineIfUncached, Fetch, CancellationToken.None);

        Assert.Equal(2, fetches);
    }

    private static ModelCatalogResult SuccessResult(string modelId) => new()
    {
        Success = true,
        Models = [new ModelCatalogEntry { Id = modelId }]
    };

    private static ModelCatalogResult FailureResult() => new()
    {
        Success = false,
        ErrorCode = ModelCatalogErrorCode.EndpointNotSupported,
        ErrorMessage = "Endpoint does not support model listing."
    };

    private static AppConfig CreateConfig(string apiKey) => new()
    {
        Providers = new Dictionary<string, AppConfig.ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["test-provider"] = new()
            {
                Protocol = ModelProviderProtocols.Anthropic,
                ApiKey = apiKey,
                EndPoint = "https://models.test"
            }
        }
    };

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class CountingCatalogProvider : IModelProvider, IModelCatalogProvider
    {
        public int FetchCount { get; private set; }

        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.Anthropic];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public Task<ModelCatalogResult> FetchModelsAsync(
            EffectiveModelRuntime runtime,
            CancellationToken cancellationToken)
        {
            FetchCount++;
            return Task.FromResult(SuccessResult("model-a"));
        }
    }
}
