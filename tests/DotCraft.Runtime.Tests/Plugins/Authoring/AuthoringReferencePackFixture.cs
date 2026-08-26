using DotCraft.Agents;
using DotCraft.Plugins;
using DotCraft.Runtime;

namespace DotCraft.Tests.Runtime.Plugins.Authoring;

public sealed class AuthoringReferencePackFixture : IDisposable
{
    public AuthoringReferencePackFixture()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "dotcraft-tests",
            "plugin-authoring",
            Guid.NewGuid().ToString("N"));
        ReferencesRoot = Path.Combine(Root, "refs");
        Directory.CreateDirectory(ReferencesRoot);

        HostAssemblyPath = Path.Combine(Root, "DotCraft.Core.dll");
        CopyWithDocumentation(typeof(IDotCraftPlugin).Assembly.Location, Root);
        CopyWithDocumentation(typeof(ChatClientAgent).Assembly.Location, Root);
        CopyFrameworkReferences();
        CopySharedPackageReferences();

        File.Copy(
            typeof(DotNetPluginReferenceSet).Assembly.Location,
            Path.Combine(Root, "DotCraft.Runtime.dll"));
    }

    public string Root { get; }

    public string ReferencesRoot { get; }

    public string HostAssemblyPath { get; }

    internal DotNetPluginReferenceSet Load() => DotNetPluginReferenceSet.Load(HostAssemblyPath);

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    private void CopyFrameworkReferences()
    {
        var references = Path.Combine(AppContext.BaseDirectory, "refs");
        foreach (var source in Directory.EnumerateFiles(references, "*.dll"))
            File.Copy(source, Path.Combine(ReferencesRoot, Path.GetFileName(source)));
    }

    private void CopySharedPackageReferences()
    {
        foreach (var name in PluginHostAssemblies.SharedPackageAssemblies)
        {
            var source = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (File.Exists(source))
                CopyWithDocumentation(source, Root);
        }
    }

    private static void CopyWithDocumentation(string assemblyPath, string destinationRoot)
    {
        File.Copy(
            assemblyPath,
            Path.Combine(destinationRoot, Path.GetFileName(assemblyPath)));

        var documentationPath = Path.ChangeExtension(assemblyPath, ".xml");
        if (File.Exists(documentationPath))
        {
            File.Copy(
                documentationPath,
                Path.Combine(destinationRoot, Path.GetFileName(documentationPath)));
        }
    }
}
