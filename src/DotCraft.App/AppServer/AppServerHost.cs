using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppBinding;
using Microsoft.Extensions.Logging;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.Text;
using DotCraft.Logging;
using DotCraft.Hosting;
using DotCraft.Hooks;
using DotCraft.InlineVisualizations;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Mcp;
using DotCraft.Modules;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Automations.Protocol;
using DotCraft.Tracing;
using DotCraft.ExternalChannel;
using Contract = DotCraft.Protocol.AppServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadGoal = DotCraft.Sessions.ThreadGoal;
using ThreadGoalSnapshot = DotCraft.Sessions.ThreadGoalSnapshot;

namespace DotCraft.AppServer;

/// <summary>
/// Host for AppServer mode.
/// Runs a stdio JSON-RPC 2.0 server that exposes <see cref="ISessionService"/> over the
/// Session Wire Protocol. When <see cref="WebSocketServerConfig"/> is present in configuration,
/// additionally starts a WebSocket listener that accepts multiple concurrent connections,
/// each with an isolated <see cref="AppServerConnection"/> sharing the same session service.
/// </summary>
public sealed class AppServerHost(
    WorkspaceRuntime runtime,
    ExternalChannelRegistry? externalChannelRegistry = null) : IDotCraftHost
{
    private readonly IServiceProvider _services = runtime.Services;

    /// <summary>
    /// Thread-safe set of currently connected transports. Used to broadcast
    /// out-of-band notifications (e.g. <c>plan/updated</c>) to all clients.
    /// </summary>
    private readonly ConcurrentDictionary<IAppServerTransport, AppServerConnection> _activeTransports = new();
    private readonly ConcurrentDictionary<IAppServerTransport, Lazy<OrderedAppServerNotificationQueue>> _terminalNotificationQueues = new();
    private readonly ConcurrentDictionary<string, RuntimeFacts> _threadRuntime = new(StringComparer.Ordinal);

    private readonly record struct RuntimeFacts(
        int PendingApprovals,
        int PendingUserInputs,
        bool Running,
        bool WaitingOnPlanConfirmation,
        string? MaintenanceKind)
    {
        public Contract.ThreadRuntimeState ToContract() => new()
        {
            Running = Running,
            WaitingOnApproval = PendingApprovals > 0,
            WaitingOnInput = PendingUserInputs > 0,
            WaitingOnPlanConfirmation = WaitingOnPlanConfirmation,
            MaintenanceKind = MaintenanceKind is null
                ? default
                : DotCraft.Protocol.Optional<string?>.FromValue(MaintenanceKind),
            Busy = Running || PendingApprovals > 0 || PendingUserInputs > 0 || MaintenanceKind != null
        };
    }

    private IReadOnlyList<IAppServerProtocolExtension> ProtocolExtensions =>
        runtime.Services.GetServices<IAppServerProtocolExtension>().ToArray();

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var appServerConfig = runtime.Config.GetSection<AppServerConfig>("AppServer");
        if (!AppServerWorkspaceLock.TryAcquire(runtime.Paths, out var workspaceLock, out var existingLock))
        {
            var owner = existingLock is null
                ? "another live process"
                : $"pid {existingLock.Pid}";
            throw new InvalidOperationException(
                $"DotCraft AppServer workspace lock is already held by {owner}: {AppServerWorkspaceLock.GetLockFilePath(runtime.Paths.CraftPath)}");
        }

        if (workspaceLock is null)
            throw new InvalidOperationException("AppServer workspace lock acquisition returned no lock file.");

        if (existingLock is not null && !existingLock.IsOwnerProcessAlive())
            AnsiConsole.MarkupLine("[grey][[AppServer]][/] Recovered stale appserver.lock");

        workspaceLock.Publish(CreateLockInfo(appServerConfig));

        var moduleRegistry = runtime.Services.GetRequiredService<ModuleRegistry>();

        try
        {
            await runtime.StartAsync(moduleRegistry, cancellationToken);
            SubscribeRuntimeEvents();

            try
            {
                switch (appServerConfig.Mode)
                {
                    case AppServerMode.WebSocket:
                        // -------------------------------------------------------------------
                        // Pure WebSocket mode: no stdio transport; the WebSocket server is
                        // the main loop. Stdout remains available for normal console output.
                        // -------------------------------------------------------------------
                        await RunWebSocketOnlyAsync(appServerConfig.WebSocket, cancellationToken);
                        break;

                    case AppServerMode.StdioAndWebSocket:
                        // -------------------------------------------------------------------
                        // Dual mode: stdio main loop + WebSocket listener running in parallel.
                        // -------------------------------------------------------------------
                        await RunStdioWithWebSocketAsync(appServerConfig.WebSocket, cancellationToken);
                        break;

                    default:
                        // -------------------------------------------------------------------
                        // Stdio-only mode (default): standard subprocess JSON-RPC over stdio.
                        // -------------------------------------------------------------------
                        await RunStdioOnlyAsync(cancellationToken);
                        break;
                }
            }
            finally
            {
                UnsubscribeRuntimeEvents();
                await runtime.StopAsync(CancellationToken.None);
            }
        }
        finally
        {
            workspaceLock.DeleteAfterDispose();
        }

        AnsiConsole.MarkupLine("[grey][[AppServer]][/] AppServer stopped");
    }

    private AppServerLockInfo CreateLockInfo(AppServerConfig appServerConfig)
        => new(
            Pid: Environment.ProcessId,
            WorkspacePath: runtime.Paths.WorkspacePath,
            ManagedByHub: ManagedAppServerEnvironment.IsManaged,
            HubApiBaseUrl: Environment.GetEnvironmentVariable(ManagedAppServerEnvironment.HubApiBaseUrl),
            StartedAt: DateTimeOffset.UtcNow,
            Version: AppVersion.Informational,
            Endpoints: BuildEndpointDictionary(appServerConfig));

    private IReadOnlyDictionary<string, string> BuildEndpointDictionary(AppServerConfig appServerConfig)
    {
        var endpoints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (appServerConfig.Mode is AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket)
        {
            endpoints["appServerWebSocket"] =
                BuildWebSocketEndpoint(appServerConfig.WebSocket.Host, appServerConfig.WebSocket.Port, appServerConfig.WebSocket.Token);
        }

        if (runtime.Config.DashBoard.Enabled && runtime.Config.Tracing.Enabled)
        {
            endpoints["dashboard"] =
                $"http://{runtime.Config.DashBoard.Host}:{runtime.Config.DashBoard.Port}/dashboard";
        }

        return endpoints;
    }

    private static string BuildWebSocketEndpoint(string host, int port, string? token)
    {
        var url = $"ws://{host}:{port}/ws";
        return string.IsNullOrEmpty(token) ? url : $"{url}?token={Uri.EscapeDataString(token)}";
    }

    private void SubscribeRuntimeEvents()
    {
        runtime.WorkspaceConfigChanged += BroadcastWorkspaceConfigChanged;
        runtime.McpStatusChanged += OnRuntimeMcpStatusChanged;
        runtime.PlanUpdated += BroadcastPlanUpdated;
        runtime.ThreadStarted += BroadcastThreadStarted;
        runtime.ThreadRenamed += BroadcastThreadRenamed;
        runtime.ThreadUpdated += BroadcastThreadUpdated;
        runtime.ThreadDeleted += BroadcastThreadDeleted;
        runtime.ThreadStatusChanged += BroadcastThreadStatusChanged;
        runtime.ThreadRuntimeSignal += OnThreadRuntimeSignal;
        runtime.ThreadGoalUpdated += BroadcastThreadGoalUpdated;
        runtime.ThreadGoalCleared += BroadcastThreadGoalCleared;
        runtime.SubAgentGraphChanged += BroadcastSubAgentGraphChanged;
        runtime.CronStateChanged += OnCronStateChanged;
        runtime.BackgroundJobResultProduced += OnBackgroundJobResultProduced;
        runtime.AutomationTaskUpdated += BroadcastAutomationTaskUpdated;
        if (_services.GetService<IBackgroundTerminalService>() is { } terminals)
            terminals.TerminalEvent += BroadcastBackgroundTerminalEvent;
        if (_services.GetService<DotCraft.Auth.OpenAI.IOpenAIUsageService>() is { } usage)
            usage.SnapshotChanged += BroadcastOpenAiUsageChanged;
        if (_services.GetService<AppBindingCoordinator>() is { } bindings)
            bindings.BindingStatusChanged += BroadcastAppBindingStatusChanged;
    }

    private void UnsubscribeRuntimeEvents()
    {
        runtime.WorkspaceConfigChanged -= BroadcastWorkspaceConfigChanged;
        runtime.McpStatusChanged -= OnRuntimeMcpStatusChanged;
        runtime.PlanUpdated -= BroadcastPlanUpdated;
        runtime.ThreadStarted -= BroadcastThreadStarted;
        runtime.ThreadRenamed -= BroadcastThreadRenamed;
        runtime.ThreadUpdated -= BroadcastThreadUpdated;
        runtime.ThreadDeleted -= BroadcastThreadDeleted;
        runtime.ThreadStatusChanged -= BroadcastThreadStatusChanged;
        runtime.ThreadRuntimeSignal -= OnThreadRuntimeSignal;
        runtime.ThreadGoalUpdated -= BroadcastThreadGoalUpdated;
        runtime.ThreadGoalCleared -= BroadcastThreadGoalCleared;
        runtime.SubAgentGraphChanged -= BroadcastSubAgentGraphChanged;
        runtime.CronStateChanged -= OnCronStateChanged;
        runtime.BackgroundJobResultProduced -= OnBackgroundJobResultProduced;
        runtime.AutomationTaskUpdated -= BroadcastAutomationTaskUpdated;
        if (_services.GetService<IBackgroundTerminalService>() is { } terminals)
            terminals.TerminalEvent -= BroadcastBackgroundTerminalEvent;
        if (_services.GetService<DotCraft.Auth.OpenAI.IOpenAIUsageService>() is { } usage)
            usage.SnapshotChanged -= BroadcastOpenAiUsageChanged;
        if (_services.GetService<AppBindingCoordinator>() is { } bindings)
            bindings.BindingStatusChanged -= BroadcastAppBindingStatusChanged;
    }

    private AppServerRequestHandler CreateRequestHandler(
        IAppServerTransport transport,
        AppServerConnection connection)
    {
        return new AppServerRequestHandler(
            runtime.SessionService,
            connection,
            transport,
            runtime.ChannelListContributor,
            new AppServerConnectionServices
            {
                ServerVersion = AppVersion.Informational,
                CronService = runtime.CronService,
                HeartbeatService = runtime.HeartbeatService,
                SkillsLoader = runtime.SkillsLoader,
                MemoryStore = runtime.MemoryStore,
                WorkspaceCraftPath = runtime.Paths.CraftPath,
                HostWorkspacePath = runtime.Paths.WorkspacePath,
                AutomationsHandler = runtime.AutomationsHandler,
                BroadcastCronStateChanged = BroadcastCronStateChanged,
                CommitMessageSuggest = runtime.CommitMessageSuggestService,
                WelcomeSuggestionService = runtime.WelcomeSuggestionService,
                DashboardUrl = runtime.DashboardUrl,
                WireAcpExtensionProxy = runtime.WireAcpExtensionProxy,
                WireNodeReplProxy = runtime.WireNodeReplProxy,
                WireDynamicToolProxy = runtime.WireDynamicToolProxy,
                ChannelStatusProvider = runtime.ChannelStatusProvider,
                McpClientManager = runtime.McpClientManager,
                McpAppTransientContextStore = _services.GetService<McpAppTransientContextStore>(),
                InlineVisualizationAssetStore = _services.GetService<InlineVisualizationAssetStore>(),
                InlineVisualizationRuntimeRegistry = _services.GetService<InlineVisualizationRuntimeRegistry>(),
                LspServerManager = runtime.LspServerManager,
                BroadcastMcpStatusChanged = BroadcastMcpStatusChanged,
                NotifyAppPrincipal = NotifyAppPrincipal,
                BroadcastTrustedNotification = BroadcastTrustedNotification,
                ProtocolExtensions = ProtocolExtensions,
                OnExternalChannelUpserted = runtime.ApplyExternalChannelUpsertAsync,
                OnExternalChannelRemoved = runtime.ApplyExternalChannelRemoveAsync,
                ExternalChannelLogProvider = runtime.ExternalChannelLogProvider,
                StreamDebugLogger = _services.GetService<SessionStreamDebugLogger>(),
                ConfigSchema = runtime.ConfigSchema,
                AppConfigMonitor = _services.GetRequiredService<IAppConfigMonitor>(),
                ChatClientRegistry = _services.GetRequiredService<ChatClientRegistry>(),
                OpenAIClientProvider = _services.GetRequiredService<OpenAIClientProvider>(),
                OpenAIAuthService = _services.GetService<DotCraft.Auth.OpenAI.IOpenAIAuthService>(),
                OpenAIUsageService = _services.GetService<DotCraft.Auth.OpenAI.IOpenAIUsageService>(),
                BackgroundTerminalService = _services.GetService<IBackgroundTerminalService>(),
                ContextPageManager = runtime.ContextPageManager,
                DreamStore = _services.GetService<DreamStore>(),
                DreamsService = runtime.DreamsService,
                AppBindingService = _services.GetService<AppBindingService>(),
                ThreadOriginPresentationProviders = _services.GetServices<IThreadOriginPresentationProvider>().ToArray(),
                PlanStore = runtime.PlanStore,
                TraceStore = _services.GetService<TraceStore>(),
                WireRuntimeAdditionalContextProvider = _services.GetService<WireRuntimeAdditionalContextProvider>(),
                HookRunner = _services.GetService<HookRunner>(),
            });
    }

    // -------------------------------------------------------------------------
    // Run modes
    // -------------------------------------------------------------------------

    private async Task RunStdioOnlyAsync(CancellationToken cancellationToken)
    {
        await using var transport = StdioTransport.CreateStdio();
        transport.Start();

        var connection = new AppServerConnection();
        _activeTransports.TryAdd(transport, connection);

        var handler = CreateRequestHandler(transport, connection);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio JSON-RPC 2.0)");

        try
        {
            await RunLoopAsync(
                transport, connection, handler,
                runtime.WireAcpExtensionProxy, runtime.WireNodeReplProxy, runtime.WireDynamicToolProxy,
                _services.GetService<WireRuntimeAdditionalContextProvider>(),
                _services.GetService<InlineVisualizationRuntimeRegistry>(),
                runtime.ContextPageManager,
                runtime.SessionService as IThreadAgentRefreshService,
                cancellationToken);
        }
        finally
        {
            _activeTransports.TryRemove(transport, out _);
            RemoveTerminalNotificationQueue(transport);
        }
    }

    private async Task RunWebSocketOnlyAsync(
        WebSocketServerConfig wsConfig,
        CancellationToken cancellationToken)
    {
        var (wsApp, wsUrl) = BuildWebSocketApp(
            wsConfig,
            cancellationToken,
            externalChannelRegistry);

        AnsiConsole.MarkupLine(
            $"[green][[AppServer]][/] DotCraft AppServer started (WebSocket at ws://{wsConfig.Host}:{wsConfig.Port}/ws)");

        // The WebSocket server IS the main loop — RunAsync blocks until shutdown.
        await wsApp.RunAsync(wsUrl);
    }

    private async Task RunStdioWithWebSocketAsync(
        WebSocketServerConfig wsConfig,
        CancellationToken cancellationToken)
    {
        // Build the WebSocket app and start it explicitly so that bind failures
        // surface immediately (fail-fast) instead of being deferred to finally.
        var (wsApp, wsUrl) = BuildWebSocketApp(
            wsConfig,
            cancellationToken,
            externalChannelRegistry);
        wsApp.Urls.Add(wsUrl);
        await wsApp.StartAsync(cancellationToken);

        AnsiConsole.MarkupLine(
            $"[green][[AppServer]][/] WebSocket listener started at ws://{wsConfig.Host}:{wsConfig.Port}/ws");

        await using var transport = StdioTransport.CreateStdio();
        transport.Start();

        var connection = new AppServerConnection();
        _activeTransports.TryAdd(transport, connection);

        var handler = CreateRequestHandler(transport, connection);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio + WebSocket)");

        try
        {
            await RunLoopAsync(
                transport, connection, handler,
                runtime.WireAcpExtensionProxy, runtime.WireNodeReplProxy, runtime.WireDynamicToolProxy,
                _services.GetService<WireRuntimeAdditionalContextProvider>(),
                _services.GetService<InlineVisualizationRuntimeRegistry>(),
                runtime.ContextPageManager,
                runtime.SessionService as IThreadAgentRefreshService,
                cancellationToken);
        }
        finally
        {
            _activeTransports.TryRemove(transport, out _);
            RemoveTerminalNotificationQueue(transport);
            // Stop the WebSocket server when stdio exits
            await wsApp.StopAsync(CancellationToken.None);
        }
    }

    // -------------------------------------------------------------------------
    // WebSocket server
    // -------------------------------------------------------------------------

    private (WebApplication App, string Url) BuildWebSocketApp(
        WebSocketServerConfig wsConfig,
        CancellationToken hostCt,
        ExternalChannelRegistry? channelRegistry = null)
    {
        // Refuse to start if the binding is non-loopback without a token (spec §15.4)
        var isLoopback = wsConfig.Host is "127.0.0.1" or "::1" or "[::1]" or "localhost";
        if (!isLoopback && string.IsNullOrEmpty(wsConfig.Token))
            throw new InvalidOperationException(
                "WebSocket listener bound to a non-loopback address requires a bearer token (AppServer.WebSocket.Token).");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        });

        // WebSocket upgrade endpoint
        app.Map("/ws", async context =>
        {
            // Token authentication: require ?token= when a token is configured (spec §15.4)
            if (!string.IsNullOrEmpty(wsConfig.Token))
            {
                var supplied = context.Request.Query["token"].FirstOrDefault();
                if (!string.Equals(supplied, wsConfig.Token, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized");
                    return;
                }
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("WebSocket upgrade required");
                return;
            }

            using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();
            await using var wsTransport = new WebSocketTransport(ws);
            wsTransport.Start();

            var wsConnection = new AppServerConnection();
            _activeTransports.TryAdd(wsTransport, wsConnection);
            try
            {
                var wsHandler = CreateRequestHandler(wsTransport, wsConnection);

                // ── Channel adapter routing (external-channel-adapter.md §4.2) ──
                //
                // Process the first message (must be 'initialize') manually. After the
                // handshake, if the client declared channelAdapter capability, route the
                // connection to the matching ExternalChannelHost and exit this handler.
                if (channelRegistry != null)
                {
                    var firstMsg = await wsTransport.ReadMessageAsync(hostCt);
                    if (firstMsg == null)
                        return; // Client disconnected before sending anything

                    if (firstMsg.IsRequest && firstMsg.Method == DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize)
                    {
                        // Process the initialize request normally
                        await ProcessRequestAsync(wsTransport, wsHandler, wsConnection, firstMsg, hostCt);

                        // Check if this is a channel adapter connection
                        if (wsConnection.IsChannelAdapter)
                        {
                            var channelName = wsConnection.ChannelAdapterName!;

                            if (channelRegistry.TryGet(channelName, out var host) && host != null)
                            {
                                // Stdio subprocess channels are registered for discovery but must not accept
                                // WebSocket adapter attach; websocket and managedWebsocket entries use /ws handover.
                                if (!host.AcceptsWebSocketAdapterAttach)
                                {
                                    AnsiConsole.MarkupLine(
                                        $"[yellow][[AppServer]][/] Rejected channel adapter '{channelName}': " +
                                        "channel uses subprocess transport; connect the adapter via stdio, not WebSocket.");

                                    await wsTransport.NotifyContractAsync(
                                        Contract.AppServerRpc.SystemEvent,
                                        new Contract.SystemEventNotification
                                        {
                                            Kind = "channelRejected",
                                            ChannelName = channelName,
                                            Message =
                                                $"Channel '{channelName}' is subprocess-only; WebSocket adapter attach is not supported."
                                        },
                                        hostCt);

                                    return;
                                }

                                // Wait for the 'initialized' notification before handover
                                var initdMsg = await wsTransport.ReadMessageAsync(hostCt);
                                if (initdMsg is { IsNotification: true, Method: DotCraft.Protocol.AppServer.AppServerMethodNames.Initialized })
                                {
                                    wsHandler.HandleNotification(initdMsg);
                                }

                                // Hand over transport and connection to the ExternalChannelHost.
                                // The host takes over the message loop; this handler returns.
                                host.AttachTransport(wsTransport, wsConnection);

                                // Block this handler until the transport's reader loop finishes
                                // (i.e. the WebSocket closes). This keeps the WebSocket and
                                // transport alive (they are 'using' scoped) without performing
                                // any additional ReceiveAsync calls on the raw WebSocket.
                                await wsTransport.Completed;
                                return;
                            }

                            // Channel name not registered — reject with system/event
                            AnsiConsole.MarkupLine(
                                $"[yellow][[AppServer]][/] Rejected channel adapter '{channelName}': " +
                                "not registered in ExternalChannels configuration.");

                            await wsTransport.NotifyContractAsync(
                                Contract.AppServerRpc.SystemEvent,
                                new Contract.SystemEventNotification
                                {
                                    Kind = "channelRejected",
                                    ChannelName = channelName,
                                    Message = $"Channel '{channelName}' is not registered in server configuration."
                                },
                                hostCt);

                            return; // Close connection
                        }

                        // Not a channel adapter — fall through to normal RunLoopAsync
                        // (initialize already processed, loop will handle subsequent messages)
                        await RunLoopAsync(
                            wsTransport, wsConnection, wsHandler,
                            runtime.WireAcpExtensionProxy, runtime.WireNodeReplProxy, runtime.WireDynamicToolProxy,
                            _services.GetService<WireRuntimeAdditionalContextProvider>(),
                            _services.GetService<InlineVisualizationRuntimeRegistry>(),
                            runtime.ContextPageManager,
                            runtime.SessionService as IThreadAgentRefreshService,
                            hostCt);
                        return;
                    }

                    // First message was not initialize — process normally and enter loop
                    if (firstMsg.IsNotification)
                    {
                        HandleNotification(firstMsg, wsHandler);
                    }
                    else if (firstMsg.IsRequest)
                    {
                        await ProcessRequestAsync(wsTransport, wsHandler, wsConnection, firstMsg, hostCt);
                    }
                }

                await RunLoopAsync(
                    wsTransport, wsConnection, wsHandler,
                    runtime.WireAcpExtensionProxy, runtime.WireNodeReplProxy, runtime.WireDynamicToolProxy,
                    _services.GetService<WireRuntimeAdditionalContextProvider>(),
                    _services.GetService<InlineVisualizationRuntimeRegistry>(),
                    runtime.ContextPageManager,
                    runtime.SessionService as IThreadAgentRefreshService,
                    hostCt);
            } // end try
            finally
            {
                _activeTransports.TryRemove(wsTransport, out _);
                RemoveTerminalNotificationQueue(wsTransport);
            }
        });

        // Health probes (spec §15.2)
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/readyz", () => Results.Ok(new { status = "ok" }));

        return (app, $"http://{wsConfig.Host}:{wsConfig.Port}");
    }

    // -------------------------------------------------------------------------
    // Main message loop
    // -------------------------------------------------------------------------

    // Fix 9: Bounded concurrency gate — at most 32 concurrent requests.
    // When full, new requests receive -32001 (server overloaded).
    private static readonly SemaphoreSlim RequestGate = new(32, 32);

    private static async Task RunLoopAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        WireAcpExtensionProxy? wireAcpProxy,
        WireNodeReplProxy? wireNodeReplProxy,
        WireDynamicToolProxy? wireDynamicToolProxy,
        WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider,
        InlineVisualizationRuntimeRegistry? inlineVisualizationRuntimeRegistry,
        IContextPageManager? contextPageManager,
        IThreadAgentRefreshService? threadAgentRefreshService,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            AppServerIncomingMessage? msg;
            try
            {
                msg = await transport.ReadMessageAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (msg == null)
                break; // EOF — client disconnected

            if (msg.IsNotification)
            {
                HandleNotification(msg, handler);
                continue;
            }

            if (!msg.IsRequest)
                continue; // Ignore unexpected responses (approval responses are routed by transport)

            // Reject immediately if the server is at capacity.
            if (!await RequestGate.WaitAsync(0, ct))
            {
                var overloadErr = AppServerErrors.ServerOverloaded().ToError();
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildErrorResponse(msg.Id, overloadErr), ct);
                continue;
            }

            // Process each request concurrently so turn/interrupt can be handled while
            // a long-running turn/start is streaming events.
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessRequestAsync(transport, handler, connection, msg, ct);
                }
                finally
                {
                    RequestGate.Release();
                }
            }, ct);
        }

        // Clean up connection-scoped capabilities when the client disconnects.
        // Active persisted turns are drained independently by the request handler.
        connection.MarkClosed();
        connection.CancelAllSubscriptions();
        wireAcpProxy?.UnbindTransport(transport);
        wireNodeReplProxy?.UnbindTransport(transport);
        wireDynamicToolProxy?.UnbindTransport(transport);
        if (inlineVisualizationRuntimeRegistry != null)
        {
            foreach (var threadId in inlineVisualizationRuntimeRegistry.UnbindTransport(transport))
            {
                contextPageManager?.ReleaseStablePage(threadId, ContextPageKeys.InlineVisualization());
                if (threadAgentRefreshService != null)
                    await threadAgentRefreshService.RefreshThreadAgentAsync(threadId, CancellationToken.None);
            }
        }
        if (wireRuntimeAdditionalContextProvider != null)
        {
            foreach (var threadId in wireRuntimeAdditionalContextProvider.UnbindTransport(transport))
                contextPageManager?.ReleaseStablePage(threadId, ContextPageKeys.RuntimeAdditionalContext());
        }
    }

    private static async Task ProcessRequestAsync(
        IAppServerTransport transport,
        AppServerRequestHandler handler,
        AppServerConnection connection,
        AppServerIncomingMessage msg,
        CancellationToken ct)
    {
        var previousTransport = AppServerRequestContext.CurrentTransport;
        var previousConnection = AppServerRequestContext.CurrentConnection;
        var previousMethod = AppServerRequestContext.CurrentMethod;
        AppServerRequestContext.CurrentTransport = transport;
        AppServerRequestContext.CurrentConnection = connection;
        AppServerRequestContext.CurrentMethod = msg.Method;
        try
        {
            object? result;
            try
            {
                result = await handler.HandleRequestAsync(msg, ct);
            }
            catch (AppServerException ex)
            {
                await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, ex.ToError()), ct);
                return;
            }
            catch (OperationCanceledException)
            {
                // Request cancelled — no response needed
                return;
            }
            catch (Exception ex)
            {
                var internalErr = AppServerErrors.InternalError(ex.Message).ToError();
                await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, internalErr), ct);
                await Console.Error.WriteLineAsync($"[AppServer] Internal error: {ex}");
                return;
            }

            // null result means the handler already sent the response inline (turn/start)
            if (result != null)
            {
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
            }
        }
        finally
        {
            AppServerRequestContext.CurrentTransport = previousTransport;
            AppServerRequestContext.CurrentConnection = previousConnection;
            AppServerRequestContext.CurrentMethod = previousMethod;
        }
    }

    private static void HandleNotification(AppServerIncomingMessage msg, AppServerRequestHandler handler)
    {
        handler.HandleNotification(msg);
    }

    public async ValueTask DisposeAsync()
    {
        var queues = _terminalNotificationQueues.Keys.ToArray();
        foreach (var transport in queues)
            RemoveTerminalNotificationQueue(transport);
        await runtime.DisposeAsync();
    }

    private void OnRuntimeMcpStatusChanged(McpServerStatusChangedEventArgs e)
    {
        BroadcastMcpStatusChanged(e.Status);
    }

    private void OnCronStateChanged(CronJob? job, string id, bool removed)
    {
        if (removed)
        {
            BroadcastCronStateChanged(new Contract.CronJobWireInfo { Id = id }, removed: true);
            return;
        }

        if (job != null)
            BroadcastCronStateChanged(CronContractMapper.ToContract(job), removed: false);
    }

    private void OnBackgroundJobResultProduced(BackgroundJobResult result)
    {
        BroadcastJobResult(
            result.Source,
            result.JobId,
            result.JobName,
            result.Result,
            result.Error,
            result.ThreadId,
            result.InputTokens,
            result.OutputTokens);
    }

    /// <summary>
    /// Broadcasts a <c>system/jobResult</c> JSON-RPC notification to all connected transports.
    /// Called when a server-managed cron or heartbeat job completes and the job was created from
    /// a CLI (non-social-channel) context. See spec Section 6.9.
    /// </summary>
    private void BroadcastJobResult(
        string source,
        string? jobId,
        string? jobName,
        string? result,
        string? error,
        string? threadId = null,
        int? inputTokens = null,
        int? outputTokens = null)
    {
        Contract.SystemJobTokenUsage? tokenUsage = null;
        if (inputTokens.HasValue || outputTokens.HasValue)
        {
            tokenUsage = new Contract.SystemJobTokenUsage
            {
                InputTokens = inputTokens ?? 0,
                OutputTokens = outputTokens ?? 0
            };
        }

        var parameters = new Contract.SystemJobResultNotification
        {
            Source = source,
            JobId = jobId,
            JobName = jobName,
            ThreadId = threadId,
            Result = result,
            Error = error,
            TokenUsage = tokenUsage
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.SystemJobResult))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.SystemJobResult, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastCronStateChanged(Contract.CronJobWireInfo job, bool removed)
    {
        var parameters = new Contract.CronStateChangedNotification { Job = job, Removed = removed };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.CronStateChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.CronStateChanged, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void NotifyAppPrincipal(string appId, string method, object? parameters)
    {
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.IsAppPrincipalAuthenticated
                || !string.Equals(connection.AppPrincipalAppId, appId, StringComparison.Ordinal))
                continue;
            _ = Task.Run(async () =>
            {
                try { await transport.NotifyContractAsync(method, parameters, CancellationToken.None); }
                catch { _activeTransports.TryRemove(transport, out _); }
            });
        }
    }

    private void BroadcastTrustedNotification(string method, object? parameters)
    {
        foreach (var (transport, connection) in _activeTransports)
        {
            if (connection.IsAppPrincipalAuthenticated || connection.IsChannelAdapter) continue;
            _ = Task.Run(async () =>
            {
                try { await transport.NotifyContractAsync(method, parameters, CancellationToken.None); }
                catch { _activeTransports.TryRemove(transport, out _); }
            });
        }
    }

    private void BroadcastAppBindingStatusChanged(Contract.ThreadAppBindingsChangedNotification notification) =>
        BroadcastTrustedNotification(
            Contract.AppServerRpc.ThreadAppBindingsChanged.Name,
            notification);

    private void BroadcastOpenAiUsageChanged(DotCraft.Auth.OpenAI.OpenAIUsageSnapshot? snapshot)
    {
        var result = Auth.OpenAI.OpenAIUsageMapping.ToWire(snapshot);
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.AuthOpenAiUsageChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.AuthOpenAiUsageChanged, result, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastMcpStatusChanged(McpServerStatusSnapshot server)
    {
        var status = server.StartupState switch
        {
            "ready" => "ready",
            "starting" => "starting",
            "disabled" => "cancelled",
            _ => "failed"
        };
        var parameters = new Contract.McpServerStartupStatusUpdatedNotification
        {
            Name = server.Name,
            Status = status,
            Error = server.LastError,
            FailureReason = server.FailureReason,
            Transport = server.Transport,
            AuthStatus = server.AuthStatus
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.McpServerStartupStatusUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.McpServerStartupStatusUpdated, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastWorkspaceConfigChanged(AppConfigChangedEventArgs change)
    {
        var parameters = new Contract.WorkspaceConfigChangedParams
        {
            Source = change.Source,
            Regions = change.Regions.ToArray(),
            ChangedAt = change.ChangedAt
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.SupportsConfigChange || !connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.WorkspaceConfigChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.WorkspaceConfigChanged, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastBackgroundTerminalEvent(BackgroundTerminalEvent evt)
    {
        var method = ResolveBackgroundTerminalNotificationMethod(evt.EventType);
        if (method is null)
            return;

        var parameters = new Contract.TerminalLifecycleNotification
        {
            Terminal = TerminalContractMapper.ToContract(evt.Terminal),
            Delta = evt.Delta
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.SupportsBackgroundTerminals || !connection.ShouldSendNotification(method))
                continue;

            var queue = _terminalNotificationQueues.GetOrAdd(
                transport,
                candidate => new Lazy<OrderedAppServerNotificationQueue>(
                    () => new OrderedAppServerNotificationQueue(
                        candidate,
                        () =>
                        {
                            _activeTransports.TryRemove(candidate, out _);
                            RemoveTerminalNotificationQueue(candidate);
                        }),
                    LazyThreadSafetyMode.ExecutionAndPublication));
            queue.Value.Enqueue(method, parameters);
        }
    }

    internal static string? ResolveBackgroundTerminalNotificationMethod(string eventType) =>
        eventType switch
        {
            "started" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalStarted,
            "outputDelta" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalOutputDelta,
            "completed" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalCompleted,
            "stalled" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalStalled,
            "cleaned" => DotCraft.Protocol.AppServer.AppServerMethodNames.TerminalCleaned,
            _ => null
        };

    private void RemoveTerminalNotificationQueue(IAppServerTransport transport)
    {
        if (_terminalNotificationQueues.TryRemove(transport, out var queue) && queue.IsValueCreated)
            queue.Value.Complete();
    }

    /// <summary>
    /// Broadcasts <c>thread/started</c> to all connected transports when any channel creates a thread
    /// in the shared <see cref="SessionService"/> (so Desktop sidebar updates without polling).
    /// </summary>
    private void BroadcastThreadStarted(SessionThread thread)
    {
        if (ThreadVisibility.IsInternal(thread))
            return;

        var wire = thread.ToWire() with
        {
            Runtime = runtime.SessionService.GetThreadRuntimeSnapshot(thread).ToWireRuntimeState()
        };
        var parameters = new Contract.ThreadNotification
        {
            Thread = AppServerContractMapper.ToContract(wire)
        };

        var skipTransport = !IsSubAgentThread(thread)
                            && (string.Equals(AppServerRequestContext.CurrentMethod, DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStart, StringComparison.Ordinal)
                                || string.Equals(AppServerRequestContext.CurrentMethod, DotCraft.Protocol.AppServer.AppServerMethodNames.WorktreeCreateAndStart, StringComparison.Ordinal))
            ? AppServerRequestContext.CurrentTransport
            : null;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStarted))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyAsync(Contract.AppServerRpc.ThreadStarted, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastSubAgentGraphChanged(string parentThreadId, string childThreadId)
    {
        var parameters = new Contract.SubAgentGraphChangedNotification
        {
            ParentThreadId = parentThreadId,
            ChildThreadId = childThreadId
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.SubAgentGraphChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.SubAgentGraphChanged, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private static bool IsSubAgentThread(SessionThread thread) =>
        string.Equals(thread.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private void BroadcastThreadGoalUpdated(ThreadGoal goal, string? turnId)
    {
        var wireGoal = AppServerContractMapper.ToContract(ThreadGoalSnapshot.FromGoal(goal));
        var parameters = new Contract.ThreadGoalUpdatedNotification
        {
            ThreadId = goal.ThreadId,
            Goal = DotCraft.Protocol.Optional<Contract.ThreadGoal?>.FromValue(wireGoal),
            TurnId = turnId is null
                ? default
                : DotCraft.Protocol.Optional<string?>.FromValue(turnId)
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.ThreadGoalUpdated, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastThreadGoalCleared(string threadId)
    {
        var parameters = new Contract.ThreadGoalClearedNotification { ThreadId = threadId };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadGoalCleared))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.ThreadGoalCleared, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    /// <summary>
    /// Broadcasts <c>thread/renamed</c> to all connected transports when a thread's display name changes
    /// (Wire <c>thread/rename</c>, first-message title, or any <see cref="ISessionService.RenameThreadAsync"/> caller).
    /// </summary>
    private void BroadcastThreadRenamed(SessionThread thread)
    {
        if (string.IsNullOrEmpty(thread.DisplayName))
            return;

        var parameters = new Contract.ThreadRenamedNotification
        {
            ThreadId = thread.Id,
            DisplayName = thread.DisplayName
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRenamed))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.ThreadRenamed, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastThreadUpdated(SessionThread thread)
    {
        if (ThreadVisibility.IsInternal(thread))
            return;

        var wire = thread.ToWire(includeTurns: false) with
        {
            Runtime = runtime.SessionService.GetThreadRuntimeSnapshot(thread).ToWireRuntimeState()
        };
        var parameters = new Contract.ThreadNotification
        {
            Thread = AppServerContractMapper.ToContract(wire)
        };

        var skipTransport = string.Equals(
            AppServerRequestContext.CurrentMethod,
            DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadWorktreeHandoff,
            StringComparison.Ordinal)
            ? AppServerRequestContext.CurrentTransport
            : null;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadUpdated))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyAsync(Contract.AppServerRpc.ThreadUpdated, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void OnThreadRuntimeSignal(string threadId, SessionThreadRuntimeSignal signal)
    {
        if (signal == SessionThreadRuntimeSignal.MemoryConsolidated)
        {
            runtime.WelcomeSuggestionService.ScheduleRefresh(runtime.Paths.WorkspacePath, threadId);
            return;
        }

        while (true)
        {
            _threadRuntime.TryGetValue(threadId, out var previous);
            var next = signal switch
            {
                SessionThreadRuntimeSignal.TurnStarted => previous with
                {
                    Running = true,
                    WaitingOnPlanConfirmation = false
                },
                SessionThreadRuntimeSignal.TurnCompleted => previous with
                {
                    Running = false,
                    WaitingOnPlanConfirmation = false
                },
                SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation => previous with
                {
                    Running = false,
                    WaitingOnPlanConfirmation = true
                },
                SessionThreadRuntimeSignal.TurnFailed => previous with
                {
                    Running = false,
                    WaitingOnPlanConfirmation = false
                },
                SessionThreadRuntimeSignal.TurnCancelled => previous with
                {
                    Running = false,
                    WaitingOnPlanConfirmation = false
                },
                SessionThreadRuntimeSignal.ApprovalRequested => previous with
                {
                    PendingApprovals = previous.PendingApprovals + 1
                },
                SessionThreadRuntimeSignal.ApprovalResolved => previous with
                {
                    PendingApprovals = Math.Max(0, previous.PendingApprovals - 1)
                },
                SessionThreadRuntimeSignal.UserInputRequested => previous with
                {
                    PendingUserInputs = previous.PendingUserInputs + 1
                },
                SessionThreadRuntimeSignal.UserInputResolved => previous with
                {
                    PendingUserInputs = Math.Max(0, previous.PendingUserInputs - 1)
                },
                SessionThreadRuntimeSignal.MaintenanceCompactingStarted => previous with
                {
                    MaintenanceKind = "compacting"
                },
                SessionThreadRuntimeSignal.MaintenanceConsolidatingStarted => previous with
                {
                    MaintenanceKind = "consolidating"
                },
                SessionThreadRuntimeSignal.MaintenanceCompleted => previous with
                {
                    MaintenanceKind = null
                },
                _ => previous
            };

            if (next.Equals(previous))
                return;

            if (_threadRuntime.TryAdd(threadId, next) || _threadRuntime.TryUpdate(threadId, next, previous))
            {
                BroadcastThreadRuntime(threadId, next.ToContract());
                RequestHubTurnNotification(threadId, signal);
                return;
            }
        }
    }

    private void RequestHubTurnNotification(string threadId, SessionThreadRuntimeSignal signal)
    {
        var spec = HubTurnNotificationPolicy.GetSpec(signal);
        if (spec is null)
            return;

        _ = Task.Run(async () =>
        {
            var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(runtime.SessionService, threadId);
            if (!decision.ShouldNotify)
                return;

            var actionUrl = decision.OpenDesktopOnClick && !string.IsNullOrWhiteSpace(decision.ThreadId)
                ? HubTurnNotificationPolicy.BuildDesktopOpenActionUrl(runtime.Paths.WorkspacePath, decision.ThreadId)
                : null;

            await HubNotificationClient.RequestAsync(
                runtime.Paths.WorkspacePath,
                spec.Kind,
                spec.TitleKey,
                FallbackText.Format(spec.TitleKey),
                spec.BodyKey,
                FallbackText.Format(spec.BodyKey, decision.DisplayName),
                new { name = decision.DisplayName },
                spec.Severity,
                decision.ThreadId,
                actionUrl,
                decision.OpenDesktopOnClick);
        });
    }

    private void BroadcastThreadRuntime(string threadId, Contract.ThreadRuntimeState runtime)
    {
        var parameters = new Contract.ThreadRuntimeChangedParams
        {
            ThreadId = threadId,
            Runtime = runtime
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadRuntimeChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.ThreadRuntimeChanged, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastThreadStatusChanged(string threadId, ThreadStatus previousStatus, ThreadStatus newStatus)
    {
        var parameters = new Contract.ThreadStatusChangedNotification
        {
            ThreadId = threadId,
            PreviousStatus = JsonNamingPolicy.CamelCase.ConvertName(previousStatus.ToString()),
            NewStatus = JsonNamingPolicy.CamelCase.ConvertName(newStatus.ToString())
        };

        var skipTransport = AppServerRequestContext.CurrentTransport;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadStatusChanged))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.ThreadStatusChanged, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    /// <summary>
    /// Broadcasts <c>thread/deleted</c> to all connected transports after permanent thread removal
    /// (Wire <c>thread/delete</c>, DashBoard, etc.).
    /// </summary>
    private void BroadcastThreadDeleted(string threadId)
    {
        _threadRuntime.TryRemove(threadId, out _);

        var parameters = new Contract.ThreadDeletedNotification { ThreadId = threadId };

        var skipTransport = AppServerRequestContext.CurrentTransport;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.ThreadDeleted))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyAsync(Contract.AppServerRpc.ThreadDeleted, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    /// <summary>
    /// Broadcasts a <c>plan/updated</c> JSON-RPC notification to all connected transports.
    /// Called from the <c>onPlanUpdated</c> callback injected into <see cref="AgentFactory"/>.
    /// The callback fires synchronously on the tool execution thread; transport writes are
    /// thread-safe (both stdio and WebSocket transports use internal write locks).
    /// </summary>
    private void BroadcastPlanUpdated(string threadId, StructuredPlan plan)
    {
        var parameters = BuildPlanUpdatedParameters(threadId, plan);

        // Fire-and-forget broadcast to all connected clients.
        // Errors on individual transports (e.g. disconnected) are silently ignored.
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.PlanUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.PlanUpdated, parameters, CancellationToken.None);
                }
                catch
                {
                    // Transport may have been disposed or disconnected; remove it.
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    internal static object BuildPlanUpdatedNotification(string threadId, StructuredPlan plan) => new
    {
        jsonrpc = "2.0",
        method = DotCraft.Protocol.AppServer.AppServerMethodNames.PlanUpdated,
        @params = BuildPlanUpdatedParameters(threadId, plan)
    };

    private static Contract.PlanUpdatedNotification BuildPlanUpdatedParameters(string threadId, StructuredPlan plan) => new()
    {
        ThreadId = threadId,
        Title = plan.Title,
        Overview = plan.Overview,
        Content = plan.Content,
        Todos = plan.Todos.Select(t => new Contract.PlanTodo
        {
            Id = t.Id,
            Content = t.Content,
            Priority = t.Priority,
            Status = t.Status
        }).ToArray()
    };

    /// <summary>
    /// Broadcasts an <c>automation/task/updated</c> JSON-RPC notification to all connected transports.
    /// Called by <see cref="AutomationsEventDispatcher"/> when a task status changes.
    /// </summary>
    private void BroadcastAutomationTaskUpdated(IAutomationTaskEventPayload task)
    {
        var parameters = AutomationsEventDispatcher.BuildNotificationParams(task, runtime.Paths.WorkspacePath);

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(DotCraft.Protocol.AppServer.AppServerMethodNames.AutomationTaskUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.AutomationTaskUpdated, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

}
