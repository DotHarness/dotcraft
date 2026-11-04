using System.Net.WebSockets;
using DotCraft.ExternalChannel;
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
                WireAcpExtensionProxy, WireNodeReplProxy, WireDynamicToolProxy,
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
        var (app, url) = BuildWebSocketApp(wsConfig, cancellationToken, externalChannelRegistry);
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
        ExternalChannelRegistry? channelRegistry = null)
    {
        var isLoopback = wsConfig.Host is "127.0.0.1" or "::1" or "[::1]" or "localhost";
        if (!isLoopback && string.IsNullOrEmpty(wsConfig.Token))
            throw new InvalidOperationException(
                "WebSocket listener bound to a non-loopback address requires a bearer token (AppServer.WebSocket.Token).");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new NonOwningLoggerProvider(_loggerFactory));

        var app = builder.Build();
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
                                // Stdio subprocess channels are registered for discovery but must not accept
                                // WebSocket adapter attach; websocket and managedWebsocket entries use /ws handover.
                                if (!host.AcceptsWebSocketAdapterAttach)
                                {
                                    _logger.LogWarning(
                                        "Rejected channel adapter {Channel}: subprocess-only channels cannot attach over WebSocket",
                                        channelName);
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

                                var initdMsg = await wsTransport.ReadMessageAsync(hostCt);
                                if (initdMsg is { IsNotification: true, Method: DotCraft.Protocol.AppServer.AppServerMethodNames.Initialized })
                                {
                                    wsHandler.HandleNotification(initdMsg);
                                }

                                host.AttachTransport(wsTransport, wsConnection);

                                // Keeps the 'using'-scoped WebSocket alive without another ReceiveAsync on it.
                                await wsTransport.Completed;
                                return;
                            }

                            _logger.LogWarning(
                                "Rejected unregistered channel adapter {Channel}",
                                channelName);
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

                            return;
                        }

                        await RunLoopAsync(
                            wsTransport, wsConnection, wsHandler,
                            WireAcpExtensionProxy, WireNodeReplProxy, WireDynamicToolProxy,
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
                    WireAcpExtensionProxy, WireNodeReplProxy, WireDynamicToolProxy,
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
