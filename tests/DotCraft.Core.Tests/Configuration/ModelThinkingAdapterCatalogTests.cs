using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Configuration;

public sealed class ModelThinkingAdapterCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"model_thinking_adapters_{Guid.NewGuid():N}");

    public ModelThinkingAdapterCatalogTests()
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

    [Theory]
    [InlineData("deepseek-reasoner")]
    [InlineData("mimo-v2.5-pro")]
    public void ShouldApplyDeepThinking_UsesBuiltInCatalog(string model)
    {
        Assert.True(ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(
            endpoint: "https://api.openai-compatible.test/v1",
            model: model));
    }

    [Fact]
    public void ShouldApplyDeepThinking_UsesNamespacedModelSuffix()
    {
        Assert.True(ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(
            endpoint: "https://api.openai-compatible.test/v1",
            model: "provider/xiaomi/mimo-v2.5-pro"));
    }

    [Fact]
    public void ShouldApplyDeepThinking_UsesEndpointHost()
    {
        Assert.True(ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(
            endpoint: "https://api.deepseek.com/v1",
            model: "custom-model"));
    }

    [Fact]
    public void ShouldApplyDeepThinking_MergesGlobalAndWorkspaceCatalogs()
    {
        var globalPath = WriteCatalog("global", """
            {
              "deepThinking": {
                "models": ["global-thinking-"],
                "endpoints": ["global-thinking"]
              }
            }
            """);
        var workspacePath = WriteCatalog("workspace", """
            {
              "deepThinking": {
                "models": ["workspace-thinking-"],
                "endpoints": ["workspace-thinking"]
              }
            }
            """);

        Assert.True(ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(
            endpoint: "https://api.openai-compatible.test/v1",
            model: "global-thinking-v1",
            globalCatalogPath: globalPath,
            workspaceCatalogPath: workspacePath));
        Assert.True(ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(
            endpoint: "https://api.workspace-thinking.test/v1",
            model: "unlisted-model",
            globalCatalogPath: globalPath,
            workspaceCatalogPath: workspacePath));
    }

    [Fact]
    public void ShouldApplyDeepThinking_IgnoresInvalidAndMissingCatalogs()
    {
        var invalidPath = WriteRawCatalog("invalid", "{");

        Assert.True(ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(
            endpoint: "https://api.openai-compatible.test/v1",
            model: "mimo-v2.5-pro",
            globalCatalogPath: invalidPath,
            workspaceCatalogPath: Path.Combine(_root, "missing", ModelThinkingAdapterCatalog.FileName)));
    }

    [Theory]
    [InlineData("claude-opus-4-7")]
    [InlineData("vertex_ai/claude-opus-4-7")]
    [InlineData("claude-mythos-preview")]
    [InlineData("claude-opus-4-6")]
    [InlineData("claude-sonnet-4-6")]
    public void ResolveAnthropicThinkingAdapter_UsesBuiltInCatalog(string model)
    {
        var adapter = ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(
            endpoint: "https://api.anthropic.com",
            model: model);

        Assert.NotNull(adapter);
        Assert.Equal("adaptive", adapter.ThinkingType);
        Assert.Equal("fromReasoningOutput", adapter.ThinkingDisplay);
        Assert.Equal("fromReasoningEffort", adapter.OutputConfigEffort);
        Assert.NotEmpty(adapter.OutputConfigEffortMap);
    }

    [Fact]
    public void ResolveAnthropicThinkingAdapter_MergesGlobalAndWorkspaceCatalogs()
    {
        var globalPath = WriteCatalog("global", """
            {
              "anthropicThinking": {
                "adapters": [
                  {
                    "models": ["global-claude-"],
                    "thinking": { "type": "adaptive", "display": "omitted" }
                  }
                ]
              }
            }
            """);
        var workspacePath = WriteCatalog("workspace", """
            {
              "anthropicThinking": {
                "adapters": [
                  {
                    "endpoints": ["workspace-anthropic"],
                    "thinking": { "type": "adaptive", "display": "summarized" },
                    "outputConfig": { "effort": "high" }
                  }
                ]
              }
            }
            """);

        var byModel = ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(
            endpoint: "https://api.anthropic.test",
            model: "global-claude-v1",
            globalCatalogPath: globalPath,
            workspaceCatalogPath: workspacePath);
        var byEndpoint = ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(
            endpoint: "https://workspace-anthropic.test/v1",
            model: "unlisted-model",
            globalCatalogPath: globalPath,
            workspaceCatalogPath: workspacePath);

        Assert.Equal("omitted", byModel!.ThinkingDisplay);
        Assert.Equal("summarized", byEndpoint!.ThinkingDisplay);
        Assert.Equal("high", byEndpoint.OutputConfigEffort);
    }

    [Fact]
    public void ResolveReasoningCapability_UsesBuiltInAnthropicCatalog()
    {
        var capability = ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            protocol: ModelProviderProtocols.Anthropic,
            endpoint: "https://api.anthropic.com",
            model: "vertex_ai/claude-opus-4-7");

        Assert.NotNull(capability);
        Assert.True(capability.SupportsDisable);
        Assert.Equal(ReasoningEffort.High, capability.DefaultEffort);
        Assert.Contains(capability.SupportedEfforts, option => option.Effort == ReasoningEffort.ExtraHigh);
        Assert.Contains(ReasoningOutput.Full, capability.SupportedOutputs);
    }

    [Fact]
    public void ResolveReasoningCapability_OpenAIProtocolUsesDefaultCapability()
    {
        var capability = ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            protocol: ModelProviderProtocols.OpenAI,
            endpoint: "https://litellm.example.test/v1",
            model: "vendor/reasoning-model-v1");

        Assert.NotNull(capability);
        Assert.True(capability.SupportsDisable);
        Assert.Equal(ReasoningEffort.Medium, capability.DefaultEffort);
        Assert.Equal(
            [ReasoningEffort.Low, ReasoningEffort.Medium, ReasoningEffort.High, ReasoningEffort.ExtraHigh],
            capability.SupportedEfforts.Select(option => option.Effort));
        Assert.Contains(ReasoningOutput.Full, capability.SupportedOutputs);
    }

    [Fact]
    public void ResolveReasoningCapability_MythosCannotDisable()
    {
        var capability = ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            protocol: ModelProviderProtocols.Anthropic,
            endpoint: "https://api.anthropic.com",
            model: "claude-mythos-preview");

        Assert.NotNull(capability);
        Assert.False(capability.SupportsDisable);
    }

    [Fact]
    public void ResolveReasoningCapability_MergesWorkspaceCatalog()
    {
        var workspacePath = WriteCatalog("workspace-capabilities", """
            {
              "reasoningCapabilities": {
                "adapters": [
                  {
                    "protocols": ["openai"],
                    "models": ["custom-reasoner-"],
                    "supportsDisable": true,
                    "supportedEfforts": ["low", "high"],
                    "defaultEffort": "high",
                    "supportedOutputs": ["summary"],
                    "defaultOutput": "summary"
                  }
                ]
              }
            }
            """);

        var capability = ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            protocol: ModelProviderProtocols.OpenAI,
            endpoint: "https://api.example.com/v1",
            model: "vendor/custom-reasoner-v1",
            workspaceCatalogPath: workspacePath);

        Assert.NotNull(capability);
        Assert.Equal(ReasoningEffort.High, capability.DefaultEffort);
        Assert.Equal([ReasoningEffort.Low, ReasoningEffort.High], capability.SupportedEfforts.Select(o => o.Effort));
        Assert.Equal([ReasoningOutput.Summary], capability.SupportedOutputs);
    }

    [Fact]
    public void LoadJson_IgnoresInvalidEntries()
    {
        var catalog = ModelThinkingAdapterCatalog.LoadJson("""
            {
              "deepThinking": {
                "models": ["valid-", "", 123],
                "endpoints": ["endpoint", null]
              },
              "anthropicThinking": {
                "adapters": [
                  {
                    "models": ["claude-test-"],
                    "thinking": { "type": "adaptive", "display": "summarized" },
                    "outputConfig": {
                      "effort": "medium",
                      "effortMap": {
                        "extraHigh": "max"
                      }
                    }
                  },
                  {
                    "models": [],
                    "thinking": { "type": "adaptive" }
                  }
                ]
              }
            }
            """);

        Assert.Contains("valid-", catalog.DeepThinking.Models);
        Assert.Contains("endpoint", catalog.DeepThinking.Endpoints);
        Assert.DoesNotContain("", catalog.DeepThinking.Models);
        var adapter = Assert.Single(catalog.AnthropicThinking.Adapters);
        Assert.Contains("claude-test-", adapter.Models);
        Assert.Equal("adaptive", adapter.ThinkingType);
        Assert.Equal("summarized", adapter.ThinkingDisplay);
        Assert.Equal("medium", adapter.OutputConfigEffort);
        Assert.Equal("max", adapter.OutputConfigEffortMap["extraHigh"]);
    }

    private string WriteCatalog(string directoryName, string json)
    {
        var directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ModelThinkingAdapterCatalog.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    private string WriteRawCatalog(string directoryName, string text)
    {
        var directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ModelThinkingAdapterCatalog.FileName);
        File.WriteAllText(path, text);
        return path;
    }
}
