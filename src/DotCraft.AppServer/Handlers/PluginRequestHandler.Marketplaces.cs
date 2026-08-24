using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Plugins.Marketplaces;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
    private async Task<AppServerTypedResult<Contract.MarketplaceAddResult>> HandleMarketplaceAddAsync(
        AppServerTypedRequest<Contract.MarketplaceAddParams> request,
        CancellationToken ct)
    {
        RequireMarketplaceSupport(Protocol.AppServer.AppServerMethodNames.MarketplaceAdd);
        var p = request.Params;
        var commitToken = EnterMutationCommit(ct);

        var result = await RunMarketplaceOperationAsync(
            () => CreateMarketplaceManager().AddAsync(
                new MarketplaceAddRequest(
                    Read(p.Source) ?? string.Empty,
                    Read(p.Ref),
                    Read(p.SparsePaths)?.ToList(),
                    Read(p.MarketplacePath)),
                commitToken)).ConfigureAwait(false);

        var discovery = NotifyMarketplaceChanged(Protocol.AppServer.AppServerMethodNames.MarketplaceAdd);
        var wireResult = new Contract.MarketplaceAddResult
            {
                Marketplace = MapMarketplaceToWire(result.Marketplace, discovery),
                AlreadyAdded = result.AlreadyAdded
            };
        return result.AlreadyAdded
            ? AppServerTypedResult<Contract.MarketplaceAddResult>.FromResult(wireResult)
            : await WriteMarketplaceMutationAsync(request.Message, wireResult, ct).ConfigureAwait(false);
    }

    private async Task<AppServerTypedResult<Contract.MarketplaceRemoveResult>> HandleMarketplaceRemoveAsync(
        AppServerTypedRequest<Contract.MarketplaceRemoveParams> request,
        CancellationToken ct)
    {
        RequireMarketplaceSupport(Protocol.AppServer.AppServerMethodNames.MarketplaceRemove);
        var name = Read(request.Params.Name);
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");
        var commitToken = EnterMutationCommit(ct);

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
        return await WriteMarketplaceMutationAsync(
            request.Message,
            new Contract.MarketplaceRemoveResult
            {
                Name = removed.Name,
                RemovedRoot = OmitIfNull(removed.RemovedRoot)
            },
            ct).ConfigureAwait(false);
    }

    private async Task<AppServerTypedResult<Contract.MarketplaceRefreshResult>> HandleMarketplaceRefreshAsync(
        AppServerTypedRequest<Contract.MarketplaceRefreshParams> request,
        CancellationToken ct)
    {
        RequireMarketplaceSupport(Protocol.AppServer.AppServerMethodNames.MarketplaceRefresh);
        var commitToken = EnterMutationCommit(ct);
        var result = await RunMarketplaceOperationAsync(
            () => CreateMarketplaceManager().RefreshAsync(Read(request.Params.Name), commitToken)).ConfigureAwait(false);

        var discovery = NotifyMarketplaceChanged(Protocol.AppServer.AppServerMethodNames.MarketplaceRefresh);
        return await WriteMarketplaceMutationAsync(
            request.Message,
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
            },
            ct).ConfigureAwait(false);
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
        var configPath = workspaceConfig.RequirePersonalConfigPath("marketplace persistence");
        var userDataPath = Path.GetDirectoryName(configPath)
            ?? throw new InvalidOperationException("UserDataPath is required for marketplace persistence.");
        return new MarketplaceManager(userDataPath, configPath);
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

    private async Task<AppServerTypedResult<TResult>> WriteMarketplaceMutationAsync<TResult>(
        AppServerIncomingMessage message,
        TResult result,
        CancellationToken ct)
        where TResult : class
    {
        AdvancePluginSnapshotRevision();
        var notification = new Contract.PluginSnapshotUpdatedNotification
        {
            SnapshotRevision = CurrentPluginSnapshotRevision,
            PluginIds = Array.Empty<string>()
        };
        var responseBarrier = QueuePluginSnapshotUpdated(notification);
        managementState.CompleteCurrentMutation();

        var responseFailure = await TryWriteMutationResponseAsync(message, result, ct).ConfigureAwait(false);
        responseBarrier?.TrySetResult();
        try
        {
            if (responseBarrier == null && responseFailure == null)
                await NotifyInitiatingPluginSnapshotUpdatedAsync(notification, ct).ConfigureAwait(false);
        }
        catch when (responseFailure != null)
        {
            // The initiating transport already failed; a host broadcast has still completed.
        }

        responseFailure?.Throw();
        return AppServerTypedResult<TResult>.Written;
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
            : workspaceConfig.PersonalConfigPath is { } personalConfigPath
                ? [.. PluginsConfigPersistence.ReadPluginRegistries(personalConfigPath)]
                : [];
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
}
