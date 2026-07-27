using DotCraft.Context;
using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Protocol.InlineVisualizations;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class InlineVisualizationRuntimeRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-visualization-runtime-tests", Guid.NewGuid().ToString("N"));

    public InlineVisualizationRuntimeRegistryTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void BindThread_ProvidesThreadScopedPromptWithoutCreatingDirectory()
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
        Assert.False(Directory.Exists(Path.Combine(_root, ".craft", "visualizations")));
        Assert.False(Directory.Exists(directory));
        var prompt = registry.GetSystemPromptSection(new ThreadSystemPromptContext(thread.Id, thread.WorkspacePath));
        Assert.Contains(directory, prompt, StringComparison.Ordinal);
        Assert.Contains("::dotcraft-inline-vis", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstAssetWrite_CreatesDirectoryAndProducesReadableFragment()
    {
        var assets = new InlineVisualizationAssetStore();
        var registry = new InlineVisualizationRuntimeRegistry(assets, new AppConfig());
        var thread = Thread("thread_a");
        var now = DateTimeOffset.UtcNow;
        var item = new SessionItem
        {
            Id = "item_agent",
            TurnId = "turn_test",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new AgentMessagePayload
            {
                Text = "::dotcraft-inline-vis{file=\"chart.html\"}"
            }
        };
        var turn = new SessionTurn
        {
            Id = "turn_test",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now,
            Items = [item]
        };
        thread.Turns.Add(turn);

        Assert.True(registry.BindThread(thread, new InMemoryTransport(), CapableConnection()));
        Assert.True(registry.TryGetAuthoringDirectory(thread.Id, out var directory));
        Assert.False(Directory.Exists(directory));

        var fileTools = new FileTools(_root, requireApprovalOutsideWorkspace: false);
        var writeResult = await fileTools.WriteFile(Path.Combine(directory, "chart.html"), "<div>chart</div>");

        Assert.StartsWith("Successfully wrote", writeResult, StringComparison.Ordinal);
        Assert.True(Directory.Exists(directory));
        Assert.Equal("<div>chart</div>", await assets.ReadReferencedFragmentAsync(thread, turn, item, "chart.html"));
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

    [Theory]
    [InlineData(".craft")]
    [InlineData(".craft/visualizations")]
    [InlineData(".craft/visualizations/thread_a")]
    public void BindThread_RejectsReparsePointsAtEveryAuthoringDirectoryLevel(string relativeLinkPath)
    {
        var target = Path.Combine(_root, "link-target");
        var link = Path.Combine(_root, relativeLinkPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }
        var registry = new InlineVisualizationRuntimeRegistry(new InlineVisualizationAssetStore(), new AppConfig());
        var thread = Thread("thread_a");

        Assert.False(registry.BindThread(thread, new InMemoryTransport(), CapableConnection()));
        Assert.False(registry.TryGetAuthoringDirectory(thread.Id, out _));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
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
