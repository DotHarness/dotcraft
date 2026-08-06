using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.GeneratedTools.Core;
using DotCraft.Memory;
using DotCraft.Persistence;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionShellCancellationTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "SessionShellCancellation_" + Guid.NewGuid().ToString("N"));

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
    public async Task CancelTurnAsync_StopsRunningShellBeforeTurnCancelled()
    {
        await using var terminals = new BackgroundTerminalService(
            _tempDir,
            new AppConfig.ShellBackgroundConfig());
        var terminalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalEvents = new ConcurrentQueue<BackgroundTerminalEvent>();
        terminals.TerminalEvent += terminalEvent =>
        {
            terminalEvents.Enqueue(terminalEvent);
            if (terminalEvent.EventType == "started" && terminalEvent.Terminal.CallId == "call-shell")
                terminalStarted.TrySetResult();
        };

        var chatClient = new ShellCallingChatClient(SleepCommand());
        var recorder = new ToolInvocationRecorderRouter();
        var dispatcher = new ToolDispatcher(recorder: recorder);
        var config = AppConfigTestFactory.CreateOpenAI();
        await using var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new SessionScopedApprovalService(new AutoApproveApprovalService()),
            blacklist: null,
            chatClient: chatClient,
            toolDispatcher: dispatcher,
            toolSources: [new ShellToolSource(new ShellTools(_tempDir, terminals))]);
        var service = new SessionService(
            agentFactory,
            new StreamingFunctionInvokingChatClient(chatClient).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            backgroundTerminalService: terminals);
        recorder.Bind(service);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user",
            WorkspacePath = _tempDir
        });
        await service.RefreshThreadAgentAsync(thread.Id);

        var sessionEvents = new ConcurrentQueue<SessionEvent>();
        var drain = Task.Run(async () =>
        {
            await foreach (var sessionEvent in service.SubmitInputAsync(
                thread.Id,
                [new TextContent("run shell")]))
            {
                sessionEvents.Enqueue(sessionEvent);
            }
        });
        await terminalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var running = await service.GetThreadAsync(thread.Id);
        var activeTurn = Assert.Single(running.Turns);

        await service.CancelTurnAsync(thread.Id, activeTurn.Id);
        await drain.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelled = await service.GetThreadAsync(thread.Id);
        Assert.Equal(TurnStatus.Cancelled, Assert.Single(cancelled.Turns).Status);
        Assert.Single(sessionEvents, sessionEvent => sessionEvent.EventType == SessionEventType.TurnCancelled);
        Assert.Equal(
            BackgroundTerminalStatus.Killed,
            Assert.Single(terminalEvents, terminalEvent =>
                terminalEvent.EventType == "completed"
                && terminalEvent.Terminal.CallId == "call-shell").Terminal.Status);
    }

    private static string SleepCommand() =>
        OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 30" : "sleep 30";

    private sealed class ShellToolSource(ShellTools shellTools) : AIFunctionToolSource
    {
        public override string SourceId => "shell-cancellation-test";

        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            yield return GeneratedToolFunctions.ShellTools_Exec(shellTools);
        }
    }

    private sealed class ShellCallingChatClient(string command) : IChatClient
    {
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
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionCallContent("call-shell", "Exec", new Dictionary<string, object?>
                {
                    ["command"] = command
                })
            ]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
