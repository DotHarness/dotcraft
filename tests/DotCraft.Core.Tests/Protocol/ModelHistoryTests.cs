using System.Reflection;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Protocol;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ModelHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft_model_history_{Guid.NewGuid():N}");

    [Fact]
    public void Codec_RoundTripsAdditionalPropertiesAndReasoningProtectedData_WithoutRawRepresentation()
    {
        var nested = JsonSerializer.Deserialize<JsonElement>("""{"array":[1,true,null,{"name":"value"}]}""");
        var reasoning = new TextReasoningContent("thinking")
        {
            ProtectedData = "encrypted-replay-token",
            RawRepresentation = new object(),
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["provider"] = "openai-responses",
                ["nested"] = nested
            }
        };
        var message = new ChatMessage(ChatRole.Assistant, [reasoning])
        {
            MessageId = "message-1",
            AuthorName = "assistant-name",
            CreatedAt = DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
            RawRepresentation = new object(),
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["messageNested"] = nested,
                ["number"] = 42
            }
        };

        var codec = new ModelHistoryCodec();
        var encoded = codec.Encode(message, "turn_001");
        var serialized = JsonSerializer.Serialize(encoded, SessionJsonOptions.Default);
        var restored = codec.Decode(
            JsonSerializer.Deserialize<ModelHistoryMessage>(serialized, SessionJsonOptions.Default)!);

        Assert.DoesNotContain("RawRepresentation", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$type", serialized, StringComparison.Ordinal);
        Assert.Null(restored.RawRepresentation);
        Assert.Equal("message-1", restored.MessageId);
        Assert.Equal("assistant-name", restored.AuthorName);
        Assert.Equal(message.CreatedAt, restored.CreatedAt);
        Assert.Equal(
            encoded.AdditionalProperties!.Value.GetRawText(),
            JsonSerializer.SerializeToElement(restored.AdditionalProperties, SessionPersistenceJsonOptions.Default).GetRawText());

        var restoredReasoning = Assert.IsType<TextReasoningContent>(Assert.Single(restored.Contents));
        Assert.Equal("thinking", restoredReasoning.Text);
        Assert.Equal("encrypted-replay-token", restoredReasoning.ProtectedData);
        Assert.Null(restoredReasoning.RawRepresentation);
        Assert.Equal(
            reasoning.AdditionalProperties!.Count,
            restoredReasoning.AdditionalProperties!.Count);
        Assert.IsType<string>(restoredReasoning.AdditionalProperties["provider"]);
        Assert.IsType<long>(restored.AdditionalProperties!["number"]);
    }

    [Fact]
    public void Codec_UsesOwnedSchemaFixtureForEveryDurableContentKind()
    {
        var codec = new ModelHistoryCodec();
        var encoded = codec.Encode(CreateComprehensiveMessage(), "turn_fixture");
        var actual = JsonSerializer.SerializeToElement(encoded, SessionJsonOptions.Default);
        var expected = ReadSchemaFixture();
        var actualJson = actual.GetRawText();

        Assert.True(JsonElement.DeepEquals(expected, actual),
            $"Persisted model-history schema changed.\nExpected: {expected}\nActual: {actual}");
        Assert.DoesNotContain("$type", actualJson, StringComparison.Ordinal);
        Assert.DoesNotContain("rawRepresentation", actualJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not persisted", actualJson, StringComparison.Ordinal);

        var decodedFixture = expected.Deserialize<ModelHistoryMessage>(SessionJsonOptions.Default)!;
        var restored = codec.Decode(decodedFixture);
        var reencoded = JsonSerializer.SerializeToElement(codec.Encode(restored, "turn_fixture"), SessionJsonOptions.Default);

        Assert.True(JsonElement.DeepEquals(expected, reencoded));
        Assert.Equal(12, restored.Contents.Count);
        Assert.DoesNotContain(restored.Contents, static content => content is ToolCallArgumentsDeltaContent);
        var data = Assert.IsType<DataContent>(restored.Contents[2]);
        Assert.Equal("AQID", data.Base64Data.ToString());
        Assert.Equal("sample.png", data.Name);
        var functionCall = Assert.IsType<FunctionCallContent>(restored.Contents[3]);
        Assert.Equal("tools", functionCall.AdditionalProperties!["openai.responses.function_call.namespace"]);
        Assert.Equal("tools--read_file", functionCall.AdditionalProperties["dotcraft.tool.provider_flat_name"]);
        Assert.Equal("sample.txt", Assert.IsType<string>(functionCall.Arguments!["path"]));
        var jsonResult = Assert.IsType<FunctionResultContent>(restored.Contents[4]);
        Assert.True(Assert.IsType<JsonElement>(jsonResult.Result).GetProperty("ok").GetBoolean());
        var contentResult = Assert.IsType<FunctionResultContent>(restored.Contents[5]);
        var nestedContents = Assert.IsAssignableFrom<IList<AIContent>>(contentResult.Result);
        Assert.Collection(
            nestedContents,
            content => Assert.Equal("nested result", Assert.IsType<TextContent>(content).Text),
            content => Assert.Equal("text/plain", Assert.IsType<DataContent>(content).MediaType));
        var hostedImage = Assert.IsType<HostedImageGenerationContent>(restored.Contents[6]);
        Assert.Equal(new byte[] { 4, 5, 6 }, hostedImage.ImageBytes);
        var imageResult = Assert.IsType<ImageGenerationToolResultContent>(restored.Contents[8]);
        Assert.Equal("https://example.invalid/image.png", Assert.IsType<UriContent>(imageResult.Outputs![1]).Uri.ToString());
        var usage = Assert.IsType<UsageContent>(restored.Contents[11]);
        Assert.Equal(15, usage.Details.TotalTokenCount);
    }

    [Fact]
    public void Codec_RejectsNullCollectionsAndInvalidUriAsJsonException()
    {
        var codec = new ModelHistoryCodec();

        var nullContents = new ModelHistoryMessage
        {
            Role = ChatRole.Assistant.Value,
            Contents = null!
        };
        Assert.Throws<JsonException>(() => codec.Decode(nullContents));

        var invalidUri = new ModelHistoryMessage
        {
            Role = ChatRole.Assistant.Value,
            Contents =
            [
                new ModelHistoryContent
                {
                    Kind = "uri",
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        uri = "not a valid absolute uri",
                        mediaType = "text/plain",
                        additionalProperties = (object?)null
                    }, JsonSerializerOptions.Web)
                }
            ]
        };
        Assert.Throws<JsonException>(() => codec.Decode(invalidUri));
    }

    [Fact]
    public void Codec_RejectsNullContentEntriesAndMissingPayloadAsJsonException()
    {
        var codec = new ModelHistoryCodec();
        var nullEntry = new ModelHistoryMessage
        {
            Role = ChatRole.Assistant.Value,
            Contents = [null!]
        };
        Assert.Throws<JsonException>(() => codec.Decode(nullEntry));

        var missingPayload = new ModelHistoryMessage
        {
            Role = ChatRole.Assistant.Value,
            Contents =
            [
                new ModelHistoryContent
                {
                    Kind = "text",
                    Payload = default
                }
            ]
        };
        Assert.Throws<JsonException>(() => codec.Decode(missingPayload));
    }

    [Fact]
    public void Codec_RejectsConflictingStrongToolIdentityAndAdditionalProperties()
    {
        var payload = new PersistedFunctionCallContent
        {
            CallId = "call_conflict",
            Name = "read_file",
            Arguments = JsonSerializer.SerializeToElement(new { path = "sample.txt" }),
            InformationalOnly = false,
            Namespace = "tools",
            ProviderFlatName = null,
            AdditionalProperties = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["openai.responses.function_call.namespace"] = "different"
            })
        };
        var message = new ModelHistoryMessage
        {
            Role = ChatRole.Assistant.Value,
            Contents =
            [
                new ModelHistoryContent
                {
                    Kind = "function_call",
                    Payload = JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web)
                }
            ]
        };

        Assert.Throws<JsonException>(() => new ModelHistoryCodec().Decode(message));
    }

    [Fact]
    public async Task ThreadStore_PersistsAndHydratesModelHistoryWithoutFrameworkSessionBlob()
    {
        Directory.CreateDirectory(_root);
        var store = new ThreadStore(_root);
        var thread = new SessionThread
        {
            Id = "thread_model_history",
            WorkspacePath = _root,
            OriginChannel = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = "thread_model_history",
                    Status = TurnStatus.Completed,
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow
                }
            ]
        };
        await store.SaveThreadAsync(thread);

        using var client = new PassiveChatClient();
        var agent = client.AsAIAgent(new ChatClientAgentOptions());
        var session = await agent.CreateSessionAsync();
        session.SetInMemoryChatHistory(
            [
                new ChatMessage(ChatRole.User, "hello"),
                new ChatMessage(ChatRole.Assistant,
                [
                    new TextReasoningContent("think") { ProtectedData = "protected" },
                    new TextContent("answer")
                ])
            ],
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default);

        await store.PersistModelHistoryAsync(session, thread.Id, "turn_001", persistedPrefixLength: 0);

        var rollout = await File.ReadAllTextAsync(
            Path.Combine(_root, "threads", "active", "thread_model_history.jsonl"));
        var historyRecord = Assert.Single(
            rollout.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.Contains("model_history_messages_appended", StringComparison.Ordinal));
        using (var historyJson = JsonDocument.Parse(historyRecord))
        {
            Assert.Equal(
                2,
                historyJson.RootElement
                    .GetProperty("modelHistoryMessagesAppended")
                    .GetProperty("messages")
                    .GetArrayLength());
        }

        var coldSession = await new ThreadStore(_root).LoadOrCreateSessionAsync(agent, thread.Id);
        Assert.True(coldSession.TryGetInMemoryChatHistory(
            out var history,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));
        Assert.Equal(2, history.Count);
        Assert.Equal("hello", history[0].Text);
        var restoredReasoning = Assert.IsType<TextReasoningContent>(history[1].Contents[0]);
        Assert.Equal("protected", restoredReasoning.ProtectedData);

        using (var connection = store.StateRuntime.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'thread_sessions'";
            Assert.Equal(0L, (long)(command.ExecuteScalar() ?? 0L));
        }
    }

    [Fact]
    public async Task OrderedWriter_PreservesRecordOrderAcrossCapacityBoundary()
    {
        var observed = new List<int>();
        var writer = new OrderedRolloutWriter((_, batch, _) =>
        {
            observed.AddRange(batch.Select(record => int.Parse(record.Kind, System.Globalization.CultureInfo.InvariantCulture)));
            return Task.FromResult(new RolloutWriteReceipt(
                observed.Count,
                batch.Count,
                new Dictionary<string, long>()));
        });
        var records = Enumerable.Range(0, 300)
            .Select(index => new ThreadRolloutRecord { Kind = index.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            .ToList();

        await writer.AddBatchAsync("thread", "unused", records);
        await writer.FlushAsync("thread");
        await writer.CloseAsync("thread");

        Assert.Equal(Enumerable.Range(0, 300), observed);
    }

    [Fact]
    public async Task OrderedWriter_DoesNotAppendSuffixAfterPersistentFailure()
    {
        var attempts = new List<IReadOnlyList<string>>();
        var writer = new OrderedRolloutWriter((_, batch, _) =>
        {
            attempts.Add(batch.Select(static record => record.Kind).ToList());
            return Task.FromException<RolloutWriteReceipt>(new IOException("persistent write failure"));
        });

        await writer.AddBatchAsync(
            "thread",
            "unused",
            [
                new ThreadRolloutRecord { Kind = "confirmed" },
                new ThreadRolloutRecord { Kind = "fail" },
                new ThreadRolloutRecord { Kind = "must-not-append" }
            ]);
        await Assert.ThrowsAsync<RolloutPersistenceException>(() => writer.FlushAsync("thread"));
        await Assert.ThrowsAsync<RolloutPersistenceException>(() => writer.CloseAsync("thread"));

        Assert.NotEmpty(attempts);
        Assert.All(attempts, attempt => Assert.Equal(["confirmed", "fail", "must-not-append"], attempt));
    }

    [Fact]
    public async Task OrderedWriter_FlushesOneQueuedBatchOnce()
    {
        var flushes = 0;
        IReadOnlyList<string>? observed = null;
        var writer = new OrderedRolloutWriter((_, batch, _) =>
        {
            flushes++;
            observed = batch.Select(static record => record.Kind).ToList();
            return Task.FromResult(new RolloutWriteReceipt(
                123,
                batch.Count,
                batch.GroupBy(static record => record.Kind)
                    .ToDictionary(static group => group.Key, static group => (long)group.Count())));
        });

        await writer.AddBatchAsync(
            "thread",
            "unused",
            [
                new ThreadRolloutRecord { Kind = "turn_state_replaced" },
                new ThreadRolloutRecord { Kind = "context_compacted" },
                new ThreadRolloutRecord { Kind = "model_history_messages_appended" }
            ]);
        var receipt = await writer.FlushAsync("thread");
        await writer.CloseAsync("thread");

        Assert.Equal(1, flushes);
        Assert.Equal(
            ["turn_state_replaced", "context_compacted", "model_history_messages_appended"],
            observed);
        Assert.Equal(3, receipt.RecordCount);
    }

    [Fact]
    public async Task OrderedWriter_PreservesDamagedTailAndResumesOnNextLine()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "damaged-tail.jsonl");
        var writer = new OrderedRolloutWriter();
        await writer.AddBatchAsync(
            "thread",
            path,
            [new ThreadRolloutRecord { Kind = "before_damage" }]);
        await writer.FlushAsync("thread");

        await File.AppendAllTextAsync(path, "{\"kind\":");
        await writer.AddBatchAsync(
            "thread",
            path,
            [new ThreadRolloutRecord { Kind = "after_damage" }]);
        await writer.FlushAsync("thread");
        await writer.CloseAsync("thread");

        var validKinds = File.ReadLines(path)
            .Select(line =>
            {
                try
                {
                    return JsonSerializer.Deserialize<ThreadRolloutRecord>(line, SessionJsonOptions.Default)?.Kind;
                }
                catch (JsonException)
                {
                    return null;
                }
            })
            .Where(static kind => kind != null)
            .ToList();
        Assert.Equal(["before_damage", "after_damage"], validKinds);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; SQLite pooling can briefly retain the file on Windows.
        }
    }

    private static ChatMessage CreateComprehensiveMessage()
    {
        var nestedProperties = JsonSerializer.Deserialize<JsonElement>("""{"array":[1,true,null,{"name":"value"}]}""");
        var functionCall = new FunctionCallContent(
            "call_1",
            "read_file",
            new Dictionary<string, object?> { ["path"] = "sample.txt", ["line"] = 7 })
        {
            InformationalOnly = true,
            Exception = new InvalidOperationException("not persisted"),
            RawRepresentation = new object(),
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["openai.responses.function_call.namespace"] = "tools",
                ["namespace"] = "tools",
                ["dotcraft.tool.provider_flat_name"] = "tools--read_file",
                ["extension"] = nestedProperties
            }
        };
        var functionResult = new FunctionResultContent(
            "call_1",
            JsonSerializer.Deserialize<JsonElement>("""{"ok":true,"count":2}"""))
        {
            Exception = new InvalidOperationException("not persisted"),
            RawRepresentation = new object()
        };
        var contentResult = new FunctionResultContent(
            "call_2",
            new List<AIContent>
            {
                new TextContent("nested result"),
                new DataContent(new byte[] { 9, 8 }, "text/plain") { Name = "nested.txt" }
            });
        var additionalCounts = new AdditionalPropertiesDictionary<long> { ["acceptedPredictionTokens"] = 3 };

        return new ChatMessage(ChatRole.Assistant,
        [
            new TextContent("hello")
            {
                AdditionalProperties = new AdditionalPropertiesDictionary { ["textMeta"] = nestedProperties }
            },
            new TextReasoningContent("thinking") { ProtectedData = "protected" },
            new DataContent(new byte[] { 1, 2, 3 }, "image/png") { Name = "sample.png" },
            functionCall,
            functionResult,
            contentResult,
            new HostedImageGenerationContent
            {
                Id = "image_1",
                Status = "completed",
                RevisedPrompt = "synthetic prompt",
                ImageBytes = new byte[] { 4, 5, 6 },
                MediaType = "image/png"
            },
            new ImageGenerationToolCallContent("image_call"),
            new ImageGenerationToolResultContent("image_call")
            {
                Outputs =
                [
                    new TextContent("generated"),
                    new UriContent("https://example.invalid/image.png", "image/png")
                ]
            },
            new ErrorContent("recoverable") { ErrorCode = "sample_error", Details = "synthetic details" },
            new UriContent("https://example.invalid/document.txt", "text/plain"),
            new UsageContent(new UsageDetails
            {
                InputTokenCount = 10,
                OutputTokenCount = 5,
                TotalTokenCount = 15,
                CachedInputTokenCount = 2,
                ReasoningTokenCount = 1,
#pragma warning disable MEAI001
                InputAudioTokenCount = 0,
                InputTextTokenCount = 10,
                OutputAudioTokenCount = 0,
                OutputTextTokenCount = 5,
#pragma warning restore MEAI001
                AdditionalCounts = additionalCounts
            }),
            new ToolCallArgumentsDeltaContent
            {
                ToolCallIndex = 0,
                ToolName = "read_file",
                CallId = "call_1",
                ArgumentsDelta = "{"
            }
        ])
        {
            MessageId = "message_fixture",
            AuthorName = "assistant_fixture",
            CreatedAt = DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
            RawRepresentation = new object(),
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["messageMeta"] = nestedProperties,
                ["nullable"] = null
            }
        };
    }

    private static JsonElement ReadSchemaFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("DotCraft.Tests.ModelHistorySchemaV1.json")
            ?? throw new InvalidOperationException("Embedded model-history schema fixture was not found.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private sealed class PassiveChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
