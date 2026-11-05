using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionThread = DotCraft.Sessions.SessionThread;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceHookTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceHookTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SSHooks_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task SubmitInputAsync_InjectsSessionStartHookOutputOncePerThreadAndIgnoresExitTwo()
    {
        var chatClient = new CapturingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(
            agentFactory,
            chatClient,
            CreateRunner(
                HookEvent.SessionStart,
                TestHookCommands.EchoAndExit("FIRST_SESSION_START_OUTPUT", 2),
                TestHookCommands.Echo("SECOND_SESSION_START_OUTPUT")));
        var thread = await CreateThreadAsync(service);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("second")]));

        Assert.Equal(2, chatClient.CapturedRequests.Count);
        var first = RequestText(chatClient.CapturedRequests[0]);
        Assert.Contains("## SessionStart Hook Context", first);
        Assert.Contains("FIRST_SESSION_START_OUTPUT", first);
        Assert.Contains("SECOND_SESSION_START_OUTPUT", first);
        var second = RequestText(chatClient.CapturedRequests[1]);
        Assert.Equal(1, Occurrences(second, "## SessionStart Hook Context"));
        Assert.Equal(1, Occurrences(second, "FIRST_SESSION_START_OUTPUT"));
    }

    [Fact]
    public async Task SubmitInputAsync_InjectsUserPromptSubmitAdditionalContextIntoPrompt()
    {
        var marker = "USER_PROMPT_SUBMIT_CONTEXT";
        File.WriteAllText(
            Path.Combine(_tempDir, "hook-output.json"),
            "{\"hookSpecificOutput\":{\"hookEventName\":\"UserPromptSubmit\",\"additionalContext\":\"" + marker + "\"}}");
        var chatClient = new CapturingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(
            agentFactory,
            chatClient,
            CreateRunner(HookEvent.UserPromptSubmit, TestHookCommands.PrintFile("hook-output.json")));
        var thread = await CreateThreadAsync(service);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var text = RequestText(Assert.Single(chatClient.CapturedRequests));
        Assert.Contains(marker, text);
        Assert.DoesNotContain("hookSpecificOutput", text);
    }

    [Fact]
    public async Task SubmitInputAsync_UserPromptSubmitExitTwoBlocksBeforeAgent()
    {
        var chatClient = new CapturingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(
            agentFactory,
            chatClient,
            CreateRunner(HookEvent.UserPromptSubmit, TestHookCommands.StderrAndExit("prompt denied", 2)));
        var thread = await CreateThreadAsync(service);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Empty(chatClient.CapturedRequests);
    }

    [Fact]
    public async Task PreTurnCompaction_KeepsPromptHookInItsOwnTurnAcrossRollbackAndColdResume()
    {
        const string marker = "PENDING_PROMPT_HOOK_CONTEXT";
        SessionThread thread;
        var seedClient = new CapturingChatClient("seed answer");
        await using (var seedFactory = CreateAgentFactory(seedClient, enableCompaction: true))
        {
            var seedService = CreateService(seedFactory, seedClient, CreateRunner(HookEvent.UserPromptSubmit));
            thread = await CreateThreadAsync(seedService);
            for (var index = 0; index < 4; index++)
                await DrainAsync(seedService.SubmitInputAsync(thread.Id, [new TextContent($"seed {index} " + new string('u', 1200))]));
            await new ThreadStore(_tempDir).SaveContextUsageTokensAsync(
                thread.Id, 9_500, source: "history_estimate", isEstimate: true);
        }

        var previousTurnId = thread.Turns[^1].Id;
        File.WriteAllText(Path.Combine(_tempDir, "prompt-hook.json"),
            "{\"hookSpecificOutput\":{\"hookEventName\":\"UserPromptSubmit\",\"additionalContext\":\"" + marker + "\"}}");
        var chatClient = new CapturingChatClient("answer");
        await using var factory = CreateAgentFactory(chatClient, enableCompaction: true);
        var service = CreateService(factory, chatClient,
            CreateRunner(HookEvent.UserPromptSubmit, TestHookCommands.PrintFile("prompt-hook.json")));
        await service.ResumeThreadAsync(thread.Id);
        var events = new List<SessionEvent>();
        await foreach (var item in service.SubmitInputAsync(thread.Id, [new TextContent("pending question")]))
            events.Add(item);

        Assert.Contains(events, item => item.SystemEventPayload?.Kind == "compacted");
        var input = Assert.Single(chatClient.CapturedRequests[^1], message =>
            message.Role == ChatRole.User && MessageText(message).Contains("pending question", StringComparison.Ordinal));
        Assert.Contains(marker, MessageText(input));
        var checkpoint = (await ReadRolloutRecordsAsync(thread.Id)).Single(record => record.ContextCompacted != null).ContextCompacted!;
        Assert.Equal(previousTurnId, checkpoint.CoveredThroughTurnId);
        Assert.DoesNotContain(marker, JsonSerializer.Serialize(checkpoint.ReplacementHistory, SessionJsonOptions.Default));

        await service.RollbackThreadAsync(thread.Id, 1);
        var coldClient = new CapturingChatClient("continued");
        await using var coldFactory = CreateAgentFactory(coldClient, enableCompaction: true);
        var coldService = CreateService(coldFactory, coldClient, CreateRunner(HookEvent.UserPromptSubmit));
        await coldService.ResumeThreadAsync(thread.Id);
        await DrainAsync(coldService.SubmitInputAsync(thread.Id, [new TextContent("replacement question")]));
        Assert.DoesNotContain(marker, RequestText(Assert.Single(coldClient.CapturedRequests)));
    }

    [Fact]
    public async Task ManualCompaction_PreservesDeliveredSessionStartContextWithoutRerunningHook()
    {
        const string marker = "ONCE_ONLY_SESSION_CONTEXT";
        var chatClient = new CapturingChatClient("answer");
        await using var factory = CreateAgentFactory(chatClient, enableCompaction: true);
        var service = CreateService(factory, chatClient, CreateRunner(HookEvent.SessionStart,
            TestHookCommands.AppendLine("session-start-runs.txt", "run"), TestHookCommands.Echo(marker)));
        var thread = await CreateThreadAsync(service);
        for (var index = 0; index < 4; index++)
            await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent($"turn {index} " + new string('u', 1200))]));

        Assert.Equal(1, Occurrences(RequestText(chatClient.CapturedRequests[^1]), marker));
        var compacted = await service.CompactThreadAsync(thread.Id);
        Assert.Equal("partial", compacted.Outcome);
        var checkpoint = (await ReadRolloutRecordsAsync(thread.Id)).Last(record => record.ContextCompacted != null).ContextCompacted!;
        Assert.Contains(marker, JsonSerializer.Serialize(checkpoint.ReplacementHistory, SessionJsonOptions.Default));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("continue after compaction")]));

        Assert.Equal(1, Occurrences(RequestText(chatClient.CapturedRequests[^1]), marker));
        Assert.Single(await File.ReadAllLinesAsync(Path.Combine(_tempDir, "session-start-runs.txt")));
    }

    private async Task<ThreadRolloutRecord[]> ReadRolloutRecordsAsync(string threadId)
    {
        var path = Directory.GetFiles(_tempDir, threadId + ".jsonl", SearchOption.AllDirectories).Single();
        return (await File.ReadAllLinesAsync(path))
            .Select(line => JsonSerializer.Deserialize<ThreadRolloutRecord>(line, SessionJsonOptions.Default)!).ToArray();
    }

    private SessionService CreateService(AgentFactory agentFactory, IChatClient chatClient, HookRunner hookRunner)
    {
        var defaultAgent = chatClient.AsAIAgent();
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            hookRunner: hookRunner);
    }

    private AgentFactory CreateAgentFactory(IChatClient chatClient, bool enableCompaction = false)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        if (enableCompaction)
        {
            config.Compaction.ContextWindow = 10_000;
            config.Compaction.SummaryReserveTokens = 1_000;
            config.Compaction.AutoCompactBufferTokens = 500;
            config.Compaction.WarningBufferTokens = 500;
            config.Compaction.ErrorBufferTokens = 250;
            config.Compaction.KeepRecentMinTokens = 1;
            config.Compaction.KeepRecentMinGroups = 1;
            config.Compaction.KeepRecentMaxTokens = 500;
            config.Compaction.MicrocompactEnabled = false;
            config.WorkspaceConfigPath = Path.Combine(_tempDir, "config.json");
            File.WriteAllText(Path.Combine(_tempDir, "models.json"), JsonSerializer.Serialize(new
            {
                models = new Dictionary<string, object>
                {
                    [ModelProviderResolver.ResolveConfiguredModel(config)!] = new { contextWindow = config.Compaction.ContextWindow }
                }
            }));
        }
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolSources: Array.Empty<IToolSource>(),
            compactionChatClient: enableCompaction ? new CapturingChatClient("<summary>compacted hook history</summary>") : null);
    }

    private HookRunner CreateRunner(HookEvent evt, params string[] commands)
    {
        var config = new HooksFileConfig
        {
            Hooks =
            {
                [evt.ToString()] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            ..commands.Select(command => new HookEntry
                            {
                                Type = "command",
                                Command = command,
                                Shell = TestHookCommands.Shell,
                                Timeout = 10
                            })
                        ]
                    }
                ]
            }
        };

        return new HookRunner(config, _tempDir);
    }

    private async Task<SessionThread> CreateThreadAsync(SessionService service) =>
        await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });

    private static string MessageText(ChatMessage message) =>
        string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));

    private static string RequestText(IEnumerable<ChatMessage> request) =>
        string.Join(Environment.NewLine, request.Select(MessageText));

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class CapturingChatClient(string responseText) : IChatClient
    {
        public List<List<ChatMessage>> CapturedRequests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedRequests.Add(chatMessages.ToList());
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
