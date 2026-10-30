using System.Collections.Frozen;
using System.Reflection;
using DotCraft.Runtime;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginLoadContextTests : IDisposable
{
    private static readonly Assembly HostContract = typeof(Assert).Assembly;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft_load_context_{Guid.NewGuid():N}");

    [Fact]
    public void HostDeclaredSharedAssembly_ResolvesToTheHostInstance_InsteadOfTheBundleCopy()
    {
        var name = HostContract.GetName().Name!;

        var declared = Load(name, [name]);
        var undeclared = Load(name, []);

        Assert.Same(HostContract, declared);
        Assert.NotSame(HostContract, undeclared);
    }

    private Assembly Load(string name, string[] hostShared)
    {
        var bundle = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundle);
        var entryPath = Path.Combine(bundle, "Entry.dll");
        File.Copy(HostContract.Location, entryPath);
        File.Copy(HostContract.Location, Path.Combine(bundle, name + ".dll"));
        File.WriteAllText(
            Path.ChangeExtension(entryPath, ".deps.json"),
            """{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0"},"targets":{".NETCoreApp,Version=v10.0":{}},"libraries":{}}""");
        var context = new PluginLoadContext(
            "test-plugin",
            bundle,
            entryPath,
            new Dictionary<string, Assembly>(),
            hostShared.ToFrozenSet(StringComparer.OrdinalIgnoreCase));
        return context.LoadFromAssemblyName(new AssemblyName(name));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // The bundle copy stays mapped until its collectible context is collected.
        }
    }
}
