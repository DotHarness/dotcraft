using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace DotCraft.Core.Tests.Agents;

public sealed class OpenAIResponsesProviderHistoryTests
{
    [Fact]
    public async Task CanonicalHistory_PreservesProviderItemsAcrossToolLoopAndColdResume()
    {
        var records = new List<ThreadRolloutRecord>();
        var turnOne = CreateTurn("turn_001");
        var identity = CreateIdentity(turnOne);
        var context = CreateContext(
            identity,
            ProviderHistorySnapshot.Empty("window_1"),
            coveredMessageCount: 0,
            records);

        var firstMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "inspect")
        };
        var first = await context.PrepareInputAsync(firstMessages, options: null, CancellationToken.None);
        var firstPrefix = ItemJson(first.Input);

        var attemptId = context.BeginAttempt();
        await context.AppendProviderOutputAsync(
            ReadResponseItem(
                """
                {
                  "type": "reasoning",
                  "id": "rs_provider",
                  "summary": [{"type":"summary_text","text":"checking"}],
                  "content": [],
                  "encrypted_content": "encrypted-provider-bytes"
                }
                """),
            outputIndex: 0,
            sequenceNumber: 10,
            CancellationToken.None);
        await context.AppendProviderOutputAsync(
            ReadResponseItem(
                """
                {
                  "type": "function_call",
                  "id": "fc_provider",
                  "call_id": "call_1",
                  "name": "read_file",
                  "arguments": "{\"path\":\"a.txt\"}",
                  "status": "completed"
                }
                """),
            outputIndex: 1,
            sequenceNumber: 11,
            CancellationToken.None);
        context.EndAttempt(attemptId);

        var assistantProjection = new ChatMessage(
            ChatRole.Assistant,
            [
                new TextReasoningContent("checking")
                {
                    ProtectedData = "encrypted-provider-bytes"
                },
                new FunctionCallContent("call_1", "read_file", new Dictionary<string, object?>())
            ]);
        context.MarkProjectionCovered([.. firstMessages, assistantProjection]);

        var secondMessages = new List<ChatMessage>(firstMessages)
        {
            assistantProjection,
            new(
                ChatRole.Tool,
                [new FunctionResultContent("call_1", "file contents")])
        };
        var second = await context.PrepareInputAsync(secondMessages, options: null, CancellationToken.None);
        Assert.Equal(firstPrefix, ItemJson(second.Input).Take(firstPrefix.Count));
        Assert.Equal(
            ["message", "reasoning", "function_call", "function_call_output"],
            second.Input.Select(ReadType));
        Assert.Equal(
            "encrypted-provider-bytes",
            second.Input[1]!["encrypted_content"]!.GetValue<string>());
        Assert.Equal("rs_provider", second.Input[1]!["id"]!.GetValue<string>());

        var snapshot = ProviderHistoryReplayer.Replay(
            "thread_test",
            "window_1",
            new HashSet<string>(["turn_001"], StringComparer.Ordinal),
            records);
        var turnTwo = CreateTurn("turn_002");
        var resumed = CreateContext(
            CreateIdentity(turnTwo),
            snapshot,
            coveredMessageCount: secondMessages.Count,
            records: []);
        var thirdMessages = new List<ChatMessage>(secondMessages)
        {
            new(ChatRole.User, "inspect")
        };
        var third = await resumed.PrepareInputAsync(thirdMessages, options: null, CancellationToken.None);
        Assert.Equal(ItemJson(second.Input), ItemJson(third.Input).Take(second.Input.Count));
        Assert.Equal("message", ReadType(third.Input[^1]));
        Assert.NotEqual(
            third.Input[0]!["id"]!.GetValue<string>(),
            third.Input[^1]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task AbortedRetryAttempt_RemovesCompletedProviderItemsFromReplay()
    {
        var records = new List<ThreadRolloutRecord>();
        var turn = CreateTurn("turn_001");
        var context = CreateContext(
            CreateIdentity(turn),
            ProviderHistorySnapshot.Empty("window_1"),
            coveredMessageCount: 0,
            records);
        await context.PrepareInputAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options: null,
            CancellationToken.None);

        var attemptId = context.BeginAttempt();
        await context.AppendProviderOutputAsync(
            ReadResponseItem(
                """
                {
                  "type": "reasoning",
                  "id": "rs_discarded",
                  "summary": [],
                  "content": [],
                  "encrypted_content": "discard-me"
                }
                """),
            outputIndex: 0,
            sequenceNumber: 1,
            CancellationToken.None);
        await context.AbortAttemptAsync(attemptId, CancellationToken.None);

        var snapshot = ProviderHistoryReplayer.Replay(
            "thread_test",
            "window_1",
            new HashSet<string>(["turn_001"], StringComparer.Ordinal),
            records);
        var item = Assert.Single(snapshot.Entries).Item;
        Assert.Equal("message", item.GetProperty("type").GetString());
    }

    [Fact]
    public async Task MissingToolOutput_IsNormalizedRequestLocallyWithStableId()
    {
        var context = CreateContext(
            CreateIdentity(CreateTurn("turn_001")),
            ProviderHistorySnapshot.Empty("window_1"),
            coveredMessageCount: 0,
            records: []);
        await context.PrepareInputAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options: null,
            CancellationToken.None);
        context.BeginAttempt();
        await context.AppendProviderOutputAsync(
            ReadResponseItem(
                """
                {
                  "type": "function_call",
                  "id": "fc_unfinished",
                  "call_id": "call_unfinished",
                  "name": "read_file",
                  "arguments": "{}",
                  "status": "completed"
                }
                """),
            outputIndex: 0,
            sequenceNumber: 1,
            CancellationToken.None);

        context.MarkProjectionCovered(
            [
                new ChatMessage(ChatRole.User, "hello"),
                new(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call_unfinished", "read_file", new Dictionary<string, object?>())])
            ]);
        var first = await context.PrepareInputAsync(
            [
                new ChatMessage(ChatRole.User, "hello"),
                new(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call_unfinished", "read_file", new Dictionary<string, object?>())])
            ],
            options: null,
            CancellationToken.None);
        var second = await context.PrepareInputAsync(
            [
                new ChatMessage(ChatRole.User, "hello"),
                new(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call_unfinished", "read_file", new Dictionary<string, object?>())])
            ],
            options: null,
            CancellationToken.None);

        Assert.Equal(
            ["message", "function_call", "function_call_output"],
            first.Input.Select(ReadType));
        Assert.Equal(
            first.Input[2]!["id"]!.GetValue<string>(),
            second.Input[2]!["id"]!.GetValue<string>());
        Assert.Equal("aborted", first.Input[2]!["output"]!.GetValue<string>());
    }

    [Fact]
    public async Task ThreadOpenedCapability_RoundTripsWithoutChangingPublicMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-provider-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ThreadRolloutStore? store = null;
        try
        {
            store = new ThreadRolloutStore(root);
            var thread = new SessionThread
            {
                Id = "thread_test",
                WorkspacePath = root,
                OriginChannel = "test",
                Status = ThreadStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow,
                ProviderHistorySchemaVersion = ProviderHistorySchema.CurrentSchemaVersion
            };
            await store.SaveThreadAsync(thread, previous: null);

            var loaded = Assert.IsType<SessionThread>(await store.LoadThreadAsync(thread.Id));
            Assert.Equal(ProviderHistorySchema.CurrentSchemaVersion, loaded.ProviderHistorySchemaVersion);
            Assert.DoesNotContain(
                "providerHistorySchemaVersion",
                JsonSerializer.Serialize(thread, SessionJsonOptions.Default),
                StringComparison.Ordinal);
        }
        finally
        {
            if (store != null)
                await store.ShutdownAsync();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadProviderHistory_WhenCheckpointOutrunsReplacement_ForcesRecoverableRebuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-provider-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var store = new ThreadStore(root);
            var turn = CreateTurn("turn_001");
            turn.Status = TurnStatus.Completed;
            var thread = new SessionThread
            {
                Id = "thread_test",
                WorkspacePath = root,
                OriginChannel = "test",
                Status = ThreadStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow,
                ProviderHistorySchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
                Turns = [turn]
            };
            await store.SaveThreadAsync(thread);
            await store.ReplaceProviderHistoryAsync(
                new ProviderHistoryReplacedPayload
                {
                    SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
                    ThreadId = thread.Id,
                    Protocol = ProviderHistorySchema.OpenAIResponsesProtocol,
                    GenerationId = "window_1",
                    ContextWindowId = "window_1",
                    CoveredThroughTurnId = turn.Id,
                    Reason = "seed",
                    Entries =
                    [
                        new ProviderHistoryEntry
                        {
                            EntryId = "entry_1",
                            Item = JsonDocument.Parse(
                                """{"type":"message","role":"user","content":[{"type":"input_text","text":"old"}]}""")
                                .RootElement.Clone()
                        }
                    ]
                });
            await store.AppendCompactionCheckpointAsync(
                thread.Id,
                turn.Id,
                [new ChatMessage(ChatRole.Assistant, "compacted")],
                "manual",
                "partial",
                tokensBefore: 100,
                tokensAfter: 10);

            var snapshot = await store.LoadProviderHistoryAsync(thread, "window_1");

            Assert.Empty(snapshot.Entries);
            Assert.Null(snapshot.CoveredThroughTurnId);
            Assert.Equal("window_1", snapshot.ContextWindowId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StreamRetry_AbortsCanonicalItemsFromDiscardedAttempt()
    {
        var records = new List<ThreadRolloutRecord>();
        var context = CreateContext(
            CreateIdentity(CreateTurn("turn_001")),
            ProviderHistorySnapshot.Empty("window_1"),
            coveredMessageCount: 0,
            records);
        using var scope = OpenAIResponsesProviderHistoryRuntimeScope.Set(context);
        using var client = new StreamRetryingChatClient(
            new ProviderHistoryRetryClient(context),
            new StreamRetryOptions(
                MaxRetries: 1,
                IdleTimeout: TimeSpan.FromSeconds(30)));

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "retry")]))
        {
            updates.Add(update);
        }

        Assert.Equal("ok", Assert.Single(updates).Text);
        Assert.Contains(records, record => record.ProviderHistoryAttemptAborted != null);
        var snapshot = ProviderHistoryReplayer.Replay(
            "thread_test",
            "window_1",
            new HashSet<string>(["turn_001"], StringComparer.Ordinal),
            records);
        Assert.Empty(snapshot.Entries);
    }

    [Fact]
    public async Task StreamRetry_AbortsCanonicalItemsWhenConsumerStopsEarly()
    {
        var records = new List<ThreadRolloutRecord>();
        var context = CreateContext(
            CreateIdentity(CreateTurn("turn_001")),
            ProviderHistorySnapshot.Empty("window_1"),
            coveredMessageCount: 0,
            records);
        using var scope = OpenAIResponsesProviderHistoryRuntimeScope.Set(context);
        using var client = new StreamRetryingChatClient(
            new ProviderHistoryEarlyDisposeClient(context),
            new StreamRetryOptions(
                MaxRetries: 0,
                IdleTimeout: TimeSpan.FromSeconds(30)));

        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "stop early")]))
        {
            Assert.Equal("partial", update.Text);
            break;
        }

        Assert.Contains(records, record => record.ProviderHistoryAttemptAborted != null);
        var snapshot = ProviderHistoryReplayer.Replay(
            "thread_test",
            "window_1",
            new HashSet<string>(["turn_001"], StringComparer.Ordinal),
            records);
        Assert.Empty(snapshot.Entries);
    }

    [Fact]
    public async Task ResponsesAdapter_CapturesRawCompletedItemBeforeMeaiProjection()
    {
        var records = new List<ThreadRolloutRecord>();
        var context = CreateContext(
            CreateIdentity(CreateTurn("turn_001")),
            ProviderHistorySnapshot.Empty("window_1"),
            coveredMessageCount: 0,
            records);
        var transport = new CapturingResponsesTransport(
            ReadStreamingUpdate(
                """
                {
                  "type": "response.output_item.done",
                  "sequence_number": 1,
                  "output_index": 0,
                  "item": {
                    "type": "reasoning",
                    "id": "rs_raw",
                    "summary": [{"type":"summary_text","text":"raw summary"}],
                    "content": [],
                    "encrypted_content": "raw-encrypted"
                  }
                }
                """));
        using var adapter = new OpenAIResponsesToolSearchChatClient(
            new ResponsesClient("sk-test"),
            "gpt-test",
            new NoopChatClient(),
            transport);
        using var scope = OpenAIResponsesProviderHistoryRuntimeScope.Set(context);

        await foreach (var _ in adapter.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hello")]))
        {
        }

        var providerAppend = Assert.Single(
            records,
            record => string.Equals(
                record.ProviderHistoryItemsAppended?.Source,
                ProviderHistorySources.ProviderOutput,
                StringComparison.Ordinal));
        var item = Assert.Single(providerAppend.ProviderHistoryItemsAppended!.Entries).Item;
        Assert.Equal("rs_raw", item.GetProperty("id").GetString());
        Assert.Equal("raw-encrypted", item.GetProperty("encrypted_content").GetString());
        Assert.Single(transport.Requests);
    }

    [Fact]
    public void Replay_MalformedActiveRecordFailsWithStableError()
    {
        var record = new ThreadRolloutRecord
        {
            Kind = "provider_history_items_appended",
            ProviderHistoryItemsAppended = new ProviderHistoryItemsAppendedPayload
            {
                SchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
                ThreadId = "thread_test",
                TurnId = "turn_001",
                Protocol = ProviderHistorySchema.OpenAIResponsesProtocol,
                GenerationId = "window_1",
                ContextWindowId = "window_1",
                Source = ProviderHistorySources.LocalInput,
                Entries =
                [
                    new ProviderHistoryEntry
                    {
                        EntryId = string.Empty,
                        Item = JsonDocument.Parse("""{"type":"message"}""").RootElement.Clone()
                    }
                ]
            }
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            ProviderHistoryReplayer.Replay(
                "thread_test",
                "window_1",
                new HashSet<string>(["turn_001"], StringComparer.Ordinal),
                [record]));
        Assert.Contains("responses_provider_history_corrupt", error.Message, StringComparison.Ordinal);
    }

    private static OpenAIResponsesProviderHistoryContext CreateContext(
        ThreadConversationIdentity identity,
        ProviderHistorySnapshot snapshot,
        int coveredMessageCount,
        List<ThreadRolloutRecord> records) =>
        new(
            identity,
            snapshot,
            coveredMessageCount,
            (payload, _) =>
            {
                records.Add(new ThreadRolloutRecord
                {
                    Kind = "provider_history_items_appended",
                    ProviderHistoryItemsAppended = payload
                });
                return Task.CompletedTask;
            },
            (payload, _) =>
            {
                records.Add(new ThreadRolloutRecord
                {
                    Kind = "provider_history_replaced",
                    ProviderHistoryReplaced = payload
                });
                return Task.CompletedTask;
            },
            (payload, _) =>
            {
                records.Add(new ThreadRolloutRecord
                {
                    Kind = "provider_history_attempt_aborted",
                    ProviderHistoryAttemptAborted = payload
                });
                return Task.CompletedTask;
            });

    private static SessionTurn CreateTurn(string id) =>
        new()
        {
            Id = id,
            ThreadId = "thread_test",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

    private static ThreadConversationIdentity CreateIdentity(SessionTurn turn) =>
        ThreadConversationIdentity.Create(
            new SessionThread
            {
                Id = "thread_test",
                Source = ThreadSource.User()
            },
            turn,
            "window_1",
            ThreadConversationRequestKind.Turn);

    private static ResponseItem ReadResponseItem(string json) =>
        ModelReaderWriter.Read<ResponseItem>(
            BinaryData.FromString(json),
            ModelReaderWriterOptions.Json)!;

    private static StreamingResponseUpdate ReadStreamingUpdate(string json) =>
        ModelReaderWriter.Read<StreamingResponseUpdate>(
            BinaryData.FromString(json),
            ModelReaderWriterOptions.Json)!;

    private static List<string> ItemJson(IEnumerable<JsonNode?> items) =>
        items.Select(item => item!.ToJsonString()).ToList();

    private static string? ReadType(JsonNode? item) =>
        item?["type"]?.GetValue<string>();

    private sealed class ProviderHistoryRetryClient(
        OpenAIResponsesProviderHistoryContext context) : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await context.AppendProviderOutputAsync(
                    ReadResponseItem(
                        """
                        {
                          "type": "reasoning",
                          "id": "rs_retry",
                          "summary": [],
                          "content": [],
                          "encrypted_content": "discard-me"
                        }
                        """),
                    outputIndex: 0,
                    sequenceNumber: 1,
                    cancellationToken);
                throw new IOException("response ended prematurely");
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IProviderConversationHistoryBridge)
                ? OpenAIResponsesProviderHistoryBridge.Instance
                : serviceType.IsInstanceOfType(this)
                    ? this
                    : null;

        public void Dispose()
        {
        }
    }

    private sealed class ProviderHistoryEarlyDisposeClient(
        OpenAIResponsesProviderHistoryContext context) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await context.AppendProviderOutputAsync(
                ReadResponseItem(
                    """
                    {
                      "type": "reasoning",
                      "id": "rs_partial",
                      "summary": [],
                      "content": [],
                      "encrypted_content": "discard-me"
                    }
                    """),
                outputIndex: 0,
                sequenceNumber: 1,
                cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "unobserved");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IProviderConversationHistoryBridge)
                ? OpenAIResponsesProviderHistoryBridge.Instance
                : serviceType.IsInstanceOfType(this)
                    ? this
                    : null;

        public void Dispose()
        {
        }
    }

    private sealed class CapturingResponsesTransport(
        params StreamingResponseUpdate[] updates) : IResponsesToolSearchTransport
    {
        public List<CreateResponseOptions> Requests { get; } = [];

        public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            CreateResponseOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(options);
            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
            }
            await Task.CompletedTask;
        }
    }

    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
