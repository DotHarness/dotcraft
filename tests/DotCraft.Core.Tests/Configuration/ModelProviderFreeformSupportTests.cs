using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ModelProviderFreeformSupportTests
{
    [Theory]
    [InlineData(ModelProviderAuthMethods.ChatGptOAuth, "", true)]
    [InlineData(ModelProviderAuthMethods.ApiKey, "", true)]
    [InlineData(ModelProviderAuthMethods.ApiKey, "https://api.deepseek.com/v1", false)]
    public void DefaultFollowsAuthMethodAndEndpoint(string authMethod, string endpoint, bool expected)
    {
        var provider = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            AuthMethod = authMethod,
            ApiKey = "sk-test",
            EndPoint = endpoint
        };

        Assert.Equal(expected, Resolve(provider).SupportsFreeformTools);
        Assert.Equal(expected, ModelProviderResolver.ResolveFreeformToolSupport(provider));
    }

    [Theory]
    [InlineData("https://api.deepseek.com/v1", true)]
    [InlineData("", false)]
    public void ExplicitValueOverridesDefault(string endpoint, bool configured)
    {
        var provider = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            ApiKey = "sk-test",
            EndPoint = endpoint,
            SupportsFreeformTools = configured
        };

        Assert.Equal(configured, Resolve(provider).SupportsFreeformTools);
        Assert.Equal(configured, ModelProviderResolver.ResolveFreeformToolSupport(provider));
    }

    private static EffectiveModelRuntime Resolve(AppConfig.ModelProviderConfig provider) =>
        ModelProviderResolver.ResolveProvider(new AppConfig { Providers = { ["test"] = provider } }, "test");
}
