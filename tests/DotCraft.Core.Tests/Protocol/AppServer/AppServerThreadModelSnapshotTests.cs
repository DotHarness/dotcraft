using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerThreadModelSnapshotTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _craftPath;

    public AppServerThreadModelSnapshotTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AppServerModelSnapshot_" + Guid.NewGuid().ToString("N")[..8]);
        _craftPath = Path.Combine(_tempDir, ".craft");
        Directory.CreateDirectory(_craftPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task WorkspaceModelUpdate_DoesNotChangeExistingThreadModel()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_craftPath, "config.json"),
            """
            {
              "ProviderId": "openai",
              "Providers": {
                "openai": {
                  "Protocol": "openai",
                  "ApiKey": "sk-test-not-used-for-network",
                  "EndPoint": "https://127.0.0.1:9/v1"
                }
              },
              "ProviderModels": { "openai": "model-a" },
              "Speed": "Fast"
            }
            """);

        var config = AppConfigTestFactory.CreateOpenAI(model: "model-a");
        config.Speed = InferenceSpeed.Fast;
        config.GlobalConfigPath = Path.Combine(_tempDir, "global", "config.json");
        var monitor = new AppConfigMonitor(config);
        await using var agentFactory = CreateAgentFactory(config);
        var service = new SessionService(
            agentFactory,
            agentFactory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            appConfigMonitor: monitor);
        var transport = new InMemoryTransport();
        var connection = new AppServerConnection();
        var handler = new AppServerRequestHandler(
            service,
            connection,
            transport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                WorkspaceCraftPath = _craftPath,
                HostWorkspacePath = _tempDir,
                MemoryStore = new MemoryStore(_tempDir),
                AppConfigMonitor = monitor,
                SkillsLoader = new SkillsLoader(_tempDir),
            });

        await InitializeAsync(handler, transport);

        var firstThreadId = await StartThreadAndAssertModelAsync(
            handler,
            transport,
            requestId: 10,
            "model-a",
            expectedSpeed: "fast");

        var update = InMemoryTransport.BuildRequest(
            AppServerMethods.WorkspaceConfigUpdate,
            new { providerModels = new Dictionary<string, string> { ["openai"] = "model-b" } },
            id: 11);
        await ExecuteRequestAsync(handler, transport, update);
        await ReadResponseForIdAsync(transport, 11);

        var oldThreadRead = InMemoryTransport.BuildRequest(
            AppServerMethods.ThreadRead,
            new { threadId = firstThreadId, includeTurns = false },
            id: 12);
        await ExecuteRequestAsync(handler, transport, oldThreadRead);
        var oldThreadReadResponse = await ReadResponseForIdAsync(transport, 12);
        var oldThread = oldThreadReadResponse.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal("model-a", GetThreadModel(oldThread));

        await StartThreadAndAssertModelAsync(handler, transport, requestId: 13, "model-b");

        await StartThreadAndAssertModelAsync(
            handler,
            transport,
            requestId: 14,
            "model-c",
            config: new { model = "model-c" });
    }

    [Fact]
    public async Task WorkspaceProviderUpdate_DoesNotChangeExistingThreadProvider()
    {
        var globalConfigPath = Path.Combine(_tempDir, "global", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(globalConfigPath)!);
        await File.WriteAllTextAsync(
            globalConfigPath,
            """
            {
              "Providers": {
                "anthropic-main": {
                  "Protocol": "anthropic",
                  "ApiKey": "sk-ant-test"
                },
                "openrouter": {
                  "Protocol": "openai",
                  "ApiKey": "sk-router-test",
                  "EndPoint": "https://openrouter.ai/api/v1"
                }
              }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(_craftPath, "config.json"),
            """
            {
              "ProviderId": "anthropic-main",
              "ProviderModels": { "anthropic-main": "claude-sonnet-4-5" }
            }
            """);

        var config = AppConfig.LoadWithGlobalFallback(Path.Combine(_craftPath, "config.json"), globalConfigPath);
        var monitor = new AppConfigMonitor(config);
        await using var agentFactory = CreateAgentFactory(config);
        var service = new SessionService(
            agentFactory,
            agentFactory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            appConfigMonitor: monitor);
        var transport = new InMemoryTransport();
        var connection = new AppServerConnection();
        var handler = new AppServerRequestHandler(
            service,
            connection,
            transport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry(), null, null),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                WorkspaceCraftPath = _craftPath,
                HostWorkspacePath = _tempDir,
                MemoryStore = new MemoryStore(_tempDir),
                AppConfigMonitor = monitor,
                SkillsLoader = new SkillsLoader(_tempDir),
            });

        await InitializeAsync(handler, transport);

        var firstThreadId = await StartThreadAndAssertModelAsync(
            handler,
            transport,
            requestId: 20,
            expectedModel: "claude-sonnet-4-5",
            expectedProviderId: "anthropic-main");

        var update = InMemoryTransport.BuildRequest(
            AppServerMethods.WorkspaceConfigUpdate,
            new
            {
                providerId = "openrouter",
                providerModels = new Dictionary<string, string>
                {
                    ["anthropic-main"] = "claude-sonnet-4-5",
                    ["openrouter"] = "openrouter-model"
                }
            },
            id: 21);
        await ExecuteRequestAsync(handler, transport, update);
        await ReadResponseForIdAsync(transport, 21);

        var oldThreadRead = InMemoryTransport.BuildRequest(
            AppServerMethods.ThreadRead,
            new { threadId = firstThreadId, includeTurns = false },
            id: 22);
        await ExecuteRequestAsync(handler, transport, oldThreadRead);
        var oldThreadReadResponse = await ReadResponseForIdAsync(transport, 22);
        var oldThread = oldThreadReadResponse.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal("anthropic-main", GetThreadProviderId(oldThread));
        Assert.Equal("claude-sonnet-4-5", GetThreadModel(oldThread));

        await StartThreadAndAssertModelAsync(
            handler,
            transport,
            requestId: 23,
            expectedModel: "openrouter-model",
            expectedProviderId: "openrouter");

        await StartThreadAndAssertModelAsync(
            handler,
            transport,
            requestId: 24,
            expectedModel: "claude-opus-4-1",
            expectedProviderId: "anthropic-main",
            config: new { providerId = "anthropic-main", model = "claude-opus-4-1" });
    }

    private AgentFactory CreateAgentFactory(AppConfig config)
    {
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolSources: Array.Empty<IToolSource>());
    }

    private async Task<string> StartThreadAndAssertModelAsync(
        AppServerRequestHandler handler,
        InMemoryTransport transport,
        int requestId,
        string expectedModel,
        string? expectedProviderId = null,
        object? config = null,
        string? expectedSpeed = null)
    {
        var start = InMemoryTransport.BuildRequest(
            AppServerMethods.ThreadStart,
            new
            {
                identity = new
                {
                    channelName = "appserver",
                    userId = "test-user",
                    workspacePath = _tempDir
                },
                config
            },
            id: requestId);

        await ExecuteRequestAsync(handler, transport, start);

        var response = await ReadResponseForIdAsync(transport, requestId);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal(expectedModel, GetThreadModel(thread));
        if (expectedProviderId != null)
            Assert.Equal(expectedProviderId, GetThreadProviderId(thread));
        if (expectedSpeed != null)
            Assert.Equal(expectedSpeed, thread.GetProperty("configuration").GetProperty("speed").GetString());

        var notification = await ReadNotificationAsync(transport, AppServerMethods.ThreadStarted);
        var notificationThread = notification.RootElement.GetProperty("params").GetProperty("thread");
        Assert.Equal(expectedModel, GetThreadModel(notificationThread));
        if (expectedProviderId != null)
            Assert.Equal(expectedProviderId, GetThreadProviderId(notificationThread));
        if (expectedSpeed != null)
            Assert.Equal(expectedSpeed, notificationThread.GetProperty("configuration").GetProperty("speed").GetString());

        return thread.GetProperty("id").GetString()!;
    }

    private static async Task InitializeAsync(AppServerRequestHandler handler, InMemoryTransport transport)
    {
        var init = InMemoryTransport.BuildRequest(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" },
            capabilities = new { approvalSupport = true, streamingSupport = true }
        }, id: 1);

        var result = await handler.HandleRequestAsync(init, default);
        if (result != null)
            await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(init.Id, result));
        _ = await ReadResponseForIdAsync(transport, 1);

        handler.HandleInitializedNotification();
    }

    private static async Task ExecuteRequestAsync(
        AppServerRequestHandler handler,
        InMemoryTransport transport,
        AppServerIncomingMessage request)
    {
        var result = await handler.HandleRequestAsync(request, default);
        if (result != null)
            await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(request.Id, result));
    }

    private static async Task<JsonDocument> ReadResponseForIdAsync(InMemoryTransport transport, int id)
    {
        for (var i = 0; i < 10; i++)
        {
            var message = await transport.ReadNextSentAsync();
            var root = message.RootElement;
            if (root.TryGetProperty("id", out var idElement) && idElement.GetInt32() == id)
                return message;
        }

        throw new InvalidOperationException($"No response with id {id} was sent.");
    }

    private static async Task<JsonDocument> ReadNotificationAsync(InMemoryTransport transport, string method)
    {
        for (var i = 0; i < 10; i++)
        {
            var message = await transport.ReadNextSentAsync();
            var root = message.RootElement;
            if (root.TryGetProperty("method", out var methodElement)
                && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal))
            {
                return message;
            }
        }

        throw new InvalidOperationException($"No '{method}' notification was sent.");
    }

    private static string? GetThreadModel(JsonElement thread)
    {
        var configuration = thread.GetProperty("configuration");
        return configuration.TryGetProperty("model", out var model)
            ? model.GetString()
            : configuration.GetProperty("Model").GetString();
    }

    private static string? GetThreadProviderId(JsonElement thread)
    {
        var configuration = thread.GetProperty("configuration");
        return configuration.TryGetProperty("providerId", out var providerId)
            ? providerId.GetString()
            : configuration.GetProperty("ProviderId").GetString();
    }

}
