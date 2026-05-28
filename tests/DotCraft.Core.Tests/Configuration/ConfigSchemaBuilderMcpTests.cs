using DotCraft.Configuration;
using DotCraft.Mcp;

namespace DotCraft.Tests.Configuration;

public class ConfigSchemaBuilderMcpTests
{
    [Fact]
    public void McpSection_HasRootKey_ItemFields_AndExpectedTypes()
    {
        var schema = ConfigSchemaBuilder.BuildAll([typeof(McpServerConfig)]);
        var mcp = Assert.Single(schema);

        Assert.Equal("McpServers", mcp.RootKey);
        Assert.NotNull(mcp.ItemFields);
        Assert.NotEmpty(mcp.ItemFields);
        Assert.Empty(mcp.Fields);

        var byKey = mcp.ItemFields!.ToDictionary(f => f.Key, f => f);

        Assert.Equal("text", byKey["Name"].Type);
        Assert.Equal("bool", byKey["Enabled"].Type);

        Assert.Equal("select", byKey["Transport"].Type);
        Assert.NotNull(byKey["Transport"].Options);
        Assert.Contains("stdio", byKey["Transport"].Options!);
        Assert.Contains("http", byKey["Transport"].Options!);

        Assert.Equal("text", byKey["Command"].Type);
        Assert.Equal("stringList", byKey["Arguments"].Type);
        Assert.Equal("keyValueMap", byKey["EnvironmentVariables"].Type);
        Assert.Equal("text", byKey["Url"].Type);
        Assert.Equal("keyValueMap", byKey["Headers"].Type);
        Assert.Equal(ReloadBehavior.Hot, byKey["Name"].Reload);
    }
}
