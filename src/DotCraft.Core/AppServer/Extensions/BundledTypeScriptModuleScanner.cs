using System.Text.Json;

namespace DotCraft.AppServer;

/// <summary>
/// Discovers built-in TypeScript channel manifests donated by Desktop through Hub runtime hints.
/// </summary>
public static class BundledTypeScriptModuleScanner
{
    private const string ModulesDirEnv = "DOTCRAFT_MODULES_DIR";

    public static IReadOnlyList<ChannelDescriptor> ScanFromEnvironment()
    {
        var modulesDir = Environment.GetEnvironmentVariable(ModulesDirEnv);
        if (string.IsNullOrWhiteSpace(modulesDir) || !Directory.Exists(modulesDir))
            return [];

        var channels = new List<ChannelDescriptor>();
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

                channels.Add(new ChannelDescriptor
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
