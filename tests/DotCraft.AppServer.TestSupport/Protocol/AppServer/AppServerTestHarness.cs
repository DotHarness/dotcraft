using System.Text.Json;
using DotCraft.AppBinding;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Commands.Core;
using DotCraft.Commands.Custom;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Hooks;
using DotCraft.Mcp;
using DotCraft.Modules;
using DotCraft.Skills;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using ApprovalRequestPayload = DotCraft.Sessions.ApprovalRequestPayload;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Per-test fixture wiring together <see cref="InMemoryTransport"/>,
/// <see cref="TestableSessionService"/>, <see cref="AppServerConnection"/>,
/// and <see cref="AppServerRequestHandler"/>.
///
/// Use <see cref="InitializeAsync"/> to complete the JSON-RPC handshake so that
/// <c>IsClientReady = true</c> before exercising thread/turn methods.
/// </summary>
public sealed class AppServerTestHarness : IDisposable
{
    private readonly string _tempDir;

    public InMemoryTransport Transport { get; }
    public TestableSessionService Service { get; }
    public AppServerConnection Connection { get; }
    public AppServerRequestHandler Handler { get; }
    public IAppConfigMonitor Monitor { get; }

    /// <summary>
    /// Default <see cref="SessionIdentity"/> using the harness temp workspace.
    /// </summary>
    public SessionIdentity Identity { get; }

    private int _requestIdCounter;

    public AppServerTestHarness(
        SessionApprovalDecision defaultApprovalDecision = SessionApprovalDecision.AcceptOnce,
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
        string? workspaceCraftPath = null,
        Func<ExternalChannelEntry, CancellationToken, Task>? onExternalChannelUpserted = null,
        Func<string, CancellationToken, Task>? onExternalChannelRemoved = null,
        IReadOnlyList<ConfigSchemaSection>? configSchema = null,
        IAppConfigMonitor? appConfigMonitor = null,
        SkillsLoader? skillsLoader = null,
        MemoryStore? memoryStore = null,
        DreamStore? dreamStore = null,
        McpClientManager? mcpClientManager = null,
        IWelcomeSuggestionService? welcomeSuggestionService = null,
        WireNodeReplProxy? wireNodeReplProxy = null,
        WireDynamicToolProxy? wireDynamicToolProxy = null,
        IBackgroundTerminalService? backgroundTerminalService = null,
        IAppServerChannelListContributor? channelListContributor = null,
        DreamsService? dreamsService = null,
        AppBindingService? appBindingService = null,
        PlanStore? planStore = null,
        IContextPageManager? contextPageManager = null,
        OpenAIClientProvider? openAIClientProvider = null,
        TraceStore? traceStore = null,
        IReadOnlyList<string>? builtInPluginSourceRoots = null,
        WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider = null,
        Func<SessionThread, SubAgentCoordinator?>? subAgentCoordinatorFactory = null,
        HookRunner? hookRunner = null,
        IReadOnlyList<IThreadOriginPresentationProvider>? threadOriginPresentationProviders = null,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "AppServerTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        var store = new ThreadStore(_tempDir);
        Service = new TestableSessionService(store);
        memoryStore ??= new MemoryStore(_tempDir);
        planStore ??= new PlanStore(_tempDir);
        Transport = new InMemoryTransport();
        Connection = new AppServerConnection();
        var defaultConfig = AppConfigTestFactory.CreateOpenAI();
        defaultConfig.GlobalConfigPath = Path.Combine(_tempDir, "user-data", "config.json");
        var providerRegistry = new ModelProviderRegistry(
            [openAIClientProvider ?? new OpenAIClientProvider(), new AnthropicClientProvider()]);
        Monitor = appConfigMonitor ?? new AppConfigMonitor(defaultConfig);
        Monitor.Current.GlobalConfigPath ??= Path.Combine(_tempDir, "user-data", "config.json");
        Handler = new AppServerRequestHandler(
            Service, Connection, Transport,
            channelListContributor ?? new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                DefaultApprovalDecision = defaultApprovalDecision,
                CommandRegistry = CommandRegistry.CreateDefault(
                    string.IsNullOrWhiteSpace(workspaceCraftPath)
                        ? ".agents"
                        : Path.GetFileName(workspaceCraftPath),
                    string.IsNullOrWhiteSpace(workspaceCraftPath)
                        ? null
                        : new CustomCommandLoader(workspaceCraftPath)),
                WorkspaceCraftPath = workspaceCraftPath,
                HostWorkspacePath = _tempDir,
                MemoryStore = memoryStore,
                ProtocolExtensions = protocolExtensions,
                WelcomeSuggestionService = welcomeSuggestionService,
                OnExternalChannelUpserted = onExternalChannelUpserted,
                OnExternalChannelRemoved = onExternalChannelRemoved,
                ConfigSchema = configSchema,
                AppConfigMonitor = Monitor,
                SkillsLoader = skillsLoader,
                McpClientManager = mcpClientManager,
                WireNodeReplProxy = wireNodeReplProxy,
                WireDynamicToolProxy = wireDynamicToolProxy,
                BackgroundTerminalService = backgroundTerminalService,
                DreamStore = dreamStore,
                DreamsService = dreamsService,
                AppBindingService = appBindingService,
                ThreadOriginPresentationProviders = threadOriginPresentationProviders,
                PlanStore = planStore,
                ContextPageManager = contextPageManager,
                ModelProviderRegistry = providerRegistry,
                ChatClientRegistry = new ChatClientRegistry(providerRegistry),
                LoggerFactory = loggerFactory,
                TraceStore = traceStore,
                BuiltInPluginSourceRoots = builtInPluginSourceRoots,
                WireRuntimeAdditionalContextProvider = wireRuntimeAdditionalContextProvider,
                SubAgentCoordinatorFactory = subAgentCoordinatorFactory,
                HookRunner = hookRunner,
            });

