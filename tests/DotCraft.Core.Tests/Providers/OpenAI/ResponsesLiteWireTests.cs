using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Sessions;
using DotCraft.Tests.Agents.TestSupport;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;
using ZstdSharp;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

/// <summary>
/// Wire-level contract tests for the ChatGPT OAuth Responses Lite path. These assert what reached
/// a loopback socket after every pipeline policy has run.
/// </summary>
public sealed class ResponsesLiteWireTests : IDisposable
{
    private const string InstallationId = "11111111-2222-4333-8444-555555555555";
    private readonly List<string> _tempRoots = [];

    public void Dispose()
    {
        TracingChatClient.CurrentSessionKey = null;
        foreach (var root in _tempRoots)
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OAuthResponses_FinalSocketRequestContainsStickyHeadersAndCanonicalBody()
    {
        const string threadId = "thread-lite-wire";
        const string turnId = "turn_028";
        const string windowId = "window-lite-wire";
        await using var server = RawRecordingHttpServer.Start(SuccessfulSseResponse());
        var auth = new RecordingAuthService("acct-token");
        var provider = CreateProvider(auth);
        TracingChatClient.CurrentSessionKey = threadId;
        var context = CreateContext(threadId, turnId, windowId);
        context.TryCaptureTurnState("state-existing");
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(context);

        using var client = provider.GetOpenAIResponsesChatClient(
            OAuthRuntime(server.Endpoint + "/backend-api/codex"));
        await ConsumeAsync(client.GetStreamingResponseAsync([
            new ChatMessage(ChatRole.User, "final wire")
        ]));

        var request = Assert.Single(server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/backend-api/codex/responses", request.Path);
        Assert.Equal("Bearer access-token", request.Headers["Authorization"]);
        Assert.Equal("acct-token", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
        Assert.Equal(OpenAIAuthConstants.Originator, request.Headers[OpenAIAuthConstants.OriginatorHeader]);
        Assert.Equal(InstallationId, request.Headers[OpenAIAuthConstants.InstallationIdHeader]);
        Assert.Equal(threadId, request.Headers[OpenAIAuthConstants.SessionIdHeader]);
        Assert.Equal(threadId, request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
        Assert.Equal(threadId, request.Headers[OpenAIAuthConstants.ClientRequestIdHeader]);
        Assert.Equal(windowId, request.Headers[OpenAIAuthConstants.WindowIdHeader]);
        Assert.Equal("state-existing", request.Headers[OpenAIAuthConstants.TurnStateHeader]);
        Assert.True(request.Headers.ContainsKey(OpenAIAuthConstants.TurnMetadataHeader));
        Assert.Contains("DotCraft/", request.Headers["User-Agent"], StringComparison.Ordinal);
        Assert.Equal("true", request.Headers[OpenAIResponsesLiteHeadersPipelinePolicy.ResponsesLiteHeader]);
        Assert.Equal(
            OpenAIOAuthPipelinePolicy.BetaFeaturesValue,
            request.Headers[OpenAIOAuthPipelinePolicy.BetaFeaturesHeader]);
        Assert.Equal("text/event-stream", request.Headers["Accept"]);
        Assert.Equal("zstd", request.Headers["Content-Encoding"]);
        Assert.Equal("28B52FFD", Convert.ToHexString(request.Body.AsSpan(0, 4)));

        using var body = JsonDocument.Parse(Decompress(request.Body));
        var root = body.RootElement;
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal(threadId, root.GetProperty("prompt_cache_key").GetString());
        var metadata = root.GetProperty("client_metadata");
        Assert.Equal(InstallationId, metadata.GetProperty(OpenAIAuthConstants.InstallationIdHeader).GetString());
        Assert.Equal(threadId, metadata.GetProperty("thread_id").GetString());
        Assert.Equal(turnId, metadata.GetProperty("turn_id").GetString());
        Assert.Equal(windowId, metadata.GetProperty(OpenAIAuthConstants.WindowIdHeader).GetString());
        var input = root.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal("additional_tools", input[0].GetProperty("type").GetString());
        var user = Assert.Single(input, item => item.GetProperty("role").GetString() == "user");
        Assert.Equal("message", user.GetProperty("type").GetString());
    }

    [Fact]
    public async Task StandardOAuthResponses_UsesTopLevelInstructionsToolsAndCallerParallelSetting()
    {
        const string threadId = "thread-standard-wire";
        await using var server = RawRecordingHttpServer.Start(SuccessfulSseResponse());
        var provider = CreateProvider(new RecordingAuthService("acct-token"));
        TracingChatClient.CurrentSessionKey = threadId;
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(
            CreateContext(threadId, "turn_standard", "window_standard"));
        using var client = provider.GetOpenAIResponsesChatClient(
            OAuthRuntime(server.Endpoint + "/backend-api/codex", useResponsesLite: false));
        var options = new ChatOptions
        {
            Instructions = "stable standard instructions",
            Tools = [AIFunctionFactory.Create(() => "ok", name: "lookup")],
            AllowMultipleToolCalls = true,
            MaxOutputTokens = 123
        };

        await ConsumeAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "standard wire")],
            options));

        var request = Assert.Single(server.Requests);
        Assert.False(request.Headers.ContainsKey(OpenAIResponsesLiteHeadersPipelinePolicy.ResponsesLiteHeader));
        Assert.Equal(
            OpenAIOAuthPipelinePolicy.BetaFeaturesValue,
            request.Headers[OpenAIOAuthPipelinePolicy.BetaFeaturesHeader]);
        Assert.Equal("zstd", request.Headers["Content-Encoding"]);
        using var body = JsonDocument.Parse(Decompress(request.Body));
        var root = body.RootElement;
        Assert.Equal("stable standard instructions", root.GetProperty("instructions").GetString());
        Assert.Equal("lookup", Assert.Single(root.GetProperty("tools").EnumerateArray()).GetProperty("name").GetString());
        Assert.True(root.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.False(root.TryGetProperty("max_output_tokens", out _));
        Assert.DoesNotContain(
            root.GetProperty("input").EnumerateArray(),
            item => item.TryGetProperty("type", out var type) && type.GetString() == "additional_tools");
    }

    [Fact]
    public async Task ApiKeyResponses_DoesNotUseOAuthCompressionOrHeaders()
    {
        await using var server = RawRecordingHttpServer.Start(SuccessfulSseResponse());
        var provider = CreateProvider(new RecordingAuthService("unused"));
        using var client = provider.GetOpenAIResponsesChatClient(
            ApiKeyRuntime(server.Endpoint + "/v1"));

        await ConsumeAsync(client.GetStreamingResponseAsync([
            new ChatMessage(ChatRole.User, "api key wire")
        ]));

        var request = Assert.Single(server.Requests);
        Assert.False(request.Headers.ContainsKey("Content-Encoding"));
        Assert.False(request.Headers.ContainsKey(OpenAIOAuthPipelinePolicy.BetaFeaturesHeader));
        Assert.False(request.Headers.ContainsKey(OpenAIResponsesLiteHeadersPipelinePolicy.ResponsesLiteHeader));
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("gpt-test", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task OAuthResponses_UnauthorizedRefreshReplaysIdenticalBodyAndStickyHeaders()
    {
        const string threadId = "thread-lite-401";
        await using var server = RawRecordingHttpServer.Start(
            RawHttpResponse.Json("{}", HttpStatusCode.Unauthorized),
            SuccessfulSseResponse());
        var auth = new RecordingAuthService("acct-token");
        var provider = CreateProvider(auth);
        TracingChatClient.CurrentSessionKey = threadId;
        var context = CreateContext(threadId, "turn_001", "window_401");
        context.TryCaptureTurnState("state-401");
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(context);

        using var client = provider.GetOpenAIResponsesChatClient(
            OAuthRuntime(server.Endpoint + "/backend-api/codex"));
        await ConsumeAsync(client.GetStreamingResponseAsync([
            new ChatMessage(ChatRole.User, "retry")
        ]));

        Assert.Equal([false, true], auth.ForceRefreshCalls);
        var requests = server.Requests;
        Assert.Collection(
            requests,
            first => Assert.Equal("Bearer access-token", first.Headers["Authorization"]),
            second => Assert.Equal("Bearer refreshed-token", second.Headers["Authorization"]));
        Assert.Equal(requests[0].Body, requests[1].Body);
        foreach (var request in requests)
        {
            Assert.Equal("acct-token", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
            Assert.Equal(threadId, request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
            Assert.Equal("state-401", request.Headers[OpenAIAuthConstants.TurnStateHeader]);
            Assert.Equal("zstd", request.Headers["Content-Encoding"]);
            Assert.Equal(
                OpenAIOAuthPipelinePolicy.BetaFeaturesValue,
                request.Headers[OpenAIOAuthPipelinePolicy.BetaFeaturesHeader]);
        }
    }

    [Fact]
    public async Task OAuthResponses_InternalServerErrorMakesOneTransportAttempt()
    {
        const string threadId = "thread-lite-500";
        await using var server = RawRecordingHttpServer.Start(RawHttpResponse.Json(
            """{"error":{"message":"provider failed","type":"server_error"}}""",
            HttpStatusCode.InternalServerError,
            new Dictionary<string, string> { ["x-request-id"] = "req-lite-500" }));
        var provider = CreateProvider(new RecordingAuthService("acct-token"));
        TracingChatClient.CurrentSessionKey = threadId;
        using var contextScope = OpenAIResponsesCodexRuntimeScope.Set(
            CreateContext(threadId, "turn_001", "window_500"));
        using var attemptScope = ModelStreamAttemptRuntimeScope.Begin(1);

        using var client = provider.GetOpenAIResponsesChatClient(
            OAuthRuntime(server.Endpoint + "/backend-api/codex"));
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await ConsumeAsync(client.GetStreamingResponseAsync([
                new ChatMessage(ChatRole.User, "fail once")
            ])));

        Assert.Single(server.Requests);
        var diagnostic = Assert.IsType<ModelStreamAttemptRuntimeContext>(ModelStreamAttemptRuntimeScope.Current);
        Assert.Equal(500, diagnostic.StatusCode);
        Assert.Equal("req-lite-500", diagnostic.RequestId);
    }

    [Fact]
    public async Task RawServerPreservesOpaqueZstdBytesAndContentEncoding()
    {
        // A valid zstd frame starts with 28 B5 2F FD. This fixture intentionally tests the test
        // seam's byte preservation; production compression tests can decode a complete frame.
        byte[] opaqueBody = [0x28, 0xB5, 0x2F, 0xFD, 0x01, 0x02, 0xFE, 0xFF];
        await using var server = RawRecordingHttpServer.Start(RawHttpResponse.Json("{}"));
        using var http = new HttpClient();
        using var content = new ByteArrayContent(opaqueBody);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentEncoding.Add("zstd");

        using var response = await http.PostAsync(server.Endpoint + "/responses", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(server.Requests);
        Assert.Equal("zstd", request.Headers["Content-Encoding"]);
        Assert.Equal(opaqueBody, request.Body);
        Assert.Equal("28B52FFD", Convert.ToHexString(request.Body.AsSpan(0, 4)));
    }

    [Fact]
    public void ResponsesClientSelection_UsesModelFlagOnlyForChatGptOAuth()
    {
        var provider = CreateProvider(new RecordingAuthService("acct-token"));

        using var oauth = provider.GetOpenAIResponsesChatClient(
            OAuthRuntime("https://example.test/backend-api/codex"));
        using var standardOAuth = provider.GetOpenAIResponsesChatClient(
            OAuthRuntime("https://example.test/backend-api/codex", useResponsesLite: false));
        using var apiKey = provider.GetOpenAIResponsesChatClient(ApiKeyRuntime());

        Assert.IsType<OpenAIResponsesLiteChatClient>(
            oauth.GetService(typeof(OpenAIResponsesLiteChatClient)));
        Assert.IsType<OpenAIResponsesToolSearchChatClient>(
            standardOAuth.GetService(typeof(OpenAIResponsesToolSearchChatClient)));
        Assert.Null(standardOAuth.GetService(typeof(OpenAIResponsesLiteChatClient)));
        Assert.IsType<OpenAIResponsesToolSearchChatClient>(
            apiKey.GetService(typeof(OpenAIResponsesToolSearchChatClient)));
        Assert.Null(apiKey.GetService(typeof(OpenAIResponsesLiteChatClient)));
    }

    [Fact]
    public void OAuthResponses_RequiresInstallationIdProvider()
    {
        var provider = new OpenAIClientProvider(
            new RecordingAuthService("acct-token"),
            chatGptHttpMessageHandler: null);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.GetOpenAIResponsesChatClient(
                OAuthRuntime("https://example.test/backend-api/codex")));

        Assert.Contains("OpenAIInstallationIdProvider", exception.Message, StringComparison.Ordinal);
    }

    private OpenAIClientProvider CreateProvider(IOpenAIAuthService auth)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-lite-wire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        File.WriteAllText(Path.Combine(root, OpenAIInstallationIdProvider.InstallationIdFileName), InstallationId);
        return new OpenAIClientProvider(
            auth,
            new OpenAIInstallationIdProvider(root),
            chatGptHttpMessageHandler: null);
    }

    private static EffectiveModelRuntime OAuthRuntime(
        string endpoint,
        bool useResponsesLite = true) => new(
        ProviderId: "openai",
        Model: "gpt-test",
        Protocol: ModelProviderProtocols.OpenAIResponses,
        DisplayName: "OpenAI (ChatGPT)",
        ApiKey: string.Empty,
        EndPoint: endpoint,
        NetworkTimeoutSeconds: 5,
        MaxOutputTokens: null,
        IsImplicit: false,
        ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses),
        AuthMethod: ModelProviderAuthMethods.ChatGptOAuth,
        ChatGptAccountId: "stale-config-account",
        UseResponsesLite: useResponsesLite);

    private static EffectiveModelRuntime ApiKeyRuntime(string endpoint = "https://example.test/v1") => new(
        ProviderId: "openai-api-key",
        Model: "gpt-test",
        Protocol: ModelProviderProtocols.OpenAIResponses,
        DisplayName: "OpenAI",
        ApiKey: "sk-test",
        EndPoint: endpoint,
        NetworkTimeoutSeconds: 5,
        MaxOutputTokens: null,
        IsImplicit: false,
        ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.OpenAIResponses));

    private static async Task ConsumeAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        await foreach (var _ in updates)
        {
        }
    }

