using DotCraft.AppServer;
using DotCraft.Context;
using DotCraft.GenerativeUi;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class GenerativeUiRuntimeRegistryTests
{
    private static readonly ThreadSystemPromptContext Context = new("thread_a", Path.GetTempPath());

    [Fact]
    public void Section_IsProvidedOnlyWhileACapableConnectionIsOpen()
    {
        var registry = new GenerativeUiRuntimeRegistry();
        var connection = Connection(generativeUi: true);

        registry.BindThread("thread_a", connection);

        Assert.False(string.IsNullOrWhiteSpace(registry.GetSystemPromptSection(Context)));
        Assert.Null(registry.GetSystemPromptSection(Context with { ThreadId = "thread_b" }));

        connection.MarkClosed();

        Assert.Null(registry.GetSystemPromptSection(Context));
    }

    [Fact]
    public void BindThread_IgnoresIncapableConnections()
    {
        var registry = new GenerativeUiRuntimeRegistry();

        registry.BindThread("thread_a", Connection(generativeUi: false));

        Assert.Null(registry.GetSystemPromptSection(Context));
    }

    private static AppServerConnection Connection(bool generativeUi)
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = "desktop", Version = "test" },
            new ClientConnectionCapabilities { GenerativeUi = generativeUi }));
        return connection;
    }
}
