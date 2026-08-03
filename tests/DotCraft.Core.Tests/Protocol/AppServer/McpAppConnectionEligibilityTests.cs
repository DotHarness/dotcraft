using DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Protocol.AppServer;

public sealed class McpAppConnectionEligibilityTests
{
    [Fact]
    public void RollbackRevocation_SignalsActiveViewsForSelectedThread()
    {
        var connection = CreateConnection(supportsMcpApps: true);
        string? revokedThreadId = null;
        connection.McpAppThreadEligibilityRevoked += threadId => revokedThreadId = threadId;

        connection.RevokeMcpAppThreadEligibility("thread-1");

        Assert.Equal("thread-1", revokedThreadId);
    }

    private static AppServerConnection CreateConnection(bool supportsMcpApps)
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = "test", Version = "1" },
            new ClientConnectionCapabilities { McpApps = supportsMcpApps }));
        return connection;
    }
}
