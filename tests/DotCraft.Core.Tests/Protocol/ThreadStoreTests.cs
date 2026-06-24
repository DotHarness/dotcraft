using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context.Compaction;
using DotCraft.Protocol;
using Microsoft.Agents.AI;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

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
    public void CloneThreadSnapshot_DeepCopiesWithSerializedShape()
    {
        var thread = CreateThread();
        thread.Metadata["custom"] = "before";
        AddTurnWithMessages(thread, "hello", "reply");

        var clone = CloneThreadSnapshotForTest(thread);

        Assert.Equal(
            JsonSerializer.Serialize(thread, SessionJsonOptions.Default),
            JsonSerializer.Serialize(clone, SessionJsonOptions.Default));

        clone.Metadata["custom"] = "after";
        clone.Turns[0].Items[0].Payload = new UserMessagePayload { Text = "changed" };

        Assert.Equal("before", thread.Metadata["custom"]);
        Assert.Equal("hello", Assert.IsType<UserMessagePayload>(thread.Turns[0].Items[0].Payload).Text);
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

        var agentItem = new SessionItem
        {
            Id = "item_002",
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = turn.StartedAt.AddSeconds(1),
            CompletedAt = turn.StartedAt.AddSeconds(1),
            Payload = new AgentMessagePayload { Text = "world" }
        };
        turn.Items.Add(agentItem);
        turn.Status = TurnStatus.Completed;
        turn.CompletedAt = turn.StartedAt.AddSeconds(1);
        thread.LastActiveAt = turn.CompletedAt.Value;
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        var loadedTurn = Assert.Single(loaded.Turns);
        Assert.Equal(TurnStatus.Completed, loadedTurn.Status);
        Assert.Equal(2, loadedTurn.Items.Count);
        Assert.Equal("hello", loadedTurn.Input?.AsUserMessage?.Text);
        Assert.Equal("world", loadedTurn.Items[1].AsAgentMessage?.Text);
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
    public async Task RebuildAndSaveSessionFromThreadAsync_IncludesPairedToolCallAndResult()
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
        await _store.RebuildAndSaveSessionFromThreadAsync(agent, thread.Id);
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
    public async Task RebuildAndSaveSessionFromThreadAsync_SubAgentMailboxUserItem_ReplaysAsUserMessage()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "hello", "before mailbox", TurnStatus.Completed);
        var turn = thread.Turns[0];
        const string notification = "<subagent_notification>{\"agentPath\":\"/root/inspect\",\"status\":{\"completed\":\"done\"}}</subagent_notification>";
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
        await _store.RebuildAndSaveSessionFromThreadAsync(agent, thread.Id);
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
    public async Task RebuildAndSaveSessionFromThreadAsync_EmptyToolArgumentsBecomeEmptyDictionary()
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
        await _store.RebuildAndSaveSessionFromThreadAsync(agent, thread.Id);
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
    public async Task RebuildAndSaveSessionFromThreadAsync_MergesReasoningTextAndToolCallIntoOneAssistantMessage()
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
        await _store.RebuildAndSaveSessionFromThreadAsync(agent, thread.Id);
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
    public async Task RebuildAndSaveSessionFromThreadAsync_SkipsDanglingToolPairs()
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
        await _store.RebuildAndSaveSessionFromThreadAsync(agent, thread.Id);
        var session = await _store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.Equal(["user:hello", "assistant:before"], FormatHistoryWithContents(session));
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
    public async Task LoadIndex_IgnoresThreadSessionsStoredInDb()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);
        InsertThreadSession(thread.Id, """{"chatHistory":[],"type":"chatHistory"}""");

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(thread.Id, index[0].Id);
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
    public async Task LoadOrCreateSessionAsync_WhenSessionRowIsInvalid_FallsBackToRolloutHistory()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "Need context", "Here is the context.");
        await _store.SaveThreadAsync(thread);
        InsertInvalidThreadSession(thread.Id, "{not valid json");

        var store = new ThreadStore(_root);
        var agent = CreateAgent();

        var session = await store.LoadOrCreateSessionAsync(agent, thread.Id);

        Assert.Equal(
            ["user:Need context", "assistant:Here is the context."],
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
        var agent = CreateAgent();
        await _store.SaveSessionFromHistoryAsync(
            agent,
            thread.Id,
            [
                .. replacementHistory,
                new ChatMessage(ChatRole.User, "after compact"),
                new ChatMessage(ChatRole.Assistant, "after answer")
            ]);
        Assert.True(_store.SessionFileExists(thread.Id));
        _store.DeleteSessionFile(thread.Id);

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
        var method = typeof(ThreadStore).GetMethod(
            "CloneThreadSnapshot",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (SessionThread)method.Invoke(null, [thread])!;
    }

    private static AIAgent CreateAgent()
        => new TestChatClient().AsAIAgent(new ChatClientAgentOptions());

    private static Task<List<string>> ExtractHistoryAsync(AIAgent _, AgentSession session)
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

    private static List<string> FormatHistoryWithContents(AgentSession session)
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

    private void InsertThreadSession(string threadId, string sessionJson)
    {
        using var connection = OpenStateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_sessions(thread_id, session_json, updated_at)
            VALUES ($thread_id, $session_json, $updated_at)
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$session_json", sessionJson);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    private void InsertInvalidThreadSession(string threadId, string sessionJson)
    {
        using var connection = OpenStateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_sessions(thread_id, session_json, updated_at)
            VALUES ($thread_id, $session_json, $updated_at)
            ON CONFLICT(thread_id) DO UPDATE SET
                session_json = excluded.session_json,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$session_json", sessionJson);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

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
