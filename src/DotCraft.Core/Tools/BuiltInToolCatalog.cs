using System.Reflection;

namespace DotCraft.Tools;

/// <summary>
/// A single built-in tool's catalog metadata: its canonical model-visible name and
/// human-readable description.
/// </summary>
public sealed record BuiltInToolDescriptor(
    string Name,
    string Description,
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
    private static readonly Lazy<IReadOnlyList<BuiltInToolDescriptor>> Cached = new(Scan);

    /// <summary>
    /// Returns all built-in tool descriptors, deduplicated by name and sorted ordinally by name.
    /// The result is computed once per process (the set is fixed for a given build).
    /// </summary>
    public static IReadOnlyList<BuiltInToolDescriptor> Enumerate() => Cached.Value;

    private static IReadOnlyList<BuiltInToolDescriptor> Scan()
    {
        var byName = new SortedDictionary<string, BuiltInToolDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in ReadGeneratedDescriptors(typeof(ToolAttribute).Assembly)
                     .Where(static descriptor => descriptor.CatalogVisible))
        {
            byName.TryAdd(
                descriptor.Name,
                new BuiltInToolDescriptor(descriptor.Name, descriptor.Description, descriptor.RpcEligible));
        }

        return byName.Values.ToList();
    }

    private static IEnumerable<GeneratedToolDescriptor> ReadGeneratedDescriptors(Assembly assembly)
    {
        foreach (var attribute in assembly.GetCustomAttributes<GeneratedToolCatalogAttribute>())
        {
            var property = attribute.ProviderType.GetProperty(
                "Descriptors",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property?.GetValue(null) is IEnumerable<GeneratedToolDescriptor> generated)
            {
                foreach (var descriptor in generated)
                    yield return descriptor;
            }
        }
    }
}