    private static OpenAIResponsesCodexRuntimeContext CreateContext(
        string threadId,
        string turnId,
        string windowId) =>
        new(new ThreadConversationIdentity(
            CurrentThreadId: threadId,
            RootThreadId: threadId,
            ParentThreadId: null,
            ForkedFromThreadId: null,
            TurnId: turnId,
            ContextWindowId: windowId,
            RequestKind: ThreadConversationRequestKind.Turn,
            TurnStartedAtUnixMs: 1778544000000,
            ThreadSource: "appserver",
            SubagentKind: null));

    private static byte[] Decompress(byte[] compressed)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(compressed).ToArray();
    }

    private static RawHttpResponse SuccessfulSseResponse()
    {
        using var document = JsonDocument.Parse(SuccessfulResponseJson);
        var compactResponse = JsonSerializer.Serialize(document.RootElement);
        return RawHttpResponse.Sse(
            $"data: {{\"type\":\"response.completed\",\"sequence_number\":1,\"response\":{compactResponse}}}\n\n",
            chunkSizes: [1, 2, 3, 5, 8, 13]);
    }

    private sealed class RecordingAuthService(string accountId) : IOpenAIAuthService
    {
        public List<bool> ForceRefreshCalls { get; } = [];
        public bool IsAuthenticated => true;
        public event Action<OpenAIAuthStatus>? LoggedIn { add { } remove { } }
        public event Action? LoggedOut { add { } remove { } }
        public OpenAIAuthStatus GetStatus() => new(
            true, accountId, "pro", "test@example.com", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        public Task<OpenAIAuthStatus> LoginAsync(
            bool openBrowser,
            Action<string>? onAuthorizationUrl,
            CancellationToken cancellationToken) => Task.FromResult(GetStatus());
        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            ForceRefreshCalls.Add(forceRefresh);
            return Task.FromResult(forceRefresh ? "refreshed-token" : "access-token");
        }
        public string? GetAccountId() => accountId;
    }

    private const string SuccessfulResponseJson = """
        {
          "id":"resp_test",
          "object":"response",
          "created_at":1778544000,
          "status":"completed",
          "model":"gpt-test",
          "output":[{
            "id":"msg_test",
            "type":"message",
            "status":"completed",
            "role":"assistant",
            "content":[{"type":"output_text","text":"ok","annotations":[]}]
          }],
          "usage":{"input_tokens":1,"output_tokens":1,"total_tokens":2}
        }
        """;
}
