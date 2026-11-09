using System.Text.Json;
using DotCraft.Mcp;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpSettingsExtensionTests
{
    private static readonly JsonElement Declaration = JsonSerializer.SerializeToElement(new
    {
        readTool = "settings_read",
        updateTool = "settings_update"
    });

    [Fact]
    public void Read_AcceptsExtensionsAndExperimental()
    {
        var fromExtensions = new ServerCapabilities
        {
            Extensions = new Dictionary<string, object> { [McpSettingsExtension.CapabilityName] = Declaration }
        };
        var fromExperimental = new ServerCapabilities
        {
            Experimental = new Dictionary<string, object> { [McpSettingsExtension.CapabilityName] = Declaration }
        };

        Assert.Equal(("settings_read", "settings_update"), McpSettingsExtension.Read(fromExtensions));
        Assert.Equal(("settings_read", "settings_update"), McpSettingsExtension.Read(fromExperimental));
    }

    [Theory]
    [InlineData("""{ "readTool": "settings", "updateTool": "settings" }""")]
    [InlineData("""{ "readTool": "settings_read" }""")]
    public void Read_RejectsInvalidDeclarations(string json)
    {
        var capabilities = new ServerCapabilities
        {
            Extensions = new Dictionary<string, object>
            {
                [McpSettingsExtension.CapabilityName] = JsonSerializer.Deserialize<JsonElement>(json)
            }
        };

        Assert.Null(McpSettingsExtension.Read(capabilities));
    }

    [Theory]
    [InlineData("settings_read", McpAppVisibility.App, true)]
    [InlineData("settings_update", McpAppVisibility.None, true)]
    [InlineData("settings_update", McpAppVisibility.Model | McpAppVisibility.App, false)]
    [InlineData("query", McpAppVisibility.App, false)]
    public void OnlyDeclaredToolsHiddenFromTheModelSkipApproval(string tool, McpAppVisibility visibility, bool exempt)
    {
        var metadata = new McpAppToolMetadata(null, visibility);

        Assert.Equal(exempt, McpLiveToolCatalogReader.IsUserSettingsTool(("settings_read", "settings_update"), tool, metadata));
        Assert.False(McpLiveToolCatalogReader.IsUserSettingsTool(null, tool, metadata));
    }
}
