using DotCraft.CLI;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Memory;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using DotCraft.AppServer;

namespace DotCraft.Acp;

/// <summary>
/// Host for ACP mode: stdio to the IDE and AppServer Session Wire protocol for session/agent work.
/// </summary>
public sealed class AcpHost(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths) : IDotCraftHost
{
    private AppServerProcess? _appServerProcess;
    private WebSocketClientConnection? _wsConnection;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var acpConfig = config.GetSection<AcpConfig>("Acp");
        var hookRunner = sp.GetService<HookRunner>();
        var customCommandLoader = sp.GetService<CustomCommandLoader>();

        using var acpLogger = AcpLogger.Create(paths.CraftPath, config.DebugMode);

        await using var transport = AcpTransport.CreateStdio();
        transport.Logger = acpLogger;
        transport.WriteTimeout = TimeSpan.FromSeconds(acpConfig.WriteTimeoutSeconds);
        transport.StartReaderLoop();

        AppServerWireClient wire;
        if (!string.IsNullOrWhiteSpace(acpConfig.AppServerUrl))
        {
            var wsUri = new Uri(acpConfig.AppServerUrl);
            AnsiConsole.MarkupLine($"[grey][[ACP]][/] Connecting to AppServer at {Markup.Escape(wsUri.ToString())}...");
            _wsConnection = await WebSocketClientConnection.ConnectAsync(
                wsUri,
                acpConfig.AppServerToken,
                cancellationToken);
            wire = _wsConnection.Wire;
        }
        else
        {
            AnsiConsole.MarkupLine("[grey][[ACP]][/] Starting AppServer subprocess...");
            _appServerProcess = await AppServerProcess.StartWithoutHandshakeAsync(
                dotcraftBin: acpConfig.AppServerBin,
                workspacePath: paths.WorkspacePath,
                ct: cancellationToken);

            acpLogger?.LogEvent($"AppServer subprocess started (PID {_appServerProcess.ProcessId})");

            _appServerProcess.OnCrashed += () =>
            {
                AnsiConsole.MarkupLine("[red][[ACP]][/] AppServer subprocess exited unexpectedly.");
                var stderr = _appServerProcess.RecentStderr.Trim();
                if (!string.IsNullOrEmpty(stderr))
                    acpLogger?.LogEvent($"AppServer stderr (tail): {stderr}");
                acpLogger?.LogEvent(
                    $"AppServer subprocess exited (PID {_appServerProcess.ProcessId}, code {_appServerProcess.ExitCode})");
            };

            if (!_appServerProcess.IsRunning)
            {
                // Allow the stderr forwarder to read any lines written before exit.
                await Task.Delay(150, cancellationToken);
                var stderr = _appServerProcess.RecentStderr.Trim();
                var msg =
                    $"AppServer subprocess exited immediately (code {_appServerProcess.ExitCode}). " +
                    (string.IsNullOrEmpty(stderr) ? "No stderr captured." : stderr);
                acpLogger?.LogError(msg);
                throw new InvalidOperationException(msg);
            }

            wire = _appServerProcess.Wire;
        }

        var planStore = new PlanStore(paths.CraftPath);
        var bridge = new AcpBridgeHandler(
            transport,
            wire,
            paths.WorkspacePath,
            customCommandLoader,
            hookRunner,
            planStore,
            acpLogger,
            _appServerProcess,
            extForwardTimeoutSeconds: acpConfig.ExtForwardTimeoutSeconds,
            permissionRequestTimeoutSeconds: acpConfig.PermissionRequestTimeoutSeconds);

        // When the transport detects client disconnection via heartbeat failures,
        // signal cancellation so the bridge exits and the client can reconnect.
        using var disconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        transport.OnDisconnected += () =>
        {
            AnsiConsole.MarkupLine("[red][[ACP]][/] Transport disconnected (heartbeat failure). Shutting down bridge.");
            acpLogger?.LogEvent("Transport disconnected via heartbeat; cancelling bridge");
            try { disconnectCts.Cancel(); } catch { /* disposed */ }
        };

        AnsiConsole.MarkupLine("[green][[ACP]][/] DotCraft ACP bridge ready (stdio → AppServer)");
        await bridge.RunAsync(disconnectCts.Token);
        AnsiConsole.MarkupLine("[grey][[ACP]][/] ACP bridge stopped");
    }

    public async ValueTask DisposeAsync()
    {
        if (_appServerProcess != null)
            await _appServerProcess.DisposeAsync();

        if (_wsConnection != null)
            await _wsConnection.DisposeAsync();
    }
}
