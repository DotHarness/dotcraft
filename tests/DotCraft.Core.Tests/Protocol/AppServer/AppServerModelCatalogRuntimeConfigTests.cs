using System.Net;
using System.Text;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerModelCatalogRuntimeConfigTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"model_catalog_runtime_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public AppServerModelCatalogRuntimeConfigTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
    }

    [Fact]
    public async Task ModelList_ReturnsChatGptOAuthRemoteCatalog()
    {
        var monitor = new AppConfigMonitor(new AppConfig
        {
            GlobalConfigPath = Path.Combine(_tempRoot, "global-oauth", "config.json"),
            WorkspaceConfigPath = Path.Combine(_workspaceCraftPath, "config.json"),
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = ModelProviderDefaults.DefaultChatGptCodexModel  } }
        });
        monitor.Current.Providers["openai"] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "OpenAI (ChatGPT)",
            Protocol = ModelProviderProtocols.OpenAIResponses,
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth,
            ChatGptAccountId = "acct_test"
        };
        var handler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "appserver-remote", "visibility": "list", "priority": 1, "minimal_client_version": "0.98.0" }
              ]
            }
            """));
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            appConfigMonitor: monitor,
            openAIClientProvider: new OpenAIClientProvider(new FakeOpenAIAuthService(), handler));
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ModelList, new { }));

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent);
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("success").GetBoolean());
        var model = Assert.Single(result.GetProperty("models").EnumerateArray());
        Assert.Equal("appserver-remote", model.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ModelList_ReturnsContextWindowMetadata()
    {
        var monitor = new AppConfigMonitor(new AppConfig
        {
            GlobalConfigPath = Path.Combine(_tempRoot, "global-context", "config.json"),
            WorkspaceConfigPath = Path.Combine(_workspaceCraftPath, "config.json"),
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = ModelProviderDefaults.DefaultChatGptCodexModel  } }
        });
        monitor.Current.Providers["openai"] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "OpenAI (ChatGPT)",
            Protocol = ModelProviderProtocols.OpenAIResponses,
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth,
            ChatGptAccountId = "acct_test"
        };
        var handler = new RecordingHandler((HttpStatusCode.OK, """
            {
              "models": [
                { "slug": "gpt-5.5", "visibility": "list", "priority": 1, "minimal_client_version": "0.98.0" }
              ]
            }
            """));
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            appConfigMonitor: monitor,
            openAIClientProvider: new OpenAIClientProvider(new FakeOpenAIAuthService(), handler));
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ModelList, new { }));

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent);
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("success").GetBoolean());
        var model = Assert.Single(result.GetProperty("models").EnumerateArray());
        var contextWindow = model.GetProperty("contextWindow");
        Assert.Equal(1_050_000, contextWindow.GetProperty("catalogWindow").GetInt32());
        Assert.Equal(256_000, contextWindow.GetProperty("configuredWindow").GetInt32());
        Assert.True(contextWindow.GetProperty("supportsMax").GetBoolean());
        Assert.Equal(1_050_000, contextWindow.GetProperty("maxWindow").GetInt32());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task ModelList_UsesRuntimeConfigMonitorInsteadOfReloadingWorkspaceConfig()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_workspaceCraftPath, "config.json"),
            """
            {
              "ApiKey": "disk-key",
              "EndPoint": "not-a-url"
            }
            """);

        var monitor = new AppConfigMonitor(new AppConfig
        {
            GlobalConfigPath = Path.Combine(_tempRoot, "global", "config.json"),
            WorkspaceConfigPath = Path.Combine(_workspaceCraftPath, "config.json")
        });
        monitor.Current.ProviderId = "openai";
        monitor.Current.Providers["openai"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAI,
            ApiKey = "",
            EndPoint = "http://127.0.0.1:8317/v1"
        };
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            appConfigMonitor: monitor);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ModelList, new { }));

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent);
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("MissingApiKey", result.GetProperty("errorCode").GetString());
        Assert.Contains("http://127.0.0.1:8317/v1", result.GetProperty("errorMessage").GetString());
    }

    private sealed class FakeOpenAIAuthService : IOpenAIAuthService
    {
        public bool IsAuthenticated => true;

        public event Action<OpenAIAuthStatus>? LoggedIn
        {
            add { }
            remove { }
        }

        public event Action? LoggedOut
        {
            add { }
            remove { }
        }

        public OpenAIAuthStatus GetStatus() => new(
            true,
            "acct_test",
            "pro",
            "test@example.com",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

        public Task<OpenAIAuthStatus> LoginAsync(
            bool openBrowser,
            Action<string>? onAuthorizationUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(GetStatus());

        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult("access-token");

        public string? GetAccountId() => "acct_test";
    }

    private sealed class RecordingHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new HttpRequestException("No test response queued.");

            var (status, body) = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

}
