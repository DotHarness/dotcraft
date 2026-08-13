using System.Reflection;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.DynamicWorkflows;
using DotCraft.Processes;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Contract = DotCraft.Protocol.AppServer;

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
            new DotCraft.Configuration.AppConfig(),
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
            new DotCraft.Configuration.AppConfig(),
            [],
            NullLogger<DynamicWorkflowService>.Instance);
        service.SetSessionService(session);
        await service.StartAsync();
        try
        {
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

            var workerReady = false;
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var journal = await store.ReadJournalAsync(started.RunId, CancellationToken.None);
                workerReady = journal.Any(entry => entry["type"]?.GetValue<string>() == "worker.ready");
                if (workerReady) break;
                await Task.Delay(50);
            }
            Assert.True(workerReady, "Workflow worker did not become ready before cancellation.");

            await service.CancelAsync(started.RunId).WaitAsync(TimeSpan.FromSeconds(10));
            var cancelled = await service.GetRunAsync(started.RunId);
            var cancelledJournal = await store.ReadJournalAsync(started.RunId, CancellationToken.None);

            Assert.Equal(DynamicWorkflowStatuses.Cancelled, cancelled?.Status);
            Assert.NotNull(cancelled?.CompletedAt);
            Assert.Equal("notApplicable", cancelled?.NotificationStatus);
            Assert.Contains(cancelledJournal, entry => entry["type"]?.GetValue<string>() == "run.cancelled");
            Assert.Equal(0, proxy.EnqueueCalls);
        }
        finally
        {
            await service.StopAsync();
        }
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
            new ManagedChildProcessFactory(), new DotCraft.Configuration.AppConfig(), [],
            NullLogger<DynamicWorkflowService>.Instance);
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

        await service.StopRunAsync(first.RunId);
        var stopped = await service.GetRunAsync(first.RunId);
        Assert.Equal(DynamicWorkflowStatuses.Stopped, stopped?.Status);
        Assert.Equal("queued", stopped?.NotificationStatus);
        Assert.Equal(1, proxy.EnqueueCalls);

        var resumed = await service.ResumeAsync(first.RunId, proxy.Thread.Id, "turn_001");
        Assert.Equal(first.RunId, resumed.ResumedFromRunId);
        Assert.NotEqual(first.RunId, resumed.RunId);
        await service.StopRunAsync(resumed.RunId);

        var sameInstanceView = await new DynamicWorkflowProjectionService(store, service)
            .ReadAsync(proxy.Thread.Id, first.RunId, CancellationToken.None);
        Assert.True(sameInstanceView?.Controls.CanResume);
        await service.StopAsync();

        var restartedService = new DynamicWorkflowService(
            _root, store, new DynamicWorkflowParser(), new StructuredWorkflowResultRegistry(),
            new ManagedChildProcessFactory(), new DotCraft.Configuration.AppConfig(), [],
            NullLogger<DynamicWorkflowService>.Instance);
        restartedService.SetSessionService(session);
        await restartedService.StartAsync();
        var restartedProjection = new DynamicWorkflowProjectionService(store, restartedService);
        var restartedView = await restartedProjection.ReadAsync(proxy.Thread.Id, first.RunId, CancellationToken.None);
        Assert.False(restartedView?.Controls.CanResume);

        var extension = new DynamicWorkflowProtocolExtension(restartedService, restartedProjection);
        await using var transport = new NullTransport();
        var context = new AppServerExtensionContext(
            new AppServerConnection(), transport, session, null, _root, null, null, null, CancellationToken.None);
        var read = Assert.IsType<Contract.WorkflowRunReadResult>(await extension.HandleContractAsync(
            Contract.AppServerRpc.WorkflowRunRead,
            new Contract.WorkflowRunParams { ThreadId = proxy.Thread.Id, RunId = first.RunId },
            new AppServerIncomingMessage { Method = "workflow/run/read" },
            context));
        Assert.False(read.Run.Controls.CanResume);

        var error = await Assert.ThrowsAsync<AppServerException>(() => extension.HandleContractAsync(
            Contract.AppServerRpc.WorkflowRunResume,
            new Contract.WorkflowRunResumeParams { ThreadId = proxy.Thread.Id, RunId = first.RunId },
            new AppServerIncomingMessage { Method = "workflow/run/resume" },
            context));
        Assert.Equal("workflow_resume_unavailable", Assert.IsType<AppServerErrorData>(error.ErrorData).Code);
        await restartedService.StopAsync();
    }

    [Fact]
    public async Task AgentCall_WithoutOverride_InheritsParentPreferenceWithoutHistory()
    {
        var (_, child, journal) = await RunAgentWorkflowAsync(
            "export const meta = { name: 'inherit', description: 'Inherit model' }; await agent('inspect code'); return { ok: true };",
            []);

        Assert.Equal("parent-model", child.Configuration?.Model);
        Assert.True(child.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ModelReasoningEffort.High, child.Configuration?.Reasoning?.Effort);
        Assert.Equal(ReasoningOutput.Summary, child.Configuration?.Reasoning?.Output);
        Assert.Equal(InferenceSpeed.Fast, child.Configuration?.Speed);
        Assert.Empty(child.Turns);

        var completed = Assert.Single(journal, entry => entry["type"]?.GetValue<string>() == "agent.completed");
        Assert.Equal("parent-model", completed["payload"]?["effectiveModel"]?.GetValue<string>());
        Assert.Equal("High", completed["payload"]?["effectiveEffort"]?.GetValue<string>());
    }

    [Fact]
    public async Task AgentCall_ExplicitModelAndEffort_OverrideRolePreference()
    {
        var (_, child, journal) = await RunAgentWorkflowAsync(
            "export const meta = { name: 'override', description: 'Override model' }; await agent('inspect code', { agentType: 'reviewer', model: 'invocation-model', effort: 'low' }); return { ok: true };",
            [new SubAgentRoleConfig { Name = "reviewer", Model = "role-model" }]);

        Assert.Equal("invocation-model", child.Configuration?.Model);
        Assert.True(child.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ModelReasoningEffort.Low, child.Configuration?.Reasoning?.Effort);
        Assert.Equal(ReasoningOutput.Summary, child.Configuration?.Reasoning?.Output);
        Assert.Equal(InferenceSpeed.Fast, child.Configuration?.Speed);

        var requested = Assert.Single(journal, entry => entry["type"]?.GetValue<string>() == "agent.requested");
        Assert.Equal("invocation-model", requested["payload"]?["model"]?.GetValue<string>());
        Assert.Equal("low", requested["payload"]?["effort"]?.GetValue<string>());
        var completed = Assert.Single(journal, entry => entry["type"]?.GetValue<string>() == "agent.completed");
        Assert.Equal("invocation-model", completed["payload"]?["effectiveModel"]?.GetValue<string>());
        Assert.Equal("Low", completed["payload"]?["effectiveEffort"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("{ model: 'invocation-model' }", "invocation-model", ModelReasoningEffort.High)]
    [InlineData("{ effort: 'low' }", "parent-model", ModelReasoningEffort.Low)]
    [InlineData("{ effort: 'max' }", "parent-model", ModelReasoningEffort.ExtraHigh)]
    public async Task AgentCall_PartialOverride_PreservesUnspecifiedPreferenceFields(
        string options,
        string expectedModel,
        ModelReasoningEffort expectedEffort)
    {
        var (_, child, _) = await RunAgentWorkflowAsync(
            $"export const meta = {{ name: 'partial', description: 'Partial override' }}; await agent('inspect code', {options}); return {{ ok: true }};",
            []);

        Assert.Equal(expectedModel, child.Configuration?.Model);
        Assert.True(child.Configuration?.Reasoning?.Enabled);
        Assert.Equal(expectedEffort, child.Configuration?.Reasoning?.Effort);
        Assert.Equal(ReasoningOutput.Summary, child.Configuration?.Reasoning?.Output);
        Assert.Equal(InferenceSpeed.Fast, child.Configuration?.Speed);
        Assert.Equal(ContextWindowMode.Default, child.Configuration?.ContextWindow?.Mode);
    }

    private async Task<(DynamicWorkflowRun Run, SessionThread Child, IReadOnlyList<JsonObject> Journal)> RunAgentWorkflowAsync(
        string script,
        IReadOnlyList<SubAgentRoleConfig> roleConfigs)
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
            Configuration = new ThreadConfiguration
            {
                ProviderId = "openai",
                Model = "parent-model",
                Reasoning = new AppConfig.ReasoningConfig
                {
                    Enabled = true,
                    Effort = ModelReasoningEffort.High,
                    Output = ReasoningOutput.Summary
                },
                Speed = InferenceSpeed.Fast,
                ContextWindow = new ThreadContextWindowConfig { Mode = ContextWindowMode.Default }
            },
            Turns = [new SessionTurn { Id = "turn_001", ThreadId = "thread_parent", Status = TurnStatus.Completed }]
        };
        var service = new DynamicWorkflowService(
            _root,
            store,
            new DynamicWorkflowParser(),
            new StructuredWorkflowResultRegistry(),
            new ManagedChildProcessFactory(),
            new AppConfig(),
            roleConfigs,
            NullLogger<DynamicWorkflowService>.Instance);
        service.SetSessionService(session);
        await service.StartAsync();
        try
        {
            var started = await service.StartInlineAsync(new DynamicWorkflowStartRequest
            {
                ParentThreadId = proxy.Thread.Id,
                ParentTurnId = "turn_001",
                Script = script
            });
            DynamicWorkflowRun? terminal = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                terminal = await service.GetRunAsync(started.RunId);
                if (terminal?.Status != DynamicWorkflowStatuses.Running) break;
                await Task.Delay(50);
            }

            Assert.Equal(DynamicWorkflowStatuses.Succeeded, terminal?.Status);
            var child = Assert.Single(proxy.Children.Values);
            return (terminal!, child, await store.ReadJournalAsync(started.RunId, CancellationToken.None));
        }
        finally
        {
            await service.StopAsync();
        }
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
        public Dictionary<string, SessionThread> Children { get; } = new(StringComparer.Ordinal);
        public List<ThreadSpawnEdge> SpawnEdges { get; } = [];
        public int EnqueueCalls { get; private set; }
        public int TryStartCalls { get; private set; }
        public TurnTriggerInfo? Trigger { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(ISessionService.GetThreadAsync) => GetThread(args),
            nameof(ISessionService.CreateThreadAsync) => CreateThread(args),
            nameof(ISessionService.ListSubAgentChildrenAsync) => ListChildren(args),
            nameof(ISessionService.UpsertThreadSpawnEdgeAsync) => UpsertSpawnEdge(args),
            nameof(ISessionService.SetThreadSpawnEdgeStatusAsync) => SetSpawnEdgeStatus(args),
            nameof(ISessionService.SubmitInputAsync) => CompleteInput(),
            nameof(ISessionService.AddSubAgentMailboxEntryAsync) => Task.CompletedTask,
            nameof(ISessionService.EnqueueTurnInputAsync) => Enqueue(args),
            nameof(ISessionService.TryStartNextQueuedTurnAsync) => StartNext(),
            _ => throw new NotSupportedException($"Unexpected Session method '{targetMethod?.Name}'.")
        };

        private Task<SessionThread> GetThread(object?[]? args)
        {
            var threadId = (string)args![0]!;
            if (string.Equals(threadId, Thread.Id, StringComparison.Ordinal))
                return Task.FromResult(Thread);
            return Task.FromResult(Children[threadId]);
        }

        private Task<SessionThread> CreateThread(object?[]? args)
        {
            var identity = (SessionIdentity)args![0]!;
            var now = DateTimeOffset.UtcNow;
            var child = new SessionThread
            {
                Id = (string)args[3]!,
                WorkspacePath = identity.WorkspacePath,
                UserId = identity.UserId,
                OriginChannel = identity.ChannelName,
                ChannelContext = identity.ChannelContext,
                Configuration = (ThreadConfiguration?)args[1],
                HistoryMode = (HistoryMode)args[2]!,
                DisplayName = (string?)args[4],
                Source = (ThreadSource?)args[6] ?? ThreadSource.User(),
                Status = ThreadStatus.Active,
                CreatedAt = now,
                LastActiveAt = now
            };
            Children.Add(child.Id, child);
            return Task.FromResult(child);
        }

        private Task<IReadOnlyList<ThreadSpawnEdge>> ListChildren(object?[]? args)
        {
            var parentThreadId = (string)args![0]!;
            var includeClosed = (bool)args[1]!;
            IReadOnlyList<ThreadSpawnEdge> result = SpawnEdges
                .Where(edge => edge.ParentThreadId == parentThreadId
                    && (includeClosed || edge.Status == ThreadSpawnEdgeStatus.Open))
                .ToArray();
            return Task.FromResult(result);
        }

        private Task UpsertSpawnEdge(object?[]? args)
        {
            var edge = (ThreadSpawnEdge)args![0]!;
            SpawnEdges.RemoveAll(existing => existing.ChildThreadId == edge.ChildThreadId);
            SpawnEdges.Add(edge);
            return Task.CompletedTask;
        }

        private Task SetSpawnEdgeStatus(object?[]? args)
        {
            var childThreadId = (string)args![1]!;
            var status = (string)args[2]!;
            var edge = SpawnEdges.Single(item => item.ChildThreadId == childThreadId);
            edge.Status = status;
            edge.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        private static async IAsyncEnumerable<SessionEvent> CompleteInput()
        {
            await Task.CompletedTask;
            yield break;
        }

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

    private sealed class NullTransport : IAppServerTransport
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null) => throw new NotSupportedException();
    }
}
