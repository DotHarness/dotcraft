using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceResidentModelHistoryTests : IDisposable
{
    private const string ThreadId = "resident-model-history";
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "ResidentHistory_" + Guid.NewGuid().ToString("N"));
    private readonly ScriptedChatClient _chat = new();

    public SessionServiceResidentModelHistoryTests() => Directory.CreateDirectory(_workspace);

    private string CraftPath => Path.Combine(_workspace, ".craft");

    [Theory]
    [InlineData("plain")]
    [InlineData("pre-turn")]
    [InlineData("post-turn")]
    [InlineData("mid-turn")]
    [InlineData("manual")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("rollback")]
    public async Task NextTurn_SendsTheHistoryAColdReplayWouldSend(string scenario)
    {
        await using var factory = CreateFactory(scenario);
        var store = new ThreadStore(CraftPath);
        var service = CreateService(factory, store);
        await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        }, threadId: ThreadId);
        for (var i = 0; i < 3; i++)
            await DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent($"seed {i} " + new string('u', 1200))]));
        await RunScenarioAsync(service, scenario);
        var rolloutPath = Directory.GetFiles(CraftPath, ThreadId + ".jsonl", SearchOption.AllDirectories).Single();
        var coldStartLength = new FileInfo(rolloutPath).Length;
        if (scenario is "pre-turn" or "post-turn" or "mid-turn" or "manual")
            Assert.Contains("\"kind\":\"context_compacted\"", await File.ReadAllTextAsync(rolloutPath));
        if (scenario == "rollback")
            await service.RollbackThreadAsync(ThreadId, 1);

        var reads = store.RolloutReplayCount;
        await DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent("next-request")]));
        Assert.Equal(reads, store.RolloutReplayCount);
        var warmRequest = Conversation(_chat.Requests[^1]);

        await store.FlushAndCloseAsync();
        await using (var rollout = new FileStream(rolloutPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            rollout.SetLength(coldStartLength);
        await using var coldFactory = CreateFactory(scenario);
        var cold = CreateService(coldFactory, new ThreadStore(CraftPath));
        await cold.ResumeThreadAsync(ThreadId);
        if (scenario == "rollback")
            await cold.RollbackThreadAsync(ThreadId, 1);
        await DrainAsync(cold.SubmitInputAsync(ThreadId, [new TextContent("next-request")]));
        Assert.Equal(warmRequest, Conversation(_chat.Requests[^1]));
    }

    private async Task RunScenarioAsync(SessionService service, string scenario)
    {
        switch (scenario)
        {
            case "pre-turn":
                await new ThreadStore(CraftPath).SaveContextUsageTokensAsync(ThreadId, 9_500, "history_estimate", isEstimate: true);
                await DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent("compacted-before")]));
                break;
            case "post-turn":
                _chat.InputTokensNext = 5_000;
                await DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent("compacted-after")]));
                break;
            case "mid-turn":
                _chat.ToolNext = true;
                _chat.InputTokensNext = 9_500;
                await DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent("check status")]));
                break;
            case "manual":
                var result = await service.CompactThreadAsync(ThreadId);
                Assert.Equal("partial", result.Outcome);
                break;
            case "failed":
                _chat.FailAfterTextNext = true;
                await DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent("fails after text")]));
                break;
            case "cancelled":
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _chat.BeforeNext = async ct =>
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                };
                var interrupted = DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent("interrupted")]));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var turnId = (await service.GetThreadAsync(ThreadId)).Turns[^1].Id;
                await service.CancelTurnAsync(ThreadId, turnId);
                await interrupted.WaitAsync(TimeSpan.FromSeconds(5));
                break;
            case "rollback":
                await DrainAsync(service.SubmitInputAsync(ThreadId, [new TextContent("rolled-back")]));
                break;
        }
    }

    private static string Conversation(IReadOnlyList<ChatMessage> messages) =>
        string.Join("\n", messages.Select(message => message.Role + ":" + JsonSerializer.Serialize(
            ThreadStore.EncodeModelHistory([message])[0].Contents,
            SessionJsonOptions.Default)));

    private AgentFactory CreateFactory(string scenario)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Compaction.ContextWindow = 10_000;
        config.Compaction.SummaryReserveTokens = 1_000;
        config.Compaction.AutoCompactBufferTokens = 500;
        config.Compaction.WarningBufferTokens = 500;
        config.Compaction.ErrorBufferTokens = 250;
        config.Compaction.KeepRecentMinTokens = 1;
        config.Compaction.KeepRecentMinGroups = 1;
        config.Compaction.KeepRecentMaxTokens = 500;
        config.Compaction.MicrocompactEnabled = false;
        if (scenario == "post-turn")
            config.Compaction.PostTurnCompactThresholdPercent = 50;
        var catalogDirectory = Path.Combine(_workspace, "catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(catalogDirectory);
        config.WorkspaceConfigPath = Path.Combine(catalogDirectory, "config.json");
        File.WriteAllText(Path.Combine(catalogDirectory, "models.json"), JsonSerializer.Serialize(new
        {
            models = new Dictionary<string, object>
            {
                [ModelProviderResolver.ResolveConfiguredModel(config)!] = new { contextWindow = config.Compaction.ContextWindow }
            }
        }));
        return new AgentFactory(
            dotcraftPath: CraftPath,
            workspacePath: _workspace,
            config: config,
            memoryStore: new MemoryStore(CraftPath),
            skillsLoader: new SkillsLoader(CraftPath),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: _chat,
            toolSources: Array.Empty<IToolSource>(),
            compactionChatClient: new SummaryChatClient());
    }

    private static SessionService CreateService(AgentFactory factory, ThreadStore store) => new(
        factory,
        factory.CreateAgentWithTools(
            [AIFunctionFactory.Create(() => "tool-result", name: "GetStatus")],
            null,
            factory.RuntimeContext,
            "Use the status tool."),
        new SessionPersistenceService(store),
        new SessionGate());

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temporary files.
        }
    }

    private sealed class ScriptedChatClient : IChatClient
    {
        private int _calls;

        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

        public bool ToolNext { get; set; }

        public bool FailAfterTextNext { get; set; }

        public long? InputTokensNext { get; set; }

        public Func<CancellationToken, Task>? BeforeNext { get; set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            var call = ++_calls;
            if (BeforeNext is { } before)
            {
                BeforeNext = null;
                await before(cancellationToken);
            }

            if (ToolNext)
            {
                ToolNext = false;
                yield return new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [new FunctionCallContent($"call_{call}", "GetStatus", new Dictionary<string, object?>())]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent($"answer {call}")]);
            }

            if (FailAfterTextNext)
            {
                FailAfterTextNext = false;
                throw new InvalidOperationException("fixture failure");
            }

            if (InputTokensNext is { } input)
            {
                InputTokensNext = null;
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    AdditionalProperties = new AdditionalPropertiesDictionary
                    {
                        [TokenUsageRequestMetadata.RequestIndexKey] = 1
                    },
                    Contents = [new UsageContent(new UsageDetails { InputTokenCount = input, OutputTokenCount = 10 })]
                };
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class SummaryChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "<summary>compacted context</summary>")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
