using System.Reflection;
using System.Runtime.Loader;

namespace DotCraft.Tools;

public sealed record BuiltInToolDescriptor(
    string Name,
    string Description,
    bool RpcEligible = false);

/// <summary>
/// Lists the tools Agent Profile <c>tools.allow</c> / <c>tools.deny</c> can name, from the catalogs
/// of loaded server assemblies.
/// </summary>
public static class BuiltInToolCatalog
{
    private static readonly Assembly CoreAssembly = typeof(ToolAttribute).Assembly;
    private static readonly Lock Gate = new();
    private static (int AssemblyCount, IReadOnlyList<BuiltInToolDescriptor> Tools) _cached = (-1, []);

    public static IReadOnlyList<BuiltInToolDescriptor> Enumerate()
    {
        var assemblies = AssemblyLoadContext.Default.Assemblies.ToArray();
        lock (Gate)
        {
            if (_cached.AssemblyCount != assemblies.Length)
                _cached = (assemblies.Length, Scan(assemblies));
            return _cached.Tools;
        }
    }

    private static IReadOnlyList<BuiltInToolDescriptor> Scan(IEnumerable<Assembly> assemblies)
    {
        var byName = new SortedDictionary<string, BuiltInToolDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in assemblies
                     .Where(DeclaresToolCatalogs)
                     .SelectMany(ReadGeneratedDescriptors)
                     .Where(static descriptor => descriptor.CatalogVisible))
        {
            byName.TryAdd(
                descriptor.Name,
                new BuiltInToolDescriptor(descriptor.Name, descriptor.Description, descriptor.RpcEligible));
        }

        return byName.Values.ToList();
    }

    private static bool DeclaresToolCatalogs(Assembly assembly) =>
        assembly == CoreAssembly
        || (!assembly.IsDynamic
            && assembly.GetReferencedAssemblies().Any(static reference => reference.Name == CoreAssembly.GetName().Name));

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
