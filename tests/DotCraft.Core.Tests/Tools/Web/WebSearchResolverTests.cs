using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tools.Web;
using Xunit;

namespace DotCraft.Tests.Tools.Web;

public sealed class WebSearchResolverTests
{
    [Theory]
    [InlineData("https://api.deepseek.com", true)]
    [InlineData("https://api.deepseek.com/v1/", true)]
    [InlineData("https://api.deepseek.com/anthropic", true)]
    [InlineData("https://api.deepseek.com/anthropic/v1", true)]
    [InlineData("https://api.deepseek.com/beta", true)]
    [InlineData("http://api.deepseek.com", false)]
    [InlineData("https://api.deepseek.com.example.com", false)]
    [InlineData("https://gateway.example/deepseek", false)]
    [InlineData("https://key@api.deepseek.com", false)]
    [InlineData("https://api.deepseek.com:444/v1", false)]
    public void AutoUsesOnlyOfficialCurrentProvider(string endpoint, bool supported)
    {
        var config = Config(endpoint);
        var result = WebSearchResolver.Resolve(config, ModelProviderResolver.ResolveProvider(config));
        Assert.Equal(supported ? "DeepSeek" : "Exa", result.Provider);
    }

    [Theory]
    [InlineData("Exa")]
    [InlineData("Bing")]
    public void ExplicitChoicesSurviveTheNewDefault(string choice)
    {
        var config = Config();
        config.Tools.Web.SearchProvider = choice;
        Assert.Equal(choice, WebSearchResolver.Resolve(config, ModelProviderResolver.ResolveProvider(config)).Provider);
    }

    [Fact]
    public void CurrentProviderWinsAndKeyUpdatesAreNotCopied()
    {
        var config = Config();
        config.Providers["second"] = new() { EndPoint = "https://api.deepseek.com/anthropic", ApiKey = "second-key", Protocol = "anthropic" };
        Assert.Equal("first-key", WebSearchResolver.Resolve(config, ModelProviderResolver.ResolveProvider(config)).ApiKey);
        Assert.Equal("second-key", WebSearchResolver.Resolve(config, ModelProviderResolver.ResolveProvider(config, "second")).ApiKey);
        config.Providers["first"].ApiKey = "updated";
        Assert.Equal("updated", WebSearchResolver.Resolve(config, ModelProviderResolver.ResolveProvider(config)).ApiKey);
        Assert.Empty(config.Tools.Web.DeepSeekApiKey);
    }

    [Fact]
    public void OtherModelsCanReuseTheOnlyProviderWithoutConfiguringAModel()
    {
        var config = Config();
        config.Tools.Web.SearchProvider = "DeepSeek";
        Assert.Equal("first-key", WebSearchResolver.Resolve(config).ApiKey);
        config.Providers["second"] = new() { EndPoint = "https://api.deepseek.com", ApiKey = "second-key" };
        Assert.Contains("Multiple", WebSearchResolver.Resolve(config).Error);
        config.Tools.Web.DeepSeekProvider = "second";
        Assert.Equal("second-key", WebSearchResolver.Resolve(config).ApiKey);
        config.Providers.Remove("second");
        Assert.Contains("not configured", WebSearchResolver.Resolve(config).Error);
    }

    [Fact]
    public void ExplicitUnauthenticatedAndUnsupportedProvidersDoNotFallBack()
    {
        var config = Config();
        config.Tools.Web.SearchProvider = "DeepSeek";
        config.Tools.Web.DeepSeekProvider = "first";
        config.Providers["first"].ApiKey = "";
        Assert.Contains("not authenticated", WebSearchResolver.Resolve(config).Error);
        config.Providers["first"].EndPoint = "https://gateway.example";
        Assert.Contains("does not support", WebSearchResolver.Resolve(config).Error);
    }

    [Fact]
    public void AutoSearchesWithTheConversationProviderRegardlessOfDeepSeekSettings()
    {
        var config = Config();
        config.Tools.Web.DeepSeekProvider = "deleted";
        config.Tools.Web.DeepSeekCredentialSource = "ApiKey";
        config.Tools.Web.DeepSeekApiKey = "independent-key";
        var target = WebSearchResolver.Resolve(config, ModelProviderResolver.ResolveProvider(config));
        Assert.Equal("DeepSeek", target.Provider);
        Assert.Null(target.Error);
        Assert.Equal("first-key", target.ApiKey);
    }

    [Fact]
    public void IndependentKeyModeHonorsConfigAndEnvironmentWithoutBorrowingProviderKeys()
    {
        var config = Config();
        config.Tools.Web.SearchProvider = "DeepSeek";
        config.Tools.Web.DeepSeekCredentialSource = "ApiKey";
        Assert.NotNull(WebSearchResolver.Resolve(config, environment: _ => null).Error);
        Assert.Equal("env-key", WebSearchResolver.Resolve(config, environment: _ => "env-key").ApiKey);
        config.Tools.Web.DeepSeekApiKey = " config-key ";
        Assert.Equal("config-key", WebSearchResolver.Resolve(config, environment: _ => "env-key").ApiKey);
    }

    [Theory]
    [InlineData(null, true, "Exa")]
    [InlineData("deepseek", false, "Exa")]
    [InlineData("deepseek", true, "DeepSeek")]
    public void RemoteSearchRequiresAdvertisedCapabilityAndAuthentication(string? backend, bool authenticated, string expected)
    {
        var config = Config();
        config.ModelService = new() { Endpoint = "https://service.example", Token = "caller-secret" };
        config.Providers["first"].RemoteAuthentication = new ProviderAuthenticationStatus(authenticated);
        config.Providers["first"].RemoteWebSearchBackend = backend;
        var result = WebSearchResolver.Resolve(config, ModelProviderResolver.ResolveProvider(config));
        Assert.Equal(expected, result.Provider);
        Assert.Null(result.ApiKey);
        if (expected == "DeepSeek") Assert.True(result.Runtime!.IsRemote);
    }

    [Fact]
    public void AutoDoesNotSelectDeepSeekJustBecauseAnotherProviderExists()
    {
        var config = Config();
        config.Providers["other"] = new() { EndPoint = "https://gateway.example", ApiKey = "other-key" };
        Assert.Equal("Exa", WebSearchResolver.Resolve(config, ModelProviderResolver.ResolveProvider(config, "other")).Provider);
    }

    private static AppConfig Config(string endpoint = "https://api.deepseek.com/v1") => new()
    {
        ProviderId = "first",
        Providers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["first"] = new() { DisplayName = "Renamed account", EndPoint = endpoint, ApiKey = "first-key" }
        }
    };
}
