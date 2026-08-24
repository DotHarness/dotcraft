using System.Security.Cryptography;
using System.Text;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotnetPluginTestBundle;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginBundleFingerprintTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"dotcraft-plugin-tree-{Guid.NewGuid():N}");

    [Fact]
    public void Compute_IsStableAndExcludesTheDeploymentMarker()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllText(Path.Combine(_root, "plugin.dll"), "one");
        File.WriteAllText(Path.Combine(_root, "nested", "data.txt"), "two");
        File.WriteAllText(Path.Combine(_root, ".builtin"), "first deployment");
        var first = PluginBundleFingerprint.Compute(_root);

        File.WriteAllText(Path.Combine(_root, ".builtin"), "second deployment");
        Assert.Equal(first, PluginBundleFingerprint.Compute(_root));

        File.WriteAllText(Path.Combine(_root, "nested", "data.txt"), "changed");
        Assert.NotEqual(first, PluginBundleFingerprint.Compute(_root));
    }

    [Fact]
    public void CopyAndFingerprint_OmitsTheDeploymentMarkerFromRuntimeSnapshots()
    {
        var bundle = Path.Combine(_root, "marker-source");
        var copy = Path.Combine(_root, "marker-copy");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "plugin.dll"), "plugin");
        File.WriteAllText(Path.Combine(bundle, ".builtin"), "deployment metadata");

        var sourceFingerprint = PluginBundleTree.CopyAndFingerprint(bundle, copy);

        Assert.False(File.Exists(Path.Combine(copy, ".builtin")));
        Assert.Equal(sourceFingerprint, PluginBundleFingerprint.Compute(copy));
    }

    [Fact]
    public void Compute_UsesUnambiguousPathAndContentBoundaries()
    {
        var firstRoot = Path.Combine(_root, "first");
        var secondRoot = Path.Combine(_root, "second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        File.WriteAllText(Path.Combine(firstRoot, "a"), "b");
        File.WriteAllText(Path.Combine(firstRoot, "cd"), string.Empty);
        File.WriteAllText(Path.Combine(secondRoot, "a"), "bc");
        File.WriteAllText(Path.Combine(secondRoot, "d"), string.Empty);

        Assert.Equal(ComputeLegacyFingerprint(firstRoot), ComputeLegacyFingerprint(secondRoot));
        Assert.NotEqual(
            PluginBundleFingerprint.Compute(firstRoot),
            PluginBundleFingerprint.Compute(secondRoot));
    }

    [Fact]
    public void FingerprintAndCopy_RejectDirectoryLinksBeforeReadingTheirContents()
    {
        var bundle = Path.Combine(_root, "bundle");
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(bundle);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "sensitive.txt"), "must not enter the fingerprint");
        var link = Path.Combine(bundle, "linked-content");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var fingerprintError = Assert.Throws<InvalidOperationException>(
            () => PluginBundleFingerprint.Compute(bundle));
        Assert.Equal("Plugin bundles cannot contain filesystem links.", fingerprintError.Message);

        var copy = Path.Combine(_root, "copy");
        var copyError = Assert.Throws<InvalidOperationException>(
            () => PluginBundleTree.CopyAndFingerprint(bundle, copy));
        Assert.Equal("Plugin bundles cannot contain filesystem links.", copyError.Message);
        Assert.False(Directory.Exists(copy));
    }

    [Fact]
    public void SnapshotStore_DoesNotDeletePathsOutsideItsRuntimeRoot()
    {
        var runtime = Path.Combine(_root, "runtime");
        var outside = Path.Combine(_root, "outside-generation");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        using var store = new PluginBundleSnapshotStore(Path.Combine(runtime, "current"));

        Assert.False(store.DeleteGeneration(outside));
        Assert.True(File.Exists(sentinel));
    }

    [Fact]
    public void SnapshotStore_RejectsAManifestWhoseIdentityChangedAfterDiscovery()
    {
        var pluginRoot = Path.Combine(_root, "identity-change");
        WritePluginBundle(
            pluginRoot,
            "copied.identity",
            "Identity.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace Identity;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """);
        var parsed = PluginManifestParser.Load(pluginRoot);
        var copiedManifest = Assert.IsType<PluginManifest>(parsed.Manifest);
        var discovered = new DiscoveredPlugin(
            copiedManifest with { Id = "discovered.identity" },
            PluginDiscoverySourceKind.Workspace,
            pluginRoot,
            Enabled: true);
        using var store = new PluginBundleSnapshotStore(
            Path.Combine(_root, "identity-runtime-roots", "current"));

        var error = Assert.Throws<InvalidOperationException>(() => store.Accept(discovered));

        Assert.Equal(
            "Copied plugin 'discovered.identity' no longer has the discovered plugin identity.",
            error.Message);
    }

    [Fact]
    public void SnapshotStore_RemovesGenerationDirectoryWhenCopyFails()
    {
        var pluginRoot = Path.Combine(_root, "generation-copy-failure");
        WritePluginBundle(
            pluginRoot,
            "generation.copy.failure",
            "GenerationCopyFailure.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace GenerationCopyFailure;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """);
        var parsed = PluginManifestParser.Load(pluginRoot);
        var manifest = Assert.IsType<PluginManifest>(parsed.Manifest);
        var discovered = new DiscoveredPlugin(
            manifest,
            PluginDiscoverySourceKind.Workspace,
            pluginRoot,
            Enabled: true);
        var runtimeRoot = Path.Combine(_root, "generation-copy-failure-runtime", "current");
        using var store = new PluginBundleSnapshotStore(runtimeRoot);
        var snapshot = store.Accept(discovered);
        var generationRoot = Path.Combine(
            runtimeRoot,
            "generations",
            manifest.Id,
            "generation-conflict");
        var conflictingManifest = Path.Combine(generationRoot, ".craft-plugin", "plugin.json");
        Directory.CreateDirectory(Path.GetDirectoryName(conflictingManifest)!);
        File.WriteAllText(conflictingManifest, "conflict");

        Assert.Throws<IOException>(() => store.CreateGenerationCopy(snapshot, "generation-conflict"));

        Assert.False(Directory.Exists(generationRoot));
    }

    [Fact]
    public void SnapshotStore_CleansOnlyRuntimeRootsWithoutAnActiveOwner()
    {
        var parent = Path.Combine(_root, "runtime-roots");
        var stale = Path.Combine(parent, "stale");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "copied-bundle.txt"), "stale");

        var activePath = Path.Combine(parent, "active");
        using var active = new PluginBundleSnapshotStore(activePath);
        File.WriteAllText(Path.Combine(activePath, "active-bundle.txt"), "active");

        using var current = new PluginBundleSnapshotStore(Path.Combine(parent, "current"));

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(activePath));
        Assert.True(File.Exists(Path.Combine(activePath, "active-bundle.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string ComputeLegacyFingerprint(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(path));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
