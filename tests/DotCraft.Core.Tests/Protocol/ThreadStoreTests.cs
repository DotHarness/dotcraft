using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using DotCraft.Plugins;
using DotCraft.Protocol;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DynamicToolCallPayload = DotCraft.Sessions.DynamicToolCallPayload;
using QueuedTurnInput = DotCraft.Sessions.QueuedTurnInput;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using ImageGenerationPayload = DotCraft.Sessions.ImageGenerationPayload;
using McpToolCallPayload = DotCraft.Sessions.McpToolCallPayload;
using ReasoningContentPayload = DotCraft.Sessions.ReasoningContentPayload;
using SubAgentThreadSource = DotCraft.Sessions.SubAgentThreadSource;
using SystemNoticePayload = DotCraft.Sessions.SystemNoticePayload;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using TokenUsageInfo = DotCraft.Sessions.TokenUsageInfo;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using ToolPresentationPayload = DotCraft.Sessions.ToolPresentationPayload;
using ToolResultPayload = DotCraft.Sessions.ToolResultPayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Unit tests for ThreadStore persistence and thread discovery.
/// Uses a temp directory so tests are hermetic.
/// </summary>
public sealed class ThreadStoreTests : IDisposable
{
    private readonly string _root;
    private readonly ThreadStore _store;

    public ThreadStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ThreadStoreTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _store = new ThreadStore(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    // -------------------------------------------------------------------------
    // Thread save / load
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveThread_ThenLoadThread_RoundTrip()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);

