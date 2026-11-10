using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Mcp;
using DotCraft.Protocol;
using DotCraft.Tools;
using DotCraft.Sessions.Wire;
using ModelContextProtocol.Protocol;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class AppServerMcpElicitationHandler(AppServerThreadSubscribers subscribers) : IMcpElicitationHandler
{
    public async ValueTask<ElicitResult> HandleAsync(
        ToolInvocationContext context,
        string serverName,
        ElicitRequestParams request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetSchema(request, out var schema))
            return new ElicitResult { Action = "decline" };

        var recipients = subscribers.GetElicitationRecipients(context.ThreadId);
        if (recipients.Count == 0)
            throw new McpElicitationUnavailableException("no_client");

        var parameters = new Contract.McpServerElicitationRequestParams
        {
            ThreadId = context.ThreadId,
            TurnId = context.TurnId,
            ServerName = serverName,
            Mode = string.IsNullOrWhiteSpace(request.Mode) ? "form" : request.Mode,
            ElicitationId = request.ElicitationId,
            Message = request.Message,
            Url = request.Url,
            RequestedSchema = schema is null ? null : JsonSerializer.SerializeToElement(schema, SessionWireJsonOptions.Default)
        };
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = recipients.Select(recipient => RequestAsync(
            recipient.Connection, recipient.Transport, parameters, schema, lifetime.Token)).ToArray();
        var remaining = tasks.ToList();
        var receivedResponse = false;
        try
        {
            while (remaining.Count > 0)
            {
                var completed = await Task.WhenAny(remaining).WaitAsync(cancellationToken);
                remaining.Remove(completed);
                var outcome = await completed;
                receivedResponse |= outcome.Responded;
                cancellationToken.ThrowIfCancellationRequested();
                if (outcome.Result is { } result)
                    return result;
            }
            throw new McpElicitationUnavailableException(receivedResponse ? "no_client" : "disconnected");
        }
        finally
        {
            await lifetime.CancelAsync();
            await Task.WhenAll(tasks);
        }
    }

    private static async Task<(ElicitResult? Result, bool Responded)> RequestAsync(
        AppServerConnection connection,
        IAppServerTransport transport,
        Contract.McpServerElicitationRequestParams parameters,
        JsonObject? schema,
        CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        AppServerPendingClientRequest? pending = null;
        try
        {
            pending = await transport.StartClientRequestAsync(
                Contract.AppServerRpc.McpServerElicitationRequest.Name,
                parameters, lifetime.Token, Timeout.InfiniteTimeSpan);
            var completed = await Task.WhenAny(pending.Response, connection.Closed).WaitAsync(lifetime.Token);
            if (completed == connection.Closed)
                return (null, false);
            var response = await pending.Response;
            if (response.Error.HasValue || !response.Result.HasValue)
                return (null, true);
            var result = response.Result.Value.Deserialize<Contract.McpServerElicitationResponse>(AppServerContractJson.Options);
            return (TryGetResult(result, schema), true);
        }
        catch (OperationCanceledException)
        {
            return (null, false);
        }
        catch (JsonException)
        {
            return (null, true);
        }
        catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex))
        {
            return (null, false);
        }
        finally
        {
            await lifetime.CancelAsync();
            if (pending is not null)
            {
                try { await pending.Response; }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (AppServerEventDispatcher.IsTransportUnavailableException(ex)) { }
                if (!connection.IsClosed)
                {
                    connection.QueueThreadNotification(transport, Contract.AppServerRpc.ServerRequestResolved.Name,
                        new Contract.ServerRequestResolvedNotification
                        {
                            ThreadId = parameters.ThreadId,
                            RequestId = pending.Id
                        });
                }
            }
        }
    }

    private static ElicitResult? TryGetResult(Contract.McpServerElicitationResponse? result, JsonObject? schema)
    {
        var action = result?.Action.IsSet == true ? result.Action.Value : null;
        if (action is not ("accept" or "decline" or "cancel"))
            return null;
        var content = result!.Content.IsSet
            ? result.Content.Value?.ToDictionary(static pair => pair.Key, static pair => pair.Value)
            : null;
        if (action != "accept")
            return content is null ? new ElicitResult { Action = action } : null;
        if (schema is null && content is not null)
            return null;
        if (schema is not null && !McpElicitationSchemaValidator.TryValidateContent(schema, content))
            return null;
        return new ElicitResult
        {
            Action = action,
            Content = content
        };
    }

    private static bool TryGetSchema(ElicitRequestParams request, out JsonObject? schema)
    {
        schema = null;
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? "form" : request.Mode;
        if (mode == "form")
        {
            if (!McpElicitationSchemaValidator.TryValidateSchema(request.RequestedSchema, out var form))
                return false;
            schema = form;
            return true;
        }
        return mode == "url" && Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
               && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp && url.IsLoopback);
    }
}
