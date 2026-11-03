using System.Diagnostics;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DotCraft.AppServer;

public sealed partial class AppServerHost
{
    private readonly Stopwatch _startupTimer = new();

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _startupTimer.Restart();
        using var moduleScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Module"] = "AppServer"
        });
        var appServerConfig = runtime.Config.GetSection<AppServerConfig>("AppServer");
        if (!AppServerWorkspaceLock.TryAcquire(runtime.Paths, out var workspaceLock, out var existingLock))
        {
            var owner = existingLock is null
                ? "another live process"
                : $"pid {existingLock.Pid}";
            throw new InvalidOperationException(
                $"DotCraft AppServer workspace lock is already held by {owner}: {AppServerWorkspaceLock.GetLockFilePath(runtime.Paths.Data.RootPath)}");
        }

        if (workspaceLock is null)
            throw new InvalidOperationException("AppServer workspace lock acquisition returned no lock file.");

        try
        {
            if (existingLock is not null)
            {
                _logger.LogInformation("Recovered stale AppServer workspace lock");
                AnsiConsole.MarkupLine("[grey][[AppServer]][/] Recovered stale appserver.lock");
            }

            var lockInfo = CreateLockInfo(appServerConfig);
            workspaceLock.Publish(lockInfo);

            void PublishWebSocketEndpoint(string endpoint)
            {
                var endpoints = new Dictionary<string, string>(lockInfo.Endpoints, StringComparer.OrdinalIgnoreCase)
                {
                    ["appServerWebSocket"] = endpoint
                };
                workspaceLock.Publish(lockInfo with { Endpoints = endpoints });
            }

            var moduleRegistry = runtime.Services.GetRequiredService<ModuleRegistry>();
            var runtimeStarted = false;
            var runtimeEventsCleanupRequired = false;

            try
            {
                LogStartupStage("workspace-lock");
                await runtime.StartAsync(moduleRegistry, cancellationToken);
                runtimeStarted = true;
                LogStartupStage("runtime-start");
                _appServerFeature = _services.GetService<IWorkspaceRuntimeAppServerFeatureFactory>()?.Create(_services);
                if (_appServerFeature != null)
                {
                    _appServerFeature.AutomationUpdated += BroadcastAutomationUpdated;
                    _appServerFeature.AutomationRunUpdated += BroadcastAutomationRunUpdated;
                    await _appServerFeature.StartAsync(
                        new WorkspaceRuntimeAppServerFeatureContext(
                            _services,
                            runtime.Config,
                            runtime.Paths,
                            moduleRegistry,
                            runtime.SessionService,
                            runtime.DreamsService,
                            emitBackgroundJobResult: OnBackgroundJobResultProduced),
                        cancellationToken);
                }

                LogStartupStage("appserver-feature-start");
                runtimeEventsCleanupRequired = true;
                SubscribeRuntimeEvents();
                StartPluginRegistrySync(cancellationToken);

                LogStartupStage("subscriptions-and-background-sync");
                switch (appServerConfig.Mode)
                {
                    case AppServerMode.WebSocket:
                        await RunWebSocketOnlyAsync(appServerConfig.WebSocket, PublishWebSocketEndpoint, cancellationToken);
                        break;

                    case AppServerMode.StdioAndWebSocket:
                        await RunStdioWithWebSocketAsync(appServerConfig.WebSocket, PublishWebSocketEndpoint, cancellationToken);
                        break;

                    default:
                        await RunStdioOnlyAsync(cancellationToken);
                        break;
                }
            }
            finally
            {
                try
                {
                    if (runtimeEventsCleanupRequired)
                        UnsubscribeRuntimeEvents();
                }
                finally
                {
                    try
                    {
                        if (_appServerFeature != null)
                        {
                            var feature = _appServerFeature;
                            _appServerFeature = null;
                            feature.AutomationUpdated -= BroadcastAutomationUpdated;
                            feature.AutomationRunUpdated -= BroadcastAutomationRunUpdated;
                            try
                            {
                                await feature.StopAsync(CancellationToken.None);
                            }
                            finally
                            {
                                await feature.DisposeAsync();
                            }
                        }
                    }
                    finally
                    {
                        if (runtimeStarted)
                            await runtime.StopAsync(CancellationToken.None);
                    }
                }
            }
        }
        finally
        {
            workspaceLock.DeleteAfterDispose();
        }

        AnsiConsole.MarkupLine("[grey][[AppServer]][/] AppServer stopped");
        _logger.LogInformation("AppServer stopped");
    }

    private void LogStartupStage(string stage)
    {
        _logger.LogInformation(
            "Startup stage {Stage} completed in {ElapsedMs}ms", stage, _startupTimer.Elapsed.TotalMilliseconds);
        _startupTimer.Restart();
    }
}
