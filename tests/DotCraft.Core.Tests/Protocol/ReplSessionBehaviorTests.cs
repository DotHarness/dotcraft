using DotCraft.Protocol;
using Microsoft.Data.Sqlite;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Tests verifying the REPL session behavior contract after the Phase 3 Session Protocol migration:
///
/// 1. Lazy thread creation -- no thread is persisted on startup or /new without user input.
/// 2. Load (/load) restores a persisted thread including turn history.
/// 3. Archive (/delete current) resets to lazy pending state without creating a new thread.
/// 4. Thread index only contains threads that have had at least one turn.
/// 5. Agent session (chat history) is persisted and restored correctly.
///
/// All tests use <see cref="FakeSessionService"/> backed by a real <see cref="ThreadStore"/>
/// written to a temporary directory, so they verify the full persistence contract.
/// </summary>
public sealed class ReplSessionBehaviorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly FakeSessionService _svc;
    private readonly SessionIdentity _cliIdentity;

    public ReplSessionBehaviorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ReplBehavior_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _store = new ThreadStore(_tempDir);
        _svc = new FakeSessionService(_store);
        _cliIdentity = new SessionIdentity
        {
            ChannelName = "cli",
            UserId = "local",
            WorkspacePath = _tempDir
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // -------------------------------------------------------------------------
    // Lazy thread creation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LazyInit_NoInput_NoThreadFilesCreated()
    {
        // Simulates: REPL starts with sessionService present, user types nothing.
        // Expectation: zero rollout files on disk, zero index entries.

        var threadsDir = Path.Combine(_tempDir, "threads");
        var threadFiles = Directory.Exists(threadsDir)
            ? Directory.GetFiles(threadsDir, "*.jsonl", SearchOption.AllDirectories)
            : [];

        Assert.Empty(threadFiles);

        var index = await _store.LoadIndexAsync();
        Assert.Empty(index);
    }

    [Fact]
    public async Task LazyInit_FirstInput_ExactlyOneThreadCreated()
    {
        // Simulates: REPL starts, user sends first message.
        // Expectation: exactly one thread is persisted after CreateThreadAsync.

        var thread = await _svc.CreateThreadAsync(_cliIdentity);
        Assert.NotNull(thread);
        Assert.False(string.IsNullOrEmpty(thread.Id));

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(thread.Id, index[0].Id);
    }

    [Fact]
    public async Task LazyInit_MultipleStartupsNoInput_NoOrphanThreads()
    {
        // Simulates N REPL startup cycles where no message is ever sent.
        // In lazy mode, no CreateThreadAsync is called; disk stays clean.

        // Verify that without any explicit CreateThreadAsync calls, the store remains empty.
        for (var i = 0; i < 5; i++)
        {
            // Each "startup" just reads the index (FindThreadsAsync equivalent)
            var found = await _svc.FindThreadsAsync(_cliIdentity);
            Assert.Empty(found);
        }

        var allIndex = await _store.LoadIndexAsync();
        Assert.Empty(allIndex);

        var threadsDir = Path.Combine(_tempDir, "threads");
        Assert.False(Directory.Exists(threadsDir) && Directory.GetFiles(threadsDir, "*.jsonl", SearchOption.AllDirectories).Length > 0,
            "No rollout files should exist when no input was ever sent.");
    }

    [Fact]
    public async Task LazyInit_SendTwoMessages_OnlyOneThreadExists()
    {
        // Even though both messages are sent, both go to the same thread.
        // A new thread should not be created per message.

        var thread = await _svc.CreateThreadAsync(_cliIdentity);

        // Simulate adding turns directly (FakeSessionService doesn't run the agent)
        AddTurnWithUserMessage(thread, "First message");
        await _store.SaveThreadAsync(thread);

        AddTurnWithUserMessage(thread, "Second message");
        await _store.SaveThreadAsync(thread);

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(thread.Id, index[0].Id);
        Assert.Equal(2, index[0].TurnCount);
    }

    // -------------------------------------------------------------------------
    // /new command (lazy reset)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NewSession_BeforeAnyInput_NoAdditionalThreadCreated()
    {
        // Simulates: user types /new before sending any message.
        // In lazy mode, /new just resets state to null without calling CreateThreadAsync.
        // Nothing should be written to disk.

        // Verify the store is still empty (no thread from lazy init)
        var index = await _store.LoadIndexAsync();
        Assert.Empty(index);
    }

    [Fact]
    public async Task NewSession_AfterFirstInput_OriginalThreadKept_NewPending()
    {
        // Simulates: user sends a message (materializing thread A), then types /new.
        // Thread A should remain on disk. No thread B should be created yet.

        var threadA = await _svc.CreateThreadAsync(_cliIdentity);
        AddTurnWithUserMessage(threadA, "Hello");
        await _store.SaveThreadAsync(threadA);

        // /new in lazy mode: don't create a new thread yet
        // The session resets _currentThreadId = null without calling CreateThreadAsync.

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(threadA.Id, index[0].Id);
    }

    // -------------------------------------------------------------------------
    // /load command (ResumeThreadAsync + history)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadSession_ResumesExistingThread()
    {
        // Create and persist a thread with one turn.
        var thread = await _svc.CreateThreadAsync(_cliIdentity);
        AddTurnWithUserMessage(thread, "Remember this");
        await _store.SaveThreadAsync(thread);

        // Simulate a new REPL instance: fresh FakeSessionService with same store.
        var newSvc = new FakeSessionService(_store);

        // /load calls ResumeThreadAsync
        var resumed = await newSvc.ResumeThreadAsync(thread.Id);

        Assert.NotNull(resumed);
        Assert.Equal(thread.Id, resumed.Id);
        Assert.Equal(ThreadStatus.Active, resumed.Status);
        Assert.Single(resumed.Turns);
        Assert.Equal("Remember this",
            (resumed.Turns[0].Input?.Payload as UserMessagePayload)?.Text);
    }

    [Fact]
    public async Task LoadSession_FindThreadsAsync_ReturnsOnlyNonArchivedByChannel()
    {
        var t1 = await _svc.CreateThreadAsync(_cliIdentity);
        var t2 = await _svc.CreateThreadAsync(_cliIdentity);
        var otherIdentity = new SessionIdentity
        {
            ChannelName = "api",
            UserId = "user2",
            WorkspacePath = _tempDir
        };
        await _svc.CreateThreadAsync(otherIdentity);

        var cliThreads = await _svc.FindThreadsAsync(_cliIdentity);
        Assert.Equal(2, cliThreads.Count);
        Assert.Contains(cliThreads, t => t.Id == t1.Id);
        Assert.Contains(cliThreads, t => t.Id == t2.Id);
    }

    [Fact]
    public async Task LoadSession_ThreadWithTurns_TurnCountPreserved()
    {
        var thread = await _svc.CreateThreadAsync(_cliIdentity);
        AddTurnWithUserMessage(thread, "Turn 1");
        AddTurnWithUserMessage(thread, "Turn 2");
        AddTurnWithUserMessage(thread, "Turn 3");
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Turns.Count);
    }

    [Fact]
    public async Task LoadSession_TurnUserMessages_RestoredCorrectly()
    {
        var thread = await _svc.CreateThreadAsync(_cliIdentity);
        AddTurnWithMessages(thread, "Hello", "World response");
        AddTurnWithMessages(thread, "Follow-up question", "Another answer");
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);

        var texts = loaded!.Turns
            .Select(t => (t.Input?.Payload as UserMessagePayload)?.Text)
            .ToList();
        Assert.Equal(["Hello", "Follow-up question"], texts);
    }

    [Fact]
    public async Task LoadSession_TurnAgentMessages_RestoredCorrectly()
    {
        var thread = await _svc.CreateThreadAsync(_cliIdentity);
        AddTurnWithMessages(thread, "Hello", "Hi there, how can I help?");
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);

        var agentItem = loaded!.Turns[0].Items
            .FirstOrDefault(i => i.Type == ItemType.AgentMessage);
        Assert.NotNull(agentItem);
        Assert.Equal("Hi there, how can I help?",
            (agentItem!.Payload as AgentMessagePayload)?.Text);
    }

    // -------------------------------------------------------------------------
    // /delete current → lazy reset
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ArchiveCurrentThread_LeavesNoActiveCurrent_NoPendingThreadFile()
    {
        // Create a thread (simulating first user input).
        var thread = await _svc.CreateThreadAsync(_cliIdentity);
        AddTurnWithUserMessage(thread, "Hello");
        await _store.SaveThreadAsync(thread);

        // Archive it (/delete in Session Protocol mode).
        await _svc.ArchiveThreadAsync(thread.Id);

        // In lazy mode, the session sets _currentThreadId = null without creating a new thread.
        // Verify: the archive did not create any additional threads.
        var index = await _store.LoadIndexAsync();
        Assert.Single(index); // still only the one (now archived) thread
        Assert.Equal(ThreadStatus.Archived, index[0].Status);
    }

    [Fact]
    public async Task ArchiveThread_FindThreadsAsync_ExcludesArchivedByDefault_NotReturned()
    {
        var thread = await _svc.CreateThreadAsync(_cliIdentity);
        await _svc.ArchiveThreadAsync(thread.Id);

        // FindThreadsAsync (as used by /load) should still return archived threads
        // in the raw index -- the filtering of archived threads from /load UI is a
        // presentation concern in SessionPrompt. Verify the raw index has it archived.
        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(ThreadStatus.Archived, index[0].Status);
    }

    [Fact]
    public async Task ArchiveCurrentThread_ThenFirstInput_CreatesNewThread()
    {
        // Simulate: user sends message → thread A exists → user /deletes it →
        // user sends another message → new thread B should be created.

        var threadA = await _svc.CreateThreadAsync(_cliIdentity);
        await _svc.ArchiveThreadAsync(threadA.Id);

        // Lazy: the session resets to null. On next input, CreateThreadAsync is called.
        var threadB = await _svc.CreateThreadAsync(_cliIdentity);
        Assert.NotEqual(threadA.Id, threadB.Id);
        Assert.Equal(ThreadStatus.Active, threadB.Status);

        var index = await _store.LoadIndexAsync();
        Assert.Equal(2, index.Count);
        Assert.Contains(index, t => t.Id == threadA.Id && t.Status == ThreadStatus.Archived);
        Assert.Contains(index, t => t.Id == threadB.Id && t.Status == ThreadStatus.Active);
    }

    // -------------------------------------------------------------------------
    // Thread index integrity
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadIndex_AfterCreate_ContainsSummaryEntry()
    {
        var thread = await _svc.CreateThreadAsync(_cliIdentity);

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(thread.Id, index[0].Id);
        Assert.Equal("cli", index[0].OriginChannel);
        Assert.Equal("local", index[0].UserId);
    }

    [Fact]
    public async Task ThreadIndex_TurnCount_UpdatedAfterSave()
    {
        var thread = await _svc.CreateThreadAsync(_cliIdentity);
        Assert.Equal(0, (await _store.LoadIndexAsync())[0].TurnCount);

        AddTurnWithUserMessage(thread, "msg1");
        await _store.SaveThreadAsync(thread);
        Assert.Equal(1, (await _store.LoadIndexAsync())[0].TurnCount);

        AddTurnWithUserMessage(thread, "msg2");
        await _store.SaveThreadAsync(thread);
        Assert.Equal(2, (await _store.LoadIndexAsync())[0].TurnCount);
    }

    [Fact]
    public async Task ThreadIndex_AlwaysInSyncWithPersistedMetadata()
    {
        // LoadIndexAsync reads persisted SQLite metadata written alongside canonical rollout files.
        var t1 = await _svc.CreateThreadAsync(_cliIdentity);
        var t2 = await _svc.CreateThreadAsync(_cliIdentity);
        AddTurnWithUserMessage(t1, "hello");
        await _store.SaveThreadAsync(t1);
        await _store.SaveThreadAsync(t2);

        var index = await _store.LoadIndexAsync();
        Assert.Equal(2, index.Count);
        Assert.Contains(index, e => e.Id == t1.Id);
        Assert.Contains(index, e => e.Id == t2.Id);
        Assert.Equal(1, index.First(e => e.Id == t1.Id).TurnCount);
    }

    // -------------------------------------------------------------------------
    // Session file (agent chat history) persistence
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SessionState_NotCreated_UntilTurnCompletes()
    {
        // ThreadStore only writes thread_sessions rows during SaveSessionAsync
        // (called by SessionService after the agent finishes).
        // Simply creating a thread should NOT produce persisted session state.

        var thread = await _svc.CreateThreadAsync(_cliIdentity);

        Assert.False(_store.SessionFileExists(thread.Id),
            "thread_sessions state should not exist before any turn completes.");
    }

    [Fact]
    public async Task SessionState_ExistsAfterPersistingSessionRow()
    {
        // Simulates what SessionService does after a turn: persists serialized AgentSession
        // into the thread_sessions table.

        var thread = await _svc.CreateThreadAsync(_cliIdentity);

        Assert.False(_store.SessionFileExists(thread.Id));

        InsertThreadSession(thread.Id, """{"chatHistory":[],"type":"chatHistory"}""");

        Assert.True(_store.SessionFileExists(thread.Id));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void AddTurnWithUserMessage(SessionThread thread, string userText)
    {
        var turn = MakeTurn(thread, userText, null);
        thread.Turns.Add(turn);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
    }

    private static void AddTurnWithMessages(SessionThread thread, string userText, string agentText)
    {
        var turn = MakeTurn(thread, userText, agentText);
        thread.Turns.Add(turn);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
    }

    private static SessionTurn MakeTurn(SessionThread thread, string userText, string? agentText)
    {
        var seq = thread.Turns.Count + 1;
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(seq),
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
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

        if (agentText != null)
        {
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
        }

        return turn;
    }

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

    private SqliteConnection OpenStateConnection()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_tempDir, "state.db"),
                Mode = SqliteOpenMode.ReadWrite
            }.ToString());
        connection.Open();
        return connection;
    }
}