        Assert.NotNull(loaded);
        Assert.Equal(thread.Id, loaded.Id);
        Assert.Equal(thread.WorkspacePath, loaded.WorkspacePath);
        Assert.Equal(thread.UserId, loaded.UserId);
        Assert.Equal(thread.OriginChannel, loaded.OriginChannel);
        Assert.Equal(thread.Status, loaded.Status);
        Assert.Equal(thread.HistoryMode, loaded.HistoryMode);
    }

    [Fact]
    public async Task RolloutPaths_ForSanitizedNameCollisions_AreDistinct()
    {
        var first = CreateThread();
        first.Id = "a:b";
        var second = CreateThread();
        second.Id = "ab";

        await _store.SaveThreadAsync(first);
        await _store.SaveThreadAsync(second);

        var rolloutStore = new ThreadRolloutStore(_root);
        var firstPath = rolloutStore.GetExpectedPath(first.Id, archived: false);
        var secondPath = rolloutStore.GetExpectedPath(second.Id, archived: false);
        Assert.NotEqual(firstPath, secondPath);
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
        Assert.Equal(first.Id, (await _store.LoadThreadAsync(first.Id))?.Id);
        Assert.Equal(second.Id, (await _store.LoadThreadAsync(second.Id))?.Id);
    }

    [Fact]
    public async Task SaveThread_CompleteSubAgentSource_RoundTripsThroughRolloutAndIndex()
    {
        var thread = CreateThread();
        thread.OriginChannel = SubAgentThreadOrigin.ChannelName;
        thread.Source = ThreadSource.ForSubAgent(new SubAgentThreadSource
        {
            ParentThreadId = "thread_parent",
            ParentTurnId = "turn_parent",
            SpawnCallId = "call_spawn",
            RootThreadId = "thread_root",
            Depth = 3,
            AgentPath = "root/reviewer",
            TaskName = "review",
            AgentNickname = "reviewer",
            AgentRole = "critic",
            ProfileName = "strict",
            RuntimeType = "process",
            SupportsSendInput = true,
            SupportsResume = true,
            SupportsSendMessage = true,
            SupportsFollowupTask = true,
            SupportsClose = false
        });

        await _store.SaveThreadAsync(thread);

        var loaded = Assert.IsType<SessionThread>(await _store.LoadThreadAsync(thread.Id));
        Assert.Equal(
            JsonSerializer.Serialize(thread.Source, SessionJsonOptions.Default),
            JsonSerializer.Serialize(loaded.Source, SessionJsonOptions.Default));
        var summary = Assert.Single(await _store.LoadIndexAsync(), item => item.Id == thread.Id);
        Assert.Equal(
            JsonSerializer.Serialize(thread.Source, SessionJsonOptions.Default),
            JsonSerializer.Serialize(summary.Source, SessionJsonOptions.Default));
    }

    [Fact]
    public async Task SaveThread_SpawnedUserSource_RoundTrips()
    {
        var thread = CreateThread();
        thread.Source = ThreadSource.SpawnedFromThread("thread_parent");

        await _store.SaveThreadAsync(thread);

        var loaded = Assert.IsType<SessionThread>(await _store.LoadThreadAsync(thread.Id));
        Assert.Equal(ThreadSourceKinds.User, loaded.Source.Kind);
        Assert.Equal("thread_parent", loaded.Source.SpawnedFromThreadId);
        Assert.Null(loaded.Source.SubAgent);
    }

    [Fact]
    public async Task SaveThread_WhenSourceChanges_AppendsNewCanonicalBaseline()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);
        thread.Source = ThreadSource.SpawnedFromThread("thread_parent");

        await _store.SaveThreadAsync(thread);

        var records = await File.ReadAllLinesAsync(GetCanonicalPath(thread.Id, archived: false));
        Assert.Equal(2, records.Count(line => line.Contains("\"kind\":\"thread_opened\"", StringComparison.Ordinal)));
        var loaded = Assert.IsType<SessionThread>(await _store.LoadThreadAsync(thread.Id));
        Assert.Equal("thread_parent", loaded.Source.SpawnedFromThreadId);
    }

    [Fact]
    public async Task LoadThread_UnknownSourceSchema_FailsExplicitly()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);
        var lines = await File.ReadAllLinesAsync(path);
        var header = JsonNode.Parse(lines[0])!.AsObject();
        header["threadOpened"]!["source"]!["schemaVersion"] = 99;
        lines[0] = header.ToJsonString(SessionJsonOptions.Default);
        await File.WriteAllLinesAsync(path, lines);

        await Assert.ThrowsAsync<NotSupportedException>(() => new ThreadStore(_root).LoadThreadAsync(thread.Id));
    }

    [Fact]
    public async Task Projection_WhenAttachmentUpdateFails_DoesNotAdvanceOffsetAndRepairsLater()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "request", "answer");
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);
        long confirmedBefore;
        using (var connection = OpenStateConnection())
        {
            using var read = connection.CreateCommand();
            read.CommandText = "SELECT projected_rollout_offset FROM threads WHERE thread_id = $id";
            read.Parameters.AddWithValue("$id", thread.Id);
            confirmedBefore = (long)read.ExecuteScalar()!;

            using var trigger = connection.CreateCommand();
            trigger.CommandText = """
                CREATE TRIGGER fail_thread_attachment_projection
                BEFORE INSERT ON thread_attachments
                BEGIN
                    SELECT RAISE(ABORT, 'injected attachment projection failure');
                END;
                """;
            trigger.ExecuteNonQuery();
        }

        var imageDir = Path.Combine(_root, "attachments", "images");
        Directory.CreateDirectory(imageDir);
        var imagePath = Path.Combine(imageDir, "projection.png");
        await File.WriteAllBytesAsync(imagePath, [1, 2, 3]);
        var user = Assert.IsType<UserMessagePayload>(thread.Turns[0].Input!.Payload);
        thread.Turns[0].Input!.Payload = user with
        {
            MaterializedInputParts = [new SessionWireInputPart { Type = "localImage", Path = imagePath }]
        };
        await _store.SaveThreadAsync(thread);

        using (var connection = OpenStateConnection())
        {
            using var read = connection.CreateCommand();
            read.CommandText = "SELECT projected_rollout_offset FROM threads WHERE thread_id = $id";
            read.Parameters.AddWithValue("$id", thread.Id);
            Assert.Equal(confirmedBefore, (long)read.ExecuteScalar()!);

            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TRIGGER fail_thread_attachment_projection";
            drop.ExecuteNonQuery();
        }

        await _store.LoadIndexAsync();

        using (var connection = OpenStateConnection())
        {
            using var read = connection.CreateCommand();
            read.CommandText = """
                SELECT projected_rollout_offset,
                       (SELECT COUNT(*) FROM thread_attachments WHERE thread_id = $id)
                FROM threads
                WHERE thread_id = $id
                """;
            read.Parameters.AddWithValue("$id", thread.Id);
            using var reader = read.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(new FileInfo(path).Length, reader.GetInt64(0));
            Assert.Equal(1, reader.GetInt64(1));
        }
    }

    [Fact]
    public async Task LoadIndex_ReadRepair_DoesNotOverwriteAuthoritativeGoal()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);
        var goal = NewGoal(ThreadGoalStatus.Active) with { ThreadId = thread.Id };
        await _store.UpsertThreadGoalAsync(goal);
        var path = GetCanonicalPath(thread.Id, archived: false);
        await File.AppendAllTextAsync(path, "{}" + Environment.NewLine);

        await _store.LoadIndexAsync();

        var restored = await _store.GetThreadGoalAsync(thread.Id);
        Assert.NotNull(restored);
        Assert.Equal(goal.GoalId, restored.GoalId);
        Assert.Equal(goal.Objective, restored.Objective);
    }

    [Fact]
    public async Task DeleteThread_WhenRolloutDeleteFails_PreservesDatabaseStateAndAttachments()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "request", "answer");
        var imageDir = Path.Combine(_root, "attachments", "images");
        Directory.CreateDirectory(imageDir);
        var imagePath = Path.Combine(imageDir, "preserved.png");
        await File.WriteAllBytesAsync(imagePath, [1, 2, 3]);
        var user = Assert.IsType<UserMessagePayload>(thread.Turns[0].Input!.Payload);
        thread.Turns[0].Input!.Payload = user with
        {
            MaterializedInputParts = [new SessionWireInputPart { Type = "localImage", Path = imagePath }]
        };
        await _store.SaveThreadAsync(thread);
        var goal = NewGoal(ThreadGoalStatus.Active) with { ThreadId = thread.Id };
        await _store.UpsertThreadGoalAsync(goal);
        var rolloutPath = GetCanonicalPath(thread.Id, archived: false);
        var failingStore = new ThreadStore(
            _root,
            _store.StateDatabase,
            (_, _) => throw new IOException("injected rollout delete failure"));

        var error = Assert.Throws<IOException>(() => failingStore.DeleteThread(thread.Id));

        Assert.Contains("injected rollout delete failure", error.Message);
        Assert.True(File.Exists(rolloutPath));
        Assert.True(File.Exists(imagePath));
        Assert.NotNull(await _store.GetThreadGoalAsync(thread.Id));
        Assert.Contains(await _store.LoadIndexAsync(), summary => summary.Id == thread.Id);
        await failingStore.DisposeAsync();
    }

    [Fact]
    public async Task LoadThread_NonExistent_ReturnsNull()
    {
        var loaded = await _store.LoadThreadAsync("nonexistent_id");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveThread_Overwrites_PreviousVersion()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);

        thread.Status = ThreadStatus.Paused;
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Paused, loaded.Status);
    }

    [Fact]
    public async Task LoadThread_SkipsCorruptRolloutLines()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "reply");
        await _store.SaveThreadAsync(thread);

        await File.AppendAllLinesAsync(GetCanonicalPath(thread.Id, archived: false), ["{ not json"]);

        var loaded = await new ThreadStore(_root).LoadThreadAsync(thread.Id);

        Assert.NotNull(loaded);
        Assert.Equal(thread.Id, loaded.Id);
        var turn = Assert.Single(loaded.Turns);
        var payload = Assert.IsType<UserMessagePayload>(turn.Input!.Payload);
        Assert.Equal("hello", payload.Text);
    }

    [Fact]
    public async Task SaveThread_PreservesMetadata()
    {
        var thread = CreateThread();
        thread.Metadata["customKey"] = "test-value";
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded.Metadata.TryGetValue("customKey", out var v));
        Assert.Equal("test-value", v);
    }

    [Fact]
    public async Task ContextUsageAnchor_RoundTripsAndFallsBackToTokenOnly()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);
        var anchor = new ContextUsageAnchor(
            Tokens: 1234,
            MessageCount: 2,
            PrefixFingerprint: "abc123",
            RequestFingerprint: "request-a",
            ContextUsageFingerprint: "context-a",
            BaseInstructionsTokenEstimate: 321);

        await _store.SaveContextUsageAnchorAsync(
            thread.Id,
            displayTokens: 5678,
            anchor: anchor,
            source: "provider_context");

        Assert.Equal(anchor, _store.LoadContextUsageAnchor(thread.Id));
        Assert.Equal(5678, _store.LoadContextUsageTokens(thread.Id));
        var anchoredSnapshot = _store.LoadContextUsageSnapshot(thread.Id);
        Assert.NotNull(anchoredSnapshot);
        Assert.Equal(5678, anchoredSnapshot!.Tokens);
        Assert.Equal("provider_context", anchoredSnapshot.Source);
        Assert.False(anchoredSnapshot.IsEstimate);

        await _store.SaveContextUsageTokensAsync(
            thread.Id,
            42,
            source: "compacted_estimate",
            isEstimate: true);

        Assert.Null(_store.LoadContextUsageAnchor(thread.Id));
        Assert.Equal(42, _store.LoadContextUsageTokens(thread.Id));
        var displaySnapshot = _store.LoadContextUsageSnapshot(thread.Id);
        Assert.NotNull(displaySnapshot);
        Assert.Equal(42, displaySnapshot!.Tokens);
        Assert.Equal("compacted_estimate", displaySnapshot.Source);
        Assert.True(displaySnapshot.IsEstimate);
    }

    [Fact]
    public async Task SaveThread_WritesCanonicalJsonlUnderThreadsActive()
    {
        var thread = CreateThread();

        await _store.SaveThreadAsync(thread);

        Assert.True(File.Exists(GetCanonicalPath(thread.Id, archived: false)));
    }

    [Fact]
    public async Task SaveThread_ArchiveAndUnarchive_MovesCanonicalJsonlBetweenDirectories()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);

        var activePath = GetCanonicalPath(thread.Id, archived: false);
        var archivedPath = GetCanonicalPath(thread.Id, archived: true);
        Assert.True(File.Exists(activePath));

        thread.Status = ThreadStatus.Archived;
        thread.LastActiveAt = thread.LastActiveAt.AddMinutes(1);
        await _store.SaveThreadAsync(thread);

        Assert.False(File.Exists(activePath));
        Assert.True(File.Exists(archivedPath));

        thread.Status = ThreadStatus.Active;
        thread.LastActiveAt = thread.LastActiveAt.AddMinutes(1);
        await _store.SaveThreadAsync(thread);

        Assert.True(File.Exists(activePath));
        Assert.False(File.Exists(archivedPath));
    }

    [Fact]
    public async Task SaveThread_UsesCachedSnapshot_ForIncrementalAppendOnRepeatedSaves()
    {
        var thread = CreateThread();
        thread.DisplayName = "First title";
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);
        var initialLineCount = File.ReadAllLines(path).Length;

        thread.DisplayName = "Updated title";
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Updated title", loaded.DisplayName);
        Assert.Equal(initialLineCount + 1, File.ReadAllLines(path).Length);
    }

    [Fact]
    public async Task SaveThread_WhenCompletedTurnOnlyAppendsNotice_WritesSingleItemRecord()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "remember this", "stored");
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);
        var initialLineCount = File.ReadAllLines(path).Length;

        thread.Turns[0].Items.Add(CreateMemoryNotice(thread.Turns[0], 3));
        await _store.SaveThreadAsync(thread);

        var lines = File.ReadAllLines(path);
        Assert.Equal(initialLineCount + 1, lines.Length);
        using var last = JsonDocument.Parse(lines[^1]);
        Assert.Equal("item_appended", last.RootElement.GetProperty("kind").GetString());
        Assert.Equal(
            "memoryConsolidated",
            last.RootElement
                .GetProperty("itemAppended")
                .GetProperty("item")
                .GetProperty("payload")
                .GetProperty("kind")
                .GetString());
    }

    [Fact]
    public async Task ThreadRolloutWrites_ForSameThread_AreSerializedAcrossThreadSaveAndCheckpointAppend()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "seed", "answer");
        await _store.SaveThreadAsync(thread);

        var noticeThread = CloneThreadSnapshotForTest(thread);
        noticeThread.Turns[0].Items.Add(CreateMemoryNotice(noticeThread.Turns[0], 3));

        await Task.WhenAll(
            Task.Run(() => _store.SaveThreadAsync(noticeThread)),
            Task.Run(() => _store.AppendCompactionCheckpointAsync(
                thread.Id,
                thread.Turns[0].Id,
                [new ChatMessage(ChatRole.Assistant, "compacted summary")],
                "manual",
                "partial",
                1000,
                100)));

        var loaded = await new ThreadStore(_root).LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Contains(
            loaded.Turns.Single().Items,
            item => item.Type == ItemType.SystemNotice
                && item.AsSystemNotice?.Kind == "memoryConsolidated");

        var rollout = await File.ReadAllTextAsync(GetCanonicalPath(thread.Id, archived: false));
        Assert.Contains("\"kind\":\"context_compacted\"", rollout);
        Assert.Contains("\"kind\":\"item_appended\"", rollout);
    }

    [Fact]
    public async Task LoadThenSave_ReusesLoadedSnapshot_ForSubsequentDiff()
    {
        var original = CreateThread();
        original.DisplayName = "Initial";
        await _store.SaveThreadAsync(original);
        var path = GetCanonicalPath(original.Id, archived: false);
        var initialLineCount = File.ReadAllLines(path).Length;

        var secondStore = new ThreadStore(_root);
        var loaded = await secondStore.LoadThreadAsync(original.Id);
        Assert.NotNull(loaded);

        loaded.DisplayName = "Renamed after load";
        await secondStore.SaveThreadAsync(loaded);

        var roundTrip = await secondStore.LoadThreadAsync(original.Id);
        Assert.NotNull(roundTrip);
        Assert.Equal("Renamed after load", roundTrip.DisplayName);
        Assert.Equal(initialLineCount + 1, File.ReadAllLines(path).Length);
    }

    [Fact]
    public async Task DeleteThread_ClearsCachedSnapshot_BeforeRecreatingSameId()
    {
        var thread = CreateThread();
        thread.DisplayName = "Before delete";
        await _store.SaveThreadAsync(thread);

        _store.DeleteThread(thread.Id);

        var recreated = CreateThread(thread.Id);
        recreated.DisplayName = "After recreate";
        recreated.LastActiveAt = recreated.LastActiveAt.AddMinutes(1);
        await _store.SaveThreadAsync(recreated);
        var path = GetCanonicalPath(thread.Id, archived: false);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal("After recreate", loaded.DisplayName);
        Assert.Equal(3, File.ReadAllLines(path).Length);
    }

    [Fact]
    public async Task ContextUsageTokens_SaveLoadAndUpdate_RoundTrips()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);

        Assert.Null(_store.LoadContextUsageTokens(thread.Id));

        await _store.SaveContextUsageTokensAsync(thread.Id, 42_000);
        Assert.Equal(42_000, _store.LoadContextUsageTokens(thread.Id));

        await _store.SaveContextUsageTokensAsync(thread.Id, 17_500);
        Assert.Equal(17_500, _store.LoadContextUsageTokens(thread.Id));
    }

    [Fact]
    public async Task ContextUsageTokens_DeleteThread_RemovesPersistedUsage()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);
        await _store.SaveContextUsageTokensAsync(thread.Id, 42_000);

        _store.DeleteThread(thread.Id);

        Assert.Null(_store.LoadContextUsageTokens(thread.Id));
    }

    [Fact]
    public async Task SaveThread_MultipleSavesWithMutableObject_RoundTripsFinalState()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);

        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = thread.CreatedAt.AddSeconds(1)
        };
        var userItem = new SessionItem
        {
            Id = "item_001",
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = turn.StartedAt,
            CompletedAt = turn.StartedAt,
            Payload = new UserMessagePayload { Text = "hello" }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
        thread.LastActiveAt = turn.StartedAt;
        await _store.SaveThreadAsync(thread);

        var toolCall = new SessionItem
        {
            Id = "item_002",
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Started,
            CreatedAt = turn.StartedAt.AddSeconds(1),
            Payload = new ToolCallPayload
            {
                ToolName = "ReadFile",
                ProviderFlatName = "ReadFile",
                CallId = "call_001"
            }
        };
        turn.Items.Add(toolCall);
        await _store.SaveThreadAsync(thread);

        toolCall.Status = ItemStatus.Completed;
        toolCall.CompletedAt = turn.StartedAt.AddSeconds(2);
        turn.Items.Add(new SessionItem
        {
            Id = "item_003",
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = turn.StartedAt.AddSeconds(2),
            CompletedAt = turn.StartedAt.AddSeconds(2),
            Payload = new ToolResultPayload
            {
                ToolName = "ReadFile",
                ProviderFlatName = "ReadFile",
                CallId = "call_001",
                Result = "world",
                Success = true
            }
        });
        await _store.SaveThreadAsync(thread);

        turn.Status = TurnStatus.Completed;
        turn.CompletedAt = turn.StartedAt.AddSeconds(3);
        thread.LastActiveAt = turn.CompletedAt.Value;
        await _store.SaveThreadAsync(thread);

        var rolloutPath = GetCanonicalPath(thread.Id, archived: false);
        var records = File.ReadAllLines(rolloutPath)
            .Select(line => JsonDocument.Parse(line))
            .ToList();
        try
        {
            Assert.Single(records, record =>
                record.RootElement.GetProperty("kind").GetString() == "turn_started"
                && record.RootElement.GetProperty("turnStarted").GetProperty("turn").GetProperty("id").GetString() == turn.Id);
            Assert.Single(records, record =>
                record.RootElement.GetProperty("kind").GetString() == "turn_completed"
                && record.RootElement.GetProperty("turnCompleted").GetProperty("turnId").GetString() == turn.Id);
            Assert.Equal(4, records.Count(record =>
                record.RootElement.GetProperty("kind").GetString() == "item_appended"
                && record.RootElement.GetProperty("itemAppended").GetProperty("turnId").GetString() == turn.Id));
        }
        finally
        {
            foreach (var record in records)
                record.Dispose();
        }

        var loaded = await new ThreadStore(_root).LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        var loadedTurn = Assert.Single(loaded.Turns);
        Assert.Equal(TurnStatus.Completed, loadedTurn.Status);
        Assert.Equal(3, loadedTurn.Items.Count);
        Assert.Equal("hello", loadedTurn.Input?.AsUserMessage?.Text);
        Assert.Equal(ItemStatus.Completed, loadedTurn.Items[1].Status);
        Assert.Equal("world", loadedTurn.Items[2].AsToolResult?.Result);
    }

    [Fact]
    public async Task RollbackThreadAsync_AppendsRollbackRecord_AndColdReloadRemovesTailTurns()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "first", "one");
        AddTurnWithMessages(thread, "second", "two");
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);
        var initialLineCount = File.ReadAllLines(path).Length;

        thread.Turns.RemoveAt(thread.Turns.Count - 1);
        thread.LastActiveAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await _store.RollbackThreadAsync(thread, 1);

        var lines = File.ReadAllLines(path);
        Assert.Equal(initialLineCount + 1, lines.Length);
        Assert.Contains("thread_rolled_back", lines[^1]);

        var secondStore = new ThreadStore(_root);
        var loaded = await secondStore.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        var remaining = Assert.Single(loaded.Turns);
        Assert.Equal("first", remaining.Input?.AsUserMessage?.Text);
        Assert.Equal("one", remaining.Items[1].AsAgentMessage?.Text);
        Assert.Equal(2, loaded.TurnSequenceHighWatermark);
        Assert.Equal(3, SessionIdGenerator.ReserveNextTurnSequence(loaded));
    }

    [Fact]
    public async Task QueuedInputs_ArePersistedAppendOnly_AndColdReloadPreservesFifo()
    {
        var thread = CreateThread();
        var first = CreateQueuedInput(thread.Id, "first");
        var second = CreateQueuedInput(thread.Id, "second");
        thread.QueuedInputs.Add(first);
        thread.QueuedInputs.Add(second);
        await _store.SaveThreadAsync(thread);

        var secondStore = new ThreadStore(_root);
        var loaded = await secondStore.LoadThreadAsync(thread.Id);

        Assert.NotNull(loaded);
        Assert.Equal(["first", "second"], loaded.QueuedInputs.Select(q => q.DisplayText).ToArray());

        thread.QueuedInputs.RemoveAt(0);
        thread.LastActiveAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await _store.SaveThreadAsync(thread);

        var thirdStore = new ThreadStore(_root);
        var reloaded = await thirdStore.LoadThreadAsync(thread.Id);

        Assert.NotNull(reloaded);
        var remaining = Assert.Single(reloaded.QueuedInputs);
        Assert.Equal(second.Id, remaining.Id);
        Assert.Equal("second", remaining.DisplayText);
    }

    [Fact]
    public async Task QueuedInputStatusUpdate_IsPersistedAppendOnly()
    {
        var thread = CreateThread();
        var queued = CreateQueuedInput(thread.Id, "guide me");
        thread.QueuedInputs.Add(queued);
        await _store.SaveThreadAsync(thread);

        thread.QueuedInputs[0] = queued with
        {
            Status = "guidancePending",
            ReadyAfterTurnId = "turn_active"
        };
        thread.LastActiveAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await _store.SaveThreadAsync(thread);

        var secondStore = new ThreadStore(_root);
        var loaded = await secondStore.LoadThreadAsync(thread.Id);

        Assert.NotNull(loaded);
        var reloaded = Assert.Single(loaded.QueuedInputs);
        Assert.Equal("guidancePending", reloaded.Status);
        Assert.Equal("turn_active", reloaded.ReadyAfterTurnId);
    }

    [Fact]
    public async Task QueuedInputTriggerMetadata_IsPersisted()
    {
        var thread = CreateThread();
        var queued = CreateQueuedInput(thread.Id, "app work") with
        {
            TriggerKind = "team",
            TriggerLabel = "Review round",
            TriggerRefId = "round-7"
        };
        thread.QueuedInputs.Add(queued);
        await _store.SaveThreadAsync(thread);

        var secondStore = new ThreadStore(_root);
        var loaded = await secondStore.LoadThreadAsync(thread.Id);

        Assert.NotNull(loaded);
        var reloaded = Assert.Single(loaded.QueuedInputs);
        Assert.Equal("team", reloaded.TriggerKind);
        Assert.Equal("Review round", reloaded.TriggerLabel);
        Assert.Equal("round-7", reloaded.TriggerRefId);
    }

    [Fact]
    public async Task QueuedInputReorder_IsPersistedAppendOnly()
    {
        var thread = CreateThread();
        var first = CreateQueuedInput(thread.Id, "first");
        var second = CreateQueuedInput(thread.Id, "second");
        var third = CreateQueuedInput(thread.Id, "third");
        thread.QueuedInputs.AddRange([first, second, third]);
        await _store.SaveThreadAsync(thread);

        thread.QueuedInputs = [third, first, second];
        thread.LastActiveAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await _store.SaveThreadAsync(thread);

        var lines = await File.ReadAllLinesAsync(GetCanonicalPath(thread.Id, archived: false));
        Assert.Contains(lines, line => line.Contains("queued_input_reordered", StringComparison.Ordinal));

        var secondStore = new ThreadStore(_root);
        var loaded = await secondStore.LoadThreadAsync(thread.Id);

        Assert.NotNull(loaded);
        Assert.Equal(["third", "first", "second"], loaded.QueuedInputs.Select(q => q.DisplayText).ToArray());
    }

    [Fact]
    public async Task GuidanceUserItem_DoesNotReplaceOriginalTurnInputOnColdReload()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "initial", "partial", TurnStatus.Running);
        await _store.SaveThreadAsync(thread);
        var turn = thread.Turns[0];

        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "ReadFile",
                ProviderFlatName = "ReadFile",
                CallId = "call_guidance_order",
                Arguments = new JsonObject()
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = "call_guidance_order",
                Result = "tool result",
                Success = true
            }
        });

        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(5),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = "guidance",
                DeliveryMode = "guidance"
            }
        });
        await _store.SaveThreadAsync(thread);

        var secondStore = new ThreadStore(_root);
        var loaded = await secondStore.LoadThreadAsync(thread.Id);

        Assert.NotNull(loaded);
        var loadedTurn = Assert.Single(loaded.Turns);
        Assert.Equal("initial", loadedTurn.Input?.AsUserMessage?.Text);
        Assert.Equal(
            [ItemType.UserMessage, ItemType.AgentMessage, ItemType.ToolCall, ItemType.ToolResult, ItemType.UserMessage],
            loadedTurn.Items.Select(i => i.Type).ToArray());
        var guidance = Assert.IsType<UserMessagePayload>(loadedTurn.Items[^1].Payload);
        Assert.Equal("guidance", guidance.Text);
        Assert.Equal("guidance", guidance.DeliveryMode);
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_IncludesPairedToolCallAndResult()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "before tool", TurnStatus.Failed);
        var turn = thread.Turns[0];
        turn.Error = "boom";

        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "ReadFile",
                ProviderFlatName = "ReadFile",
                CallId = "call-1",
                Arguments = new JsonObject { ["path"] = "a.txt" }
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = "call-1",
                Result = "tool result",
                Success = true
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(5),
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new AgentMessagePayload { Text = "partial answer" }
        });
        await _store.SaveThreadAsync(thread);

        var agent = CreateAgent();
        var session = await _store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.Equal(
            [
                "user:hello",
                "assistant:before toolfunction_call:ReadFile:call-1",
                "tool:function_result:call-1:tool result",
                "assistant:partial answer"
            ],
            FormatHistoryWithContents(session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_RestoresToolImageAsStructuredContent()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "inspect", "before tool", TurnStatus.Completed);
        var turn = thread.Turns[0];
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "Screenshot",
                ProviderFlatName = "Screenshot",
                CallId = "call-image",
                Arguments = new JsonObject()
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = "call-image",
                Result = "fallback",
                Success = true,
                ContentItems =
                [
                    new PluginFunctionContentItem { Type = "text", Text = "captured" },
                    new PluginFunctionContentItem
                    {
                        Type = "image",
                        MediaType = "image/png",
                        DataBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 })
                    }
                ]
            }
        });
        await _store.SaveThreadAsync(thread);

        var agent = CreateAgent();
        var session = await _store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.True(session.TryGetInMemoryChatHistory(
            out var history,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));
        var functionResult = Assert.Single(
            history.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        var modelContents = functionResult.Result switch
        {
            IList<AIContent> contents => contents.ToList(),
            JsonElement json => json.Deserialize<List<AIContent>>(SessionPersistenceJsonOptions.Default),
            _ => null
        };
        Assert.NotNull(modelContents);
        Assert.IsType<TextContent>(modelContents![0]);
        Assert.IsType<DataContent>(modelContents[1]);
        Assert.DoesNotContain(modelContents, content => content is TextContent text
            && text.Text.Contains("data:image/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_ReplaysHostedImageGenerationAsAssistantContent()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "before image", TurnStatus.Completed);
        var turn = thread.Turns[0];
        var imageBytes = "png-bytes"u8.ToArray();
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = HostedImageGenerationContent.ToolName,
                ProviderFlatName = HostedImageGenerationContent.ToolName,
                CallId = "ig_123",
                Arguments = new JsonObject()
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = "ig_123",
                Result = "A red square",
                Success = true,
                ContentItems =
                [
                    new DotCraft.Plugins.PluginFunctionContentItem
                    {
                        Type = "image",
                        MediaType = "image/png",
                        DataBase64 = Convert.ToBase64String(imageBytes)
                    }
                ]
            }
        });
        await _store.SaveThreadAsync(thread);

        var agent = CreateAgent();
        var session = await _store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.True(session.TryGetInMemoryChatHistory(
            out var chatHistory,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));
        Assert.DoesNotContain(
            chatHistory.SelectMany(message => message.Contents),
            content => content is FunctionCallContent { Name: "image_generation" } or FunctionResultContent { CallId: "ig_123" });
        var imageContent = chatHistory
            .SelectMany(message => message.Contents)
            .OfType<HostedImageGenerationContent>()
            .Single();
        Assert.Equal("ig_123", imageContent.Id);
        Assert.Equal("A red square", imageContent.RevisedPrompt);
        Assert.Equal(imageBytes, imageContent.ImageBytes);
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_ReplaysImageGenerationItemAsAssistantContent()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "before image", TurnStatus.Completed);
        var turn = thread.Turns[0];
        var imageBytes = "png-bytes"u8.ToArray();
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ImageGeneration,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ImageGenerationPayload
            {
                CallId = "ig_new",
                Status = "completed",
                RevisedPrompt = "A blue square",
                Result = Convert.ToBase64String(imageBytes),
                MediaType = "image/png"
            }
        });
        await _store.SaveThreadAsync(thread);

        var agent = CreateAgent();
        var session = await _store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.True(session.TryGetInMemoryChatHistory(
            out var chatHistory,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));
        Assert.DoesNotContain(
            chatHistory.SelectMany(message => message.Contents),
            content => content is FunctionCallContent { Name: "image_generation" } or FunctionResultContent { CallId: "ig_new" });
        var imageContent = chatHistory
            .SelectMany(message => message.Contents)
            .OfType<HostedImageGenerationContent>()
            .Single();
        Assert.Equal("ig_new", imageContent.Id);
        Assert.Equal("A blue square", imageContent.RevisedPrompt);
        Assert.Equal(imageBytes, imageContent.ImageBytes);
    }

    [Fact]
    public async Task PendingSubAgentMailbox_IsOrderedByCreatedAtThenId()
    {
        var root = CreateThread();
        await _store.SaveThreadAsync(root);
        var createdAt = DateTimeOffset.UtcNow;
        foreach (var entry in new[]
        {
            new SubAgentMailboxEntry
            {
                Id = "mail-z",
                RootThreadId = root.Id,
                TargetAgentPath = "/root/worker",
                Message = "third",
                CreatedAt = createdAt
            },
            new SubAgentMailboxEntry
            {
                Id = "mail-b",
                RootThreadId = root.Id,
                TargetAgentPath = "/root/worker",
                Message = "first",
                CreatedAt = createdAt.AddSeconds(-1)
            },
            new SubAgentMailboxEntry
            {
                Id = "mail-a",
                RootThreadId = root.Id,
                TargetAgentPath = "/root/worker",
                Message = "second",
                CreatedAt = createdAt
            }
        })
        {
            await _store.AddSubAgentMailboxEntryAsync(entry);
        }

        var pending = await _store.ListPendingSubAgentMailboxAsync(root.Id, "/root/worker");

        Assert.Equal(["mail-b", "mail-a", "mail-z"], pending.Select(entry => entry.Id));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_SubAgentMailboxUserItem_ReplaysAsUserMessage()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "before mailbox", TurnStatus.Completed);
        var turn = thread.Turns[0];
        const string notification = """
            Message Type: FINAL_ANSWER
            Task name: /root
            Sender: /root/inspect
            Payload:
            {"agentPath":"/root/inspect","status":{"completed":"done"}}
            """;
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = "SubAgent message from /root/inspect",
                DeliveryMode = SubAgentMailboxDelivery.DeliveryMode,
                MaterializedInputParts = [new SessionWireInputPart { Type = "text", Text = notification }]
            }
        });
        await _store.SaveThreadAsync(thread);

        var agent = CreateAgent();
        var session = await _store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.Equal(
            [
                "user:hello",
                "assistant:before mailbox",
                $"user:{notification}"
            ],
            FormatHistoryWithContents(session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_EmptyToolArgumentsBecomeEmptyDictionary()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "before tool", TurnStatus.Failed);
        var turn = thread.Turns[0];
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "GetStatus",
                ProviderFlatName = "GetStatus",
                CallId = "call-1",
                Arguments = new JsonObject()
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = "call-1",
                Result = "ok",
                Success = true
            }
        });
        await _store.SaveThreadAsync(thread);

        var agent = CreateAgent();
        var session = await _store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.True(session.TryGetInMemoryChatHistory(
            out var chatHistory,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));
        var call = chatHistory
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Single();
        var arguments = Assert.IsAssignableFrom<IDictionary<string, object?>>(call.Arguments);
        Assert.Empty(arguments);
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_MergesReasoningTextAndToolCallIntoOneAssistantMessage()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "I will inspect.");
        var turn = thread.Turns[0];
        turn.Items.Insert(1, new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ReasoningContent,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ReasoningContentPayload { Text = "need file" }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "ReadFile",
                ProviderFlatName = "ReadFile",
                CallId = "call-1",
                Arguments = new JsonObject { ["path"] = "a.txt" }
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(5),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = "call-1",
                Result = "contents",
                Success = true
            }
        });
        await _store.SaveThreadAsync(thread);

        var agent = CreateAgent();
        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.Equal(
            [
                "user:hello",
                "assistant:reasoning:need fileI will inspect.function_call:ReadFile:call-1",
                "tool:function_result:call-1:contents"
            ],
            FormatHistoryWithContents(session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_MergesMultipleToolCallsAndKeepsToolResultsPaired()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "Checking tools.");
        var turn = thread.Turns[0];
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "ReadFile",
                ProviderFlatName = "ReadFile",
                CallId = "call-1",
                Arguments = new JsonObject { ["path"] = "a.txt" }
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "ListFiles",
                ProviderFlatName = "ListFiles",
                CallId = "call-2",
                Arguments = new JsonObject()
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(5),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = "call-2",
                Result = "files",
                Success = true
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(6),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = "call-1",
                Result = "contents",
                Success = true
            }
        });
        await _store.SaveThreadAsync(thread);

        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);

        Assert.Equal(
            [
                "user:hello",
                "assistant:Checking tools.function_call:ReadFile:call-1function_call:ListFiles:call-2",
                "tool:function_result:call-2:files",
                "tool:function_result:call-1:contents"
            ],
            FormatHistoryWithContents(session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_SkipsDanglingToolPairs()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "before", TurnStatus.Failed);
        var turn = thread.Turns[0];
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "ReadFile",
                ProviderFlatName = "ReadFile",
                CallId = "missing",
                Arguments = new JsonObject()
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = string.Empty,
                Result = "ignored",
                Success = true
            }
        });
        await _store.SaveThreadAsync(thread);

        var agent = CreateAgent();
        var session = await _store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.Equal(["user:hello", "assistant:before"], FormatHistoryWithContents(session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_RebuildsMcpLifecycleItemWithoutLeakingClientData()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "calling MCP");
        var turn = thread.Turns[0];
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.McpToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new McpToolCallPayload
            {
                Namespace = "mcp__review",
                ToolName = "lookup",
                ProviderFlatName = "mcp__review__lookup",
                ToolDefinitionId = "Mcp:review:lookup_raw",
                RuntimeBindingId = "mcp:review:lookup_raw:7",
                BindingRevision = 7,
                SnapshotRevision = 11,
                McpGeneration = 7,
                Source = new ToolSourceProvenancePayload
                {
                    Kind = "Mcp",
                    SourceId = "review",
                    SourceToolId = "lookup_raw",
                    Origin = "workspace"
                },
                Presentation = new ToolPresentationPayload
                {
                    PresentationId = "core.review",
                    Options = new JsonObject { ["variant"] = "compact" }
                },
                Server = "review",
                SourceToolId = "lookup_raw",
                CallId = "mcp-call-1",
                Status = "completed",
                Success = true,
                McpAppResourceUri = "ui://review/lookup",
                Arguments = new JsonObject { ["id"] = 7 },
                ModelContentItems = [new PluginFunctionContentItem { Type = "text", Text = "safe result" }],
                StructuredContent = new JsonObject { ["secret"] = "client-only" },
                Meta = new JsonObject { ["token"] = "host-only" }
            }
        });
        await _store.SaveThreadAsync(thread);

        var persisted = await _store.LoadThreadAsync(thread.Id);
        var persistedPayload = Assert.IsType<McpToolCallPayload>(
            Assert.Single(
                Assert.Single(persisted!.Turns).Items,
                item => item.Type == ItemType.McpToolCall).Payload);
        Assert.Equal("Mcp:review:lookup_raw", persistedPayload.ToolDefinitionId);
        Assert.Equal("mcp:review:lookup_raw:7", persistedPayload.RuntimeBindingId);
        Assert.Equal(7, persistedPayload.BindingRevision);
        Assert.Equal(11, persistedPayload.SnapshotRevision);
        Assert.Equal(7, persistedPayload.McpGeneration);
        Assert.Equal("ui://review/lookup", persistedPayload.McpAppResourceUri);
        Assert.Equal("workspace", persistedPayload.Source!.Origin);
        Assert.Equal("core.review", persistedPayload.Presentation!.PresentationId);

        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);
        var formatted = FormatHistoryWithContents(session);

        Assert.Contains("assistant:calling MCPfunction_call:lookup:mcp-call-1", formatted);
        Assert.Contains("tool:function_result:mcp-call-1:safe result", formatted);
        Assert.DoesNotContain(formatted, value => value.Contains("client-only", StringComparison.Ordinal));
        Assert.DoesNotContain(formatted, value => value.Contains("host-only", StringComparison.Ordinal));

        Assert.True(session.TryGetInMemoryChatHistory(
            out var history,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));
        var call = Assert.Single(history.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
        Assert.Equal("mcp__review", call.AdditionalProperties!["openai.responses.function_call.namespace"]);
        Assert.Equal("mcp__review__lookup", call.AdditionalProperties["dotcraft.tool.provider_flat_name"]);
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_SkipsInProgressDynamicLifecycleItem()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "calling dynamic tool");
        var turn = thread.Turns[0];
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.DynamicToolCall,
            Status = ItemStatus.Streaming,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new DynamicToolCallPayload
            {
                Namespace = "board",
                ToolName = "lookup",
                ProviderFlatName = "board__lookup",
                CallId = "dynamic-call-1",
                Status = "inProgress",
                Arguments = new JsonObject { ["id"] = 7 }
            }
        });
        await _store.SaveThreadAsync(thread);

        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);

        Assert.Equal(
            ["user:hello", "assistant:calling dynamic tool"],
            FormatHistoryWithContents(session));
    }

    // -------------------------------------------------------------------------
    // Thread discovery (LoadIndexAsync reads persisted SQLite metadata)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadIndex_EmptyDirectory_ReturnsEmptyList()
    {
        var index = await _store.LoadIndexAsync();
        Assert.Empty(index);
    }

    [Fact]
    public async Task LoadIndex_AfterSavingThreads_ReturnsSummaries()
    {
        var t1 = CreateThread();
        var t2 = CreateThread();
        await _store.SaveThreadAsync(t1);
        await _store.SaveThreadAsync(t2);

        var index = await _store.LoadIndexAsync();
        Assert.Equal(2, index.Count);
        Assert.Contains(index, s => s.Id == t1.Id);
        Assert.Contains(index, s => s.Id == t2.Id);
    }

    [Fact]
    public async Task LoadIndex_PreservesThreadMetadata()
    {
        var thread = CreateThread();
        thread.Metadata[ThreadVisibility.InternalMetadataKey] = "background-helper";
        await _store.SaveThreadAsync(thread);

        var index = await _store.LoadIndexAsync();
        var summary = Assert.Single(index);

        Assert.True(ThreadVisibility.IsInternal(summary));
        Assert.Equal("background-helper", summary.Metadata[ThreadVisibility.InternalMetadataKey]);
    }

    [Fact]
    public async Task LoadIndex_AfterDeletingThread_ExcludesDeleted()
    {
        var t1 = CreateThread();
        var t2 = CreateThread();
        await _store.SaveThreadAsync(t1);
        await _store.SaveThreadAsync(t2);

        _store.DeleteThread(t1.Id);

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(t2.Id, index[0].Id);
    }

    [Fact]
    public async Task LoadIndex_WhenRolloutFileIsMissing_ExcludesStaleMetadataRow()
    {
        var stale = CreateThread();
        var current = CreateThread();
        await _store.SaveThreadAsync(stale);
        await _store.SaveThreadAsync(current);
        File.Delete(GetCanonicalPath(stale.Id, archived: false));

        var index = await new ThreadStore(_root).LoadIndexAsync();

        Assert.DoesNotContain(index, s => s.Id == stale.Id);
        Assert.Contains(index, s => s.Id == current.Id);
    }

    [Fact]
    public async Task LoadIndex_WhenProjectionRowIsMissing_RepairsFromRolloutImmediately()
    {
        var thread = CreateThread();
        thread.DisplayName = "Recovered thread";
        AddTurnWithMessages(thread, "first request", "answer");
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);

        using (var connection = OpenStateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM threads WHERE thread_id = $thread_id";
            command.Parameters.AddWithValue("$thread_id", thread.Id);
            command.ExecuteNonQuery();
        }

        var summary = Assert.Single(await _store.LoadIndexAsync());
        Assert.Equal(thread.Id, summary.Id);
        Assert.Equal("Recovered thread", summary.DisplayName);
        Assert.Equal(1, summary.TurnCount);

        using var repairedConnection = OpenStateConnection();
        using var repairedCommand = repairedConnection.CreateCommand();
        repairedCommand.CommandText = "SELECT projected_rollout_offset FROM threads WHERE thread_id = $thread_id";
        repairedCommand.Parameters.AddWithValue("$thread_id", thread.Id);
        Assert.Equal(new FileInfo(path).Length, (long)repairedCommand.ExecuteScalar()!);
    }

    [Fact]
    public async Task LoadIndex_WhenProjectionOffsetIsStale_RebuildsLatestMetadata()
    {
        var thread = CreateThread();
        thread.DisplayName = "Latest name";
        AddTurnWithMessages(thread, "first request", "answer");
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);

        using (var connection = OpenStateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE threads
                SET display_name = 'stale name', turn_count = 0, projected_rollout_offset = 0
                WHERE thread_id = $thread_id
                """;
            command.Parameters.AddWithValue("$thread_id", thread.Id);
            command.ExecuteNonQuery();
        }

        var summary = Assert.Single(await _store.LoadIndexAsync());
        Assert.Equal("Latest name", summary.DisplayName);
        Assert.Equal(1, summary.TurnCount);

        using var repairedConnection = OpenStateConnection();
        using var repairedCommand = repairedConnection.CreateCommand();
        repairedCommand.CommandText = "SELECT projected_rollout_offset FROM threads WHERE thread_id = $thread_id";
        repairedCommand.Parameters.AddWithValue("$thread_id", thread.Id);
        Assert.Equal(new FileInfo(path).Length, (long)repairedCommand.ExecuteScalar()!);
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_WhenSessionRowMissing_RebuildsChatHistoryFromRollout()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "world");
        await _store.SaveThreadAsync(thread);

        var store = new ThreadStore(_root);
        var agent = CreateAgent();

        var session = await store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.Equal(
            ["user:hello", "assistant:world"],
            await ExtractHistoryAsync(agent, session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_WhenOnlyCancelledTurnExists_ReplaysCompletedTextItems()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "Partial request", "Partial answer", TurnStatus.Cancelled);
        await _store.SaveThreadAsync(thread);

        var store = new ThreadStore(_root);
        var agent = CreateAgent();

        var session = await store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.Equal(
            ["user:Partial request", "assistant:Partial answer"],
            await ExtractHistoryAsync(agent, session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_PrefersMaterializedInputParts_WhenRebuildingHistory()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "display only", "assistant reply");
        var userPayload = Assert.IsType<UserMessagePayload>(thread.Turns[0].Input?.Payload);
        thread.Turns[0].Input!.Payload = userPayload with
        {
            NativeInputParts = [new SessionWireInputPart { Type = "text", Text = "native text" }],
            MaterializedInputParts = [new SessionWireInputPart { Type = "text", Text = "materialized text" }]
        };
        await _store.SaveThreadAsync(thread);

        var store = new ThreadStore(_root);
        var agent = CreateAgent();

        var session = await store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.Equal(
            ["user:materialized text", "assistant:assistant reply"],
            await ExtractHistoryAsync(agent, session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_WhenCheckpointExists_RebuildsFromCheckpointAndTail()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "old seed", "old answer");
        AddTurnWithMessages(thread, "recent request", "recent answer");
        await _store.SaveThreadAsync(thread);
        await _store.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[0].Id,
            [new ChatMessage(ChatRole.Assistant, "compacted summary")],
            "manual",
            "partial",
            1000,
            100);

        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);

        Assert.Equal(
            ["assistant:compacted summary", "user:recent request", "assistant:recent answer"],
            await ExtractHistoryAsync(CreateAgent(), session));
    }

    [Fact]
    public async Task ReplayModelHistory_WithLargeCoveredPrefix_ReadsOnlyCheckpointTail()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "old request", new string('x', 512 * 1024));
        AddTurnWithMessages(thread, "recent request", "recent projected answer");
        await _store.SaveThreadAsync(thread);
        await _store.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[0].Id,
            [new ChatMessage(ChatRole.Assistant, "compacted summary")],
            "manual",
            "partial",
            1000,
            100);
        await _store.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.User, "recent exact request"), new ChatMessage(ChatRole.Assistant, "recent exact answer")],
            thread.Turns[1].Id);

        var replay = await new ThreadRolloutStore(_root).ReplayModelHistoryAsync(thread.Id, thread);
        var pathLength = new FileInfo(GetCanonicalPath(thread.Id, archived: false)).Length;

        Assert.Equal(
            ["compacted summary", "recent exact request", "recent exact answer"],
            replay.Messages.Select(static message => message.Text));
        Assert.True(replay.BytesRead < pathLength / 2, $"Expected bounded tail scan, read {replay.BytesRead} of {pathLength} bytes.");
        Assert.InRange(replay.RecordsDecoded, 2, 3);
    }

    [Fact]
    public async Task ReplayModelHistory_InvalidBatchFallsBackWholeTurnAndPreservesOtherExactTurns()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "projected first request", "projected first answer");
        AddTurnWithMessages(thread, "projected second request", "projected second answer");
        await _store.SaveThreadAsync(thread);
        await _store.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.User, "exact first request"), new ChatMessage(ChatRole.Assistant, "exact first answer")],
            thread.Turns[0].Id);
        await _store.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.User, "exact second request"), new ChatMessage(ChatRole.Assistant, "exact second answer")],
            thread.Turns[1].Id);

        var invalidBatch = new
        {
            kind = "model_history_messages_appended",
            timestamp = DateTimeOffset.UtcNow,
            modelHistoryMessagesAppended = new
            {
                threadId = thread.Id,
                turnId = thread.Turns[0].Id,
                messages = new[]
                {
                    new { schemaVersion = 99, turnId = thread.Turns[0].Id, role = "assistant", contents = Array.Empty<object>() }
                }
            }
        };
        var path = GetCanonicalPath(thread.Id, archived: false);
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(invalidBatch, SessionJsonOptions.Default) + Environment.NewLine);

        var replay = await new RolloutReplayer().ReplayModelHistoryAsync(path, thread.Turns);

        Assert.Equal(
            ["projected first request", "projected first answer", "exact second request", "exact second answer"],
            replay.Messages.Select(static message => message.Text));
        Assert.Equal(1, replay.RejectedRecords);
        Assert.Contains(thread.Turns[0].Id, Assert.IsAssignableFrom<IReadOnlySet<string>>(replay.FallbackTurnIds));
        Assert.DoesNotContain(thread.Turns[1].Id, replay.FallbackTurnIds!);
        Assert.Contains(replay.Warnings!, warning =>
            warning.Code == "invalid_model_batch" && warning.TurnId == thread.Turns[0].Id);
    }

    [Fact]
    public async Task ReplayModelHistory_ConflictingToolIdentityFallsBackWholeTurn()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "projected first request", "projected first answer");
        AddTurnWithMessages(thread, "projected second request", "projected second answer");
        await _store.SaveThreadAsync(thread);
        await _store.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.User, "exact first request")],
            thread.Turns[0].Id);
        await _store.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.User, "exact second request")],
            thread.Turns[1].Id);

        var conflictingBatch = new
        {
            kind = "model_history_messages_appended",
            timestamp = DateTimeOffset.UtcNow,
            modelHistoryMessagesAppended = new
            {
                threadId = thread.Id,
                turnId = thread.Turns[0].Id,
                messages = new[]
                {
                    new
                    {
                        schemaVersion = 1,
                        turnId = thread.Turns[0].Id,
                        role = "assistant",
                        contents = new[]
                        {
                            new
                            {
                                kind = "function_call",
                                payload = new
                                {
                                    callId = "call_conflict",
                                    name = "read_file",
                                    arguments = new { path = "sample.txt" },
                                    informationalOnly = false,
                                    @namespace = "tools",
                                    providerFlatName = (string?)null,
                                    additionalProperties = new Dictionary<string, object?>
                                    {
                                        ["openai.responses.function_call.namespace"] = "different"
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var path = GetCanonicalPath(thread.Id, archived: false);
        await File.AppendAllTextAsync(
            path,
            JsonSerializer.Serialize(conflictingBatch, SessionJsonOptions.Default) + Environment.NewLine);

        var replay = await new RolloutReplayer().ReplayModelHistoryAsync(path, thread.Turns);

        Assert.Equal(
            ["projected first request", "projected first answer", "exact second request"],
            replay.Messages.Select(static message => message.Text));
        Assert.Equal(1, replay.RejectedRecords);
        Assert.Contains(thread.Turns[0].Id, Assert.IsAssignableFrom<IReadOnlySet<string>>(replay.FallbackTurnIds));
        Assert.Contains(replay.Warnings!, warning =>
            warning.Code == "invalid_model_batch" && warning.TurnId == thread.Turns[0].Id);
    }

    [Fact]
    public async Task ReplayModelHistory_CrossThreadBatchFallsBackToCanonicalTurn()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "projected request", "projected answer");
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);
        var foreignBatch = new
        {
            kind = "model_history_messages_appended",
            timestamp = DateTimeOffset.UtcNow,
            modelHistoryMessagesAppended = new
            {
                threadId = "different-thread",
                turnId = thread.Turns[0].Id,
                messages = new[]
                {
                    new
                    {
                        schemaVersion = 1,
                        turnId = thread.Turns[0].Id,
                        role = "assistant",
                        contents = new[]
                        {
                            new { kind = "text", payload = new { text = "foreign answer" } }
                        }
                    }
                }
            }
        };
        await File.AppendAllTextAsync(
            path,
            JsonSerializer.Serialize(foreignBatch, SessionJsonOptions.Default) + Environment.NewLine);

        var replay = await new RolloutReplayer().ReplayModelHistoryAsync(
            path,
            thread.Turns,
            expectedThreadId: thread.Id);

        Assert.Equal(["projected request", "projected answer"], replay.Messages.Select(static message => message.Text));
        Assert.Contains(replay.Warnings!, warning =>
            warning.Code == "cross_thread_record" && warning.TurnId == thread.Turns[0].Id);
        Assert.Contains(thread.Turns[0].Id, Assert.IsAssignableFrom<IReadOnlySet<string>>(replay.FallbackTurnIds));
    }

    [Fact]
    public async Task ReplayModelHistory_MalformedOrdinaryLineReportsWarningAndContinues()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "projected request", "projected answer");
        await _store.SaveThreadAsync(thread);
        await _store.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.User, "exact request"), new ChatMessage(ChatRole.Assistant, "exact answer")],
            thread.Turns[0].Id);
        var path = GetCanonicalPath(thread.Id, archived: false);
        await File.AppendAllTextAsync(path, "{ invalid json" + Environment.NewLine);

        var replay = await new RolloutReplayer().ReplayModelHistoryAsync(path, thread.Turns);

        Assert.Equal(["exact request", "exact answer"], replay.Messages.Select(static message => message.Text));
        Assert.Equal(1, replay.RejectedRecords);
        Assert.Contains(replay.Warnings!, warning => warning.Code == "malformed_json");
    }

    [Fact]
    public async Task ReplayModelHistory_NullMessagesFallsBackWholeTurnAndContinues()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "projected request", "projected answer");
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);
        var invalidBatch = new
        {
            kind = "model_history_messages_appended",
            timestamp = DateTimeOffset.UtcNow,
            modelHistoryMessagesAppended = new
            {
                threadId = thread.Id,
                turnId = thread.Turns[0].Id,
                messages = (object?)null
            }
        };
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(invalidBatch, SessionJsonOptions.Default) + Environment.NewLine);

        var replay = await new RolloutReplayer().ReplayModelHistoryAsync(path, thread.Turns);

        Assert.Equal(["projected request", "projected answer"], replay.Messages.Select(static message => message.Text));
        Assert.Equal(1, replay.RejectedRecords);
        Assert.Contains(thread.Turns[0].Id, replay.FallbackTurnIds!);
        Assert.Contains(replay.Warnings!, warning => warning.Code == "invalid_model_batch");
    }

    [Fact]
    public async Task ReplayModelHistory_InconsistentEnvelopeFallsBackWholeTurn()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "projected request", "projected answer");
        await _store.SaveThreadAsync(thread);
        var path = GetCanonicalPath(thread.Id, archived: false);
        var inconsistentRecord = new
        {
            kind = "model_history_messages_appended",
            timestamp = DateTimeOffset.UtcNow,
            contextCompacted = new
            {
                threadId = thread.Id,
                coveredThroughTurnId = thread.Turns[0].Id,
                replacementHistory = Array.Empty<object>()
            }
        };
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(inconsistentRecord, SessionJsonOptions.Default) + Environment.NewLine);

        var replay = await new RolloutReplayer().ReplayModelHistoryAsync(path, thread.Turns);

        Assert.Equal(["projected request", "projected answer"], replay.Messages.Select(static message => message.Text));
        Assert.Equal(1, replay.RejectedRecords);
        Assert.Contains(thread.Turns[0].Id, replay.FallbackTurnIds!);
        Assert.Contains(replay.Warnings!, warning => warning.Code == "malformed_record");
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_WhenCoveredTurnHasPostCheckpointSuffix_AppendsThatSuffix()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "raw request", "raw answer");
        await _store.SaveThreadAsync(thread);
        await _store.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[0].Id,
            [new ChatMessage(ChatRole.Assistant, "compacted summary")],
            "auto",
            "partial",
            1000,
            100);
        await _store.AppendModelHistoryAsync(
            thread.Id,
            [new ChatMessage(ChatRole.Assistant, "same-turn suffix")],
            thread.Turns[0].Id);

        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);

        Assert.Equal(
            ["assistant:compacted summary", "assistant:same-turn suffix"],
            await ExtractHistoryAsync(CreateAgent(), session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_WhenNewestCheckpointSchemaIsUnsupported_UsesOlderCheckpoint()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "seed", "answer");
        await _store.SaveThreadAsync(thread);
        await _store.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[0].Id,
            [new ChatMessage(ChatRole.Assistant, "older valid summary")],
            "manual",
            "partial",
            1000,
            100);
        var unsupportedCheckpoint = new
        {
            kind = "context_compacted",
            timestamp = DateTimeOffset.UtcNow,
            contextCompacted = new
            {
                threadId = thread.Id,
                coveredThroughTurnId = thread.Turns[0].Id,
                checkpointId = "unsupported_checkpoint",
                trigger = "manual",
                mode = "partial",
                tokensBefore = 1000,
                tokensAfter = 100,
                createdAt = DateTimeOffset.UtcNow,
                replacementHistory = new[]
                {
                    new
                    {
                        schemaVersion = 2,
                        role = "assistant",
                        contents = Array.Empty<object>()
                    }
                }
            }
        };
        await File.AppendAllTextAsync(
            GetCanonicalPath(thread.Id, archived: false),
            JsonSerializer.Serialize(unsupportedCheckpoint, SessionJsonOptions.Default) + Environment.NewLine);

        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);

        Assert.Equal(
            ["assistant:older valid summary"],
            await ExtractHistoryAsync(CreateAgent(), session));
    }

    [Fact]
    public async Task SaveTurnAsync_AppendsSingleAtomicReplacement_AndColdReplayUsesIt()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);
        AddTurnWithMessages(thread, "atomic request", "atomic answer");
        var turn = Assert.Single(thread.Turns);
        thread.LastActiveAt = turn.CompletedAt ?? turn.StartedAt;

        await _store.SaveTurnAsync(thread, turn);

        var records = File.ReadAllLines(GetCanonicalPath(thread.Id, archived: false));
        Assert.Single(records, line => line.Contains("\"kind\":\"turn_state_replaced\"", StringComparison.Ordinal));
        var loaded = await new ThreadStore(_root).LoadThreadAsync(thread.Id);
        var loadedTurn = Assert.Single(Assert.IsType<SessionThread>(loaded).Turns);
        Assert.Equal(TurnStatus.Completed, loadedTurn.Status);
        Assert.Equal("atomic request", loadedTurn.Input?.AsUserMessage?.Text);
        Assert.Equal("atomic answer", loadedTurn.Items.Last().AsAgentMessage?.Text);
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_AfterRollbackPastCheckpoint_RebuildsCheckpointWithoutRemovedTail()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "old seed", "old answer");
        AddTurnWithMessages(thread, "rolled back request", "rolled back answer");
        await _store.SaveThreadAsync(thread);
        await _store.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[0].Id,
            [new ChatMessage(ChatRole.Assistant, "compacted summary")],
            "manual",
            "partial",
            1000,
            100);

        thread.Turns.RemoveAt(1);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.RollbackThreadAsync(thread, 1);

        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);

        Assert.Equal(
            ["assistant:compacted summary"],
            await ExtractHistoryAsync(CreateAgent(), session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_AfterRollbackRemovesCoveredTurn_IgnoresCheckpoint()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "surviving seed", "surviving answer");
        AddTurnWithMessages(thread, "covered request", "covered answer");
        await _store.SaveThreadAsync(thread);
        await _store.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[1].Id,
            [new ChatMessage(ChatRole.Assistant, "summary including removed turn")],
            "manual",
            "partial",
            1000,
            100);

        thread.Turns.RemoveAt(1);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.RollbackThreadAsync(thread, 1);

        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);

        Assert.Equal(
            ["user:surviving seed", "assistant:surviving answer"],
            await ExtractHistoryAsync(CreateAgent(), session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_WhenCheckpointHistoryIsInvalid_FallsBackToRolloutHistory()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "seed", "answer");
        await _store.SaveThreadAsync(thread);
        var badCheckpoint = new
        {
            kind = "context_compacted",
            timestamp = DateTimeOffset.UtcNow,
            contextCompacted = new
            {
                threadId = thread.Id,
                coveredThroughTurnId = thread.Turns[0].Id,
                checkpointId = "bad_checkpoint",
                trigger = "manual",
                mode = "partial",
                tokensBefore = 1000,
                tokensAfter = 100,
                createdAt = DateTimeOffset.UtcNow,
                replacementHistory = new { invalid = true }
            }
        };
        await File.AppendAllTextAsync(
            GetCanonicalPath(thread.Id, archived: false),
            JsonSerializer.Serialize(badCheckpoint, SessionJsonOptions.Default) + Environment.NewLine);

        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);

        Assert.Equal(
            ["user:seed", "assistant:answer"],
            await ExtractHistoryAsync(CreateAgent(), session));
    }

    [Fact]
    public async Task LoadOrCreateSessionAsync_ForkCheckpointAfterDeletedSession_RebuildsReplacementHistoryPlusSuffix()
    {
        var thread = CreateThread();
        thread.ForkedFromId = "thread_source";
        AddTurnWithMessages(thread, "raw before compact", "raw answer");
        AddTurnWithMessages(thread, "covered raw request", "covered raw answer");
        AddTurnWithMessages(thread, "after compact", "after answer");
        await _store.SaveThreadAsync(thread);
        var replacementHistory = new[]
        {
            new ChatMessage(ChatRole.User, "compacted summary"),
            new ChatMessage(ChatRole.Assistant, "summary answer")
        };
        await _store.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[1].Id,
            replacementHistory,
            "fork",
            "partial",
            10_000,
            100);
        var session = await new ThreadStore(_root).LoadOrCreateSessionAsync(CreateAgent(), thread.Id);

        Assert.Equal(
            [
                "user:compacted summary",
                "assistant:summary answer",
                "user:after compact",
                "assistant:after answer"
            ],
            await ExtractHistoryAsync(CreateAgent(), session));
    }

    [Fact]
    public async Task AccountThreadGoalUsageAsync_ActiveOnly_SkipsStoppedAndCompleteGoals()
    {
        foreach (var status in new[]
                 {
                     ThreadGoalStatus.Paused,
                     ThreadGoalStatus.Blocked,
                     ThreadGoalStatus.UsageLimited,
                     ThreadGoalStatus.Complete
                 })
        {
            var goal = NewGoal(status);
            await _store.SaveThreadAsync(CreateThread(goal.ThreadId));
            await _store.UpsertThreadGoalAsync(goal);

            var outcome = await _store.AccountThreadGoalUsageAsync(
                goal.ThreadId,
                goal.GoalId,
                Usage(7),
                3,
                GoalAccountingMode.ActiveOnly);

            var loaded = await _store.GetThreadGoalAsync(goal.ThreadId);
            Assert.False(outcome.Updated);
            Assert.NotNull(loaded);
            Assert.Equal(0, loaded.TokensUsed.TotalTokens);
            Assert.Equal(status, loaded.Status);
        }
    }

    [Fact]
    public async Task AccountThreadGoalUsageAsync_ModePredicates_IncludeCompleteAndStoppedWhenRequested()
    {
        var complete = NewGoal(ThreadGoalStatus.Complete);
        await _store.SaveThreadAsync(CreateThread(complete.ThreadId));
        await _store.UpsertThreadGoalAsync(complete);
        var completeOutcome = await _store.AccountThreadGoalUsageAsync(
            complete.ThreadId,
            complete.GoalId,
            Usage(11),
            4,
            GoalAccountingMode.ActiveOrComplete);
        Assert.True(completeOutcome.Updated);
        Assert.Equal(11, completeOutcome.Goal?.TokensUsed.TotalTokens);
        Assert.Equal(ThreadGoalStatus.Complete, completeOutcome.Goal?.Status);

        var blocked = NewGoal(ThreadGoalStatus.Blocked);
        await _store.SaveThreadAsync(CreateThread(blocked.ThreadId));
        await _store.UpsertThreadGoalAsync(blocked);
        var blockedOutcome = await _store.AccountThreadGoalUsageAsync(
            blocked.ThreadId,
            blocked.GoalId,
            Usage(13),
            5,
            GoalAccountingMode.ActiveOrStopped);
        Assert.True(blockedOutcome.Updated);
        Assert.Equal(13, blockedOutcome.Goal?.TokensUsed.TotalTokens);
        Assert.Equal(ThreadGoalStatus.Blocked, blockedOutcome.Goal?.Status);
    }

    [Fact]
    public async Task AccountThreadGoalUsageAsync_StaleGoalId_DoesNotMutateCurrentGoal()
    {
        var goal = NewGoal(ThreadGoalStatus.Active);
        await _store.SaveThreadAsync(CreateThread(goal.ThreadId));
        await _store.UpsertThreadGoalAsync(goal);

        var outcome = await _store.AccountThreadGoalUsageAsync(
            goal.ThreadId,
            "goal_stale",
            Usage(9),
            2,
            GoalAccountingMode.ActiveOnly);

        var loaded = await _store.GetThreadGoalAsync(goal.ThreadId);
        Assert.False(outcome.Updated);
        Assert.NotNull(loaded);
        Assert.Equal(0, loaded.TokensUsed.TotalTokens);
        Assert.Equal(goal.GoalId, loaded.GoalId);
    }

    [Fact]
    public async Task AccountThreadGoalUsageAsync_BudgetCrossing_ChangesActiveGoalToBudgetLimited()
    {
        var goal = NewGoal(ThreadGoalStatus.Active) with { TokenBudget = 10 };
        await _store.SaveThreadAsync(CreateThread(goal.ThreadId));
        await _store.UpsertThreadGoalAsync(goal);

        var outcome = await _store.AccountThreadGoalUsageAsync(
            goal.ThreadId,
            goal.GoalId,
            Usage(12),
            6,
            GoalAccountingMode.ActiveOnly);

        Assert.True(outcome.Updated);
        Assert.Equal(12, outcome.Goal?.TokensUsed.TotalTokens);
        Assert.Equal(6, outcome.Goal?.TimeUsedSeconds);
        Assert.Equal(ThreadGoalStatus.BudgetLimited, outcome.Goal?.Status);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SessionThread CreateThread(string? id = null) => new()
    {
        Id = id ?? SessionIdGenerator.NewThreadId(),
        WorkspacePath = "/workspace",
        UserId = "user1",
        OriginChannel = "console",
        Status = ThreadStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow,
        HistoryMode = HistoryMode.Server
    };

    private static ThreadGoal NewGoal(ThreadGoalStatus status) => new()
    {
        ThreadId = SessionIdGenerator.NewThreadId(),
        GoalId = SessionIdGenerator.NewGoalId(),
        Objective = $"Goal {Guid.NewGuid():N}",
        Status = status,
        TokensUsed = new TokenUsageInfo(),
        TimeUsedSeconds = 0,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static TokenUsageInfo Usage(long total) => new()
    {
        InputTokens = total,
        TotalTokens = total
    };

    private static SessionThread CloneThreadSnapshotForTest(SessionThread thread)
    {
        var json = JsonSerializer.Serialize(thread, SessionJsonOptions.Default);
        return JsonSerializer.Deserialize<SessionThread>(json, SessionJsonOptions.Default)!;
    }

    private static ChatClientAgent CreateAgent()
        => new TestChatClient().AsAIAgent();

    private static Task<List<string>> ExtractHistoryAsync(ChatClientAgent _, List<ChatMessage> session)
    {
        Assert.True(session.TryGetInMemoryChatHistory(
            out var chatHistory,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));

        var history = new List<string>();
        foreach (var message in chatHistory)
        {
            history.Add($"{message.Role}:{message.Text}");
        }

        return Task.FromResult(history);
    }

    private static List<string> FormatHistoryWithContents(List<ChatMessage> session)
    {
        Assert.True(session.TryGetInMemoryChatHistory(
            out var chatHistory,
            jsonSerializerOptions: SessionPersistenceJsonOptions.Default));

        return chatHistory.Select(message =>
        {
            var text = string.Concat(message.Contents.Select(content => content switch
            {
                TextContent tc => tc.Text,
                TextReasoningContent rc => $"reasoning:{rc.Text}",
                FunctionCallContent fc => $"function_call:{fc.Name}:{fc.CallId}",
                FunctionResultContent fr => $"function_result:{fr.CallId}:{fr.Result}",
                _ => content.ToString() ?? string.Empty
            }));
            return $"{message.Role}:{text}";
        }).ToList();
    }

    private static void AddTurnWithMessages(
        SessionThread thread,
        string userText,
        string agentText,
        TurnStatus status = TurnStatus.Completed)
    {
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1),
            ThreadId = thread.Id,
            Status = status,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };

        var userItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(1),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload { Text = userText }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);

        var agentItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(2),
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new AgentMessagePayload { Text = agentText }
        };
        turn.Items.Add(agentItem);

        thread.Turns.Add(turn);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
    }

    private static SessionItem CreateMemoryNotice(SessionTurn turn, int seq) => new()
    {
        Id = SessionIdGenerator.NewItemId(seq),
        TurnId = turn.Id,
        Type = ItemType.SystemNotice,
        Status = ItemStatus.Completed,
        CreatedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Payload = new SystemNoticePayload
        {
            Kind = "memoryConsolidated"
        }
    };

    private static QueuedTurnInput CreateQueuedInput(string threadId, string text) => new()
    {
        Id = SessionIdGenerator.NewQueuedInputId(),
        ThreadId = threadId,
        NativeInputParts = [new SessionWireInputPart { Type = "text", Text = text }],
        MaterializedInputParts = [new SessionWireInputPart { Type = "text", Text = text }],
        DisplayText = text,
        Status = "queued",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private string GetCanonicalPath(string threadId, bool archived)
        => Path.Combine(_root, "threads", archived ? "archived" : "active", $"{threadId}.jsonl");

    private SqliteConnection OpenStateConnection()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_root, "state.db"),
                Mode = SqliteOpenMode.ReadWrite
            }.ToString());
        connection.Open();
        return connection;
    }

    private sealed class TestChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
