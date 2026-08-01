using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ProviderManagementTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"provider_management_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public ProviderManagementTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
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
    public async Task ProviderCreate_WritesPersonalConfigRedactsSecretAndEmitsProviderRegistryRegion()
    {
        var events = new List<AppConfigChangedEventArgs>();
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        harness.Monitor.Changed += OnChanged;
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderCreate, new
        {
            id = "anthropic-main",
            displayName = "Anthropic Main",
            protocol = "anthropic",
            apiKey = "sk-ant-test",
            endPoint = "https://api.anthropic.com",
            networkTimeoutSeconds = 180,
            maxOutputTokens = 64000,
            streamMaxRetries = 3,
            streamIdleTimeoutMs = 120000
        }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var provider = response.RootElement.GetProperty("result").GetProperty("provider");
        Assert.Equal("anthropic-main", provider.GetProperty("id").GetString());
        Assert.Equal("Anthropic Main", provider.GetProperty("displayName").GetString());
        Assert.Equal("anthropic", provider.GetProperty("protocol").GetString());
        Assert.Equal("********", provider.GetProperty("apiKey").GetString());
        Assert.True(provider.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("https://api.anthropic.com", provider.GetProperty("endPoint").GetString());
        Assert.Equal(180, provider.GetProperty("networkTimeoutSeconds").GetInt32());
        Assert.Equal(64000, provider.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(3, provider.GetProperty("streamMaxRetries").GetInt32());
        Assert.Equal(120000, provider.GetProperty("streamIdleTimeoutMs").GetInt32());
        Assert.False(provider.GetProperty("capabilities").GetProperty("rawMetadataPassthrough").GetBoolean());

        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        var persisted = personal.RootElement.GetProperty("Providers").GetProperty("anthropic-main");
        Assert.Equal("sk-ant-test", persisted.GetProperty("ApiKey").GetString());
        Assert.Equal("anthropic", persisted.GetProperty("Protocol").GetString());
        Assert.Equal(64000, persisted.GetProperty("MaxOutputTokens").GetInt32());
        Assert.Equal(3, persisted.GetProperty("StreamMaxRetries").GetInt32());
        Assert.Equal(120000, persisted.GetProperty("StreamIdleTimeoutMs").GetInt32());

        var change = Assert.Single(events);
        Assert.Equal(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderCreate, change.Source);
        Assert.Contains(ConfigChangeRegions.ProviderRegistry, change.Regions);
        harness.Monitor.Changed -= OnChanged;

        void OnChanged(object? sender, AppConfigChangedEventArgs change) => events.Add(change);
    }

    [Fact]
    public async Task ProviderCreate_AllowsOpenAiProviderWithoutEndpoint()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderCreate, new
        {
            id = "openai-api",
            displayName = "OpenAI",
            protocol = "openai-chat-completions",
            apiKey = "sk-openai",
            endPoint = ""
        }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var provider = response.RootElement.GetProperty("result").GetProperty("provider");
        Assert.Equal("openai-api", provider.GetProperty("id").GetString());
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, provider.GetProperty("protocol").GetString());
        Assert.Equal("", provider.GetProperty("endPoint").GetString());

        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        var persisted = personal.RootElement.GetProperty("Providers").GetProperty("openai-api");
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, persisted.GetProperty("Protocol").GetString());
        Assert.False(persisted.TryGetProperty("EndPoint", out _));
    }

    [Fact]
    public async Task ProviderCreate_OpenAIResponsesReportsNativeDeferredCapability()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderCreate, new
        {
            id = "openai-responses",
            displayName = "OpenAI Responses",
            protocol = ModelProviderProtocols.OpenAIResponses,
            apiKey = "sk-openai",
            endPoint = ""
        }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var provider = response.RootElement.GetProperty("result").GetProperty("provider");
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, provider.GetProperty("protocol").GetString());
        Assert.True(provider.GetProperty("capabilities").GetProperty("responsesApi").GetBoolean());
        Assert.True(provider.GetProperty("capabilities").GetProperty("nativeDeferredToolLoading").GetBoolean());
        Assert.True(provider.GetProperty("supportsHostedImageGeneration").GetBoolean());

        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        var persisted = personal.RootElement.GetProperty("Providers").GetProperty("openai-responses");
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, persisted.GetProperty("Protocol").GetString());
        Assert.True(persisted.GetProperty("SupportsHostedImageGeneration").GetBoolean());
    }

    [Fact]
    public async Task ProviderCreate_CustomResponsesEndpointDefaultsHostedImageGenerationOff()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderCreate, new
        {
            id = "custom-responses",
            displayName = "Custom Responses",
            protocol = ModelProviderProtocols.OpenAIResponses,
            apiKey = "sk-openai",
            endPoint = "https://openai-compatible.example/v1"
        }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var provider = response.RootElement.GetProperty("result").GetProperty("provider");
        Assert.False(provider.GetProperty("supportsHostedImageGeneration").GetBoolean());

        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        var persisted = personal.RootElement.GetProperty("Providers").GetProperty("custom-responses");
        Assert.False(persisted.GetProperty("SupportsHostedImageGeneration").GetBoolean());
    }

    [Fact]
    public async Task ProviderCreate_PersistsHostedImageGenerationOverride()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderCreate, new
        {
            id = "responses-on",
            displayName = "Responses On",
            protocol = ModelProviderProtocols.OpenAIResponses,
            apiKey = "sk-openai",
            supportsHostedImageGeneration = true
        }));

        var enabledResponse = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var enabledProvider = enabledResponse.RootElement.GetProperty("result").GetProperty("provider");
        Assert.True(enabledProvider.GetProperty("supportsHostedImageGeneration").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderCreate, new
        {
            id = "responses-off",
            displayName = "Responses Off",
            protocol = ModelProviderProtocols.OpenAIResponses,
            apiKey = "sk-openai",
            supportsHostedImageGeneration = false
        }));

        var disabledResponse = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var disabledProvider = disabledResponse.RootElement.GetProperty("result").GetProperty("provider");
        Assert.False(disabledProvider.GetProperty("supportsHostedImageGeneration").GetBoolean());

        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        var providers = personal.RootElement.GetProperty("Providers");
        Assert.True(providers.GetProperty("responses-on").GetProperty("SupportsHostedImageGeneration").GetBoolean());
        Assert.False(providers.GetProperty("responses-off").GetProperty("SupportsHostedImageGeneration").GetBoolean());
    }

    [Fact]
    public async Task ProviderUpdate_PreservesHostedImageGenerationAndRejectsNull()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(
            harness,
            """
            {
              "Providers": {
                "openai-responses": {
                  "DisplayName": "OpenAI Responses",
                  "Protocol": "openai-responses",
                  "ApiKey": "old-key",
                  "SupportsHostedImageGeneration": true
                }
              }
            }
            """);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderUpdate, new
        {
            id = "openai-responses",
            displayName = "Renamed Responses"
        }));

        var preservedResponse = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var preservedProvider = preservedResponse.RootElement.GetProperty("result").GetProperty("provider");
        Assert.True(preservedProvider.GetProperty("supportsHostedImageGeneration").GetBoolean());

        var personalAfterPreserve = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        Assert.True(personalAfterPreserve.RootElement
            .GetProperty("Providers")
            .GetProperty("openai-responses")
            .GetProperty("SupportsHostedImageGeneration")
            .GetBoolean());

        var nullParams = new JsonObject
        {
            ["id"] = "openai-responses",
            ["supportsHostedImageGeneration"] = null
        };
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderUpdate, nullParams));

        var nullResponse = Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        AppServerTestHarness.AssertIsErrorResponse(nullResponse, AppServerErrors.InvalidParamsCode);

        var personalAfterNull = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        Assert.True(personalAfterNull.RootElement
            .GetProperty("Providers")
            .GetProperty("openai-responses")
            .GetProperty("SupportsHostedImageGeneration")
            .GetBoolean());
    }

    [Fact]
    public async Task ProviderUpdate_UpdatesPersonalProviderAndRedactsResponse()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(
            harness,
            """
            {
              "Providers": {
                "openrouter": {
                  "DisplayName": "Old Router",
                  "Protocol": "openai-chat-completions",
                  "ApiKey": "old-key",
                  "EndPoint": "https://old.example/v1"
                }
              }
            }
            """);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderUpdate, new
        {
            id = "openrouter",
            displayName = "OpenRouter",
            apiKey = "new-key",
            endPoint = "https://openrouter.ai/api/v1",
            networkTimeoutSeconds = 90,
            maxOutputTokens = 4096,
            streamMaxRetries = 2,
            streamIdleTimeoutMs = 90000
        }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var provider = response.RootElement.GetProperty("result").GetProperty("provider");
        Assert.Equal("OpenRouter", provider.GetProperty("displayName").GetString());
        Assert.Equal("********", provider.GetProperty("apiKey").GetString());
        Assert.Equal("https://openrouter.ai/api/v1", provider.GetProperty("endPoint").GetString());
        Assert.Equal(90, provider.GetProperty("networkTimeoutSeconds").GetInt32());
        Assert.Equal(4096, provider.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(2, provider.GetProperty("streamMaxRetries").GetInt32());
        Assert.Equal(90000, provider.GetProperty("streamIdleTimeoutMs").GetInt32());

        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        var persisted = personal.RootElement.GetProperty("Providers").GetProperty("openrouter");
        Assert.Equal("new-key", persisted.GetProperty("ApiKey").GetString());
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, persisted.GetProperty("Protocol").GetString());
        Assert.Equal("https://openrouter.ai/api/v1", persisted.GetProperty("EndPoint").GetString());
        Assert.Equal(4096, persisted.GetProperty("MaxOutputTokens").GetInt32());
        Assert.Equal(2, persisted.GetProperty("StreamMaxRetries").GetInt32());
        Assert.Equal(90000, persisted.GetProperty("StreamIdleTimeoutMs").GetInt32());
    }

    [Fact]
    public async Task ProviderDelete_RefusesProviderSelectedByWorkspace()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_workspaceCraftPath, "config.json"),
            """
            {
              "ProviderId": "anthropic-main",
              "Model": "claude-sonnet-4-5"
            }
            """);
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(
            harness,
            """
            {
              "Providers": {
                "anthropic-main": {
                  "Protocol": "anthropic",
                  "ApiKey": "sk-ant-test"
                }
              }
            }
            """);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderDelete, new { id = "anthropic-main" }));

        var response = Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_SelectsProviderModelWithoutChangingProviderCredentials()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_workspaceCraftPath, "config.json"),
            """
            {
              "Theme": "dark"
            }
            """);
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(
            harness,
            """
            {
              "Providers": {
                "anthropic-main": {
                  "Protocol": "anthropic",
                  "ApiKey": "old-key"
                }
              }
            }
            """);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new
        {
            providerId = "anthropic-main",
            providerPreferences = new Dictionary<string, ModelPreference>
            {
                ["anthropic-main"] = ModelPreferenceRules.CreateManual("claude-sonnet-4-5")
            }
        }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var result = response.RootElement.GetProperty("result");
        Assert.Equal("anthropic-main", result.GetProperty("providerId").GetString());
        Assert.Equal("claude-sonnet-4-5", result.GetProperty("providerPreferences").GetProperty("anthropic-main").GetProperty("model").GetString());
        Assert.False(result.TryGetProperty("apiKey", out _));
        Assert.False(result.TryGetProperty("endPoint", out _));

        var workspace = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json")));
        Assert.Equal("anthropic-main", workspace.RootElement.GetProperty("ProviderId").GetString());
        Assert.Equal("claude-sonnet-4-5", workspace.RootElement.GetProperty("ProviderPreferences").GetProperty("anthropic-main").GetProperty("Model").GetString());
        Assert.Equal("dark", workspace.RootElement.GetProperty("Theme").GetString());

        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        var provider = personal.RootElement.GetProperty("Providers").GetProperty("anthropic-main");
        Assert.Equal("old-key", provider.GetProperty("ApiKey").GetString());
        Assert.False(provider.TryGetProperty("EndPoint", out _));
    }


    [Fact]
    public async Task ProviderList_IncludesExplicitProvidersOnly()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(
            harness,
            """
            {
              "Providers": {
                "anthropic-main": {
                  "DisplayName": "Anthropic Main",
                  "Protocol": "anthropic",
                  "ApiKey": "sk-ant-test"
                }
              }
            }
            """);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderList, new { }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var providers = response.RootElement.GetProperty("result").GetProperty("providers").EnumerateArray().ToList();
        var explicitProvider = Assert.Single(providers);
        Assert.Equal("anthropic-main", explicitProvider.GetProperty("id").GetString());
        Assert.False(explicitProvider.GetProperty("isImplicit").GetBoolean());
        Assert.Equal("Anthropic Main", explicitProvider.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task ProviderTest_DraftOpenAICompatibleProvider_ReturnsModelsAndDoesNotPersistDraft()
    {
        var (endpoint, serverTask) = await StartSingleJsonResponseServerAsync(
            """
            {
              "object": "list",
              "data": [
                {
                  "id": "gpt-test",
                  "object": "model",
                  "created": 1778544000,
                  "owned_by": "dotcraft-test"
                }
              ]
            }
            """);
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderTest, new
        {
            protocol = "openai-chat-completions",
            apiKey = "sk-openai-test",
            endPoint = $"{endpoint}/v1",
            networkTimeoutSeconds = 5
        }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, result.GetProperty("protocol").GetString());
        Assert.Equal("gpt-test", result.GetProperty("models")[0].GetProperty("id").GetString());
        Assert.False(result.TryGetProperty("providerId", out _));

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        if (File.Exists(harness.Monitor.Current.GlobalConfigPath!))
        {
            var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
            Assert.False(personal.RootElement.TryGetProperty("Providers", out _));
        }
    }

    [Fact]
    public async Task ProviderTest_DraftAnthropicProvider_ReturnsModelsAndDoesNotPersistDraft()
    {
        var (endpoint, serverTask) = await StartSingleJsonResponseServerAsync(
            """
            {
              "data": [
                {
                  "id": "claude-test",
                  "display_name": "Claude Test",
                  "created_at": "2026-05-12T00:00:00Z"
                }
              ]
            }
            """);
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderTest, new
        {
            protocol = "anthropic",
            apiKey = "sk-ant-test",
            endPoint = endpoint,
            networkTimeoutSeconds = 5
        }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("anthropic", result.GetProperty("protocol").GetString());
        Assert.Equal("claude-test", result.GetProperty("models")[0].GetProperty("id").GetString());
        Assert.False(result.TryGetProperty("providerId", out _));

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        if (File.Exists(harness.Monitor.Current.GlobalConfigPath!))
        {
            var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
            Assert.False(personal.RootElement.TryGetProperty("Providers", out _));
        }
    }

    [Fact]
    public async Task ProviderTest_PersistedProviderMapsConfigurationErrors()
    {
        using var harness = new AppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(
            harness,
            """
            {
              "Providers": {
                "anthropic-main": {
                  "Protocol": "anthropic"
                }
              }
            }
            """);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProviderTest, new { providerId = "anthropic-main" }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("anthropic-main", result.GetProperty("providerId").GetString());
        Assert.Equal("anthropic", result.GetProperty("protocol").GetString());
        Assert.Equal("MissingApiKey", result.GetProperty("errorCode").GetString());
    }

    private static async Task WritePersonalConfigAsync(AppServerTestHarness harness, string json)
    {
        var path = harness.Monitor.Current.GlobalConfigPath!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json);
    }

    private static async Task<(string Endpoint, Task ServerTask)> StartSingleJsonResponseServerAsync(string json)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var serverTask = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                var request = new StringBuilder();
                while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read <= 0)
                        break;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var body = Encoding.UTF8.GetBytes(json);
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: application/json\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(body);
            }
            finally
            {
                listener.Stop();
            }
        });

        return ($"http://127.0.0.1:{endpoint.Port}", serverTask);
    }

    private static JsonDocument AssertSingleResult(IReadOnlyList<JsonDocument> sent)
    {
        var response = Assert.Single(sent);
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response;
    }
}
