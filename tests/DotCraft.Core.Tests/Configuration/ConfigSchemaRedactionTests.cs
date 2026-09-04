using System.Text.Json.Nodes;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ConfigSchemaRedactionTests
{
    [Fact]
    public void MaskSensitiveKeys_ReachesSecretsInsideMapsAndLists()
    {
        var root = Parse("""
            {
              "Providers": { "anthropic": { "ApiKey": "sk-live", "EndPoint": "https://example.test" } },
              "McpServers": [ { "Name": "demo", "Token": "tok-live" } ],
              "Tools": { "Sandbox": { "ApiKey": "" } }
            }
            """);

        ConfigSchemaUtilities.MaskSensitiveKeys(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ApiKey",
            "Token"
        });

        Assert.Equal("***", root["Providers"]!["anthropic"]!["ApiKey"]!.GetValue<string>());
        Assert.Equal("***", root["McpServers"]![0]!["Token"]!.GetValue<string>());
        Assert.Equal("https://example.test", root["Providers"]!["anthropic"]!["EndPoint"]!.GetValue<string>());
        Assert.Equal(string.Empty, root["Tools"]!["Sandbox"]!["ApiKey"]!.GetValue<string>());
    }

    [Fact]
    public void MaskSensitiveKeys_MasksCredentialSuffixesAndAuthorizationWithoutSchema()
    {
        var root = Parse("""
            {
              "CLI": { "AppServerToken": "tok-live", "AppServerUrl": "ws://127.0.0.1:9100" },
              "Extra": { "ClientSecret": "s3cret", "BearerTokenEnvVar": "MY_VAR", "MaxOutputTokens": 4096 }
            }
            """);

        ConfigSchemaUtilities.MaskSensitiveKeys(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal("***", root["CLI"]!["AppServerToken"]!.GetValue<string>());
        Assert.Equal("***", root["Extra"]!["ClientSecret"]!.GetValue<string>());
        Assert.Equal("ws://127.0.0.1:9100", root["CLI"]!["AppServerUrl"]!.GetValue<string>());
        Assert.Equal("MY_VAR", root["Extra"]!["BearerTokenEnvVar"]!.GetValue<string>());
        Assert.Equal(4096, root["Extra"]!["MaxOutputTokens"]!.GetValue<int>());
    }

    [Fact]
    public void MaskSensitiveKeys_MasksEveryValueInsideHeaderAndEnvironmentMaps()
    {
        var root = Parse("""
            {
              "McpServers": [
                {
                  "Name": "demo",
                  "Headers": { "Authorization": "Bearer live", "Accept": "application/json" },
                  "EnvironmentVariables": { "OPENAI_API_KEY": "sk-live", "PATH": "/usr/bin" },
                  "EnvHttpHeaders": { "X-Client-Id": "MY_ENV" }
                }
              ]
            }
            """);

        ConfigSchemaUtilities.MaskSensitiveKeys(root, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var server = root["McpServers"]![0]!;
        Assert.Equal("***", server["Headers"]!["Authorization"]!.GetValue<string>());
        Assert.Equal("***", server["Headers"]!["Accept"]!.GetValue<string>());
        Assert.Equal("***", server["EnvironmentVariables"]!["OPENAI_API_KEY"]!.GetValue<string>());
        Assert.Equal("***", server["EnvironmentVariables"]!["PATH"]!.GetValue<string>());
        Assert.Equal("MY_ENV", server["EnvHttpHeaders"]!["X-Client-Id"]!.GetValue<string>());
        Assert.Equal("demo", server["Name"]!.GetValue<string>());
    }

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;
}
