using System.Collections.Frozen;
using DotCraft.Runtime;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins.Authoring;

public sealed class DotNetPluginReferenceSetTests(
    AuthoringReferencePackFixture fixture) : IClassFixture<AuthoringReferencePackFixture>
{
    [Fact]
    public void LoadCurrentHost_UsesThePreservedCompilationContext()
    {
        var names = DotNetPluginReferenceSet.LoadCurrentHost(FrozenSet<string>.Empty).References
            .Cast<PortableExecutableReference>()
            .Select(static reference => Path.GetFileNameWithoutExtension(reference.FilePath))
            .ToArray();

        Assert.Contains("DotCraft.Core", names);
        Assert.Contains("DotCraft.Agents", names);
        Assert.Contains("System.Runtime", names);
    }

    [Fact]
    public void Load_SelectsOnlySupportedReferenceKindsInStableOrder()
    {
        var references = fixture.Load();
        var names = references.References
            .Cast<PortableExecutableReference>()
            .Select(static reference => Path.GetFileNameWithoutExtension(reference.FilePath))
            .ToArray();

        Assert.Contains("DotCraft.Core", names);
        Assert.Contains("DotCraft.Agents", names);
        Assert.Contains("System.Runtime", names);
        Assert.DoesNotContain("DotCraft.Runtime", names);
        Assert.DoesNotContain("DotCraft.Generators", names);
        Assert.DoesNotContain("Microsoft.CodeAnalysis", names);
        Assert.DoesNotContain("Microsoft.CodeAnalysis.CSharp", names);
        Assert.Equal(names.OrderBy(static name => name, StringComparer.Ordinal), names);

        var shippedSharedPackage = PluginHostAssemblies.SharedPackageAssemblies
            .FirstOrDefault(name => File.Exists(Path.Combine(fixture.Root, name + ".dll")));
        Assert.NotNull(shippedSharedPackage);
        Assert.Contains(shippedSharedPackage, names);
    }

    [Fact]
    public void Load_ReferencesHostDeclaredSharedAssemblies()
    {
        var hostContract = typeof(Assert).Assembly;
        var declared = new[] { hostContract.GetName().Name! }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        var references = DotNetPluginReferenceSet.Load(fixture.HostAssemblyPath, declared);

        Assert.Contains(
            references.References.Cast<PortableExecutableReference>(),
            reference => reference.FilePath == hostContract.Location);
        Assert.True(references.IsPluginApiAssembly(hostContract.GetName().Name!));
        Assert.False(fixture.Load().IsPluginApiAssembly(hostContract.GetName().Name!));
    }

    [Fact]
    public void Load_RequiresSiblingReferenceDirectory()
    {
        var root = Path.Combine(fixture.Root, "missing-pack");
        Directory.CreateDirectory(root);
        var hostPath = Path.Combine(root, "DotCraft.Core.dll");

        var error = Assert.Throws<DirectoryNotFoundException>(
            () => DotNetPluginReferenceSet.Load(hostPath, FrozenSet<string>.Empty));

        Assert.DoesNotContain(root, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("DotCraft.Core.xml")]
    public void Load_RequiresPluginApiDocumentation(string missingFile)
    {
        var root = Path.Combine(fixture.Root, Path.GetFileNameWithoutExtension(missingFile));
        Directory.CreateDirectory(Path.Combine(root, "refs"));

        foreach (var name in new[]
                 {
                     "DotCraft.Core.dll",
                     "DotCraft.Core.xml",
                     "DotCraft.Agents.dll",
                     "DotCraft.Agents.xml"
                 })
        {
            File.Copy(
                Path.Combine(fixture.Root, name),
                Path.Combine(root, name));
        }

        File.Delete(Path.Combine(root, missingFile));

        var error = Assert.Throws<FileNotFoundException>(
            () => DotNetPluginReferenceSet.Load(Path.Combine(root, "DotCraft.Core.dll"), FrozenSet<string>.Empty));

        Assert.Contains(missingFile, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(root, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
