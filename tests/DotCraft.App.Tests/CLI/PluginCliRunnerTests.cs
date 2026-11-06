using System.Text.Json;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class PluginCliRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-plugincli-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifyJson_ReportsDiagnosticsAndFailingExitCode()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(_root, ".craft-plugin", "plugin.json"),
            """{ "schemaVersion": 1, "id": "demo", "displayName": "Demo", "skills": "./skills/" }""");
        using var output = new StringWriter();

        var exitCode = await PluginCliRunner.VerifyAsync(_root, forPublish: true, json: true, output);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("isValid").GetBoolean());
        Assert.True(root.GetProperty("forPublish").GetBoolean());
        Assert.Equal("demo", root.GetProperty("pluginId").GetString());
        var first = root.GetProperty("diagnostics")[0];
        Assert.Equal("error", first.GetProperty("severity").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("code").GetString()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
