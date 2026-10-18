using DotCraft.AppServer;
using DotCraft.Sessions;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ThreadTokenUsageNotificationTests
{
    private static readonly ThreadTokenUsageUpdate ChildUpdate = new(
        "thread_child",
        "thread_parent",
        "turn_002",
        new TokenUsageInfo { InputTokens = 1_200, OutputTokens = 80, CachedInputTokens = 900, ReasoningOutputTokens = 30, TotalTokens = 1_280 },
        new TokenUsageInfo { InputTokens = 400, OutputTokens = 20, CachedInputTokens = 350, TotalTokens = 420 },
        ModelContextWindow: 200_000);

    [Fact]
    public async Task SubAgentUsage_ReachesParentAndChildSubscribersOnce()
    {
        var subscribers = new AppServerThreadSubscribers();
        var parent = Register(subscribers, Connection(), "thread_parent");
        var child = Register(subscribers, Connection(), "thread_child");
        var both = Register(subscribers, Connection(), "thread_parent", "thread_child");
        var unrelated = Register(subscribers, Connection(), "thread_other");

        subscribers.NotifyThreadTokenUsageUpdated(ChildUpdate);

        foreach (var transport in new[] { parent, child, both })
        {
            var message = (await transport.ReadNextSentAsync()).RootElement;
            Assert.Equal(Contract.AppServerMethodNames.ThreadTokenUsageUpdated, message.GetProperty("method").GetString());
            var parameters = message.GetProperty("params");
            Assert.Equal("thread_child", parameters.GetProperty("threadId").GetString());
            Assert.Equal("turn_002", parameters.GetProperty("turnId").GetString());
            var usage = parameters.GetProperty("tokenUsage");
            Assert.Equal(1_280, usage.GetProperty("total").GetProperty("totalTokens").GetInt64());
            Assert.Equal(900, usage.GetProperty("total").GetProperty("cachedInputTokens").GetInt64());
            Assert.Equal(30, usage.GetProperty("total").GetProperty("reasoningOutputTokens").GetInt64());
            Assert.Equal(420, usage.GetProperty("last").GetProperty("totalTokens").GetInt64());
            Assert.Equal(200_000, usage.GetProperty("modelContextWindow").GetInt32());
            Assert.Empty(transport.DrainSent());
        }

        Assert.Empty(unrelated.DrainSent());
    }

    [Fact]
    public void OptedOutConnection_ReceivesNoUsage()
    {
        var subscribers = new AppServerThreadSubscribers();
        var optedOut = Register(
            subscribers,
            Connection(Contract.AppServerMethodNames.ThreadTokenUsageUpdated),
            "thread_parent",
            "thread_child");

        subscribers.NotifyThreadTokenUsageUpdated(ChildUpdate);

        Assert.Empty(optedOut.DrainSent());
    }

    private static InMemoryTransport Register(
        AppServerThreadSubscribers subscribers,
        AppServerConnection connection,
        params string[] threadIds)
    {
        var transport = new InMemoryTransport();
        foreach (var threadId in threadIds)
            Assert.True(connection.TryAddSubscription(threadId, new CancellationTokenSource()));
        subscribers.Register(transport, connection);
        return transport;
    }

    private static AppServerConnection Connection(params string[] optOutMethods)
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = "desktop", Version = "test" },
            new ClientConnectionCapabilities { OptOutNotificationMethods = [.. optOutMethods] }));
        return connection;
    }
}
