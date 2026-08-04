using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class InterruptOnApprovalServiceTests
{
    [Fact]
    public async Task ApprovalRequests_ReturnFalseWithoutCancellingTurn()
    {
        var service = new InterruptOnApprovalService();

        Assert.False(await service.RequestFileApprovalAsync("read", "outside.txt"));
        Assert.False(await service.RequestShellApprovalAsync("dotnet test", "workspace"));
        Assert.False(await service.RequestResourceApprovalAsync("remoteResource", "publish", "github"));
    }
}
