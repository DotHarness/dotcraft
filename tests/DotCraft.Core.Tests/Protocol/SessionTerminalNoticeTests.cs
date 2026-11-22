using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.GeneratedTools.Core;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tests.Tools;
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
        var command = GatedEchoCommand("notice-tail", out var releasePath);
        var chatClient = new NoticeAwareChatClient(command, OperatingSystem.IsWindows() ? "powershell" : "sh");
        await using var harness = await CreateHarnessAsync(chatClient);

        await harness.RunTurnAsync();
        var running = Assert.Single(await harness.Terminals.ListAsync(harness.ThreadId));
        Assert.Equal(BackgroundTerminalStatus.Running, running.Status);
        await File.WriteAllTextAsync(releasePath, "");
        var thread = await WaitForThreadAsync(harness, t => t.TurnCount == 2 && t.Turns[^1].Status == TurnStatus.Completed);

        var woken = thread.Turns[^1].Input?.AsUserMessage;
        Assert.NotNull(woken);
        Assert.Equal(SessionService.TerminalTriggerKind, woken.TriggerKind);
        Assert.StartsWith("term_", woken.TriggerRefId);
        Assert.Equal(BackgroundTerminalStatus.Completed, woken.TriggerStatus);
        var notice = Assert.Single(chatClient.ReceivedNotices);
        Assert.Contains($"<task_id>{woken.TriggerRefId}</task_id>", notice);
        Assert.Contains("exit code 0", notice);
    }

    [Fact]
    public async Task SequentialBackgroundCommands_KeepWakingTheThreadWithoutUserInput()
    {
        string[] commands = ["echo first", "echo second", "echo third", "echo fourth"];
        var snapshots = new ConcurrentDictionary<string, BackgroundTerminalSnapshot>();
        var terminals = new StubBackgroundTerminalService
        {
            StartHandler = (request, _) =>
            {
                var terminal = new BackgroundTerminalSnapshot
                {
                    SessionId = "term_" + Guid.NewGuid().ToString("N")[..12],
                    ThreadId = request.ThreadId,
                    TurnId = request.TurnId,
                    CallId = request.CallId,
                    Command = request.Command,
                    WorkingDirectory = request.WorkingDirectory,
                    StartedAt = DateTimeOffset.UtcNow,
                    IsBackground = request.RunInBackground,
                    CompletionNoticeArmed = request.NotifyOnCompletion
                };
                snapshots[terminal.SessionId] = terminal;
                return Task.FromResult(terminal);
            },
            ReadHandler = (sessionId, _) => Task.FromResult(snapshots[sessionId])
        };
        var chatClient = new NoticeAwareChatClient(commands[0], followupCommands: commands[1..]);
        await using var harness = await CreateHarnessAsync(chatClient, terminals);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var observation = await harness.Service.ObserveThreadAsync(harness.ThreadId, ct: timeout.Token);
        await using var events = observation.Events.GetAsyncEnumerator();

        await harness.RunTurnAsync(timeout.Token);
        var completedTurnId = await ReadCompletedTurnIdAsync();
        var terminalIds = new List<string>();
        foreach (var command in commands)
        {
            var running = Assert.Single(snapshots.Values, terminal => terminal.TurnId == completedTurnId);
            Assert.Equal(command, running.Command);
            Assert.True(running.CompletionNoticeArmed);
            var completed = running with
            {
                Status = BackgroundTerminalStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
                ExitCode = 0
            };
            snapshots[completed.SessionId] = completed;
            terminalIds.Add(completed.SessionId);
            terminals.Raise(new BackgroundTerminalEvent { EventType = BackgroundTerminalEventTypes.CompletionNotice, Terminal = completed });
            terminals.Raise(new BackgroundTerminalEvent { EventType = BackgroundTerminalEventTypes.Completed, Terminal = completed });
            completedTurnId = await ReadCompletedTurnIdAsync();
        }

        var turns = await harness.Service.ListThreadTurnsAsync(
            harness.ThreadId, null, commands.Length + 2, ThreadHistorySortDirection.Ascending, timeout.Token);
        Assert.Equal(commands.Length + 1, turns.Data.Count);
        Assert.All(turns.Data, turn => Assert.Equal(TurnStatus.Completed, turn.Status));
        Assert.Equal(terminalIds, turns.Data.Skip(1).Select(turn => turn.Input?.AsUserMessage?.TriggerRefId));
        Assert.Equal(commands.Length, chatClient.ReceivedNotices.Count);
        Assert.Equal(commands.Length, snapshots.Count);
        var thread = await harness.Service.ReadThreadSnapshotAsync(harness.ThreadId, timeout.Token);
        Assert.Empty(thread.Thread.QueuedInputs);

        async Task<string> ReadCompletedTurnIdAsync()
        {
            while (await events.MoveNextAsync())
            {
                var evt = events.Current;
                if (evt.EventType is not (SessionEventType.TurnCompleted or SessionEventType.TurnFailed or SessionEventType.TurnCancelled))
                    continue;
                Assert.Equal(SessionEventType.TurnCompleted, evt.EventType);
                return evt.TurnId!;
            }
            throw new InvalidOperationException("The thread observation ended before the turn completed.");
        }
    }

    [Fact]
    public async Task ClientStop_WakesTheThreadWithAUserStopNotice()
    {
        var chatClient = new NoticeAwareChatClient(LongRunningCommand());
        await using var harness = await CreateHarnessAsync(chatClient);

        await harness.RunTurnAsync();
        var running = Assert.Single(await harness.Terminals.ListAsync(harness.ThreadId));
        await harness.Terminals.StopAsync(running.SessionId);
        var thread = await WaitForThreadAsync(harness, t => t.TurnCount == 2 && t.Turns[^1].Status == TurnStatus.Completed);

        Assert.Equal(BackgroundTerminalStatus.Killed, thread.Turns[^1].Input?.AsUserMessage?.TriggerStatus);
        var notice = Assert.Single(chatClient.ReceivedNotices);
        Assert.Contains("was stopped by the user", notice);
    }

    [Fact]
    public async Task TaskStop_StopsTheTerminalAndNoNoticeFollows()
    {
        var chatClient = new StopAfterStartChatClient(LongRunningCommand());
        await using var harness = await CreateHarnessAsync(chatClient);

        await harness.RunTurnAsync();
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
        var chatClient = new NoticeAwareChatClient(LongRunningCommand());
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

    private async Task<Harness> CreateHarnessAsync(IChatClient chatClient, IBackgroundTerminalService? terminals = null)
    {
        terminals ??= new BackgroundTerminalService(_tempDir, new AppConfig.ShellBackgroundConfig());
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
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
            if (condition(thread))
                return thread;
            Assert.True(DateTimeOffset.UtcNow < deadline, "The thread did not reach the expected state.");
            await Task.Delay(100);
        }
    }

    private string GatedEchoCommand(string text, out string releasePath)
    {
        releasePath = Path.Combine(_tempDir, "release-command-" + Guid.NewGuid().ToString("N"));
        return OperatingSystem.IsWindows()
            ? $"while (-not (Test-Path -LiteralPath '{releasePath.Replace("'", "''")}')) {{ Start-Sleep -Milliseconds 20 }}; Write-Output '{text}'"
            : $"until [ -f '{releasePath}' ]; do sleep 0.02; done; echo '{text}'";
    }

    private static string TestShell => OperatingSystem.IsWindows() ? "cmd" : "sh";

    private static string LongRunningCommand() =>
        OperatingSystem.IsWindows() ? "ping -n 60 127.0.0.1 >nul" : "sleep 60";

    private sealed class Harness(
        SessionService service,
        IBackgroundTerminalService terminals,
        AgentFactory agentFactory,
        string threadId) : IAsyncDisposable
    {
        public SessionService Service { get; } = service;

        public IBackgroundTerminalService Terminals { get; } = terminals;

        public string ThreadId { get; } = threadId;

        public async Task RunTurnAsync(CancellationToken ct = default)
        {
            await foreach (var _ in Service.SubmitInputAsync(ThreadId, [new TextContent("run the build")], ct: ct))
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Service.DrainTerminalNoticesAsync();
            if (Terminals is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
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

    private sealed class NoticeAwareChatClient(string command, string? shell = null, params string[] followupCommands) : IChatClient
    {
        private readonly Queue<string> _commands = new([command, .. followupCommands]);
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
            var shouldStartCommand = call == 1;
            var notice = messages.LastOrDefault(message => message.Text.Contains(NoticeTag, StringComparison.Ordinal));
            if (notice != null && !ReceivedNotices.Contains(notice.Text))
            {
                ReceivedNotices.Enqueue(notice.Text);
                shouldStartCommand = true;
            }

            if (shouldStartCommand && _commands.TryDequeue(out var nextCommand))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent($"call-build-{call}", "Exec", new Dictionary<string, object?>
                    {
                        ["command"] = nextCommand,
                        ["shell"] = shell ?? TestShell,
                        ["runInBackground"] = true,
                        ["yieldTimeMs"] = 100
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
                            ["shell"] = TestShell,
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
