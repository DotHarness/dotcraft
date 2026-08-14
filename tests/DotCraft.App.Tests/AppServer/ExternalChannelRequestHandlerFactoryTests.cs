using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.ExternalChannel;
using DotCraft.Modules;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class ExternalChannelRequestHandlerFactoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "ExternalChannelRequestHandlerFactoryTests_" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public async Task Handler_UsesWorkspaceProviderRegistries_ForThreadAndTurnStart()
    {
        Directory.CreateDirectory(_tempDir);
        var service = new TestableSessionService(new ThreadStore(_tempDir));
        await using var transport = new InMemoryTransport();
        var connection = new AppServerConnection();
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Providers["openai"].Protocol = ModelProviderProtocols.OpenAIResponses;
        var monitor = new AppConfigMonitor(config);
        var providers = new ModelProviderRegistry([new OpenAIClientProvider()]);
        var chats = new ChatClientRegistry(providers);
        var factory = new ExternalChannelRequestHandlerFactory(
            service,
            "0.0.1-test",
            new ModuleRegistry(),
            _tempDir,
            chats,
            providers,
            streamDebugLogger: null,
            appConfigMonitor: monitor,
            protocolExtensions: [],
            appBindingService: null,
            originPresentationProviders: [],
            loggerFactory: null);
        var handler = factory.Create(connection, transport, cronService: null, heartbeatService: null);

        var initialize = InMemoryTransport.BuildRequest("initialize", new
        {
            clientInfo = new { name = "channel-test", version = "0.0.1" },
            capabilities = new { approvalSupport = true, streamingSupport = true }
        });
        _ = await handler.HandleRequestAsync(initialize, default);
        handler.HandleInitializedNotification();

        var threadStart = InMemoryTransport.BuildRequest("thread/start", new
        {
            identity = new { channelName = "external-test", userId = "user-1", workspacePath = _tempDir }
        }, id: 2);
        await ExecuteAsync(handler, transport, threadStart);
        using var threadResponse = await transport.ReadNextSentAsync();
        var threadId = threadResponse.RootElement
            .GetProperty("result")
            .GetProperty("thread")
            .GetProperty("id")
            .GetString()!;
        service.EnqueueSubmitEvents(threadId, AppServerTestHarness.BuildTurnEventSequence(threadId));
        var turnStart = InMemoryTransport.BuildRequest("turn/start", new
        {
            threadId,
            input = new[] { new { type = "text", text = "hello from channel" } },
            sender = new
            {
                senderId = "user-1",
                senderName = "Tester",
                senderRole = "member",
                groupId = "chat-1"
            }
        }, id: 3);
        await ExecuteAsync(handler, transport, turnStart);
        using var turnResponse = await ReadResponseAsync(transport, id: 3);

        Assert.True(turnResponse.RootElement.TryGetProperty("result", out var result));
        Assert.StartsWith("turn_", result.GetProperty("turn").GetProperty("id").GetString());
        var submitted = Assert.Single(service.LastSubmittedContent);
        Assert.Equal("hello from channel", Assert.IsType<TextContent>(submitted).Text);
    }

    private static async Task ExecuteAsync(
        AppServerRequestHandler handler,
        InMemoryTransport transport,
        AppServerIncomingMessage request)
    {
        var result = await handler.HandleRequestAsync(request, default);
        if (result != null)
        {
            await transport.WriteMessageAsync(
                AppServerRequestHandler.BuildResponse(request.Id, result));
        }
    }

    private static async Task<JsonDocument> ReadResponseAsync(InMemoryTransport transport, int id)
    {
        while (true)
        {
            var message = await transport.ReadNextSentAsync();
            if (message.RootElement.TryGetProperty("id", out var responseId)
                && responseId.GetInt32() == id)
            {
                return message;
            }
            message.Dispose();
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }
}
