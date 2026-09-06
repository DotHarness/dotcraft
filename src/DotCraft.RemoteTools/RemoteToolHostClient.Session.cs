using System.Text.Json.Nodes;
using DotCraft.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostClient
{
    private static async ValueTask<TResult> SendAsync<TParams, TResult>(
        McpClient client,
        string method,
        TParams parameters,
        CancellationToken cancellationToken) where TResult : notnull
    {
        var response = await client.SendRequestAsync<TParams, ExtensionResponse<TResult>>(
            method,
            parameters,
            RemoteToolHostProtocol.JsonOptions,
            default,
            cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Result is null)
            throw new RemoteToolHostException(
                response.Error?.Code ?? RemoteToolErrorCodes.ProtocolMismatch,
                response.Error?.Message ?? $"Remote extension '{method}' returned no result.");
        return response.Result;
    }

    internal static RemoteToolHostException MapConnectionError(
        Exception exception,
        string? invocationId,
        string? closeDescription)
    {
        if (exception is RemoteToolHostException typed)
            return typed;
        return closeDescription switch
        {
            SatelliteWire.OfflineClose => new RemoteToolHostException(
                RemoteToolErrorCodes.HostOffline,
                "The paired machine is not connected to the Hub.",
                invocationId,
                exception),
            SatelliteWire.SessionFailedClose => new RemoteToolHostException(
                RemoteToolErrorCodes.SatelliteSessionFailed,
                "The paired machine did not open the requested session.",
                invocationId,
                exception),
            _ => new RemoteToolHostException(
                RemoteToolErrorCodes.HostOffline,
                exception.Message,
                invocationId,
                exception)
        };
    }

    private sealed class SharedLease
    {
        private readonly string _clientInstanceId;
        private readonly Action<RemoteToolRoute> _lost;
        private readonly CancellationTokenSource _heartbeatCts = new();
        private Task? _heartbeatTask;

        public SharedLease(
            RemoteToolRoute route,
            string workspacePath,
            HostSession session,
            string clientInstanceId,
            Action<RemoteToolRoute> lost)
        {
            Route = route;
            WorkspacePath = workspacePath;
            Session = session;
            _clientInstanceId = clientInstanceId;
            _lost = lost;
        }

        public RemoteToolRoute Route { get; }
        public string WorkspacePath { get; }
        public HostSession Session { get; }
        public int ReferenceCount { get; set; }
        public bool Lost { get; set; }
        public string HostName { get; set; } = "unknown";
        public string OperatingSystem { get; set; } = "unknown";
        public string UserName { get; set; } = "unknown";
        public string BuildVersion { get; set; } = "unknown";

        public void StartHeartbeat() => _heartbeatTask = RunHeartbeatAsync();

        public async Task DisposeAndReleaseAsync(CancellationToken cancellationToken)
        {
            _heartbeatCts.Cancel();
            if (_heartbeatTask is not null)
            {
                try { await _heartbeatTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            try
            {
                await SendAsync<WorkspaceLeaseRequest, JsonObject>(
                    Session.Client,
                    RemoteToolHostProtocol.WorkspacesRelease,
                    new WorkspaceLeaseRequest(
                        RemoteToolHostProtocol.ProfileVersion,
                        _clientInstanceId,
                        Route.LeaseId,
                        Route.WorkspaceId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // TTL reclaims an unconfirmed release; disconnect still removes local routing immediately.
            }
            _heartbeatCts.Dispose();
        }

        private async Task RunHeartbeatAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var lastSuccess = DateTimeOffset.UtcNow;
            try
            {
                while (await timer.WaitForNextTickAsync(_heartbeatCts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await SendAsync<WorkspaceLeaseRequest, WorkspaceHeartbeatResponse>(
                            Session.Client,
                            RemoteToolHostProtocol.WorkspacesHeartbeat,
                            new WorkspaceLeaseRequest(
                                RemoteToolHostProtocol.ProfileVersion,
                                _clientInstanceId,
                                Route.LeaseId,
                                Route.WorkspaceId),
                            _heartbeatCts.Token).ConfigureAwait(false);
                        lastSuccess = DateTimeOffset.UtcNow;
                    }
                    catch (RemoteToolHostException ex) when (
                        !string.Equals(ex.Code, RemoteToolErrorCodes.LeaseLost, StringComparison.Ordinal)
                        && DateTimeOffset.UtcNow - lastSuccess < TimeSpan.FromSeconds(60))
                    {
                    }
                    catch when (DateTimeOffset.UtcNow - lastSuccess < TimeSpan.FromSeconds(60))
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (_heartbeatCts.IsCancellationRequested)
            {
            }
            catch
            {
                _lost(Route);
            }
        }
    }

    private sealed class HostSession : IAsyncDisposable
    {
        private readonly RemoteToolHostConnection _connection;

        private HostSession(McpClient client, string hostId, RemoteToolHostConnection connection)
        {
            Client = client;
            HostId = hostId;
            _connection = connection;
        }

        public McpClient Client { get; }
        private string HostId { get; }

        public string? CloseDescription => _connection.CloseDescription;

        public bool Matches(string hostId, string? hubEndpoint) =>
            string.Equals(HostId, hostId, StringComparison.Ordinal)
            && string.Equals(_connection.Endpoint, hubEndpoint, StringComparison.Ordinal);

        public static async Task<HostSession> CreateAsync(
            string hostId,
            RemoteToolHostConnection connection,
            Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> elicitationHandler,
            CancellationToken cancellationToken)
        {
            try
            {
                var client = await McpClient.CreateAsync(
                    connection.Transport,
                    new McpClientOptions
                    {
                        ProtocolVersion = RemoteToolHostProtocol.McpProtocolVersion,
                        Handlers = new McpClientHandlers { ElicitationHandler = elicitationHandler }
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return new HostSession(client, hostId, connection);
            }
            catch (Exception ex)
            {
                var description = connection.CloseDescription;
                await connection.DisposeAsync().ConfigureAwait(false);
                throw MapConnectionError(ex, null, description);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
