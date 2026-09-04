using System.Text.Json;
using DotCraft.CLI;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class ConfigCliRunnerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-configcli-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Schema_Json_UsesWireFieldNamesAndReloadStrings()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConfigCliRunner.SchemaAsync(
            new ConfigSchemaCommandOptions("Tools.Web", Json: true), output, error);

        using var document = JsonDocument.Parse(output.ToString());
        var section = Assert.Single(document.RootElement.EnumerateArray().ToArray());
        var field = section.GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("key").GetString() == "SearchProvider");

        Assert.Equal(0, exitCode);
        Assert.Equal("select", field.GetProperty("type").GetString());
        Assert.Equal("processRestart", field.GetProperty("reload").GetString());
        Assert.Equal(
            new[] { "Tools", "Web" },
            section.GetProperty("path").EnumerateArray().Select(v => v.GetString()).ToArray());
    }

    [Theory]
    [InlineData("tools > web")]
    [InlineData("Tools.Web")]
    public async Task Schema_Section_AcceptsDisplayNameOrPath(string filter)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConfigCliRunner.SchemaAsync(
            new ConfigSchemaCommandOptions(filter, Json: true), output, error);

        using var document = JsonDocument.Parse(output.ToString());
        var section = Assert.Single(document.RootElement.EnumerateArray().ToArray());

        Assert.Equal(0, exitCode);
        Assert.Equal("Tools > Web", section.GetProperty("section").GetString());
    }

    [Fact]
    public async Task Show_MasksProviderCredentialsInsideMaps()
    {
        var workspace = CreateWorkspace("""
            { "Providers": { "anthropic": { "ApiKey": "sk-live-secret", "EndPoint": "https://example.test" } } }
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConfigCliRunner.ShowAsync(
            new ConfigShowCommandOptions(workspace, Json: true, GlobalConfigPath: EmptyGlobalConfig()),
            output,
            error);

        using var document = JsonDocument.Parse(output.ToString());
        var provider = document.RootElement.GetProperty("Providers").GetProperty("anthropic");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("sk-live-secret", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("***", provider.GetProperty("ApiKey").GetString());
        Assert.Equal("https://example.test", provider.GetProperty("EndPoint").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string CreateWorkspace(string configJson)
    {
        var workspace = Path.Combine(_tempRoot, "ws-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(workspace, ".craft"));
        File.WriteAllText(Path.Combine(workspace, ".craft", "config.json"), configJson);
        return workspace;
    }

    private string EmptyGlobalConfig()
    {
        Directory.CreateDirectory(_tempRoot);
        var path = Path.Combine(_tempRoot, "empty-global-config.json");
        if (!File.Exists(path))
            File.WriteAllText(path, "{}");
        return path;
    }
}
