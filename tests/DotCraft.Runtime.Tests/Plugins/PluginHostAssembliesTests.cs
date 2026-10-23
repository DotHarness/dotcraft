using System.Reflection;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginHostAssembliesTests
{
    [Fact]
    public void SharedSet_CoversEveryNonFrameworkReferenceOfTheKernelAssemblies()
    {
        var kernelAssemblies = new[]
        {
            typeof(IDotCraftPlugin).Assembly,
            typeof(ToolName).Assembly
        };

        var unshared = kernelAssemblies
            .SelectMany(static assembly => assembly.GetReferencedAssemblies()
                .Select(reference => (Owner: assembly.GetName().Name, Reference: reference.Name)))
            .Where(static entry => !PluginHostAssemblies.IsFrameworkAssembly(entry.Reference))
            .Where(static entry => !PluginHostAssemblies.IsShared(entry.Reference))
            .Select(static entry => $"{entry.Owner} -> {entry.Reference}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unshared.Length == 0,
            "Every non-framework assembly the kernel references must be shared with plugin load "
            + "contexts, or plugin type identity splits silently. Add these to "
            + $"{nameof(PluginHostAssemblies)}.{nameof(PluginHostAssemblies.SharedPackageAssemblies)}: "
            + string.Join(", ", unshared));
    }

    [Fact]
    public void SharedSet_HoldsKernelAndDeclaredPackages_ButNotFrameworkOrPluginPrivateAssemblies()
    {
        Assert.True(PluginHostAssemblies.IsShared("DotCraft.Core"));
        Assert.True(PluginHostAssemblies.IsShared("DotCraft.Agents"));
        Assert.True(PluginHostAssemblies.IsShared("DotCraft.Runtime"));
        Assert.True(PluginHostAssemblies.IsShared("Microsoft.Extensions.AI.Abstractions"));
        Assert.True(PluginHostAssemblies.IsShared("microsoft.extensions.ai"));
        Assert.False(PluginHostAssemblies.IsShared("Microsoft.Extensions.AI.Extra"));
        Assert.False(PluginHostAssemblies.IsShared(null));
        Assert.False(PluginHostAssemblies.IsShared(string.Empty));

        Assert.False(PluginHostAssemblies.IsShared("System.Text.Json"));
        Assert.True(PluginHostAssemblies.IsFrameworkAssembly("System.Text.Json"));
        Assert.False(PluginHostAssemblies.IsShared("Contoso.Internal.Library"));
        Assert.False(PluginHostAssemblies.IsFrameworkAssembly("Contoso.Internal.Library"));
        // The trusted platform list carries the application too, so the framework predicate must not.
        Assert.False(PluginHostAssemblies.IsFrameworkAssembly("DotCraft.Core"));
        Assert.False(PluginHostAssemblies.IsFrameworkAssembly("xunit.core"));
    }

    [Fact]
    public void SharedAssemblies_ResolveToTheHostInstance()
    {
        Assert.Same(typeof(IDotCraftPlugin).Assembly, PluginHostAssemblies.TryResolveShared("DotCraft.Core"));
        Assert.Null(PluginHostAssemblies.TryResolveShared("DotCraft.NotAProductAssembly"));
    }

    [Fact]
    public void EveryDeclaredSharedPackage_IsLoadableFromTheHost()
    {
        // Names the Host does not ship are allowed, so only what resolves is checked.
        var loadable = PluginHostAssemblies.SharedPackageAssemblies
            .Select(static name => (Name: name, Assembly: TryLoad(name)))
            .Where(static entry => entry.Assembly != null)
            .ToArray();

        Assert.NotEmpty(loadable);
        foreach (var (name, assembly) in loadable)
            Assert.Equal(name, assembly!.GetName().Name, StringComparer.OrdinalIgnoreCase);
    }

    private static Assembly? TryLoad(string simpleName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(simpleName));
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                              or FileLoadException
                                              or BadImageFormatException)
        {
            return null;
        }
    }
}
