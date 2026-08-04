using System.Text.Json;
using DotCraft.Persistence;
using DotCraft.Tracing;
using DotCraft.Sessions;
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

        var reader = new TraceStore(_stateRuntime, 5000);

        var session = reader.GetSession("thread-1");
        Assert.NotNull(session);
        Assert.Equal(1, session.RequestCount);
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
        Assert.Equal(4, reader.GetEvents("thread-1").Count);

        var summary = reader.GetSummary();
        Assert.Equal(1, summary.SessionCount);
        Assert.Equal(1, summary.TotalRequests);
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
        var writer = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        var startedAt = new DateTimeOffset(2026, 5, 29, 1, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5005; i++)
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

        var reader = new TraceStore(_stateRuntime, 5000);

        Assert.Equal(5000, reader.GetEvents("thread-page").Count);
        Assert.DoesNotContain(reader.GetEvents("thread-page"), evt => evt.Content == "request-0");
        Assert.Contains(reader.GetEvents("thread-page"), evt => evt.Content == "latest maintenance");

        var firstPage = reader.GetEventPage("thread-page", limit: 1000);
        Assert.Equal(1000, firstPage.Events.Count);
        Assert.True(firstPage.HasMore);
        Assert.Equal("latest maintenance", firstPage.Events[^1].Content);
        Assert.NotNull(firstPage.OldestCursor);

        var secondPage = reader.GetEventPage("thread-page", limit: 1000, beforeCursor: firstPage.OldestCursor);
        Assert.Equal(1000, secondPage.Events.Count);
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
    public void TraceStore_Captures_FirstUserRequest_And_Does_Not_Overwrite_It()
    {
        var writer = new TraceStore(_stateRuntime, 5000);
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-first-request",
            Type = TraceEventType.Request,
            Content = "first request"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-first-request",
            Type = TraceEventType.Request,
            Content = "second request"
        });
        writer.WaitForPendingPersistence();

        var liveSession = writer.GetSession("thread-first-request");
        Assert.NotNull(liveSession);
        Assert.Equal(2, liveSession.RequestCount);
        Assert.Equal("first request", liveSession.FirstUserRequest);

        var reader = new TraceStore(_stateRuntime, 5000);

        var restoredSession = reader.GetSession("thread-first-request");
        Assert.NotNull(restoredSession);
        Assert.Equal(2, restoredSession.RequestCount);
        Assert.Equal("first request", restoredSession.FirstUserRequest);
    }

    [Fact]
    public void TraceStore_RoundTrips_PromptCacheDiagnosticFields_Via_StateDb()
    {
        var writer = new TraceStore(_stateRuntime, 5000);
        writer.Record(new TraceEvent
        {
            SessionKey = "prompt-cache-session",
            Type = TraceEventType.SessionMetadata,
            FinalSystemPrompt = "system",
            ToolNames = ["ReadFile"],
            SystemPromptHash = "aaa",
            ToolSchemaHash = "bbb",
            PromptCacheEventKind = PromptCacheEventKinds.Baseline,
            CurrentSystemPromptHash = "aaa",
            CurrentToolSchemaHash = "bbb"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "prompt-cache-session",
            Type = TraceEventType.SessionMetadata,
            FinalSystemPrompt = "system changed",
            ToolNames = ["ReadFile"],
            SystemPromptHash = "ccc",
            ToolSchemaHash = "bbb",
            PromptDriftDetected = true,
            PromptCacheEventKind = PromptCacheEventKinds.Drift,
            PromptCacheChangedFields = [PromptCacheChangedFields.Prompt],
            PreviousSystemPromptHash = "aaa",
            PreviousToolSchemaHash = "bbb",
            CurrentSystemPromptHash = "ccc",
            CurrentToolSchemaHash = "bbb"
        });
        writer.WaitForPendingPersistence();

        var reader = new TraceStore(_stateRuntime, 5000);

        var drift = reader.GetEvents("prompt-cache-session")
            .Single(e => e.PromptCacheEventKind == PromptCacheEventKinds.Drift);
        Assert.NotNull(drift.PromptCacheChangedFields);
        Assert.Equal([PromptCacheChangedFields.Prompt], drift.PromptCacheChangedFields);
        Assert.Equal("aaa", drift.PreviousSystemPromptHash);
        Assert.Equal("ccc", drift.CurrentSystemPromptHash);

        var session = reader.GetSession("prompt-cache-session");
        Assert.NotNull(session);
        Assert.Equal(1, session.PromptDriftCount);
        Assert.Equal(PromptCacheEventKinds.Drift, session.LastPromptCacheChangeKind);
        Assert.Equal([PromptCacheChangedFields.Prompt], session.LastPromptCacheChangedFields);

        var listed = Assert.Single(reader.GetSessions());
        Assert.Equal("ccc", listed.SystemPromptHash);
        Assert.Equal("bbb", listed.ToolSchemaHash);
        Assert.Equal(1, listed.PromptDriftCount);
        Assert.Equal(PromptCacheEventKinds.Drift, listed.LastPromptCacheChangeKind);
        Assert.Equal([PromptCacheChangedFields.Prompt], listed.LastPromptCacheChangedFields);
    }


    [Fact]
    public void TraceStore_RoundTrips_PromptCacheRequestShape_Via_StateDb()
    {
        var writer = new TraceStore(_stateRuntime, 5000, synchronousPersist: true);
        writer.Record(new TraceEvent
        {
            SessionKey = "prompt-cache-shape-session",
            Type = TraceEventType.PromptCacheRequestShape,
            ModelId = "gpt-test",
            RequestIndex = 2,
            MetadataJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                protocol = "openai-responses",
                inputHash = "sha256:input",
                inputItemHashes = new[] { "sha256:item" }
            })
        });

        var reader = new TraceStore(_stateRuntime, 5000);

        var evt = Assert.Single(reader.GetEvents("prompt-cache-shape-session"));
        Assert.Equal(TraceEventType.PromptCacheRequestShape, evt.Type);
        Assert.Equal("gpt-test", evt.ModelId);
        Assert.Equal(2, evt.RequestIndex);
        Assert.Contains("sha256:item", evt.MetadataJson);

        var page = reader.GetEventPage("prompt-cache-shape-session", limit: 1000, filter: "PromptCacheRequestShape");
        Assert.Single(page.Events);
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
    public void TokenUsageStore_RoundTrips_Records_Via_StateDb()
    {
        var writer = new TokenUsageStore(_stateRuntime);
        writer.Record(new TokenUsageRecord
        {
            SourceId = "qq",
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.User,
            SubjectId = "u1",
            SubjectLabel = "Alice",
            SessionKey = "thread-1",
            ThreadId = "thread-1",
            InputTokens = 3,
            OutputTokens = 5,
            CachedInputTokens = 1,
            CacheWriteInputTokens = 1,
            ReasoningOutputTokens = 2,
            LlmCallCount = 2
        });
        writer.Record(new TokenUsageRecord
        {
            SourceId = "qq",
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.User,
            SubjectId = "u2",
            SubjectLabel = "Bob",
            ContextKind = TokenUsageContextKinds.Group,
            ContextId = "42",
            ContextLabel = "Team",
            SessionKey = "thread-2",
            ThreadId = "thread-2",
            InputTokens = 7,
            OutputTokens = 11,
            CachedInputTokens = 4,
            CacheWriteInputTokens = 2,
            ReasoningOutputTokens = 3,
            LlmCallCount = 3
        });

        var reader = new TokenUsageStore(_stateRuntime);

        var summary = Assert.Single(reader.GetSourceSummaries());
        Assert.Equal("qq", summary.SourceId);
        Assert.Equal(26, summary.TotalTokens);
        Assert.Equal(5, summary.TotalCachedInputTokens);
        Assert.Equal(3, summary.TotalCacheWriteInputTokens);
        Assert.Equal(2, summary.TotalFreshInputTokens);
        Assert.Equal(5, summary.TotalNonCachedInputTokens);
        Assert.Equal(5, summary.TotalReasoningOutputTokens);
        Assert.Equal(0.5, summary.CacheHitRate);
        Assert.Equal(2, summary.RequestCount);
        Assert.Equal(5, summary.LlmCallCount);
        Assert.Equal(2, summary.SubjectCount);
        Assert.Equal(1, summary.ContextCount);
        Assert.Equal(TokenUsageSubjectKinds.User, summary.SubjectKind);
        Assert.Equal(TokenUsageContextKinds.Group, summary.ContextKind);

        Assert.Equal(2, reader.GetSubjectBreakdown("qq").Count);
        var context = Assert.Single(reader.GetContextBreakdown("qq"));
        Assert.Equal("42", context.Id);
        Assert.Equal("Team", context.Label);
        Assert.Equal(1, context.RelatedSubjectCount);
        Assert.Equal(4, context.TotalCachedInputTokens);
        Assert.Equal(2, context.TotalCacheWriteInputTokens);
        Assert.Equal(3, context.LlmCallCount);
    }

    [Fact]
    public void TokenUsageStore_StateDb_Aggregates_MixedKinds_And_Sorts_Breakdowns()
    {
        var writer = new TokenUsageStore(_stateRuntime);
        writer.Record(new TokenUsageRecord
        {
            Timestamp = new DateTimeOffset(2026, 4, 24, 8, 0, 0, TimeSpan.Zero),
            SourceId = "mixed",
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.User,
            SubjectId = "user-2",
            SubjectLabel = "Zulu",
            ContextKind = TokenUsageContextKinds.Group,
            ContextId = "group-1",
            ContextLabel = "Team",
            InputTokens = 2,
            OutputTokens = 3
        });
        writer.Record(new TokenUsageRecord
        {
            Timestamp = new DateTimeOffset(2026, 4, 24, 8, 5, 0, TimeSpan.Zero),
            SourceId = "mixed",
            SourceMode = TokenUsageSourceModes.ClientManaged,
            SubjectKind = TokenUsageSubjectKinds.Session,
            SubjectId = "session-1",
            SubjectLabel = "Alpha",
            ContextKind = "room",
            ContextId = "room-1",
            ContextLabel = "Room",
            InputTokens = 4,
            OutputTokens = 1
        });
        writer.Record(new TokenUsageRecord
        {
            Timestamp = new DateTimeOffset(2026, 4, 24, 8, 10, 0, TimeSpan.Zero),
            SourceId = "apple",
            SourceMode = TokenUsageSourceModes.ClientManaged,
            SubjectKind = TokenUsageSubjectKinds.Session,
            SubjectId = "session-2",
            SubjectLabel = "Apple Session",
            InputTokens = 5,
            OutputTokens = 5
        });

        var reader = new TokenUsageStore(_stateRuntime);

        var summaries = reader.GetSourceSummaries();
        Assert.Equal(["apple", "mixed"], summaries.Select(summary => summary.SourceId).ToArray());
        Assert.Null(summaries[0].ContextKind);

        var mixed = summaries[1];
        Assert.Equal(TokenUsageSourceModes.Mixed, mixed.SourceMode);
        Assert.Equal(TokenUsageSubjectKinds.Mixed, mixed.SubjectKind);
        Assert.Equal(TokenUsageContextKinds.Mixed, mixed.ContextKind);
        Assert.Equal(2, mixed.SubjectCount);
        Assert.Equal(2, mixed.ContextCount);
        Assert.Equal(2, mixed.RequestCount);
        Assert.Equal(10, mixed.TotalTokens);

        var subjects = reader.GetSubjectBreakdown("MIXED");
        Assert.Collection(
            subjects,
            first =>
            {
                Assert.Equal(TokenUsageSubjectKinds.Session, first.Kind);
                Assert.Equal("session-1", first.Id);
                Assert.Equal("Alpha", first.Label);
                Assert.Equal(5, first.TotalTokens);
            },
            second =>
            {
                Assert.Equal(TokenUsageSubjectKinds.User, second.Kind);
                Assert.Equal("user-2", second.Id);
                Assert.Equal("Zulu", second.Label);
                Assert.Equal(5, second.TotalTokens);
            });

        var contexts = reader.GetContextBreakdown("mixed");
        Assert.Collection(
            contexts,
            first =>
            {
                Assert.Equal("room", first.Kind);
                Assert.Equal("room-1", first.Id);
                Assert.Equal("Room", first.Label);
                Assert.Equal(1, first.RelatedSubjectCount);
                Assert.Equal(5, first.TotalTokens);
            },
            second =>
            {
                Assert.Equal(TokenUsageContextKinds.Group, second.Kind);
                Assert.Equal("group-1", second.Id);
                Assert.Equal("Team", second.Label);
                Assert.Equal(1, second.RelatedSubjectCount);
                Assert.Equal(5, second.TotalTokens);
            });
    }

    [Fact]
    public void TokenUsageStore_DeleteDashboardUsageRecords_Removes_Linked_Rows_Only()
    {
        var store = new TokenUsageStore(_stateRuntime);
        store.Record(CreateUsageRecord("thread-1", "thread-1"));
        store.Record(CreateUsageRecord("thread-2", "thread-2"));
        store.Record(CreateUsageRecord(null, null));

        var deleted = store.DeleteDashboardUsageRecords(["thread-1"], ["thread-2"]);

        Assert.Equal(2, deleted);
        Assert.Equal(1L, CountRows("dashboard_usage_records"));
        Assert.Equal(1L, CountRows("dashboard_usage_records", "thread_id IS NULL AND session_key IS NULL"));
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
        var tokenUsageStore = new TokenUsageStore(_stateRuntime);
        var persistence = new SessionPersistenceService(threadStore, traceStore, tokenUsageStore, _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        InsertThreadContextUsage(thread.Id);
        tokenUsageStore.Record(CreateUsageRecord(thread.Id, thread.Id));
        tokenUsageStore.Record(CreateUsageRecord(thread.Id, $"{thread.Id}:sub:child1"));
        tokenUsageStore.Record(CreateUsageRecord(null, null));

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
        Assert.Equal(0L, CountRows("dashboard_usage_records", $"thread_id = '{thread.Id}' OR session_key = '{thread.Id}' OR session_key = '{thread.Id}:sub:child1'"));
        Assert.Equal(1L, CountRows("dashboard_usage_records", "thread_id IS NULL AND session_key IS NULL"));
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteTraceSessionAsync_Removes_Unbound_Trace_Only()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_stateRuntime, 5000);
        var tokenUsageStore = new TokenUsageStore(_stateRuntime);
        var persistence = new SessionPersistenceService(threadStore, traceStore, tokenUsageStore, _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);
        tokenUsageStore.Record(CreateUsageRecord(thread.Id, thread.Id));
        tokenUsageStore.Record(CreateUsageRecord(null, "standalone:trace"));

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
        Assert.Equal(0L, CountRows("dashboard_usage_records", "session_key = 'standalone:trace'"));
        Assert.Equal(1L, CountRows("dashboard_usage_records", $"thread_id = '{thread.Id}'"));
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteTraceSessionAsync_For_ChildTrace_Deletes_Root_Thread()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_stateRuntime, 5000);
        var tokenUsageStore = new TokenUsageStore(_stateRuntime);
        var persistence = new SessionPersistenceService(threadStore, traceStore, tokenUsageStore, _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);
        tokenUsageStore.Record(CreateUsageRecord(thread.Id, thread.Id));
        tokenUsageStore.Record(CreateUsageRecord(thread.Id, $"{thread.Id}:sub:child1"));

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
        Assert.Equal(0L, CountRows("dashboard_usage_records"));
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteTraceSessionsAsync_Removes_Related_Usage_And_Preserves_Global_Usage()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_stateRuntime, 5000);
        var tokenUsageStore = new TokenUsageStore(_stateRuntime);
        var persistence = new SessionPersistenceService(threadStore, traceStore, tokenUsageStore, _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);
        tokenUsageStore.Record(CreateUsageRecord(thread.Id, thread.Id));
        tokenUsageStore.Record(CreateUsageRecord(null, "standalone:trace"));
        tokenUsageStore.Record(CreateUsageRecord(null, null));

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
        Assert.Equal(1L, CountRows("dashboard_usage_records"));
        Assert.Equal(1L, CountRows("dashboard_usage_records", "thread_id IS NULL AND session_key IS NULL"));
    }

    [Fact]
    public void StateRuntime_CompactIfWorthwhile_ForcedVacuum_ReclaimsFreePages()
    {
        using (var connection = _stateRuntime.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE cleanup_probe(payload TEXT NOT NULL);
                WITH RECURSIVE n(x) AS (
                    SELECT 1
                    UNION ALL
                    SELECT x + 1 FROM n WHERE x < 200
                )
                INSERT INTO cleanup_probe(payload)
                SELECT printf('%4096s', 'x') FROM n;
                DELETE FROM cleanup_probe;
                """;
            command.ExecuteNonQuery();
        }

        var beforeFreelist = ReadPragmaLong("freelist_count");

        var compacted = _stateRuntime.CompactIfWorthwhile(force: true);

        Assert.True(compacted);
        Assert.True(ReadPragmaLong("freelist_count") <= beforeFreelist);
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

    private long ReadPragmaLong(string pragmaName)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName}";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static TokenUsageRecord CreateUsageRecord(string? threadId, string? sessionKey) => new()
    {
        SourceId = "dashboard",
        SourceMode = TokenUsageSourceModes.ServerManaged,
        SubjectKind = threadId == null ? TokenUsageSubjectKinds.User : TokenUsageSubjectKinds.Thread,
        SubjectId = threadId ?? "global-user",
        SubjectLabel = threadId ?? "Global User",
        ThreadId = threadId,
        SessionKey = sessionKey,
        InputTokens = 10,
        OutputTokens = 5
    };

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

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }
}
