using System.Reflection;
using DotCraft.CLI;
using DotCraft.DynamicWorkflows;
using DotCraft.Processes;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed class DynamicWorkflowServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-workflow-service-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartInline_RunsInBackgroundPersistsTerminalStateAndQueuesOnce()
    {
        _ = typeof(CommandLineArgs).Assembly;
        var store = new DynamicWorkflowStore(Path.Combine(_root, ".craft"));
        var session = DispatchProxy.Create<ISessionService, SessionServiceProxy>();
        var proxy = (SessionServiceProxy)(object)session;
        proxy.Thread = new SessionThread
        {
            Id = "thread_parent",
            WorkspacePath = _root,
            OriginChannel = "test",
            Turns = [new SessionTurn { Id = "turn_001", ThreadId = "thread_parent", Status = TurnStatus.Completed }]
        };
        var service = new DynamicWorkflowService(
            _root,
            store,
            new DynamicWorkflowParser(),
            new StructuredWorkflowResultRegistry(),
            new ManagedChildProcessFactory(),
            [],
            NullLogger<DynamicWorkflowService>.Instance);
        service.SetSessionService(session);
        await service.StartAsync();

        var started = await service.StartInlineAsync(new DynamicWorkflowStartRequest
        {
            ParentThreadId = proxy.Thread.Id,
            ParentTurnId = "turn_001",
            Script = "export const meta = { name: 'service', description: 'Service test' }; return { ok: true };",
            Args = new System.Text.Json.Nodes.JsonObject()
        });

        Assert.Equal(DynamicWorkflowStatuses.Running, started.Status);
        DynamicWorkflowRun? completed = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            completed = await service.GetRunAsync(started.RunId);
            if (completed?.Status != DynamicWorkflowStatuses.Running
                && completed?.NotificationStatus == "queued") break;
            await Task.Delay(50);
        }
        Assert.Equal(DynamicWorkflowStatuses.Succeeded, completed?.Status);
        Assert.True(completed?.Result?["ok"]?.GetValue<bool>());
        Assert.Equal("queued", completed?.NotificationStatus);
        Assert.Equal("queued_test", completed?.NotificationInputId);
        for (var attempt = 0; attempt < 20 && proxy.TryStartCalls == 0; attempt++)
            await Task.Delay(25);
        Assert.Equal(1, proxy.EnqueueCalls);
        Assert.Equal(1, proxy.TryStartCalls);
        Assert.Equal("workflow", proxy.Trigger?.Kind);
        Assert.Equal(started.RunId, proxy.Trigger?.RefId);
        Assert.True(File.Exists(Path.Combine(store.GetRunDirectory(started.RunId), "state.json")));
        Assert.True(File.Exists(Path.Combine(store.GetRunDirectory(started.RunId), "journal.jsonl")));

        await service.OnThreadDeletingAsync(proxy.Thread);
        Assert.False(Directory.Exists(store.GetRunDirectory(started.RunId)));
        await service.StopAsync();
    }

    [Fact]
    public async Task CancelAsync_StopsWorkerAndDoesNotQueueContinuation()
    {
        _ = typeof(CommandLineArgs).Assembly;
        var store = new DynamicWorkflowStore(Path.Combine(_root, ".craft"));
        var session = DispatchProxy.Create<ISessionService, SessionServiceProxy>();
        var proxy = (SessionServiceProxy)(object)session;
        proxy.Thread = new SessionThread
        {
            Id = "thread_parent",
            WorkspacePath = _root,
            OriginChannel = "test",
            Turns = [new SessionTurn { Id = "turn_001", ThreadId = "thread_parent", Status = TurnStatus.Completed }]
        };
        var service = new DynamicWorkflowService(
            _root,
            store,
            new DynamicWorkflowParser(),
            new StructuredWorkflowResultRegistry(),
            new ManagedChildProcessFactory(),
            [],
            NullLogger<DynamicWorkflowService>.Instance);
        service.SetSessionService(session);
        await service.StartAsync();
        var started = await service.StartInlineAsync(new DynamicWorkflowStartRequest
        {
            ParentThreadId = proxy.Thread.Id,
            ParentTurnId = "turn_001",
            Script = "export const meta = { name: 'cancel', description: 'Cancel test' }; while (true) {}",
            LimitsOverride = new DynamicWorkflowLimits
            {
                MaxStatements = int.MaxValue,
                RunTimeout = TimeSpan.FromSeconds(30)
            }
        });

        await service.CancelAsync(started.RunId);
        DynamicWorkflowRun? cancelled = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancelled = await service.GetRunAsync(started.RunId);
            if (cancelled?.Status != DynamicWorkflowStatuses.Running) break;
            await Task.Delay(50);
        }

        Assert.Equal(DynamicWorkflowStatuses.Cancelled, cancelled?.Status);
        Assert.Equal("notApplicable", cancelled?.NotificationStatus);
        Assert.Equal(0, proxy.EnqueueCalls);
        await service.StopAsync();
    }

    [Fact]
    public async Task PauseAndResume_CreateLinkedRunWithinCurrentServerInstance()
    {
        _ = typeof(CommandLineArgs).Assembly;
        var store = new DynamicWorkflowStore(Path.Combine(_root, ".craft"));
        var session = DispatchProxy.Create<ISessionService, SessionServiceProxy>();
        var proxy = (SessionServiceProxy)(object)session;
        proxy.Thread = new SessionThread
        {
            Id = "thread_parent",
            WorkspacePath = _root,
            OriginChannel = "test",
            Turns = [new SessionTurn { Id = "turn_001", ThreadId = "thread_parent", Status = TurnStatus.Completed }]
        };
        var service = new DynamicWorkflowService(
            _root, store, new DynamicWorkflowParser(), new StructuredWorkflowResultRegistry(),
            new ManagedChildProcessFactory(), [], NullLogger<DynamicWorkflowService>.Instance);
        service.SetSessionService(session);
        await service.StartAsync();
        var first = await service.StartInlineAsync(new DynamicWorkflowStartRequest
        {
            ParentThreadId = proxy.Thread.Id,
            ParentTurnId = "turn_001",
            Script = "export const meta = { name: 'resume', description: 'Resume test' }; while (true) {}",
            LimitsOverride = new DynamicWorkflowLimits { MaxStatements = int.MaxValue, RunTimeout = TimeSpan.FromSeconds(30) }
        });
        await service.PauseAsync(first.RunId);
        DynamicWorkflowRun? paused = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            paused = await service.GetRunAsync(first.RunId);
            if (paused?.Status == DynamicWorkflowStatuses.Paused) break;
            await Task.Delay(50);
        }
        Assert.Equal(DynamicWorkflowStatuses.Paused, paused?.Status);
        Assert.Equal(0, proxy.EnqueueCalls);

        var resumed = await service.ResumeAsync(first.RunId, proxy.Thread.Id, "turn_001");
        Assert.Equal(first.RunId, resumed.ResumedFromRunId);
        Assert.NotEqual(first.RunId, resumed.RunId);
        await service.StopRunAsync(resumed.RunId);
        await service.StopAsync();
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 10 && Directory.Exists(_root); attempt++)
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) when (attempt < 9) { Thread.Sleep(50); }
        }
    }

    public class SessionServiceProxy : DispatchProxy
    {
        public required SessionThread Thread { get; set; }
        public int EnqueueCalls { get; private set; }
        public int TryStartCalls { get; private set; }
        public TurnTriggerInfo? Trigger { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(ISessionService.GetThreadAsync) => Task.FromResult(Thread),
            nameof(ISessionService.EnqueueTurnInputAsync) => Enqueue(args),
            nameof(ISessionService.TryStartNextQueuedTurnAsync) => StartNext(),
            _ => throw new NotSupportedException($"Unexpected Session method '{targetMethod?.Name}'.")
        };

        private Task<QueuedTurnInput> Enqueue(object?[]? args)
        {
            EnqueueCalls++;
            Trigger = TurnTriggerScope.Current;
            return Task.FromResult(new QueuedTurnInput
            {
                Id = "queued_test",
                ThreadId = Thread.Id,
                TriggerKind = Trigger?.Kind,
                TriggerRefId = Trigger?.RefId
            });
        }

        private Task StartNext()
        {
            TryStartCalls++;
            return Task.CompletedTask;
        }
    }
}
