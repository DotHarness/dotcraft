using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.GeneratedTools.Core;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionTerminalNoticeTests : IAsyncLifetime
{
    private const string NoticeTag = "<task_notification>";

    private readonly string _tempDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "SessionTerminalNotice_" + Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task IdleThread_IsWokenByACompletionNotice()
    {
        var chatClient = new NoticeAwareChatClient(DelayedEchoCommand("notice-tail"), keepWorking: false);
        await using var harness = await CreateHarnessAsync(chatClient);

        await harness.RunTurnAsync();
        var thread = await WaitForThreadAsync(harness, t => t.Turns.Count == 2 && t.Turns[1].Status == TurnStatus.Completed);

        var woken = thread.Turns[1].Input?.AsUserMessage;
        Assert.NotNull(woken);
        Assert.Equal(SessionService.TerminalTriggerKind, woken.TriggerKind);
        Assert.StartsWith("term_", woken.TriggerRefId);
        Assert.Equal(BackgroundTerminalStatus.Completed, woken.TriggerStatus);
        var notice = Assert.Single(chatClient.ReceivedNotices);
        Assert.Contains($"<task_id>{woken.TriggerRefId}</task_id>", notice);
        Assert.Contains("notice-tail", notice);
        Assert.Contains("exit code 0", notice);
    }

    [Fact]
    public async Task ClientStop_WakesTheThreadWithAUserStopNotice()
    {
        var chatClient = new NoticeAwareChatClient(LongRunningCommand(), keepWorking: false);
        await using var harness = await CreateHarnessAsync(chatClient);

        await harness.RunTurnAsync();
        var running = Assert.Single(await harness.Terminals.ListAsync(harness.ThreadId));
        await harness.Terminals.StopAsync(running.SessionId);
        var thread = await WaitForThreadAsync(harness, t => t.Turns.Count == 2 && t.Turns[1].Status == TurnStatus.Completed);

        Assert.Equal(BackgroundTerminalStatus.Killed, thread.Turns[1].Input?.AsUserMessage?.TriggerStatus);
        var notice = Assert.Single(chatClient.ReceivedNotices);
        Assert.Contains("was stopped by the user", notice);
    }

    [Fact]
    public async Task RunningTurn_ReceivesTheNoticeAsGuidance()
    {
        var chatClient = new NoticeAwareChatClient(DelayedEchoCommand("guided-tail"), keepWorking: true);
        await using var harness = await CreateHarnessAsync(chatClient);

        await harness.RunTurnAsync();
        await Task.Delay(500);

        var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
        var turn = Assert.Single(thread.Turns);
        var guidance = Assert.Single(turn.Items, item =>
            item.AsUserMessage?.TriggerKind == SessionService.TerminalTriggerKind);
        Assert.Equal("guidance", guidance.AsUserMessage!.DeliveryMode);
        Assert.Contains("guided-tail", Assert.Single(chatClient.ReceivedNotices));
    }

    [Fact]
    public async Task TaskStop_StopsTheTerminalAndNoNoticeFollows()
    {
        var chatClient = new StopAfterStartChatClient(LongRunningCommand());
        await using var harness = await CreateHarnessAsync(chatClient);

        await harness.RunTurnAsync();
        await Task.Delay(500);
        await harness.Service.DrainTerminalNoticesAsync();

        var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
        var turn = Assert.Single(thread.Turns);
        Assert.Empty(thread.QueuedInputs);
        Assert.DoesNotContain(turn.Items, item => item.AsUserMessage?.TriggerKind == SessionService.TerminalTriggerKind);
        var call = Assert.Single(turn.Items, item => item.AsToolCall?.ToolName == "TaskStop").AsToolCall!;
        Assert.Equal("shell", call.Presentation?.Options?["kind"]?.GetValue<string>());
        using var result = JsonDocument.Parse(Assert.Single(turn.Items, item => item.AsToolResult?.ToolName == "TaskStop").AsToolResult!.Result);
        Assert.Equal("shell", result.RootElement.GetProperty("kind").GetString());
        Assert.Equal("stopped", result.RootElement.GetProperty("status").GetString());
        var terminal = Assert.Single(await harness.Terminals.ListAsync(harness.ThreadId));
        Assert.Equal(terminal.SessionId, result.RootElement.GetProperty("taskId").GetString());
        Assert.Equal(BackgroundTerminalStatus.Killed, terminal.Status);
    }

    [Fact]
    public async Task TaskStop_RejectsATerminalStartedByAnotherThread()
    {
        var chatClient = new NoticeAwareChatClient(LongRunningCommand(), keepWorking: false);
        await using var harness = await CreateHarnessAsync(chatClient);
        await harness.RunTurnAsync();
        var running = Assert.Single(await harness.Terminals.ListAsync(harness.ThreadId));
        var other = await harness.Service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user",
            WorkspacePath = _tempDir
        });

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            harness.Service.BackgroundTasks.StopAsync(new BackgroundTaskCaller(other, other.Id), running.SessionId));

        Assert.Equal(BackgroundTerminalStatus.Running, (await harness.Terminals.ReadAsync(running.SessionId)).Status);
    }

    private async Task<Harness> CreateHarnessAsync(IChatClient chatClient)
    {
        var terminals = new BackgroundTerminalService(_tempDir, new AppConfig.ShellBackgroundConfig());
        var config = AppConfigTestFactory.CreateOpenAI();
        var recorder = new ToolInvocationRecorderRouter();
        var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new SessionScopedApprovalService(new AutoApproveApprovalService()),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolDispatcher: new ToolDispatcher(recorder: recorder),
            toolSources: [new ShellToolSource(new ShellTools(_tempDir, terminals, timeoutSeconds: 30))]);
        var service = new SessionService(
            agentFactory,
            new StreamingFunctionInvokingChatClient(chatClient).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            backgroundTerminalService: terminals);
        recorder.Bind(service);
        service.AttachTerminalNotices();
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user",
            WorkspacePath = _tempDir
        });
        await service.RefreshThreadAgentAsync(thread.Id);
        return new Harness(service, terminals, agentFactory, thread.Id);
    }

    private static async Task<SessionThread> WaitForThreadAsync(Harness harness, Func<SessionThread, bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (true)
        {
            var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
            if (condition(thread))
                return thread;
            Assert.True(DateTimeOffset.UtcNow < deadline, "The thread did not reach the expected state.");
            await Task.Delay(100);
        }
    }

    private static string DelayedEchoCommand(string text) =>
        OperatingSystem.IsWindows()
            ? $"Start-Sleep -Milliseconds 800; Write-Output '{text}'"
            : $"sleep 0.8; echo '{text}'";

    private static string LongRunningCommand() =>
        OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 60" : "sleep 60";

    private static string QuickCommand() =>
        OperatingSystem.IsWindows() ? "Start-Sleep -Milliseconds 300; Write-Output tick" : "sleep 0.3; echo tick";

    private sealed class Harness(
        SessionService service,
        BackgroundTerminalService terminals,
        AgentFactory agentFactory,
        string threadId) : IAsyncDisposable
    {
        public SessionService Service { get; } = service;

        public BackgroundTerminalService Terminals { get; } = terminals;

        public string ThreadId { get; } = threadId;

        public async Task RunTurnAsync()
        {
            await foreach (var _ in Service.SubmitInputAsync(ThreadId, [new TextContent("run the build")]))
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DrainTerminalNoticesAsync();
            await Terminals.DisposeAsync();
            await agentFactory.DisposeAsync();
        }
    }

    private sealed class ShellToolSource(ShellTools shellTools) : AIFunctionToolSource
    {
        public override string SourceId => "terminal-notice-test";

        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            yield return GeneratedToolFunctions.ShellTools_Exec(shellTools);
            yield return GeneratedToolFunctions.BackgroundTaskTools_TaskStop(new BackgroundTaskTools());
        }
    }

    private sealed class NoticeAwareChatClient(string command, bool keepWorking) : IChatClient
    {
        private int _calls;

        public ConcurrentQueue<string> ReceivedNotices { get; } = new();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var messages = chatMessages.ToList();
            var call = Interlocked.Increment(ref _calls);
            var notice = messages.LastOrDefault(message => message.Text.Contains(NoticeTag, StringComparison.Ordinal));
            if (notice != null && !ReceivedNotices.Contains(notice.Text))
            {
                ReceivedNotices.Enqueue(notice.Text);
                yield return new ChatResponseUpdate(ChatRole.Assistant, "noted");
                yield break;
            }

            if (call == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("call-build", "Exec", new Dictionary<string, object?>
                    {
                        ["command"] = command,
                        ["runInBackground"] = true,
                        ["yieldTimeMs"] = 100
                    })
                ]);
                yield break;
            }

            if (keepWorking && call < 60)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent($"call-tick-{call}", "Exec", new Dictionary<string, object?>
                    {
                        ["command"] = QuickCommand()
                    })
                ]);
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StopAfterStartChatClient(string command) : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var messages = chatMessages.ToList();
            switch (Interlocked.Increment(ref _calls))
            {
                case 1:
                    yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [
                        new FunctionCallContent("call-build", "Exec", new Dictionary<string, object?>
                        {
                            ["command"] = command,
                            ["runInBackground"] = true,
                            ["yieldTimeMs"] = 100
                        })
                    ]);
                    break;
                case 2:
                    var started = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single().Result?.ToString() ?? string.Empty;
                    var sessionId = System.Text.RegularExpressions.Regex.Match(started, "term_[0-9a-f]{12}").Value;
                    yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [
                        new FunctionCallContent("call-stop", "TaskStop", new Dictionary<string, object?> { ["taskId"] = sessionId })
                    ]);
                    break;
                default:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
                    break;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
