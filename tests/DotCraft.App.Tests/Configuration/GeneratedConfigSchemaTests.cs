using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class GeneratedConfigSchemaTests
{
    [Fact]
    public void GeneratedConfigSchema_ContainsCoreReloadAndDefaultMetadata()
    {
        var schema = ConfigSchemaRegistrations.GetConfigSchema();

        var core = Assert.Single(schema, s => s.Section == "Core");
        var fields = core.Fields.ToDictionary(f => f.Key, f => f);

        Assert.Equal(ReloadBehavior.ProcessRestart, fields["ProviderId"].Reload);
        Assert.Equal("number", fields["NetworkTimeoutSeconds"].Type);
        Assert.Equal(1, fields["NetworkTimeoutSeconds"].Min);

        var skills = Assert.Single(schema, s => s.Path is ["Skills"]);
        var disabledSkills = Assert.Single(skills.Fields, f => f.Key == "DisabledSkills");
        Assert.Equal("stringList", disabledSkills.Type);
        Assert.Equal(ReloadBehavior.Hot, disabledSkills.Reload);
    }

    [Fact]
    public void GeneratedConfigSchema_ContainsMcpAndLspCollectionMetadata()
    {
        var schema = ConfigSchemaRegistrations.GetConfigSchema();

        var mcp = Assert.Single(schema, s => s.RootKey == "McpServers");
        Assert.Empty(mcp.Fields);
        Assert.NotNull(mcp.ItemFields);
        var mcpFields = mcp.ItemFields!.ToDictionary(f => f.Key, f => f);

        Assert.Equal("text", mcpFields["Name"].Type);
        Assert.Equal("bool", mcpFields["Enabled"].Type);
        Assert.Equal("select", mcpFields["Transport"].Type);
        Assert.Contains("stdio", mcpFields["Transport"].Options!);
        Assert.Contains("http", mcpFields["Transport"].Options!);
        Assert.Equal("text", mcpFields["Command"].Type);
        Assert.Equal("stringList", mcpFields["Arguments"].Type);
        Assert.Equal("keyValueMap", mcpFields["EnvironmentVariables"].Type);
        Assert.Equal("text", mcpFields["Url"].Type);
        Assert.Equal("keyValueMap", mcpFields["Headers"].Type);
        Assert.All(mcp.ItemFields!, field =>
        {
            Assert.Equal(ReloadBehavior.Hot, field.Reload);
            Assert.Null(field.SubsystemKey);
        });

        var lsp = Assert.Single(schema, s => s.RootKey == "LspServers");
        Assert.Empty(lsp.Fields);
        Assert.NotNull(lsp.ItemFields);
        var lspFields = lsp.ItemFields!.ToDictionary(f => f.Key, f => f);

        Assert.Equal("text", lspFields["Command"].Type);
        Assert.Equal("stringList", lspFields["Arguments"].Type);
        Assert.Equal("keyValueMap", lspFields["ExtensionToLanguage"].Type);
        Assert.Equal("keyValueMap", lspFields["EnvironmentVariables"].Type);
        Assert.Equal("json", lspFields["InitializationOptions"].Type);
        Assert.Equal("json", lspFields["Settings"].Type);
        Assert.Equal("select", lspFields["Transport"].Type);
        Assert.Contains("stdio", lspFields["Transport"].Options!);

        var toolsLsp = Assert.Single(schema, s => s.Path is ["Tools", "Lsp"]);
        var toolsLspFields = toolsLsp.Fields.ToDictionary(f => f.Key, f => f);
        Assert.Equal("bool", toolsLspFields["Enabled"].Type);
        Assert.Equal("number", toolsLspFields["MaxFileSize"].Type);
        Assert.Equal(ReloadBehavior.ProcessRestart, toolsLspFields["Enabled"].Reload);
    }

    [Fact]
    public void GeneratedConfigSchema_ContainsExternalChannelAndSubAgentProfileMetadata()
    {
        var schema = ConfigSchemaRegistrations.GetConfigSchema();

        var externalChannels = Assert.Single(schema, s => s.RootKey == "ExternalChannels");
        Assert.Empty(externalChannels.Fields);
        Assert.NotNull(externalChannels.ItemFields);
        var externalFields = externalChannels.ItemFields!.ToDictionary(f => f.Key, f => f);

        Assert.Equal("text", externalFields["Name"].Type);
        Assert.Equal("bool", externalFields["Enabled"].Type);
        Assert.Equal("select", externalFields["Transport"].Type);
        Assert.Contains("subprocess", externalFields["Transport"].Options!);
        Assert.Contains("websocket", externalFields["Transport"].Options!);
        Assert.Contains("managedWebsocket", externalFields["Transport"].Options!);
        Assert.Equal("text", externalFields["Command"].Type);
        Assert.Equal("stringList", externalFields["Args"].Type);
        Assert.Equal("text", externalFields["WorkingDirectory"].Type);
        Assert.Equal("keyValueMap", externalFields["Env"].Type);

        var profiles = Assert.Single(schema, s => s.RootKey == "SubAgentProfiles");
        Assert.Empty(profiles.Fields);
        Assert.NotNull(profiles.ItemFields);
        var profileFields = profiles.ItemFields!.ToDictionary(f => f.Key, f => f);

        Assert.Equal("text", profileFields["Name"].Type);
        Assert.Equal("text", profileFields["Runtime"].Type);
        Assert.Equal("native", profileFields["Runtime"].DefaultValue);
        Assert.Equal("text", profileFields["Bin"].Type);
        Assert.Equal("stringList", profileFields["Args"].Type);
        Assert.Equal("keyValueMap", profileFields["Env"].Type);
        Assert.Equal("stringList", profileFields["EnvPassthrough"].Type);
        Assert.Equal("select", profileFields["WorkingDirectoryMode"].Type);
        Assert.Contains("workspace", profileFields["WorkingDirectoryMode"].Options!);
        Assert.Contains("specified", profileFields["WorkingDirectoryMode"].Options!);
        Assert.Equal("select", profileFields["InputMode"].Type);
        Assert.Contains("stdin", profileFields["InputMode"].Options!);
        Assert.Equal("keyValueMap", profileFields["PermissionModeMapping"].Type);
        Assert.Equal("json", profileFields["SanitizationRules"].Type);
    }

    [Fact]
    public void GeneratedConfigSchema_BuildsSensitivePathsWithoutReflection()
    {
        var paths = ConfigSchemaUtilities
            .BuildSensitivePaths(ConfigSchemaRegistrations.GetConfigSchema())
            .Select(path => string.Join(".", path))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("DashBoard.Password", paths);
        Assert.Contains("Tools.Sandbox.ApiKey", paths);
        Assert.Contains("AppServer.WebSocket.Token", paths);
    }
}
