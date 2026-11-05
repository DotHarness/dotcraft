using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceProviderSwitchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"provider-switch-{Guid.NewGuid():N}");
    private readonly RecordingProvider _provider = new();

    [Theory]
    [InlineData("target", "source-model")]
    [InlineData("source", "target-model")]
    public async Task ResponsesSwitch_RebasesForeignStateAndPreservesOwnershipAcrossColdResume(
        string targetProvider, string targetModel)
    {
        Directory.CreateDirectory(_root);
        var config = AppConfigTestFactory.CreateOpenAI(model: "source-model", providerId: "source");
        config.Providers["source"].Protocol = ModelProviderProtocols.OpenAIResponses;
        config.Providers["target"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            ApiKey = "unused-test-key",
            EndPoint = "https://127.0.0.1:9/v1"
        };
        config.ProviderPreferences["target"] = new ModelPreference { Model = targetModel };
        await using var factory = new AgentFactory(_root, _root, config, new MemoryStore(_root),
            new SkillsLoader(_root), new AutoApproveApprovalService(), blacklist: null,
            toolSources: [], chatClientRegistry: new ChatClientRegistry(_provider));
        var store = new ThreadStore(Path.Combine(_root, ".craft"));
        var persistence = new SessionPersistenceService(store);
        var service = new SessionService(factory, factory.CreateDefaultAgent(), persistence, new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test", UserId = "user", WorkspacePath = _root
        });

        await SubmitAsync(service, thread.Id, "first");
        var first = await SnapshotAsync();
        var sourceOrigin = new ProviderMessageOrigin("source", ModelProviderProtocols.OpenAIResponses, "source-model");
        Assert.Equal(sourceOrigin, first.Origin);
        Assert.Contains(first.Entries, entry => ReadString(entry.Item, "encrypted_content") == "source/source-model");

        await service.UpdateThreadConfigurationAsync(thread.Id, new ThreadConfiguration
        {
            ProviderId = targetProvider, Model = targetModel
        });
        await SubmitAsync(service, thread.Id, "continue");
        var switched = await SnapshotAsync();
        var targetOrigin = new ProviderMessageOrigin(targetProvider, ModelProviderProtocols.OpenAIResponses, targetModel);
        Assert.Equal(targetOrigin, switched.Origin);
        Assert.NotEqual(first.GenerationId, switched.GenerationId);
        var switchedInput = _provider.Requests[1].GetProperty("input");
        Assert.DoesNotContain(switchedInput.EnumerateArray(), item => ReadString(item, "type") == "reasoning");
        Assert.Contains(switchedInput.EnumerateArray(), item => item.GetRawText().Contains("answer-1"));
        Assert.DoesNotContain("source/source-model", switchedInput.GetRawText());

        var rawHistory = await persistence.LoadModelHistoryAsync(thread.Id, CancellationToken.None);
        Assert.Contains(rawHistory, message => ProviderMessageProjection.ReadOrigin(message) == sourceOrigin
            && message.Contents.OfType<TextReasoningContent>().Any(reasoning => reasoning.ProtectedData == "source/source-model"));

        var prefix = switched.Entries.Select(entry => entry.Item.GetRawText()).ToArray();
        var delay = TimeSpan.FromMinutes(1);
        Assert.Equal(1, await service.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow + delay + delay, delay));
        await service.EnsureThreadLoadedAsync(thread.Id);
        await SubmitAsync(service, thread.Id, "after reload");
        var resumed = await SnapshotAsync();
        Assert.Equal(targetOrigin, resumed.Origin);
        Assert.Equal(switched.GenerationId, resumed.GenerationId);
        Assert.Equal(prefix, _provider.Requests[2].GetProperty("input").EnumerateArray()
            .Skip(1).Take(prefix.Length).Select(item => item.GetRawText()));

        await service.UpdateThreadConfigurationAsync(thread.Id, new ThreadConfiguration
        {
            ProviderId = "source", Model = "source-model"
        });
        await SubmitAsync(service, thread.Id, "back to source");
        var returned = await SnapshotAsync();
        Assert.Equal(sourceOrigin, returned.Origin);
        var returnedInput = _provider.Requests[3].GetProperty("input").GetRawText();
        Assert.Contains("source/source-model", returnedInput);
        Assert.DoesNotContain($"{targetProvider}/{targetModel}", returnedInput);

        async Task<ProviderHistorySnapshot> SnapshotAsync()
        {
            var current = await service.GetThreadAsync(thread.Id);
            return await persistence.LoadProviderHistoryAsync(current, "unused-fallback", CancellationToken.None);
        }
    }

    [Fact]
    public async Task LocalCompaction_ProjectsForeignReasoningBeforeRequestingSummary()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "target-model", providerId: "target");
        config.Providers["target"].Protocol = ModelProviderProtocols.OpenAIResponses;
        var runtime = new ChatClientRegistry(_provider).ResolveMainRuntime(config);
        using var client = ProviderChatClientAdapters.CreateRequestAdaptedClient(
            _provider.CreateChatClient(runtime), config, runtime);
        var history = new ChatMessage(ChatRole.Assistant,
            [new TextReasoningContent("private") { ProtectedData = "source-private" }, new TextContent("visible answer")]);
        ProviderMessageProjection.Stamp(history, new("source", ModelProviderProtocols.OpenAIResponses, "source-model"));
        var snapshot = PromptRequestSnapshot.Capture(
            [new ChatMessage(ChatRole.User, "question"), history], new ChatOptions { ModelId = runtime.Model });

        var result = await new MaintenanceForkRunner(client).RunAsync(snapshot,
            new MaintenanceForkTask(MaintenanceForkTaskKind.ContextCompaction, "Summarize this conversation."));

        Assert.Null(result.FallbackReason);
        var input = Assert.Single(_provider.Requests).GetProperty("input");
        Assert.DoesNotContain(input.EnumerateArray(), item => ReadString(item, "type") == "reasoning");
        Assert.Contains("visible answer", input.GetRawText());
        Assert.Equal("source-private", Assert.Single(history.Contents.OfType<TextReasoningContent>()).ProtectedData);
    }

    private static async Task SubmitAsync(SessionService service, string threadId, string text)
    {
        await foreach (var item in service.SubmitInputAsync(threadId, [new TextContent(text)]))
            Assert.NotEqual(SessionEventType.TurnFailed, item.EventType);
        Assert.Equal(TurnStatus.Completed, (await service.GetThreadAsync(threadId)).Turns.Last().Status);
    }

    private static string? ReadString(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class RecordingProvider : IModelProvider
    {
        private readonly OpenAIClientProvider _history = new();
        public List<JsonElement> Requests { get; } = [];
        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAIResponses];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new OpenAIResponsesToolSearchChatClient(
            new ResponsesClient("unused-test-key"), runtime.Model, new EmptyClient(), new Transport(this, runtime));

        public object? GetService(Type type, object? serviceKey = null) =>
            serviceKey is null && type == typeof(IProviderHistorySessionFactory) ? _history : null;

        private sealed class Transport(RecordingProvider owner, EffectiveModelRuntime runtime) : IResponsesToolSearchTransport
        {
            public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(
                CreateResponseOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                using var request = JsonDocument.Parse(ModelReaderWriter.Write(options).ToString());
                owner.Requests.Add(request.RootElement.Clone());
                var origin = $"{runtime.ProviderId}/{runtime.Model}";
                var number = owner.Requests.Count;
                yield return ReadUpdate(new
                {
                    type = "response.output_item.done", sequence_number = 1, output_index = 0,
                    item = new
                    {
                        type = "reasoning", id = $"rs_{number}", status = "completed",
                        encrypted_content = origin,
                        summary = new[] { new { type = "summary_text", text = $"thinking-{number}" } }
                    }
                });
                yield return new StreamingResponseOutputTextDeltaUpdate
                {
                    SequenceNumber = 2, ItemId = $"msg_{number}", OutputIndex = 1, ContentIndex = 0, Delta = $"answer-{number}"
                };
                yield return ReadUpdate(new
                {
                    type = "response.output_item.done", sequence_number = 3, output_index = 1,
                    item = new
                    {
                        type = "message", id = $"msg_{number}", role = "assistant", status = "completed",
                        content = new[] { new { type = "output_text", text = $"answer-{number}", annotations = Array.Empty<object>() } }
                    }
                });
                await Task.CompletedTask;
            }

            private static StreamingResponseUpdate ReadUpdate(object update) =>
                ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString(JsonSerializer.Serialize(update)))!;
        }
    }

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
