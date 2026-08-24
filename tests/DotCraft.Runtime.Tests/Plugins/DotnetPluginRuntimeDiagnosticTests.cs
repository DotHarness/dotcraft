using System.Text.Json;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class DotnetPluginRuntimeDiagnosticTests
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
                "fingerprint",
                []),
            enabled: true);

        for (var index = 0; index < DotnetPluginRuntimeManager.MaxDiagnosticsPerPlugin + 8; index++)
        {
            var generationId = $"generation-{index}";
            DotnetPluginRuntimeManager.AppendDiagnostics(
                node,
                [Diagnostic(generationId, "first"), Diagnostic(generationId, "newest")]);
        }

        Assert.Equal(DotnetPluginRuntimeManager.MaxDiagnosticsPerPlugin, node.Diagnostics.Count);
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
