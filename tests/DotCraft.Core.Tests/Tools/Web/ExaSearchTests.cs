using System.Net;
using System.Text;
using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tools;
using DotCraft.Tools.Web;
using Xunit;

namespace DotCraft.Tests.Tools.Web;

public sealed class ExaSearchTests
{
    private const string RateLimitedBody =
        "event: message\ndata: {\"result\":{\"_meta\":{\"ai.exa/rateLimited\":true},\"content\":[{\"type\":\"text\",\"text\":\"You've hit Exa's free MCP rate limit. To continue using without limits, create your own Exa API key....\"}]},\"jsonrpc\":\"2.0\",\"id\":1}";

    private const string ResultText =
        "Title: DotCraft\nURL: https://example.org/dotcraft\nPublished: 2026-10-01\nAuthor: Aki\nHighlights:\nAn agent runtime.\nMore text.\n\n---\n\nTitle: Second\nURL: https://example.org/second\nHighlights:\nSecond body";

    private static string RpcBody(string text, bool isError = false) =>
        JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new { content = new[] { new { type = "text", text } }, isError }
        });

    [Fact]
    public async Task RateLimitedFreeEndpointIsReportedAsError()
    {
        StubHttpHandler? handler = null;
        var output = await SearchWith(null, _ => Text(RateLimitedBody, "text/event-stream"), h => handler = h);

        Assert.Equal("https://mcp.exa.ai/mcp", Assert.Single(handler!.Requests).RequestUri!.ToString());
        var error = output.RootElement.GetProperty("error").GetString();
        Assert.Contains("rate limited", error);
        Assert.Contains("Tools.Web.ExaApiKey", error);
        Assert.Contains("Bing", error);
        Assert.Equal("dotcraft", output.RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public void IsErrorResultIsReportedAsError()
    {
        var ex = Assert.Throws<WebToolException>(() => ExaSearch.ParseMcpResponse(RpcBody("Invalid query", isError: true)));
        Assert.Contains("Invalid query", ex.Message);
    }

    [Fact]
    public void UnparseableTextIsReportedAsError()
    {
        var ex = Assert.Throws<WebToolException>(() => ExaSearch.ParseMcpResponse(RpcBody("Something unexpected happened.")));
        Assert.Contains("Something unexpected happened.", ex.Message);
    }

    [Fact]
    public void ParsesSseBody()
    {
        var results = ExaSearch.ParseMcpResponse($"event: message\r\ndata: {RpcBody(ResultText)}\r\n\r\n");

        Assert.Equal(2, results.Count);
        Assert.Equal("DotCraft", results[0].Title);
        Assert.Equal("https://example.org/dotcraft", results[0].Url);
        Assert.Equal("Aki", results[0].Author);
        Assert.Equal("2026-10-01", results[0].PublishedDate);
        Assert.Equal("An agent runtime. More text.", results[0].Snippet);
    }

    [Fact]
    public void ParsesHighlightsFormatWithUnknownFields()
    {
        const string text = "Title: N/A\nURL: https://example.org/a\nPublished: N/A\nAuthor: N/A\nHighlights:\nFirst highlight.\nSecond highlight.\n\n---\n\nTitle: B\nURL: https://example.org/b\nPublished: 2026-09-01\nAuthor: Kim\nHighlights:\nOnly one.";

        var results = ExaSearch.ParseMcpResponse(RpcBody(text));

        Assert.Equal(2, results.Count);
        Assert.Equal("https://example.org/a", results[0].Title);
        Assert.Null(results[0].Author);
        Assert.Null(results[0].PublishedDate);
        Assert.Equal("First highlight. Second highlight.", results[0].Snippet);
        Assert.Equal("B", results[1].Title);
        Assert.Equal("Kim", results[1].Author);
        Assert.Equal("2026-09-01", results[1].PublishedDate);
        Assert.Equal("Only one.", results[1].Snippet);
    }

    [Fact]
    public void ParsesPlainJsonBody()
    {
        var results = ExaSearch.ParseMcpResponse(RpcBody(ResultText));

        Assert.Equal(["https://example.org/dotcraft", "https://example.org/second"], results.Select(r => r.Url));
    }

    [Fact]
    public async Task ApiKeyUsesRestEndpointAndParsesResults()
    {
        const string body = """
            {"requestId":"r1","results":[
              {"title":"Exa","url":"https://exa.ai/","publishedDate":"2026-09-30T00:00:00.000Z","author":"Exa Team","highlights":["Search built for AI."]},
              {"title":null,"url":"https://example.org/untitled","highlights":[]}
            ]}
            """;
        StubHttpHandler? handler = null;
        var output = await SearchWith("test-key", _ => Text(body, "application/json"), h => handler = h);

        var request = Assert.Single(handler!.Requests);
        Assert.Equal("https://api.exa.ai/search", request.RequestUri!.ToString());
        Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());
        using var sent = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal("dotcraft", sent.RootElement.GetProperty("query").GetString());
        Assert.Equal("auto", sent.RootElement.GetProperty("type").GetString());
        Assert.Equal(3, sent.RootElement.GetProperty("numResults").GetInt32());
        Assert.Equal(1, sent.RootElement.GetProperty("contents").GetProperty("highlights").GetProperty("highlightsPerUrl").GetInt32());

        var root = output.RootElement;
        Assert.Equal("exa", root.GetProperty("provider").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("notice").GetString()));
        var results = root.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Equal("Exa", results[0].GetProperty("title").GetString());
        Assert.Equal("Search built for AI.", results[0].GetProperty("snippet").GetString());
        Assert.Equal("Exa Team", results[0].GetProperty("author").GetString());
        Assert.Equal("2026-09-30T00:00:00.000Z", results[0].GetProperty("publishedDate").GetString());
        Assert.Equal("https://example.org/untitled", results[1].GetProperty("title").GetString());
    }

    [Fact]
    public async Task RestHttpErrorIsReportedAsError()
    {
        var output = await SearchWith("bad-key", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"Invalid API key\"}", Encoding.UTF8, "application/json")
        });

        Assert.Contains("401", output.RootElement.GetProperty("error").GetString());
    }

    private static async Task<JsonDocument> SearchWith(
        string? apiKey,
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        Action<StubHttpHandler>? capture = null)
    {
        var handler = new StubHttpHandler(respond);
        capture?.Invoke(handler);
        var tools = new WebTools(new AppConfig.WebToolsConfig(), apiKey, new HttpClient(handler));
        return JsonDocument.Parse(await tools.WebSearch("dotcraft", 3));
    }

    private static HttpResponseMessage Text(string body, string mediaType) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
}
