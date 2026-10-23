using DotCraft.Agents;
using DotCraft.Hooks;
using DotCraft.Security;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class SubAgentApprovalModeResolverTests
{
    [Fact]
    public void Resolve_DirectServices_MapToTheirPolicy()
    {
        Assert.Equal(SubAgentApprovalModeResolver.RestrictedMode, SubAgentApprovalModeResolver.Resolve(null, null));
        Assert.Equal(SubAgentApprovalModeResolver.AutoApproveMode, SubAgentApprovalModeResolver.Resolve(new AutoApproveApprovalService(), null));
        Assert.Equal(SubAgentApprovalModeResolver.RestrictedMode, SubAgentApprovalModeResolver.Resolve(new DenyApprovalService(), null));
    }

    [Fact]
    public void Resolve_SessionScopedOverride_UsesOverrideService()
    {
        var scoped = new SessionScopedApprovalService(new ConsoleApprovalService());
        using (SessionScopedApprovalService.SetOverride(new AutoApproveApprovalService()))
        {
            var mode = SubAgentApprovalModeResolver.Resolve(scoped, null);
            Assert.Equal(SubAgentApprovalModeResolver.AutoApproveMode, mode);
        }
    }

    [Fact]
    public void Resolve_ChannelRouting_UsesApprovalContextSource()
    {
        var routing = new ChannelRoutingApprovalService(
            new Dictionary<string, IApprovalService>(StringComparer.OrdinalIgnoreCase)
            {
                ["qq"] = new AutoApproveApprovalService()
            },
            new ConsoleApprovalService());
        var context = new ApprovalContext { Source = "qq" };

        Assert.Equal(SubAgentApprovalModeResolver.AutoApproveMode, SubAgentApprovalModeResolver.Resolve(routing, context));
        Assert.Equal(SubAgentApprovalModeResolver.AutoApproveMode, SubAgentApprovalModeResolver.Resolve(Hooked(routing), context));
    }

    [Fact]
    public void Resolve_HookDecoratedTurnService_SeesTheUnderlyingPolicy()
    {
        var scoped = new SessionScopedApprovalService(new ConsoleApprovalService());
        using (SessionScopedApprovalService.SetOverride(Hooked(new AutoApproveApprovalService())))
        {
            Assert.Equal(
                SubAgentApprovalModeResolver.AutoApproveMode,
                SubAgentApprovalModeResolver.Resolve(scoped, null));
        }

        using (SessionScopedApprovalService.SetOverride(Hooked(new ConsoleApprovalService())))
        {
            Assert.Equal(
                SubAgentApprovalModeResolver.InteractiveMode,
                SubAgentApprovalModeResolver.Resolve(scoped, null));
        }
    }

    private static IApprovalService Hooked(IApprovalService inner) =>
        new HookApprovalService(
            inner,
            new HookRunner(new HooksFileConfig(), Path.GetTempPath()),
            threadId: "thread-1",
            turnId: "turn-1",
            workspacePath: Path.GetTempPath(),
            stopHookActive: false);
}
