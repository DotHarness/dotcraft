using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;

namespace DotCraft.Core.Tests.Protocol.AppServer;

public sealed class WireAcpExtensionProxyTests
{
    private sealed class StubTransport : IAppServerTransport
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);
        public Task WriteMessageAsync(object message, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null) =>
            throw new NotImplementedException();
    }

    [Fact]
    public void BindThread_UnbindTransport_RemovesBinding()
    {
        var prev = TracingChatClient.CurrentSessionKey;
        try
        {
            var proxy = new WireAcpExtensionProxy();
            var transport = new StubTransport();
            var connection = new AppServerConnection();
            Assert.True(connection.TryMarkInitialized(
                new AppServerClientInfo { Name = "t", Version = "1" },
                new AppServerClientCapabilities
                {
                    AcpExtensions = new AcpExtensionCapability { FsReadTextFile = true }
                }));

            proxy.BindThread("thread-a", transport, connection);
            TracingChatClient.CurrentSessionKey = "thread-a";
            Assert.True(proxy.SupportsFileRead);

            proxy.UnbindTransport(transport);
            Assert.False(proxy.SupportsFileRead);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = prev;
        }
    }

    [Fact]
    public void MapToWireMethod_PrefixesExtAcp()
    {
        Assert.Equal("ext/acp/fs/readTextFile", WireAcpExtensionProxy.MapToWireMethod("fs/readTextFile"));
        Assert.Equal("ext/acp/_unity/scene_query", WireAcpExtensionProxy.MapToWireMethod("_unity/scene_query"));
        Assert.Equal("ext/acp/fs/readTextFile", WireAcpExtensionProxy.MapToWireMethod("ext/acp/fs/readTextFile"));
    }
}
