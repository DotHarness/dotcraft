using System.Text.Json.Nodes;

namespace DotCraft.AppServerTestClient;

internal static class DeferredLoadingSmokeWorkspace
{
    public const string McpServerName = "deferred-loading-smoke";

    public static string Create(
        string workRoot,
        DeferredLoadingSmokeProviderSelection provider,
        string mcpServerCommand,
        IReadOnlyList<string>? mcpServerArguments = null)
    {
        var workspacePath = Path.Combine(
            workRoot,
            SanitizePathSegment(provider.Protocol),
            SanitizePathSegment(provider.ProviderId),
            DeferredLoadingSmokeScenarios.NativeDeferredToolSearch);
        var craftPath = Path.Combine(workspacePath, ".craft");
        Directory.CreateDirectory(craftPath);
        File.WriteAllText(
            Path.Combine(craftPath, "config.json"),
            BuildConfigJson(provider.ProviderId, provider.Model, mcpServerCommand, mcpServerArguments));
        return workspacePath;
    }

    public static string BuildConfigJson(
        string providerId,
        string model,
        string mcpServerCommand,
        IReadOnlyList<string>? mcpServerArguments = null)
    {
        var arguments = mcpServerArguments is { Count: > 0 }
            ? new JsonArray(mcpServerArguments.Select(static argument => JsonValue.Create(argument)).ToArray<JsonNode?>())
            : new JsonArray("deferred-loading-smoke-mcp-server");
        var root = new JsonObject
        {
            ["ProviderId"] = providerId,
            ["Model"] = model,
            ["McpServers"] = new JsonObject
            {
                [McpServerName] = new JsonObject
                {
                    ["Transport"] = "stdio",
                    ["Command"] = mcpServerCommand,
                    ["Arguments"] = arguments,
                    ["StartupTimeoutSec"] = 10,
                    ["ToolTimeoutSec"] = 20
                }
            },
            ["LspServers"] = new JsonArray(),
            ["ExternalChannels"] = new JsonArray(),
            ["Tools"] = new JsonObject
            {
                ["DeferredLoading"] = new JsonObject
                {
                    ["Strategy"] = "Native",
                    ["DeferThreshold"] = 1,
                    ["MaxSearchResults"] = 5,
                    ["AlwaysLoadedTools"] = new JsonArray()
                }
            },
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

        return root.ToJsonString(DeferredLoadingSmokeJson.Options);
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
