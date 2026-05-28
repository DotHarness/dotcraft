using System.ClientModel;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.AppBinding;
using Microsoft.Extensions.Logging;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Localization;
using DotCraft.Logging;
using DotCraft.Hosting;
using DotCraft.Lsp;
using DotCraft.Memory;
using DotCraft.Dreams;
using DotCraft.Mcp;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.Tracing;
using DotCraft.ExternalChannel;
using DotCraft.Channels;
using DotCraft.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

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
    private readonly WorkspaceRuntime _runtime = runtime;
    private readonly IServiceProvider _services = runtime.Services;

    /// <summary>
    /// Thread-safe set of currently connected transports. Used to broadcast
    /// out-of-band notifications (e.g. <c>plan/updated</c>) to all clients.
    /// </summary>
    private readonly ConcurrentDictionary<IAppServerTransport, AppServerConnection> _activeTransports = new();
    private readonly ConcurrentDictionary<string, RuntimeFacts> _threadRuntime = new(StringComparer.Ordinal);

    private readonly record struct RuntimeFacts(
        int PendingApprovals,
        int PendingUserInputs,
        bool Running,
        bool WaitingOnPlanConfirmation,
        string? MaintenanceKind)
    {
        public ThreadRuntimeState ToWire() => new()
        {
            Running = Running,
            WaitingOnApproval = PendingApprovals > 0,
            WaitingOnInput = PendingUserInputs > 0,
            WaitingOnPlanConfirmation = WaitingOnPlanConfirmation,
            MaintenanceKind = MaintenanceKind,
            Busy = Running || PendingApprovals > 0 || PendingUserInputs > 0 || MaintenanceKind != null
        };
    }

    private IReadOnlyList<IAppServerProtocolExtension> ProtocolExtensions =>
        _runtime.Services.GetServices<IAppServerProtocolExtension>().ToArray();

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var appServerConfig = _runtime.Config.GetSection<AppServerConfig>("AppServer");
        if (!AppServerWorkspaceLock.TryAcquire(_runtime.Paths, out var workspaceLock, out var existingLock))
        {
            var owner = existingLock is null
                ? "another live process"
                : $"pid {existingLock.Pid}";
            throw new InvalidOperationException(
                $"DotCraft AppServer workspace lock is already held by {owner}: {AppServerWorkspaceLock.GetLockFilePath(_runtime.Paths.CraftPath)}");
        }

        if (workspaceLock is null)
            throw new InvalidOperationException("AppServer workspace lock acquisition returned no lock file.");

        if (existingLock is not null && !existingLock.IsProcessAlive())
            AnsiConsole.MarkupLine("[grey][[AppServer]][/] Recovered stale appserver.lock");

        workspaceLock.Publish(CreateLockInfo(appServerConfig));

        var moduleRegistry = _runtime.Services.GetRequiredService<ModuleRegistry>();

        try
        {
            await _runtime.StartAsync(moduleRegistry, cancellationToken);
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
                await _runtime.StopAsync(CancellationToken.None);
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
            WorkspacePath: _runtime.Paths.WorkspacePath,
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

        if (_runtime.Config.DashBoard.Enabled && _runtime.Config.Tracing.Enabled)
        {
            endpoints["dashboard"] =
                $"http://{_runtime.Config.DashBoard.Host}:{_runtime.Config.DashBoard.Port}/dashboard";
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
        _runtime.WorkspaceConfigChanged += BroadcastWorkspaceConfigChanged;
        _runtime.McpStatusChanged += OnRuntimeMcpStatusChanged;
        _runtime.PlanUpdated += BroadcastPlanUpdated;
        _runtime.ThreadStarted += BroadcastThreadStarted;
        _runtime.ThreadRenamed += BroadcastThreadRenamed;
        _runtime.ThreadDeleted += BroadcastThreadDeleted;
        _runtime.ThreadStatusChanged += BroadcastThreadStatusChanged;
        _runtime.ThreadRuntimeSignal += OnThreadRuntimeSignal;
        _runtime.ThreadGoalUpdated += BroadcastThreadGoalUpdated;
        _runtime.ThreadGoalCleared += BroadcastThreadGoalCleared;
        _runtime.SubAgentGraphChanged += BroadcastSubAgentGraphChanged;
        _runtime.CronStateChanged += OnCronStateChanged;
        _runtime.BackgroundJobResultProduced += OnBackgroundJobResultProduced;
        _runtime.AutomationTaskUpdated += BroadcastAutomationTaskUpdated;
        if (_services.GetService<IBackgroundTerminalService>() is { } terminals)
            terminals.TerminalEvent += BroadcastBackgroundTerminalEvent;
        if (_services.GetService<DotCraft.Auth.OpenAI.IOpenAIUsageService>() is { } usage)
            usage.SnapshotChanged += BroadcastOpenAiUsageChanged;
    }

    private void UnsubscribeRuntimeEvents()
    {
        _runtime.WorkspaceConfigChanged -= BroadcastWorkspaceConfigChanged;
        _runtime.McpStatusChanged -= OnRuntimeMcpStatusChanged;
        _runtime.PlanUpdated -= BroadcastPlanUpdated;
        _runtime.ThreadStarted -= BroadcastThreadStarted;
        _runtime.ThreadRenamed -= BroadcastThreadRenamed;
        _runtime.ThreadDeleted -= BroadcastThreadDeleted;
        _runtime.ThreadStatusChanged -= BroadcastThreadStatusChanged;
        _runtime.ThreadRuntimeSignal -= OnThreadRuntimeSignal;
        _runtime.ThreadGoalUpdated -= BroadcastThreadGoalUpdated;
        _runtime.ThreadGoalCleared -= BroadcastThreadGoalCleared;
        _runtime.SubAgentGraphChanged -= BroadcastSubAgentGraphChanged;
        _runtime.CronStateChanged -= OnCronStateChanged;
        _runtime.BackgroundJobResultProduced -= OnBackgroundJobResultProduced;
        _runtime.AutomationTaskUpdated -= BroadcastAutomationTaskUpdated;
        if (_services.GetService<IBackgroundTerminalService>() is { } terminals)
            terminals.TerminalEvent -= BroadcastBackgroundTerminalEvent;
        if (_services.GetService<DotCraft.Auth.OpenAI.IOpenAIUsageService>() is { } usage)
            usage.SnapshotChanged -= BroadcastOpenAiUsageChanged;
    }

    private AppServerRequestHandler CreateRequestHandler(
        IAppServerTransport transport,
        AppServerConnection connection)
    {
        return new AppServerRequestHandler(
            _runtime.SessionService,
            connection,
            transport,
            _runtime.ChannelListContributor,
            serverVersion: AppVersion.Informational,
            cronService: _runtime.CronService,
            heartbeatService: _runtime.HeartbeatService,
            skillsLoader: _runtime.SkillsLoader,
            memoryStore: _runtime.MemoryStore,
            workspaceCraftPath: _runtime.Paths.CraftPath,
            hostWorkspacePath: _runtime.Paths.WorkspacePath,
            automationsHandler: _runtime.AutomationsHandler,
            broadcastCronStateChanged: BroadcastCronStateChanged,
            commitMessageSuggest: _runtime.CommitMessageSuggestService,
            welcomeSuggestionService: _runtime.WelcomeSuggestionService,
            dashboardUrl: _runtime.DashboardUrl,
            wireAcpExtensionProxy: _runtime.WireAcpExtensionProxy,
            wireNodeReplProxy: _runtime.WireNodeReplProxy,
            wireDynamicToolProxy: _runtime.WireDynamicToolProxy,
            channelStatusProvider: _runtime.ChannelStatusProvider,
            mcpClientManager: _runtime.McpClientManager,
            lspServerManager: _runtime.LspServerManager,
            broadcastMcpStatusChanged: BroadcastMcpStatusChanged,
            protocolExtensions: ProtocolExtensions,
            onExternalChannelUpserted: _runtime.ApplyExternalChannelUpsertAsync,
            onExternalChannelRemoved: _runtime.ApplyExternalChannelRemoveAsync,
            externalChannelLogProvider: _runtime.ExternalChannelLogProvider,
            streamDebugLogger: _services.GetService<SessionStreamDebugLogger>(),
            configSchema: _runtime.ConfigSchema,
            appConfigMonitor: _services.GetRequiredService<IAppConfigMonitor>(),
            chatClientRegistry: _services.GetRequiredService<ChatClientRegistry>(),
            openAIClientProvider: _services.GetRequiredService<OpenAIClientProvider>(),
            openAIAuthService: _services.GetService<DotCraft.Auth.OpenAI.IOpenAIAuthService>(),
            openAIUsageService: _services.GetService<DotCraft.Auth.OpenAI.IOpenAIUsageService>(),
            backgroundTerminalService: _services.GetService<IBackgroundTerminalService>(),
            contextPageManager: _runtime.ContextPageManager,
            dreamStore: _services.GetService<DreamStore>(),
            dreamsService: _runtime.DreamsService,
            appBindingService: _services.GetService<AppBindingService>(),
            planStore: _runtime.PlanStore,
            traceStore: _services.GetService<TraceStore>());
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
            await RunLoopAsync(transport, connection, handler, _runtime.WireAcpExtensionProxy, _runtime.WireNodeReplProxy, _runtime.WireDynamicToolProxy, cancellationToken);
        }
        finally
        {
            _activeTransports.TryRemove(transport, out _);
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
            await RunLoopAsync(transport, connection, handler, _runtime.WireAcpExtensionProxy, _runtime.WireNodeReplProxy, _runtime.WireDynamicToolProxy, cancellationToken);
        }
        finally
        {
            _activeTransports.TryRemove(transport, out _);
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

                    if (firstMsg.IsRequest && firstMsg.Method == AppServerMethods.Initialize)
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

                                    await wsTransport.WriteMessageAsync(new
                                    {
                                        jsonrpc = "2.0",
                                        method = AppServerMethods.SystemEvent,
                                        @params = new
                                        {
                                            kind = "channelRejected",
                                            channelName,
                                            message =
                                                $"Channel '{channelName}' is subprocess-only; WebSocket adapter attach is not supported."
                                        }
                                    }, hostCt);

                                    return;
                                }

                                // Wait for the 'initialized' notification before handover
                                var initdMsg = await wsTransport.ReadMessageAsync(hostCt);
                                if (initdMsg is { IsNotification: true, Method: AppServerMethods.Initialized })
                                {
                                    wsHandler.HandleInitializedNotification();
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

                            await wsTransport.WriteMessageAsync(new
                            {
                                jsonrpc = "2.0",
                                method = AppServerMethods.SystemEvent,
                                @params = new
                                {
                                    kind = "channelRejected",
                                    channelName,
                                    message = $"Channel '{channelName}' is not registered in server configuration."
                                }
                            }, hostCt);

                            return; // Close connection
                        }

                        // Not a channel adapter — fall through to normal RunLoopAsync
                        // (initialize already processed, loop will handle subsequent messages)
                        await RunLoopAsync(wsTransport, wsConnection, wsHandler, _runtime.WireAcpExtensionProxy, _runtime.WireNodeReplProxy, _runtime.WireDynamicToolProxy, hostCt);
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

                await RunLoopAsync(wsTransport, wsConnection, wsHandler, _runtime.WireAcpExtensionProxy, _runtime.WireNodeReplProxy, _runtime.WireDynamicToolProxy, hostCt);
            } // end try
            finally
            {
                _activeTransports.TryRemove(wsTransport, out _);
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
        switch (msg.Method)
        {
            case AppServerMethods.Initialized:
                handler.HandleInitializedNotification();
                break;
            // Other client notifications (none defined in v1) are silently ignored
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _runtime.DisposeAsync();
    }

    private void OnRuntimeMcpStatusChanged(McpServerStatusChangedEventArgs e)
    {
        BroadcastMcpStatusChanged(new McpStatusInfoWire
        {
            Name = e.Status.Name,
            Enabled = e.Status.Enabled,
            StartupState = e.Status.StartupState,
            ToolCount = e.Status.ToolCount,
            ResourceCount = e.Status.ResourceCount,
            ResourceTemplateCount = e.Status.ResourceTemplateCount,
            LastError = e.Status.LastError,
            Transport = e.Status.Transport,
            Origin = new McpServerOriginWire
            {
                Kind = e.Status.Origin.Kind,
                PluginId = e.Status.Origin.PluginId,
                PluginDisplayName = e.Status.Origin.PluginDisplayName,
                DeclaredName = e.Status.Origin.DeclaredName
            },
            ReadOnly = e.Status.ReadOnly
        });
    }

    private void OnCronStateChanged(CronJob? job, string id, bool removed)
    {
        if (removed)
        {
            BroadcastCronStateChanged(new CronJobWireInfo { Id = id }, removed: true);
            return;
        }

        if (job != null)
            BroadcastCronStateChanged(CronJobWireMapping.ToWire(job), removed: false);
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
        object? tokenUsage = null;
        if (inputTokens.HasValue || outputTokens.HasValue)
        {
            tokenUsage = new
            {
                inputTokens = inputTokens ?? 0,
                outputTokens = outputTokens ?? 0
            };
        }

        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.SystemJobResult,
            @params = new
            {
                source,
                jobId,
                jobName,
                threadId,
                result,
                error,
                tokenUsage
            }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.SystemJobResult))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastCronStateChanged(CronJobWireInfo job, bool removed)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.CronStateChanged,
            @params = new { job, removed }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.CronStateChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastOpenAiUsageChanged(DotCraft.Auth.OpenAI.OpenAIUsageSnapshot? snapshot)
    {
        var result = DotCraft.Auth.OpenAI.OpenAIUsageMapping.ToWire(snapshot);
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.AuthOpenAiUsageChanged,
            @params = result
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.AuthOpenAiUsageChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    private void BroadcastMcpStatusChanged(McpStatusInfoWire server)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.McpStatusUpdated,
            @params = new { server }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.McpStatusUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.WorkspaceConfigChanged,
            @params = new WorkspaceConfigChangedParams
            {
                Source = change.Source,
                Regions = [.. change.Regions],
                ChangedAt = change.ChangedAt
            }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.SupportsConfigChange || !connection.ShouldSendNotification(AppServerMethods.WorkspaceConfigChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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
        var method = evt.EventType switch
        {
            "started" => AppServerMethods.TerminalStarted,
            "outputDelta" => AppServerMethods.TerminalOutputDelta,
            "completed" => AppServerMethods.TerminalCompleted,
            "stalled" => AppServerMethods.TerminalStalled,
            "cleaned" => AppServerMethods.TerminalCleaned,
            _ => "terminal/event"
        };
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params = new
            {
                terminal = evt.Terminal,
                delta = evt.Delta
            }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.SupportsBackgroundTerminals || !connection.ShouldSendNotification(method))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
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
            Runtime = _runtime.SessionService.GetThreadRuntimeSnapshot(thread).ToWireRuntimeState()
        };
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadStarted,
            @params = new { thread = wire }
        };

        var skipTransport = !IsSubAgentThread(thread)
                            && string.Equals(AppServerRequestContext.CurrentMethod, AppServerMethods.ThreadStart, StringComparison.Ordinal)
            ? AppServerRequestContext.CurrentTransport
            : null;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadStarted))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.SubAgentGraphChanged,
            @params = new { parentThreadId, childThreadId }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.SubAgentGraphChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadGoalUpdated,
            @params = new { threadId = goal.ThreadId, goal, turnId }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadGoalUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadGoalCleared,
            @params = new { threadId }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadGoalCleared))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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

        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadRenamed,
            @params = new { threadId = thread.Id, displayName = thread.DisplayName }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadRenamed))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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
            _runtime.WelcomeSuggestionService.ScheduleRefresh(_runtime.Paths.WorkspacePath, threadId);
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
                BroadcastThreadRuntime(threadId, next.ToWire());
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
            var lang = LanguageService.Current;
            var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(_runtime.SessionService, threadId);
            if (!decision.ShouldNotify)
                return;

            var actionUrl = decision.OpenDesktopOnClick && !string.IsNullOrWhiteSpace(decision.ThreadId)
                ? HubTurnNotificationPolicy.BuildDesktopOpenActionUrl(_runtime.Paths.WorkspacePath, decision.ThreadId)
                : null;

            await HubNotificationClient.RequestAsync(
                _runtime.Paths.WorkspacePath,
                spec.Kind,
                lang.T(spec.TitleKey),
                lang.T(spec.BodyKey, decision.DisplayName),
                spec.Severity,
                decision.ThreadId,
                actionUrl,
                decision.OpenDesktopOnClick);
        });
    }

    private void BroadcastThreadRuntime(string threadId, ThreadRuntimeState runtime)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadRuntimeChanged,
            @params = new ThreadRuntimeChangedParams
            {
                ThreadId = threadId,
                Runtime = runtime
            }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadRuntimeChanged))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadStatusChanged,
            @params = new { threadId, previousStatus, newStatus }
        };

        var skipTransport = AppServerRequestContext.CurrentTransport;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadStatusChanged))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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

        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadDeleted,
            @params = new { threadId }
        };

        var skipTransport = AppServerRequestContext.CurrentTransport;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadDeleted))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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
        var notification = BuildPlanUpdatedNotification(threadId, plan);

        // Fire-and-forget broadcast to all connected clients.
        // Errors on individual transports (e.g. disconnected) are silently ignored.
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.PlanUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
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
        method = AppServerMethods.PlanUpdated,
        @params = new
        {
            threadId,
            title = plan.Title,
            overview = plan.Overview,
            content = plan.Content,
            todos = plan.Todos.Select(t => new
            {
                id = t.Id,
                content = t.Content,
                priority = t.Priority,
                status = t.Status
            }).ToArray()
        }
    };

    /// <summary>
    /// Broadcasts an <c>automation/task/updated</c> JSON-RPC notification to all connected transports.
    /// Called by <see cref="AutomationsEventDispatcher"/> when a task status changes.
    /// </summary>
    private void BroadcastAutomationTaskUpdated(IAutomationTaskEventPayload task)
    {
        var notification = AutomationsEventDispatcher.BuildNotification(task, _runtime.Paths.WorkspacePath);

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.AutomationTaskUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

}
