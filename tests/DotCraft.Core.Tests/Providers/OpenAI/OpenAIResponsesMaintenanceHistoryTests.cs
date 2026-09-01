using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Sessions;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Core.Tests.Agents;

public sealed class OpenAIResponsesMaintenanceHistoryTests
{
    [Theory]
    [InlineData(ProviderRequestKind.Compaction)]
    [InlineData(ProviderRequestKind.Memory)]
    public async Task AuxiliaryRequestUsesExplicitMessagesWithoutMutatingCanonicalHistory(
        ProviderRequestKind requestKind)
    {
        var records = new List<ThreadRolloutRecord>();
        var context = CreateContext(
            CreateIdentity(requestKind),
            [
                new ChatMessage(ChatRole.User, "covered user"),
                new ChatMessage(ChatRole.Assistant, "covered assistant")
            ],
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
                    "id": "rs_maintenance",
                    "summary": [{"type":"summary_text","text":"maintenance reasoning"}],
                    "content": [],
                    "encrypted_content": "maintenance-encrypted"
                  }
                }
                """));
        using var adapter = CreateAdapter(transport);
        using var scope = OpenAIResponsesProviderHistoryRuntimeScope.Set(context);

        await foreach (var _ in adapter.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "explicit maintenance input")]))
        {
        }

        using var requestDocument = JsonDocument.Parse(
            ModelReaderWriter.Write(Assert.Single(transport.Requests)).ToString());
        var explicitItem = Assert.Single(
            requestDocument.RootElement.GetProperty("input").EnumerateArray());
        Assert.Contains("explicit maintenance input", explicitItem.GetRawText(), StringComparison.Ordinal);
        Assert.Empty(records);
    }

    [Fact]
    public async Task LocalSummaryAfterOversizedSnapshotUsesTrimmedInputOutsideCanonicalHistory()
    {
        const string threadId = "thread_test";
        var config = new CompactionConfig
        {
            ContextWindow = 1_000,
            SummaryMaxOutputTokens = 100,
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000
        };
        var messages = BuildMessages();
        var records = new List<ThreadRolloutRecord>();
        var context = CreateContext(CreateIdentity(ProviderRequestKind.Turn), messages, records);
        var transport = new CapturingResponsesTransport(
            new StreamingResponseOutputTextDeltaUpdate
            {
                SequenceNumber = 1,
                ItemId = "msg_summary",
                OutputIndex = 0,
                ContentIndex = 0,
                Delta = "<analysis>thinking</analysis><summary>legacy after preflight</summary>"
            });
        var traceStore = new TraceStore();
        var traceCollector = new TraceCollector(traceStore);
        using var adapter = CreateAdapter(transport, traceCollector);
        var partial = new PartialCompactor(
            adapter,
            config,
            new MaintenanceForkRunner(adapter, traceCollector),
            traceCollector);
        var snapshot = PromptRequestSnapshot.Capture(
            messages,
            new ChatOptions
            {
                Instructions = "stable base",
                ModelId = "gpt-test"
            },
            providerId: "openai",
            mode: "agent",
            threadId: threadId,
            turnId: "turn_001",
            estimatedInputTokens: 10_000);
        using var scope = OpenAIResponsesProviderHistoryRuntimeScope.Set(context);
        using var requestKindScope = ProviderRequestContextScope.Current!.ConversationState!
            .OverrideRequestKind(ProviderRequestKind.Compaction);

        var result = await partial.CompactAsync(messages, snapshot, threadId);

        Assert.NotNull(result.Result);
        Assert.Contains("legacy after preflight", result.Result!.FormattedSummary, StringComparison.Ordinal);
        Assert.Single(transport.Requests);
        Assert.Empty(records);
        var traceEvents = traceStore.GetEvents(threadId);
        Assert.Contains(
            traceEvents,
            traceEvent => traceEvent.Type == TraceEventType.MaintenanceForkResponse
                && traceEvent.MetadataJson?.Contains(
                    MaintenanceForkFallbackReasons.SnapshotTooLarge,
                    StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            traceEvents,
            traceEvent => traceEvent.Content?.Contains(
                    "responses_provider_history_corrupt",
                    StringComparison.Ordinal) == true
                || traceEvent.MetadataJson?.Contains(
                    "responses_provider_history_corrupt",
                    StringComparison.Ordinal) == true);
    }

    private static List<ChatMessage> BuildMessages()
    {
        var messages = new List<ChatMessage>();
        for (var round = 0; round < 4; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }

        return messages;
    }

    private static ProviderConversationIdentity CreateIdentity(ProviderRequestKind requestKind) =>
        new(
            "thread_test",
            "thread_test",
            ParentThreadId: null,
            ForkedFromThreadId: null,
            TurnId: "turn_001",
            ContextWindowId: "window_1",
            requestKind,
            TurnStartedAtUnixMs: 0,
            ThreadSource: "user",
            SubagentKind: null);

    private static OpenAIResponsesProviderHistoryContext CreateContext(
        ProviderConversationIdentity identity,
        IReadOnlyList<ChatMessage> coveredMessages,
        ICollection<ThreadRolloutRecord> records) =>
        new(
            identity,
            "openai",
            ProviderHistorySnapshot.Empty("window_1"),
            coveredMessages,
            (payload, _) =>
            {
                records.Add(new ThreadRolloutRecord
                {
                    Kind = "provider_history_items_appended",
                    ProviderHistoryItemsAppended = payload
                });
                return Task.CompletedTask;
            },
            replaceAsync: null,
            abortAsync: null);

    private static OpenAIResponsesToolSearchChatClient CreateAdapter(
        CapturingResponsesTransport transport,
        TraceCollector? traceCollector = null) =>
        new(
            new ResponsesClient("sk-test"),
            "gpt-test",
            new NoopChatClient(),
            transport,
            traceCollector);

    private static StreamingResponseUpdate ReadStreamingUpdate(string json) =>
        ModelReaderWriter.Read<StreamingResponseUpdate>(
            BinaryData.FromString(json),
            ModelReaderWriterOptions.Json)!;

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
