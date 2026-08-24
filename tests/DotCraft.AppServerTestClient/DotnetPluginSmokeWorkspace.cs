using System.Text.Json.Nodes;

namespace DotCraft.AppServerTestClient;

internal static class DotnetPluginSmokeWorkspace
{
    public static string Create(string workRoot, DotnetPluginSmokeProviderSelection provider)
    {
        var workspacePath = Path.Combine(workRoot, "workspace");
        var craftPath = Path.Combine(workspacePath, ".craft");
        Directory.CreateDirectory(craftPath);
        File.WriteAllText(
            Path.Combine(craftPath, "config.json"),
            BuildConfigJson(provider.ProviderId, provider.Model));
        return workspacePath;
    }

    public static string BuildConfigJson(string providerId, string model)
    {
        var root = new JsonObject
        {
            ["ProviderId"] = providerId,
            ["ProviderPreferences"] = new JsonObject
            {
                [providerId] = SmokeModelPreference.Create(model)
            },
            ["McpServers"] = new JsonObject(),
            ["LspServers"] = new JsonObject(),
            ["ExternalChannels"] = new JsonArray(),
            ["Tracing"] = new JsonObject { ["Enabled"] = false },
            ["Compaction"] = new JsonObject
            {
                ["AutoCompactEnabled"] = false,
                ["ReactiveCompactEnabled"] = false,
                ["MicrocompactEnabled"] = false
            }
        };
        return root.ToJsonString(DotnetPluginSmokeJson.Options);
    }

    public static async Task DeleteWorkspaceAsync(string workspacePath)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (!Directory.Exists(workspacePath))
                return;
            try
            {
                Directory.Delete(workspacePath, recursive: true);
                return;
            }
            catch (Exception exception) when (
                attempt < 19 && exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }
    }
}
