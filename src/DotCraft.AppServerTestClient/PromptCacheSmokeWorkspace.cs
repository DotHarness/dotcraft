using System.Text.Json.Nodes;

namespace DotCraft.AppServerTestClient;

internal static class PromptCacheSmokeWorkspace
{
    public static string Create(
        string workRoot,
        PromptCacheSmokeProviderSelection provider)
    {
        var workspacePath = Path.Combine(
            workRoot,
            SanitizePathSegment(provider.Protocol),
            SanitizePathSegment(provider.ProviderId),
            PromptCacheSmokeScenarios.PromptCacheBaseline);
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
            ["ProviderModels"] = new JsonObject { [providerId] = model },
            ["McpServers"] = new JsonArray(),
            ["LspServers"] = new JsonArray(),
            ["ExternalChannels"] = new JsonArray(),
            ["Tracing"] = new JsonObject
            {
                ["Enabled"] = true
            },
            ["Compaction"] = new JsonObject
            {
                ["AutoCompactEnabled"] = false,
                ["ReactiveCompactEnabled"] = false,
                ["ContextWindow"] = 256000,
                ["MaxContextWindow"] = 256000,
                ["MicrocompactEnabled"] = false
            }
        };

        return root.ToJsonString(PromptCacheSmokeJson.Options);
    }

    public static string TraceDbPath(string workspacePath) =>
        Path.Combine(workspacePath, ".craft", "state.db");

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }
}
