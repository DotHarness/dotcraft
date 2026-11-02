using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Beta.Messages;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicCompactionReplayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotWithImages_ReplaysOriginalPrefixAndAppendsTask(bool full)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        var history = History();
        var options = new ChatOptions
        {
            Instructions = "Original system instructions",
            Tools = [AIFunctionFactory.Create(() => "ok", name: "Inspect")]
        };
        await client.GetResponseAsync(history, options);
        var original = handler.Requests.Single();
        var snapshot = PromptRequestSnapshot.Capture(history, options);
        var completeHistory = history.Append(new ChatMessage(ChatRole.Assistant, "latest answer")).ToArray();

        if (full)
            Assert.NotNull((await new FullCompactor(client, new MaintenanceForkRunner(client))
                .CompactAsync(completeHistory, snapshot)).Result);
        else
            Assert.NotNull((await new PartialCompactor(client, Config(), new MaintenanceForkRunner(client))
                .CompactAsync(completeHistory, snapshot)).Result);

        var summary = handler.Requests.Last();
        Assert.True(JsonNode.DeepEquals(original["system"], summary["system"]));
        Assert.True(JsonNode.DeepEquals(original["tools"], summary["tools"]));
        var prefix = original["messages"]!.AsArray();
        var replay = summary["messages"]!.AsArray();
        for (var i = 0; i < prefix.Count; i++)
            Assert.True(JsonNode.DeepEquals(prefix[i], replay[i]), $"Prefix changed at message {i}");
        Assert.Equal(prefix.Count + 2, replay.Count);
        Assert.Equal("latest answer", replay[^2]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("user", replay[^1]!["role"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RebuiltSummaryAndContinuation_RemoveBoundThinking(bool full, bool overBudget)
    {
        using var handler = new CaptureHandler { RejectThinking = true };
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        var history = History();
        history.Add(new ChatMessage(ChatRole.Assistant,
        [
            new TextReasoningContent("latest private thinking") { ProtectedData = "latest-signature" },
            new TextContent("latest answer")
        ]));
        history[1].Contents.Add(new AIContent
        {
            RawRepresentation = new BetaContentBlockParam(new BetaThinkingBlockParam
            {
                Thinking = "raw private thinking", Signature = "raw-signature"
            })
        });
        history.Insert(4, new ChatMessage(ChatRole.Assistant,
            [new TextReasoningContent("reasoning only") { ProtectedData = "reasoning-only-signature" }]));
        var before = history.Select(message => message.Contents.ToArray()).ToArray();
        var snapshot = overBudget
            ? PromptRequestSnapshot.Capture(history, new ChatOptions { Instructions = "Original system" }, threadId: "compact-test", estimatedInputTokens: 500_000)
            : null;
        var config = Config();
        var trace = new TraceStore();
        var collector = new TraceCollector(trace);
        var runner = new MaintenanceForkRunner(client, collector);
        if (full)
            Assert.NotNull((await new FullCompactor(client, runner, collector, config)
                .CompactAsync(history, snapshot, "compact-test")).Result);
        else
        {
            var result = await new PartialCompactor(client, config, runner, collector)
                .CompactAsync(history, snapshot, "compact-test");
            Assert.NotNull(result.Result);
            await client.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, result.Result.FormattedSummary) }
                    .Concat(result.Result.PreservedTail).Append(new ChatMessage(ChatRole.User, "continue")));
        }
        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, request => Assert.DoesNotContain(Blocks(request), block =>
            block["type"]?.GetValue<string>() is "thinking" or "redacted_thinking"));
        var summaryBlocks = Blocks(handler.Requests[0]).ToArray();
        Assert.Contains(summaryBlocks, block => block["type"]?.GetValue<string>() == "tool_use");
        Assert.Contains(summaryBlocks, block => block["type"]?.GetValue<string>() == "tool_result");
        for (var i = 0; i < history.Count; i++)
            Assert.Equal(before[i], history[i].Contents);
        Assert.Contains(history.SelectMany(message => message.Contents), content => content is TextReasoningContent);
        var fallback = Assert.Single(trace.GetEvents("compact-test"), entry =>
            entry.Type == TraceEventType.MaintenanceForkRequest
            && JsonNode.Parse(entry.MetadataJson!)!["requestPath"]!.GetValue<string>() == "fallback");
        using var metadata = JsonDocument.Parse(fallback.MetadataJson!);
        Assert.Equal(overBudget ? MaintenanceForkFallbackReasons.SnapshotTooLarge : "snapshot_absent",
            metadata.RootElement.GetProperty("selectionReason").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderRejection_PreservesFailureAndOriginalHistory(bool withSnapshot)
    {
        using var handler = new CaptureHandler { RejectRequests = true };
        using var http = new HttpClient(handler);
        using var client = CreateClient(http);
        var trace = new TraceStore();
        var history = History();
        history.Add(new ChatMessage(ChatRole.Assistant, "latest answer"));
        var snapshot = withSnapshot ? PromptRequestSnapshot.Capture(history, new ChatOptions(), threadId: "test") : null;
        var result = await new CompactionPipeline(Config(), client, new TraceCollector(trace)).TryManualCompactHistoryAsync(
            history, "test", null, CancellationToken.None, snapshot: snapshot);
        Assert.False(result.Status.Success);
        Assert.Equal("provider_bad_request", result.Status.FailureReason);
        Assert.Same(history, result.Messages);
        Assert.Contains(trace.GetEvents("test"), entry => entry.Type == TraceEventType.MaintenanceForkResponse
            && entry.MetadataJson!.Contains("request-test", StringComparison.Ordinal));
    }

    private static IEnumerable<JsonNode> Blocks(JsonNode request) => request["messages"]!.AsArray()
        .SelectMany(message => message!["content"]!.AsArray()).Select(block => block!);

    private static IChatClient CreateClient(HttpClient http) =>
        new AnthropicClientProvider(new Transport(http)).CreateChatClient(new EffectiveModelRuntime(
            "test", "test-model", ModelProviderProtocols.Anthropic, "Test", "key",
            "https://example.invalid", 30, 1024,
            ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic)));

    private static CompactionConfig Config() => new()
    {
        KeepRecentMinTokens = 1, KeepRecentMinGroups = 1, KeepRecentMaxTokens = 100_000
    };

    private static List<ChatMessage> History() =>
    [
        new(ChatRole.User, [new TextContent("inspect"), Image()]),
        new(ChatRole.Assistant,
        [
            new TextReasoningContent("private reasoning") { ProtectedData = "signed-reasoning" },
            new TextReasoningContent("")
            {
                ProtectedData = "redacted-reasoning",
                RawRepresentation = new BetaRedactedThinkingBlock { Data = "redacted-reasoning" }
            },
            new TextContent("inspecting"),
            new FunctionCallContent("call_1", "Inspect", new Dictionary<string, object?>())
        ]),
        new(ChatRole.Tool, [new FunctionResultContent("call_1", new List<AIContent> { new TextContent("captured"), Image() })]),
        new(ChatRole.Assistant, "inspection done"),
        new(ChatRole.User, "continue")
    ];

    private static DataContent Image() => new(Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="), "image/png");

    private sealed class Transport(HttpClient client) : IProviderHttpTransport
    {
        public HttpClient CreateClient(string providerId, Uri providerEndpoint) => client;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<JsonNode> Requests { get; } = [];
        public bool RejectThinking { get; init; }
        public bool RejectRequests { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!);
            if (RejectRequests || (RejectThinking && Blocks(Requests[^1]).Any(block =>
                    block["type"]?.GetValue<string>() is "thinking" or "redacted_thinking")))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""
                        {"type":"error","error":{"type":"invalid_request_error","message":"Thinking prefix does not match."},"request_id":"request-test"}
                        """, Encoding.UTF8, "application/json")
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"msg_summary","type":"message","role":"assistant","model":"test-model","content":[{"type":"text","text":"A useful summary."}],"stop_reason":"end_turn","usage":{"input_tokens":12,"output_tokens":8}}
                    """, Encoding.UTF8, "application/json")
            };
        }
    }
}
