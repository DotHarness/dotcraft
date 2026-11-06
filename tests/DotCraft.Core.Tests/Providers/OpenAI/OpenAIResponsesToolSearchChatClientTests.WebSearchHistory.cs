using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIResponsesToolSearchChatClientTests
{
    [Fact]
    public async Task WebSearchReplay_NeutralReplacementRebuildsOneNativeItemFromPersistedProjection()
    {
        var native = NativeWebSearch("ws_replacement", "{\"type\":\"search\",\"query\":\"weather\"}");
        var transport = new FakeToolSearchTransport(WebSearchEvents(native, 0, true));
        using var adapter = CreateClient(new FakeChatClient(new ChatResponse([])), transport);
        var records = new List<ThreadRolloutRecord>();
        var context = CreateWebSearchHistory(records);
        using var scope = OpenAIResponsesProviderHistoryRuntimeScope.Set(context);
        var user = new ChatMessage(ChatRole.User, "search");
        var response = await adapter.GetResponseAsync([user]);
        var projection = new[] { user }.Concat(response.Messages)
            .Select(message => ModelHistoryJson.Decode(ModelHistoryJson.Encode(message))).ToList();

        await context.ReplaceAsync(projection, options: null, "neutral_compaction", CancellationToken.None);
        await adapter.GetResponseAsync([.. projection, new ChatMessage(ChatRole.User, "continue")]);

        using var request = JsonDocument.Parse(SerializeOptions(transport.Requests[1]));
        var replay = Assert.Single(request.RootElement.GetProperty("input").EnumerateArray(),
            item => item.GetProperty("type").GetString() == "web_search_call");
        AssertWebSearchReplay(native, replay);
        Assert.Single(records, record => record.ProviderHistoryReplaced is not null);
    }

    [Fact]
    public async Task WebSearchReplay_DiscardedStreamAttemptDoesNotReappearInNextRequestOrRolloutReplay()
    {
        var native = NativeWebSearch("ws_discarded", "{\"type\":\"search\",\"query\":\"weather\"}");
        var transport = new FakeToolSearchTransport(WebSearchEvents(native, 0, false));
        using var adapter = CreateClient(new FakeChatClient(new ChatResponse([])), transport);
        using var retrying = new StreamRetryingChatClient(adapter, new StreamRetryOptions(0, TimeSpan.FromSeconds(30)));
        var records = new List<ThreadRolloutRecord>();
        var context = CreateWebSearchHistory(records);
        using var scope = OpenAIResponsesProviderHistoryRuntimeScope.Set(context);
        var user = new ChatMessage(ChatRole.User, "search");

        await foreach (var update in retrying.GetStreamingResponseAsync([user]))
        {
            if (update.Contents.OfType<WebSearchToolResultContent>().Any())
                break;
        }

        Assert.Contains(records, record => record.ProviderHistoryItemsAppended?.Entries.Any(
            entry => entry.Item.GetProperty("type").GetString() == "web_search_call") == true);
        Assert.Single(records, record => record.ProviderHistoryAttemptAborted is not null);
        var recovered = ProviderHistoryReplayer.Replay("thread_search", "window_search",
            new HashSet<string>(["turn_search"], StringComparer.Ordinal), records);
        Assert.DoesNotContain(recovered.Entries,
            entry => entry.Item.GetProperty("type").GetString() == "web_search_call");

        await retrying.GetResponseAsync([user]);
        using var request = JsonDocument.Parse(SerializeOptions(transport.Requests[1]));
        Assert.DoesNotContain(request.RootElement.GetProperty("input").EnumerateArray(),
            item => item.GetProperty("type").GetString() is "web_search_call" or "function_call_output");
    }

    private static OpenAIResponsesProviderHistoryContext CreateWebSearchHistory(List<ThreadRolloutRecord> records)
    {
        var identity = ThreadConversationIdentity.Create(
            new SessionThread { Id = "thread_search" },
            new SessionTurn { Id = "turn_search", ThreadId = "thread_search", Status = TurnStatus.Running },
            "window_search", ProviderRequestKind.Turn);
        return new OpenAIResponsesProviderHistoryContext(identity, "openai",
            ProviderHistorySnapshot.Empty("window_search"), [],
            (payload, _) =>
            {
                records.Add(new ThreadRolloutRecord
                {
                    Kind = RolloutKinds.ProviderHistoryItemsAppended, ProviderHistoryItemsAppended = payload
                });
                return Task.CompletedTask;
            },
            (payload, _) =>
            {
                records.Add(new ThreadRolloutRecord
                {
                    Kind = RolloutKinds.ProviderHistoryReplaced, ProviderHistoryReplaced = payload
                });
                return Task.CompletedTask;
            },
            (payload, _) =>
            {
                records.Add(new ThreadRolloutRecord
                {
                    Kind = RolloutKinds.ProviderHistoryAttemptAborted, ProviderHistoryAttemptAborted = payload
                });
                return Task.CompletedTask;
            });
    }
}
