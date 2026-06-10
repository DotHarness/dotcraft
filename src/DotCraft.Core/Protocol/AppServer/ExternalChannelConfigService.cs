using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Workspace persistence helper for external channel configuration.
/// </summary>
internal sealed class ExternalChannelConfigService(
    IAppServerChannelListContributor channelListContributor,
    string? workspaceCraftPath)
{
    public void EnsureManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("externalChannel/*");
    }

    public List<ExternalChannelEntry> LoadWorkspaceChannels()
    {
        EnsureManagementAvailable();
        var configPath = Path.Combine(workspaceCraftPath!, "config.json");
        return AppConfig.Load(configPath).ExternalChannels.Select(c => c.Clone()).ToList();
    }

    public void SaveWorkspaceChannels(IReadOnlyCollection<ExternalChannelEntry> channels)
    {
        EnsureManagementAvailable();
        var configPath = Path.Combine(workspaceCraftPath!, "config.json");
        Directory.CreateDirectory(workspaceCraftPath!);
        var root = WorkspaceConfigEditor.LoadObject(configPath);

        var key = WorkspaceConfigEditor.FindCaseInsensitiveKey(root, "ExternalChannels") ?? "ExternalChannels";
        var channelObject = new JsonObject();
        foreach (var channel in channels.Where(c => !string.IsNullOrWhiteSpace(c.Name))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            channelObject[channel.Name] = BuildExternalChannelNode(channel);
        }

        root[key] = channelObject;
        WorkspaceConfigEditor.WriteObject(configPath, root);
    }

    public void EnsureNameAvailable(string name)
    {
        var nativeChannels = new List<ChannelInfo>();
        channelListContributor.AppendBaseChannels(nativeChannels, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (nativeChannels.Any(c =>
                !string.Equals(c.Category, "external", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw AppServerErrors.ExternalChannelNameConflict(
                $"'{name}' conflicts with a native channel name.");
        }
    }

    private static JsonObject BuildExternalChannelNode(ExternalChannelEntry channel)
    {
        var node = new JsonObject
        {
            ["enabled"] = channel.Enabled,
            ["transport"] = ExternalChannelWireMapper.TransportToWire(channel.Transport)
        };

        if (channel.Transport is ExternalChannelTransport.Subprocess or ExternalChannelTransport.ManagedWebsocket)
        {
            if (!string.IsNullOrWhiteSpace(channel.Command))
                node["command"] = channel.Command;
            if (!string.IsNullOrWhiteSpace(channel.BuiltinModule))
                node["builtinModule"] = channel.BuiltinModule;
            if (channel.Args is { Count: > 0 })
                node["args"] = JsonSerializer.SerializeToNode(channel.Args);
            if (!string.IsNullOrWhiteSpace(channel.WorkingDirectory))
                node["workingDirectory"] = channel.WorkingDirectory;
            if (channel.Env is { Count: > 0 })
                node["env"] = JsonSerializer.SerializeToNode(channel.Env);
        }

        return node;
    }
}
