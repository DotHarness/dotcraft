using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadSummary = DotCraft.Sessions.ThreadSummary;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using ToolResultPayload = DotCraft.Sessions.ToolResultPayload;
using Xunit;

namespace DotCraft.Core.Tests.Protocol;

public sealed class ThreadSummaryRuntimeTests
{
    [Fact]
    public void FromThread_IncludesRunningRuntimeSnapshot()
    {
        var thread = CreateThread();
        var startedAt = DateTimeOffset.UtcNow;
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = startedAt
        });

        var summary = ThreadSummary.FromThread(thread);

        Assert.NotNull(summary.Runtime);
        Assert.True(summary.Runtime.Running);
        Assert.Equal("turn_001", summary.Runtime.ActiveTurnId);
        Assert.Equal(startedAt, summary.Runtime.ActiveTurnStartedAt);
        Assert.False(summary.Runtime.WaitingOnApproval);
        Assert.False(summary.Runtime.WaitingOnPlanConfirmation);
    }

    [Fact]
    public void FromThread_IncludesWaitingApprovalRuntimeSnapshot()
    {
        var thread = CreateThread();
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.WaitingApproval,
            StartedAt = DateTimeOffset.UtcNow
        });

        var summary = ThreadSummary.FromThread(thread);

        Assert.NotNull(summary.Runtime);
        Assert.True(summary.Runtime.Running);
        Assert.True(summary.Runtime.WaitingOnApproval);
        Assert.False(summary.Runtime.WaitingOnPlanConfirmation);
    }

    [Fact]
    public void FromThread_IncludesWaitingInputRuntimeSnapshot()
    {
        var thread = CreateThread();
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.WaitingInput,
            StartedAt = DateTimeOffset.UtcNow
        });

        var summary = ThreadSummary.FromThread(thread);

        Assert.NotNull(summary.Runtime);
        Assert.True(summary.Runtime.Running);
        Assert.False(summary.Runtime.WaitingOnApproval);
        Assert.True(summary.Runtime.WaitingOnInput);
        Assert.False(summary.Runtime.WaitingOnPlanConfirmation);
    }

    [Fact]
    public void FromThread_PreservesPlanConfirmationWhenCleanupToolFollowsCreatePlan()
    {
        var thread = CreateThread("plan");
        thread.Turns.Add(CreateCompletedTurn(
            thread,
            "turn_001",
            CreateToolCall("turn_001", "item_001", "call_plan", "CreatePlan"),
            CreateToolResult("turn_001", "item_002", "call_plan", "CreatePlan", success: true),
            CreateToolCall("turn_001", "item_003", "call_close", "CloseAgent"),
            CreateToolResult("turn_001", "item_004", "call_close", "CloseAgent", success: true)));

        var summary = ThreadSummary.FromThread(thread);

        Assert.NotNull(summary.Runtime);
        Assert.Null(summary.Runtime.ActiveTurnId);
        Assert.Null(summary.Runtime.ActiveTurnStartedAt);
        Assert.True(summary.Runtime.WaitingOnPlanConfirmation);
    }

    [Fact]
    public void FromThread_DoesNotWaitForFailedCreatePlan()
    {
        var thread = CreateThread("plan");
        thread.Turns.Add(CreateCompletedTurn(
            thread,
            "turn_001",
            CreateToolCall("turn_001", "item_001", "call_plan", "CreatePlan"),
            CreateToolResult("turn_001", "item_002", "call_plan", "CreatePlan", success: false)));

        var summary = ThreadSummary.FromThread(thread);

        Assert.NotNull(summary.Runtime);
        Assert.False(summary.Runtime.WaitingOnPlanConfirmation);
    }

    [Fact]
    public void FromThread_DoesNotWaitForCreatePlanOutsidePlanMode()
    {
        var thread = CreateThread("agent");
        thread.Turns.Add(CreateCompletedTurn(
            thread,
            "turn_001",
            CreateToolCall("turn_001", "item_001", "call_plan", "CreatePlan"),
            CreateToolResult("turn_001", "item_002", "call_plan", "CreatePlan", success: true)));

        var summary = ThreadSummary.FromThread(thread);

        Assert.NotNull(summary.Runtime);
        Assert.False(summary.Runtime.WaitingOnPlanConfirmation);
    }

    [Fact]
    public void FromThread_DoesNotRevivePlanFromEarlierCompletedTurn()
    {
        var thread = CreateThread("plan");
        thread.Turns.Add(CreateCompletedTurn(
            thread,
            "turn_001",
            CreateToolCall("turn_001", "item_001", "call_plan", "CreatePlan"),
            CreateToolResult("turn_001", "item_002", "call_plan", "CreatePlan", success: true)));
        thread.Turns.Add(CreateCompletedTurn(thread, "turn_002"));

        var summary = ThreadSummary.FromThread(thread);

        Assert.NotNull(summary.Runtime);
        Assert.False(summary.Runtime.WaitingOnPlanConfirmation);
    }

    private static SessionThread CreateThread(string mode = "agent") => new()
    {
        Id = "thread_runtime",
        WorkspacePath = "/workspace",
        UserId = "local",
        OriginChannel = "dotcraft-desktop",
        Status = ThreadStatus.Active,
        Configuration = new ThreadConfiguration { Mode = mode },
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow
    };

    private static SessionTurn CreateCompletedTurn(
        SessionThread thread,
        string turnId,
        params SessionItem[] items) => new()
        {
            Id = turnId,
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Items = [.. items]
        };

    private static SessionItem CreateToolCall(
        string turnId,
        string itemId,
        string callId,
        string toolName) => new()
        {
            Id = itemId,
            TurnId = turnId,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                CallId = callId,
                ToolName = toolName
            }
        };

    private static SessionItem CreateToolResult(
        string turnId,
        string itemId,
        string callId,
        string toolName,
        bool success) => new()
        {
            Id = itemId,
            TurnId = turnId,
            Type = ItemType.ToolResult,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolResultPayload
            {
                CallId = callId,
                ToolName = toolName,
                Success = success,
                Result = success ? "ok" : "failed"
            }
        };
}
