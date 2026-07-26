using System.Text.Json.Nodes;

namespace DotCraft.AppServerTestClient;

internal static class CompactSmokeWorkspace
{
    public static string Create(
        string workRoot,
        CompactSmokeProviderSelection provider,
        string scenario)
    {
        var workspacePath = Path.Combine(
            workRoot,
            SanitizePathSegment(provider.Protocol),
            SanitizePathSegment(provider.ProviderId),
            SanitizePathSegment(scenario));
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
            ["ProviderPreferences"] = new JsonObject { [providerId] = SmokeModelPreference.Create(model) },
            ["McpServers"] = new JsonArray(),
            ["LspServers"] = new JsonArray(),
            ["ExternalChannels"] = new JsonArray(),
            ["Tracing"] = new JsonObject
            {
                ["Enabled"] = true
            },
            ["Compaction"] = new JsonObject
            {
                ["AutoCompactEnabled"] = true,
                ["ReactiveCompactEnabled"] = true,
                ["ContextWindow"] = 40000,
                ["MaxContextWindow"] = 40000,
                ["SummaryReserveTokens"] = 4000,
                ["SummaryMaxOutputTokens"] = 1500,
                ["AutoCompactBufferTokens"] = 14000,
                ["WarningBufferTokens"] = 1000,
                ["ErrorBufferTokens"] = 500,
                ["ManualCompactBufferTokens"] = 3000,
                ["KeepRecentMinTokens"] = 1,
                ["KeepRecentMinGroups"] = 1,
                ["KeepRecentMaxTokens"] = 8000,
                ["MicrocompactEnabled"] = false,
                ["MaxConsecutiveFailures"] = 1
            }
        };

        return root.ToJsonString(CompactSmokeJson.Options);
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
