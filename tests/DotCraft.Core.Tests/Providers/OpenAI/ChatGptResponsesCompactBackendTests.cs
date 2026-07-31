using System.ClientModel.Primitives;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context.Compaction;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Core.Tests.Agents;

public sealed class ChatGptResponsesCompactBackendTests
{
    [Theory]
    [InlineData(ModelProviderAuthMethods.ChatGptOAuth, ModelProviderProtocols.OpenAIResponses, HistoryMode.Server, 1, true)]
    [InlineData(ModelProviderAuthMethods.ApiKey, ModelProviderProtocols.OpenAIResponses, HistoryMode.Server, 1, false)]
    [InlineData(ModelProviderAuthMethods.ChatGptOAuth, ModelProviderProtocols.OpenAIChatCompletions, HistoryMode.Server, 1, false)]
    [InlineData(ModelProviderAuthMethods.ChatGptOAuth, ModelProviderProtocols.OpenAIResponses, HistoryMode.Client, 1, false)]
    [InlineData(ModelProviderAuthMethods.ChatGptOAuth, ModelProviderProtocols.OpenAIResponses, HistoryMode.Server, 0, false)]
    public void Eligibility_MatchesRuntimeHistoryMatrix(
        string authMethod,
        string protocol,
        HistoryMode historyMode,
        int schemaVersion,
        bool expected)
    {
        var runtime = new EffectiveModelRuntime(
            "provider",
            "model",
            protocol,
            "Provider",
            string.Empty,
            "https://example.test",
            30,
            MaxOutputTokens: null,
            IsImplicit: false,
            ModelProviderCapabilities.ForProtocol(protocol),
            AuthMethod: authMethod);

        Assert.Equal(
            expected,
            ChatGptResponsesCompactEligibility.IsEligible(runtime, historyMode, schemaVersion));
    }

