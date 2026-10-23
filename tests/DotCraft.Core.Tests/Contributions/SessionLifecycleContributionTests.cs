using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Tests.Contributions;

public sealed class SessionLifecycleContributionTests : IDisposable
{
    private readonly ContributionAgentHost _host = new("LifecycleContribution");
    private readonly ContributionRegistry _registry = new();

    [Fact]
    public async Task ThreadContributor_ObservesStartedResumedAndDeletingDespiteAThrowingContributor()
    {
        var recorder = new RecordingThreadContributor();
        _registry.Add<IThreadLifecycleContributor>(new ThrowingThreadContributor(), new ContributionOptions(Order: -1));
        _registry.Add<IThreadLifecycleContributor>(recorder, new ContributionOptions(Order: 1));
        var service = CreateService(new ContributionChatClient());

        var thread = await CreateThreadAsync(service);
        var order = new List<string>();
        _registry.Add<IThreadLifecycleContributor>(
            new DisposableThreadContributor(order),
            ContributionOptions.ForThread(thread.Id));
        await service.ResumeThreadAsync(thread.Id);
        await service.DeleteThreadPermanentlyAsync(thread.Id);

        Assert.Equal(
            [
                $"started:{thread.Id}",
                $"resumed:{thread.Id}",
                $"deleting:{thread.Id}"
            ],
            recorder.Calls);
        Assert.Equal(["deleting", "disposed"], order);
    }

    [Fact]
    public async Task ThreadScopedContributor_ObservesOnlyItsThreadIsNotInheritedByAForkAndIsReleasedAfterItsDeletion()
    {
        var service = CreateService(new ContributionChatClient());
        var thread = await CreateThreadAsync(service);
        var kept = await CreateThreadAsync(service);
        var recorder = new RecordingThreadContributor();
        var order = new List<string>();
        var keptOrder = new List<string>();
        _registry.Add<IThreadLifecycleContributor>(recorder, ContributionOptions.ForThread(thread.Id));
        _registry.Add<IThreadLifecycleContributor>(
            new DisposableThreadContributor(order),
            ContributionOptions.ForThread(thread.Id));
        _registry.Add<IThreadLifecycleContributor>(
            new DisposableThreadContributor(keptOrder),
            ContributionOptions.ForThread(kept.Id));

        var forked = await service.ForkThreadAsync(thread.Id);
        Assert.NotEqual(thread.Id, forked.Id);
        Assert.Empty(recorder.Calls);
        Assert.Empty(_registry.Resolve<IThreadLifecycleContributor>(forked.Id));
        await service.DeleteThreadPermanentlyAsync(thread.Id);

        Assert.Equal([$"deleting:{thread.Id}"], recorder.Calls);
        Assert.Equal(["deleting", "disposed"], order);
        Assert.Empty(keptOrder);
        Assert.Empty(_registry.Resolve<IThreadLifecycleContributor>(thread.Id));
        Assert.Single(_registry.Resolve<IThreadLifecycleContributor>(kept.Id));
    }

    [Fact]
    public async Task ThreadLifecycleObserver_AlsoRegisteredAsAContribution_ReceivesDeletionOnce()
    {
        var observer = new RecordingThreadObserver();
        _registry.Add<IThreadLifecycleContributor>(observer);
        var service = CreateService(new ContributionChatClient(), observers: [observer]);

        var thread = await CreateThreadAsync(service);
        await service.DeleteThreadPermanentlyAsync(thread.Id);

        Assert.Equal([thread.Id], observer.Deleted);
    }

