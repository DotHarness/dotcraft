using DotCraft.Configuration;
using DotCraft.InlineVisualizations;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using DotCraft.Sessions;
using DotCraft.Workspaces;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class InlineVisualizationViewLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-inline-visualization-view-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OpenMessageAndClose_UseConnectionOwnedHandle()
    {
        Directory.CreateDirectory(_root);
        var thread = CreateThread();
        var sessions = new TestableSessionService(new ThreadStore(_root));
        await sessions.SeedThreadAsync(thread);
        var connection = CapableConnection();
        await using var transport = new InMemoryTransport();
        var assets = new InlineVisualizationAssetStore(
            new DotCraftPaths(_root, Path.Combine(_root, ".craft"), userDataPath: null));
        var runtime = new InlineVisualizationRuntimeRegistry(assets, new AppConfig());
        Assert.True(runtime.BindThread(thread, transport, connection));
        var directory = runtime.TryGetAuthoringDirectory(thread.Id, out var authoringDirectory)
            ? authoringDirectory
            : throw new InvalidOperationException("The authoring directory was not bound.");
        var fileTools = new FileTools(_root, requireApprovalOutsideWorkspace: false);
        var writeResult = await fileTools.WriteFile(Path.Combine(directory, "chart.html"), "<div>chart</div>");
        Assert.StartsWith("Successfully wrote", writeResult, StringComparison.Ordinal);

        using var handler = new InlineVisualizationRequestHandler(sessions, connection, assets, runtime);
        var table = new AppServerMethodTable();
        handler.RegisterMethods(table);
        Assert.True(table.TryGet(DotCraft.Protocol.AppServer.AppServerMethodNames.InlineVisualizationViewOpen, out var open));
        var opened = Assert.IsType<Contract.InlineVisualizationViewOpenResult>(await open(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.InlineVisualizationViewOpen,
                new { threadId = thread.Id, turnId = "turn_test", itemId = "item_agent", file = "chart.html" }),
            CancellationToken.None));
        Assert.Equal("<div>chart</div>", opened.Fragment.Value);

        Assert.True(table.TryGet(DotCraft.Protocol.AppServer.AppServerMethodNames.InlineVisualizationViewMessage, out var message));
        var sent = Assert.IsType<Contract.InlineVisualizationViewMessageResult>(await message(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.InlineVisualizationViewMessage,
                new { viewHandle = opened.ViewHandle.Value, prompt = "Explain the selected point." }),
            CancellationToken.None));
        Assert.Equal(sent.QueuedInputId.Value, sessions.LastStartedQueuedInput?.Id);
        Assert.Equal("visualization", sessions.LastStartedQueuedInput?.TriggerKind);
        Assert.Equal("chart.html", sessions.LastStartedQueuedInput?.TriggerLabel);
        Assert.Equal("item_agent", sessions.LastStartedQueuedInput?.TriggerRefId);
        Assert.Equal(
            "Explain the selected point.",
            Assert.IsType<TextContent>(Assert.Single(sessions.LastSubmittedContent)).Text);

        Assert.True(table.TryGet(DotCraft.Protocol.AppServer.AppServerMethodNames.InlineVisualizationViewClose, out var close));
        var closed = Assert.IsType<Contract.InlineVisualizationViewCloseResult>(await close(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.InlineVisualizationViewClose,
                new { viewHandle = opened.ViewHandle.Value }),
            CancellationToken.None));
        Assert.True(closed.Closed.Value);

        var stale = await Assert.ThrowsAsync<AppServerException>(() => message(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.InlineVisualizationViewMessage,
                new { viewHandle = opened.ViewHandle.Value, prompt = "retry" }),
            CancellationToken.None));
        Assert.Equal("stale_view", Assert.IsType<AppServerErrorData>(stale.ErrorData).Code);
    }

    private SessionThread CreateThread()
    {
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
        return new SessionThread
        {
            Id = "thread_test",
            WorkspacePath = _root,
            CreatedAt = now,
            LastActiveAt = now,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_test",
                    ThreadId = "thread_test",
                    Status = TurnStatus.Completed,
                    StartedAt = now,
                    CompletedAt = now,
                    Items = [item]
                }
            ]
        };
    }

    private static AppServerConnection CapableConnection()
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = "desktop", Version = "test" },
            new ClientConnectionCapabilities { InlineVisualizations = true }));
        return connection;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // SQLite may retain a pooled handle briefly on Windows; cleanup is best-effort.
        }
    }
}
