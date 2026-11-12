using DotCraft.Configuration;
using DotCraft.Plugins;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
    private async Task<(PluginDiscoveryResult Discovery, List<Contract.MarketplaceFailure> Errors)> RefreshManagedPluginsAsync(
        string? marketplaceName,
        CancellationToken ct)
    {
        SyncConfiguredMarketplaces();
        var diagnostics = new List<PluginDiagnostic>();
        var dataPath = PluginDataPath("user");
        var deployer = new BuiltInPluginDeployer(
            Path.Combine(dataPath, "plugins"),
            builtInPluginSourceRoots,
            appConfigMonitor?.Current.Plugins ?? new AppConfig.PluginsConfig(),
            workspaceConfig.UserDataPath);
        var pending = deployer.GetPendingUpdates(diagnostics, marketplaceName);
        var changed = false;
        foreach (var pluginId in pending)
        {
            var quiesceFailure = await QuiesceRootBackedContributionsAsync(pluginId, ct).ConfigureAwait(false);
            if (quiesceFailure != null)
            {
                diagnostics.Add(quiesceFailure);
                continue;
            }

            try
            {
                var deployment = deployer.DeployPlugin(pluginId);
                diagnostics.AddRange(deployment);
                changed |= !deployment.Any(diagnostic => IsBlockingDeployDiagnosticForPlugin(diagnostic, pluginId));
            }
            finally
            {
                try
                {
                    await RestoreRootBackedContributionsAsync(ct).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    diagnostics.Add(PluginDiagnostic.Error(
                        "PluginContributionRestoreFailed",
                        $"Plugin '{pluginId}' contributions could not be refreshed: {exception.Message}",
                        pluginId));
                }
            }
        }

        PluginDiagnosticsLogger.Write(diagnostics, logger);
        PluginDiscoveryResult discovery;
        if (changed)
        {
            PublishPluginRevision(dataPath);
            discovery = await FinalizePluginContributionRefreshAsync(
                Protocol.AppServer.AppServerMethodNames.MarketplaceRefresh, ct).ConfigureAwait(false);
        }
        else
        {
            discovery = NotifyMarketplaceChanged(Protocol.AppServer.AppServerMethodNames.MarketplaceRefresh);
        }

        var errors = diagnostics.Where(diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error)
            .Select(diagnostic => new Contract.MarketplaceFailure
            {
                Name = discovery.Plugins.FirstOrDefault(plugin =>
                        PluginIds.EqualsCanonical(plugin.Manifest.Id, diagnostic.PluginId ?? string.Empty))?.MarketplaceName
                    ?? marketplaceName ?? diagnostic.PluginId ?? "plugins",
                Code = diagnostic.Code,
                Message = diagnostic.Message
            })
            .ToList();
        return (discovery, errors);
    }
}
