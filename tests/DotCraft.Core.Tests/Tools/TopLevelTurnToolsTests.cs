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

        Assert.False(enter.Success);
        Assert.False(exit.Success);
        Assert.Contains("top-level thread", enter.Content, StringComparison.Ordinal);
        Assert.Contains("top-level thread", exit.Content, StringComparison.Ordinal);
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

    [Fact]
    public async Task WorktreeTools_ProjectStructuredResultAndFailOnRefusedExit()
    {
        var worktree = new DotCraft.Sessions.ThreadWorktreeInfo
        {
            BranchName = "feat/isolate",
            Path = "/repo/.worktrees/feat-isolate",
            WorkspacePath = "/repo",
            BaseRef = "main"
        };
        var refuseExit = false;
        var invocation = TurnToolInvocation.For(new TurnToolServices
        {
            IsTopLevelThread = true,
            Worktree = new WorktreeToolRuntimeContext(
                (_, _, _) => Task.FromResult(worktree),
                (_, _, _) => Task.FromResult(refuseExit
                    ? DotCraft.Sessions.WorktreeExitResult.Refused(new DotCraft.Sessions.ThreadWorktreeStatus
                    {
                        HasUncommittedChanges = true,
                        AheadCount = 1
                    })
                    : DotCraft.Sessions.WorktreeExitResult.Exited(worktree)),
                () => "/repo")
        });
        var tools = new WorktreeToolMethods();

        var entered = await tools.EnterWorktree(invocation, name: "feat/isolate");
        Assert.True(entered.Success);
        Assert.Equal("entered", entered.StructuredContent!.Value.GetProperty("outcome").GetString());
        Assert.Equal("feat/isolate", entered.StructuredContent.Value.GetProperty("branchName").GetString());
        Assert.Equal("/repo/.worktrees/feat-isolate", entered.StructuredContent.Value.GetProperty("path").GetString());
        Assert.Equal("main", entered.StructuredContent.Value.GetProperty("baseRef").GetString());

        var exited = await tools.ExitWorktree(invocation, ExitWorktreeAction.Keep);
        Assert.True(exited.Success);
        Assert.Equal("kept", exited.StructuredContent!.Value.GetProperty("outcome").GetString());
        Assert.Equal("/repo", exited.StructuredContent.Value.GetProperty("checkoutPath").GetString());

        refuseExit = true;
        var refused = await tools.ExitWorktree(invocation, ExitWorktreeAction.Remove);
        Assert.False(refused.Success);
        Assert.Contains("discardChanges: true", refused.Content, StringComparison.Ordinal);
    }
}
