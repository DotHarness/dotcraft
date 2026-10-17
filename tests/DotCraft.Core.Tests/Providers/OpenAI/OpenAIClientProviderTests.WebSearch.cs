using System.Net;
using System.Text.Json.Nodes;
using DotCraft.Auth.OpenAI;
using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIClientProviderTests
{
    [Fact]
    public async Task SearchAsync_PostsCommandsToTheSearchServiceAndReturnsOutputAndResults()
    {
        await using var server = RecordingHttpServer.Start(JsonResponse(
            """
            {"encrypted_output":"opaque","output":"Answer citeturn0search0",
             "results":[{"type":"text_result","domain":"example.com","ref_id":"turn0search0","snippet":"s","title":"t","url":"https://example.com"},"ignored"]}
            """));
        IProviderWebSearch provider = new OpenAIClientProvider(
            new FakeOpenAIAuthService("acct-token"),
            chatGptHttpMessageHandler: null);
        var input = new JsonArray(new JsonObject
        {
            ["type"] = "message",
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = "latest news" })
        });

        var result = await provider.SearchAsync(
            OAuthRuntime($"{server.Endpoint}/backend-api/agent", "acct-token"),
            new ProviderWebSearchRequest(
                "thread_1",
                input,
                new JsonObject { ["search_query"] = new JsonArray(new JsonObject { ["q"] = "news" }) }),
            CancellationToken.None);

        Assert.Equal("Answer citeturn0search0", result.Output);
        var entry = Assert.IsType<JsonObject>(Assert.Single(result.Results));
        Assert.Equal("turn0search0", entry["ref_id"]!.GetValue<string>());
        var request = Assert.Single(server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/backend-api/agent/alpha/search", request.Path);
        Assert.Equal("Bearer access-token", request.Headers["Authorization"]);
        Assert.Equal("acct-token", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
        Assert.Equal(OpenAIAuthConstants.Originator, request.Headers[OpenAIAuthConstants.OriginatorHeader]);
        Assert.False(string.IsNullOrWhiteSpace(request.Headers["User-Agent"]));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                {
                  "id": "thread_1",
                  "model": "gpt-test",
                  "input": [{"type":"message","role":"user","content":[{"type":"input_text","text":"latest news"}]}],
                  "commands": {"search_query":[{"q":"news"}]},
                  "settings": {"allowed_callers":["direct"],"external_web_access":true},
                  "max_output_tokens": 10000
                }
                """),
            JsonNode.Parse(request.Body)));
    }

    [Fact]
    public async Task SearchAsync_HttpErrorSurfacesStatusAndBody()
    {
        await using var server = RecordingHttpServer.Start(new RecordingHttpServer.ResponseSpec(
            HttpStatusCode.Forbidden,
            "text/plain",
            "error code: 1010"));
        IProviderWebSearch provider = new OpenAIClientProvider(
            new FakeOpenAIAuthService("acct-token"),
            chatGptHttpMessageHandler: null);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchAsync(
            OAuthRuntime($"{server.Endpoint}/backend-api/agent", "acct-token"),
            new ProviderWebSearchRequest("thread_1", new JsonArray(), new JsonObject()),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Contains("403", error.Message, StringComparison.Ordinal);
        Assert.Contains("error code: 1010", error.Message, StringComparison.Ordinal);
    }
}
