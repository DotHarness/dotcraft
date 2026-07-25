using DotCraft.Agents;
using DotCraft.Protocol;
using DotCraft.Security;

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
}
