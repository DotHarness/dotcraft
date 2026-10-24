using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.Tools;

public sealed class TopLevelTurnToolsTests
{
    [Fact]
    public async Task ChildThread_CannotMoveAWorktree()
    {
        var called = false;
        var invocation = TurnToolInvocation.For(new TurnToolServices
        {
            IsTopLevelThread = false,
            Worktree = new WorktreeToolRuntimeContext(
                (_, _, _) =>
                {
                    called = true;
                    return Task.FromResult(new DotCraft.Sessions.ThreadWorktreeInfo());
                },
                (_, _, _) =>
                {
                    called = true;
                    return Task.FromResult(new DotCraft.Sessions.WorktreeExitResult());
                },
                () => "parent")
        });
        var tools = new WorktreeToolMethods();

        var enter = await tools.EnterWorktree(invocation, name: "child");
        var exit = await tools.ExitWorktree(invocation, ExitWorktreeAction.Remove);

        Assert.Contains("top-level thread", enter, StringComparison.Ordinal);
        Assert.Contains("top-level thread", exit, StringComparison.Ordinal);
        Assert.False(called);
    }

    [Fact]
    public async Task ChildThread_CannotReachTheGoal()
    {
        var invocation = TurnToolInvocation.For(new TurnToolServices
        {
            IsTopLevelThread = false,
            Goal = new GoalToolRuntimeContext(null!, "parent", "turn")
        });

        var result = await new GoalToolMethods().GetGoal(invocation);

        Assert.Contains("top-level thread", result, StringComparison.Ordinal);
    }
}
