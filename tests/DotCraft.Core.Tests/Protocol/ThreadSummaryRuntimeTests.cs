using DotCraft.Protocol;

namespace DotCraft.Core.Tests.Protocol;

public sealed class ThreadSummaryRuntimeTests
{
    [Fact]
    public void FromThread_IncludesRunningRuntimeSnapshot()
    {
        var thread = CreateThread();
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        var summary = ThreadSummary.FromThread(thread);

        Assert.NotNull(summary.Runtime);
        Assert.True(summary.Runtime.Running);
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

    private static SessionThread CreateThread() => new()
    {
        Id = "thread_runtime",
        WorkspacePath = "/workspace",
        UserId = "local",
        OriginChannel = "dotcraft-desktop",
        Status = ThreadStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow
    };
}
