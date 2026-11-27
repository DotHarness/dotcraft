namespace DotCraft.Mcp;

public static class McpServerComposition
{
    public static IReadOnlyList<McpServerConfig> Compose(
        string threadId,
        IReadOnlyList<McpServerConfig>? threadServers,
        IEnumerable<McpServerConfig> inheritedServers,
        IEnumerable<McpServerConfig>? additionalServers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(inheritedServers);

        var result = new List<McpServerConfig>();
        var runtimeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (threadServers is null)
        {
            foreach (var server in inheritedServers)
                AddIfNamed(result, runtimeNames, server.Clone());
        }
        else
        {
            foreach (var server in threadServers)
            {
                if (string.IsNullOrWhiteSpace(server.Name))
                    continue;

                var clone = server.Clone();
                var declaredName = clone.Origin.DeclaredName ?? clone.Name;
                clone.Origin = McpServerOrigin.Thread(threadId, declaredName);
                AddIfNamed(result, runtimeNames, clone);
            }
        }

        if (additionalServers is not null)
        {
            foreach (var server in additionalServers)
            {
                if (string.IsNullOrWhiteSpace(server.Name))
                    continue;

                var clone = server.Clone();
                var declaredName = clone.Origin.DeclaredName ?? clone.Name;
                clone.Origin = McpServerOrigin.Thread(threadId, declaredName);
                UpsertByName(result, runtimeNames, clone);
            }
        }

        return result;
    }

    private static void AddIfNamed(
        ICollection<McpServerConfig> result,
        ISet<string> runtimeNames,
        McpServerConfig server)
    {
        if (!string.IsNullOrWhiteSpace(server.Name) && runtimeNames.Add(server.Name))
            result.Add(server);
    }

    private static void UpsertByName(
        List<McpServerConfig> result,
        ISet<string> runtimeNames,
        McpServerConfig server)
    {
        if (runtimeNames.Add(server.Name))
        {
            result.Add(server);
            return;
        }

        var existingIndex = result.FindIndex(candidate =>
            string.Equals(candidate.Name, server.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            result[existingIndex] = server;
        else
            result.Add(server);
    }
}
