using System.Text.Json;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Discovers built-in TypeScript channel manifests donated by Desktop through Hub runtime hints.
/// </summary>
public static class BundledTypeScriptModuleScanner
{
    private const string ModulesDirEnv = "DOTCRAFT_MODULES_DIR";

    public static IReadOnlyList<ChannelInfo> ScanFromEnvironment()
    {
        var modulesDir = Environment.GetEnvironmentVariable(ModulesDirEnv);
        if (string.IsNullOrWhiteSpace(modulesDir) || !Directory.Exists(modulesDir))
            return [];

        var channels = new List<ChannelInfo>();
        foreach (var moduleDir in Directory.EnumerateDirectories(modulesDir))
        {
            var manifestPath = Path.Combine(moduleDir, "manifest.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (!doc.RootElement.TryGetProperty("channelName", out var channelNameElement))
                    continue;
                var channelName = channelNameElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(channelName))
                    continue;

                channels.Add(new ChannelInfo
                {
                    Name = channelName,
                    Category = "external"
                });
            }
            catch
            {
                // Ignore malformed module manifests; Desktop still owns rich module UI diagnostics.
            }
        }

        return channels
            .OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
