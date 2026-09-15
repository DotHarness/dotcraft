using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginInstalledBundleTests
{
    [Fact]
    public async Task RecreatedWorkspacePreservesUncollectedInstallation_ThenReclaimsIt()
    {
        using var harness = new PluginRuntimeHarness();
        harness.WriteNoop("probe");
        var installed = Path.Combine(harness.Root, "installed");
        var root = Path.Combine(installed, "probe", PluginBundleFingerprint.Compute(harness.PluginRoot("probe")));
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        Directory.Move(harness.PluginRoot("probe"), root);
        PreserveAcrossWorkspaceDisposal(root, installed, harness.Root);

        using var next = new PluginBundleSnapshotStore(Path.Combine(harness.Root, "next"), installed);
        for (var attempt = 0; attempt < 100 && Directory.Exists(root); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            next.PruneInstalled(["probe"]);
            await Task.Delay(20);
        }
        Assert.False(Directory.Exists(root));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PreserveAcrossWorkspaceDisposal(string root, string installed, string temporaryRoot)
    {
        var manifest = PluginManifestParser.Load(root).Manifest!;
        using var previous = new PluginBundleSnapshotStore(Path.Combine(temporaryRoot, "previous"), installed);
        var snapshot = previous.AcceptInstalled(root);
        Assert.Equal(root, previous.CreateGenerationCopy(snapshot, "first"));
        var context = new AssemblyLoadContext("installed-probe", isCollectible: true);
        context.LoadFromAssemblyPath(Path.Combine(root, manifest.Dotnet!.EntryAssembly));
        context.Unload();
        previous.RetainUnloadedGeneration(new(new WeakReference(context), "probe", "first", root, []));
        previous.Dispose();
        using var next = new PluginBundleSnapshotStore(Path.Combine(temporaryRoot, "next"), installed);
        next.PruneInstalled(["probe"]);
        Assert.True(File.Exists(Path.Combine(root, manifest.Dotnet.EntryAssembly)));
        GC.KeepAlive(context);
    }
}
