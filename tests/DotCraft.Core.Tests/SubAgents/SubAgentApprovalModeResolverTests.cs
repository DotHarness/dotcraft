using DotCraft.Agents;
using DotCraft.Hooks;
using DotCraft.Security;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class SubAgentApprovalModeResolverTests
{
    [Fact]
    public void Resolve_NullService_ReturnsRestricted()
    {
        var mode = SubAgentApprovalModeResolver.Resolve(null, null);
        Assert.Equal(SubAgentApprovalModeResolver.RestrictedMode, mode);
    }

    [Fact]
    public void Resolve_AutoApproveService_ReturnsAutoApprove()
    {
        var mode = SubAgentApprovalModeResolver.Resolve(new AutoApproveApprovalService(), null);
        Assert.Equal(SubAgentApprovalModeResolver.AutoApproveMode, mode);
    }

    [Fact]
    public void Resolve_InterruptService_ReturnsRestricted()
    {
        var mode = SubAgentApprovalModeResolver.Resolve(new InterruptOnApprovalService(), null);
        Assert.Equal(SubAgentApprovalModeResolver.RestrictedMode, mode);
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

        var mode = SubAgentApprovalModeResolver.Resolve(routing, context);

        Assert.Equal(SubAgentApprovalModeResolver.AutoApproveMode, mode);
    }

    /// <summary>The standard SessionService turn shape: a hook decorator between the scope and the policy.</summary>
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

    /// <summary>Decoration order is not part of the contract: a hook above the router resolves the same.</summary>
    [Fact]
    public void Resolve_HookAboveChannelRouting_StillUsesApprovalContextSource()
    {
        var routing = new ChannelRoutingApprovalService(
            new Dictionary<string, IApprovalService>(StringComparer.OrdinalIgnoreCase)
            {
                ["qq"] = new AutoApproveApprovalService()
            },
            new ConsoleApprovalService());

        var mode = SubAgentApprovalModeResolver.Resolve(
            Hooked(routing),
            new ApprovalContext { Source = "qq" });

        Assert.Equal(SubAgentApprovalModeResolver.AutoApproveMode, mode);
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
