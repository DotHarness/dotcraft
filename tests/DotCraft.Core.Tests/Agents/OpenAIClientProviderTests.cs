using System.ClientModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed class OpenAIClientProviderTests : IDisposable
{
    private readonly List<string> _tempRoots = [];

    public void Dispose()
    {
        foreach (var tempRoot in _tempRoots)
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void CreateClientOptions_UsesConfiguredNetworkTimeout()
    {
        var endpoint = new Uri("https://example.test/v1");

        var options = OpenAIClientProvider.CreateClientOptions(endpoint, 240);

        Assert.Equal(endpoint, options.Endpoint);
        Assert.Equal(TimeSpan.FromSeconds(240), options.NetworkTimeout);
    }

    [Fact]
    public async Task GetOpenAIClient_ReplacesSdkUserAgent()
    {
        var (endpoint, requestTask) = await StartSingleJsonResponseServerAsync(
            """
            {
              "object": "list",
              "data": [
                {
                  "id": "gpt-test",
                  "object": "model",
                  "created": 1778544000,
                  "owned_by": "dotcraft-test"
                }
              ]
            }
            """);
        var provider = new OpenAIClientProvider();
        var runtime = Runtime(
            ModelProviderProtocols.OpenAI,
            networkTimeoutSeconds: 5,
            endpoint: $"{endpoint}/v1");

        var models = await provider.GetOpenAIClient(runtime).GetOpenAIModelClient().GetModelsAsync();

        Assert.Single(models.Value);
        var request = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("GET /v1/models", request, StringComparison.Ordinal);
        Assert.Contains("User-Agent: DotCraft/", request, StringComparison.Ordinal);
        Assert.DoesNotContain("User-Agent: OpenAI/", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOpenAIClient_DisablesSdkRequestRetries()
    {
        await using var server = RetryProbeServer.Start();
        var provider = new OpenAIClientProvider();
        var runtime = Runtime(
            ModelProviderProtocols.OpenAI,
            networkTimeoutSeconds: 5,
            endpoint: $"{server.Endpoint}/v1");

        await Assert.ThrowsAsync<ClientResultException>(async () =>
        {
            await provider.GetOpenAIClient(runtime).GetOpenAIModelClient().GetModelsAsync();
        });

        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public void GetOpenAIClient_CacheKeyIncludesNetworkTimeout()
    {
        var provider = new OpenAIClientProvider();
        var baseRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        var sameRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        var differentTimeoutRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 900);

        var first = provider.GetOpenAIClient(baseRuntime);
        var same = provider.GetOpenAIClient(sameRuntime);
        var differentTimeout = provider.GetOpenAIClient(differentTimeoutRuntime);

        Assert.Same(first, same);
        Assert.NotSame(first, differentTimeout);
    }

    [Fact]
    public void GetOpenAIImageClient_CacheKeyIncludesImageModel()
    {
        var provider = new OpenAIClientProvider();
        var runtime = Runtime(ModelProviderProtocols.OpenAI);

        var first = provider.GetOpenAIImageClient(runtime, "gpt-image-2");
        var same = provider.GetOpenAIImageClient(runtime, "gpt-image-2");
        var differentModel = provider.GetOpenAIImageClient(runtime, "gpt-image-2-mini");

        Assert.Same(first, same);
        Assert.NotSame(first, differentModel);
    }

    [Fact]
    public async Task GenerateOpenAIImageEditAsync_SendsMultipartWithProviderHeaders()
    {
        var expectedBytes = new byte[] { 4, 5, 6, 7 };
        await using var server = RecordingHttpServer.Start(JsonResponse(
            $$"""
            {
              "created": 1778544000,
              "data": [
                { "b64_json": "{{Convert.ToBase64String(expectedBytes)}}" }
              ]
            }
            """));
        var provider = new OpenAIClientProvider();
        var runtime = Runtime(
            ModelProviderProtocols.OpenAI,
            networkTimeoutSeconds: 5,
            endpoint: $"{server.Endpoint}/v1");

        var result = await provider.GenerateOpenAIImageEditAsync(
            runtime,
            "gpt-image-2",
            "edit both references",
            [
                new OpenAIImageEditInput([1, 2, 3], "first.png", "image/png"),
                new OpenAIImageEditInput([8, 9, 10], "second.webp", "image/webp")
            ],
            CancellationToken.None);

        Assert.Equal(expectedBytes, result);
        var request = Assert.Single(server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/v1/images/edits", request.Path);
        Assert.Equal("Bearer sk-test", request.Headers["Authorization"]);
        Assert.Contains("DotCraft/", request.Headers["User-Agent"], StringComparison.Ordinal);
        Assert.Contains("name=model", request.Body, StringComparison.Ordinal);
        Assert.Contains("gpt-image-2", request.Body, StringComparison.Ordinal);
        Assert.Contains("name=prompt", request.Body, StringComparison.Ordinal);
        Assert.Contains("edit both references", request.Body, StringComparison.Ordinal);
        Assert.Contains("filename=first.png", request.Body, StringComparison.Ordinal);
        Assert.Contains("filename=second.webp", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void GetOpenAIClient_RejectsNonOpenAIProtocol()
    {
        var provider = new OpenAIClientProvider();
        var runtime = Runtime(ModelProviderProtocols.Anthropic);

        Assert.Throws<ArgumentException>(() => provider.GetOpenAIClient(runtime));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    public void LlmHttpCapture_IsOptIn(string? value, bool expected)
    {
        var previous = Environment.GetEnvironmentVariable(LlmHttpCapturePipelinePolicy.EnabledEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(LlmHttpCapturePipelinePolicy.EnabledEnvironmentVariable, value);

            Assert.Equal(expected, LlmHttpCapturePipelinePolicy.IsEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(LlmHttpCapturePipelinePolicy.EnabledEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void LlmHttpCapture_SanitizesSessionKeyForFileName()
    {
        var sanitized = LlmHttpCapturePipelinePolicy.SanitizeFileSegment("""thread:a\b/c*?""");

        Assert.DoesNotContain(':', sanitized);
        Assert.DoesNotContain('\\', sanitized);
        Assert.DoesNotContain('/', sanitized);
        Assert.DoesNotContain('*', sanitized);
        Assert.DoesNotContain('?', sanitized);
    }

    [Fact]
    public async Task ChatGptOAuthResponsesWireRequestUsesDynamicAccountAndStickyHeaders()
    {
        var previousUaProfile = Environment.GetEnvironmentVariable(OpenAIOAuthPipelinePolicy.UserAgentProfileEnvironmentVariable);
        var previousBeta = Environment.GetEnvironmentVariable(OpenAIOAuthPipelinePolicy.OpenAIBetaEnvironmentVariable);
        const string installationId = "11111111-1111-4111-8111-111111111111";
        const string sessionKey = "thread-oauth-wire";
        const string turnId = "turn_001";
        const string windowId = "0192b455-3e7c-7000-8000-000000000001";

        try
        {
            Environment.SetEnvironmentVariable(OpenAIOAuthPipelinePolicy.UserAgentProfileEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(OpenAIOAuthPipelinePolicy.OpenAIBetaEnvironmentVariable, null);
            await using var server = RecordingHttpServer.Start(JsonResponse(SuccessfulResponseJson));
            var provider = CreateOAuthProvider(installationId, accountId: "acct_token");
            TracingChatClient.CurrentSessionKey = sessionKey;
            using var codexScope = OpenAIResponsesCodexRuntimeScope.Set(new OpenAIResponsesCodexRuntimeContext
            {
                ThreadId = sessionKey,
                TurnId = turnId,
                WindowId = windowId,
                TurnStartedAtUnixMs = 1778544000000,
                RequestKind = OpenAIResponsesCodexRequestKinds.Turn
            });

            await provider.GetOpenAIClient(OAuthRuntime(
                    $"{server.Endpoint}/backend-api/codex",
                    accountId: "acct_config_stale"))
                .GetResponsesClient()
                .CreateResponseAsync(CreateNonStreamingResponseOptions(
                    "gpt-test",
                    "hello",
                    maxOutputTokens: 12000));

            var request = Assert.Single(server.Requests);
            Assert.Equal("POST", request.Method);
            Assert.Equal("/backend-api/codex/responses", request.Path);
            Assert.Equal("Bearer access-token", request.Headers["Authorization"]);
            Assert.Equal("acct_token", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
            Assert.Equal(OpenAIAuthConstants.Originator, request.Headers[OpenAIAuthConstants.OriginatorHeader]);
            Assert.Equal(installationId, request.Headers[OpenAIAuthConstants.InstallationIdHeader]);
            Assert.Equal(sessionKey, request.Headers[OpenAIAuthConstants.SessionIdHeader]);
            Assert.Equal(sessionKey, request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
            Assert.False(request.Headers.ContainsKey(OpenAIAuthConstants.SessionIdCompatHeader));
            Assert.False(request.Headers.ContainsKey(OpenAIAuthConstants.ConversationIdHeader));
            Assert.Equal(windowId, request.Headers[OpenAIAuthConstants.WindowIdHeader]);
            Assert.True(request.Headers.ContainsKey(OpenAIAuthConstants.TurnMetadataHeader));
            Assert.False(request.Headers.ContainsKey(OpenAIAuthConstants.TurnStateHeader));
            Assert.StartsWith("DotCraft/", request.Headers["User-Agent"], StringComparison.Ordinal);
            Assert.False(request.Headers.ContainsKey("OpenAI-Beta"));

            using var document = JsonDocument.Parse(request.Body);
            Assert.Equal(sessionKey, document.RootElement.GetProperty("prompt_cache_key").GetString());
            Assert.False(document.RootElement.TryGetProperty("max_output_tokens", out _));
            var metadata = document.RootElement.GetProperty("client_metadata");
            Assert.Equal(installationId, metadata.GetProperty(OpenAIAuthConstants.InstallationIdHeader).GetString());
            Assert.Equal(sessionKey, metadata.GetProperty(OpenAIAuthConstants.SessionIdCompatHeader).GetString());
            Assert.Equal(sessionKey, metadata.GetProperty("thread_id").GetString());
            Assert.Equal(turnId, metadata.GetProperty("turn_id").GetString());
            Assert.Equal(windowId, metadata.GetProperty(OpenAIAuthConstants.WindowIdHeader).GetString());

            using var turnMetadata = JsonDocument.Parse(metadata.GetProperty(OpenAIAuthConstants.TurnMetadataHeader).GetString()!);
            var turnMetadataRoot = turnMetadata.RootElement;
            Assert.Equal(installationId, turnMetadataRoot.GetProperty("installation_id").GetString());
            Assert.Equal(sessionKey, turnMetadataRoot.GetProperty("session_id").GetString());
            Assert.Equal(sessionKey, turnMetadataRoot.GetProperty("thread_id").GetString());
            Assert.Equal(turnId, turnMetadataRoot.GetProperty("turn_id").GetString());
            Assert.Equal(windowId, turnMetadataRoot.GetProperty("window_id").GetString());
            Assert.Equal("turn", turnMetadataRoot.GetProperty("request_kind").GetString());
            Assert.Equal(1778544000000, turnMetadataRoot.GetProperty("turn_started_at_unix_ms").GetInt64());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession(sessionKey);
            Environment.SetEnvironmentVariable(OpenAIOAuthPipelinePolicy.UserAgentProfileEnvironmentVariable, previousUaProfile);
            Environment.SetEnvironmentVariable(OpenAIOAuthPipelinePolicy.OpenAIBetaEnvironmentVariable, previousBeta);
        }
    }

    [Fact]
    public async Task ChatGptOAuthResponsesRetryPreservesHeadersAfterUnauthorized()
    {
        const string installationId = "11111111-1111-4111-8111-111111111111";
        const string sessionKey = "thread-oauth-retry";
        const string windowId = "0192b455-3e7c-7000-8000-000000000002";
        var auth = new FakeOpenAIAuthService("acct_token");

        try
        {
            await using var server = RecordingHttpServer.Start(
                JsonResponse("{}", HttpStatusCode.Unauthorized),
                JsonResponse(SuccessfulResponseJson));
            var provider = CreateOAuthProvider(installationId, auth);
            TracingChatClient.CurrentSessionKey = sessionKey;
            var codexContext = new OpenAIResponsesCodexRuntimeContext
            {
                ThreadId = sessionKey,
                TurnId = "turn_001",
                WindowId = windowId,
                TurnStartedAtUnixMs = 1778544000000,
                RequestKind = OpenAIResponsesCodexRequestKinds.Turn
            };
            codexContext.TryCaptureTurnState("state-existing");
            using var codexScope = OpenAIResponsesCodexRuntimeScope.Set(codexContext);

            await provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
                .GetResponsesClient()
                .CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "retry"));

            Assert.Equal([false, true], auth.ForceRefreshCalls);
            Assert.Equal(2, server.Requests.Count);
            Assert.Equal("Bearer access-token", server.Requests[0].Headers["Authorization"]);
            Assert.Equal("Bearer refreshed-token", server.Requests[1].Headers["Authorization"]);
            foreach (var request in server.Requests)
            {
                Assert.Equal("acct_token", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
                Assert.Equal(OpenAIAuthConstants.Originator, request.Headers[OpenAIAuthConstants.OriginatorHeader]);
                Assert.Equal(installationId, request.Headers[OpenAIAuthConstants.InstallationIdHeader]);
                Assert.Equal(sessionKey, request.Headers[OpenAIAuthConstants.SessionIdHeader]);
                Assert.Equal(sessionKey, request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
                Assert.False(request.Headers.ContainsKey(OpenAIAuthConstants.SessionIdCompatHeader));
                Assert.False(request.Headers.ContainsKey(OpenAIAuthConstants.ConversationIdHeader));
                Assert.Equal(windowId, request.Headers[OpenAIAuthConstants.WindowIdHeader]);
                Assert.Equal("state-existing", request.Headers[OpenAIAuthConstants.TurnStateHeader]);
            }
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession(sessionKey);
        }
    }

    [Fact]
    public async Task ChatGptOAuthResponsesReloadsRotatedDiskTokenAfterUnauthorized()
    {
        const string installationId = "11111111-1111-4111-8111-111111111111";
        var authDir = Path.Combine(Path.GetTempPath(), "dotcraft-oauth-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(authDir);
        _tempRoots.Add(authDir);
        var store = new OpenAITokenStore(authDir);
        store.Save(CreateAuth("access-token", "refresh-1"));
        var auth = new OpenAIAuthManager(store, new HttpClient(new UnexpectedHttpHandler()));

        await using var server = RecordingHttpServer.Start(
            JsonResponse("{}", HttpStatusCode.Unauthorized),
            JsonResponse(SuccessfulResponseJson));
        var provider = CreateOAuthProvider(installationId, auth);
        store.Save(CreateAuth("rotated-token", "refresh-2"));

        await provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
            .GetResponsesClient()
            .CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "retry"));

        Assert.Equal(2, server.Requests.Count);
        Assert.Equal("Bearer access-token", server.Requests[0].Headers["Authorization"]);
        Assert.Equal("Bearer rotated-token", server.Requests[1].Headers["Authorization"]);

        static AuthDotJson CreateAuth(string accessToken, string refreshToken) => new()
        {
            Tokens = new OpenAITokenSet
            {
                IdToken = "id-token",
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccountId = "acct-token"
            },
            LastRefresh = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task ChatGptOAuthResponsesReplaysTurnStateOnlyWithinSameRuntimeScope()
    {
        const string installationId = "11111111-1111-4111-8111-111111111111";
        const string sessionKey = "thread-oauth-turn-state";
        const string accountId = "acct_test";
        const string firstWindowId = "0192b455-3e7c-7000-8000-000000000003";
        const string secondWindowId = "0192b455-3e7c-7000-8000-000000000004";

        try
        {
            await using var server = RecordingHttpServer.Start(
                JsonResponse(
                    SuccessfulResponseJson,
                    headers: new Dictionary<string, string>
                    {
                        [OpenAIAuthConstants.TurnStateHeader] = "state-one"
                    }),
                JsonResponse(SuccessfulResponseJson),
                JsonResponse(SuccessfulResponseJson));
            var provider = CreateOAuthProvider(installationId, accountId: accountId);
            var client = provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
                .GetResponsesClient();
            TracingChatClient.CurrentSessionKey = sessionKey;

            var firstContext = new OpenAIResponsesCodexRuntimeContext
            {
                ThreadId = sessionKey,
                TurnId = "turn_001",
                WindowId = firstWindowId,
                TurnStartedAtUnixMs = 1778544000000,
                RequestKind = OpenAIResponsesCodexRequestKinds.Turn
            };
            using (OpenAIResponsesCodexRuntimeScope.Set(firstContext))
            {
                await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "first"));
                await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "second"));
            }

            using (OpenAIResponsesCodexRuntimeScope.Set(new OpenAIResponsesCodexRuntimeContext
            {
                ThreadId = sessionKey,
                TurnId = "turn_002",
                WindowId = secondWindowId,
                TurnStartedAtUnixMs = 1778544100000,
                RequestKind = OpenAIResponsesCodexRequestKinds.Turn
            }))
            {
                await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "third"));
            }

            Assert.Equal(3, server.Requests.Count);
            Assert.False(server.Requests[0].Headers.ContainsKey(OpenAIAuthConstants.TurnStateHeader));
            Assert.Equal("state-one", server.Requests[1].Headers[OpenAIAuthConstants.TurnStateHeader]);
            Assert.False(server.Requests[2].Headers.ContainsKey(OpenAIAuthConstants.TurnStateHeader));
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession(sessionKey);
        }
    }

    [Fact]
    public async Task ChatGptOAuthExperimentalHeadersAreOptIn()
    {
        var previousUaProfile = Environment.GetEnvironmentVariable(OpenAIOAuthPipelinePolicy.UserAgentProfileEnvironmentVariable);
        var previousBeta = Environment.GetEnvironmentVariable(OpenAIOAuthPipelinePolicy.OpenAIBetaEnvironmentVariable);
        const string installationId = "11111111-1111-4111-8111-111111111111";
        const string sessionKey = "thread-oauth-experimental";

        try
        {
            Environment.SetEnvironmentVariable(OpenAIOAuthPipelinePolicy.UserAgentProfileEnvironmentVariable, "codex");
            Environment.SetEnvironmentVariable(OpenAIOAuthPipelinePolicy.OpenAIBetaEnvironmentVariable, "responses=experimental");
            await using var server = RecordingHttpServer.Start(JsonResponse(SuccessfulResponseJson));
            var provider = CreateOAuthProvider(installationId, accountId: "acct_token");
            TracingChatClient.CurrentSessionKey = sessionKey;

            await provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
                .GetResponsesClient()
                .CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "hello"));

            var request = Assert.Single(server.Requests);
            Assert.StartsWith(
                $"{OpenAIAuthConstants.Originator}/",
                request.Headers["User-Agent"],
                StringComparison.Ordinal);
            Assert.Contains(" dotcraft", request.Headers["User-Agent"], StringComparison.Ordinal);
            Assert.Equal("responses=experimental", request.Headers["OpenAI-Beta"]);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession(sessionKey);
            Environment.SetEnvironmentVariable(OpenAIOAuthPipelinePolicy.UserAgentProfileEnvironmentVariable, previousUaProfile);
            Environment.SetEnvironmentVariable(OpenAIOAuthPipelinePolicy.OpenAIBetaEnvironmentVariable, previousBeta);
        }
    }

    private static EffectiveModelRuntime Runtime(string protocol, int networkTimeoutSeconds = 600, string? endpoint = null) => new(
        ProviderId: protocol,
        Model: "model-a",
        Protocol: protocol,
        DisplayName: protocol,
        ApiKey: "sk-test",
        EndPoint: endpoint ?? (protocol == ModelProviderProtocols.Anthropic
            ? "https://api.anthropic.com"
            : "https://example.test/v1"),
        NetworkTimeoutSeconds: networkTimeoutSeconds,
        MaxOutputTokens: null,
        IsImplicit: false,
        ModelProviderCapabilities.ForProtocol(protocol));

    private static EffectiveModelRuntime OAuthRuntime(string endpoint, string? accountId = null) => new(
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
        ChatGptAccountId: accountId);

    private OpenAIClientProvider CreateOAuthProvider(
        string installationId,
        string accountId)
        => CreateOAuthProvider(installationId, new FakeOpenAIAuthService(accountId));

    private OpenAIClientProvider CreateOAuthProvider(
        string installationId,
        IOpenAIAuthService auth)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dotcraft-oauth-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _tempRoots.Add(tempDir);
        File.WriteAllText(Path.Combine(tempDir, OpenAIInstallationIdProvider.InstallationIdFileName), installationId);
        return new OpenAIClientProvider(
            auth,
            new OpenAIInstallationIdProvider(tempDir),
            chatGptHttpMessageHandler: null);
    }

    private static OpenAI.Responses.CreateResponseOptions CreateNonStreamingResponseOptions(
        string model,
        string userMessage,
        int? maxOutputTokens = null)
    {
        var options = ResponsesToolSearchMapper.CreateResponseOptions(
            model,
            [new ChatMessage(ChatRole.User, userMessage)],
            new ChatOptions
            {
                MaxOutputTokens = maxOutputTokens
            });
        options.StreamingEnabled = false;
        return options;
    }

    private static RecordingHttpServer.ResponseSpec JsonResponse(
        string json,
        HttpStatusCode status = HttpStatusCode.OK,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(status, "application/json", json, headers);

    private const string SuccessfulResponseJson = """
        {
          "id": "resp_test",
          "object": "response",
          "created_at": 1778544000,
          "status": "completed",
          "model": "gpt-test",
          "output": [
            {
              "id": "msg_test",
              "type": "message",
              "status": "completed",
              "role": "assistant",
              "content": [
                {
                  "type": "output_text",
                  "text": "ok",
                  "annotations": []
                }
              ]
            }
          ],
          "usage": {
            "input_tokens": 1,
            "output_tokens": 1,
            "total_tokens": 2
          }
        }
        """;

    private static async Task<(string Endpoint, Task<string> RequestTask)> StartSingleJsonResponseServerAsync(string json)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var requestTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                var request = new StringBuilder();
                while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read <= 0)
                        break;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var body = Encoding.UTF8.GetBytes(json);
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: application/json\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(body);
                return request.ToString();
            }
            finally
            {
                listener.Stop();
            }
        });

        return ($"http://{IPAddress.Loopback}:{endpoint.Port}", requestTask);
    }

    private sealed class RetryProbeServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _acceptLoop;
        private int _requestCount;

        private RetryProbeServer(TcpListener listener, string endpoint)
        {
            _listener = listener;
            Endpoint = endpoint;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string Endpoint { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public static RetryProbeServer Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            return new RetryProbeServer(listener, $"http://{IPAddress.Loopback}:{endpoint.Port}");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _stop.CancelAsync();
                _listener.Stop();
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var requestIndex = Interlocked.Increment(ref _requestCount);
                    await WriteResponseAsync(client, requestIndex);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task WriteResponseAsync(TcpClient client, int requestIndex)
        {
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var request = new StringBuilder();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer);
                if (read <= 0)
                    break;
                request.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var bodyText = requestIndex == 1
                ? """{"error":{"message":"retry probe","type":"server_error"}}"""
                : """
                  {
                    "object": "list",
                    "data": [
                      {
                        "id": "gpt-test",
                        "object": "model",
                        "created": 1778544000,
                        "owned_by": "dotcraft-test"
                      }
                    ]
                  }
                  """;
            var status = requestIndex == 1
                ? "500 Internal Server Error"
                : "200 OK";
            var body = Encoding.UTF8.GetBytes(bodyText);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
        }
    }

    private sealed class FakeOpenAIAuthService(string accountId) : IOpenAIAuthService
    {
        public List<bool> ForceRefreshCalls { get; } = [];

        public bool IsAuthenticated => true;

        public event Action<OpenAIAuthStatus>? LoggedIn
        {
            add { }
            remove { }
        }

        public event Action? LoggedOut
        {
            add { }
            remove { }
        }

        public OpenAIAuthStatus GetStatus() => new(
            true,
            accountId,
            "pro",
            "test@example.com",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

        public Task<OpenAIAuthStatus> LoginAsync(
            bool openBrowser,
            Action<string>? onAuthorizationUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(GetStatus());

        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            ForceRefreshCalls.Add(forceRefresh);
            return Task.FromResult(forceRefresh ? "refreshed-token" : "access-token");
        }

        public string? GetAccountId() => accountId;
    }

    private sealed class UnexpectedHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The OAuth authority should not be contacted.");
    }

    private sealed class RecordingHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Queue<ResponseSpec> _responses;
        private readonly Task _acceptLoop;

        private RecordingHttpServer(TcpListener listener, string endpoint, IEnumerable<ResponseSpec> responses)
        {
            _listener = listener;
            Endpoint = endpoint;
            _responses = new Queue<ResponseSpec>(responses);
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string Endpoint { get; }

        public List<RecordedHttpRequest> Requests { get; } = [];

        public static RecordingHttpServer Start(params ResponseSpec[] responses)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            return new RecordingHttpServer(listener, $"http://{IPAddress.Loopback}:{endpoint.Port}", responses);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _stop.CancelAsync();
                _listener.Stop();
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var request = await ReadRequestAsync(client.GetStream(), _stop.Token);
                    Requests.Add(request);
                    var response = _responses.Count > 0
                        ? _responses.Dequeue()
                        : new ResponseSpec(HttpStatusCode.InternalServerError, "application/json", "{}");
                    await WriteResponseAsync(client.GetStream(), response, _stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task<RecordedHttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var buffer = new byte[1024];
            var headerEnd = -1;
            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;

                for (var i = 0; i < read; i++)
                    bytes.Add(buffer[i]);
                headerEnd = FindHeaderEnd(bytes);
            }

            if (headerEnd < 0)
                throw new IOException("HTTP request headers were incomplete.");

            var headerText = Encoding.ASCII.GetString(bytes.Take(headerEnd).ToArray());
            var lines = headerText.Split("\r\n", StringSplitOptions.None);
            var requestLine = lines[0].Split(' ', 3);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;

                headers[line[..separator]] = line[(separator + 1)..].Trim();
            }

            var contentLength = headers.TryGetValue("Content-Length", out var rawLength) &&
                int.TryParse(rawLength, out var parsedLength)
                    ? parsedLength
                    : 0;
            var bodyStart = headerEnd + 4;
            while (bytes.Count - bodyStart < contentLength)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;

                for (var i = 0; i < read; i++)
                    bytes.Add(buffer[i]);
            }

            var body = contentLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(bytes.Skip(bodyStart).Take(contentLength).ToArray());
            return new RecordedHttpRequest(
                requestLine.ElementAtOrDefault(0) ?? string.Empty,
                requestLine.ElementAtOrDefault(1) ?? string.Empty,
                headers,
                body);
        }

        private static int FindHeaderEnd(IReadOnlyList<byte> bytes)
        {
            for (var i = 3; i < bytes.Count; i++)
            {
                if (bytes[i - 3] == '\r' &&
                    bytes[i - 2] == '\n' &&
                    bytes[i - 1] == '\r' &&
                    bytes[i] == '\n')
                {
                    return i - 3;
                }
            }

            return -1;
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            ResponseSpec response,
            CancellationToken cancellationToken)
        {
            var body = Encoding.UTF8.GetBytes(response.Body);
            var extraHeaders = response.Headers == null
                ? string.Empty
                : string.Concat(response.Headers.Select(header => $"{header.Key}: {header.Value}\r\n"));
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)response.StatusCode} {ReasonPhrase(response.StatusCode)}\r\n" +
                $"Content-Type: {response.ContentType}\r\n" +
                extraHeaders +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
        }

        private static string ReasonPhrase(HttpStatusCode statusCode) => statusCode switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.Unauthorized => "Unauthorized",
            _ => statusCode.ToString()
        };

        public sealed record ResponseSpec(
            HttpStatusCode StatusCode,
            string ContentType,
            string Body,
            IReadOnlyDictionary<string, string>? Headers = null);
    }

    private sealed record RecordedHttpRequest(
        string Method,
        string Path,
        Dictionary<string, string> Headers,
        string Body);
}