    [Fact]
    public void RequestBuilder_ProjectsOnlyCompactSupportedFields()
    {
        var input = new ProviderCompactionInput(
            [ReadObject("""{"type":"message","role":"user","content":[{"type":"input_text","text":"hello"}]}""")],
            CoveredMessageCount: 1,
            CoveredThroughTurnId: "turn_1");
        var tool = AIFunctionFactory.Create(
            (string value) => value,
            name: "echo",
            description: "Echo a value.");
        var options = new ChatOptions
        {
            ModelId = "gpt-test",
            Instructions = "stay concise",
            Tools = [tool],
            AllowMultipleToolCalls = true,
            MaxOutputTokens = 123,
            ResponseFormat = ChatResponseFormat.Json,
            Reasoning = new ReasoningOptions
            {
                Effort = ReasoningEffort.High,
                Output = ReasoningOutput.Summary
            },
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ResponsesToolSearchMapper.PromptCacheKeyAdditionalProperty] = "cache-key"
            },
            RawRepresentationFactory = _ =>
            {
                var raw = new CreateResponseOptions();
                ResponsesToolSearchMapper.PatchResponseServiceTier(raw, "priority");
                return raw;
            }
        };

        var body = ChatGptResponsesCompactRequestBuilder.Build(
            "gpt-test",
            input,
            [new ChatMessage(ChatRole.User, "hello")],
            options,
            new NoopChatClient());
        var wire = JsonSerializer.SerializeToElement(
            body,
            ChatGptResponsesCompactJson.Options);

        Assert.Equal(
            [
                "input",
                "instructions",
                "model",
                "parallel_tool_calls",
                "prompt_cache_key",
                "reasoning",
                "service_tier",
                "text",
                "tools"
            ],
            wire.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal("priority", body.ServiceTier);
        Assert.Equal("cache-key", body.PromptCacheKey);
        Assert.Equal("high", body.Reasoning?.GetProperty("effort").GetString());
        Assert.Equal("message", Assert.Single(body.Input).GetProperty("type").GetString());
        Assert.False(wire.TryGetProperty("stream", out _));
        Assert.False(wire.TryGetProperty("store", out _));
        Assert.False(wire.TryGetProperty("include", out _));
        Assert.False(wire.TryGetProperty("client_metadata", out _));
        Assert.False(wire.TryGetProperty("max_output_tokens", out _));
    }

    [Fact]
    public void RequestBuilder_OmitsEmptyInstructionsAndTools()
    {
        var input = new ProviderCompactionInput(
            [ReadObject("""{"type":"retained","provider_only":true}""")],
            CoveredMessageCount: 1,
            CoveredThroughTurnId: "turn_1");
        var body = ChatGptResponsesCompactRequestBuilder.Build(
            "gpt-test",
            input,
            [new ChatMessage(ChatRole.User, "neutral history")],
            new ChatOptions { Instructions = "   ", Tools = [] });
        var wire = JsonSerializer.SerializeToElement(
            body,
            ChatGptResponsesCompactJson.Options);

        Assert.Null(body.Instructions);
        Assert.Null(body.Tools);
        Assert.Equal("retained", Assert.Single(body.Input).GetProperty("type").GetString());
        Assert.False(wire.TryGetProperty("instructions", out _));
        Assert.False(wire.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task Backend_PreservesOrderedUnknownOutputAndDoesNotFallback()
    {
        var bridge = new FakeBridge(
            new ProviderCompactionInput(
                [ReadObject("""{"type":"message","role":"user","content":[]}""")],
                1,
                "turn_1"),
            estimatedTokens: 42);
        var transport = new FakeTransport(
            """
            {
              "id": "compact_response_1",
              "output": [
                {"type":"retained","provider_field":{"value":1}},
                {"type":"future_compaction","encrypted_content":"YWJjZA=="}
              ]
            }
            """);
        var backend = new ChatGptResponsesCompactBackend(
            "gpt-test",
            transport,
            tokens => Threshold(tokens));

        var result = await backend.ExecuteAsync(
            new CompactionExecutionRequest(
                CompactionTrigger.Auto,
                CompactionPhase.MidTurn,
                [new ChatMessage(ChatRole.User, "hello")],
                "thread_1",
                100,
                null,
                Options: new ChatOptions(),
                ProviderBridge: bridge),
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Equal(42, result.Status.EstimatedTokensAfter);
        var replacement = Assert.IsType<CompactionReplacement.ProviderNative>(result.Replacement);
        Assert.Equal(["retained", "future_compaction"], replacement.Items.Select(ReadType));
        Assert.Equal(1, transport.CallCount);
        Assert.Equal(1, bridge.CaptureCount);
    }

    [Theory]
    [InlineData("""{"output":null}""")]
    [InlineData("""{"output":[]}""")]
    [InlineData("""{"output":[1]}""")]
    public void ValidateOutput_RejectsInvalidWindows(string json)
    {
        var response = JsonSerializer.Deserialize<ChatGptResponsesCompactResponse>(
            json,
            ChatGptResponsesCompactJson.Options)!;
        var error = Assert.Throws<InvalidDataException>(
            () => ChatGptResponsesCompactBackend.ValidateOutput(response));
        Assert.Contains("provider_compaction_invalid_response", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactResponseDto_RequiresOutput()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ChatGptResponsesCompactResponse>(
                "{}",
                ChatGptResponsesCompactJson.Options));
    }

    [Fact]
    public async Task ProviderHistoryCapture_IsReadOnlyAndPhaseAware()
    {
        var appendCount = 0;
        var context = CreateContext(
            new ProviderHistorySnapshot(
                "window_1",
                "window_1",
                [Entry("old", """{"type":"message","role":"user","content":[]}""")],
                "turn_1"),
            coveredMessageCount: 1,
            appendAsync: (_, _) =>
            {
                appendCount++;
                return Task.CompletedTask;
            });
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "old"),
            new ChatMessage(ChatRole.User, "new")
        };

        var preTurn = await context.CaptureCompactionInputAsync(
            CompactionPhase.PreTurn,
            messages,
            options: null,
            CancellationToken.None);
        var midTurn = await context.CaptureCompactionInputAsync(
            CompactionPhase.MidTurn,
            messages,
            options: null,
            CancellationToken.None);

        Assert.Single(preTurn.Items);
        Assert.Equal(1, preTurn.CoveredMessageCount);
        Assert.Equal(2, midTurn.Items.Count);
        Assert.Equal(2, midTurn.CoveredMessageCount);
        Assert.Equal(0, appendCount);
        Assert.Single(context.CaptureSnapshot().Entries);
    }

    [Fact]
    public async Task ProviderHistoryNativeInstall_PublishesOnlyAfterDurableReplacement()
    {
        ProviderHistoryReplacedPayload? persisted = null;
        ProviderHistorySnapshot? snapshotDuringCommit = null;
        string? reconciledWindow = null;
        OpenAIResponsesProviderHistoryContext? context = null;
        context = CreateContext(
            new ProviderHistorySnapshot(
                "window_1",
                "window_1",
                [Entry("old", """{"type":"message","role":"user","content":[]}""")],
                "turn_1"),
            coveredMessageCount: 1,
            replaceAsync: (payload, _) =>
            {
                snapshotDuringCommit = context!.CaptureSnapshot();
                persisted = payload;
                return Task.CompletedTask;
            },
            reconcileAsync: (_, committed, _) =>
            {
                reconciledWindow = committed;
                return Task.CompletedTask;
            });
        var replacement = new CompactionReplacement.ProviderNative(
            ProviderHistorySchema.OpenAIResponsesProtocol,
            [
                ReadObject("""{"type":"retained","unknown":true}"""),
                ReadObject("""{"type":"compaction","encrypted_content":"YWJj"}""")
            ],
            CoveredMessageCount: 2,
            CoveredThroughTurnId: "turn_2",
            EstimatedTokensAfter: 10);

        await context.ReplaceNativeAsync(replacement, CancellationToken.None);

        Assert.Equal("window_1", snapshotDuringCommit!.ContextWindowId);
        Assert.NotNull(persisted);
        Assert.Equal(ProviderHistoryReasons.RemoteCompaction, persisted!.Reason);
        Assert.Equal(2, persisted.Entries.Count);
        Assert.Equal(persisted.ContextWindowId, reconciledWindow);
        var installed = context.CaptureSnapshot();
        Assert.True(installed.IsNativeCompacted);
        Assert.Equal(persisted.ContextWindowId, installed.ContextWindowId);
        Assert.Equal(["retained", "compaction"], installed.Entries.Select(entry => ReadType(entry.Item)));
    }

    [Fact]
    public async Task ProviderHistoryNativeInstall_PersistFailureKeepsPreviousGeneration()
    {
        var context = CreateContext(
            new ProviderHistorySnapshot(
                "window_1",
                "window_1",
                [Entry("old", """{"type":"message","role":"user","content":[]}""")],
                "turn_1"),
            coveredMessageCount: 1,
            replaceAsync: (_, _) => throw new IOException("write failed"));
        var replacement = new CompactionReplacement.ProviderNative(
            ProviderHistorySchema.OpenAIResponsesProtocol,
            [ReadObject("""{"type":"compaction","encrypted_content":"YWJj"}""")],
            1,
            "turn_1",
            10);

        await Assert.ThrowsAsync<IOException>(
            async () => await context.ReplaceNativeAsync(replacement, CancellationToken.None));

        var snapshot = context.CaptureSnapshot();
        Assert.Equal("window_1", snapshot.ContextWindowId);
        Assert.False(snapshot.IsNativeCompacted);
        Assert.Equal("message", ReadType(Assert.Single(snapshot.Entries).Item));
    }

    [Fact]
    public void ProviderHistoryReplay_RestoresNativeGenerationAndUnknownItems()
    {
        var replacement = new ProviderHistoryReplacedPayload
        {
            SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
            ThreadId = "thread_1",
            Protocol = ProviderHistorySchema.OpenAIResponsesProtocol,
            GenerationId = "window_2",
            ContextWindowId = "window_2",
            CoveredThroughTurnId = "turn_1",
            Reason = ProviderHistoryReasons.RemoteCompaction,
            Entries =
            [
                Entry("retained", """{"type":"retained","provider_only":{"x":1}}"""),
                Entry("future", """{"type":"future_compaction","encrypted_content":"YWJj"}""")
            ]
        };

        var snapshot = ProviderHistoryReplayer.Replay(
            "thread_1",
            "window_1",
            new HashSet<string>(["turn_1"], StringComparer.Ordinal),
            [
                new ThreadRolloutRecord
                {
                    Kind = "provider_history_replaced",
                    ProviderHistoryReplaced = replacement
                }
            ]);

        Assert.True(snapshot.IsNativeCompacted);
        Assert.Equal("window_2", snapshot.ContextWindowId);
        Assert.Equal(["retained", "future_compaction"], snapshot.Entries.Select(entry => ReadType(entry.Item)));
        Assert.Equal(1, snapshot.Entries[0].Item.GetProperty("provider_only").GetProperty("x").GetInt32());
    }

    [Fact]
    public void NativeEstimator_IncludesProtectedPayloadControlsAndPendingTail()
    {
        var item = ReadObject(
            """{"type":"compaction","encrypted_content":"QUJDREVGR0hJSktMTU5PUA=="}""");

        var baseEstimate = OpenAIResponsesNativeTokenEstimator.Estimate(
            [item],
            pendingTail: [],
            options: null);
        var expanded = OpenAIResponsesNativeTokenEstimator.Estimate(
            [item],
            [new ChatMessage(ChatRole.User, "pending tail")],
            new ChatOptions
            {
                Instructions = "instructions",
                Tools =
                [
                    AIFunctionFactory.Create(
                        (string value) => value,
                        name: "echo",
                        description: "Echo a value.")
                ]
            });

        Assert.True(baseEstimate > 0);
        Assert.True(expanded > baseEstimate);
    }

    [Fact]
    public void NativeEstimator_InlineImagePayloadSizeDoesNotInflateEstimate()
    {
        var small = CreateMessageWithContent(
            new
            {
                type = "input_image",
                image_url = "DATA:image/png;charset=utf-8;BASE64,AAAA"
            });
        var large = CreateMessageWithContent(
            new
            {
                type = "input_image",
                image_url = "DATA:image/png;charset=utf-8;BASE64," + new string('A', 100_000)
            });

        Assert.Equal(EstimateNative(small), EstimateNative(large));
    }

    [Fact]
    public void NativeEstimator_RecursivelyAccountsForEachImageWithoutItsPayload()
    {
        var oneImage = CreateMessageWithContent(
            new
            {
                type = "input_image",
                image_url = "data:image/png;base64," + new string('A', 4_000)
            });
        var twoImages = JsonSerializer.Serialize(new
        {
            type = "message",
            role = "user",
            content = new object[]
            {
                new
                {
                    type = "input_image",
                    image_url = "data:image/png;base64," + new string('A', 4)
                },
                new
                {
                    type = "output_image",
                    image_url = "data:image/jpeg;base64," + new string('B', 40_000)
                }
            }
        });

        var delta = EstimateNative(twoImages) - EstimateNative(oneImage);

        Assert.InRange(delta, 1_950, 2_050);
    }

    [Fact]
    public void NativeEstimator_AudioAndGeneratedImagePayloadSizesDoNotInflateEstimate()
    {
        var small = JsonSerializer.Serialize(new object[]
        {
            new
            {
                type = "message",
                content = new object[]
                {
                    new
                    {
                        type = "input_audio",
                        input_audio = new { data = "AAAA", format = "wav" }
                    }
                }
            },
            new { type = "image_generation_call", result = "AAAA" }
        });
        var large = JsonSerializer.Serialize(new object[]
        {
            new
            {
                type = "message",
                content = new object[]
                {
                    new
                    {
                        type = "input_audio",
                        input_audio = new { data = new string('A', 80_000), format = "wav" }
                    }
                }
            },
            new { type = "image_generation_call", result = new string('B', 120_000) }
        });

        Assert.Equal(EstimateNative(small), EstimateNative(large));
    }

    [Fact]
    public void NativeEstimator_InlineAudioUrlPayloadSizeDoesNotInflateEstimate()
    {
        var small = CreateMessageWithContent(
            new
            {
                type = "input_audio",
                audio_url = "data:audio/wav;charset=utf-8;base64,AAAA"
            });
        var large = CreateMessageWithContent(
            new
            {
                type = "input_audio",
                audio_url = "data:audio/wav;charset=utf-8;base64," + new string('A', 100_000)
            });

        Assert.Equal(EstimateNative(small), EstimateNative(large));
    }

    [Fact]
    public void NativeEstimator_PreservesRemoteMalformedAndUnknownPayloadBytes()
    {
        var shortRemote = CreateMessageWithContent(
            new { type = "input_image", image_url = "https://example.test/a.png" });
        var longRemote = CreateMessageWithContent(
            new { type = "input_image", image_url = "https://example.test/" + new string('a', 4_000) });
        var shortMalformed = CreateMessageWithContent(
            new { type = "input_image", image_url = "data:text/plain;base64,AAAA" });
        var longMalformed = CreateMessageWithContent(
            new { type = "input_image", image_url = "data:text/plain;base64," + new string('A', 4_000) });
        var shortUnknown = JsonSerializer.Serialize(
            new { type = "future_item", payload = "AAAA" });
        var longUnknown = JsonSerializer.Serialize(
            new { type = "future_item", payload = new string('A', 4_000) });

        Assert.True(EstimateNative(longRemote) > EstimateNative(shortRemote) + 900);
        Assert.True(EstimateNative(longMalformed) > EstimateNative(shortMalformed) + 900);
        Assert.True(EstimateNative(longUnknown) > EstimateNative(shortUnknown) + 900);
    }

    private static OpenAIResponsesProviderHistoryContext CreateContext(
        ProviderHistorySnapshot snapshot,
        int coveredMessageCount,
        Func<ProviderHistoryItemsAppendedPayload, CancellationToken, Task>? appendAsync = null,
        Func<ProviderHistoryReplacedPayload, CancellationToken, Task>? replaceAsync = null,
        Func<string, string, CancellationToken, Task>? reconcileAsync = null) =>
        new(
            new ThreadConversationIdentity(
                "thread_1",
                "thread_1",
                ParentThreadId: null,
                ForkedFromThreadId: null,
                TurnId: "turn_2",
                ContextWindowId: snapshot.ContextWindowId,
                ThreadConversationRequestKind.Compaction,
                TurnStartedAtUnixMs: 1,
                ThreadSource: "test",
                SubagentKind: null),
            snapshot,
            coveredMessageCount,
            appendAsync,
            replaceAsync,
            abortAsync: null,
            reconcileAsync);

    private static ProviderHistoryEntry Entry(string id, string json) =>
        new()
        {
            EntryId = id,
            Item = ReadObject(json)
        };

    private static JsonElement ReadObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string CreateMessageWithContent(object content) =>
        JsonSerializer.Serialize(new
        {
            type = "message",
            role = "user",
            content = new[] { content }
        });

    private static long EstimateNative(string json) =>
        OpenAIResponsesNativeTokenEstimator.Estimate(
            [ReadObject(json)],
            pendingTail: [],
            options: null);

    private static string? ReadType(JsonElement item) =>
        item.GetProperty("type").GetString();

    private static CompactionThreshold Threshold(long tokens) =>
        new(tokens, 80, 85, 90, 100, Math.Max(0, 1 - tokens / 100d));

    private sealed class FakeTransport(string responseJson) : IChatGptResponsesCompactTransport
    {
        public int CallCount { get; private set; }

        public Task<ChatGptResponsesCompactResponse> CompactAsync(
            ChatGptResponsesCompactRequest requestBody,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(
                JsonSerializer.Deserialize<ChatGptResponsesCompactResponse>(
                    responseJson,
                    ChatGptResponsesCompactJson.Options)!);
        }
    }

    private sealed class FakeBridge(
        ProviderCompactionInput input,
        long estimatedTokens) : IProviderHistoryCompactionBridge
    {
        public int CaptureCount { get; private set; }

        public ValueTask<ProviderCompactionInput> CaptureCompactionInputAsync(
            CompactionPhase phase,
            IReadOnlyList<ChatMessage> messages,
            ChatOptions? options,
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            return ValueTask.FromResult(input);
        }

        public ValueTask ReplaceNativeAsync(
            CompactionReplacement.ProviderNative replacement,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public long EstimateNativeContextTokens(
            ProviderNativeSnapshot snapshot,
            IReadOnlyList<ChatMessage> pendingTail,
            ChatOptions? options) =>
            estimatedTokens;
    }

    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
