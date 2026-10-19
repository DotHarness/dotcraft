using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class RemoteProviderManagementTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("remote-provider-").FullName;
    public void Dispose() => Directory.Delete(_root, true);
    [Fact]
    public async Task ConfigWritesCannotEditOrReplaceServiceManagedProviders()
    {
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _root);
        var config = harness.Monitor.Current;
        config.ModelService = new AppConfig.ModelServiceConfig
            { Endpoint = "https://models.example/model-service", Token = "client-test" };
        var managed = new AppConfig.ModelProviderConfig { Protocol = ModelProviderProtocols.OpenAIResponses };
        config.Providers = new(StringComparer.OrdinalIgnoreCase) { ["primary"] = managed };
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest("config/batchWrite", new
        {
            edits = new[] { new { keyPath = "Providers.local", value = new { ApiKey = "sk-local" }, mergeStrategy = "replace" } }
        }));
        await harness.ExecuteRequestAsync(harness.BuildRequest("config/batchWrite", new
        {
            edits = new object[]
            {
                new { keyPath = "ProviderId", value = "primary", mergeStrategy = "replace" },
                new { keyPath = "InstantInterruptEnabled", value = false, mergeStrategy = "replace" }
            }
        }));

        var responses = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        AppServerTestHarness.AssertConfigWriteError(responses[0], "configValidationError");
        CoreAppServerTestHarness.AssertIsSuccessResponse(responses[1]);
        Assert.Same(managed, Assert.Single(config.Providers).Value);
        Assert.Equal("primary", config.ProviderId);
        Assert.False(File.Exists(config.GlobalConfigPath));
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
