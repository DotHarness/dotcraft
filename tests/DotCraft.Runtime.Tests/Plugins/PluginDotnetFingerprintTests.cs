using System.Text.Json.Nodes;
using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginDotnetFingerprintTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"dotcraft-dotnet-fingerprint-{Guid.NewGuid():N}");

    [Fact]
    public void Compute_ExcludesDesktopContentAndDeclaration()
    {
        WritePluginBundle(
            _root,
            "fingerprint.desktop",
            "Fingerprint.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace Fingerprint;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """);
        var executionFingerprint = PluginDotnetFingerprint.Compute(_root);
        var contentFingerprint = PluginBundleFingerprint.Compute(_root);

        AddDesktopModule(_root, "export default { activate() {} };", ["./desktop/dist/index.css"]);

        Assert.Equal(executionFingerprint, PluginDotnetFingerprint.Compute(_root));
        Assert.NotEqual(contentFingerprint, PluginBundleFingerprint.Compute(_root));

        File.WriteAllText(Path.Combine(_root, "desktop", "dist", "index.mjs"), "export default { activate() { return 1; } };");
        Assert.Equal(executionFingerprint, PluginDotnetFingerprint.Compute(_root));
    }

    [Fact]
    public void Compute_IncludesDotnetManifestContractAndNonDesktopContent()
    {
        WritePluginBundle(
            _root,
            "fingerprint.dotnet",
            "Fingerprint.Plugin",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            namespace Fingerprint;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """);
        var original = PluginDotnetFingerprint.Compute(_root);
        var manifestPath = Path.Combine(_root, ".craft-plugin", "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["version"] = "1.0.1";
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        var versionChanged = PluginDotnetFingerprint.Compute(_root);
        Assert.NotEqual(original, versionChanged);

        File.WriteAllText(Path.Combine(_root, "runtime-data.txt"), "runtime-visible");
        Assert.NotEqual(versionChanged, PluginDotnetFingerprint.Compute(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    internal static void AddDesktopModule(
        string pluginRoot,
        string source,
        IReadOnlyList<string> styles)
    {
        var outputRoot = Path.Combine(pluginRoot, "desktop", "dist");
        Directory.CreateDirectory(outputRoot);
        File.WriteAllText(Path.Combine(outputRoot, "index.mjs"), source);
        foreach (var style in styles)
            File.WriteAllText(Path.Combine(pluginRoot, style[2..].Replace('/', Path.DirectorySeparatorChar)), ".plugin {}");

        var manifestPath = Path.Combine(pluginRoot, ".craft-plugin", "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["desktop"] = new JsonObject
        {
            ["entry"] = "./desktop/dist/index.mjs",
            ["styles"] = new JsonArray(styles
                .Select(static style => (JsonNode?)JsonValue.Create(style))
                .ToArray())
        };
        File.WriteAllText(manifestPath, manifest.ToJsonString());
    }
}
