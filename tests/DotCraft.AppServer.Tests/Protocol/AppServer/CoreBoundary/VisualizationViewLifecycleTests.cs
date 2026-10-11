using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Visualizations;
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

public sealed class VisualizationViewLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-visualization-view-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OpenMessageAndClose_UseConnectionOwnedHandle()
    {
        var (sessions, handler, table, path) = await CreateHandlerAsync();
        using var _ = handler;
        var opened = await OpenAsync(table, path);
        Assert.Equal("<div>chart</div>", opened.Fragment.Value);
        Assert.Null(opened.WidgetState.Value);

        Assert.True(table.TryGet(DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewMessage, out var message));
        var sent = Assert.IsType<Contract.VisualizationViewMessageResult>(await message(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewMessage,
                new { viewHandle = opened.ViewHandle.Value, prompt = "Explain the selected point." }),
            CancellationToken.None));
        Assert.Equal(sent.QueuedInputId.Value, sessions.LastStartedQueuedInput?.Id);
        Assert.Equal("visualization", sessions.LastStartedQueuedInput?.TriggerKind);
        Assert.Equal("chart.html", sessions.LastStartedQueuedInput?.TriggerLabel);
        Assert.Equal("item_agent", sessions.LastStartedQueuedInput?.TriggerRefId);
        Assert.Equal(
            "Explain the selected point.",
            Assert.IsType<TextContent>(Assert.Single(sessions.LastSubmittedContent)).Text);

        Assert.True(table.TryGet(DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewClose, out var close));
        var closed = Assert.IsType<Contract.VisualizationViewCloseResult>(await close(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewClose,
                new { viewHandle = opened.ViewHandle.Value }),
            CancellationToken.None));
        Assert.True(closed.Closed.Value);

        var stale = await Assert.ThrowsAsync<AppServerException>(() => message(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewMessage,
                new { viewHandle = opened.ViewHandle.Value, prompt = "retry" }),
            CancellationToken.None));
        Assert.Equal("stale_view", Assert.IsType<AppServerErrorData>(stale.ErrorData).Code);
    }

    [Fact]
    public async Task WidgetState_RoundTripsThroughOpenAndRejectsOversizedSnapshots()
    {
        var (_, handler, table, path) = await CreateHandlerAsync();
        using var _ = handler;
        var first = await OpenAsync(table, path);
        Assert.True(table.TryGet(DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewWidgetStateUpdate, out var update));

        await update(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewWidgetStateUpdate,
                new { viewHandle = first.ViewHandle.Value, modelContent = new { region = "north" }, privateContent = new { scroll = 120 } }),
            CancellationToken.None);
        var tooLarge = await Assert.ThrowsAsync<AppServerException>(() => update(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewWidgetStateUpdate,
                new { viewHandle = first.ViewHandle.Value, privateContent = new string('x', 16 * 1024) }),
            CancellationToken.None));
        Assert.Equal("state_too_large", Assert.IsType<AppServerErrorData>(tooLarge.ErrorData).Code);

        var reopened = (await OpenAsync(table, path)).WidgetState.Value!;
        Assert.Equal("north", reopened.ModelContent.Value!.Value.GetProperty("region").GetString());
        Assert.Equal(120, reopened.PrivateContent.Value!.Value.GetProperty("scroll").GetInt32());

        await update(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewWidgetStateUpdate,
                new { viewHandle = first.ViewHandle.Value, modelContent = (object?)null }),
            CancellationToken.None);
        Assert.Null((await OpenAsync(table, path)).WidgetState.Value);
    }

    [Fact]
    public async Task WidgetStateUpdate_NotifiesOtherCapableSubscribersOfTheThread()
    {
        var subscribers = new AppServerThreadSubscribers();
        var (_, handler, table, path) = await CreateHandlerAsync(subscribers);
        using var _ = handler;
        var peer = new InMemoryTransport();
        var peerConnection = CapableConnection();
        Assert.True(peerConnection.TryAddSubscription("thread_test", new CancellationTokenSource()));
        subscribers.Register(peer, peerConnection);
        var opened = await OpenAsync(table, path);
        Assert.True(table.TryGet(DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewWidgetStateUpdate, out var update));

        await update(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewWidgetStateUpdate,
                new { viewHandle = opened.ViewHandle.Value, modelContent = new { region = "north" } }),
            CancellationToken.None);
        var updated = (await peer.ReadNextSentAsync()).RootElement;
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationWidgetStateUpdated, updated.GetProperty("method").GetString());
        var parameters = updated.GetProperty("params");
        Assert.Equal("thread_test", parameters.GetProperty("threadId").GetString());
        Assert.Equal(Path.GetFullPath(path), parameters.GetProperty("path").GetString());
        Assert.Equal("north", parameters.GetProperty("widgetState").GetProperty("modelContent").GetProperty("region").GetString());
        Assert.Equal(JsonValueKind.Null, parameters.GetProperty("widgetState").GetProperty("privateContent").ValueKind);

        await update(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewWidgetStateUpdate,
                new { viewHandle = opened.ViewHandle.Value }),
            CancellationToken.None);
        var cleared = (await peer.ReadNextSentAsync()).RootElement.GetProperty("params");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("widgetState").ValueKind);
    }

    [Fact]
    public async Task Message_AppendsContextAsVisibleJsonAndRejectsOversizedContext()
    {
        var (sessions, handler, table, path) = await CreateHandlerAsync();
        using var _ = handler;
        var opened = await OpenAsync(table, path);
        Assert.True(table.TryGet(DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewMessage, out var message));

        await message(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewMessage,
                new { viewHandle = opened.ViewHandle.Value, prompt = "Explain the selection.", context = new { selected = "```north" } }),
            CancellationToken.None);
        var text = Assert.IsType<TextContent>(Assert.Single(sessions.LastSubmittedContent)).Text;
        var normalized = text.ReplaceLineEndings("\n");
        Assert.StartsWith("Explain the selection.\n\nCurrent visualization context (JSON):\n```json\n", normalized, StringComparison.Ordinal);
        Assert.EndsWith("\n```", normalized, StringComparison.Ordinal);
        Assert.Equal(2, text.Split("```").Length - 1);

        var tooLarge = await Assert.ThrowsAsync<AppServerException>(() => message(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewMessage,
                new { viewHandle = opened.ViewHandle.Value, prompt = "Explain.", context = new string('x', 32 * 1024) }),
            CancellationToken.None));
        Assert.Equal("context_too_large", Assert.IsType<AppServerErrorData>(tooLarge.ErrorData).Code);
    }

    private async Task<(TestableSessionService Sessions, VisualizationRequestHandler Handler, AppServerMethodTable Table, string Path)> CreateHandlerAsync(
        AppServerThreadSubscribers? subscribers = null)
    {
        Directory.CreateDirectory(_root);
        var thread = CreateThread();
        var sessions = new TestableSessionService(new ThreadStore(_root));
        await sessions.SeedThreadAsync(thread);
        var connection = CapableConnection();
        var transport = new InMemoryTransport();
        var assets = new VisualizationAssetStore(
            new DotCraftPaths(_root, Path.Combine(_root, ".craft"), userDataPath: null));
        var runtime = new VisualizationRuntimeRegistry(assets);
        Assert.True(runtime.BindThread(thread, transport, connection));
        var directory = runtime.TryGetAuthoringDirectory(thread.Id, out var authoringDirectory)
            ? authoringDirectory
            : throw new InvalidOperationException("The authoring directory was not bound.");
        var fileTools = new FileTools(_root, requireApprovalOutsideWorkspace: false);
        var path = Path.Combine(directory, "chart.html");
        var writeResult = await fileTools.WriteFile(path, "<div>chart</div>");
        Assert.True(writeResult.Success, writeResult.Error?.Message);

        Assert.True(connection.TryAddSubscription(thread.Id, new CancellationTokenSource()));
        subscribers?.Register(transport, connection);
        var handler = new VisualizationRequestHandler(sessions, connection, assets, runtime, subscribers);
        var table = new AppServerMethodTable();
        handler.RegisterMethods(table);
        return (sessions, handler, table, path);
    }

    private static async Task<Contract.VisualizationViewOpenResult> OpenAsync(AppServerMethodTable table, string path)
    {
        Assert.True(table.TryGet(DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewOpen, out var open));
        return Assert.IsType<Contract.VisualizationViewOpenResult>(await open(
            InMemoryTransport.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.VisualizationViewOpen,
                new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", path }),
            CancellationToken.None));
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
                Text = $"visualize{JsonSerializer.Serialize(new { path = Path.Combine(_root, ".craft", "visualizations", "thread_test", "chart.html") })}"
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
            new ClientConnectionCapabilities { Visualizations = true }));
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
