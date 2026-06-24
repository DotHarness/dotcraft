using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Plugins;
using DotCraft.Skills;

namespace DotCraft.Protocol.AppServer;

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
    WorkspaceConfigEditor workspaceConfig) : IAppServerDomainHandler
{
    private const string AppBindingAppListUpdatedNotification = "app/list/updated";
    private const string AppBindingThreadBindingsChangedNotification = "thread/appBindings/changed";

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.PluginList, HandlePluginListAsync);
        table.Map(AppServerMethods.PluginView, HandlePluginViewAsync);
        table.Map(AppServerMethods.PluginInstall, HandlePluginInstallAsync);
        table.Map(AppServerMethods.PluginInstallLocal, HandlePluginInstallLocalAsync);
        table.Map(AppServerMethods.PluginRemove, HandlePluginRemoveAsync);
        table.Map(AppServerMethods.PluginSetEnabled, HandlePluginSetEnabledAsync);
    }

    private Task<object?> HandlePluginListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginList);

        var p = AppServerParams.Get<PluginListParams>(msg);
        var discovery = RefreshPluginRuntime();
        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugins = discovery.Plugins
            .Where(plugin => p.IncludeDisabled != false || plugin.Enabled)
            .Select(plugin => MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries))
            .OrderBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<object?>(new PluginListResult
        {
            Plugins = plugins,
            Diagnostics = diagnostics.Select(MapPluginDiagnosticToWire).ToList()
        });
    }

    private Task<object?> HandlePluginViewAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginView);

        var p = AppServerParams.Get<PluginViewParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var discovery = RefreshPluginRuntime();
        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(
            candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, p.Id));
        if (plugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{p.Id}' was not found.");

        return Task.FromResult<object?>(new PluginViewResult
        {
            Plugin = MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries)
        });
    }

    private async Task<object?> HandlePluginSetEnabledAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginSetEnabled);

        var p = AppServerParams.Get<PluginSetEnabledParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var pluginId = PluginIds.Canonicalize(p.Id.Trim());
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var before = RefreshPluginRuntime();
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (!beforePlugin.Installed)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' is not installed.");

        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        if (p.Enabled)
            disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        else if (!disabled.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            disabled.Add(pluginId);

        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        var discovery = RefreshPluginRuntime();
        await ReconnectEffectiveMcpRuntimeAsync(await GetWorkspaceMcpServersAsync(ct), ct);
        await ReconnectEffectiveLspRuntimeAsync(ct);
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.PluginSetEnabled,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp]);
        AppServerContextInvalidation.MarkSkills(contextPageManager);

        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (plugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");

        var result = new PluginSetEnabledResult
        {
            Plugin = MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries)
        };
        var appListUpdate = TryBuildAppListUpdatedNotification(discovery, pluginId, p.Enabled ? "plugin/enable" : "plugin/disable");
        IReadOnlyList<ThreadAppBindingWire> offlineBindings = p.Enabled
            ? []
            : TryMoveActiveAppBindingsOfflineForPlugin(
                TryDiscoverAppBindingCatalog(),
                pluginId,
                "The owning plugin was disabled.",
                "binding.offline.pluginDisabled");
        return await MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
            msg,
            result,
            appListUpdate,
            offlineBindings,
            ct);
    }

    private async Task<object?> HandlePluginInstallAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginInstall);

        var p = AppServerParams.Get<PluginInstallParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var pluginId = PluginIds.Canonicalize(p.Id.Trim());
        var before = RefreshPluginRuntime();
        var beforeDiagnostics = before.Diagnostics.ToList();
        var beforeMcpSummaries = BuildPluginMcpSummaryIndex(before, beforeDiagnostics);
        var beforeLspSummaries = BuildPluginLspSummaryIndex(before, beforeDiagnostics);
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (beforePlugin.Installed)
            return new PluginInstallResult { Plugin = MapPluginToWire(beforePlugin, beforeDiagnostics, beforeMcpSummaries, beforeLspSummaries) };
        if (!beforePlugin.Installable)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' is not installable.");

        var deployDiagnostics = new BuiltInPluginDeployer(Path.Combine(workspaceCraftPath, "plugins"), builtInPluginSourceRoots)
            .DeployPlugin(pluginId);
        PluginDiagnosticsStore.Shared.Append(deployDiagnostics);
        PluginDiagnosticsLogger.Write(deployDiagnostics);
        if (deployDiagnostics.Any(d => d.Severity == PluginDiagnosticSeverity.Error || d.Code == "BuiltInPluginNotFound"))
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' could not be installed.");

        return await FinalizeInstalledPluginAsync(pluginId, AppServerMethods.PluginInstall, "plugin/install", msg, ct);
    }

    private async Task<object?> HandlePluginInstallLocalAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginInstallLocal);

        var p = AppServerParams.Get<PluginInstallLocalParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Path))
            throw AppServerErrors.InvalidParams("'path' is required.");

        var install = new LocalPluginInstaller(Path.Combine(workspaceCraftPath, "plugins")).Install(p.Path.Trim());
        PluginDiagnosticsStore.Shared.Append(install.Diagnostics);
        PluginDiagnosticsLogger.Write(install.Diagnostics);
        if (install.PluginId == null)
        {
            var error = install.Diagnostics.FirstOrDefault(d => d.Severity == PluginDiagnosticSeverity.Error);
            throw AppServerErrors.InvalidParams(error?.Message ?? "The selected folder is not a valid plugin.");
        }

        return await FinalizeInstalledPluginAsync(install.PluginId, AppServerMethods.PluginInstallLocal, "plugin/installLocal", msg, ct);
    }

    // Shared tail for plugin/install and plugin/installLocal: clears the id from the
    // workspace disabled list, refreshes runtime, reconciles MCP/LSP, notifies, and
    // returns the installed PluginInfo (with any deferred App Binding notifications).
    private async Task<object?> FinalizeInstalledPluginAsync(
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
        appConfigMonitor?.NotifyChanged(
            source,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp]);
        AppServerContextInvalidation.MarkSkills(contextPageManager);

        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (plugin == null || !plugin.Installed)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' could not be installed.");

        var result = new PluginInstallResult
        {
            Plugin = MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries)
        };
        return await MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
            msg,
            result,
            TryBuildAppListUpdatedNotification(discovery, pluginId, appListReason),
            [],
            ct);
    }

    private async Task<object?> HandlePluginRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.PluginRemove);

        var p = AppServerParams.Get<PluginRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        var pluginId = PluginIds.Canonicalize(p.Id.Trim());
        var before = RefreshPluginRuntime();
        var beforeDiagnostics = before.Diagnostics.ToList();
        var beforeMcpSummaries = BuildPluginMcpSummaryIndex(before, beforeDiagnostics);
        var beforeLspSummaries = BuildPluginLspSummaryIndex(before, beforeDiagnostics);
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (!beforePlugin.Installed)
            return new PluginRemoveResult { Plugin = MapPluginToWire(beforePlugin, beforeDiagnostics, beforeMcpSummaries, beforeLspSummaries) };
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
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.PluginRemove,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp]);
        AppServerContextInvalidation.MarkSkills(contextPageManager);

        var diagnostics = discovery.Diagnostics.ToList();
        var mcpSummaries = BuildPluginMcpSummaryIndex(discovery, diagnostics);
        var lspSummaries = BuildPluginLspSummaryIndex(discovery, diagnostics);
        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        var result = new PluginRemoveResult
        {
            Plugin = plugin == null ? null : MapPluginToWire(plugin, diagnostics, mcpSummaries, lspSummaries)
        };
        var offlineBindings = TryMoveActiveAppBindingsOfflineForPlugin(
            removalCatalog,
            pluginId,
            "The owning plugin was removed.",
            "binding.offline.pluginRemoved");
        return await MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
            msg,
            result,
            appListUpdate,
            offlineBindings,
            ct);
    }

    private async Task<object?> MaybeSendAppBindingLifecycleNotificationsAfterResponseAsync(
        AppServerIncomingMessage msg,
        object result,
        object? appListUpdatedParams,
        IReadOnlyList<ThreadAppBindingWire> offlineBindings,
        CancellationToken ct)
    {
        if (appListUpdatedParams == null && offlineBindings.Count == 0)
            return result;

        ReleaseAppContextPagesForBindings(offlineBindings);
        await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
        if (appListUpdatedParams != null)
        {
            await transport.WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method = AppBindingAppListUpdatedNotification,
                @params = appListUpdatedParams
            }, ct);
        }

        foreach (var binding in offlineBindings)
        {
            await transport.WriteMessageAsync(new
            {
                jsonrpc = "2.0",
                method = AppBindingThreadBindingsChangedNotification,
                @params = new
                {
                    threadId = binding.ThreadId,
                    bindingId = binding.BindingId,
                    appId = binding.AppId,
                    state = binding.State,
                    previousState = AppBindingStates.Active,
                    changeKind = "offline"
                }
            }, ct);
        }

        return null;
    }

    private void ReleaseAppContextPagesForBindings(IReadOnlyList<ThreadAppBindingWire> bindings)
    {
        if (contextPageManager == null || bindings.Count == 0)
            return;

        foreach (var threadId in bindings
                     .Select(binding => binding.ThreadId)
                     .Where(threadId => !string.IsNullOrWhiteSpace(threadId))
                     .Distinct(StringComparer.Ordinal))
        {
            contextPageManager.ReleaseStablePage(threadId, ContextPageKeys.AppContextBlocks());
        }
    }

    private object? TryBuildAppListUpdatedNotification(
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

        return new
        {
            pluginId,
            reason,
            appIds
        };
    }

    private IReadOnlyList<ThreadAppBindingWire> TryMoveActiveAppBindingsOfflineForPlugin(
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
        return appIds.Count == 0
            ? []
            : appBindingService.MoveActiveBindingsOfflineForApps(catalog, workspaceCraftPath, appIds, diagnostic, auditEvent);
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
            return appBindingService.DiscoverCatalog(
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

    private IReadOnlyDictionary<string, IReadOnlyList<PluginLspServerSummary>> BuildPluginLspSummaryIndex(
        PluginDiscoveryResult discovery,
        List<PluginDiagnostic> diagnostics) =>
        PluginLspServerResolver.BuildPluginLspServerSummaries(
            discovery.Plugins,
            GetWorkspaceLspServersSnapshot(),
            diagnostics,
            lspToolEnabled: appConfigMonitor?.Current.Tools.Lsp.Enabled ?? false);

    private PluginInfoWire MapPluginToWire(
        DiscoveredPlugin plugin,
        IReadOnlyList<PluginDiagnostic> diagnostics,
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
        return new PluginInfoWire
        {
            Id = manifest.Id,
            DisplayName = manifest.Interface?.DisplayName ?? manifest.DisplayName,
            Description = manifest.Interface?.ShortDescription ?? manifest.Description,
            Version = manifest.Version,
            Enabled = plugin.Enabled,
            Installed = plugin.Installed,
            Installable = plugin.Installable,
            Removable = plugin.Removable,
            Source = plugin.SourceKind.ToString().ToLowerInvariant(),
            RootPath = manifest.RootPath,
            Interface = MapPluginInterfaceToWire(manifest.Interface),
            Functions = [],
            Skills = MapPluginSkillsToWire(plugin),
            Apps = apps,
            DesktopExtensions = desktopExtensions,
            McpServers = mcpSummaries.TryGetValue(manifest.Id, out var servers)
                ? servers.Select(MapPluginMcpServerToWire).ToList()
                : [],
            LspServers = lspSummaries.TryGetValue(manifest.Id, out var lspServers)
                ? lspServers.Select(MapPluginLspServerToWire).ToList()
                : [],
            Diagnostics = pluginDiagnostics
        };
    }

    private static List<PluginDesktopExtensionInfoWire> MapPluginDesktopExtensionsToWire(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics) =>
        PluginDesktopExtensionCatalog.LoadPluginDesktopExtensions(plugin, diagnostics)
            .Select(extension => new PluginDesktopExtensionInfoWire
            {
                Id = extension.Id,
                DisplayName = extension.DisplayName,
                Description = extension.Description,
                Entry = extension.Entry,
                Styles = extension.Styles.ToList(),
                RequiredAppIds = extension.RequiredAppIds.ToList(),
                ConnectOrigins = extension.ConnectOrigins.ToList(),
                SurfaceWriteScopes = extension.SurfaceWriteScopes.ToList(),
                Surfaces = extension.Surfaces
                    .Select(surface => new PluginDesktopExtensionSurfaceWire
                    {
                        Type = surface.Type,
                        ViewId = surface.ViewId,
                        Label = surface.Label,
                        LocalizedLabel = surface.LocalizedLabel is { Count: > 0 }
                            ? new Dictionary<string, string>(surface.LocalizedLabel)
                            : null,
                        Icon = surface.Icon,
                        Placement = surface.Placement,
                        Order = surface.Order,
                        Title = surface.Title,
                        Description = surface.Description,
                        Slot = surface.Slot,
                        RendererId = surface.RendererId,
                        ActionId = surface.ActionId,
                        SettingsId = surface.SettingsId
                    })
                    .ToList()
            })
            .ToList();

    private static List<PluginAppInfoWire> MapPluginAppsToWire(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics) =>
        AppBindingCatalog.LoadPluginAppDescriptors(plugin, diagnostics)
            .Select(app => new PluginAppInfoWire
            {
                AppId = app.AppId,
                ToolNamespace = app.ToolNamespace,
                DisplayName = app.DisplayName,
                DeveloperName = app.DeveloperName,
                Description = app.Description,
                Category = app.Category,
                Icon = TryReadDataUrl(app.Icon) ?? app.Icon,
                ReleasePage = app.ReleasePage,
                NativeApplication = app.NativeApplication == null
                    ? null
                    : new PluginAppNativeApplicationWire
                    {
                        DisplayName = app.NativeApplication.DisplayName,
                        Protocol = app.NativeApplication.Protocol,
                        InstallUrl = app.NativeApplication.InstallUrl
                    },
                ToolCatalog = app.ToolCatalog
                    .Select(tool => new PluginAppToolInfoWire
                    {
                        Name = tool.Name,
                        Scope = tool.Scope,
                        Risk = tool.Risk,
                        DefaultExposure = tool.DefaultExposure,
                        Description = tool.Description
                    })
                    .ToList(),
                DynamicToolCatalog = new PluginAppDynamicToolCatalogWire
                {
                    Enabled = app.DynamicToolCatalog.Enabled,
                    Description = app.DynamicToolCatalog.Description
                }
            })
            .ToList();

    private static PluginMcpServerInfoWire MapPluginMcpServerToWire(PluginMcpServerSummary server) =>
        new()
        {
            Name = server.Name,
            RuntimeName = server.RuntimeName,
            Transport = server.Transport,
            Enabled = server.Enabled,
            Active = server.Active,
            ShadowedBy = server.ShadowedBy
        };

    private static PluginLspServerInfoWire MapPluginLspServerToWire(PluginLspServerSummary server) =>
        new()
        {
            Name = server.Name,
            RuntimeName = server.RuntimeName,
            Transport = server.Transport,
            Enabled = server.Enabled,
            Active = server.Active,
            Extensions = [.. server.Extensions],
            ShadowedBy = server.ShadowedBy
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

    private PluginInterfaceWire? MapPluginInterfaceToWire(PluginInterfaceMetadata? metadata)
    {
        if (metadata == null)
            return null;

        return new PluginInterfaceWire
        {
            DisplayName = metadata.DisplayName,
            ShortDescription = metadata.ShortDescription,
            LongDescription = metadata.LongDescription,
            DeveloperName = metadata.DeveloperName,
            Category = metadata.Category,
            Capabilities = metadata.Capabilities.ToList(),
            DefaultPrompt = metadata.DefaultPrompt,
            BrandColor = metadata.BrandColor,
            ComposerIconDataUrl = TryReadDataUrl(metadata.ComposerIcon),
            LogoDataUrl = TryReadDataUrl(metadata.Logo),
            WebsiteUrl = metadata.WebsiteUrl,
            PrivacyPolicyUrl = metadata.PrivacyPolicyUrl,
            TermsOfServiceUrl = metadata.TermsOfServiceUrl
        };
    }

    private List<PluginSkillInfoWire> MapPluginSkillsToWire(DiscoveredPlugin plugin)
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
                return new PluginSkillInfoWire
                {
                    Name = name,
                    Description = SkillsLoader.GetSkillDescriptionFromFile(skillFile)
                                  ?? skillsLoader?.GetSkillDescription(name)
                                  ?? name,
                    DisplayName = interfaceInfo?.DisplayName,
                    ShortDescription = interfaceInfo?.ShortDescription,
                    Enabled = plugin.Installed
                              && plugin.Enabled
                              && (skill?.Enabled
                              ?? (!string.Equals(manifest.Id, PluginIds.Browser, StringComparison.OrdinalIgnoreCase)
                                  || (appConfigMonitor?.Current ?? new AppConfig()).Plugins.IsPluginEnabled(PluginIds.Browser, true)))
                };
            })
            .ToList();
    }

    private static PluginDiagnosticWire MapPluginDiagnosticToWire(PluginDiagnostic diagnostic) =>
        new()
        {
            Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
            Code = diagnostic.Code,
            Message = diagnostic.Message,
            PluginId = diagnostic.PluginId,
            Path = diagnostic.Path
        };

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
