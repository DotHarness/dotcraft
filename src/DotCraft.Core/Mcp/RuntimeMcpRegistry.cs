using System.Collections.Concurrent;
using System.Net;

namespace DotCraft.Mcp;

public sealed class RuntimeMcpRegistry
{
    private readonly ConcurrentDictionary<(string OwnerId, string Name), McpServerConfig> _entries =
        new(OwnerNameComparer.Instance);

    public McpServerConfig Register(string ownerId, McpServerConfig server)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(server);

        var name = server.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A runtime MCP server name is required.", nameof(server));

        if (!string.Equals(server.NormalizedTransport, "streamableHttp", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Runtime MCP servers must use the 'streamableHttp' transport.", nameof(server));

        ValidateLoopbackHttpUrl(server.Url);

        var clone = server.Clone();
        clone.Name = name;
        clone.Transport = "streamableHttp";
        clone.Origin = McpServerOrigin.HostRuntime(ownerId, name);
        clone.HasSensitiveHeaders = true;
        clone.RequiredPlugins = clone.RequiredPlugins
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _entries[(ownerId, name)] = clone;
        return clone.Clone();
    }

    public bool Unregister(string ownerId, string name)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(name))
            return false;

        return _entries.TryRemove((ownerId, name.Trim()), out _);
    }

    public bool UnregisterOwner(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return false;

        var removed = false;
        foreach (var key in _entries.Keys)
        {
            if (string.Equals(key.OwnerId, ownerId, StringComparison.Ordinal) && _entries.TryRemove(key, out _))
                removed = true;
        }

        return removed;
    }

    public IReadOnlyList<McpServerConfig> ListActive(IReadOnlySet<string>? enabledPluginIds = null)
    {
        var result = new List<McpServerConfig>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in _entries.Values.OrderBy(static s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!server.Enabled)
                continue;
            if (server.RequiredPlugins.Count > 0
                && (enabledPluginIds is null || !server.RequiredPlugins.Any(enabledPluginIds.Contains)))
            {
                continue;
            }

            if (seenNames.Add(server.Name))
                result.Add(server.Clone());
        }

        return result;
    }

    public static void ValidateLoopbackHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException("A valid absolute HTTP loopback URL is required.", nameof(url));

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Runtime MCP server URLs must use the 'http' scheme on loopback.", nameof(url));

        if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) || !IPAddress.IsLoopback(address))
            throw new ArgumentException("Runtime MCP server URLs must target a loopback IP address (127.0.0.1 or [::1]).", nameof(url));
    }

    private sealed class OwnerNameComparer : IEqualityComparer<(string OwnerId, string Name)>
    {
        public static readonly OwnerNameComparer Instance = new();

        public bool Equals((string OwnerId, string Name) x, (string OwnerId, string Name) y) =>
            string.Equals(x.OwnerId, y.OwnerId, StringComparison.Ordinal)
            && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string OwnerId, string Name) obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.OwnerId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
