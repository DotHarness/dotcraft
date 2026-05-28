using DotCraft.Configuration;
using DotCraft.Lsp;

namespace DotCraft.Tests.Configuration;

public class ConfigSchemaBuilderLspTests
{
    [Fact]
    public void LspSections_HaveExpectedSchemaMetadata()
    {
        var schema = ConfigSchemaBuilder.BuildAll([typeof(LspServerConfig), typeof(AppConfig.LspToolsConfig)]);

        var lspServers = Assert.Single(schema, s => s.RootKey == "LspServers");
        Assert.Equal("LspServers", lspServers.RootKey);
        Assert.NotNull(lspServers.ItemFields);
        Assert.Empty(lspServers.Fields);

        var itemFields = lspServers.ItemFields!.ToDictionary(f => f.Key, f => f);
        Assert.Equal("text", itemFields["Command"].Type);
        Assert.Equal("stringList", itemFields["Arguments"].Type);
        Assert.Equal("keyValueMap", itemFields["ExtensionToLanguage"].Type);
        Assert.Equal("keyValueMap", itemFields["EnvironmentVariables"].Type);
        Assert.Equal("json", itemFields["InitializationOptions"].Type);
        Assert.Equal("json", itemFields["Settings"].Type);
        Assert.Equal("select", itemFields["Transport"].Type);
        Assert.Contains("stdio", itemFields["Transport"].Options!);

        var toolsLsp = Assert.Single(schema, s => s.Path is ["Tools", "Lsp"]);
        Assert.NotNull(toolsLsp.Fields);
        Assert.True(toolsLsp.ItemFields == null || toolsLsp.ItemFields.Count == 0);
        var fields = toolsLsp.Fields!.ToDictionary(f => f.Key, f => f);
        Assert.Equal("bool", fields["Enabled"].Type);
        Assert.Equal("number", fields["MaxFileSize"].Type);
        Assert.Equal(ReloadBehavior.ProcessRestart, fields["Enabled"].Reload);
    }
}
