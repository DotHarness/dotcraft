using DotCraft.Sessions;
using DotCraft.Tests.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class DenyApprovalServiceTests
{
    [Fact]
    public async Task ApprovalRequests_ReturnFalseWithoutCancellingTurn()
    {
        var service = new DenyApprovalService();

        Assert.False(await service.RequestFileApprovalAsync("read", "outside.txt"));
        Assert.False(await service.RequestShellApprovalAsync(ShellApprovalRequests.For("dotnet test")));
        Assert.False(await service.RequestResourceApprovalAsync("remoteResource", "publish", "github"));
    }
}
