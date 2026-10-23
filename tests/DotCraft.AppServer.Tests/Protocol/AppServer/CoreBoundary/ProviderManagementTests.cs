using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DotCraft.Configuration;
using DotCraft.AppServer;
using Xunit;

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
        }
    }

    [Fact]
    public async Task ProviderList_ResolvesDefaultsFromStoredFieldsAndRedactsKeys()
    {
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(
            harness,
            """
            {
              "Providers": {
                "minimal": { "ApiKey": "sk-minimal" },
                "official-responses": { "Protocol": "OpenAI-Responses", "ApiKey": "sk-openai" },
                "custom-responses": { "Protocol": "openai-responses", "EndPoint": "https://openai-compatible.example/v1" },
                "responses-off": { "Protocol": "openai-responses", "SupportsImageGeneration": false },
                "anthropic-main": {
                  "DisplayName": "Anthropic Main",
                  "Protocol": "anthropic",
                  "ApiKey": "sk-ant-test",
                  "NetworkTimeoutSeconds": 180,
                  "MaxOutputTokens": 64000,
                  "StreamMaxRetries": 3,
                  "StreamIdleTimeoutMs": 120000
                }
              }
            }
            """);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ProviderList, new { }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain("sk-", response.RootElement.GetRawText());
        var providers = response.RootElement.GetProperty("result").GetProperty("providers").EnumerateArray()
            .ToDictionary(provider => provider.GetProperty("id").GetString()!);
        Assert.Equal(["anthropic-main", "custom-responses", "minimal", "official-responses", "responses-off"], providers.Keys.Order());

        var minimal = providers["minimal"];
        Assert.Equal("minimal", minimal.GetProperty("displayName").GetString());
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, minimal.GetProperty("protocol").GetString());
        Assert.Equal(ModelProviderAuthMethods.ApiKey, minimal.GetProperty("authMethod").GetString());
        Assert.True(minimal.GetProperty("supportsImageGeneration").GetBoolean());

        var official = providers["official-responses"];
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, official.GetProperty("protocol").GetString());
        Assert.True(official.GetProperty("capabilities").GetProperty("responsesApi").GetBoolean());
        Assert.True(official.GetProperty("capabilities").GetProperty("nativeDeferredToolLoading").GetBoolean());
        Assert.True(official.GetProperty("supportsImageGeneration").GetBoolean());
        Assert.False(providers["custom-responses"].GetProperty("supportsImageGeneration").GetBoolean());
        Assert.False(providers["responses-off"].GetProperty("supportsImageGeneration").GetBoolean());

        var anthropic = providers["anthropic-main"];
        Assert.True(anthropic.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal(180, anthropic.GetProperty("networkTimeoutSeconds").GetInt32());
        Assert.Equal(64000, anthropic.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(3, anthropic.GetProperty("streamMaxRetries").GetInt32());
        Assert.Equal(120000, anthropic.GetProperty("streamIdleTimeoutMs").GetInt32());
    }

    [Fact]
    public async Task ConfigBatchWrite_ProviderUpsert_KeepsMaskedKeyAndRemovesClearedFields()
    {
        var events = new List<AppConfigChangedEventArgs>();
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(
            harness,
            """
            {
              "Providers": {
                "Router": {
                  "DisplayName": "Router",
                  "EndPoint": "https://router.example/v1",
                  "ApiKey": "sk-router",
                  "SupportsImageGeneration": true
                }
              }
            }
            """);
        await harness.InitializeAsync();
        harness.Monitor.Changed += (_, change) => events.Add(change);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigBatchWrite,
            JsonNode.Parse("""
                {
                  "edits": [
                    {
                      "keyPath": "Providers.Router",
                      "value": { "ApiKey": "***", "DisplayName": null, "EndPoint": "https://openrouter.ai/api/v1", "StreamMaxRetries": 2 },
                      "mergeStrategy": "upsert"
                    }
                  ]
                }
                """)));
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigBatchWrite,
            JsonNode.Parse("""
                { "edits": [{ "keyPath": "Providers.Router.SupportsImageGeneration", "value": false, "mergeStrategy": "replace" }] }
                """)));

        var responses = await harness.Transport.WaitAndDrainAsync(2, TimeSpan.FromSeconds(5));
        Assert.All(responses, response => AssertSingleResult([response]));
        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        var persisted = personal.RootElement.GetProperty("Providers").GetProperty("Router");
        Assert.Equal("sk-router", persisted.GetProperty("ApiKey").GetString());
        Assert.False(persisted.TryGetProperty("DisplayName", out _));
        Assert.Equal("https://openrouter.ai/api/v1", persisted.GetProperty("EndPoint").GetString());
        Assert.Equal(2, persisted.GetProperty("StreamMaxRetries").GetInt32());
        Assert.False(persisted.GetProperty("SupportsImageGeneration").GetBoolean());
        Assert.Equal(2, events.Count);
        Assert.All(events, change => Assert.Equal(["Providers", ConfigChangeRegions.ProviderRegistry], change.Regions));
    }

    [Fact]
    public async Task ConfigBatchWrite_NullProviderEntry_DeletesItCaseInsensitively()
    {
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(harness, """{ "Theme": "dark", "Providers": { "old": { "ApiKey": "sk-old" } } }""");
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigBatchWrite,
            JsonNode.Parse("""{ "edits": [{ "keyPath": "Providers.OLD", "value": null, "mergeStrategy": "replace" }] }""")));

        AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        Assert.False(personal.RootElement.TryGetProperty("Providers", out _));
        Assert.Equal("dark", personal.RootElement.GetProperty("Theme").GetString());
        Assert.Empty(harness.Monitor.Current.Providers);
    }

    [Fact]
    public async Task ConfigBatchWrite_DeletingTheSelectedProvider_FailsAndKeepsTheFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json"), """{ "ProviderId": "anthropic-main" }""");
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await WritePersonalConfigAsync(harness, """{ "Providers": { "anthropic-main": { "Protocol": "anthropic", "ApiKey": "sk-ant-test" } } }""");
        await harness.InitializeAsync();
        var before = await File.ReadAllBytesAsync(harness.Monitor.Current.GlobalConfigPath!);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigBatchWrite,
            JsonNode.Parse("""{ "edits": [{ "keyPath": "Providers.anthropic-main", "value": null, "mergeStrategy": "replace" }] }""")));

        AppServerTestHarness.AssertConfigWriteError(
            Assert.Single(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5))),
            "configValidationError");
        Assert.Equal(before, await File.ReadAllBytesAsync(harness.Monitor.Current.GlobalConfigPath!));
    }

    [Fact]
    public async Task ConfigBatchWrite_ProviderEntry_CreatesItInTheUserLayerForProviderList()
    {
        var events = new List<AppConfigChangedEventArgs>();
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();
        harness.Monitor.Changed += (_, change) => events.Add(change);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigBatchWrite,
            JsonNode.Parse("""
                {
                  "edits": [
                    { "keyPath": "Providers.p1", "value": { "Protocol": "anthropic", "ApiKey": "sk-p1" }, "mergeStrategy": "replace" }
                  ]
                }
                """)));

        var written = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain("sk-p1", written.RootElement.GetRawText());
        var change = Assert.Single(events);
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigBatchWrite, change.Source);
        Assert.Equal(["Providers", ConfigChangeRegions.ProviderRegistry], change.Regions);
        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        Assert.Equal("sk-p1", personal.RootElement.GetProperty("Providers").GetProperty("p1").GetProperty("ApiKey").GetString());

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ProviderList, new { }));

        var listed = Assert.Single(
            AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)))
                .RootElement.GetProperty("result").GetProperty("providers").EnumerateArray());
        Assert.Equal("p1", listed.GetProperty("displayName").GetString());
        Assert.True(listed.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("sk-p1", harness.Monitor.Current.Providers["p1"].ApiKey);
    }

    [Fact]
    public async Task ConfigBatchWrite_SelectsProviderModelWithoutChangingProviderCredentials()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_workspaceCraftPath, "config.json"),
            """
            {
              "Theme": "dark"
            }
            """);
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
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

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.ConfigBatchWrite,
            System.Text.Json.Nodes.JsonNode.Parse(
                """
                {
                  "edits": [
                    { "keyPath": "ProviderId", "value": "anthropic-main", "mergeStrategy": "replace" },
                    { "keyPath": "ProviderPreferences.anthropic-main", "value": { "Model": "claude-sonnet-4-5" }, "mergeStrategy": "replace" }
                  ]
                }
                """)));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        Assert.Equal("ok", response.RootElement.GetProperty("result").GetProperty("status").GetString());

        var workspace = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json")));
        Assert.Equal("anthropic-main", workspace.RootElement.GetProperty("ProviderId").GetString());
        Assert.Equal("claude-sonnet-4-5", workspace.RootElement.GetProperty("ProviderPreferences").GetProperty("anthropic-main").GetProperty("Model").GetString());
        Assert.Equal("dark", workspace.RootElement.GetProperty("Theme").GetString());

        var personal = JsonDocument.Parse(await File.ReadAllTextAsync(harness.Monitor.Current.GlobalConfigPath!));
        var provider = personal.RootElement.GetProperty("Providers").GetProperty("anthropic-main");
        Assert.Equal("old-key", provider.GetProperty("ApiKey").GetString());
        Assert.False(provider.TryGetProperty("EndPoint", out _));
    }

    [Theory]
    [InlineData("openai-chat-completions", "/v1", """{ "object": "list", "data": [{ "id": "gpt-test", "object": "model", "created": 1778544000, "owned_by": "dotcraft-test" }] }""", "gpt-test")]
    [InlineData("anthropic", "", """{ "data": [{ "id": "claude-test", "display_name": "Claude Test", "created_at": "2026-05-12T00:00:00Z" }] }""", "claude-test")]
    public async Task ProviderTest_DraftProvider_ReturnsModelsAndDoesNotPersistDraft(
        string protocol,
        string endpointSuffix,
        string modelsJson,
        string expectedModel)
    {
        var (endpoint, serverTask) = await StartSingleJsonResponseServerAsync(modelsJson);
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ProviderTest, new
        {
            protocol,
            apiKey = "sk-draft-test",
            endPoint = endpoint + endpointSuffix,
            networkTimeoutSeconds = 5
        }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var result = response.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(protocol, result.GetProperty("protocol").GetString());
        Assert.Equal(expectedModel, result.GetProperty("models")[0].GetProperty("id").GetString());
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
        using var harness = new CoreAppServerTestHarness(workspaceCraftPath: _workspaceCraftPath);
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

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ProviderTest, new { providerId = "anthropic-main" }));

        var response = AssertSingleResult(await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5)));
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("anthropic-main", result.GetProperty("providerId").GetString());
        Assert.Equal("anthropic", result.GetProperty("protocol").GetString());
        Assert.Equal("MissingApiKey", result.GetProperty("errorCode").GetString());
    }

    private static async Task WritePersonalConfigAsync(CoreAppServerTestHarness harness, string json)
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
        CoreAppServerTestHarness.AssertIsSuccessResponse(response);
        return response;
    }
}
