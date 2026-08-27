using System.Text.Json;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class DotNetPluginRuntimeDiagnosticTests
{
    [Fact]
    public void AppendDiagnostics_DeduplicatesEachGenerationAndKeepsTheNewestBoundedHistory()
    {
        var node = new PluginRuntimeNode(
            new PluginAcceptedSnapshot(
                new PluginManifest
                {
                    SchemaVersion = PluginManifestParser.SupportedSchemaVersion,
                    Id = "diagnostic-test",
                    Version = "1.0.0",
                    DisplayName = "Diagnostic test",
                    RootPath = "bundle",
                    ManifestPath = "bundle/plugin.json"
                },
                "content",
                "content-fingerprint",
                "fingerprint",
                []),
            enabled: true,
            workspaceRoot: Path.GetTempPath());

        for (var index = 0; index < DotNetPluginRuntimeManager.MaxDiagnosticsPerPlugin + 8; index++)
        {
            var generationId = $"generation-{index}";
            DotNetPluginRuntimeManager.AppendDiagnostics(
                node,
                [Diagnostic(generationId, "first"), Diagnostic(generationId, "newest")]);
        }

        Assert.Equal(DotNetPluginRuntimeManager.MaxDiagnosticsPerPlugin, node.Diagnostics.Count);
        Assert.Equal("generation-8", Parameter(node.Diagnostics[0], "generationId"));
        Assert.Equal("generation-39", Parameter(node.Diagnostics[^1], "generationId"));
        Assert.All(node.Diagnostics, diagnostic => Assert.Equal("newest", diagnostic.Message));
    }

    private static PluginDiagnostic Diagnostic(string generationId, string message) =>
        PluginDiagnostic.Error(
            "PluginActivationFailed",
            message,
            "diagnostic-test",
            parameters: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["phase"] = JsonSerializer.SerializeToElement("activation"),
                ["generationId"] = JsonSerializer.SerializeToElement(generationId)
            });

    private static string? Parameter(PluginDiagnostic diagnostic, string name) =>
        diagnostic.Parameters[name].GetString();
}
