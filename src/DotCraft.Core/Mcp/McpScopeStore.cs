using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Mcp;

public static class McpScopeStore
{
    public static List<McpServerConfig> Read(string path, string scope)
    {
        var document = AtomicConfigDocument.Read(path);
        var node = document[AtomicConfigDocument.Key(document, "McpServers") ?? "McpServers"];
        if (node == null) return [];
        if (node is not JsonObject servers) throw new InvalidDataException("McpServers must be an object.");
        return servers.Select(pair =>
        {
            var server = pair.Value!.Deserialize<McpServerConfig>(AppConfig.SerializerOptions)!;
            server.Name = pair.Key;
            server.Origin = new McpServerOrigin { Kind = scope };
            return server;
        }).ToList();
    }

    public static void Upsert(string path, McpServerConfig server) => AtomicConfigDocument.Update(path, root =>
    {
        var servers = AtomicConfigDocument.Object(root, "McpServers");
        servers[AtomicConfigDocument.Key(servers, server.Name) ?? server.Name] = JsonSerializer.SerializeToNode(server, AppConfig.SerializerOptions);
    });

    public static bool Remove(string path, string name)
    {
        var removed = false;
        AtomicConfigDocument.Update(path, root =>
        {
            var servers = AtomicConfigDocument.Object(root, "McpServers");
            if (AtomicConfigDocument.Key(servers, name) is { } key) removed = servers.Remove(key);
        });
        return removed;
    }

    public static List<McpServerConfig> Effective(string workspacePath, string? userPath)
    {
        var servers = userPath == null ? [] : Read(userPath, "user");
        foreach (var server in Read(workspacePath, "workspace"))
        {
            servers.RemoveAll(s => string.Equals(s.Name, server.Name, StringComparison.OrdinalIgnoreCase));
            servers.Add(server);
        }
        return servers;
    }

    public static IEnumerable<McpServerConfig> WithOrigins(IEnumerable<McpServerConfig> servers, string workspacePath, string? userPath)
    {
        var workspaceNames = Read(workspacePath, "workspace").Select(server => server.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userNames = userPath == null ? new HashSet<string>() : Read(userPath, "user").Select(server => server.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return servers.Select(server =>
        {
            var clone = server.Clone();
            if (userNames.Contains(server.Name) && !workspaceNames.Contains(server.Name)) clone.Origin = new() { Kind = "user" };
            return clone;
        });
    }
}
