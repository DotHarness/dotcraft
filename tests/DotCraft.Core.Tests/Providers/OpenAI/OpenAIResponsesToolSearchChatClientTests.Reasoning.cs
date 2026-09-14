using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIResponsesToolSearchChatClientTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task ReasoningReplay_PreservesNativeFieldsThroughStreamingAndPersistence(
        bool plain, bool summary, bool encrypted)
    {
        var native = ReasoningItem("rs_replay", plain, summary, encrypted);
        var transport = new FakeToolSearchTransport(ReasoningEvents(native, 0));
        using var client = CreateClient(new FakeChatClient(new ChatResponse([])), transport);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "inspect")]);
        var content = Assert.Single(Assert.Single(response.Messages).Contents.OfType<TextReasoningContent>());
        Assert.Equal((summary ? "brief" : "") + (plain ? "checking inputs" : ""), content.Text);
        Assert.Equal(encrypted ? "opaque" : null, content.ProtectedData);

        var codec = new ModelHistoryCodec();
        var restored = response.Messages.Select(message => codec.Decode(codec.Encode(message, "turn_reasoning")));
        using var request = JsonDocument.Parse(CreateRequestJson("deepseek-test", restored, null));
        var replay = Assert.Single(request.RootElement.GetProperty("input").EnumerateArray());
        AssertReplay(native, replay);
        Assert.DoesNotContain(ResponsesReasoningMetadata.Key, request.RootElement.GetRawText());
        Assert.DoesNotContain(AgentResponseAggregation.ReasoningGroupKey, request.RootElement.GetRawText());
    }

    [Fact]
    public async Task ReasoningReplay_LegacyNativeTextPartKeepsItsOriginalType()
    {
        var native = ReasoningItem("rs_legacy_text", true, false, true);
        native["content"]![0]!["type"] = "text";
        using var client = CreateClient(new FakeChatClient(new ChatResponse([])),
            new FakeToolSearchTransport(ReasoningEvents(native, 0)));
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "inspect")]);
        var codec = new ModelHistoryCodec();
        using var request = JsonDocument.Parse(CreateRequestJson("test",
            response.Messages.Select(message => codec.Decode(codec.Encode(message, "legacy"))), null));
        AssertReplay(native, Assert.Single(request.RootElement.GetProperty("input").EnumerateArray()));
    }

    [Fact]
    public async Task ReasoningReplay_AdjacentUnencryptedItemsStaySeparate()
    {
        var first = ReasoningItem("rs_first", true, false, false);
        var second = ReasoningItem("rs_second", false, true, false);
        var transport = new FakeToolSearchTransport([
            .. ReasoningEvents(first, 0), .. ReasoningEvents(second, 1)
        ]);
        using var client = CreateClient(new FakeChatClient(new ChatResponse([])), transport);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "inspect")]);
        var message = Assert.Single(response.Messages);
        Assert.Equal(2, message.Contents.OfType<TextReasoningContent>().Count());
        var codec = new ModelHistoryCodec();
        using var request = JsonDocument.Parse(CreateRequestJson("deepseek-test",
            [codec.Decode(codec.Encode(message, "turn_adjacent"))], null));
        var items = request.RootElement.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        AssertReplay(first, items[0]);
        AssertReplay(second, items[1]);
    }

    [Fact]
    public async Task ReasoningReplay_CompletionWithoutDeltasEmitsTextOnce()
    {
        var native = ReasoningItem("rs_done", true, false, false);
        var transport = new FakeToolSearchTransport(ReasoningEvents(native, 0).Last());
        using var client = CreateClient(new FakeChatClient(new ChatResponse([])), transport);
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "inspect")]);
        Assert.Equal("checking inputs", Assert.Single(Assert.Single(response.Messages).Contents
            .OfType<TextReasoningContent>()).Text);
        using var request = JsonDocument.Parse(CreateRequestJson("deepseek-test", response.Messages, null));
        AssertReplay(native, Assert.Single(request.RootElement.GetProperty("input").EnumerateArray()));
    }

    [Theory]
    [InlineData("{\"version\":2,\"item\":{\"type\":\"reasoning\"}}")]
    [InlineData("{\"version\":1,\"item\":{\"type\":\"reasoning\",\"content\":\"secret\"}}")]
    [InlineData("{\"version\":1,\"item\":{\"type\":\"reasoning\",\"content\":[{\"type\":\"summary_text\",\"text\":\"secret\"}]}}")]
    [InlineData("null")]
    public void ReasoningReplay_InvalidMetadataDoesNotFallBackToEncryptedContent(string metadata)
    {
        var reasoning = new TextReasoningContent("secret")
        {
            ProtectedData = "secret-encrypted",
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ResponsesReasoningMetadata.Key] = JsonSerializer.Deserialize<JsonElement>(metadata)
            }
        };
        var ex = Assert.Throws<InvalidDataException>(() => CreateRequestJson("deepseek-test",
            [new ChatMessage(ChatRole.Assistant, [reasoning])], null));
        Assert.StartsWith("responses_reasoning_metadata_invalid:", ex.Message);
        Assert.DoesNotContain("secret", ex.ToString());
    }

    [Fact]
    public void ReasoningReplay_LegacyTextIsNotGuessedToBePlainReasoning()
    {
        using var request = JsonDocument.Parse(CreateRequestJson("test",
            [new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("unknown source")
            {
                ProtectedData = "legacy-encrypted"
            }])], null));
        var item = Assert.Single(request.RootElement.GetProperty("input").EnumerateArray());
        Assert.Empty(item.GetProperty("content").EnumerateArray());
        Assert.Equal("legacy-encrypted", item.GetProperty("encrypted_content").GetString());
    }

    [Fact]
    public async Task ReasoningReplay_ToolLoopContinuesAfterPartialCompactionAndColdRebuild()
    {
        var native = ReasoningItem("rs_tool", true, false, false);
        var transport = new FakeToolSearchTransport([
            [.. ReasoningEvents(native, 0), CreateFunctionCallDoneUpdate(20, 1, "fc_lookup", "call_lookup", "lookup")],
            [new StreamingResponseOutputTextDeltaUpdate
                { SequenceNumber = 21, ItemId = "msg_final", OutputIndex = 0, ContentIndex = 0, Delta = "done" }]
        ]);
        var server = new ReasoningRequiredTransport(transport);
        using var client = CreateClient(new FakeChatClient(new ChatResponse([])), server);
        using var loop = new StreamingFunctionInvokingChatClient(client);
        var identity = new ProviderConversationIdentity("thread_reasoning", "thread_reasoning", null, null,
            "turn_reasoning", "window_reasoning", ProviderRequestKind.Turn, 0, "user", null);
        var replacements = new List<ProviderHistoryReplacedPayload>();
        var history = new OpenAIResponsesProviderHistoryContext(identity, "deepseek",
            ProviderHistorySnapshot.Empty(identity.ContextWindowId), [], null,
            (payload, _) => { replacements.Add(payload); return Task.CompletedTask; }, null);
        using var historyScope = OpenAIResponsesProviderHistoryRuntimeScope.Set(history);
        var codec = new ModelHistoryCodec();
        var compacted = false;
        using var preparation = StreamingSamplingRuntimeScope.Set(async (messages, _, ct) =>
        {
            if (!messages.Any(message => message.Contents.OfType<FunctionResultContent>()
                    .Any(result => result.CallId == "call_lookup")))
                return new StreamingSamplingPreparation(messages, false, false);

            var compactor = new PartialCompactor(
                new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "earlier context")])),
                new CompactionConfig { KeepRecentMinTokens = 1, KeepRecentMinGroups = 1, KeepRecentMaxTokens = 100_000 });
            var attempt = await compactor.CompactAsync(messages, ct);
            Assert.NotNull(attempt.Result);
            var compactHistory = new List<ChatMessage> { CompactionSummaryMessage.Create(attempt.Result!.FormattedSummary) };
            compactHistory.AddRange(attempt.Result.PreservedTail);
            var replacement = compactHistory
                .Select(message => codec.Decode(codec.Encode(message, "turn_compacted"))).ToArray();
            compacted = true;
            return new StreamingSamplingPreparation(replacement, true, true)
                { NeutralHistoryReplacement = replacement };
        });
        var tool = AIFunctionFactory.Create(() => "value", name: "lookup");
        var response = await loop.GetResponseAsync([
            new ChatMessage(ChatRole.User, "old question"),
            new ChatMessage(ChatRole.Assistant, "old answer"),
            new ChatMessage(ChatRole.User, "look up value")
        ], new ChatOptions { Tools = [tool] });

        Assert.True(compacted);
        Assert.Equal("compaction", Assert.Single(replacements).Reason);
        Assert.Contains("done", response.Text);
        using var request = JsonDocument.Parse(server.Requests[1]);
        var input = request.RootElement.GetProperty("input").EnumerateArray().ToArray();
        var reasoning = Assert.Single(input, item => item.GetProperty("type").GetString() == "reasoning");
        AssertReplay(native, reasoning);
        Assert.Single(input, item => item.GetProperty("type").GetString() == "function_call");
        Assert.Single(input, item => item.GetProperty("type").GetString() == "function_call_output");
    }

    [Fact]
    public async Task ReasoningReplay_DisposedStreamDoesNotLeakIntoNextRequest()
    {
        var original = ReasoningItem("rs_original", true, false, false);
        var replacement = ReasoningItem("rs_replacement", false, true, true);
        var transport = new FakeToolSearchTransport([ReasoningEvents(original, 0), ReasoningEvents(replacement, 0)]);
        using var client = CreateClient(new FakeChatClient(new ChatResponse([])), transport);
        await using (var stream = client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "first")])
                         .GetAsyncEnumerator())
        {
            Assert.True(await stream.MoveNextAsync());
        }
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "retry")]);
        using var request = JsonDocument.Parse(CreateRequestJson("test", response.Messages, null));
        AssertReplay(replacement, Assert.Single(request.RootElement.GetProperty("input").EnumerateArray()));
    }

    private sealed class ReasoningRequiredTransport(IResponsesToolSearchTransport inner) : IResponsesToolSearchTransport
    {
        public List<string> Requests { get; } = [];
        public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            CreateResponseOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var json = SerializeOptions(options);
            Requests.Add(json);
            using var document = JsonDocument.Parse(json);
            var input = document.RootElement.GetProperty("input").EnumerateArray().ToArray();
            if (input.Length > 0 && input[0].TryGetProperty("role", out var firstRole)
                && firstRole.GetString() == "assistant"
                && input.Any(item => item.GetProperty("type").GetString() == "function_call_output"))
                throw new HttpRequestException("Assistant summary has no thinking-mode boundary", null, System.Net.HttpStatusCode.BadRequest);
            if (input.Any(item => item.GetProperty("type").GetString() == "function_call_output")
                && !input.Any(item => item.GetProperty("type").GetString() == "reasoning"
                    && item.GetProperty("content").EnumerateArray().Any(part =>
                        part.GetProperty("type").GetString() == "reasoning_text")))
                throw new HttpRequestException("reasoning_text must be passed back", null, System.Net.HttpStatusCode.BadRequest);
            await foreach (var update in inner.CreateResponseStreamingAsync(options, cancellationToken))
                yield return update;
        }
    }

    private static JsonObject ReasoningItem(string id, bool plain, bool summary, bool encrypted) => new()
    {
        ["type"] = "reasoning", ["id"] = id,
        ["content"] = plain
            ? new JsonArray(new JsonObject { ["type"] = "reasoning_text", ["text"] = "checking inputs" }) : new JsonArray(),
        ["summary"] = summary
            ? new JsonArray(new JsonObject { ["type"] = "summary_text", ["text"] = "brief" }) : new JsonArray(),
        ["encrypted_content"] = encrypted ? "opaque" : null
    };

    private static StreamingResponseUpdate[] ReasoningEvents(JsonObject item, int outputIndex)
    {
        var updates = new List<StreamingResponseUpdate>();
        if (item["summary"] is JsonArray { Count: > 0 })
            updates.Add(new StreamingResponseReasoningSummaryTextDeltaUpdate
            {
                SequenceNumber = 1, OutputIndex = outputIndex, ItemId = item["id"]!.GetValue<string>(),
                SummaryIndex = 0, Delta = "brief"
            });
        if (item["content"] is JsonArray { Count: > 0 })
        {
            foreach (var delta in new[] { "checking ", "inputs" })
                updates.Add(new StreamingResponseReasoningTextDeltaUpdate
                {
                    SequenceNumber = updates.Count + 2, OutputIndex = outputIndex,
                    ItemId = item["id"]!.GetValue<string>(), ContentIndex = 0, Delta = delta
                });
        }
        updates.Add(CreateStreamingUpdate(new JsonObject
        {
            ["type"] = "response.output_item.done", ["sequence_number"] = 10,
            ["output_index"] = outputIndex, ["item"] = item.DeepClone()
        }.ToJsonString()));
        return updates.ToArray();
    }

    private static void AssertReplay(JsonObject expected, JsonElement actual)
    {
        foreach (var key in new[] { "id", "content", "summary", "encrypted_content" })
        {
            var value = actual.TryGetProperty(key, out var element) ? JsonNode.Parse(element.GetRawText()) : null;
            Assert.True(JsonNode.DeepEquals(expected[key], value), $"Reasoning field {key} changed.");
        }
    }
}
