using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class ChannelRequestHandler(
    AppServerConnection connection,
    IAppServerChannelListContributor channelListContributor,
    AppServerConnectionServices services) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.ChannelList, HandleList);
        table.Map(Contract.AppServerRpc.ChannelStatus, HandleStatus);
        table.Map(Contract.AppServerRpc.ChannelSetEnabled, (request, ct) =>
            services.PluginManagementState.RunMutationAsync(token => HandleSetEnabledAsync(request, token), ct));
        table.Map(Contract.AppServerRpc.ChannelLogs, HandleLogs);
        table.Map(Contract.AppServerRpc.ChannelConfigOptionAvailability, HandleConfigOptionAvailabilityAsync);
        table.Map(Contract.AppServerRpc.ChannelSetupRead, HandleSetupRead);
    }

    private Task<AppServerTypedResult<Contract.ChannelListResult>> HandleList(
        AppServerTypedRequest<RpcEmpty> request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var channels = new List<ChannelDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        channelListContributor.AppendBaseChannels(channels, seen);

        if (services.ChannelStatusProvider is { } provider)
        {
            foreach (var status in provider.GetChannelStatuses())
            {
                if (status.Enabled && status.Running && seen.Add(status.Name))
                    channels.Add(new ChannelDescriptor { Name = status.Name, Category = status.Category });
            }
        }

        return Task.FromResult(AppServerTypedResult<Contract.ChannelListResult>.FromResult(new()
        {
            Channels = channels
                .OrderBy(channel => CategoryOrder(channel.Category))
                .ThenBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
                .Select(channel => new Contract.ChannelInfo { Name = channel.Name, Category = channel.Category })
                .ToArray()
        }));
    }

    private Task<AppServerTypedResult<Contract.ChannelStatusResult>> HandleStatus(
        AppServerTypedRequest<RpcEmpty> request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var provider = services.ChannelStatusProvider ?? throw AppServerErrors.MethodNotFound(Contract.AppServerRpc.ChannelStatus.Name);
        return Task.FromResult(AppServerTypedResult<Contract.ChannelStatusResult>.FromResult(new()
        {
            Channels = provider.GetChannelStatuses()
                .OrderBy(channel => CategoryOrder(channel.Category))
                .ThenBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
                .Select(status => new Contract.ChannelStatusInfo
                {
                    Name = status.Name,
                    Category = status.Category,
                    PluginId = status.PluginId,
                    Enabled = status.Enabled,
                    Running = status.Running,
                    RuntimeState = status.RuntimeState is { Length: > 0 } runtimeState ? new Optional<string>(runtimeState) : default,
                    FailureCode = status.FailureCode is { Length: > 0 } failureCode ? new Optional<string>(failureCode) : default
                }).ToArray()
        }));
    }

    private async Task<AppServerTypedResult<RpcEmpty>> HandleSetEnabledAsync(
        AppServerTypedRequest<Contract.ChannelSetEnabledParams> request, CancellationToken ct)
    {
        var provider = RequireManagement(Contract.AppServerRpc.ChannelSetEnabled.Name);
        if (request.Params.ExtensionData is { Count: > 0 })
            throw AppServerErrors.ChannelValidationFailed("Only 'name', 'pluginId', 'enabled', and 'select' may be supplied.");

        var channel = new ChannelEntry
        {
            Name = Required(request.Params.Name, "name"),
            PluginId = Required(request.Params.PluginId, "pluginId"),
            Enabled = request.Params.Enabled
        };
        var origins = new List<ChannelDescriptor>();
        channelListContributor.AppendBaseChannels(origins, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (origins.Any(origin => string.Equals(origin.Name, channel.Name, StringComparison.OrdinalIgnoreCase)))
            throw AppServerErrors.ChannelNameConflict($"'{channel.Name}' conflicts with a session origin.");

        ct.ThrowIfCancellationRequested();
        if (SaveSelection(channel, provider.ChannelPlugins!, request.Params.Select))
        {
            if (services.OnChannelSetEnabled is { } apply)
                await apply(channel, CancellationToken.None);
            services.AppConfigMonitor?.NotifyChanged(Contract.AppServerRpc.ChannelSetEnabled.Name, [ConfigChangeRegions.Channels]);
        }

        return AppServerTypedResult<RpcEmpty>.FromResult(new());
    }

    private bool SaveSelection(ChannelEntry channel, ChannelPluginWorkspace plugins, bool select)
    {
        var applied = false;
        try
        {
            AtomicConfigDocument.Update(Path.Combine(services.WorkspaceCraftPath!, "config.json"), root =>
            {
                var channels = AtomicConfigDocument.Value(root, "Channels") as JsonObject;
                var key = (channels == null ? null : AtomicConfigDocument.Key(channels, channel.Name)) ?? channel.Name;
                var selectedPluginId = channels?[key] is JsonObject selected
                    && AtomicConfigDocument.Value(selected, "pluginId") is JsonValue id
                    && id.TryGetValue<string>(out var pluginId) ? pluginId : null;
                var matchesSelection = string.Equals(selectedPluginId, channel.PluginId, StringComparison.OrdinalIgnoreCase);

                if (!channel.Enabled && !select && !matchesSelection)
                    return;

                if (channel.Enabled || !matchesSelection)
                    plugins.Authorize(channel);

                var entry = new JsonObject { ["pluginId"] = channel.PluginId, ["enabled"] = channel.Enabled };
                if (channel.PluginActivationId is { Length: > 0 } activationId)
                    entry["pluginActivationId"] = activationId;
                (channels ?? AtomicConfigDocument.Object(root, "Channels"))[key] = entry;
                applied = true;
            });
            return applied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw AppServerErrors.ChannelValidationFailed("The workspace channel selection could not be saved.");
        }
    }

    private Task<AppServerTypedResult<Contract.ChannelLogsResult>> HandleLogs(
        AppServerTypedRequest<Contract.ChannelLogsParams> request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var provider = RequireManagement(Contract.AppServerRpc.ChannelLogs.Name);
        var name = Required(request.Params.Name, "name");
        return Task.FromResult(AppServerTypedResult<Contract.ChannelLogsResult>.FromResult(new()
        {
            Name = name,
            Lines = provider.GetRecentChannelLogs(name, request.Params.Tail)
        }));
    }

    private async Task<AppServerTypedResult<Contract.ChannelConfigOptionAvailabilityResult>> HandleConfigOptionAvailabilityAsync(
        AppServerTypedRequest<Contract.ChannelConfigOptionAvailabilityParams> request, CancellationToken ct)
    {
        var provider = RequireManagement(Contract.AppServerRpc.ChannelConfigOptionAvailability.Name);
        var name = Required(request.Params.Name, "name");
        var key = Required(request.Params.Key, "key");
        var available = await provider.GetConfigOptionAvailabilityAsync(name, key, ct);
        return AppServerTypedResult<Contract.ChannelConfigOptionAvailabilityResult>.FromResult(new()
        {
            Name = name,
            Key = key,
            Available = available
        });
    }

    private Task<AppServerTypedResult<Contract.ChannelSetupReadResult>> HandleSetupRead(
        AppServerTypedRequest<Contract.ChannelSetupReadParams> request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var provider = RequireManagement(Contract.AppServerRpc.ChannelSetupRead.Name);
        var pluginId = Required(request.Params.PluginId, "pluginId");
        return Task.FromResult(AppServerTypedResult<Contract.ChannelSetupReadResult>.FromResult(new()
        {
            Artifact = provider.GetChannelSetupArtifact(pluginId)
        }));
    }

    private IChannelRuntimeProvider RequireManagement(string method)
    {
        if (connection.IsChannelAdapter || connection.IsAppPrincipalAuthenticated)
            throw AppServerErrors.InvalidRequest("Channel management requires a workspace management connection.");
        if (string.IsNullOrWhiteSpace(services.WorkspaceCraftPath) || services.ChannelRuntimeProvider?.ChannelPlugins == null)
            throw AppServerErrors.MethodNotFound(method);
        return services.ChannelRuntimeProvider;
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw AppServerErrors.InvalidParams($"'{field}' is required.") : value.Trim();

    private static int CategoryOrder(string category) => category switch
    {
        "builtin" => 0,
        "social" => 1,
        "system" => 2,
        _ => 3
    };
}
