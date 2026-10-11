using System.Text.Json;
using DotCraft.Context;
using DotCraft.Configuration;
using DotCraft.Visualizations;
using DotCraft.Tools;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Workspaces;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class VisualizationRuntimeRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-visualization-runtime-tests", Guid.NewGuid().ToString("N"));

    public VisualizationRuntimeRegistryTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void BindThread_ProvidesThreadScopedPromptWithoutCreatingDirectory()
    {
        var assets = CreateAssetStore();
        var registry = new VisualizationRuntimeRegistry(assets);
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
    }

    [Fact]
    public async Task FirstAssetWrite_CreatesDirectoryAndProducesReadableFragment()
    {
        var assets = CreateAssetStore();
        var registry = new VisualizationRuntimeRegistry(assets);
        var thread = Thread("thread_a");
        var path = Path.Combine(_root, ".craft", "visualizations", thread.Id, "chart.html");
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
                Text = $"visualize{JsonSerializer.Serialize(new { path })}"
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
        var writeResult = await fileTools.WriteFile(path, "<div>chart</div>");

        Assert.True(writeResult.Success, writeResult.Error?.Message);
        Assert.True(Directory.Exists(directory));
        Assert.Equal("<div>chart</div>", await assets.ReadReferencedFragmentAsync(thread, turn, item, path));
    }

    [Fact]
    public void Binding_IsConnectionAndTransportScoped()
    {
        var registry = new VisualizationRuntimeRegistry(CreateAssetStore());
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
    public void BindThread_DoesNotEnableIncapableClients()
    {
        var thread = Thread("thread_a");
        Assert.False(new VisualizationRuntimeRegistry(CreateAssetStore())
            .BindThread(thread, new InMemoryTransport(), new AppServerConnection()));
    }

    [Fact]
    public void BindThread_DoesNotEnableAnUnavailableWorkspace()
    {
        var registry = new VisualizationRuntimeRegistry(CreateAssetStore());
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
        var registry = new VisualizationRuntimeRegistry(CreateAssetStore());
        var thread = Thread("thread_a");

        Assert.False(registry.BindThread(thread, new InMemoryTransport(), CapableConnection()));
        Assert.False(registry.TryGetAuthoringDirectory(thread.Id, out _));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    private static AppServerConnection CapableConnection()
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = "desktop", Version = "test" },
            new ClientConnectionCapabilities { Visualizations = true }));
        return connection;
    }

    private VisualizationAssetStore CreateAssetStore() =>
        new(new DotCraftPaths(_root, Path.Combine(_root, ".craft"), userDataPath: null));

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
