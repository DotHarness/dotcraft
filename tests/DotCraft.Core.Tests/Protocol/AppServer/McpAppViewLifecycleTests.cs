using DotCraft.Mcp;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpAppViewLifecycleTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"McpAppViewTests_{Guid.NewGuid():N}");

    [Fact]
    public async Task Eligibility_RequiresPersistedAssociationExactTurnAndCurrentAppRegistration()
    {
        await using var manager = await CreateConnectedManagerAsync();
        var generation = Assert.NotNull(await manager.GetGenerationAsync("review"));
        var registration = Registration(
            "review",
            "show",
            McpAppVisibility.Model | McpAppVisibility.App,
            resourceUri: "ui://review/show",
            generation);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 4);
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_002",
            Type = ItemType.McpToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new McpToolCallPayload
            {
                Namespace = registration.Definition.Name.Namespace,
                ToolName = registration.Definition.Name.Name,
                ProviderFlatName = "mcp__review__show",
                ToolDefinitionId = registration.Definition.Id.ToString(),
                RuntimeBindingId = registration.Binding.Id.Value,
                BindingRevision = registration.Binding.Revision,
                SnapshotRevision = snapshot.Revision,
                McpGeneration = generation,
                Server = "review",
                SourceToolId = "show",
                CallId = "call-1",
                Status = "completed",
                Success = true,
                McpAppResourceUri = "ui://review/show"
            }
        };

        var eligible = await McpAppEligibilityResolver.ResolveAsync(
            "thread-1",
            "turn_002",
            item,
            new SnapshotSource(snapshot),
            new McpRuntimeService(manager),
            CancellationToken.None);
        var wrongTurn = await McpAppEligibilityResolver.ResolveAsync(
            "thread-1",
            "turn_001",
            item,
            new SnapshotSource(snapshot),
            new McpRuntimeService(manager),
            CancellationToken.None);

        Assert.NotNull(eligible);
        Assert.Equal("ui://review/show", eligible.AppMetadata.ResourceUri?.AbsoluteUri.TrimEnd('/'));
        Assert.Null(wrongTurn);
    }

    [Fact]
    public async Task ThreadProjection_DerivesHistoricalAvailabilityFromCurrentRuntime()
    {
        Directory.CreateDirectory(_tempDir);
        await using var manager = await CreateConnectedManagerAsync();
        var generation = Assert.NotNull(await manager.GetGenerationAsync("review"));
        var registration = Registration(
            "review",
            "show",
            McpAppVisibility.Model | McpAppVisibility.App,
            resourceUri: "ui://review/show",
            generation);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 4);
        var now = DateTimeOffset.UtcNow;
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.McpToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new McpToolCallPayload
            {
                Namespace = registration.Definition.Name.Namespace,
                ToolName = registration.Definition.Name.Name,
                ProviderFlatName = "mcp__review__show",
                ToolDefinitionId = registration.Definition.Id.ToString(),
                RuntimeBindingId = registration.Binding.Id.Value,
                BindingRevision = registration.Binding.Revision,
                SnapshotRevision = snapshot.Revision,
                McpGeneration = generation,
                Server = "review",
                SourceToolId = "show",
                CallId = "call-1",
                Status = "completed",
                Success = true,
                McpAppResourceUri = "ui://review/show"
            }
        };
        var turn = new SessionTurn
        {
            Id = item.TurnId,
            ThreadId = "thread-1",
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now,
            Items = [item]
        };
        var thread = new SessionThread
        {
            Id = "thread-1",
            WorkspacePath = _tempDir,
            CreatedAt = now,
            LastActiveAt = now,
            Turns = [turn]
        };
        var store = new ThreadStore(_tempDir);
        await store.SaveThreadAsync(thread);
        var sessions = new TestableSessionService(store);
        var projector = new AppServerThreadWireProjector(
            sessions,
            CreateMcpAppConnection(),
            appConfigMonitor: null,
            new WorkspaceConfigEditor(null, null),
            skillsLoader: null,
            planStore: null,
            appBindingService: null,
            originPresentationProviders: null,
            builtInPluginSourceRoots: null,
            new SnapshotSource(snapshot),
            new McpRuntimeService(manager));

        var projected = await projector.ProjectAsync(
            thread,
            includeTurns: true,
            filterToolExecutions: false,
            CancellationToken.None);

        var projectedItem = Assert.Single(Assert.Single(projected.Turns!).Items!);
        Assert.True(projectedItem.McpApp?.Available);
    }

    [Fact]
    public async Task SnapshotChange_ActivelyRevokesView_NotifiesClient_AndClearsContext()
    {
        Directory.CreateDirectory(_tempDir);
        var sessions = new TestableSessionService(new ThreadStore(_tempDir));
        var connection = CreateMcpAppConnection();
        await using var transport = new InMemoryTransport();
        var snapshots = new SnapshotSource();
        var contexts = new McpAppTransientContextStore();
        var views = new McpAppViewRegistry();
        await using var manager = new McpClientManager();
        using var handler = new McpAppRequestHandler(
            sessions,
            connection,
            transport,
            dispatcher: null,
            snapshots,
            mcpRuntime: null,
            contexts,
            views);
        var view = AddView(views, manager, snapshotRevision: 4);
        contexts.Set(view.Handle, view.ThreadId, [new TextContent("pending")]);

        snapshots.Publish(view.ThreadId, revision: 5);

        using var notification = await transport.ReadNextSentAsync();
        Assert.Equal(AppServerMethods.McpAppViewStatusUpdated, notification.RootElement.GetProperty("method").GetString());
        var parameters = notification.RootElement.GetProperty("params");
        Assert.Equal(view.Handle, parameters.GetProperty("viewHandle").GetString());
        Assert.Equal("revoked", parameters.GetProperty("status").GetString());
        Assert.Equal("snapshot_changed", parameters.GetProperty("code").GetString());
        var stale = Assert.Throws<AppServerException>(() => views.Get(view.Handle));
        Assert.Equal("stale", Assert.IsType<AppServerErrorData>(stale.ErrorData).Code);
        Assert.Empty(contexts.TakeForThread(view.ThreadId));
    }

    [Fact]
    public async Task ConnectionClose_DisposesViews_ClearsContext_AndUnsubscribesSnapshotListener()
    {
        Directory.CreateDirectory(_tempDir);
        var sessions = new TestableSessionService(new ThreadStore(_tempDir));
        var connection = CreateMcpAppConnection();
        await using var transport = new InMemoryTransport();
        var snapshots = new SnapshotSource();
        var contexts = new McpAppTransientContextStore();
        var views = new McpAppViewRegistry();
        await using var manager = new McpClientManager();
        using var handler = new McpAppRequestHandler(
            sessions,
            connection,
            transport,
            dispatcher: null,
            snapshots,
            mcpRuntime: null,
            contexts,
            views);
        var view = AddView(views, manager, snapshotRevision: 4);
        contexts.Set(view.Handle, view.ThreadId, [new TextContent("pending")]);

        connection.MarkClosed();
        await WaitUntilAsync(() => IsViewClosed(views, view.Handle));
        snapshots.Publish(view.ThreadId, revision: 5);

        Assert.Throws<AppServerException>(() => views.Get(view.Handle));
        Assert.Empty(contexts.TakeForThread(view.ThreadId));
        Assert.Null(transport.TryReadSent());
    }

    [Fact]
    public async Task ViewProtocol_EnforcesServerVisibility_AndCapturesMessageContextOnce()
    {
        Directory.CreateDirectory(_tempDir);
        var sessions = new TestableSessionService(new ThreadStore(_tempDir));
        await sessions.CreateThreadAsync(
            new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = _tempDir },
            threadId: "thread-1");
        var connection = CreateMcpAppConnection();
        await using var transport = new InMemoryTransport();
        await using var manager = await CreateConnectedManagerAsync();
        var generation = Assert.NotNull(await manager.GetGenerationAsync("review"));
        var source = Registration("review", "show", McpAppVisibility.App, resourceUri: "ui://review/show", generation);
        var appTool = Registration("review", "comment", McpAppVisibility.App, resourceUri: null, generation);
        var modelOnly = Registration("review", "private", McpAppVisibility.Model, resourceUri: null, generation);
        var otherServer = Registration("other", "foreign", McpAppVisibility.App, resourceUri: null, generation);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([source, appTool, modelOnly, otherServer], revision: 4);
        var snapshots = new SnapshotSource(snapshot);
        var contexts = new McpAppTransientContextStore();
        var views = new McpAppViewRegistry();
        using var handler = new McpAppRequestHandler(
            sessions,
            connection,
            transport,
            new DispatchService(),
            snapshots,
            new McpRuntimeService(manager),
            contexts,
            views);
        var view = views.Add(new McpAppViewState
        {
            Handle = $"view_{Guid.NewGuid():N}",
            ThreadId = "thread-1",
            TurnId = "turn-1",
            SourceItemId = "item-1",
            ServerName = "review",
            Origin = "workspace",
            Generation = generation,
            ToolName = source.Definition.Name,
            DefinitionId = source.Definition.Id,
            RuntimeBindingId = source.Binding.Id,
            SnapshotRevision = snapshot.Revision,
            BindingRevision = source.Binding.Revision,
            RawSourceToolId = "show",
            ResourceUri = new Uri("ui://review/show"),
            Manager = manager
        });
        var table = new AppServerMethodTable();
        handler.RegisterMethods(table);

        Assert.True(table.TryGet(AppServerMethods.McpAppViewToolsList, out var list));
        var listed = Assert.IsType<McpAppViewToolsListResult>(await list(
            InMemoryTransport.BuildRequest(AppServerMethods.McpAppViewToolsList, new { viewHandle = view.Handle }),
            CancellationToken.None));
        Assert.Equal(["comment", "show"], listed.Tools.Select(static tool => tool.Name));

        Assert.True(table.TryGet(AppServerMethods.McpAppViewToolCall, out var call));
        var error = await Assert.ThrowsAsync<AppServerException>(() => call(
            InMemoryTransport.BuildRequest(
                AppServerMethods.McpAppViewToolCall,
                new { viewHandle = view.Handle, tool = "foreign", arguments = new { } }),
            CancellationToken.None));
        Assert.Equal("unauthorized", Assert.IsType<AppServerErrorData>(error.ErrorData).Code);

        Assert.True(table.TryGet(AppServerMethods.McpAppViewModelContextUpdate, out var updateContext));
        _ = await updateContext(
            InMemoryTransport.BuildRequest(
                AppServerMethods.McpAppViewModelContextUpdate,
                new
                {
                    viewHandle = view.Handle,
                    content = new[] { new { type = "text", text = "context" } }
                }),
            CancellationToken.None);
        Assert.True(table.TryGet(AppServerMethods.McpAppViewMessage, out var message));
        var messageResult = Assert.IsType<McpAppViewMessageResult>(await message(
            InMemoryTransport.BuildRequest(
                AppServerMethods.McpAppViewMessage,
                new
                {
                    viewHandle = view.Handle,
                    role = "user",
                    content = new { type = "text", text = "hello" }
                }),
            CancellationToken.None));
        Assert.Equal(
            "context",
            Assert.IsType<TextContent>(Assert.Single(contexts.TakeForQueuedInput(messageResult.QueuedInputId))).Text);
        Assert.Empty(contexts.TakeForQueuedInput(messageResult.QueuedInputId));
        Assert.Equal("hello", Assert.IsType<TextContent>(Assert.Single(sessions.LastSubmittedContent)).Text);
    }

    private static AppServerConnection CreateMcpAppConnection()
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new AppServerClientInfo { Name = "test", Version = "1" },
            new AppServerClientCapabilities { McpApps = true }));
        connection.MarkClientReady();
        return connection;
    }

    private static McpAppViewState AddView(McpAppViewRegistry views, McpClientManager manager, long snapshotRevision) =>
        views.Add(new McpAppViewState
        {
            Handle = $"view_{Guid.NewGuid():N}",
            ThreadId = "thread-1",
            TurnId = "turn-1",
            SourceItemId = "item-1",
            ServerName = "review",
            Origin = "workspace",
            Generation = 1,
            ToolName = new ToolName("review", "show"),
            DefinitionId = new ToolDefinitionId(ToolSourceKind.Mcp, "review", new SourceToolId("show")),
            RuntimeBindingId = new RuntimeBindingId("binding-1"),
            SnapshotRevision = snapshotRevision,
            BindingRevision = 1,
            RawSourceToolId = "show",
            ResourceUri = new Uri("ui://review/show"),
            Manager = manager
        });

    private static async Task<McpClientManager> CreateConnectedManagerAsync()
    {
        var manager = new McpClientManager((_, _) => Task.FromResult(
            new McpConnectionResult(new FakeClient(), [new FakeFunction("show")])));
        await manager.ConnectAsync(
        [
            new McpServerConfig
            {
                Name = "review",
                Enabled = true,
                Transport = "streamableHttp",
                Url = "https://example.test/mcp"
            }
        ]);
        await manager.WaitForStartupCompletionAsync();
        return manager;
    }

    private static ToolRegistration Registration(
        string server,
        string name,
        McpAppVisibility visibility,
        string? resourceUri,
        long generation)
    {
        var sourceToolId = new SourceToolId(name);
        var definitionId = new ToolDefinitionId(ToolSourceKind.Mcp, server, sourceToolId);
        var metadata = new McpAppToolMetadata(
            resourceUri is null ? null : new Uri(resourceUri),
            visibility);
        var definition = new ToolDefinition(
            definitionId,
            McpToolNaming.CanonicalToolName(server, name),
            name,
            JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
            annotations: new Dictionary<string, JsonElement>
            {
                [McpAppMetadataParser.ToolAnnotationKey] = McpAppMetadataParser.ToDefinitionAnnotation(metadata)
            },
            provenance: new ToolProvenance(ToolSourceKind.Mcp, server));
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"mcp:{server}:{name}:{generation}"),
            definitionId,
            new FakeRuntime(),
            ToolBindingLeases.AlwaysAvailable,
            $"mcp:{server}:{generation}",
            generation);
        var audiences = ToolInvocationAudience.Host;
        if (visibility.HasFlag(McpAppVisibility.App))
            audiences |= ToolInvocationAudience.App;
        if (visibility.HasFlag(McpAppVisibility.Model))
            audiences |= ToolInvocationAudience.Model;
        return new ToolRegistration(
            definition,
            binding,
            ToolProjectionShape.McpLifecycle,
            ToolExposure.Direct,
            audiences);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Condition was not met before the deadline.");
            await Task.Delay(10);
        }
    }

    private static bool IsViewClosed(McpAppViewRegistry views, string handle)
    {
        try
        {
            _ = views.Get(handle);
            return false;
        }
        catch (AppServerException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for Windows handles released after test disposal.
        }
    }

    private sealed class SnapshotSource(EffectiveToolSnapshot? snapshot = null) : IThreadToolSnapshotService, IThreadToolSnapshotChangeSource
    {
        public event EventHandler<EffectiveToolSnapshotChangedEventArgs>? EffectiveToolSnapshotChanged;

        public Task<EffectiveToolSnapshot> GetEffectiveToolSnapshotAsync(
            string threadId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot ?? throw new NotSupportedException());

        public void Publish(string threadId, long revision) =>
            EffectiveToolSnapshotChanged?.Invoke(this, new EffectiveToolSnapshotChangedEventArgs(threadId, revision));
    }

    private sealed class McpRuntimeService(McpClientManager manager) : IThreadMcpRuntimeService
    {
        public Task<McpClientManager?> GetEffectiveMcpRuntimeAsync(
            string threadId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<McpClientManager?>(manager);
    }

    private sealed class DispatchService : IThreadToolDispatchService
    {
        public Task<ToolExecutionResult> DispatchThreadToolAsync(
            string threadId,
            ToolName toolName,
            JsonObject arguments,
            string callId,
            ToolInvocationAudience audience = ToolInvocationAudience.Host,
            CancellationToken cancellationToken = default,
            ToolInvocationOrigin? origin = null) =>
            Task.FromResult(ToolExecutionResult.Succeeded("ok"));
    }

    private sealed class FakeRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }

    private sealed class FakeClient : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFunction(string name) : AIFunction
    {
        public override string Name { get; } = name;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<object?>("ok");
    }
}
