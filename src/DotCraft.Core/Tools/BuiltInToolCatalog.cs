using System.ComponentModel;
using System.Reflection;

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
/// Enumerates the built-in tools the server can expose to the model by reflecting over
/// the methods decorated with <see cref="ToolAttribute"/> in the Core tools assembly.
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
    /// The tool name is the method name (the canonical model-visible tool name); the description is
    /// taken from the method's <see cref="DescriptionAttribute"/>; the icon from <see cref="ToolAttribute.Icon"/>.
    /// The result is computed once per process (the set is fixed for a given build).
    /// </summary>
    public static IReadOnlyList<BuiltInToolDescriptor> Enumerate() => Cached.Value;

    private static IReadOnlyList<BuiltInToolDescriptor> Scan()
    {
        var assembly = typeof(ToolAttribute).Assembly;
        var byName = new SortedDictionary<string, BuiltInToolDescriptor>(StringComparer.Ordinal);
        var generatedDescriptors = ToolRegistry.ReadGeneratedDescriptors(assembly);
        if (generatedDescriptors.Count > 0)
        {
            foreach (var descriptor in generatedDescriptors.Where(static descriptor => descriptor.CatalogVisible))
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

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition || !type.IsClass)
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var tool = method.GetCustomAttribute<ToolAttribute>();
                if (tool == null)
                    continue;

                // First declaration of a given tool name wins; identical tool methods may be
                // declared on more than one class (e.g. host vs sandbox variants).
                if (byName.ContainsKey(method.Name))
                    continue;

                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                var icon = string.IsNullOrEmpty(tool.Icon) ? DefaultIcon : tool.Icon;
                byName[method.Name] = new BuiltInToolDescriptor(
                    method.Name,
                    description,
                    icon,
                    method.IsDefined(typeof(ToolRpcAttribute), inherit: false));
            }
        }

        return byName.Values.ToList();
    }
}
