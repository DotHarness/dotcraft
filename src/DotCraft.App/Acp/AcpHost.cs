using DotCraft.AppServer;
using DotCraft.CLI;
using DotCraft.Commands.Core;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Memory;
using DotCraft.Workspaces;
using AcpStdioTransport = DotHarness.Acp.Transport.StdioTransport;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DotCraft.Acp;

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
        var commandRegistry = sp.GetService<CommandRegistry>();

        using var acpLogger = AcpLogger.Create(paths.Data.RootPath, config.DebugMode);
        await using var transport = new AcpLoggingTransport(
            AcpStdioTransport.OpenStandardStreams(),
            acpLogger,
            TimeSpan.FromSeconds(acpConfig.WriteTimeoutSeconds));

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

        var planStore = new PlanStore(paths.Data.RootPath);
        var bridge = new AcpBridgeHandler(
            wire,
            paths.WorkspacePath,
            hookRunner,
            planStore,
            acpLogger,
            _appServerProcess,
            extForwardTimeoutSeconds: acpConfig.ExtForwardTimeoutSeconds,
            permissionRequestTimeoutSeconds: acpConfig.PermissionRequestTimeoutSeconds,
            commandRegistry: commandRegistry);

        AnsiConsole.MarkupLine("[green][[ACP]][/] DotCraft ACP bridge ready (stdio → AppServer)");
        try
        {
            await bridge.RunAsync(transport, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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
