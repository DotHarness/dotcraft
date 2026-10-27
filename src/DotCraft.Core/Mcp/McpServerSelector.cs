namespace DotCraft.Mcp;

public static class McpServerSelector
{
    public static IReadOnlySet<string> Resolve(
        IEnumerable<string> selectors,
        IReadOnlyCollection<McpServerConfig> servers)
    {
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in selectors)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var selector = raw.Trim();
            resolved.Add(Find(selector, servers)?.Name ?? selector);
        }

        return resolved;
    }

    public static McpServerConfig? Find(string selector, IReadOnlyCollection<McpServerConfig> servers) =>
        servers.FirstOrDefault(server => string.Equals(server.Name, selector, StringComparison.Ordinal))
        ?? servers
            .Where(server => server.Origin.IsPlugin
                             && string.Equals(server.Origin.DeclaredName, selector, StringComparison.Ordinal))
            .OrderBy(server => server.Origin.PluginId, StringComparer.Ordinal)
            .FirstOrDefault();
}
