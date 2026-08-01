using DotCraft.Configuration;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles <c>channel/*</c> and <c>externalChannel/*</c> wire methods.
/// </summary>
internal sealed class ChannelRequestHandler(
    IAppServerChannelListContributor channelListContributor,
    IChannelStatusProvider? channelStatusProvider,
    WorkspaceConfigEditor workspaceConfig,
    ExternalChannelConfigService externalChannelConfig,
    string? workspaceCraftPath,
    IAppConfigMonitor? appConfigMonitor,
    Func<ExternalChannelEntry, CancellationToken, Task>? onExternalChannelUpserted,
    Func<string, CancellationToken, Task>? onExternalChannelRemoved,
    IExternalChannelLogProvider? externalChannelLogProvider) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ChannelList, HandleChannelListAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ChannelStatus, HandleChannelStatusAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ExternalChannelList, HandleExternalChannelListAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ExternalChannelGet, HandleExternalChannelGetAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ExternalChannelUpsert, HandleExternalChannelUpsertAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ExternalChannelRemove, HandleExternalChannelRemoveAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ExternalChannelLogs, HandleExternalChannelLogsAsync);
    }

    private Task<object?> HandleChannelListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;

        var channels = new List<ChannelInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string category)
        {
            if (!seen.Add(name))
                return;
            channels.Add(new ChannelInfo { Name = name, Category = category });
        }

        channelListContributor.AppendBaseChannels(channels, seen);

        if (!string.IsNullOrEmpty(workspaceCraftPath))
        {
            var configPath = Path.Combine(workspaceCraftPath, "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var cfg = AppConfig.LoadWithGlobalFallback(configPath, workspaceConfig.EffectiveGlobalConfigPath);
                    foreach (var entry in cfg.ExternalChannels)
                    {
                        if (!entry.Enabled || string.IsNullOrWhiteSpace(entry.Name))
                            continue;
                        Add(entry.Name, "external");
                    }
                }
                catch
                {
                    // Best-effort: invalid config should not fail channel/list.
                }
            }
        }

        channels.Sort((a, b) =>
        {
            var cmp = CategoryOrder(a.Category).CompareTo(CategoryOrder(b.Category));
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return Task.FromResult<object?>(new ChannelListResult { Channels = channels });
    }

    private Task<object?> HandleChannelStatusAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;

        if (channelStatusProvider == null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ChannelStatus);

        var statuses = channelStatusProvider.GetChannelStatuses();
        return Task.FromResult<object?>(new ChannelStatusResult { Channels = [.. statuses] });
    }

    private Task<object?> HandleExternalChannelListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;
        var channels = externalChannelConfig.LoadWorkspaceChannels();
        return Task.FromResult<object?>(new ExternalChannelListResult
        {
            Channels = channels.Select(ExternalChannelWireMapper.ToWire).ToList()
        });
    }

    private Task<object?> HandleExternalChannelGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = AppServerParams.Get<ExternalChannelGetParams>(msg);
        externalChannelConfig.EnsureManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var channel = externalChannelConfig.LoadWorkspaceChannels()
            .FirstOrDefault(c => string.Equals(c.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (channel == null)
            throw AppServerErrors.ExternalChannelNotFound(p.Name);

        return Task.FromResult<object?>(new ExternalChannelGetResult
        {
            Channel = ExternalChannelWireMapper.ToWire(channel)
        });
    }

    private async Task<object?> HandleExternalChannelUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ExternalChannelUpsertParams>(msg);
        externalChannelConfig.EnsureManagementAvailable();
        ExternalChannelWireMapper.ValidateConfig(p.Channel);

        var channel = ExternalChannelWireMapper.FromWire(p.Channel);
        externalChannelConfig.EnsureNameAvailable(channel.Name);

        var channels = externalChannelConfig.LoadWorkspaceChannels();
        var existingIndex = channels.FindIndex(c => string.Equals(c.Name, channel.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            channels[existingIndex] = channel;
        else
            channels.Add(channel);

        externalChannelConfig.SaveWorkspaceChannels(channels);
        if (onExternalChannelUpserted != null)
            await onExternalChannelUpserted(channel, ct);
        appConfigMonitor?.NotifyChanged(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExternalChannelUpsert,
            [ConfigChangeRegions.ExternalChannel]);

        return new ExternalChannelUpsertResult
        {
            Channel = ExternalChannelWireMapper.ToWire(channel)
        };
    }

    private async Task<object?> HandleExternalChannelRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ExternalChannelRemoveParams>(msg);
        externalChannelConfig.EnsureManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var channels = externalChannelConfig.LoadWorkspaceChannels();
        var removed = channels.RemoveAll(c => string.Equals(c.Name, p.Name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            throw AppServerErrors.ExternalChannelNotFound(p.Name);

        externalChannelConfig.SaveWorkspaceChannels(channels);
        if (onExternalChannelRemoved != null)
            await onExternalChannelRemoved(p.Name, ct);
        appConfigMonitor?.NotifyChanged(
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExternalChannelRemove,
            [ConfigChangeRegions.ExternalChannel]);

        return new ExternalChannelRemoveResult { Removed = true };
    }

    private Task<object?> HandleExternalChannelLogsAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = AppServerParams.Get<ExternalChannelLogsParams>(msg);
        externalChannelConfig.EnsureManagementAvailable();
        if (externalChannelLogProvider == null)
            throw AppServerErrors.InvalidRequest("External channel log retrieval is not available.");
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var lines = externalChannelLogProvider.GetRecentExternalChannelLogs(p.Name.Trim(), p.Tail);
        return Task.FromResult<object?>(new ExternalChannelLogsResult
        {
            Name = p.Name.Trim(),
            Lines = lines.ToList()
        });
    }

    private static int CategoryOrder(string c) => c switch
    {
        "builtin" => 0,
        "social" => 1,
        "system" => 2,
        "external" => 3,
        _ => 4
    };
}
