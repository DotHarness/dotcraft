using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;
namespace DotCraft.Mcp;

/// <summary>
/// Composes the user-configured and binding-owned MCP inputs for one thread.
/// </summary>
/// <remarks>
/// The nullable thread list selects inherited, disabled, or replacement user configuration.
/// Binding-owned servers are always appended and receive connection-unique runtime names so
/// that their sessions cannot be shadowed by user configuration in <see cref="McpClientManager"/>.
/// </remarks>
public static class McpServerComposition
{
    /// <summary>
    /// Builds the effective server list for a thread while preserving the independent,
    /// additive semantics of binding-owned MCP sessions.
    /// </summary>
    /// <param name="threadId">The thread that owns the composed runtime.</param>
    /// <param name="threadServers">
    /// <see langword="null"/> to inherit <paramref name="inheritedServers"/>; an empty list to
    /// disable inherited user configuration; or a non-empty list to replace it.
    /// </param>
    /// <param name="inheritedServers">Effective workspace and enabled-plugin servers.</param>
    /// <param name="bindingServers">Binding-owned servers to append independently.</param>
    public static IReadOnlyList<McpServerConfig> Compose(
        string threadId,
        IReadOnlyList<McpServerConfig>? threadServers,
        IEnumerable<McpServerConfig> inheritedServers,
        IEnumerable<McpServerConfig> bindingServers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(inheritedServers);
        ArgumentNullException.ThrowIfNull(bindingServers);

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

        foreach (var server in bindingServers)
        {
            if (string.IsNullOrWhiteSpace(server.Name))
                continue;
            if (!server.Origin.IsBinding || string.IsNullOrWhiteSpace(server.Origin.BindingId))
                throw new ArgumentException(
                    $"Binding MCP server '{server.Name}' must have a binding origin and binding identifier.",
                    nameof(bindingServers));

            var clone = server.Clone();
            var declaredName = clone.Origin.DeclaredName ?? clone.Name;
            clone.Origin = McpServerOrigin.Binding(clone.Origin.BindingId!, declaredName);
            clone.Name = CreateUniqueBindingRuntimeName(
                clone.Origin.BindingId!,
                declaredName,
                runtimeNames);
            result.Add(clone);
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

    private static string CreateUniqueBindingRuntimeName(
        string bindingId,
        string declaredName,
        ISet<string> runtimeNames)
    {
        var baseName = $"binding:{bindingId}:{declaredName}";
        var candidate = baseName;
        var suffix = 2;
        while (!runtimeNames.Add(candidate))
            candidate = $"{baseName}~{suffix++}";
        return candidate;
    }
}
