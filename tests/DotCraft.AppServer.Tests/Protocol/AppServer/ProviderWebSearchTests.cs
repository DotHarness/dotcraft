using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ProviderWebSearchTests
{
    [Fact]
    public void ProviderListAdvertisesCapabilitiesWithoutCopyingSecrets()
    {
        var provider = new AppConfig.ModelProviderConfig
        {
            EndPoint = "https://api.deepseek.com/anthropic",
            Protocol = "anthropic",
            ApiKey = "provider-secret"
        };
        var info = ProviderContractMapper.BuildProviderInfo("renamed", provider);
        Assert.Equal("deepseek", info.WebSearchBackend.Value);
        Assert.True(info.HasApiKey.Value);
        Assert.DoesNotContain("provider-secret", JsonSerializer.Serialize(info));
        provider.EndPoint = "https://third-party.example/v1";
        Assert.False(ProviderContractMapper.BuildProviderInfo("deepseek", provider).WebSearchBackend.IsSet);
    }

    [Fact]
    public void RemoteProvidersRequireServiceMetadataEvenWhenTheirEndpointIsOfficial()
    {
        var provider = new AppConfig.ModelProviderConfig
        {
            EndPoint = "https://api.deepseek.com/v1",
            RemoteAuthentication = new ProviderAuthenticationStatus(true)
        };
        Assert.False(ProviderContractMapper.BuildProviderInfo("remote", provider).WebSearchBackend.IsSet);
        provider.RemoteWebSearchBackend = "deepseek";
        var info = ProviderContractMapper.BuildProviderInfo("remote", provider);
        Assert.Equal("deepseek", info.WebSearchBackend.Value);
        Assert.False(info.HasApiKey.Value);
        Assert.True(info.IsAuthenticated.Value);
    }
}
