using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ModelPreferenceRulesBackendTests : IDisposable
{
    private const string Model = "test-reasoning-model";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"model_preference_rules_{Guid.NewGuid():N}");

    public ModelPreferenceRulesBackendTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "config.json"), "{}");
        File.WriteAllText(Path.Combine(_root, ModelThinkingAdapterCatalog.FileName), """
            {
              "reasoningCapabilities": {
                "adapters": [
                  {
                    "protocols": ["anthropic"],
                    "models": ["test-reasoning-model"],
                    "supportsDisable": false,
                    "supportedEfforts": ["low", "high"],
                    "defaultEffort": "high"
                  }
                ]
              }
            }
            """);
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
    public void BuiltInProtocolCatalogNeverRepairsOrRejectsBackendReasoning()
    {
        var reasoningOff = new AppConfig.ReasoningConfig { Enabled = false };
        var preference = new ModelPreference { Model = Model, Reasoning = reasoningOff };

        var builtIn = Config(backendId: null);
        Assert.True(ModelPreferenceRules.Normalize(builtIn, "claude", preference).Reasoning.Enabled);
        Assert.NotNull(ModelPreferenceRules.ValidateReasoning(builtIn, "claude", Model, reasoningOff));

        var backend = Config(backendId: "demo-backend");
        Assert.False(ModelPreferenceRules.Normalize(backend, "claude", preference).Reasoning.Enabled);
        Assert.False(ModelPreferenceRules.CreateDefault(backend, "claude", Model).Reasoning.Enabled);
        Assert.Null(ModelPreferenceRules.ValidateReasoning(backend, "claude", Model, reasoningOff));
    }

    private AppConfig Config(string? backendId) => new()
    {
        WorkspaceConfigPath = Path.Combine(_root, "config.json"),
        Providers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude"] = new()
            {
                Protocol = ModelProviderProtocols.Anthropic,
                BackendId = backendId,
                ApiKey = "test-key",
                EndPoint = "https://api.example.com"
            }
        }
    };
}
