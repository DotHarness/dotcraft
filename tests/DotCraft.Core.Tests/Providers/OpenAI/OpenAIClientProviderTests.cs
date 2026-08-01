using System.ClientModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Protocol;
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
    public async Task CompactTransport_UsesResponsesFamilyOAuthWithoutCreateBodyMutation()
    {
        const string installationId = "11111111-2222-4333-8444-555555555555";
        await using var server = RecordingHttpServer.Start(
            JsonResponse(
                """{"output":[{"type":"compaction","encrypted_content":"YWJj"}]}""",
                headers: new Dictionary<string, string>
                {
                    [OpenAIAuthConstants.TurnStateHeader] = "next-turn-state"
                }));
        var provider = CreateOAuthProvider(installationId, "account-test");
        var runtime = OAuthRuntime($"{server.Endpoint}/backend-api/codex", "account-test");
        var context = CreateCodexRuntimeContext(
            "thread-test",
            "turn-test",
            "window-test",
            requestKind: ThreadConversationRequestKind.Compaction);
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(context);
        var compactRequest = new ChatGptResponsesCompactRequest
        {
            Model = "gpt-test",
            Input = [ReadObject("""{"type":"message","role":"user","content":[]}""")],
            Reasoning = ReadObject("{}")
        };

        var response = await provider
            .GetChatGptResponsesCompactTransport(runtime)
            .CompactAsync(compactRequest, CancellationToken.None);

        Assert.Equal("compaction", Assert.Single(response.Output!).GetProperty("type").GetString());
        var request = Assert.Single(server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/backend-api/codex/responses/compact", request.Path);
        Assert.Equal("application/json", request.Headers["Content-Type"]);
        Assert.Equal("Bearer access-token", request.Headers["Authorization"]);
        Assert.Equal("account-test", request.Headers[OpenAIAuthConstants.AccountIdHeader]);
        Assert.Equal(installationId, request.Headers[OpenAIAuthConstants.InstallationIdHeader]);
        Assert.Equal("thread-test", request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
        Assert.Equal("window-test", request.Headers[OpenAIAuthConstants.WindowIdHeader]);
        using var sent = JsonDocument.Parse(request.Body);
        Assert.False(sent.RootElement.TryGetProperty("client_metadata", out _));
        Assert.False(sent.RootElement.TryGetProperty("stream", out _));
        Assert.Equal("next-turn-state", context.TurnState);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>gateway failure</html>")]
    [InlineData("{\"output\":[")]
    [InlineData("{}")]
    [InlineData("{\"output\":{}}")]
    public async Task CompactTransport_InvalidResponseUsesStableErrorCode(string responseBody)
    {
        await using var server = RecordingHttpServer.Start(JsonResponse(responseBody));
        var provider = CreateOAuthProvider(
            "11111111-2222-4333-8444-555555555555",
            "account-test");
        var runtime = OAuthRuntime($"{server.Endpoint}/backend-api/codex", "account-test");
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
            "thread-test",
            "turn-test",
            "window-test",
            requestKind: ThreadConversationRequestKind.Compaction));
        var compactRequest = new ChatGptResponsesCompactRequest
        {
            Model = "gpt-test",
            Input = []
        };

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await provider
                .GetChatGptResponsesCompactTransport(runtime)
                .CompactAsync(compactRequest, CancellationToken.None));

        Assert.Equal(
            "provider_compaction_invalid_response: Compact response body must match the expected JSON envelope.",
            error.Message);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
    }

    [Fact]
    public async Task CompactTransport_HonorsCancellation()
    {
        await using var server = RecordingHttpServer.Start(
            JsonResponse("""{"output":[{"type":"compaction","encrypted_content":"YWJj"}]}"""));
        var provider = CreateOAuthProvider(
            "11111111-2222-4333-8444-555555555555",
            "account-test");
        var runtime = OAuthRuntime($"{server.Endpoint}/backend-api/codex", "account-test");
        using var scope = OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
            "thread-test",
            "turn-test",
            "window-test",
            requestKind: ThreadConversationRequestKind.Compaction));
        var compactRequest = new ChatGptResponsesCompactRequest
        {
            Model = "gpt-test",
            Input = []
        };
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await provider
                .GetChatGptResponsesCompactTransport(runtime)
                .CompactAsync(compactRequest, cancellation.Token));
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
            using var codexScope = OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
                sessionKey,
                turnId,
                windowId,
                turnStartedAtUnixMs: 1778544000000));

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
    public async Task ChatGptOAuthResponsesWireRequestPreservesCacheableInputShape()
    {
        const string installationId = "11111111-1111-4111-8111-111111111111";
        const string threadId = "thread-oauth-cache-shape";
        var userMessage = new ChatMessage(ChatRole.User, "inspect the repository")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [OpenAIResponsesItemIdentity.MetadataKey] = "msg_user_input"
            }
        };
        var reasoning = new TextReasoningContent("checking")
        {
            ProtectedData = "encrypted-reasoning-payload",
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [OpenAIResponsesItemIdentity.MetadataKey] = "rs_provider_reasoning"
            }
        };
        var functionCall = new FunctionCallContent(
            "call_read",
            "ReadRepository",
            new Dictionary<string, object?> { ["path"] = "README.md" })
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [OpenAIResponsesItemIdentity.MetadataKey] = "fc_provider_call"
            }
        };
        var functionResult = new FunctionResultContent("call_read", "repository contents")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [OpenAIResponsesItemIdentity.MetadataKey] = "fco_provider_output"
            }
        };

        try
        {
            await using var server = RecordingHttpServer.Start(JsonResponse(SuccessfulResponseJson));
            var provider = CreateOAuthProvider(installationId, accountId: "acct_token");
            using var codexScope = OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
                threadId,
                "turn_cache_shape",
                "window_cache_shape",
                turnStartedAtUnixMs: 1778544000000));
            var options = ResponsesToolSearchMapper.CreateResponseOptions(
                "gpt-test",
                [
                    userMessage,
                    new ChatMessage(ChatRole.Assistant, [reasoning, functionCall]),
                    new ChatMessage(ChatRole.Tool, [functionResult])
                ],
                new ChatOptions
                {
                    Instructions = "Follow repository guidance.",
                    MaxOutputTokens = 12000,
                    Reasoning = new ReasoningOptions
                    {
                        Effort = ReasoningEffort.High,
                        Output = ReasoningOutput.Summary
                    },
                    Tools =
                    [
                        AIFunctionFactory.Create(
                            (string path) => path,
                            name: "ReadRepository",
                            description: "Read one repository file.")
                    ]
                });
            options.StreamingEnabled = false;

            await provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
                .GetResponsesClient()
                .CreateResponseAsync(options);

            var request = Assert.Single(server.Requests);
            using var document = JsonDocument.Parse(request.Body);
            var root = document.RootElement;
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.False(root.TryGetProperty("max_output_tokens", out _));
            Assert.Equal(threadId, root.GetProperty("prompt_cache_key").GetString());
            Assert.Equal("high", root.GetProperty("reasoning").GetProperty("effort").GetString());
            Assert.Equal("auto", root.GetProperty("reasoning").GetProperty("summary").GetString());
            Assert.Contains(
                root.GetProperty("include").EnumerateArray(),
                item => item.GetString() == "reasoning.encrypted_content");
            Assert.Contains(
                root.GetProperty("tools").EnumerateArray(),
                tool => tool.GetProperty("name").GetString() == "ReadRepository");

            var input = root.GetProperty("input").EnumerateArray().ToArray();
            Assert.Equal(
                ["message", "reasoning", "function_call", "function_call_output"],
                input.Select(item => item.GetProperty("type").GetString()!).ToArray());
            Assert.Equal(
                ["msg_user_input", "rs_provider_reasoning", "fc_provider_call", "fco_provider_output"],
                input.Select(item => item.GetProperty("id").GetString()!).ToArray());
            Assert.Equal(
                "encrypted-reasoning-payload",
                input[1].GetProperty("encrypted_content").GetString());
            Assert.Equal("call_read", input[2].GetProperty("call_id").GetString());
            Assert.Equal("call_read", input[3].GetProperty("call_id").GetString());
            Assert.Equal("repository contents", input[3].GetProperty("output").GetString());

            Assert.Equal(
                "58cb79548e1ce09cdf80f62a2be4d7e7d80365401dd13a1b6d5e2187e52361bf",
                ComputeSha256(request.Body));
            Assert.Equal(
                "08572d865e83c3a9294c8fae459645435976038e42ea8799c8c4e3e7b6f90c8f",
                ComputeSha256(BuildSanitizedCacheWireSnapshot(request)));
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession(threadId);
        }
    }

    [Theory]
    [InlineData("root", null, null, null, "user")]
    [InlineData("subagent", "thread-root", null, "native", "subagent")]
    [InlineData("fork", null, "thread-root", null, "user")]
    public async Task ChatGptOAuthResponsesWireIdentityUsesCurrentThreadAndPreservesLineageMetadata(
        string scenario,
        string? parentThreadId,
        string? forkedFromThreadId,
        string? runtimeType,
        string threadSource)
    {
        const string installationId = "11111111-1111-4111-8111-111111111111";
        var currentThreadId = $"thread-{scenario}";
        var turnId = $"turn-{scenario}";
        var windowId = $"window-{scenario}";

        try
        {
            await using var server = RecordingHttpServer.Start(JsonResponse(SuccessfulResponseJson));
            var provider = CreateOAuthProvider(installationId, accountId: "acct_token");
            TracingChatClient.CurrentSessionKey = "stale-tracing-thread";
            var thread = new SessionThread
            {
                Id = currentThreadId,
                Source = scenario switch
                {
                    "subagent" => ThreadSource.ForSubAgent(new SubAgentThreadSource
                    {
                        ParentThreadId = parentThreadId!,
                        RootThreadId = "thread-root",
                        RuntimeType = runtimeType,
                        AgentRole = "worker",
                        ProfileName = "default"
                    }),
                    _ => ThreadSource.User()
                },
                ForkedFromId = forkedFromThreadId
            };
            var turn = new SessionTurn
            {
                Id = turnId,
                ThreadId = currentThreadId,
                StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(1778544000000)
            };
            var context = new OpenAIResponsesCodexRuntimeContext(ThreadConversationIdentity.Create(
                thread,
                turn,
                windowId,
                ThreadConversationRequestKind.Turn));
            Assert.Equal(
                scenario == "subagent" ? "thread-root" : currentThreadId,
                context.ConversationIdentity.RootThreadId);
            Assert.Equal(
                scenario == "subagent" ? "thread_spawn" : null,
                context.ConversationIdentity.SubagentKind);
            if (scenario == "subagent")
            {
                Assert.Equal(runtimeType, thread.Source.SubAgent?.RuntimeType);
                Assert.Equal("worker", thread.Source.SubAgent?.AgentRole);
                Assert.Equal("default", thread.Source.SubAgent?.ProfileName);
            }
            using var codexScope = OpenAIResponsesCodexRuntimeScope.Set(context);

            await provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
                .GetResponsesClient()
                .CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "hello"));

            var request = Assert.Single(server.Requests);
            var expectedSessionId = scenario == "subagent" ? "thread-root" : currentThreadId;
            Assert.Equal(expectedSessionId, request.Headers[OpenAIAuthConstants.SessionIdHeader]);
            Assert.Equal(currentThreadId, request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
            Assert.Equal(currentThreadId, request.Headers[OpenAIAuthConstants.ClientRequestIdHeader]);
            Assert.Equal(parentThreadId != null, request.Headers.ContainsKey(OpenAIAuthConstants.ParentThreadIdHeader));
            Assert.Equal(runtimeType != null, request.Headers.ContainsKey(OpenAIAuthConstants.SubAgentHeader));
            if (parentThreadId != null)
                Assert.Equal(parentThreadId, request.Headers[OpenAIAuthConstants.ParentThreadIdHeader]);
            if (runtimeType != null)
                Assert.Equal("collab_spawn", request.Headers[OpenAIAuthConstants.SubAgentHeader]);

            using var document = JsonDocument.Parse(request.Body);
            var root = document.RootElement;
            Assert.Equal(expectedSessionId, root.GetProperty("prompt_cache_key").GetString());

            var metadata = root.GetProperty("client_metadata");
            Assert.Equal(expectedSessionId, metadata.GetProperty(OpenAIAuthConstants.SessionIdCompatHeader).GetString());
            Assert.Equal(currentThreadId, metadata.GetProperty("thread_id").GetString());
            Assert.Equal(turnId, metadata.GetProperty("turn_id").GetString());
            Assert.Equal(windowId, metadata.GetProperty(OpenAIAuthConstants.WindowIdHeader).GetString());
            Assert.Equal(parentThreadId != null, metadata.TryGetProperty(OpenAIAuthConstants.ParentThreadIdHeader, out _));
            Assert.Equal(runtimeType != null, metadata.TryGetProperty(OpenAIAuthConstants.SubAgentHeader, out _));
            if (runtimeType != null)
                Assert.Equal("collab_spawn", metadata.GetProperty(OpenAIAuthConstants.SubAgentHeader).GetString());

            using var turnMetadata = JsonDocument.Parse(
                metadata.GetProperty(OpenAIAuthConstants.TurnMetadataHeader).GetString()!);
            var turnMetadataRoot = turnMetadata.RootElement;
            Assert.Equal(expectedSessionId, turnMetadataRoot.GetProperty("session_id").GetString());
            Assert.Equal(currentThreadId, turnMetadataRoot.GetProperty("thread_id").GetString());
            Assert.Equal(threadSource, turnMetadataRoot.GetProperty("thread_source").GetString());
            Assert.Equal(parentThreadId != null, turnMetadataRoot.TryGetProperty("parent_thread_id", out _));
            Assert.Equal(forkedFromThreadId != null, turnMetadataRoot.TryGetProperty("forked_from_thread_id", out _));
            Assert.Equal(runtimeType != null, turnMetadataRoot.TryGetProperty("subagent_kind", out _));
            if (parentThreadId != null)
                Assert.Equal(parentThreadId, turnMetadataRoot.GetProperty("parent_thread_id").GetString());
            if (forkedFromThreadId != null)
                Assert.Equal(forkedFromThreadId, turnMetadataRoot.GetProperty("forked_from_thread_id").GetString());
            if (runtimeType != null)
                Assert.Equal("thread_spawn", turnMetadataRoot.GetProperty("subagent_kind").GetString());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession("stale-tracing-thread");
            TracingChatClient.ClearActiveSession(currentThreadId);
        }
    }

    [Fact]
    public async Task ChatGptOAuthResponsesExplicitPromptCacheKeyDoesNotChangeWireIdentity()
    {
        const string installationId = "11111111-1111-4111-8111-111111111111";
        const string currentThreadId = "thread-explicit-cache";
        const string explicitPromptCacheKey = "caller-cache-key";

        try
        {
            await using var server = RecordingHttpServer.Start(JsonResponse(SuccessfulResponseJson));
            var provider = CreateOAuthProvider(installationId, accountId: "acct_token");
            TracingChatClient.CurrentSessionKey = "stale-tracing-thread";
            using var codexScope = OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
                currentThreadId,
                "turn-explicit-cache",
                "window-explicit-cache",
                turnStartedAtUnixMs: 1778544000000));
            var options = ResponsesToolSearchMapper.CreateResponseOptions(
                "gpt-test",
                [new ChatMessage(ChatRole.User, "hello")],
                new ChatOptions
                {
                    AdditionalProperties = new AdditionalPropertiesDictionary
                    {
                        [ResponsesToolSearchMapper.PromptCacheKeyAdditionalProperty] = explicitPromptCacheKey
                    }
                });
            options.StreamingEnabled = false;

            await provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
                .GetResponsesClient()
                .CreateResponseAsync(options);

            var request = Assert.Single(server.Requests);
            Assert.Equal(currentThreadId, request.Headers[OpenAIAuthConstants.SessionIdHeader]);
            Assert.Equal(currentThreadId, request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
            Assert.Equal(currentThreadId, request.Headers[OpenAIAuthConstants.ClientRequestIdHeader]);

            using var document = JsonDocument.Parse(request.Body);
            Assert.Equal(explicitPromptCacheKey, document.RootElement.GetProperty("prompt_cache_key").GetString());
            var metadata = document.RootElement.GetProperty("client_metadata");
            Assert.Equal(currentThreadId, metadata.GetProperty(OpenAIAuthConstants.SessionIdCompatHeader).GetString());
            Assert.Equal(currentThreadId, metadata.GetProperty("thread_id").GetString());
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession("stale-tracing-thread");
            TracingChatClient.ClearActiveSession(currentThreadId);
        }
    }

    [Fact]
    public async Task ChatGptOAuthResponsesAttemptDiagnosticCapturesOnlyStatusRequestIdAndRoutingHashes()
    {
        const string installationId = "11111111-1111-4111-8111-111111111111";
        const string rootThreadId = "thread-root-secret";
        const string currentThreadId = "thread-child-secret";
        const string requestId = "req_attempt_500";

        try
        {
            await using var server = RecordingHttpServer.Start(JsonResponse(
                """{"error":{"message":"provider failed","type":"server_error"}}""",
                HttpStatusCode.InternalServerError,
                new Dictionary<string, string> { ["x-request-id"] = requestId }));
            var provider = CreateOAuthProvider(installationId, accountId: "acct_token");
            TracingChatClient.CurrentSessionKey = currentThreadId;
            using var codexScope = OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
                currentThreadId,
                "turn_attempt",
                "window_attempt",
                rootThreadId: rootThreadId,
                parentThreadId: rootThreadId,
                subagentKind: "thread_spawn"));
            using var attemptScope = ModelStreamAttemptRuntimeScope.Begin(1);

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
                    .GetResponsesClient()
                    .CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "hello")));

            var diagnostic = Assert.IsType<ModelStreamAttemptRuntimeContext>(ModelStreamAttemptRuntimeScope.Current);
            Assert.Equal(500, diagnostic.StatusCode);
            Assert.Equal(requestId, diagnostic.RequestId);
            Assert.Equal(ComputeSha256(rootThreadId), diagnostic.SessionIdHash);
            Assert.Equal(ComputeSha256(currentThreadId), diagnostic.ThreadIdHash);
            Assert.Equal(ComputeSha256(rootThreadId), diagnostic.PromptCacheKeyHash);
            var serialized = JsonSerializer.Serialize(diagnostic);
            Assert.DoesNotContain(rootThreadId, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(currentThreadId, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("acct_token", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("provider failed", serialized, StringComparison.Ordinal);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession(currentThreadId);
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
            var codexContext = CreateCodexRuntimeContext(
                sessionKey,
                "turn_001",
                windowId,
                turnStartedAtUnixMs: 1778544000000);
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
    public async Task ChatGptOAuthResponsesRetryPinsCompatibilityFallbackIdentityFromFirstRequest()
    {
        const string installationId = "11111111-1111-4111-8111-111111111111";
        const string firstThreadId = "thread-fallback-a";
        const string changedThreadId = "thread-fallback-b";
        var fallbackChanged = false;
        var auth = new FakeOpenAIAuthService("acct_token")
        {
            OnGetAccessToken = forceRefresh =>
            {
                if (forceRefresh)
                {
                    TracingChatClient.CurrentSessionKey = changedThreadId;
                    fallbackChanged = true;
                }
            }
        };

        try
        {
            await using var server = RecordingHttpServer.Start(
                JsonResponse("{}", HttpStatusCode.Unauthorized),
                JsonResponse(SuccessfulResponseJson));
            var provider = CreateOAuthProvider(installationId, auth);
            TracingChatClient.CurrentSessionKey = firstThreadId;
            var options = CreateNonStreamingResponseOptions("gpt-test", "retry");

            await provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
                .GetResponsesClient()
                .CreateResponseAsync(options);

            Assert.Equal([false, true], auth.ForceRefreshCalls);
            Assert.True(fallbackChanged);
            Assert.Equal(2, server.Requests.Count);
            foreach (var request in server.Requests)
            {
                Assert.Equal(firstThreadId, request.Headers[OpenAIAuthConstants.SessionIdHeader]);
                Assert.Equal(firstThreadId, request.Headers[OpenAIAuthConstants.ThreadIdHeader]);

                using var document = JsonDocument.Parse(request.Body);
                Assert.Equal(firstThreadId, document.RootElement.GetProperty("prompt_cache_key").GetString());
                var metadata = document.RootElement.GetProperty("client_metadata");
                Assert.Equal(firstThreadId, metadata.GetProperty(OpenAIAuthConstants.SessionIdCompatHeader).GetString());
                Assert.Equal(firstThreadId, metadata.GetProperty("thread_id").GetString());
            }
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession(firstThreadId);
            TracingChatClient.ClearActiveSession(changedThreadId);
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

            var firstContext = CreateCodexRuntimeContext(
                sessionKey,
                "turn_001",
                firstWindowId,
                turnStartedAtUnixMs: 1778544000000);
            using (OpenAIResponsesCodexRuntimeScope.Set(firstContext))
            {
                await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "first"));
                await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "second"));
            }

            using (OpenAIResponsesCodexRuntimeScope.Set(CreateCodexRuntimeContext(
                       sessionKey,
                       "turn_002",
                       secondWindowId,
                       turnStartedAtUnixMs: 1778544100000)))
            {
                await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "third"));
            }

            Assert.Equal(3, server.Requests.Count);
            Assert.False(server.Requests[0].Headers.ContainsKey(OpenAIAuthConstants.TurnStateHeader));
            Assert.Equal("state-one", server.Requests[1].Headers[OpenAIAuthConstants.TurnStateHeader]);
            Assert.False(server.Requests[2].Headers.ContainsKey(OpenAIAuthConstants.TurnStateHeader));
            var expectedWindowIds = new[] { firstWindowId, firstWindowId, secondWindowId };
            for (var index = 0; index < server.Requests.Count; index++)
            {
                var request = server.Requests[index];
                Assert.Equal(sessionKey, request.Headers[OpenAIAuthConstants.SessionIdHeader]);
                Assert.Equal(sessionKey, request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
                Assert.Equal(expectedWindowIds[index], request.Headers[OpenAIAuthConstants.WindowIdHeader]);

                using var document = JsonDocument.Parse(request.Body);
                Assert.Equal(sessionKey, document.RootElement.GetProperty("prompt_cache_key").GetString());
                var metadata = document.RootElement.GetProperty("client_metadata");
                Assert.Equal(sessionKey, metadata.GetProperty(OpenAIAuthConstants.SessionIdCompatHeader).GetString());
                Assert.Equal(sessionKey, metadata.GetProperty("thread_id").GetString());
                Assert.Equal(expectedWindowIds[index], metadata.GetProperty(OpenAIAuthConstants.WindowIdHeader).GetString());
            }
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession(sessionKey);
        }
    }

    [Fact]
    public async Task ChatGptOAuthResponsesRuntimeTransitionsPreserveIdentityAndTurnState()
    {
        const string installationId = "11111111-1111-4111-8111-111111111111";
        const string threadId = "thread-runtime-transitions";
        const string firstWindowId = "window-runtime-a";
        const string secondWindowId = "window-runtime-b";
        const string turnState = "state-runtime";

        try
        {
            await using var server = RecordingHttpServer.Start(
                JsonResponse(
                    SuccessfulResponseJson,
                    headers: new Dictionary<string, string>
                    {
                        [OpenAIAuthConstants.TurnStateHeader] = turnState
                    }),
                JsonResponse(SuccessfulResponseJson),
                JsonResponse(SuccessfulResponseJson),
                JsonResponse(SuccessfulResponseJson));
            var provider = CreateOAuthProvider(installationId, accountId: "acct_token");
            var client = provider.GetOpenAIClient(OAuthRuntime($"{server.Endpoint}/backend-api/codex"))
                .GetResponsesClient();
            var context = CreateCodexRuntimeContext(
                threadId,
                "turn-runtime",
                firstWindowId,
                turnStartedAtUnixMs: 1778544000000);
            using var codexScope = OpenAIResponsesCodexRuntimeScope.Set(context);

            await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "turn-before"));
            using (context.OverrideRequestKind(ThreadConversationRequestKind.Compaction))
            {
                await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "compaction"));
            }

            await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "turn-after"));
            context.AdvanceContextWindow(secondWindowId);
            await client.CreateResponseAsync(CreateNonStreamingResponseOptions("gpt-test", "new-window"));

            Assert.Equal(4, server.Requests.Count);
            var expectedRequestKinds = new[] { "turn", "compaction", "turn", "turn" };
            var expectedWindowIds = new[] { firstWindowId, firstWindowId, firstWindowId, secondWindowId };
            for (var index = 0; index < server.Requests.Count; index++)
            {
                var request = server.Requests[index];
                Assert.Equal(threadId, request.Headers[OpenAIAuthConstants.SessionIdHeader]);
                Assert.Equal(threadId, request.Headers[OpenAIAuthConstants.ThreadIdHeader]);
                Assert.Equal(expectedWindowIds[index], request.Headers[OpenAIAuthConstants.WindowIdHeader]);
                if (index == 0)
                    Assert.False(request.Headers.ContainsKey(OpenAIAuthConstants.TurnStateHeader));
                else
                    Assert.Equal(turnState, request.Headers[OpenAIAuthConstants.TurnStateHeader]);

                using var document = JsonDocument.Parse(request.Body);
                Assert.Equal(threadId, document.RootElement.GetProperty("prompt_cache_key").GetString());
                var metadata = document.RootElement.GetProperty("client_metadata");
                Assert.Equal(threadId, metadata.GetProperty(OpenAIAuthConstants.SessionIdCompatHeader).GetString());
                Assert.Equal(threadId, metadata.GetProperty("thread_id").GetString());
                Assert.Equal(expectedWindowIds[index], metadata.GetProperty(OpenAIAuthConstants.WindowIdHeader).GetString());

                using var turnMetadata = JsonDocument.Parse(
                    metadata.GetProperty(OpenAIAuthConstants.TurnMetadataHeader).GetString()!);
                Assert.Equal(
                    expectedRequestKinds[index],
                    turnMetadata.RootElement.GetProperty("request_kind").GetString());
                Assert.Equal(
                    expectedWindowIds[index],
                    turnMetadata.RootElement.GetProperty("window_id").GetString());
                Assert.Equal(threadId, turnMetadata.RootElement.GetProperty("session_id").GetString());
                Assert.Equal(threadId, turnMetadata.RootElement.GetProperty("thread_id").GetString());
            }
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = null;
            TracingChatClient.ClearActiveSession(threadId);
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

    private static OpenAIResponsesCodexRuntimeContext CreateCodexRuntimeContext(
        string currentThreadId,
        string? turnId,
        string contextWindowId,
        string? rootThreadId = null,
        string? parentThreadId = null,
        string? forkedFromThreadId = null,
        string? subagentKind = null,
        string threadSource = "appserver",
        long turnStartedAtUnixMs = 0,
        ThreadConversationRequestKind requestKind = ThreadConversationRequestKind.Turn) =>
        new(new ThreadConversationIdentity(
            CurrentThreadId: currentThreadId,
            RootThreadId: rootThreadId ?? currentThreadId,
            ParentThreadId: parentThreadId,
            ForkedFromThreadId: forkedFromThreadId,
            TurnId: turnId,
            ContextWindowId: contextWindowId,
            RequestKind: requestKind,
            TurnStartedAtUnixMs: turnStartedAtUnixMs,
            ThreadSource: threadSource,
            SubagentKind: subagentKind));

    private static RecordingHttpServer.ResponseSpec JsonResponse(
        string json,
        HttpStatusCode status = HttpStatusCode.OK,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(status, "application/json", json, headers);

    private static JsonElement ReadObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string BuildSanitizedCacheWireSnapshot(RecordedHttpRequest request)
    {
        var relevantHeaders = new[]
        {
            "Authorization",
            "Content-Type",
            OpenAIAuthConstants.AccountIdHeader,
            OpenAIAuthConstants.OriginatorHeader,
            OpenAIAuthConstants.SessionIdHeader,
            OpenAIAuthConstants.ThreadIdHeader,
            OpenAIAuthConstants.WindowIdHeader,
            OpenAIAuthConstants.TurnMetadataHeader,
            OpenAIAuthConstants.TurnStateHeader
        };
        var builder = new StringBuilder()
            .Append(request.Method)
            .Append(' ')
            .Append(request.Path)
            .Append('\n');
        foreach (var headerName in relevantHeaders.Order(StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(headerName).Append(':');
            if (request.Headers.TryGetValue(headerName, out var value))
            {
                builder.Append(
                    headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                        ? "<redacted>"
                        : value);
            }
            else
            {
                builder.Append("<absent>");
            }

            builder.Append('\n');
        }

        return builder.Append(request.Body).ToString();
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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

        public Action<bool>? OnGetAccessToken { get; init; }

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
            OnGetAccessToken?.Invoke(forceRefresh);
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
