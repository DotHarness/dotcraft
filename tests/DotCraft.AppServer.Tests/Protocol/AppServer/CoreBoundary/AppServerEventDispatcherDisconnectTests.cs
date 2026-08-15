using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Tools;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using McpToolCallPayload = DotCraft.Sessions.McpToolCallPayload;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadOriginAppSnapshot = DotCraft.Sessions.ThreadOriginAppSnapshot;
using UserInputRequestPayload = DotCraft.Sessions.UserInputRequestPayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerEventDispatcherDisconnectTests
{
    [Fact]
    public async Task RunAsync_ReplayRecent_DoesNotSendHistoricalThreadStatusChanges()
    {
        using var harness = new AppServerTestHarness();
        var broker = new ThreadEventBroker("thread_001");
        broker.PublishThreadStatusChanged(ThreadStatus.Active, ThreadStatus.Archived);
        broker.PublishThreadStatusChanged(ThreadStatus.Archived, ThreadStatus.Active);
        broker.CreateTurnChannel("turn_001").EmitTurnCompleted(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_001",
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });

        using var replayCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var replayedEvents = new List<SessionEvent>();
        await foreach (var evt in broker.SubscribeAsync(replayRecent: true, ct: replayCts.Token))
        {
            replayedEvents.Add(evt);
            break;
        }

        var transport = new CapturingTransport();
        var dispatcher = new AppServerEventDispatcher(
            TrackEvents(replayedEvents, []),
            CreateReadyConnection(),
            transport,
            harness.Service);

        await dispatcher.RunAsync();

        var sent = Assert.Single(transport.Sent);
        var json = JsonSerializer.Serialize(sent, sent.GetType());
        Assert.Contains(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnCompleted, json);
        Assert.DoesNotContain(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStatusChanged, json);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(true, true, 1)]
    public async Task RunAsync_TerminalMirrorCommandDelta_UsesTerminalWhenAvailable(
        bool backgroundTerminals,
        bool optOutTerminalDelta,
        int expectedNotifications)
    {
        using var harness = new AppServerTestHarness();
        var transport = new CapturingTransport();
        var evt = new SessionEvent
        {
            EventId = "e_command_delta",
            EventType = SessionEventType.ItemDelta,
            ThreadId = "thread_001",
            TurnId = "turn_001",
            ItemId = "item_command_001",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new CommandExecutionOutputDelta
            {
                TextDelta = "live output",
                MirrorsTerminalOutput = true
            }
        };
        var dispatcher = new AppServerEventDispatcher(
            TrackEvents([evt], []),
            CreateReadyConnection(backgroundTerminals: backgroundTerminals, optOutTerminalDelta: optOutTerminalDelta),
            transport,
            harness.Service);

        await dispatcher.RunAsync();

        Assert.Equal(expectedNotifications, transport.Sent.Count);
        if (expectedNotifications > 0)
        {
            var json = JsonSerializer.Serialize(Assert.Single(transport.Sent));
            Assert.Contains(DotCraft.Protocol.AppServer.AppServerMethodNames.CommandOutputDelta, json);
            Assert.Contains("live output", json);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task RunAsync_WhenNotificationWriteFails_DrainsRemainingTurnEvents(int failOnWriteAttempt)
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var events = AppServerTestHarness.BuildTurnEventSequence(thread.Id);
        var consumed = new List<SessionEventType>();
        var transport = new FailingTransport(failOnWriteAttempt: failOnWriteAttempt);

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents(events, consumed),
            CreateReadyConnection(),
            transport,
            harness.Service);

        await dispatcher.RunAsync();

        Assert.Equal(events.Select(e => e.EventType), consumed);
        Assert.Equal(failOnWriteAttempt, transport.WriteAttempts);
    }

    // Regression: server-originated threads (e.g. Teams mission members) reach the client only via the
    // ThreadCreated event stream. The dispatcher must apply the thread-wire enricher so the notification
    // carries origin-app branding — otherwise the desktop falls back to the generic channel icon.
    [Fact]
    public async Task RunAsync_ThreadCreated_AppliesThreadWireEnricherToNotification()
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var createdEvent = new SessionEvent
        {
            EventId = "e_created",
            EventType = SessionEventType.ThreadCreated,
            ThreadId = thread.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = thread
        };
        var transport = new CapturingTransport();
        SessionWireThread? enricherInput = null;

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents([createdEvent], []),
            CreateReadyConnection(),
            transport,
            harness.Service,
            enrichThreadWire: wire =>
            {
                enricherInput = wire;
                return wire with
                {
                    OriginApp = new ThreadOriginAppSnapshot
                    {
                        AppId = "com.example.workflow",
                        DisplayName = "Worker",
                        Icon = "data:image/svg+xml;base64,SENTINEL_WORKER",
                        MemberId = "worker"
                    }
                };
            });

        await dispatcher.RunAsync();

        Assert.NotNull(enricherInput);
        Assert.Equal(thread.Id, enricherInput!.Id);
        var sent = Assert.Single(transport.Sent);
        var json = JsonSerializer.Serialize(sent, sent.GetType());
        Assert.Contains("SENTINEL_WORKER", json);
        Assert.Contains("worker", json);
    }

    [Fact]
    public async Task RunAsync_OrdinaryMcpCompletionDoesNotAdvertiseAnAppView()
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.McpToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new McpToolCallPayload
            {
                Namespace = "mcp__knowledge_service",
                ToolName = "answer_query",
                ProviderFlatName = "mcp__knowledge_service__answer_query",
                ToolDefinitionId = "Mcp:knowledge-service:answer_query",
                RuntimeBindingId = "mcp:knowledge-service:answer_query:1",
                BindingRevision = 1,
                SnapshotRevision = 1,
                McpGeneration = 1,
                Server = "knowledge-service",
                SourceToolId = "answer_query",
                CallId = "call-1",
                Status = "failed",
                Success = false,
                ErrorCode = ToolErrorCodes.InputInvalid,
                ErrorMessage = "invalid"
            }
        };
        var evt = new SessionEvent
        {
            EventId = "e_mcp",
            EventType = SessionEventType.ItemCompleted,
            ThreadId = thread.Id,
            TurnId = item.TurnId,
            ItemId = item.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = item
        };
        var transport = new CapturingTransport();
        var dispatcher = new AppServerEventDispatcher(
            TrackEvents([evt], []),
            CreateReadyConnection(mcpApps: true),
            transport,
            harness.Service);

        await dispatcher.RunAsync();

        var sent = Assert.Single(transport.Sent);
        var json = JsonSerializer.Serialize(sent, sent.GetType());
        Assert.DoesNotContain("\"mcpApp\"", json);
    }

    [Fact]
    public async Task RunAsync_WhenTurnStartedResponseCallbackFails_DrainsRemainingTurnEvents()
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var events = AppServerTestHarness.BuildTurnEventSequence(thread.Id);
        var consumed = new List<SessionEventType>();
        var callbackCalled = false;
        var transport = new FailingTransport();

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents(events, consumed),
            CreateReadyConnection(),
            transport,
            harness.Service,
            onTurnStarted: _ =>
            {
                callbackCalled = true;
                throw new IOException("client disconnected");
            });

        await dispatcher.RunAsync();

        Assert.True(callbackCalled);
        Assert.Equal(events.Select(e => e.EventType), consumed);
        Assert.Equal(0, transport.WriteAttempts);
    }

    [Fact]
    public async Task RunAsync_WhenApprovalRequestTransportFails_ResolvesWithNonInteractiveFallback()
    {
        using var harness = new AppServerTestHarness(
            defaultApprovalDecision: SessionApprovalDecision.Reject);
        var thread = await harness.Service.CreateThreadAsync(
            harness.Identity,
            new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.AutoApprove });
        var events = AppServerTestHarness.BuildApprovalEventSequence(thread.Id);
        var consumed = new List<SessionEventType>();
        var transport = new FailingTransport(clientRequestException: new IOException("client disconnected"));

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents(events, consumed),
            CreateReadyConnection(),
            transport,
            harness.Service,
            defaultApprovalDecision: SessionApprovalDecision.Reject);

        await dispatcher.RunAsync();

        Assert.Equal(events.Select(e => e.EventType), consumed);
        var resolved = Assert.Single(harness.Service.ResolvedApprovals);
        Assert.Equal("req_001", resolved.requestId);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, resolved.decision);
    }

    [Fact]
    public async Task RunAsync_WhenUserInputRequestTokenCancelled_DoesNotResolveUntilClientResponds()
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var userInputEvent = BuildUserInputRequestedEvent(thread.Id, "turn_001", "req_input_001");
        var consumed = new List<SessionEventType>();
        var transport = new BlockingClientRequestTransport();
        using var cts = new CancellationTokenSource();

        var dispatcher = new AppServerEventDispatcher(
            TrackEventsThenWait([userInputEvent], consumed),
            CreateReadyConnection(requestUserInputSupport: true),
            transport,
            harness.Service);

        var runTask = dispatcher.RunAsync(cts.Token);
        await transport.RequestObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(Timeout.InfiniteTimeSpan, transport.LastTimeout);

        cts.Cancel();
        await Task.Delay(50);

        Assert.False(transport.RequestCancellationToken.IsCancellationRequested);
        Assert.Empty(harness.Service.ResolvedUserInputs);

        transport.Complete(InMemoryTransport.BuildClientResponse(1, new
        {
            answers = new Dictionary<string, object>
            {
                ["choice"] = new { answers = new[] { "B" } }
            }
        }));

        try
        {
            await runTask.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (OperationCanceledException)
        {
            // The subscription stream may observe the cancelled token after the
            // interactive request resolves; the important behavior is that the
            // request itself was not answered by that cancellation.
        }
        await WaitForAsync(() => harness.Service.ResolvedUserInputs.Count == 1);
        var resolved = Assert.Single(harness.Service.ResolvedUserInputs);
        Assert.Equal("req_input_001", resolved.requestId);
        Assert.Equal("B", Assert.Single(resolved.response.Answers["choice"].Answers));
    }

    [Fact]
    public async Task RunAsync_WhenUserInputRequestIsPending_DrainsLaterTurnEvents()
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var userInputEvent = BuildUserInputRequestedEvent(thread.Id, "turn_001", "req_input_001");
        var turnCompleted = AppServerTestHarness.BuildTurnEventSequence(thread.Id).Last();
        var consumed = new List<SessionEventType>();
        var transport = new BlockingClientRequestTransport();

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents([userInputEvent, turnCompleted], consumed),
            CreateReadyConnection(requestUserInputSupport: true),
            transport,
            harness.Service);

        await dispatcher.RunAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            [SessionEventType.UserInputRequested, SessionEventType.TurnCompleted],
            consumed);
        Assert.Equal(Timeout.InfiniteTimeSpan, transport.LastTimeout);

        transport.Complete(InMemoryTransport.BuildClientResponse(1, new { answers = new Dictionary<string, object>() }));
        await WaitForAsync(() => harness.Service.ResolvedUserInputs.Count == 1);
    }

    [Fact]
    public async Task RunAsync_WhenUserInputUnsupported_ResolvesWithEmptyAnswers()
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var userInputEvent = BuildUserInputRequestedEvent(thread.Id, "turn_001", "req_input_001");
        var transport = new BlockingClientRequestTransport();

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents([userInputEvent], []),
            CreateReadyConnection(requestUserInputSupport: false),
            transport,
            harness.Service);

        await dispatcher.RunAsync();

        Assert.False(transport.RequestObserved.Task.IsCompleted);
        var resolved = Assert.Single(harness.Service.ResolvedUserInputs);
        Assert.Equal("req_input_001", resolved.requestId);
        Assert.Empty(resolved.response.Answers);
    }

    [Fact]
    public async Task RunAsync_WhenUserInputTransportFails_ResolvesWithEmptyAnswers()
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var userInputEvent = BuildUserInputRequestedEvent(thread.Id, "turn_001", "req_input_001");
        var transport = new FailingTransport(clientRequestException: new IOException("client disconnected"));

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents([userInputEvent], []),
            CreateReadyConnection(requestUserInputSupport: true),
            transport,
            harness.Service);

        await dispatcher.RunAsync();

        await WaitForAsync(() => harness.Service.ResolvedUserInputs.Count == 1);
        var resolved = Assert.Single(harness.Service.ResolvedUserInputs);
        Assert.Equal("req_input_001", resolved.requestId);
        Assert.Empty(resolved.response.Answers);
    }

    [Fact]
    public async Task TurnStart_UsesNonConnectionCancellationTokenForPersistedTurnExecution()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var events = AppServerTestHarness.BuildTurnEventSequence(thread.Id);
        harness.Service.EnqueueSubmitEvents(thread.Id, events);

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "keep running" } }
        }));

        Assert.False(harness.Service.LastSubmitCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task TurnStart_WhenSubscribedAndConnectionCloses_DrainsActiveTurnStream()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var events = AppServerTestHarness.BuildTurnEventSequence(thread.Id);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadSubscribe,
            new { threadId = thread.Id }));
        await harness.Transport.ReadNextSentAsync();

        harness.Service.EnqueueSubmitEvents(thread.Id, events);
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "subscribed turn" } }
        }));

        harness.Connection.MarkClosed();
        harness.Connection.CancelAllSubscriptions();

        await WaitForAsync(() => harness.Service.YieldedSubmitEventTypes.Count >= events.Length);
        Assert.Equal(events.Select(e => e.EventType), harness.Service.YieldedSubmitEventTypes);
    }

    [Fact]
    public async Task TurnStart_WhenSubscribedAndConnectionClosesDuringApproval_UsesFallbackDecision()
    {
        using var harness = new AppServerTestHarness(
            defaultApprovalDecision: SessionApprovalDecision.Reject);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(
            harness.Identity,
            new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.AutoApprove });
        var events = AppServerTestHarness.BuildApprovalEventSequence(thread.Id);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadSubscribe,
            new { threadId = thread.Id }));
        await harness.Transport.ReadNextSentAsync();

        harness.Service.EnqueueSubmitEvents(thread.Id, events);
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "approval while subscribed" } }
        }));

        harness.Connection.MarkClosed();
        harness.Connection.CancelAllSubscriptions();

        await WaitForAsync(() => harness.Service.ResolvedApprovals.Count == 1);
        var resolved = Assert.Single(harness.Service.ResolvedApprovals);
        Assert.Equal("req_001", resolved.requestId);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, resolved.decision);
    }

    [Fact]
    public void CancelAllSubscriptions_CancelsPassiveSubscriptionsOnDisconnect()
    {
        var connection = new AppServerConnection();
        var cts = new CancellationTokenSource();
        var cancelled = false;
        using var _ = cts.Token.Register(() => cancelled = true);

        Assert.True(connection.TryAddSubscription("thread_001", cts));

        connection.CancelAllSubscriptions();

        Assert.True(cancelled);
        Assert.False(connection.HasSubscription("thread_001"));
    }

    [Fact]
    public async Task MarkClosed_CompletesConnectionClosedTask()
    {
        var connection = new AppServerConnection();

        connection.MarkClosed();

        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(connection.IsClosed);
    }

    private static AppServerConnection CreateReadyConnection(
        bool requestUserInputSupport = false,
        bool mcpApps = false,
        bool backgroundTerminals = false,
        bool optOutTerminalDelta = false)
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = "desktop", Version = "0.0.1-test" },
            new ClientConnectionCapabilities
            {
                ApprovalSupport = true,
                StreamingSupport = true,
                RequestUserInputSupport = requestUserInputSupport,
                McpApps = mcpApps,
                BackgroundTerminals = backgroundTerminals,
                OptOutNotificationMethods = optOutTerminalDelta
                    ? [DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalOutputDelta]
                    : []
            }));
        connection.MarkClientReady();
        return connection;
    }

    private static SessionEvent BuildUserInputRequestedEvent(
        string threadId,
        string turnId,
        string requestId)
    {
        var item = new SessionItem
        {
            Id = "item_input_001",
            TurnId = turnId,
            Type = ItemType.UserInputRequest,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserInputRequestPayload
            {
                RequestId = requestId,
                Questions =
                [
                    new RequestUserInputQuestion
                    {
                        Id = "choice",
                        Header = "Choice",
                        Question = "Pick one?",
                        IsOther = true,
                        Options =
                        [
                            new RequestUserInputQuestionOption
                            {
                                Label = "A",
                                Description = "Pick A."
                            },
                            new RequestUserInputQuestionOption
                            {
                                Label = "B",
                                Description = "Pick B."
                            }
                        ]
                    }
                ]
            }
        };

        return new SessionEvent
        {
            EventId = "e_input",
            EventType = SessionEventType.UserInputRequested,
            ThreadId = threadId,
            TurnId = turnId,
            ItemId = item.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = item
        };
    }

    private static async IAsyncEnumerable<SessionEvent> TrackEvents(
        IEnumerable<SessionEvent> events,
        List<SessionEventType> consumed,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            consumed.Add(evt.EventType);
            await Task.Yield();
            yield return evt;
        }
    }

    private static async IAsyncEnumerable<SessionEvent> TrackEventsThenWait(
        IEnumerable<SessionEvent> events,
        List<SessionEventType> consumed,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in TrackEvents(events, consumed, ct))
        {
            yield return evt;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    private sealed class CapturingTransport : IAppServerTransport
    {
        public List<object> Sent { get; } = [];

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null) =>
            Task.FromResult(InMemoryTransport.BuildClientResponse(1, new { }));
    }

    private sealed class FailingTransport(
        int? failOnWriteAttempt = null,
        Exception? clientRequestException = null) : IAppServerTransport
    {
        public int WriteAttempts { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default)
        {
            WriteAttempts++;
            if (failOnWriteAttempt == WriteAttempts)
                throw new IOException("client disconnected");

            return Task.CompletedTask;
        }

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            if (clientRequestException != null)
                throw clientRequestException;

            return Task.FromResult(InMemoryTransport.BuildClientResponse(1, new { decision = "accept" }));
        }
    }

    private sealed class BlockingClientRequestTransport : IAppServerTransport
    {
        private readonly TaskCompletionSource<AppServerIncomingMessage> _response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RequestObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken RequestCancellationToken { get; private set; }

        public TimeSpan? LastTimeout { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default) =>
            Task.CompletedTask;

        public async Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            RequestCancellationToken = ct;
            LastTimeout = timeout;
            RequestObserved.TrySetResult();
            return await _response.Task.WaitAsync(ct);
        }

        public void Complete(AppServerIncomingMessage response) =>
            _response.TrySetResult(response);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }
}
