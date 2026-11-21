using System.Diagnostics;
using DotCraft.AppServer;
using DotCraft.Processes;
using Microsoft.Extensions.Logging;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Channels;

public sealed partial class ChannelHost
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopped = false;
        _permanentlyFailed = false;
        _consecutiveFailures = 0;
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _runCts.Token;

        using var channelScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Module"] = "ExternalChannel",
            ["Channel"] = Name
        });
        _logger.LogInformation("Starting external channel");

        try
        {
            while (!ct.IsCancellationRequested && !_permanentlyFailed)
            {
                try
                {
                    await RunManagedWebSocketCycleAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;

                    if (_consecutiveFailures >= _maxConsecutiveFailures)
                    {
                        _permanentlyFailed = true;
                        _logger.LogError(
                            ex,
                            "External channel permanently failed after {FailureCount} consecutive failures",
                            _consecutiveFailures);
                        break;
                    }

                    var backoff = CalculateBackoff(_consecutiveFailures);
                    _logger.LogWarning(
                        ex,
                        "External channel failed on attempt {FailureCount}/{MaxFailures}; retrying in {BackoffSeconds}s",
                        _consecutiveFailures,
                        _maxConsecutiveFailures,
                        backoff.TotalSeconds);

                    await Task.Delay(backoff, ct);
                }
            }
        }
        finally
        {
            _stopped = true;
            StopHeartbeatTimer();
            _logger.LogInformation("External channel {Channel} stopped", Name);
        }
    }

    public async Task StopAsync()
    {
        _stopped = true;
        _permanentlyFailed = false;
        StopHeartbeatTimer();

        if (_runCts is { } cts)
        {
            await cts.CancelAsync();
        }

        await TerminateAdapterProcessAsync();

        _wsAttachTcs?.TrySetCanceled();

        var connection = _connection;
        var transport = _transport;
        ClearCurrentSession(connection);
        connection?.CancelAllSubscriptions();

        if (transport is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    public bool TryAttachTransport(IAppServerTransport transport, AppServerConnection connection) =>
        connection.IsClientReady && _wsAttachTcs?.TrySetResult((transport, connection)) == true;

    private ManagedChildProcess SpawnAdapterProcess()
    {
        var launch = _channelPluginLaunch ?? throw new InvalidOperationException("The channel plugin is not staged.");
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = _hostWorkspacePath
        };
        launch.ClearQr();
        launch.Configure(startInfo, _hostWorkspacePath);
        var (wsUrl, token) = ResolveManagedWebSocketEndpoint();
        startInfo.Environment["DOTCRAFT_CHANNEL_TRANSPORT"] = "websocket";
        startInfo.Environment["DOTCRAFT_CHANNEL_WS_URL"] = wsUrl;
        startInfo.Environment["DOTCRAFT_CHANNEL_WS_TOKEN"] = token ?? string.Empty;
        return _managedChildProcessFactory(startInfo);
    }

    private (string WsUrl, string? Token) ResolveManagedWebSocketEndpoint()
    {
        var appServerConfig = _appConfigMonitor?.Current.GetSection<AppServerConfig>("AppServer")
            ?? throw new InvalidOperationException(
                "Managed WebSocket channels require an AppServer configuration monitor.");

        if (appServerConfig.Mode is not (AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket))
            throw new InvalidOperationException(
                "Managed WebSocket channels require AppServer WebSocket mode.");

        var wsConfig = appServerConfig.WebSocket;
        if (wsConfig.Port <= 0)
            throw new InvalidOperationException(
                "Managed WebSocket channels require a valid AppServer WebSocket port.");

        return (BuildManagedWebSocketUrl(wsConfig.Host, wsConfig.Port), wsConfig.Token);
    }

    private static string BuildManagedWebSocketUrl(string host, int port)
    {
        var connectHost = host.Trim();
        if (string.IsNullOrWhiteSpace(connectHost) || connectHost == "0.0.0.0")
            connectHost = "127.0.0.1";
        else if (connectHost == "::")
            connectHost = "::1";
        if (connectHost.Contains(':') && !connectHost.StartsWith('['))
            connectHost = $"[{connectHost}]";
        return $"ws://{connectHost}:{port}/ws";
    }

    private void AppendLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        var stamped = $"{DateTimeOffset.UtcNow:O} {line}";
        lock (_recentLogLock)
        {
            _recentLogLines.Enqueue(stamped);
            while (_recentLogLines.Count > MaxLogLines)
                _recentLogLines.Dequeue();
        }
    }

    private async Task ForwardProcessOutputAsync(TextReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null)
                    break;
                AppendLogLine(line);
                _logger.LogWarning(
                    "Channel adapter output: {AdapterOutput}",
                    line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* output forwarding is best-effort */ }
    }

    private async Task TerminateAdapterProcessAsync()
    {
        if (Interlocked.Exchange(ref _adapterProcess, null) is not { } adapterProcess)
            return;

        try
        {
            await adapterProcess.DisposeAsync();
        }
        catch { /* best-effort */ }
    }

    private async Task RunManagedWebSocketCycleAsync(CancellationToken ct)
    {
        _wsAttachTcs = new TaskCompletionSource<(IAppServerTransport, AppServerConnection)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var adapterProcess = SpawnAdapterProcess();
            _adapterProcess = adapterProcess;
            var process = adapterProcess.Process;

            _ = ForwardProcessOutputAsync(process.StandardOutput, ct);
            _ = ForwardProcessOutputAsync(process.StandardError, ct);

            _logger.LogInformation("Channel adapter spawned with process {ProcessId}", process.Id);

            var attachTask = _wsAttachTcs.Task;
            var exitTask = process.WaitForExitAsync(ct);
            var completed = await Task.WhenAny(attachTask, exitTask);
            if (completed == exitTask)
            {
                await exitTask;
                throw new InvalidOperationException(
                    $"Managed WebSocket adapter process exited before attach (exit code {process.ExitCode}).");
            }

            var (transport, connection) = await attachTask;

            _transport = transport;
            _connection = connection;
            PublishToolBinding(transport, connection);

            _logger.LogInformation(
                "Channel adapter connected as client {ClientName}",
                connection.ClientInfo?.Name ?? "unknown");

            StartHeartbeatTimer();
            var handler = _requestHandlerFactory.Create(connection, transport);
            var messageLoopTask = RunMessageLoopAsync(transport, connection, handler, ct);
            var completedAfterAttach = await Task.WhenAny(messageLoopTask, exitTask);
            if (completedAfterAttach == exitTask)
            {
                await exitTask;
                await transport.DisposeAsync();
            }

            await messageLoopTask;

            var exitedBeforeTerminate = !ct.IsCancellationRequested && process.HasExited;
            int? exitCodeBeforeTerminate = exitedBeforeTerminate ? process.ExitCode : null;

            await TerminateAdapterProcessAsync();

            if (exitedBeforeTerminate && exitCodeBeforeTerminate is { } exitCode)
            {
                _logger.LogWarning("Managed WebSocket adapter exited with code {ExitCode}", exitCode);

                if (exitCode != 0)
                    throw new InvalidOperationException(
                        $"Managed WebSocket adapter process exited with code {exitCode}");
            }

            _consecutiveFailures = 0;
        }
        finally
        {
            _wsAttachTcs = null;
            StopHeartbeatTimer();
            await TerminateAdapterProcessAsync();
        }
    }

    private void StartHeartbeatTimer()
    {
        StopHeartbeatTimer();
        _heartbeatTimer = new Timer(
            __ => _ = SendHeartbeatAsync(),
            state: null,
            dueTime: HeartbeatInterval,
            period: HeartbeatInterval);
    }

    private void StopHeartbeatTimer()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    private async Task SendHeartbeatAsync()
    {
        if (_stopped || _transport == null || _connection is not { IsClientReady: true })
            return;

        try
        {
            await _transport.RequestAsync(
                Contract.AppServerRpc.ExtChannelHeartbeat,
                new DotCraft.Protocol.RpcEmpty(),
                timeout: HeartbeatTimeout);
        }
        catch (OperationCanceledException) when (!_stopped && _runCts is { IsCancellationRequested: false })
        {
            _logger.LogError("External channel heartbeat timed out; reconnecting");

            // Dispose only the transport so the host lifetime can reconnect.
            if (_transport is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External channel heartbeat failed");
        }
    }

    private TimeSpan CalculateBackoff(int failures)
    {
        var seconds = Math.Min(
            _initialBackoff.TotalSeconds * Math.Pow(2, failures - 1),
            _maxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

}
