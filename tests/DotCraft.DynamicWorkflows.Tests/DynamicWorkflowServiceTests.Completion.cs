using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.Processes;
using DotCraft.Sessions;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed partial class DynamicWorkflowServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NotificationFailure_PreservesSuccessAndRetriesPending(bool restart)
    {
        var (service, store, proxy) = CreateCompletionService();
        var clock = new NotificationClock();
        service.NotificationTimeProvider = clock;
        proxy.NotificationHandler = (_, _) => proxy.EnqueueCalls == 1
            ? Task.FromException<string?>(new IOException("notification unavailable"))
            : Task.FromResult<string?>("queued_retry");
        await service.StartAsync();
        DynamicWorkflowService? restarted = null;
        try
        {
            var started = await service.StartInlineAsync(CompletionRequest("return { ok: true };"));
            var retry = await clock.NextTimer.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var pending = await service.GetRunAsync(started.RunId);
            Assert.Equal(DynamicWorkflowStatuses.Succeeded, pending?.Status);
            Assert.True(pending?.Result?["ok"]?.GetValue<bool>());
            Assert.Equal("pending", pending?.NotificationStatus);
            Assert.Null(pending?.Error);

            Task acknowledged;
            if (restart)
            {
                await service.StopAsync();
                restarted = CreateService(store, proxy);
                acknowledged = ObserveNotificationAck(restarted, proxy, 2);
                await restarted.StartAsync();
            }
            else
            {
                acknowledged = ObserveNotificationAck(service, proxy, 2);
                retry.Fire();
            }
            await acknowledged.WaitAsync(TimeSpan.FromSeconds(5));
            var delivered = await (restarted ?? service).GetRunAsync(started.RunId);
            Assert.Equal(DynamicWorkflowStatuses.Succeeded, delivered?.Status);
            Assert.Equal("queued", delivered?.NotificationStatus);
            Assert.Equal("queued_retry", delivered?.NotificationInputId);
            var journal = await store.ReadJournalAsync(started.RunId, CancellationToken.None);
            Assert.Single(journal, entry => entry["type"]?.GetValue<string>() == "run.succeeded");
            Assert.DoesNotContain(journal, entry => entry["type"]?.GetValue<string>() == "run.failed");
        }
        finally
        {
            if (restarted != null) await restarted.StopAsync();
            await service.StopAsync();
        }
    }

    [Fact]
    public async Task NotificationAckFailure_RetainsOutcomeAndRetriesTheSameNotification()
    {
        var (service, store, proxy) = CreateCompletionService();
        var clock = new NotificationClock();
        service.NotificationTimeProvider = clock;
        string? statePath = null;
        string? persisted = null;
        proxy.NotificationHandler = (ending, _) =>
        {
            if (proxy.EnqueueCalls == 1)
            {
                statePath = Path.Combine(store.GetRunDirectory(ending.TaskId), "state.json");
                persisted = File.ReadAllText(statePath);
                File.Delete(statePath);
                Directory.CreateDirectory(statePath);
            }
            return Task.FromResult<string?>("queued_once");
        };
        await service.StartAsync();
        try
        {
            var started = await service.StartInlineAsync(CompletionRequest("return { ok: true };"));
            var retry = await clock.NextTimer.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(statePath);
            Directory.Delete(statePath);
            await File.WriteAllTextAsync(statePath, persisted);
            var pending = await service.GetRunAsync(started.RunId);
            Assert.Equal(DynamicWorkflowStatuses.Succeeded, pending?.Status);
            Assert.Equal("pending", pending?.NotificationStatus);

            var acknowledged = ObserveNotificationAck(service, proxy, 2);
            retry.Fire();
            await acknowledged.WaitAsync(TimeSpan.FromSeconds(5));
            var delivered = await service.GetRunAsync(started.RunId);
            Assert.Equal(DynamicWorkflowStatuses.Succeeded, delivered?.Status);
            Assert.Equal("queued_once", delivered?.NotificationInputId);
        }
        finally { await service.StopAsync(); }
    }

    [Fact]
    public async Task WorkerFailure_CancelsAnIssuedAgentBeforeWaitingForIt()
    {
        var (service, _, proxy) = CreateCompletionService();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.InputHandler = WaitForCancellation;
        await service.StartAsync();
        try
        {
            var acknowledged = ObserveNotificationAck(service, proxy, 1);
            var started = await service.StartInlineAsync(CompletionRequest(
                "agent('wait', { label: 'wait' }); await agent('signal', { label: 'signal' }); throw new Error('script failed');"));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await acknowledged.WaitAsync(TimeSpan.FromSeconds(5));
            var failed = await service.GetRunAsync(started.RunId);
            Assert.Equal(DynamicWorkflowStatuses.Failed, failed?.Status);
            Assert.Contains("script failed", failed?.Error);
        }
        finally { await service.StopAsync(); }

        async IAsyncEnumerable<SessionEvent> WaitForCancellation(string id, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return new SessionEvent { EventType = SessionEventType.TurnStarted };
            if (proxy.Children[id].Source.SubAgent?.AgentNickname == "signal")
            {
                await waiting.Task.WaitAsync(ct);
                yield return new SessionEvent
                {
                    EventType = SessionEventType.TurnCompleted,
                    Payload = new SessionTurn { Id = "turn_signal", ThreadId = id, Status = TurnStatus.Completed }
                };
            }
            else
            {
                using var registration = ct.Register(() => cancelled.TrySetResult());
                waiting.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
        }
    }

    [Fact]
    public async Task SuccessfulWorker_JoinsIssuedCallsEvenWithoutAwaitInScript()
    {
        var (service, _, proxy) = CreateCompletionService();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.InputHandler = WaitForRelease;
        await service.StartAsync();
        try
        {
            var acknowledged = ObserveNotificationAck(service, proxy, 1);
            var started = await service.StartInlineAsync(CompletionRequest("agent('wait'); return { ok: true };"));
            await admitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DynamicWorkflowStatuses.Running, (await service.GetRunAsync(started.RunId))?.Status);
            release.TrySetResult();
            await acknowledged.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DynamicWorkflowStatuses.Succeeded, (await service.GetRunAsync(started.RunId))?.Status);
        }
        finally
        {
            release.TrySetResult();
            await service.StopAsync();
        }

        async IAsyncEnumerable<SessionEvent> WaitForRelease(string id, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return new SessionEvent { EventType = SessionEventType.TurnStarted };
            admitted.TrySetResult();
            await release.Task.WaitAsync(ct);
            yield return new SessionEvent
            {
                EventType = SessionEventType.TurnCompleted,
                Payload = new SessionTurn { Id = "turn_child", ThreadId = id, Status = TurnStatus.Completed }
            };
        }
    }

    [Fact]
    public async Task ResumeFromReplayedRun_ReusesTheCompletedPrefixAcrossGenerations()
    {
        var (service, store, proxy) = CreateCompletionService();
        await service.StartAsync();
        try
        {
            var acknowledged = ObserveNotificationAck(service, proxy, 1);
            var first = await service.StartInlineAsync(CompletionRequest("await agent('inspect'); return { ok: true };"));
            await acknowledged.WaitAsync(TimeSpan.FromSeconds(5));
            acknowledged = ObserveNotificationAck(service, proxy, 2);
            var second = await service.ResumeAsync(first.RunId, proxy.Thread.Id, "turn_parent");
            await acknowledged.WaitAsync(TimeSpan.FromSeconds(5));
            acknowledged = ObserveNotificationAck(service, proxy, 3);
            var third = await service.ResumeAsync(second.RunId, proxy.Thread.Id, "turn_parent");
            await acknowledged.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(proxy.Children);
            foreach (var run in new[] { second, third })
            {
                Assert.Equal(DynamicWorkflowStatuses.Succeeded, (await service.GetRunAsync(run.RunId))?.Status);
                var journal = await store.ReadJournalAsync(run.RunId, CancellationToken.None);
                Assert.Single(journal, entry => entry["type"]?.GetValue<string>() == "agent.replayed");
            }
        }
        finally { await service.StopAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedChild_CleansOnlyConfirmedCleanWorktree(bool dirty)
    {
        var (service, _, proxy) = CreateCompletionService();
        proxy.InputHandler = FailAfterAdmission;
        proxy.WorktreeStatusHandler = child => new ThreadWorktreeStatus
        {
            ThreadId = child.Id, Path = child.Worktree!.Path, BranchName = child.Worktree.BranchName,
            Exists = true, IsGitWorktree = true, HasUncommittedChanges = dirty
        };
        await service.StartAsync();
        try
        {
            var acknowledged = ObserveNotificationAck(service, proxy, 1);
            var started = await service.StartInlineAsync(CompletionRequest(
                "await agent('inspect', { isolation: 'worktree' }); return { ok: true };"));
            await acknowledged.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DynamicWorkflowStatuses.Succeeded, (await service.GetRunAsync(started.RunId))?.Status);
            var child = Assert.Single(proxy.Children).Value;
            if (dirty)
            {
                Assert.Empty(proxy.RemovedWorktrees);
            }
            else
            {
                Assert.Equal(child.Id, Assert.Single(proxy.RemovedWorktrees));
            }
        }
        finally { await service.StopAsync(); }

        static async IAsyncEnumerable<SessionEvent> FailAfterAdmission(
            string id, [EnumeratorCancellation] CancellationToken ct)
        {
            yield return new SessionEvent { EventType = SessionEventType.TurnStarted };
            await Task.Yield();
            throw new InvalidOperationException("child failed");
        }
    }

    private (DynamicWorkflowService Service, DynamicWorkflowStore Store, SessionServiceProxy Proxy) CreateCompletionService()
    {
        _ = typeof(CommandLineArgs).Assembly;
        var store = new DynamicWorkflowStore(Path.Combine(_root, ".craft"));
        var session = DispatchProxy.Create<INotifyingSessionService, SessionServiceProxy>();
        var proxy = (SessionServiceProxy)(object)session;
        proxy.Thread = new SessionThread
        {
            Id = "thread_parent", WorkspacePath = _root, OriginChannel = "test",
            Configuration = new ThreadConfiguration { ProviderId = "openai", Model = "test-model" },
            Turns = [new SessionTurn { Id = "turn_parent", ThreadId = "thread_parent", Status = TurnStatus.Completed }]
        };
        return (CreateService(store, proxy), store, proxy);
    }

    private DynamicWorkflowService CreateService(DynamicWorkflowStore store, SessionServiceProxy proxy)
    {
        var service = new DynamicWorkflowService(
            _root, store, new DynamicWorkflowParser(), new StructuredWorkflowResultRegistry(),
            new ManagedChildProcessFactory(), new AppConfig(), []);
        service.SetSessionService((INotifyingSessionService)(object)proxy);
        return service;
    }

    private static DynamicWorkflowStartRequest CompletionRequest(string body) => new()
    {
        ParentThreadId = "thread_parent", ParentTurnId = "turn_parent",
        Script = "export const meta = { name: 'completion', description: 'Completion lifecycle' }; " + body
    };

    private static Task ObserveNotificationAck(DynamicWorkflowService service, SessionServiceProxy proxy, int count)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(DynamicWorkflowRunChanged change)
        {
            if (change.Reason != "state" || proxy.EnqueueCalls != count) return;
            service.RunChanged -= Observe;
            completion.TrySetResult();
        }
        service.RunChanged += Observe;
        return completion.Task;
    }

    private sealed class NotificationClock : TimeProvider
    {
        public TaskCompletionSource<NotificationTimer> NextTimer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new NotificationTimer(callback, state);
            NextTimer.TrySetResult(timer);
            return timer;
        }
    }

    private sealed class NotificationTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public void Fire() { if (!_disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