    [Fact]
    public async Task ThreadScopedTurnContributor_PairsStartAndEndOfItsThreadsTurnDespiteAThrowingContributor()
    {
        var service = CreateService(new ContributionChatClient());
        var observed = await CreateThreadAsync(service);
        var other = await CreateThreadAsync(service);
        var recorder = new RecordingTurnContributor();
        _registry.Add<ITurnLifecycleContributor>(new ThrowingTurnContributor(), new ContributionOptions(Order: -1));
        _registry.Add<ITurnLifecycleContributor>(recorder, ContributionOptions.ForThread(observed.Id, order: 1));

        await DrainAsync(service.SubmitInputAsync(other.Id, [new TextContent("hello")]));
        await DrainAsync(service.SubmitInputAsync(observed.Id, [new TextContent("hello")]));

        var started = Assert.Single(recorder.Started);
        var ended = Assert.Single(recorder.Ended);
        Assert.Equal(observed.Id, started.ThreadId);
        Assert.Equal(started.TurnId, ended.TurnId);
        Assert.Equal(TurnStatus.Completed, ended.Status);
        Assert.Null(ended.Error);
        var persisted = await service.GetThreadAsync(observed.Id);
        Assert.Equal(TurnStatus.Completed, Assert.Single(persisted!.Turns).Status);
    }

    [Fact]
    public async Task TurnContributor_ReportsTheEndOfAFailingTurn()
    {
        var recorder = new RecordingTurnContributor();
        _registry.Add<ITurnLifecycleContributor>(recorder);
        var service = CreateService(new FailingContributionChatClient());
        var thread = await CreateThreadAsync(service);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Single(recorder.Started);
        var ended = Assert.Single(recorder.Ended);
        Assert.Equal(TurnStatus.Failed, ended.Status);
        Assert.False(string.IsNullOrWhiteSpace(ended.Error));
    }

    private SessionService CreateService(
        IChatClient chatClient,
        IEnumerable<IThreadLifecycleObserver>? observers = null) =>
        new(
            _host.CreateFactory(_registry, chatClient),
            chatClient.AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_host.WorkspacePath)),
            new SessionGate(),
            threadLifecycleObservers: observers);

    private Task<SessionThread> CreateThreadAsync(SessionService service) =>
        service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _host.WorkspacePath
        });

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    public void Dispose() => _host.Dispose();

    private sealed class RecordingThreadContributor : IThreadLifecycleContributor
    {
        private readonly ConcurrentQueue<string> _calls = new();

        public IReadOnlyList<string> Calls => _calls.ToArray();

        public Task OnThreadStartedAsync(SessionThread thread, CancellationToken cancellationToken = default)
        {
            _calls.Enqueue($"started:{thread.Id}");
            return Task.CompletedTask;
        }

        public Task OnThreadResumedAsync(SessionThread thread, CancellationToken cancellationToken = default)
        {
            _calls.Enqueue($"resumed:{thread.Id}");
            return Task.CompletedTask;
        }

        public Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default)
        {
            _calls.Enqueue($"deleting:{thread.Id}");
            return Task.CompletedTask;
        }
    }

    private sealed class DisposableThreadContributor(List<string> order) : IThreadLifecycleContributor, IDisposable
    {
        public Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default)
        {
            order.Add("deleting");
            return Task.CompletedTask;
        }

        public void Dispose() => order.Add("disposed");
    }

    private sealed class ThrowingThreadContributor : IThreadLifecycleContributor
    {
        public Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("contributor is broken");
    }

    private sealed class RecordingThreadObserver : IThreadLifecycleObserver
    {
        private readonly ConcurrentQueue<string> _deleted = new();

        public IReadOnlyList<string> Deleted => _deleted.ToArray();

        public Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default)
        {
            _deleted.Enqueue(thread.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTurnContributor : ITurnLifecycleContributor
    {
        private readonly ConcurrentQueue<TurnLifecycleContext> _started = new();
        private readonly ConcurrentQueue<TurnLifecycleContext> _ended = new();

        public IReadOnlyList<TurnLifecycleContext> Started => _started.ToArray();

        public IReadOnlyList<TurnLifecycleContext> Ended => _ended.ToArray();

        public Task OnTurnStartedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default)
        {
            _started.Enqueue(context);
            return Task.CompletedTask;
        }

        public Task OnTurnEndedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default)
        {
            _ended.Enqueue(context);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingTurnContributor : ITurnLifecycleContributor
    {
        public Task OnTurnStartedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("contributor is broken");

        public Task OnTurnEndedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("contributor is broken");
    }
}
