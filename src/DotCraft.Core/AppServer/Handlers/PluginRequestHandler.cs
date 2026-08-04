using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Hooks;
using DotCraft.Lsp;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using DotCraft.Skills;
using Contract = DotCraft.Protocol.AppServer;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using PluginDiagnostic = DotCraft.Plugins.PluginDiagnostic;

namespace DotCraft.AppServer;

internal sealed class PluginRequestHandler(
    IAppServerTransport transport,
    SkillsLoader? skillsLoader,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    string? hostWorkspacePath,
    IReadOnlyList<string>? builtInPluginSourceRoots,
    AppServerMcpConfigService mcpConfig,
    LspServerManager? lspServerManager,
    IContextPageManager? contextPageManager,
    AppBindingService? appBindingService,
    WorkspaceConfigEditor workspaceConfig,
    AppServerRuntimeConfigRefresher runtimeConfig,
    HookRunner? hookRunner) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.PluginList, HandlePluginListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.PluginView, HandlePluginViewAsync);
        table.Map(Protocol.AppServer.AppServerRpc.PluginInstall, HandlePluginInstallAsync);
        table.Map(Protocol.AppServer.AppServerRpc.PluginInstallLocal, HandlePluginInstallLocalAsync);
        table.Map(Protocol.AppServer.AppServerRpc.PluginRemove, HandlePluginRemoveAsync);
        table.Map(Protocol.AppServer.AppServerRpc.PluginSetEnabled, HandlePluginSetEnabledAsync);
        table.Map(Protocol.AppServer.AppServerRpc.MarketplaceAdd, HandleMarketplaceAddAsync);
        table.Map(Protocol.AppServer.AppServerRpc.MarketplaceRemove, HandleMarketplaceRemoveAsync);
        table.Map(Protocol.AppServer.AppServerRpc.MarketplaceRefresh, HandleMarketplaceRefreshAsync);
    }

    private Task<AppServerTypedResult<Contract.PluginListResult>> HandlePluginListAsync(
        AppServerTypedRequest<Contract.PluginListParams> request,
        CancellationToken ct)
    {
        _ = ct;
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginList);

        var p = request.Params;
        var discovery = RefreshPluginRuntime();
        var diagnostics = discovery.Diagnostics.ToList();
        var hookSummaries = BuildPluginHookSummaryIndex(discovery, diagnostics);
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugins = discovery.Plugins
            .Where(plugin => Read(p.IncludeDisabled) != false || plugin.Enabled)
            .Select(plugin => MapPluginToWire(plugin, diagnostics, hookSummaries, mcpSummaries, lspSummaries))
            .OrderBy(plugin => Read(plugin.DisplayName), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(AppServerTypedResult<Contract.PluginListResult>.FromResult(
            new Contract.PluginListResult
            {
                Plugins = plugins,
                Marketplaces = BuildMarketplaceList(discovery),
                Diagnostics = diagnostics.Select(MapPluginDiagnosticToWire).ToList()
            }));
    }

    private async Task<AppServerTypedResult<Contract.MarketplaceAddResult>> HandleMarketplaceAddAsync(
        AppServerTypedRequest<Contract.MarketplaceAddParams> request,
        CancellationToken ct)
    {
        RequireMarketplaceSupport(Protocol.AppServer.AppServerMethodNames.MarketplaceAdd);
        var p = request.Params;

        var result = await RunMarketplaceOperationAsync(
            () => CreateMarketplaceManager().AddAsync(
                new MarketplaceAddRequest(
                    Read(p.Source) ?? string.Empty,
                    Read(p.Ref),
                    Read(p.SparsePaths)?.ToList(),
                    Read(p.MarketplacePath)),
                ct)).ConfigureAwait(false);

        var discovery = NotifyMarketplaceChanged(Protocol.AppServer.AppServerMethodNames.MarketplaceAdd);
        return AppServerTypedResult<Contract.MarketplaceAddResult>.FromResult(
            new Contract.MarketplaceAddResult
            {
                Marketplace = MapMarketplaceToWire(result.Marketplace, discovery),
                AlreadyAdded = result.AlreadyAdded
            });
    }

    private Task<AppServerTypedResult<Contract.MarketplaceRemoveResult>> HandleMarketplaceRemoveAsync(
        AppServerTypedRequest<Contract.MarketplaceRemoveParams> request,
        CancellationToken ct)
    {
        _ = ct;
        RequireMarketplaceSupport(Protocol.AppServer.AppServerMethodNames.MarketplaceRemove);
        var name = Read(request.Params.Name);
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        MarketplaceRemoveOutcome removed;
        try
        {
            removed = CreateMarketplaceManager().Remove(name);
        }
        catch (MarketplaceException ex)
        {
            throw AppServerErrors.Marketplace(ex.Code, ex.Message, name);
        }

        NotifyMarketplaceChanged(Protocol.AppServer.AppServerMethodNames.MarketplaceRemove);
        return Task.FromResult(AppServerTypedResult<Contract.MarketplaceRemoveResult>.FromResult(
            new Contract.MarketplaceRemoveResult
            {
                Name = removed.Name,
                RemovedRoot = OmitIfNull(removed.RemovedRoot)
            }));
    }

    private async Task<AppServerTypedResult<Contract.MarketplaceRefreshResult>> HandleMarketplaceRefreshAsync(
        AppServerTypedRequest<Contract.MarketplaceRefreshParams> request,
        CancellationToken ct)
    {
        RequireMarketplaceSupport(Protocol.AppServer.AppServerMethodNames.MarketplaceRefresh);
        var result = await RunMarketplaceOperationAsync(
            () => CreateMarketplaceManager().RefreshAsync(Read(request.Params.Name), ct)).ConfigureAwait(false);

        var discovery = NotifyMarketplaceChanged(Protocol.AppServer.AppServerMethodNames.MarketplaceRefresh);
        return AppServerTypedResult<Contract.MarketplaceRefreshResult>.FromResult(
            new Contract.MarketplaceRefreshResult
            {
                Marketplaces = result.Marketplaces.Select(entry => MapMarketplaceToWire(entry, discovery)).ToList(),
                Errors = result.Errors
                .Select(failure => new Contract.MarketplaceFailure
                {
                    Name = failure.Name,
                    Code = failure.Code,
                    Message = failure.Message
                })
                .ToList()
            });
    }

    private void RequireMarketplaceSupport(string method)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(method);
    }

    private static async Task<T> RunMarketplaceOperationAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (MarketplaceException ex)
        {
            throw AppServerErrors.Marketplace(ex.Code, ex.Message);
        }
    }

    private MarketplaceManager CreateMarketplaceManager()
    {
        var configPath = workspaceConfig.PersonalConfigPath;
        return new MarketplaceManager(Path.GetDirectoryName(configPath), configPath);
    }

    // Adding, refreshing, or removing a marketplace changes which plugins are installable but
    // installs nothing, so only the plugin catalog is invalidated.
    private PluginDiscoveryResult NotifyMarketplaceChanged(string source)
    {
        SyncConfiguredMarketplaces();
        var discovery = RefreshPluginRuntime();
        appConfigMonitor?.NotifyChanged(source, [ConfigChangeRegions.Plugins]);
        return discovery;
    }

    // Marketplace sources live in the user-global config file, which the in-memory snapshot
    // does not reflect until the next reload. After a marketplace mutation the snapshot is
    // brought to what a reload would produce, keeping the ordinary precedence rule that a
    // workspace-declared list wins over the global one.
    private void SyncConfiguredMarketplaces()
    {
        var current = appConfigMonitor?.Current;
        if (current == null || string.IsNullOrEmpty(workspaceCraftPath))
            return;

        var workspaceEntries = PluginsConfigPersistence.ReadPluginRegistries(
            Path.Combine(workspaceCraftPath, "config.json"));
        current.Plugins.PluginRegistries = workspaceEntries.Count > 0
            ? [.. workspaceEntries]
            : [.. PluginsConfigPersistence.ReadPluginRegistries(workspaceConfig.PersonalConfigPath)];
    }

    private List<Contract.MarketplaceInfo> BuildMarketplaceList(PluginDiscoveryResult discovery)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            return [];

        try
        {
            return CreateMarketplaceManager()
                .List()
                .Select(entry => MapMarketplaceToWire(entry, discovery))
                .ToList();
        }
        catch (MarketplaceException)
        {
            return [];
        }
    }

    private static Contract.MarketplaceInfo MapMarketplaceToWire(MarketplaceEntry entry, PluginDiscoveryResult discovery) =>
        new()
        {
            Name = entry.Name,
            DisplayName = OmitIfNull(entry.DisplayName),
            SourceType = entry.Kind.ToString().ToLowerInvariant(),
            Source = entry.Source,
            Ref = OmitIfNull(entry.Ref),
            SparsePaths = entry.SparsePaths.ToArray(),
            Root = OmitIfNull(entry.Root),
            LastUpdated = OmitIfNull(entry.LastUpdated),
            Revision = OmitIfNull(entry.Revision),
            Removable = entry.Removable,
            PluginIds = discovery.Plugins
                .Where(plugin => string.Equals(plugin.MarketplaceName, entry.Name, StringComparison.OrdinalIgnoreCase))
                .Select(plugin => plugin.Manifest.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

    private Task<AppServerTypedResult<Contract.PluginViewResult>> HandlePluginViewAsync(
        AppServerTypedRequest<Contract.PluginViewParams> request,
        CancellationToken ct)
    {
        _ = ct;
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginView);

        var id = Read(request.Params.Id);
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var discovery = RefreshPluginRuntime();
        var diagnostics = discovery.Diagnostics.ToList();
        var hookSummaries = BuildPluginHookSummaryIndex(discovery, diagnostics);
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(
            candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, id));
        if (plugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{id}' was not found.");

        return Task.FromResult(AppServerTypedResult<Contract.PluginViewResult>.FromResult(
            new Contract.PluginViewResult
            {
                Plugin = MapPluginToWire(plugin, diagnostics, hookSummaries, mcpSummaries, lspSummaries)
            }));
    }

    private async Task<AppServerTypedResult<Contract.PluginSetEnabledResult>> HandlePluginSetEnabledAsync(
        AppServerTypedRequest<Contract.PluginSetEnabledParams> request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginSetEnabled);

        var id = Read(request.Params.Id);
        var enabled = Read(request.Params.Enabled);
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var pluginId = PluginIds.Canonicalize(id.Trim());
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var before = RefreshPluginRuntime();
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (!beforePlugin.Installed)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' is not installed.");

        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        if (enabled)
            disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        else if (!disabled.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            disabled.Add(pluginId);

        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        var discovery = RefreshPluginRuntime();
        await ReconnectEffectiveMcpRuntimeAsync(await GetWorkspaceMcpServersAsync(ct), ct);
        await ReconnectEffectiveLspRuntimeAsync(ct);
        RefreshHooksAfterPluginChange();
        appConfigMonitor?.NotifyChanged(
            Protocol.AppServer.AppServerMethodNames.PluginSetEnabled,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp, ConfigChangeRegions.Hooks]);
        AppServerContextInvalidation.MarkSkills(contextPageManager);

        var diagnostics = discovery.Diagnostics.ToList();
        var hookSummaries = BuildPluginHookSummaryIndex(discovery, diagnostics);
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (plugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");

        var result = new Contract.PluginSetEnabledResult
        {
            Plugin = MapPluginToWire(plugin, diagnostics, hookSummaries, mcpSummaries, lspSummaries)
        };
        var appListUpdate = TryBuildAppListUpdatedNotification(discovery, pluginId, enabled ? "plugin/enable" : "plugin/disable");
        IReadOnlyList<AppBindingSnapshot> offlineBindings = enabled
            ? []
            : TryMoveActiveAppBindingsOfflineForPlugin(
                TryDiscoverAppBindingCatalog(),
                pluginId,
                "The owning plugin was disabled.",
                "binding.offline.pluginDisabled");
        return await MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
            request.Message,
            result,
            appListUpdate,
            offlineBindings,
            ct);
    }

    private async Task<AppServerTypedResult<Contract.PluginInstallResult>> HandlePluginInstallAsync(
        AppServerTypedRequest<Contract.PluginInstallParams> request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginInstall);

        var id = Read(request.Params.Id);
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var pluginId = PluginIds.Canonicalize(id.Trim());
        var before = RefreshPluginRuntime();
        var beforeDiagnostics = before.Diagnostics.ToList();
        var beforeHookSummaries = BuildPluginHookSummaryIndex(before, beforeDiagnostics);
        var beforeMcpSummaries = BuildPluginMcpSummaryIndex(before, beforeDiagnostics);
        var beforeLspSummaries = BuildPluginLspSummaryIndex(before, beforeDiagnostics);
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (beforePlugin.Installed)
            return AppServerTypedResult<Contract.PluginInstallResult>.FromResult(
                new Contract.PluginInstallResult { Plugin = MapPluginToWire(beforePlugin, beforeDiagnostics, beforeHookSummaries, beforeMcpSummaries, beforeLspSummaries) });
        if (!beforePlugin.Installable)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' is not installable.");

        var deployDiagnostics = new BuiltInPluginDeployer(
                Path.Combine(workspaceCraftPath, "plugins"),
                builtInPluginSourceRoots,
                appConfigMonitor?.Current.Plugins ?? new AppConfig.PluginsConfig())
            .DeployPlugin(pluginId);
        PluginDiagnosticsStore.Shared.Append(deployDiagnostics);
        PluginDiagnosticsLogger.Write(deployDiagnostics);
        if (deployDiagnostics.Any(d => IsBlockingDeployDiagnosticForPlugin(d, pluginId)))
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' could not be installed.");

        return await FinalizeInstalledPluginAsync(
            pluginId,
            Protocol.AppServer.AppServerMethodNames.PluginInstall,
            "plugin/install",
            request.Message,
            ct);
    }

    private static bool IsBlockingDeployDiagnosticForPlugin(PluginDiagnostic diagnostic, string pluginId) =>
        (diagnostic.Severity == PluginDiagnosticSeverity.Error || diagnostic.Code == "BuiltInPluginNotFound")
        && !string.IsNullOrWhiteSpace(diagnostic.PluginId)
        && PluginIds.EqualsCanonical(diagnostic.PluginId, pluginId);

    private async Task<AppServerTypedResult<Contract.PluginInstallResult>> HandlePluginInstallLocalAsync(
        AppServerTypedRequest<Contract.PluginInstallLocalParams> request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginInstallLocal);

        var path = Read(request.Params.Path);
        if (string.IsNullOrWhiteSpace(path))
            throw AppServerErrors.InvalidParams("'path' is required.");

        var install = new LocalPluginInstaller(Path.Combine(workspaceCraftPath, "plugins")).Install(path.Trim());
        PluginDiagnosticsStore.Shared.Append(install.Diagnostics);
        PluginDiagnosticsLogger.Write(install.Diagnostics);
        if (install.PluginId == null)
        {
            var error = install.Diagnostics.FirstOrDefault(d => d.Severity == PluginDiagnosticSeverity.Error);
            throw AppServerErrors.InvalidParams(error?.Message ?? "The selected folder is not a valid plugin.");
        }

        return await FinalizeInstalledPluginAsync(
            install.PluginId,
            Protocol.AppServer.AppServerMethodNames.PluginInstallLocal,
            "plugin/installLocal",
            request.Message,
            ct);
    }

    // Shared tail for plugin/install and plugin/installLocal: clears the id from the
    // workspace disabled list, refreshes runtime, reconciles MCP/LSP, notifies, and
    // returns the installed PluginInfo (with any deferred App Binding notifications).
    private async Task<AppServerTypedResult<Contract.PluginInstallResult>> FinalizeInstalledPluginAsync(
        string pluginId,
        string source,
        string appListReason,
        AppServerIncomingMessage msg,
        CancellationToken ct)
    {
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath!, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        var discovery = RefreshPluginRuntime();
        await ReconnectEffectiveMcpRuntimeAsync(await GetWorkspaceMcpServersAsync(ct), ct);
        await ReconnectEffectiveLspRuntimeAsync(ct);
        RefreshHooksAfterPluginChange();
        appConfigMonitor?.NotifyChanged(
            source,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp, ConfigChangeRegions.Hooks]);
        AppServerContextInvalidation.MarkSkills(contextPageManager);

        var diagnostics = discovery.Diagnostics.ToList();
        var hookSummaries = BuildPluginHookSummaryIndex(discovery, diagnostics);
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (plugin == null || !plugin.Installed)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' could not be installed.");

        var result = new Contract.PluginInstallResult
        {
            Plugin = MapPluginToWire(plugin, diagnostics, hookSummaries, mcpSummaries, lspSummaries)
        };
        return await MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
            msg,
            result,
            TryBuildAppListUpdatedNotification(discovery, pluginId, appListReason),
            [],
            ct);
    }

    private async Task<AppServerTypedResult<Contract.PluginRemoveResult>> HandlePluginRemoveAsync(
        AppServerTypedRequest<Contract.PluginRemoveParams> request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginRemove);

        var id = Read(request.Params.Id);
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var pluginId = PluginIds.Canonicalize(id.Trim());
        var before = RefreshPluginRuntime();
        var beforeDiagnostics = before.Diagnostics.ToList();
        var beforeHookSummaries = BuildPluginHookSummaryIndex(before, beforeDiagnostics);
        var beforeMcpSummaries = BuildPluginMcpSummaryIndex(before, beforeDiagnostics);
        var beforeLspSummaries = BuildPluginLspSummaryIndex(before, beforeDiagnostics);
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (!beforePlugin.Installed)
            return AppServerTypedResult<Contract.PluginRemoveResult>.FromResult(
                new Contract.PluginRemoveResult
                {
                    Plugin = MapPluginToWire(beforePlugin, beforeDiagnostics, beforeHookSummaries, beforeMcpSummaries, beforeLspSummaries)
                });
        if (!beforePlugin.Removable)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' cannot be removed by DotCraft.");

        var pluginRoot = Path.GetFullPath(beforePlugin.Manifest.RootPath);
        var workspacePluginsRoot = Path.GetFullPath(Path.Combine(workspaceCraftPath, "plugins"));
        if (beforePlugin.SourceKind != PluginDiscoverySourceKind.Workspace
            || !IsStrictPathWithin(pluginRoot, workspacePluginsRoot))
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' cannot be removed by DotCraft.");

        var removalCatalog = TryDiscoverAppBindingCatalog();
        var appListUpdate = TryBuildAppListUpdatedNotification(before, pluginId, "plugin/remove");

        PluginDirectoryDeleter.Delete(pluginRoot);

        var current = appConfigMonitor?.Current ?? new AppConfig();
        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        var discovery = RefreshPluginRuntime();
        await ReconnectEffectiveMcpRuntimeAsync(await GetWorkspaceMcpServersAsync(ct), ct);
        await ReconnectEffectiveLspRuntimeAsync(ct);
        RefreshHooksAfterPluginChange();
        appConfigMonitor?.NotifyChanged(
            Protocol.AppServer.AppServerMethodNames.PluginRemove,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp, ConfigChangeRegions.Hooks]);
        AppServerContextInvalidation.MarkSkills(contextPageManager);

        var diagnostics = discovery.Diagnostics.ToList();
        var hookSummaries = BuildPluginHookSummaryIndex(discovery, diagnostics);
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        var result = new Contract.PluginRemoveResult
        {
            Plugin = plugin == null
                ? default
                : Protocol.Optional<Contract.PluginInfo?>.FromValue(
                    MapPluginToWire(plugin, diagnostics, hookSummaries, mcpSummaries, lspSummaries))
        };
        var offlineBindings = TryMoveActiveAppBindingsOfflineForPlugin(
            removalCatalog,
            pluginId,
            "The owning plugin was removed.",
            "binding.offline.pluginRemoved");
        return await MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
            request.Message,
            result,
            appListUpdate,
            offlineBindings,
            ct);
    }

    private async Task<AppServerTypedResult<TResult>> MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync<TResult>(
        AppServerIncomingMessage msg,
        TResult result,
        Contract.AppListUpdatedNotification? appListUpdatedParams,
        IReadOnlyList<AppBindingSnapshot> offlineBindings,
        CancellationToken ct)
        where TResult : class
    {
        if (appListUpdatedParams == null && offlineBindings.Count == 0)
            return AppServerTypedResult<TResult>.FromResult(result);

        await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
        if (appListUpdatedParams != null)
        {
            await transport.NotifyContractAsync(
                Protocol.AppServer.AppServerRpc.AppListUpdated,
                appListUpdatedParams,
                ct);
        }

        foreach (var binding in offlineBindings)
        {
            await transport.NotifyContractAsync(
                Protocol.AppServer.AppServerRpc.ThreadAppBindingsChanged,
                new Contract.ThreadAppBindingsChangedNotification
                {
                    ThreadId = binding.ThreadId,
                    BindingId = binding.BindingId,
                    AppId = binding.AppId,
                    State = binding.State,
                    PreviousState = AppBindingStates.Active,
                    ChangeKind = "offline"
                },
                ct);
        }

        return AppServerTypedResult<TResult>.Written;
    }

    private Contract.AppListUpdatedNotification? TryBuildAppListUpdatedNotification(
        PluginDiscoveryResult discovery,
        string pluginId,
        string reason)
    {
        if (appBindingService == null
            || string.IsNullOrWhiteSpace(workspaceCraftPath)
            || string.IsNullOrWhiteSpace(hostWorkspacePath))
        {
            return null;
        }

        var pluginHasAppContribution = discovery.Plugins.Any(plugin =>
            PluginIds.EqualsCanonical(plugin.Manifest.Id, pluginId)
            && !string.IsNullOrWhiteSpace(plugin.Manifest.AppsPath));

        var appIds = TryResolveAppBindingAppIdsForPlugin(pluginId);
        if (!pluginHasAppContribution && appIds.Count == 0)
            return null;

        return new Contract.AppListUpdatedNotification
        {
            PluginId = pluginId,
            Reason = reason,
            AppIds = appIds
        };
    }

    private IReadOnlyList<AppBindingSnapshot> TryMoveActiveAppBindingsOfflineForPlugin(
        AppCatalogSnapshot? catalog,
        string pluginId,
        string diagnostic,
        string auditEvent)
    {
        if (appBindingService == null
            || catalog == null
            || string.IsNullOrWhiteSpace(workspaceCraftPath))
        {
            return [];
        }

        var appIds = ResolveAppBindingAppIdsForPlugin(catalog, pluginId);
        _ = auditEvent;
        return appIds.Count == 0
            ? []
            : appIds
                .SelectMany(appId => appBindingService.ListAppBindings(workspaceCraftPath, appId))
                .Where(binding => binding.State == AppBindingStates.Active)
                .Select(binding => appBindingService.MarkUnavailable(
                    workspaceCraftPath,
                    binding.BindingId,
                    diagnostic,
                    failed: false))
                .ToArray();
    }

    private List<string> TryResolveAppBindingAppIdsForPlugin(string pluginId)
    {
        var catalog = TryDiscoverAppBindingCatalog();
        return catalog == null ? [] : ResolveAppBindingAppIdsForPlugin(catalog, pluginId);
    }

    private AppCatalogSnapshot? TryDiscoverAppBindingCatalog()
    {
        if (appBindingService == null
            || string.IsNullOrWhiteSpace(workspaceCraftPath)
            || string.IsNullOrWhiteSpace(hostWorkspacePath))
        {
            return null;
        }

        try
        {
            return AppBindingCatalog.Discover(
                appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig(),
                hostWorkspacePath,
                workspaceCraftPath,
                skillsLoader,
                builtInPluginSourceRoots);
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ResolveAppBindingAppIdsForPlugin(AppCatalogSnapshot catalog, string pluginId) =>
        catalog.Entries
            .Where(entry => PluginIds.EqualsCanonical(entry.Plugin.Manifest.Id, pluginId))
            .Select(entry => entry.Descriptor.AppId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(appId => appId, StringComparer.Ordinal)
            .ToList();

    private PluginDiscoveryResult RefreshPluginRuntime()
    {
        var current = appConfigMonitor?.Current ?? new AppConfig();
        return PluginRuntimeConfigurator.ConfigureSkillsLoader(
            skillsLoader,
            current,
            hostWorkspacePath
            ?? (workspaceCraftPath == null ? Directory.GetCurrentDirectory() : Directory.GetParent(workspaceCraftPath)?.FullName)
            ?? Directory.GetCurrentDirectory(),
            workspaceCraftPath ?? Path.Combine(Directory.GetCurrentDirectory(), ".craft"),
            PluginDiagnosticsStore.Shared,
            builtInPluginSourceRoots);
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

    private async Task<List<McpServerConfig>> GetWorkspaceMcpServersAsync(CancellationToken ct)
        => await mcpConfig.GetWorkspaceServersAsync(ct);

    private List<McpServerConfig> GetWorkspaceMcpServersSnapshot()
        => mcpConfig.GetWorkspaceServersSnapshot();

    private List<LspServerConfig> GetWorkspaceLspServersSnapshot()
    {
        return (appConfigMonitor?.Current.LspServers ?? [])
            .Where(server => !server.ReadOnly)
            .Select(CloneAsWorkspaceLspServer)
            .ToList();
    }

    private async Task ReconnectEffectiveMcpRuntimeAsync(
        IReadOnlyList<McpServerConfig> workspaceServers,
        CancellationToken ct)
        => await mcpConfig.ReconnectEffectiveRuntimeAsync(workspaceServers, ct);

    private async Task ReconnectEffectiveLspRuntimeAsync(CancellationToken ct)
    {
        if (lspServerManager == null)
            return;

        ct.ThrowIfCancellationRequested();
        await lspServerManager.InitializeAsync(ct);
    }

    private static LspServerConfig CloneAsWorkspaceLspServer(LspServerConfig server)
    {
        var clone = server.Clone();
        clone.Origin = LspServerOrigin.Workspace();
        return clone;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<PluginMcpServerSummary>> BuildPluginMcpSummaryIndex(
        PluginDiscoveryResult discovery,
        List<PluginDiagnostic> diagnostics) =>
        PluginMcpServerResolver.BuildPluginMcpServerSummaries(
            discovery.Plugins,
            GetWorkspaceMcpServersSnapshot(),
            diagnostics);

    private static IReadOnlyDictionary<string, IReadOnlyList<PluginHookDeclaration>> BuildPluginHookSummaryIndex(
        PluginDiscoveryResult discovery,
        List<PluginDiagnostic> diagnostics)
    {
        var summaries = new Dictionary<string, IReadOnlyList<PluginHookDeclaration>>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in discovery.Plugins)
        {
            var hookSources = PluginHookLoader.LoadPluginHooks(plugin, diagnostics);
            var declarations = PluginHookLoader.BuildDeclarations(hookSources);
            if (declarations.Count > 0)
                summaries[plugin.Manifest.Id] = declarations;
        }

        return summaries;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<PluginLspServerSummary>> BuildPluginLspSummaryIndex(
        PluginDiscoveryResult discovery,
        List<PluginDiagnostic> diagnostics) =>
        PluginLspServerResolver.BuildPluginLspServerSummaries(
            discovery.Plugins,
            GetWorkspaceLspServersSnapshot(),
            diagnostics,
            lspToolEnabled: appConfigMonitor?.Current.Tools.Lsp.Enabled ?? false);

    private Contract.PluginInfo MapPluginToWire(
        DiscoveredPlugin plugin,
        IReadOnlyList<PluginDiagnostic> diagnostics,
        IReadOnlyDictionary<string, IReadOnlyList<PluginHookDeclaration>> hookSummaries,
        IReadOnlyDictionary<string, IReadOnlyList<PluginMcpServerSummary>> mcpSummaries,
        IReadOnlyDictionary<string, IReadOnlyList<PluginLspServerSummary>> lspSummaries)
    {
        var manifest = plugin.Manifest;
        var appDiagnostics = new List<PluginDiagnostic>();
        var apps = MapPluginAppsToWire(plugin, appDiagnostics);
        var desktopExtensionDiagnostics = new List<PluginDiagnostic>();
        var desktopExtensions = MapPluginDesktopExtensionsToWire(plugin, desktopExtensionDiagnostics);
        var pluginDiagnostics = diagnostics
            .Concat(appDiagnostics)
            .Concat(desktopExtensionDiagnostics)
            .Where(d => string.Equals(d.PluginId, manifest.Id, StringComparison.OrdinalIgnoreCase))
            .Select(MapPluginDiagnosticToWire)
            .ToList();
        return new Contract.PluginInfo
        {
            Id = manifest.Id,
            DisplayName = manifest.Interface?.DisplayName ?? manifest.DisplayName,
            Description = OmitIfNull(manifest.Interface?.ShortDescription ?? manifest.Description),
            Version = OmitIfNull(manifest.Version),
            Enabled = plugin.Enabled,
            Installed = plugin.Installed,
            Installable = plugin.Installable,
            Removable = plugin.Removable,
            Source = plugin.SourceKind.ToString().ToLowerInvariant(),
            RootPath = manifest.RootPath,
            MarketplaceName = OmitIfNull(plugin.MarketplaceName),
            Interface = MapPluginInterfaceToWire(manifest.Interface) is { } pluginInterface
                ? Protocol.Optional<Contract.PluginInterface?>.FromValue(pluginInterface)
                : default,
            Functions = Array.Empty<Contract.PluginFunctionInfo>(),
            Skills = MapPluginSkillsToWire(plugin),
            Apps = apps,
            DesktopExtensions = desktopExtensions,
            Hooks = hookSummaries.TryGetValue(manifest.Id, out var hooks)
                ? hooks.Select(MapPluginHookToWire).ToList()
                : Array.Empty<Contract.PluginHookInfo>(),
            McpServers = mcpSummaries.TryGetValue(manifest.Id, out var servers)
                ? servers.Select(MapPluginMcpServerToWire).ToList()
                : Array.Empty<Contract.PluginMcpServerInfo>(),
            LspServers = lspSummaries.TryGetValue(manifest.Id, out var lspServers)
                ? lspServers.Select(MapPluginLspServerToWire).ToList()
                : Array.Empty<Contract.PluginLspServerInfo>(),
            Diagnostics = pluginDiagnostics
        };
    }

    private static List<Contract.PluginDesktopExtensionInfo> MapPluginDesktopExtensionsToWire(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics) =>
        PluginDesktopExtensionCatalog.LoadPluginDesktopExtensions(plugin, diagnostics)
            .Select(extension => new Contract.PluginDesktopExtensionInfo
            {
                Id = extension.Id,
                DisplayName = extension.DisplayName,
                Description = OmitIfNull(extension.Description),
                Entry = extension.Entry,
                Styles = extension.Styles.ToList(),
                RequiredAppIds = extension.RequiredAppIds.ToList(),
                ConnectOrigins = extension.ConnectOrigins.ToList(),
                SurfaceWriteScopes = extension.SurfaceWriteScopes.ToList(),
                Surfaces = extension.Surfaces
                    .Select(surface => new Contract.PluginDesktopExtensionSurface
                    {
                        Type = surface.Type,
                        ViewId = OmitIfNull(surface.ViewId),
                        Label = OmitIfNull(surface.Label),
                        LocalizedLabel = surface.LocalizedLabel is { Count: > 0 }
                            ? new Dictionary<string, string>(surface.LocalizedLabel)
                            : default,
                        Icon = OmitIfNull(surface.Icon),
                        Placement = OmitIfNull(surface.Placement),
                        Order = OmitIfNull(surface.Order),
                        Title = OmitIfNull(surface.Title),
                        Description = OmitIfNull(surface.Description),
                        Slot = OmitIfNull(surface.Slot),
                        RendererId = OmitIfNull(surface.RendererId),
                        ActionId = OmitIfNull(surface.ActionId),
                        SettingsId = OmitIfNull(surface.SettingsId)
                    })
                    .ToList()
            })
            .ToList();

    private static List<Contract.PluginAppInfo> MapPluginAppsToWire(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics) =>
        AppBindingCatalog.LoadPluginAppDescriptors(plugin, diagnostics)
            .Select(app => new Contract.PluginAppInfo
            {
                AppId = app.AppId,
                DisplayName = app.DisplayName,
                DeveloperName = app.DeveloperName,
                Description = app.Description,
                Category = OmitIfNull(app.Category),
                Icon = OmitIfNull(TryReadDataUrl(app.Icon) ?? app.Icon),
                ReleasePage = OmitIfNull(app.ReleasePage),
                NativeApplication = app.NativeApplication == null
                    ? default
                    : Protocol.Optional<Contract.PluginAppNativeApplication?>.FromValue(
                      new Contract.PluginAppNativeApplication
                    {
                        DisplayName = app.NativeApplication.DisplayName,
                        Protocol = app.NativeApplication.Protocol,
                        InstallUrl = OmitIfNull(app.NativeApplication.InstallUrl)
                    })
            })
            .ToList();

    private static Contract.PluginMcpServerInfo MapPluginMcpServerToWire(PluginMcpServerSummary server) =>
        new()
        {
            Name = server.Name,
            RuntimeName = server.RuntimeName,
            Transport = server.Transport,
            Enabled = server.Enabled,
            Active = server.Active,
            ShadowedBy = OmitIfNull(server.ShadowedBy)
        };

    private static Contract.PluginHookInfo MapPluginHookToWire(PluginHookDeclaration hook) =>
        new()
        {
            Key = hook.Key,
            EventName = hook.EventName
        };

    private static Contract.PluginLspServerInfo MapPluginLspServerToWire(PluginLspServerSummary server) =>
        new()
        {
            Name = server.Name,
            RuntimeName = server.RuntimeName,
            Transport = server.Transport,
            Enabled = server.Enabled,
            Active = server.Active,
            Extensions = server.Extensions.ToArray(),
            ShadowedBy = OmitIfNull(server.ShadowedBy)
        };

    private static bool IsPathWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsStrictPathWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !string.IsNullOrWhiteSpace(relative)
               && !relative.Equals(".", StringComparison.Ordinal)
               && IsPathWithin(path, root);
    }

    private Contract.PluginInterface? MapPluginInterfaceToWire(PluginInterfaceMetadata? metadata)
    {
        if (metadata == null)
            return null;

        return new Contract.PluginInterface
        {
            DisplayName = OmitIfNull(metadata.DisplayName),
            ShortDescription = OmitIfNull(metadata.ShortDescription),
            LongDescription = OmitIfNull(metadata.LongDescription),
            DeveloperName = OmitIfNull(metadata.DeveloperName),
            Category = OmitIfNull(metadata.Category),
            Capabilities = metadata.Capabilities.ToList(),
            DefaultPrompt = OmitIfNull(metadata.DefaultPrompt),
            BrandColor = OmitIfNull(metadata.BrandColor),
            ComposerIconDataUrl = OmitIfNull(TryReadDataUrl(metadata.ComposerIcon)),
            LogoDataUrl = OmitIfNull(TryReadDataUrl(metadata.Logo)),
            WebsiteUrl = OmitIfNull(metadata.WebsiteUrl),
            PrivacyPolicyUrl = OmitIfNull(metadata.PrivacyPolicyUrl),
            TermsOfServiceUrl = OmitIfNull(metadata.TermsOfServiceUrl)
        };
    }

    private List<Contract.PluginSkillInfo> MapPluginSkillsToWire(DiscoveredPlugin plugin)
    {
        var manifest = plugin.Manifest;
        if (string.IsNullOrWhiteSpace(manifest.SkillsPath) || !Directory.Exists(manifest.SkillsPath))
            return [];

        var allSkills = skillsLoader?.ListSkills(filterUnavailable: false) ?? new List<SkillsLoader.SkillInfo>();
        return Directory.GetDirectories(manifest.SkillsPath)
            .Where(dir => File.Exists(Path.Combine(dir, "SKILL.md")))
            .Select(dir =>
            {
                var name = Path.GetFileName(dir);
                var skillFile = Path.Combine(dir, "SKILL.md");
                var skill = allSkills.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
                var interfaceInfo = SkillsLoader.GetSkillInterfaceFromFile(skillFile)
                                    ?? skillsLoader?.GetSkillInterface(name);
                return new Contract.PluginSkillInfo
                {
                    Name = name,
                    Description = SkillsLoader.GetSkillDescriptionFromFile(skillFile)
                                  ?? skillsLoader?.GetSkillDescription(name)
                                  ?? name,
                    DisplayName = OmitIfNull(interfaceInfo?.DisplayName),
                    ShortDescription = OmitIfNull(interfaceInfo?.ShortDescription),
                    Enabled = plugin.Installed
                              && plugin.Enabled
                              && (skill?.Enabled
                              ?? (!string.Equals(manifest.Id, PluginIds.Browser, StringComparison.OrdinalIgnoreCase)
                                  || (appConfigMonitor?.Current ?? new AppConfig()).Plugins.IsPluginEnabled(PluginIds.Browser, true)))
                };
            })
            .ToList();
    }

    private static Contract.PluginDiagnostic MapPluginDiagnosticToWire(PluginDiagnostic diagnostic) =>
        new()
        {
            Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
            Code = diagnostic.Code,
            Message = diagnostic.Message,
            PluginId = OmitIfNull(diagnostic.PluginId),
            Path = OmitIfNull(diagnostic.Path)
        };

    private static T? Read<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);

    private static string? TryReadDataUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var mimeType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };
        if (mimeType == null)
            return null;

        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > 512 * 1024)
            return null;

        return $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }
}
