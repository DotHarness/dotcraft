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

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceNativeCompactionTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "NativeCompact_" + Guid.NewGuid().ToString("N"));
    private readonly NativeProvider _provider = new();

    public SessionServiceNativeCompactionTests()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, ".git"));
        File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "Preserve the blue project convention.");
    }

    [Fact]
    public async Task ManualCompaction_PreservesInstructionsCoverageAcrossContinuationAndColdResume()
    {
        const string threadId = "native-instructions";
        await using (var factory = CreateFactory())
        {
            var service = CreateService(factory);
            await service.CreateThreadAsync(new SessionIdentity
            {
                WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
            }, threadId: threadId);
            await DrainAsync(service.SubmitInputAsync(threadId, [new TextContent("initial question")]));

            var persistence = new SessionPersistenceService(new ThreadStore(_workspace));
            var before = await persistence.LoadModelHistoryAsync(threadId, CancellationToken.None);
            Assert.Single(before, AgentInstructionsHistory.IsInstructions);
            await CompactAsync(service, threadId);
            Assert.Equal(before.Count, (await persistence.LoadModelHistoryAsync(threadId, CancellationToken.None)).Count);
            Assert.Single(_provider.CompactHistories[^1], AgentInstructionsHistory.IsInstructions);
            Assert.Equal(before.Count, _provider.CompactHistories[^1].Count);

            await DrainAsync(service.SubmitInputAsync(threadId, [new TextContent("after-first-compact")]));
            AssertContainsOnce(_provider.Requests[^1], "after-first-compact");
            Assert.Contains("encrypted-test", _provider.Requests[^1]);
            await CompactAsync(service, threadId);
        }

        await using (var factory = CreateFactory())
        {
            var service = CreateService(factory);
            await service.GetThreadAsync(threadId);
            await CompactAsync(service, threadId);
            await DrainAsync(service.SubmitInputAsync(threadId, [new TextContent("after-cold-resume")]));
            AssertContainsOnce(_provider.Requests[^1], "after-cold-resume");
            Assert.Contains("encrypted-test", _provider.Requests[^1]);
        }

        var records = await ReadRecordsAsync(threadId);
        Assert.Equal(3, records.Count(record => record.ProviderHistoryReplaced?.Reason == "remote_compaction"));
        Assert.DoesNotContain(records, record => record.Kind == RolloutKinds.ContextCompacted);
    }

    [Fact]
    public async Task ManualCompaction_FailureDoesNotInstallReplacement()
    {
        await using var factory = CreateFactory();
        var service = CreateService(factory);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        });
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        _provider.Failure = new IOException("transport failed");
        var result = await service.CompactThreadAsync(thread.Id);
        Assert.False(result.Outcome is "partial" or "compacted");
        var records = await ReadRecordsAsync(thread.Id);
        Assert.DoesNotContain(records, record => record.ProviderHistoryReplaced?.Reason == "remote_compaction");
    }

    [Fact]
    public async Task ReactiveCompaction_CoversRejectedInputAndAllowsResubmission()
    {
        await using var factory = CreateFactory();
        var service = CreateService(factory);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        });
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("before-overflow")]));
        _provider.OverflowNextRequest = true;
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("overflow-request")]));
        Assert.Single(_provider.Compactions);
        Assert.Single(_provider.CompactHistories.Single(), AgentInstructionsHistory.IsInstructions);
        Assert.Contains("overflow-request", JsonSerializer.Serialize(_provider.Compactions.Single().Input));
        using var rejected = JsonDocument.Parse(_provider.Requests[^1]);
        Assert.Equal(
            rejected.RootElement.GetProperty("input").EnumerateArray().Select(item => JsonSerializer.Serialize(item)),
            _provider.Compactions.Single().Input.SkipLast(1).Select(item => JsonSerializer.Serialize(item)));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("retry-after-overflow")]));
        Assert.Contains("encrypted-test", _provider.Requests[^1]);
        AssertContainsOnce(_provider.Requests[^1], "retry-after-overflow");
    }

    [Fact]
    public async Task PreTurnCompaction_ExcludesPendingUserAndAppendsItOnce()
    {
        await using var factory = CreateFactory();
        var service = CreateService(factory);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        });
        _provider.ReportHighUsageNextResponse = true;
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("committed-prefix")]));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("pending-user-tail")]));
        Assert.Single(_provider.Compactions);
        Assert.DoesNotContain("pending-user-tail", JsonSerializer.Serialize(_provider.Compactions.Single().Input));
        AssertContainsOnce(_provider.Requests[^1], "pending-user-tail");
        Assert.Contains("encrypted-test", _provider.Requests[^1]);
    }

    [Fact]
    public async Task MidTurnCompaction_CoversToolResultTail()
    {
        await using var factory = CreateFactory();
        SessionService? service = null;
        const string threadId = "mid-turn-guidance";
        var agent = factory.CreateAgentWithTools(
            [AIFunctionFactory.Create(async () =>
            {
                await service!.SteerTurnAsync(threadId, "turn_001", [new TextContent("steered-user-tail")]);
                return "tool-result-tail";
            }, name: "GetStatus")], null, factory.RuntimeContext, "Use the status tool.");
        service = new SessionService(factory, agent,
            new SessionPersistenceService(new ThreadStore(_workspace)), new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        }, threadId: threadId);
        _provider.ToolNextResponse = true;
        _provider.ReportHighUsageNextResponse = true;
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("check status")]));
        Assert.Single(_provider.Compactions);
        Assert.Contains("tool-result-tail", JsonSerializer.Serialize(_provider.Compactions.Single().Input));
        AssertContainsOnce(JsonSerializer.Serialize(_provider.Compactions.Single().Input), "steered-user-tail");
        Assert.Single(_provider.CompactHistories.Single(), AgentInstructionsHistory.IsInstructions);
        Assert.Contains("encrypted-test", _provider.Requests[^1]);
        Assert.DoesNotContain("tool-result-tail", _provider.Requests[^1]);
    }

    [Fact]
    public async Task ApiKeyResponses_LocalSummaryFiltersAndReloadsInstructions()
    {
        await using var factory = CreateFactory(oauth: false);
        var service = CreateService(factory);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        });
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("summarize this conversation")]));
        var requestCount = _provider.Requests.Count;
        await CompactAsync(service, thread.Id);
        Assert.Empty(_provider.Compactions);
        Assert.True(_provider.Requests.Count > requestCount);
        Assert.All(_provider.Requests.Skip(requestCount), request => Assert.DoesNotContain("blue project", request));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("continue after summary")]));
        var history = await new SessionPersistenceService(new ThreadStore(_workspace))
            .LoadModelHistoryAsync(thread.Id, CancellationToken.None);
        Assert.Single(history, AgentInstructionsHistory.IsInstructions);
    }

    private async Task<ThreadRolloutRecord[]> ReadRecordsAsync(string threadId)
    {
        var path = Directory.GetFiles(_workspace, threadId + ".jsonl", SearchOption.AllDirectories).Single();
        return (await File.ReadAllLinesAsync(path)).Select(line =>
            JsonSerializer.Deserialize<ThreadRolloutRecord>(line, SessionJsonOptions.Default)!).ToArray();
    }

    private static async Task CompactAsync(SessionService service, string threadId)
    {
        var result = await service.CompactThreadAsync(threadId);
        Assert.True(result.Outcome == "partial", result.Message ?? result.Outcome);
    }

    private AgentFactory CreateFactory(bool oauth = true)
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "gpt-test");
        config.Providers["openai"].Protocol = ModelProviderProtocols.OpenAIResponses;
        if (oauth)
            config.Providers["openai"].AuthMethod = ModelProviderAuthMethods.ChatGptOAuth;
        config.Compaction.ContextWindow = 200_000;
        return new AgentFactory(_workspace, _workspace, config, new MemoryStore(_workspace),
            new SkillsLoader(_workspace), new AutoApproveApprovalService(), blacklist: null,
            toolSources: [], chatClientRegistry: new ChatClientRegistry(_provider));
    }

    private SessionService CreateService(AgentFactory factory) => new(factory,
        factory.CreateDefaultAgent(), new SessionPersistenceService(new ThreadStore(_workspace)), new SessionGate());

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events) { }
    }

    private static void AssertContainsOnce(string input, string text) => Assert.Equal(1,
        input.Split(text, StringSplitOptions.None).Length - 1);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_workspace, recursive: true);
    }

    private sealed class NativeProvider : IModelProvider, IProviderNativeCompactorFactory,
        IResponsesToolSearchTransport, IChatGptResponsesCompactTransport, IProviderNativeCompactor
    {
        private readonly OpenAIClientProvider _historyProvider = new();
        public List<string> Requests { get; } = [];
        public List<ChatGptResponsesCompactRequest> Compactions { get; } = [];
        public List<IReadOnlyList<ChatMessage>> CompactHistories { get; } = [];
        public Exception? Failure { get; set; }
        public bool OverflowNextRequest { get; set; }
        public bool ReportHighUsageNextResponse { get; set; }
        public bool ToolNextResponse { get; set; }
        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAIResponses];
        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => CreateClient();
        public IChatClient CreateClient() => new OpenAIResponsesToolSearchChatClient(
            new ResponsesClient("sk-test"), "gpt-test", new EmptyClient(), this);
        public object? GetService(Type type, object? key = null) =>
            type == typeof(IProviderHistorySessionFactory) ? _historyProvider :
            type.IsInstanceOfType(this) ? this : null;
        public IProviderNativeCompactor CreateCompactor(EffectiveModelRuntime runtime, IChatClient? rawRepresentationClient = null) =>
            this;

        public Task<ProviderNativeCompactionReplacement> CompactAsync(ProviderNativeCompactionInput input,
            IReadOnlyList<ChatMessage> neutralHistory, ChatOptions? options, CancellationToken cancellationToken)
        {
            CompactHistories.Add(neutralHistory);
            return new OpenAIResponsesCompactor("gpt-test", false, this)
                .CompactAsync(input, neutralHistory, options, cancellationToken);
        }

        public Task<ChatGptResponsesCompactResponse> CompactAsync(ChatGptResponsesCompactRequest request,
            CancellationToken cancellationToken)
        {
            Compactions.Add(request);
            if (Failure is not null)
                return Task.FromException<ChatGptResponsesCompactResponse>(Failure);
            return Task.FromResult(new ChatGptResponsesCompactResponse
            {
                Output = [JsonSerializer.SerializeToElement(new { type = "compaction", encrypted_content = "encrypted-test" })]
            });
        }

        public async IAsyncEnumerable<StreamingResponseUpdate> CreateResponseStreamingAsync(CreateResponseOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(ModelReaderWriter.Write(options).ToString());
            if (OverflowNextRequest)
            {
                OverflowNextRequest = false;
                throw new InvalidOperationException("context_length_exceeded");
            }
            var tool = ToolNextResponse;
            ToolNextResponse = false;
            if (tool)
            {
                yield return ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString("""
                    {"type":"response.output_item.done","sequence_number":1,"output_index":0,
                     "item":{"type":"function_call","id":"fc_test","call_id":"call_test","name":"GetStatus",
                     "arguments":"{}","status":"completed"}}
                    """))!;
            }
            else
            {
                yield return new StreamingResponseOutputTextDeltaUpdate
                {
                    SequenceNumber = 0, ItemId = "msg_test", OutputIndex = 0, ContentIndex = 0, Delta = "ok"
                };
                yield return ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString("""
                    {"type":"response.output_item.done","sequence_number":1,"output_index":0,
                     "item":{"type":"message","id":"msg_test","role":"assistant","status":"completed",
                     "content":[{"type":"output_text","text":"ok","annotations":[]}]}}
                    """))!;
            }
            if (ReportHighUsageNextResponse)
            {
                ReportHighUsageNextResponse = false;
                yield return ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString("""
                    {"type":"response.completed","sequence_number":2,"response":{"id":"resp_test","object":"response",
                     "model":"gpt-test","status":"completed","output":[],
                     "usage":{"input_tokens":900000,"output_tokens":10,"total_tokens":900010}}}
                    """))!;
            }
            await Task.CompletedTask;
        }
    }

    private sealed class EmptyClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => Task.FromResult(new ChatResponse([]));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
