using DotCraft.Protocol;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Conformance tests verifying Phase 3 channel migration contracts:
/// - HistoryMode.Client propagation through CreateThreadAsync and ThreadStore
/// - Channel identity metadata (channelContext) stored correctly on threads
/// - SessionEvent payload accessor patterns used by channel adapters
/// </summary>
public sealed class ChannelConformanceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly FakeSessionService _svc;

    public ChannelConformanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ChannelConf_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _store = new ThreadStore(_tempDir);
        _svc = new FakeSessionService(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    // -------------------------------------------------------------------------
    // HistoryMode.Client
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateThread_DefaultHistoryMode_IsServer()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity("api", "u1"));
        Assert.Equal(HistoryMode.Server, thread.HistoryMode);
    }

    [Fact]
    public async Task CreateThread_ClientHistoryMode_IsPreserved()
    {
        var thread = await _svc.CreateThreadAsync(
            MakeIdentity("api", "u1"),
            historyMode: HistoryMode.Client);

        Assert.Equal(HistoryMode.Client, thread.HistoryMode);
    }

    [Fact]
    public async Task CreateThread_ClientHistoryMode_IsPersisted()
    {
        var thread = await _svc.CreateThreadAsync(
            MakeIdentity("api", "u2"),
            historyMode: HistoryMode.Client);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(HistoryMode.Client, loaded!.HistoryMode);
    }

    [Fact]
    public async Task CreateThread_ServerHistoryMode_IsPersisted()
    {
        var thread = await _svc.CreateThreadAsync(
            MakeIdentity("cli", "u3"),
            historyMode: HistoryMode.Server);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(HistoryMode.Server, loaded!.HistoryMode);
    }

    // -------------------------------------------------------------------------
    // Channel identity metadata
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateThread_ChannelContext_StoredInMetadataAndFirstClassProperty()
    {
        var identity = new SessionIdentity
        {
            ChannelName = "qq",
            UserId = "user_123",
            ChannelContext = "group:456",
            WorkspacePath = _tempDir
        };

        var thread = await _svc.CreateThreadAsync(identity);

        Assert.Equal("group:456", thread.ChannelContext);
        Assert.Equal("group:456", thread.Metadata["channelContext"]);
    }

    [Fact]
    public async Task CreateThread_NullChannelContext_NoMetadataEntry()
    {
        var identity = new SessionIdentity
        {
            ChannelName = "cli",
            UserId = "u_cli",
            ChannelContext = null,
            WorkspacePath = _tempDir
        };

        var thread = await _svc.CreateThreadAsync(identity);

        Assert.False(thread.Metadata.ContainsKey("channelContext"));
    }

    [Fact]
    public async Task CreateThread_ChannelName_StoredAsOriginChannel()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity("wecom", "wc_user"));
        Assert.Equal("wecom", thread.OriginChannel);
    }

    [Fact]
    public async Task CreateThread_UserId_StoredOnThread()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity("agui", "agui_thread_xyz"));
        Assert.Equal("agui_thread_xyz", thread.UserId);
    }

    // -------------------------------------------------------------------------
    // SessionEvent payload accessor patterns
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionEvent_DeltaPayload_AccessedCorrectly()
    {
        var delta = new AgentMessageDelta { TextDelta = "hello" };
        var evt = new SessionEvent
        {
            EventType = SessionEventType.ItemDelta,
            Payload = delta,
            EventId = "e1",
            ThreadId = "t1",
            Timestamp = DateTimeOffset.UtcNow
        };

        Assert.NotNull(evt.DeltaPayload);
        Assert.Equal("hello", evt.DeltaPayload!.TextDelta);
        Assert.Null(evt.ItemPayload);
        Assert.Null(evt.TurnPayload);
    }

    [Fact]
    public void SessionEvent_ItemPayload_ToolCall_AccessedCorrectly()
    {
        var toolPayload = new ToolCallPayload
        {
            ToolName = "WriteFile",
            CallId = "call_001"
        };
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.ToolCall,
            Status = ItemStatus.Started,
            Payload = toolPayload,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var evt = new SessionEvent
        {
            EventType = SessionEventType.ItemStarted,
            Payload = item,
            EventId = "e2",
            ThreadId = "t1",
            ItemId = item.Id,
            Timestamp = DateTimeOffset.UtcNow
        };

        Assert.NotNull(evt.ItemPayload);
        Assert.Equal(ItemType.ToolCall, evt.ItemPayload!.Type);
        Assert.NotNull(evt.ItemPayload.AsToolCall);
        Assert.Equal("WriteFile", evt.ItemPayload.AsToolCall!.ToolName);
        Assert.Equal("call_001", evt.ItemPayload.AsToolCall.CallId);
    }

    [Fact]
    public void SessionEvent_ItemPayload_ToolResult_AccessedCorrectly()
    {
        var resultPayload = new ToolResultPayload
        {
            CallId = "call_001",
            Result = "done",
            Success = true
        };
        var item = new SessionItem
        {
            Id = "item_002",
            TurnId = "turn_001",
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            Payload = resultPayload,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var evt = new SessionEvent
        {
            EventType = SessionEventType.ItemCompleted,
            Payload = item,
            EventId = "e3",
            ThreadId = "t1",
            ItemId = item.Id,
            Timestamp = DateTimeOffset.UtcNow
        };

        Assert.Equal(ItemType.ToolResult, evt.ItemPayload!.Type);
        Assert.NotNull(evt.ItemPayload.AsToolResult);
        Assert.Equal("call_001", evt.ItemPayload.AsToolResult!.CallId);
        Assert.Equal("done", evt.ItemPayload.AsToolResult.Result);
    }

    [Fact]
    public void SessionEvent_TurnPayload_Error_IsString()
    {
        var turn = new SessionTurn
        {
            Id = "turn_fail",
            ThreadId = "t1",
            Status = TurnStatus.Failed,
            Error = "agent_error: connection timeout",
            StartedAt = DateTimeOffset.UtcNow
        };
        var evt = new SessionEvent
        {
            EventType = SessionEventType.TurnFailed,
            Payload = turn,
            EventId = "e4",
            ThreadId = "t1",
            TurnId = turn.Id,
            Timestamp = DateTimeOffset.UtcNow
        };

        Assert.NotNull(evt.TurnPayload);
        Assert.Equal("agent_error: connection timeout", evt.TurnPayload!.Error);
    }

    [Fact]
    public void SessionEvent_ApprovalPayload_AccessedViaItemPayload()
    {
        var approvalPayload = new ApprovalRequestPayload
        {
            ApprovalType = "shell",
            Operation = "rm -rf /tmp/test",
            Target = "/tmp",
            RequestId = "req_001"
        };
        var item = new SessionItem
        {
            Id = "item_003",
            TurnId = "turn_001",
            Type = ItemType.ApprovalRequest,
            Status = ItemStatus.Started,
            Payload = approvalPayload,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var evt = new SessionEvent
        {
            EventType = SessionEventType.ApprovalRequested,
            Payload = item,
            EventId = "e5",
            ThreadId = "t1",
            Timestamp = DateTimeOffset.UtcNow
        };

        Assert.NotNull(evt.ItemPayload);
        Assert.Equal(ItemType.ApprovalRequest, evt.ItemPayload!.Type);
        Assert.NotNull(evt.ItemPayload.AsApprovalRequest);
        Assert.Equal("req_001", evt.ItemPayload.AsApprovalRequest!.RequestId);
        Assert.Equal("shell", evt.ItemPayload.AsApprovalRequest.ApprovalType);
    }

    // -------------------------------------------------------------------------
    // Thread index (FindThreads) by identity
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindThreads_ByChannelAndUser_ReturnsOnlyMatching()
    {
        var idQQ = MakeIdentity("qq", "qq_user_a");
        var idCli = MakeIdentity("cli", "cli_user_b");

        var qqThread = await _svc.CreateThreadAsync(idQQ);
        await _svc.CreateThreadAsync(idCli);

        var results = await _svc.FindThreadsAsync(idQQ);

        Assert.Single(results);
        Assert.Equal(qqThread.Id, results[0].Id);
    }

    [Fact]
    public async Task FindThreads_QQ_PrivateAndGroupChatAreIsolated()
    {
        var userId = "12345678";
        var privateIdentity = new SessionIdentity
        {
            ChannelName = "qq",
            UserId = userId,
            ChannelContext = $"user:{userId}",
            WorkspacePath = _tempDir
        };
        var groupIdentity = new SessionIdentity
        {
            ChannelName = "qq",
            UserId = userId,
            ChannelContext = "group:987654",
            WorkspacePath = _tempDir
        };

        var privateThread = await _svc.CreateThreadAsync(privateIdentity);
        var groupThread = await _svc.CreateThreadAsync(groupIdentity);

        var privateResults = await _svc.FindThreadsAsync(privateIdentity);
        var groupResults = await _svc.FindThreadsAsync(groupIdentity);

        Assert.Single(privateResults);
        Assert.Equal(privateThread.Id, privateResults[0].Id);

        Assert.Single(groupResults);
        Assert.Equal(groupThread.Id, groupResults[0].Id);
    }

    [Fact]
    public async Task FindThreads_WeCom_DifferentChatsAreIsolated()
    {
        var userId = "wc_user_01";
        var chat1Identity = new SessionIdentity
        {
            ChannelName = "wecom",
            UserId = userId,
            ChannelContext = "chat:room_a",
            WorkspacePath = _tempDir
        };
        var chat2Identity = new SessionIdentity
        {
            ChannelName = "wecom",
            UserId = userId,
            ChannelContext = "chat:room_b",
            WorkspacePath = _tempDir
        };

        var thread1 = await _svc.CreateThreadAsync(chat1Identity);
        var thread2 = await _svc.CreateThreadAsync(chat2Identity);

        var room1Results = await _svc.FindThreadsAsync(chat1Identity);
        var room2Results = await _svc.FindThreadsAsync(chat2Identity);

        Assert.Single(room1Results);
        Assert.Equal(thread1.Id, room1Results[0].Id);

        Assert.Single(room2Results);
        Assert.Equal(thread2.Id, room2Results[0].Id);
    }

    [Fact]
    public async Task FindThreads_NullChannelContext_DoesNotMatchContextualThreads()
    {
        var cliIdentity = MakeIdentity("cli", "local");
        var qqIdentity = new SessionIdentity
        {
            ChannelName = "qq",
            UserId = "local",
            ChannelContext = "user:local",
            WorkspacePath = _tempDir
        };

        await _svc.CreateThreadAsync(qqIdentity);
        var cliResults = await _svc.FindThreadsAsync(cliIdentity);

        Assert.Empty(cliResults);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private SessionIdentity MakeIdentity(string channel = "test", string userId = "user_001")
        => new()
        {
            ChannelName = channel,
            UserId = userId,
            ChannelContext = null,
            WorkspacePath = _tempDir
        };
}
