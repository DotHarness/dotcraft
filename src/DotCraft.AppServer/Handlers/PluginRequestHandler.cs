using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Hooks;
using DotCraft.Lsp;
using DotCraft.Plugins;
using DotCraft.Skills;
using Contract = DotCraft.Protocol.AppServer;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using PluginDiagnostic = DotCraft.Plugins.PluginDiagnostic;
using Microsoft.Extensions.Logging;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler(
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
    HookRunner? hookRunner,
    IPluginWorkflowSummaryProvider? workflowSummaryProvider,
    IPluginDotnetRuntimeCoordinator? dotnetRuntime,
    PluginConfigStore? pluginConfigStore,
    Action<IAppServerTransport, Contract.PluginSnapshotUpdatedNotification, Task>?
        broadcastPluginSnapshotUpdated,
    AppServerPluginManagementState managementState,
    ILogger? logger) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        MapSnapshotRead(table, Protocol.AppServer.AppServerRpc.PluginList, HandlePluginListAsync);
        MapSnapshotRead(table, Protocol.AppServer.AppServerRpc.PluginView, HandlePluginViewAsync);
        MapSnapshotRead(table, Protocol.AppServer.AppServerRpc.PluginConfigGet, HandlePluginConfigGetAsync);
        MapMutation(table, Protocol.AppServer.AppServerRpc.PluginInstall, HandlePluginInstallAsync);
        MapMutation(table, Protocol.AppServer.AppServerRpc.PluginInstallLocal, HandlePluginInstallLocalAsync);
        MapMutation(table, Protocol.AppServer.AppServerRpc.PluginRemove, HandlePluginRemoveAsync);
        MapMutation(table, Protocol.AppServer.AppServerRpc.PluginSetEnabled, HandlePluginSetEnabledAsync);
        MapMutation(table, Protocol.AppServer.AppServerRpc.PluginSetTrusted, HandlePluginSetTrustedAsync);
        MapMutation(table, Protocol.AppServer.AppServerRpc.PluginConfigMutate, HandlePluginConfigMutateAsync);
        MapMutation(table, Protocol.AppServer.AppServerRpc.MarketplaceAdd, HandleMarketplaceAddAsync);
        MapMutation(table, Protocol.AppServer.AppServerRpc.MarketplaceRemove, HandleMarketplaceRemoveAsync);
        MapMutation(table, Protocol.AppServer.AppServerRpc.MarketplaceRefresh, HandleMarketplaceRefreshAsync);
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
        AppendPluginConfigDiagnostics(discovery, diagnostics);
        // The .NET runtime's own findings, such as a failed activation, have no other way to a client.
        diagnostics.AddRange(dotnetRuntime?.Snapshot.Diagnostics ?? []);
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
                Diagnostics = diagnostics.Select(MapPluginDiagnosticToWire).ToList(),
                SnapshotRevision = CurrentPluginSnapshotRevision
            }));
    }


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
        AppendPluginConfigDiagnostics(discovery, diagnostics);
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
                Plugin = MapPluginToWire(plugin, diagnostics, hookSummaries, mcpSummaries, lspSummaries),
                SnapshotRevision = CurrentPluginSnapshotRevision
            }));
    }

    private async Task<AppServerTypedResult<Contract.PluginOperationResult>> HandlePluginSetEnabledAsync(
        AppServerTypedRequest<Contract.PluginSetEnabledParams> request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginSetEnabled);

        var pluginId = RequireLifecyclePluginId(request.Params.Id);
        var enabled = request.Params.Enabled;
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var runtimeBefore = dotnetRuntime?.Snapshot;
        var before = RefreshPluginRuntime();
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (!beforePlugin.Installed)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' is not installed.");

        if (beforePlugin.Enabled == enabled)
        {
            return AppServerTypedResult<Contract.PluginOperationResult>.FromResult(
                BuildOperationResult(before, pluginId, "noChange", [], []));
        }
        var commitToken = EnterMutationCommit(ct);

        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        if (enabled)
            disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        else if (!disabled.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            disabled.Add(pluginId);

        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        if (dotnetRuntime?.Snapshot.Plugins.Any(plugin =>
                PluginIds.EqualsCanonical(plugin.PluginId, pluginId)) == true)
        {
            await dotnetRuntime.SetEnabledAsync(pluginId, enabled, commitToken).ConfigureAwait(false);
        }

        var discovery = await FinalizePluginContributionRefreshAsync(
            Protocol.AppServer.AppServerMethodNames.PluginSetEnabled,
            commitToken).ConfigureAwait(false);

        var affected = runtimeBefore == null || dotnetRuntime == null
            ? []
            : ChangedRuntimeIds(runtimeBefore, dotnetRuntime.Snapshot, pluginId);
        AdvancePluginSnapshotRevision();
        var result = BuildOperationResult(discovery, pluginId, "applied", affected, []);
        var appListUpdate = TryBuildAppListUpdatedNotification(discovery, pluginId, enabled ? "plugin/enable" : "plugin/disable");
        IReadOnlyList<AppBindingSnapshot> offlineBindings = enabled
            ? []
            : TryMoveActiveAppBindingsOfflineForPlugin(
                TryDiscoverAppBindingCatalog(),
                pluginId,
                "The owning plugin was disabled.");
        return await WriteWithLifecycleNotificationsAsync(
            request.Message,
            result,
            appListUpdate,
            offlineBindings,
            [pluginId, .. affected],
            ct);
    }

    private async Task<AppServerTypedResult<Contract.PluginOperationResult>> HandlePluginInstallAsync(
        AppServerTypedRequest<Contract.PluginInstallParams> request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginInstall);

        var pluginId = RequireLifecyclePluginId(request.Params.Id);
        var before = RefreshPluginRuntime();
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (beforePlugin.Installed)
            return AppServerTypedResult<Contract.PluginOperationResult>.FromResult(
                BuildOperationResult(before, pluginId, "noChange", [], []));
        if (!beforePlugin.Installable)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' is not installable.");
        var commitToken = EnterMutationCommit(ct);

        var deployDiagnostics = new BuiltInPluginDeployer(
                Path.Combine(workspaceCraftPath, "plugins"),
                builtInPluginSourceRoots,
                appConfigMonitor?.Current.Plugins ?? new AppConfig.PluginsConfig(),
                workspaceConfig.PersonalConfigPath is { } personalConfigPath
                    ? Path.GetDirectoryName(personalConfigPath)
                    : null)
            .DeployPlugin(pluginId);
        PluginDiagnosticsLogger.Write(deployDiagnostics, logger);
        if (deployDiagnostics.Any(d => IsBlockingDeployDiagnosticForPlugin(d, pluginId)))
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' could not be installed.");

        return await FinalizeInstalledPluginAsync(
            pluginId,
            Protocol.AppServer.AppServerMethodNames.PluginInstall,
            "plugin/install",
            request.Message,
            commitToken,
            ct);
    }

    private static bool IsBlockingDeployDiagnosticForPlugin(PluginDiagnostic diagnostic, string pluginId) =>
        (diagnostic.Severity == PluginDiagnosticSeverity.Error || diagnostic.Code == "BuiltInPluginNotFound")
        && !string.IsNullOrWhiteSpace(diagnostic.PluginId)
        && PluginIds.EqualsCanonical(diagnostic.PluginId, pluginId);

    private async Task<AppServerTypedResult<Contract.PluginOperationResult>> HandlePluginInstallLocalAsync(
        AppServerTypedRequest<Contract.PluginInstallLocalParams> request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginInstallLocal);

        var path = Read(request.Params.Path);
        if (string.IsNullOrWhiteSpace(path))
            throw AppServerErrors.InvalidParams("'path' is required.");
        var commitToken = EnterMutationCommit(ct);

        var install = new LocalPluginInstaller(Path.Combine(workspaceCraftPath, "plugins")).Install(path.Trim());
        PluginDiagnosticsLogger.Write(install.Diagnostics, logger);
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
            commitToken,
            ct);
    }

    private async Task<AppServerTypedResult<Contract.PluginOperationResult>> FinalizeInstalledPluginAsync(
        string pluginId,
        string source,
        string appListReason,
        AppServerIncomingMessage msg,
        CancellationToken commitToken,
        CancellationToken deliveryToken)
    {
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath!, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        var runtimeMutation = dotnetRuntime == null
            ? null
            : await dotnetRuntime.ReconcileAfterMutationAsync(pluginId, commitToken).ConfigureAwait(false);

        var discovery = await FinalizePluginContributionRefreshAsync(source, commitToken).ConfigureAwait(false);

        var plugin = discovery.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (plugin == null || !plugin.Installed)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' could not be installed.");

        AdvancePluginSnapshotRevision();
        var result = BuildOperationResult(
            discovery,
            pluginId,
            "applied",
            runtimeMutation?.AffectedPluginIds ?? [],
            runtimeMutation?.Diagnostics ?? []);
        return await WriteWithLifecycleNotificationsAsync(
            msg,
            result,
            TryBuildAppListUpdatedNotification(discovery, pluginId, appListReason),
            [],
            [pluginId, .. (runtimeMutation?.AffectedPluginIds ?? [])],
            deliveryToken);
    }

    private async Task<AppServerTypedResult<Contract.PluginOperationResult>> HandlePluginRemoveAsync(
        AppServerTypedRequest<Contract.PluginRemoveParams> request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginRemove);

        var pluginId = RequireLifecyclePluginId(request.Params.Id);
        var before = RefreshPluginRuntime();
        var beforePlugin = before.Plugins.FirstOrDefault(candidate => PluginIds.EqualsCanonical(candidate.Manifest.Id, pluginId));
        if (beforePlugin == null)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' was not found.");
        if (!beforePlugin.Installed)
            return AppServerTypedResult<Contract.PluginOperationResult>.FromResult(
                BuildOperationResult(before, pluginId, "noChange", [], []));
        if (!beforePlugin.Removable)
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' cannot be removed by DotCraft.");

        var pluginRoot = Path.GetFullPath(beforePlugin.Manifest.RootPath);
        var workspacePluginsRoot = Path.GetFullPath(Path.Combine(workspaceCraftPath, "plugins"));
        if (beforePlugin.SourceKind != PluginDiscoverySourceKind.Workspace
            || !IsStrictPathWithin(pluginRoot, workspacePluginsRoot))
            throw AppServerErrors.InvalidParams($"Plugin '{pluginId}' cannot be removed by DotCraft.");
        var commitToken = EnterMutationCommit(ct);

        var removalCatalog = TryDiscoverAppBindingCatalog();
        var appListUpdate = TryBuildAppListUpdatedNotification(before, pluginId, "plugin/remove");

        var quiesce = dotnetRuntime == null
            ? null
            : await dotnetRuntime.QuiesceForMutationAsync(pluginId, commitToken).ConfigureAwait(false);
        if (quiesce?.Outcome == PluginRuntimeMutationOutcome.NotApplied)
        {
            AdvancePluginSnapshotRevision();
            var quiesceResult = BuildOperationResult(
                RefreshPluginRuntime(),
                pluginId,
                "notApplied",
                quiesce.AffectedPluginIds,
                quiesce.Diagnostics);
            return await WriteWithSnapshotInvalidationAsync(
                request.Message,
                quiesceResult,
                [pluginId, .. quiesce.AffectedPluginIds],
                ct).ConfigureAwait(false);
        }

        var contributionFailure = await QuiesceRootBackedContributionsAsync(pluginId, commitToken).ConfigureAwait(false);
        if (contributionFailure != null)
        {
            var recovery = dotnetRuntime == null
                ? null
                : await dotnetRuntime.ReconcileAfterMutationAsync(pluginId, commitToken).ConfigureAwait(false);
            var affectedPluginIds = (quiesce?.AffectedPluginIds ?? [])
                .Concat(recovery?.AffectedPluginIds ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            AdvancePluginSnapshotRevision();
            return await WriteWithSnapshotInvalidationAsync(
                request.Message,
                BuildOperationResult(
                    RefreshPluginRuntime(),
                    pluginId,
                    "notApplied",
                    affectedPluginIds,
                    [contributionFailure]),
                [pluginId, .. affectedPluginIds],
                ct).ConfigureAwait(false);
        }

        try
        {
            PluginDirectoryDeleter.Delete(pluginRoot);
        }
        catch
        {
            var recovery = dotnetRuntime == null
                ? null
                : await dotnetRuntime.ReconcileAfterMutationAsync(pluginId, commitToken).ConfigureAwait(false);
            await RestoreRootBackedContributionsAsync(commitToken).ConfigureAwait(false);
            var affectedPluginIds = (quiesce?.AffectedPluginIds ?? [])
                .Concat(recovery?.AffectedPluginIds ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            AdvancePluginSnapshotRevision();
            return await WriteWithSnapshotInvalidationAsync(
                request.Message,
                BuildOperationResult(
                    RefreshPluginRuntime(),
                    pluginId,
                    "notApplied",
                    affectedPluginIds,
                    [PluginDiagnostic.Error(
                        "PluginFilesystemCommitFailed",
                        "The plugin directory could not be deleted.",
                        pluginId,
                        parameters: new Dictionary<string, System.Text.Json.JsonElement>
                        {
                            ["operation"] = System.Text.Json.JsonSerializer.SerializeToElement("remove"),
                            ["phase"] = System.Text.Json.JsonSerializer.SerializeToElement("delete")
                        })]),
                [pluginId, .. affectedPluginIds],
                ct).ConfigureAwait(false);
        }

        var current = appConfigMonitor?.Current ?? new AppConfig();
        var disabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(current.Plugins.DisabledPlugins).ToList();
        disabled.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));
        PluginsConfigPersistence.WriteWorkspaceDisabledPlugins(workspaceCraftPath, disabled);
        current.Plugins.DisabledPlugins = disabled;
        current.Plugins.EnabledPlugins.RemoveAll(id => PluginIds.EqualsCanonical(id, pluginId));

        var runtimeMutation = dotnetRuntime == null
            ? null
            : await dotnetRuntime.ReconcileAfterMutationAsync(pluginId, commitToken).ConfigureAwait(false);

        var discovery = await FinalizePluginContributionRefreshAsync(
            Protocol.AppServer.AppServerMethodNames.PluginRemove,
            commitToken).ConfigureAwait(false);

        AdvancePluginSnapshotRevision();
        var result = BuildOperationResult(
            discovery,
            pluginId,
            "applied",
            runtimeMutation?.AffectedPluginIds ?? quiesce?.AffectedPluginIds ?? [],
            runtimeMutation?.Diagnostics ?? []);
        var offlineBindings = TryMoveActiveAppBindingsOfflineForPlugin(
            removalCatalog,
            pluginId,
            "The owning plugin was removed.");
        return await WriteWithLifecycleNotificationsAsync(
            request.Message,
            result,
            appListUpdate,
            offlineBindings,
            [pluginId, .. (runtimeMutation?.AffectedPluginIds ?? quiesce?.AffectedPluginIds ?? [])],
            ct);
    }

    private static string RequireLifecyclePluginId(Protocol.Optional<string> value)
        => RequireLifecyclePluginId(Read(value));

    private static string RequireLifecyclePluginId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");
        return PluginIds.Canonicalize(id.Trim());
    }

    private static string MapOutcome(PluginRuntimeMutationOutcome outcome) => outcome switch
    {
        PluginRuntimeMutationOutcome.Applied => "applied",
        PluginRuntimeMutationOutcome.NoChange => "noChange",
        _ => "notApplied"
    };

    private static IReadOnlyList<string> ChangedRuntimeIds(
        PluginRuntimeSnapshot before,
        PluginRuntimeSnapshot after,
        string selectedPluginId)
    {
        var old = before.Plugins.ToDictionary(static plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase);
        return after.Plugins
            .Where(plugin => !PluginIds.EqualsCanonical(plugin.PluginId, selectedPluginId)
                             && (!old.TryGetValue(plugin.PluginId, out var previous)
                                 || previous != plugin))
            .Select(static plugin => plugin.PluginId)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
    }

    // The response is written before the notifications: a client must never see an invalidation
    // for a mutation whose outcome it has not been told yet.
    private async Task<AppServerTypedResult<TResult>> WriteWithLifecycleNotificationsAsync<TResult>(
        AppServerIncomingMessage msg,
        TResult result,
        Contract.AppListUpdatedNotification? appListUpdatedParams,
        IReadOnlyList<AppBindingSnapshot> offlineBindings,
        IReadOnlyList<string> pluginSnapshotIds,
        CancellationToken ct)
        where TResult : class
    {
        Contract.PluginSnapshotUpdatedNotification? pluginSnapshot = pluginSnapshotIds.Count == 0
            ? null
            : new Contract.PluginSnapshotUpdatedNotification
            {
                SnapshotRevision = CurrentPluginSnapshotRevision,
                PluginIds = pluginSnapshotIds
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray()
            };
        var responseBarrier = pluginSnapshot == null ? null : QueuePluginSnapshotUpdated(pluginSnapshot);
        managementState.CompleteCurrentMutation();

        var deliveryFailure = await TryWriteMutationResponseAsync(msg, result, ct).ConfigureAwait(false);
        if (deliveryFailure == null)
        {
            try
            {
                if (appListUpdatedParams != null)
                {
                    await transport.NotifyContractAsync(
                        Protocol.AppServer.AppServerRpc.AppListUpdated,
                        appListUpdatedParams,
                        ct).ConfigureAwait(false);
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
                        ct).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                deliveryFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
            }
        }

        responseBarrier?.TrySetResult();

        if (pluginSnapshot != null && responseBarrier == null && deliveryFailure == null)
        {
            await NotifyInitiatingPluginSnapshotUpdatedAsync(pluginSnapshot, ct).ConfigureAwait(false);
        }

        deliveryFailure?.Throw();
        return AppServerTypedResult<TResult>.Written;
    }

    private TaskCompletionSource? QueuePluginSnapshotUpdated(
        Contract.PluginSnapshotUpdatedNotification notification)
    {
        if (broadcastPluginSnapshotUpdated == null)
            return null;

        var responseCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broadcastPluginSnapshotUpdated(transport, notification, responseCompleted.Task);
        return responseCompleted;
    }

    private Task NotifyInitiatingPluginSnapshotUpdatedAsync(
        Contract.PluginSnapshotUpdatedNotification notification,
        CancellationToken cancellationToken) =>
        transport.NotifyContractAsync(
            Protocol.AppServer.AppServerRpc.PluginSnapshotUpdated,
            notification,
            cancellationToken);

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
        string diagnostic)
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
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.PluginList);
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

    private static T? Read<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);
}
