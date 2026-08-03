using DotCraft.Protocol;
using DotCraft.Sessions;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using Xunit;

namespace DotCraft.Tests.Protocol;

public sealed class ThreadWorkspaceResolverTests
{
    [Fact]
    public void Apply_CwdOnly_RetargetsOldCwdAndPreservesAdditionalRoots()
    {
        var stateWorkspace = Path.GetFullPath("state");
        var oldCwd = Path.GetFullPath("primary-old");
        var newCwd = Path.GetFullPath("primary-new");
        var secondary = Path.GetFullPath("secondary");
        var config = new ThreadConfiguration
        {
            Cwd = oldCwd,
            RuntimeWorkspaceRoots = [oldCwd, secondary, newCwd]
        };

        ThreadWorkspaceResolver.Apply(stateWorkspace, config, newCwd, null);

        Assert.Equal(newCwd, config.Cwd);
        Assert.Equal([newCwd, secondary], config.RuntimeWorkspaceRoots);
    }

    [Fact]
    public void Resolve_WorktreeRetargetsPrimaryRootAndPreservesExplicitEmptyRoots()
    {
        var stateWorkspace = Path.GetFullPath("state");
        var primary = Path.GetFullPath("primary");
        var secondary = Path.GetFullPath("secondary");
        var worktree = Path.GetFullPath("worktree");

        var worktreeContext = ThreadWorkspaceResolver.Resolve(
            stateWorkspace,
            new ThreadConfiguration
            {
                Cwd = primary,
                RuntimeWorkspaceRoots = [primary, secondary],
                ExecutionWorkspaceOverride = worktree
            });
        var emptyContext = ThreadWorkspaceResolver.Resolve(
            stateWorkspace,
            new ThreadConfiguration
            {
                Cwd = primary,
                RuntimeWorkspaceRoots = []
            });

        Assert.Equal(worktree, worktreeContext.Cwd);
        Assert.Equal([worktree, secondary], worktreeContext.RuntimeWorkspaceRoots);
        Assert.Empty(emptyContext.RuntimeWorkspaceRoots);
    }
}
