using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tools;
using DotCraft.Tools.Web;
using Xunit;

namespace DotCraft.Tests.Tools.Web;

public sealed class DeepSeekSearchTests
{
    [Fact]
    public async Task SearchUsesBearerAndMapsSummariesWithoutChangingTheToolContract()
    {
        var handler = new StubHttpHandler(_ => Json("""
            {"results":[
              {"title":"First","url":"https://example.org/a","summary":"长摘要\n...\na > b & c","snippet":"Short","date":"2026-10-17T00:00:00Z"},
              {"title":"","url":"https://example.org/b","summary":"","snippet":"Fallback"}]}
            """));
        var tools = Tools(handler);
        var result = await tools.WebSearch("DeepSeek", 2);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(ProviderWebSearchBackend.DeepSeekSearchEndpoint, sent.RequestUri);
        Assert.Equal("Bearer search-secret", sent.Headers.Authorization!.ToString());
        using var body = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal("DeepSeek", body.RootElement.GetProperty("query").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("count").GetInt32());
        Assert.False(body.RootElement.GetProperty("need_content").GetBoolean());
        Assert.Contains("- [First](https://example.org/a) (2026-10-17T00:00:00Z)\n  长摘要 ... a > b & c\n", result.Content);
        var structured = result.StructuredContent!.Value;
        Assert.Equal("deepseek", structured.GetProperty("provider").GetString());
        var items = structured.GetProperty("results");
        Assert.Equal("长摘要\n...\na > b & c", items[0].GetProperty("snippet").GetString());
        Assert.Equal("2026-10-17T00:00:00Z", items[0].GetProperty("publishedDate").GetString());
        Assert.Equal("https://example.org/b", items[1].GetProperty("title").GetString());
        Assert.Equal("Fallback", items[1].GetProperty("snippet").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("author").ValueKind);
    }

    [Theory]
    [InlineData(128, false)]
    [InlineData(129, true)]
    public async Task QueryLimitCountsUnicodeScalars(int count, bool invalid)
    {
        var handler = new StubHttpHandler(_ => Json("""{"results":[]}"""));
        var query = string.Concat(Enumerable.Repeat("😀", count));
        var result = await Tools(handler).WebSearch(query);
        Assert.Equal(invalid, !result.Success);
        Assert.Equal(invalid ? 0 : 1, handler.Requests.Count);
        if (!invalid) Assert.Equal(0, result.StructuredContent!.Value.GetProperty("results").GetArrayLength());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"results\":null}")]
    public async Task InvalidResponsesReturnToolErrors(string body)
    {
        Assert.False((await Tools(new StubHttpHandler(_ => Json(body))).WebSearch("query")).Success);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(402)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task HttpFailuresDoNotRetryOrExposeResponseSecrets(int status)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("search-secret") });
        var result = (await Tools(handler).WebSearch("query")).Content;
        Assert.Contains(status.ToString(), result);
        Assert.DoesNotContain("search-secret", result);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutReturnsErrorButCallerCancellationPropagates(bool callerCancels)
    {
        using var cancellation = new CancellationTokenSource();
        var config = new AppConfig.WebToolsConfig { SearchProvider = "DeepSeek", Timeout = 1 };
        var tools = new WebTools(config, null, new HttpClient(new WaitingHandler()), new WebSearchTarget("DeepSeek", "key"));
        if (callerCancels)
        {
            cancellation.CancelAfter(20);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.WebSearch("query", cancellationToken: cancellation.Token));
        }
        else
        {
            var result = await tools.WebSearch("query");
            Assert.Equal(ToolErrorCodes.Timeout, result.Error!.Code);
        }
    }

    [Fact]
    public async Task RemoteSearchUsesTheSelectedProviderTransportWithoutSendingAnUpstreamKey()
    {
        var handler = new StubHttpHandler(request =>
        {
            Assert.Equal("https://api.deepseek.com/anthropic/tools/search", request.RequestUri!.ToString());
            Assert.Null(request.Headers.Authorization);
            return Json("""{"results":[]}""");
        });
        var transport = new SearchTransport(new HttpClient(handler));
        var runtime = new EffectiveModelRuntime("account", "", "anthropic", "Account", "", "https://api.deepseek.com/anthropic",
            30, null, ModelProviderCapabilities.ForProtocol("anthropic"), IsRemote: true, WebSearchBackend: "deepseek");
        var tools = new WebTools(new AppConfig.WebToolsConfig(), null,
            new HttpClient(new StubHttpHandler(_ => throw new InvalidOperationException("Unexpected direct request."))),
            new WebSearchTarget("DeepSeek", Runtime: runtime), transport);
        Assert.True((await tools.WebSearch("query")).Success);
        Assert.Equal("account", transport.ProviderId);
        Assert.Single(handler.Requests);
    }

    private sealed class SearchTransport(HttpClient client) : IProviderHttpTransport
    {
        public string? ProviderId { get; private set; }
        public HttpClient CreateClient(string providerId, Uri providerEndpoint)
        {
            ProviderId = providerId;
            return client;
        }
    }

    private static WebTools Tools(StubHttpHandler handler) => new(
        new AppConfig.WebToolsConfig { SearchProvider = "DeepSeek" }, null, new HttpClient(handler), new WebSearchTarget("DeepSeek", "search-secret"));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
