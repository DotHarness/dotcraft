using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.OpenRouter;

public sealed class CatalogTests
{
    [Fact]
    public async Task SupplierDirectoryWorksBeforeAnApiKeyIsConfiguredAndPreservesSlugs()
    {
        var handler = new Replies((request, _) =>
        {
            Assert.Equal("/api/v1/providers", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return Reply("""{"data":[{"slug":"supplier-b","name":"Second"},{"slug":"supplier-a","name":"First"}]}""");
        });
        var provider = RoutingTests.Create(RoutingTests.Runtime() with { ApiKey = "" }, new HttpClient(handler));
        var suppliers = await provider.FetchRoutingProvidersAsync(default);
        Assert.Equal(["supplier-a", "supplier-b"], suppliers.Select(supplier => supplier.Id));
        Assert.Equal(["First", "Second"], suppliers.Select(supplier => supplier.DisplayName));
    }

    internal const string Catalog = """
        {"data":[{"id":"openai/new-model","name":"A model","created":1,"architecture":{"input_modalities":["text","image"],"output_modalities":["text"]},"supported_parameters":["tools","reasoning"],"endpoints":["/api/v1/responses","/api/v1/chat/completions"],"context_length":200000,"top_provider":{"context_length":100000,"max_completion_tokens":32000},"reasoning":{"mandatory":true,"supported_efforts":["medium","xhigh","max"],"default_effort":"xhigh"},"pricing":{"prompt":"0.000001","completion":"0.000002"}}]}
        """;

    [Fact]
    public async Task RefreshUsesKeyScopedMetadataAndPersistsOnlySuccessfulCatalogs()
    {
        var directory = Path.Combine(Path.GetTempPath(), "openrouter_catalog_" + Guid.NewGuid().ToString("N"));
        var handler = new Replies((request, call) => call < 2 ? Reply(Catalog) : new(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        var runtime = RoutingTests.Runtime() with { ProviderStateDirectory = directory };
        try
        {
            var provider = RoutingTests.Create(runtime, http);
            var refreshed = await provider.FetchModelsAsync(runtime, default);
            Assert.True(refreshed.Success);
            Assert.Equal("user", refreshed.Source);
            var model = Assert.Single(refreshed.Models);
            Assert.Equal(100000, model.ContextWindow);
            Assert.Equal(32000, model.MaxOutputTokens);
            Assert.False(model.Reasoning!.SupportsDisable);
            Assert.Equal(ProviderReasoningEffort.ExtraHigh, model.Reasoning.DefaultEffort);
            Assert.Equal(1m, model.Pricing!.InputPerMillion);
            Assert.All(handler.Authorizations, value => Assert.Equal("Bearer test-key", value));
            var failed = await provider.FetchModelsAsync(runtime, default);
            Assert.False(failed.Success);
            Assert.Equal(ModelCatalogErrorCode.Unauthorized, failed.ErrorCode);
            Assert.Equal("usingCachedCatalog", failed.RefreshNotice);
            Assert.Single(failed.Models);
            Assert.Equal("openai-responses", provider.ResolveRuntime(runtime with { Model = model.Id }).Protocol);
            var restarted = RoutingTests.Create(runtime, http);
            Assert.Equal(100000, restarted.FindModel(model.Id).ContextWindow);
            var otherKey = RoutingTests.Create(runtime with { ApiKey = "different-key" }, http);
            Assert.Throws<ArgumentException>(() => otherKey.FindModel(model.Id));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LimitedKeyUsagePreservesAmountsAndMissingValues()
    {
        var provider = RoutingTests.Create(http: new HttpClient(new Replies((_, _) =>
            Reply("""{"data":{"usage":12.5,"usage_daily":1.25,"usage_weekly":4.5,"limit":50,"limit_remaining":37.5}}"""))));
        var usage = (await provider.ReadAsync(default))!;
        Assert.Equal(12.5, usage.Values["usage"]);
        Assert.Equal(1.25, usage.Values["usage_daily"]);
        Assert.Equal(4.5, usage.Values["usage_weekly"]);
        Assert.Equal(50, usage.Values["limit"]);
        Assert.Equal(37.5, usage.Values["limit_remaining"]);
        Assert.False(usage.Values.ContainsKey("usage_monthly"));
        Assert.False(usage.Values.ContainsKey("last_request_cost"));
        Assert.False(usage.Credits!.Unlimited);
        Assert.Equal("37.5", usage.Credits.Balance);
    }

    [Theory]
    [InlineData("{}", 5)]
    [InlineData("{\"supported_efforts\":null}", 5)]
    [InlineData("{\"supported_efforts\":[]}", 0)]
    public void ReasoningMetadataKeepsMissingNullAndEmptyEffortsDistinct(string reasoning, int expected)
    {
        var json = JsonNode.Parse(Catalog)!;
        json["data"]![0]!["id"] = "anthropic/claude-opus-5.5";
        json["data"]![0]!["reasoning"] = JsonNode.Parse(reasoning);
        var model = Assert.Single(OpenRouterModelCatalog.Read(json.ToJsonString()));
        Assert.Equal(expected, model.Reasoning!.SupportedEfforts.Count);
        Assert.Equal(ProviderReasoningEffort.Medium, model.Reasoning.DefaultEffort);
        Assert.False(model.Reasoning.SupportsDisable);
    }

    [Theory]
    [InlineData("""["max","xhigh","high","medium","low"]""")]
    [InlineData("""["medium","max","low","high","xhigh","low","minimal","unknown"]""")]
    public void ReasoningEffortsAreNormalizedToAscendingIntensityWithoutChangingDefaults(string efforts)
    {
        var json = JsonNode.Parse(Catalog)!;
        json["data"]![0]!["reasoning"]!["supported_efforts"] = JsonNode.Parse(efforts);
        var reasoning = Assert.Single(OpenRouterModelCatalog.Read(json.ToJsonString())).Reasoning!;
        Assert.Equal(
            [ProviderReasoningEffort.Low, ProviderReasoningEffort.Medium, ProviderReasoningEffort.High,
                ProviderReasoningEffort.ExtraHigh, ProviderReasoningEffort.Max],
            reasoning.SupportedEfforts);
        Assert.Equal(ProviderReasoningEffort.ExtraHigh, reasoning.DefaultEffort);
        Assert.Equal(ProviderReasoningEffort.ExtraHigh, reasoning.UpstreamDefaultEffort);
    }

    [Fact]
    public async Task MissingUsagePermissionIsUnavailableRatherThanZero()
    {
        var provider = RoutingTests.Create(http: new HttpClient(new Replies((_, _) => new(HttpStatusCode.Forbidden))));
        Assert.Null(await provider.ReadAsync(default));
    }

    [Fact]
    public async Task UnsupportedPersonalizationIsVisibleAndUsageDoesNotInventCosts()
    {
        var handler = new Replies((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/models/user")
            ? new(HttpStatusCode.NotFound) : request.RequestUri.AbsolutePath.EndsWith("/key")
                ? Reply("""{"data":{"usage":12.5,"limit":null,"limit_remaining":null}}""") : Reply(Catalog));
        var provider = RoutingTests.Create(http: new HttpClient(handler));
        var catalog = await provider.FetchModelsAsync(RoutingTests.Runtime(), default);
        Assert.True(catalog.Success);
        Assert.Equal("personalizationUnavailable", catalog.RefreshNotice);
        var usage = await provider.ReadAsync(default);
        Assert.Equal(12.5, usage!.Values["usage"]);
        Assert.False(usage.Values.ContainsKey("last_request_cost"));
        Assert.True(usage.Credits!.Unlimited);
        Assert.Null(usage.Credits.Balance);
    }

    internal static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    internal sealed class Replies(Func<HttpRequestMessage, int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        private int _calls;
        internal List<string?> Authorizations { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Authorizations.Add(request.Headers.Authorization?.ToString()); return Task.FromResult(reply(request, _calls++)); }
    }
}
