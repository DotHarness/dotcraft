using DotCraft.Mcp;
using System.Text.Json;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpServerConfigConverterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new McpServerConfigListConverter() }
    };

    [Fact]
    public void Read_ObjectMap_AcceptsCanonicalFields()
    {
        var servers = Deserialize(
            """
{
  "review": {
    "transport": "stdio",
    "command": "node",
    "arguments": ["./mcp-server/index.js"]
  }
}
""");

        var server = Assert.Single(servers);
        Assert.Equal("review", server.Name);
        Assert.Equal(["./mcp-server/index.js"], server.Arguments);
    }

    [Fact]
    public void Read_ObjectMap_RejectsUnknownFields()
    {
        const string json =
            """
{
  "review": {
    "transport": "stdio",
    "command": "node",
    "unsupportedOption": true
  }
}
""";

        Assert.Throws<JsonException>(() => Deserialize(json));
    }

    private static List<McpServerConfig> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<McpServerConfig>>(json, JsonOptions) ?? [];
}
