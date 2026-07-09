using DotCraft.Configuration;

namespace DotCraft.Tests.Configuration;

public sealed class ModelContextWindowCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"model_context_windows_{Guid.NewGuid():N}");

    public ModelContextWindowCatalogTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Resolve_UsesDefaultForUnknownModel()
    {
        var contextWindow = ModelContextWindowCatalog.Resolve("unknown-model");

        Assert.Equal(256_000, contextWindow);
    }

    [Fact]
    public void ResolveDetailed_MarksUnknownModelAsFallback()
    {
        var resolution = ModelContextWindowCatalog.ResolveDetailed("unknown-model");

        Assert.Equal(256_000, resolution.ContextWindow);
        Assert.False(resolution.HasExplicitMatch);
        Assert.Null(resolution.MatchedPattern);
        Assert.Null(resolution.MatchKind);
    }

    [Fact]
    public void Resolve_UsesLongestPrefix()
    {
        var catalogPath = WriteCatalog("global", """
            {
              "models": {
                "test-": 100000,
                "test-long": 200000
              }
            }
            """);

        var contextWindow = ModelContextWindowCatalog.Resolve("test-long-v1", globalCatalogPath: catalogPath);

        Assert.Equal(200_000, contextWindow);
    }

    [Fact]
    public void ResolveDetailed_RecordsExplicitPrefixMatch()
    {
        var catalogPath = WriteCatalog("global", """
            {
              "models": {
                "test-": 100000,
                "test-long": 200000
              }
            }
            """);

        var resolution = ModelContextWindowCatalog.ResolveDetailed("test-long-v1", globalCatalogPath: catalogPath);

        Assert.Equal(200_000, resolution.ContextWindow);
        Assert.True(resolution.HasExplicitMatch);
        Assert.Equal("test-long", resolution.MatchedPattern);
        Assert.Equal("prefix", resolution.MatchKind);
    }

    [Fact]
    public void Resolve_UsesNamespacedModelSuffix()
    {
        var catalogPath = WriteCatalog("global", """
            {
              "models": {
                "gpt-special": 321000
              }
            }
            """);

        var contextWindow = ModelContextWindowCatalog.Resolve("azure/gpt-special-deployment", globalCatalogPath: catalogPath);

        Assert.Equal(321_000, contextWindow);
    }

    [Fact]
    public void Resolve_UsesMultiSegmentNamespacedModelSuffix()
    {
        var catalogPath = WriteCatalog("global", """
            {
              "models": {
                "qwen3-coder-plus": 997952
              }
            }
            """);

        var contextWindow = ModelContextWindowCatalog.Resolve(
            "openrouter/qwen/qwen3-coder-plus",
            globalCatalogPath: catalogPath);

        Assert.Equal(997_952, contextWindow);
    }

    [Fact]
    public void Resolve_UsesFinalModelSegmentWhenProviderPrefixDoesNotMatch()
    {
        var contextWindow = ModelContextWindowCatalog.Resolve("provider/glm-5.1");

        Assert.Equal(200_000, contextWindow);
    }

    [Fact]
    public void Resolve_UsesBuiltInClaudeOpusCatalogThroughProviderPrefixes()
    {
        Assert.Equal(1_000_000, ModelContextWindowCatalog.Resolve("provider/claude-opus-4-8"));
    }

    [Fact]
    public void ResolveCompactionConfig_CapsGpt55InferredWindowByDefault()
    {
        Assert.Equal(1_050_000, ModelContextWindowCatalog.Resolve("gpt-5.5"));

        var config = new AppConfig
        {
            Model = "gpt-5.5"
        };
        ModelContextWindowCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "gpt-5.5" }""")!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        Assert.Equal(256_000, config.Compaction.ContextWindow);
        Assert.Equal(236_000, config.Compaction.EffectiveContextWindow());
    }

    [Fact]
    public void ResolveCompactionConfig_MaxModeUsesExplicitCatalogWindow()
    {
        var config = new AppConfig
        {
            Model = "gpt-5.5"
        };
        ModelContextWindowCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "gpt-5.5" }""")!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        var defaultCompaction = ModelContextWindowCatalog.ResolveCompactionConfig(
            config,
            "gpt-5.5",
            ContextWindowMode.Default);
        var maxCompaction = ModelContextWindowCatalog.ResolveCompactionConfig(
            config,
            "gpt-5.5",
            ContextWindowMode.Max);

        Assert.Equal(256_000, defaultCompaction.ContextWindow);
        Assert.Equal(1_050_000, maxCompaction.ContextWindow);
        Assert.Equal(1_030_000, maxCompaction.EffectiveContextWindow());
    }

    [Fact]
    public void ResolveContextWindowCapability_DoesNotEnableMaxForFallbackCatalogResolution()
    {
        var config = new AppConfig
        {
            Model = "unknown-model"
        };
        ModelContextWindowCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "unknown-model" }""")!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        var capability = ModelContextWindowCatalog.ResolveContextWindowCapability(config, "unknown-model");
        var maxCompaction = ModelContextWindowCatalog.ResolveCompactionConfig(
            config,
            "unknown-model",
            ContextWindowMode.Max);

        Assert.Equal(256_000, capability.CatalogWindow);
        Assert.Equal(256_000, capability.ConfiguredWindow);
        Assert.False(capability.SupportsMax);
        Assert.False(capability.HasExplicitCatalogMatch);
        Assert.Equal(256_000, capability.MaxWindow);
        Assert.Equal(256_000, maxCompaction.ContextWindow);
    }

    [Fact]
    public void ResolveCompactionConfig_UsesEffectiveModel_WhenContextWindowIsInferred()
    {
        var config = new AppConfig
        {
            Model = "mimo-v2.5-pro"
        };
        ModelContextWindowCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "mimo-v2.5-pro" }""")!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        var compaction = ModelContextWindowCatalog.ResolveCompactionConfig(config, "provider/glm-5.1");

        Assert.Equal(200_000, compaction.ContextWindow);
        Assert.Equal(180_000, compaction.EffectiveContextWindow());
    }

    [Fact]
    public void ResolveCompactionConfig_CapsInferredModelWindow()
    {
        var config = new AppConfig
        {
            Model = "mimo-v2.5-pro",
            Compaction =
            {
                MaxContextWindow = 300_000
            }
        };
        ModelContextWindowCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "mimo-v2.5-pro" }""")!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        var compaction = ModelContextWindowCatalog.ResolveCompactionConfig(config, "gateway/xiaomi/mimo-v2.5-pro");

        Assert.Equal(300_000, config.Compaction.ContextWindow);
        Assert.Equal(300_000, compaction.ContextWindow);
    }

    [Fact]
    public void ResolveCompactionConfig_PreservesExplicitContextWindow()
    {
        var config = new AppConfig
        {
            Model = "mimo-v2.5-pro",
            Compaction = new DotCraft.Context.Compaction.CompactionConfig
            {
                ContextWindow = 123_000,
                MaxContextWindow = 100_000
            }
        };
        ModelContextWindowCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""
                {
                  "Model": "mimo-v2.5-pro",
                  "Compaction": {
                    "ContextWindow": 123000
                  }
                }
                """)!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        var compaction = ModelContextWindowCatalog.ResolveCompactionConfig(config, "provider/glm-5.1");

        Assert.Equal(123_000, compaction.ContextWindow);
    }

    [Fact]
    public void Resolve_UsesBuiltInChineseModelCatalogThroughProviderPrefixes()
    {
        Assert.Equal(204_800, ModelContextWindowCatalog.Resolve("openrouter/minimax/minimax-m2.7"));
        Assert.Equal(1_048_576, ModelContextWindowCatalog.Resolve("gateway/xiaomi/mimo-v2.5-pro"));
        Assert.Equal(131_072, ModelContextWindowCatalog.Resolve("siliconflow/tencent/Hunyuan-A13B-Instruct"));
    }

    [Fact]
    public void Resolve_WorkspaceCatalogOverridesGlobalCatalog()
    {
        var globalPath = WriteCatalog("global", """
            {
              "models": {
                "my-model": 111000
              }
            }
            """);
        var workspacePath = WriteCatalog("workspace", """
            {
              "models": {
                "my-model": 222000
              }
            }
            """);

        var contextWindow = ModelContextWindowCatalog.Resolve(
            "my-model",
            globalCatalogPath: globalPath,
            workspaceCatalogPath: workspacePath);

        Assert.Equal(222_000, contextWindow);
    }

    [Fact]
    public void HasExplicitCompactionContextWindow_DetectsSnakeCase()
    {
        var configNode = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "Compaction": {
                "context_window": 123000
              }
            }
            """)!;

        Assert.True(ModelContextWindowCatalog.HasExplicitCompactionContextWindow(configNode));
    }

    [Fact]
    public void Load_UsesSiblingCatalogWhenContextWindowIsNotExplicit()
    {
        var configPath = WriteConfig("workspace", """
            {
              "Model": "my-model",
              "Compaction": {
                "MaxContextWindow": 400000
              }
            }
            """);
        WriteCatalog("workspace", """
            {
              "models": {
                "my-model": 333000
              }
            }
            """);

        var config = AppConfig.Load(configPath);

        Assert.Equal(333_000, config.Compaction.ContextWindow);
    }

    [Fact]
    public void Load_UsesCatalogEvenWhenConfigFileIsMissing()
    {
        var configPath = Path.Combine(_root, "missing", "config.json");

        var config = AppConfig.Load(configPath);

        Assert.Equal(128_000, config.Compaction.ContextWindow);
    }

    [Fact]
    public void Load_PreservesExplicitContextWindow()
    {
        var configPath = WriteConfig("workspace", """
            {
              "Model": "my-model",
              "Compaction": {
                "ContextWindow": 123000
              }
            }
            """);
        WriteCatalog("workspace", """
            {
              "models": {
                "my-model": 333000
              }
            }
            """);

        var config = AppConfig.Load(configPath);

        Assert.Equal(123_000, config.Compaction.ContextWindow);
    }

    [Fact]
    public void LoadJson_IgnoresInvalidModelWindows()
    {
        var catalog = ModelContextWindowCatalog.LoadJson("""
            {
              "defaultContextWindow": 999,
              "models": {
                "too-small": 999,
                "valid": 64000
              }
            }
            """);

        Assert.Null(catalog.DefaultContextWindow);
        Assert.False(catalog.Models.ContainsKey("too-small"));
        Assert.Equal(64_000, catalog.Models["valid"]);
    }

    private string WriteConfig(string directoryName, string json)
    {
        var directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private string WriteCatalog(string directoryName, string json)
    {
        var directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ModelContextWindowCatalog.FileName);
        File.WriteAllText(path, json);
        return path;
    }
}
