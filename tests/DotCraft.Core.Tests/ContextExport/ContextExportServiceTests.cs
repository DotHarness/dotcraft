using System.Text.Json.Nodes;
using DotCraft.ContextExport;
using DotCraft.Protocol;
using DotCraft.State;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.ContextExport;

public sealed class ContextExportServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;
    private readonly StateRuntime _stateRuntime;
    private readonly ThreadStore _threadStore;

    public ContextExportServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ContextExportTests_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_workspace);
        _stateRuntime = new StateRuntime(_craft);
        _threadStore = new ThreadStore(_craft, _stateRuntime);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task ExportAsync_AfterRollback_OmitsRolledBackTurnAndListsContinuityEvent()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "keep this request", "keep this answer");
        AddTurnWithMessages(thread, "remove this request", "remove this answer");
        await _threadStore.SaveThreadAsync(thread);

        thread.Turns.RemoveAt(1);
        thread.LastActiveAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await _threadStore.RollbackThreadAsync(thread, 1);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace
        });

        Assert.Contains("keep this request", result.Markdown);
        Assert.Contains("keep this answer", result.Markdown);
        Assert.DoesNotContain("remove this request", result.Markdown);
        Assert.DoesNotContain("remove this answer", result.Markdown);
        Assert.Contains("Rollback", result.Markdown);
    }

    [Fact]
    public async Task ExportAsync_WithToolResultsNone_OmitsPersistedResultBody()
    {
        var thread = CreateThread();
        AddTurnWithToolResult(thread, "SECRET_TOOL_OUTPUT");
        await _threadStore.SaveThreadAsync(thread);

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace,
            ToolResults = ContextExportToolResultMode.None
        });

        Assert.DoesNotContain("SECRET_TOOL_OUTPUT", result.Markdown);
        Assert.Contains("omitted by `--tool-results none`", result.Markdown);
    }

    [Fact]
    public async Task ExportAsync_UsesSurvivingCompactionCheckpointAndReadsMemory()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "old seed", "old answer");
        AddTurnWithMessages(thread, "recent request", "recent answer");
        await _threadStore.SaveThreadAsync(thread);
        await _threadStore.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[0].Id,
            [new ChatMessage(ChatRole.Assistant, "compacted summary")],
            "manual",
            "partial",
            1000,
            100);

        var memoryDir = Path.Combine(_craft, "memory");
        Directory.CreateDirectory(memoryDir);
        await File.WriteAllTextAsync(Path.Combine(memoryDir, "MEMORY.md"), "Remember: use readonly export.");
        await File.WriteAllTextAsync(Path.Combine(memoryDir, "HISTORY.md"), "old event\n\nrecent memory event");

        var result = await new ContextExportService().ExportAsync(new ContextExportOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _workspace
        });

        Assert.Contains("compacted summary", result.Markdown);
        Assert.Contains("recent request", result.Markdown);
        Assert.Contains("Remember: use readonly export.", result.Markdown);
        Assert.Contains("recent memory event", result.Markdown);
        Assert.Contains("Compaction", result.Markdown);
    }

    [Fact]
    public async Task SearchAsync_TraceEventMatch_ReturnsBoundThreadEvidence()
    {
        var thread = CreateThread();
        AddTurnWithMessages(thread, "provider issue", "failed");
        await _threadStore.SaveThreadAsync(thread);

        var traceStore = new TraceStore(Path.Combine(_craft, "tracing"), 5000, false, _stateRuntime);
        traceStore.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Error,
            Content = "provider context explosion",
            ModelId = "gpt-test"
        });
        traceStore.WaitForPendingPersistence();

        var result = await new ContextSearchService().SearchAsync(new ContextSearchOptions
        {
            WorkspacePath = _workspace,
            Query = "context explosion",
            Limit = 5
        });

        var hit = Assert.Single(result.Hits);
        Assert.Equal(thread.Id, hit.ThreadId);
        Assert.Contains(hit.Evidence, evidence => evidence.Source == "trace_events");
    }

    [Fact]
    public async Task SearchAsync_WhenCraftDirectoryMissing_DoesNotCreateWorkspaceState()
    {
        var workspace = Path.Combine(_root, "empty-workspace");
        Directory.CreateDirectory(workspace);

        var result = await new ContextSearchService().SearchAsync(new ContextSearchOptions
        {
            WorkspacePath = workspace,
            Query = "anything"
        });

        Assert.Empty(result.Hits);
        Assert.NotEmpty(result.Warnings);
        Assert.False(Directory.Exists(Path.Combine(workspace, ".craft")));
    }

    private SessionThread CreateThread() => new()
    {
        Id = SessionIdGenerator.NewThreadId(),
        WorkspacePath = _workspace,
        UserId = "user1",
        OriginChannel = "cli",
        Status = ThreadStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow,
        HistoryMode = HistoryMode.Server
    };

    private static void AddTurnWithMessages(
        SessionThread thread,
        string userText,
        string agentText)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(thread.Turns.Count);
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1),
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now.AddSeconds(1)
        };
        var userItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(1),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = userText }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(2),
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now.AddMilliseconds(100),
            CompletedAt = now.AddMilliseconds(100),
            Payload = new AgentMessagePayload { Text = agentText }
        });
        thread.Turns.Add(turn);
        thread.LastActiveAt = turn.CompletedAt.Value;
    }

    private static void AddTurnWithToolResult(SessionThread thread, string resultText)
    {
        AddTurnWithMessages(thread, "run tool", "calling tool");
        var turn = thread.Turns[0];
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(3),
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = turn.StartedAt.AddMilliseconds(200),
            CompletedAt = turn.StartedAt.AddMilliseconds(200),
            Payload = new ToolCallPayload
            {
                ToolName = "ReadFile",
                CallId = "call_1",
                Arguments = new JsonObject { ["path"] = "demo.txt" }
            }
        });
        turn.Items.Add(new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(4),
            TurnId = turn.Id,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = turn.StartedAt.AddMilliseconds(300),
            CompletedAt = turn.StartedAt.AddMilliseconds(300),
            Payload = new ToolResultPayload
            {
                CallId = "call_1",
                Result = resultText,
                Success = true
            }
        });
    }
}
