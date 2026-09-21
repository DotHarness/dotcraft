using DotCraft.Agents;
using DotCraft.Persistence;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed partial class OpenAIResponsesToolSearchChatClientTests
{
    [Theory]
    [InlineData(false, null, 20)]
    [InlineData(false, 0, 20)]
    [InlineData(false, 30, 0)]
    [InlineData(false, 30, 20)]
    [InlineData(true, null, 20)]
    [InlineData(true, 0, 20)]
    [InlineData(true, 30, 0)]
    [InlineData(true, 30, 20)]
    public async Task CacheWriteUsage_PreservesStreamingAndAggregatedCounts(bool lite, int? write, int cached)
    {
        using var streamingClient = CreateUsageClient(lite, CreateUsageCompletedUpdate(write, cached));
        var updates = await CollectStreamingAsync(streamingClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")]));
        var usage = Assert.Single(updates.SelectMany(update => update.Contents).OfType<UsageContent>());
        Assert.Null(usage.RawRepresentation);
        AssertCacheWriteDetails(usage.Details, write, cached);
        AssertCacheWriteSnapshot(TokenUsageExtractor.FromUsageContent(usage), write, cached);

        using var aggregateClient = CreateUsageClient(lite, CreateUsageCompletedUpdate(write, cached));
        var response = await aggregateClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);
        AssertCacheWriteDetails(Assert.IsType<UsageDetails>(response.Usage), write, cached);
        AssertCacheWriteSnapshot(TokenUsageExtractor.FromResponse(response), write, cached);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CacheWriteUsage_TracesAndPersistsEachContinuationOnce(bool lite)
    {
        var root = Path.Combine(Path.GetTempPath(), "responses-usage-tests", Guid.NewGuid().ToString("N"));
        var sessionKey = "cache-write-session";
        var previousSessionKey = TracingChatClient.CurrentSessionKey;
        try
        {
            using var database = new WorkspaceStateDatabase(root);
            var store = new TraceStore(database, 5000, synchronousPersist: true);
            var collector = new TraceCollector(store);
            using var provider = CreateUsageClient(lite,
                [CreateFunctionCallDoneUpdate(0, 0, "fc-inventory", "call-inventory", "inventory"),
                    CreateUsageCompletedUpdate(30, 20)],
                [CreateUsageCompletedUpdate(10, 20)]);
            using var client = new TracingChatClient(new StreamingFunctionInvokingChatClient(provider), collector);
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = sessionKey;

            var executions = 0;
            var inventory = AIFunctionFactory.Create(() =>
            {
                executions++;
                return "stock=7";
            }, name: "inventory");
            await CollectStreamingAsync(client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "Read the synthetic inventory.")],
                new ChatOptions { Tools = [inventory] }));
            Assert.Equal(1, executions);
            store.WaitForPendingPersistence();

            var reader = new TraceStore(database, 5000, synchronousPersist: true);
            var events = reader.GetEvents(sessionKey).Where(evt => evt.Type == TraceEventType.TokenUsage).ToArray();
            Assert.Equal(2, events.Length);
            Assert.Equal(30, events[0].CacheWriteInputTokens);
            Assert.Equal(50, events[0].FreshInputTokens);
            Assert.Equal(10, events[1].CacheWriteInputTokens);
            Assert.Equal(70, events[1].FreshInputTokens);
            var session = Assert.IsType<TraceSession>(reader.GetSession(sessionKey));
            Assert.Equal(200, session.TotalInputTokens);
            Assert.Equal(40, session.TotalCachedInputTokens);
            Assert.Equal(40, session.TotalCacheWriteInputTokens);
            Assert.Equal(120, session.TotalFreshInputTokens);
            Assert.Equal(40, reader.GetSummary().TotalCacheWriteInputTokens);
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = previousSessionKey;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertCacheWriteDetails(UsageDetails details, int? write, int cached)
    {
        Assert.Equal(100, details.InputTokenCount);
        Assert.Equal(10, details.OutputTokenCount);
        Assert.Equal(110, details.TotalTokenCount);
        Assert.Equal(cached, details.CachedInputTokenCount);
        Assert.Equal(2, details.ReasoningTokenCount);
        Assert.Equal(write ?? 0, details.AdditionalCounts!["CacheWriteInputTokenCount"]);
    }

    private static void AssertCacheWriteSnapshot(TokenUsageSnapshot usage, int? write, int cached)
    {
        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(10, usage.OutputTokens);
        Assert.Equal(110, usage.TotalTokens);
        Assert.Equal(cached, usage.CachedInputTokens);
        Assert.Equal(2, usage.ReasoningOutputTokens);
        Assert.Equal(write ?? 0, usage.CacheWriteInputTokens);
        Assert.Equal(100 - cached - (write ?? 0), usage.FreshInputTokens);
    }

    private static StreamingResponseUpdate CreateUsageCompletedUpdate(int? write, int cached)
    {
        var writeField = write.HasValue ? $",\"cache_write_tokens\":{write.Value}" : string.Empty;
        return CreateStreamingUpdate($$$"""
            {"type":"response.completed","sequence_number":1,"response":{
              "id":"resp-usage","object":"response","created_at":0,"model":"gpt-test",
              "status":"completed","output":[],
              "usage":{"input_tokens":100,"output_tokens":10,"total_tokens":110,
                "input_tokens_details":{"cached_tokens":{{{cached}}}{{{writeField}}}},
                "output_tokens_details":{"reasoning_tokens":2}}
            }}
            """);
    }

    private static IChatClient CreateUsageClient(bool lite, params StreamingResponseUpdate[] updates) =>
        CreateUsageClient(lite, new[] { updates });

    private static IChatClient CreateUsageClient(bool lite, params StreamingResponseUpdate[][] responses)
    {
        var inner = new FakeChatClient(new ChatResponse([new ChatMessage(ChatRole.Assistant, "unused")]));
        var transport = new FakeToolSearchTransport(responses);
        return lite
            ? new OpenAIResponsesLiteChatClient(new ResponsesClient("sk-test"), "gpt-test", inner,
                new UsageLiteTransport(transport), "test-installation")
            : CreateClient(inner, transport);
    }

    private sealed class UsageLiteTransport(FakeToolSearchTransport transport) : IResponsesLiteTransport
    {
        public IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
            BinaryData wireBody, CancellationToken cancellationToken) =>
            transport.CreateResponseStreamingAsync(new CreateResponseOptions(), cancellationToken);
    }
}
