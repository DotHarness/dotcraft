namespace DotCraft.Tools;

/// <summary>
/// A single built-in tool's catalog metadata: its canonical model-visible name,
/// human-readable description, and display icon.
/// </summary>
public sealed record BuiltInToolDescriptor(
    string Name,
    string Description,
    string Icon,
    bool RpcEligible = false);

/// <summary>
/// Enumerates the built-in tools the server can expose to the model from the Core assembly's
/// generated tool catalog.
///
/// This is the source of truth for the <c>tool/list</c> AppServer method (spec Section 18A):
/// it describes the tool identifiers that may appear in Agent Profile <c>tools.allow</c> /
/// <c>tools.deny</c> lists. It carries no workspace, thread, or MCP/plugin configuration and is
/// deterministic for a given server build.
/// </summary>
public static class BuiltInToolCatalog
{
    private const string DefaultIcon = "🔧";

    private static readonly Lazy<IReadOnlyList<BuiltInToolDescriptor>> Cached = new(Scan);

    /// <summary>
    /// Returns all built-in tool descriptors, deduplicated by name and sorted ordinally by name.
    /// The tool name is the canonical model-visible tool name; the description and icon come from the
    /// tool's declaration.
    /// The result is computed once per process (the set is fixed for a given build).
    /// </summary>
    public static IReadOnlyList<BuiltInToolDescriptor> Enumerate() => Cached.Value;

    private static IReadOnlyList<BuiltInToolDescriptor> Scan()
    {
        var byName = new SortedDictionary<string, BuiltInToolDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in ToolRegistry.ReadGeneratedDescriptors(typeof(ToolAttribute).Assembly)
                     .Where(static descriptor => descriptor.CatalogVisible))
        {
            if (byName.ContainsKey(descriptor.Name))
                continue;

            var icon = string.IsNullOrEmpty(descriptor.Icon) ? DefaultIcon : descriptor.Icon;
            byName[descriptor.Name] = new BuiltInToolDescriptor(
                descriptor.Name,
                descriptor.Description,
                icon,
                descriptor.RpcEligible);
        }

        return byName.Values.ToList();
    }
}
