using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceWebSearchTests : SessionServiceRuntimeSignalTestBase
{
    [Fact]
    public async Task SubmitInputAsync_WebRunToolCall_ProjectsSingleWebSearchItem()
    {
        var searchProvider = new FakeWebSearchProvider();
        var searchConfig = AppConfigTestFactory.CreateOpenAI();
        searchConfig.Providers["openai"].AuthMethod = ModelProviderAuthMethods.ChatGptOAuth;
        var chatClient = new CoordinationToolCallChatClient(
            "web__run",
            new Dictionary<string, object?>
            {
                ["search_query"] = new JsonArray(new JsonObject { ["q"] = "dotnet release" })
            });
        var recorder = new ToolInvocationRecorderRouter();
        var dispatcher = new ToolDispatcher(recorder: recorder);
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            [new WebSearchToolSource(searchConfig, new ChatClientRegistry(searchProvider))],
            toolDispatcher: dispatcher);
        var service = CreateService(agentFactory, chatClient, useStreamingFunctionInvoker: true);
        recorder.Bind(service);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("what shipped?")]));

        var turn = Assert.Single((await service.GetThreadAsync(thread.Id)).Turns);
        Assert.True(turn.Status == TurnStatus.Completed, turn.Error);
        Assert.DoesNotContain(turn.Items, item => item.Type is ItemType.ToolCall or ItemType.ToolResult);
        var item = Assert.Single(turn.Items, item => item.Type == ItemType.WebSearch);
        var payload = Assert.IsType<WebSearchPayload>(item.Payload);
        Assert.Equal("completed", payload.Status);
        Assert.Equal("coordination-call", payload.CallId);
        Assert.Equal("dotnet release", payload.Query);
        Assert.Equal("https://example.com/release", payload.Results![0]!["url"]!.GetValue<string>());
        var request = Assert.Single(searchProvider.Requests);
        Assert.Equal(thread.Id, request.Id);
        Assert.Equal("what shipped?", request.Input[0]!["content"]![0]!["text"]!.GetValue<string>());
        var started = Assert.Single(events, e =>
            e.EventType == SessionEventType.ItemStarted && e.ItemPayload?.Type == ItemType.WebSearch);
        var completed = Assert.Single(events, e =>
            e.EventType == SessionEventType.ItemCompleted && e.ItemPayload?.Type == ItemType.WebSearch);
        Assert.Equal(started.ItemId, completed.ItemId);

        var toolOutput = chatClient.FollowUpMessages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(result => result.CallId == "coordination-call");
        Assert.Contains("Release notes", toolOutput.Result?.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeWebSearchProvider : IModelProvider, IProviderWebSearch
    {
        public List<ProviderWebSearchRequest> Requests { get; } = [];

        public IReadOnlyCollection<string> Protocols { get; } =
            [ModelProviderProtocols.OpenAIChatCompletions, ModelProviderProtocols.OpenAIResponses];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => throw new NotSupportedException();

        public Task<ProviderWebSearchResult> SearchAsync(
            EffectiveModelRuntime runtime,
            ProviderWebSearchRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ProviderWebSearchResult(
                "Release notes citeturn0search0",
                [new JsonObject { ["ref_id"] = "turn0search0", ["url"] = "https://example.com/release" }]));
        }
    }
}
