using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class RemoteProviderManagementTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("remote-provider-").FullName;
    public void Dispose() => Directory.Delete(_root, true);
    [Theory]
    [InlineData("provider/create")]
    [InlineData("provider/update")]
    [InlineData("provider/delete")]
    [InlineData("auth/openai/login")]
    [InlineData("auth/openai/logout")]
    public async Task ManagedRuntimeRejectsUpstreamManagement(string method)
    {
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _root);
        harness.Monitor.Current.ModelService = new AppConfig.ModelServiceConfig
            { Endpoint = "https://models.example/model-service", Token = "client-test" };
        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, new { id = "primary", providerId = "primary" }));
        var response = Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        Assert.Equal(AppServerErrors.InvalidRequestCode, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ProviderListProjectsServiceOwnershipAndAuthenticationWithoutLocalKeys()
    {
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _root);
        var config = harness.Monitor.Current;
        config.ModelService = new AppConfig.ModelServiceConfig
            { Endpoint = "https://models.example/model-service", Token = "client-test" };
        config.Providers.Clear();
        config.Providers["primary"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            EndPoint = "https://upstream.example/v1",
            RemoteAuthentication = new ProviderAuthenticationStatus(true)
        };
        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest("provider/list", new { }));
        var response = Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("modelService", result.GetProperty("managedBy").GetString());
        var provider = Assert.Single(result.GetProperty("providers").EnumerateArray());
        Assert.True(provider.GetProperty("isAuthenticated").GetBoolean());
        Assert.False(provider.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("modelService", provider.GetProperty("managedBy").GetString());
    }
}
