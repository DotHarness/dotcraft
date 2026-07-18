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
    public void ResolveAnthropicThinkingAdapter_MergesGlobalAndWorkspaceCatalogs()
    {
        var globalPath = WriteCatalog("global", """
            {
              "anthropicThinking": {
                "adapters": [
                  {
                    "models": ["global-adaptive-"],
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
            model: "global-adaptive-v1",
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
                    "models": ["test-adaptive-"],
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
              },
              "anthropicMessageContent": {
                "adapters": [
                  {
                    "endpoints": ["reasoning-provider"],
                    "reasoningHistory": { "blockType": "thinking" }
                  },
                  {
                    "models": ["missing-block-type"],
                    "reasoningHistory": {}
                  }
                ]
              }
            }
            """);

        Assert.Contains("valid-", catalog.DeepThinking.Models);
        Assert.Contains("endpoint", catalog.DeepThinking.Endpoints);
        Assert.DoesNotContain("", catalog.DeepThinking.Models);
        var adapter = Assert.Single(catalog.AnthropicThinking.Adapters);
        Assert.Contains("test-adaptive-", adapter.Models);
        Assert.Equal("adaptive", adapter.ThinkingType);
        Assert.Equal("summarized", adapter.ThinkingDisplay);
        Assert.Equal("medium", adapter.OutputConfigEffort);
        Assert.Equal("max", adapter.OutputConfigEffortMap["extraHigh"]);
        var messageAdapter = Assert.Single(catalog.AnthropicMessageContent.Adapters);
        Assert.Contains("reasoning-provider", messageAdapter.Endpoints);
        Assert.Equal("thinking", messageAdapter.ReasoningHistoryBlockType);
    }

    private string WriteCatalog(string directoryName, string json)
    {
        var directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ModelThinkingAdapterCatalog.FileName);
        File.WriteAllText(path, json);
        return path;
    }

}
