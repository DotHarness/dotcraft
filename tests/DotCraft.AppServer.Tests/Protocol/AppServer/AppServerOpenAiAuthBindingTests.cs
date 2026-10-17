using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using Xunit;
using Methods = DotCraft.Protocol.AppServer.AppServerMethodNames;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerOpenAiAuthBindingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("openai-auth-binding-").FullName;
    private readonly List<AppConfigChangedEventArgs> _events = [];

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task Login_BindsAndSelectsTheProviderThroughConfiguration()
    {
        using var harness = await StartAsync(null);

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.AuthOpenAiLogin, new { openBrowser = false }));

        var response = Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        var user = ReadUser(harness);
        var provider = user["Providers"]!["openai"]!;
        Assert.Equal(ModelProviderAuthMethods.ChatGptOAuth, provider["AuthMethod"]!.GetValue<string>());
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, provider["Protocol"]!.GetValue<string>());
        Assert.Equal("acct_test", provider["ChatGptAccountId"]!.GetValue<string>());
        Assert.Equal("pro", provider["ChatGptPlanType"]!.GetValue<string>());
        Assert.Equal("openai", user["ProviderId"]!.GetValue<string>());
        Assert.Equal(ModelProviderDefaults.DefaultChatGptCodexModel, user["ProviderPreferences"]!["openai"]!["Model"]!.GetValue<string>());
        Assert.Equal(ModelProviderAuthMethods.ChatGptOAuth, harness.Monitor.Current.Providers["openai"].AuthMethod);
        var change = Assert.Single(_events);
        Assert.Equal(Methods.AuthOpenAiLogin, change.Source);
        Assert.Contains("Providers", change.Regions);
        Assert.Contains(ConfigChangeRegions.ProviderRegistry, change.Regions);
    }

    [Fact]
    public async Task Login_KeepsAnExistingUserSelection()
    {
        using var harness = await StartAsync("""{ "ProviderId": "work", "Providers": { "work": { "ApiKey": "sk-work" } } }""");

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.AuthOpenAiLogin, new { openBrowser = false }));

        CoreAppServerTestHarness.AssertIsSuccessResponse(Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5))));
        var user = ReadUser(harness);
        Assert.Equal("work", user["ProviderId"]!.GetValue<string>());
        Assert.Equal("sk-work", user["Providers"]!["work"]!["ApiKey"]!.GetValue<string>());
        Assert.NotNull(user["Providers"]!["openai"]);
    }

    [Fact]
    public async Task Login_RejectedBindingReportsInvalidRequestAndLeavesTheFileUnchanged()
    {
        using var harness = await StartAsync("""{ "Theme": "dark" }""");
        var before = await File.ReadAllBytesAsync(harness.Monitor.Current.GlobalConfigPath!);

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.AuthOpenAiLogin, new { openBrowser = false, providerId = "a:b" }));

        var response = Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        CoreAppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidRequestCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(harness.Monitor.Current.GlobalConfigPath!));
        Assert.Empty(_events);
    }

    [Fact]
    public async Task Logout_UnbindsTheProviderThroughConfiguration()
    {
        using var harness = await StartAsync("""
            {
              "ProviderId": "openai",
              "Providers": {
                "openai": { "AuthMethod": "chatgptOAuth", "Protocol": "openai-responses", "ChatGptAccountId": "acct", "ChatGptPlanType": "pro" }
              }
            }
            """);

        await harness.ExecuteRequestAsync(harness.BuildRequest(Methods.AuthOpenAiLogout, new { }));

        CoreAppServerTestHarness.AssertIsSuccessResponse(Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5))));
        var user = ReadUser(harness);
        var provider = user["Providers"]!["openai"]!.AsObject();
        Assert.Equal(ModelProviderAuthMethods.ApiKey, provider["AuthMethod"]!.GetValue<string>());
        Assert.False(provider.ContainsKey("ChatGptAccountId"));
        Assert.False(provider.ContainsKey("ChatGptPlanType"));
        Assert.Equal("openai", user["ProviderId"]!.GetValue<string>());
        var change = Assert.Single(_events);
        Assert.Equal(Methods.AuthOpenAiLogout, change.Source);
        Assert.Equal(["Providers", ConfigChangeRegions.ProviderRegistry], change.Regions);
    }

    private async Task<CoreAppServerTestHarness> StartAsync(string? userConfig)
    {
        var harness = new CoreAppServerTestHarness(
            workspaceCraftPath: Path.Combine(_root, ".craft"),
            openAIClientProvider: new OpenAIClientProvider(new AppServerOpenAiAuthStatusTests.FakeOpenAIAuthService()));
        if (userConfig != null)
        {
            var path = harness.Monitor.Current.GlobalConfigPath!;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, userConfig);
        }

        await harness.InitializeAsync();
        harness.Monitor.Changed += (_, change) => _events.Add(change);
        return harness;
    }

    private static JsonObject ReadUser(CoreAppServerTestHarness harness) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(harness.Monitor.Current.GlobalConfigPath!))!;
}
