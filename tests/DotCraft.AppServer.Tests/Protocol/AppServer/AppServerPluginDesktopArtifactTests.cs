using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerPluginManagementTests
{
    [Fact]
    public async Task DesktopArtifact_DownloadsExactDesktopTreeInBoundedChunks()
    {
        var manifest = WriteDesktopArtifactFixture();
        using var harness = CreateHarness(includeBundledRoots: false);
        using var init = await harness.InitializeAsync();
        Assert.True(init.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("desktopPluginArtifacts").GetBoolean());
        await harness.ExecuteRequestAsync(harness.BuildRequest("plugin/list", new { }));
        using var list = await harness.Transport.ReadNextSentAsync();
        Assert.False(string.IsNullOrEmpty(list.RootElement.GetProperty("result").GetProperty("workspacePath").GetString()));

        using var bytes = new MemoryStream();
        long total;
        var chunks = 0;
        do
        {
            using var response = await ReadDesktopArtifactAsync(harness, manifest, bytes.Length);
            AppServerTestHarness.AssertIsSuccessResponse(response);
            var result = response.RootElement.GetProperty("result");
            total = result.GetProperty("totalBytes").GetInt64();
            var chunk = Convert.FromBase64String(result.GetProperty("dataBase64").GetString()!);
            Assert.InRange(chunk.Length, 1, 1024 * 1024);
            bytes.Write(chunk);
            chunks++;
        } while (bytes.Length < total);
        Assert.True(chunks >= 3);
        bytes.Position = 0;
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, entry => entry.FullName == "desktop/dist/empty/");
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("secret") || entry.FullName.StartsWith("lib/"));
        var extracted = Path.Combine(_tempRoot, "extracted");
        zip.ExtractToDirectory(extracted);
        var parsed = PluginManifestParser.Load(extracted);
        Assert.Equal(manifest.Desktop!.Revision, parsed.Manifest?.Desktop?.Revision);
        using var exhausted = await ReadDesktopArtifactAsync(harness, manifest, total);
        Assert.Equal("DesktopArtifactOffsetInvalid", exhausted.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task DesktopArtifact_RejectsChangedOrRemovedPluginAndSupportsCancellation()
    {
        var manifest = WriteDesktopArtifactFixture();
        using var harness = CreateHarness(includeBundledRoots: false);
        await harness.InitializeAsync();
        using var first = await ReadDesktopArtifactAsync(harness, manifest, 0);
        AppServerTestHarness.AssertIsSuccessResponse(first);
        using var cancel = await ReadDesktopArtifactAsync(harness, manifest, -1);
        AppServerTestHarness.AssertIsSuccessResponse(cancel);
        using var afterCancel = await ReadDesktopArtifactAsync(harness, manifest, 1024 * 1024);
        Assert.True(afterCancel.RootElement.TryGetProperty("error", out _));

        using var restarted = await ReadDesktopArtifactAsync(harness, manifest, 0);
        using var middle = await ReadDesktopArtifactAsync(harness, manifest, 1024 * 1024);
        File.AppendAllText(Path.Combine(manifest.RootPath, "desktop", "dist", "index.mjs"), "\nexport const changed = true;");
        using var changed = await ReadDesktopArtifactAsync(harness, manifest, 2 * 1024 * 1024);
        Assert.Equal("DesktopArtifactChanged", changed.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
        Directory.Delete(manifest.RootPath, recursive: true);
        using var removed = await ReadDesktopArtifactAsync(harness, manifest, 0);
        Assert.Equal("DesktopArtifactUnavailable", removed.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task DesktopArtifact_RejectsDisabledPlugin()
    {
        var manifest = WriteDesktopArtifactFixture();
        var config = new DotCraft.Configuration.AppConfig();
        config.Plugins.DisabledPlugins.Add(manifest.Id);
        using var harness = CreateHarness(config, includeBundledRoots: false);
        await harness.InitializeAsync();
        using var response = await ReadDesktopArtifactAsync(harness, manifest, 0);
        Assert.Equal("DesktopArtifactUnavailable", response.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    private PluginManifest WriteDesktopArtifactFixture()
    {
        var root = Path.Combine(_workspaceCraftPath, "plugins", "artifact-test");
        Directory.CreateDirectory(Path.Combine(root, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(root, "desktop", "dist", "empty"));
        File.WriteAllText(Path.Combine(root, "desktop", "dist", "index.mjs"), "export function activate() { return {}; }");
        File.WriteAllBytes(Path.Combine(root, "desktop", "dist", "asset.bin"), RandomNumberGenerator.GetBytes(2_500_000));
        File.WriteAllText(Path.Combine(root, "secret.txt"), "do not export");
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        File.WriteAllText(Path.Combine(root, "lib", "server.dll"), "do not export");
        File.WriteAllText(Path.Combine(root, ".craft-plugin", "plugin.json"), """
            { "schemaVersion": 1, "id": "artifact-test", "displayName": "Artifact test", "version": "1.0.0", "desktop": { "entry": "./desktop/dist/index.mjs", "styles": [] } }
            """);
        return Assert.IsType<PluginManifest>(PluginManifestParser.Load(root).Manifest);
    }

    private static async Task<JsonDocument> ReadDesktopArtifactAsync(AppServerTestHarness harness, PluginManifest manifest, long offset)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest("plugin/desktop/read", new { id = manifest.Id, revision = manifest.Desktop!.Revision, offset }));
        return await harness.Transport.ReadNextSentAsync();
    }
}
