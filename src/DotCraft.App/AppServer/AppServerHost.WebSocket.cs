using System.Net.WebSockets;
using DotCraft.Channels;
using DotCraft.GenerativeUi;
using DotCraft.Logging;
using DotCraft.Sessions;
using DotCraft.Visualizations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public sealed partial class AppServerHost
{
    private async Task RunWebSocketOnlyAsync(
        WebSocketServerConfig wsConfig,
        Action<string> publishEndpoint,
        CancellationToken cancellationToken)
    {
        await using var wsApp = await StartWebSocketAppAsync(wsConfig, publishEndpoint, cancellationToken);
        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (WebSocket)");
        await wsApp.WaitForShutdownAsync(cancellationToken);
    }

    private async Task RunStdioWithWebSocketAsync(
        WebSocketServerConfig wsConfig,
        Action<string> publishEndpoint,
        CancellationToken cancellationToken)
    {
        await using var wsApp = await StartWebSocketAppAsync(wsConfig, publishEndpoint, cancellationToken);
        var connection = new AppServerConnection();
        await using var transport = StdioTransport.CreateStdio(_logger, connection.ConnectionId);
        transport.Start();
        _activeTransports.TryAdd(transport, connection);

        var handler = CreateRequestHandler(transport, connection);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio + WebSocket)");
        _logger.LogInformation("AppServer started with stdio and WebSocket transports");
        LogStartupStage("stdio-and-websocket-transports-ready");

        try
        {
            await RunLoopAsync(
                transport, connection, handler,
                WireAcpExtensionProxy, WireDynamicToolProxy,
                _services.GetService<WireRuntimeAdditionalContextProvider>(),
                _services.GetService<VisualizationRuntimeRegistry>(),
                _services.GetService<GenerativeUiRuntimeRegistry>(),
                runtime.ContextPageManager,
                runtime.SessionService as IThreadAgentRefreshService,
                _logger,
                cancellationToken);
        }
        finally
        {
            _activeTransports.TryRemove(transport, out _);
            RemoveTerminalNotificationQueue(transport);
            RemovePluginNotificationQueue(transport);
            _threadRuntimeNotifications.RemoveTransport(transport);
            await wsApp.StopAsync(CancellationToken.None);
        }
    }

    private async Task<WebApplication> StartWebSocketAppAsync(
        WebSocketServerConfig wsConfig,
        Action<string> publishEndpoint,
        CancellationToken cancellationToken)
    {
        var (app, url) = BuildWebSocketApp(wsConfig, cancellationToken, channelHostRegistry);
        try
        {
            app.Urls.Add(url);
            await app.StartAsync(cancellationToken);
            var port = new Uri(app.Urls.Single()).Port;
            publishEndpoint(BuildWebSocketEndpoint(wsConfig.Host, port, wsConfig.Token));
            AnsiConsole.MarkupLine(
                $"[green][[AppServer]][/] WebSocket listener started at ws://{wsConfig.Host}:{port}/ws");
            _logger.LogInformation("AppServer WebSocket listener started at {Host}:{Port}", wsConfig.Host, port);
            LogStartupStage("websocket-transport-ready");
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private (WebApplication App, string Url) BuildWebSocketApp(
        WebSocketServerConfig wsConfig,
        CancellationToken hostCt,
        ChannelHostRegistry? channelRegistry = null)
    {
        var isLoopback = wsConfig.Host is "127.0.0.1" or "::1" or "[::1]" or "localhost";
        if (!isLoopback && string.IsNullOrEmpty(wsConfig.Token))
            throw new InvalidOperationException(
                "WebSocket listener bound to a non-loopback address requires a bearer token (AppServer.WebSocket.Token).");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new NonOwningLoggerProvider(_loggerFactory));
        var dashboardUrl = ManagedAppServerEnvironment.GetDashboardUrl(runtime.Config);
        if (dashboardUrl is not null)
            AppServerDashboard.ConfigureServices(builder.Services, _services);

        var app = builder.Build();
        if (dashboardUrl is not null && !string.IsNullOrEmpty(wsConfig.Token))
            AppServerDashboard.MapManaged(app, runtime, dashboardUrl.AbsolutePath.TrimEnd('/'), wsConfig.Token);
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        });

        app.Map("/ws", async context =>
        {
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
            var wsConnection = new AppServerConnection();
            await using var wsTransport = new WebSocketTransport(ws, _logger, wsConnection.ConnectionId);
            wsTransport.Start();
            _activeTransports.TryAdd(wsTransport, wsConnection);
            try
            {
                var wsHandler = CreateRequestHandler(wsTransport, wsConnection);

                if (channelRegistry != null)
                {
                    var firstMsg = await wsTransport.ReadMessageAsync(hostCt);
                    if (firstMsg == null)
                        return;

                    if (firstMsg.IsRequest && firstMsg.Method == DotCraft.Protocol.AppServer.AppServerMethodNames.Initialize)
                    {
                        await AppServerRequestDispatcher.ProcessRequestAsync(wsTransport, wsHandler, wsConnection, firstMsg, _logger, hostCt);

                        if (wsConnection.IsChannelAdapter)
                        {
                            var channelName = wsConnection.ChannelAdapterName!;

                            if (channelRegistry.TryGet(channelName, out var host) && host != null)
                            {
                                var initdMsg = await wsTransport.ReadMessageAsync(hostCt);
                                if (initdMsg is { IsNotification: true, Method: DotCraft.Protocol.AppServer.AppServerMethodNames.Initialized })
                                {
                                    wsHandler.HandleNotification(initdMsg);
                                    if (host.TryAttachTransport(wsTransport, wsConnection))
                                    {
                                        await wsTransport.Completed;
                                        return;
                                    }
                                }
                            }

                            _logger.LogWarning(
                                "Rejected channel adapter {Channel} without an awaiting plugin process",
                                channelName);
                            AnsiConsole.MarkupLine(
                                $"[yellow][[AppServer]][/] Rejected channel adapter '{channelName}': " +
                                "no active plugin process is awaiting this adapter.");

                            await wsTransport.NotifyContractAsync(
                                Contract.AppServerRpc.SystemEvent,
                                new Contract.SystemEventNotification
                                {
                                    Kind = "channelRejected",
                                    ChannelName = channelName,
                                    Message = $"Channel '{channelName}' is not awaiting an adapter connection."
                                },
                                hostCt);

                            return;
                        }

                        await RunLoopAsync(
                            wsTransport, wsConnection, wsHandler,
                            WireAcpExtensionProxy, WireDynamicToolProxy,
                            _services.GetService<WireRuntimeAdditionalContextProvider>(),
                            _services.GetService<VisualizationRuntimeRegistry>(),
                            _services.GetService<GenerativeUiRuntimeRegistry>(),
                            runtime.ContextPageManager,
                            runtime.SessionService as IThreadAgentRefreshService,
                            _logger,
                            hostCt);
                        return;
                    }

                    if (firstMsg.IsNotification)
                    {
                        wsHandler.HandleNotification(firstMsg);
                    }
                    else if (firstMsg.IsRequest)
                    {
                        await AppServerRequestDispatcher.ProcessRequestAsync(wsTransport, wsHandler, wsConnection, firstMsg, _logger, hostCt);
                    }
                }

                await RunLoopAsync(
                    wsTransport, wsConnection, wsHandler,
                    WireAcpExtensionProxy, WireDynamicToolProxy,
                    _services.GetService<WireRuntimeAdditionalContextProvider>(),
                    _services.GetService<VisualizationRuntimeRegistry>(),
                    _services.GetService<GenerativeUiRuntimeRegistry>(),
                    runtime.ContextPageManager,
                    runtime.SessionService as IThreadAgentRefreshService,
                    _logger,
                    hostCt);
            } // end try
            finally
            {
                _activeTransports.TryRemove(wsTransport, out _);
                RemoveTerminalNotificationQueue(wsTransport);
                RemovePluginNotificationQueue(wsTransport);
                _threadRuntimeNotifications.RemoveTransport(wsTransport);
            }
        });

        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/readyz", () => Results.Ok(new { status = "ok" }));

        return (app, $"http://{wsConfig.Host}:{wsConfig.Port}");
    }
}
