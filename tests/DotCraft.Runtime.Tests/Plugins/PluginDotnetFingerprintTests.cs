using System.Text.Json.Nodes;
using DotCraft.Runtime;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginDotnetFingerprintTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"dotcraft-dotnet-fingerprint-{Guid.NewGuid():N}");

    [Fact]
    public void Compute_ExcludesDesktopContentAndDeclaration()
    {
        WriteFingerprintBundle("fingerprint.desktop");
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
        WriteFingerprintBundle("fingerprint.dotnet");
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

    private void WriteFingerprintBundle(string id)
    {
        var manifestRoot = Path.Combine(_root, ".craft-plugin");
        var dotnetRoot = Path.Combine(_root, "dotnet");
        Directory.CreateDirectory(manifestRoot);
        Directory.CreateDirectory(dotnetRoot);
        File.WriteAllBytes(Path.Combine(dotnetRoot, "Plugin.dll"), [1, 2, 3, 4]);
        File.WriteAllText(Path.Combine(manifestRoot, "plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "version": "1.0.0",
              "displayName": "{{id}}",
              "capabilities": ["dotnet"],
              "dotnet": {
                "minHostVersion": "0.0.0",
                "entryAssembly": "./dotnet/Plugin.dll",
                "entryType": "Fingerprint.Plugin",
                "exportedApiAssemblies": []
              }
            }
            """);
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
