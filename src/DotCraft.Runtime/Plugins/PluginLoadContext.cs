using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace DotCraft.Runtime;

/// <summary>
/// The collectible load context of one plugin activation generation. Resolution order is load-bearing
/// (spec §3): Host-shared by simple name, provider APIs by exact identity, <c>.deps.json</c>, adjacent probe.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _root;
    private readonly string _entryDirectory;
    private readonly AssemblyDependencyResolver _resolver;
    private readonly IReadOnlyDictionary<string, Assembly> _providerApiAssemblies;

    public PluginLoadContext(
        string pluginId,
        string root,
        string entryAssemblyPath,
        IReadOnlyDictionary<string, Assembly> providerApiAssemblies)
        : base($"dotcraft-plugin:{pluginId}:{Guid.NewGuid():N}", isCollectible: true)
    {
        _root = Path.GetFullPath(root);
        _entryDirectory = Path.GetDirectoryName(entryAssemblyPath)!;
        _providerApiAssemblies = providerApiAssemblies;
        ValidateDependencyManifest(Path.ChangeExtension(entryAssemblyPath, ".deps.json"));
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var simpleName = assemblyName.Name;
        if (simpleName == null)
            return null;

        if (PluginHostAssemblies.IsShared(simpleName))
        {
            var shared = PluginHostAssemblies.TryResolveShared(simpleName);
            if (shared != null)
                return shared;
        }

        if (_providerApiAssemblies.TryGetValue(simpleName, out var providerApi))
        {
            if (!HasExactIdentity(assemblyName, providerApi.GetName()))
            {
                throw new FileLoadException(
                    $"Provider API identity '{assemblyName.FullName}' does not match active assembly '{providerApi.FullName}'.");
            }

            return providerApi;
        }

        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolved == null)
        {
            var adjacent = Path.Combine(_entryDirectory, simpleName + ".dll");
            if (File.Exists(adjacent))
                resolved = adjacent;
        }

        return resolved == null ? null : LoadFromAssemblyPath(Confine(resolved));
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (resolved == null)
        {
            foreach (var candidate in NativeCandidates(unmanagedDllName))
            {
                var adjacent = Path.Combine(_entryDirectory, candidate);
                if (File.Exists(adjacent))
                {
                    resolved = adjacent;
                    break;
                }
            }
        }

        return resolved == null ? 0 : LoadUnmanagedDllFromPath(Confine(resolved));
    }

    private string Confine(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(_root, fullPath);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new FileLoadException("Plugin dependency resolved outside its generation shadow copy.");
        }

        return fullPath;
    }

    private static bool HasExactIdentity(AssemblyName requested, AssemblyName expected) =>
        string.Equals(requested.Name, expected.Name, StringComparison.Ordinal)
        && Equals(requested.Version, expected.Version)
        && string.Equals(requested.CultureName ?? string.Empty, expected.CultureName ?? string.Empty, StringComparison.Ordinal)
        && requested.GetPublicKeyToken().AsSpan().SequenceEqual(expected.GetPublicKeyToken());

    private static IEnumerable<string> NativeCandidates(string name)
    {
        yield return name;
        if (OperatingSystem.IsWindows())
            yield return name + ".dll";
        else if (OperatingSystem.IsMacOS())
            yield return "lib" + name + ".dylib";
        else
            yield return "lib" + name + ".so";
    }

    private static void ValidateDependencyManifest(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("runtimeTarget", out var runtimeTarget)
                || runtimeTarget.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("targets", out var targets)
                || targets.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("libraries", out var libraries)
                || libraries.ValueKind != JsonValueKind.Object)
            {
                throw new FileLoadException("Plugin dependency manifest is invalid.");
            }
        }
        catch (JsonException exception)
        {
            throw new FileLoadException("Plugin dependency manifest is invalid.", exception);
        }
    }
}
