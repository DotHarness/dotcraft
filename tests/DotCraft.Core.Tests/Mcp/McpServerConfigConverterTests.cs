using DotCraft.Mcp;
using System.Text.Json;

namespace DotCraft.Tests.Mcp;

public sealed class McpServerConfigConverterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new McpServerConfigListConverter() }
    };

    [Fact]
    public void Read_ObjectMap_AcceptsArgsAlias()
    {
        var servers = Deserialize(
            """
{
  "review": {
    "transport": "stdio",
    "command": "node",
    "args": ["./mcp-server/index.js"]
  }
}
""");

        var server = Assert.Single(servers);
        Assert.Equal("review", server.Name);
        Assert.Equal(["./mcp-server/index.js"], server.Arguments);
    }

    [Fact]
    public void Read_Array_AcceptsArgsAlias()
    {
        var servers = Deserialize(
            """
[
  {
    "name": "review",
    "transport": "stdio",
    "command": "node",
    "args": ["./mcp-server/index.js"]
  }
]
""");

        var server = Assert.Single(servers);
        Assert.Equal("review", server.Name);
        Assert.Equal(["./mcp-server/index.js"], server.Arguments);
    }

    [Fact]
    public void Read_ArgumentsFieldWinsOverArgsAlias()
    {
        var servers = Deserialize(
            """
{
  "review": {
    "transport": "stdio",
    "command": "node",
    "arguments": ["canonical.js"],
    "args": ["alias.js"]
  }
}
""");

        var server = Assert.Single(servers);
        Assert.Equal(["canonical.js"], server.Arguments);
    }

    [Fact]
    public void Read_AcceptsEnvAndHttpHeadersAliases()
    {
        var servers = Deserialize(
            """
{
  "remote": {
    "transport": "http",
    "url": "https://example.test/mcp",
    "env": {
      "REVIEW_TOKEN": "test-token"
    },
    "httpHeaders": {
      "X-Review": "enabled"
    }
  }
}
""");

        var server = Assert.Single(servers);
        Assert.Equal("test-token", server.EnvironmentVariables["REVIEW_TOKEN"]);
        Assert.Equal("enabled", server.Headers["X-Review"]);
    }

    private static List<McpServerConfig> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<McpServerConfig>>(json, JsonOptions) ?? [];
}
