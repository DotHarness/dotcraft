using System.Text.Json.Nodes;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public class AppConfigEnvVarExpansionTests : IDisposable
{
    private readonly List<string> _setVars = [];

    private void SetEnv(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _setVars.Add(name);
    }

    public void Dispose()
    {
        foreach (var name in _setVars)
            Environment.SetEnvironmentVariable(name, null);
    }

    [Fact]
    public void WholeAndInlineVariables_AreReplacedAndUnsetOnesKeepTheirPlaceholder()
    {
        SetEnv("CRAFT_TEST_1_KEY", "val1");
        SetEnv("CRAFT_TEST_HOST2", "host2");
        SetEnv("CRAFT_TEST_PORT2", "9090");
        Environment.SetEnvironmentVariable("CRAFT_UNSET_XYZ_9876", null);
        var node = JsonNode.Parse("""
            {
              "Token": "$CRAFT_TEST_1_KEY",
              "Url": "https://${CRAFT_TEST_HOST2}:${CRAFT_TEST_PORT2}/v1",
              "Missing": "$CRAFT_UNSET_XYZ_9876",
              "MissingInline": "https://${CRAFT_UNSET_XYZ_9876}/api"
            }
            """)!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal("val1", node["Token"]!.GetValue<string>());
        Assert.Equal("https://host2:9090/v1", node["Url"]!.GetValue<string>());
        Assert.Equal("$CRAFT_UNSET_XYZ_9876", node["Missing"]!.GetValue<string>());
        Assert.Equal("https://${CRAFT_UNSET_XYZ_9876}/api", node["MissingInline"]!.GetValue<string>());
    }

    [Fact]
    public void Expansion_TraversesNestedObjectsAndArraysAndLeavesNonStringsUnchanged()
    {
        SetEnv("CRAFT_TEST_DEEP_VAL", "deep-value");
        var node = JsonNode.Parse("""
            {
              "A": { "B": { "C": "$CRAFT_TEST_DEEP_VAL", "Port": 443 } },
              "Items": ["literal", "$CRAFT_TEST_DEEP_VAL", 42],
              "Enabled": true,
              "Count": 3.14,
              "Null": null
            }
            """)!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal("deep-value", node["A"]!["B"]!["C"]!.GetValue<string>());
        Assert.Equal(443, node["A"]!["B"]!["Port"]!.GetValue<int>());
        var items = node["Items"]!.AsArray();
        Assert.Equal("literal", items[0]!.GetValue<string>());
        Assert.Equal("deep-value", items[1]!.GetValue<string>());
        Assert.Equal(42, items[2]!.GetValue<int>());
        Assert.True(node["Enabled"]!.GetValue<bool>());
        Assert.Equal(3.14, node["Count"]!.GetValue<double>());
        Assert.Null(node["Null"]);
    }

    [Fact]
    public void Load_ExpandsEnvVarsInConfigJson()
    {
        SetEnv("CRAFT_TEST_LOAD_KEY", "loaded-api-key");

        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                tmpFile,
                """
                {
                  "ProviderId": "openai",
                  "Providers": {
                    "openai": {
                      "Protocol": "openai-chat-completions",
                      "ApiKey": "$CRAFT_TEST_LOAD_KEY"
                    }
                  }
                }
                """);

            var config = AppConfig.Load(tmpFile);

            Assert.Equal("loaded-api-key", config.Providers["openai"].ApiKey);
            Assert.Empty(config.ProviderPreferences);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }
}
