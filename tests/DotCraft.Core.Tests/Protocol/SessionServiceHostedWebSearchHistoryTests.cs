using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceHostedWebSearchHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"hosted-search-{Guid.NewGuid():N}");
    private int _localSearchCalls;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostedSearch_SurvivesTurnCommitAndColdResumeWithoutLocalExecution(bool lite)
    {
        var config = CreateConfig();
        var firstProvider = new RecordingProvider(lite, emitSearch: true);
        string threadId;
        JsonElement expected;
        await using (var factory = CreateFactory(config, firstProvider))
        {
            var store = new ThreadStore(Path.Combine(_root, ".craft"));
            var persistence = new SessionPersistenceService(store);
            var service = CreateService(factory, persistence);
            var thread = await service.CreateThreadAsync(new SessionIdentity
            {
                ChannelName = "test", UserId = "user", WorkspacePath = _root
            });
            threadId = thread.Id;
            await SubmitAsync(service, threadId, "search for release notes");

            var history = await persistence.LoadModelHistoryAsync(threadId, CancellationToken.None);
            var call = Assert.Single(history.SelectMany(message => message.Contents).OfType<WebSearchToolCallContent>());
            var result = Assert.Single(history.SelectMany(message => message.Contents).OfType<WebSearchToolResultContent>());
            Assert.Equal("ws_native", call.CallId);
            Assert.Equal(new[] { "sample release", "sample notes" }, call.Queries);
            Assert.Equal(call.CallId, result.CallId);
            Assert.Null(result.RawRepresentation);
            Assert.NotNull(result.AdditionalProperties![ResponsesWebSearchMetadata.Key]);
            Assert.All(history.Where(message => message.Contents.Any(content =>
                content is WebSearchToolCallContent or WebSearchToolResultContent)),
                message => Assert.Equal(ChatRole.Assistant, message.Role));

            var saved = await persistence.LoadProviderHistoryAsync(
                await service.GetThreadAsync(threadId), "unused", CancellationToken.None);
            expected = Assert.Single(saved.Entries, entry => IsSearch(entry.Item)).Item.Clone();
            Assert.Equal("native-private", expected.GetProperty("encrypted_content").GetString());
            await store.FlushAndCloseAsync();
        }

        var resumedProvider = new RecordingProvider(lite, emitSearch: false);
        await using var resumedFactory = CreateFactory(config, resumedProvider);
        var resumedStore = new ThreadStore(Path.Combine(_root, ".craft"));
        var resumedPersistence = new SessionPersistenceService(resumedStore);
        var resumed = CreateService(resumedFactory, resumedPersistence);
        await resumed.ResumeThreadAsync(threadId);
        await SubmitAsync(resumed, threadId, "continue from the search");

        var input = Assert.Single(resumedProvider.Requests).GetProperty("input");
        var replay = Assert.Single(input.EnumerateArray(), IsSearch);
        Assert.True(JsonElement.DeepEquals(expected, replay));
        Assert.DoesNotContain(input.EnumerateArray(), item => ReadType(item) == "function_call_output");
        Assert.DoesNotContain("Unsupported content", input.GetRawText());
        Assert.DoesNotContain(ResponsesWebSearchMetadata.Key, input.GetRawText());
        Assert.Equal(0, _localSearchCalls);
        Assert.Single(firstProvider.Requests);
        await resumedStore.FlushAndCloseAsync();
    }

    [Theory]
    [InlineData("target", "source-model")]
    [InlineData("source", "target-model")]
    public async Task HostedSearch_ProviderOrModelSwitchDoesNotReplayForeignNativeState(
        string targetProvider, string targetModel)
    {
        var config = CreateConfig();
        var provider = new RecordingProvider(lite: false, emitSearch: true);
        await using var factory = CreateFactory(config, provider);
        var store = new ThreadStore(Path.Combine(_root, ".craft"));
        var persistence = new SessionPersistenceService(store);
        var service = CreateService(factory, persistence);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test", UserId = "user", WorkspacePath = _root
        });
        await SubmitAsync(service, thread.Id, "search");
        await service.UpdateThreadConfigurationAsync(thread.Id, new ThreadConfiguration
        {
            ProviderId = targetProvider, Model = targetModel
        });
        await SubmitAsync(service, thread.Id, "continue on another origin");

        var input = provider.Requests[1].GetProperty("input");
        Assert.DoesNotContain(input.EnumerateArray(), IsSearch);
        Assert.DoesNotContain("native-private", input.GetRawText());
        Assert.Contains("answer-1", input.GetRawText());
        var history = await persistence.LoadModelHistoryAsync(thread.Id, CancellationToken.None);
        Assert.Single(history.SelectMany(message => message.Contents).OfType<WebSearchToolResultContent>());
        Assert.Equal(0, _localSearchCalls);
        await store.FlushAndCloseAsync();
    }

    private static AppConfig CreateConfig()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "source-model", providerId: "source");
        config.Providers["source"].Protocol = ModelProviderProtocols.OpenAIResponses;
        config.Providers["target"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            ApiKey = "unused-test-key",
            EndPoint = "https://127.0.0.1:9/v1"
        };
        return config;
    }

    private AgentFactory CreateFactory(AppConfig config, RecordingProvider provider)
    {
        Directory.CreateDirectory(_root);
        return new AgentFactory(_root, _root, config, new MemoryStore(_root),
            new SkillsLoader(_root), new AutoApproveApprovalService(), blacklist: null,
            toolSources: [], chatClientRegistry: new ChatClientRegistry(provider));
    }

    private SessionService CreateService(AgentFactory factory, SessionPersistenceService persistence) =>
        new(factory, factory.CreateAgentWithTools(
            [AIFunctionFactory.Create(() => { _localSearchCalls++; return "local result"; }, name: "web__run")],
            null, factory.RuntimeContext, "Answer the user."), persistence, new SessionGate());

    private static async Task SubmitAsync(SessionService service, string threadId, string text)
    {
        await foreach (var item in service.SubmitInputAsync(threadId, [new TextContent(text)]))
            Assert.NotEqual(SessionEventType.TurnFailed, item.EventType);
        var turn = (await service.GetThreadAsync(threadId)).Turns.Last();
        Assert.True(turn.Status == TurnStatus.Completed, turn.Error);
        Assert.DoesNotContain(turn.Items, item => item.Type is ItemType.ToolCall or ItemType.ToolResult);
    }

    private static string? ReadType(JsonElement item) => item.GetProperty("type").GetString();
    private static bool IsSearch(JsonElement item) => ReadType(item) == "web_search_call";

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class RecordingProvider(bool lite, bool emitSearch) : IModelProvider
    {
        private readonly OpenAIClientProvider _history = new();
        private readonly bool _emitSearch = emitSearch;
        public List<JsonElement> Requests { get; } = [];
        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAIResponses];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
        {
            var client = new ResponsesClient("unused-test-key");
            var transport = new Transport(this);
            return lite
                ? new OpenAIResponsesLiteChatClient(client, runtime.Model, new EmptyClient(), transport, "test-installation")
                : new OpenAIResponsesToolSearchChatClient(client, runtime.Model, new EmptyClient(), transport);
        }

        public object? GetService(Type type, object? serviceKey = null) =>
            serviceKey is null && type == typeof(IProviderHistorySessionFactory) ? _history : null;

        private sealed class Transport(RecordingProvider owner) : IResponsesToolSearchTransport, IResponsesLiteTransport
        {
            public IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
                CreateResponseOptions options, CancellationToken cancellationToken) =>
                Stream(BinaryData.FromString(ModelReaderWriter.Write(options).ToString()), cancellationToken);

            public IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
                BinaryData wireBody, CancellationToken cancellationToken) => Stream(wireBody, cancellationToken);

            private async IAsyncEnumerable<StreamingResponseUpdate> Stream(
                BinaryData body, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                using var request = JsonDocument.Parse(body.ToString());
                owner.Requests.Add(request.RootElement.Clone());
                var number = owner.Requests.Count;
                if (owner._emitSearch && number == 1)
                {
                    yield return ReadUpdate(new
                    {
                        type = "response.web_search_call.in_progress", sequence_number = 1,
                        output_index = 0, item_id = "ws_native"
                    });
                    yield return ReadUpdate(new
                    {
                        type = "response.output_item.done", sequence_number = 2, output_index = 0,
                        item = new
                        {
                            type = "web_search_call", id = "ws_native", status = "completed",
                            action = new { type = "search", query = "sample release", queries = new[] { "sample release", "sample notes" } },
                            encrypted_content = "native-private"
                        }
                    });
                }
                yield return new StreamingResponseOutputTextDeltaUpdate
                {
                    SequenceNumber = 3, ItemId = $"msg_{number}", OutputIndex = 1,
                    ContentIndex = 0, Delta = $"answer-{number}"
                };
                yield return ReadUpdate(new
                {
                    type = "response.output_item.done", sequence_number = 4, output_index = 1,
                    item = new
                    {
                        type = "message", id = $"msg_{number}", role = "assistant", status = "completed",
                        content = new[] { new { type = "output_text", text = $"answer-{number}", annotations = Array.Empty<object>() } }
                    }
                });
                await Task.CompletedTask;
            }
        }
    }

    private static StreamingResponseUpdate ReadUpdate(object update) =>
        ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString(JsonSerializer.Serialize(update)))!;

    private sealed class EmptyClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type type, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
