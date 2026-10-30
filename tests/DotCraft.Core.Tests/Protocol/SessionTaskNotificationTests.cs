using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionTaskNotificationTests : IAsyncLifetime
{
    private const string EnvelopeTag = "<task_notification>";

    private readonly string _tempDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "SessionTaskNotification_" + Guid.NewGuid().ToString("N"));

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

    [Theory]
    [InlineData(BackgroundTaskKinds.Shell, "term_0123456789ab", "terminal", "Background command finished (exit code 0): npm run build")]
    [InlineData(BackgroundTaskKinds.Agent, "/root/docs_reviewer", "subagent", "Subagent docs_reviewer finished")]
    [InlineData(BackgroundTaskKinds.Workflow, "run_20261017_abc123", "workflow", "Workflow review-change finished")]
    public async Task IdleThread_IsWokenWithTheSharedEnvelope(string kind, string taskId, string triggerKind, string displayText)
    {
        await using var harness = await CreateHarnessAsync();
        var label = kind switch
        {
            BackgroundTaskKinds.Shell => "npm run build",
            BackgroundTaskKinds.Agent => "docs_reviewer",
            _ => "review-change"
        };

        await harness.Service.NotifyBackgroundTaskEndedAsync(Ending(harness, kind, taskId, label) with
        {
            ExitCode = kind == BackgroundTaskKinds.Shell ? 0 : null,
            OutputPath = "out/" + taskId,
            Result = "the result"
        });
        var thread = await WaitForThreadAsync(harness, t => t.Turns.Count == 1 && t.Turns[0].Status == TurnStatus.Completed);

        var input = thread.Turns[0].Input!.AsUserMessage!;
        Assert.Equal(triggerKind, input.TriggerKind);
        Assert.Equal(label, input.TriggerLabel);
        Assert.Equal(taskId, input.TriggerRefId);
        Assert.Equal("completed", input.TriggerStatus);
        Assert.Equal(displayText, input.Text);
        var envelope = Assert.Single(harness.Client.Notifications);
        Assert.Contains(EnvelopeTag, envelope);
        Assert.Contains($"<task_id>{taskId}</task_id>", envelope);
        Assert.Contains("<status>completed</status>", envelope);
        Assert.Contains($"<output_file>out/{taskId}</output_file>", envelope);
        Assert.Contains("the result", envelope);
        Assert.Empty(thread.QueuedInputs);
    }

    [Theory]
    [InlineData(BackgroundTaskKinds.Agent, "/root/worker")]
    [InlineData(BackgroundTaskKinds.Workflow, "run_20261017_abc123")]
    public async Task RunningTurn_DrainsTheNotificationAtItsNextSamplingBoundary(string kind, string taskId)
    {
        await using var harness = await CreateHarnessAsync();
        harness.Client.HoldFirstRequest();
        var turn = harness.RunUserTurnAsync("work");
        await harness.Client.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(10));

        await harness.Service.NotifyBackgroundTaskEndedAsync(Ending(harness, kind, taskId, "worker"));
        harness.Client.ReleaseFirstRequest();
        await turn;

        var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
        var only = Assert.Single(thread.Turns);
        var guidance = Assert.Single(only.Items, item => item.AsUserMessage?.TriggerRefId == taskId).AsUserMessage!;
        Assert.Equal("guidance", guidance.DeliveryMode);
        Assert.Contains(taskId, Assert.Single(harness.Client.Notifications));
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task TasksEndingTogether_WakeTheIdleThreadOnce()
    {
        await using var harness = await CreateHarnessAsync();
        harness.Client.HoldFirstRequest();

        await harness.Service.NotifyBackgroundTaskEndedAsync(Ending(harness, BackgroundTaskKinds.Shell, "term_000000000001", "first"));
        await harness.Client.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(10));
        await harness.Service.NotifyBackgroundTaskEndedAsync(Ending(harness, BackgroundTaskKinds.Agent, "/root/second", "second"));
        harness.Client.ReleaseFirstRequest();
        var thread = await WaitForThreadAsync(harness, t => t.Turns.Count == 1 && t.Turns[0].Status == TurnStatus.Completed);

        Assert.Equal(
            ["term_000000000001", "/root/second"],
            thread.Turns[0].Items.Select(item => item.AsUserMessage?.TriggerRefId).OfType<string>().ToArray());
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task DeliveryOff_KeepsNotificationsQueuedAndTheNextTurnAdmitsThemTogether()
    {
        await using var harness = await CreateHarnessAsync(config => config.Tools.BackgroundTasks.CompletionDelivery = "off");

        await harness.Service.NotifyBackgroundTaskEndedAsync(Ending(harness, BackgroundTaskKinds.Shell, "term_000000000001", "first"));
        await harness.Service.NotifyBackgroundTaskEndedAsync(Ending(harness, BackgroundTaskKinds.Workflow, "run_20261017_abc123", "second"));
        var held = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Empty(held.Turns);
        Assert.Equal(2, held.QueuedInputs.Count);

        await harness.RunUserTurnAsync("what happened?");

        var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
        var turn = Assert.Single(thread.Turns);
        Assert.Null(turn.Input!.AsUserMessage!.TriggerKind);
        Assert.Equal(
            ["term_000000000001", "run_20261017_abc123"],
            turn.Items.Select(item => item.AsUserMessage?.TriggerRefId).OfType<string>().ToArray());
        Assert.Empty(thread.QueuedInputs);
        Assert.Equal(2, harness.Client.Notifications.Count);
    }

    [Fact]
    public async Task WakeLoop_IsHeldAfterTheLimitAndDeliveredWithTheNextUserInput()
    {
        await using var harness = await CreateHarnessAsync(config => config.Tools.BackgroundTasks.MaxConsecutiveWakes = 1);
        await harness.RunUserTurnAsync("start the build");

        await harness.Service.NotifyBackgroundTaskEndedAsync(
            Ending(harness, BackgroundTaskKinds.Shell, "term_000000000001", "first") with { StartTurnId = "turn_001" });
        var woken = await WaitForThreadAsync(harness, t => t.TurnCount == 2 && t.Turns[^1].Status == TurnStatus.Completed);
        await harness.Service.NotifyBackgroundTaskEndedAsync(
            Ending(harness, BackgroundTaskKinds.Shell, "term_000000000002", "second") with { StartTurnId = woken.Turns[^1].Id });
        await Task.Delay(300);

        var held = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Equal(2, held.TurnCount);
        Assert.Equal("term_000000000002", Assert.Single(held.QueuedInputs).TriggerRefId);

        await harness.RunUserTurnAsync("status?");

        var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Equal(3, thread.TurnCount);
        Assert.Contains(thread.Turns[^1].Items, item => item.AsUserMessage?.TriggerRefId == "term_000000000002");
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task WakeLimit_DoesNotHoldNotificationsWhoseWokenTurnsStartedNoTask()
    {
        await using var harness = await CreateHarnessAsync(config => config.Tools.BackgroundTasks.MaxConsecutiveWakes = 1);
        await harness.RunUserTurnAsync("start two builds");

        await harness.Service.NotifyBackgroundTaskEndedAsync(
            Ending(harness, BackgroundTaskKinds.Shell, "term_000000000001", "first") with { StartTurnId = "turn_001" });
        await WaitForThreadAsync(harness, t => t.TurnCount == 2 && t.Turns[^1].Status == TurnStatus.Completed);
        await harness.Service.NotifyBackgroundTaskEndedAsync(
            Ending(harness, BackgroundTaskKinds.Shell, "term_000000000002", "second") with { StartTurnId = "turn_001" });

        await WaitForThreadAsync(harness, t => t.TurnCount == 3 && t.Turns[^1].Status == TurnStatus.Completed);
    }

    [Fact]
    public async Task AgentMessage_WakesAnIdleRootWithTheMessage()
    {
        await using var harness = await CreateHarnessAsync();
        var worker = await ChildContextAsync(harness, "worker");

        await SubAgentSessionControl.SendAgentMessageAsync(worker, "/root", "found the cause", coordinator: null, CancellationToken.None);

        var thread = await WaitForThreadAsync(harness, t => t.TurnCount == 1 && t.Turns[0].Status == TurnStatus.Completed);
        var input = thread.Turns[0].Input!.AsUserMessage!;
        Assert.Equal(SubAgentMailboxDelivery.DeliveryMode, input.TriggerKind);
        Assert.Contains("found the cause", string.Concat(input.MaterializedInputParts!.Select(part => part.Text)));
        Assert.Empty(await harness.Service.ListPendingSubAgentMailboxAsync(harness.ThreadId, AgentPath.Root));
    }

    [Fact]
    public async Task BoardPost_DoesNotWakeAnIdleExplicitRecipient()
    {
        await using var harness = await CreateHarnessAsync();
        var worker = await ChildContextAsync(harness, "worker");

        await harness.Service.ExecuteBoardAsync(
            worker,
            new BoardRequest("post", NewChannelName: "work", Text: "ready for review", AgentsToNotify: ["/root"]),
            "post",
            CancellationToken.None);
        await Task.Delay(300);

        Assert.Empty((await harness.Service.GetThreadAsync(harness.ThreadId)).Turns);
        Assert.Empty(await harness.Service.ListPendingSubAgentMailboxAsync(harness.ThreadId, AgentPath.Root));
    }

    [Fact]
    public async Task ArchivedThread_KeepsTheNotificationQueuedForItsNextTurn()
    {
        await using var harness = await CreateHarnessAsync();
        await harness.Service.ArchiveThreadAsync(harness.ThreadId);

        await harness.Service.NotifyBackgroundTaskEndedAsync(Ending(harness, BackgroundTaskKinds.Agent, "/root/worker", "worker"));
        await Task.Delay(200);
        var archived = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Empty(archived.Turns);
        Assert.Equal("/root/worker", Assert.Single(archived.QueuedInputs).TriggerRefId);

        await harness.Service.UnarchiveThreadAsync(harness.ThreadId);
        await harness.RunUserTurnAsync("continue");

        var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Contains(Assert.Single(thread.Turns).Items, item => item.AsUserMessage?.TriggerRefId == "/root/worker");
        Assert.Empty(thread.QueuedInputs);
    }

    [Theory]
    [InlineData(BackgroundTaskKinds.Agent, "/root/worker", "Subagent worker was stopped by the user")]
    [InlineData(BackgroundTaskKinds.Workflow, "run_20261017_abc123", "Workflow worker was stopped by the user")]
    public async Task UserStop_TellsTheModelTheUserStoppedTheTask(string kind, string taskId, string displayText)
    {
        await using var harness = await CreateHarnessAsync();

        await harness.Service.NotifyBackgroundTaskEndedAsync(Ending(harness, kind, taskId, "worker") with
        {
            Status = BackgroundTaskStatus.Stopped,
            StoppedByUser = true
        });
        var thread = await WaitForThreadAsync(harness, t => t.Turns.Count == 1 && t.Turns[0].Status == TurnStatus.Completed);

        Assert.Equal(displayText, thread.Turns[0].Input!.AsUserMessage!.Text);
        var envelope = Assert.Single(harness.Client.Notifications);
        Assert.Contains("<status>stopped</status>", envelope);
    }

    [Fact]
    public async Task NotificationThatMayNotWake_StaysQueued()
    {
        await using var harness = await CreateHarnessAsync();

        await harness.Service.NotifyBackgroundTaskEndedAsync(
            Ending(harness, BackgroundTaskKinds.Agent, "/root/worker", "worker") with { MayWake = false });
        await Task.Delay(200);

        var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Empty(thread.Turns);
        Assert.Single(thread.QueuedInputs);
    }

    private static BackgroundTaskEnding Ending(Harness harness, string kind, string taskId, string label) => new()
    {
        TaskId = taskId,
        Kind = kind,
        OwnerThreadId = harness.ThreadId,
        Label = label,
        Status = BackgroundTaskStatus.Completed,
        KindStatus = "completed",
        Summary = $"{label} ended."
    };

    private async Task<Harness> CreateHarnessAsync(Action<AppConfig>? configure = null)
    {
        var client = new NotificationChatClient();
        var config = AppConfigTestFactory.CreateOpenAI();
        configure?.Invoke(config);
        var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new SessionScopedApprovalService(new AutoApproveApprovalService()),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: client,
            toolDispatcher: new ToolDispatcher(),
            toolSources: []);
        var service = new SessionService(
            agentFactory,
            new StreamingFunctionInvokingChatClient(client).AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user",
            WorkspacePath = _tempDir
        });
        await service.RefreshThreadAgentAsync(thread.Id);
        return new Harness(service, client, agentFactory, thread.Id, _tempDir);
    }

    private static async Task<SubAgentSessionContext> ChildContextAsync(Harness harness, string name)
    {
        var path = "/root/" + name;
        var child = await harness.Service.CreateThreadAsync(
            new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = harness.WorkspacePath },
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = harness.ThreadId, RootThreadId = harness.ThreadId, AgentPath = path,
                TaskName = name, RuntimeType = "native", Depth = 1
            }));
        await harness.Service.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = harness.ThreadId, ChildThreadId = child.Id, AgentPath = path,
            TaskName = name, RuntimeType = "native", Status = ThreadSpawnEdgeStatus.Open, Depth = 1
        });
        return new SubAgentSessionContext
        {
            SessionService = harness.Service,
            ParentThread = child,
            ParentTurnId = name + "-turn",
            RootThreadId = harness.ThreadId,
            Depth = 1
        };
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
            await Task.Delay(50);
        }
    }

    private sealed class Harness(
        SessionService service,
        NotificationChatClient client,
        AgentFactory agentFactory,
        string threadId,
        string workspacePath) : IAsyncDisposable
    {
        public SessionService Service { get; } = service;

        public NotificationChatClient Client { get; } = client;

        public string ThreadId { get; } = threadId;

        public string WorkspacePath { get; } = workspacePath;

        public async Task RunUserTurnAsync(string text)
        {
            await foreach (var _ in Service.SubmitInputAsync(ThreadId, [new TextContent(text)]))
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.ReleaseFirstRequest();
            await agentFactory.DisposeAsync();
        }
    }

    private sealed class NotificationChatClient : IChatClient
    {
        private readonly TaskCompletionSource _firstRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _firstRequestGate;
        private int _calls;

        public ConcurrentQueue<string> Notifications { get; } = new();

        public Task FirstRequestStarted => _firstRequestStarted.Task;

        public void HoldFirstRequest() =>
            _firstRequestGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseFirstRequest() => _firstRequestGate?.TrySetResult();

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
            var messages = chatMessages.ToList();
            if (Interlocked.Increment(ref _calls) == 1)
            {
                _firstRequestStarted.TrySetResult();
                if (_firstRequestGate is { } gate)
                    await gate.Task.WaitAsync(cancellationToken);
            }
            else
            {
                await Task.Yield();
            }

            foreach (var message in messages.Where(message => message.Text.Contains(EnvelopeTag, StringComparison.Ordinal)))
            {
                if (!Notifications.Contains(message.Text))
                    Notifications.Enqueue(message.Text);
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "noted");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
