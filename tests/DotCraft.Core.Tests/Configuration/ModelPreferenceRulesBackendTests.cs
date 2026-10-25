using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ModelPreferenceRulesBackendTests
{
    [Fact]
    public void BuiltInProtocolCatalogNeverRepairsOrRejectsBackendReasoning()
    {
        var reasoningOff = new AppConfig.ReasoningConfig { Enabled = false };
        var preference = new ModelPreference { Model = "claude-opus-5-5", Reasoning = reasoningOff };

        var builtIn = Config(backendId: null);
        Assert.True(ModelPreferenceRules.Normalize(builtIn, "claude", preference).Reasoning.Enabled);
        Assert.NotNull(ModelPreferenceRules.ValidateReasoning(builtIn, "claude", "claude-opus-5-5", reasoningOff));

        var backend = Config(backendId: "demo-backend");
        Assert.False(ModelPreferenceRules.Normalize(backend, "claude", preference).Reasoning.Enabled);
        Assert.False(ModelPreferenceRules.CreateDefault(backend, "claude", "claude-opus-5-5").Reasoning.Enabled);
        Assert.Null(ModelPreferenceRules.ValidateReasoning(backend, "claude", "claude-opus-5-5", reasoningOff));
    }

    private static AppConfig Config(string? backendId) => new()
    {
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
