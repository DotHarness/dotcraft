using System.Text.Json.Nodes;

namespace DotCraft.AppServerTestClient;

internal static class StreamRetrySmokeWorkspace
{
    public const int StreamMaxRetries = 1;
    public const int StreamIdleTimeoutMs = 10_000;

    public static string Create(
        string workRoot,
        StreamRetrySmokeProviderSelection provider,
        Uri proxyEndpoint)
    {
        var workspacePath = Path.Combine(
            workRoot,
            SanitizePathSegment(provider.Protocol),
            SanitizePathSegment(provider.ProviderId),
            "stream-retry");
        var craftPath = Path.Combine(workspacePath, ".craft");
        Directory.CreateDirectory(craftPath);
        File.WriteAllText(
            Path.Combine(craftPath, "config.json"),
            BuildConfigJson(provider.ProviderId, provider.Model, proxyEndpoint));
        return workspacePath;
    }

    public static string BuildConfigJson(
        string providerId,
        string model,
        Uri proxyEndpoint,
        int streamMaxRetries = StreamMaxRetries,
        int streamIdleTimeoutMs = StreamIdleTimeoutMs)
    {
        var provider = new JsonObject
        {
            ["EndPoint"] = proxyEndpoint.GetLeftPart(UriPartial.Authority),
            ["StreamMaxRetries"] = streamMaxRetries,
            ["StreamIdleTimeoutMs"] = streamIdleTimeoutMs
        };

        var root = new JsonObject
        {
            ["ProviderId"] = providerId,
            ["ProviderPreferences"] = new JsonObject { [providerId] = SmokeModelPreference.Create(model) },
            ["McpServers"] = new JsonArray(),
            ["LspServers"] = new JsonArray(),
            ["ExternalChannels"] = new JsonArray(),
            ["Providers"] = new JsonObject
            {
                [providerId] = provider
            },
            ["Memory"] = new JsonObject
            {
                ["AutoConsolidateEnabled"] = false
            },
            ["Tracing"] = new JsonObject
            {
                ["Enabled"] = true
            }
        };

        return root.ToJsonString(CompactSmokeJson.Options);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }
}
