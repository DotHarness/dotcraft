using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Tests.Configuration;

public class LspServerConfigConverterTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void AppConfig_Deserializes_LspServers_ObjectMap()
    {
        const string json = """
        {
          "LspServers": {
            "csharp": {
              "enabled": true,
              "command": "dotnet",
              "arguments": ["csharp-ls"],
              "extensionToLanguage": {
                ".cs": "csharp"
              }
            }
          }
        }
        """;

        var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
        Assert.NotNull(config);
        var server = Assert.Single(config!.LspServers);
        Assert.Equal("csharp", server.Name);
        Assert.True(server.Enabled);
        Assert.Equal("dotnet", server.Command);
        Assert.Equal(["csharp-ls"], server.Arguments);
        Assert.Equal("csharp", server.ExtensionToLanguage[".cs"]);
    }

    [Fact]
    public void AppConfig_Deserializes_LspServers_ArrayForm()
    {
        const string json = """
        {
          "LspServers": [
            {
              "name": "typescript",
              "command": "typescript-language-server",
              "arguments": ["--stdio"],
              "extensionToLanguage": {
                ".ts": "typescript"
              }
            }
          ]
        }
        """;

        var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
        Assert.NotNull(config);
        var server = Assert.Single(config!.LspServers);
        Assert.Equal("typescript", server.Name);
        Assert.Equal("typescript-language-server", server.Command);
        Assert.Equal(["--stdio"], server.Arguments);
    }

    [Fact]
    public void AppConfig_Serializes_LspServers_AsObjectMap()
    {
        var config = new AppConfig
        {
            LspServers =
            [
                new()
                {
                    Name = "python",
                    Command = "pylsp",
                    Arguments = [],
                    ExtensionToLanguage = new Dictionary<string, string>
                    {
                        [".py"] = "python"
                    }
                }
            ]
        };

        var node = JsonSerializer.SerializeToNode(config, SerializerOptions);
        Assert.NotNull(node);
        var root = Assert.IsType<JsonObject>(node);
        var servers = Assert.IsType<JsonObject>(root["LspServers"]);
        var python = Assert.IsType<JsonObject>(servers["python"]);

        Assert.Equal("pylsp", python["Command"]?.GetValue<string>());
        Assert.False(python.ContainsKey("Name"));
    }
}
