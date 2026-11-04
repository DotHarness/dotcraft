using DotCraft.Sessions;
using Microsoft.Extensions.Logging;

namespace DotCraft.AppServer;

internal static class AppServerRequestDispatcher
{
    internal static async Task RunAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        ILogger logger,
        CancellationToken ct)
    {
        using var connectionScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["ConnectionId"] = connection.ConnectionId,
            ["Transport"] = transport.GetType().Name
        });
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await transport.ReadMessageAsync(ct);
                if (msg == null)
                    break;

                if (msg.IsNotification)
                {
                    handler.HandleNotification(msg);
                    continue;
                }

                if (!msg.IsRequest)
                    continue;

                _ = Task.Run(async () =>
                {
                    if (connection.IsClosed)
                        return;
                    try
                    {
                        await ProcessRequestAsync(transport, handler, connection, msg, logger, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "AppServer request dispatch failed");
                    }
                }, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            connection.MarkClosed();
        }
    }

    internal static async Task ProcessRequestAsync(
        IAppServerTransport transport,
        AppServerRequestHandler handler,
        AppServerConnection connection,
        AppServerIncomingMessage msg,
        ILogger logger,
        CancellationToken ct)
    {
        using var requestScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestMethod"] = msg.Method,
            ["RequestId"] = msg.Id?.ToString()
        });
        var previousTransport = AppServerRequestContext.CurrentTransport;
        var previousConnection = AppServerRequestContext.CurrentConnection;
        var previousMethod = AppServerRequestContext.CurrentMethod;
        var previousCapabilities = SessionClientCapabilitiesScope.Current;
        AppServerRequestContext.CurrentTransport = transport;
        AppServerRequestContext.CurrentConnection = connection;
        AppServerRequestContext.CurrentMethod = msg.Method;
        SessionClientCapabilitiesScope.Current = new SessionClientCapabilities(
            connection.SupportsCommandExecutionStreaming,
            connection.SupportsToolExecutionLifecycle);
        try
        {
            object? result;
            try
            {
                result = await handler.HandleRequestAsync(msg, ct);
            }
            catch (AppServerException ex)
            {
                if (ex.Code == AppServerErrors.InternalErrorCode)
                    logger.LogWarning(ex, "AppServer request failed with a protocol error");
                else
                    logger.LogDebug("AppServer request returned protocol error {ErrorCode}: {ErrorMessage}", ex.Code, ex.Message);
                await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, ex.ToError()), ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled AppServer request failure");
                var internalErr = AppServerErrors.InternalError(ex.Message).ToError();
                await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, internalErr), ct);
                return;
            }

            // A null result means the handler already wrote its response.
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
            SessionClientCapabilitiesScope.Current = previousCapabilities;
        }
    }
}
