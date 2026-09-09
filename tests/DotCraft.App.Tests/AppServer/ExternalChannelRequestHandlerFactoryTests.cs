using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Commands.Core;
using DotCraft.Context;
using DotCraft.ExternalChannel;
using DotCraft.Modules;
using DotCraft.Sessions;
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
    public async Task Handler_ExposesAndExecutesWorkspaceModuleCommands()
    {
        Directory.CreateDirectory(_tempDir);
        var service = new TestableSessionService(new ThreadStore(_tempDir));
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "telegram", UserId = "user-1", WorkspacePath = _tempDir
        });
        await using var transport = new InMemoryTransport();
        var providers = new ModelProviderRegistry([new OpenAIClientProvider()]);
        var commands = CommandRegistry.CreateDefault(".craft");
        commands.RegisterHandler(new AutomationTestCommand());
        var factory = new ExternalChannelRequestHandlerFactory(
            service, "test", new ModuleRegistry(), _tempDir,
            new ChatClientRegistry(providers), providers,
            streamDebugLogger: null, appConfigMonitor: null, protocolExtensions: [],
            appBindingService: null, originPresentationProviders: [], loggerFactory: null,
            commandRegistry: commands);
        var handler = factory.Create(new AppServerConnection(), transport);
        await ExecuteAsync(handler, transport, InMemoryTransport.BuildRequest("initialize", new
        {
            clientInfo = new { name = "channel-test", version = "test" }
        }));
        using var initialized = await ReadResponseAsync(transport, 1);
        handler.HandleInitializedNotification();

        await ExecuteAsync(handler, transport, InMemoryTransport.BuildRequest("command/list", new { }, id: 2));
        using var listed = await ReadResponseAsync(transport, 2);
        var names = listed.RootElement.GetProperty("result").GetProperty("commands")
            .EnumerateArray().Select(command => command.GetProperty("name").GetString()).ToArray();
        Assert.Contains("/automate", names);

        await ExecuteAsync(handler, transport, InMemoryTransport.BuildRequest("command/execute", new
        {
            threadId = thread.Id, command = "/automate", arguments = new[] { "pause", "a1" },
            sender = new { senderId = "user-1", senderName = "User", senderRole = "user", groupId = "group:42" }
        }, id: 3));
        using var executed = await ReadResponseAsync(transport, 3);
        var result = executed.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("handled").GetBoolean());
        Assert.Equal("pause a1 group:42", result.GetProperty("message").GetString());
    }

    private sealed class AutomationTestCommand : ICommandHandler
    {
        public string[] Commands => ["/automate"];
        public Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder) =>
            Task.FromResult(CommandResult.HandledResult($"{string.Join(' ', context.Arguments)} {context.GroupId}"));
    }

    [Fact]
    public async Task Handler_RuntimeAdditionalContextCapability_MatchesStartAndResumeSupport()
    {
        Directory.CreateDirectory(_tempDir);
        var service = new TestableSessionService(new ThreadStore(_tempDir));
        await using var transport = new InMemoryTransport();
        var connection = new AppServerConnection();
        var config = AppConfigTestFactory.CreateOpenAI();
        var monitor = new AppConfigMonitor(config);
        var providers = new ModelProviderRegistry([new OpenAIClientProvider()]);
        var chats = new ChatClientRegistry(providers);
        var runtimeContextProvider = new WireRuntimeAdditionalContextProvider();
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
            loggerFactory: null,
            wireRuntimeAdditionalContextProvider: runtimeContextProvider);
        var handler = factory.Create(connection, transport);

        await ExecuteAsync(handler, transport, InMemoryTransport.BuildRequest("initialize", new
        {
            clientInfo = new { name = "channel-test", version = "0.0.1" },
            capabilities = new { streamingSupport = true }
        }));
        using var initializeResponse = await ReadResponseAsync(transport, id: 1);
        Assert.True(initializeResponse.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("runtimeAdditionalContext")
            .GetBoolean());
        handler.HandleInitializedNotification();

        await ExecuteAsync(handler, transport, InMemoryTransport.BuildRequest("thread/start", new
        {
            identity = new { channelName = "external-test", userId = "user-1", workspacePath = _tempDir },
            additionalContext = new Dictionary<string, RuntimeAdditionalContextValue>
            {
                ["test.runtime"] = new()
                {
                    Kind = RuntimeAdditionalContextKinds.Application,
                    Value = "initial runtime context"
                }
            }
        }, id: 2));
        using var startResponse = await ReadResponseAsync(transport, id: 2);
        var threadId = startResponse.RootElement
            .GetProperty("result")
            .GetProperty("thread")
            .GetProperty("id")
            .GetString()!;
        var initialSection = runtimeContextProvider.GetSystemPromptSection(
            new ThreadSystemPromptContext(threadId, _tempDir, "external-test"));
        Assert.Contains("initial runtime context", initialSection);

        await ExecuteAsync(handler, transport, InMemoryTransport.BuildRequest("thread/resume", new
        {
            threadId,
            additionalContext = new Dictionary<string, RuntimeAdditionalContextValue>
            {
                ["test.runtime"] = new()
                {
                    Kind = RuntimeAdditionalContextKinds.Application,
                    Value = "restored runtime context"
                }
            }
        }, id: 3));
        using var resumeResponse = await ReadResponseAsync(transport, id: 3);
        Assert.True(resumeResponse.RootElement.TryGetProperty("result", out _));
        var restoredSection = runtimeContextProvider.GetSystemPromptSection(
            new ThreadSystemPromptContext(threadId, _tempDir, "external-test"));
        Assert.Contains("restored runtime context", restoredSection);
        Assert.DoesNotContain("initial runtime context", restoredSection);
    }

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
        var handler = factory.Create(connection, transport);

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
