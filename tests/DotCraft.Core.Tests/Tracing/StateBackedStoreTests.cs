using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Persistence;
using DotCraft.Tracing;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using SessionThread = DotCraft.Sessions.SessionThread;
using Xunit;

namespace DotCraft.Tests.Tracing;

public sealed class StateBackedStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _craftPath;
    private readonly WorkspaceStateDatabase _stateRuntime;

    public StateBackedStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "state-backed-store-tests", Guid.NewGuid().ToString("N"));
        _craftPath = Path.Combine(_root, ".craft");
        _stateRuntime = new WorkspaceStateDatabase(_craftPath);
    }

    [Fact]
    public void TraceStore_RoundTrips_SubAgentRelationshipAndLatestPrefixDiagnostic()
    {
        var writer = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        writer.BindThreadMainSession("parent");
        writer.BindChildSession("child", "parent", "parent");
        writer.Record(new TraceEvent
        {
            SessionKey = "child",
            Type = TraceEventType.SubAgentPrefixDiagnostic,
            MetadataJson = """{"schemaVersion":1,"status":"match","matchedInputItemCount":2}"""
        });

        var reader = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var relationship = Assert.Single(reader.DescribeSessionRelationships(["child"])).Value;
        Assert.Equal("parent", relationship.ParentSessionKey);
        Assert.Equal("threadChild", relationship.BindingKind);

        var diagnostic = Assert.Single(reader.GetLatestEvents(
            ["child"],
            TraceEventType.SubAgentPrefixDiagnostic)).Value;
        Assert.Contains("\"status\":\"match\"", diagnostic.MetadataJson);
    }

    [Fact]
    public void TraceCollector_RoundTripsAgentInstructionsAndDeduplicatesByFingerprint()
    {
        var writer = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var collector = new TraceCollector(writer);
        var firstSource = Path.Combine(_root, "workspace", "AGENTS.md");
        var secondSource = Path.Combine(_root, "workspace", "src", "AGENTS.md");

        collector.RecordAgentInstructions("thread-agents", "project rules", [firstSource], "sha256:first");
        collector.RecordAgentInstructions("thread-agents", "project rules", [firstSource], "sha256:first");
        collector.RecordAgentInstructions("thread-agents", "project rules", [secondSource], "sha256:second");

        var reader = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var events = reader.GetEvents("thread-agents")
            .Where(evt => evt.Type == TraceEventType.AgentInstructions)
            .ToList();

        Assert.Equal(2, events.Count);
        Assert.Equal("project rules", events[^1].Content);
        using var metadata = JsonDocument.Parse(events[^1].MetadataJson!);
        Assert.Equal("sha256:second", metadata.RootElement.GetProperty("fingerprint").GetString());
        Assert.Equal(
            [secondSource],
            metadata.RootElement.GetProperty("sources").EnumerateArray().Select(source => source.GetString()));
    }

    [Fact]
    public void TraceCollector_CountsToolFailuresAndPersistsOnlyTheErrorCode()
    {
        var writer = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var collector = new TraceCollector(writer);
        var failure = StreamingFunctionInvokingChatClient.CreateToolFailureResult(
            "failed-call",
            "sensitive execution detail",
            ToolErrorCodes.InputInvalid);

        collector.RecordToolCallCompleted("tool-errors", failure, "Automation", 12);
        collector.RecordToolCallCompleted(
            "tool-errors",
            new FunctionResultContent("successful-call", "ok"),
            "CurrentTime",
            3);

        var reader = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var session = reader.GetSession("tool-errors");
        Assert.NotNull(session);
        Assert.Equal(2, session.ToolCallCount);
        Assert.Equal(1, session.ErrorCount);
        var failedEvent = Assert.Single(reader.GetEvents("tool-errors"), static evt => evt.CallId == "failed-call");
        using var metadata = JsonDocument.Parse(failedEvent.MetadataJson!);
        Assert.Equal(ToolErrorCodes.InputInvalid, metadata.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("sensitive execution detail", failedEvent.MetadataJson);
    }

    [Fact]
    public void TraceCollector_ColdResumeDoesNotRecaptureSubAgentPrefixAnchor()
    {
        var store = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var firstCollector = new TraceCollector(store);
        firstCollector.RecordPromptCacheRequestShape("parent", PrefixShape(["a"]), 1, 1);
        firstCollector.BindChildSession("child", "parent", "parent");
        firstCollector.RecordPromptCacheRequestShape("child", PrefixShape(["a", "task"]), 1, 1);

        var resumedCollector = new TraceCollector(new TraceStore(
            _stateRuntime,
            5000,
            synchronousPersist: true));
        resumedCollector.BindChildSession("child", "parent", "parent");
        resumedCollector.RecordPromptCacheRequestShape("child", PrefixShape(["a", "task", "reply"]), 2, 1);

        var diagnostics = store.GetEvents("child")
            .Where(evt => evt.Type == TraceEventType.SubAgentPrefixDiagnostic)
            .ToList();
        Assert.Single(diagnostics);
    }

    [Fact]
    public void TraceCollector_ExistingChildShapeIsNotBackfilledAfterResume()
    {
        var store = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var originalCollector = new TraceCollector(store);
        originalCollector.RecordPromptCacheRequestShape("parent", PrefixShape(["a"]), 1, 1);
        originalCollector.RecordPromptCacheRequestShape("child", PrefixShape(["a", "task"]), 1, 1);

        var resumedCollector = new TraceCollector(new TraceStore(
            _stateRuntime,
            5000,
            synchronousPersist: true));
        resumedCollector.BindChildSession("child", "parent", "parent");
        resumedCollector.RecordPromptCacheRequestShape("child", PrefixShape(["a", "task", "reply"]), 2, 1);

        Assert.DoesNotContain(
            store.GetEvents("child"),
            evt => evt.Type == TraceEventType.SubAgentPrefixDiagnostic);
    }

    [Fact]
    public void TraceStore_RoundTrips_Events_And_Summary_Via_StateDb()
    {
        var writer = new TraceStore(_stateRuntime, 5000);
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-1",
            Type = TraceEventType.SessionMetadata,
            FinalSystemPrompt = "system",
            ToolNames = ["ReadFile", "EditFile"]
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-1",
            Type = TraceEventType.Request,
            Content = "hello"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-1",
            Type = TraceEventType.Request,
            Content = "second request"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-1",
            Type = TraceEventType.TokenUsage,
            InputTokens = 11,
            OutputTokens = 7,
            CachedInputTokens = 3,
            CacheWriteInputTokens = 2,
            TotalTokens = 18
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-1",
            Type = TraceEventType.ToolCallCompleted,
            ToolName = "ReadFile",
            DurationMs = 42
        });
        writer.WaitForPendingPersistence();

        Assert.Equal("hello", writer.GetSession("thread-1")?.FirstUserRequest);

        var reader = new TraceStore(_stateRuntime, 5000);

        var session = reader.GetSession("thread-1");
        Assert.NotNull(session);
        Assert.Equal(2, session.RequestCount);
        Assert.Equal(11, session.TotalInputTokens);
        Assert.Equal(7, session.TotalOutputTokens);
        Assert.Equal(3, session.TotalCachedInputTokens);
        Assert.Equal(2, session.TotalCacheWriteInputTokens);
        Assert.Equal(6, session.TotalFreshInputTokens);
        Assert.Equal(1, session.TokenUsageCount);
        Assert.Equal(1, session.ToolCallCount);
        Assert.Equal(42, session.MaxToolDurationMs);
        Assert.Equal("system", session.FinalSystemPrompt);
        Assert.Equal("hello", session.FirstUserRequest);
        Assert.Contains("ReadFile", session.ToolNames);
        Assert.Equal(5, reader.GetEvents("thread-1").Count);

        var summary = reader.GetSummary();
        Assert.Equal(1, summary.SessionCount);
        Assert.Equal(2, summary.TotalRequests);
        Assert.Equal(18, summary.TotalTokens);
        Assert.Equal(2, summary.TotalCacheWriteInputTokens);
        Assert.Equal(1, summary.TotalToolCalls);
    }

    [Fact]
    public void TraceStore_StateDbSummary_DoesNotRequire_EventJson_Load()
    {
        var writer = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var startedAt = new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero);
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-summary-only",
            Type = TraceEventType.Request,
            Content = "hello",
            Timestamp = startedAt
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-summary-only",
            Type = TraceEventType.SessionMetadata,
            SystemPromptHash = "aaa",
            ToolSchemaHash = "bbb",
            PromptCacheEventKind = PromptCacheEventKinds.Baseline,
            Timestamp = startedAt.AddSeconds(1)
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-summary-only",
            Type = TraceEventType.SessionMetadata,
            SystemPromptHash = "ccc",
            ToolSchemaHash = "bbb",
            PromptCacheEventKind = PromptCacheEventKinds.Drift,
            PromptCacheChangedFields = [PromptCacheChangedFields.Prompt],
            Timestamp = startedAt.AddSeconds(2)
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-summary-only",
            Type = TraceEventType.TokenUsage,
            InputTokens = 11,
            OutputTokens = 7,
            Timestamp = startedAt.AddSeconds(3)
        });

        using (var connection = _stateRuntime.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE trace_events SET event_json = '{ broken json' WHERE session_key = $session_key";
            command.Parameters.AddWithValue("$session_key", "thread-summary-only");
            command.ExecuteNonQuery();
        }

        var reader = new TraceStore(_stateRuntime, 5000);

        var summary = reader.GetSummary();
        Assert.Equal(1, summary.SessionCount);
        Assert.Equal(1, summary.TotalRequests);
        Assert.Equal(18, summary.TotalTokens);

        var session = Assert.Single(reader.GetSessions());
        Assert.Equal("hello", session.FirstUserRequest);
        Assert.Equal("ccc", session.SystemPromptHash);
        Assert.Equal("bbb", session.ToolSchemaHash);
        Assert.Equal(1, session.PromptDriftCount);
        Assert.Equal(startedAt.AddSeconds(2), session.SessionMetadataCapturedAt);
        Assert.Equal(startedAt.AddSeconds(2), session.LastPromptCacheChangeAt);
        Assert.Equal(PromptCacheEventKinds.Drift, session.LastPromptCacheChangeKind);
        Assert.Equal([PromptCacheChangedFields.Prompt], session.LastPromptCacheChangedFields);
    }

    [Fact]
    public void TraceStore_ResponseTerminal_UpdatesFinishReasonWithoutResponseCount()
    {
        var writer = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-terminal",
            Type = TraceEventType.ResponseTerminal,
            FinishReason = "Length",
            ResponseId = "resp-terminal",
            MetadataJson = JsonSerializer.Serialize(new
            {
                hasText = false,
                terminalUpdateSeen = true
            })
        });

        var reader = new TraceStore(_stateRuntime, 5000);

        var session = reader.GetSession("thread-terminal");
        var evt = Assert.Single(reader.GetEvents("thread-terminal"));

        Assert.Equal("Length", session?.LastFinishReason);
        Assert.Equal(0, session?.ResponseCount);
        Assert.Equal(TraceEventType.ResponseTerminal, evt.Type);
        Assert.Equal("resp-terminal", evt.ResponseId);
        Assert.Contains("terminalUpdateSeen", evt.MetadataJson);
    }

    [Fact]
    public void TraceStore_PageQuery_Returns_Latest_StateDb_Events_Beyond_InMemory_Cap()
    {
        var writer = new TraceStore(_stateRuntime, 20, synchronousPersist: true);
        var startedAt = new DateTimeOffset(2026, 5, 29, 1, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 25; i++)
        {
            writer.Record(new TraceEvent
            {
                SessionKey = "thread-page",
                Type = TraceEventType.Request,
                Content = "request-" + i,
                Timestamp = startedAt.AddSeconds(i)
            });
        }

        writer.Record(new TraceEvent
        {
            SessionKey = "thread-page",
            Type = TraceEventType.MaintenanceForkRequest,
            Content = "latest maintenance",
            Timestamp = startedAt.AddSeconds(6000)
        });

        var reader = new TraceStore(_stateRuntime, 20);

        Assert.Equal(20, reader.GetEvents("thread-page").Count);
        Assert.DoesNotContain(reader.GetEvents("thread-page"), evt => evt.Content == "request-0");
        Assert.Contains(reader.GetEvents("thread-page"), evt => evt.Content == "latest maintenance");

        var firstPage = reader.GetEventPage("thread-page", limit: 10);
        Assert.Equal(10, firstPage.Events.Count);
        Assert.True(firstPage.HasMore);
        Assert.Equal("latest maintenance", firstPage.Events[^1].Content);
        Assert.NotNull(firstPage.OldestCursor);

        var secondPage = reader.GetEventPage("thread-page", limit: 10, beforeCursor: firstPage.OldestCursor);
        Assert.Equal(10, secondPage.Events.Count);
        Assert.DoesNotContain(
            secondPage.Events.Select(static evt => evt.Id),
            id => firstPage.Events.Any(evt => evt.Id == id));

        var maintenancePage = reader.GetEventPage("thread-page", limit: 1000, filter: "Maintenance");
        var maintenance = Assert.Single(maintenancePage.Events);
        Assert.Equal(TraceEventType.MaintenanceForkRequest, maintenance.Type);
        Assert.Equal("latest maintenance", maintenance.Content);
    }

    [Fact]
    public void TraceCollector_RecordThreadRollback_RoundTrips_And_MaintenanceFilterIncludesIt()
    {
        var writer = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var collector = new TraceCollector(writer);
        var timestamp = new DateTimeOffset(2026, 6, 1, 8, 30, 0, TimeSpan.Zero);

        collector.RecordThreadRollback("thread-rollback", "thread-rollback", 2, 3, timestamp);

        var reader = new TraceStore(_stateRuntime, 5000);

        var evt = Assert.Single(reader.GetEvents("thread-rollback"), e => e.Type == TraceEventType.ThreadRollback);
        Assert.Equal(timestamp, evt.Timestamp);
        Assert.Equal("Rollback removed 2 turn(s)", evt.Content);
        using var metadata = JsonDocument.Parse(evt.MetadataJson!);
        Assert.Equal("thread-rollback", metadata.RootElement.GetProperty("threadId").GetString());
        Assert.Equal(2, metadata.RootElement.GetProperty("numTurns").GetInt32());
        Assert.Equal(3, metadata.RootElement.GetProperty("remainingTurns").GetInt32());

        var page = reader.GetEventPage("thread-rollback", limit: 1000, filter: "Maintenance");
        var maintenance = Assert.Single(page.Events);
        Assert.Equal(TraceEventType.ThreadRollback, maintenance.Type);
    }

    [Fact]
    public void TraceStore_Counts_MaintenanceFork_Separately_From_Normal_Request_Response()
    {
        var store = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        store.Record(new TraceEvent { SessionKey = "thread-maint", Type = TraceEventType.Request, Content = "user" });
        store.Record(new TraceEvent { SessionKey = "thread-maint", Type = TraceEventType.MaintenanceForkRequest, Content = "maint request" });
        store.Record(new TraceEvent { SessionKey = "thread-maint", Type = TraceEventType.Response, Content = "assistant" });
        store.Record(new TraceEvent { SessionKey = "thread-maint", Type = TraceEventType.MaintenanceForkResponse, Content = "maint response" });

        var session = store.GetSession("thread-maint");
        Assert.NotNull(session);
        Assert.Equal(1, session.RequestCount);
        Assert.Equal(1, session.ResponseCount);
        Assert.Equal(1, session.MaintenanceForkRequestCount);
        Assert.Equal(1, session.MaintenanceForkResponseCount);

        var summary = store.GetSummary();
        Assert.Equal(1, summary.TotalRequests);
        Assert.Equal(1, summary.TotalResponses);
        Assert.Equal(1, summary.TotalMaintenanceForkRequests);
        Assert.Equal(1, summary.TotalMaintenanceForkResponses);
    }


    [Fact]
    public void TraceStore_StateBackedStore_DoesNotBuffer_Events_InMemory()
    {
        var store = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var largePayload = new string('x', 64 * 1024);

        store.Record(new TraceEvent
        {
            SessionKey = "state-backed-no-buffer",
            Type = TraceEventType.Request,
            Content = largePayload
        });
        store.Record(new TraceEvent
        {
            SessionKey = "state-backed-no-buffer",
            Type = TraceEventType.PromptCacheRequestShape,
            MetadataJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                requestShape = largePayload
            })
        });

        Assert.Equal(0, store.GetBufferedEventCount("state-backed-no-buffer"));

        var events = store.GetEvents("state-backed-no-buffer");
        Assert.Equal(2, events.Count);
        Assert.Contains(events, evt => evt.Type == TraceEventType.Request && evt.Content == largePayload);
        Assert.Contains(events, evt => evt.Type == TraceEventType.PromptCacheRequestShape && evt.MetadataJson?.Contains(largePayload) == true);

        var page = store.GetEventPage("state-backed-no-buffer", limit: 10);
        Assert.Equal(2, page.Events.Count);
        Assert.Equal(0, store.GetBufferedEventCount("state-backed-no-buffer"));
    }

    [Fact]
    public void TraceStore_InMemoryStore_Retains_Events_Up_To_Max()
    {
        var store = new TraceStore(maxEventsPerSession: 2);

        store.Record(new TraceEvent
        {
            SessionKey = "in-memory-retention",
            Type = TraceEventType.Request,
            Content = "one",
            Timestamp = new DateTimeOffset(2026, 6, 19, 1, 0, 0, TimeSpan.Zero)
        });
        store.Record(new TraceEvent
        {
            SessionKey = "in-memory-retention",
            Type = TraceEventType.Request,
            Content = "two",
            Timestamp = new DateTimeOffset(2026, 6, 19, 1, 0, 1, TimeSpan.Zero)
        });
        store.Record(new TraceEvent
        {
            SessionKey = "in-memory-retention",
            Type = TraceEventType.Request,
            Content = "three",
            Timestamp = new DateTimeOffset(2026, 6, 19, 1, 0, 2, TimeSpan.Zero)
        });

        Assert.Equal(2, store.GetBufferedEventCount("in-memory-retention"));
        Assert.Equal(["two", "three"], store.GetEvents("in-memory-retention").Select(evt => evt.Content ?? string.Empty).ToArray());
    }

    [Fact]
    public void TraceStore_RefreshFromDisk_Rebuilds_From_Shared_StateDb()
    {
        var reader = new TraceStore(_stateRuntime, 5000);

        var writer = new TraceStore(_stateRuntime, 5000);
        writer.Record(new TraceEvent
        {
            SessionKey = "shared-session",
            Type = TraceEventType.Request,
            Content = "ping"
        });
        writer.WaitForPendingPersistence();

        reader.RefreshFromDisk();

        Assert.Single(reader.GetSessions());
        Assert.Single(reader.GetEvents("shared-session"));
    }

    [Fact]
    public void TraceStore_Preserves_MaxToolDuration_When_Earlier_Event_Replaces_StartedAt()
    {
        var store = new TraceStore(_stateRuntime, 5000);
        var sessionKey = "session-with-earlier-event";
        var later = new DateTimeOffset(2026, 4, 21, 10, 0, 0, TimeSpan.Zero);
        var earlier = later.AddMinutes(-5);

        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.TokenUsage,
            Timestamp = later,
            InputTokens = 11,
            OutputTokens = 7
        });
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.ToolCallCompleted,
            Timestamp = later.AddSeconds(1),
            ToolName = "ToolA",
            DurationMs = 100
        });
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.ToolCallCompleted,
            Timestamp = later.AddSeconds(2),
            ToolName = "ToolB",
            DurationMs = 200
        });
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.ToolCallCompleted,
            Timestamp = later.AddSeconds(3),
            ToolName = "ToolC",
            DurationMs = 300
        });
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.Request,
            Timestamp = earlier,
            Content = "trigger clone with earlier event"
        });

        var session = store.GetSession(sessionKey);
        Assert.NotNull(session);
        Assert.Equal(earlier, session.StartedAt);
        Assert.Equal(11, session.TotalInputTokens);
        Assert.Equal(7, session.TotalOutputTokens);
        Assert.Equal(3, session.ToolCallCount);
        Assert.Equal(600, session.TotalToolDurationMs);
        Assert.Equal(300, session.MaxToolDurationMs);
    }

    [Fact]
    public async Task TraceSessionBindingStore_InferBindings_For_Main_Child_And_Unbound_Sessions()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        var writer = new TraceStore(_stateRuntime, 5000);
        writer.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Request,
            Content = "root"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = $"{thread.Id}:sub:child1",
            Type = TraceEventType.Request,
            Content = "child"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "standalone:trace",
            Type = TraceEventType.Request,
            Content = "standalone"
        });
        writer.WaitForPendingPersistence();

        var rootBinding = writer.DescribeSessionDeletion(thread.Id);
        Assert.Equal("threadMain", rootBinding.BindingKind);
        Assert.Equal(thread.Id, rootBinding.RootThreadId);

        var childBinding = writer.DescribeSessionDeletion($"{thread.Id}:sub:child1");
        Assert.Equal("threadChild", childBinding.BindingKind);
        Assert.Equal(thread.Id, childBinding.RootThreadId);

        var standaloneBinding = writer.DescribeSessionDeletion("standalone:trace");
        Assert.Equal("unbound", standaloneBinding.BindingKind);
        Assert.Null(standaloneBinding.RootThreadId);
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteThreadCascade_Removes_Thread_And_Bound_Traces()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_stateRuntime, 5000);
        var persistence = new SessionPersistenceService(threadStore, traceStore, stateRuntime: _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        InsertThreadContextUsage(thread.Id);

        traceStore.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Request,
            Content = "root"
        });
        traceStore.Record(new TraceEvent
        {
            SessionKey = $"{thread.Id}:sub:child1",
            Type = TraceEventType.Request,
            Content = "child"
        });
        traceStore.WaitForPendingPersistence();

        await persistence.DeleteThreadCascadeAsync(thread.Id);

        Assert.Null(await threadStore.LoadThreadAsync(thread.Id));
        Assert.Null(traceStore.GetSession(thread.Id));
        Assert.Null(traceStore.GetSession($"{thread.Id}:sub:child1"));
        Assert.Equal("unbound", traceStore.DescribeSessionDeletion(thread.Id).BindingKind);
        Assert.Equal("unbound", traceStore.DescribeSessionDeletion($"{thread.Id}:sub:child1").BindingKind);

        using var connection = _stateRuntime.OpenConnection();
        using var threadCommand = connection.CreateCommand();
        threadCommand.CommandText = "SELECT COUNT(*) FROM threads WHERE thread_id = $thread_id";
        threadCommand.Parameters.AddWithValue("$thread_id", thread.Id);
        Assert.Equal(0L, (long)(threadCommand.ExecuteScalar() ?? 0L));

        using var eventCommand = connection.CreateCommand();
        eventCommand.CommandText = "SELECT COUNT(*) FROM trace_events WHERE session_key LIKE $session_key";
        eventCommand.Parameters.AddWithValue("$session_key", $"{thread.Id}%");
        Assert.Equal(0L, (long)(eventCommand.ExecuteScalar() ?? 0L));
        Assert.Equal(0L, CountRows("thread_context_usage", $"thread_id = '{thread.Id}'"));
        Assert.Equal(0L, CountRows("trace_session_bindings", $"root_thread_id = '{thread.Id}'"));
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteTraceSessionAsync_Removes_Unbound_Trace_Only()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_stateRuntime, 5000);
        var persistence = new SessionPersistenceService(threadStore, traceStore, stateRuntime: _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        traceStore.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Request,
            Content = "bound"
        });
        traceStore.Record(new TraceEvent
        {
            SessionKey = "standalone:trace",
            Type = TraceEventType.Request,
            Content = "unbound"
        });
        traceStore.WaitForPendingPersistence();

        await persistence.DeleteTraceSessionAsync(
            "standalone:trace",
            (_, _) => throw new InvalidOperationException("Bound thread deletion should not run for unbound traces."));

        Assert.NotNull(await threadStore.LoadThreadAsync(thread.Id));
        Assert.NotNull(traceStore.GetSession(thread.Id));
        Assert.Null(traceStore.GetSession("standalone:trace"));
        Assert.Equal("unbound", traceStore.DescribeSessionDeletion("standalone:trace").BindingKind);
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteTraceSessionAsync_For_ChildTrace_Deletes_Root_Thread()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_stateRuntime, 5000);
        var persistence = new SessionPersistenceService(threadStore, traceStore, stateRuntime: _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        traceStore.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Request,
            Content = "root"
        });
        traceStore.Record(new TraceEvent
        {
            SessionKey = $"{thread.Id}:sub:child1",
            Type = TraceEventType.Request,
            Content = "child"
        });
        traceStore.WaitForPendingPersistence();

        string? deletedThreadId = null;
        await persistence.DeleteTraceSessionAsync(
            $"{thread.Id}:sub:child1",
            async (threadId, ct) =>
            {
                deletedThreadId = threadId;
                await persistence.DeleteThreadCascadeAsync(threadId, ct);
            });

        Assert.Equal(thread.Id, deletedThreadId);
        Assert.Null(await threadStore.LoadThreadAsync(thread.Id));
        Assert.Null(traceStore.GetSession(thread.Id));
        Assert.Null(traceStore.GetSession($"{thread.Id}:sub:child1"));
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteTraceSessionsAsync_RemovesThreadsAndTraces()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_stateRuntime, 5000);
        var persistence = new SessionPersistenceService(threadStore, traceStore, stateRuntime: _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        traceStore.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Request,
            Content = "root"
        });
        traceStore.Record(new TraceEvent
        {
            SessionKey = "standalone:trace",
            Type = TraceEventType.Request,
            Content = "standalone"
        });
        traceStore.WaitForPendingPersistence();

        await persistence.DeleteTraceSessionsAsync([thread.Id, "standalone:trace"]);
        var compacted = persistence.CompactStateIfWorthwhile(force: true);

        Assert.True(compacted);
        Assert.Null(await threadStore.LoadThreadAsync(thread.Id));
        Assert.Null(traceStore.GetSession(thread.Id));
        Assert.Null(traceStore.GetSession("standalone:trace"));
    }

    private void InsertThreadContextUsage(string threadId)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_context_usage(thread_id, context_usage_tokens, updated_at)
            VALUES ($thread_id, $context_usage_tokens, $updated_at)
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$context_usage_tokens", 123);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    private long CountRows(string tableName, string? whereClause = null)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(whereClause)
            ? $"SELECT COUNT(*) FROM {tableName}"
            : $"SELECT COUNT(*) FROM {tableName} WHERE {whereClause}";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static SessionThread CreateThread() => new()
    {
        Id = SessionIdGenerator.NewThreadId(),
        WorkspacePath = "/workspace",
        UserId = "user1",
        OriginChannel = "console",
        Status = ThreadStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow,
        HistoryMode = HistoryMode.Server
    };

    private static PromptCacheRequestShapeSnapshot PrefixShape(IReadOnlyList<string> inputItemHashes) => new(
        1,
        "openai-responses",
        "gpt-test",
        "cache",
        "thread",
        "instructions",
        "tools",
        "reasoning",
        "input",
        inputItemHashes.Count,
        inputItemHashes,
        100,
        0,
        0,
        0,
        0,
        0,
        null,
        false,
        false,
        null,
        "auto",
        1,
        true);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
