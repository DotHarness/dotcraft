using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Hooks;
using DotCraft.Lsp;
using DotCraft.Plugins;
using DotCraft.Skills;
using Microsoft.Extensions.Logging;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed record ManagedPluginRefreshResult(
    PluginDiscoveryResult? Discovery,
    IReadOnlyList<string> UpdatedPluginIds,
    List<Contract.MarketplaceFailure> Errors)
{
    public bool Changed => UpdatedPluginIds.Count > 0;
}

internal sealed class AppServerManagedPluginRefresher(
    SkillsLoader? skillsLoader,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    string? hostWorkspacePath,
    IReadOnlyList<string>? builtInPluginSourceRoots,
    AppServerMcpConfigService mcpConfig,
    LspServerManager? lspServerManager,
    IContextPageManager? contextPageManager,
    WorkspaceConfigEditor workspaceConfig,
    AppServerRuntimeConfigRefresher runtimeConfig,
    HookRunner? hookRunner,
    AppServerPluginManagementState managementState,
    ILogger? logger)
{
    public PluginDiscoveryResult RefreshPluginRuntime()
    {
        var current = appConfigMonitor?.Current ?? new AppConfig();
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Contract.AppServerMethodNames.PluginList);

        var workspacePath = hostWorkspacePath
                            ?? Directory.GetParent(workspaceCraftPath)?.FullName
                            ?? throw new InvalidOperationException("The workspace path could not be resolved from DataPath.");
        return PluginRuntimeConfigurator.ConfigureSkillsLoader(
            skillsLoader,
            current,
            workspacePath,
            workspaceCraftPath,
            builtInPluginSourceRoots,
            logger);
    }

    private void RefreshHooksAfterPluginChange()
    {
        if (hookRunner == null)
            return;

        AppServerHookRuntimeRefresher.Refresh(
            hookRunner,
            appConfigMonitor,
            workspaceCraftPath,
            hostWorkspacePath,
            workspaceConfig,
            runtimeConfig,
            builtInPluginSourceRoots);
    }

    // Marketplace sources live in the user-global config file until the next reload, while
    // workspace-declared lists keep precedence over the global list.
    public void SyncConfiguredMarketplaces()
    {
        var current = appConfigMonitor?.Current;
        if (current == null || string.IsNullOrEmpty(workspaceCraftPath))
            return;

        var workspaceEntries = PluginsConfigPersistence.ReadPluginRegistries(
            Path.Combine(workspaceCraftPath, "config.json"));
        current.Plugins.PluginRegistries = workspaceEntries.Count > 0
            ? [.. workspaceEntries]
            : workspaceConfig.PersonalConfigPath is { } personalConfigPath
                ? [.. PluginsConfigPersistence.ReadPluginRegistries(personalConfigPath)]
                : [];
    }

    public static void PublishPluginRevision(string dataPath) =>
        AtomicConfigDocument.Update(
            Path.Combine(dataPath, "imports", "revision.json"),
            root => root["revision"] = Guid.NewGuid().ToString("N"));

    public static bool IsBlockingDeployDiagnosticForPlugin(PluginDiagnostic diagnostic, string pluginId) =>
        (diagnostic.Severity == PluginDiagnosticSeverity.Error || diagnostic.Code == "BuiltInPluginNotFound")
        && !string.IsNullOrWhiteSpace(diagnostic.PluginId)
        && PluginIds.EqualsCanonical(diagnostic.PluginId, pluginId);

    public async Task<PluginDiscoveryResult> FinalizePluginContributionRefreshAsync(
        string source,
        CancellationToken ct)
    {
        var discovery = RefreshPluginRuntime();
        await mcpConfig.ReconnectEffectiveRuntimeAsync(
                await mcpConfig.GetWorkspaceServersAsync(ct).ConfigureAwait(false),
                ct)
            .ConfigureAwait(false);
        await runtimeConfig.RefreshMcpRuntimesAsync(ct).ConfigureAwait(false);
        await ReconnectEffectiveLspRuntimeAsync(ct).ConfigureAwait(false);
        RefreshHooksAfterPluginChange();
        appConfigMonitor?.NotifyChanged(
            source,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp, ConfigChangeRegions.Hooks]);
        AppServerContextInvalidation.MarkSkills(contextPageManager);
        return discovery;
    }

    public async Task<PluginDiagnostic?> QuiesceRootBackedContributionsAsync(
        string pluginId,
        CancellationToken ct)
    {
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var originalDisabled = current.Plugins.DisabledPlugins.ToList();
        var temporarilyDisabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(originalDisabled).ToList();
        if (!temporarilyDisabled.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            temporarilyDisabled.Add(pluginId);

        PluginDiagnostic? failure = null;
        current.Plugins.DisabledPlugins = temporarilyDisabled;
        try
        {
            await RestoreRootBackedContributionsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = PluginDiagnostic.Error(
                "PluginContributionQuiesceFailed",
                "The plugin's root-backed contributions could not be stopped.",
                pluginId,
                parameters: new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["phase"] = System.Text.Json.JsonSerializer.SerializeToElement("quiesce"),
                    ["failureType"] = System.Text.Json.JsonSerializer.SerializeToElement(exception.GetType().Name)
                });
        }
        finally
        {
            current.Plugins.DisabledPlugins = originalDisabled;
        }

        if (failure == null)
            return null;

        try
        {
            await RestoreRootBackedContributionsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
        return failure;
    }

    public async Task RestoreRootBackedContributionsAsync(CancellationToken ct)
    {
        RefreshPluginRuntime();
        await mcpConfig.ReconnectEffectiveRuntimeAsync(
                await mcpConfig.GetWorkspaceServersAsync(ct).ConfigureAwait(false),
                ct)
            .ConfigureAwait(false);
        await ReconnectEffectiveLspRuntimeAsync(ct).ConfigureAwait(false);
        RefreshHooksAfterPluginChange();
        AppServerContextInvalidation.MarkSkills(contextPageManager);
    }

    public async Task RunStartupSyncAsync(
        string craftHome,
        PluginRegistrySyncService syncService,
        Action<Contract.PluginSnapshotUpdatedNotification> broadcastSnapshotUpdated,
        CancellationToken ct)
    {
        try
        {
            await ReconcileStartupAsync(
                    craftHome,
                    source: "startup/pluginReconcile",
                    notifyWhenUnchanged: false,
                    broadcastSnapshotUpdated,
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Startup managed plugin reconciliation failed");
        }

        try
        {
            var pluginsConfig = appConfigMonitor?.Current.Plugins ?? new AppConfig.PluginsConfig();
            if (!await syncService.SyncDueAsync(pluginsConfig, force: false, ct).ConfigureAwait(false))
                return;

            await ReconcileStartupAsync(
                    craftHome,
                    source: "startup/registrySync",
                    notifyWhenUnchanged: true,
                    broadcastSnapshotUpdated,
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Plugin registry sync failed");
        }
    }

    public async Task<ManagedPluginRefreshResult> RefreshManagedPluginsLockedAsync(
        string dataPath,
        string? marketplaceName,
        string source,
        bool notifyWhenUnchanged,
        CancellationToken ct)
    {
        SyncConfiguredMarketplaces();
        var diagnostics = new List<PluginDiagnostic>();
        var deployer = new BuiltInPluginDeployer(
            Path.Combine(dataPath, "plugins"),
            builtInPluginSourceRoots,
            appConfigMonitor?.Current.Plugins ?? new AppConfig.PluginsConfig(),
            dataPath,
            logger);
        var pending = deployer.GetPendingUpdates(diagnostics, marketplaceName);
        if (pending.Count == 0 && !notifyWhenUnchanged)
        {
            PluginDiagnosticsLogger.Write(diagnostics, logger);
            return new ManagedPluginRefreshResult(null, [], []);
        }

        var updatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pluginKey in pending)
        {
            ct.ThrowIfCancellationRequested();

            var pluginId = PluginIds.IdFromKey(pluginKey);
            var quiesceFailure = await QuiesceRootBackedContributionsAsync(pluginId, ct)
                .ConfigureAwait(false);
            if (quiesceFailure != null)
            {
                diagnostics.Add(quiesceFailure);
                continue;
            }

            try
            {
                var deployment = deployer.DeployPlugin(pluginKey);
                diagnostics.AddRange(deployment);
                if (!deployment.Any(diagnostic => IsBlockingDeployDiagnosticForPlugin(diagnostic, pluginId)))
                    updatedIds.Add(pluginId);
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
        PluginDiscoveryResult? discovery = null;
        if (updatedIds.Count > 0)
        {
            PublishPluginRevision(dataPath);
            discovery = await FinalizePluginContributionRefreshAsync(source, ct)
                .ConfigureAwait(false);
        }
        else if (notifyWhenUnchanged)
        {
            discovery = RefreshPluginRuntime();
            appConfigMonitor?.NotifyChanged(source, [ConfigChangeRegions.Plugins]);
        }

        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error)
            .Select(diagnostic => new Contract.MarketplaceFailure
            {
                Name = discovery?.Plugins.FirstOrDefault(plugin =>
                        PluginIds.EqualsCanonical(plugin.Manifest.Id, diagnostic.PluginId ?? string.Empty))?.MarketplaceName
                    ?? marketplaceName ?? diagnostic.PluginId ?? "plugins",
                Code = diagnostic.Code,
                Message = diagnostic.Message
            })
            .ToList();

        var sortedUpdatedIds = updatedIds
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        return new ManagedPluginRefreshResult(discovery, sortedUpdatedIds, errors);
    }

    private Task ReconcileStartupAsync(
        string craftHome,
        string source,
        bool notifyWhenUnchanged,
        Action<Contract.PluginSnapshotUpdatedNotification> broadcastSnapshotUpdated,
        CancellationToken ct) =>
        managementState.RunMutationAsync(async token =>
        {
            var result = await RefreshManagedPluginsLockedAsync(
                    craftHome,
                    marketplaceName: null,
                    source,
                    notifyWhenUnchanged,
                    token)
                .ConfigureAwait(false);

            if (result.Changed || notifyWhenUnchanged)
            {
                var snapshotRevision = managementState.AdvanceSnapshotRevision(0);
                broadcastSnapshotUpdated(new Contract.PluginSnapshotUpdatedNotification
                {
                    SnapshotRevision = snapshotRevision,
                    PluginIds = Protocol.Optional<IReadOnlyList<string>>.FromValue(result.UpdatedPluginIds)
                });
            }

            return true;
        }, ct);

    private async Task ReconnectEffectiveLspRuntimeAsync(CancellationToken ct)
    {
        if (lspServerManager == null)
            return;

        ct.ThrowIfCancellationRequested();
        await lspServerManager.InitializeAsync(ct).ConfigureAwait(false);
    }
}
