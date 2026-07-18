using DotCraft.Configuration;

namespace DotCraft.Tests.Configuration;

public sealed class ModelCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"model_catalog_{Guid.NewGuid():N}");

    public ModelCatalogTests()
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
        var contextWindow = ModelCatalog.Resolve("unknown-model");

        Assert.Equal(256_000, contextWindow);
    }

    [Fact]
    public void ResolveDetailed_MarksUnknownModelAsFallback()
    {
        var resolution = ModelCatalog.ResolveDetailed("unknown-model");

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
                "test-": { "contextWindow": 100000 },
                "test-long": { "contextWindow": 200000 }
              }
            }
            """);

        var contextWindow = ModelCatalog.Resolve("test-long-v1", globalCatalogPath: catalogPath);

        Assert.Equal(200_000, contextWindow);
    }

    [Fact]
    public void ResolveDetailed_RecordsExplicitPrefixMatch()
    {
        var catalogPath = WriteCatalog("global", """
            {
              "models": {
                "test-": { "contextWindow": 100000 },
                "test-long": { "contextWindow": 200000 }
              }
            }
            """);

        var resolution = ModelCatalog.ResolveDetailed("test-long-v1", globalCatalogPath: catalogPath);

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
                "gpt-special": { "contextWindow": 321000 }
              }
            }
            """);

        var contextWindow = ModelCatalog.Resolve("azure/gpt-special-deployment", globalCatalogPath: catalogPath);

        Assert.Equal(321_000, contextWindow);
    }

    [Fact]
    public void Resolve_UsesMultiSegmentNamespacedModelSuffix()
    {
        var catalogPath = WriteCatalog("global", """
            {
              "models": {
                "model-plus": { "contextWindow": 997952 }
              }
            }
            """);

        var contextWindow = ModelCatalog.Resolve(
            "gateway/vendor/model-plus",
            globalCatalogPath: catalogPath);

        Assert.Equal(997_952, contextWindow);
    }

    [Fact]
    public void ResolveCompactionConfig_CapsInferredWindowByDefault()
    {
        var configPath = WriteConfig("capped-default", "{}");
        WriteCatalog("capped-default", """
            {
              "models": {
                "large-model": { "contextWindow": 1050000 }
              }
            }
            """);

        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderModels = new() { ["test"] = "large-model" }
        };
        ModelCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "large-model" }""")!,
            globalConfigPath: configPath,
            workspaceConfigPath: null);

        Assert.Equal(256_000, config.Compaction.ContextWindow);
        Assert.Equal(236_000, config.Compaction.EffectiveContextWindow());
    }

    [Fact]
    public void ResolveCompactionConfig_MaxModeUsesExplicitCatalogWindow()
    {
        var configPath = WriteConfig("max-mode", "{}");
        WriteCatalog("max-mode", """
            {
              "models": {
                "large-model": { "contextWindow": 1050000 }
              }
            }
            """);

        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderModels = new() { ["test"] = "large-model" }
        };
        ModelCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "large-model" }""")!,
            globalConfigPath: configPath,
            workspaceConfigPath: null);

        var defaultCompaction = ModelCatalog.ResolveCompactionConfig(
            config,
            "large-model",
            ContextWindowMode.Default);
        var maxCompaction = ModelCatalog.ResolveCompactionConfig(
            config,
            "large-model",
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
            ProviderId = "test",
            ProviderModels = new() { ["test"] = "unknown-model" }
        };
        ModelCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "unknown-model" }""")!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        var capability = ModelCatalog.ResolveContextWindowCapability(config, "unknown-model");
        var maxCompaction = ModelCatalog.ResolveCompactionConfig(
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
        var configPath = WriteConfig("effective-model", "{}");
        WriteCatalog("effective-model", """
            {
              "models": {
                "configured-model": { "contextWindow": 400000 },
                "active-model": { "contextWindow": 200000 }
              }
            }
            """);

        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderModels = new() { ["test"] = "configured-model" }
        };
        ModelCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "configured-model" }""")!,
            globalConfigPath: configPath,
            workspaceConfigPath: null);

        var compaction = ModelCatalog.ResolveCompactionConfig(config, "provider/active-model");

        Assert.Equal(200_000, compaction.ContextWindow);
        Assert.Equal(180_000, compaction.EffectiveContextWindow());
    }

    [Fact]
    public void ResolveCompactionConfig_CapsInferredModelWindow()
    {
        var configPath = WriteConfig("capped-model", "{}");
        WriteCatalog("capped-model", """
            {
              "models": {
                "large-model": { "contextWindow": 1000000 }
              }
            }
            """);

        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderModels = new() { ["test"] = "large-model" },
            Compaction =
            {
                MaxContextWindow = 300_000
            }
        };
        ModelCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""{ "Model": "large-model" }""")!,
            globalConfigPath: configPath,
            workspaceConfigPath: null);

        var compaction = ModelCatalog.ResolveCompactionConfig(config, "gateway/vendor/large-model");

        Assert.Equal(300_000, config.Compaction.ContextWindow);
        Assert.Equal(300_000, compaction.ContextWindow);
    }

    [Fact]
    public void ResolveCompactionConfig_PreservesExplicitContextWindow()
    {
        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderModels = new() { ["test"] = "custom-model" },
            Compaction = new DotCraft.Context.Compaction.CompactionConfig
            {
                ContextWindow = 123_000,
                MaxContextWindow = 100_000
            }
        };
        ModelCatalog.ApplyToConfig(
            config,
            System.Text.Json.Nodes.JsonNode.Parse("""
                {
                  "Model": "custom-model",
                  "Compaction": {
                    "ContextWindow": 123000
                  }
                }
                """)!,
            globalConfigPath: null,
            workspaceConfigPath: null);

        var compaction = ModelCatalog.ResolveCompactionConfig(config, "provider/other-model");

        Assert.Equal(123_000, compaction.ContextWindow);
    }

    [Fact]
    public void Resolve_WorkspaceCatalogOverridesGlobalCatalog()
    {
        var globalPath = WriteCatalog("global", """
            {
              "models": {
                "my-model": { "contextWindow": 111000 }
              }
            }
            """);
        var workspacePath = WriteCatalog("workspace", """
            {
              "models": {
                "my-model": { "contextWindow": 222000 }
              }
            }
            """);

        var contextWindow = ModelCatalog.Resolve(
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

        Assert.True(ModelCatalog.HasExplicitCompactionContextWindow(configNode));
    }

    [Fact]
    public void Load_UsesSiblingCatalogWhenContextWindowIsNotExplicit()
    {
        var configPath = WriteConfig("workspace", """
            {
              "ProviderId": "test",
              "ProviderModels": { "test": "my-model" },
              "Compaction": {
                "MaxContextWindow": 400000
              }
            }
            """);
        WriteCatalog("workspace", """
            {
              "models": {
                "my-model": { "contextWindow": 333000 }
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

        Assert.Equal(ModelCatalog.DefaultContextWindow, config.Compaction.ContextWindow);
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
                "my-model": { "contextWindow": 333000 }
              }
            }
            """);

        var config = AppConfig.Load(configPath);

        Assert.Equal(123_000, config.Compaction.ContextWindow);
    }

    [Fact]
    public void LoadJson_IgnoresInvalidModelWindows()
    {
        var catalog = ModelCatalog.LoadJson("""
            {
              "defaultContextWindow": 999,
              "models": {
                "too-small": { "contextWindow": 999 },
                "valid": { "contextWindow": 64000 }
              }
            }
            """);

        Assert.Null(catalog.DefaultContextWindow);
        Assert.False(catalog.Models.ContainsKey("too-small"));
        Assert.Equal(64_000, catalog.Models["valid"].ContextWindow);
    }

    [Fact]
    public void CapabilityResolution_UsesIndependentMostSpecificRules()
    {
        var catalogPath = WriteCatalog("global", """
            {
              "models": {
                "vendor/": {
                  "contextWindow": 64000
                },
                "custom-": {
                  "fast": { "protocols": ["openai-responses"] }
                },
                "custom-large": {
                  "contextWindow": 512000
                }
              }
            }
            """);

        Assert.Equal(512_000, ModelCatalog.Resolve("vendor/custom-large-v2", globalCatalogPath: catalogPath));
        Assert.True(ModelCatalog.SupportsFast(
            ModelProviderProtocols.OpenAIResponses,
            "vendor/custom-large-v2",
            globalCatalogPath: catalogPath));
    }

    [Fact]
    public void WorkspaceCatalog_MergesModelFieldsWithoutDroppingGlobalFast()
    {
        var globalPath = WriteCatalog("global", """
            {
              "models": {
                "custom-model": {
                  "contextWindow": 128000,
                  "fast": { "protocols": ["openai-responses"] }
                }
              }
            }
            """);
        var workspacePath = WriteCatalog("workspace", """
            {
              "models": {
                "custom-model": { "contextWindow": 640000 }
              }
            }
            """);

        Assert.Equal(640_000, ModelCatalog.Resolve("custom-model", globalPath, workspacePath));
        Assert.True(ModelCatalog.SupportsFast(
            ModelProviderProtocols.OpenAIResponses,
            "custom-model",
            globalPath,
            workspacePath));
    }

    [Fact]
    public void WorkspaceCatalog_FastNullDisablesInheritedFastWithoutDroppingContextWindow()
    {
        var globalPath = WriteCatalog("global-fast-null", """
            {
              "models": {
                "custom-model": {
                  "contextWindow": 640000,
                  "fast": { "protocols": ["openai-responses"] }
                }
              }
            }
            """);
        var workspacePath = WriteCatalog("workspace", """
            {
              "models": {
                "custom-model": { "fast": null }
              }
            }
            """);

        Assert.Equal(640_000, ModelCatalog.Resolve("custom-model", globalPath, workspacePath));
        Assert.False(ModelCatalog.SupportsFast(
            ModelProviderProtocols.OpenAIResponses,
            "custom-model",
            globalPath,
            workspacePath));
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
        var path = Path.Combine(directory, ModelCatalog.FileName);
        File.WriteAllText(path, json);
        return path;
    }
}
