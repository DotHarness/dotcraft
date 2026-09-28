using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.Commands.Custom;

public sealed partial class CustomCommandLoader
{
    private void LoadPluginCommands(Dictionary<string, CustomCommandInfo> commands)
    {
        var data = Path.GetDirectoryName(WorkspaceCommandsPath)!;
        var userData = UserCommandsPath == null ? null : Path.GetDirectoryName(UserCommandsPath);
        var config = AppConfig.LoadWithGlobalFallback(Path.Combine(data, "config.json"), userData == null ? null : Path.Combine(userData, "config.json"));
        var discovery = new PluginDiscoveryService(userData == null ? null : Path.Combine(userData, "plugins"), craftHome: userData)
            .Discover(config, Path.GetDirectoryName(data)!, data);
        foreach (var plugin in discovery.Plugins)
        {
            if (plugin.Manifest.CommandsPath is not { } directory || !Directory.Exists(directory)) continue;
            var contributed = new Dictionary<string, CustomCommandInfo>(StringComparer.OrdinalIgnoreCase);
            ScanDirectory(directory, "plugin", contributed);
            foreach (var (name, command) in contributed)
            {
                command.Name = plugin.Manifest.Id + ":" + name;
                commands.TryAdd(command.Name, command);
            }
        }
    }
}
