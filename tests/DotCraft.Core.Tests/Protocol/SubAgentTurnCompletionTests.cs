using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Sessions;
using DotCraft.Tests.Contributions;
using Microsoft.Extensions.AI;
using Xunit;
using TestableSessionService = DotCraft.Tests.Sessions.Protocol.AppServer.CoreTestableSessionService;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentTurnCompletionTests
{
    [Theory]
    [InlineData(TurnStatus.Completed)]
    [InlineData(TurnStatus.Failed)]
    [InlineData(TurnStatus.Cancelled)]
    public async Task Spawn_ReturnsCommittedOutcomeWhileTurnEndedIsStillRunning(TurnStatus status)
    {
        using var host = new ContributionAgentHost("SubAgentCompletion");
        var blocker = new BlockingTurnContributor();
        var contributions = new ContributionRegistry();
        contributions.Add<ITurnLifecycleContributor>(blocker);
        var cancellable = new CancellableClient();
        IChatClient client = status switch
        {
            TurnStatus.Failed => new FailingContributionChatClient(),
            TurnStatus.Cancelled => cancellable,
            _ => new ContributionChatClient("done")
        };
        var factory = host.CreateFactory(contributions, client);
        await using var store = new ThreadStore(host.WorkspacePath);
        var service = new SessionService(factory, client.AsAIAgent(), new SessionPersistenceService(store), new SessionGate());
        var context = await CreateContextAsync(service, host.WorkspacePath);
        string? childId = null;
        var spawn = SubAgentSessionControl.SpawnAgentAsync(context, new()
        {
            AgentPrompt = "work", TaskName = "worker", ForkTurns = "none", SilentCompletion = true,
            ChildCreated = (child, _) => { childId = child.Id; return Task.CompletedTask; }
        }, true, null, CancellationToken.None);
        try
        {
            if (status == TurnStatus.Cancelled)
            {
                await cancellable.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var child = await service.GetThreadAsync(childId!);
                await service.CancelTurnAsync(child.Id, Assert.Single(child.Turns).Id);
            }
            await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var result = await spawn.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(status.ToString().ToLowerInvariant(), result.Status);
            var saved = await store.LoadThreadAsync(result.ChildThreadId);
            Assert.Equal(status, Assert.Single(saved!.Turns).Status);
            Assert.False(blocker.Exited.Task.IsCompleted);
            var tasks = new BackgroundTaskRegistry([new SubAgentTaskKind(service)]);
            var task = await tasks.FindAsync(new(context.ParentThread, context.RootThreadId), result.AgentPath!);
            Assert.NotEqual(BackgroundTaskStatus.Running, task.Status);
        }
        finally
        {
            blocker.Release.TrySetResult();
            if (blocker.Entered.Task.IsCompletedSuccessfully)
                await blocker.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task QueuedNativeTurn_NotifiesBeforeItsTurnEndedContributorFinishes()
    {
        using var host = new ContributionAgentHost("QueuedSubAgentCompletion");
        var blocker = new BlockingTurnContributor(blockInvocation: 2);
        var contributions = new ContributionRegistry();
        contributions.Add<ITurnLifecycleContributor>(blocker);
        var client = new ContributionChatClient("done");
        var factory = host.CreateFactory(contributions, client);
        factory.RuntimeContext.Config.Tools.BackgroundTasks.CompletionDelivery = "off";
        await using var store = new ThreadStore(host.WorkspacePath);
        var service = new SessionService(factory, client.AsAIAgent(), new SessionPersistenceService(store), new SessionGate());
        var context = await CreateContextAsync(service, host.WorkspacePath);
        using var observation = await service.ObserveThreadAsync(context.ParentThread.Id);
        await using var notifications = observation.Events.GetAsyncEnumerator();
        var initialNotice = ReadNotificationCountAsync(notifications, 1);
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(context,
            new() { AgentPrompt = "first", TaskName = "worker", ForkTurns = "none" }, true, null, CancellationToken.None);
        await initialNotice.WaitAsync(TimeSpan.FromSeconds(10));
        var queuedNotice = ReadNotificationCountAsync(notifications, 2);
        var queued = await service.EnqueueTurnInputAsync(spawned.ChildThreadId, [new TextContent("follow up")]);
        try
        {
            await service.StartQueuedTurnAsync(spawned.ChildThreadId, queued.Id);
            await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await queuedNotice.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(blocker.Exited.Task.IsCompleted);
            var saved = await store.LoadThreadAsync(spawned.ChildThreadId);
            Assert.Equal(2, saved!.Turns.Count);
            Assert.Equal(TurnStatus.Completed, saved.Turns[^1].Status);
        }
        finally
        {
            blocker.Release.TrySetResult();
            if (blocker.Entered.Task.IsCompletedSuccessfully)
                await blocker.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Spawn_StreamWithoutTerminalOutcomeDoesNotReturnSuccess()
    {
        using var host = new ContributionAgentHost("SubAgentMissingOutcome");
        await using var store = new ThreadStore(host.WorkspacePath);
        var service = new TestableSessionService(store)
        {
            SubmitInputHandler = (threadId, _, _) => [new SessionEvent
            {
                ThreadId = threadId, TurnId = "turn_001", EventType = SessionEventType.TurnStarted
            }]
        };
        var parent = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test", UserId = "user", WorkspacePath = host.WorkspacePath
        });
        var context = new SubAgentSessionContext
        {
            SessionService = service, ParentThread = parent, RootThreadId = parent.Id, ParentTurnId = "parent"
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SubAgentSessionControl.SpawnAgentAsync(
            context, new() { AgentPrompt = "work", TaskName = "worker", ForkTurns = "none" }, true, null, CancellationToken.None));
        Assert.Contains("terminal Turn result", error.Message);
    }

    private static async Task<SubAgentSessionContext> CreateContextAsync(SessionService service, string workspace)
    {
        var parent = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test", UserId = "user", WorkspacePath = workspace
        });
        return new() { SessionService = service, ParentThread = parent, ParentTurnId = "parent", RootThreadId = parent.Id };
    }

    private static async Task ReadNotificationCountAsync(IAsyncEnumerator<SessionEvent> events, int count)
    {
        while (await events.MoveNextAsync())
            if (events.Current.ThreadQueueUpdatedPayload?.QueuedInputs.Count == count)
                return;
        throw new InvalidOperationException("Notification stream closed before the expected task ending.");
    }

    private sealed class BlockingTurnContributor(int blockInvocation = 1) : ITurnLifecycleContributor
    {
        private int _invocations;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task OnTurnEndedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _invocations) != blockInvocation) return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            Exited.TrySetResult();
        }
    }

    private sealed class CancellableClient : IChatClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
