using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Protocol.AppServer;

public sealed class McpAppConnectionEligibilityTests
{
    [Fact]
    public void LiveEligibility_IsConnectionAndItemScoped()
    {
        var owner = CreateConnection(supportsMcpApps: true);
        var other = CreateConnection(supportsMcpApps: true);

        Assert.True(owner.TryRegisterMcpAppItem("thread-1", "turn-1", "item-1"));
        Assert.True(owner.IsMcpAppItemEligible("thread-1", "turn-1", "item-1"));
        Assert.False(owner.IsMcpAppItemEligible("thread-2", "turn-1", "item-1"));
        Assert.False(owner.IsMcpAppItemEligible("thread-1", "turn-2", "item-1"));
        Assert.False(owner.IsMcpAppItemEligible("thread-1", "turn-1", "item-2"));
        Assert.False(other.IsMcpAppItemEligible("thread-1", "turn-1", "item-1"));
    }

    [Fact]
    public void LiveEligibility_RequiresCapabilityAndEndsWithConnection()
    {
        var unsupported = CreateConnection(supportsMcpApps: false);
        Assert.False(unsupported.TryRegisterMcpAppItem("thread-1", "turn-1", "item-1"));

        var supported = CreateConnection(supportsMcpApps: true);
        Assert.True(supported.TryRegisterMcpAppItem("thread-1", "turn-1", "item-1"));

        supported.MarkClosed();

        Assert.False(supported.IsMcpAppItemEligible("thread-1", "turn-1", "item-1"));
        Assert.False(supported.TryRegisterMcpAppItem("thread-1", "turn-1", "item-2"));
    }

    [Fact]
    public void RollbackRevocation_ClearsOnlyTheSelectedThreadAndSignalsViews()
    {
        var connection = CreateConnection(supportsMcpApps: true);
        Assert.True(connection.TryRegisterMcpAppItem("thread-1", "turn-7", "item-1"));
        Assert.True(connection.TryRegisterMcpAppItem("thread-2", "turn-1", "item-1"));
        string? revokedThreadId = null;
        connection.McpAppThreadEligibilityRevoked += threadId => revokedThreadId = threadId;

        connection.RevokeMcpAppThreadEligibility("thread-1");

        Assert.Equal("thread-1", revokedThreadId);
        Assert.False(connection.IsMcpAppItemEligible("thread-1", "turn-7", "item-1"));
        Assert.True(connection.IsMcpAppItemEligible("thread-2", "turn-1", "item-1"));
    }

    private static AppServerConnection CreateConnection(bool supportsMcpApps)
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new AppServerClientInfo { Name = "test", Version = "1" },
            new AppServerClientCapabilities { McpApps = supportsMcpApps }));
        return connection;
    }
}
