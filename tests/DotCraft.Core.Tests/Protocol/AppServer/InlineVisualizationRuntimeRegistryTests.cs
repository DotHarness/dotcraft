using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Protocol.InlineVisualizations;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class InlineVisualizationRuntimeRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-visualization-runtime-tests", Guid.NewGuid().ToString("N"));

    public InlineVisualizationRuntimeRegistryTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void BindThread_ProvidesThreadScopedPromptAndWritableRoot()
    {
        var assets = new InlineVisualizationAssetStore();
        var registry = new InlineVisualizationRuntimeRegistry(assets, new AppConfig());
        var connection = CapableConnection();
        var transport = new InMemoryTransport();
        var thread = Thread("thread_a");

        Assert.True(registry.BindThread(thread, transport, connection));
        Assert.True(registry.IsBoundTo(thread.Id, connection));
        Assert.True(registry.TryGetAuthoringDirectory(thread.Id, out var directory));
        Assert.Equal(Path.Combine(_root, ".craft", "visualizations", thread.Id), directory);
        Assert.True(Directory.Exists(directory));
        var prompt = registry.GetSystemPromptSection(new ThreadSystemPromptContext(thread.Id, thread.WorkspacePath));
        Assert.Contains(directory, prompt, StringComparison.Ordinal);
        Assert.Contains("::dotcraft-inline-vis", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_IsConnectionAndTransportScoped()
    {
        var registry = new InlineVisualizationRuntimeRegistry(new InlineVisualizationAssetStore(), new AppConfig());
        var owner = CapableConnection();
        var other = CapableConnection();
        var transport = new InMemoryTransport();
        var thread = Thread("thread_a");
        registry.BindThread(thread, transport, owner);

        Assert.False(registry.IsBoundTo(thread.Id, other));
        Assert.Equal([thread.Id], registry.UnbindTransport(transport));
        Assert.False(registry.TryGetAuthoringDirectory(thread.Id, out _));
    }

    [Fact]
    public void BindThread_DoesNotEnableSandboxOrIncapableClients()
    {
        var sandboxConfig = new AppConfig();
        sandboxConfig.Tools.Sandbox.Enabled = true;
        var sandboxRegistry = new InlineVisualizationRuntimeRegistry(new InlineVisualizationAssetStore(), sandboxConfig);
        var thread = Thread("thread_a");

        Assert.False(sandboxRegistry.BindThread(thread, new InMemoryTransport(), CapableConnection()));
        Assert.False(new InlineVisualizationRuntimeRegistry(new InlineVisualizationAssetStore(), new AppConfig())
            .BindThread(thread, new InMemoryTransport(), new AppServerConnection()));
    }

    [Fact]
    public void BindThread_DoesNotEnableAnUnavailableWorkspace()
    {
        var registry = new InlineVisualizationRuntimeRegistry(new InlineVisualizationAssetStore(), new AppConfig());
        var thread = Thread("thread_missing");
        thread.WorkspacePath = Path.Combine(_root, "missing");

        Assert.False(registry.BindThread(thread, new InMemoryTransport(), CapableConnection()));
        Assert.False(registry.TryGetAuthoringDirectory(thread.Id, out _));
    }

    private static AppServerConnection CapableConnection()
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new AppServerClientInfo { Name = "desktop", Version = "test" },
            new AppServerClientCapabilities { InlineVisualizations = true }));
        return connection;
    }

    private SessionThread Thread(string id) => new()
    {
        Id = id,
        CreatedAt = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero),
        WorkspacePath = _root
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
