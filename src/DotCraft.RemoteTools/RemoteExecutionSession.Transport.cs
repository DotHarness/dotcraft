using System.Text.Json.Nodes;
using DotCraft.Tools;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

public sealed partial class RemoteExecutionSession
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
                "The remote device is not connected.",
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

    private sealed class SessionLease
    {
        private readonly string _clientInstanceId;
        private readonly Action _lost;
        private readonly CancellationTokenSource _heartbeatCts = new();
        private Task? _heartbeatTask;

        public SessionLease(
            RemoteToolRoute route,
            string workspacePath,
            HostSession session,
            string clientInstanceId,
            Action lost)
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
        public bool Lost { get; set; }
        public string HostName { get; set; } = "unknown";
        public string OperatingSystem { get; set; } = "unknown";
        public string UserName { get; set; } = "unknown";
        public string BuildVersion { get; set; } = "unknown";
        public bool SupportsPlugins { get; set; }

        public void StartHeartbeat() => _heartbeatTask = RunHeartbeatAsync();
        public void StopHeartbeat() => _heartbeatCts.Cancel();

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
                    catch (RemoteToolHostException ex) when (ex.Code == RemoteToolErrorCodes.LeaseLost)
                    {
                        _lost();
                        return;
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
                _lost();
            }
        }
    }

    private sealed class HostSession : IAsyncDisposable
    {
        private readonly RemoteToolHostConnection _connection;

        private HostSession(McpClient client, RemoteToolHostConnection connection)
        {
            Client = client;
            _connection = connection;
        }

        public McpClient Client { get; }

        public static async Task<HostSession> CreateAsync(
            RemoteToolHostConnection connection,
            CancellationToken cancellationToken)
        {
            try
            {
                var client = await McpClient.CreateAsync(
                    connection.Transport,
                    new McpClientOptions
                    {
                        ProtocolVersion = RemoteToolHostProtocol.McpProtocolVersion
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return new HostSession(client, connection);
            }
            catch (OperationCanceledException)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
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
