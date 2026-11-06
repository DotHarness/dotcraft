using System.Runtime.CompilerServices;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class ProviderMessageProjectionTests
{
    private static readonly ProviderMessageOrigin Source = new("source", "openai-responses", "model-a");

    [Theory]
    [InlineData("target", "openai-responses", "model-a")]
    [InlineData("source", "anthropic", "model-a")]
    [InlineData("source", "openai-responses", "model-b")]
    public void Foreign_projection_preserves_visible_content_without_mutating_private_history(
        string provider, string protocol, string model)
    {
        var reasoning = new TextReasoningContent("private thought") { ProtectedData = "encrypted" };
        var text = new TextContent("answer") { AdditionalProperties = new() { ["signature"] = "private" } };
        var call = new FunctionCallContent("call_1", "lookup")
        {
            RawRepresentation = new object(),
            AdditionalProperties = new()
            {
                ["native_id"] = "fc_private",
                [ProviderFunctionCallMetadata.NamespaceKey] = "search",
                ["dotcraft.tool.provider_flat_name"] = "search__lookup"
            }
        };
        var searchCall = new WebSearchToolCallContent("ws_private") { Queries = ["private query"] };
        var searchResult = new WebSearchToolResultContent("ws_private")
        {
            AdditionalProperties = new() { ["openai.responses.web_search"] = "private replay item" }
        };
        var message = new ChatMessage(ChatRole.Assistant, [reasoning, text, call, searchCall, searchResult])
        {
            MessageId = "msg_private", RawRepresentation = new object()
        };
        ProviderMessageProjection.Stamp(message, Source);

        var projected = Assert.Single(ProviderMessageProjection.Project([message], new(provider, protocol, model)));
        Assert.Equal("answer", Assert.Single(projected.Contents.OfType<TextContent>()).Text);
        Assert.Empty(projected.Contents.OfType<TextReasoningContent>());
        Assert.DoesNotContain(projected.Contents, content => content is WebSearchToolCallContent or WebSearchToolResultContent);
        Assert.Null(projected.MessageId);
        Assert.Null(projected.RawRepresentation);
        Assert.Null(projected.AdditionalProperties);
        var projectedCall = Assert.Single(projected.Contents.OfType<FunctionCallContent>());
        Assert.Equal("call_1", projectedCall.CallId);
        Assert.Null(projectedCall.RawRepresentation);
        Assert.Equal("search", projectedCall.AdditionalProperties![ProviderFunctionCallMetadata.NamespaceKey]);
        Assert.Equal("search__lookup", projectedCall.AdditionalProperties["dotcraft.tool.provider_flat_name"]);
        Assert.False(projectedCall.AdditionalProperties.ContainsKey("native_id"));
        Assert.Null(Assert.Single(projected.Contents.OfType<TextContent>()).AdditionalProperties);
        Assert.Equal("encrypted", reasoning.ProtectedData);
        Assert.Equal("private", text.AdditionalProperties!["signature"]);
        Assert.Equal("msg_private", message.MessageId);
        var sameOrigin = Assert.Single(ProviderMessageProjection.Project([message], Source));
        Assert.Same(searchCall, Assert.Single(sameOrigin.Contents.OfType<WebSearchToolCallContent>()));
        Assert.Same(searchResult, Assert.Single(sameOrigin.Contents.OfType<WebSearchToolResultContent>()));
        Assert.Equal("encrypted", Assert.Single(
            ProviderMessageProjection.Project([message], Source)[0].Contents.OfType<TextReasoningContent>()).ProtectedData);
    }

    [Fact]
    public void Unattributed_reasoning_is_omitted_but_exact_origin_replays_private_content()
    {
        var reasoning = new TextReasoningContent("") { ProtectedData = "encrypted" };
        var message = new ChatMessage(ChatRole.Assistant, [reasoning]);
        Assert.Empty(ProviderMessageProjection.Project([message], Source));
        ProviderMessageProjection.Stamp(message, Source);
        var replay = Assert.Single(ProviderMessageProjection.Project([message], Source));
        Assert.Equal("encrypted", Assert.Single(replay.Contents.OfType<TextReasoningContent>()).ProtectedData);
    }

    [Fact]
    public void Foreign_call_ids_and_results_are_remapped_together()
    {
        var longId = new string('x', 90) + "|native";
        var secondId = "call with spaces";
        var calls = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(longId, "lookup"), new FunctionCallContent(secondId, "lookup")]);
        var result = new FunctionResultContent(longId, "42") { RawRepresentation = new object() };
        ChatMessage[] history = [calls, new(ChatRole.Tool, [result, new FunctionResultContent(secondId, "43")])];
        var projected = ProviderMessageProjection.Project(history, Source);
        var projectedCalls = projected[0].Contents.OfType<FunctionCallContent>().ToArray();
        var projectedResults = projected[1].Contents.OfType<FunctionResultContent>().ToArray();
        Assert.Equal(projectedCalls.Select(call => call.CallId), projectedResults.Select(item => item.CallId));
        Assert.Equal("42", projectedResults[0].Result);
        Assert.Equal("43", projectedResults[1].Result);
        Assert.All(projectedCalls, call => Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", call.CallId));
        Assert.Equal(projectedCalls[0].CallId,
            ProviderMessageProjection.Project(history, Source)[0].Contents.OfType<FunctionCallContent>().First().CallId);
        Assert.NotEqual(projectedCalls[0].CallId, projectedCalls[1].CallId);
        Assert.Null(projectedResults[0].RawRepresentation);
        Assert.Equal(longId, result.CallId);
        Assert.Equal(longId, calls.Contents.OfType<FunctionCallContent>().First().CallId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Agent_stamps_output_before_tool_continuation_without_attributing_historical_input(bool streaming)
    {
        using var model = new ToolClient();
        using var loop = new StreamingFunctionInvokingChatClient(model);
        var agent = new ChatClientAgent(loop, new ChatClientAgentOptions
        {
            MessageOrigin = Source,
            ChatOptions = new() { Tools = [AIFunctionFactory.Create(() => "42", name: "lookup")] }
        });
        var history = new List<ChatMessage>();
        var imported = new ChatMessage(ChatRole.Assistant, "imported");
        if (streaming)
        {
            await foreach (var _ in agent.RunStreamingAsync(imported, history)) { }
        }
        else
            await agent.RunAsync(imported, history);
        Assert.Equal(2, model.RequestCount);
        Assert.Null(ProviderMessageProjection.ReadOrigin(history[0]));
        Assert.All(history.Where(message => message.Role == ChatRole.Assistant && message.Text != "imported"),
            message => Assert.Equal(Source, ProviderMessageProjection.ReadOrigin(message)));
    }

    private sealed class ToolClient : IChatClient
    {
        public int RequestCount { get; private set; }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (++RequestCount == 1)
                yield return new(ChatRole.Assistant,
                    [new TextReasoningContent("thinking") { ProtectedData = "encrypted" }, new FunctionCallContent("call_1", "lookup")]);
            else
            {
                var prior = Assert.Single(messages, message => message.Contents.OfType<FunctionCallContent>().Any());
                Assert.Equal(Source, ProviderMessageProjection.ReadOrigin(prior));
                Assert.Equal("encrypted", Assert.Single(prior.Contents.OfType<TextReasoningContent>()).ProtectedData);
                yield return new(ChatRole.Assistant, "answer");
            }
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
