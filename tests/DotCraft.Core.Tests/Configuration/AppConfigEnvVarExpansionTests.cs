using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Tests.Configuration;

public class AppConfigEnvVarExpansionTests : IDisposable
{
    // Track env vars set during a test so we can clean them up.
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

    // -------------------------------------------------------------------------
    // $VAR whole-value substitution
    // -------------------------------------------------------------------------

    [Fact]
    public void WholeValue_DollarVar_IsReplaced()
    {
        SetEnv("CRAFT_TEST_API_KEY", "fixture-config-token");
        var node = JsonNode.Parse("""{"Token": "$CRAFT_TEST_API_KEY"}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal("fixture-config-token", node["Token"]!.GetValue<string>());
    }

    [Fact]
    public void WholeValue_DollarVar_UnderscoreAndDigits_IsReplaced()
    {
        SetEnv("CRAFT_TEST_1_KEY", "val1");
        var node = JsonNode.Parse("""{"Token": "$CRAFT_TEST_1_KEY"}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal("val1", node["Token"]!.GetValue<string>());
    }

    [Fact]
    public void WholeValue_UnsetVar_KeepsPlaceholder()
    {
        // Ensure the variable is definitely not set.
        Environment.SetEnvironmentVariable("CRAFT_UNSET_XYZ_9876", null);

        var node = JsonNode.Parse("""{"Token": "$CRAFT_UNSET_XYZ_9876"}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal("$CRAFT_UNSET_XYZ_9876", node["Token"]!.GetValue<string>());
    }

    // -------------------------------------------------------------------------
    // ${VAR} inline substitution
    // -------------------------------------------------------------------------

    [Fact]
    public void Inline_BraceVar_IsReplaced()
    {
        SetEnv("CRAFT_TEST_HOST", "myhost.example.com");
        var node = JsonNode.Parse("""{"Url": "https://${CRAFT_TEST_HOST}/api"}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal("https://myhost.example.com/api", node["Url"]!.GetValue<string>());
    }

    [Fact]
    public void Inline_MultipleBraceVars_AllReplaced()
    {
        SetEnv("CRAFT_TEST_HOST2", "host2");
        SetEnv("CRAFT_TEST_PORT2", "9090");
        var node = JsonNode.Parse("""{"Url": "https://${CRAFT_TEST_HOST2}:${CRAFT_TEST_PORT2}/v1"}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal("https://host2:9090/v1", node["Url"]!.GetValue<string>());
    }

    [Fact]
    public void Inline_UnsetBraceVar_KeepsPlaceholder()
    {
        Environment.SetEnvironmentVariable("CRAFT_UNSET_INLINE_ABC", null);

        var node = JsonNode.Parse("""{"Url": "https://${CRAFT_UNSET_INLINE_ABC}/api"}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal("https://${CRAFT_UNSET_INLINE_ABC}/api", node["Url"]!.GetValue<string>());
    }

    // -------------------------------------------------------------------------
    // Non-string values are untouched
    // -------------------------------------------------------------------------

    [Fact]
    public void NonStringValues_AreUnchanged()
    {
        var node = JsonNode.Parse("""{"Port": 8080, "Enabled": true, "Count": 3.14, "Null": null}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal(8080, node["Port"]!.GetValue<int>());
        Assert.True(node["Enabled"]!.GetValue<bool>());
        Assert.Equal(3.14, node["Count"]!.GetValue<double>());
        Assert.Null(node["Null"]);
    }

    // -------------------------------------------------------------------------
    // Nested object traversal
    // -------------------------------------------------------------------------

    [Fact]
    public void NestedObject_StringValuesAreExpanded()
    {
        SetEnv("CRAFT_TEST_NESTED_KEY", "nested-secret");
        var node = JsonNode.Parse("""{"Tracker": {"ApiKey": "$CRAFT_TEST_NESTED_KEY", "Port": 443}}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        var tracker = node["Tracker"]!.AsObject();
        Assert.Equal("nested-secret", tracker["ApiKey"]!.GetValue<string>());
        Assert.Equal(443, tracker["Port"]!.GetValue<int>());
    }

    [Fact]
    public void DeeplyNested_StringValuesAreExpanded()
    {
        SetEnv("CRAFT_TEST_DEEP_VAL", "deep-value");
        var node = JsonNode.Parse("""{"A": {"B": {"C": "$CRAFT_TEST_DEEP_VAL"}}}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        Assert.Equal("deep-value", node["A"]!["B"]!["C"]!.GetValue<string>());
    }

    // -------------------------------------------------------------------------
    // Array traversal
    // -------------------------------------------------------------------------

    [Fact]
    public void Array_StringElementsAreExpanded()
    {
        SetEnv("CRAFT_TEST_ARR_VAL", "expanded-item");
        var node = JsonNode.Parse("""{"Items": ["literal", "$CRAFT_TEST_ARR_VAL", 42]}""")!;

        AppConfig.ExpandEnvironmentVariables(node);

        var arr = node["Items"]!.AsArray();
        Assert.Equal("literal", arr[0]!.GetValue<string>());
        Assert.Equal("expanded-item", arr[1]!.GetValue<string>());
        Assert.Equal(42, arr[2]!.GetValue<int>());
    }

    // -------------------------------------------------------------------------
    // Full AppConfig.Load round-trip
    // -------------------------------------------------------------------------

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
                      "Protocol": "openai",
                      "ApiKey": "$CRAFT_TEST_LOAD_KEY"
                    }
                  },
                  "Model": "gpt-4o"
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
