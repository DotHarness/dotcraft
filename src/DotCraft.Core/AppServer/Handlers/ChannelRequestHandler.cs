using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

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
        table.Map(Protocol.AppServer.AppServerRpc.ChannelList, HandleChannelListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ChannelStatus, HandleChannelStatusAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ExternalChannelList, HandleExternalChannelListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ExternalChannelGet, HandleExternalChannelGetAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ExternalChannelUpsert, HandleExternalChannelUpsertAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ExternalChannelRemove, HandleExternalChannelRemoveAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ExternalChannelLogs, HandleExternalChannelLogsAsync);
    }

    private Task<AppServerTypedResult<Contract.ChannelListResult>> HandleChannelListAsync(
        AppServerTypedRequest<Protocol.RpcEmpty> request,
        CancellationToken ct)
    {
        _ = request;
        _ = ct;

        var channels = new List<ChannelDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string category)
        {
            if (!seen.Add(name))
                return;
            channels.Add(new ChannelDescriptor { Name = name, Category = category });
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

        return Task.FromResult(AppServerTypedResult<Contract.ChannelListResult>.FromResult(new Contract.ChannelListResult
        {
            Channels = channels.Select(channel => new Contract.ChannelInfo
            {
                Name = channel.Name,
                Category = channel.Category
            }).ToList()
        }));
    }

    private Task<AppServerTypedResult<Contract.ChannelStatusResult>> HandleChannelStatusAsync(
        AppServerTypedRequest<Protocol.RpcEmpty> request,
        CancellationToken ct)
    {
        _ = request;
        _ = ct;

        if (channelStatusProvider == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.ChannelStatus);

        var statuses = channelStatusProvider.GetChannelStatuses();
        return Task.FromResult(AppServerTypedResult<Contract.ChannelStatusResult>.FromResult(new Contract.ChannelStatusResult
        {
            Channels = statuses.Select(status => new Contract.ChannelStatusInfo
            {
                Name = status.Name,
                Category = status.Category,
                Enabled = status.Enabled,
                Running = status.Running
            }).ToList()
        }));
    }

    private Task<AppServerTypedResult<Contract.ExternalChannelListResult>> HandleExternalChannelListAsync(
        AppServerTypedRequest<Protocol.RpcEmpty> request,
        CancellationToken ct)
    {
        _ = request;
        _ = ct;
        var channels = externalChannelConfig.LoadWorkspaceChannels();
        return Task.FromResult(AppServerTypedResult<Contract.ExternalChannelListResult>.FromResult(new Contract.ExternalChannelListResult
        {
            Channels = channels.Select(ExternalChannelWireMapper.ToContract).ToList()
        }));
    }

    private Task<AppServerTypedResult<Contract.ExternalChannelGetResult>> HandleExternalChannelGetAsync(
        AppServerTypedRequest<Contract.ExternalChannelGetParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var name = RequiredName(request.Params.Name);
        externalChannelConfig.EnsureManagementAvailable();

        var channel = externalChannelConfig.LoadWorkspaceChannels()
            .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (channel == null)
            throw AppServerErrors.ExternalChannelNotFound(name);

        return Task.FromResult(AppServerTypedResult<Contract.ExternalChannelGetResult>.FromResult(new Contract.ExternalChannelGetResult
        {
            Channel = ExternalChannelWireMapper.ToContract(channel)
        }));
    }

    private async Task<AppServerTypedResult<Contract.ExternalChannelUpsertResult>> HandleExternalChannelUpsertAsync(
        AppServerTypedRequest<Contract.ExternalChannelUpsertParams> request,
        CancellationToken ct)
    {
        externalChannelConfig.EnsureManagementAvailable();
        var wire = request.Params.Channel.IsSet
            ? request.Params.Channel.Value
            : throw AppServerErrors.InvalidParams("'channel' is required.");
        ExternalChannelWireMapper.ValidateContract(wire!);

        var channel = ExternalChannelWireMapper.FromContract(wire!);
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
            Protocol.AppServer.AppServerMethodNames.ExternalChannelUpsert,
            [ConfigChangeRegions.ExternalChannel]);

        return AppServerTypedResult<Contract.ExternalChannelUpsertResult>.FromResult(new Contract.ExternalChannelUpsertResult
        {
            Channel = ExternalChannelWireMapper.ToContract(channel)
        });
    }

    private async Task<AppServerTypedResult<Contract.ExternalChannelRemoveResult>> HandleExternalChannelRemoveAsync(
        AppServerTypedRequest<Contract.ExternalChannelRemoveParams> request,
        CancellationToken ct)
    {
        externalChannelConfig.EnsureManagementAvailable();
        var name = RequiredName(request.Params.Name);

        var channels = externalChannelConfig.LoadWorkspaceChannels();
        var removed = channels.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            throw AppServerErrors.ExternalChannelNotFound(name);

        externalChannelConfig.SaveWorkspaceChannels(channels);
        if (onExternalChannelRemoved != null)
            await onExternalChannelRemoved(name, ct);
        appConfigMonitor?.NotifyChanged(
            Protocol.AppServer.AppServerMethodNames.ExternalChannelRemove,
            [ConfigChangeRegions.ExternalChannel]);

        return AppServerTypedResult<Contract.ExternalChannelRemoveResult>.FromResult(
            new Contract.ExternalChannelRemoveResult { Removed = true });
    }

    private Task<AppServerTypedResult<Contract.ExternalChannelLogsResult>> HandleExternalChannelLogsAsync(
        AppServerTypedRequest<Contract.ExternalChannelLogsParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var p = request.Params;
        externalChannelConfig.EnsureManagementAvailable();
        if (externalChannelLogProvider == null)
            throw AppServerErrors.InvalidRequest("External channel log retrieval is not available.");
        var name = RequiredName(p.Name);

        var tail = p.Tail.IsSet ? p.Tail.Value : null;
        var lines = externalChannelLogProvider.GetRecentExternalChannelLogs(name, tail);
        return Task.FromResult(AppServerTypedResult<Contract.ExternalChannelLogsResult>.FromResult(new Contract.ExternalChannelLogsResult
        {
            Name = name,
            Lines = lines.ToList()
        }));
    }

    private static string RequiredName(Protocol.Optional<string> value)
    {
        var name = value.IsSet ? value.Value?.Trim() : null;
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");
        return name;
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
