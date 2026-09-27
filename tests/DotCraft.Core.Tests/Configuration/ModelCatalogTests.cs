using DotCraft.Configuration;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

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
    public void ResolveDetailed_MarksUnknownModelAsFallback()
    {
        var resolution = ModelCatalog.ResolveDetailed("unknown-model");

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
                "special-model": { "contextWindow": 321000 }
              }
            }
            """);

        var contextWindow = ModelCatalog.Resolve("azure/special-model-deployment", globalCatalogPath: catalogPath);

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

    [Theory]
    [InlineData(null, 1_050_000)]
    [InlineData(-1, 1_050_000)]
    [InlineData(256_000, 256_000)]
    [InlineData(2_000_000, 1_050_000)]
    public void ResolveCompactionConfig_AppliesClientBudget(int? budget, int expectedWindow)
    {
        var configPath = WriteConfig("full-window", "{}");
        WriteCatalog("full-window", """
            {
              "models": {
                "large-model": { "contextWindow": 1050000 }
              }
            }
            """);

        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderPreferences = new() { ["test"] = new ModelPreference { Model = "large-model"  } }
        };
        if (budget.HasValue)
            config.Compaction.MaxContextWindow = budget.Value;
        ModelCatalog.ApplyToConfig(
            config,
            globalConfigPath: configPath,
            workspaceConfigPath: null);

        var compaction = ModelCatalog.ResolveCompactionConfig(config, "gateway/large-model");
        Assert.Equal(expectedWindow, compaction.ContextWindow);
        Assert.Equal(expectedWindow, config.Compaction.ContextWindow);
        Assert.Equal(expectedWindow - 20_000, compaction.EffectiveContextWindow());
    }

    [Fact]
    public void ResolveCompactionConfig_UsesEffectiveModel()
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
            ProviderPreferences = new() { ["test"] = new ModelPreference { Model = "configured-model"  } }
        };
        config.Compaction.MaxContextWindow = 256_000;
        ModelCatalog.ApplyToConfig(
            config,
            globalConfigPath: configPath,
            workspaceConfigPath: null);

        var compaction = ModelCatalog.ResolveCompactionConfig(config, "provider/active-model");

        Assert.Equal(256_000, config.Compaction.ContextWindow);
        Assert.Equal(200_000, compaction.ContextWindow);
        Assert.Equal(180_000, compaction.EffectiveContextWindow());
    }

    [Theory]
    [InlineData(null, 256_000)]
    [InlineData(-1, 1_000_000)]
    [InlineData(128_000, 128_000)]
    public void LoadWithGlobalFallback_AppliesWorkspaceBudget(int? workspaceBudget, int expectedWindow)
    {
        var globalPath = WriteConfig("global-budget", """
            {
              "ProviderId": "test",
              "ProviderPreferences": { "test": { "Model": "budget-model" } },
              "Compaction": { "MaxContextWindow": 256000 }
            }
            """);
        WriteCatalog("global-budget", """
            { "models": { "budget-model": { "contextWindow": 1000000 } } }
            """);
        var workspace = new System.Text.Json.Nodes.JsonObject();
        if (workspaceBudget.HasValue)
            workspace["Compaction"] = new System.Text.Json.Nodes.JsonObject { ["MaxContextWindow"] = workspaceBudget.Value };
        var workspacePath = WriteConfig("workspace-budget", workspace.ToJsonString());

        var config = AppConfig.LoadWithGlobalFallback(workspacePath, globalPath);

        Assert.Equal(expectedWindow, config.Compaction.ContextWindow);
        Assert.Equal(expectedWindow, ModelCatalog.ResolveCompactionConfig(config, "budget-model").ContextWindow);
        Assert.Equal(1_000_000, ModelCatalog.Resolve(config, "budget-model"));
    }

    [Theory]
    [InlineData(-1, 256_000)]
    [InlineData(128_000, 128_000)]
    public void ResolveCompactionConfig_AppliesBudgetToUnknownModelFallback(int budget, int expectedWindow)
    {
        var config = new AppConfig();
        config.Compaction.MaxContextWindow = budget;

        Assert.Equal(expectedWindow, ModelCatalog.ResolveCompactionConfig(config, "unknown-model").ContextWindow);
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
    public void Load_UsesSiblingCatalog()
    {
        var configPath = WriteConfig("workspace", """
            {
              "ProviderId": "test",
              "ProviderPreferences": {
                "test": {
                  "Model": "my-model",
                  "Reasoning": { "Enabled": false, "Effort": "Medium", "Output": "Full" },
                  "Speed": "Standard"
                }
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