        Identity = new SessionIdentity
        {
            ChannelName = "appserver",
            UserId = "test_user",
            WorkspacePath = _tempDir
        };
    }

    // -------------------------------------------------------------------------
    // Protocol helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Performs the full initialize → initialized handshake so that the connection
    /// reaches <see cref="AppServerConnection.IsClientReady"/> = true.
    /// Returns the parsed initialize response so tests can assert on server info.
    /// </summary>
    public async Task<JsonDocument> InitializeAsync(
        bool approvalSupport = true,
        bool streamingSupport = true,
        bool toolExecutionLifecycle = false,
        bool requestUserInputSupport = false,
        bool interactiveToolUi = false,
        bool backgroundTerminals = false,
        bool? configChange = null,
        List<string>? optOutMethods = null,
        bool nodeReplBrowserUse = false,
        List<string>? browserUseBackends = null)
    {
        var caps = new
        {
            approvalSupport,
            streamingSupport,
            toolExecutionLifecycle,
            requestUserInputSupport,
            interactiveToolUi,
            backgroundTerminals,
            configChange,
            optOutNotificationMethods = optOutMethods ?? [],
            nodeRepl = nodeReplBrowserUse
                ? new
                {
                    backend = "desktop-node"
                }
                : null,
            browserUse = nodeReplBrowserUse
                ? new
                {
                    backend = "desktop-iab",
                    backends = browserUseBackends,
                    protocolVersion = 2,
                    supportsCancel = true,
                    browserSessionProtocolVersion = 1,
                    supportsCommandCancel = true,
                    maxBrowserResultBytes = 1048576,
                    defaultCommandTimeoutMs = 15000,
                    maxCommandTimeoutMs = 120000,
                    supportsTypedFinalize = true,
                    supportsChromeDiagnostics = true
                }
                : null
        };
        var initMsg = BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" },
            capabilities = caps
        });

        var result = await Handler.HandleRequestAsync(initMsg, default);
        // Write the response to transport (simulating the host ProcessRequestAsync)
        if (result != null)
            await Transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(initMsg.Id, result));

        // Drain the initialize response so it doesn't interfere with subsequent assertions
        var initResponse = Transport.TryReadSent()!;

        // Send the initialized notification to complete the handshake
        Assert.True(Handler.HandleNotification(BuildNotification(Contract.AppServerRpc.Initialized.Name, new { })));

        return initResponse;
    }

    /// <summary>
    /// Executes a request through the full handler + ProcessRequestAsync pipeline.
    /// Mirrors <c>AppServerHost.ProcessRequestAsync</c> exactly: if the handler returns
    /// a non-null result, it is written to the transport; null means the response was
    /// already sent inline (thread/start, turn/start, etc.).
    /// </summary>
    public async Task ExecuteRequestAsync(
        AppServerIncomingMessage msg,
        CancellationToken ct = default)
    {
        object? result;
        try
        {
            result = await Handler.HandleRequestAsync(msg, ct);
        }
        catch (AppServerException ex)
        {
            await Transport.WriteMessageAsync(
                AppServerRequestHandler.BuildErrorResponse(msg.Id, ex.ToError()), ct);
            return;
        }
        catch (Exception ex)
        {
            var err = AppServerErrors.InternalError(ex.Message).ToError();
            await Transport.WriteMessageAsync(
                AppServerRequestHandler.BuildErrorResponse(msg.Id, err), ct);
            return;
        }

        if (result != null)
            await Transport.WriteMessageAsync(
                AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
    }

    /// <summary>
    /// Builds an <see cref="AppServerIncomingMessage"/> representing a JSON-RPC request.
    /// </summary>
    public AppServerIncomingMessage BuildRequest(string method, object? @params = null) =>
        InMemoryTransport.BuildRequest(method, @params, Interlocked.Increment(ref _requestIdCounter));

    /// <summary>
    /// Builds an <see cref="AppServerIncomingMessage"/> representing a JSON-RPC notification.
    /// </summary>
    public AppServerIncomingMessage BuildNotification(string method, object? @params = null) =>
        InMemoryTransport.BuildNotification(method, @params);

    // -------------------------------------------------------------------------
    // Session event factories
    // -------------------------------------------------------------------------

    public static SessionTurn MakeTurn(string threadId, string turnId = "turn_001") => new()
    {
        Id = turnId,
        ThreadId = threadId,
        Status = TurnStatus.Running,
        StartedAt = DateTimeOffset.UtcNow
    };

    public static SessionTurn MakeCompletedTurn(string threadId, string turnId = "turn_001") => new()
    {
        Id = turnId,
        ThreadId = threadId,
        Status = TurnStatus.Completed,
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow
    };

    public static SessionItem MakeAgentMessageItem(string turnId, string itemId = "item_001") => new()
    {
        Id = itemId,
        TurnId = turnId,
        Type = ItemType.AgentMessage,
        Status = ItemStatus.Started,
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = new AgentMessagePayload { Text = "Hello from agent." }
    };

    public static SessionItem MakeCompletedAgentMessageItem(string turnId, string itemId = "item_001") => new()
    {
        Id = itemId,
        TurnId = turnId,
        Type = ItemType.AgentMessage,
        Status = ItemStatus.Completed,
        CreatedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Payload = new AgentMessagePayload { Text = "Hello from agent." }
    };

    /// <summary>
    /// Builds a minimal turn event sequence for testing turn/start:
    /// TurnStarted → ItemStarted → ItemDelta (agentMessage) → ItemCompleted → TurnCompleted
    /// </summary>
    public static SessionEvent[] BuildTurnEventSequence(string threadId, string turnId = "turn_001")
    {
        var turn = MakeTurn(threadId, turnId);
        var completedTurn = MakeCompletedTurn(threadId, turnId);
        var item = MakeAgentMessageItem(turnId);
        var completedItem = MakeCompletedAgentMessageItem(turnId);
        var now = DateTimeOffset.UtcNow;

        return
        [
            new SessionEvent
            {
                EventId = "e1", EventType = SessionEventType.TurnStarted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = turn
            },
            new SessionEvent
            {
                EventId = "e2", EventType = SessionEventType.ItemStarted,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now, Payload = item
            },
            new SessionEvent
            {
                EventId = "e3", EventType = SessionEventType.ItemDelta,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now,
                Payload = new AgentMessageDelta { TextDelta = "Hello" }
            },
            new SessionEvent
            {
                EventId = "e4", EventType = SessionEventType.ItemCompleted,
                ThreadId = threadId, TurnId = turnId, ItemId = item.Id, Timestamp = now,
                Payload = completedItem
            },
            new SessionEvent
            {
                EventId = "e5", EventType = SessionEventType.TurnCompleted,
                ThreadId = threadId, TurnId = turnId, Timestamp = now, Payload = completedTurn
            }
        ];
    }

    /// <summary>
    /// Builds a turn event sequence containing streaming tool-call argument deltas.
    /// </summary>
    public static SessionEvent[] BuildStreamingToolCallEventSequence(string threadId, string turnId = "turn_001")
    {
        var turn = MakeTurn(threadId, turnId);
        var completedTurn = MakeCompletedTurn(threadId, turnId);
        var toolCallItem = new SessionItem
        {
            Id = "item_tool_001",
            TurnId = turnId,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Streaming,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "WriteFile",
                CallId = "call_001"
            }
        };
        var completedToolCall = new SessionItem
        {
            Id = "item_tool_001",
            TurnId = turnId,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Completed,
            CreatedAt = toolCallItem.CreatedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ToolCallPayload
            {
                ToolName = "WriteFile",
                CallId = "call_001"
            }
        };
        var now = DateTimeOffset.UtcNow;

        return
        [
            new SessionEvent
            {
                EventId = "e1",
                EventType = SessionEventType.TurnStarted,
                ThreadId = threadId,
                TurnId = turnId,
                Timestamp = now,
                Payload = turn
            },
            new SessionEvent
            {
                EventId = "e2",
                EventType = SessionEventType.ItemStarted,
                ThreadId = threadId,
                TurnId = turnId,
                ItemId = toolCallItem.Id,
                Timestamp = now,
                Payload = toolCallItem
            },
            new SessionEvent
            {
                EventId = "e3",
                EventType = SessionEventType.ItemDelta,
                ThreadId = threadId,
                TurnId = turnId,
                ItemId = toolCallItem.Id,
                Timestamp = now,
                Payload = new ToolCallArgumentsDelta
                {
                    ToolName = "WriteFile",
                    CallId = "call_001",
                    Delta = "{\"path\":\"foo.txt\",\"content\":\"hel"
                }
            },
            new SessionEvent
            {
                EventId = "e4",
                EventType = SessionEventType.ItemDelta,
                ThreadId = threadId,
                TurnId = turnId,
                ItemId = toolCallItem.Id,
                Timestamp = now,
                Payload = new ToolCallArgumentsDelta
                {
                    Delta = "lo\"}"
                }
            },
            new SessionEvent
            {
                EventId = "e5",
                EventType = SessionEventType.ItemCompleted,
                ThreadId = threadId,
                TurnId = turnId,
                ItemId = completedToolCall.Id,
                Timestamp = now,
                Payload = completedToolCall
            },
            new SessionEvent
            {
                EventId = "e6",
                EventType = SessionEventType.TurnCompleted,
                ThreadId = threadId,
                TurnId = turnId,
                Timestamp = now,
                Payload = completedTurn
            }
        ];
    }

    public static SessionEvent[] BuildApprovalEventSequence(
        string threadId, string turnId = "turn_001", string requestId = "req_001")
    {
        var turn = MakeTurn(threadId, turnId);
        var approvalItem = new SessionItem
        {
            Id = "item_001", TurnId = turnId,
            Type = ItemType.ApprovalRequest, Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new ApprovalRequestPayload
            {
                RequestId = requestId,
                ApprovalType = "shell",
                Operation = "exec",
                Target = "echo hello",
                ScopeKey = "scope1",
                Reason = "test approval",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            }
        };
        var resolvedItem = new SessionItem
        {
            Id = "item_001", TurnId = turnId,
            Type = ItemType.ApprovalRequest, Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
            Payload = approvalItem.Payload
        };
        var completedTurn = MakeCompletedTurn(threadId, turnId);

        return
        [
            new SessionEvent
            {
                EventId = "e1", EventType = SessionEventType.TurnStarted,
                ThreadId = threadId, TurnId = turnId, Timestamp = DateTimeOffset.UtcNow,
                Payload = MakeTurn(threadId, turnId)
            },
            new SessionEvent
            {
                EventId = "e2", EventType = SessionEventType.ApprovalRequested,
                ThreadId = threadId, TurnId = turnId, ItemId = "item_001",
                Timestamp = DateTimeOffset.UtcNow, Payload = approvalItem
            },
            new SessionEvent
            {
                EventId = "e3", EventType = SessionEventType.ApprovalResolved,
                ThreadId = threadId, TurnId = turnId, ItemId = "item_001",
                Timestamp = DateTimeOffset.UtcNow, Payload = resolvedItem
            },
            new SessionEvent
            {
                EventId = "e4", EventType = SessionEventType.TurnCompleted,
                ThreadId = threadId, TurnId = turnId, Timestamp = DateTimeOffset.UtcNow,
                Payload = completedTurn
            }
        ];
    }

    // -------------------------------------------------------------------------
    // Assert helpers
    // -------------------------------------------------------------------------

    public static void AssertIsSuccessResponse(JsonDocument doc)
    {
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.True(
            root.TryGetProperty("result", out _),
            $"Expected 'result' property in success response. Response: {root.GetRawText()}");
        Assert.False(
            root.TryGetProperty("error", out _),
            $"Unexpected 'error' in success response. Response: {root.GetRawText()}");
    }

    public static void AssertIsErrorResponse(JsonDocument doc, int expectedCode)
    {
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.True(root.TryGetProperty("error", out var errorEl), "Expected 'error' property in error response");
        Assert.Equal(expectedCode, errorEl.GetProperty("code").GetInt32());
    }

    public static void AssertIsNotification(JsonDocument doc, string expectedMethod)
    {
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(expectedMethod, root.GetProperty("method").GetString());
        Assert.False(root.TryGetProperty("id", out _), "Notifications must not have an 'id'");
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }
}
